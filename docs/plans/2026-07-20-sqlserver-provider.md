# SmartIndexManager SQL Server Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `SmartIndexManager.Providers.SqlServer`, the SQL Server implementation of the provider contract: connect with the three auth modes, detect version/edition/platform/uptime/permissions/Query Store state, derive capabilities, collect index metadata and diagnostics, and execute the two mutating actions (DROP INDEX, enable Query Store). The mapping logic is unit-testable without a database; only the real SqlClient execution needs a live server, exercised through Testcontainers and skipped when Docker is absent.

**Architecture:** The provider abstraction (`IIndexProvider`, `IIndexProviderFactory`, and its DTOs) is added to `SmartIndexManager.Core` under `Provider/`, so the App and a future CLI or PostgreSQL provider depend only on Core. Execution is abstracted behind `ISqlExecutor` returning `SqlRow` lists; pure mappers turn rows into Core model types and are unit-tested with fake rows. `SqlClientExecutor` is the only type that touches `Microsoft.Data.SqlClient`. External `.sql` files under `sql/sqlserver/` carry `-- sim:` headers and are loaded and column-validated through the Core `SqlScriptLoader`.

**Tech Stack:** C#, .NET 10, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.DependencyInjection`, xUnit, Testcontainers (`Testcontainers.MsSql`). Records with `required`/`init`, nullable reference types enabled.

## Global Constraints

Copied verbatim from `docs/specs/2026-07-20-smartindexmanager-design.md` and the Core plan; every task inherits these:

- Target framework `net10.0`; nullable reference types and implicit usings enabled.
- `SmartIndexManager.Core` never references `Microsoft.Data.SqlClient` or any UI framework. The provider abstraction added to Core is interfaces and plain DTOs only.
- Passwords are never stored on any long-lived object. The password is a parameter to `ConnectAsync`, used once to build the connection string, then dropped. Persisted connection descriptors never contain the password.
- All queries live in external files under `sql/sqlserver/<name>.sql`, user-editable, with no embedded fallback. A missing, unreadable, or invalid file makes the corresponding feature fail with an explicit message, never crashes the app.
- Each `.sql` file begins with a `-- sim:` header declaring `name`, `minversion`, `azure`, and `columns`. Result columns are read by name, never by position. A declared column missing from the result set is an invalid-file error.
- Queries are parameterized with named `SqlParameter`s (`@Database`, `@SchemaName`, `@IndexName`, ...), never string concatenation.
- Only nonclustered rowstore non-unique indexes are droppable; the provider's `DropIndexAsync` trusts that the caller (Core `DeletionSafetyEvaluator`) already gated eligibility, but still refuses anything that is not a plain `DROP INDEX` target as a defence in depth.
- Capability decisions are made from `ProviderCapabilities`, never from a raw version check at the call site. The provider fills `ProviderCapabilities` once at connect time via `CapabilityResolver`.
- On Azure SQL Database, `VIEW DATABASE STATE` replaces `VIEW SERVER STATE` and DMVs are database-scoped. Integration tests run against an on-premises SQL Server container; Azure-specific SQL variants are written but validated by the user, not by CI.
- Integration tests are skipped automatically when Docker is unavailable.
- `Microsoft.Data.SqlClient` version: 6 minimum, latest stable compatible with .NET 10 chosen at implementation time.

## File Structure

```
src/
  SmartIndexManager.Core/
    Provider/
      AuthMode.cs  ServerPlatform.cs  QueryStoreState.cs  UsageSource.cs
      ConnectionRequest.cs  ServerInfo.cs  PermissionReport.cs  QueryUsage.cs
      IndexHint.cs  QueryStoreSettings.cs  IndexRef.cs
      IIndexProvider.cs  IIndexProviderFactory.cs
    Sql/
      SqlScript.cs  SqlScriptLoader.cs           (new; SqlFileHeaderParser already exists)
  SmartIndexManager.Providers.SqlServer/
    SmartIndexManager.Providers.SqlServer.csproj
    Execution/
      SqlRow.cs  ISqlExecutor.cs  SqlClientExecutor.cs
    Connection/
      SqlServerConnectionFactory.cs
    Capabilities/
      CapabilityResolver.cs
    Mapping/
      ServerInfoMapper.cs  PermissionMapper.cs  IndexColumnRow.cs
      IndexRowMapper.cs  IndexColumnMapper.cs
      QueryUsageMapper.cs  HintMapper.cs  QueryStoreStateMapper.cs
    SqlServerIndexProvider.cs
    SqlServerIndexProviderFactory.cs
    ServiceCollectionExtensions.cs
sql/
  sqlserver/
    server-info.sql  permissions-check.sql  querystore-state.sql  querystore-enable.sql
    index-list.sql  index-columns.sql  fk-support.sql
    index-used-by-queries.sql  index-used-by-queries-query-store.sql
    index-hints-plancache.sql  replication-ag-check.sql
tests/
  SmartIndexManager.Providers.SqlServer.Tests/
    SmartIndexManager.Providers.SqlServer.Tests.csproj
    (unit test files mirroring Mapping/, Connection/, Capabilities/)
    Sql/ScriptContractTests.cs
    Integration/
      SqlServerContainerFixture.cs  DockerAvailable.cs
      ConnectAndDetectTests.cs  GetIndexesTests.cs  DiagnosticsTests.cs  ActionsTests.cs
```

---

### Task 1: Provider abstraction in Core

**Files:**
- Create: `src/SmartIndexManager.Core/Provider/AuthMode.cs`, `ServerPlatform.cs`, `QueryStoreState.cs`, `UsageSource.cs`, `ConnectionRequest.cs`, `ServerInfo.cs`, `PermissionReport.cs`, `QueryUsage.cs`, `IndexHint.cs`, `QueryStoreSettings.cs`, `IndexRef.cs`, `IIndexProvider.cs`, `IIndexProviderFactory.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Provider/ProviderContractTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `ProviderCapabilities` (existing Core types).
- Produces: the full provider contract every later task depends on. Exact shapes below.

- [ ] **Step 1: Write the enums and DTOs**

`AuthMode.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public enum AuthMode { WindowsIntegrated, SqlLogin, EntraIdInteractive }
```

`ServerPlatform.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public enum ServerPlatform { OnPremises, AzureSqlDatabase, AzureManagedInstance }
```

`QueryStoreState.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public enum QueryStoreState { NotSupported, Off, ReadOnly, ReadWrite }
```

`UsageSource.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public enum UsageSource { PlanCache, QueryStore }
```

`ConnectionRequest.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public sealed record ConnectionRequest
{
    public required string Server { get; init; }
    public int? Port { get; init; }
    public required AuthMode Auth { get; init; }
    public string? Login { get; init; }                 // required for SqlLogin, ignored otherwise
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public string ApplicationName { get; init; } = "SmartIndexManager";
    public int ConnectTimeoutSeconds { get; init; } = 15;
}
```

`ServerInfo.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public sealed record ServerInfo
{
    public required string ServerName { get; init; }
    public required Version ProductVersion { get; init; }
    public required string Edition { get; init; }
    public required ServerPlatform Platform { get; init; }
    // Days since the engine started. -1 means unknown (for example Azure SQL Database,
    // where the server-scoped uptime DMV is not readable); consumers treat -1 as
    // "reliability unknown" and lean on the Azure platform badge instead.
    public required int UptimeDays { get; init; }
}
```

`PermissionReport.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public sealed record PermissionReport
{
    public required bool CanViewState { get; init; }        // VIEW SERVER STATE or VIEW DATABASE STATE
    public required bool CanAlter { get; init; }            // ALTER rights for DROP and Query Store
    public required bool CanAccessQueryStore { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
```

`QueryUsage.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public sealed record QueryUsage(
    string QueryText,
    long ExecutionCount,
    DateTime? LastExecutionUtc,
    UsageSource Source);
```

`IndexHint.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public sealed record IndexHint(string Reference, string Kind);   // Kind: "query hint" or "plan guide"
```

`QueryStoreSettings.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

// OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO and SIZE_BASED_CLEANUP_MODE = AUTO
// are fixed defaults applied by querystore-enable.sql. Only the two numbers vary.
public sealed record QueryStoreSettings
{
    public int MaxStorageSizeMb { get; init; } = 1000;
    public int StaleQueryThresholdDays { get; init; } = 30;
}
```

`IndexRef.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

using SmartIndexManager.Core.Model;

public sealed record IndexRef(string Database, string Schema, string Table, string Index)
{
    public static IndexRef Of(IndexModel index)
        => new(index.Database, index.Schema, index.Table, index.Name);
}
```

- [ ] **Step 2: Write the interfaces**

`IIndexProvider.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Provider;

// One connected session against one instance. Created by IIndexProviderFactory.
// All long operations are async and honour the CancellationToken.
public interface IIndexProvider : IAsyncDisposable
{
    ServerInfo ServerInfo { get; }
    ProviderCapabilities Capabilities { get; }
    PermissionReport Permissions { get; }

    Task<IReadOnlyList<IndexModel>> GetIndexesAsync(
        IReadOnlyList<string> databases, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default);

    // Per-index so the server filters by name; the dry-run flags a specific index as
    // hint-referenced. Scanning all hints for a database is never needed.
    Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default);

    Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default);

    Task EnableQueryStoreAsync(
        string database, QueryStoreSettings settings, CancellationToken cancellationToken = default);

    Task DropIndexAsync(
        IndexRef index, TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

`IIndexProviderFactory.cs`:
```csharp
namespace SmartIndexManager.Core.Provider;

public interface IIndexProviderFactory
{
    // password is used once to build the connection string and never stored.
    // Null for WindowsIntegrated and EntraIdInteractive.
    Task<IIndexProvider> ConnectAsync(
        ConnectionRequest request, string? password, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Provider/ProviderContractTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.Core.Tests.Provider;

public class ProviderContractTests
{
    [Fact]
    public void IndexRef_Of_copies_the_four_identity_parts()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Legacy",
            Type = IndexType.NonclusteredRowstore
        };
        Assert.Equal(new IndexRef("Sales", "dbo", "Orders", "IX_Legacy"), IndexRef.Of(index));
    }

    [Fact]
    public void QueryStoreSettings_defaults_match_the_spec()
    {
        var s = new QueryStoreSettings();
        Assert.Equal(1000, s.MaxStorageSizeMb);
        Assert.Equal(30, s.StaleQueryThresholdDays);
    }

    [Fact]
    public void ServerInfo_unknown_uptime_is_minus_one()
    {
        var info = new ServerInfo
        {
            ServerName = "x", ProductVersion = new Version(16, 0), Edition = "Developer",
            Platform = ServerPlatform.AzureSqlDatabase, UptimeDays = -1
        };
        Assert.Equal(-1, info.UptimeDays);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~ProviderContractTests"`
Expected: PASS (the test compiles only once the types exist).

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Provider/ tests/SmartIndexManager.Core.Tests/Provider/
git commit -m "feat(core): provider abstraction (IIndexProvider, DTOs)"
```

---

### Task 2: SQL script loader in Core

**Files:**
- Create: `src/SmartIndexManager.Core/Sql/SqlScript.cs`, `SqlScriptLoader.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Sql/SqlScriptLoaderTests.cs`

**Interfaces:**
- Consumes: `SqlFileHeaderParser`, `SqlFileHeader`, `SqlFileHeaderException` (existing Core).
- Produces:
  - `SqlScript(string Name, string Sql, SqlFileHeader Header)` with `IReadOnlyList<string> ExpectedColumns => Header.Columns`
  - `SqlScriptLoader.Load(string scriptRoot, string name) -> SqlScript` (reads `<scriptRoot>/<name>.sql`)

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Sql/SqlScriptLoaderTests.cs`:
```csharp
using SmartIndexManager.Core.Sql;
using Xunit;

namespace SmartIndexManager.Core.Tests.Sql;

public class SqlScriptLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-scripts-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteScript(string name, string content) => File.WriteAllText(Path.Combine(_dir, $"{name}.sql"), content);

    [Fact]
    public void Load_reads_sql_and_parses_header()
    {
        WriteScript("server-info", """
            -- sim: name=server-info
            -- sim: minversion=11.0
            -- sim: columns=ServerName,Edition
            SELECT 1 AS ServerName, 2 AS Edition;
            """);

        var script = SqlScriptLoader.Load(_dir, "server-info");

        Assert.Equal("server-info", script.Name);
        Assert.Contains("SELECT 1 AS ServerName", script.Sql);
        Assert.Equal(new[] { "ServerName", "Edition" }, script.ExpectedColumns);
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException_with_the_path()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => SqlScriptLoader.Load(_dir, "does-not-exist"));
        Assert.Contains("does-not-exist.sql", ex.Message);
    }

    [Fact]
    public void Header_name_must_match_the_requested_name()
    {
        WriteScript("server-info", "-- sim: name=other\n-- sim: columns=A\nSELECT 1 AS A;");
        Assert.Throws<SqlFileHeaderException>(() => SqlScriptLoader.Load(_dir, "server-info"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~SqlScriptLoaderTests"`
Expected: FAIL, `SqlScriptLoader` does not exist.

- [ ] **Step 3: Write the implementation**

`SqlScript.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public sealed record SqlScript(string Name, string Sql, SqlFileHeader Header)
{
    public IReadOnlyList<string> ExpectedColumns => Header.Columns;
}
```

`SqlScriptLoader.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public static class SqlScriptLoader
{
    public static SqlScript Load(string scriptRoot, string name)
    {
        var path = Path.Combine(scriptRoot, $"{name}.sql");
        if (!File.Exists(path))
            throw new FileNotFoundException($"SQL script not found: {path}", path);

        var content = File.ReadAllText(path);
        var header = SqlFileHeaderParser.Parse(content);
        if (!string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            throw new SqlFileHeaderException(
                $"script '{path}' declares name '{header.Name}' but was loaded as '{name}'");

        return new SqlScript(name, content, header);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Core.Tests --filter "FullyQualifiedName~SqlScriptLoaderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Sql/SqlScript.cs src/SmartIndexManager.Core/Sql/SqlScriptLoader.cs tests/SmartIndexManager.Core.Tests/Sql/SqlScriptLoaderTests.cs
git commit -m "feat(core): external SQL script loader with header validation"
```

---

### Task 3: Provider project scaffold, SqlRow, and the executor interface

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/SmartIndexManager.Providers.SqlServer.csproj`, `Execution/SqlRow.cs`, `Execution/ISqlExecutor.cs`
- Create: `tests/SmartIndexManager.Providers.SqlServer.Tests/SmartIndexManager.Providers.SqlServer.Tests.csproj`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Execution/SqlRowTests.cs`

**Interfaces:**
- Produces:
  - `SqlRow` wrapping a case-insensitive column map, with `T? Get<T>(string)`, `object? GetRaw(string)`, `bool Has(string)`
  - `ISqlExecutor` (async, disposable): `QueryAsync`, `ScalarAsync<T>`, `ExecuteAsync`

- [ ] **Step 1: Scaffold the projects**

Run:
```bash
cd /home/rudi/Sources/Repos/SmartIndexManager
dotnet new classlib -n SmartIndexManager.Providers.SqlServer -o src/SmartIndexManager.Providers.SqlServer -f net10.0
dotnet new xunit -n SmartIndexManager.Providers.SqlServer.Tests -o tests/SmartIndexManager.Providers.SqlServer.Tests -f net10.0
rm src/SmartIndexManager.Providers.SqlServer/Class1.cs tests/SmartIndexManager.Providers.SqlServer.Tests/UnitTest1.cs
dotnet sln add src/SmartIndexManager.Providers.SqlServer/SmartIndexManager.Providers.SqlServer.csproj tests/SmartIndexManager.Providers.SqlServer.Tests/SmartIndexManager.Providers.SqlServer.Tests.csproj
dotnet add src/SmartIndexManager.Providers.SqlServer reference src/SmartIndexManager.Core
dotnet add src/SmartIndexManager.Providers.SqlServer package Microsoft.Data.SqlClient
dotnet add src/SmartIndexManager.Providers.SqlServer package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add tests/SmartIndexManager.Providers.SqlServer.Tests reference src/SmartIndexManager.Providers.SqlServer
dotnet add tests/SmartIndexManager.Providers.SqlServer.Tests package Testcontainers.MsSql
```

- [ ] **Step 2: Enable nullable and implicit usings in both new csproj**

Add to each `<PropertyGroup>`:
```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
```

- [ ] **Step 3: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Execution/SqlRowTests.cs`:
```csharp
using SmartIndexManager.Providers.SqlServer.Execution;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Execution;

public class SqlRowTests
{
    private static SqlRow Row(params (string, object?)[] cells)
        => new(cells.ToDictionary(c => c.Item1, c => c.Item2, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Get_reads_by_case_insensitive_name()
    {
        var row = Row(("Name", "IX_A"), ("Seeks", 5L));
        Assert.Equal("IX_A", row.Get<string>("name"));
        Assert.Equal(5L, row.Get<long>("SEEKS"));
    }

    [Fact]
    public void Get_maps_DBNull_and_missing_to_default()
    {
        var row = Row(("LastRead", DBNull.Value));
        Assert.Null(row.Get<DateTime?>("LastRead"));
        Assert.Equal(0L, row.Get<long>("Absent"));
    }

    [Fact]
    public void Get_bool_reads_bit_and_int()
    {
        Assert.True(Row(("IsUnique", true)).Get<bool>("IsUnique"));
        Assert.True(Row(("IsUnique", 1)).Get<bool>("IsUnique"));
        Assert.False(Row(("IsUnique", 0)).Get<bool>("IsUnique"));
    }
}
```

- [ ] **Step 4: Write `SqlRow` and `ISqlExecutor`**

`Execution/SqlRow.cs`:
```csharp
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Execution;

public sealed class SqlRow
{
    private readonly IReadOnlyDictionary<string, object?> _cells;

    public SqlRow(IReadOnlyDictionary<string, object?> cells) => _cells = cells;

    public bool Has(string column) => _cells.ContainsKey(column);

    public object? GetRaw(string column)
        => _cells.TryGetValue(column, out var v) && v is not DBNull ? v : null;

    public T? Get<T>(string column)
    {
        var raw = GetRaw(column);
        if (raw is null) return default;
        if (raw is T typed) return typed;

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        // SQL Server bit surfaces as bool; ints from CASE expressions must coerce to bool too.
        if (target == typeof(bool)) return (T)(object)Convert.ToBoolean(raw);
        return (T)Convert.ChangeType(raw, target);
    }
}
```

`Execution/ISqlExecutor.cs`:
```csharp
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Execution;

public interface ISqlExecutor : IAsyncDisposable
{
    // Runs a loaded script and validates that every column the header declares is
    // present in the result set (by name); a missing declared column is an invalid file.
    Task<IReadOnlyList<SqlRow>> QueryAsync(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    Task<T?> ScalarAsync<T>(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    // Runs a mutating statement (DROP, ALTER). timeout null uses the connection default.
    Task<int> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~SqlRowTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/ tests/SmartIndexManager.Providers.SqlServer.Tests/ SmartIndexManager.sln
git commit -m "feat(provider): scaffold SQL Server provider, SqlRow and executor interface"
```

---

### Task 4: Connection string factory

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Connection/SqlServerConnectionFactory.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Connection/SqlServerConnectionFactoryTests.cs`

**Interfaces:**
- Consumes: `ConnectionRequest`, `AuthMode` (Core).
- Produces: `SqlServerConnectionFactory.BuildConnectionString(ConnectionRequest request, string? password) -> string` (pure; no connection opened).

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Connection/SqlServerConnectionFactoryTests.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Connection;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Connection;

public class SqlServerConnectionFactoryTests
{
    private static ConnectionRequest Base(AuthMode auth) => new()
    {
        Server = "srv01", Auth = auth, Login = "app", Encrypt = true, TrustServerCertificate = true
    };

    private static SqlConnectionStringBuilder Parse(string cs) => new(cs);

    [Fact]
    public void Sql_login_sets_user_and_password_and_disables_integrated_security()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin), "s3cret");
        var b = Parse(cs);
        Assert.Equal("app", b.UserID);
        Assert.Equal("s3cret", b.Password);
        Assert.False(b.IntegratedSecurity);
        Assert.Equal(SqlAuthenticationMethod.SqlPassword, b.Authentication);
    }

    [Fact]
    public void Windows_integrated_sets_integrated_security_and_no_password()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.WindowsIntegrated), null);
        var b = Parse(cs);
        Assert.True(b.IntegratedSecurity);
        Assert.True(string.IsNullOrEmpty(b.Password));
    }

    [Fact]
    public void Entra_interactive_sets_the_active_directory_interactive_method()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.EntraIdInteractive), null);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, Parse(cs).Authentication);
    }

    [Fact]
    public void Port_is_appended_to_the_data_source_when_present()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin) with { Port = 14330 }, "x");
        Assert.Equal("srv01,14330", Parse(cs).DataSource);
    }

    [Fact]
    public void Sql_login_without_password_throws()
    {
        Assert.Throws<ArgumentException>(
            () => SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin), null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~SqlServerConnectionFactoryTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`Connection/SqlServerConnectionFactory.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Connection;

public static class SqlServerConnectionFactory
{
    public static string BuildConnectionString(ConnectionRequest request, string? password)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = request.Port is int p ? $"{request.Server},{p}" : request.Server,
            Encrypt = request.Encrypt,
            TrustServerCertificate = request.TrustServerCertificate,
            ApplicationName = request.ApplicationName,
            ConnectTimeout = request.ConnectTimeoutSeconds
        };

        switch (request.Auth)
        {
            case AuthMode.WindowsIntegrated:
                b.IntegratedSecurity = true;
                break;

            case AuthMode.SqlLogin:
                if (string.IsNullOrEmpty(request.Login))
                    throw new ArgumentException("SQL login requires a user name.", nameof(request));
                if (string.IsNullOrEmpty(password))
                    throw new ArgumentException("SQL login requires a password.", nameof(password));
                b.Authentication = SqlAuthenticationMethod.SqlPassword;
                b.UserID = request.Login;
                b.Password = password;
                break;

            case AuthMode.EntraIdInteractive:
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                if (!string.IsNullOrEmpty(request.Login)) b.UserID = request.Login;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Auth, "Unknown auth mode.");
        }

        return b.ConnectionString;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~SqlServerConnectionFactoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Connection/ tests/SmartIndexManager.Providers.SqlServer.Tests/Connection/
git commit -m "feat(provider): connection string factory per auth mode"
```

---

### Task 5: Capability resolver

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Capabilities/CapabilityResolver.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Capabilities/CapabilityResolverTests.cs`

**Interfaces:**
- Consumes: `ServerInfo`, `ServerPlatform`, `ProviderCapabilities` (Core).
- Produces: `CapabilityResolver.Resolve(ServerInfo info) -> ProviderCapabilities` (pure).

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Capabilities/CapabilityResolverTests.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Capabilities;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Capabilities;

public class CapabilityResolverTests
{
    private static ServerInfo Info(int major, ServerPlatform platform, string edition = "Developer Edition") => new()
    {
        ServerName = "x", ProductVersion = new Version(major, 0), Edition = edition,
        Platform = platform, UptimeDays = 100
    };

    [Fact]
    public void Query_store_needs_2016_on_premises()
    {
        Assert.False(CapabilityResolver.Resolve(Info(12, ServerPlatform.OnPremises)).SupportsQueryStore); // 2014
        Assert.True(CapabilityResolver.Resolve(Info(13, ServerPlatform.OnPremises)).SupportsQueryStore);  // 2016
    }

    [Fact]
    public void Azure_sql_database_always_supports_query_store_and_needs_database_scoped_dmv()
    {
        var caps = CapabilityResolver.Resolve(Info(12, ServerPlatform.AzureSqlDatabase));
        Assert.True(caps.SupportsQueryStore);
        Assert.True(caps.RequiresDatabaseScopedDmv);
    }

    [Fact]
    public void On_premises_does_not_require_database_scoped_dmv()
        => Assert.False(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises)).RequiresDatabaseScopedDmv);

    [Fact]
    public void Online_drop_needs_enterprise_or_azure()
    {
        Assert.False(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises, "Standard Edition")).SupportsOnlineDrop);
        Assert.True(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises, "Enterprise Edition")).SupportsOnlineDrop);
        Assert.True(CapabilityResolver.Resolve(Info(12, ServerPlatform.AzureSqlDatabase)).SupportsOnlineDrop);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~CapabilityResolverTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`Capabilities/CapabilityResolver.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Capabilities;

public static class CapabilityResolver
{
    public static ProviderCapabilities Resolve(ServerInfo info)
    {
        bool azure = info.Platform is ServerPlatform.AzureSqlDatabase or ServerPlatform.AzureManagedInstance;
        bool queryStore = azure || info.ProductVersion.Major >= 13;          // 2016+
        bool columnstore = azure || info.ProductVersion.Major >= 11;         // 2012 nonclustered CS
        bool enterprise = info.Edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
                       || info.Edition.Contains("Developer", StringComparison.OrdinalIgnoreCase);

        return new ProviderCapabilities
        {
            SupportsQueryStore = queryStore,
            SupportsPlanCache = true,
            SupportsColumnstore = columnstore,
            // Informational only. The MVP DROP is deliberately a plain DROP INDEX with no
            // implicit WITH (ONLINE = ON) (design spec section 6). This flag lets the App
            // offer an online option later; DropIndexAsync does not read it in the MVP.
            SupportsOnlineDrop = azure || enterprise,
            RequiresDatabaseScopedDmv = info.Platform == ServerPlatform.AzureSqlDatabase
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~CapabilityResolverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Capabilities/ tests/SmartIndexManager.Providers.SqlServer.Tests/Capabilities/
git commit -m "feat(provider): capability resolver from server info"
```

---

### Task 6: Server-info and permission mappers

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Mapping/ServerInfoMapper.cs`, `PermissionMapper.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/ServerInfoMapperTests.cs`, `PermissionMapperTests.cs`

**Interfaces:**
- Consumes: `SqlRow`, `ServerInfo`, `ServerPlatform`, `PermissionReport`.
- Produces:
  - `ServerInfoMapper.Map(SqlRow row) -> ServerInfo` (reads `ServerName`, `ProductVersion`, `Edition`, `EngineEdition`, `UptimeDays`)
  - `PermissionMapper.Map(SqlRow row) -> PermissionReport` (reads `CanViewState`, `CanAlter`, `CanAccessQueryStore` as bit columns)

- [ ] **Step 1: Write the failing tests**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/ServerInfoMapperTests.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class ServerInfoMapperTests
{
    private static SqlRow Row(object engineEdition, object uptime) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ServerName"] = "PROD01",
        ["ProductVersion"] = "16.0.1000.6",
        ["Edition"] = "Developer Edition (64-bit)",
        ["EngineEdition"] = engineEdition,
        ["UptimeDays"] = uptime
    });

    [Fact]
    public void Maps_version_edition_and_on_premises_platform()
    {
        var info = ServerInfoMapper.Map(Row(3, 92));   // EngineEdition 3 = on-premises
        Assert.Equal("PROD01", info.ServerName);
        Assert.Equal(new Version(16, 0, 1000, 6), info.ProductVersion);
        Assert.Equal(ServerPlatform.OnPremises, info.Platform);
        Assert.Equal(92, info.UptimeDays);
    }

    [Theory]
    [InlineData(5, ServerPlatform.AzureSqlDatabase)]
    [InlineData(8, ServerPlatform.AzureManagedInstance)]
    [InlineData(3, ServerPlatform.OnPremises)]
    public void Maps_engine_edition_to_platform(int engineEdition, ServerPlatform expected)
        => Assert.Equal(expected, ServerInfoMapper.Map(Row(engineEdition, 1)).Platform);

    [Fact]
    public void Null_uptime_becomes_minus_one()
        => Assert.Equal(-1, ServerInfoMapper.Map(Row(5, DBNull.Value)).UptimeDays);
}
```

`tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/PermissionMapperTests.cs`:
```csharp
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class PermissionMapperTests
{
    private static SqlRow Row(bool view, bool alter, bool qs) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CanViewState"] = view, ["CanAlter"] = alter, ["CanAccessQueryStore"] = qs
    });

    [Fact]
    public void Maps_flags_and_notes_missing_permissions()
    {
        var report = PermissionMapper.Map(Row(view: false, alter: true, qs: true));
        Assert.False(report.CanViewState);
        Assert.True(report.CanAlter);
        Assert.Contains(report.Notes, n => n.Contains("VIEW", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_granted_has_no_notes()
        => Assert.Empty(PermissionMapper.Map(Row(true, true, true)).Notes);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~Mapper"`
Expected: FAIL.

- [ ] **Step 3: Write the implementations**

`Mapping/ServerInfoMapper.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class ServerInfoMapper
{
    public static ServerInfo Map(SqlRow row)
    {
        int engineEdition = row.Get<int>("EngineEdition");
        int? uptime = row.GetRaw("UptimeDays") is null ? null : row.Get<int>("UptimeDays");

        return new ServerInfo
        {
            ServerName = row.Get<string>("ServerName") ?? "",
            ProductVersion = Version.Parse(row.Get<string>("ProductVersion") ?? "0.0"),
            Edition = row.Get<string>("Edition") ?? "",
            Platform = engineEdition switch
            {
                5 => ServerPlatform.AzureSqlDatabase,
                8 => ServerPlatform.AzureManagedInstance,
                _ => ServerPlatform.OnPremises
            },
            UptimeDays = uptime ?? -1
        };
    }
}
```

`Mapping/PermissionMapper.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class PermissionMapper
{
    public static PermissionReport Map(SqlRow row)
    {
        bool view = row.Get<bool>("CanViewState");
        bool alter = row.Get<bool>("CanAlter");
        bool qs = row.Get<bool>("CanAccessQueryStore");

        var notes = new List<string>();
        if (!view) notes.Add("Missing VIEW SERVER STATE / VIEW DATABASE STATE: usage stats, plan cache and hints are unavailable.");
        if (!alter) notes.Add("Missing ALTER: DROP INDEX and Query Store activation are disabled; script generation stays available.");
        if (!qs) notes.Add("No access to Query Store: Query Store features are unavailable.");

        return new PermissionReport
        {
            CanViewState = view,
            CanAlter = alter,
            CanAccessQueryStore = qs,
            Notes = notes
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~Mapper"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Mapping/ServerInfoMapper.cs src/SmartIndexManager.Providers.SqlServer/Mapping/PermissionMapper.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/
git commit -m "feat(provider): server-info and permission mappers"
```

---

### Task 7: Index column mapper

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Mapping/IndexColumnRow.cs`, `IndexColumnMapper.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexColumnMapperTests.cs`

**Interfaces:**
- Consumes: `SqlRow`, `IndexColumn`, `SortDirection` (Core).
- Produces:
  - `IndexColumnRow(long ObjectId, int IndexId, string ColumnName, int KeyOrdinal, bool IsIncluded, bool IsDescending)`
  - `IndexColumnMapper.Map(SqlRow) -> IndexColumnRow`
  - `IndexColumnMapper.KeyColumns(IEnumerable<IndexColumnRow>) -> IReadOnlyList<IndexColumn>` (key columns ordered by `KeyOrdinal`, excluding included)
  - `IndexColumnMapper.IncludedColumns(IEnumerable<IndexColumnRow>) -> IReadOnlyList<string>`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexColumnMapperTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class IndexColumnMapperTests
{
    private static SqlRow Row(string name, int ordinal, bool included, bool desc) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ObjectId"] = 1L, ["IndexId"] = 2, ["ColumnName"] = name,
        ["KeyOrdinal"] = ordinal, ["IsIncluded"] = included, ["IsDescending"] = desc
    });

    [Fact]
    public void Key_columns_are_ordered_by_ordinal_with_direction()
    {
        var rows = new[]
        {
            IndexColumnMapper.Map(Row("OrderDate", 2, false, true)),
            IndexColumnMapper.Map(Row("CustomerId", 1, false, false))
        };

        var keys = IndexColumnMapper.KeyColumns(rows);
        Assert.Equal(new[] { "CustomerId", "OrderDate" }, keys.Select(k => k.Name));
        Assert.Equal(SortDirection.Ascending, keys[0].Direction);
        Assert.Equal(SortDirection.Descending, keys[1].Direction);
    }

    [Fact]
    public void Included_columns_are_separated_from_key_columns()
    {
        var rows = new[]
        {
            IndexColumnMapper.Map(Row("CustomerId", 1, false, false)),
            IndexColumnMapper.Map(Row("Total", 0, true, false))
        };

        Assert.Equal(new[] { "CustomerId" }, IndexColumnMapper.KeyColumns(rows).Select(k => k.Name));
        Assert.Equal(new[] { "Total" }, IndexColumnMapper.IncludedColumns(rows));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~IndexColumnMapperTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`Mapping/IndexColumnRow.cs`:
```csharp
namespace SmartIndexManager.Providers.SqlServer.Mapping;

public sealed record IndexColumnRow(
    long ObjectId, int IndexId, string ColumnName, int KeyOrdinal, bool IsIncluded, bool IsDescending);
```

`Mapping/IndexColumnMapper.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class IndexColumnMapper
{
    public static IndexColumnRow Map(SqlRow row) => new(
        ObjectId: row.Get<long>("ObjectId"),
        IndexId: row.Get<int>("IndexId"),
        ColumnName: row.Get<string>("ColumnName") ?? "",
        KeyOrdinal: row.Get<int>("KeyOrdinal"),
        IsIncluded: row.Get<bool>("IsIncluded"),
        IsDescending: row.Get<bool>("IsDescending"));

    public static IReadOnlyList<IndexColumn> KeyColumns(IEnumerable<IndexColumnRow> rows)
        => rows.Where(r => !r.IsIncluded)
               .OrderBy(r => r.KeyOrdinal)
               .Select(r => new IndexColumn(r.ColumnName,
                   r.IsDescending ? SortDirection.Descending : SortDirection.Ascending))
               .ToList();

    public static IReadOnlyList<string> IncludedColumns(IEnumerable<IndexColumnRow> rows)
        => rows.Where(r => r.IsIncluded)
               .Select(r => r.ColumnName)
               .ToList();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~IndexColumnMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Mapping/IndexColumnRow.cs src/SmartIndexManager.Providers.SqlServer/Mapping/IndexColumnMapper.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexColumnMapperTests.cs
git commit -m "feat(provider): index column mapper (key order, includes, direction)"
```

---

### Task 8: Index row mapper (index-level to IndexModel)

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Mapping/IndexRowMapper.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexRowMapperTests.cs`

**Interfaces:**
- Consumes: `SqlRow`, `IndexColumnRow`, `IndexColumnMapper`, and all Core model types (`IndexModel`, `IndexType`, `ConstraintKind`, `DataCompression`, `IndexOptions`).
- Produces: `IndexRowMapper.Map(SqlRow indexRow, IReadOnlyList<IndexColumnRow> columns) -> IndexModel`.
  Expected columns on the index row: `DatabaseName`, `SchemaName`, `TableName`, `IndexName`, `ObjectId`, `IndexId`, `IndexTypeCode`, `IsUnique`, `IsPrimaryKey`, `IsUniqueConstraint`, `IsDisabled`, `HasFilter`, `FilterDefinition`, `FillFactor`, `IsPadded`, `AllowRowLocks`, `AllowPageLocks`, `IgnoreDupKey`, `DataCompressionCode`, `IsOnView`, `IsSystemObject`, `DataSpaceName`, `DataSpaceType`, `IsPartitioned`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexRowMapperTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class IndexRowMapperTests
{
    private static SqlRow IndexRow(int typeCode, Action<Dictionary<string, object?>>? tweak = null)
    {
        var cells = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DatabaseName"] = "Sales", ["SchemaName"] = "dbo", ["TableName"] = "Orders",
            ["IndexName"] = "IX_Orders", ["ObjectId"] = 1L, ["IndexId"] = 2,
            ["IndexTypeCode"] = typeCode, ["IsUnique"] = false, ["IsPrimaryKey"] = false,
            ["IsUniqueConstraint"] = false, ["IsDisabled"] = false, ["HasFilter"] = false,
            ["FilterDefinition"] = DBNull.Value, ["FillFactor"] = 0, ["IsPadded"] = false,
            ["AllowRowLocks"] = true, ["AllowPageLocks"] = true, ["IgnoreDupKey"] = false,
            ["DataCompressionCode"] = 0, ["IsOnView"] = false, ["IsSystemObject"] = false,
            ["DataSpaceName"] = "PRIMARY", ["DataSpaceType"] = "FG", ["IsPartitioned"] = false
        };
        tweak?.Invoke(cells);
        return new SqlRow(cells);
    }

    private static readonly IReadOnlyList<IndexColumnRow> OneKey =
        [new IndexColumnRow(1, 2, "CustomerId", 1, false, false)];

    [Fact]
    public void Maps_nonclustered_rowstore_identity_and_key()
    {
        var index = IndexRowMapper.Map(IndexRow(2), OneKey);
        Assert.Equal(IndexType.NonclusteredRowstore, index.Type);
        Assert.Equal("Sales", index.Database);
        Assert.Equal("dbo", index.Schema);
        Assert.Equal("IX_Orders", index.Name);
        Assert.Equal(new[] { "CustomerId" }, index.KeyColumns.Select(k => k.Name));
        Assert.Equal("PRIMARY", index.DataSpace);
    }

    [Theory]
    [InlineData(0, IndexType.Heap)]
    [InlineData(1, IndexType.ClusteredRowstore)]
    [InlineData(2, IndexType.NonclusteredRowstore)]
    [InlineData(3, IndexType.Xml)]
    [InlineData(4, IndexType.Spatial)]
    [InlineData(5, IndexType.ClusteredColumnstore)]
    [InlineData(6, IndexType.NonclusteredColumnstore)]
    public void Maps_type_codes(int code, IndexType expected)
        => Assert.Equal(expected, IndexRowMapper.Map(IndexRow(code), OneKey).Type);

    [Fact]
    public void Primary_key_maps_to_constraint_and_unique()
    {
        var index = IndexRowMapper.Map(IndexRow(1, c => { c["IsPrimaryKey"] = true; c["IsUnique"] = true; }), OneKey);
        Assert.Equal(ConstraintKind.PrimaryKey, index.Constraint);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Unique_constraint_maps_to_constraint_unique()
        => Assert.Equal(ConstraintKind.Unique,
            IndexRowMapper.Map(IndexRow(2, c => { c["IsUniqueConstraint"] = true; c["IsUnique"] = true; }), OneKey).Constraint);

    [Fact]
    public void Filter_definition_is_carried_when_has_filter()
    {
        var index = IndexRowMapper.Map(IndexRow(2, c => { c["HasFilter"] = true; c["FilterDefinition"] = "([Status]=(1))"; }), OneKey);
        Assert.Equal("([Status]=(1))", index.FilterPredicate);
    }

    [Fact]
    public void Options_and_flags_are_mapped()
    {
        var index = IndexRowMapper.Map(IndexRow(2, c =>
        {
            c["FillFactor"] = 80; c["IsPadded"] = true; c["AllowPageLocks"] = false;
            c["DataCompressionCode"] = 2; c["IsDisabled"] = true; c["IsPartitioned"] = true;
        }), OneKey);

        Assert.Equal(80, index.Options.FillFactor);
        Assert.True(index.Options.PadIndex);
        Assert.False(index.Options.AllowPageLocks);
        Assert.Equal(DataCompression.Page, index.Options.Compression);
        Assert.True(index.IsDisabled);
        Assert.True(index.IsPartitioned);
    }

    [Fact]
    public void Fill_factor_zero_maps_to_null()
        => Assert.Null(IndexRowMapper.Map(IndexRow(2, c => c["FillFactor"] = 0), OneKey).Options.FillFactor);

    [Fact]
    public void Unknown_compression_code_maps_to_unsupported()
        => Assert.Equal(DataCompression.Unsupported,
            IndexRowMapper.Map(IndexRow(2, c => c["DataCompressionCode"] = 3), OneKey).Options.Compression);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~IndexRowMapperTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`Mapping/IndexRowMapper.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class IndexRowMapper
{
    public static IndexModel Map(SqlRow row, IReadOnlyList<IndexColumnRow> columns)
    {
        bool isPrimaryKey = row.Get<bool>("IsPrimaryKey");
        bool isUniqueConstraint = row.Get<bool>("IsUniqueConstraint");
        int fillFactor = row.Get<int>("FillFactor");

        return new IndexModel
        {
            Database = row.Get<string>("DatabaseName") ?? "",
            Schema = row.Get<string>("SchemaName") ?? "",
            Table = row.Get<string>("TableName") ?? "",
            Name = row.Get<string>("IndexName") ?? "",
            Type = MapType(row.Get<int>("IndexTypeCode")),
            KeyColumns = IndexColumnMapper.KeyColumns(columns),
            IncludedColumns = IndexColumnMapper.IncludedColumns(columns),
            FilterPredicate = row.Get<bool>("HasFilter") ? row.Get<string>("FilterDefinition") : null,
            IsUnique = row.Get<bool>("IsUnique"),
            Constraint = isPrimaryKey ? ConstraintKind.PrimaryKey
                       : isUniqueConstraint ? ConstraintKind.Unique
                       : ConstraintKind.None,
            IsDisabled = row.Get<bool>("IsDisabled"),
            IsOnView = row.Get<bool>("IsOnView"),
            IsOnSystemTable = row.Get<bool>("IsSystemObject"),
            IsPartitioned = row.Get<bool>("IsPartitioned"),
            DataSpace = row.Get<string>("DataSpaceName"),
            Options = new IndexOptions
            {
                FillFactor = fillFactor is >= 1 and <= 100 ? fillFactor : null,
                PadIndex = row.Get<bool>("IsPadded"),
                AllowRowLocks = row.Get<bool>("AllowRowLocks"),
                AllowPageLocks = row.Get<bool>("AllowPageLocks"),
                IgnoreDupKey = row.Get<bool>("IgnoreDupKey"),
                Compression = MapCompression(row.Get<int>("DataCompressionCode"))
            }
        };
    }

    private static IndexType MapType(int code) => code switch
    {
        0 => IndexType.Heap,
        1 => IndexType.ClusteredRowstore,
        2 => IndexType.NonclusteredRowstore,
        3 => IndexType.Xml,
        4 => IndexType.Spatial,
        5 => IndexType.ClusteredColumnstore,
        6 => IndexType.NonclusteredColumnstore,
        7 => IndexType.Spatial,          // 7 = extended/spatial family; treated as non-droppable
        _ => IndexType.Hypothetical
    };

    private static DataCompression MapCompression(int code) => code switch
    {
        0 => DataCompression.None,
        1 => DataCompression.Row,
        2 => DataCompression.Page,
        _ => DataCompression.Unsupported     // 3/4 = columnstore compression, not reconstructable here
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~IndexRowMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Mapping/IndexRowMapper.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/IndexRowMapperTests.cs
git commit -m "feat(provider): index row mapper to IndexModel"
```

---

### Task 9: Usage, hint and Query Store state mappers

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Mapping/QueryUsageMapper.cs`, `HintMapper.cs`, `QueryStoreStateMapper.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/DiagnosticMapperTests.cs`

**Interfaces:**
- Consumes: `SqlRow`, `QueryUsage`, `UsageSource`, `IndexHint`, `QueryStoreState`.
- Produces:
  - `QueryUsageMapper.Map(SqlRow, UsageSource) -> QueryUsage` (columns `QueryText`, `ExecutionCount`, `LastExecutionUtc`)
  - `HintMapper.Map(SqlRow) -> IndexHint` (columns `Reference`, `Kind`)
  - `QueryStoreStateMapper.Map(int? desiredStateCode) -> QueryStoreState` (from `sys.database_query_store_options.actual_state`)

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/DiagnosticMapperTests.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class DiagnosticMapperTests
{
    [Fact]
    public void Usage_mapper_carries_source_and_fields()
    {
        var row = new SqlRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["QueryText"] = "SELECT ...", ["ExecutionCount"] = 42L,
            ["LastExecutionUtc"] = new DateTime(2026, 07, 20, 8, 0, 0, DateTimeKind.Utc)
        });

        var usage = QueryUsageMapper.Map(row, UsageSource.QueryStore);
        Assert.Equal(UsageSource.QueryStore, usage.Source);
        Assert.Equal(42L, usage.ExecutionCount);
        Assert.Equal(2026, usage.LastExecutionUtc!.Value.Year);
    }

    [Fact]
    public void Hint_mapper_reads_reference_and_kind()
    {
        var row = new SqlRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reference"] = "dbo.GetOrders", ["Kind"] = "query hint"
        });
        var hint = HintMapper.Map(row);
        Assert.Equal("dbo.GetOrders", hint.Reference);
        Assert.Equal("query hint", hint.Kind);
    }

    [Theory]
    [InlineData(null, QueryStoreState.Off)]
    [InlineData(0, QueryStoreState.Off)]
    [InlineData(1, QueryStoreState.ReadOnly)]
    [InlineData(2, QueryStoreState.ReadWrite)]
    public void Query_store_state_maps_actual_state_code(int? code, QueryStoreState expected)
        => Assert.Equal(expected, QueryStoreStateMapper.Map(code));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~DiagnosticMapperTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementations**

`Mapping/QueryUsageMapper.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class QueryUsageMapper
{
    public static QueryUsage Map(SqlRow row, UsageSource source) => new(
        QueryText: row.Get<string>("QueryText") ?? "",
        ExecutionCount: row.Get<long>("ExecutionCount"),
        LastExecutionUtc: row.GetRaw("LastExecutionUtc") is null ? null : row.Get<DateTime>("LastExecutionUtc"),
        Source: source);
}
```

`Mapping/HintMapper.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class HintMapper
{
    public static IndexHint Map(SqlRow row)
        => new(row.Get<string>("Reference") ?? "", row.Get<string>("Kind") ?? "query hint");
}
```

`Mapping/QueryStoreStateMapper.cs`:
```csharp
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class QueryStoreStateMapper
{
    // sys.database_query_store_options.actual_state: 0 OFF, 1 READ_ONLY, 2 READ_WRITE, 3 ERROR.
    // Null means the row is absent (Query Store never configured) -> treated as OFF.
    public static QueryStoreState Map(int? actualState) => actualState switch
    {
        1 => QueryStoreState.ReadOnly,
        2 => QueryStoreState.ReadWrite,
        _ => QueryStoreState.Off
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~DiagnosticMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Mapping/QueryUsageMapper.cs src/SmartIndexManager.Providers.SqlServer/Mapping/HintMapper.cs src/SmartIndexManager.Providers.SqlServer/Mapping/QueryStoreStateMapper.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Mapping/DiagnosticMapperTests.cs
git commit -m "feat(provider): usage, hint and Query Store state mappers"
```

---

### Task 10: SQL scripts, detection and metadata

**Files:**
- Create: `sql/sqlserver/server-info.sql`, `permissions-check.sql`, `querystore-state.sql`, `index-list.sql`, `index-columns.sql`, `fk-support.sql`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs`

**Interfaces:**
- Consumes: `SqlScriptLoader`, `SqlScript` (Core).
- Produces: the six read scripts, each loadable and column-declaring. The contract test asserts each ships, parses, and declares the columns its mapper reads.

Design notes: the scripts target on-premises SQL Server 2012+ and are what the Testcontainers integration exercises. `server-info.sql` reads server-scoped uptime via `sys.dm_os_sys_info`, which is unavailable on Azure SQL Database; an `azure=only` variant returning `UptimeDays = NULL` is a follow-up captured in the self-review, not built here. Identifiers are quoted by the DDL generator in Core, not here; these read queries take named parameters only.

- [ ] **Step 1: Write `server-info.sql`**

`sql/sqlserver/server-info.sql`:
```sql
-- sim: name=server-info
-- sim: minversion=11.0
-- sim: azure=unsupported
-- sim: columns=ServerName,ProductVersion,Edition,EngineEdition,UptimeDays
SELECT
    CAST(SERVERPROPERTY('ServerName')       AS nvarchar(256)) AS ServerName,
    CAST(SERVERPROPERTY('ProductVersion')   AS nvarchar(64))  AS ProductVersion,
    CAST(SERVERPROPERTY('Edition')          AS nvarchar(128)) AS Edition,
    CAST(SERVERPROPERTY('EngineEdition')    AS int)           AS EngineEdition,
    DATEDIFF(DAY, si.sqlserver_start_time, SYSUTCDATETIME())  AS UptimeDays
FROM sys.dm_os_sys_info AS si;
```

- [ ] **Step 2: Write `permissions-check.sql`**

`sql/sqlserver/permissions-check.sql`:
```sql
-- sim: name=permissions-check
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=CanViewState,CanAlter,CanAccessQueryStore
SELECT
    CASE WHEN HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE') = 1
           OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanViewState,
    CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanAlter,
    CASE WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') = 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS CanAccessQueryStore;
```

- [ ] **Step 3: Write `querystore-state.sql`**

`sql/sqlserver/querystore-state.sql`:
```sql
-- sim: name=querystore-state
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=ActualState
SELECT TOP (1) CAST(actual_state AS int) AS ActualState
FROM sys.database_query_store_options;
```

- [ ] **Step 4: Write `index-list.sql`**

`sql/sqlserver/index-list.sql`:
```sql
-- sim: name=index-list
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=DatabaseName,SchemaName,TableName,IndexName,ObjectId,IndexId,IndexTypeCode,IsUnique,IsPrimaryKey,IsUniqueConstraint,IsDisabled,HasFilter,FilterDefinition,FillFactor,IsPadded,AllowRowLocks,AllowPageLocks,IgnoreDupKey,DataCompressionCode,IsOnView,IsSystemObject,DataSpaceName,DataSpaceType,IsPartitioned
SELECT
    DB_NAME()               AS DatabaseName,
    s.name                  AS SchemaName,
    o.name                  AS TableName,
    i.name                  AS IndexName,
    i.object_id             AS ObjectId,
    i.index_id              AS IndexId,
    i.type                  AS IndexTypeCode,
    i.is_unique             AS IsUnique,
    i.is_primary_key        AS IsPrimaryKey,
    i.is_unique_constraint  AS IsUniqueConstraint,
    i.is_disabled           AS IsDisabled,
    i.has_filter            AS HasFilter,
    i.filter_definition     AS FilterDefinition,
    i.fill_factor           AS FillFactor,
    i.is_padded             AS IsPadded,
    i.allow_row_locks       AS AllowRowLocks,
    i.allow_page_locks      AS AllowPageLocks,
    i.ignore_dup_key        AS IgnoreDupKey,
    ISNULL((SELECT TOP (1) p.data_compression
              FROM sys.partitions p
             WHERE p.object_id = i.object_id AND p.index_id = i.index_id
             ORDER BY p.partition_number), 0) AS DataCompressionCode,
    CASE WHEN o.type = 'V' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsOnView,
    o.is_ms_shipped         AS IsSystemObject,
    ds.name                 AS DataSpaceName,
    ds.type                 AS DataSpaceType,
    CASE WHEN (SELECT COUNT(*) FROM sys.partitions p2
                WHERE p2.object_id = i.object_id AND p2.index_id = i.index_id) > 1
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsPartitioned
FROM sys.indexes i
JOIN sys.objects o        ON o.object_id = i.object_id
JOIN sys.schemas s        ON s.schema_id = o.schema_id
LEFT JOIN sys.data_spaces ds ON ds.data_space_id = i.data_space_id
WHERE o.type IN ('U', 'V') AND i.index_id >= 0 AND i.name IS NOT NULL
ORDER BY s.name, o.name, i.index_id;
```

- [ ] **Step 5: Write `index-columns.sql`**

`sql/sqlserver/index-columns.sql`:
```sql
-- sim: name=index-columns
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=ObjectId,IndexId,ColumnName,KeyOrdinal,IsIncluded,IsDescending
SELECT
    ic.object_id          AS ObjectId,
    ic.index_id           AS IndexId,
    c.name                AS ColumnName,
    ic.key_ordinal        AS KeyOrdinal,
    ic.is_included_column AS IsIncluded,
    ic.is_descending_key  AS IsDescending
FROM sys.index_columns ic
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
JOIN sys.objects o ON o.object_id = ic.object_id
WHERE o.type IN ('U', 'V')
ORDER BY ic.object_id, ic.index_id, ic.is_included_column, ic.key_ordinal;
```

- [ ] **Step 6: Write `fk-support.sql`**

`sql/sqlserver/fk-support.sql`:
```sql
-- sim: name=fk-support
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=ObjectId,IndexId
-- Conservative heuristic: an index is flagged FK-supporting when its first key column is a
-- referencing column of some foreign key on the same table. This deliberately over-flags: it
-- does not verify that the whole FK column set is an ordered prefix of the index key. For a
-- guard-rail warning a false "supports a foreign key" is safe; a missed one is not.
SELECT DISTINCT i.object_id AS ObjectId, i.index_id AS IndexId
FROM sys.indexes i
JOIN sys.index_columns ic
      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
     AND ic.is_included_column = 0 AND ic.key_ordinal = 1
WHERE i.index_id >= 1
  AND EXISTS (
      SELECT 1
      FROM sys.foreign_key_columns fkc
      JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
      WHERE fk.parent_object_id = i.object_id
        AND fkc.parent_column_id = ic.column_id);
```

- [ ] **Step 7: Write the contract test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs`:
```csharp
using SmartIndexManager.Core.Sql;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Sql;

public class ScriptContractTests
{
    // Repo-relative path to the shipped scripts, resolved from the test binary location.
    private static string ScriptRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return Path.Combine(dir!, "sql", "sqlserver");
    }

    [Theory]
    [InlineData("server-info", new[] { "ServerName", "ProductVersion", "Edition", "EngineEdition", "UptimeDays" })]
    [InlineData("permissions-check", new[] { "CanViewState", "CanAlter", "CanAccessQueryStore" })]
    [InlineData("querystore-state", new[] { "ActualState" })]
    [InlineData("index-columns", new[] { "ObjectId", "IndexId", "ColumnName", "KeyOrdinal", "IsIncluded", "IsDescending" })]
    [InlineData("fk-support", new[] { "ObjectId", "IndexId" })]
    public void Script_ships_and_declares_expected_columns(string name, string[] expected)
    {
        var script = SqlScriptLoader.Load(ScriptRoot(), name);
        foreach (var column in expected)
            Assert.Contains(column, script.ExpectedColumns);
    }

    [Fact]
    public void Index_list_declares_every_column_the_mapper_reads()
    {
        var script = SqlScriptLoader.Load(ScriptRoot(), "index-list");
        foreach (var column in new[]
        {
            "DatabaseName", "SchemaName", "TableName", "IndexName", "IndexTypeCode",
            "IsUnique", "IsPrimaryKey", "IsUniqueConstraint", "IsDisabled", "HasFilter",
            "FilterDefinition", "FillFactor", "DataCompressionCode", "DataSpaceName", "IsPartitioned"
        })
            Assert.Contains(column, script.ExpectedColumns);
    }
}
```

- [ ] **Step 8: Copy the scripts to the test output**

The contract test walks up from the test binary to find `sql/sqlserver`, so no copy is needed when tests run inside the repo. To make the scripts available to the packaged app later, add to `src/SmartIndexManager.Providers.SqlServer.csproj` a note (not a build step yet): the App plan will copy `sql/sqlserver/**` to output. For now, verify the test resolves the repo path.

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ScriptContractTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add sql/sqlserver/server-info.sql sql/sqlserver/permissions-check.sql sql/sqlserver/querystore-state.sql sql/sqlserver/index-list.sql sql/sqlserver/index-columns.sql sql/sqlserver/fk-support.sql tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs
git commit -m "feat(provider): detection and metadata SQL scripts with contract tests"
```

---

### Task 11: SQL scripts, diagnostics and actions

**Files:**
- Create: `sql/sqlserver/index-used-by-queries.sql`, `index-used-by-queries-query-store.sql`, `index-hints-plancache.sql`, `replication-ag-check.sql`, `querystore-enable.sql`
- Modify: `tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs` (add these to the theory)

**Interfaces:**
- Consumes: `SqlScriptLoader`.
- Produces: five more scripts. `querystore-enable.sql` uses `QUOTENAME` on the database name inside T-SQL because `ALTER DATABASE` cannot be parameterized; the C# side still passes the name as a validated parameter to a wrapper (Task 16).

- [ ] **Step 1: Write `index-used-by-queries.sql` (plan cache)**

`sql/sqlserver/index-used-by-queries.sql`:
```sql
-- sim: name=index-used-by-queries
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=QueryText,ExecutionCount,LastExecutionUtc
-- Queries in the plan cache whose plan references the given index by name.
SELECT
    SUBSTRING(st.text, 1, 4000) AS QueryText,
    qs.execution_count          AS ExecutionCount,
    qs.last_execution_time      AS LastExecutionUtc
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
WHERE CAST(qp.query_plan AS nvarchar(max)) LIKE '%' + @IndexName + '%'
ORDER BY qs.execution_count DESC;
```

- [ ] **Step 2: Write `index-used-by-queries-query-store.sql`**

`sql/sqlserver/index-used-by-queries-query-store.sql`:
```sql
-- sim: name=index-used-by-queries-query-store
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=QueryText,ExecutionCount,LastExecutionUtc
SELECT
    SUBSTRING(qt.query_sql_text, 1, 4000) AS QueryText,
    SUM(rs.count_executions)              AS ExecutionCount,
    MAX(rs.last_execution_time)           AS LastExecutionUtc
FROM sys.query_store_plan qp
JOIN sys.query_store_query q  ON q.query_id = qp.query_id
JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id = qp.plan_id
WHERE CAST(qp.query_plan AS nvarchar(max)) LIKE '%' + @IndexName + '%'
GROUP BY qt.query_sql_text
ORDER BY SUM(rs.count_executions) DESC;
```

- [ ] **Step 3: Write `index-hints-plancache.sql`**

`sql/sqlserver/index-hints-plancache.sql`:
```sql
-- sim: name=index-hints-plancache
-- sim: minversion=11.0
-- sim: azure=supported
-- sim: columns=Reference,Kind
-- Plan guides plus cached queries that force an index by name via a table hint.
SELECT
    pg.name AS Reference,
    'plan guide' AS Kind
FROM sys.plan_guides pg
WHERE pg.hints LIKE '%INDEX%' + @IndexName + '%'
   OR pg.query_text LIKE '%INDEX%' + @IndexName + '%'
UNION ALL
SELECT
    SUBSTRING(st.text, 1, 256) AS Reference,
    'query hint' AS Kind
FROM sys.dm_exec_cached_plans cp
CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
WHERE st.text LIKE '%INDEX%(%' + @IndexName + '%)%'
  AND st.text LIKE '%WITH%';
```

- [ ] **Step 4: Write `replication-ag-check.sql`**

`sql/sqlserver/replication-ag-check.sql`:
```sql
-- sim: name=replication-ag-check
-- sim: minversion=11.0
-- sim: azure=unsupported
-- sim: columns=InReplicationOrAg
SELECT
    CASE WHEN d.is_published = 1 OR d.is_subscribed = 1 OR d.is_merge_published = 1
              OR EXISTS (SELECT 1 FROM sys.dm_hadr_database_replica_states rs
                          WHERE rs.database_id = d.database_id)
         THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS InReplicationOrAg
FROM sys.databases d
WHERE d.database_id = DB_ID();
```

- [ ] **Step 5: Write `querystore-enable.sql`**

`sql/sqlserver/querystore-enable.sql`:
```sql
-- sim: name=querystore-enable
-- sim: minversion=13.0
-- sim: azure=supported
-- sim: columns=Applied
-- @DatabaseName, @MaxStorageSizeMb and @StaleQueryThresholdDays are supplied as parameters.
-- ALTER DATABASE cannot be parameterized, so the name is quoted with QUOTENAME and the
-- numeric options are concatenated from integer parameters only (never free text).
DECLARE @sql nvarchar(max) =
    N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N' SET QUERY_STORE = ON (' +
    N' OPERATION_MODE = READ_WRITE,' +
    N' QUERY_CAPTURE_MODE = AUTO,' +
    N' SIZE_BASED_CLEANUP_MODE = AUTO,' +
    N' MAX_STORAGE_SIZE_MB = ' + CAST(@MaxStorageSizeMb AS nvarchar(20)) + N',' +
    N' CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = ' + CAST(@StaleQueryThresholdDays AS nvarchar(20)) + N')' +
    N');';
EXEC sys.sp_executesql @sql;
SELECT CAST(1 AS bit) AS Applied;
```

- [ ] **Step 6: Extend the contract test**

In `ScriptContractTests.cs`, add to the `[Theory]` cases:
```csharp
    [InlineData("index-used-by-queries", new[] { "QueryText", "ExecutionCount", "LastExecutionUtc" })]
    [InlineData("index-used-by-queries-query-store", new[] { "QueryText", "ExecutionCount", "LastExecutionUtc" })]
    [InlineData("index-hints-plancache", new[] { "Reference", "Kind" })]
    [InlineData("replication-ag-check", new[] { "InReplicationOrAg" })]
    [InlineData("querystore-enable", new[] { "Applied" })]
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ScriptContractTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add sql/sqlserver/index-used-by-queries.sql sql/sqlserver/index-used-by-queries-query-store.sql sql/sqlserver/index-hints-plancache.sql sql/sqlserver/replication-ag-check.sql sql/sqlserver/querystore-enable.sql tests/SmartIndexManager.Providers.SqlServer.Tests/Sql/ScriptContractTests.cs
git commit -m "feat(provider): diagnostic and action SQL scripts with contract tests"
```

---

### Task 12: SqlClient executor and the Testcontainers fixture

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/Execution/SqlClientExecutor.cs`
- Create: `tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/DockerAvailable.cs`, `SqlServerContainerFixture.cs`, `Integration/ExecutorIntegrationTests.cs`

**Interfaces:**
- Consumes: `ISqlExecutor`, `SqlRow`, `SqlScript` (declared columns for validation).
- Produces: `SqlClientExecutor(SqlConnection connection)` implementing `ISqlExecutor`, validating declared columns against the reader, mapping named parameters to `SqlParameter`s.

- [ ] **Step 1: Write the Docker-availability gate and fixture**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/DockerAvailable.cs`:
```csharp
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

// Skip the whole integration suite when Docker is not reachable.
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerAvailable.Value) Skip = "Docker is not available; skipping integration tests.";
    }
}

public static class DockerAvailable
{
    public static readonly bool Value = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker", Arguments = "info", RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false
            });
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch { return false; }
    }
}
```

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/SqlServerContainerFixture.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using Testcontainers.MsSql;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public string ConnectionString { get; private set; } = "";

    public string Database => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    public static string ScriptRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        return Path.Combine(dir!, "sql", "sqlserver");
    }

    // Shared connect helper so every integration test stops rebuilding the request by hand.
    public async Task<IIndexProvider> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var b = new SqlConnectionStringBuilder(ConnectionString);
        var factory = new SqlServerIndexProviderFactory(ScriptRoot());
        var request = new ConnectionRequest
        {
            Server = b.DataSource, Auth = AuthMode.SqlLogin, Login = b.UserID, TrustServerCertificate = true
        };
        return await factory.ConnectAsync(request, b.Password, cancellationToken);
    }

    public async ValueTask InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        _container = new MsSqlBuilder().Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE dbo.Orders (Id int IDENTITY PRIMARY KEY, CustomerId int NOT NULL, OrderDate date NULL, Total money NULL);
            CREATE NONCLUSTERED INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
            CREATE NONCLUSTERED INDEX IX_Orders_Unused ON dbo.Orders (OrderDate) INCLUDE (Total);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>;
```

- [ ] **Step 2: Write `SqlClientExecutor`**

`src/SmartIndexManager.Providers.SqlServer/Execution/SqlClientExecutor.cs`:
```csharp
using System.Data;
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Execution;

public sealed class SqlClientExecutor : ISqlExecutor
{
    private readonly SqlConnection _connection;

    public SqlClientExecutor(SqlConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<SqlRow>> QueryAsync(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(script.Sql, parameters, timeout: null);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        ValidateColumns(script, reader);

        var rows = new List<SqlRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // DBNull is normalized to null here so mappers never see DBNull (SqlRow also guards).
            var cells = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                cells[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(new SqlRow(cells));
        }
        return rows;
    }

    public async Task<T?> ScalarAsync<T>(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(script.Sql, parameters, timeout: null);
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull) return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, target);
    }

    public async Task<int> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(sql, parameters, timeout);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqlCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (timeout is TimeSpan t) cmd.CommandTimeout = (int)t.TotalSeconds;
        if (parameters is not null)
            foreach (var (key, value) in parameters)
                cmd.Parameters.Add(new SqlParameter(key, value ?? DBNull.Value));
        return cmd;
    }

    private static void ValidateColumns(SqlScript script, SqlDataReader reader)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++) present.Add(reader.GetName(i));

        var missing = script.ExpectedColumns.Where(c => !present.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"script '{script.Name}' is missing declared column(s): {string.Join(", ", missing)}");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

- [ ] **Step 3: Write the integration test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ExecutorIntegrationTests.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Execution;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ExecutorIntegrationTests
{
    private readonly SqlServerContainerFixture _fx;
    public ExecutorIntegrationTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Runs_index_list_and_returns_seeded_indexes()
    {
        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var executor = new SqlClientExecutor(conn);

        var script = SqlScriptLoader.Load(SqlServerContainerFixture.ScriptRoot(), "index-list");
        var rows = await executor.QueryAsync(script, null, CancellationToken.None);

        var names = rows.Select(r => r.Get<string>("IndexName")).ToList();
        Assert.Contains("IX_Orders_Customer", names);
        Assert.Contains("IX_Orders_Unused", names);
    }

    [RequiresDockerFact]
    public async Task Missing_declared_column_throws()
    {
        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var executor = new SqlClientExecutor(conn);

        // A script whose header declares a column the query does not return.
        var bad = new SqlScript("bad", "SELECT 1 AS Present;",
            new SqlFileHeader("bad", new Version(11, 0), AzureSupport.Supported, new[] { "Missing" }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.QueryAsync(bad, null, CancellationToken.None).AsTask());
    }
}
```

Note: `QueryAsync` returns `Task<IReadOnlyList<SqlRow>>`; `.AsTask()` is only needed if it were a `ValueTask`. Since it is a `Task`, call it directly: `() => executor.QueryAsync(bad, null, CancellationToken.None)`.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ExecutorIntegrationTests"`
Expected: PASS if Docker is available; SKIPPED otherwise.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/Execution/SqlClientExecutor.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/
git commit -m "feat(provider): SqlClient executor with column validation and Testcontainers fixture"
```

---

### Task 13: Provider connect, detect and capabilities

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.cs` (partial: connect + detection + the read scaffolding), `SqlServerIndexProviderFactory.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ConnectAndDetectTests.cs`

**Interfaces:**
- Consumes: `IIndexProvider`, `IIndexProviderFactory`, `SqlServerConnectionFactory`, `SqlClientExecutor`, `ServerInfoMapper`, `PermissionMapper`, `CapabilityResolver`, `SqlScriptLoader`.
- Produces:
  - `SqlServerIndexProviderFactory(string scriptRoot) : IIndexProviderFactory`
  - `SqlServerIndexProvider : IIndexProvider` with `ServerInfo`, `Capabilities`, `Permissions` populated at connect.

- [ ] **Step 1: Write the provider constructor and factory (detection wired in)**

`src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProviderFactory.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Capabilities;
using SmartIndexManager.Providers.SqlServer.Connection;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed class SqlServerIndexProviderFactory : IIndexProviderFactory
{
    private readonly string _scriptRoot;

    public SqlServerIndexProviderFactory(string scriptRoot) => _scriptRoot = scriptRoot;

    public async Task<IIndexProvider> ConnectAsync(
        ConnectionRequest request, string? password, CancellationToken cancellationToken = default)
    {
        var connectionString = SqlServerConnectionFactory.BuildConnectionString(request, password);
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var executor = new SqlClientExecutor(connection);
            var serverInfo = await DetectServerInfoAsync(executor, cancellationToken).ConfigureAwait(false);
            var permissions = await DetectPermissionsAsync(executor, cancellationToken).ConfigureAwait(false);
            var capabilities = CapabilityResolver.Resolve(serverInfo);

            // Ownership of the connection passes to the provider (disposed via the executor).
            return new SqlServerIndexProvider(executor, _scriptRoot, serverInfo, capabilities, permissions);
        }
        catch
        {
            // Open succeeded but detection failed: do not leak the connection.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ServerInfo> DetectServerInfoAsync(ISqlExecutor executor, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, "server-info");
        var rows = await executor.QueryAsync(script, null, ct).ConfigureAwait(false);
        return ServerInfoMapper.Map(rows[0]);
    }

    private async Task<PermissionReport> DetectPermissionsAsync(ISqlExecutor executor, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, "permissions-check");
        var rows = await executor.QueryAsync(script, null, ct).ConfigureAwait(false);
        return PermissionMapper.Map(rows[0]);
    }
}
```

`src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider : IIndexProvider
{
    private readonly ISqlExecutor _executor;
    private readonly string _scriptRoot;

    public ServerInfo ServerInfo { get; }
    public ProviderCapabilities Capabilities { get; }
    public PermissionReport Permissions { get; }

    internal SqlServerIndexProvider(
        ISqlExecutor executor, string scriptRoot,
        ServerInfo serverInfo, ProviderCapabilities capabilities, PermissionReport permissions)
    {
        _executor = executor;
        _scriptRoot = scriptRoot;
        ServerInfo = serverInfo;
        Capabilities = capabilities;
        Permissions = permissions;
    }

    public ValueTask DisposeAsync() => _executor.DisposeAsync();

    // GetIndexesAsync, GetQueryUsageAsync, GetHintsAsync, GetQueryStoreStateAsync,
    // EnableQueryStoreAsync and DropIndexAsync are added in Tasks 14-16 (partial class).
}
```

- [ ] **Step 2: Write the integration test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ConnectAndDetectTests.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ConnectAndDetectTests
{
    private readonly SqlServerContainerFixture _fx;
    public ConnectAndDetectTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Connects_and_detects_on_premises_server()
    {
        await using var provider = await _fx.ConnectAsync();

        Assert.Equal(ServerPlatform.OnPremises, provider.ServerInfo.Platform);
        Assert.True(provider.ServerInfo.ProductVersion.Major >= 15);   // container image is 2022
        Assert.True(provider.Permissions.CanViewState);                // sa has full rights
        Assert.True(provider.Capabilities.SupportsQueryStore);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ConnectAndDetectTests"`
Expected: PASS if Docker available; SKIPPED otherwise.

- [ ] **Step 4: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.cs src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProviderFactory.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ConnectAndDetectTests.cs
git commit -m "feat(provider): connect, detect server info, permissions and capabilities"
```

---

### Task 14: GetIndexesAsync

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Indexes.cs` (partial)
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/GetIndexesTests.cs`

**Interfaces:**
- Consumes: `IndexRowMapper`, `IndexColumnMapper`, `IndexColumnRow`, `SqlScriptLoader`, the fk-support script.
- Produces: `SqlServerIndexProvider.GetIndexesAsync` implementation. For each database: run `index-list`, `index-columns`, `fk-support`; group columns by (ObjectId, IndexId); map each index; the FK-support set is exposed via `IndexModel.ProviderProperties["fkSupport"] = "true"` so the App and Core can flag it (Core's safety takes `SupportsForeignKey` from the caller, which reads this property).

- [ ] **Step 1: Write the implementation**

`src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Indexes.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public async Task<IReadOnlyList<IndexModel>> GetIndexesAsync(
        IReadOnlyList<string> databases, CancellationToken cancellationToken = default)
    {
        var result = new List<IndexModel>();
        foreach (var database in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.AddRange(await GetIndexesForDatabaseAsync(database, cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    private async Task<IReadOnlyList<IndexModel>> GetIndexesForDatabaseAsync(string database, CancellationToken ct)
    {
        // index-list / index-columns / fk-support are database-scoped; run them in the target database.
        await UseDatabaseAsync(database, ct).ConfigureAwait(false);

        var indexRows = await QueryAsync("index-list", null, ct).ConfigureAwait(false);
        var columnRows = await QueryAsync("index-columns", null, ct).ConfigureAwait(false);
        var fkRows = await QueryAsync("fk-support", null, ct).ConfigureAwait(false);

        var columnsByIndex = columnRows
            .Select(IndexColumnMapper.Map)
            .GroupBy(c => (c.ObjectId, c.IndexId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IndexColumnRow>)g.ToList());

        var fkSupport = fkRows
            .Select(r => (r.Get<long>("ObjectId"), r.Get<int>("IndexId")))
            .ToHashSet();

        var models = new List<IndexModel>(indexRows.Count);
        foreach (var row in indexRows)
        {
            var key = (row.Get<long>("ObjectId"), row.Get<int>("IndexId"));
            var columns = columnsByIndex.TryGetValue(key, out var c) ? c : [];
            var model = IndexRowMapper.Map(row, columns);
            if (fkSupport.Contains(key))
                model = model with
                {
                    ProviderProperties = new Dictionary<string, string>(model.ProviderProperties) { ["fkSupport"] = "true" }
                };
            models.Add(model);
        }
        return models;
    }

    private async Task<IReadOnlyList<Execution.SqlRow>> QueryAsync(
        string scriptName, IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, scriptName);
        return await _executor.QueryAsync(script, parameters, ct).ConfigureAwait(false);
    }

    // Switching database: for on-premises a USE keeps the same connection; index-list uses
    // DB_NAME() so the result carries the right database. USE is not parameterizable, so the
    // name is validated against sys.databases before being quoted.
    private async Task UseDatabaseAsync(string database, CancellationToken ct)
    {
        var sql = $"IF DB_ID(@db) IS NULL RAISERROR('unknown database', 16, 1); ELSE EXEC('USE ' + QUOTENAME(@db));";
        await _executor.ExecuteAsync(sql,
            new Dictionary<string, object?> { ["@db"] = database }, timeout: null, ct).ConfigureAwait(false);
    }
}
```

Design note: `USE` inside `EXEC` runs in its own batch scope and does not change the outer session database. The correct approach on a shared connection is to open the connection against the target database or set `SqlConnection.ChangeDatabase`. Replace `UseDatabaseAsync` with a `ChangeDatabase` call:
```csharp
    private Task UseDatabaseAsync(string database, CancellationToken ct)
    {
        // SqlClientExecutor exposes the connection's database switch; see Task 14 Step 2.
        return _executor.ChangeDatabaseAsync(database, ct);
    }
```

- [ ] **Step 2: Add `ChangeDatabaseAsync` to the executor**

Add to `ISqlExecutor`:
```csharp
    Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken);
```
Implement in `SqlClientExecutor`:
```csharp
    public async Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        // Validate against the catalog, then use the driver's own ChangeDatabase (no SQL injection surface).
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.databases WHERE name = @db;";
        cmd.Parameters.Add(new SqlParameter("@db", database));
        var exists = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (exists is null) throw new InvalidOperationException($"unknown database: {database}");
        await _connection.ChangeDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
    }
```
(Then delete the `EXEC USE` version of `UseDatabaseAsync` and keep the `ChangeDatabaseAsync` call.)

- [ ] **Step 3: Write the integration test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/GetIndexesTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class GetIndexesTests
{
    private readonly SqlServerContainerFixture _fx;
    public GetIndexesTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Lists_seeded_indexes_with_structure()
    {
        await using var provider = await _fx.ConnectAsync();
        var indexes = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);

        var unused = Assert.Single(indexes, i => i.Name == "IX_Orders_Unused");
        Assert.Equal(IndexType.NonclusteredRowstore, unused.Type);
        Assert.Equal(new[] { "OrderDate" }, unused.KeyColumns.Select(k => k.Name));
        Assert.Equal(new[] { "Total" }, unused.IncludedColumns);

        var pk = Assert.Single(indexes, i => i.Constraint == ConstraintKind.PrimaryKey);
        Assert.True(pk.IsUnique);
    }

    [RequiresDockerFact]
    public async Task Fk_supporting_index_is_flagged_in_provider_properties()
    {
        await using var provider = await _fx.ConnectAsync();
        var indexes = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);

        // The seed has no FK, so no index should be flagged; this asserts the property path is wired without throwing.
        Assert.All(indexes, i => Assert.False(i.ProviderProperties.ContainsKey("fkSupport")));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~GetIndexesTests"`
Expected: PASS if Docker available; SKIPPED otherwise.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/ tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/GetIndexesTests.cs
git commit -m "feat(provider): GetIndexesAsync with column and FK-support mapping"
```

---

### Task 15: Diagnostics (usage, hints, Query Store state)

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Diagnostics.cs` (partial)
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/DiagnosticsTests.cs`

**Interfaces:**
- Consumes: `QueryUsageMapper`, `HintMapper`, `QueryStoreStateMapper`, capability gating.
- Produces: `GetQueryUsageAsync`, `GetHintsAsync`, `GetQueryStoreStateAsync`. Query Store methods return empty / `NotSupported` when `Capabilities.SupportsQueryStore` is false, without touching the server.

- [ ] **Step 1: Write the implementation**

`src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Diagnostics.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public async Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);
        var parameters = new Dictionary<string, object?> { ["@IndexName"] = index.Index };

        var usage = new List<QueryUsage>();
        if (Capabilities.SupportsPlanCache)
        {
            var rows = await QueryAsync("index-used-by-queries", parameters, cancellationToken).ConfigureAwait(false);
            usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.PlanCache)));
        }
        // The database is already current here: read Query Store state without switching again.
        if (Capabilities.SupportsQueryStore
            && await ReadQueryStoreStateAsync(cancellationToken).ConfigureAwait(false) != QueryStoreState.Off)
        {
            var rows = await QueryAsync("index-used-by-queries-query-store", parameters, cancellationToken).ConfigureAwait(false);
            usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.QueryStore)));
        }
        return usage;
    }

    public async Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);
        var rows = await QueryAsync("index-hints-plancache",
            new Dictionary<string, object?> { ["@IndexName"] = index.Index }, cancellationToken).ConfigureAwait(false);
        return rows.Select(HintMapper.Map).ToList();
    }

    public async Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsQueryStore) return QueryStoreState.NotSupported;
        await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
        return await ReadQueryStoreStateAsync(cancellationToken).ConfigureAwait(false);
    }

    // Reads Query Store state assuming the target database is already current (no re-switch).
    private async Task<QueryStoreState> ReadQueryStoreStateAsync(CancellationToken cancellationToken)
    {
        var script = Core.Sql.SqlScriptLoader.Load(_scriptRoot, "querystore-state");
        var state = await _executor.ScalarAsync<int?>(script, null, cancellationToken).ConfigureAwait(false);
        return QueryStoreStateMapper.Map(state);
    }
}
```

The `index-hints-plancache.sql` `LIKE` predicate is a known imprecision (it can over- or under-match on the index name as a substring); a stricter query-plan-XML shredding version is a v1.x refinement, tracked in the self-review.

- [ ] **Step 2: Write the integration test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/DiagnosticsTests.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class DiagnosticsTests
{
    private readonly SqlServerContainerFixture _fx;
    public DiagnosticsTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Query_store_state_is_off_on_a_fresh_database()
    {
        await using var provider = await _fx.ConnectAsync();
        var state = await provider.GetQueryStoreStateAsync(_fx.Database, CancellationToken.None);
        Assert.Equal(QueryStoreState.Off, state);
    }

    [RequiresDockerFact]
    public async Task Query_usage_runs_without_error_and_returns_a_list()
    {
        await using var provider = await _fx.ConnectAsync();
        var usage = await provider.GetQueryUsageAsync(
            new IndexRef(_fx.Database, "dbo", "Orders", "IX_Orders_Unused"), CancellationToken.None);
        Assert.NotNull(usage);   // may be empty; the point is the plan-cache query executes
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~DiagnosticsTests"`
Expected: PASS if Docker available; SKIPPED otherwise.

- [ ] **Step 4: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Diagnostics.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/DiagnosticsTests.cs
git commit -m "feat(provider): diagnostics (usage, hints, Query Store state) with capability gating"
```

---

### Task 16: Actions (DROP, enable Query Store) and DI registration

**Files:**
- Create: `src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs` (partial), `ServiceCollectionExtensions.cs`
- Test: `tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ActionsTests.cs`

**Interfaces:**
- Consumes: the `querystore-enable` script, `Permissions.CanAlter` for the pre-check.
- Produces: `DropIndexAsync`, `EnableQueryStoreAsync`, and `services.AddSqlServerProvider(scriptRoot)`.

Note: `DropIndexAsync` takes an `IndexRef` (identity only), so it cannot and does not run Core's `SqlServerDdlGenerator`; eligibility is the caller's responsibility (Core `DeletionSafetyEvaluator`). `DROP INDEX` names an identifier, which SQL Server does not allow as a parameter, so this is the one place identifiers are concatenated rather than parameterized; the `Quote` helper (bracket-quoting with `]]` escaping) is the mitigation, exactly as in Core's DDL generator. The same applies to `QUOTENAME` in `querystore-enable.sql` and to `ChangeDatabaseAsync`, which validates the name against `sys.databases` before switching.

- [ ] **Step 1: Write the actions**

`src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs`:
```csharp
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public async Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!Permissions.CanAlter)
            throw new InvalidOperationException("DROP INDEX requires ALTER permission, which the current login lacks.");
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);
        // DROP INDEX <index> ON <schema>.<table>. Identifiers cannot be parameterized, so they are
        // bracket-quoted (]] escaped). The caller (Core DeletionSafetyEvaluator) has already gated eligibility.
        var sql = $"DROP INDEX {Quote(index.Index)} ON {Quote(index.Schema)}.{Quote(index.Table)};";
        await _executor.ExecuteAsync(sql, null, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnableQueryStoreAsync(
        string database, QueryStoreSettings settings, CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsQueryStore)
            throw new InvalidOperationException("Query Store is not supported on this server.");
        if (!Permissions.CanAlter)
            throw new InvalidOperationException("Enabling Query Store requires ALTER permission, which the current login lacks.");
        await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);

        var script = SqlScriptLoader.Load(_scriptRoot, "querystore-enable");
        var parameters = new Dictionary<string, object?>
        {
            ["@DatabaseName"] = database,
            ["@MaxStorageSizeMb"] = settings.MaxStorageSizeMb,
            ["@StaleQueryThresholdDays"] = settings.StaleQueryThresholdDays
        };
        await _executor.ScalarAsync<bool?>(script, parameters, cancellationToken).ConfigureAwait(false);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
```

`src/SmartIndexManager.Providers.SqlServer/ServiceCollectionExtensions.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerProvider(this IServiceCollection services, string scriptRoot)
    {
        services.AddSingleton<IIndexProviderFactory>(_ => new SqlServerIndexProviderFactory(scriptRoot));
        return services;
    }
}
```

- [ ] **Step 2: Write the integration test**

`tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ActionsTests.cs`:
```csharp
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ActionsTests
{
    private readonly SqlServerContainerFixture _fx;
    public ActionsTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Drop_index_removes_it_from_the_listing()
    {
        // Seed a disposable index so the shared fixture stays intact.
        await using (var conn = new SqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "IF INDEXPROPERTY(OBJECT_ID('dbo.Orders'),'IX_Tmp_Drop','IndexID') IS NULL CREATE INDEX IX_Tmp_Drop ON dbo.Orders(Total);";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var provider = await _fx.ConnectAsync();
        await provider.DropIndexAsync(
            new IndexRef(_fx.Database, "dbo", "Orders", "IX_Tmp_Drop"), TimeSpan.FromSeconds(30), CancellationToken.None);

        var remaining = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);
        Assert.DoesNotContain(remaining, i => i.Name == "IX_Tmp_Drop");
    }

    [RequiresDockerFact]
    public async Task Enable_query_store_moves_state_to_read_write()
    {
        await using var provider = await _fx.ConnectAsync();

        await provider.EnableQueryStoreAsync(_fx.Database, new QueryStoreSettings(), CancellationToken.None);
        var state = await provider.GetQueryStoreStateAsync(_fx.Database, CancellationToken.None);

        Assert.Equal(QueryStoreState.ReadWrite, state);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/SmartIndexManager.Providers.SqlServer.Tests --filter "FullyQualifiedName~ActionsTests"`
Expected: PASS if Docker available; SKIPPED otherwise.

- [ ] **Step 4: Run the whole solution**

Run: `dotnet test`
Expected: every unit test passes; integration tests pass under Docker, otherwise skipped.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Providers.SqlServer/SqlServerIndexProvider.Actions.cs src/SmartIndexManager.Providers.SqlServer/ServiceCollectionExtensions.cs tests/SmartIndexManager.Providers.SqlServer.Tests/Integration/ActionsTests.cs
git commit -m "feat(provider): DROP index, enable Query Store, DI registration"
```

---

## Self-Review

Spec coverage against `docs/specs/2026-07-20-smartindexmanager-design.md`:

- Section 3 (IIndexProvider, capabilities object, connection-time detection): Tasks 1, 5, 13. `ProviderCapabilities` is filled once by `CapabilityResolver` and every call gates on it (diagnostics Task 15).
- Section 4 (external SQL contract, file inventory, named parameters, DDL generated by Core not SQL): Tasks 2, 10, 11. Every shipped script has a `-- sim:` header and is column-validated at load (`SqlScriptLoader`) and at execution (`SqlClientExecutor.ValidateColumns`). The recreate DDL stays in Core's `SqlServerDdlGenerator`; the provider only issues `DROP`.
- Section 6 (hard exclusions, DROP flow): the provider's `DropIndexAsync` is the execution leaf; eligibility and the transactional backup/verify/audit flow live in Core plus the App orchestration. `DropIndexAsync` quotes identifiers and honours the timeout.
- Section 11 (connections, three auth modes, no password storage, permission degradation, Query Store enable with the spec's default parameters): Tasks 1, 4, 6, 13, 16. Password is a transient parameter to `ConnectAsync`; `querystore-enable.sql` applies `READ_WRITE`/`AUTO`/`AUTO` with `MAX_STORAGE_SIZE_MB` and `STALE_QUERY_THRESHOLD_DAYS` defaults 1000 and 30.

Deferred to the App plan (not gaps): the dry-run report assembly, the deletion basket and transactional orchestration (regenerate DDL, write backup, verify, drop, audit), the grid and detail UX, per-index hint filtering, and copying `sql/sqlserver/**` to the app output directory.

Requirements carried forward:

- App plan: `GetHintsAsync(IndexRef)` filters server-side by index name for the dry-run's per-index risk flag. The `LIKE` match on the index name can over- or under-match on a substring; the stricter query-plan-XML version is the v1.x refinement below.
- User validation (not CI): the Azure SQL Database SQL variants. `server-info.sql` and `replication-ag-check.sql` are marked `azure=unsupported` and need `azure=only` companions returning `UptimeDays = NULL` and replication/AG = false; the on-premises path is what Testcontainers verifies.
- Provider hardening: `index-hints-plancache.sql` uses `LIKE` matching on plan/hint text, which can over-match (a substring of the index name) or under-match; a stricter XML-shredding version of the plan-cache query is a v1.x refinement.

Placeholder scan: no TBD/TODO. Two design notes intentionally document a deliberate simplification (the `USE`/`ChangeDatabase` correction in Task 14 and the `GetHintsAsync` scoping in Task 15); both ship with working code, not placeholders.

Type consistency: `SqlRow.Get<T>` is the single row-reading primitive used by every mapper; `ISqlExecutor` gains `ChangeDatabaseAsync` in Task 14 and is implemented in `SqlClientExecutor`; `IndexRef`, `ServerInfo`, `PermissionReport`, `QueryStoreState`, `QueryStoreSettings` are the Core types consumed unchanged across Tasks 13-16. `ProviderProperties["fkSupport"]` is the agreed channel for FK-support (string map, matching the Core MVP decision).

One correction folded in: Task 14's first `UseDatabaseAsync` via `EXEC('USE ...')` is wrong on a shared connection (batch-scoped), so the task replaces it with `SqlConnection.ChangeDatabaseAsync` after validating the name against `sys.databases`. The plan states this inline rather than shipping the broken version.
