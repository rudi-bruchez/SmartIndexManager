# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SmartIndexManager is a cross-platform Avalonia desktop tool for DBAs to manage SQL Server indexes: list indexes, see which are actually used, and drop the ones that are safe to remove, with guardrails, a dry-run impact report, systematic DDL backup, and integrated restore. SQL Server is the first engine; the provider architecture is designed so PostgreSQL and others can follow. .NET 10, C#, `Nullable` and `ImplicitUsings` enabled everywhere.

Design is fully specified in `docs/specs/2026-07-20-smartindexmanager-design.md` (French, authoritative). Per-area implementation plans and locked API contracts live in `docs/plans/`. When intended behavior is unclear, the spec wins over inference from partial code.

## Build, test, run

```bash
dotnet build SmartIndexManager.sln               # build everything
dotnet test                                      # run all test projects
dotnet run --project src/SmartIndexManager.App   # launch the GUI

# Single test project
dotnet test tests/SmartIndexManager.Core.Tests

# Single test class or method (xUnit filter)
dotnet test --filter "FullyQualifiedName~RedundancyR1Tests"
dotnet test --filter "DisplayName~drops_a_safe_index"
```

There is no `Directory.Build.props`, `global.json`, or separate lint step; the SDK-default analyzers and a zero-warning compiler are the bar. The `read` shell builtin is shadowed by an RTK alias in this environment, so prefer the Read tool over bash `cat`/`read` loops.

## Architecture

Three strictly separated layers; dependencies flow downward only:

- `src/SmartIndexManager.Core` : domain model, redundancy engine, confidence scoring, DDL generation, safety evaluation, snapshots, manifests, audit, and the SQL-file header parser. No UI, no `SqlClient`, no database I/O. Everything here is unit-testable without a server.
- `src/SmartIndexManager.Providers.SqlServer` : the `IIndexProvider` implementation. Connects, runs the external SQL files, maps result rows to the common model. Depends on Core only. A future `Providers.PostgreSql` follows the same contract.
- `src/SmartIndexManager.App` : Avalonia 11 UI, CommunityToolkit.Mvvm ViewModels, DI via `Microsoft.Extensions.DependencyInjection`. Consumes Core through interfaces; the provider is injected via `IIndexProviderFactory`.

Hard design constraint: every feature must be reachable from an xUnit test or a future CLI without instantiating the UI. Keep logic out of ViewModels and code-behind when it belongs in Core or a service.

The provider (`SqlServerIndexProvider`) is split across partial-class files by concern: `.cs` (ctor/state), `.Indexes.cs`, `.Diagnostics.cs`, `.Actions.cs` (DROP, Query Store enable). `ISqlExecutor` / `SqlClientExecutor` is the seam that lets tests fake the database.

## Rules that are easy to get wrong

External SQL files are the only source of queries. Every server query lives in `sql/sqlserver/<name>.sql`. There is no embedded fallback by design: a missing, unreadable, or invalid file marks that one feature as errored (with an explicit message) and must not crash the app. `SqlScriptLoader.Load` requires the file's `-- sim: name=` header to match the logical name it is loaded as. Columns are read by name (from the `-- sim: columns=` header), never by position. Parameters go through named `SqlCommand` parameters (`@SchemaName`, ...), never string concatenation. When adding a query, add both the `.sql` file (with header) and an `InlineData` case in `ScriptContractTests`.

Capabilities, not versions. Feature availability is decided via `ProviderCapabilities` (`SupportsQueryStore`, `RequiresDatabaseScopedDmv` for Azure, and so on). Never branch on a raw SQL Server version number in Core or App.

Deletion safety is layered. Core's `DeletionSafetyEvaluator` decides eligibility; the provider's `DropIndexAsync` re-validates with `index-droppable-check.sql` as defense-in-depth before issuing `DROP INDEX`. Only plain nonclustered rowstore non-unique indexes on user tables are droppable. Clustered, columnstore, XML, spatial, fulltext, unique (with or without constraint), PK, disabled, hypothetical, and system indexes are hard-excluded and cannot enter the deletion basket.

Recreation DDL is generated in C# (`SqlServerDdlGenerator`), not by a SQL query, so it stays deterministic and testable without a database. An index whose DDL cannot be guaranteed (partitioning, unsupported options) is refused for deletion.

Single connection, no MARS. The SQL Server provider uses one connection without Multiple Active Result Sets, so overlapping commands break it. Detail loads in `MainWindowViewModel` are serialized with a `SemaphoreSlim(1,1)` gate: cancel any in-flight load, acquire the gate, re-check state after the wait, run, release in `finally`. Preserve this pattern when touching detail or async-load code.

Secrets. SQL passwords are never persisted in any form; they are prompted on each connect. Do not add password storage.

Snapshots are keyed by server instance name then database: `<configDir>/snapshots/<server>/<database>/<timestamp>.json`, where server is `provider.ServerInfo.ServerName`, not the database name. At runtime `SqlScriptRoot` resolves to `AppContext.BaseDirectory/sql/sqlserver`; tests walk up the tree to the repo-root `sql/sqlserver`.

## Testing

xUnit across all three test projects. Core and App tests need no database. Provider integration tests use Testcontainers (`Testcontainers.MsSql`) and are gated by `[RequiresDockerFact]`, which auto-skips when `docker info` fails, so `dotnet test` is green without Docker. Provider unit tests (`tests/.../Unit/`) exercise gates and guards through `RecordingExecutor` with no container. The provider project exposes internals to its test project via `InternalsVisibleTo`.

<!-- rtk-instructions v2 -->
# RTK (Rust Token Killer) - Token-Optimized Commands

## Golden Rule

**Always prefix commands with `rtk`**. If RTK has a dedicated filter, it uses it. If not, it passes through unchanged. This means RTK is always safe to use.

**Important**: Even in command chains with `&&`, use `rtk`:
```bash
# ❌ Wrong
git add . && git commit -m "msg" && git push

# ✅ Correct
rtk git add . && rtk git commit -m "msg" && rtk git push
```

## RTK Commands by Workflow

### Build & Compile (80-90% savings)
```bash
rtk cargo build         # Cargo build output
rtk cargo check         # Cargo check output
rtk cargo clippy        # Clippy warnings grouped by file (80%)
rtk tsc                 # TypeScript errors grouped by file/code (83%)
rtk lint                # ESLint/Biome violations grouped (84%)
rtk prettier --check    # Files needing format only (70%)
rtk next build          # Next.js build with route metrics (87%)
```

### Test (90-99% savings)
```bash
rtk cargo test          # Cargo test failures only (90%)
rtk vitest run          # Vitest failures only (99.5%)
rtk playwright test     # Playwright failures only (94%)
rtk test <cmd>          # Generic test wrapper - failures only
```

### Git (59-80% savings)
```bash
rtk git status          # Compact status
rtk git log             # Compact log (works with all git flags)
rtk git diff            # Compact diff (80%)
rtk git show            # Compact show (80%)
rtk git add             # Ultra-compact confirmations (59%)
rtk git commit          # Ultra-compact confirmations (59%)
rtk git push            # Ultra-compact confirmations
rtk git pull            # Ultra-compact confirmations
rtk git branch          # Compact branch list
rtk git fetch           # Compact fetch
rtk git stash           # Compact stash
rtk git worktree        # Compact worktree
```

Note: Git passthrough works for ALL subcommands, even those not explicitly listed.

### GitHub (26-87% savings)
```bash
rtk gh pr view <num>    # Compact PR view (87%)
rtk gh pr checks        # Compact PR checks (79%)
rtk gh run list         # Compact workflow runs (82%)
rtk gh issue list       # Compact issue list (80%)
rtk gh api              # Compact API responses (26%)
```

### JavaScript/TypeScript Tooling (70-90% savings)
```bash
rtk pnpm list           # Compact dependency tree (70%)
rtk pnpm outdated       # Compact outdated packages (80%)
rtk pnpm install        # Compact install output (90%)
rtk npm run <script>    # Compact npm script output
rtk npx <cmd>           # Compact npx command output
rtk prisma              # Prisma without ASCII art (88%)
```

### Files & Search (60-75% savings)
```bash
rtk ls <path>           # Tree format, compact (65%)
rtk read <file>         # Code reading with filtering (60%)
rtk grep <pattern>      # Search grouped by file (75%)
rtk find <pattern>      # Find grouped by directory (70%)
```

### Analysis & Debug (70-90% savings)
```bash
rtk err <cmd>           # Filter errors only from any command
rtk log <file>          # Deduplicated logs with counts
rtk json <file>         # JSON structure without values
rtk deps                # Dependency overview
rtk env                 # Environment variables compact
rtk summary <cmd>       # Smart summary of command output
rtk diff                # Ultra-compact diffs
```

### Infrastructure (85% savings)
```bash
rtk docker ps           # Compact container list
rtk docker images       # Compact image list
rtk docker logs <c>     # Deduplicated logs
rtk kubectl get         # Compact resource list
rtk kubectl logs        # Deduplicated pod logs
```

### Network (65-70% savings)
```bash
rtk curl <url>          # Compact HTTP responses (70%)
rtk wget <url>          # Compact download output (65%)
```

### Meta Commands
```bash
rtk gain                # View token savings statistics
rtk gain --history      # View command history with savings
rtk discover            # Analyze Claude Code sessions for missed RTK usage
rtk proxy <cmd>         # Run command without filtering (for debugging)
rtk init                # Add RTK instructions to CLAUDE.md
rtk init --global       # Add RTK to ~/.claude/CLAUDE.md
```

## Token Savings Overview

| Category | Commands | Typical Savings |
|----------|----------|-----------------|
| Tests | vitest, playwright, cargo test | 90-99% |
| Build | next, tsc, lint, prettier | 70-87% |
| Git | status, log, diff, add, commit | 59-80% |
| GitHub | gh pr, gh run, gh issue | 26-87% |
| Package Managers | pnpm, npm, npx | 70-90% |
| Files | ls, read, grep, find | 60-75% |
| Infrastructure | docker, kubectl | 85% |
| Network | curl, wget | 65-70% |

Overall average: **60-90% token reduction** on common development operations.
<!-- /rtk-instructions -->
