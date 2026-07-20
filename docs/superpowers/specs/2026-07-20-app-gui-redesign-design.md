# SmartIndexManager.App GUI redesign, design

Date : 2026-07-20
Statut : validé, prêt pour le plan d'implémentation
Portée : `src/SmartIndexManager.App` uniquement. Aucune modification de `Core` ni du provider SQL Server.

## 1. Contexte et objectif

L'application actuelle est fonctionnelle mais visuellement basique : `App.axaml` ne charge qu'un `<FluentTheme />` par défaut, les couleurs sont codées en dur dans le XAML de la grille, le score de confiance (le signal central du produit, spec §7) s'affiche comme un simple nombre, la `DataGrid` reste au style par défaut, et il n'existe ni surfaces (cartes), ni états intermédiaires (vide, chargement, erreur), ni identité visuelle.

Objectif : livrer une interface de qualité pour le périmètre déjà implémenté (le navigateur en lecture seule) et poser une coquille applicative prête à accueillir la feuille de route (panier de suppression, restauration, audit, réglages) sans refonte ultérieure.

Le Core et le provider ne changent pas. Le score et sa classification couleur restent calculés dans le Core (`Scoring/ScoreColor`), l'UI ne fait que les afficher.

## 2. Décisions actées

Ces choix sont fermes pour cette itération :

- Base de thème : `Semi.Avalonia` (`SemiTheme`), en remplacement du `FluentTheme` nu.
- Coquille : rail de navigation vertical à gauche, multi-destinations (Browse, Basket, Restore, Audit, Settings).
- Connexions : barre de connexion persistante en haut de la zone de contenu (serveur/bases actifs, Connect, Disconnect), gestion des profils dans une boîte de dialogue modale.
- Densité : compacte, orientée power-user DBA, sur toute l'application.
- Icônes : `Material.Icons.Avalonia`.
- Périmètre (approche A) : coquille plus expérience Browse aboutie ; Basket, Restore, Audit, Settings sont des destinations réelles affichant un état vide soigné, pas des fonctionnalités simulées.
- Group-by dans la grille : reporté (spec §12 le prévoit, mais hors de cette itération).

Contrainte de conception inchangée : toute logique doit être atteignable depuis un test xUnit sans instancier l'UI.

## 3. Dépendances ajoutées

- `Semi.Avalonia` (thème, MIT).
- `Material.Icons.Avalonia` (jeu d'icônes, MIT).

Ces deux paquets sont purement de présentation. `Avalonia.Themes.Fluent` peut être retiré des styles applicatifs une fois Semi en place (à confirmer à l'implémentation ; certains contrôles peuvent dépendre de ressources Fluent).

## 4. Architecture de la coquille et navigation

`MainWindowViewModel` concentre aujourd'hui trop de responsabilités : connexion, chargement, sérialisation du détail, thème, plus la possession directe de la grille, du détail, des connexions et des permissions. Le passage au rail est l'occasion de scinder ces responsabilités, ce qui rend chaque pièce testable isolément.

Structure cible :

- `ShellViewModel` (l'ancien `MainWindowViewModel` renommé et allégé) possède la coquille : la liste des destinations de navigation, la page courante, le bouton de thème, la session de connexion et l'état des permissions. Il ne porte aucune logique de grille ou de détail.
- `INavigationDestination` : petit enregistrement `Title`, `MaterialIconKind`, `PageViewModel`, `IsEnabled`. Le rail est une `ListBox` liée à cette collection ; la sélection affecte `ShellViewModel.CurrentPage`. Les vues sont résolues par des `DataTemplate` (ViewModel vers View) déclarés dans `App.axaml`.
- `ConnectionSessionViewModel` possède l'état de connexion active (serveur, bases sélectionnées, connecté/déconnecté), les commandes `Connect`, `Disconnect`, `Cancel`, et le déclenchement de la boîte de dialogue de gestion des profils. Il vit au niveau de la coquille pour que la barre de connexion persiste sur toutes les destinations. Le CRUD des profils reste dans `ConnectionManagerViewModel`, désormais hébergé dans la modale.
- `BrowseViewModel` possède ce qui constitue l'application actuelle : la grille d'index, le panneau détail, le flux de chargement après connexion, et la sérialisation du détail par `SemaphoreSlim` (déplacée ici, comportement inchangé). Il reçoit le provider actif depuis la session de connexion.
- ViewModels de destinations en attente (`BasketViewModel`, `RestoreViewModel`, `AuditViewModel`, `SettingsViewModel`) : minimaux, chacun rendant un état vide soigné.

Régions de disposition : rail ancré à gauche, barre de connexion ancrée en haut de la zone de contenu, barre d'état des permissions ancrée en bas, page courante remplissant le centre. Déconnecté, la zone de contenu affiche une invite de connexion plutôt qu'une grille vide.

Boîte de dialogue : le déclenchement de la modale de gestion des connexions passe par une abstraction `IDialogService` pour rester testable sans fenêtre réelle.

## 5. Fondation de jetons de conception

Le problème central actuel est l'absence de couche de conception et les couleurs codées en dur. Un seul emplacement porte chaque constante visuelle, adaptée aux thèmes clair et sombre.

Câblage du thème dans `App.axaml` : `SemiTheme` fournit le style de base des contrôles, `Material.Icons.Avalonia` fournit les icônes, et notre dictionnaire de jetons est fusionné par-dessus pour surcharger et étendre. `ThemeService` continue de piloter `RequestedThemeVariant` ; le bouton de thème et son test de persistance restent valides.

Les jetons vivent dans `Resources/Tokens.axaml` (`ResourceDictionary`), avec les jetons de couleur répartis en `ThemeDictionaries` (Light / Dark) pour qu'un jeton comme `SurfaceCardBrush` se résolve selon la variante. Catégories :

- Couleur, sémantique et non littérale : accent, niveaux de surface (fenêtre, carte, surélevé), bordure, texte (primaire/secondaire/atténué). La plupart héritent de Semi ; on ne fixe que ce que l'on réutilise.
- Couleurs de score : `ScoreSafeBrush`, `ScoreCautionBrush`, `ScoreRiskBrush`. L'UI ne calcule aucun seuil : le Core expose déjà `Scoring/ScoreColor` sur le résultat de confiance, le ViewModel de ligne relaie cet enum et l'UI le mappe vers le bon pinceau. La logique de seuil reste dans le Core, où elle est testée.
- Couleurs de badge, sorties du hex en dur vers des jetons adaptés au thème : `DangerBrush` (non supprimable), `WarnBrush` (redondant), `InfoBrush` (clé étrangère), `AccentAltBrush` (hint). Le badge de fiabilité réutilise les pinceaux de score.
- Échelle d'espacement pour la cible compacte : 2, 4, 8, 12, 16, 24 en `SpacingXs` à `SpacingXl`, plus un jeton `GridRowHeight` autour de 28 px.
- Typographie : classes de style `TextBlock` (Caption, Body, Subtitle, Title) en corps compact 12 à 13 px, plus une classe `Code` sur une famille monospace pour les surfaces DDL et SQL (ajoute une ressource de police mono ; Inter reste la police d'UI).
- Rayon : `RadiusSm` pour pastilles et badges, `RadiusMd` pour cartes.

Résultat : badges, pastilles de score et toute surface référencent les jetons par clé. Un changement de thème ou de densité devient une édition de jetons, pas une chasse dans le XAML, et le mode sombre cesse de lutter contre des couleurs codées en dur.

## 6. Expérience Browse

### Grille

- Lignes compactes au jeton `GridRowHeight`, bandes alternées de Semi, en-tête collant.
- Intention de largeur de colonne plutôt que tout en automatique : les colonnes d'identité (base, schéma, table, index) prennent une largeur proportionnelle ; type et les colonnes numériques ont des largeurs fixes compactes.
- Colonnes numériques (taille, seeks, scans, updates) alignées à droite avec séparateurs de milliers via un convertisseur de format, pour rendre les ordres de grandeur lisibles.
- La colonne de score cesse d'être un nombre nu et devient une pastille de score : une petite pastille arrondie remplie depuis le mappage `ScoreColor` vers pinceau (safe/caution/risk), la valeur à l'intérieur. Le tri se fait toujours sur le score numérique sous-jacent.
- Les badges quittent le hex en dur pour des pinceaux à jetons et gagnent une icône Material plus l'infobulle existante (la raison de non-suppression circule déjà par `NotDeletableReason`). Mêmes quatre sémantiques : non supprimable, redondant, clé étrangère, hint ; adaptées au thème et de taille uniforme.
- Le filtre reçoit une vraie barre d'outils : un champ de recherche avec icône de tête et un compteur vivant « N sur M index ».

### Panneau détail

Aujourd'hui un panneau plat, il devient des cartes titrées dimensionnées à leur contenu. Une carte ne se rend que si sa donnée existe.

- Carte d'en-tête : nom de l'index, type, badges, pastille de score affichée en grand.
- Carte DDL : le DDL de recréation en style monospace `Code`, avec un bouton de copie.
- Carte structure : colonnes de clé ordonnées avec flèches de direction, includes, prédicat de filtre, unicité/contrainte.
- Carte usage : seeks, scans, lookups, updates, dernier read, et « observé depuis » à partir du plus ancien snapshot local.
- Carte explication du score : chaque `ScoreFactor` et sa contribution (le Core les expose déjà), plus le badge de fiabilité.
- Carte redondance : affichée seulement si présente, nommant l'index couvrant et la règle (R1/R2/R3).
- Propriétés provider : repliable, priorité basse.

Sans sélection, le panneau affiche un état vide discret « sélectionnez un index » plutôt qu'un blanc.

## 7. États et destinations en attente

Une part de l'aspect basique actuel vient de l'absence d'états intermédiaires. Un contrôle réutilisable `EmptyStateView` (icône Material, titre, message, bouton d'action optionnel) sous-tend la plupart de ces états, ce qui les rend cohérents et peu coûteux.

- Déconnecté : sans connexion active, la zone de contenu affiche une invite de connexion centrée (icône, « Connect to a server to browse indexes », un bouton Connect et un lien Manage) au lieu d'une grille vide.
- Chargement : la barre de connexion affiche une progression indéterminée avec le Cancel existant ; la zone de grille affiche un indicateur de chargement léger. Le panneau détail affiche son propre indicateur en ligne pendant un chargement de détail, ce qui s'accorde avec la sérialisation par `SemaphoreSlim` déjà en place.
- Résultats vides : connecté mais aucune ligne, ou un filtre sans correspondance : un état vide avenant, avec une action « effacer le filtre » dans le cas du filtre.
- Erreur, non bloquante et en ligne : un échec de chargement, ou l'échec de contrat de fichier SQL de la spec (un `sql/sqlserver/*.sql` absent ou invalide marque cette fonctionnalité en erreur), se matérialise par un bandeau en ligne avec le message explicite et un Retry, jamais un plantage.
- Permissions dégradées : quand `VIEW SERVER STATE` ou équivalent manque, les colonnes d'usage indiquent « unavailable » et la barre d'état des permissions en bas (déjà modélisée par `PermissionStatusViewModel`) explique ce qui manque. Aucune erreur bloquante.

Destinations en attente : Basket, Restore, Audit et Settings sont des éléments de rail réels et navigables, chacun rendant l'`EmptyStateView` partagé : une icône pertinente, le nom de la fonctionnalité, et une courte ligne « prévu pour une version future ». Quand ces fonctionnalités seront construites, elles remplacent le ViewModel de substitution sans toucher à la coquille.

## 8. État de l'écran modélisé en ViewModel

Pour rester testable sans rendu, l'écran Browse est modélisé par un enum `BrowseState` (Disconnected, Loading, Ready, Empty, Error) exposé sur le ViewModel. La vue se contente de commuter dessus. Les décisions d'état sont donc assertables directement en test, plutôt qu'enfouies dans des déclencheurs XAML.

## 9. Testing

La refonte est majoritairement du XAML plus un remaniement de ViewModels. La stratégie : garder les décisions dans des ViewModels testables et des convertisseurs purs, et laisser le compilateur contrôler le XAML. Tout reste atteignable sans instancier l'UI.

Migration des tests existants selon le découpage :

- `MainWindowViewModelTests` se scinde : les préoccupations de coquille (navigation, bascule de thème et sa persistance, changement de page courante) vont vers `ShellViewModelTests` ; le flux de chargement et la sérialisation du détail vers `BrowseViewModelTests`.
- `DetailConcurrencyTests` se déplace sur `BrowseViewModel`, intention inchangée, conservant l'invariant « MaxConcurrent reste 1 » qui protège la connexion sans MARS.
- `ConnectionManagerViewModelTests` reste tel quel ; le CRUD des profils n'a pas bougé.

Nouveaux tests unitaires, sans UI :

- Navigation : la destination par défaut est Browse, sélectionner une destination affecte `CurrentPage`, une destination désactivée n'est pas sélectionnable.
- `ScoreColorToBrushConverter` : chaque valeur `ScoreColor` du Core mappe vers le jeton attendu ; test de convertisseur pur, sans rendu.
- Convertisseur de format numérique : séparateurs de milliers, gestion de null et zéro.
- `IndexRowViewModel` relaie `ScoreColor` depuis l'évaluation de score.
- État Browse : les transitions de `BrowseState` sont assertées directement.
- `ConnectionSessionViewModel` : transitions Connect, Disconnect, Cancel, et « Manage » invoque une boîte de dialogue via le joint `IDialogService` pour que les tests n'aient besoin d'aucune fenêtre.

Correction du XAML : chaque nouvelle vue garde les liaisons compilées (`x:DataType`), de sorte qu'une liaison erronée devient une erreur de build, pas une surprise à l'exécution. Cela plus `dotnet build` constitue le garde automatique du balisage. Semi et Material.Icons sont des paquets de style sans impact de test au-delà de la compilation.

Vérification visuelle : manuelle. Si un affichage est disponible, l'application peut être lancée et capturée à chaque passe ; sinon revue à l'oeil. Rien ici n'en dépend.

## 10. Carte d'impact fichiers (indicative)

Nouveaux :

- `App.axaml` (styles Semi + Material + jetons), `Resources/Tokens.axaml`.
- `ViewModels/ShellViewModel.cs`, `ConnectionSessionViewModel.cs`, `BrowseViewModel.cs`, `BasketViewModel.cs`, `RestoreViewModel.cs`, `AuditViewModel.cs`, `SettingsViewModel.cs`, `INavigationDestination`.
- `Views/ShellView` (ou `MainWindow` remanié), `Views/BrowseView`, `Views/EmptyStateView`, dialogue de gestion des connexions.
- `Converters/ScoreColorToBrushConverter.cs`, convertisseur de format numérique.
- `Services/IDialogService.cs` et implémentation Avalonia.

Modifiés :

- `MainWindow.axaml` / `.axaml.cs` (deviennent la coquille), `IndexGridView`, `IndexDetailView`, `ConnectionManagerView` (hébergé en modale), `IndexRowViewModel` (expose `ScoreColor`), `SmartIndexManager.App.csproj` (paquets), `Localization/Strings.resx` (nouvelles chaînes : titres de destinations, états vides, libellés de barre de connexion).

Tests : découpage décrit en §9.

## 11. Hors périmètre

- Group-by et regroupement de la grille (reporté).
- Toute fonctionnalité de Basket, Restore, Audit (états vides seulement).
- Toute modification du Core ou du provider SQL Server.
- Bascule de densité à l'exécution (les jetons la rendent possible plus tard, non exposée ici).
