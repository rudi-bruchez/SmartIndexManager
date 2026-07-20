# App GUI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `SmartIndexManager.App` as a Semi.Avalonia navigation shell with a polished, data-dense Browse experience (score pills, badges, titled detail cards, real empty/loading/error states), while keeping Core and the SQL Server provider untouched.

**Architecture:** `MainWindowViewModel` is decomposed into a `ShellViewModel` (navigation, theme, permissions), a `ConnectionSessionViewModel` (connect/disconnect/cancel, active provider, manage dialog), and a `BrowseViewModel` (grid, detail, detail-load serialization). `MainWindow` becomes the shell (left nav rail, top connection bar, content region, bottom permission status bar). A design-token `ResourceDictionary` with light/dark `ThemeDictionaries` replaces all hardcoded colors. Views resolve from ViewModels via `DataTemplate`s.

**Tech Stack:** .NET 10, Avalonia 11.3, Semi.Avalonia, Material.Icons.Avalonia, CommunityToolkit.Mvvm 8.4, xUnit, Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Target framework `net10.0`; `Nullable` and `ImplicitUsings` enabled; `LangVersion` latest.
- Scope is `src/SmartIndexManager.App` and `tests/SmartIndexManager.App.Tests` only. Do not modify `Core` or the SQL Server provider.
- All logic must be reachable from an xUnit test without instantiating the UI. Pure-XAML behavior is verified by `dotnet build` (compiled bindings, `x:DataType`) plus the manual visual checklist in Task 14.
- The score's colour classification stays in Core (`Scoring/ScoreColor`, values `Green`, `Orange`, `Red`). The UI never computes thresholds.
- The SQL Server provider holds one connection without MARS: two detail loads must never issue overlapping commands. The `DetailConcurrencyTests` invariant `MaxConcurrent == 1` must keep passing.
- UI text is English and lives in `Localization/Strings.resx`; add a `Strings.Designer.cs` accessor for every new key.
- Keep compiled bindings (`x:DataType`) on every view.
- Build command: `dotnet build SmartIndexManager.sln`. Test command: `dotnet test tests/SmartIndexManager.App.Tests`.

**Deviations from the spec (intent preserved):**
1. Score-pill colour uses Avalonia class-binding (`Classes.score-safe="{Binding IsScoreSafe}"`) plus `DynamicResource` brushes, not a `ScoreColorToClassConverter`. Same theme-reactivity; the tested unit is three bools on `IndexRowViewModel`. Numeric formatting uses binding `StringFormat`, so no numeric converter is needed either.
2. `MainWindowViewModel` is not renamed in place. The new VMs are added alongside it and a single cutover task (Task 13) switches wiring and deletes the old VM, so the build stays green at every task.
3. The four placeholder destinations share one `PlaceholderPageViewModel` type (four configured instances) instead of four empty classes (YAGNI). Each real feature later replaces its instance with a dedicated VM and `DataTemplate`.

---

## File Structure

New files:
- `src/SmartIndexManager.App/Resources/Tokens.axaml` — colour (light/dark), spacing, typography, radius tokens.
- `src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs` — grid + detail + detail serialization + `BrowseState`.
- `src/SmartIndexManager.App/ViewModels/ConnectionSessionViewModel.cs` — connect/disconnect/cancel + active provider + manage dialog.
- `src/SmartIndexManager.App/ViewModels/ShellViewModel.cs` — navigation, theme, permissions, orchestration.
- `src/SmartIndexManager.App/ViewModels/NavigationDestination.cs` — nav item record.
- `src/SmartIndexManager.App/ViewModels/PlaceholderPageViewModel.cs` — shared empty-destination VM.
- `src/SmartIndexManager.App/Services/IDialogService.cs` + `Services/AvaloniaDialogService.cs`.
- `src/SmartIndexManager.App/Views/BrowseView.axaml` (+ `.axaml.cs`).
- `src/SmartIndexManager.App/Views/EmptyStateView.axaml` (+ `.axaml.cs`).
- `src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml` (+ `.axaml.cs`).
- Tests under `tests/SmartIndexManager.App.Tests/ViewModels/` and `Services/`.

Modified files:
- `SmartIndexManager.App.csproj` (packages), `App.axaml` (styles + resources), `App.axaml.cs` (DI + shell), `Composition/ServiceRegistration.cs` (registrations), `Views/MainWindow.axaml` (+ `.axaml.cs`) (shell), `ViewModels/IndexRowViewModel.cs` (score bools), `ViewModels/IndexDetailView.axaml` (cards), `ViewModels/IndexGridView.axaml` (folded into `BrowseView`), `Localization/Strings.resx` (+ `Strings.Designer.cs`).

Deleted at cutover (Task 13): `ViewModels/MainWindowViewModel.cs`, `tests/.../ViewModels/MainWindowViewModelTests.cs` (superseded by Shell/Browse/ConnectionSession tests).

---

## Task 1: Add packages and wire the base theme

**Files:**
- Modify: `src/SmartIndexManager.App/SmartIndexManager.App.csproj`
- Modify: `src/SmartIndexManager.App/App.axaml`
- Create: `src/SmartIndexManager.App/Resources/Tokens.axaml` (stub, filled in Task 2)

**Interfaces:**
- Produces: a running app themed by Semi with Material icons available; `Tokens.axaml` merged into `App.Resources`.

- [ ] **Step 1: Add the package references**

In `SmartIndexManager.App.csproj`, inside the existing `<ItemGroup>` that holds `PackageReference`s, add:

```xml
<PackageReference Include="Semi.Avalonia" Version="11.2.1.9" />
<PackageReference Include="Material.Icons.Avalonia" Version="2.4.1" />
```

- [ ] **Step 2: Create the token dictionary stub**

Create `src/SmartIndexManager.App/Resources/Tokens.axaml`:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <x:Double x:Key="GridRowHeight">28</x:Double>
</ResourceDictionary>
```

- [ ] **Step 3: Wire Semi, Material icons, and tokens in App.axaml**

Replace the contents of `src/SmartIndexManager.App/App.axaml` with:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:semi="clr-namespace:Semi.Avalonia;assembly=Semi.Avalonia"
             xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="SmartIndexManager.App.App"
             RequestedThemeVariant="Default">
    <Application.Styles>
        <semi:SemiTheme Locale="en-US" />
        <mi:MaterialIconStyles />
    </Application.Styles>
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://SmartIndexManager.App/Resources/Tokens.axaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Note: the exact `Semi.Avalonia` include element and namespace can differ per package version. If Step 4 fails to build, open the installed `Semi.Avalonia` package README (or its `nupkg` `Themes` folder) and use the exact `<StyleInclude>`/`SemiTheme` line it documents. This is a third-party integration point verified by the build, not a design choice.

- [ ] **Step 4: Build to verify the theme wires up**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors. Package versions restore.

- [ ] **Step 5: Launch check (if a display is available)**

Run: `dotnet run --project src/SmartIndexManager.App`
Expected: the window opens with Semi styling (rounded controls, Semi's default surfaces). Close it. If no display, skip and rely on the build.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/SmartIndexManager.App.csproj src/SmartIndexManager.App/App.axaml src/SmartIndexManager.App/Resources/Tokens.axaml
git commit -m "feat(app): add Semi.Avalonia + Material.Icons and wire base theme"
```

---

## Task 2: Fill the design-token dictionary

**Files:**
- Modify: `src/SmartIndexManager.App/Resources/Tokens.axaml`

**Interfaces:**
- Produces resource keys consumed by later view tasks: brushes `ScoreSafeBrush`, `ScoreCautionBrush`, `ScoreRiskBrush`, `DangerBrush`, `WarnBrush`, `InfoBrush`, `AccentAltBrush`, `SurfaceCardBrush`; doubles `SpacingXs`..`SpacingXl`, `GridRowHeight`, `RadiusSm`, `RadiusMd`; `TextBlock` classes `caption`, `subtitle`, `title`, `code`.

- [ ] **Step 1: Write the full token dictionary**

Replace `src/SmartIndexManager.App/Resources/Tokens.axaml` with:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="ScoreSafeBrush" Color="#2E7D32" />
            <SolidColorBrush x:Key="ScoreCautionBrush" Color="#B26A00" />
            <SolidColorBrush x:Key="ScoreRiskBrush" Color="#C0392B" />
            <SolidColorBrush x:Key="DangerBrush" Color="#C0392B" />
            <SolidColorBrush x:Key="WarnBrush" Color="#B5651D" />
            <SolidColorBrush x:Key="InfoBrush" Color="#2E86C1" />
            <SolidColorBrush x:Key="AccentAltBrush" Color="#7D3C98" />
            <SolidColorBrush x:Key="SurfaceCardBrush" Color="#FFFFFF" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="ScoreSafeBrush" Color="#66BB6A" />
            <SolidColorBrush x:Key="ScoreCautionBrush" Color="#E0A030" />
            <SolidColorBrush x:Key="ScoreRiskBrush" Color="#E57368" />
            <SolidColorBrush x:Key="DangerBrush" Color="#E57368" />
            <SolidColorBrush x:Key="WarnBrush" Color="#CE9B63" />
            <SolidColorBrush x:Key="InfoBrush" Color="#5FA8DD" />
            <SolidColorBrush x:Key="AccentAltBrush" Color="#B07CC6" />
            <SolidColorBrush x:Key="SurfaceCardBrush" Color="#2A2A2E" />
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

    <x:Double x:Key="SpacingXs">2</x:Double>
    <x:Double x:Key="SpacingSm">4</x:Double>
    <x:Double x:Key="SpacingMd">8</x:Double>
    <x:Double x:Key="SpacingLg">12</x:Double>
    <x:Double x:Key="SpacingXl">16</x:Double>
    <x:Double x:Key="GridRowHeight">28</x:Double>
    <CornerRadius x:Key="RadiusSm">3</CornerRadius>
    <CornerRadius x:Key="RadiusMd">6</CornerRadius>

    <ControlTheme x:Key="{x:Type TextBlock}" TargetType="TextBlock" />

    <Style Selector="TextBlock.caption">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Opacity" Value="0.7" />
    </Style>
    <Style Selector="TextBlock.subtitle">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style Selector="TextBlock.title">
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>
    <Style Selector="TextBlock.code">
        <Setter Property="FontFamily" Value="Cascadia Code,Consolas,Menlo,monospace" />
        <Setter Property="FontSize" Value="12" />
    </Style>
</ResourceDictionary>
```

Note: `Style` elements inside a merged `ResourceDictionary` are applied because `App.axaml` merges this dictionary into `Application.Resources`; Avalonia hoists `Style` selectors from merged resource dictionaries. If the build rejects a top-level `Style` in a `ResourceDictionary`, move the four `Style` blocks into `App.axaml` under `<Application.Styles>` instead and keep only resources here. Verified by Step 2.

- [ ] **Step 2: Build to verify tokens parse**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SmartIndexManager.App/Resources/Tokens.axaml
git commit -m "feat(app): design-token dictionary with light/dark theme dictionaries"
```

---

## Task 3: Expose score-colour bools on IndexRowViewModel

**Files:**
- Modify: `src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelTests.cs`

**Interfaces:**
- Produces: `IndexRowViewModel.IsScoreSafe`, `.IsScoreCaution`, `.IsScoreRisk` (all `bool`), derived from `ScoreColor` (`Green`/`Orange`/`Red`). Consumed by the grid score pill in Task 10.

- [ ] **Step 1: Write the failing test**

Append to `tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelTests.cs` (create the file with this content if it does not yet cover scoring; use the existing test class name if it exists):

```csharp
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexRowViewModelScoreColorTests
{
    private static IndexRowViewModel Row(ScoreColor color)
    {
        var index = IndexModelFactory.Nonclustered();
        var score = new ConfidenceScore(90, color, []);
        var safety = new SafetyAssessment(DeletionEligibility.Deletable, null, []);
        return new IndexRowViewModel(index, score, safety, isRedundant: false, isReferencedByHint: false);
    }

    [Fact]
    public void Green_maps_to_safe()
    {
        var r = Row(ScoreColor.Green);
        Assert.True(r.IsScoreSafe);
        Assert.False(r.IsScoreCaution);
        Assert.False(r.IsScoreRisk);
    }

    [Fact]
    public void Orange_maps_to_caution()
    {
        var r = Row(ScoreColor.Orange);
        Assert.True(r.IsScoreCaution);
        Assert.False(r.IsScoreSafe);
    }

    [Fact]
    public void Red_maps_to_risk()
    {
        var r = Row(ScoreColor.Red);
        Assert.True(r.IsScoreRisk);
        Assert.False(r.IsScoreSafe);
    }
}
```

Note: confirm the exact `ConfidenceScore` and `SafetyAssessment` constructor shapes from `src/SmartIndexManager.Core/Scoring/ConfidenceScore.cs` and `src/SmartIndexManager.Core/Safety/SafetyAssessment.cs`; adjust the two `new(...)` calls in the helper to match. Only the helper changes, not the asserts.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexRowViewModelScoreColorTests"`
Expected: FAIL, `IsScoreSafe` does not exist (compile error).

- [ ] **Step 3: Add the bools**

In `src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs`, after the existing `public ScoreColor? ScoreColor => ScoreDetail?.Color;` line, add:

```csharp
    public bool IsScoreSafe => ScoreColor == Core.Scoring.ScoreColor.Green;
    public bool IsScoreCaution => ScoreColor == Core.Scoring.ScoreColor.Orange;
    public bool IsScoreRisk => ScoreColor == Core.Scoring.ScoreColor.Red;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexRowViewModelScoreColorTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexRowViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexRowViewModelScoreColorTests.cs
git commit -m "feat(app): expose score-colour bools on IndexRowViewModel"
```

---

## Task 4: BrowseViewModel with BrowseState and detail serialization

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/BrowseViewModelTests.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/BrowseConcurrencyTests.cs`

**Interfaces:**
- Consumes: `IndexGridViewModel`, `IAppPaths`, `ILocalizer`, `IIndexProvider`, `IReadOnlyList<IndexRowViewModel>`.
- Produces:
  - `enum BrowseState { Disconnected, Loading, Ready, Empty, Error }`
  - `BrowseViewModel(IndexGridViewModel grid, IAppPaths paths, ILocalizer loc)`
  - `BrowseState State { get; }`, `string? ErrorMessage { get; }`, `IndexGridViewModel Grid { get; }`, `IndexDetailViewModel? Detail { get; }`
  - `Task OnConnectedAsync(IIndexProvider provider, IReadOnlyList<IndexRowViewModel> rows, CancellationToken ct)`
  - `Task OnDisconnectedAsync()`
  - `Task ShowDetailAsync(IndexRowViewModel? row)`

- [ ] **Step 1: Write the failing tests**

Create `tests/SmartIndexManager.App.Tests/ViewModels/BrowseViewModelTests.cs`:

```csharp
using System.Linq;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class BrowseViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-browse-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeIndexProvider Provider(params IndexModel[] indexes) => new()
    {
        ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
        Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
        Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
        Indexes = indexes
    };

    private BrowseViewModel Build() =>
        new(new IndexGridViewModel(), new AppPaths(_dir, _dir, _dir), new ResxLocalizer());

    [Fact]
    public void Starts_disconnected()
    {
        Assert.Equal(BrowseState.Disconnected, Build().State);
    }

    [Fact]
    public async Task OnConnected_with_rows_becomes_ready_and_fills_grid()
    {
        var vm = Build();
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(Provider(), rows, CancellationToken.None);

        Assert.Equal(BrowseState.Ready, vm.State);
        Assert.Equal(1, vm.Grid.VisibleCount);
        Assert.NotNull(vm.Detail);
    }

    [Fact]
    public async Task OnConnected_with_no_rows_becomes_empty()
    {
        var vm = Build();
        await vm.OnConnectedAsync(Provider(), Array.Empty<IndexRowViewModel>(), CancellationToken.None);
        Assert.Equal(BrowseState.Empty, vm.State);
    }

    [Fact]
    public async Task OnDisconnected_clears_grid_and_returns_to_disconnected()
    {
        var vm = Build();
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(Provider(), rows, CancellationToken.None);
        await vm.OnDisconnectedAsync();

        Assert.Equal(BrowseState.Disconnected, vm.State);
        Assert.Equal(0, vm.Grid.VisibleCount);
        Assert.Null(vm.Detail);
    }

    private static Core.Safety.SafetyAssessment Safe() =>
        new(Core.Safety.DeletionEligibility.Deletable, null, []);
}
```

Create `tests/SmartIndexManager.App.Tests/ViewModels/BrowseConcurrencyTests.cs` by copying `DetailConcurrencyTests.cs` and adapting it to `BrowseViewModel`: keep the `ConcurrencyProbeProvider` inner class verbatim, and replace the `Build`/`Overlapping_...` body so it drives `BrowseViewModel` directly:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class BrowseConcurrencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-browseconc-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Paste the ConcurrencyProbeProvider inner class from DetailConcurrencyTests.cs here, unchanged.

    [Fact]
    public async Task Overlapping_detail_loads_never_run_concurrently_on_the_provider()
    {
        var probe = new ConcurrencyProbeProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered(name: "IX_A"), IndexModelFactory.Nonclustered(name: "IX_B")]
        };
        var vm = new BrowseViewModel(new IndexGridViewModel(), new AppPaths(_dir, _dir, _dir), new ResxLocalizer());
        var rows = probe.Indexes.Select(i => new IndexRowViewModel(i, null, new Core.Safety.SafetyAssessment(Core.Safety.DeletionEligibility.Deletable, null, []), false, false)).ToList();
        await vm.OnConnectedAsync(probe, rows, CancellationToken.None);

        var t1 = vm.ShowDetailAsync(rows[0]);
        var t2 = vm.ShowDetailAsync(rows[1]);
        var t3 = vm.ShowDetailAsync(rows[0]);
        probe.Release();
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(1, probe.MaxConcurrent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~BrowseViewModelTests|FullyQualifiedName~BrowseConcurrencyTests"`
Expected: FAIL, `BrowseViewModel` does not exist.

- [ ] **Step 3: Implement BrowseViewModel**

Create `src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public enum BrowseState { Disconnected, Loading, Ready, Empty, Error }

public sealed partial class BrowseViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;
    private readonly SemaphoreSlim _detailGate = new(1, 1);
    private CancellationTokenSource? _detailCts;
    private IndexDetailViewModel? _detail;

    [ObservableProperty] private BrowseState _state = BrowseState.Disconnected;
    [ObservableProperty] private string? _errorMessage;

    public IndexGridViewModel Grid { get; }

    public IndexDetailViewModel? Detail
    {
        get => _detail;
        private set { _detail = value; OnPropertyChanged(nameof(Detail)); }
    }

    public BrowseViewModel(IndexGridViewModel grid, IAppPaths paths, ILocalizer loc)
    {
        Grid = grid;
        _paths = paths;
        _loc = loc;
        Grid.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IndexGridViewModel.SelectedRow))
                _ = ShowDetailAsync(Grid.SelectedRow);
        };
    }

    public async Task OnConnectedAsync(IIndexProvider provider, IReadOnlyList<IndexRowViewModel> rows, CancellationToken ct)
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        Detail = new IndexDetailViewModel(provider, _paths, _loc);
        Grid.SetRows(rows);
        ErrorMessage = null;
        State = rows.Count > 0 ? BrowseState.Ready : BrowseState.Empty;
    }

    public async Task OnDisconnectedAsync()
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        Detail = null;
        Grid.SetRows([]);
        State = BrowseState.Disconnected;
    }

    public async Task ShowDetailAsync(IndexRowViewModel? row)
    {
        if (row is null || Detail is null) return;

        _detailCts?.Cancel();
        await _detailGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var detail = Detail;
            if (detail is null) return;

            var cts = new CancellationTokenSource();
            _detailCts = cts;
            try
            {
                await detail.ShowAsync(row, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                ErrorMessage = _loc["Detail_Error"];
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_detailCts, cts)) _detailCts = null;
            }
        }
        finally
        {
            _detailGate.Release();
        }
    }

    private async Task StopDetailWorkAsync()
    {
        _detailCts?.Cancel();
        await _detailGate.WaitAsync().ConfigureAwait(true);
        _detailGate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        _detailGate.Dispose();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~BrowseViewModelTests|FullyQualifiedName~BrowseConcurrencyTests"`
Expected: PASS. `MaxConcurrent == 1` holds.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/BrowseViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/BrowseViewModelTests.cs tests/SmartIndexManager.App.Tests/ViewModels/BrowseConcurrencyTests.cs
git commit -m "feat(app): BrowseViewModel with BrowseState and detail serialization"
```

---

## Task 5: IDialogService and ConnectionSessionViewModel

**Files:**
- Create: `src/SmartIndexManager.App/Services/IDialogService.cs`
- Create: `src/SmartIndexManager.App/ViewModels/ConnectionSessionViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/ConnectionSessionViewModelTests.cs`

**Interfaces:**
- Produces:
  - `interface IDialogService { Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm); }`
  - `ConnectionSessionViewModel(IIndexLoadService load, IPasswordPrompt prompt, ConnectionManagerViewModel connections, IDialogService dialogs, ILocalizer loc)`
  - properties `IsBusy`, `IsConnected`, `StatusMessage`, `IIndexProvider? ActiveProvider`, `ConnectionManagerViewModel Connections`
  - events `event Func<LoadResult, Task>? Connected;` and `event Func<Task>? Disconnected;`
  - commands `ConnectCommand`, `DisconnectCommand`, `Cancel`, `ManageCommand`

- [ ] **Step 1: Write the failing test**

Create `tests/SmartIndexManager.App.Tests/ViewModels/ConnectionSessionViewModelTests.cs`:

```csharp
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionSessionViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-session-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubPrompt(string? pw) : IPasswordPrompt
    {
        public Task<string?> RequestAsync(string name, CancellationToken ct) => Task.FromResult(pw);
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public int ShownCount { get; private set; }
        public Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm) { ShownCount++; return Task.CompletedTask; }
    }

    private (ConnectionSessionViewModel vm, RecordingDialogs dialogs) Build()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var dialogs = new RecordingDialogs();
        var vm = new ConnectionSessionViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(provider), paths),
            new StubPrompt("pw"), connections, dialogs, new ResxLocalizer());
        return (vm, dialogs);
    }

    [Fact]
    public async Task Connect_sets_connected_and_raises_Connected_with_rows()
    {
        var (vm, _) = Build();
        LoadResult? seen = null;
        vm.Connected += r => { seen = r; return Task.CompletedTask; };

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.NotNull(vm.ActiveProvider);
        Assert.NotNull(seen);
        Assert.Single(seen!.Rows);
    }

    [Fact]
    public async Task Disconnect_disposes_provider_and_raises_Disconnected()
    {
        var (vm, _) = Build();
        await vm.ConnectCommand.ExecuteAsync(null);
        var raised = false;
        vm.Disconnected += () => { raised = true; return Task.CompletedTask; };

        await vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.Null(vm.ActiveProvider);
        Assert.True(raised);
    }

    [Fact]
    public async Task Manage_invokes_the_dialog_service()
    {
        var (vm, dialogs) = Build();
        await vm.ManageCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.ShownCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionSessionViewModelTests"`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Create IDialogService**

Create `src/SmartIndexManager.App/Services/IDialogService.cs`:

```csharp
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Services;

public interface IDialogService
{
    Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm);
}
```

- [ ] **Step 4: Create ConnectionSessionViewModel**

Create `src/SmartIndexManager.App/ViewModels/ConnectionSessionViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionSessionViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IIndexLoadService _load;
    private readonly IPasswordPrompt _prompt;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _statusMessage;

    public IIndexProvider? ActiveProvider { get; private set; }
    public ConnectionManagerViewModel Connections { get; }

    public event Func<LoadResult, Task>? Connected;
    public event Func<Task>? Disconnected;

    public ConnectionSessionViewModel(
        IIndexLoadService load, IPasswordPrompt prompt,
        ConnectionManagerViewModel connections, IDialogService dialogs, ILocalizer loc)
    {
        _load = load;
        _prompt = prompt;
        _dialogs = dialogs;
        _loc = loc;
        Connections = connections;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var profile = Connections.Selected;
        if (profile is null) return;

        var password = profile.Auth == AuthMode.SqlLogin
            ? await _prompt.RequestAsync(profile.Name, CancellationToken.None).ConfigureAwait(true)
            : null;
        if (profile.Auth == AuthMode.SqlLogin && password is null) return;

        await TearDownAsync().ConfigureAwait(true);

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = _loc["Action_Connect"];
        try
        {
            var result = await _load.LoadAsync(profile, password, Connections.SelectedDatabases, _cts.Token).ConfigureAwait(true);
            ActiveProvider = result.Provider;
            IsConnected = true;
            StatusMessage = result.Server.ServerName;
            if (Connected is not null) await Connected(result).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _loc["Action_Cancel"];
        }
        catch (Exception)
        {
            StatusMessage = _loc["Connection_Error"];
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync() => await TearDownAsync().ConfigureAwait(true);

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task ManageAsync() => await _dialogs.ShowConnectionManagerAsync(Connections).ConfigureAwait(true);

    private async Task TearDownAsync()
    {
        if (ActiveProvider is null) { IsConnected = false; return; }
        if (Disconnected is not null) await Disconnected().ConfigureAwait(true);
        await ActiveProvider.DisposeAsync().ConfigureAwait(true);
        ActiveProvider = null;
        IsConnected = false;
    }

    public async ValueTask DisposeAsync() => await TearDownAsync().ConfigureAwait(true);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ConnectionSessionViewModelTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/Services/IDialogService.cs src/SmartIndexManager.App/ViewModels/ConnectionSessionViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/ConnectionSessionViewModelTests.cs
git commit -m "feat(app): IDialogService and ConnectionSessionViewModel"
```

---

## Task 6: NavigationDestination and PlaceholderPageViewModel

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/NavigationDestination.cs`
- Create: `src/SmartIndexManager.App/ViewModels/PlaceholderPageViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/NavigationDestinationTests.cs`

**Interfaces:**
- Produces:
  - `sealed record NavigationDestination(string Title, Material.Icons.MaterialIconKind IconKind, object PageViewModel, bool IsEnabled = true)`
  - `sealed class PlaceholderPageViewModel(string title, Material.Icons.MaterialIconKind iconKind, string message)` with `Title`, `IconKind`, `Message` getters.

- [ ] **Step 1: Write the failing test**

Create `tests/SmartIndexManager.App.Tests/ViewModels/NavigationDestinationTests.cs`:

```csharp
using Material.Icons;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Tests.ViewModels;

public class NavigationDestinationTests
{
    [Fact]
    public void Destination_carries_its_page_and_defaults_to_enabled()
    {
        var page = new PlaceholderPageViewModel("Audit", MaterialIconKind.History, "Planned for a future version.");
        var dest = new NavigationDestination("Audit", MaterialIconKind.History, page);

        Assert.Equal("Audit", dest.Title);
        Assert.Same(page, dest.PageViewModel);
        Assert.True(dest.IsEnabled);
        Assert.Equal("Planned for a future version.", page.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~NavigationDestinationTests"`
Expected: FAIL, types do not exist.

- [ ] **Step 3: Implement the types**

Create `src/SmartIndexManager.App/ViewModels/NavigationDestination.cs`:

```csharp
using Material.Icons;

namespace SmartIndexManager.App.ViewModels;

public sealed record NavigationDestination(
    string Title, MaterialIconKind IconKind, object PageViewModel, bool IsEnabled = true);
```

Create `src/SmartIndexManager.App/ViewModels/PlaceholderPageViewModel.cs`:

```csharp
using Material.Icons;

namespace SmartIndexManager.App.ViewModels;

public sealed class PlaceholderPageViewModel : ViewModelBase
{
    public string Title { get; }
    public MaterialIconKind IconKind { get; }
    public string Message { get; }

    public PlaceholderPageViewModel(string title, MaterialIconKind iconKind, string message)
    {
        Title = title;
        IconKind = iconKind;
        Message = message;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~NavigationDestinationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/NavigationDestination.cs src/SmartIndexManager.App/ViewModels/PlaceholderPageViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/NavigationDestinationTests.cs
git commit -m "feat(app): NavigationDestination and PlaceholderPageViewModel"
```

---

## Task 7: ShellViewModel (navigation, theme, orchestration)

**Files:**
- Create: `src/SmartIndexManager.App/ViewModels/ShellViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: `ConnectionSessionViewModel`, `BrowseViewModel`, `PermissionStatusViewModel`, `IThemeService`, `ILocalizer`.
- Produces:
  - `ShellViewModel(ConnectionSessionViewModel connection, BrowseViewModel browse, PermissionStatusViewModel permissions, IThemeService theme, ILocalizer loc)`
  - `IReadOnlyList<NavigationDestination> Destinations`, `NavigationDestination? SelectedDestination`, `object? CurrentPage`, `bool IsDarkTheme`, `ConnectionSessionViewModel Connection`, `PermissionStatusViewModel Permissions`, `ToggleThemeCommand`, `IAsyncDisposable`.
  - On construction: wires `connection.Connected` to push rows into `browse` and update `permissions`, and `connection.Disconnected` to clear `browse`; default `SelectedDestination` is the Browse destination.

- [ ] **Step 1: Write the failing test**

Create `tests/SmartIndexManager.App.Tests/ViewModels/ShellViewModelTests.cs`:

```csharp
using System.Linq;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ShellViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-shell-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class NoDialogs : IDialogService
    {
        public Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm) => Task.CompletedTask;
    }

    private ShellViewModel Build()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.WindowsIntegrated },
            DatabasesText = "Sales"
        };
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var session = new ConnectionSessionViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(provider), paths),
            new NullPasswordPrompt(), connections, new NoDialogs(), new ResxLocalizer());
        var browse = new BrowseViewModel(new IndexGridViewModel(), paths, new ResxLocalizer());
        return new ShellViewModel(session, browse, new PermissionStatusViewModel(new ResxLocalizer()), new ThemeService(paths), new ResxLocalizer());
    }

    [Fact]
    public void Default_destination_is_browse_and_current_page_is_the_browse_vm()
    {
        var shell = Build();
        Assert.Equal("Browse", shell.SelectedDestination?.Title);
        Assert.IsType<BrowseViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void Selecting_a_destination_sets_current_page()
    {
        var shell = Build();
        var settings = shell.Destinations.First(d => d.Title == "Settings");
        shell.SelectedDestination = settings;
        Assert.Same(settings.PageViewModel, shell.CurrentPage);
    }

    [Fact]
    public async Task Connecting_fills_browse_and_updates_permissions()
    {
        var shell = Build();
        await shell.Connection.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(BrowseState.Ready, ((BrowseViewModel)shell.CurrentPage!).State);
        Assert.False(shell.Permissions.UsageAvailable);
    }

    [Fact]
    public void ToggleTheme_flips_and_persists()
    {
        var shell = Build();
        var before = shell.IsDarkTheme;
        shell.ToggleThemeCommand.Execute(null);
        Assert.NotEqual(before, shell.IsDarkTheme);
        Assert.Equal(shell.IsDarkTheme, new ThemeService(new AppPaths(_dir, _dir, _dir)).Current == ThemeVariantKind.Dark);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ShellViewModelTests"`
Expected: FAIL, `ShellViewModel` does not exist.

- [ ] **Step 3: Implement ShellViewModel**

Create `src/SmartIndexManager.App/ViewModels/ShellViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly BrowseViewModel _browse;
    private readonly IThemeService _theme;

    [ObservableProperty] private NavigationDestination? _selectedDestination;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isDarkTheme;

    public IReadOnlyList<NavigationDestination> Destinations { get; }
    public ConnectionSessionViewModel Connection { get; }
    public PermissionStatusViewModel Permissions { get; }

    public ShellViewModel(
        ConnectionSessionViewModel connection, BrowseViewModel browse,
        PermissionStatusViewModel permissions, IThemeService theme, ILocalizer loc)
    {
        Connection = connection;
        _browse = browse;
        Permissions = permissions;
        _theme = theme;
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;

        Destinations =
        [
            new NavigationDestination(loc["Nav_Browse"], MaterialIconKind.DatabaseSearch, browse),
            new NavigationDestination(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline,
                new PlaceholderPageViewModel(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Restore"], MaterialIconKind.BackupRestore,
                new PlaceholderPageViewModel(loc["Nav_Restore"], MaterialIconKind.BackupRestore, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Audit"], MaterialIconKind.History,
                new PlaceholderPageViewModel(loc["Nav_Audit"], MaterialIconKind.History, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Settings"], MaterialIconKind.CogOutline,
                new PlaceholderPageViewModel(loc["Nav_Settings"], MaterialIconKind.CogOutline, loc["Placeholder_Message"])),
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
        await Connection.DisposeAsync().ConfigureAwait(true);
        await _browse.DisposeAsync().ConfigureAwait(true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ShellViewModelTests"`
Expected: PASS (4 tests). Add the `Nav_*` and `Placeholder_Message` keys in Task 8; until then `ResxLocalizer` returns the key name, which the tests tolerate (they assert `"Browse"`, `"Settings"` which are the key suffixes). If `ResxLocalizer` returns the full key, adjust the two title asserts to match the localizer's missing-key behavior, or run Task 8 first.

Note: if the localizer throws on a missing key rather than echoing it, reorder so Task 8 runs before this step. Confirm `ResxLocalizer` missing-key behavior in `src/SmartIndexManager.App/Localization/ResxLocalizer.cs`.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/ShellViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/ShellViewModelTests.cs
git commit -m "feat(app): ShellViewModel with navigation, theme, and connection orchestration"
```

---

## Task 8: Localization strings

**Files:**
- Modify: `src/SmartIndexManager.App/Localization/Strings.resx`
- Modify: `src/SmartIndexManager.App/Localization/Strings.Designer.cs`

**Interfaces:**
- Produces resource keys used by the shell and views: `Nav_Browse`, `Nav_Basket`, `Nav_Restore`, `Nav_Audit`, `Nav_Settings`, `Placeholder_Message`, `Connection_Connect`, `Connection_Disconnect`, `Connection_Manage`, `Connection_Prompt`, `Grid_MatchCount`, `Detail_Empty`, `Detail_Copy`, `Detail_Section_Ddl`, `Detail_Section_Structure`, `Detail_Section_Usage`, `Detail_Section_Score`, `Detail_Section_Redundancy`, `Browse_ErrorRetry`.

- [ ] **Step 1: Add the resx entries**

In `src/SmartIndexManager.App/Localization/Strings.resx`, add one `<data>` element per key. Example for two of them (repeat the pattern for every key above):

```xml
<data name="Nav_Browse" xml:space="preserve"><value>Browse</value></data>
<data name="Nav_Basket" xml:space="preserve"><value>Deletion basket</value></data>
<data name="Nav_Restore" xml:space="preserve"><value>Restore</value></data>
<data name="Nav_Audit" xml:space="preserve"><value>Audit</value></data>
<data name="Nav_Settings" xml:space="preserve"><value>Settings</value></data>
<data name="Placeholder_Message" xml:space="preserve"><value>Planned for a future version.</value></data>
<data name="Connection_Connect" xml:space="preserve"><value>Connect</value></data>
<data name="Connection_Disconnect" xml:space="preserve"><value>Disconnect</value></data>
<data name="Connection_Manage" xml:space="preserve"><value>Manage…</value></data>
<data name="Connection_Prompt" xml:space="preserve"><value>Connect to a server to browse indexes.</value></data>
<data name="Grid_MatchCount" xml:space="preserve"><value>{0} of {1} indexes</value></data>
<data name="Detail_Empty" xml:space="preserve"><value>Select an index to see its details.</value></data>
<data name="Detail_Copy" xml:space="preserve"><value>Copy</value></data>
<data name="Detail_Section_Ddl" xml:space="preserve"><value>Recreation DDL</value></data>
<data name="Detail_Section_Structure" xml:space="preserve"><value>Structure</value></data>
<data name="Detail_Section_Usage" xml:space="preserve"><value>Usage</value></data>
<data name="Detail_Section_Score" xml:space="preserve"><value>Score explanation</value></data>
<data name="Detail_Section_Redundancy" xml:space="preserve"><value>Redundancy</value></data>
<data name="Browse_ErrorRetry" xml:space="preserve"><value>Retry</value></data>
```

- [ ] **Step 2: Add the Designer accessors**

In `src/SmartIndexManager.App/Localization/Strings.Designer.cs`, add a static accessor per key, following the existing pattern in that file. Example for two:

```csharp
public static string Nav_Browse => ResourceManager.GetString("Nav_Browse", Culture)!;
public static string Placeholder_Message => ResourceManager.GetString("Placeholder_Message", Culture)!;
```

Match the exact property style already present in `Strings.Designer.cs` (some generators use full-bodied properties). Add one accessor for every key added in Step 1.

- [ ] **Step 3: Build to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/SmartIndexManager.App/Localization/Strings.resx src/SmartIndexManager.App/Localization/Strings.Designer.cs
git commit -m "feat(app): localization strings for shell, connection bar, detail cards"
```

---

## Task 9: EmptyStateView

**Files:**
- Create: `src/SmartIndexManager.App/Views/EmptyStateView.axaml`
- Create: `src/SmartIndexManager.App/Views/EmptyStateView.axaml.cs`

**Interfaces:**
- Produces: a reusable `UserControl` rendering an icon, title, message, and optional action button. Bound via `DataContext` to any object exposing `IconKind` (`MaterialIconKind`), `Title` (`string`), `Message` (`string`); used directly for `PlaceholderPageViewModel` and embedded literally in Browse states.

- [ ] **Step 1: Create the code-behind**

Create `src/SmartIndexManager.App/Views/EmptyStateView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Create the view**

Create `src/SmartIndexManager.App/Views/EmptyStateView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="SmartIndexManager.App.Views.EmptyStateView"
             x:DataType="vm:PlaceholderPageViewModel">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8" MaxWidth="360">
        <mi:MaterialIcon Kind="{Binding IconKind}" Width="48" Height="48" Opacity="0.6"
                         HorizontalAlignment="Center" />
        <TextBlock Classes="title" HorizontalAlignment="Center" Text="{Binding Title}" />
        <TextBlock Classes="caption" HorizontalAlignment="Center" TextWrapping="Wrap"
                   TextAlignment="Center" Text="{Binding Message}" />
    </StackPanel>
</UserControl>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/SmartIndexManager.App/Views/EmptyStateView.axaml src/SmartIndexManager.App/Views/EmptyStateView.axaml.cs
git commit -m "feat(app): reusable EmptyStateView"
```

---

## Task 10: BrowseView (grid, score pill, badges, states)

**Files:**
- Create: `src/SmartIndexManager.App/Views/BrowseView.axaml`
- Create: `src/SmartIndexManager.App/Views/BrowseView.axaml.cs`

**Interfaces:**
- Consumes: `BrowseViewModel` (`State`, `Grid`, `Detail`, `ErrorMessage`) and `IndexRowViewModel` (`IsScoreSafe/Caution/Risk`, `Score`, badge bools, `Seeks`/`Scans`/`Updates`/`SizeMb`).
- Produces: the Browse page view. Detail cards come from Task 11 (`IndexDetailView`), referenced here.

- [ ] **Step 1: Create the code-behind**

Create `src/SmartIndexManager.App/Views/BrowseView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class BrowseView : UserControl
{
    public BrowseView() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Create the view**

Create `src/SmartIndexManager.App/Views/BrowseView.axaml`. This folds in the old `IndexGridView` markup, adds the score pill (class-bound, theme-reactive), token-based badges, `StringFormat` numerics, a filter toolbar, and switches on `BrowseState`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:views="clr-namespace:SmartIndexManager.App.Views"
             xmlns:loc="clr-namespace:SmartIndexManager.App.Localization"
             xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="SmartIndexManager.App.Views.BrowseView"
             x:DataType="vm:BrowseViewModel">
    <UserControl.Styles>
        <Style Selector="Border.score-pill">
            <Setter Property="CornerRadius" Value="{StaticResource RadiusSm}" />
            <Setter Property="Padding" Value="6,1" />
            <Setter Property="MinWidth" Value="32" />
        </Style>
        <Style Selector="Border.score-pill.score-safe">
            <Setter Property="Background" Value="{DynamicResource ScoreSafeBrush}" />
        </Style>
        <Style Selector="Border.score-pill.score-caution">
            <Setter Property="Background" Value="{DynamicResource ScoreCautionBrush}" />
        </Style>
        <Style Selector="Border.score-pill.score-risk">
            <Setter Property="Background" Value="{DynamicResource ScoreRiskBrush}" />
        </Style>
        <Style Selector="Border.badge">
            <Setter Property="CornerRadius" Value="{StaticResource RadiusSm}" />
            <Setter Property="Padding" Value="4,1" />
        </Style>
    </UserControl.Styles>

    <Panel>
        <!-- Ready / Empty: the grid + detail split -->
        <Grid ColumnDefinitions="2*,Auto,1*"
              IsVisible="{Binding State, Converter={x:Static views:BrowseStateConverters.IsGridVisible}}">
            <DockPanel Grid.Column="0">
                <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto" Margin="8,8,8,4">
                    <mi:MaterialIcon Grid.Column="0" Kind="Magnify" Width="16" Height="16"
                                     VerticalAlignment="Center" Margin="0,0,4,0" />
                    <TextBox Grid.Column="1" Watermark="{x:Static loc:Strings.Grid_Filter}"
                             Text="{Binding Grid.FilterText}" />
                    <TextBlock Grid.Column="2" Classes="caption" VerticalAlignment="Center" Margin="8,0,0,0"
                               Text="{Binding Grid.VisibleCount}" />
                </Grid>
                <DataGrid ItemsSource="{Binding Grid.View}" SelectedItem="{Binding Grid.SelectedRow}"
                          IsReadOnly="True" CanUserSortColumns="True" CanUserReorderColumns="True"
                          RowHeight="{StaticResource GridRowHeight}"
                          GridLinesVisibility="Horizontal">
                    <DataGrid.Columns>
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Database}" Binding="{Binding Database}" Width="*" MinWidth="80" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Schema}"   Binding="{Binding Schema}" Width="Auto" MinWidth="60" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Table}"    Binding="{Binding Table}" Width="*" MinWidth="90" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Index}"    Binding="{Binding Name}" Width="*" MinWidth="120" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Type}"     Binding="{Binding Type}" Width="Auto" MinWidth="90" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_SizeMb}"   Binding="{Binding SizeMb, StringFormat={}{0:N1}}" Width="Auto" MinWidth="70" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Seeks}"    Binding="{Binding Seeks, StringFormat={}{0:N0}}" Width="Auto" MinWidth="70" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Scans}"    Binding="{Binding Scans, StringFormat={}{0:N0}}" Width="Auto" MinWidth="70" />
                        <DataGridTextColumn Header="{x:Static loc:Strings.Grid_Column_Updates}"  Binding="{Binding Updates, StringFormat={}{0:N0}}" Width="Auto" MinWidth="80" />
                        <DataGridTemplateColumn Header="{x:Static loc:Strings.Grid_Column_Score}" Width="Auto">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <Border Classes="score-pill"
                                            Classes.score-safe="{Binding IsScoreSafe}"
                                            Classes.score-caution="{Binding IsScoreCaution}"
                                            Classes.score-risk="{Binding IsScoreRisk}"
                                            HorizontalAlignment="Center" VerticalAlignment="Center"
                                            IsVisible="{Binding Score, Converter={x:Static ObjectConverters.IsNotNull}}">
                                        <TextBlock Text="{Binding Score}" Foreground="White" FontSize="11"
                                                   HorizontalAlignment="Center" />
                                    </Border>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <StackPanel Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                                        <Border Classes="badge" Background="{DynamicResource DangerBrush}"
                                                IsVisible="{Binding NotDeletable}" ToolTip.Tip="{Binding NotDeletableReason}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <mi:MaterialIcon Kind="LockOutline" Width="11" Height="11" Foreground="White" />
                                                <TextBlock Text="{x:Static loc:Strings.Badge_NotDeletable}" Foreground="White" FontSize="11" />
                                            </StackPanel>
                                        </Border>
                                        <Border Classes="badge" Background="{DynamicResource WarnBrush}" IsVisible="{Binding Redundant}">
                                            <TextBlock Text="{x:Static loc:Strings.Badge_Redundant}" Foreground="White" FontSize="11" />
                                        </Border>
                                        <Border Classes="badge" Background="{DynamicResource InfoBrush}" IsVisible="{Binding SupportsForeignKey}">
                                            <TextBlock Text="{x:Static loc:Strings.Badge_ForeignKey}" Foreground="White" FontSize="11" />
                                        </Border>
                                        <Border Classes="badge" Background="{DynamicResource AccentAltBrush}" IsVisible="{Binding ReferencedByHint}">
                                            <TextBlock Text="{x:Static loc:Strings.Badge_Hint}" Foreground="White" FontSize="11" />
                                        </Border>
                                    </StackPanel>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </DockPanel>
            <GridSplitter Grid.Column="1" Width="4" />
            <views:IndexDetailView Grid.Column="2" DataContext="{Binding Detail}" />
        </Grid>

        <!-- Disconnected -->
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8"
                    IsVisible="{Binding State, Converter={x:Static views:BrowseStateConverters.IsDisconnected}}">
            <mi:MaterialIcon Kind="DatabaseOffOutline" Width="48" Height="48" Opacity="0.6" HorizontalAlignment="Center" />
            <TextBlock Classes="caption" Text="{x:Static loc:Strings.Connection_Prompt}" />
        </StackPanel>

        <!-- Error -->
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8"
                    IsVisible="{Binding State, Converter={x:Static views:BrowseStateConverters.IsError}}">
            <mi:MaterialIcon Kind="AlertCircleOutline" Width="48" Height="48"
                             Foreground="{DynamicResource DangerBrush}" HorizontalAlignment="Center" />
            <TextBlock Classes="caption" Text="{Binding ErrorMessage}" TextWrapping="Wrap" MaxWidth="360" TextAlignment="Center" />
        </StackPanel>
    </Panel>
</UserControl>
```

- [ ] **Step 3: Add the BrowseState converters**

Create `src/SmartIndexManager.App/Views/BrowseStateConverters.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public static class BrowseStateConverters
{
    public static readonly IValueConverter IsGridVisible = new FuncValueConverter<BrowseState, bool>(
        s => s is BrowseState.Ready or BrowseState.Empty);
    public static readonly IValueConverter IsDisconnected = new FuncValueConverter<BrowseState, bool>(
        s => s == BrowseState.Disconnected);
    public static readonly IValueConverter IsError = new FuncValueConverter<BrowseState, bool>(
        s => s == BrowseState.Error);
}
```

Note: `FuncValueConverter` is deterministic and could be unit-tested, but these are trivial one-liners over an enum; the build plus the visual checklist cover them. If you prefer a test, assert `IsGridVisible.Convert(BrowseState.Ready, ...)` returns `true`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds. `IndexDetailView` is referenced; it still exists from before this task (redesigned in Task 11). If the old `IndexDetailView` binds properties that still exist on `IndexDetailViewModel`, the build passes.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/Views/BrowseView.axaml src/SmartIndexManager.App/Views/BrowseView.axaml.cs src/SmartIndexManager.App/Views/BrowseStateConverters.cs
git commit -m "feat(app): BrowseView with score pills, token badges, and state switching"
```

---

## Task 11: IndexDetailView redesigned as titled cards

**Files:**
- Modify: `src/SmartIndexManager.App/Views/IndexDetailView.axaml`
- Modify: `src/SmartIndexManager.App/Views/IndexDetailView.axaml.cs` (only if needed)

**Interfaces:**
- Consumes: `IndexDetailViewModel` (`Ddl`, `OldestSnapshotText`, `Queries`, `Hints`, `ScoreFactors`).

- [ ] **Step 1: Rewrite the detail view as cards**

Replace the contents of `src/SmartIndexManager.App/Views/IndexDetailView.axaml` with:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:loc="clr-namespace:SmartIndexManager.App.Localization"
             x:Class="SmartIndexManager.App.Views.IndexDetailView"
             x:DataType="vm:IndexDetailViewModel">
    <UserControl.Styles>
        <Style Selector="Border.card">
            <Setter Property="Background" Value="{DynamicResource SurfaceCardBrush}" />
            <Setter Property="CornerRadius" Value="{StaticResource RadiusMd}" />
            <Setter Property="Padding" Value="12" />
            <Setter Property="Margin" Value="0,0,0,8" />
        </Style>
    </UserControl.Styles>
    <ScrollViewer>
        <StackPanel Margin="8">
            <!-- Empty state when no detail is bound -->
            <TextBlock Classes="caption" Text="{x:Static loc:Strings.Detail_Empty}"
                       IsVisible="{Binding $parent[UserControl].DataContext, Converter={x:Static ObjectConverters.IsNull}}" />

            <Border Classes="card">
                <StackPanel Spacing="4">
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Detail_Section_Ddl}" />
                    <TextBox Classes="code" Text="{Binding Ddl}" IsReadOnly="True"
                             AcceptsReturn="True" TextWrapping="NoWrap"
                             HorizontalScrollBarVisibility="Auto" />
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="4">
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Detail_Section_Usage}" />
                    <TextBlock Classes="caption" Text="{Binding OldestSnapshotText}" />
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="4">
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Detail_Section_Score}" />
                    <ItemsControl ItemsSource="{Binding ScoreFactors}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Classes="caption" Text="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

Note: the `ScoreFactor` item template shows `{Binding}`, which renders `ScoreFactor.ToString()`. If `ScoreFactor` (see `src/SmartIndexManager.Core/Scoring/ScoreFactor.cs`) exposes named members (e.g. `Name`, `Contribution`), bind those two in a two-column layout instead. Adjust only the inner `DataTemplate`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/SmartIndexManager.App/Views/IndexDetailView.axaml
git commit -m "feat(app): IndexDetailView as titled cards (DDL, usage, score)"
```

---

## Task 12: Connection manager dialog and AvaloniaDialogService

**Files:**
- Create: `src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml` (+ `.axaml.cs`)
- Create: `src/SmartIndexManager.App/Services/AvaloniaDialogService.cs`

**Interfaces:**
- Consumes: `ConnectionManagerViewModel`, existing `ConnectionManagerView`.
- Produces: `AvaloniaDialogService : IDialogService` that shows `ConnectionManagerDialog` modally over the main window.

- [ ] **Step 1: Create the dialog window code-behind**

Create `src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class ConnectionManagerDialog : Window
{
    public ConnectionManagerDialog() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 2: Create the dialog window**

Create `src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
        xmlns:views="clr-namespace:SmartIndexManager.App.Views"
        xmlns:loc="clr-namespace:SmartIndexManager.App.Localization"
        x:Class="SmartIndexManager.App.Views.ConnectionManagerDialog"
        x:DataType="vm:ConnectionManagerViewModel"
        Width="640" Height="480" WindowStartupLocation="CenterOwner"
        Title="{x:Static loc:Strings.Connection_Title}">
    <DockPanel Margin="12">
        <Button DockPanel.Dock="Bottom" HorizontalAlignment="Right" Content="Close"
                Click="OnClose" Margin="0,8,0,0" />
        <views:ConnectionManagerView />
    </DockPanel>
</Window>
```

Add the close handler to `ConnectionManagerDialog.axaml.cs`:

```csharp
    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
```

Note: `Connection_Title` already exists in `Strings.resx` (used by the current `ConnectionManagerView`). If not, add it in a follow-up to Task 8.

- [ ] **Step 3: Create AvaloniaDialogService**

Create `src/SmartIndexManager.App/Services/AvaloniaDialogService.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    public async Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var dialog = new ConnectionManagerDialog { DataContext = vm };
        await dialog.ShowDialog(desktop.MainWindow);
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml src/SmartIndexManager.App/Views/ConnectionManagerDialog.axaml.cs src/SmartIndexManager.App/Services/AvaloniaDialogService.cs
git commit -m "feat(app): connection manager dialog and AvaloniaDialogService"
```

---

## Task 13: Cutover to the shell

**Files:**
- Modify: `src/SmartIndexManager.App/Views/MainWindow.axaml` and `.axaml.cs`
- Modify: `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`
- Modify: `src/SmartIndexManager.App/App.axaml.cs`
- Delete: `src/SmartIndexManager.App/ViewModels/MainWindowViewModel.cs`
- Delete: `tests/SmartIndexManager.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Delete: `tests/SmartIndexManager.App.Tests/ViewModels/DetailConcurrencyTests.cs` (superseded by `BrowseConcurrencyTests`)
- Delete: `src/SmartIndexManager.App/Views/IndexGridView.axaml` and `.axaml.cs` (folded into `BrowseView`)

**Interfaces:**
- Consumes: `ShellViewModel`, `BrowseView`, `EmptyStateView`, all page VMs.
- Produces: the running shell. After this task the app launches as the redesigned UI.

- [ ] **Step 1: Rewrite MainWindow as the shell**

Replace the contents of `src/SmartIndexManager.App/Views/MainWindow.axaml` with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
        xmlns:views="clr-namespace:SmartIndexManager.App.Views"
        xmlns:loc="clr-namespace:SmartIndexManager.App.Localization"
        xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
        x:Class="SmartIndexManager.App.Views.MainWindow"
        x:DataType="vm:ShellViewModel"
        Width="1200" Height="800" Title="{x:Static loc:Strings.App_Title}">

    <Window.DataTemplates>
        <DataTemplate DataType="vm:BrowseViewModel">
            <views:BrowseView />
        </DataTemplate>
        <DataTemplate DataType="vm:PlaceholderPageViewModel">
            <views:EmptyStateView />
        </DataTemplate>
    </Window.DataTemplates>

    <Window.KeyBindings>
        <KeyBinding Gesture="Ctrl+Enter" Command="{Binding Connection.ConnectCommand}" />
    </Window.KeyBindings>

    <DockPanel>
        <!-- Nav rail -->
        <ListBox DockPanel.Dock="Left" Width="56" Padding="0,8"
                 ItemsSource="{Binding Destinations}" SelectedItem="{Binding SelectedDestination}">
            <ListBox.ItemTemplate>
                <DataTemplate x:DataType="vm:NavigationDestination">
                    <mi:MaterialIcon Kind="{Binding IconKind}" Width="22" Height="22"
                                     ToolTip.Tip="{Binding Title}"
                                     AutomationProperties.Name="{Binding Title}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <!-- Connection bar -->
        <Border DockPanel.Dock="Top" Padding="8" BorderThickness="0,0,0,1"
                BorderBrush="{DynamicResource SemiColorBorder}">
            <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto">
                <TextBox Grid.Column="0" MinWidth="220"
                         Watermark="{x:Static loc:Strings.Connection_DatabasesWatermark}"
                         Text="{Binding Connection.Connections.DatabasesText}" />
                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6" Margin="8,0,0,0">
                    <Button Content="{x:Static loc:Strings.Connection_Connect}" Command="{Binding Connection.ConnectCommand}"
                            IsEnabled="{Binding !Connection.IsBusy}" />
                    <Button Content="{x:Static loc:Strings.Connection_Disconnect}" Command="{Binding Connection.DisconnectCommand}"
                            IsVisible="{Binding Connection.IsConnected}" />
                    <Button Content="{x:Static loc:Strings.Action_Cancel}" Command="{Binding Connection.CancelCommand}"
                            IsVisible="{Binding Connection.IsBusy}" />
                    <ProgressBar IsIndeterminate="True" IsVisible="{Binding Connection.IsBusy}" Width="100" />
                    <Button Content="{x:Static loc:Strings.Connection_Manage}" Command="{Binding Connection.ManageCommand}" />
                </StackPanel>
                <TextBlock Grid.Column="3" Classes="caption" VerticalAlignment="Center" Margin="8,0"
                           Text="{Binding Connection.StatusMessage}" />
                <Button Grid.Column="4" Command="{Binding ToggleThemeCommand}"
                        AutomationProperties.Name="{x:Static loc:Strings.Action_ToggleTheme}">
                    <mi:MaterialIcon Kind="ThemeLightDark" Width="18" Height="18" />
                </Button>
            </Grid>
        </Border>

        <!-- Permission status bar -->
        <views:PermissionStatusBar DockPanel.Dock="Bottom" DataContext="{Binding Permissions}" />

        <!-- Current page -->
        <ContentControl Content="{Binding CurrentPage}" />
    </DockPanel>
</Window>
```

Note: `SemiColorBorder` is Semi's border resource key. If the build cannot resolve it, replace with a token you add to `Tokens.axaml` (`BorderBrush`) or use `{DynamicResource ThemeBorderMidBrush}`. Verified by Step 5.

- [ ] **Step 2: Update MainWindow code-behind to ShellViewModel**

In `src/SmartIndexManager.App/Views/MainWindow.axaml.cs`, change every `MainWindowViewModel` reference to `ShellViewModel` (the `_observed` field type, the `as` cast, and the `nameof(MainWindowViewModel.IsDarkTheme)` to `nameof(ShellViewModel.IsDarkTheme)`). The theme-applying logic is unchanged.

- [ ] **Step 3: Update DI registration**

In `src/SmartIndexManager.App/Composition/ServiceRegistration.cs`, replace the ViewModel registrations block (the `AddTransient<ConnectionManagerViewModel>()` through `AddSingleton<MainWindowViewModel>()` lines) with:

```csharp
        services.AddTransient<ConnectionManagerViewModel>();
        services.AddSingleton<IndexGridViewModel>();
        services.AddSingleton<PermissionStatusViewModel>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<ConnectionSessionViewModel>();
        services.AddSingleton<ShellViewModel>();
```

- [ ] **Step 4: Update App.axaml.cs to build the shell**

In `src/SmartIndexManager.App/App.axaml.cs`, change the `desktop.MainWindow` assignment from `MainWindowViewModel` to `ShellViewModel`:

```csharp
            desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<ShellViewModel>() };
```

- [ ] **Step 5: Delete superseded files**

```bash
git rm src/SmartIndexManager.App/ViewModels/MainWindowViewModel.cs \
       tests/SmartIndexManager.App.Tests/ViewModels/MainWindowViewModelTests.cs \
       tests/SmartIndexManager.App.Tests/ViewModels/DetailConcurrencyTests.cs \
       src/SmartIndexManager.App/Views/IndexGridView.axaml \
       src/SmartIndexManager.App/Views/IndexGridView.axaml.cs
```

- [ ] **Step 6: Build and test**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors, 0 warnings.

Run: `dotnet test tests/SmartIndexManager.App.Tests`
Expected: all tests pass (Shell, Browse, BrowseConcurrency, ConnectionSession, NavigationDestination, IndexRowViewModel, and the untouched connection/theme/service tests). `MaxConcurrent == 1` still holds via `BrowseConcurrencyTests`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(app): cut over to the Semi.Avalonia navigation shell"
```

---

## Task 14: Final validation

**Files:** none (verification and cleanup only)

- [ ] **Step 1: Full build and test sweep**

Run: `dotnet build SmartIndexManager.sln`
Expected: 0 errors, 0 warnings.

Run: `dotnet test`
Expected: all projects green. Provider integration tests auto-skip without Docker.

- [ ] **Step 2: Fluent-removal follow-up check**

Attempt to remove `Avalonia.Themes.Fluent` from the App styles (if any `<FluentTheme />` remains) and from the `.csproj` if it was only pulled transitively. Rebuild and, if a display is available, launch. If any control renders unstyled or the app throws a missing-resource exception, revert this step and leave Fluent in place with a one-line comment in `App.axaml` explaining why. This is expected to be optional.

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds either way.

- [ ] **Step 3: Manual visual checklist (if a display is available)**

Run: `dotnet run --project src/SmartIndexManager.App`

Verify, toggling the theme button between light and dark for each:
- Nav rail renders icons with tooltips; arrow keys move selection; each destination shows its page.
- Disconnected content region shows the connect prompt.
- Basket / Restore / Audit / Settings show the empty state with icon, title, message.
- Connection bar: Connect, Disconnect (visible only when connected), Cancel + progress (visible only when busy), Manage opens the modal dialog.
- Ctrl+Enter triggers Connect.
- Grid: compact rows, right-aligned formatted numbers, score pills in the three colours, badges with tooltips.
- Detail pane: titled cards (DDL monospace, usage, score); empty state when nothing selected.
- Both themes: pills, badges, and text keep sufficient contrast.

Note any issues and fix in follow-up commits. If no display, record that visual validation was skipped.

- [ ] **Step 4: Record completion**

```bash
git commit --allow-empty -m "chore(app): GUI redesign complete; build+tests green, visual checklist recorded"
```

---

## Self-Review (completed during authoring)

- Spec coverage: shell/nav (Tasks 6, 7, 13), connection bar + dialog (Tasks 5, 12, 13), tokens (Tasks 1, 2), score pill reusing Core `ScoreColor` (Tasks 3, 10), badges/grid/detail (Tasks 10, 11), states + placeholders (Tasks 9, 10, 13), accessibility (Task 13), testing + visual validation (all VM tasks + Task 14). Group-by and Basket/Restore/Audit/Settings features remain out of scope per the spec.
- Type consistency: `OnConnectedAsync(IIndexProvider, IReadOnlyList<IndexRowViewModel>, CancellationToken)`, `OnDisconnectedAsync()`, `event Func<LoadResult, Task>? Connected`, `event Func<Task>? Disconnected`, `NavigationDestination(string, MaterialIconKind, object, bool)`, and `BrowseState` values are used identically across Tasks 4, 5, 7, 10, 13.
- Known verification hooks left for the implementer (all build- or test-gated, none are placeholders for our own logic): exact Semi include line (Task 1), `ConfidenceScore`/`SafetyAssessment`/`ScoreFactor` member shapes from Core (Tasks 3, 11), `ResxLocalizer` missing-key behavior (Task 7), and Semi border resource key (Task 13).
