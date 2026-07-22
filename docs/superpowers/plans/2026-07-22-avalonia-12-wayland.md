# Avalonia 12 + Wayland Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade SmartIndexManager.App to Avalonia 12.1.0 and enable native Wayland support with X11 fallback.

**Architecture:** Bump all Avalonia ecosystem packages to 12.1.0, add `Avalonia.Wayland`, and chain `UseWaylandWithFallback()` in `Program.cs`. Then iteratively build and fix Avalonia 11→12 breaking changes until the solution compiles and all tests pass.

**Tech Stack:** .NET 10, Avalonia 12.1.0, Semi.Avalonia 12.1.0, Material.Icons.Avalonia 3.0.2, xUnit.

## Global Constraints

- Avalonia packages must all use 12.1.0.
- `Avalonia.Wayland` 12.1.0 is required for Wayland support.
- `Material.Icons.Avalonia` must be updated to 3.0.2 for Avalonia 12 compatibility.
- `dotnet build SmartIndexManager.sln` must produce 0 errors and 0 warnings.
- `dotnet test` must pass all 233 tests.

---

### Task 1: Update Avalonia core packages

**Files:**
- Modify: `src/SmartIndexManager.App/SmartIndexManager.App.csproj`

**Interfaces:**
- Consumes: existing package references
- Produces: updated package references for Avalonia 12.1.0

- [ ] **Step 1: Update Avalonia package versions**

  In `src/SmartIndexManager.App/SmartIndexManager.App.csproj`, change:

  ```xml
  <PackageReference Include="Avalonia" Version="11.3.18" />
  <PackageReference Include="Avalonia.Controls.DataGrid" Version="11.3.13" />
  <PackageReference Include="Avalonia.Desktop" Version="11.3.18" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.18" />
  ```

  to:

  ```xml
  <PackageReference Include="Avalonia" Version="12.1.0" />
  <PackageReference Include="Avalonia.Controls.DataGrid" Version="12.1.0" />
  <PackageReference Include="Avalonia.Desktop" Version="12.1.0" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.0" />
  ```

- [ ] **Step 2: Add Avalonia.Wayland package**

  Add the new package reference inside the same `ItemGroup`:

  ```xml
  <PackageReference Include="Avalonia.Wayland" Version="12.1.0" />
  ```

- [ ] **Step 3: Restore packages**

  Run:

  ```bash
  dotnet restore SmartIndexManager.sln
  ```

  Expected: restore succeeds with no package resolution errors.

---

### Task 2: Update theme and icon packages

**Files:**
- Modify: `src/SmartIndexManager.App/SmartIndexManager.App.csproj`

**Interfaces:**
- Consumes: existing `Semi.Avalonia` and `Material.Icons.Avalonia` references
- Produces: updated compatible versions

- [ ] **Step 1: Update Semi.Avalonia**

  Change:

  ```xml
  <PackageReference Include="Semi.Avalonia" Version="11.3.*" />
  ```

  to:

  ```xml
  <PackageReference Include="Semi.Avalonia" Version="12.1.0" />
  ```

- [ ] **Step 2: Update Material.Icons.Avalonia**

  Change:

  ```xml
  <PackageReference Include="Material.Icons.Avalonia" Version="2.1.12" />
  ```

  to:

  ```xml
  <PackageReference Include="Material.Icons.Avalonia" Version="3.0.2" />
  ```

- [ ] **Step 3: Restore and check lock file**

  Run:

  ```bash
  dotnet restore SmartIndexManager.sln
  ```

  Expected: restore succeeds.

---

### Task 3: Enable Wayland fallback in Program.cs

**Files:**
- Modify: `src/SmartIndexManager.App/Program.cs`

**Interfaces:**
- Consumes: `AppBuilder` from Avalonia
- Produces: `UseWaylandWithFallback()` chained after `UsePlatformDetect()`

- [ ] **Step 1: Add Wayland fallback call**

  Replace:

  ```csharp
  public static AppBuilder BuildAvaloniaApp() =>
      AppBuilder.Configure<App>()
          .UsePlatformDetect()
          .WithInterFont()
          .LogToTrace();
  ```

  with:

  ```csharp
  public static AppBuilder BuildAvaloniaApp() =>
      AppBuilder.Configure<App>()
          .UsePlatformDetect()
          .UseWaylandWithFallback()
          .WithInterFont()
          .LogToTrace();
  ```

- [ ] **Step 2: Verify no compile error yet**

  Note: the method may not resolve until packages are restored; this is expected to be validated in Task 4.

---

### Task 4: Build and fix Avalonia 11→12 breaking changes

**Files:**
- Modify: any `*.cs` or `*.axaml` files that fail to compile

**Interfaces:**
- Consumes: updated Avalonia 12 APIs
- Produces: code compatible with Avalonia 12

- [ ] **Step 1: Run build**

  Run:

  ```bash
  dotnet build SmartIndexManager.sln
  ```

  Expected: build may fail with Avalonia 12 migration errors.

- [ ] **Step 2: Fix compilation errors one by one**

  For each error:

  1. Read the error message and locate the file/line.
  2. Check Avalonia 12 release notes or API changes for the replacement.
  3. Apply the minimal fix.
  4. Re-run `dotnet build SmartIndexManager.sln`.

  Common patterns to watch for:

  - Theme API changes in `App.axaml` or `App.axaml.cs`.
  - `DataGrid` style or namespace changes.
  - `MaterialIconStyles` initialization changes in `App.axaml`.
  - `Window` or `TopLevel` API changes.

- [ ] **Step 3: Achieve zero warnings**

  Run:

  ```bash
  dotnet build SmartIndexManager.sln
  ```

  Expected: 0 errors, 0 warnings.

---

### Task 5: Run tests

**Files:**
- None (verification only)

**Interfaces:**
- Consumes: fully migrated solution
- Produces: green test suite

- [ ] **Step 1: Run all tests**

  Run:

  ```bash
  dotnet test
  ```

  Expected: 233 tests passed, 0 warnings in 3 projects.

- [ ] **Step 2: If tests fail, fix them**

  Investigate any failing test, apply minimal fix, and re-run `dotnet test`.

---

### Task 6: Verify application launch

**Files:**
- None (runtime verification)

**Interfaces:**
- Consumes: built application
- Produces: confirmed UI startup

- [ ] **Step 1: Launch the GUI**

  Run:

  ```bash
  dotnet run --project src/SmartIndexManager.App
  ```

  Expected: application window opens, Semi theme renders, no unhandled exception.

- [ ] **Step 2: Check Wayland on Linux (if available)**

  On a Wayland session, run:

  ```bash
  dotnet run --project src/SmartIndexManager.App
  ```

  Verify via `xeyes` or `wlroots` tools that the window is a native Wayland client, not XWayland.

---

### Task 7: Update docs and finalize

**Files:**
- Modify: `docs/superpowers/specs/2026-07-22-avalonia-12-wayland-design.md` if needed
- Modify: any `AGENTS.md` or README if mentioned package versions are documented

**Interfaces:**
- Consumes: final implementation state
- Produces: up-to-date documentation

- [ ] **Step 1: Update spec if breaking changes required notable design shifts**

  If the migration required non-trivial API changes, document them in the spec.

- [ ] **Step 2: Review git status**

  Run:

  ```bash
  git status
  ```

  Ensure only intended files are modified.

- [ ] **Step 3: Summarize changes for the user**

  Report: package versions, `Program.cs` change, breaking changes fixed, test results.
