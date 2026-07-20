# SmartIndexManager, spécification de conception

Date : 2026-07-20
Statut : consolidé, prêt pour le plan d'implémentation
Remplace : `TASKS01.md` (conservé comme trace historique du brainstorm)

## 1. Objectif et périmètre

SmartIndexManager est un outil de bureau multi-plateforme (Windows, Linux, macOS) de gestion des index de bases de données, destiné aux DBA. SQL Server est le premier moteur supporté, PostgreSQL et d'autres moteurs viendront ensuite via une architecture de providers.

L'outil aide le DBA à répondre à trois questions : quels index existent, lesquels sont réellement utilisés, lesquels peuvent être supprimés en confiance. La suppression est encadrée par des garde-fous, un rapport d'impact (dry-run), une sauvegarde systématique du DDL et une restauration intégrée.

### Roadmap

MVP :

- Listing complet des index de une ou plusieurs bases d'une instance, tous types confondus, avec structure, tailles et statistiques d'usage.
- Détection algorithmique des index redondants (règles R1, R2, R3, section 5).
- Recherche des requêtes utilisant un index donné (plan cache et Query Store).
- Détection des hints d'index (`WITH (INDEX(...))`) et plan guides, intégrée au dry-run.
- Score de confiance de suppression 0 à 100 avec code couleur.
- Dry-run avec rapport d'impact exportable.
- Suppression en deux modes : exécution directe avec confirmation, ou génération de script sans exécution.
- Sauvegarde DDL (un fichier `.sql` par index) plus `manifest.json` par session, restauration intégrée.
- Gestionnaire de connexions nommées (Windows, SQL, Entra ID interactif), sans stockage de mot de passe.
- Vérification des permissions à la connexion avec rapport de dégradation par fonctionnalité.
- Détection de l'état du Query Store et activation assistée.
- Snapshots locaux d'usage à chaque connexion (capture seule, exploitation ultérieure).
- Journal d'audit local, export CSV/JSON des grilles.

v1.x :

- Suggestions de fusion d'index (recouvrement partiel).
- Création des index manquants (`missing indexes` DMV et Query Store).
- Rebuild et réorganisation.
- Exploitation basique des snapshots locaux (comparaison entre captures, détection d'usage intermittent).

v2 :

- Provider PostgreSQL.
- Analyse de tendance sur l'historique de snapshots (usage mensuel, trimestriel).
- Mode CLI pour l'automatisation (le Core est conçu dès le MVP pour le permettre sans refactoring).

## 2. Décisions actées

Ces décisions consolident les réponses inline de `TASKS01.md` et les arbitrages du brainstorm. Elles sont fermes pour le MVP.

Périmètre :

- La suppression est la seule action corrective du MVP. Rebuild, réorganisation et création d'index manquants sont prévus dans l'architecture mais implémentés en v1.x.
- Multi-bases : sélection d'une ou plusieurs bases d'une même instance. Une seule instance à la fois.
- Application purement interactive. Pas de CLI dans le MVP, mais le Core reste indépendant de l'UI pour qu'un CLI futur ne coûte rien.

Suppression :

- Aucun seuil automatique ne qualifie un index de supprimable : l'outil affiche seeks, scans, lookups, updates, dates de dernier usage, et un score de confiance coloré. La décision reste humaine.
- Pas d'étape `ALTER INDEX ... DISABLE` intermédiaire : la sauvegarde DDL sur disque remplit le rôle de filet de sécurité.
- Deux modes d'exécution : direct (avec double confirmation) et génération de script `.sql` sans exécution. Dans les deux cas, la sauvegarde DDL et le manifeste sont produits.
- La raison de suppression est générée automatiquement (statistiques d'usage, score, redondances détectées) et le DBA peut ajouter un commentaire libre optionnel.

Sauvegarde et restauration :

- Un fichier `.sql` par index supprimé, plus un `manifest.json` par session de suppression (section 9).
- Répertoire de sauvegarde configurable, défaut `Documents/SmartIndexManager/<serveur>/<horodatage>/`.
- Restauration intégrée : rejouer le DDL sauvegardé depuis l'outil.

Plateforme et compatibilité :

- SQL Server 2012 (11.0) minimum, Azure SQL Database inclus.
- Les fonctionnalités dépendantes de la version (Query Store en 2016+) sont exposées via un objet de capacités par provider ; l'UI dégrade proprement.
- Sur Azure SQL Database, `VIEW DATABASE STATE` remplace `VIEW SERVER STATE` ; les DMV d'usage y sont par base, pas par instance.

Technique :

- Un module (provider) par SGBD, modèle d'index commun plus propriétés spécifiques (section 3).
- Requêtes SQL en fichiers externes modifiables par l'utilisateur, strictement : pas de fallback embarqué. Fichier absent ou invalide = fonctionnalité en erreur avec message explicite (section 4).
- MVVM avec CommunityToolkit.Mvvm.
- UI en anglais, infrastructure i18n (`.resx`) en place dès le départ.
- CI d'intégration (conteneur SQL Server) prise en charge par l'utilisateur ; l'outillage Testcontainers est prévu côté code.

Sécurité :

- Les mots de passe SQL ne sont jamais stockés, sous aucune forme. Ils sont demandés à chaque connexion.
- Types d'index : tout est listé (rowstore, columnstore, XML, spatial, fulltext, hypothétiques, désactivés), mais seuls les nonclustered rowstore non uniques sont éligibles à la suppression dans le MVP.
- Les index `UNIQUE` non adossés à une contrainte sont exclus de la suppression, au même titre que les contraintes `UNIQUE` et les PK.
- Abandon de l'idée de tables de collecte côté serveur (persistance des DMV avant shutdown) : remplacée par les snapshots locaux côté client (section 10).

## 3. Architecture

Trois couches strictement séparées :

- `SmartIndexManager.Core` : modèle, moteur de redondance, scoring, génération DDL, orchestration des opérations, manifestes, snapshots, audit. Aucune dépendance UI ni SqlClient.
- `SmartIndexManager.Providers.SqlServer` : implémentation SQL Server (connexion, exécution des fichiers SQL externes, mapping vers le modèle commun). Un futur `Providers.PostgreSql` suivra le même contrat.
- `SmartIndexManager.App` : UI Avalonia, ViewModels CommunityToolkit.Mvvm. Consomme le Core uniquement via ses interfaces.

Contrainte de conception : toute fonctionnalité doit être appelable depuis un test xUnit ou un futur CLI sans instancier l'UI.

### Contrat provider

`IIndexProvider` expose :

- Connexion et session (ouverture, fermeture, base(s) sélectionnée(s)).
- Découverte : version, édition, plateforme (on-premises ou Azure SQL Database), uptime de l'instance, permissions effectives.
- Capacités : objet `ProviderCapabilities` avec au minimum `SupportsQueryStore`, `SupportsPlanCache`, `SupportsColumnstore`, `SupportsOnlineDrop`, `RequiresDatabaseScopedDmv` (Azure). L'UI et le Core testent les capacités, jamais la version brute.
- Métadonnées : liste des index avec structure complète, tailles, statistiques d'usage et opérationnelles.
- Diagnostic : requêtes utilisant un index (plan cache, Query Store), hints et plan guides, support de FK, appartenance à la réplication ou à un availability group.
- Actions : exécution d'un `DROP INDEX`, activation du Query Store.

### Modèle d'index commun

Propriétés communes : base, schéma, table, nom, type (énumération : clustered, nonclustered, clustered columnstore, nonclustered columnstore, XML, spatial, fulltext, heap, hypothetical), colonnes de clé ordonnées avec direction, colonnes `INCLUDE`, prédicat de filtre, unicité, contrainte associée (PK, UNIQUE, aucune), désactivé ou non, tailles (pages, lignes), statistiques d'usage (seeks, scans, lookups, updates, dates de dernier usage) et statistiques opérationnelles.

Propriétés spécifiques au SGBD : dictionnaire `ProviderProperties` (clé/valeur typée) porté par l'index, rempli par le provider (exemples SQL Server : fillfactor, compression, partitionnement). Le Core ne raisonne que sur les propriétés communes ; l'UI peut afficher le dictionnaire dans le panneau détail.

### Détection à la connexion

À l'ouverture d'une connexion, le provider détermine dans l'ordre : version et édition, plateforme (Azure ou non), uptime de l'instance (précision : sur Azure, l'uptime lu est celui du moteur et les DMV peuvent être réinitialisées par les reconfigurations du service ; le badge de fiabilité du dry-run en tient compte), permissions effectives par fonctionnalité, état du Query Store par base sélectionnée. Le résultat alimente le rapport de dégradation (section 11).

## 4. Contrat des fichiers SQL externes

Toutes les requêtes envoyées au serveur vivent dans des fichiers externes, organisés par provider : `sql/sqlserver/<fonction>.sql`. L'utilisateur peut les modifier. Il n'existe aucune copie embarquée : si un fichier est absent, illisible ou invalide, la fonctionnalité correspondante est marquée en erreur dans l'UI avec un message explicite (nom du fichier, nature du problème), sans faire tomber l'application.

### En-tête de métadonnées

Chaque fichier commence par un bloc de commentaires structuré :

```sql
-- sim: name=unused-indexes
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=SchemaName,TableName,IndexName,UserSeeks,UserScans,UserLookups,UserUpdates
SELECT ...
```

- `name` : identifiant logique, doit correspondre au nom attendu par le provider.
- `minversion` : version SQL Server minimale ; en deçà, la fonctionnalité est dégradée avec message.
- `azure` : `supported`, `unsupported` ou `only` (variante dédiée Azure).
- `columns` : colonnes que le provider lira, par nom (jamais par position). Colonnes supplémentaires ignorées ; colonne attendue manquante = fichier invalide.

Les paramètres sont passés en paramètres `SqlCommand` nommés (`@SchemaName`, `@IndexName`, etc.), jamais par concaténation.

### Inventaire des fichiers

Repris ou adaptés du dépôt `tsql-scripts/index-management` : `index-usage.sql`, `unused-indexes.sql`, `index-used-by-queries.sql`, `index-used-by-queries-query-store.sql`, `missing-indexes.sql` (v1.x), `index-on-table.sql`, `index-operational-stats.sql`, `index-physical-stats.sql`, `index-used-by-proc.sql`.

À écrire en plus :

- `index-list.sql` : métadonnées complètes de tous les index de la base (tous types, colonnes, includes, filtres, unicité, contraintes, tailles, désactivés, hypothétiques).
- `fk-support.sql` : index dont les colonnes de tête couvrent une clé étrangère.
- `index-hints-plancache.sql` et `index-hints-querystore.sql` : requêtes référençant un index en dur (hint ou plan guide).
- `permissions-check.sql` : permissions effectives (`VIEW SERVER STATE`, `VIEW DATABASE STATE`, `ALTER` sur les objets).
- `querystore-state.sql` et `querystore-enable.sql` : état et activation du Query Store.
- `server-info.sql` : version, édition, plateforme, uptime.
- `replication-ag-check.sql` : appartenance de la base à la réplication ou à un availability group.

Le DDL de recréation n'est pas produit par une requête SQL : il est généré par le Core en C# à partir des métadonnées collectées (déterministe et testable sans base).

## 5. Algorithme de redondance

La détection ne compare que les index nonclustered rowstore d'une même table. Les autres types sont hors périmètre de la redondance dans le MVP.

### Normalisation

Chaque index est réduit à un tuple : liste ordonnée de (colonne, direction), ensemble des colonnes `INCLUDE`, prédicat de filtre normalisé (casse, espaces, parenthèses superflues), drapeau d'unicité. Les noms de colonnes sont comparés sans sensibilité à la casse (collation par défaut du serveur notée dans le panneau détail si elle diffère).

### Règles

- R1, doublon exact : même clé (colonnes et directions), même filtre, mêmes `INCLUDE`. La paire est signalée ; l'outil suggère de garder celui qui porte une contrainte ou, à défaut, le plus utilisé.
- R2, préfixe couvert : la clé de A est un préfixe strict de la clé de B (mêmes directions sur le préfixe), même filtre, et clé restante plus `INCLUDE` de A inclus dans clé plus `INCLUDE` de B. A est signalé redondant par rapport à B.
- R3, includes dominés : même clé, même filtre, `INCLUDE` de A strictement inclus dans `INCLUDE` de B. A est signalé redondant par rapport à B.

### Cas limites tranchés

- Un index unique (contrainte ou non) n'est jamais signalé redondant : il porte une sémantique d'intégrité.
- Une direction différente sur une colonne du préfixe casse la redondance.
- Deux prédicats de filtre différents (après normalisation) cassent la redondance ; deux index filtrés au même prédicat se comparent normalement.
- Un index filtré n'est jamais redondant par rapport à un index non filtré, ni l'inverse (le non filtré couvre plus, mais le coût diffère ; signalé en information, pas en redondance).
- Recouvrement partiel de clés (ni préfixe ni égalité) : pas de redondance ; une suggestion de fusion est prévue en v1.x.

La redondance alimente le score de confiance et la raison automatique, mais ne déclenche jamais une suppression seule.

## 6. Sécurité de la suppression

### Exclusions dures (jamais proposés à la suppression)

Clés primaires, contraintes `UNIQUE`, index `UNIQUE` sans contrainte, index clustered (rowstore et columnstore), index nonclustered columnstore, XML, spatial, fulltext, index de vues indexées, index sur tables système, index hypothétiques, index désactivés. Ces index apparaissent dans la grille avec un badge « non supprimable » et la raison.

### Garde-fous avec avertissement (suppression possible, alerte bloquante à confirmer)

- Index supportant une clé étrangère (colonnes de tête couvrant une FK).
- Index filtré.
- Index référencé par un hint ou un plan guide (risque d'échec des requêtes, pas seulement de ralentissement).
- Base membre d'une réplication ou d'un availability group.
- Uptime de l'instance inférieur au seuil configurable (défaut 30 jours) : les statistiques d'usage sont peu fiables.

### Déroulé d'une suppression

Pour chaque index du panier, dans l'ordre, et l'opération s'arrête pour cet index à la première étape en échec :

1. Regénérer le DDL de recréation depuis les métadonnées fraîches.
2. Écrire le fichier `.sql` de sauvegarde et mettre à jour le `manifest.json`.
3. Relire le fichier sur disque et vérifier son contenu (taille non nulle, DDL présent).
4. Exécuter `DROP INDEX` avec un timeout configurable (défaut 60 secondes). Pas de `WITH (ONLINE)` implicite ; l'opération est un DROP simple.
5. Journaliser dans l'audit local (succès ou échec, horodatage, serveur, base, opérateur).

En lot : un échec sur un index n'arrête pas le lot ; l'index est marqué en échec dans le récapitulatif et dans le manifeste (`status: failed`), et le traitement continue. Le récapitulatif final liste succès et échecs.

En mode « génération de script » : les étapes 1 à 3 et 5 sont identiques ; l'étape 4 est remplacée par l'écriture d'un script de suppression global horodaté, avec les mêmes garde-fous matérialisés en commentaires.

## 7. Score de confiance

Un score de 0 à 100 par index éligible, où 100 signifie « suppression très sûre ». Le score est explicable : le panneau détail liste chaque facteur et sa contribution.

Calcul (valeurs par défaut, configurables) :

- Base 100 si aucune lecture (seeks + scans + lookups = 0) depuis le démarrage. Toute lecture fait chuter le score proportionnellement au volume et à la fraîcheur (une lecture récente pèse plus qu'une ancienne).
- Uptime : si l'uptime est inférieur au seuil de fiabilité (défaut 30 jours), le score est plafonné à 60 et le badge de fiabilité passe en orange.
- Updates élevés avec zéro lecture : bonus (l'index coûte sans servir), plafonné à plus 10.
- Redondance détectée (R1, R2, R3) : bonus plus 10 (un jumeau couvre le besoin).
- Support de FK : plafonné à 40.
- Index filtré : plafonné à 50.
- Hint ou plan guide détecté : plafonné à 10.

Seuils de couleur configurables, défaut : vert 80 et plus, orange 50 à 79, rouge moins de 50. Le score est un indicateur d'aide à la décision, jamais un déclencheur automatique.

## 8. Dry-run

Le dry-run produit, pour la sélection courante du panier, un rapport d'impact sans rien exécuter :

- Par index : structure, tailles, statistiques d'usage et dates de dernier usage.
- Requêtes du plan cache et du Query Store utilisant l'index (texte, nombre d'exécutions, dernière exécution).
- Hints et plan guides référençant l'index, mis en évidence comme risque bloquant.
- Clés étrangères supportées.
- Espace libéré estimé (somme des tailles) et économie d'écritures estimée (updates évités).
- Badge de fiabilité global : vert, orange ou rouge selon l'uptime, la disponibilité du Query Store et la plateforme (Azure : DMV par base, réinitialisations possibles).

Le rapport est exportable en JSON et en texte (markdown), et il est archivé dans le répertoire de session de suppression s'il précède une exécution.

## 9. Sauvegarde et restauration

### Fichiers

- Répertoire par session : `<racine>/<serveur>/<horodatage ISO>/`, racine configurable, défaut `Documents/SmartIndexManager/`. Le nom de serveur est assaini pour le système de fichiers (caractères interdits remplacés).
- Un fichier par index : `<base>.<schéma>.<table>.<index>.sql`, contenant le DDL de recréation complet (colonnes, includes, filtre, unicité, options pertinentes du dictionnaire provider comme fillfactor et compression), précédé d'un en-tête en commentaires : date, serveur, base, opérateur, raison automatique, commentaire libre, statistiques au moment de la suppression.
- Le script global de suppression (mode génération) est écrit dans le même répertoire : `drop-session.sql`.

### `manifest.json`

Un par session :

```json
{
  "tool": "SmartIndexManager",
  "toolVersion": "1.0.0",
  "createdUtc": "2026-07-20T10:00:00Z",
  "server": "PROD01",
  "operator": "DOMAIN\\rudi",
  "instanceUptimeDays": 92,
  "mode": "execute",
  "indexes": [
    {
      "database": "Sales",
      "schema": "dbo",
      "table": "Orders",
      "index": "IX_Orders_Legacy",
      "file": "Sales.dbo.Orders.IX_Orders_Legacy.sql",
      "reason": "0 reads in 92 days, 1.2M updates, redundant with IX_Orders_Date (R2)",
      "comment": "",
      "score": 94,
      "stats": { "seeks": 0, "scans": 0, "lookups": 0, "updates": 1200000, "lastRead": null, "sizeMB": 830 },
      "status": "dropped",
      "restoredUtc": null
    }
  ]
}
```

`mode` vaut `execute` ou `script`. `status` vaut `dropped`, `failed` ou `scripted`.

### Écran de restauration

Liste les sessions trouvées dans la racine de sauvegarde (lecture des manifestes), affiche les index de chaque session avec leur statut, permet de rejouer le DDL d'un ou plusieurs index sur la connexion courante. Une restauration réussie met à jour `restoredUtc` dans le manifeste et écrit une entrée d'audit. Si l'index existe déjà, la restauration de cet index est refusée avec message (pas de `DROP` implicite).

## 10. Snapshots locaux d'usage (MVP light)

Objectif : atténuer la volatilité des DMV sans rien installer côté serveur.

- Quand : automatiquement à chaque connexion à une base, après la collecte des métadonnées.
- Quoi : par index, les compteurs d'usage (seeks, scans, lookups, updates, dates de dernier usage) plus l'uptime de l'instance au moment de la capture.
- Où : répertoire de configuration de l'application (emplacement standard par plateforme : `ApplicationData` sur Windows, `XDG_CONFIG_HOME` sur Linux, `~/Library/Application Support` sur macOS), sous `snapshots/<serveur>/<base>/<horodatage>.json`.
- Format : JSON, une capture par fichier. Une rétention configurable (défaut 90 jours) supprime les captures anciennes.

Dans le MVP, l'outil capture et affiche seulement la date de la capture la plus ancienne disponible (« observé depuis le ... ») dans le panneau détail. La comparaison entre captures et la détection d'usage intermittent arrivent en v1.x, l'analyse de tendance en v2. Le format de fichier est conçu pour rester lisible par ces versions.

## 11. Connexions et permissions

### Gestionnaire de connexions

Connexions nommées, persistées dans la configuration locale : nom, serveur, port, options (chiffrement, trust server certificate), type d'authentification (Windows intégrée, SQL, Microsoft Entra ID interactif), login pour l'authentification SQL. Le mot de passe n'est jamais persisté, ni en clair, ni chiffré, ni dans un keychain : il est saisi à chaque connexion. L'authentification Entra ID interactive passe par le flux du fournisseur (MSAL via `Microsoft.Data.SqlClient`), sans stockage de secret par l'outil.

### Vérification des permissions

À la connexion, l'outil vérifie les permissions effectives et produit un rapport de dégradation par fonctionnalité, affiché dans la barre d'état et détaillé à la demande :

- `VIEW SERVER STATE` (on-premises) ou `VIEW DATABASE STATE` (Azure SQL Database) : requis pour les statistiques d'usage, le plan cache, les hints. Absent : le listing fonctionne, l'usage est marqué « indisponible ».
- Droits DDL (`ALTER` sur les tables concernées ou rôle équivalent) : requis pour le DROP et l'activation du Query Store. Absent : mode lecture seule, la génération de script reste disponible.
- Accès au Query Store : requis pour les fonctions Query Store.

Aucune fonctionnalité manquante n'est une erreur bloquante : chaque manque est signalé avec la permission à obtenir.

### Query Store

Par base sélectionnée (SQL Server 2016+ et Azure), l'outil affiche l'état du Query Store (désactivé, lecture seule, lecture écriture). S'il est désactivé, il propose l'activation avec ces paramètres par défaut, modifiables avant exécution :

```sql
ALTER DATABASE [<base>] SET QUERY_STORE = ON (
  OPERATION_MODE = READ_WRITE,
  QUERY_CAPTURE_MODE = AUTO,
  MAX_STORAGE_SIZE_MB = 1000,
  SIZE_BASED_CLEANUP_MODE = AUTO,
  CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30)
);
```

L'activation est journalisée dans l'audit.

## 12. UX

- Grille unique multi-bases : tous les index des bases sélectionnées dans une seule grille, avec colonne « base », filtrable, triable et groupable (par base, table, type, couleur de score). Colonnes principales : base, schéma, table, index, type, clé, includes, filtre, unique, taille, seeks, scans, lookups, updates, dernier usage, score, badges (non supprimable, FK, hint, redondant).
- Panneau détail par index : DDL de recréation, structure complète, dictionnaire de propriétés provider, statistiques détaillées et opérationnelles, requêtes utilisatrices (plan cache et Query Store), hints, redondances détectées avec la règle applicable, explication du score, date de plus ancien snapshot local.
- Panier de suppression : le DBA ajoute des index au panier depuis la grille ; le panier affiche le récapitulatif, les garde-fous, le dry-run, puis exige une double confirmation en mode exécution (confirmation du lot, puis saisie du mot `DROP` ou équivalent). Les exclusions dures ne peuvent pas entrer dans le panier.
- Exécution asynchrone et annulable : les collectes et les suppressions tournent hors du thread UI, avec barre de progression et bouton d'annulation (l'annulation n'interrompt pas un `DROP` déjà envoyé, elle arrête le lot avant l'index suivant).
- Export CSV et JSON de la grille filtrée et du rapport de dry-run.
- Journal d'audit local : fichier JSONL en append dans le répertoire de configuration, une entrée par action sensible (DROP, restauration, activation Query Store, génération de script), consultable dans l'outil.
- Thème clair et sombre.
- Langue : anglais, textes dans des ressources `.resx` dès le MVP pour permettre des traductions ultérieures.

## 13. Pile technique

- C#, .NET 10.
- Avalonia 11 (Windows, Linux, macOS).
- CommunityToolkit.Mvvm pour le MVVM.
- `Microsoft.Data.SqlClient` (auth Windows, SQL, Entra ID interactif).
- `Microsoft.Extensions.DependencyInjection` pour l'injection de dépendances dans les trois couches.
- Tests : xUnit. Le Core est testable sans base : normalisation et règles de redondance, calcul de score, génération de DDL, parsing des en-têtes de fichiers SQL, lecture et écriture des manifestes et snapshots. Tests d'intégration provider avec Testcontainers (SQL Server en conteneur) ; l'exécution en CI est prise en charge par l'utilisateur.
- Structure de la solution :

```
SmartIndexManager/
  src/
    SmartIndexManager.Core/
    SmartIndexManager.Providers.SqlServer/
    SmartIndexManager.App/
  sql/
    sqlserver/
  tests/
    SmartIndexManager.Core.Tests/
    SmartIndexManager.Providers.SqlServer.Tests/
  docs/
    specs/
```
