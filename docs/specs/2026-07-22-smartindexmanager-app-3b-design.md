# SmartIndexManager, spécification du plan App 3b : actions correctives

Date : 2026-07-22
Statut : validé, prêt pour le plan d’implémentation
S’appuie sur : `docs/specs/2026-07-20-smartindexmanager-design.md`, `docs/plans/2026-07-20-core-library.md`, `docs/plans/2026-07-20-sqlserver-provider.md`, `docs/plans/2026-07-20-app-readonly-browser.md`

## 1. Objectif et périmètre

Le plan App 3b ajoute les actions correctives à SmartIndexManager. Jusqu’ici, l’application est un navigateur en lecture seule : elle liste les index, calcule les scores, signale les redondances et affiche les garde-fous. Ce plan transforme ces informations en actions contrôlées : suppression avec filets de sécurité, restauration, audit et exports.

Périmètre du plan 3b :

- Boîte de dialogue de saisie du mot de passe SQL à la connexion, sans stockage.
- Panier de suppression : ajout, retrait, validation de l’éligibilité, warnings.
- Dry-run : rapport d’impact par index (structure, usage, hints, FK, espace libéré, fiabilité), affichage dans l’UI et export JSON/markdown.
- Suppression en deux modes : exécution directe avec double confirmation, ou génération de script.
- Sauvegarde DDL (un fichier `.sql` par index) et `manifest.json` par session, dans les deux modes.
- Journal d’audit local (DROP, restauration, activation Query Store, génération de script).
- Écran de restauration : liste des sessions, restauration par index ou multi-sélection.
- Activation assistée du Query Store depuis la barre d’état.
- Journal d’audit consultable dans l’application.
- Paramètres : répertoires de backup et de snapshots, rétention des snapshots.

Ce qui reste hors périmètre : les exports CSV/JSON de la grille filtrée, l’édition des seuils de scoring, les suggestions de fusion d’index et la création d’index manquants (v1.x), le provider PostgreSQL (v2).

## 2. Décisions actées

Architecture :

- Approche Core-first : les nouveaux services d’orchestration vivent dans `SmartIndexManager.Core` et sont testables sans UI ni base de données.
- Les ViewModels App consomment les services Core via leurs interfaces. Aucune logique métier (score, redondance, éligibilité, DDL) ne vit dans l’UI.
- Le provider SQL Server gagne une méthode `ExecuteDdlAsync` pour la restauration, implémentée comme un passage direct à l’exécuteur. Cette méthode est une dérogation assumée à la règle « toute requête serveur vit dans un fichier SQL externe », car le DDL à rejouer provient des fichiers de backup déjà écrits par l’application.

Sécurité :

- Le mot de passe SQL n’est jamais stocké, même temporairement dans un ViewModel persistant. Il est demandé par une boîte de dialogue Avalonia, passé à `IIndexProviderFactory.ConnectAsync`, puis oublié.
- La double confirmation de suppression est une case à cocher explicite : « Je comprends que cette action est irréversible ».
- L’orchestrateur de suppression regénère le DDL depuis les métadonnées fraîches, écrit le fichier, le relit pour vérifier sa taille, puis exécute le `DROP INDEX`.
- Un échec sur un index n’arrête pas le lot ; l’index est marqué `failed` dans le manifeste et l’audit enregistre l’échec.

Restauration :

- L’écran de restauration liste toutes les sessions trouvées sous le répertoire de backup, par serveur et par horodatage.
- Chaque index d’une session peut être restauré individuellement ; une restauration multi-sélection est possible.
- La restauration refuse un index s’il existe déjà, si la table n’existe plus ou si la base n’existe plus.
- Une restauration réussie met à jour `restoredUtc` dans le manifeste et écrit une entrée d’audit.

Query Store :

- L’état du Query Store est affiché dans la barre d’état / panneau permissions pour la base courante.
- Si Query Store est désactivé et que l’utilisateur a les droits `ALTER`, un bouton permet de l’activer avec les paramètres par défaut du spec.

## 3. Architecture

### Couches

```
SmartIndexManager.Core (nouveaux fichiers)
  Deletion/
    DeletionBasket.cs
    DeletionBasketEntry.cs
    DeletionSession.cs
    DeletionOptions.cs
    DeletionOrchestrator.cs
    DeletionResult.cs
    DeletionProgress.cs
  DryRun/
    DryRunReport.cs
    DryRunReportEntry.cs
    DryRunReportBuilder.cs
    DryRunReportExporter.cs
    DryRunReliabilityBadge.cs
  Restore/
    RestoreService.cs
    RestoreSession.cs
  Settings/
    SettingsService.cs
    AppSettings.cs

SmartIndexManager.Core (réutilisés sans changer leur comportement)
  Backup/BackupWriter.cs
  Backup/FileNameSanitizer.cs
  Ddl/SqlServerDdlGenerator.cs
  Persistence/ManifestStore.cs
  Audit/AuditLog.cs
  Safety/DeletionSafetyEvaluator.cs
  Scoring/ConfidenceScorer.cs

SmartIndexManager.Core (modifiés)
  Persistence/Manifest.cs : ajout de la valeur Pending à IndexDeletionStatus

SmartIndexManager.Providers.SqlServer
  IIndexProvider enrichi de ExecuteDdlAsync
  SqlServerIndexProvider.Actions.cs : implémentation de ExecuteDdlAsync

SmartIndexManager.App
  Services/
    IPasswordPrompt.cs (déjà défini)
    PasswordPromptViewModel.cs
  ViewModels/
    DeletionBasketViewModel.cs
    DryRunViewModel.cs
    DryRunReportViewModel.cs
    RestoreViewModel.cs
    RestoreSessionViewModel.cs
    AuditViewModel.cs
    SettingsViewModel.cs
    QueryStoreStatusViewModel.cs
  Views/
    DeletionBasketView.axaml
    DryRunView.axaml
    RestoreView.axaml
    AuditView.axaml
    SettingsView.axaml
    PasswordPromptWindow.axaml
```

### Changements d’interface provider

`IIndexProvider` gagne :

```csharp
Task ExecuteDdlAsync(string database, string sql, CancellationToken cancellationToken = default);
```

Cette méthode exécute un DDL arbitraire (utilisé pour la restauration). Le provider SQL Server valide le nom de base contre `sys.databases` puis exécute le SQL.

## 4. Composants Core

### 4.1 DeletionBasket

Stocke les index ajoutés au panier. Chaque entrée garde le `SafetyAssessment` calculé au moment de l’ajout. Si l’éligibilité change entre temps (par exemple après un rechargement), l’entrée reste affichée avec un warning et l’orchestrateur refuse de la traiter.

```csharp
public sealed class DeletionBasket
{
    public IReadOnlyList<DeletionBasketEntry> Entries { get; }
    public BasketResult Add(IndexModel index, SafetyAssessment safety);
    public void Remove(IndexModel index);
    public void Clear();
}

public sealed record DeletionBasketEntry(
    IndexModel Index,
    SafetyAssessment Safety,
    ConfidenceScore? Score);
```

### 4.2 DeletionOrchestrator

Exécute le lot de suppression.

```csharp
public sealed record DeletionSession(
    string Server,
    string Operator,
    string ToolVersion,
    int InstanceUptimeDays,
    string BackupRoot,
    DeletionMode Mode);

public sealed record DeletionOptions(
    TimeSpan DropTimeout,
    string? Comment = null);

public sealed class DeletionOrchestrator
{
    public async Task<DeletionResult> DeleteAsync(
        IIndexProvider provider,
        DeletionSession session,
        DeletionBasket basket,
        DeletionOptions options,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken);
}
```

Déroulé :

Avant la boucle, recharge une seule fois les index des bases concernées via `provider.GetIndexesAsync`, puis filtre en mémoire pour chaque entrée du panier.

Pour chaque index :

1. Vérifie que l’entrée est toujours éligible (`Safety.Eligibility == Deletable`). Si ce n’est pas le cas, l’index est marqué `failed` sans action destructive.
2. Récupère les métadonnées fraîches depuis le rechargement unique. Si l’index a disparu ou est devenu non éligible, l’index est marqué `failed`.
3. Réévalue la sécurité avec les métadonnées fraîches et le DDL régénéré.
4. Génère le DDL de recréation via `SqlServerDdlGenerator.Generate`. Si le résultat est `DdlNotBackupable`, l’index est marqué `failed`.
5. Crée le répertoire de session `<BackupRoot>/<serveur assaini>/<horodatage ISO>/` ; le nom de serveur est assaini via `FileNameSanitizer.SanitizeComponent`.
6. Écrit le fichier de backup via `BackupWriter.WriteIndexBackup`.
7. Relit le fichier et vérifie qu’il n’est pas vide.
8. Ajoute ou met à jour l’entrée dans l’objet `Manifest` en mémoire avec le statut `Pending`, puis écrit le manifeste complet via `ManifestStore.Write`.
9. Mode `Execute` : appelle `provider.DropIndexAsync`.
   Mode `Script` : ajoute le `DROP INDEX` au fichier `drop-session.sql`.
10. Met à jour l’entrée du manifeste en mémoire avec le statut final (`Dropped`, `Failed` ou `Scripted`) et réécrit le manifeste complet via `ManifestStore.Write`.
11. Écrit une entrée d’audit via `AuditLog.Append` (succès ou échec).

Un échec à n’importe quelle étape entre 2 et 9 marque l’index `Failed`, met à jour l’entrée en mémoire, réécrit le manifeste et écrit l’audit, sans arrêter le lot.

`IndexDeletionStatus` est étendu avec la valeur `Pending`. L’écran de restauration tolère cette valeur (un index `Pending` peut être restauré car son backup est déjà sur disque).

### 4.3 DryRunReportBuilder

Assemble le rapport d’impact.

```csharp
public sealed class DryRunReportBuilder
{
    public async Task<DryRunReport> BuildAsync(
        IIndexProvider provider,
        DeletionBasket basket,
        CancellationToken cancellationToken);
}
```

Pour chaque index du panier :

- Structure et options.
- Statistiques d’usage.
- Requêtes utilisatrices via `GetQueryUsageAsync`.
- Hints via `GetHintsAsync`.
- Clés étrangères supportées (via `ProviderProperties["fkSupport"]`).
- Warnings du `SafetyAssessment`.
- Score et facteurs.

Le rapport inclut un résumé global : espace libéré estimé, updates évités, badge de fiabilité basé sur l’uptime, la plateforme et la disponibilité du Query Store.

### 4.4 DryRunReportExporter

Exporte le rapport en JSON et en markdown.

```csharp
public static class DryRunReportExporter
{
    public static void ExportJson(string path, DryRunReport report);
    public static void ExportMarkdown(string path, DryRunReport report);
}
```

Deux usages distincts :

- Archivage en session : quand le dry-run précède une suppression, les fichiers `dry-run.json` et `dry-run.md` sont écrits dans le répertoire de session au moment où la suppression est lancée. L’orchestrateur crée ce répertoire, puis y écrit une copie du rapport déjà généré en mémoire.
- Export autonome : depuis l’écran dry-run, l’utilisateur peut choisir un chemin cible (via boîte de dialogue d’enregistrement) ; l’exporteur écrit alors à l’emplacement demandé.

### 4.5 RestoreService

Gère la découverte des sessions et la restauration.

```csharp
public sealed class RestoreService
{
    public Task<IReadOnlyList<RestoreSession>> FindSessionsAsync(
        string backupRoot, string server, CancellationToken cancellationToken);

    public Task<RestoreResult> RestoreAsync(
        RestoreSession session,
        IReadOnlyList<ManifestIndexEntry> entries,
        IIndexProvider provider,
        CancellationToken cancellationToken);
}
```

La restauration :

- Passe sur la base cible via `provider` (sérialisé avec la même contrainte de connexion unique que les autres opérations longues).
- Vérifie que la base existe (`sql/sqlserver/database-exists.sql`).
- Vérifie que le schéma et la table existent (`sql/sqlserver/table-exists.sql`).
- Vérifie que l’index n’existe pas déjà (`sql/sqlserver/index-exists.sql`).
- Lit le fichier `.sql` de backup.
- Appelle `provider.ExecuteDdlAsync(database, ddl)`.
- Met à jour le manifeste via `ManifestStore.MarkRestored`, puis `ManifestStore.Write`.
- Écrit une entrée d’audit.

Fichiers SQL ajoutés pour les validations serveur :

- `sql/sqlserver/database-exists.sql` : colonne `Exists`.
- `sql/sqlserver/table-exists.sql` : colonnes `SchemaName`, `TableName`, `Exists`.
- `sql/sqlserver/index-exists.sql` : colonne `Exists`.

Chaque fichier porte un en-tête `-- sim:` et est couvert par `ScriptContractTests`.

### 4.6 SettingsService et AppSettings

```csharp
public sealed record AppSettings
{
    public string? DefaultBackupRoot { get; init; }
    public string? SnapshotRoot { get; init; }
    public int SnapshotRetentionDays { get; init; } = 90;
}

public sealed class SettingsService
{
    public AppSettings Load(string configDir);
    public void Save(string configDir, AppSettings settings);
}
```

`AppPaths` utilisera ces paramètres s’ils sont définis, sinon conservera les valeurs par défaut.

## 5. UI Avalonia

### 5.1 Remplacement des placeholders

Dans `ShellViewModel`, les quatre destinations placeholders deviennent :

- `DeletionBasketViewModel`
- `RestoreViewModel`
- `AuditViewModel`
- `SettingsViewModel`

### 5.2 Panier de suppression

`DeletionBasketView` affiche :

- La liste des index du panier avec base, schéma, table, nom, score, warnings.
- Boutons : Remove, Clear, Run dry-run, Delete (mode execute), Generate script (mode script).
- Une case à cocher de double confirmation avant suppression.

`BrowseView` gagne un bouton « Add to basket » sur chaque ligne ou dans le panneau de détail.

### 5.3 Dry-run

`DryRunView` affiche :

- Le résumé global (espace, updates, fiabilité).
- La liste des index avec leurs détails.
- Boutons Export JSON, Export Markdown.
- Bouton Proceed to deletion / Generate script.

### 5.4 Restauration

`RestoreView` affiche :

- Une arborescence par serveur, puis par session d’horodatage.
- Pour chaque session, la liste des index avec leur statut. `IndexDeletionStatus` contient `Dropped`, `Failed`, `Scripted`. Un index est affiché comme `restored` quand `RestoredUtc` est non nul, indépendamment du champ `Status`.
- Cases à cocher pour la multi-sélection.
- Bouton Restore pour la sélection.

### 5.5 Audit

`AuditView` affiche les entrées du fichier `audit.jsonl` sous le répertoire de configuration, avec filtrage par action, serveur et base.

### 5.6 Query Store

`PermissionStatusBar` est enrichi pour afficher l’état Query Store de la base courante et un bouton d’activation si applicable. `QueryStoreStatusViewModel` encapsule cet état et la commande d’activation.

### 5.7 Paramètres

`SettingsView` permet de modifier :

- Le répertoire de backup par défaut.
- Le répertoire de snapshots.
- La rétention des snapshots en jours.

### 5.8 Boîte de dialogue mot de passe

`PasswordPromptWindow` est une fenêtre modale Avalonia avec :

- Un message « Enter password for <connectionName> ».
- Un `TextBox` masqué (`PasswordChar`).
- Les boutons Connect et Cancel.

`PasswordPromptViewModel` expose `Password`, `ConnectCommand`, `CancelCommand`.

`AvaloniaDialogService` implémente `IPasswordPrompt` en affichant cette fenêtre.

## 6. Flux utilisateur

Connexion :

1. L’utilisateur sélectionne un profil et clique Connect.
2. Si le profil est en authentification SQL, `AvaloniaDialogService` affiche `PasswordPromptWindow`.
3. Le mot de passe est passé à `IIndexProviderFactory.ConnectAsync` et n’est stocké nulle part.

Sélection et suppression :

1. L’utilisateur ajoute des index éligibles au panier depuis Browse.
2. Il ouvre le panier, lance le dry-run.
3. Le dry-run affiche le rapport et écrit les fichiers JSON/markdown.
4. L’utilisateur choisit Delete ou Generate script.
5. Une boîte de dialogue avec case à cocher demande confirmation.
6. L’orchestrateur exécute le lot, écrit les backups, le manifeste et l’audit.

Restauration :

1. L’utilisateur ouvre Restore.
2. Les sessions de suppression sont listées.
3. Il sélectionne un ou plusieurs index et clique Restore.
4. `RestoreService` rejoue le DDL, met à jour le manifeste et écrit l’audit.

## 7. Tests

Core :

- `DeletionBasketTests` : ajout, retrait, refus des non éligibles, warnings.
- `DeletionOrchestratorTests` : mode Execute, mode Script, échec partiel, manifeste, audit, vérification du fichier backup.
- `DryRunReportBuilderTests` : assemblage du rapport avec `FakeIndexProvider`.
- `DryRunReportExporterTests` : JSON et markdown.
- `RestoreServiceTests` : découverte de sessions, restauration, refus si l’index existe.
- `SettingsServiceTests` : chargement/sauvegarde.

Provider :

- `ExecuteDdlAsync` : validation du nom de base et exécution.

App :

- `DeletionBasketViewModelTests` : commandes, navigation vers dry-run.
- `DryRunViewModelTests` : génération, exports.
- `RestoreViewModelTests` : sessions, sélection, restauration.
- `AuditViewModelTests` : chargement.
- `SettingsViewModelTests` : modification et persistance.
- `PasswordPromptViewModelTests` : saisie, validation, annulation.
- `QueryStoreStatusViewModelTests` : état, activation.

Tous les tests Core et App s’exécutent sans base de données.

## 8. Dépendances et impacts

Ajouts d’interface :

- `IIndexProvider.ExecuteDdlAsync`.
- `IAppPaths.DefaultBackupRoot` et `SnapshotRoot` tiennent compte de `AppSettings` si défini.

Fichiers modifiés :

- `src/SmartIndexManager.Core/Provider/IIndexProvider.cs`
- `src/SmartIndexManager.Core/Persistence/Manifest.cs`
- `src/SmartIndexManager.App/Services/IAppPaths.cs`
- `src/SmartIndexManager.App/Services/AppPaths.cs`
- `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`
- `src/SmartIndexManager.App/ViewModels/ShellViewModel.cs`
- `src/SmartIndexManager.App/Views/ShellView.axaml` (si nécessaire)
- `src/SmartIndexManager.App/Views/BrowseView.axaml`
- `src/SmartIndexManager.App/Views/PermissionStatusBar.axaml`

Fichiers SQL ajoutés :

- `sql/sqlserver/database-exists.sql`
- `sql/sqlserver/table-exists.sql`
- `sql/sqlserver/index-exists.sql`

Aucune modification des fichiers SQL existants n’est requise.
