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
4. Numeric grid columns use `DataGridTemplateColumn` with `SortMemberPath` to right-align while staying sortable, because Avalonia's `DataGridTextColumn` has no WPF-style `ElementStyle`. The detail redundancy card is minimal (an indicator shown when the index is redundant); the full covering-index + rule (R1/R2/R3) breakdown is deferred until that pairing is threaded to the detail VM.

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

Do not pin a guessed version. Let the restore pick a build compatible with the pinned Avalonia 11.3:

Run:
```bash
dotnet add src/SmartIndexManager.App package Semi.Avalonia
dotnet add src/SmartIndexManager.App package Material.Icons.Avalonia
```

Then open `SmartIndexManager.App.csproj` and confirm two `<PackageReference>` lines were added. Verify the resolved `Semi.Avalonia` version targets Avalonia 11.3 (its major.minor tracks Avalonia; a `11.2.x` Semi build against Avalonia 11.3 can throw missing-resource errors at runtime). If restore pulled a `11.2.x` Semi, pin the latest `11.3.x` explicitly: `dotnet add src/SmartIndexManager.App package Semi.Avalonia --version 11.3.*`.

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
        <semi:SemiTheme />
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

Note: the exact `Semi.Avalonia` include element/namespace and Semi resource keys (such as the border brush used in Task 13) are package-specific API surface that differs across versions. Before finishing this task, open the installed `Semi.Avalonia` package README (or its `nupkg` `Themes` folder) and confirm: (a) the exact `SemiTheme`/`StyleInclude` line, and (b) the border brush resource key you will reference in Task 13 (commonly `SemiColorBorder`). Add a `Locale` attribute to `SemiTheme` only if the package README documents one. Record the confirmed border key in a comment in `Tokens.axaml` so Task 13 does not rediscover it. This is a third-party integration point verified by the build, not a design choice.

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

First extract the concurrency probe into a shared fake so it is not copy-pasted. Create `tests/SmartIndexManager.App.Tests/Fakes/ConcurrencyProbeProvider.cs` by moving the `ConcurrencyProbeProvider` inner class out of `DetailConcurrencyTests.cs` into a `public sealed class ConcurrencyProbeProvider : IIndexProvider` in namespace `SmartIndexManager.App.Tests.Fakes` (body unchanged), and update `DetailConcurrencyTests.cs` to reference the shared type (add `using SmartIndexManager.App.Tests.Fakes;`, delete its inner copy). `DetailConcurrencyTests.cs` is deleted at cutover in Task 13; until then both test classes share the one probe. Then create `tests/SmartIndexManager.App.Tests/ViewModels/BrowseConcurrencyTests.cs` driving `BrowseViewModel` with the shared probe:

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
        Grid.PropertyChanged += OnGridPropertyChanged;
    }

    private void OnGridPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IndexGridViewModel.SelectedRow))
            _ = ShowDetailAsync(Grid.SelectedRow);
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
            catch (Exception ex)
            {
                ErrorMessage = $"{_loc["Detail_Error"]}: {ex.Message}";
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
        Grid.PropertyChanged -= OnGridPropertyChanged;
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

    private sealed class ThrowingFactory : IIndexProviderFactory
    {
        public Task<IIndexProvider> ConnectAsync(ConnectionRequest request, string? password, CancellationToken ct = default)
            => throw new InvalidOperationException("connection refused");
    }

    private (ConnectionSessionViewModel vm, RecordingDialogs dialogs) Build(string? password = "pw")
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
            new StubPrompt(password), connections, dialogs, new ResxLocalizer());
        return (vm, dialogs);
    }

    private ConnectionSessionViewModel BuildFailing()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ConnectionSessionViewModel(
            new IndexLoadService(new ThrowingFactory(), paths),
            new StubPrompt("pw"), connections, dialogs, new ResxLocalizer());
        return vm;
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

    [Fact]
    public async Task Connect_does_nothing_when_the_password_prompt_is_cancelled()
    {
        var (vm, _) = Build(password: null);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsConnected);
        Assert.Null(vm.ActiveProvider);
    }

    [Fact]
    public async Task Connect_sets_error_status_and_clears_busy_on_failure()
    {
        var vm = BuildFailing();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(Strings.Connection_Error, vm.StatusMessage);
        Assert.False(vm.IsBusy);
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
        Assert.Equal(5, shell.Destinations.Count);
        Assert.Same(shell.Destinations[0], shell.SelectedDestination);
        Assert.IsType<BrowseViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void Selecting_a_destination_sets_current_page()
    {
        var shell = Build();
        var last = shell.Destinations[^1];   // Settings, by position (title is localized, so assert by position)
        shell.SelectedDestination = last;
        Assert.Same(last.PageViewModel, shell.CurrentPage);
        Assert.IsType<PlaceholderPageViewModel>(shell.CurrentPage);
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
        Connection.Connected -= OnConnectedAsync;
        Connection.Disconnected -= OnDisconnectedAsync;
        await Connection.DisposeAsync().ConfigureAwait(true);
        await _browse.DisposeAsync().ConfigureAwait(true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~ShellViewModelTests"`
Expected: PASS (4 tests).

Verified: `ResxLocalizer` returns `"[key]"` for a missing key and never throws (see `src/SmartIndexManager.App/Localization/ResxLocalizer.cs`), so the `ShellViewModel` constructor is safe even though the `Nav_*` and `Placeholder_Message` keys are not added until Task 8. The tests assert by destination position and page-VM type, not by localized title, so they pass regardless of task order.

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
- Produces these NEW resource keys: `Nav_Browse`, `Nav_Basket`, `Nav_Restore`, `Nav_Audit`, `Nav_Settings`, `Placeholder_Message`, `Connection_Disconnect`, `Connection_Manage`, `Connection_Prompt`, `Grid_MatchCount`, `Grid_EmptyTitle`, `Grid_EmptyMessage`, `Grid_NoMatchesTitle`, `Grid_NoMatchesMessage`, `Grid_ClearFilter`, `Detail_Empty`, `Detail_Copy`, `Detail_Section_Structure`, `Detail_Section_Usage`, `Detail_Section_ProviderProps`.
- REUSES these keys that already exist in `Strings.resx` (verified present; do not re-add): `App_Title`, `Action_Connect`, `Action_Cancel`, `Action_ToggleTheme`, `Connection_DatabasesWatermark`, `Connection_Title`, `Connection_Error`, `Grid_Filter`, `Grid_Column_Database/Schema/Table/Index/Type/SizeMb/Seeks/Scans/Updates/Score`, `Badge_NotDeletable/Redundant/ForeignKey/Hint`, `Detail_Ddl`, `Detail_ScoreFactors`, `Detail_Error`, `Detail_OldestSnapshot`. The Connect button reuses `Action_Connect`; the DDL card title reuses `Detail_Ddl`; the score card title reuses `Detail_ScoreFactors`.

- [ ] **Step 1: Add the new resx entries**

In `src/SmartIndexManager.App/Localization/Strings.resx`, add one `<data>` element per NEW key only (the reused keys above are already present):

```xml
<data name="Nav_Browse" xml:space="preserve"><value>Browse</value></data>
<data name="Nav_Basket" xml:space="preserve"><value>Deletion basket</value></data>
<data name="Nav_Restore" xml:space="preserve"><value>Restore</value></data>
<data name="Nav_Audit" xml:space="preserve"><value>Audit</value></data>
<data name="Nav_Settings" xml:space="preserve"><value>Settings</value></data>
<data name="Placeholder_Message" xml:space="preserve"><value>Planned for a future version.</value></data>
<data name="Connection_Disconnect" xml:space="preserve"><value>Disconnect</value></data>
<data name="Connection_Manage" xml:space="preserve"><value>Manage…</value></data>
<data name="Connection_Prompt" xml:space="preserve"><value>Connect to a server to browse indexes.</value></data>
<data name="Grid_MatchCount" xml:space="preserve"><value>{0} of {1} indexes</value></data>
<data name="Grid_EmptyTitle" xml:space="preserve"><value>No indexes</value></data>
<data name="Grid_EmptyMessage" xml:space="preserve"><value>Connect to a server to browse indexes.</value></data>
<data name="Grid_NoMatchesTitle" xml:space="preserve"><value>No matches</value></data>
<data name="Grid_NoMatchesMessage" xml:space="preserve"><value>No index matches the current filter.</value></data>
<data name="Grid_ClearFilter" xml:space="preserve"><value>Clear filter</value></data>
<data name="Detail_Empty" xml:space="preserve"><value>Select an index to see its details.</value></data>
<data name="Detail_Copy" xml:space="preserve"><value>Copy</value></data>
<data name="Detail_Section_Structure" xml:space="preserve"><value>Structure</value></data>
<data name="Detail_Section_Usage" xml:space="preserve"><value>Usage</value></data>
<data name="Detail_Section_ProviderProps" xml:space="preserve"><value>Provider properties</value></data>
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
- Produces: a reusable `UserControl` rendering an icon, title, and message (no action button; none of the current empty states needs one, YAGNI). Bound via `DataContext` to any object exposing `IconKind` (`MaterialIconKind`), `Title` (`string`), `Message` (`string`); used directly for `PlaceholderPageViewModel`.

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
- Modify: `src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs`
- Create: `src/SmartIndexManager.App/Views/BrowseView.axaml`
- Create: `src/SmartIndexManager.App/Views/BrowseView.axaml.cs`
- Create: `src/SmartIndexManager.App/Views/BrowseStateConverters.cs`

**Interfaces:**
- Consumes: `BrowseViewModel` (`State`, `Grid`, `Detail`, `ErrorMessage`) and `IndexRowViewModel` (`IsScoreSafe/Caution/Risk`, `Score`, badge bools, `Seeks`/`Scans`/`Updates`/`SizeMb`).
- Produces: `IndexGridViewModel.TotalCount` (`int`) and `.MatchCountText` (`string`); the Browse page view. Detail cards come from Task 11 (`IndexDetailView`), referenced here.

- [ ] **Step 1: Add TotalCount and MatchCountText to IndexGridViewModel (TDD)**

Append to `tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs`:

```csharp
[Fact]
public void MatchCountText_reflects_filter_and_total()
{
    var vm = new IndexGridViewModel();   // no localizer -> "V of T" fallback
    IndexRowViewModel Row(string name) => new(
        SmartIndexManager.App.Tests.Fakes.IndexModelFactory.Nonclustered(name: name),
        null, new SmartIndexManager.Core.Safety.SafetyAssessment(SmartIndexManager.Core.Safety.DeletionEligibility.Deletable, null, []), false, false);

    vm.SetRows([Row("AAA"), Row("BBB"), Row("HR_legacy")]);
    Assert.Equal(3, vm.TotalCount);
    Assert.Equal("3 of 3", vm.MatchCountText);

    vm.FilterText = "HR";
    Assert.Equal("1 of 3", vm.MatchCountText);

    vm.FilterText = "";
    Assert.Equal("3 of 3", vm.MatchCountText);
}

[Fact]
public void Filter_flags_and_clear_command_reset_filter()
{
    var vm = new IndexGridViewModel();
    vm.SetRows([Row("AAA"), Row("BBB"), Row("HR_legacy")]);
    Assert.False(vm.IsFiltered);
    Assert.True(vm.HasVisibleRows);

    vm.FilterText = "ZZZ";
    Assert.True(vm.IsFiltered);
    Assert.False(vm.HasVisibleRows);

    vm.ClearFilterCommand.Execute(null);
    Assert.Equal("", vm.FilterText);
    Assert.False(vm.IsFiltered);
    Assert.True(vm.HasVisibleRows);
}
```

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexGridViewModelTests.MatchCountText|FullyQualifiedName~IndexGridViewModelTests.Filter_flags"`
Expected: FAIL (`TotalCount`/`MatchCountText` do not exist).

Then edit `src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs`: add `using SmartIndexManager.App.Localization;` and `using CommunityToolkit.Mvvm.Input;`, replace the constructor, `SetRows`, and `OnFilterTextChanged` with the versions below, and add the new members:

```csharp
    private readonly ILocalizer? _loc;

    public IndexGridViewModel(ILocalizer? loc = null)
    {
        _loc = loc;
        View = new DataGridCollectionView(_all) { Filter = Matches };
    }

    public int TotalCount => _all.Count;

    public string MatchCountText => _loc is not null
        ? string.Format(_loc["Grid_MatchCount"], VisibleCount, TotalCount)
        : $"{VisibleCount} of {TotalCount}";

    public bool IsFiltered => FilterText.Length > 0;

    public bool HasVisibleRows => VisibleCount > 0;

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    public void SetRows(IReadOnlyList<IndexRowViewModel> rows)
    {
        _all.Clear();
        _all.AddRange(rows);
        View.Refresh();
        NotifyCounts();
    }

    partial void OnFilterTextChanged(string value)
    {
        View.Refresh();
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(MatchCountText));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(HasVisibleRows));
    }
```

The optional `ILocalizer? loc = null` keeps every existing `new IndexGridViewModel()` call compiling; DI passes the registered `ILocalizer`.

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexGridViewModelTests.MatchCountText"`
Expected: PASS.

- [ ] **Step 2: Create the code-behind**

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

- [ ] **Step 3: Create the view**

Create `src/SmartIndexManager.App/Views/BrowseView.axaml`. This folds in the old `IndexGridView` markup, adds the score pill (class-bound, theme-reactive), token-based badges with icons, right-aligned formatted numerics, a filter toolbar with a live match count, a BrowseView-owned empty-detail state, and switches on `BrowseState`:

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
                               Text="{Binding Grid.MatchCountText}" />
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
                        <DataGridTemplateColumn Header="{x:Static loc:Strings.Grid_Column_SizeMb}" Width="Auto" MinWidth="70" SortMemberPath="SizeMb">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <TextBlock Text="{Binding SizeMb, StringFormat={}{0:N1}}" TextAlignment="Right" Margin="0,0,8,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="{x:Static loc:Strings.Grid_Column_Seeks}" Width="Auto" MinWidth="70" SortMemberPath="Seeks">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <TextBlock Text="{Binding Seeks, StringFormat={}{0:N0}}" TextAlignment="Right" Margin="0,0,8,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="{x:Static loc:Strings.Grid_Column_Scans}" Width="Auto" MinWidth="70" SortMemberPath="Scans">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <TextBlock Text="{Binding Scans, StringFormat={}{0:N0}}" TextAlignment="Right" Margin="0,0,8,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="{x:Static loc:Strings.Grid_Column_Updates}" Width="Auto" MinWidth="80" SortMemberPath="Updates">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:IndexRowViewModel">
                                    <TextBlock Text="{Binding Updates, StringFormat={}{0:N0}}" TextAlignment="Right" Margin="0,0,8,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
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
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <mi:MaterialIcon Kind="ContentDuplicate" Width="11" Height="11" Foreground="White" />
                                                <TextBlock Text="{x:Static loc:Strings.Badge_Redundant}" Foreground="White" FontSize="11" />
                                            </StackPanel>
                                        </Border>
                                        <Border Classes="badge" Background="{DynamicResource InfoBrush}" IsVisible="{Binding SupportsForeignKey}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <mi:MaterialIcon Kind="KeyLink" Width="11" Height="11" Foreground="White" />
                                                <TextBlock Text="{x:Static loc:Strings.Badge_ForeignKey}" Foreground="White" FontSize="11" />
                                            </StackPanel>
                                        </Border>
                                        <Border Classes="badge" Background="{DynamicResource AccentAltBrush}" IsVisible="{Binding ReferencedByHint}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <mi:MaterialIcon Kind="FlagOutline" Width="11" Height="11" Foreground="White" />
                                                <TextBlock Text="{x:Static loc:Strings.Badge_Hint}" Foreground="White" FontSize="11" />
                                            </StackPanel>
                                        </Border>
                                    </StackPanel>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>

                <!-- Empty results state (no rows at all, or filter with no matches) -->
                <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8"
                            IsVisible="{Binding !Grid.HasVisibleRows}">
                    <StackPanel IsVisible="{Binding Grid.IsFiltered}">
                        <mi:MaterialIcon Kind="FileSearchOutline" Width="48" Height="48" Opacity="0.6" HorizontalAlignment="Center" />
                        <TextBlock Classes="title" HorizontalAlignment="Center" Text="{x:Static loc:Strings.Grid_NoMatchesTitle}" />
                        <TextBlock Classes="caption" HorizontalAlignment="Center" Text="{x:Static loc:Strings.Grid_NoMatchesMessage}" />
                        <Button HorizontalAlignment="Center" Margin="0,4,0,0"
                                Content="{x:Static loc:Strings.Grid_ClearFilter}"
                                Command="{Binding Grid.ClearFilterCommand}" />
                    </StackPanel>
                    <StackPanel IsVisible="{Binding !Grid.IsFiltered}">
                        <mi:MaterialIcon Kind="DatabaseOffOutline" Width="48" Height="48" Opacity="0.6" HorizontalAlignment="Center" />
                        <TextBlock Classes="title" HorizontalAlignment="Center" Text="{x:Static loc:Strings.Grid_EmptyTitle}" />
                        <TextBlock Classes="caption" HorizontalAlignment="Center" Text="{x:Static loc:Strings.Grid_EmptyMessage}" />
                    </StackPanel>
                </StackPanel>
            </DockPanel>
            <GridSplitter Grid.Column="1" Width="4" />
            <Panel Grid.Column="2">
                <views:IndexDetailView DataContext="{Binding Detail}"
                                       IsVisible="{Binding Detail, Converter={x:Static ObjectConverters.IsNotNull}}" />
                <TextBlock Classes="caption" Text="{x:Static loc:Strings.Detail_Empty}"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           IsVisible="{Binding Detail, Converter={x:Static ObjectConverters.IsNull}}" />
            </Panel>
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

Note on the numeric columns: they use `DataGridTemplateColumn` with a right-aligned `TextBlock` plus `SortMemberPath` (which keeps them sortable), rather than `DataGridTextColumn.ElementStyle`. Avalonia's `DataGridTextColumn` does not expose a WPF-style `ElementStyle` for per-cell text alignment, so the template-column form is the reliable way to right-align while preserving sort.

- [ ] **Step 4: Add the BrowseState converters**

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

- [ ] **Step 5: Build and test to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds. `IndexDetailView` is referenced; it still exists from before this task (redesigned in Task 11) and binds properties that still exist on `IndexDetailViewModel`, so the build passes.

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexGridViewModelTests"`
Expected: PASS, including the new match-count test.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexGridViewModel.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexGridViewModelTests.cs src/SmartIndexManager.App/Views/BrowseView.axaml src/SmartIndexManager.App/Views/BrowseView.axaml.cs src/SmartIndexManager.App/Views/BrowseStateConverters.cs
git commit -m "feat(app): BrowseView with score pills, icon badges, right-aligned numerics, match count, and state switching"
```

---

## Task 11: IndexDetailView redesigned as titled cards

**Files:**
- Modify: `src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs`
- Modify: `src/SmartIndexManager.App/Views/IndexDetailView.axaml`
- Modify: `src/SmartIndexManager.App/Views/IndexDetailView.axaml.cs`
- Test: `tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs`

**Interfaces:**
- Produces on `IndexDetailViewModel` (populated in `ShowAsync` from the selected `IndexRowViewModel`): `HeaderName`, `TypeText`, `KeyColumnsText`, `IncludesText` (`string`), `IsUnique`, `IsRedundant` (`bool`), `Score` (`int?`), `IsScoreSafe`/`IsScoreCaution`/`IsScoreRisk` (`bool`), `ProviderProps` (`ObservableCollection<KeyValuePair<string,string>>`). Existing `Ddl`, `OldestSnapshotText`, `ScoreFactors` remain.

Cards implemented: header (name, type, score pill), DDL (with copy), structure (key columns, includes), usage, score explanation, provider properties (collapsible). Deferred with an explicit note: the full redundancy card (covering index + rule R1/R2/R3) is out of scope here because the covering-index pairing is not yet threaded to the detail VM; a minimal "redundant" card is shown when `IsRedundant` is true.

- [ ] **Step 1: Extend IndexDetailViewModel (TDD)**

Append to `tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs` (create the file if absent, matching the existing test namespace):

```csharp
[Fact]
public async Task ShowAsync_populates_header_score_and_redundancy()
{
    var dir = Directory.CreateTempSubdirectory("sim-detailvm-").FullName;
    try
    {
        var provider = new SmartIndexManager.App.Tests.Fakes.FakeIndexProvider
        {
            ServerInfo = new SmartIndexManager.Core.Provider.ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = SmartIndexManager.Core.Provider.ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new SmartIndexManager.Core.Provider.ProviderCapabilities(),
            Permissions = new SmartIndexManager.Core.Provider.PermissionReport(),
            Indexes = []
        };
        var vm = new IndexDetailViewModel(provider, new SmartIndexManager.App.Services.AppPaths(dir, dir, dir), new SmartIndexManager.App.Localization.ResxLocalizer());
        var index = SmartIndexManager.App.Tests.Fakes.IndexModelFactory.Nonclustered(name: "IX_Test");
        var score = new SmartIndexManager.Core.Scoring.ConfidenceScore(90, SmartIndexManager.Core.Scoring.ScoreColor.Green, []);
        var safety = new SmartIndexManager.Core.Safety.SafetyAssessment(SmartIndexManager.Core.Safety.DeletionEligibility.Deletable, null, []);
        var row = new IndexRowViewModel(index, score, safety, isRedundant: true, isReferencedByHint: false);

        await vm.ShowAsync(row, CancellationToken.None);

        Assert.Equal("IX_Test", vm.HeaderName);
        Assert.Equal(90, vm.Score);
        Assert.True(vm.IsScoreSafe);
        Assert.True(vm.IsRedundant);
    }
    finally { Directory.Delete(dir, recursive: true); }
}
```

Confirm the `ConfidenceScore` and `SafetyAssessment` constructor shapes from Core and adjust the two `new(...)` calls if needed (only those lines).

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexDetailViewModelTests.ShowAsync_populates"`
Expected: FAIL (`HeaderName` etc. do not exist).

Then edit `src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs`: add `using System.Linq;`, add the fields, and populate them at the top of `ShowAsync` (right after `var index = row.Index;`):

```csharp
    [ObservableProperty] private string _headerName = "";
    [ObservableProperty] private string _typeText = "";
    [ObservableProperty] private string _keyColumnsText = "";
    [ObservableProperty] private string _includesText = "";
    [ObservableProperty] private bool _isUnique;
    [ObservableProperty] private bool _isRedundant;
    [ObservableProperty] private int? _score;
    [ObservableProperty] private bool _isScoreSafe;
    [ObservableProperty] private bool _isScoreCaution;
    [ObservableProperty] private bool _isScoreRisk;

    public ObservableCollection<KeyValuePair<string, string>> ProviderProps { get; } = [];
```

```csharp
        // Populate the header/structure/score cards from the selected row (no provider round-trip).
        HeaderName = index.Name;
        TypeText = index.Type.ToString();
        KeyColumnsText = string.Join(", ", index.KeyColumns.Select(c => c.Name));
        IncludesText = string.Join(", ", index.IncludedColumns);
        IsUnique = index.IsUnique;
        IsRedundant = row.Redundant;
        Score = row.Score;
        IsScoreSafe = row.IsScoreSafe;
        IsScoreCaution = row.IsScoreCaution;
        IsScoreRisk = row.IsScoreRisk;
        ProviderProps.Clear();
        foreach (var kv in index.ProviderProperties)
            ProviderProps.Add(new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));
```

Confirm the `ProviderProperties` value type in `IndexModel`; `kv.Value?.ToString()` is safe whether it is `object?` or `string`.

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexDetailViewModelTests.ShowAsync_populates"`
Expected: PASS.

- [ ] **Step 2: Rewrite the detail view as cards**

Replace the contents of `src/SmartIndexManager.App/Views/IndexDetailView.axaml` with:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SmartIndexManager.App.ViewModels"
             xmlns:loc="clr-namespace:SmartIndexManager.App.Localization"
             xmlns:mi="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             xmlns:scoring="clr-namespace:SmartIndexManager.Core.Scoring;assembly=SmartIndexManager.Core"
             x:Class="SmartIndexManager.App.Views.IndexDetailView"
             x:DataType="vm:IndexDetailViewModel">
    <UserControl.Styles>
        <Style Selector="Border.card">
            <Setter Property="Background" Value="{DynamicResource SurfaceCardBrush}" />
            <Setter Property="CornerRadius" Value="{StaticResource RadiusMd}" />
            <Setter Property="Padding" Value="12" />
            <Setter Property="Margin" Value="0,0,0,8" />
        </Style>
        <Style Selector="Border.score-pill">
            <Setter Property="CornerRadius" Value="{StaticResource RadiusSm}" />
            <Setter Property="Padding" Value="8,2" />
        </Style>
        <Style Selector="Border.score-pill.score-safe"><Setter Property="Background" Value="{DynamicResource ScoreSafeBrush}" /></Style>
        <Style Selector="Border.score-pill.score-caution"><Setter Property="Background" Value="{DynamicResource ScoreCautionBrush}" /></Style>
        <Style Selector="Border.score-pill.score-risk"><Setter Property="Background" Value="{DynamicResource ScoreRiskBrush}" /></Style>
    </UserControl.Styles>
    <ScrollViewer>
        <StackPanel Margin="8">

            <Border Classes="card">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel Grid.Column="0" Spacing="2">
                        <TextBlock Classes="title" Text="{Binding HeaderName}" />
                        <TextBlock Classes="caption" Text="{Binding TypeText}" />
                    </StackPanel>
                    <Border Grid.Column="1" Classes="score-pill" VerticalAlignment="Center"
                            Classes.score-safe="{Binding IsScoreSafe}"
                            Classes.score-caution="{Binding IsScoreCaution}"
                            Classes.score-risk="{Binding IsScoreRisk}"
                            IsVisible="{Binding Score, Converter={x:Static ObjectConverters.IsNotNull}}">
                        <TextBlock Text="{Binding Score}" Foreground="White" />
                    </Border>
                </Grid>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="4">
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock Grid.Column="0" Classes="subtitle" Text="{x:Static loc:Strings.Detail_Ddl}" />
                        <Button Grid.Column="1" Click="OnCopyDdl" Padding="4,0"
                                ToolTip.Tip="{x:Static loc:Strings.Detail_Copy}">
                            <mi:MaterialIcon Kind="ContentCopy" Width="14" Height="14" />
                        </Button>
                    </Grid>
                    <TextBox Classes="code" Text="{Binding Ddl}" IsReadOnly="True"
                             AcceptsReturn="True" TextWrapping="NoWrap"
                             HorizontalScrollBarVisibility="Auto" />
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="4">
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Detail_Section_Structure}" />
                    <TextBlock Classes="caption" Text="{Binding KeyColumnsText}" />
                    <TextBlock Classes="caption" Text="{Binding IncludesText}" />
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
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Detail_ScoreFactors}" />
                    <ItemsControl ItemsSource="{Binding ScoreFactors}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="scoring:ScoreFactor">
                                <Grid ColumnDefinitions="Auto,*" Margin="0,1">
                                    <TextBlock Grid.Column="0" Classes="caption" FontWeight="SemiBold"
                                               Text="{Binding Name}" Margin="0,0,8,0" />
                                    <TextBlock Grid.Column="1" Classes="caption" TextWrapping="Wrap"
                                               Text="{Binding Description}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <!-- Minimal redundancy indicator. Full covering-index + rule detail deferred (data not yet threaded). -->
            <Border Classes="card" IsVisible="{Binding IsRedundant}">
                <StackPanel Spacing="4">
                    <TextBlock Classes="subtitle" Text="{x:Static loc:Strings.Badge_Redundant}" />
                    <mi:MaterialIcon Kind="ContentDuplicate" Width="16" Height="16" HorizontalAlignment="Left" />
                </StackPanel>
            </Border>

            <Expander Header="{x:Static loc:Strings.Detail_Section_ProviderProps}" Margin="0,0,0,8">
                <ItemsControl ItemsSource="{Binding ProviderProps}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid ColumnDefinitions="Auto,*" Margin="0,1">
                                <TextBlock Grid.Column="0" Classes="caption" FontWeight="SemiBold"
                                           Text="{Binding Key}" Margin="0,0,8,0" />
                                <TextBlock Grid.Column="1" Classes="caption" Text="{Binding Value}" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Expander>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

The empty-detail state is handled by `BrowseView` (Task 10), so this view assumes a non-null detail VM. Confirm `ScoreFactor`'s members are `Name` and `Description` (verified: `record ScoreFactor(string Name, string Description)`).

- [ ] **Step 3: Add the copy-to-clipboard handler**

Replace `src/SmartIndexManager.App/Views/IndexDetailView.axaml.cs` with:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public partial class IndexDetailView : UserControl
{
    public IndexDetailView() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyDdl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IndexDetailViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.Ddl);
    }
}
```

- [ ] **Step 4: Build and test to verify**

Run: `dotnet build SmartIndexManager.sln`
Expected: build succeeds, 0 errors.

Run: `dotnet test tests/SmartIndexManager.App.Tests --filter "FullyQualifiedName~IndexDetailViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.App/ViewModels/IndexDetailViewModel.cs src/SmartIndexManager.App/Views/IndexDetailView.axaml src/SmartIndexManager.App/Views/IndexDetailView.axaml.cs tests/SmartIndexManager.App.Tests/ViewModels/IndexDetailViewModelTests.cs
git commit -m "feat(app): IndexDetailView as titled cards (header, DDL+copy, structure, usage, score, provider props)"
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
                    <Button Content="{x:Static loc:Strings.Action_Connect}" Command="{Binding Connection.ConnectCommand}"
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

- [ ] **Step 5: Confirm no lingering references, then delete superseded files**

First confirm nothing outside the files being removed still references the old shell VM or the folded-in grid view (the distinct `IndexGridViewModel` type stays):

Run: `grep -rn "MainWindowViewModel\|IndexGridView\b" src tests --include=*.cs --include=*.axaml`
Expected: matches only inside the five files listed below (and none referencing the `IndexGridView` control elsewhere). `IndexGridViewModel` matches are fine and must remain.

Then:

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

First see whether `Avalonia.Themes.Fluent` is still needed at all:

Run: `dotnet list src/SmartIndexManager.App package --include-transitive`
Inspect whether `Avalonia.Themes.Fluent` appears only transitively (pulled by `Avalonia.Desktop`/Semi) or is referenced directly. There is no direct `<PackageReference>` to it in the current `.csproj`, so removal is only about any `<FluentTheme />` still present in `App.axaml`.

Then confirm no `<FluentTheme />` element remains in `App.axaml` (Task 1 already replaced it with `SemiTheme`). If one remains, remove it, rebuild, and if a display is available, launch. If any control renders unstyled or the app throws a missing-resource exception, restore `<FluentTheme />` above `SemiTheme` with a one-line comment explaining why. This is expected to be optional cleanup.

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
- Resolved during review: `ResxLocalizer` returns `"[key]"` on a missing key (verified, no throw), and `ScoreFactor` is `record ScoreFactor(string Name, string Description)` (verified). Most view keys (`Grid_Column_*`, `Badge_*`, `Action_*`, `Detail_Ddl`, `Detail_ScoreFactors`) already exist in `Strings.resx`; Task 8 adds only the genuinely new keys.
- Known verification hooks left for the implementer (all build- or test-gated, none are placeholders for our own logic): the exact `Semi.Avalonia` include line, its Avalonia-11.3-compatible version, and its border resource key (Task 1, recorded for Task 13); the `ConfidenceScore` and `SafetyAssessment` constructor shapes and the `IndexModel.ProviderProperties` value type from Core (Tasks 3, 11).
