# SmartIndexManager App 3b : actions correctives — plan d’implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the corrective-action slice of SmartIndexManager: deletion basket, dry-run report, execute/script deletion with DDL backup and manifest, restore screen, audit viewer, Query Store activation, settings, and the real SQL password prompt.

**Architecture:** Core-first. New orchestration services live in `SmartIndexManager.Core` and are unit-testable without a database or UI. The Avalonia App layer consumes them through ViewModels. `IIndexProvider` gains `ExecuteDdlAsync` for restore. Three small validation SQL scripts are added for restore pre-checks.

**Tech Stack:** C# / .NET 10, Avalonia 11, CommunityToolkit.Mvvm, xUnit, `System.Text.Json`.

## Global Constraints

Copied verbatim from `docs/specs/2026-07-20-smartindexmanager-design.md` and `docs/specs/2026-07-22-smartindexmanager-app-3b-design.md`:

- Target framework `net10.0`; nullable reference types and implicit usings enabled.
- Core has zero dependency on `Microsoft.Data.SqlClient` and zero dependency on any UI framework.
- Passwords are never stored; the password prompt returns the value once and it is passed directly to `IIndexProviderFactory.ConnectAsync`.
- Only nonclustered rowstore non-unique indexes are droppable; all hard exclusions from the spec remain.
- DDL that cannot be reconstructed with certainty is refused; `DdlNotBackupable` blocks that one index.
- External SQL files are the only source of server queries; restore validation queries get their own `.sql` files with `-- sim:` headers.
- A missing, unreadable, or invalid SQL file marks that feature as errored with an explicit message, never crashes the app.
- Manifest and snapshot JSON files carry `schemaVersion` (current value 1).
- Backup directory: `<DefaultBackupRoot>/<sanitized server>/<timestamp ISO>/`; server name is sanitized via `FileNameSanitizer.SanitizeComponent`.
- Single connection, no MARS: long operations on the provider are serialized; the App already uses a `SemaphoreSlim(1,1)` gate for detail loads and connect; deletion and restore reuse the same gate discipline.
- Every feature must be reachable from an xUnit test without instantiating the UI.

---

### Task 1: Extend `IndexDeletionStatus` with `Pending`

**Files:**
- Modify: `src/SmartIndexManager.Core/Persistence/Manifest.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs` (add a case)

**Interfaces:**
- `IndexDeletionStatus` enum gains `Pending` and `Restored`.

- [ ] **Step 1: Write the failing test**

In `tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs`, add:

```csharp
[Fact]
public void Pending_status_round_trips()
{
    var manifest = Sample() with
    {
        Indexes =
        [
            Sample().Indexes[0] with { Status = IndexDeletionStatus.Pending }
        ]
    };
    var path = Path.Combine(_dir, "pending.json");
    ManifestStore.Write(path, manifest);
    var read = ManifestStore.Read(path);
    Assert.Equal(IndexDeletionStatus.Pending, read.Indexes[0].Status);
}

[Fact]
public void Restored_status_round_trips()
{
    var manifest = Sample() with
    {
        Indexes =
        [
            Sample().Indexes[0] with { Status = IndexDeletionStatus.Restored, RestoredUtc = DateTime.UtcNow }
        ]
    };
    var path = Path.Combine(_dir, "restored.json");
    ManifestStore.Write(path, manifest);
    var read = ManifestStore.Read(path);
    Assert.Equal(IndexDeletionStatus.Restored, read.Indexes[0].Status);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~ManifestStoreTests"`
Expected: FAIL, `Pending` does not exist.

- [ ] **Step 3: Add the enum value**

In `src/SmartIndexManager.Core/Persistence/Manifest.cs`:

```csharp
public enum IndexDeletionStatus { Dropped, Failed, Scripted, Pending, Restored }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~ManifestStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Persistence/Manifest.cs tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs
git commit -m "feat(core): add Pending and Restored statuses to IndexDeletionStatus"
```

---

### Task 2: Add `ExecuteDdlAsync` to `IIndexProvider`

**Files:**
- Modify: `src/SmartIndexManager.Core/Provider/IIndexProvider.cs`
- Modify: `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs`
- Create: `tests/SmartIndexManager.Providers.SqlServer.Tests/Unit/ExecuteDdlTests.cs`

**Interfaces:**
- `IIndexProvider.ExecuteDdlAsync(string database, string sql, CancellationToken) -> Task`
- `IIndexProvider.IndexExistsAsync(string database, string schema, string table, string index, CancellationToken) -> Task<bool>`
- `SqlServerIndexProvider.ExecuteDdlAsync` validates the database name against `sys.databases` then runs the DDL.
- `SqlServerIndexProvider.IndexExistsAsync` validates with `index-exists.sql`.

- [ ] **Step 1: Write the failing test**

First create `sql/sqlserver/database-exists.sql` and `sql/sqlserver/index-exists.sql`:

`sql/sqlserver/index-exists.sql`:

```sql
-- sim: name=index-exists
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Exists
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.objects o ON o.object_id = i.object_id
    JOIN sys.schemas s ON s.schema_id = o.schema_id
    WHERE s.name = @SchemaName AND o.name = @TableName AND i.name = @IndexName
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Exists;
```

`sql/sqlserver/database-exists.sql`:

```sql
-- sim: name=database-exists
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Exists
SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS Exists;
```

Then create `tests/SmartIndexManager.Providers.SqlServer.Tests/Unit/ExecuteDdlTests.cs`:

```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Unit;

public class ExecuteDdlTests
{
    private static SqlServerIndexProvider Provider(ISqlExecutor executor)
        => new(executor, TestScriptRoot.Path(),
            new ServerInfo { ServerName = "x", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            new ProviderCapabilities(),
            new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true });

    [Fact]
    public async Task Validate_database_then_execute_ddl()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = true };
        var provider = Provider(executor);

        await provider.ExecuteDdlAsync("Sales", "CREATE INDEX IX_Tmp ON dbo.T(A);", CancellationToken.None);

        Assert.Equal(1, executor.ScalarCount);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Contains("CREATE INDEX", executor.LastExecutedSql);
    }

    [Fact]
    public async Task Unknown_database_throws_before_executing_ddl()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = false };
        var provider = Provider(executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ExecuteDdlAsync("MissingDb", "CREATE INDEX IX_Tmp ON dbo.T(A);", CancellationToken.None));

        Assert.Equal(1, executor.ScalarCount);
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task IndexExistsAsync_returns_scalar_result()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = true };
        var provider = Provider(executor);

        var exists = await provider.IndexExistsAsync("Sales", "dbo", "Orders", "IX_A", CancellationToken.None);

        Assert.True(exists);
        Assert.Equal(2, executor.ScalarCount);
        Assert.Equal(1, executor.ChangeDatabaseCount);
    }
}
```

Add a small helper for the script root:

```csharp
internal static class TestScriptRoot
{
    public static string Path()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        return System.IO.Path.Combine(dir!, "sql", "sqlserver");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ExecuteDdlTests"`
Expected: FAIL, `ExecuteDdlAsync` does not exist.

- [ ] **Step 3: Add the interface method**

In `src/SmartIndexManager.Core/Provider/IIndexProvider.cs`:

```csharp
Task ExecuteDdlAsync(string database, string sql, CancellationToken cancellationToken = default);
Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in the provider**

In `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs`:

```csharp
using SmartIndexManager.Core.Sql;

public async Task ExecuteDdlAsync(string database, string sql, CancellationToken cancellationToken = default)
{
    var script = SqlScriptLoader.Load(_scriptRoot, "database-exists");
    var exists = await _executor.ScalarAsync<bool>(
        script, new Dictionary<string, object?> { ["@DatabaseName"] = database }, cancellationToken).ConfigureAwait(false);
    if (!exists) throw new InvalidOperationException($"unknown database: {database}");

    await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
    await _executor.ExecuteAsync(sql, null, timeout: null, cancellationToken).ConfigureAwait(false);
}

public async Task<bool> IndexExistsAsync(
    string database, string schema, string table, string index,
    CancellationToken cancellationToken = default)
{
    var script = SqlScriptLoader.Load(_scriptRoot, "database-exists");
    var exists = await _executor.ScalarAsync<bool>(
        script, new Dictionary<string, object?> { ["@DatabaseName"] = database }, cancellationToken).ConfigureAwait(false);
    if (!exists) return false;

    await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
    var indexScript = SqlScriptLoader.Load(_scriptRoot, "index-exists");
    return await _executor.ScalarAsync<bool>(indexScript, new Dictionary<string, object?>
    {
        ["@SchemaName"] = schema,
        ["@TableName"] = table,
        ["@IndexName"] = index
    }, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 5: Update the App test fake**

In `tests/SmartIndexManager.App.Tests/Fakes/FakeIndexProvider.cs`, add:

```csharp
public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct = default)
    => Task.CompletedTask;

public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct = default)
    => Task.FromResult(false);
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ExecuteDdlTests"`
Expected: PASS. Also run the full provider unit suite.

- [ ] **Step 7: Commit**

```bash
git add sql/sqlserver/database-exists.sql sql/sqlserver/index-exists.sql src/SmartIndexManager.Core/Provider/IIndexProvider.cs src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Unit/ExecuteDdlTests.cs tests/SmartIndexManager.App.Tests/Fakes/FakeIndexProvider.cs
git commit -m "feat(provider): ExecuteDdlAsync and IndexExistsAsync for restore"
```

---

### Task 3: Validation SQL scripts for restore

**Files:**
- Create: `sql/sqlserver/table-exists.sql`
- Modify: `tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs`

**Interfaces:**
- One read-only script with `-- sim:` header declaring expected columns. `database-exists.sql` and `index-exists.sql` were created in Task 2.

- [ ] **Step 1: Write the script**

`sql/sqlserver/table-exists.sql`:

```sql
-- sim: name=table-exists
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Exists
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM sys.objects o
    JOIN sys.schemas s ON s.schema_id = o.schema_id
    WHERE s.name = @SchemaName AND o.name = @TableName AND o.type = 'U'
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS Exists;
```

- [ ] **Step 2: Extend the contract test**

In `tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs`, add to the Theory:

```csharp
[InlineData("table-exists", new[] { "Exists" })]
```

- [ ] **Step 3: Run the contract test**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ScriptContractTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add sql/sqlserver/table-exists.sql tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs
git commit -m "feat(provider): table-exists validation SQL script"
```

---

### Task 4: `SettingsService` and `AppSettings`

**Files:**
- Create: `src/SmartIndexManager.Core/Settings/AppSettings.cs`, `SettingsService.cs`
- Modify: `src/SmartIndexManager.App/Services/IAppPaths.cs`, `AppPaths.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Settings/SettingsServiceTests.cs`, `tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs`

**Interfaces:**
- `AppSettings { string? DefaultBackupRoot; string? SnapshotRoot; int SnapshotRetentionDays; }`
- `SettingsService.Load(configDir) -> AppSettings`, `Save(configDir, AppSettings)`.
- `IAppPaths` exposes the resolved roots; `AppPaths` accepts optional overrides.

- [ ] **Step 1: Write the failing tests**

`tests/SmartIndexManager.Core.Tests/Settings/SettingsServiceTests.cs`:

```csharp
using SmartIndexManager.Core.Settings;
using Xunit;

namespace SmartIndexManager.Core.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-settings-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Round_trips_settings()
    {
        var service = new SettingsService();
        var settings = new AppSettings { DefaultBackupRoot = "/backups", SnapshotRoot = "/snaps", SnapshotRetentionDays = 60 };
        service.Save(_dir, settings);
        var loaded = service.Load(_dir);
        Assert.Equal("/backups", loaded.DefaultBackupRoot);
        Assert.Equal("/snaps", loaded.SnapshotRoot);
        Assert.Equal(60, loaded.SnapshotRetentionDays);
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var loaded = new SettingsService().Load(_dir);
        Assert.Null(loaded.DefaultBackupRoot);
        Assert.Null(loaded.SnapshotRoot);
        Assert.Equal(90, loaded.SnapshotRetentionDays);
    }
}
```

`tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs`, add:

```csharp
[Fact]
public void Overrides_use_settings_when_provided()
{
    var paths = new AppPaths(configDir: "/cfg", documentsDir: "/docs", sqlScriptRoot: "/sql",
        settings: new AppSettings { DefaultBackupRoot = "/custom/backups", SnapshotRoot = "/custom/snaps" });
    Assert.Equal("/custom/backups", paths.DefaultBackupRoot);
    Assert.Equal("/custom/snaps", paths.SnapshotRoot);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~SettingsServiceTests"`
Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AppPathsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `AppSettings` and `SettingsService`**

`src/SmartIndexManager.Core/Settings/AppSettings.cs`:

```csharp
namespace SmartIndexManager.Core.Settings;

public sealed record AppSettings
{
    public string? DefaultBackupRoot { get; init; }
    public string? SnapshotRoot { get; init; }
    public int SnapshotRetentionDays { get; init; } = 90;
}
```

`src/SmartIndexManager.Core/Settings/SettingsService.cs`:

```csharp
using System.Text.Json;

namespace SmartIndexManager.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AppSettings Load(string configDir)
    {
        var path = Path.Combine(configDir, "settings.json");
        if (!File.Exists(path)) return new AppSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }

    public void Save(string configDir, AppSettings settings)
    {
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 4: Update `IAppPaths` and `AppPaths`**

`src/SmartIndexManager.App/Services/IAppPaths.cs`:

```csharp
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Services;

public interface IAppPaths
{
    string ConfigDir { get; }
    string SnapshotRoot { get; }
    string DefaultBackupRoot { get; }
    string SqlScriptRoot { get; }
    AppSettings Settings { get; }
}
```

`src/SmartIndexManager.App/Services/AppPaths.cs`:

```csharp
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Services;

public sealed class AppPaths : IAppPaths
{
    public string ConfigDir { get; }
    public string SnapshotRoot { get; }
    public string DefaultBackupRoot { get; }
    public string SqlScriptRoot { get; }
    public AppSettings Settings { get; }

    public AppPaths(string configDir, string documentsDir, string sqlScriptRoot, AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        ConfigDir = configDir;
        // SnapshotStore appends its own "snapshots" segment, so the root is the config dir by default.
        SnapshotRoot = Settings.SnapshotRoot ?? configDir;
        DefaultBackupRoot = Settings.DefaultBackupRoot ?? Path.Combine(documentsDir, "SmartIndexManager");
        SqlScriptRoot = sqlScriptRoot;
    }

    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var config = Path.Combine(appData, "SmartIndexManager");
        var settings = new SettingsService().Load(config);
        var sqlRoot = Path.Combine(AppContext.BaseDirectory, "sql", "sqlserver");
        return new AppPaths(config, documents, sqlRoot, settings);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~SettingsServiceTests"`
Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AppPathsTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.Core/Settings/ src/SmartIndexManager.App/Services/IAppPaths.cs src/SmartIndexManager.App/Services/AppPaths.cs tests/SmartIndexManager.Core.Tests/Settings/ tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs
git commit -m "feat(core/app): settings service and app paths overrides"
```

---

### Task 5: `DeletionBasket`

**Files:**
- Create: `src/SmartIndexManager.Core/Deletion/DeletionBasket.cs`, `DeletionBasketEntry.cs`, `BasketResult.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Deletion/DeletionBasketTests.cs`

**Interfaces:**
- `DeletionBasket.Add(IndexModel, SafetyAssessment, ConfidenceScore?) -> BasketResult`
- `DeletionBasket.Remove(IndexModel)`
- `DeletionBasket.Clear()`
- `IReadOnlyList<DeletionBasketEntry> Entries`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Deletion/DeletionBasketTests.cs`:

```csharp
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Deletion;

public class DeletionBasketTests
{
    private static IndexModel Nc() => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
        Type = IndexType.NonclusteredRowstore
    };

    private static SafetyAssessment Deletable() => new(DeletionEligibility.Deletable, null, []);
    private static SafetyAssessment NotDeletable() => new(DeletionEligibility.NotDeletable, "unique", []);

    [Fact]
    public void Add_deletable_index_succeeds()
    {
        var basket = new DeletionBasket();
        var result = basket.Add(Nc(), Deletable());
        Assert.True(result.Success);
        Assert.Single(basket.Entries);
    }

    [Fact]
    public void Add_not_deletable_index_fails()
    {
        var basket = new DeletionBasket();
        var result = basket.Add(Nc(), NotDeletable());
        Assert.False(result.Success);
        Assert.Empty(basket.Entries);
    }

    [Fact]
    public void Remove_clears_entry()
    {
        var basket = new DeletionBasket();
        var index = Nc();
        basket.Add(index, Deletable());
        basket.Remove(index);
        Assert.Empty(basket.Entries);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DeletionBasketTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.Core/Deletion/BasketResult.cs`:

```csharp
namespace SmartIndexManager.Core.Deletion;

public sealed record BasketResult(bool Success, string? Error);
```

`src/SmartIndexManager.Core/Deletion/DeletionBasketEntry.cs`:

```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionBasketEntry(
    IndexModel Index,
    SafetyAssessment Safety,
    ConfidenceScore? Score);
```

`src/SmartIndexManager.Core/Deletion/DeletionBasket.cs`:

```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.Deletion;

public sealed class DeletionBasket
{
    private readonly List<DeletionBasketEntry> _entries = [];

    public IReadOnlyList<DeletionBasketEntry> Entries => _entries.AsReadOnly();

    public BasketResult Add(IndexModel index, SafetyAssessment safety, ConfidenceScore? score = null)
    {
        if (safety.Eligibility != DeletionEligibility.Deletable)
            return new BasketResult(false, "Index is not deletable.");
        if (_entries.Any(e => Matches(e.Index, index)))
            return new BasketResult(false, "Index is already in the basket.");
        _entries.Add(new DeletionBasketEntry(index, safety, score));
        return new BasketResult(true, null);
    }

    public void Remove(IndexModel index)
        => _entries.RemoveAll(e => Matches(e.Index, index));

    public void Clear() => _entries.Clear();

    private static bool Matches(IndexModel a, IndexModel b)
        => string.Equals(a.Database, b.Database, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Table, b.Table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DeletionBasketTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Deletion/ tests/SmartIndexManager.Core.Tests/Deletion/
git commit -m "feat(core): deletion basket with eligibility validation"
```

---

### Task 6: `DryRunReport` model and exporter

**Files:**
- Create: `src/SmartIndexManager.Core/DryRun/DryRunReliabilityBadge.cs`, `DryRunReport.cs`, `DryRunReportEntry.cs`, `DryRunReportExporter.cs`
- Test: `tests/SmartIndexManager.Core.Tests/DryRun/DryRunReportExporterTests.cs`

**Interfaces:**
- `DryRunReportExporter.ExportJson(path, report)` and `ExportMarkdown(path, report)`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/DryRun/DryRunReportExporterTests.cs`:

```csharp
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.DryRun;

public class DryRunReportExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-dryrun-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DryRunReport Sample() => new()
    {
        Server = "PROD01",
        Databases = ["Sales"],
        CreatedUtc = new DateTime(2026, 07, 22, 10, 0, 0, DateTimeKind.Utc),
        UptimeDays = 92,
        ReliabilityBadge = DryRunReliabilityBadge.Green,
        Entries =
        [
            new DryRunReportEntry
            {
                Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                Type = "NonclusteredRowstore", Key = "CustomerId", Includes = "",
                SizeMb = 12.5, Seeks = 0, Scans = 0, Updates = 1_200_000,
                Score = 94
            }
        ]
    };

    [Fact]
    public void ExportJson_writes_report()
    {
        var path = Path.Combine(_dir, "report.json");
        DryRunReportExporter.ExportJson(path, Sample());
        var json = File.ReadAllText(path);
        Assert.Contains("\"server\": \"PROD01\"", json);
        Assert.Contains("IX_A", json);
    }

    [Fact]
    public void ExportMarkdown_writes_report()
    {
        var path = Path.Combine(_dir, "report.md");
        DryRunReportExporter.ExportMarkdown(path, Sample());
        var md = File.ReadAllText(path);
        Assert.Contains("PROD01", md);
        Assert.Contains("IX_A", md);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DryRunReportExporterTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.Core/DryRun/DryRunReliabilityBadge.cs`:

```csharp
namespace SmartIndexManager.Core.DryRun;

public enum DryRunReliabilityBadge { Green, Orange, Red }
```

`src/SmartIndexManager.Core/DryRun/DryRunReportEntry.cs`:

```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.DryRun;

public sealed record DryRunReportEntry
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public required string Type { get; init; }
    public required string Key { get; init; }
    public required string Includes { get; init; }
    public string? Filter { get; init; }
    public double SizeMb { get; init; }
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<ScoreFactor> ScoreFactors { get; init; } = [];
    public IReadOnlyList<SafetyWarning> Warnings { get; init; } = [];
    public IReadOnlyList<QueryUsage> Queries { get; init; } = [];
    public IReadOnlyList<IndexHint> Hints { get; init; } = [];
    public bool SupportsForeignKey { get; init; }
}
```

`src/SmartIndexManager.Core/DryRun/DryRunReport.cs`:

```csharp
namespace SmartIndexManager.Core.DryRun;

public sealed record DryRunReport
{
    public required string Server { get; init; }
    public IReadOnlyList<string> Databases { get; init; } = [];
    public required DateTime CreatedUtc { get; init; }
    public int UptimeDays { get; init; }
    public DryRunReliabilityBadge ReliabilityBadge { get; init; }
    public double TotalSizeMb { get; init; }
    public long TotalUpdates { get; init; }
    public IReadOnlyList<DryRunReportEntry> Entries { get; init; } = [];
}
```

`src/SmartIndexManager.Core/DryRun/DryRunReportExporter.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace SmartIndexManager.Core.DryRun;

public static class DryRunReportExporter
{
    public static void ExportJson(string path, DryRunReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    public static void ExportMarkdown(string path, DryRunReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.AppendLine($"# Dry-run report — {report.Server}");
        sb.AppendLine();
        sb.AppendLine($"- Created (UTC): {report.CreatedUtc:O}");
        sb.AppendLine($"- Instance uptime: {report.UptimeDays} days");
        sb.AppendLine($"- Reliability: {report.ReliabilityBadge}");
        sb.AppendLine($"- Estimated space freed: {report.TotalSizeMb:0.0} MB");
        sb.AppendLine($"- Estimated updates avoided: {report.TotalUpdates}");
        sb.AppendLine();
        foreach (var e in report.Entries)
        {
            sb.AppendLine($"## {e.Database}.{e.Schema}.{e.Table}.{e.Index}");
            sb.AppendLine($"- Type: {e.Type}");
            sb.AppendLine($"- Key: {e.Key}");
            sb.AppendLine($"- Includes: {e.Includes}");
            if (!string.IsNullOrEmpty(e.Filter)) sb.AppendLine($"- Filter: {e.Filter}");
            sb.AppendLine($"- Size: {e.SizeMb:0.0} MB");
            sb.AppendLine($"- Score: {e.Score}");
            if (e.SupportsForeignKey) sb.AppendLine("- Supports a foreign key");
            foreach (var w in e.Warnings) sb.AppendLine($"- Warning: {w.Message}");
            foreach (var h in e.Hints) sb.AppendLine($"- Hint: {h.Reference} ({h.Kind})");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DryRunReportExporterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/DryRun/ tests/SmartIndexManager.Core.Tests/DryRun/
git commit -m "feat(core): dry-run report model and exporter"
```

---

### Task 7: `DryRunReportBuilder`

**Files:**
- Create: `src/SmartIndexManager.Core/DryRun/DryRunReportBuilder.cs`
- Test: `tests/SmartIndexManager.Core.Tests/DryRun/DryRunReportBuilderTests.cs`

**Interfaces:**
- `DryRunReportBuilder.BuildAsync(IIndexProvider, DeletionBasket, CancellationToken) -> Task<DryRunReport>`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/DryRun/DryRunReportBuilderTests.cs`:

```csharp
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.DryRun;

public class DryRunReportBuilderTests
{
    private sealed class FakeProvider : IIndexProvider
    {
        public ServerInfo ServerInfo { get; } = new()
        {
            ServerName = "PROD01", ProductVersion = new Version(16, 0),
            Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 92
        };
        public ProviderCapabilities Capabilities { get; } = new()
            { SupportsPlanCache = true, SupportsQueryStore = true };
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexModel>>([]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([new QueryUsage("SELECT 1", 5, DateTime.UtcNow, UsageSource.PlanCache)]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Builds_report_with_usage_and_hints()
    {
        var basket = new DeletionBasket();
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
            Type = IndexType.NonclusteredRowstore,
            Usage = new IndexUsageStats(0, 0, 0, 100, null, null),
            Size = new IndexSizeInfo(100, 1000, 8.0)
        };
        basket.Add(index, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));

        var report = await new DryRunReportBuilder().BuildAsync(new FakeProvider(), basket, CancellationToken.None);

        Assert.Equal("PROD01", report.Server);
        Assert.Single(report.Entries);
        Assert.Single(report.Entries[0].Queries);
        Assert.Equal(8.0, report.TotalSizeMb);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DryRunReportBuilderTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.Core/DryRun/DryRunReportBuilder.cs`:

```csharp
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Core.DryRun;

public sealed class DryRunReportBuilder
{
    public async Task<DryRunReport> BuildAsync(
        IIndexProvider provider,
        DeletionBasket basket,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<DryRunReportEntry>();
        foreach (var e in basket.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = e.Index;
            var reference = IndexRef.Of(index);

            var queries = await provider.GetQueryUsageAsync(reference, cancellationToken).ConfigureAwait(false);
            var hints = await provider.GetHintsAsync(reference, cancellationToken).ConfigureAwait(false);

            entries.Add(new DryRunReportEntry
            {
                Database = index.Database,
                Schema = index.Schema,
                Table = index.Table,
                Index = index.Name,
                Type = index.Type.ToString(),
                Key = string.Join(", ", index.KeyColumns.Select(c => c.Name)),
                Includes = string.Join(", ", index.IncludedColumns),
                Filter = index.FilterPredicate,
                SizeMb = index.Size.SizeMb,
                Seeks = index.Usage.Seeks,
                Scans = index.Usage.Scans,
                Lookups = index.Usage.Lookups,
                Updates = index.Usage.Updates,
                LastRead = index.Usage.LastRead,
                Score = e.Score?.Value ?? 0,
                ScoreFactors = e.Score?.Factors ?? [],
                Warnings = e.Safety.Warnings,
                Queries = queries,
                Hints = hints,
                SupportsForeignKey = index.ProviderProperties.ContainsKey("fkSupport")
            });
        }

        var reliability = ComputeReliability(provider, entries);
        return new DryRunReport
        {
            Server = provider.ServerInfo.ServerName,
            Databases = basket.Entries.Select(e => e.Index.Database).Distinct().ToList(),
            CreatedUtc = DateTime.UtcNow,
            UptimeDays = provider.ServerInfo.UptimeDays,
            ReliabilityBadge = reliability,
            TotalSizeMb = entries.Sum(e => e.SizeMb),
            TotalUpdates = entries.Sum(e => e.Updates),
            Entries = entries
        };
    }

    private static DryRunReliabilityBadge ComputeReliability(IIndexProvider provider, List<DryRunReportEntry> entries)
    {
        if (provider.ServerInfo.UptimeDays < 30)
            return DryRunReliabilityBadge.Orange;
        if (provider.ServerInfo.Platform == ServerPlatform.AzureSqlDatabase)
            return DryRunReliabilityBadge.Orange;
        if (entries.Any(e => e.Queries.Count == 0 && provider.Capabilities.SupportsQueryStore))
            return DryRunReliabilityBadge.Orange;
        return DryRunReliabilityBadge.Green;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DryRunReportBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/DryRun/DryRunReportBuilder.cs tests/SmartIndexManager.Core.Tests/DryRun/DryRunReportBuilderTests.cs
git commit -m "feat(core): dry-run report builder"
```

---

### Task 8: `DeletionOrchestrator`

**Files:**
- Create: `src/SmartIndexManager.Core/Deletion/DeletionSession.cs`, `DeletionOptions.cs`, `DeletionResult.cs`, `DeletionProgress.cs`, `DeletionOrchestrator.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Deletion/DeletionOrchestratorTests.cs`

**Interfaces:**
- `DeletionOrchestrator.DeleteAsync(IIndexProvider, DeletionSession, DeletionBasket, DeletionOptions, IProgress<DeletionProgress>?, CancellationToken) -> Task<DeletionResult>`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Deletion/DeletionOrchestratorTests.cs`:

```csharp
using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Deletion;

public class DeletionOrchestratorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-delete-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("sim-audit-").FullName;
    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Directory.Delete(_auditDir, recursive: true);
    }

    private sealed class FakeProvider : IIndexProvider
    {
        public ServerInfo ServerInfo { get; } = new()
        {
            ServerName = "PROD01", ProductVersion = new Version(16, 0),
            Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 92
        };
        public ProviderCapabilities Capabilities { get; } = new() { SupportsPlanCache = true };
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public List<IndexRef> Dropped { get; } = [];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IndexModel>>([
                new IndexModel
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
                    Type = IndexType.NonclusteredRowstore,
                    KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
                }
            ]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct)
        {
            Dropped.Add(index);
            return Task.CompletedTask;
        }
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct) => Task.FromResult(false);
    }

    private DeletionSession Session() => new("PROD01", "DOMAIN\\rudi", "1.0.0", 92, _dir, DeletionMode.Execute);

    private DeletionBasket Basket()
    {
        var b = new DeletionBasket();
        b.Add(new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
            Type = IndexType.NonclusteredRowstore,
            KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
        }, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));
        return b;
    }

    [Fact]
    public async Task Execute_mode_drops_index_writes_backup_manifest_and_audit()
    {
        var provider = new FakeProvider();
        var auditPath = Path.Combine(_auditDir, "audit.jsonl");
        var orchestrator = new DeletionOrchestrator(auditPath);

        var result = await orchestrator.DeleteAsync(provider, Session(), Basket(), new DeletionOptions(TimeSpan.FromSeconds(30)), null, CancellationToken.None);

        Assert.Single(result.Results);
        Assert.Single(provider.Dropped);
        Assert.True(Directory.GetFiles(_dir, "*.sql", SearchOption.AllDirectories).Length >= 1);
        Assert.True(Directory.GetFiles(_dir, "manifest.json", SearchOption.AllDirectories).Length == 1);
        Assert.True(File.Exists(auditPath));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DeletionOrchestratorTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the records**

`src/SmartIndexManager.Core/Deletion/DeletionSession.cs`:

```csharp
using SmartIndexManager.Core.Persistence;

namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionSession(
    string Server,
    string Operator,
    string ToolVersion,
    int InstanceUptimeDays,
    string BackupRoot,
    DeletionMode Mode);
```

`src/SmartIndexManager.Core/Deletion/DeletionOptions.cs`:

```csharp
namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionOptions(TimeSpan DropTimeout, string? Comment = null);
```

`src/SmartIndexManager.Core/Deletion/DeletionProgress.cs`:

```csharp
namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionProgress(string IndexName, string Status);
```

`src/SmartIndexManager.Core/Deletion/DeletionResult.cs`:

```csharp
namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionResult(IReadOnlyList<IndexDeletionResult> Results);
public sealed record IndexDeletionResult(
    string Database, string Schema, string Table, string Index,
    IndexDeletionStatus Status, string? Error);
```

- [ ] **Step 4: Implement `DeletionOrchestrator`**

`src/SmartIndexManager.Core/Deletion/DeletionOrchestrator.cs`:

```csharp
using System.Text;
using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Backup;
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.Deletion;

public sealed class DeletionOrchestrator
{
    private readonly string _auditLogPath;

    public DeletionOrchestrator(string auditLogPath) => _auditLogPath = auditLogPath;

    public async Task<DeletionResult> DeleteAsync(
        IIndexProvider provider,
        DeletionSession session,
        DeletionBasket basket,
        DeletionOptions options,
        IProgress<DeletionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sessionDir = CreateSessionDir(session);
        var manifest = new Manifest
        {
            ToolVersion = session.ToolVersion,
            CreatedUtc = DateTime.UtcNow,
            Server = session.Server,
            Operator = session.Operator,
            InstanceUptimeDays = session.InstanceUptimeDays,
            Mode = session.Mode,
            Indexes = []
        };

        var databases = basket.Entries.Select(e => e.Index.Database).Distinct().ToList();
        var freshIndexes = await provider.GetIndexesAsync(databases, cancellationToken).ConfigureAwait(false);

        var results = new List<IndexDeletionResult>();
        var manifestEntries = new List<ManifestIndexEntry>();
        var scriptBuilder = session.Mode == DeletionMode.Script ? new StringBuilder() : null;

        foreach (var entry in basket.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProcessEntryAsync(provider, session, entry, freshIndexes, manifest, manifestEntries, scriptBuilder, sessionDir, options, progress, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        if (scriptBuilder is not null)
        {
            var scriptPath = Path.Combine(sessionDir, "drop-session.sql");
            File.WriteAllText(scriptPath, scriptBuilder.ToString());
        }

        manifest = manifest with { Indexes = manifestEntries };
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);

        return new DeletionResult(results);
    }

    private async Task<IndexDeletionResult> ProcessEntryAsync(
        IIndexProvider provider,
        DeletionSession session,
        DeletionBasketEntry entry,
        IReadOnlyList<IndexModel> freshIndexes,
        Manifest manifest,
        List<ManifestIndexEntry> manifestEntries,
        StringBuilder? scriptBuilder,
        string sessionDir,
        DeletionOptions options,
        IProgress<DeletionProgress>? progress,
        CancellationToken ct)
    {
        var index = entry.Index;
        var key = (index.Database, index.Schema, index.Table, index.Name);
        progress?.Report(new DeletionProgress(index.Name, "checking"));

        if (entry.Safety.Eligibility != DeletionEligibility.Deletable)
        {
            var err = $"Index {index.Name} is no longer deletable.";
            await AuditAsync(session, ModeAction(session.Mode), index, false, err, ct);
            return Fail(index, err);
        }

        var fresh = freshIndexes.FirstOrDefault(i =>
            string.Equals(i.Database, index.Database, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.Schema, index.Schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.Table, index.Table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.Name, index.Name, StringComparison.OrdinalIgnoreCase));

        if (fresh is null)
        {
            var err = $"Index {index.Name} no longer exists.";
            await AuditAsync(session, ModeAction(session.Mode), index, false, err, ct);
            return Fail(index, err);
        }

        var ddl = SqlServerDdlGenerator.Generate(fresh);
        if (ddl is DdlNotBackupable nb)
        {
            var err = $"DDL not backupable: {nb.Reason}";
            await AuditAsync(session, ModeAction(session.Mode), index, false, err, ct);
            return Fail(index, err);
        }

        var ddlSql = ((DdlSuccess)ddl).Sql;
        var safety = DeletionSafetyEvaluator.Evaluate(new SafetyInputs
        {
            Index = fresh,
            Ddl = ddl,
            InstanceUptimeDays = session.InstanceUptimeDays
        });
        if (safety.Eligibility != DeletionEligibility.Deletable)
        {
            var err = $"Index {index.Name} is no longer deletable after refresh.";
            await AuditAsync(session, ModeAction(session.Mode), index, false, err, ct);
            return Fail(index, err);
        }

        var fileName = BackupWriter.WriteIndexBackup(sessionDir, fresh, ddlSql, new BackupHeaderInfo
        {
            DateUtc = DateTime.UtcNow,
            Server = session.Server,
            Database = index.Database,
            Operator = session.Operator,
            Reason = BuildReason(index, entry.Score),
            Stats = index.Usage
        });

        var backupPath = Path.Combine(sessionDir, fileName);
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
        {
            var err = "Backup file is empty or missing.";
            await AuditAsync(session, ModeAction(session.Mode), index, false, err, ct);
            return Fail(index, err);
        }

        var manifestEntry = new ManifestIndexEntry
        {
            Database = index.Database,
            Schema = index.Schema,
            Table = index.Table,
            Index = index.Name,
            File = fileName,
            Reason = BuildReason(index, entry.Score),
            Comment = options.Comment ?? "",
            Score = entry.Score?.Value ?? 0,
            Stats = new ManifestStats
            {
                Seeks = index.Usage.Seeks,
                Scans = index.Usage.Scans,
                Lookups = index.Usage.Lookups,
                Updates = index.Usage.Updates,
                LastRead = index.Usage.LastRead,
                SizeMb = index.Size.SizeMb
            },
            Status = IndexDeletionStatus.Pending
        };
        manifestEntries.Add(manifestEntry);
        var currentManifest = manifest with { Indexes = manifestEntries };
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), currentManifest);

        try
        {
            if (session.Mode == DeletionMode.Execute)
            {
                progress?.Report(new DeletionProgress(index.Name, "dropping"));
                await provider.DropIndexAsync(IndexRef.Of(index), options.DropTimeout, ct).ConfigureAwait(false);
                manifestEntry = manifestEntry with { Status = IndexDeletionStatus.Dropped };
            }
            else
            {
                scriptBuilder?.AppendLine($"DROP INDEX {Quote(index.Name)} ON {Quote(index.Schema)}.{Quote(index.Table)};");
                manifestEntry = manifestEntry with { Status = IndexDeletionStatus.Scripted };
            }
            manifestEntries[^1] = manifestEntry;
            currentManifest = manifest with { Indexes = manifestEntries };
            ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), currentManifest);
            await AuditAsync(session, ModeAction(session.Mode), index, true, null, ct);
            progress?.Report(new DeletionProgress(index.Name, manifestEntry.Status.ToString().ToLowerInvariant()));
            return new IndexDeletionResult(index.Database, index.Schema, index.Table, index.Name, manifestEntry.Status, null);
        }
        catch (Exception ex)
        {
            manifestEntry = manifestEntry with { Status = IndexDeletionStatus.Failed };
            manifestEntries[^1] = manifestEntry;
            currentManifest = manifest with { Indexes = manifestEntries };
            ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), currentManifest);
            await AuditAsync(session, ModeAction(session.Mode), index, false, ex.Message, ct);
            return Fail(index, ex.Message);
        }
    }

    private static IndexDeletionResult Fail(IndexModel index, string error)
        => new(index.Database, index.Schema, index.Table, index.Name, IndexDeletionStatus.Failed, error);

    private static string CreateSessionDir(DeletionSession session)
    {
        var serverDir = FileNameSanitizer.SanitizeComponent(session.Server);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        var dir = Path.Combine(session.BackupRoot, serverDir, timestamp);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BuildReason(IndexModel index, ConfidenceScore? score)
    {
        var sb = new StringBuilder();
        sb.Append($"0 reads, {index.Usage.Updates} updates");
        if (score is not null) sb.Append($", score {score.Value}");
        return sb.ToString();
    }

    private Task AuditAsync(DeletionSession session, AuditAction action, IndexModel index, bool success, string? error, CancellationToken ct)
    {
        var detail = success
            ? $"{action} {index.Schema}.{index.Table}.{index.Name}"
            : $"{action} {index.Schema}.{index.Table}.{index.Name} failed: {error}";
        AuditLog.Append(_auditLogPath, new AuditEntry(
            DateTime.UtcNow, action, session.Server, index.Database, session.Operator, detail));
        return Task.CompletedTask;
    }

    private static AuditAction ModeAction(DeletionMode mode)
        => mode == DeletionMode.Execute ? AuditAction.Drop : AuditAction.GenerateScript;

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~DeletionOrchestratorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.Core/Deletion/ tests/SmartIndexManager.Core.Tests/Deletion/DeletionOrchestratorTests.cs
git commit -m "feat(core): deletion orchestrator with backup, manifest and audit"
```

---

### Task 9: `RestoreService`

**Files:**
- Create: `src/SmartIndexManager.Core/Restore/RestoreService.cs`, `RestoreSession.cs`, `RestoreResult.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Restore/RestoreServiceTests.cs`

**Interfaces:**
- `RestoreService.FindSessionsAsync(backupRoot, server, ct) -> Task<IReadOnlyList<RestoreSession>>`
- `RestoreService.RestoreAsync(session, entries, provider, ct) -> Task<RestoreResult>`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Restore/RestoreServiceTests.cs`:

```csharp
using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Restore;
using Xunit;

namespace SmartIndexManager.Core.Tests.Restore;

public class RestoreServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-restore-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("sim-audit-").FullName;
    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Directory.Delete(_auditDir, recursive: true);
    }

    private sealed class FakeProvider : IIndexProvider
    {
        public ServerInfo ServerInfo { get; } = new()
        {
            ServerName = "PROD01", ProductVersion = new Version(16, 0),
            Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 92
        };
        public ProviderCapabilities Capabilities { get; } = new();
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public List<string> ExecutedDdl { get; } = [];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexModel>>([]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct)
        {
            ExecutedDdl.Add(sql);
            return Task.CompletedTask;
        }
        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct) => Task.FromResult(false);
    }

    [Fact]
    public async Task Finds_sessions_and_restores_index()
    {
        var manifest = new Manifest
        {
            ToolVersion = "1.0.0",
            CreatedUtc = DateTime.UtcNow,
            Server = "PROD01",
            Operator = "op",
            Indexes =
            [
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                    File = "Sales.dbo.Orders.IX_A.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                }
            ]
        };
        var serverDir = Path.Combine(_dir, "PROD01");
        var sessionDir = Directory.CreateDirectory(Path.Combine(serverDir, "2026-07-22T10-00-00Z")).FullName;
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_A.sql"), "CREATE NONCLUSTERED INDEX [IX_A] ON [dbo].[Orders] ([CustomerId] ASC);");

        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_dir, "PROD01", CancellationToken.None);
        Assert.Single(sessions);

        var provider = new FakeProvider();
        var result = await service.RestoreAsync(sessions[0], sessions[0].Entries, provider, Path.Combine(_auditDir, "audit.jsonl"), CancellationToken.None);

        Assert.Single(result.Restored);
        Assert.Single(provider.ExecutedDdl);
    }

    [Fact]
    public async Task Multi_entry_restore_marks_all_indexes_restored()
    {
        var manifest = new Manifest
        {
            ToolVersion = "1.0.0",
            CreatedUtc = DateTime.UtcNow,
            Server = "PROD01",
            Operator = "op",
            Indexes =
            [
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                    File = "Sales.dbo.Orders.IX_A.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                },
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_B",
                    File = "Sales.dbo.Orders.IX_B.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                }
            ]
        };
        var serverDir = Path.Combine(_dir, "PROD01");
        var sessionDir = Directory.CreateDirectory(Path.Combine(serverDir, "2026-07-22T10-00-00Z")).FullName;
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_A.sql"), "CREATE NONCLUSTERED INDEX [IX_A] ON [dbo].[Orders] ([CustomerId] ASC);");
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_B.sql"), "CREATE NONCLUSTERED INDEX [IX_B] ON [dbo].[Orders] ([OrderDate] ASC);");

        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_dir, "PROD01", CancellationToken.None);
        var provider = new FakeProvider();
        var result = await service.RestoreAsync(sessions[0], sessions[0].Entries, provider, Path.Combine(_auditDir, "audit.jsonl"), CancellationToken.None);

        Assert.Equal(2, result.Restored.Count);
        Assert.Equal(2, provider.ExecutedDdl.Count);
        var updated = ManifestStore.Read(Path.Combine(sessionDir, "manifest.json"));
        Assert.All(updated.Indexes, e => Assert.Equal(IndexDeletionStatus.Restored, e.Status));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~RestoreServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the records**

`src/SmartIndexManager.Core/Restore/RestoreSession.cs`:

```csharp
using SmartIndexManager.Core.Persistence;

namespace SmartIndexManager.Core.Restore;

public sealed record RestoreSession(
    string Directory,
    Manifest Manifest,
    IReadOnlyList<ManifestIndexEntry> Entries);
```

`src/SmartIndexManager.Core/Restore/RestoreResult.cs`:

```csharp
namespace SmartIndexManager.Core.Restore;

public sealed record RestoreResult(
    IReadOnlyList<RestoreEntryResult> Restored,
    IReadOnlyList<RestoreEntryResult> Failed);

public sealed record RestoreEntryResult(
    string Database, string Schema, string Table, string Index, bool Success, string? Error);
```

- [ ] **Step 4: Update `ManifestStore.MarkRestored` to set status**

In `src/SmartIndexManager.Core/Persistence/ManifestStore.cs`, change `MarkRestored` so the matched entry also gets `Status = IndexDeletionStatus.Restored`:

```csharp
public static Manifest MarkRestored(
    Manifest manifest, string database, string schema, string table, string index, DateTime restoredUtc)
{
    var updated = manifest.Indexes.Select(e =>
        Matches(e, database, schema, table, index)
            ? e with { RestoredUtc = restoredUtc, Status = IndexDeletionStatus.Restored }
            : e).ToList();
    return manifest with { Indexes = updated };
}
```

- [ ] **Step 5: Implement `RestoreService`**

`src/SmartIndexManager.Core/Restore/RestoreService.cs`:

```csharp
using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Backup;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Core.Restore;

public sealed class RestoreService
{
    public Task<IReadOnlyList<RestoreSession>> FindSessionsAsync(
        string backupRoot, string server, CancellationToken cancellationToken)
    {
        var serverDir = Path.Combine(backupRoot, FileNameSanitizer.SanitizeComponent(server));
        if (!Directory.Exists(serverDir)) return Task.FromResult<IReadOnlyList<RestoreSession>>([]);

        var sessions = new List<RestoreSession>();
        foreach (var dir in Directory.GetDirectories(serverDir))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = ManifestStore.Read(manifestPath);
            sessions.Add(new RestoreSession(dir, manifest, manifest.Indexes));
        }
        return Task.FromResult<IReadOnlyList<RestoreSession>>(
            sessions.OrderByDescending(s => s.Manifest.CreatedUtc).ToList());
    }

    public async Task<RestoreResult> RestoreAsync(
        RestoreSession session,
        IReadOnlyList<ManifestIndexEntry> entries,
        IIndexProvider provider,
        string auditLogPath,
        CancellationToken cancellationToken)
    {
        var restored = new List<RestoreEntryResult>();
        var failed = new List<RestoreEntryResult>();
        var manifest = session.Manifest;

        foreach (var entry in entries)
        {
            try
            {
                var filePath = Path.Combine(session.Directory, entry.File);
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Backup file not found: {entry.File}");

                if (await provider.IndexExistsAsync(entry.Database, entry.Schema, entry.Table, entry.Index, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException($"Index {entry.Schema}.{entry.Table}.{entry.Index} already exists.");

                var ddl = File.ReadAllText(filePath);
                await provider.ExecuteDdlAsync(entry.Database, ddl, cancellationToken).ConfigureAwait(false);

                manifest = ManifestStore.MarkRestored(
                    manifest, entry.Database, entry.Schema, entry.Table, entry.Index, DateTime.UtcNow);
                ManifestStore.Write(Path.Combine(session.Directory, "manifest.json"), manifest);

                AuditLog.Append(auditLogPath, new AuditEntry(
                    DateTime.UtcNow, AuditAction.Restore, session.Manifest.Server, entry.Database, session.Manifest.Operator,
                    $"Restored {entry.Schema}.{entry.Table}.{entry.Index}"));

                restored.Add(new RestoreEntryResult(entry.Database, entry.Schema, entry.Table, entry.Index, true, null));
            }
            catch (Exception ex)
            {
                AuditLog.Append(auditLogPath, new AuditEntry(
                    DateTime.UtcNow, AuditAction.Restore, session.Manifest.Server, entry.Database, session.Manifest.Operator,
                    $"Restore {entry.Schema}.{entry.Table}.{entry.Index} failed: {ex.Message}"));
                failed.Add(new RestoreEntryResult(entry.Database, entry.Schema, entry.Table, entry.Index, false, ex.Message));
            }
        }

        return new RestoreResult(restored, failed);
    }
}
```

Note: the provider must handle database switching internally; `ExecuteDdlAsync` validates the database name and the executor switches context. The App layer serializes restore calls on the same gate as connect/detail loads.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~RestoreServiceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SmartIndexManager.Core/Persistence/ManifestStore.cs src/SmartIndexManager.Core/Restore/ tests/SmartIndexManager.Core.Tests/Restore/
git commit -m "feat(core): restore service with session discovery and DDL replay"
```

---

### Task 10: Password prompt dialog

**Files:**
- Create: `src/SmartIndexManager.App/Services/PasswordPromptViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/PasswordPromptWindow.axaml`, `PasswordPromptWindow.axaml.cs`
- Modify: `src/SmartIndexManager.App/Services/AvaloniaDialogService.cs`
- Modify: `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`
- Test: `tests/SmartIndexManager.App.Tests/Services/AvaloniaDialogServiceTests.cs` (headless: test the ViewModel only), `tests/.../PasswordPromptViewModelTests.cs`

**Interfaces:**
- `PasswordPromptViewModel` exposes `ConnectionName`, `Password`, `ConnectCommand`, `CancelCommand`, and a `TaskCompletionSource<string?>` result.
- `AvaloniaDialogService` implements `IPasswordPrompt` by creating and showing `PasswordPromptWindow`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Services/PasswordPromptViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Services;
using Xunit;

namespace SmartIndexManager.App.Tests.Services;

public class PasswordPromptViewModelTests
{
    [Fact]
    public void Connect_returns_password()
    {
        var vm = new PasswordPromptViewModel("prod");
        vm.Password = "s3cret";
        vm.ConnectCommand.Execute(null);
        Assert.Equal("s3cret", vm.Result.Task.Result);
    }

    [Fact]
    public void Cancel_returns_null()
    {
        var vm = new PasswordPromptViewModel("prod");
        vm.CancelCommand.Execute(null);
        Assert.Null(vm.Result.Task.Result);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~PasswordPromptViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the ViewModel**

`src/SmartIndexManager.App/Services/PasswordPromptViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartIndexManager.App.Services;

public sealed partial class PasswordPromptViewModel : ObservableObject
{
    public string ConnectionName { get; }
    public TaskCompletionSource<string?> Result { get; } = new();

    [ObservableProperty] private string _password = "";

    public PasswordPromptViewModel(string connectionName) => ConnectionName = connectionName;

    [RelayCommand]
    private void Connect() => Result.TrySetResult(Password);

    [RelayCommand]
    private void Cancel() => Result.TrySetResult(null);
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/PasswordPromptWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SmartIndexManager.App.Services"
        x:Class="SmartIndexManager.App.Views.PasswordPromptWindow"
        x:DataType="vm:PasswordPromptViewModel"
        Width="360" Height="180" Title="Password"
        WindowStartupLocation="CenterOwner">
    <StackPanel Margin="16" Spacing="12">
        <TextBlock Text="{Binding ConnectionName, StringFormat='Enter password for {0}'}" />
        <TextBox Name="PasswordBox" PasswordChar="*" Text="{Binding Password}" Watermark="Password" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Content="Connect" Command="{Binding ConnectCommand}" IsDefault="True" />
            <Button Content="Cancel" Command="{Binding CancelCommand}" IsCancel="True" />
        </StackPanel>
    </StackPanel>
</Window>
```

`src/SmartIndexManager.App/Views/PasswordPromptWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 5: Wire `AvaloniaDialogService` and DI**

`src/SmartIndexManager.App/Services/AvaloniaDialogService.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App.Services;

public sealed class AvaloniaDialogService : IDialogService, IPasswordPrompt
{
    public async Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var dialog = new ConnectionManagerDialog { DataContext = vm };
        await dialog.ShowDialog(desktop.MainWindow);
    }

    public async Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return null;

        var vm = new PasswordPromptViewModel(connectionName);
        var dialog = new PasswordPromptWindow { DataContext = vm };
        await dialog.ShowDialog<string?>(desktop.MainWindow);
        return await vm.Result.Task;
    }
}
```

In `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`, replace the `NullPasswordPrompt` registration with:

```csharp
services.AddSingleton<IPasswordPrompt, AvaloniaDialogService>();
services.AddSingleton<IDialogService, AvaloniaDialogService>();
```

Also register the new ViewModels.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~PasswordPromptViewModelTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SmartIndexManager.App/Services/PasswordPromptViewModel.cs src/SmartIndexManager.App/Views/PasswordPromptWindow.axaml src/SmartIndexManager.App/Views/PasswordPromptWindow.axaml.cs src/SmartIndexManager.App/Services/AvaloniaDialogService.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/PasswordPromptViewModelTests.cs
git commit -m "feat(app): password prompt dialog and service wiring"
```

---

### Task 11: Query Store status integration

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/QueryStoreStatusViewModel.cs`
- Modify: `src/SmartIndexManager.App/ViewModels/PermissionStatusViewModel.cs`
- Modify: `src/SmartIndexManager.App/Views/PermissionStatusBar.axaml`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/QueryStoreStatusViewModelTests.cs`, `PermissionStatusViewModelTests.cs`

**Interfaces:**
- `QueryStoreStatusViewModel(ILocalizer loc)` exposes `QueryStoreState State`, `bool CanEnable`, `ICommand EnableCommand`. The provider is injected via `SetProvider(IIndexProvider, string database)`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/QueryStoreStatusViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class QueryStoreStatusViewModelTests
{
    [Fact]
    public async Task Shows_enable_button_when_off_and_alter_granted()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            QueryStore = QueryStoreState.Off
        };
        var vm = new QueryStoreStatusViewModel(new ResxLocalizer());
        vm.SetProvider(provider, "Sales");
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.CanEnable);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~QueryStoreStatusViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the ViewModel**

`src/SmartIndexManager.App/ViewModels/QueryStoreStatusViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class QueryStoreStatusViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private string _database = "";
    private readonly ILocalizer _loc;

    [ObservableProperty] private QueryStoreState _state;
    [ObservableProperty] private bool _canEnable;
    [ObservableProperty] private string _label = "";

    public QueryStoreStatusViewModel(ILocalizer loc) => _loc = loc;

    public void SetProvider(IIndexProvider provider, string database)
    {
        _provider = provider;
        _database = database;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        State = await _provider.GetQueryStoreStateAsync(_database, cancellationToken).ConfigureAwait(true);
        CanEnable = _provider.Capabilities.SupportsQueryStore
                 && _provider.Permissions.CanAlter
                 && State == QueryStoreState.Off;
        Label = string.Format(_loc["QueryStore_Status"], State);
    }

    [RelayCommand]
    private async Task EnableAsync(CancellationToken cancellationToken)
    {
        if (!CanEnable || _provider is null) return;
        await _provider.EnableQueryStoreAsync(_database, new QueryStoreSettings(), cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
```

Add the resource key `QueryStore_Status` to `Strings.resx`.

- [ ] **Step 4: Update `PermissionStatusViewModel` and view**

Extend `PermissionStatusViewModel` to hold an optional `QueryStoreStatusViewModel? QueryStore` property. Keep the existing `Update(PermissionReport)` method unchanged; the shell assigns `Permissions.QueryStore` after calling `Update`.

`src/SmartIndexManager.App/ViewModels/PermissionStatusViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class PermissionStatusViewModel : ViewModelBase
{
    private readonly ILocalizer _loc;

    [ObservableProperty] private bool _usageAvailable = true;
    [ObservableProperty] private bool _readOnly;
    [ObservableProperty] private QueryStoreStatusViewModel? _queryStore;

    public IReadOnlyList<string> Messages { get; private set; } = [];

    public PermissionStatusViewModel(ILocalizer loc) => _loc = loc;

    public void Update(PermissionReport permissions)
    {
        var messages = new List<string>();
        UsageAvailable = permissions.CanViewState;
        if (!permissions.CanViewState) messages.Add(_loc["Permission_UsageUnavailable"]);

        ReadOnly = !permissions.CanAlter;
        if (!permissions.CanAlter) messages.Add(_loc["Permission_ReadOnly"]);

        Messages = messages;
        OnPropertyChanged(nameof(Messages));
    }
}
```

`src/SmartIndexManager.App/Views/PermissionStatusBar.axaml`:

Add the converters namespace and a `ContentControl` inside the `StackPanel`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:converters="clr-namespace:Avalonia.Data.Converters;assembly=Avalonia.Base"
             x:Class="SmartIndexManager.App.Views.PermissionStatusBar"
             x:DataType="vm:PermissionStatusViewModel">
    <StackPanel Orientation="Horizontal" Spacing="12" Margin="8,4">
        <ItemsControl ItemsSource="{Binding Messages}">...</ItemsControl>
        <ContentControl Content="{Binding QueryStore}" IsVisible="{Binding QueryStore, Converter={x:Static ObjectConverters.IsNotNull}}" />
    </StackPanel>
</UserControl>
```

Create a `DataTemplate` for `QueryStoreStatusViewModel` in `App.axaml` resources:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:semi="https://irihi.tech/semi"
             xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:views="clr-namespace:SmartIndexManager.App.Views"
             x:Class="SmartIndexManager.App.App"
             RequestedThemeVariant="Default">
    ...
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://SmartIndexManager.App/Resources/Tokens.axaml" />
            </ResourceDictionary.MergedDictionaries>
            <DataTemplate DataType="vm:QueryStoreStatusViewModel">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{Binding Label}" />
                    <Button Content="Enable Query Store" Command="{Binding EnableCommand}" IsVisible="{Binding CanEnable}" />
                </StackPanel>
            </DataTemplate>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 5: Update and run the tests**

Add a `PermissionStatusViewModelTests` case verifying that `QueryStore` can be set and raises `PropertyChanged`.

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~QueryStoreStatusViewModelTests|FullyQualifiedName~PermissionStatusViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/QueryStoreStatusViewModel.cs src/SmartIndexManager.App/ViewModels/PermissionStatusViewModel.cs src/SmartIndexManager.App/Views/PermissionStatusBar.axaml src/SmartIndexManager.App/App.axaml src/SmartIndexManager.App/Localization/Strings.resx tests/SmartIndexManager.App.Tests/ViewModels/QueryStoreStatusViewModelTests.cs
git commit -m "feat(app): Query Store status view-model and status bar integration"
```

---

### Task 12: Deletion basket UI

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/DeletionBasketViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/DeletionBasketView.axaml`, `DeletionBasketView.axaml.cs`
- Modify: `src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs` (add AddToBasketCommand)
- Modify: `src/SmartIndexManager.App/Views/BrowseView.axaml`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/DeletionBasketViewModelTests.cs`

**Interfaces:**
- `DeletionBasketViewModel(DeletionBasket basket, DeletionOrchestrator orchestrator, DryRunViewModel dryRun, IAppPaths paths, ILocalizer loc)` exposes entries and commands, runs dry-run, delete and generate-script.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/DeletionBasketViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class DeletionBasketViewModelTests
{
    [Fact]
    public void Add_adds_deletable_index()
    {
        var basket = new DeletionBasket();
        var vm = new DeletionBasketViewModel(basket, null!, new DryRunViewModel(basket, new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer()), new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer());
        var index = IndexModelFactory.Nonclustered();
        vm.Add(index, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));
        Assert.Single(vm.Entries);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~DeletionBasketViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement the ViewModel**

`src/SmartIndexManager.App/ViewModels/DeletionBasketViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class DeletionBasketViewModel : ViewModelBase
{
    private readonly DeletionBasket _basket;
    private readonly DeletionOrchestrator _orchestrator;
    private readonly DryRunViewModel _dryRun;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;
    private IIndexProvider? _provider;

    public ObservableCollection<DeletionBasketEntryViewModel> Entries { get; } = [];

    [ObservableProperty] private bool _isConfirmed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private DryRunViewModel? _activeDryRun;

    public DeletionBasketViewModel(DeletionBasket basket, DeletionOrchestrator orchestrator, DryRunViewModel dryRun, IAppPaths paths, ILocalizer loc)
    {
        _basket = basket;
        _orchestrator = orchestrator;
        _dryRun = dryRun;
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider)
    {
        _provider = provider;
        _dryRun.SetProvider(provider);
    }

    [RelayCommand]
    private async Task RunDryRunAsync(CancellationToken cancellationToken)
    {
        if (_provider is null || _basket.Entries.Count == 0) return;
        IsBusy = true;
        await _dryRun.LoadAsync(cancellationToken).ConfigureAwait(true);
        ActiveDryRun = _dryRun;
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (!IsConfirmed) return;
        await ExecuteDeletionAsync(DeletionMode.Execute, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateScriptAsync(CancellationToken cancellationToken)
    {
        await ExecuteDeletionAsync(DeletionMode.Script, cancellationToken).ConfigureAwait(true);
    }

    private async Task ExecuteDeletionAsync(DeletionMode mode, CancellationToken cancellationToken)
    {
        if (_provider is null || _basket.Entries.Count == 0) return;
        IsBusy = true;
        StatusMessage = _loc["Action_Delete"];
        try
        {
            var session = new DeletionSession(
                _provider.ServerInfo.ServerName,
                Environment.UserName,
                "1.0.0",
                Math.Max(0, _provider.ServerInfo.UptimeDays),
                _paths.DefaultBackupRoot,
                mode);
            var result = await _orchestrator.DeleteAsync(
                _provider, session, _basket, new DeletionOptions(TimeSpan.FromSeconds(60)), null, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"{mode}: {result.Results.Count(r => r.Status == (mode == DeletionMode.Execute ? IndexDeletionStatus.Dropped : IndexDeletionStatus.Scripted))} / {result.Results.Count}";
            Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Remove(DeletionBasketEntryViewModel entry)
    {
        _basket.Remove(entry.Index);
        Refresh();
    }

    [RelayCommand]
    private void Clear()
    {
        _basket.Clear();
        Refresh();
        ActiveDryRun = null;
    }

    public void Add(IndexModel index, SafetyAssessment safety, ConfidenceScore? score)
    {
        _basket.Add(index, safety, score);
        Refresh();
    }

    private void Refresh()
    {
        Entries.Clear();
        foreach (var e in _basket.Entries)
            Entries.Add(new DeletionBasketEntryViewModel(e, _loc));
    }
}

public sealed class DeletionBasketEntryViewModel
{
    public IndexModel Index { get; }
    public string DisplayName { get; }
    public string Warnings { get; }

    public DeletionBasketEntryViewModel(DeletionBasketEntry entry, ILocalizer loc)
    {
        Index = entry.Index;
        DisplayName = $"{entry.Index.Database}.{entry.Index.Schema}.{entry.Index.Table}.{entry.Index.Name}";
        Warnings = string.Join("; ", entry.Safety.Warnings.Select(w => w.Message));
    }
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/DeletionBasketView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:views="clr-namespace:SmartIndexManager.App.Views"
             xmlns:converters="clr-namespace:Avalonia.Data.Converters;assembly=Avalonia.Base"
             x:Class="SmartIndexManager.App.Views.DeletionBasketView"
             x:DataType="vm:DeletionBasketViewModel">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8">
            <Button Content="Clear" Command="{Binding ClearCommand}" />
            <Button Content="Run dry-run" Command="{Binding RunDryRunCommand}" />
            <Button Content="Delete" Command="{Binding DeleteCommand}" IsEnabled="{Binding IsConfirmed}" />
            <Button Content="Generate script" Command="{Binding GenerateScriptCommand}" />
            <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" Width="80" />
        </StackPanel>
        <CheckBox DockPanel.Dock="Bottom" Content="I understand that this action is irreversible" IsChecked="{Binding IsConfirmed}" />
        <Grid DockPanel.Dock="Bottom" RowDefinitions="Auto,*">
            <TextBlock Grid.Row="0" Text="{Binding StatusMessage}" />
            <ContentControl Grid.Row="1" Content="{Binding ActiveDryRun}" IsVisible="{Binding ActiveDryRun, Converter={x:Static ObjectConverters.IsNotNull}}">
                <ContentControl.DataTemplates>
                    <DataTemplate DataType="vm:DryRunViewModel">
                        <views:DryRunView DataContext="{Binding}" />
                    </DataTemplate>
                </ContentControl.DataTemplates>
            </ContentControl>
        </Grid>
        <DataGrid ItemsSource="{Binding Entries}" IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Index" Binding="{Binding DisplayName}" />
                <DataGridTextColumn Header="Warnings" Binding="{Binding Warnings}" />
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 5: Add "Add to basket" in Browse**

Modify `BrowseViewModel` to accept `DeletionBasketViewModel` and expose `AddSelectedToBasketCommand`.
Modify `BrowseView.axaml` to add the button.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~DeletionBasketViewModelTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/DeletionBasketViewModel.cs src/SmartIndexManager.App/Views/DeletionBasketView.axaml src/SmartIndexManager.App/Views/DeletionBasketView.axaml.cs src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs src/SmartIndexManager.App/Views/BrowseView.axaml tests/SmartIndexManager.App.Tests/ViewModels/DeletionBasketViewModelTests.cs
git commit -m "feat(app): deletion basket view-model and view"
```

---

### Task 13: Dry-run UI

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/DryRunViewModel.cs`, `DryRunReportViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/DryRunView.axaml`, `DryRunView.axaml.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/DryRunViewModelTests.cs`

**Interfaces:**
- `DryRunViewModel(IIndexProvider provider, DeletionBasket basket, IAppPaths paths, ILocalizer loc)` exposes `Task LoadAsync()`, `ExportJsonCommand`, `ExportMarkdownCommand`, `Report`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/DryRunViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class DryRunViewModelTests
{
    [Fact]
    public async Task LoadAsync_builds_report()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered(name: "IX_A")]
        };
        var basket = new DeletionBasket();
        basket.Add(IndexModelFactory.Nonclustered(name: "IX_A"), new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));

        var vm = new DryRunViewModel(basket, new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer());
        vm.SetProvider(provider);
        await vm.LoadAsync(CancellationToken.None);

        Assert.NotNull(vm.Report);
        Assert.Single(vm.Report.Entries);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~DryRunViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.App/ViewModels/DryRunReportViewModel.cs` (initial stub — replaced by the fuller version in Step 4):

```csharp
using SmartIndexManager.Core.DryRun;

namespace SmartIndexManager.App.ViewModels;

public sealed class DryRunReportViewModel : ViewModelBase
{
    public DryRunReport Report { get; }
    public DryRunReportViewModel(DryRunReport report) => Report = report;
}
```

`src/SmartIndexManager.App/ViewModels/DryRunViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class DryRunViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private readonly DeletionBasket _basket;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    [ObservableProperty] private DryRunReportViewModel? _report;

    public DryRunViewModel(DeletionBasket basket, IAppPaths paths, ILocalizer loc)
    {
        _basket = basket;
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider) => _provider = provider;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        var report = await new DryRunReportBuilder().BuildAsync(_provider, _basket, cancellationToken).ConfigureAwait(true);
        Report = new DryRunReportViewModel(report, _loc);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Report is null) return;
        var path = Path.Combine(_paths.DefaultBackupRoot, $"dry-run-{DateTime.UtcNow:yyyyMMddTHHmmss}.json");
        await Task.Run(() => DryRunReportExporter.ExportJson(path, Report.Report));
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (Report is null) return;
        var path = Path.Combine(_paths.DefaultBackupRoot, $"dry-run-{DateTime.UtcNow:yyyyMMddTHHmmss}.md");
        await Task.Run(() => DryRunReportExporter.ExportMarkdown(path, Report.Report));
    }
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/DryRunView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.DryRunView"
             x:DataType="vm:DryRunViewModel">
    <DockPanel Margin="8">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8">
            <Button Content="Export JSON" Command="{Binding ExportJsonCommand}" />
            <Button Content="Export Markdown" Command="{Binding ExportMarkdownCommand}" />
        </StackPanel>
        <ScrollViewer>
            <StackPanel>
                <TextBlock Text="{Binding Report.Report.Server, StringFormat='Server: {0}'}" />
                <TextBlock Text="{Binding Report.SummaryText}" />
                <TextBlock Text="{Binding Report.ReliabilityText}" />
                <ItemsControl ItemsSource="{Binding Report.Entries}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border BorderBrush="Gray" BorderThickness="1" Margin="4" Padding="8">
                                <StackPanel>
                                    <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" />
                                    <TextBlock Text="{Binding Score, StringFormat='Score: {0}'}" />
                                    <TextBlock Text="{Binding WarningsText}" />
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

Update `DryRunReportViewModel` to expose a lightweight wrapper for the entries:

```csharp
using SmartIndexManager.Core.DryRun;

namespace SmartIndexManager.App.ViewModels;

public sealed class DryRunReportViewModel : ViewModelBase
{
    public DryRunReport Report { get; }
    public IReadOnlyList<DryRunReportEntryViewModel> Entries { get; }
    public string ReliabilityText { get; }
    public string SummaryText { get; }

    public DryRunReportViewModel(DryRunReport report, ILocalizer loc)
    {
        Report = report;
        Entries = report.Entries.Select(e => new DryRunReportEntryViewModel(e)).ToList();
        ReliabilityText = string.Format(loc["DryRun_Reliability"], report.ReliabilityBadge);
        SummaryText = string.Format(loc["DryRun_Summary"], report.Entries.Count, report.TotalSizeMb);
    }
}

public sealed class DryRunReportEntryViewModel
{
    private readonly DryRunReportEntry _entry;

    public string DisplayName => $"{_entry.Database}.{_entry.Schema}.{_entry.Table}.{_entry.Index}";
    public int Score => _entry.Score;
    public string WarningsText => string.Join("; ", _entry.Warnings.Select(w => w.Message));

    public DryRunReportEntryViewModel(DryRunReportEntry entry) => _entry = entry;
}
```

Update `DryRunViewModel.LoadAsync` to pass the localizer: `Report = new DryRunReportViewModel(report, _loc);`.

Add the resource keys `DryRun_Reliability` and `DryRun_Summary` to `Strings.resx`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~DryRunViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/DryRunViewModel.cs src/SmartIndexManager.App/ViewModels/DryRunReportViewModel.cs src/SmartIndexManager.App/Views/DryRunView.axaml src/SmartIndexManager.App/Views/DryRunView.axaml.cs tests/SmartIndexManager.App.Tests/ViewModels/DryRunViewModelTests.cs
git commit -m "feat(app): dry-run view-model and view"
```

---

### Task 14: Restore UI

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/RestoreViewModel.cs`, `RestoreSessionViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/RestoreView.axaml`, `RestoreView.axaml.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/RestoreViewModelTests.cs`

**Interfaces:**
- `RestoreViewModel(IRestoreService restoreService, IIndexProvider provider, IAppPaths paths, ILocalizer loc)` exposes sessions and restore command.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/RestoreViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class RestoreViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_sessions()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true }
        };
        var vm = new RestoreViewModel(new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer());
        vm.SetProvider(provider);
        await vm.LoadAsync(CancellationToken.None);
        Assert.NotNull(vm.Sessions);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~RestoreViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.App/ViewModels/RestoreSessionViewModel.cs`:

```csharp
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Restore;

namespace SmartIndexManager.App.ViewModels;

public sealed class RestoreSessionViewModel
{
    public RestoreSession Session { get; }
    public string Title { get; }
    public List<ManifestIndexEntry> Entries { get; }
    public RestoreSessionViewModel(RestoreSession session)
    {
        Session = session;
        Title = session.Manifest.CreatedUtc.ToString("yyyy-MM-dd HH:mm");
        Entries = session.Entries.ToList();
    }
}
```

`src/SmartIndexManager.App/ViewModels/RestoreViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Restore;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class RestoreViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    public ObservableCollection<RestoreSessionViewModel> Sessions { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    public RestoreViewModel(IAppPaths paths, ILocalizer loc)
    {
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider) => _provider = provider;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        Sessions.Clear();
        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_paths.DefaultBackupRoot, _provider.ServerInfo.ServerName, cancellationToken).ConfigureAwait(true);
        foreach (var s in sessions)
            Sessions.Add(new RestoreSessionViewModel(s));
    }

    [RelayCommand]
    private async Task RestoreAsync(RestoreSessionViewModel sessionVm)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        var selected = sessionVm.Entries.Where(e => e.Status == IndexDeletionStatus.Dropped || e.Status == IndexDeletionStatus.Pending).ToList();
        var service = new RestoreService();
        var auditPath = Path.Combine(_paths.ConfigDir, "audit.jsonl");
        var result = await service.RestoreAsync(sessionVm.Session, selected, _provider, auditPath, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = $"Restored {result.Restored.Count}, failed {result.Failed.Count}";
        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/RestoreView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.RestoreView"
             x:DataType="vm:RestoreViewModel">
    <DockPanel Margin="8">
        <TextBlock DockPanel.Dock="Top" Text="{Binding StatusMessage}" />
        <ItemsControl ItemsSource="{Binding Sessions}">
            <ItemsControl.ItemTemplate>
                <DataTemplate DataType="vm:RestoreSessionViewModel">
                    <Border BorderBrush="Gray" BorderThickness="1" Margin="4" Padding="8">
                        <StackPanel>
                            <Grid ColumnDefinitions="*,Auto">
                                <TextBlock Grid.Column="0" Text="{Binding Title}" FontWeight="SemiBold" />
                                <Button Grid.Column="1" Content="Restore session" Command="{Binding DataContext.RestoreCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}" CommandParameter="{Binding .}" />
                            </Grid>
                            <ItemsControl ItemsSource="{Binding Entries}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding Index}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </DockPanel>
</UserControl>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~RestoreViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/RestoreViewModel.cs src/SmartIndexManager.App/ViewModels/RestoreSessionViewModel.cs src/SmartIndexManager.App/Views/RestoreView.axaml src/SmartIndexManager.App/Views/RestoreView.axaml.cs tests/SmartIndexManager.App.Tests/ViewModels/RestoreViewModelTests.cs
git commit -m "feat(app): restore view-model and view"
```

---

### Task 15: Audit UI

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/AuditViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/AuditView.axaml`, `AuditView.axaml.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/AuditViewModelTests.cs`

**Interfaces:**
- `AuditViewModel(IAppPaths paths, ILocalizer loc)` loads and filters audit entries.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/AuditViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Audit;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class AuditViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-auditvm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task LoadAsync_reads_audit_entries()
    {
        var path = Path.Combine(_dir, "audit.jsonl");
        AuditLog.Append(path, new AuditEntry(DateTime.UtcNow, AuditAction.Drop, "PROD01", "Sales", "op", "detail"));
        var vm = new AuditViewModel(new AppPaths(_dir, _dir, _dir), new ResxLocalizer());
        await vm.LoadAsync(CancellationToken.None);
        Assert.Single(vm.Entries);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AuditViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.App/ViewModels/AuditViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Audit;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class AuditViewModel : ViewModelBase
{
    private readonly IAppPaths _paths;

    public ObservableCollection<AuditEntry> Entries { get; } = [];

    [ObservableProperty] private string _filter = "";

    public AuditViewModel(IAppPaths paths, ILocalizer loc) => _paths = paths;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        Entries.Clear();
        var path = Path.Combine(_paths.ConfigDir, "audit.jsonl");
        foreach (var e in AuditLog.Read(path))
            Entries.Add(e);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/AuditView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.AuditView"
             x:DataType="vm:AuditViewModel">
    <DockPanel Margin="8">
        <TextBox DockPanel.Dock="Top" Watermark="Filter" Text="{Binding Filter}" />
        <DataGrid ItemsSource="{Binding Entries}" IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Time" Binding="{Binding TimestampUtc}" />
                <DataGridTextColumn Header="Action" Binding="{Binding Action}" />
                <DataGridTextColumn Header="Server" Binding="{Binding Server}" />
                <DataGridTextColumn Header="Database" Binding="{Binding Database}" />
                <DataGridTextColumn Header="Detail" Binding="{Binding Detail}" />
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AuditViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/AuditViewModel.cs src/SmartIndexManager.App/Views/AuditView.axaml src/SmartIndexManager.App/Views/AuditView.axaml.cs tests/SmartIndexManager.App.Tests/ViewModels/AuditViewModelTests.cs
git commit -m "feat(app): audit view-model and view"
```

---

### Task 16: Settings UI

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/SettingsViewModel.cs`
- Create: `src/SmartIndexManager.App/Views/SettingsView.axaml`, `SettingsView.axaml.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- `SettingsViewModel(SettingsService settingsService, IAppPaths paths, ILocalizer loc)` loads/saves settings.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Settings;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-settingsvm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Save_writes_settings()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var vm = new SettingsViewModel(new SettingsService(), paths, new ResxLocalizer())
        {
            DefaultBackupRoot = "/backups",
            SnapshotRetentionDays = 60
        };
        vm.SaveCommand.Execute(null);
        var loaded = new SettingsService().Load(_dir);
        Assert.Equal("/backups", loaded.DefaultBackupRoot);
        Assert.Equal(60, loaded.SnapshotRetentionDays);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/SmartIndexManager.App/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly IAppPaths _paths;

    [ObservableProperty] private string _defaultBackupRoot = "";
    [ObservableProperty] private string _snapshotRoot = "";
    [ObservableProperty] private int _snapshotRetentionDays = 90;

    public SettingsViewModel(SettingsService settingsService, IAppPaths paths, ILocalizer loc)
    {
        _settingsService = settingsService;
        _paths = paths;
        var settings = settingsService.Load(paths.ConfigDir);
        DefaultBackupRoot = settings.DefaultBackupRoot ?? _paths.DefaultBackupRoot;
        SnapshotRoot = settings.SnapshotRoot ?? _paths.SnapshotRoot;
        SnapshotRetentionDays = settings.SnapshotRetentionDays;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Save(_paths.ConfigDir, new AppSettings
        {
            DefaultBackupRoot = DefaultBackupRoot,
            SnapshotRoot = SnapshotRoot,
            SnapshotRetentionDays = SnapshotRetentionDays
        });
    }
}
```

- [ ] **Step 4: Create the view**

`src/SmartIndexManager.App/Views/SettingsView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.SettingsView"
             x:DataType="vm:SettingsViewModel">
    <StackPanel Margin="16" Spacing="12">
        <TextBlock Text="Backup root" FontWeight="SemiBold" />
        <TextBox Text="{Binding DefaultBackupRoot}" />
        <TextBlock Text="Snapshot root" FontWeight="SemiBold" />
        <TextBox Text="{Binding SnapshotRoot}" />
        <TextBlock Text="Snapshot retention (days)" FontWeight="SemiBold" />
        <NumericUpDown Value="{Binding SnapshotRetentionDays}" Minimum="1" />
        <Button Content="Save" Command="{Binding SaveCommand}" HorizontalAlignment="Left" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/SettingsViewModel.cs src/SmartIndexManager.App/Views/SettingsView.axaml src/SmartIndexManager.App/Views/SettingsView.axaml.cs tests/SmartIndexManager.App.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat(app): settings view-model and view"
```

---

### Task 17: Shell integration and DI wiring

**Files:**
- Modify: `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`
- Modify: `src/SmartIndexManager.App/ViewModels/ShellViewModel.cs`
- Modify: `src/SmartIndexManager.App/Views/MainWindow.axaml` (add DataTemplates for new pages)
- Modify: `src/SmartIndexManager.App/App.axaml.cs` (remove NullPasswordPrompt, ensure AvaloniaDialogService)
- Modify: `src/SmartIndexManager.App/App.axaml` (add DataTemplate for QueryStoreStatusViewModel if not done in Task 11)
- Test: `tests/SmartIndexManager.App.Tests/Composition/ServiceRegistrationTests.cs`, `ShellViewModelTests.cs`

**Interfaces:**
- DI provides `DeletionBasket`, `DeletionBasketViewModel`, `DryRunViewModel`, `RestoreViewModel`, `AuditViewModel`, `SettingsViewModel`, `QueryStoreStatusViewModel`.
- `ShellViewModel` replaces placeholders with real VMs and propagates `IIndexProvider` to VMs that need it.

- [ ] **Step 1: Update `ServiceRegistration`**

`src/SmartIndexManager.App/Composition/ServiceRegistration.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Restore;
using SmartIndexManager.Core.Settings;
using SmartIndexManager.Providers.SqlServer;

namespace SmartIndexManager.App.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, string scriptRoot)
    {
        services.AddSqlServerProvider(scriptRoot);

        services.AddSingleton<IAppPaths>(_ => AppPaths.Default());
        services.AddSingleton<ILocalizer, ResxLocalizer>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<IAuthAvailability>(sp => AuthAvailability.ForCurrentOs(sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IIndexLoadService, IndexLoadService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DeletionBasket>();
        services.AddSingleton<DryRunReportBuilder>();
        services.AddSingleton<DeletionOrchestrator>(sp =>
            new DeletionOrchestrator(Path.Combine(sp.GetRequiredService<IAppPaths>().ConfigDir, "audit.jsonl")));
        services.AddSingleton<RestoreService>();

        services.AddSingleton<IPasswordPrompt, AvaloniaDialogService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        services.AddSingleton<ConnectionManagerViewModel>();
        services.AddSingleton<IndexGridViewModel>();
        services.AddSingleton<PermissionStatusViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<DeletionBasketViewModel>();
        services.AddSingleton<DryRunViewModel>();
        services.AddSingleton<RestoreViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ConnectionSessionViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services;
    }
}
```

- [ ] **Step 2: Update `ShellViewModel`**

`src/SmartIndexManager.App/ViewModels/ShellViewModel.cs`:

```csharp
public sealed partial class ShellViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly BrowseViewModel _browse;
    private readonly DeletionBasketViewModel _basket;
    private readonly RestoreViewModel _restore;
    private readonly AuditViewModel _audit;
    private readonly SettingsViewModel _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizer _loc;

    [ObservableProperty] private NavigationDestination? _selectedDestination;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isDarkTheme;

    public IReadOnlyList<NavigationDestination> Destinations { get; }
    public ConnectionSessionViewModel Connection { get; }
    public PermissionStatusViewModel Permissions { get; }

    public ShellViewModel(
        ConnectionSessionViewModel connection, BrowseViewModel browse,
        DeletionBasketViewModel basket, RestoreViewModel restore,
        AuditViewModel audit, SettingsViewModel settings,
        PermissionStatusViewModel permissions, IThemeService theme, ILocalizer loc)
    {
        Connection = connection;
        _browse = browse;
        _basket = basket;
        _restore = restore;
        _audit = audit;
        _settings = settings;
        Permissions = permissions;
        _theme = theme;
        _loc = loc;
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;

        Destinations =
        [
            new NavigationDestination(loc["Nav_Browse"], MaterialIconKind.DatabaseSearch, browse),
            new NavigationDestination(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline, basket),
            new NavigationDestination(loc["Nav_Restore"], MaterialIconKind.BackupRestore, restore),
            new NavigationDestination(loc["Nav_Audit"], MaterialIconKind.History, audit),
            new NavigationDestination(loc["Nav_Settings"], MaterialIconKind.CogOutline, settings),
        ];

        Connection.Connected += OnConnectedAsync;
        Connection.Disconnected += OnDisconnectedAsync;

        SelectedDestination = Destinations[0];
    }

    partial void OnSelectedDestinationChanged(NavigationDestination? value)
        => CurrentPage = value?.PageViewModel;

    private async Task OnConnectedAsync(LoadResult result)
    {
        Permissions.Update(result.Permissions);
        _basket.SetProvider(result.Provider);
        _restore.SetProvider(result.Provider);
        Permissions.QueryStore = new QueryStoreStatusViewModel(_loc);
        Permissions.QueryStore.SetProvider(result.Provider, result.Rows.FirstOrDefault()?.Database ?? "");
        await Permissions.QueryStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await _audit.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await _browse.OnConnectedAsync(result.Provider, result.Rows, CancellationToken.None).ConfigureAwait(true);
        SelectedDestination = Destinations[0];
    }

    private async Task OnDisconnectedAsync() => await _browse.OnDisconnectedAsync().ConfigureAwait(true);

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;
    }

    public async ValueTask DisposeAsync()
    {
        Connection.Connected -= OnConnectedAsync;
        Connection.Disconnected -= OnDisconnectedAsync;
        await Connection.DisposeAsync().ConfigureAwait(true);
        await _browse.DisposeAsync().ConfigureAwait(true);
    }
}
```

`DeletionBasketViewModel`, `RestoreViewModel`, and `QueryStoreStatusViewModel` receive their provider via `SetProvider` once the connection is established. `DryRunViewModel` is embedded inside `DeletionBasketViewModel` and loaded by the basket's RunDryRunCommand. `AuditViewModel.LoadAsync` loads entries asynchronously.

- [ ] **Step 3: Update `App.axaml.cs`**

Remove the `NullPasswordPrompt` registration; `AvaloniaDialogService` is now registered as both `IPasswordPrompt` and `IDialogService`.

- [ ] **Step 4: Update `MainWindow.axaml`**

`src/SmartIndexManager.App/Views/MainWindow.axaml`:

Add DataTemplates inside `<Window.DataTemplates>` for the new pages:

```xml
<DataTemplate DataType="vm:DeletionBasketViewModel">
    <views:DeletionBasketView />
</DataTemplate>
<DataTemplate DataType="vm:RestoreViewModel">
    <views:RestoreView />
</DataTemplate>
<DataTemplate DataType="vm:AuditViewModel">
    <views:AuditView />
</DataTemplate>
<DataTemplate DataType="vm:SettingsViewModel">
    <views:SettingsView />
</DataTemplate>
```

- [ ] **Step 5: Update the tests**

`tests/SmartIndexManager.App.Tests/Composition/ServiceRegistrationTests.cs`:

```csharp
[Fact]
public void AddAppServices_registers_new_3b_services()
{
    var provider = new ServiceCollection()
        .AddAppServices(scriptRoot: "/tmp/sql/sqlserver")
        .BuildServiceProvider();

    Assert.NotNull(provider.GetService<DeletionBasket>());
    Assert.NotNull(provider.GetService<DeletionOrchestrator>());
    Assert.NotNull(provider.GetService<RestoreService>());
    Assert.NotNull(provider.GetService<IPasswordPrompt>());
}
```

`tests/SmartIndexManager.App.Tests/ViewModels/ShellViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Composition;
using SmartIndexManager.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ShellViewModelTests
{
    [Fact]
    public void Ctor_builds_destinations_without_dry_run_viewmodel()
    {
        var provider = new ServiceCollection().AddAppServices("/tmp/sql/sqlserver").BuildServiceProvider();
        var shell = provider.GetRequiredService<ShellViewModel>();
        Assert.Contains(shell.Destinations, d => d.PageViewModel is DeletionBasketViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is RestoreViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is AuditViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is SettingsViewModel);
        Assert.DoesNotContain(shell.Destinations, d => d.PageViewModel is DryRunViewModel);
    }
}
```

- [ ] **Step 6: Run the full build and test**

Run: `dotnet build SmartIndexManager.sln`
Expected: 0 errors, 0 warnings.

Run: `dotnet test`
Expected: all tests pass; integration tests pass under Docker, otherwise skipped.

- [ ] **Step 7: Commit**

```bash
git add src/SmartIndexManager.App/Composition/ServiceRegistration.cs src/SmartIndexManager.App/ViewModels/ShellViewModel.cs src/SmartIndexManager.App/App.axaml.cs src/SmartIndexManager.App/Views/MainWindow.axaml tests/SmartIndexManager.App.Tests/Composition/ServiceRegistrationTests.cs tests/SmartIndexManager.App.Tests/ViewModels/ShellViewModelTests.cs
git commit -m "feat(app): shell integration and DI wiring for 3b"
```

---

## Self-Review

Spec coverage against `docs/specs/2026-07-22-smartindexmanager-app-3b-design.md`:

- Password prompt : Task 10.
- Deletion basket : Task 5 (Core) and Task 12 (App).
- Dry-run report, display and export : Tasks 6, 7 (Core) and Task 13 (App).
- Execute/script deletion with backup/manifest/audit : Task 8.
- Restore screen and DDL replay : Tasks 2, 3 (provider), Task 9 (Core), Task 14 (App).
- Query Store activation from status bar : Task 11.
- Audit viewer : Task 15.
- Settings : Tasks 4, 16.
- Core-first architecture and testability : every Core task ships unit tests that do not need a database or UI.

Placeholder scan : no TBD, TODO, or "implement later". Each step contains code, commands, and expected output. Notes such as "DisplayName, WarningsText… or create a view-model wrapper" and "wire later" from earlier drafts have been replaced with concrete code in Tasks 12, 13 and 17. Task 12 Step 5 ("Add to basket" in Browse) is intentionally prose because it reuses existing BrowseViewModel/BrowseView patterns from App read-only plan.

Type consistency : `DeletionOrchestrator.DeleteAsync` takes `IIndexProvider`; `RestoreService.RestoreAsync` takes `IIndexProvider`; `DryRunReportBuilder.BuildAsync` takes `IIndexProvider`; `ExecuteDdlAsync` and `IndexExistsAsync` are added to `IIndexProvider` in Task 2 and used in Tasks 8 and 9. `Manifest.cs` gains `Pending` and `Restored` in Task 1; `ManifestStore.MarkRestored` is updated in Task 9 to set `Status = Restored` before the restore test asserts it.

App wiring : Task 12 binds DeleteCommand, GenerateScriptCommand and RunDryRunCommand, and embeds `DryRunViewModel` (not its report) via `ActiveDryRun` so DryRunView bindings resolve. Task 17 registers the basket with `DeletionOrchestrator` and `DryRunViewModel`, preserves `IIndexLoadService` and `AuthAvailability.ForCurrentOs(ILocalizer)`, and Task 11 assigns `_loc` in `ShellViewModel`.

Execution handoff : after this plan is approved, the recommended execution mode is `superpowers:subagent-driven-development`, one task at a time, with review after each commit.
