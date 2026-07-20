# SmartIndexManager App (read-only browser) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `SmartIndexManager.App` as a cross-platform Avalonia desktop tool that connects to a SQL Server instance, lists every index across the selected databases in one grid with computed score / redundancy / safety badges, and shows a per-index detail panel; it captures a usage snapshot on connect and reports permission degradation. This is plan 3a of the App: read-only. The corrective actions (deletion basket, dry-run, backup/restore, Query Store activation, audit, exports) are plan 3b.

**Architecture:** Three layers already exist: `SmartIndexManager.Core` (model, redundancy, scoring, DDL, safety, snapshots, audit; no UI, no SqlClient) and `SmartIndexManager.Providers.SqlServer` (the SQL Server `IIndexProvider`). This plan adds `SmartIndexManager.App`, an Avalonia 11 + CommunityToolkit.Mvvm front end that consumes Core through its interfaces and the provider through `IIndexProviderFactory`. All feature logic lives in App services and ViewModels that are unit-tested with a fake `IIndexProvider`, never by instantiating the UI (per the spec's testability constraint); XAML views are thin bindings, assembled and smoke-verified.

**Tech Stack:** C# / .NET 10, Avalonia 11 (Desktop, Fluent theme, `Avalonia.Controls.DataGrid`), CommunityToolkit.Mvvm 8+, Microsoft.Extensions.DependencyInjection, `.resx` resources (English), xUnit.

## Global Constraints

Copied from `docs/specs/2026-07-20-smartindexmanager-design.md`; every task inherits these.

- Target framework `net10.0`; nullable reference types and implicit usings enabled.
- The App references `SmartIndexManager.Core` and `SmartIndexManager.Providers.SqlServer`; it consumes Core only through its public types/interfaces. It never re-implements Core logic (redundancy, scoring, DDL, safety, snapshots) — it calls Core.
- Every feature must be reachable from an xUnit test without instantiating the UI. Feature logic lives in ViewModels and services; `.axaml` views hold only bindings.
- SQL passwords are never persisted, in any form (not clear, not encrypted, not a keychain). A password exists only as a transient value passed to `IIndexProviderFactory.ConnectAsync` and is never stored on a persisted object or a long-lived field.
- Windows Integrated auth is offered only where it can work (Windows, or Linux/macOS configured for Kerberos); elsewhere the option is disabled with an explanation. SQL and Entra ID interactive stay available everywhere.
- UI text is English and lives in `.resx` resources from the start (no hard-coded user-facing strings in views or ViewModels).
- Long operations (connect, collect) are async (`Task`/`Task<T>`) and honour a `CancellationToken`; they run off the UI thread with progress and a cancel affordance.
- Only nonclustered rowstore non-unique indexes are deletion-eligible; hard-excluded indexes (PK, UNIQUE constraint, UNIQUE index, clustered, columnstore, XML, spatial, fulltext, indexed-view, system-table, hypothetical, disabled) carry a "not deletable" badge with the reason. This plan only displays eligibility; it performs no deletion.
- Capability decisions read `ProviderCapabilities`, never a raw version check.
- CommunityToolkit.Mvvm version 8 minimum; Microsoft.Data.SqlClient 6 minimum; Avalonia 11 — latest stable compatible with .NET 10 chosen at implementation time.

## File Structure

```
src/
  SmartIndexManager.App/
    SmartIndexManager.App.csproj
    Program.cs                         (entry point, BuildAvaloniaApp)
    App.axaml  App.axaml.cs            (Application, FluentTheme, DI container build, main window)
    Composition/
      ServiceRegistration.cs           (IServiceCollection wiring for App services + provider)
    Services/
      IAppPaths.cs  AppPaths.cs        (platform config dir, snapshot root, backup root default, sql script root)
      ConnectionProfile.cs             (persisted named connection, NO password)
      IConnectionStore.cs  ConnectionStore.cs   (JSON load/save of profiles in config dir)
      IAuthAvailability.cs  AuthAvailability.cs  (which AuthMode values are usable on this OS)
      IPasswordPrompt.cs               (abstraction over asking the user for a password)
      IIndexLoadService.cs  IndexLoadService.cs  (connect -> get indexes -> snapshot -> score/redundancy/safety)
      LoadResult.cs                    (the assembled result the grid/detail bind to)
      IThemeService.cs  ThemeService.cs (light/dark toggle + persistence)
    Localization/
      Strings.resx  Strings.Designer.cs
      ILocalizer.cs  ResxLocalizer.cs
    ViewModels/
      ViewModelBase.cs
      IndexRowViewModel.cs             (one grid row: IndexModel + score/redundancy/safety/badges)
      IndexGridViewModel.cs            (rows, filter/sort/group, selection)
      IndexDetailViewModel.cs          (DDL, usage, queries, hints, redundancy, score factors, oldest snapshot)
      PermissionStatusViewModel.cs     (status-bar degradation report)
      ConnectionEditorViewModel.cs     (add/edit one profile)
      ConnectionManagerViewModel.cs    (list, connect, database selection)
      MainWindowViewModel.cs           (top-level: connection state + grid + detail + permissions + async load)
    Views/
      MainWindow.axaml  MainWindow.axaml.cs
      IndexGridView.axaml  IndexGridView.axaml.cs
      IndexDetailView.axaml  IndexDetailView.axaml.cs
      ConnectionManagerView.axaml  ConnectionManagerView.axaml.cs
      PermissionStatusBar.axaml  PermissionStatusBar.axaml.cs
tests/
  SmartIndexManager.App.Tests/
    SmartIndexManager.App.Tests.csproj
    Fakes/
      FakeIndexProvider.cs  FakeIndexProviderFactory.cs
      IndexModelFactory.cs             (builders for IndexModel test data)
    Services/   ViewModels/            (unit tests mirroring the two folders above)
```

---

### Task 1: App and test project scaffold, DI composition root

**Files:**
- Create: `src/SmartIndexManager.App/SmartIndexManager.App.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Composition/ServiceRegistration.cs`, `ViewModels/ViewModelBase.cs`
- Create: `tests/SmartIndexManager.App.Tests/SmartIndexManager.App.Tests.csproj`
- Test: `tests/SmartIndexManager.App.Tests/Composition/ServiceRegistrationTests.cs`

**Interfaces:**
- Consumes: `SmartIndexManager.Providers.SqlServer.ServiceCollectionExtensions.AddSqlServerProvider(scriptRoot)` (existing), `IIndexProviderFactory` (Core).
- Produces:
  - `ViewModelBase : ObservableObject` (base for all ViewModels)
  - `ServiceRegistration.AddAppServices(this IServiceCollection services, string scriptRoot) -> IServiceCollection` (registers `IIndexProviderFactory` via the provider extension and the App services added in later tasks)

- [ ] **Step 1: Scaffold the projects**

Run:
```bash
cd /home/rudi/Sources/Repos/SmartIndexManager
dotnet new avalonia.mvvm -n SmartIndexManager.App -o src/SmartIndexManager.App -f net10.0 || dotnet new avalonia.app -n SmartIndexManager.App -o src/SmartIndexManager.App -f net10.0
dotnet new xunit -n SmartIndexManager.App.Tests -o tests/SmartIndexManager.App.Tests -f net10.0
dotnet sln add src/SmartIndexManager.App/SmartIndexManager.App.csproj tests/SmartIndexManager.App.Tests/SmartIndexManager.App.Tests.csproj
dotnet add src/SmartIndexManager.App reference src/SmartIndexManager.Core src/SmartIndexManager.Providers.SqlServer
dotnet add tests/SmartIndexManager.App.Tests reference src/SmartIndexManager.App
dotnet add src/SmartIndexManager.App package Avalonia
dotnet add src/SmartIndexManager.App package Avalonia.Desktop
dotnet add src/SmartIndexManager.App package Avalonia.Themes.Fluent
dotnet add src/SmartIndexManager.App package Avalonia.Fonts.Inter
dotnet add src/SmartIndexManager.App package Avalonia.Controls.DataGrid
dotnet add src/SmartIndexManager.App package CommunityToolkit.Mvvm
dotnet add src/SmartIndexManager.App package Microsoft.Extensions.DependencyInjection
```
If the `avalonia.*` templates are not installed, first run `dotnet new install Avalonia.Templates`. Delete any template sample files the later tasks replace (`ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml`, `App.axaml`/`App.axaml.cs`, `ViewLocator.cs`, `tests/.../UnitTest1.cs`) so this plan's versions are authoritative. Resolve the latest stable Avalonia 11.x and CommunityToolkit.Mvvm 8.x compatible with .NET 10.

- [ ] **Step 2: Enable nullable and implicit usings on both new csproj**

Ensure each `<PropertyGroup>` contains:
```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
```
The App csproj is an `OutputType=WinExe`/`Exe` Avalonia app; keep the template's `<OutputType>` and `BuiltInComInteropSupport`/`ApplicationManifest` lines if present.

- [ ] **Step 3: Write `ViewModelBase`**

`src/SmartIndexManager.App/ViewModels/ViewModelBase.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartIndexManager.App.ViewModels;

public abstract class ViewModelBase : ObservableObject;
```

- [ ] **Step 4: Write the failing test for DI registration**

`tests/SmartIndexManager.App.Tests/Composition/ServiceRegistrationTests.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Composition;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddAppServices_registers_the_provider_factory()
    {
        var provider = new ServiceCollection()
            .AddAppServices(scriptRoot: "/tmp/sql/sqlserver")
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IIndexProviderFactory>());
    }
}
```
Note: the provider test project may already declare a global `Xunit` using. Whether the App test project does is unknown at scaffold time; if `dotnet new xunit` added `<Using Include="Xunit" />`, omit explicit `using Xunit;` in every test file this plan creates; otherwise add `using Xunit;`. Determine this once (inspect the generated csproj) and apply consistently.

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ServiceRegistrationTests"`
Expected: FAIL, `AddAppServices` does not exist.

- [ ] **Step 6: Write `ServiceRegistration`**

`src/SmartIndexManager.App/Composition/ServiceRegistration.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.Providers.SqlServer;

namespace SmartIndexManager.App.Composition;

public static class ServiceRegistration
{
    // Registers the SQL Server provider factory and (in later tasks) the App services.
    // Later tasks add more registrations to this method.
    public static IServiceCollection AddAppServices(this IServiceCollection services, string scriptRoot)
    {
        services.AddSqlServerProvider(scriptRoot);
        return services;
    }
}
```

- [ ] **Step 7: Write `App.axaml` / `App.axaml.cs` and `Program.cs`**

`src/SmartIndexManager.App/App.axaml`:
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="SmartIndexManager.App.App"
             RequestedThemeVariant="Default">
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

`src/SmartIndexManager.App/App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The main window and its DI-resolved ViewModel are wired in Task 13.
        base.OnFrameworkInitializationCompleted();
    }
}
```

`src/SmartIndexManager.App/Program.cs`:
```csharp
using Avalonia;

namespace SmartIndexManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 8: Run the test to verify it passes and the app builds**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ServiceRegistrationTests"` — Expected: PASS.
Run: `dotnet build src/SmartIndexManager.App` — Expected: 0 errors, 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/SmartIndexManager.App/ tests/SmartIndexManager.App.Tests/ SmartIndexManager.sln
git commit -m "feat(app): scaffold Avalonia app, test project and DI composition root"
```

---

### Task 2: AppPaths service (platform directories)

**Files:**
- Create: `src/SmartIndexManager.App/Services/IAppPaths.cs`, `AppPaths.cs`
- Test: `tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs`

**Interfaces:**
- Produces:
  - `IAppPaths` with `string ConfigDir { get; }`, `string SnapshotRoot { get; }`, `string DefaultBackupRoot { get; }`, `string SqlScriptRoot { get; }`
  - `AppPaths(string configDir, string documentsDir, string sqlScriptRoot) : IAppPaths` (explicit dirs so tests inject temp paths; a static `AppPaths.Default()` resolves the real per-platform locations)

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs`:
```csharp
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void Derives_snapshot_and_backup_roots_from_the_base_dirs()
    {
        var paths = new AppPaths(configDir: "/cfg", documentsDir: "/docs", sqlScriptRoot: "/app/sql/sqlserver");

        Assert.Equal("/cfg", paths.ConfigDir);
        Assert.Equal(Path.Combine("/cfg", "snapshots"), paths.SnapshotRoot);
        Assert.Equal(Path.Combine("/docs", "SmartIndexManager"), paths.DefaultBackupRoot);
        Assert.Equal("/app/sql/sqlserver", paths.SqlScriptRoot);
    }

    [Fact]
    public void Default_resolves_a_config_dir_under_the_app_name()
    {
        var paths = AppPaths.Default();
        Assert.Contains("SmartIndexManager", paths.ConfigDir);
        Assert.EndsWith(Path.Combine("sql", "sqlserver"), paths.SqlScriptRoot);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AppPathsTests"`
Expected: FAIL, `AppPaths` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/Services/IAppPaths.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public interface IAppPaths
{
    string ConfigDir { get; }          // per-platform app config directory
    string SnapshotRoot { get; }       // <ConfigDir>/snapshots
    string DefaultBackupRoot { get; }  // <Documents>/SmartIndexManager
    string SqlScriptRoot { get; }      // directory holding sql/sqlserver/*.sql
}
```

`src/SmartIndexManager.App/Services/AppPaths.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public sealed class AppPaths : IAppPaths
{
    public string ConfigDir { get; }
    public string SnapshotRoot { get; }
    public string DefaultBackupRoot { get; }
    public string SqlScriptRoot { get; }

    public AppPaths(string configDir, string documentsDir, string sqlScriptRoot)
    {
        ConfigDir = configDir;
        SnapshotRoot = Path.Combine(configDir, "snapshots");
        DefaultBackupRoot = Path.Combine(documentsDir, "SmartIndexManager");
        SqlScriptRoot = sqlScriptRoot;
    }

    // Real per-platform locations. ApplicationData maps to %APPDATA% on Windows,
    // $XDG_CONFIG_HOME (or ~/.config) on Linux, ~/Library/Application Support on macOS.
    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var config = Path.Combine(appData, "SmartIndexManager");
        var sqlRoot = Path.Combine(AppContext.BaseDirectory, "sql", "sqlserver");
        return new AppPaths(config, documents, sqlRoot);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AppPathsTests"` — Expected: PASS.

- [ ] **Step 5: Register and commit**

Add to `ServiceRegistration.AddAppServices` (before `return services;`):
```csharp
        services.AddSingleton<IAppPaths>(_ => AppPaths.Default());
```
Then:
```bash
git add src/SmartIndexManager.App/Services/IAppPaths.cs src/SmartIndexManager.App/Services/AppPaths.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/AppPathsTests.cs
git commit -m "feat(app): platform path service"
```

---

### Task 3: Localization (English .resx + ILocalizer)

**Files:**
- Create: `src/SmartIndexManager.App/Localization/Strings.resx`, `Strings.Designer.cs`, `ILocalizer.cs`, `ResxLocalizer.cs`
- Modify: `src/SmartIndexManager.App/SmartIndexManager.App.csproj` (embed the resx)
- Test: `tests/SmartIndexManager.App.Tests/Localization/ResxLocalizerTests.cs`

**Interfaces:**
- Produces: `ILocalizer` with `string this[string key] { get; }` and `string Get(string key)`; `ResxLocalizer : ILocalizer` reading `Strings` resources.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Localization/ResxLocalizerTests.cs`:
```csharp
using SmartIndexManager.App.Localization;

namespace SmartIndexManager.App.Tests.Localization;

public class ResxLocalizerTests
{
    [Fact]
    public void Returns_the_english_string_for_a_known_key()
    {
        ILocalizer loc = new ResxLocalizer();
        Assert.Equal("Connect", loc["Action_Connect"]);
    }

    [Fact]
    public void Unknown_key_returns_the_key_in_brackets_so_gaps_are_visible()
    {
        ILocalizer loc = new ResxLocalizer();
        Assert.Equal("[Missing_Key]", loc["Missing_Key"]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ResxLocalizerTests"`
Expected: FAIL, `ILocalizer`/`ResxLocalizer` do not exist.

- [ ] **Step 3: Create the resx and access classes**

`src/SmartIndexManager.App/Localization/Strings.resx` — a standard .NET resx. Include at least these entries (add more as views need them; keys are stable identifiers, values are English):
```
Action_Connect            = Connect
Action_Cancel             = Cancel
Action_Refresh            = Refresh
Grid_Column_Database      = Database
Grid_Column_Schema        = Schema
Grid_Column_Table         = Table
Grid_Column_Index         = Index
Grid_Column_Type          = Type
Grid_Column_Score         = Score
Badge_NotDeletable        = Not deletable
Badge_ForeignKey          = FK
Badge_Hint                = Hint
Badge_Redundant           = Redundant
Permission_UsageUnavailable = Usage statistics unavailable (missing VIEW SERVER STATE / VIEW DATABASE STATE)
Permission_ReadOnly       = Read-only: ALTER permission missing
Detail_OldestSnapshot     = Observed since {0}
```
Set `<EmbeddedResource Update="Localization\Strings.resx"><Generator>ResXFileCodeGenerator</Generator><LastGenNamespace>SmartIndexManager.App.Localization</LastGenNamespace></EmbeddedResource>` in the csproj (or let the SDK generate `Strings.Designer.cs`). The generated `Strings` class exposes a `ResourceManager` and typed properties.

`src/SmartIndexManager.App/Localization/ILocalizer.cs`:
```csharp
namespace SmartIndexManager.App.Localization;

public interface ILocalizer
{
    string this[string key] { get; }
    string Get(string key);
}
```

`src/SmartIndexManager.App/Localization/ResxLocalizer.cs`:
```csharp
using System.Globalization;

namespace SmartIndexManager.App.Localization;

public sealed class ResxLocalizer : ILocalizer
{
    public string this[string key] => Get(key);

    // Missing keys return "[key]" so untranslated gaps are visible in the UI, never a crash.
    public string Get(string key)
        => Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ResxLocalizerTests"` — Expected: PASS.

- [ ] **Step 5: Register and commit**

Add to `AddAppServices`: `services.AddSingleton<ILocalizer, ResxLocalizer>();`
```bash
git add src/SmartIndexManager.App/Localization/ src/SmartIndexManager.App/SmartIndexManager.App.csproj src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Localization/
git commit -m "feat(app): English resx localization with visible-gap fallback"
```

---

### Task 4: Connection profiles and store (no password)

**Files:**
- Create: `src/SmartIndexManager.App/Services/ConnectionProfile.cs`, `IConnectionStore.cs`, `ConnectionStore.cs`
- Test: `tests/SmartIndexManager.App.Tests/Services/ConnectionStoreTests.cs`

**Interfaces:**
- Consumes: `IAppPaths` (Task 2), `AuthMode` (Core).
- Produces:
  - `ConnectionProfile` record: `Name`, `Server`, `Port` (`int?`), `Encrypt` (`bool`, default true), `TrustServerCertificate` (`bool`), `Auth` (`AuthMode`), `Login` (`string?`). NO password field.
  - `IConnectionStore` with `IReadOnlyList<ConnectionProfile> Load()` and `void Save(IReadOnlyList<ConnectionProfile> profiles)`.
  - `ConnectionStore(IAppPaths paths) : IConnectionStore` persisting JSON at `<ConfigDir>/connections.json`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Services/ConnectionStoreTests.cs`:
```csharp
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class ConnectionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-conn-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConnectionStore Store()
        => new(new AppPaths(configDir: _dir, documentsDir: _dir, sqlScriptRoot: _dir));

    [Fact]
    public void Save_then_Load_roundtrips_profiles()
    {
        var profiles = new[]
        {
            new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            new ConnectionProfile { Name = "local", Server = ".", Port = 14330, Auth = AuthMode.WindowsIntegrated }
        };

        Store().Save(profiles);
        var loaded = Store().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("PROD01", loaded[0].Server);
        Assert.Equal(AuthMode.WindowsIntegrated, loaded[1].Auth);
    }

    [Fact]
    public void Load_returns_empty_when_no_file_exists()
        => Assert.Empty(Store().Load());

    [Fact]
    public void Persisted_json_never_contains_a_password_property()
    {
        Store().Save(new[] { new ConnectionProfile { Name = "p", Server = "s", Auth = AuthMode.SqlLogin, Login = "u" } });
        var json = File.ReadAllText(Path.Combine(_dir, "connections.json"));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionStoreTests"`
Expected: FAIL, `ConnectionProfile`/`ConnectionStore` do not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/Services/ConnectionProfile.cs`:
```csharp
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

// A persisted named connection. NEVER contains a password (spec security constraint).
public sealed record ConnectionProfile
{
    public required string Name { get; init; }
    public required string Server { get; init; }
    public int? Port { get; init; }
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public required AuthMode Auth { get; init; }
    public string? Login { get; init; }   // used for SqlLogin, optional for Entra
}
```

`src/SmartIndexManager.App/Services/IConnectionStore.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public interface IConnectionStore
{
    IReadOnlyList<ConnectionProfile> Load();
    void Save(IReadOnlyList<ConnectionProfile> profiles);
}
```

`src/SmartIndexManager.App/Services/ConnectionStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.App.Services;

public sealed class ConnectionStore : IConnectionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public ConnectionStore(IAppPaths paths)
        => _path = Path.Combine(paths.ConfigDir, "connections.json");

    public IReadOnlyList<ConnectionProfile> Load()
    {
        if (!File.Exists(_path)) return [];
        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, Options) ?? [];
    }

    public void Save(IReadOnlyList<ConnectionProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(profiles, Options));
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionStoreTests"` — Expected: PASS.

- [ ] **Step 5: Register and commit**

Add to `AddAppServices`: `services.AddSingleton<IConnectionStore, ConnectionStore>();`
```bash
git add src/SmartIndexManager.App/Services/ConnectionProfile.cs src/SmartIndexManager.App/Services/IConnectionStore.cs src/SmartIndexManager.App/Services/ConnectionStore.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/ConnectionStoreTests.cs
git commit -m "feat(app): named connection store (no password persisted)"
```

---

### Task 5: Auth-mode availability per platform

**Files:**
- Create: `src/SmartIndexManager.App/Services/IAuthAvailability.cs`, `AuthAvailability.cs`
- Test: `tests/SmartIndexManager.App.Tests/Services/AuthAvailabilityTests.cs`

**Interfaces:**
- Consumes: `AuthMode` (Core).
- Produces:
  - `IAuthAvailability` with `bool IsAvailable(AuthMode mode)` and `string? UnavailableReason(AuthMode mode)`.
  - `AuthAvailability(bool isWindows, bool kerberosConfigured) : IAuthAvailability`; a static `AuthAvailability.ForCurrentOs()` detects the OS. Windows Integrated is available when `isWindows` OR `kerberosConfigured`; SQL and Entra are always available.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Services/AuthAvailabilityTests.cs`:
```csharp
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class AuthAvailabilityTests
{
    [Fact]
    public void Windows_integrated_is_available_on_windows()
        => Assert.True(new AuthAvailability(isWindows: true, kerberosConfigured: false).IsAvailable(AuthMode.WindowsIntegrated));

    [Fact]
    public void Windows_integrated_is_unavailable_on_non_windows_without_kerberos()
    {
        var a = new AuthAvailability(isWindows: false, kerberosConfigured: false);
        Assert.False(a.IsAvailable(AuthMode.WindowsIntegrated));
        Assert.NotNull(a.UnavailableReason(AuthMode.WindowsIntegrated));
    }

    [Fact]
    public void Windows_integrated_becomes_available_with_kerberos()
        => Assert.True(new AuthAvailability(isWindows: false, kerberosConfigured: true).IsAvailable(AuthMode.WindowsIntegrated));

    [Theory]
    [InlineData(AuthMode.SqlLogin)]
    [InlineData(AuthMode.EntraIdInteractive)]
    public void Sql_and_entra_are_always_available(AuthMode mode)
        => Assert.True(new AuthAvailability(isWindows: false, kerberosConfigured: false).IsAvailable(mode));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AuthAvailabilityTests"`
Expected: FAIL, type does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/Services/IAuthAvailability.cs`:
```csharp
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public interface IAuthAvailability
{
    bool IsAvailable(AuthMode mode);
    string? UnavailableReason(AuthMode mode);   // non-null only when IsAvailable is false
}
```

`src/SmartIndexManager.App/Services/AuthAvailability.cs`:
```csharp
using System.Runtime.InteropServices;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public sealed class AuthAvailability : IAuthAvailability
{
    private readonly bool _isWindows;
    private readonly bool _kerberosConfigured;

    public AuthAvailability(bool isWindows, bool kerberosConfigured)
    {
        _isWindows = isWindows;
        _kerberosConfigured = kerberosConfigured;
    }

    // Heuristic for Kerberos on non-Windows: a krb5 config is present. Refined later if needed.
    public static AuthAvailability ForCurrentOs()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool kerberos = !isWindows &&
            (File.Exists("/etc/krb5.conf") || Environment.GetEnvironmentVariable("KRB5_CONFIG") is not null);
        return new AuthAvailability(isWindows, kerberos);
    }

    public bool IsAvailable(AuthMode mode) => mode switch
    {
        AuthMode.WindowsIntegrated => _isWindows || _kerberosConfigured,
        _ => true
    };

    public string? UnavailableReason(AuthMode mode)
        => IsAvailable(mode)
            ? null
            : "Windows Integrated authentication needs Windows or a Kerberos-configured host. Use SQL or Entra ID instead.";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~AuthAvailabilityTests"` — Expected: PASS.

- [ ] **Step 5: Register and commit**

Add to `AddAppServices`: `services.AddSingleton<IAuthAvailability>(_ => AuthAvailability.ForCurrentOs());`
```bash
git add src/SmartIndexManager.App/Services/IAuthAvailability.cs src/SmartIndexManager.App/Services/AuthAvailability.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/AuthAvailabilityTests.cs
git commit -m "feat(app): per-platform auth-mode availability"
```

---

### Task 6: Test fakes and IndexModel builders

**Files:**
- Create: `tests/SmartIndexManager.App.Tests/Fakes/IndexModelFactory.cs`, `FakeIndexProvider.cs`, `FakeIndexProviderFactory.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexType`, `IndexUsageStats`, `IndexSizeInfo`, `IndexOptions` (Core.Model); `IIndexProvider`, `IIndexProviderFactory`, `ServerInfo`, `ProviderCapabilities`, `PermissionReport`, `QueryUsage`, `IndexHint`, `IndexRef`, `QueryStoreState`, `QueryStoreSettings`, `ConnectionRequest` (Core.Provider).
- Produces: test-only fakes every later ViewModel/service test uses.

This task ships no production code; it is the shared test harness. It ends with a compiling test project (the fakes referenced by a trivial assertion), so a reviewer can gate it.

- [ ] **Step 1: Write the IndexModel builder**

`tests/SmartIndexManager.App.Tests/Fakes/IndexModelFactory.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.App.Tests.Fakes;

// Builders for IndexModel test data. Defaults describe a plain deletable nonclustered rowstore index.
public static class IndexModelFactory
{
    public static IndexModel Nonclustered(
        string db = "Sales", string schema = "dbo", string table = "Orders", string name = "IX_Orders_Customer",
        IReadOnlyList<string>? keyColumns = null, IReadOnlyList<string>? includedColumns = null,
        bool isUnique = false, ConstraintKind constraint = ConstraintKind.None,
        long seeks = 0, long scans = 0, long lookups = 0, long updates = 0, DateTime? lastRead = null)
        => new()
        {
            Database = db, Schema = schema, Table = table, Name = name,
            Type = IndexType.NonclusteredRowstore,
            KeyColumns = (keyColumns ?? ["CustomerId"]).Select(c => new IndexColumn(c, SortDirection.Ascending)).ToList(),
            IncludedColumns = includedColumns ?? [],
            IsUnique = isUnique,
            Constraint = constraint,
            Usage = new IndexUsageStats(seeks, scans, lookups, updates, lastRead, null),
            Size = new IndexSizeInfo(Pages: 100, Rows: 1000, SizeMb: 8.0),
            Options = new IndexOptions()
        };

    public static IndexModel PrimaryKey(string name = "PK_Orders")
        => Nonclustered(name: name, isUnique: true, constraint: ConstraintKind.PrimaryKey) with
           { Type = IndexType.ClusteredRowstore };
}
```
Confirm the exact `IndexUsageStats` constructor parameter order against `src/SmartIndexManager.Core/Model/IndexUsageStats.cs` before writing (it is a positional record: seeks, scans, lookups, updates, lastRead, lastWrite); adjust the call if the order differs.

- [ ] **Step 2: Write the fake provider and factory**

`tests/SmartIndexManager.App.Tests/Fakes/FakeIndexProvider.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public sealed class FakeIndexProvider : IIndexProvider
{
    public required ServerInfo ServerInfo { get; init; }
    public required ProviderCapabilities Capabilities { get; init; }
    public required PermissionReport Permissions { get; init; }

    public IReadOnlyList<IndexModel> Indexes { get; init; } = [];
    public IReadOnlyList<QueryUsage> Usage { get; init; } = [];
    public IReadOnlyList<IndexHint> Hints { get; init; } = [];
    public QueryStoreState QueryStore { get; init; } = QueryStoreState.Off;
    public bool Disposed { get; private set; }

    public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct = default)
        => Task.FromResult(Indexes);

    public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
        => Task.FromResult(Usage);

    public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
        => Task.FromResult(Hints);

    public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
        => Task.FromResult(QueryStore);

    public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
```

`tests/SmartIndexManager.App.Tests/Fakes/FakeIndexProviderFactory.cs`:
```csharp
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public sealed class FakeIndexProviderFactory : IIndexProviderFactory
{
    private readonly IIndexProvider _provider;
    public ConnectionRequest? LastRequest { get; private set; }
    public string? LastPassword { get; private set; }

    public FakeIndexProviderFactory(IIndexProvider provider) => _provider = provider;

    public Task<IIndexProvider> ConnectAsync(ConnectionRequest request, string? password, CancellationToken ct = default)
    {
        LastRequest = request;
        LastPassword = password;
        return Task.FromResult(_provider);
    }
}
```

- [ ] **Step 3: Write a compile/smoke assertion**

`tests/SmartIndexManager.App.Tests/Fakes/FakesSmokeTests.cs`:
```csharp
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public class FakesSmokeTests
{
    [Fact]
    public async Task Fake_provider_returns_its_canned_indexes()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var factory = new FakeIndexProviderFactory(provider);
        var connected = await factory.ConnectAsync(new ConnectionRequest { Server = "s", Auth = AuthMode.SqlLogin, Login = "u" }, "pw");
        var indexes = await connected.GetIndexesAsync(["Sales"]);
        Assert.Single(indexes);
        Assert.Equal("pw", factory.LastPassword);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~FakesSmokeTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/SmartIndexManager.App.Tests/Fakes/
git commit -m "test(app): fake provider, fake factory and IndexModel builders"
```

---

### Task 7: IndexRowViewModel (score, redundancy, safety, badges)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelTests.cs`

**Interfaces:**
- Consumes: `IndexModel` (Core.Model), `ConfidenceScore`/`ScoreColor` (Core.Scoring), `SafetyAssessment`/`DeletionEligibility` (Core.Safety), `RedundancyFinding` (Core.Redundancy).
- Produces: `IndexRowViewModel(IndexModel index, ConfidenceScore? score, SafetyAssessment safety, bool isRedundant, bool isReferencedByHint)` exposing display columns and badge flags. Pure view-model wrapper; no I/O.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexRowViewModelTests
{
    [Fact]
    public void Exposes_identity_columns_and_key_summary()
    {
        var index = IndexModelFactory.Nonclustered(keyColumns: ["CustomerId", "OrderDate"], includedColumns: ["Total"]);
        var row = new IndexRowViewModel(index,
            score: new ConfidenceScore(90, ScoreColor.Green, []),
            safety: new SafetyAssessment(DeletionEligibility.Deletable, []),
            isRedundant: false, isReferencedByHint: false);

        Assert.Equal("Sales", row.Database);
        Assert.Equal("Orders", row.Table);
        Assert.Equal("CustomerId, OrderDate", row.KeySummary);
        Assert.Equal("Total", row.IncludeSummary);
        Assert.Equal(90, row.Score);
        Assert.False(row.NotDeletable);
    }

    [Fact]
    public void Not_deletable_index_sets_the_badge_and_hides_the_score()
    {
        var index = IndexModelFactory.PrimaryKey();
        var row = new IndexRowViewModel(index,
            score: null,
            safety: new SafetyAssessment(DeletionEligibility.NotDeletable, []),
            isRedundant: false, isReferencedByHint: false);

        Assert.True(row.NotDeletable);
        Assert.Null(row.Score);
    }

    [Fact]
    public void Badges_reflect_redundancy_and_hint_flags()
    {
        var row = new IndexRowViewModel(IndexModelFactory.Nonclustered(),
            score: new ConfidenceScore(70, ScoreColor.Orange, []),
            safety: new SafetyAssessment(DeletionEligibility.Deletable, []),
            isRedundant: true, isReferencedByHint: true);

        Assert.True(row.Redundant);
        Assert.True(row.ReferencedByHint);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexRowViewModelTests"`
Expected: FAIL, `IndexRowViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.ViewModels;

public sealed class IndexRowViewModel : ViewModelBase
{
    public IndexModel Index { get; }
    public ConfidenceScore? ScoreDetail { get; }

    public IndexRowViewModel(
        IndexModel index, ConfidenceScore? score, SafetyAssessment safety,
        bool isRedundant, bool isReferencedByHint)
    {
        Index = index;
        ScoreDetail = score;
        NotDeletable = safety.Eligibility == DeletionEligibility.NotDeletable;
        Redundant = isRedundant;
        ReferencedByHint = isReferencedByHint;
        SupportsForeignKey = index.ProviderProperties.ContainsKey("fkSupport");
    }

    public string Database => Index.Database;
    public string Schema => Index.Schema;
    public string Table => Index.Table;
    public string Name => Index.Name;
    public IndexType Type => Index.Type;
    public string KeySummary => string.Join(", ", Index.KeyColumns.Select(c => c.Name));
    public string IncludeSummary => string.Join(", ", Index.IncludedColumns);
    public bool IsUnique => Index.IsUnique;
    public double SizeMb => Index.Size.SizeMb;
    public long Seeks => Index.Usage.Seeks;
    public long Scans => Index.Usage.Scans;
    public long Lookups => Index.Usage.Lookups;
    public long Updates => Index.Usage.Updates;
    public DateTime? LastRead => Index.Usage.LastRead;

    public int? Score => ScoreDetail?.Value;
    public ScoreColor? ScoreColor => ScoreDetail?.Color;

    public bool NotDeletable { get; }
    public bool Redundant { get; }
    public bool ReferencedByHint { get; }
    public bool SupportsForeignKey { get; }
}
```
Confirm the `IndexUsageStats` property names (`Seeks`, `Scans`, `Lookups`, `Updates`, `LastRead`) and `IndexSizeInfo.SizeMb` against Core before writing; adjust if they differ.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexRowViewModelTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelTests.cs
git commit -m "feat(app): index row view-model with score, safety and badges"
```

---

### Task 8: IndexLoadService (connect, collect, snapshot, compute)

**Files:**
- Create: `src/SmartIndexManager.App/Services/LoadResult.cs`, `IIndexLoadService.cs`, `IndexLoadService.cs`
- Test: `tests/SmartIndexManager.App.Tests/Services/IndexLoadServiceTests.cs`

**Interfaces:**
- Consumes: `IIndexProviderFactory`, `IIndexProvider`, `ConnectionRequest`, `PermissionReport`, `ProviderCapabilities`, `ServerInfo` (Core.Provider); `RedundancyAnalyzer`, `RedundancyFinding`, `RedundancyRule` (Core.Redundancy); `ConfidenceScorer`, `ConfidenceScore`, `ScoreInputs` (Core.Scoring); `DeletionSafetyEvaluator`, `SafetyAssessment`, `SafetyInputs` (Core.Safety); `SnapshotStore`, `UsageSnapshot`, `SnapshotIndexUsage` (Core.Persistence); `IAppPaths`, `ConnectionProfile`, `IAuthAvailability`.
- Produces:
  - `LoadResult` record: `ServerInfo Server`, `ProviderCapabilities Capabilities`, `PermissionReport Permissions`, `IReadOnlyList<IndexRowViewModel> Rows`.
  - `IIndexLoadService.LoadAsync(ConnectionProfile profile, string? password, IReadOnlyList<string> databases, CancellationToken) -> Task<LoadResult>`.
  - `IndexLoadService(IIndexProviderFactory factory, IAppPaths paths) : IIndexLoadService`.

This is the orchestration heart. It connects, lists indexes, writes a usage snapshot, runs Core redundancy + scoring + safety, and produces row view-models. It never touches the UI and is fully tested with the fakes.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/Services/IndexLoadServiceTests.cs`:
```csharp
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class IndexLoadServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-load-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeIndexProvider Provider(params SmartIndexManager.Core.Model.IndexModel[] indexes) => new()
    {
        ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
        Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
        Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
        Indexes = indexes
    };

    private IndexLoadService Service(FakeIndexProvider provider)
        => new(new FakeIndexProviderFactory(provider), new AppPaths(_dir, _dir, _dir));

    private static ConnectionProfile Profile() => new() { Name = "p", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" };

    [Fact]
    public async Task Builds_a_row_per_index_with_scores_and_safety()
    {
        var provider = Provider(
            IndexModelFactory.Nonclustered(name: "IX_Used", seeks: 5000, lastRead: DateTime.UtcNow),
            IndexModelFactory.PrimaryKey());

        var result = await Service(provider).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("PROD01", result.Server.ServerName);
        var pk = result.Rows.Single(r => r.Name == "PK_Orders");
        Assert.True(pk.NotDeletable);            // hard exclusion
        var used = result.Rows.Single(r => r.Name == "IX_Used");
        Assert.NotNull(used.Score);              // eligible -> scored
    }

    [Fact]
    public async Task Flags_R1_redundant_duplicates()
    {
        var a = IndexModelFactory.Nonclustered(name: "IX_A", keyColumns: ["CustomerId"]);
        var b = IndexModelFactory.Nonclustered(name: "IX_B", keyColumns: ["CustomerId"]);
        var result = await Service(Provider(a, b)).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);

        Assert.All(result.Rows, r => Assert.True(r.Redundant));
    }

    [Fact]
    public async Task Writes_a_usage_snapshot_under_the_snapshot_root()
    {
        await Service(Provider(IndexModelFactory.Nonclustered())).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);
        var snapshotDir = Path.Combine(_dir, "snapshots");
        Assert.True(Directory.Exists(snapshotDir));
        Assert.NotEmpty(Directory.GetFiles(snapshotDir, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Passes_the_password_to_connect_and_never_retains_it()
    {
        var factory = new FakeIndexProviderFactory(Provider(IndexModelFactory.Nonclustered()));
        var service = new IndexLoadService(factory, new AppPaths(_dir, _dir, _dir));
        await service.LoadAsync(Profile(), "s3cret", ["Sales"], CancellationToken.None);
        Assert.Equal("s3cret", factory.LastPassword);   // used once for connect
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexLoadServiceTests"`
Expected: FAIL, `IndexLoadService` does not exist.

- [ ] **Step 3: Write `LoadResult` and the interface**

`src/SmartIndexManager.App/Services/LoadResult.cs`:
```csharp
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public sealed record LoadResult(
    ServerInfo Server,
    ProviderCapabilities Capabilities,
    PermissionReport Permissions,
    IReadOnlyList<IndexRowViewModel> Rows);
```

`src/SmartIndexManager.App/Services/IIndexLoadService.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public interface IIndexLoadService
{
    Task<LoadResult> LoadAsync(
        ConnectionProfile profile, string? password,
        IReadOnlyList<string> databases, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write `IndexLoadService`**

`src/SmartIndexManager.App/Services/IndexLoadService.cs`:
```csharp
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Redundancy;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Services;

public sealed class IndexLoadService : IIndexLoadService
{
    private readonly IIndexProviderFactory _factory;
    private readonly IAppPaths _paths;
    private readonly ConfidenceScorer _scorer = new();

    public IndexLoadService(IIndexProviderFactory factory, IAppPaths paths)
    {
        _factory = factory;
        _paths = paths;
    }

    public async Task<LoadResult> LoadAsync(
        ConnectionProfile profile, string? password,
        IReadOnlyList<string> databases, CancellationToken cancellationToken)
    {
        var request = ToRequest(profile);
        await using var provider = await _factory.ConnectAsync(request, password, cancellationToken).ConfigureAwait(false);

        var indexes = await provider.GetIndexesAsync(databases, cancellationToken).ConfigureAwait(false);

        WriteSnapshot(provider.ServerInfo, databases, indexes);

        var redundant = new HashSet<(string, string, string, string)>();
        foreach (var f in RedundancyAnalyzer.Analyze(indexes))
        {
            redundant.Add(Key(f.Redundant));
            redundant.Add(Key(f.CoveredBy));
        }

        int uptime = Math.Max(0, provider.ServerInfo.UptimeDays);
        var rows = new List<IndexRowViewModel>(indexes.Count);
        foreach (var index in indexes)
        {
            bool isRedundant = redundant.Contains(Key(index));
            bool fkSupport = index.ProviderProperties.ContainsKey("fkSupport");

            var safety = DeletionSafetyEvaluator.Evaluate(new SafetyInputs
            {
                Index = index,
                Ddl = SqlServerDdlGenerator.Generate(index),
                SupportsForeignKey = fkSupport,
                InstanceUptimeDays = uptime
            });

            ConfidenceScore? score = safety.Eligibility == DeletionEligibility.Deletable
                ? _scorer.Score(new ScoreInputs
                {
                    Index = index,
                    InstanceUptimeDays = uptime,
                    IsRedundant = isRedundant,
                    SupportsForeignKey = fkSupport,
                    NowUtc = DateTime.UtcNow
                })
                : null;

            rows.Add(new IndexRowViewModel(index, score, safety, isRedundant, isReferencedByHint: false));
        }

        return new LoadResult(provider.ServerInfo, provider.Capabilities, provider.Permissions, rows);
    }

    private void WriteSnapshot(ServerInfo server, IReadOnlyList<string> databases, IReadOnlyList<IndexModel> indexes)
    {
        foreach (var database in databases)
        {
            var forDb = indexes.Where(i => string.Equals(i.Database, database, StringComparison.OrdinalIgnoreCase))
                .Select(i => new SnapshotIndexUsage
                {
                    Schema = i.Schema, Table = i.Table, Index = i.Name,
                    Seeks = i.Usage.Seeks, Scans = i.Usage.Scans, Lookups = i.Usage.Lookups,
                    Updates = i.Usage.Updates, LastRead = i.Usage.LastRead
                })
                .ToList();
            var snapshot = new UsageSnapshot
            {
                CapturedUtc = DateTime.UtcNow,
                Server = server.ServerName,
                Database = database,
                InstanceUptimeDays = server.UptimeDays,
                Indexes = forDb
            };
            SnapshotStore.Write(_paths.SnapshotRoot, snapshot);
        }
    }

    private static (string, string, string, string) Key(IndexModel i) => (i.Database, i.Schema, i.Table, i.Name);

    private static ConnectionRequest ToRequest(ConnectionProfile p) => new()
    {
        Server = p.Server, Port = p.Port, Auth = p.Auth, Login = p.Login,
        Encrypt = p.Encrypt, TrustServerCertificate = p.TrustServerCertificate
    };
}
```
These Core shapes are verified as of this plan (bind to them exactly): `RedundancyFinding(IndexModel Redundant, IndexModel CoveredBy, RedundancyRule Rule)`; `ScoreInputs { Index, InstanceUptimeDays, SupportsForeignKey, ReferencedByHint, IsRedundant, NowUtc }` (Index/InstanceUptimeDays/NowUtc are `required`); `SafetyInputs { Index, Ddl (required DdlResult), SupportsForeignKey, ReferencedByHint, DatabaseInReplicationOrAg, InstanceUptimeDays (required), UptimeReliabilityThresholdDays=30 }` — hence `Ddl` is filled from `SqlServerDdlGenerator.Generate(index)`; `SnapshotIndexUsage { Schema, Table, Index (all required), Seeks, Scans, Lookups, Updates, LastRead }` (no `LastWrite`); `UsageSnapshot { Server, Database, CapturedUtc (required), InstanceUptimeDays, Indexes, SchemaVersion (defaulted) }`; `SnapshotStore.Write(string rootDir, UsageSnapshot) -> string`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexLoadServiceTests"` — Expected: PASS.

- [ ] **Step 6: Register and commit**

Add to `AddAppServices`: `services.AddSingleton<IIndexLoadService, IndexLoadService>();`
```bash
git add src/SmartIndexManager.App/Services/LoadResult.cs src/SmartIndexManager.App/Services/IIndexLoadService.cs src/SmartIndexManager.App/Services/IndexLoadService.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/IndexLoadServiceTests.cs
git commit -m "feat(app): index load service (connect, snapshot, score, redundancy, safety)"
```

---

### Task 9: IndexGridViewModel (filter, sort, group, selection)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs`

**Interfaces:**
- Consumes: `IndexRowViewModel` (Task 7).
- Produces:
  - `IndexGridViewModel` with `void SetRows(IReadOnlyList<IndexRowViewModel> rows)`, an observable `FilterText` (`[ObservableProperty]`), `DataGridCollectionView View { get; }` (filtered/sortable/groupable, bound by the DataGrid), `IndexRowViewModel? SelectedRow` (`[ObservableProperty]`), and `int VisibleCount { get; }`.
- The `DataGridCollectionView` type lives in `Avalonia.Collections` (from the `Avalonia.Controls.DataGrid` package) and works headlessly in tests — no UI instantiation needed.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexGridViewModelTests
{
    private static IndexRowViewModel Row(string db, string table, string name)
        => new(IndexModelFactory.Nonclustered(db: db, table: table, name: name),
               new ConfidenceScore(50, ScoreColor.Orange, []),
               new SafetyAssessment(DeletionEligibility.Deletable, []),
               isRedundant: false, isReferencedByHint: false);

    [Fact]
    public void SetRows_populates_the_view()
    {
        var vm = new IndexGridViewModel();
        vm.SetRows([Row("Sales", "Orders", "IX_A"), Row("HR", "Staff", "IX_B")]);
        Assert.Equal(2, vm.VisibleCount);
    }

    [Fact]
    public void FilterText_narrows_the_view_by_substring_across_columns()
    {
        var vm = new IndexGridViewModel();
        vm.SetRows([Row("Sales", "Orders", "IX_Orders_Customer"), Row("HR", "Staff", "IX_Staff_Name")]);

        vm.FilterText = "orders";
        Assert.Equal(1, vm.VisibleCount);

        vm.FilterText = "";
        Assert.Equal(2, vm.VisibleCount);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexGridViewModelTests"`
Expected: FAIL, `IndexGridViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs`:
```csharp
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class IndexGridViewModel : ViewModelBase
{
    private readonly List<IndexRowViewModel> _all = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private IndexRowViewModel? _selectedRow;

    public DataGridCollectionView View { get; }

    public IndexGridViewModel()
    {
        View = new DataGridCollectionView(_all) { Filter = Matches };
    }

    public int VisibleCount => View.Cast<object>().Count();

    public void SetRows(IReadOnlyList<IndexRowViewModel> rows)
    {
        _all.Clear();
        _all.AddRange(rows);
        View.Refresh();
    }

    partial void OnFilterTextChanged(string value) => View.Refresh();

    private bool Matches(object item)
    {
        if (FilterText is not { Length: > 0 }) return true;
        var r = (IndexRowViewModel)item;
        return Contains(r.Database) || Contains(r.Schema) || Contains(r.Table) || Contains(r.Name);

        bool Contains(string s) => s.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }
}
```
Grouping and column sorting are configured on the same `DataGridCollectionView` (via `GroupDescriptions` / `SortDescriptions`) and driven by the DataGrid in Task 13; the VM already owns the view so those are added declaratively in XAML without further VM code.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexGridViewModelTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs
git commit -m "feat(app): index grid view-model with filtering"
```

---

### Task 10: IndexDetailViewModel (DDL, usage, queries, hints, score, snapshot)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs`

**Interfaces:**
- Consumes: `IIndexProvider`, `IndexRef`, `QueryUsage`, `IndexHint` (Core.Provider); `SqlServerDdlGenerator`, `DdlResult`/`DdlSuccess`/`DdlNotBackupable` (Core.Ddl); `ConfidenceScore`, `ScoreFactor` (Core.Scoring); `SnapshotStore.OldestCaptureUtc` (Core.Persistence); `IndexRowViewModel` (Task 7); `IAppPaths`, `ILocalizer`.
- Produces: `IndexDetailViewModel(IIndexProvider provider, IAppPaths paths, ILocalizer loc)` with `Task ShowAsync(IndexRowViewModel row, CancellationToken)` populating `Ddl`, `Queries`, `Hints`, `ScoreFactors`, `OldestSnapshotText`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexDetailViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-detail-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ShowAsync_fills_ddl_queries_hints_and_score_factors()
    {
        var index = IndexModelFactory.Nonclustered(name: "IX_Detail");
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Usage = [new QueryUsage("SELECT ...", 42, DateTime.UtcNow, UsageSource.PlanCache)],
            Hints = [new IndexHint("dbo.GetOrders", "query hint")]
        };
        var row = new IndexRowViewModel(index,
            new ConfidenceScore(88, ScoreColor.Green, [new ScoreFactor("no-reads", "0 reads since instance start")]),
            new SafetyAssessment(DeletionEligibility.Deletable, []), false, false);

        var vm = new IndexDetailViewModel(provider, new AppPaths(_dir, _dir, _dir), new ResxLocalizer());
        await vm.ShowAsync(row, CancellationToken.None);

        Assert.Contains("IX_Detail", vm.Ddl);
        Assert.Single(vm.Queries);
        Assert.Single(vm.Hints);
        Assert.Contains(vm.ScoreFactors, f => f.Name == "no-reads");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexDetailViewModelTests"`
Expected: FAIL, `IndexDetailViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class IndexDetailViewModel : ViewModelBase
{
    private readonly IIndexProvider _provider;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    [ObservableProperty] private string _ddl = "";
    [ObservableProperty] private string _oldestSnapshotText = "";

    public ObservableCollection<QueryUsage> Queries { get; } = [];
    public ObservableCollection<IndexHint> Hints { get; } = [];
    public ObservableCollection<ScoreFactor> ScoreFactors { get; } = [];

    public IndexDetailViewModel(IIndexProvider provider, IAppPaths paths, ILocalizer loc)
    {
        _provider = provider;
        _paths = paths;
        _loc = loc;
    }

    public async Task ShowAsync(IndexRowViewModel row, CancellationToken cancellationToken)
    {
        var index = row.Index;

        Ddl = SqlServerDdlGenerator.Generate(index) switch
        {
            DdlSuccess s => s.Sql,
            DdlNotBackupable n => $"-- {n.Reason}",
            _ => ""
        };

        var reference = IndexRef.Of(index);
        Queries.Clear();
        foreach (var q in await _provider.GetQueryUsageAsync(reference, cancellationToken).ConfigureAwait(false))
            Queries.Add(q);

        Hints.Clear();
        foreach (var h in await _provider.GetHintsAsync(reference, cancellationToken).ConfigureAwait(false))
            Hints.Add(h);

        ScoreFactors.Clear();
        foreach (var f in row.ScoreDetail?.Factors ?? [])
            ScoreFactors.Add(f);

        var oldest = SnapshotStore.OldestCaptureUtc(_paths.SnapshotRoot, _provider.ServerInfo.ServerName, index.Database);
        OldestSnapshotText = oldest is DateTime d
            ? string.Format(_loc["Detail_OldestSnapshot"], d.ToString("yyyy-MM-dd"))
            : "";
    }
}
```
Verified Core shapes: `SnapshotStore.OldestCaptureUtc(string rootDir, string server, string database) -> DateTime?` (server is the instance name that the snapshot was written under, i.e. `ServerInfo.ServerName`, NOT the database); `SqlServerDdlGenerator.Generate(IndexModel) -> DdlResult` with `DdlSuccess(string Sql)` / `DdlNotBackupable(string Reason)`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexDetailViewModelTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs
git commit -m "feat(app): index detail view-model (ddl, queries, hints, score, snapshot)"
```

---

### Task 11: PermissionStatusViewModel (degradation report)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/PermissionStatusViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/PermissionStatusViewModelTests.cs`

**Interfaces:**
- Consumes: `PermissionReport`, `ProviderCapabilities` (Core.Provider); `ILocalizer`.
- Produces: `PermissionStatusViewModel(ILocalizer loc)` with `void Update(PermissionReport permissions, ProviderCapabilities capabilities)`, exposing `bool UsageAvailable`, `bool ReadOnly`, `IReadOnlyList<string> Messages`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/PermissionStatusViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class PermissionStatusViewModelTests
{
    [Fact]
    public void Missing_view_state_marks_usage_unavailable_with_a_message()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = false },
                  new ProviderCapabilities { SupportsPlanCache = true });

        Assert.False(vm.UsageAvailable);
        Assert.Contains(vm.Messages, m => m.Contains("Usage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_alter_marks_read_only()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = true, CanAlter = false, CanAccessQueryStore = true },
                  new ProviderCapabilities());
        Assert.True(vm.ReadOnly);
    }

    [Fact]
    public void All_granted_reports_no_degradation()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
                  new ProviderCapabilities());
        Assert.True(vm.UsageAvailable);
        Assert.False(vm.ReadOnly);
        Assert.Empty(vm.Messages);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~PermissionStatusViewModelTests"`
Expected: FAIL, type does not exist.

- [ ] **Step 3: Write the implementation**

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

    public IReadOnlyList<string> Messages { get; private set; } = [];

    public PermissionStatusViewModel(ILocalizer loc) => _loc = loc;

    public void Update(PermissionReport permissions, ProviderCapabilities capabilities)
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

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~PermissionStatusViewModelTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/PermissionStatusViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/PermissionStatusViewModelTests.cs
git commit -m "feat(app): permission degradation status view-model"
```

---

### Task 12: Connection manager view-models

**Files:**
- Create: `src/SmartIndexManager.App/Services/IPasswordPrompt.cs`, `src/SmartIndexManager.App/ViewModels/ConnectionEditorViewModel.cs`, `ConnectionManagerViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/ConnectionEditorViewModelTests.cs`, `ConnectionManagerViewModelTests.cs`

**Interfaces:**
- Consumes: `IConnectionStore`, `IAuthAvailability`, `ConnectionProfile` (Tasks 4-5); `AuthMode` (Core.Provider).
- Produces:
  - `IPasswordPrompt` with `Task<string?> RequestAsync(string connectionName, CancellationToken)` (returns null when cancelled).
  - `ConnectionEditorViewModel(IAuthAvailability auth)` editing one profile: `[ObservableProperty]` fields Name/Server/Port/Login/Auth/Encrypt/TrustServerCertificate, `bool WindowsIntegratedAvailable`, `ConnectionProfile ToProfile()`, and a `static FromProfile`.
  - `ConnectionManagerViewModel(IConnectionStore store, IAuthAvailability auth)` with `ObservableCollection<ConnectionProfile> Profiles`, `ConnectionProfile? Selected`, an observable `DatabasesText` (comma-separated), `IReadOnlyList<string> SelectedDatabases { get; }`, `RelayCommand AddCommand`/`DeleteCommand`/`SaveCommand`, and an event/`ConnectionProfile?` surface the main window reads to trigger a connect.

- [ ] **Step 1: Write the failing tests**

`tests/SmartIndexManager.App.Tests/ViewModels/ConnectionEditorViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionEditorViewModelTests
{
    [Fact]
    public void ToProfile_captures_the_edited_fields()
    {
        var vm = new ConnectionEditorViewModel(new AuthAvailability(isWindows: true, kerberosConfigured: false))
        {
            Name = "prod", Server = "PROD01", Port = 14330, Auth = AuthMode.SqlLogin, Login = "app"
        };
        var profile = vm.ToProfile();
        Assert.Equal("PROD01", profile.Server);
        Assert.Equal(14330, profile.Port);
        Assert.Equal(AuthMode.SqlLogin, profile.Auth);
    }

    [Fact]
    public void Windows_integrated_availability_reflects_the_platform()
    {
        Assert.True(new ConnectionEditorViewModel(new AuthAvailability(true, false)).WindowsIntegratedAvailable);
        Assert.False(new ConnectionEditorViewModel(new AuthAvailability(false, false)).WindowsIntegratedAvailable);
    }
}
```

`tests/SmartIndexManager.App.Tests/ViewModels/ConnectionManagerViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionManagerViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-cm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConnectionManagerViewModel Vm()
        => new(new ConnectionStore(new AppPaths(_dir, _dir, _dir)), new AuthAvailability(true, false));

    [Fact]
    public void Loads_persisted_profiles_on_construction()
    {
        new ConnectionStore(new AppPaths(_dir, _dir, _dir)).Save(
            [new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" }]);

        Assert.Single(Vm().Profiles);
    }

    [Fact]
    public void SelectedDatabases_splits_and_trims_the_text()
    {
        var vm = Vm();
        vm.DatabasesText = " Sales , HR ,, Ops ";
        Assert.Equal(new[] { "Sales", "HR", "Ops" }, vm.SelectedDatabases);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionEditorViewModelTests|FullyQualifiedName~ConnectionManagerViewModelTests"`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Write `IPasswordPrompt`**

`src/SmartIndexManager.App/Services/IPasswordPrompt.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public interface IPasswordPrompt
{
    // Returns null if the user cancels. The result is used once for connect and never stored.
    Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write `ConnectionEditorViewModel`**

`src/SmartIndexManager.App/ViewModels/ConnectionEditorViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionEditorViewModel : ViewModelBase
{
    private readonly IAuthAvailability _auth;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private int? _port;
    [ObservableProperty] private string? _login;
    [ObservableProperty] private AuthMode _auth = AuthMode.SqlLogin;
    [ObservableProperty] private bool _encrypt = true;
    [ObservableProperty] private bool _trustServerCertificate;

    public ConnectionEditorViewModel(IAuthAvailability auth) => _auth = auth;

    public bool WindowsIntegratedAvailable => _auth.IsAvailable(AuthMode.WindowsIntegrated);
    public string? WindowsIntegratedReason => _auth.UnavailableReason(AuthMode.WindowsIntegrated);

    public ConnectionProfile ToProfile() => new()
    {
        Name = Name, Server = Server, Port = Port, Login = Login,
        Auth = Auth, Encrypt = Encrypt, TrustServerCertificate = TrustServerCertificate
    };

    public static ConnectionEditorViewModel FromProfile(ConnectionProfile p, IAuthAvailability auth) => new(auth)
    {
        Name = p.Name, Server = p.Server, Port = p.Port, Login = p.Login,
        Auth = p.Auth, Encrypt = p.Encrypt, TrustServerCertificate = p.TrustServerCertificate
    };
}
```
Note: the private auth field is named `_auth` and the observable is also `Auth`. To avoid the source generator colliding with the injected `IAuthAvailability _auth`, rename the injected field to `_authAvailability` in this file (constructor parameter stays `auth`); update `WindowsIntegratedAvailable`/`WindowsIntegratedReason`/`FromProfile` to use `_authAvailability`.

- [ ] **Step 5: Write `ConnectionManagerViewModel`**

`src/SmartIndexManager.App/ViewModels/ConnectionManagerViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionManagerViewModel : ViewModelBase
{
    private readonly IConnectionStore _store;
    private readonly IAuthAvailability _auth;

    [ObservableProperty] private ConnectionProfile? _selected;
    [ObservableProperty] private string _databasesText = "";

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ConnectionManagerViewModel(IConnectionStore store, IAuthAvailability auth)
    {
        _store = store;
        _auth = auth;
        foreach (var p in _store.Load()) Profiles.Add(p);
    }

    public IReadOnlyList<string> SelectedDatabases =>
        DatabasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public ConnectionEditorViewModel NewEditor() => new(_auth);

    public ConnectionEditorViewModel EditorFor(ConnectionProfile profile) => ConnectionEditorViewModel.FromProfile(profile, _auth);

    [RelayCommand]
    private void Delete(ConnectionProfile profile)
    {
        Profiles.Remove(profile);
        _store.Save(Profiles.ToList());
    }

    public void Upsert(ConnectionProfile profile)
    {
        var existing = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
        else Profiles.Add(profile);
        _store.Save(Profiles.ToList());
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionEditorViewModelTests|FullyQualifiedName~ConnectionManagerViewModelTests"` — Expected: PASS.

- [ ] **Step 7: Register and commit**

Add to `AddAppServices`: `services.AddTransient<ConnectionManagerViewModel>();`
```bash
git add src/SmartIndexManager.App/Services/IPasswordPrompt.cs src/SmartIndexManager.App/ViewModels/ConnectionEditorViewModel.cs src/SmartIndexManager.App/ViewModels/ConnectionManagerViewModel.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/ViewModels/ConnectionEditorViewModelTests.cs tests/SmartIndexManager.App.Tests/ViewModels/ConnectionManagerViewModelTests.cs
git commit -m "feat(app): connection manager and editor view-models"
```

---

### Task 13: MainWindowViewModel (async cancellable load orchestration)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `IIndexLoadService`, `IPasswordPrompt`, `ConnectionManagerViewModel`, `IndexGridViewModel`, `PermissionStatusViewModel`, `ILocalizer`.
- Produces: `MainWindowViewModel(IIndexLoadService load, IPasswordPrompt prompt, ConnectionManagerViewModel connections, IndexGridViewModel grid, PermissionStatusViewModel permissions, ILocalizer loc)` with an `AsyncRelayCommand ConnectCommand` (prompts for a password, loads, fills the grid + permission status; cancellable), `[ObservableProperty] bool IsBusy`, `[ObservableProperty] string? StatusMessage`, and `CancelCommand`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.App.Tests/ViewModels/MainWindowViewModelTests.cs`:
```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-main-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubPrompt(string? password) : IPasswordPrompt
    {
        public Task<string?> RequestAsync(string name, CancellationToken ct) => Task.FromResult(password);
    }

    private MainWindowViewModel Build(string? password)
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        store.Save([new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" }]);

        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        return new MainWindowViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(provider), paths),
            new StubPrompt(password), connections, new IndexGridViewModel(),
            new PermissionStatusViewModel(new ResxLocalizer()), new ResxLocalizer());
    }

    [Fact]
    public async Task Connect_loads_rows_into_the_grid_and_updates_permissions()
    {
        var vm = Build(password: "pw");
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Grid.VisibleCount);
        Assert.False(vm.Permissions.UsageAvailable);   // provider reported CanViewState=false
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Connect_does_nothing_when_the_password_prompt_is_cancelled()
    {
        var vm = Build(password: null);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Grid.VisibleCount);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: FAIL, `MainWindowViewModel` does not exist.

- [ ] **Step 3: Write the implementation**

`src/SmartIndexManager.App/ViewModels/MainWindowViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IIndexLoadService _load;
    private readonly IPasswordPrompt _prompt;
    private readonly ILocalizer _loc;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public ConnectionManagerViewModel Connections { get; }
    public IndexGridViewModel Grid { get; }
    public PermissionStatusViewModel Permissions { get; }

    public MainWindowViewModel(
        IIndexLoadService load, IPasswordPrompt prompt,
        ConnectionManagerViewModel connections, IndexGridViewModel grid,
        PermissionStatusViewModel permissions, ILocalizer loc)
    {
        _load = load;
        _prompt = prompt;
        _loc = loc;
        Connections = connections;
        Grid = grid;
        Permissions = permissions;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var profile = Connections.Selected;
        if (profile is null) return;

        var password = profile.Auth == AuthMode.SqlLogin
            ? await _prompt.RequestAsync(profile.Name, CancellationToken.None).ConfigureAwait(true)
            : null;
        if (profile.Auth == AuthMode.SqlLogin && password is null) return;   // cancelled

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = _loc["Action_Connect"];
        try
        {
            var result = await _load.LoadAsync(profile, password, Connections.SelectedDatabases, _cts.Token).ConfigureAwait(true);
            Grid.SetRows(result.Rows);
            Permissions.Update(result.Permissions, result.Capabilities);
            StatusMessage = result.Server.ServerName;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _loc["Action_Cancel"];
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
```
Note the `using SmartIndexManager.Core.Provider;` needed for `AuthMode`; add it. `ConfigureAwait(true)` keeps the continuation on the UI context for property updates in the real app; tests run without a sync context so they are unaffected.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"` — Expected: PASS.

- [ ] **Step 5: Register and commit**

Add to `AddAppServices`: register the child VMs and the main VM:
```csharp
        services.AddSingleton<IndexGridViewModel>();
        services.AddSingleton<PermissionStatusViewModel>();
        services.AddSingleton<MainWindowViewModel>();
```
```bash
git add src/SmartIndexManager.App/ViewModels/MainWindowViewModel.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(app): main window view-model with async cancellable connect"
```

---

### Task 14: Views, theme toggle, and DI-wired startup (smoke)

**Files:**
- Create: `src/SmartIndexManager.App/Services/IThemeService.cs`, `ThemeService.cs`, `Views/MainWindow.axaml(.cs)`, `Views/IndexGridView.axaml(.cs)`, `Views/IndexDetailView.axaml(.cs)`, `Views/ConnectionManagerView.axaml(.cs)`, `Views/PermissionStatusBar.axaml(.cs)`
- Modify: `src/SmartIndexManager.App/App.axaml.cs` (build the DI container, resolve `MainWindowViewModel`, show `MainWindow`)
- Test: `tests/SmartIndexManager.App.Tests/Services/ThemeServiceTests.cs`

**Interfaces:**
- Consumes: every ViewModel from Tasks 9-13, `IAppPaths`.
- Produces: `IThemeService` with `ThemeVariantKind Current { get; }` and `void Toggle()` persisting the choice to `<ConfigDir>/theme.txt`; the composed, runnable app.

Views hold only bindings; their logic already lives in tested ViewModels. Only `ThemeService` (pure persistence) carries a unit test; the views are verified by a clean build plus a manual smoke run.

- [ ] **Step 1: Write the failing ThemeService test**

`tests/SmartIndexManager.App.Tests/Services/ThemeServiceTests.cs`:
```csharp
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.Tests.Services;

public class ThemeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-theme-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ThemeService Service() => new(new AppPaths(_dir, _dir, _dir));

    [Fact]
    public void Toggle_flips_and_persists_the_variant()
    {
        var s = Service();
        var first = s.Current;
        s.Toggle();
        Assert.NotEqual(first, s.Current);
        Assert.Equal(s.Current, Service().Current);   // reloaded from disk
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ThemeServiceTests"`
Expected: FAIL, `ThemeService` does not exist.

- [ ] **Step 3: Write `IThemeService` / `ThemeService`**

`src/SmartIndexManager.App/Services/IThemeService.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public enum ThemeVariantKind { Light, Dark }

public interface IThemeService
{
    ThemeVariantKind Current { get; }
    void Toggle();
}
```

`src/SmartIndexManager.App/Services/ThemeService.cs`:
```csharp
namespace SmartIndexManager.App.Services;

public sealed class ThemeService : IThemeService
{
    private readonly string _path;
    public ThemeVariantKind Current { get; private set; }

    public ThemeService(IAppPaths paths)
    {
        _path = Path.Combine(paths.ConfigDir, "theme.txt");
        Current = File.Exists(_path) && File.ReadAllText(_path).Trim() == nameof(ThemeVariantKind.Dark)
            ? ThemeVariantKind.Dark : ThemeVariantKind.Light;
    }

    public void Toggle()
    {
        Current = Current == ThemeVariantKind.Light ? ThemeVariantKind.Dark : ThemeVariantKind.Light;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, Current.ToString());
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ThemeServiceTests"` — Expected: PASS. Register it: add `services.AddSingleton<IThemeService, ThemeService>();` to `AddAppServices`.

- [ ] **Step 5: Write the views**

`src/SmartIndexManager.App/Views/PermissionStatusBar.axaml` (a bound `UserControl`):
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.PermissionStatusBar"
             x:DataType="vm:PermissionStatusViewModel">
    <StackPanel Orientation="Horizontal" Spacing="12" Margin="8,4">
        <TextBlock Text="{Binding StatusForeground}" IsVisible="False" />
        <ItemsControl ItemsSource="{Binding Messages}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><StackPanel Orientation="Horizontal" Spacing="12" /></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><TextBlock Text="{Binding}" Foreground="#B5651D" /></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```
(Remove the stray `StatusForeground` line if not needed; it is a placeholder to show the binding root.)

`src/SmartIndexManager.App/Views/IndexGridView.axaml` (the multi-database grid, bound to `IndexGridViewModel`):
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.IndexGridView"
             x:DataType="vm:IndexGridViewModel">
    <DockPanel>
        <TextBox DockPanel.Dock="Top" Watermark="Filter" Text="{Binding FilterText}" Margin="4" />
        <DataGrid ItemsSource="{Binding View}" SelectedItem="{Binding SelectedRow}"
                  IsReadOnly="True" CanUserSortColumns="True" CanUserReorderColumns="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Database" Binding="{Binding Database}" />
                <DataGridTextColumn Header="Schema"   Binding="{Binding Schema}" />
                <DataGridTextColumn Header="Table"    Binding="{Binding Table}" />
                <DataGridTextColumn Header="Index"    Binding="{Binding Name}" />
                <DataGridTextColumn Header="Type"     Binding="{Binding Type}" />
                <DataGridTextColumn Header="Key"      Binding="{Binding KeySummary}" />
                <DataGridTextColumn Header="Includes" Binding="{Binding IncludeSummary}" />
                <DataGridTextColumn Header="Size MB"  Binding="{Binding SizeMb}" />
                <DataGridTextColumn Header="Seeks"    Binding="{Binding Seeks}" />
                <DataGridTextColumn Header="Scans"    Binding="{Binding Scans}" />
                <DataGridTextColumn Header="Updates"  Binding="{Binding Updates}" />
                <DataGridTextColumn Header="Score"    Binding="{Binding Score}" />
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

`src/SmartIndexManager.App/Views/IndexDetailView.axaml` (bound to `IndexDetailViewModel`):
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             x:Class="SmartIndexManager.App.Views.IndexDetailView"
             x:DataType="vm:IndexDetailViewModel">
    <ScrollViewer>
        <StackPanel Margin="8" Spacing="8">
            <TextBlock Text="DDL" FontWeight="SemiBold" />
            <TextBox Text="{Binding Ddl}" IsReadOnly="True" AcceptsReturn="True" TextWrapping="NoWrap" MinHeight="120" />
            <TextBlock Text="{Binding OldestSnapshotText}" />
            <TextBlock Text="Queries" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding Queries}">
                <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding QueryText}" /></DataTemplate></ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Text="Hints" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding Hints}">
                <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding Reference}" /></DataTemplate></ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock Text="Score factors" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding ScoreFactors}">
                <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding Description}" /></DataTemplate></ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`src/SmartIndexManager.App/Views/MainWindow.axaml` (top-level shell):
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
        xmlns:views="clr-namespace:SmartIndexManager.App.Views"
        x:Class="SmartIndexManager.App.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Width="1200" Height="800" Title="SmartIndexManager">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="8">
            <ComboBox ItemsSource="{Binding Connections.Profiles}" SelectedItem="{Binding Connections.Selected}"
                      DisplayMemberBinding="{Binding Name}" MinWidth="180" />
            <TextBox Watermark="Databases (comma separated)" Text="{Binding Connections.DatabasesText}" MinWidth="240" />
            <Button Content="Connect" Command="{Binding ConnectCommand}" IsEnabled="{Binding !IsBusy}" />
            <Button Content="Cancel" Command="{Binding CancelCommand}" IsVisible="{Binding IsBusy}" />
            <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy}" Width="120" />
        </StackPanel>
        <views:PermissionStatusBar DockPanel.Dock="Bottom" DataContext="{Binding Permissions}" />
        <Grid ColumnDefinitions="2*,Auto,1*">
            <views:IndexGridView Grid.Column="0" DataContext="{Binding Grid}" />
            <GridSplitter Grid.Column="1" Width="4" />
            <views:IndexDetailView Grid.Column="2" x:Name="Detail" />
        </Grid>
    </DockPanel>
</Window>
```
The detail panel is populated by the code-behind reacting to `Grid.SelectedRow` (a fresh `IndexDetailViewModel` per selection needs the current `IIndexProvider`; in this read-only plan wire it in `MainWindow.axaml.cs` by subscribing to the grid's `PropertyChanged` and calling `ShowAsync`). Keep code-behind minimal.

Each `*.axaml.cs` is the standard partial with `InitializeComponent()`. Example `MainWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
```
Write the analogous trivial code-behind for each `UserControl`.

- [ ] **Step 6: Wire DI-built startup in `App.axaml.cs`**

Replace `OnFrameworkInitializationCompleted`:
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var paths = AppPaths.Default();
        var services = new ServiceCollection()
            .AddAppServices(paths.SqlScriptRoot)
            // The real password prompt (a dialog) is added in Task 3b; for read-only browsing
            // register a console-less prompt that returns null so SqlLogin connects are gated in the UI.
            .AddSingleton<IPasswordPrompt, NullPasswordPrompt>()
            .BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var theme = services.GetRequiredService<IThemeService>();
            RequestedThemeVariant = theme.Current == ThemeVariantKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```
Add a temporary `NullPasswordPrompt : IPasswordPrompt` returning `Task.FromResult<string?>(null)` in `Services/` so read-only browsing with Windows/Entra auth works now; the real dialog-backed prompt arrives with plan 3b. (Windows/Entra profiles do not need a password, so the browser is usable today.)

- [ ] **Step 7: Build and smoke-run**

Run: `dotnet build src/SmartIndexManager.App` — Expected: 0 errors, 0 warnings.
Run (manual, where a display is available): `dotnet run --project src/SmartIndexManager.App` — Expected: the window opens, shows the connection bar, grid and detail panels, and the theme matches the persisted variant. In a headless environment, skip the run and rely on the build plus the ViewModel tests.

- [ ] **Step 8: Commit**

```bash
git add src/SmartIndexManager.App/Views/ src/SmartIndexManager.App/Services/IThemeService.cs src/SmartIndexManager.App/Services/ThemeService.cs src/SmartIndexManager.App/Services/NullPasswordPrompt.cs src/SmartIndexManager.App/App.axaml.cs src/SmartIndexManager.App/Composition/ServiceRegistration.cs tests/SmartIndexManager.App.Tests/Services/ThemeServiceTests.cs
git commit -m "feat(app): views, theme toggle and DI-wired read-only startup"
```

---

## Self-Review

Spec coverage for the read-only slice (section numbers from `docs/specs/2026-07-20-smartindexmanager-design.md`):

- Section 3 (three layers, App consumes Core via interfaces, everything callable without UI): Tasks 6-13 keep all logic in services/ViewModels tested with fakes; views are bindings only (Task 14).
- Section 11 (named connections without password, three auth modes, Windows greyed off-platform, permission degradation report): Tasks 4, 5, 11, 12. Password never persisted (Task 4 asserts it), only passed to connect (Task 8 asserts it).
- Section 10 (snapshot on connect, under the config dir): Task 8 writes the snapshot via Core `SnapshotStore`; the oldest-capture line shows in the detail panel (Task 10).
- Section 12 (single multi-database grid with filter/sort/group and badges; detail panel with DDL, usage, queries, hints, redundancy, score, oldest snapshot; async cancellable; light/dark; English .resx): Tasks 7-14. Sort/group are configured on the shared `DataGridCollectionView`.
- Section 7 and 5 (score and redundancy) are computed by Core and surfaced per row (Tasks 7-8); the App never re-implements them.
- Section 6 eligibility badges: surfaced read-only via Core `DeletionSafetyEvaluator` (Task 8); no deletion here.

Deferred to plan 3b (not gaps): the deletion basket and double confirmation, the dry-run report and its export, execute/script deletion with backup + manifest + audit, the restore screen, Query Store state display and activation, the audit-log viewer, CSV/JSON grid export, and the real password-prompt dialog (a `NullPasswordPrompt` placeholder unblocks Windows/Entra browsing now).

Cross-task type consistency: `IIndexLoadService.LoadAsync` returns `LoadResult` consumed by `MainWindowViewModel`; `IndexRowViewModel` is produced by Task 7 and consumed by Tasks 8, 9, 10, 13; `IndexGridViewModel.View` is the `DataGridCollectionView` bound in Task 14; `ConnectionProfile` (Task 4) maps to Core `ConnectionRequest` in Task 8. Each task that consumes a Core type carries a "confirm against Core before writing" note where the exact signature was not verified in the plan (`IndexUsageStats` order, `RedundancyFinding`/`ScoreInputs`/`SafetyInputs`/`UsageSnapshot` member names, `SnapshotStore.OldestCaptureUtc` parameter order) so the implementer binds to the real API rather than a guessed one.

Placeholder scan: no TBD/TODO. The `NullPasswordPrompt` and the `StatusForeground` XAML line are explicitly flagged as temporary/removable, not silent placeholders.
