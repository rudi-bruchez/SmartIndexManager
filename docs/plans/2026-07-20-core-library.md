# SmartIndexManager.Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `SmartIndexManager.Core`, the database-agnostic and UI-agnostic library that holds the index model, redundancy engine, confidence scoring, T-SQL DDL generation, deletion-safety rules, SQL header parsing, and persistence of manifests, snapshots, backups and audit log. Everything is unit-testable in xUnit without a database or an Avalonia UI.

**Architecture:** One .NET 10 class library plus one xUnit test project. No dependency on `Microsoft.Data.SqlClient`, no dependency on Avalonia. Pure domain logic and file I/O only. A later plan adds `SmartIndexManager.Providers.SqlServer`; another adds `SmartIndexManager.App`. This library is the foundation both consume, and a future CLI can consume it unchanged.

**Tech Stack:** C#, .NET 10, xUnit, `System.Text.Json` (stdlib, no extra JSON dependency). Records with `required` members and nullable reference types enabled.

## Global Constraints

Every task inherits these, copied verbatim from `docs/specs/2026-07-20-smartindexmanager-design.md`:

- Target framework: `net10.0`. Nullable reference types and implicit usings enabled.
- The Core has zero dependency on `Microsoft.Data.SqlClient` and zero dependency on any UI framework.
- Passwords are never handled or stored anywhere in this library (no connection code lives here at all).
- Only nonclustered rowstore non-unique indexes are ever eligible for deletion. Everything else is listed but excluded.
- A unique index (constraint-backed or not) is never flagged redundant and never deletable.
- DDL that cannot be reconstructed with certainty (partitioned index, unsupported options) is refused with a "DDL non sauvegardable" reason, never approximated.
- Confidence score is 0 to 100; bonuses are added first, then caps are applied, then the result is clamped to 0..100. A cap always wins over a bonus.
- Default score caps: short uptime 60, FK support 40, filtered 50, hint or plan guide 10. Default bonuses: redundancy +10, costly-updates +10. Default color thresholds: green >= 80, orange 50..79, red < 50. Default uptime reliability threshold: 30 days. All configurable.
- Filter-predicate normalization is syntactic, not semantic.
- Manifest and snapshot JSON files carry a `schemaVersion` field (current value 1).
- Backup file name: the dot stays the separator between the four components `<database>.<schema>.<table>.<index>`; sanitization happens inside each component (dots, brackets, slashes, spaces, filesystem-illegal chars become `_`). The name is not reversible; the manifest `file` field holds the physical file name and the real identifiers live in the manifest structured fields.
- Snapshot retention purge is not written to the audit log; the audit log is reserved for sensitive server-affecting actions (drop, restore, enable Query Store, script generation).
- CommunityToolkit.Mvvm and `Microsoft.Data.SqlClient` are not referenced by this project (they belong to the App and Provider plans).

---

## File Structure

```
SmartIndexManager/
  SmartIndexManager.sln
  src/
    SmartIndexManager.Core/
      SmartIndexManager.Core.csproj
      Model/
        IndexType.cs            SortDirection.cs      ConstraintKind.cs
        DataCompression.cs      IndexColumn.cs        IndexUsageStats.cs
        IndexSizeInfo.cs        IndexOptions.cs        IndexModel.cs
        ProviderCapabilities.cs
      Redundancy/
        NormalizedIndex.cs      IndexNormalizer.cs
        RedundancyRule.cs       RedundancyFinding.cs  RedundancyAnalyzer.cs
      Scoring/
        ScoringOptions.cs       ScoreInputs.cs        ScoreFactor.cs
        ScoreColor.cs           ConfidenceScore.cs    ConfidenceScorer.cs
      Ddl/
        DdlResult.cs            SqlServerDdlGenerator.cs
      Safety/
        SafetyInputs.cs         SafetyWarning.cs      SafetyAssessment.cs
        DeletionSafetyEvaluator.cs
      Sql/
        AzureSupport.cs         SqlFileHeader.cs
        SqlFileHeaderException.cs SqlFileHeaderParser.cs
      Persistence/
        Manifest.cs             ManifestStore.cs
        UsageSnapshot.cs        SnapshotStore.cs
        CoreJson.cs
      Backup/
        FileNameSanitizer.cs    BackupWriter.cs
      Audit/
        AuditEntry.cs           AuditLog.cs
  tests/
    SmartIndexManager.Core.Tests/
      SmartIndexManager.Core.Tests.csproj
      (one test file per component, mirroring the folders above)
```

`CoreJson.cs` centralizes one shared `JsonSerializerOptions` (camelCase, string enums, indented) so manifest, snapshot and audit serialization stay consistent.

---

### Task 1: Solution scaffold, domain model, and provider capabilities

**Files:**
- Create: `SmartIndexManager.sln`, `src/SmartIndexManager.Core/SmartIndexManager.Core.csproj`, `tests/SmartIndexManager.Core.Tests/SmartIndexManager.Core.Tests.csproj`
- Create: `src/SmartIndexManager.Core/Model/IndexType.cs`, `SortDirection.cs`, `ConstraintKind.cs`, `DataCompression.cs`, `IndexColumn.cs`, `IndexUsageStats.cs`, `IndexSizeInfo.cs`, `IndexOptions.cs`, `IndexModel.cs`, `ProviderCapabilities.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Model/IndexModelTests.cs`

**Interfaces:**
- Produces: the whole domain vocabulary used by every later task. Exact enum members and record members below are the contract.

- [ ] **Step 1: Scaffold the solution and projects**

Run:
```bash
cd /home/rudi/Sources/Repos/SmartIndexManager
dotnet new sln -n SmartIndexManager
dotnet new classlib -n SmartIndexManager.Core -o src/SmartIndexManager.Core -f net10.0
dotnet new xunit -n SmartIndexManager.Core.Tests -o tests/SmartIndexManager.Core.Tests -f net10.0
rm src/SmartIndexManager.Core/Class1.cs tests/SmartIndexManager.Core.Tests/UnitTest1.cs
dotnet sln add src/SmartIndexManager.Core/SmartIndexManager.Core.csproj tests/SmartIndexManager.Core.Tests/SmartIndexManager.Core.Tests.csproj
dotnet add tests/SmartIndexManager.Core.Tests/SmartIndexManager.Core.Tests.csproj reference src/SmartIndexManager.Core/SmartIndexManager.Core.csproj
```

- [ ] **Step 2: Enable nullable and implicit usings in both csproj**

Ensure both `.csproj` files contain, inside `<PropertyGroup>`:
```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
```

- [ ] **Step 3: Write the enums**

`src/SmartIndexManager.Core/Model/IndexType.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public enum IndexType
{
    Heap,
    ClusteredRowstore,
    NonclusteredRowstore,
    ClusteredColumnstore,
    NonclusteredColumnstore,
    Xml,
    Spatial,
    FullText,
    Hypothetical
}
```

`SortDirection.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public enum SortDirection { Ascending, Descending }
```

`ConstraintKind.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public enum ConstraintKind { None, PrimaryKey, Unique }
```

`DataCompression.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public enum DataCompression { None, Row, Page, Unsupported }
```

- [ ] **Step 4: Write the value records**

`IndexColumn.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record IndexColumn(string Name, SortDirection Direction);
```

`IndexUsageStats.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record IndexUsageStats(
    long Seeks,
    long Scans,
    long Lookups,
    long Updates,
    DateTime? LastRead,
    DateTime? LastWrite)
{
    public long TotalReads => Seeks + Scans + Lookups;
    public static IndexUsageStats Empty { get; } = new(0, 0, 0, 0, null, null);
}
```

`IndexSizeInfo.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record IndexSizeInfo(long Pages, long Rows, double SizeMb)
{
    public static IndexSizeInfo Empty { get; } = new(0, 0, 0);
}
```

`IndexOptions.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record IndexOptions
{
    public int? FillFactor { get; init; }
    public bool PadIndex { get; init; }
    public bool AllowRowLocks { get; init; } = true;
    public bool AllowPageLocks { get; init; } = true;
    public bool IgnoreDupKey { get; init; }
    public DataCompression Compression { get; init; } = DataCompression.None;
}
```

- [ ] **Step 5: Write `IndexModel` and `ProviderCapabilities`**

`IndexModel.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record IndexModel
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Name { get; init; }
    public required IndexType Type { get; init; }
    public IReadOnlyList<IndexColumn> KeyColumns { get; init; } = [];
    public IReadOnlyList<string> IncludedColumns { get; init; } = [];
    public string? FilterPredicate { get; init; }
    public bool IsUnique { get; init; }
    public ConstraintKind Constraint { get; init; } = ConstraintKind.None;
    public bool IsDisabled { get; init; }
    public bool IsOnView { get; init; }
    public bool IsOnSystemTable { get; init; }
    public bool IsPartitioned { get; init; }
    // Filegroup or partition-scheme name the index lives on. Emitted as ON [DataSpace]
    // so a restore recreates the index on its original filegroup, not the default.
    // Null means the default filegroup (no ON clause emitted).
    public string? DataSpace { get; init; }
    public IndexUsageStats Usage { get; init; } = IndexUsageStats.Empty;
    public IndexSizeInfo Size { get; init; } = IndexSizeInfo.Empty;
    public IndexOptions Options { get; init; } = new();
    // MVP simplification: provider-specific properties are display-only strings.
    // The Core never reasons on them (only on the common properties above), so a
    // string map is enough for the MVP. If a later version needs typed values, widen
    // to a small union type then; do not pre-generalize now (YAGNI).
    public IReadOnlyDictionary<string, string> ProviderProperties { get; init; }
        = new Dictionary<string, string>();
}
```

`ProviderCapabilities.cs`:
```csharp
namespace SmartIndexManager.Core.Model;

public sealed record ProviderCapabilities
{
    public bool SupportsQueryStore { get; init; }
    public bool SupportsPlanCache { get; init; }
    public bool SupportsColumnstore { get; init; }
    public bool SupportsOnlineDrop { get; init; }
    public bool RequiresDatabaseScopedDmv { get; init; }
}
```

- [ ] **Step 6: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Model/IndexModelTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Model;

public class IndexModelTests
{
    [Fact]
    public void TotalReads_sums_seeks_scans_lookups()
    {
        var stats = new IndexUsageStats(3, 5, 2, 10, null, null);
        Assert.Equal(10, stats.TotalReads);
    }

    [Fact]
    public void IndexModel_defaults_are_empty_not_null()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders",
            Name = "IX_Orders", Type = IndexType.NonclusteredRowstore
        };

        Assert.Empty(index.KeyColumns);
        Assert.Empty(index.IncludedColumns);
        Assert.Equal(ConstraintKind.None, index.Constraint);
        Assert.Same(IndexUsageStats.Empty, index.Usage);
    }
}
```

- [ ] **Step 7: Run test to verify it fails, then passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexModelTests"`
Expected: FAIL before the model exists (compile error), PASS once Steps 3-5 are in place.

- [ ] **Step 8: Commit**

```bash
git add SmartIndexManager.sln src/ tests/
git commit -m "feat(core): scaffold solution and domain model"
```

---

### Task 2: Index normalizer

**Files:**
- Create: `src/SmartIndexManager.Core/Redundancy/NormalizedIndex.cs`, `IndexNormalizer.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Redundancy/IndexNormalizerTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexColumn`, `SortDirection`.
- Produces:
  - `NormalizedIndex(IReadOnlyList<(string Column, SortDirection Direction)> Key, IReadOnlySet<string> Includes, string? Filter, bool IsUnique)`
  - `IndexNormalizer.Normalize(IndexModel) -> NormalizedIndex`
  - `IndexNormalizer.NormalizeFilter(string?) -> string?`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Redundancy/IndexNormalizerTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class IndexNormalizerTests
{
    private static IndexModel Index(string filter = null!, params string[] keys) => new()
    {
        Database = "db", Schema = "dbo", Table = "T", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        FilterPredicate = filter
    };

    [Fact]
    public void Key_columns_are_lowercased_for_comparison()
    {
        var n = IndexNormalizer.Normalize(Index(null!, "CustomerId", "OrderDate"));
        Assert.Equal(new[] { "customerid", "orderdate" }, n.Key.Select(k => k.Column));
    }

    [Fact]
    public void Includes_are_a_case_insensitive_set()
    {
        var index = Index(null!, "A") with { IncludedColumns = new[] { "Total", "total", "Qty" } };
        var n = IndexNormalizer.Normalize(index);
        Assert.Equal(2, n.Includes.Count);
        Assert.Contains("total", n.Includes);
        Assert.Contains("qty", n.Includes);
    }

    [Theory]
    [InlineData("[Status] = 1", "[status] = 1")]
    [InlineData("(  Status   =   1  )", "status = 1")]
    [InlineData("((Status = 1))", "status = 1")]
    [InlineData(null, null)]
    public void Filter_is_normalized_syntactically(string? input, string? expected)
    {
        Assert.Equal(expected, IndexNormalizer.NormalizeFilter(input));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IndexNormalizerTests"`
Expected: FAIL, `IndexNormalizer` does not exist.

- [ ] **Step 3: Write the implementation**

`NormalizedIndex.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public sealed record NormalizedIndex(
    IReadOnlyList<(string Column, SortDirection Direction)> Key,
    IReadOnlySet<string> Includes,
    string? Filter,
    bool IsUnique);
```

`IndexNormalizer.cs`:
```csharp
using System.Text.RegularExpressions;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public static partial class IndexNormalizer
{
    public static NormalizedIndex Normalize(IndexModel index)
    {
        var key = index.KeyColumns
            .Select(c => (Column: c.Name.ToLowerInvariant(), c.Direction))
            .ToList();
        var includes = index.IncludedColumns
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        return new NormalizedIndex(key, includes, NormalizeFilter(index.FilterPredicate), index.IsUnique);
    }

    // Syntactic normalization only: lower-case, collapse whitespace, strip
    // fully-enclosing redundant parentheses. Brackets are deliberately NOT stripped:
    // removing them could unify two genuinely different predicates (for example a
    // LIKE '[0-9]%' character class), which would be a false positive. The invariant
    // is "fewer redundancies reported, never a false one", so bracketed and
    // unbracketed identifiers stay distinct (a safe false negative).
    public static string? NormalizeFilter(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate)) return null;
        var s = predicate.ToLowerInvariant();
        s = Whitespace().Replace(s, " ").Trim();
        s = StripEnclosingParens(s);
        return s;
    }

    private static string StripEnclosingParens(string s)
    {
        while (s.Length >= 2 && s[0] == '(' && s[^1] == ')' && ParensBalancedInside(s))
            s = s[1..^1].Trim();
        return s;
    }

    private static bool ParensBalancedInside(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            if (depth == 0 && i < s.Length - 1) return false; // closes before the end
        }
        return depth == 0;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~IndexNormalizerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Redundancy/ tests/SmartIndexManager.Core.Tests/Redundancy/IndexNormalizerTests.cs
git commit -m "feat(core): index normalization for redundancy comparison"
```

---

### Task 3: Redundancy rule R1 (exact duplicate)

**Files:**
- Create: `src/SmartIndexManager.Core/Redundancy/RedundancyRule.cs`, `RedundancyFinding.cs`, `RedundancyAnalyzer.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR1Tests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexNormalizer`, `NormalizedIndex`.
- Produces:
  - `enum RedundancyRule { R1ExactDuplicate, R2CoveredPrefix, R3DominatedIncludes }`
  - `RedundancyFinding(IndexModel Redundant, IndexModel CoveredBy, RedundancyRule Rule)`
  - `RedundancyAnalyzer.Analyze(IEnumerable<IndexModel>) -> IReadOnlyList<RedundancyFinding>`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR1Tests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR1Tests
{
    private static IndexModel Nc(string name, string[] keys, string[]? includes = null,
        bool unique = false, ConstraintKind constraint = ConstraintKind.None,
        long reads = 0) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        IncludedColumns = includes ?? [],
        IsUnique = unique, Constraint = constraint,
        Usage = new IndexUsageStats(reads, 0, 0, 0, null, null)
    };

    [Fact]
    public void Identical_indexes_produce_one_R1_finding()
    {
        var a = Nc("IX_A", ["CustomerId"], reads: 100);
        var b = Nc("IX_B", ["CustomerId"], reads: 0);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r1 = Assert.Single(findings);
        Assert.Equal(RedundancyRule.R1ExactDuplicate, r1.Rule);
        Assert.Equal("IX_B", r1.Redundant.Name);   // keep the more-used one
        Assert.Equal("IX_A", r1.CoveredBy.Name);
    }

    [Fact]
    public void Duplicate_keeps_the_constraint_backed_index()
    {
        var a = Nc("IX_Plain", ["CustomerId"], reads: 0);
        var b = Nc("UQ_Cust", ["CustomerId"], unique: true, constraint: ConstraintKind.Unique);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r1 = Assert.Single(findings);
        Assert.Equal("IX_Plain", r1.Redundant.Name);
        Assert.Equal("UQ_Cust", r1.CoveredBy.Name);
    }

    [Fact]
    public void Unique_index_is_never_the_redundant_one()
    {
        var a = Nc("UQ_A", ["CustomerId"], unique: true);
        var b = Nc("UQ_B", ["CustomerId"], unique: true);

        // both unique => neither may be flagged redundant
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Different_tables_are_never_compared()
    {
        var a = Nc("IX_A", ["CustomerId"]);
        var b = Nc("IX_B", ["CustomerId"]) with { Table = "Customers" };
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Disabled_indexes_are_excluded_from_analysis()
    {
        var a = Nc("IX_A", ["CustomerId"]) with { IsDisabled = true };
        var b = Nc("IX_B", ["CustomerId"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyR1Tests"`
Expected: FAIL, `RedundancyAnalyzer` does not exist.

- [ ] **Step 3: Write the implementation**

`RedundancyRule.cs`:
```csharp
namespace SmartIndexManager.Core.Redundancy;

public enum RedundancyRule { R1ExactDuplicate, R2CoveredPrefix, R3DominatedIncludes }
```

`RedundancyFinding.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public sealed record RedundancyFinding(
    IndexModel Redundant,
    IndexModel CoveredBy,
    RedundancyRule Rule);
```

`RedundancyAnalyzer.cs` (R1 only for now; R2 and R3 arrive in the next two tasks):
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public static class RedundancyAnalyzer
{
    public static IReadOnlyList<RedundancyFinding> Analyze(IEnumerable<IndexModel> indexes)
    {
        var findings = new List<RedundancyFinding>();
        var groups = indexes
            // Disabled indexes serve no query; comparing them would be misleading.
            .Where(i => i.Type == IndexType.NonclusteredRowstore && !i.IsDisabled)
            .GroupBy(i => (i.Database, i.Schema, i.Table),
                     TupleComparer.CaseInsensitive);

        foreach (var group in groups)
        {
            var items = group
                .Select(i => (Index: i, Norm: IndexNormalizer.Normalize(i)))
                .ToList();

            for (int a = 0; a < items.Count; a++)
            for (int b = a + 1; b < items.Count; b++)
            {
                var finding = Compare(items[a].Index, items[a].Norm, items[b].Index, items[b].Norm);
                if (finding is not null) findings.Add(finding);
            }
        }
        return findings;
    }

    private static RedundancyFinding? Compare(IndexModel ia, NormalizedIndex na, IndexModel ib, NormalizedIndex nb)
    {
        // R1: exact duplicate
        if (SameKey(na.Key, nb.Key) && na.Filter == nb.Filter && na.Includes.SetEquals(nb.Includes))
        {
            var (redundant, coveredBy) = ChooseKeeper(ia, ib);
            if (redundant is null) return null; // both unique => never flag
            return new RedundancyFinding(redundant, coveredBy!, RedundancyRule.R1ExactDuplicate);
        }
        return null;
    }

    // Returns (indexToDrop, indexToKeep). Null indexToDrop means neither may be dropped.
    private static (IndexModel?, IndexModel?) ChooseKeeper(IndexModel x, IndexModel y)
    {
        bool xUnique = x.IsUnique, yUnique = y.IsUnique;
        if (xUnique && yUnique) return (null, null);
        if (xUnique) return (y, x);
        if (yUnique) return (x, y);

        // prefer to keep the constraint-backed one, else the more-used one
        bool xConstraint = x.Constraint != ConstraintKind.None;
        bool yConstraint = y.Constraint != ConstraintKind.None;
        if (xConstraint && !yConstraint) return (y, x);
        if (yConstraint && !xConstraint) return (x, y);

        return x.Usage.TotalReads >= y.Usage.TotalReads ? (y, x) : (x, y);
    }

    private static bool SameKey(
        IReadOnlyList<(string Column, SortDirection Direction)> a,
        IReadOnlyList<(string Column, SortDirection Direction)> b)
        => a.Count == b.Count && a.SequenceEqual(b);

    private sealed class TupleComparer : IEqualityComparer<(string Database, string Schema, string Table)>
    {
        public static readonly TupleComparer CaseInsensitive = new();
        public bool Equals((string Database, string Schema, string Table) x, (string Database, string Schema, string Table) y)
            => string.Equals(x.Database, y.Database, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Database, string Schema, string Table) o)
            => HashCode.Combine(
                o.Database.ToLowerInvariant(), o.Schema.ToLowerInvariant(), o.Table.ToLowerInvariant());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyR1Tests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Redundancy/ tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR1Tests.cs
git commit -m "feat(core): redundancy rule R1 exact duplicate"
```

---

### Task 4: Redundancy rule R2 (covered prefix)

**Files:**
- Modify: `src/SmartIndexManager.Core/Redundancy/RedundancyAnalyzer.cs` (add R2 branch inside `Compare`)
- Test: `tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR2Tests.cs`

**Interfaces:**
- Consumes and produces the same types as Task 3. `Compare` now also detects `R2CoveredPrefix`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR2Tests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR2Tests
{
    private static IndexModel Nc(string name, (string, SortDirection)[] keys,
        string[]? includes = null, bool unique = false, string? filter = null) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k.Item1, k.Item2)).ToList(),
        IncludedColumns = includes ?? [], IsUnique = unique, FilterPredicate = filter
    };

    private static (string, SortDirection) Asc(string c) => (c, SortDirection.Ascending);
    private static (string, SortDirection) Desc(string c) => (c, SortDirection.Descending);

    [Fact]
    public void Strict_prefix_with_included_covered_is_R2()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r2 = Assert.Single(findings);
        Assert.Equal(RedundancyRule.R2CoveredPrefix, r2.Rule);
        Assert.Equal("IX_Cust", r2.Redundant.Name);
        Assert.Equal("IX_CustDate", r2.CoveredBy.Name);
    }

    [Fact]
    public void Prefix_includes_must_be_covered_by_key_or_includes_of_the_longer()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")], includes: ["Total"]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")], includes: ["Total"]);
        Assert.Equal(RedundancyRule.R2CoveredPrefix, Assert.Single(RedundancyAnalyzer.Analyze([a, b])).Rule);

        var c = Nc("IX_Cust2", [Asc("CustomerId")], includes: ["Qty"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([c, b])); // Qty not covered by b
    }

    [Fact]
    public void Different_direction_on_prefix_breaks_R2()
    {
        var a = Nc("IX_Cust", [Desc("CustomerId")]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Different_filter_breaks_R2()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")], filter: "Status = 1");
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")], filter: "Status = 2");
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Unique_shorter_index_is_never_flagged()
    {
        var a = Nc("UQ_Cust", [Asc("CustomerId")], unique: true);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyR2Tests"`
Expected: FAIL, R2 not yet detected.

- [ ] **Step 3: Add the R2 branch to `Compare`**

In `RedundancyAnalyzer.cs`, replace the body of `Compare` with:
```csharp
    private static RedundancyFinding? Compare(IndexModel ia, NormalizedIndex na, IndexModel ib, NormalizedIndex nb)
    {
        // R1: exact duplicate
        if (SameKey(na.Key, nb.Key) && na.Filter == nb.Filter && na.Includes.SetEquals(nb.Includes))
        {
            var (redundant, coveredBy) = ChooseKeeper(ia, ib);
            if (redundant is null) return null;
            return new RedundancyFinding(redundant, coveredBy!, RedundancyRule.R1ExactDuplicate);
        }

        // R2: one key is a strict prefix of the other, same filter, shorter's includes covered
        if (na.Filter == nb.Filter)
        {
            var r2 = TryCoveredPrefix(ia, na, ib, nb) ?? TryCoveredPrefix(ib, nb, ia, na);
            if (r2 is not null) return r2;
        }

        return null;
    }

    // Is `shorter` redundant against `longer`? shorter.Key strict prefix of longer.Key
    // (same directions on the prefix) and shorter.Includes subset of (longer.Key columns
    // beyond the prefix union longer.Includes). Only `shorter` is ever flagged, and only
    // when it is non-unique; `longer` may be unique (we keep it, which is correct). The UI
    // wording should make clear the covering index is the one being kept, not dropped.
    private static RedundancyFinding? TryCoveredPrefix(
        IndexModel shorter, NormalizedIndex ns, IndexModel longer, NormalizedIndex nl)
    {
        if (shorter.IsUnique) return null;
        if (ns.Key.Count >= nl.Key.Count) return null;
        for (int i = 0; i < ns.Key.Count; i++)
            if (ns.Key[i] != nl.Key[i]) return null;

        var covered = new HashSet<string>(StringComparer.Ordinal);
        for (int i = ns.Key.Count; i < nl.Key.Count; i++) covered.Add(nl.Key[i].Column);
        foreach (var inc in nl.Includes) covered.Add(inc);

        if (!ns.Includes.IsSubsetOf(covered)) return null;
        return new RedundancyFinding(shorter, longer, RedundancyRule.R2CoveredPrefix);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyR2Tests"`
Expected: PASS. Also re-run R1: `dotnet test --filter "FullyQualifiedName~RedundancyR1Tests"` still PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Redundancy/RedundancyAnalyzer.cs tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR2Tests.cs
git commit -m "feat(core): redundancy rule R2 covered prefix"
```

---

### Task 5: Redundancy rule R3 (dominated includes) and remaining edge cases

**Files:**
- Modify: `src/SmartIndexManager.Core/Redundancy/RedundancyAnalyzer.cs` (add R3 branch)
- Test: `tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR3Tests.cs`

**Interfaces:**
- Same types. `Compare` now also detects `R3DominatedIncludes`.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR3Tests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR3Tests
{
    private static IndexModel Nc(string name, string[] keys, string[]? includes = null,
        bool unique = false, string? filter = null) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        IncludedColumns = includes ?? [], IsUnique = unique, FilterPredicate = filter
    };

    [Fact]
    public void Same_key_with_strictly_smaller_includes_is_R3()
    {
        var a = Nc("IX_Small", ["CustomerId"], includes: ["Total"]);
        var b = Nc("IX_Big", ["CustomerId"], includes: ["Total", "Qty"]);

        var r3 = Assert.Single(RedundancyAnalyzer.Analyze([a, b]));
        Assert.Equal(RedundancyRule.R3DominatedIncludes, r3.Rule);
        Assert.Equal("IX_Small", r3.Redundant.Name);
        Assert.Equal("IX_Big", r3.CoveredBy.Name);
    }

    [Fact]
    public void Equal_includes_is_R1_not_R3()
    {
        var a = Nc("IX_A", ["CustomerId"], includes: ["Total"]);
        var b = Nc("IX_B", ["CustomerId"], includes: ["Total"]);
        Assert.Equal(RedundancyRule.R1ExactDuplicate, Assert.Single(RedundancyAnalyzer.Analyze([a, b])).Rule);
    }

    [Fact]
    public void Filtered_versus_non_filtered_is_not_redundant()
    {
        var a = Nc("IX_Filtered", ["CustomerId"], filter: "Status = 1");
        var b = Nc("IX_Plain", ["CustomerId"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Partial_key_overlap_is_not_redundant()
    {
        var a = Nc("IX_A", ["CustomerId", "OrderDate"]);
        var b = Nc("IX_B", ["CustomerId", "ShipDate"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RedundancyR3Tests"`
Expected: FAIL on `Same_key_with_strictly_smaller_includes_is_R3`.

- [ ] **Step 3: Add the R3 branch**

In `RedundancyAnalyzer.cs`, inside `Compare`, add before the final `return null;`:
```csharp
        // R3: same key, same filter, one includes strictly dominates the other
        if (SameKey(na.Key, nb.Key) && na.Filter == nb.Filter)
        {
            if (!ia.IsUnique && na.Includes.IsProperSubsetOf(nb.Includes))
                return new RedundancyFinding(ia, ib, RedundancyRule.R3DominatedIncludes);
            if (!ib.IsUnique && nb.Includes.IsProperSubsetOf(na.Includes))
                return new RedundancyFinding(ib, ia, RedundancyRule.R3DominatedIncludes);
        }
```

- [ ] **Step 4: Run all redundancy tests**

Run: `dotnet test --filter "FullyQualifiedName~Redundancy"`
Expected: PASS across R1, R2, R3.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Redundancy/RedundancyAnalyzer.cs tests/SmartIndexManager.Core.Tests/Redundancy/RedundancyR3Tests.cs
git commit -m "feat(core): redundancy rule R3 dominated includes and edge cases"
```

---

### Task 6: Confidence scoring, base read score and options

**Files:**
- Create: `src/SmartIndexManager.Core/Scoring/ScoringOptions.cs`, `ScoreInputs.cs`, `ScoreFactor.cs`, `ScoreColor.cs`, `ConfidenceScore.cs`, `ConfidenceScorer.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerBaseTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexUsageStats`.
- Produces:
  - `ScoringOptions` (all knobs with defaults from Global Constraints)
  - `ScoreInputs { IndexModel Index; int InstanceUptimeDays; bool SupportsForeignKey; bool ReferencedByHint; bool IsRedundant; DateTime NowUtc }`
  - `ScoreColor { Green, Orange, Red }`
  - `ScoreFactor(string Name, string Description)`
  - `ConfidenceScore(int Value, ScoreColor Color, IReadOnlyList<ScoreFactor> Factors)`
  - `ConfidenceScorer(ScoringOptions? options = null)` with `ConfidenceScore Score(ScoreInputs inputs)`

Note on the freshness weight (spec section 7, point flagged during review): the default here is a concrete linear decay so the code is complete and testable. `FreshnessWindowDays` is the single tuning knob to finalize; treat "calibrate freshness decay" as a follow-up task, but the code ships with a working default, never a placeholder.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerBaseTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Scoring;

public class ConfidenceScorerBaseTests
{
    private static readonly DateTime Now = new(2026, 07, 20, 0, 0, 0, DateTimeKind.Utc);

    private static ScoreInputs Inputs(IndexUsageStats usage, int uptime = 90) => new()
    {
        Index = new IndexModel
        {
            Database = "db", Schema = "dbo", Table = "T", Name = "IX",
            Type = IndexType.NonclusteredRowstore, Usage = usage
        },
        InstanceUptimeDays = uptime,
        NowUtc = Now
    };

    [Fact]
    public void Zero_reads_scores_100()
    {
        var score = new ConfidenceScorer().Score(Inputs(IndexUsageStats.Empty));
        Assert.Equal(100, score.Value);
        Assert.Equal(ScoreColor.Green, score.Color);
    }

    [Fact]
    public void Reads_lower_the_score()
    {
        var few = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(10, 0, 0, 0, Now, null)));
        var many = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1_000_000, 0, 0, 0, Now, null)));

        Assert.True(few.Value < 100);
        Assert.True(many.Value < few.Value);
        Assert.InRange(many.Value, 0, 100);
    }

    [Fact]
    public void Older_reads_weigh_less_than_recent_reads()
    {
        var recent = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1000, 0, 0, 0, Now, null)));
        var old = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1000, 0, 0, 0, Now.AddDays(-200), null)));

        Assert.True(old.Value > recent.Value); // stale reads are less alarming
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConfidenceScorerBaseTests"`
Expected: FAIL, scoring types do not exist.

- [ ] **Step 3: Write the option, input and result types**

`ScoringOptions.cs`:
```csharp
namespace SmartIndexManager.Core.Scoring;

public sealed record ScoringOptions
{
    public int UptimeReliabilityThresholdDays { get; init; } = 30;
    public int ShortUptimeCap { get; init; } = 60;
    public int FkSupportCap { get; init; } = 40;
    public int FilteredCap { get; init; } = 50;
    public int HintCap { get; init; } = 10;
    public int RedundancyBonus { get; init; } = 10;
    public int CostlyUpdatesBonus { get; init; } = 10;
    public int FreshnessWindowDays { get; init; } = 90;
    public double ReadWeightMultiplier { get; init; } = 20.0;
    public double MinFreshnessFactor { get; init; } = 0.25;
    public int GreenThreshold { get; init; } = 80;
    public int OrangeThreshold { get; init; } = 50;
}
```

`ScoreInputs.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Scoring;

public sealed record ScoreInputs
{
    public required IndexModel Index { get; init; }
    public required int InstanceUptimeDays { get; init; }
    public bool SupportsForeignKey { get; init; }
    public bool ReferencedByHint { get; init; }
    public bool IsRedundant { get; init; }
    public required DateTime NowUtc { get; init; }
}
```

`ScoreColor.cs`:
```csharp
namespace SmartIndexManager.Core.Scoring;

public enum ScoreColor { Green, Orange, Red }
```

`ScoreFactor.cs`:
```csharp
namespace SmartIndexManager.Core.Scoring;

public sealed record ScoreFactor(string Name, string Description);
```

`ConfidenceScore.cs`:
```csharp
namespace SmartIndexManager.Core.Scoring;

public sealed record ConfidenceScore(int Value, ScoreColor Color, IReadOnlyList<ScoreFactor> Factors);
```

- [ ] **Step 4: Write `ConfidenceScorer` (base read score only; caps and bonuses added in Task 7)**

`ConfidenceScorer.cs`:
```csharp
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Scoring;

public sealed class ConfidenceScorer
{
    private readonly ScoringOptions _options;

    public ConfidenceScorer(ScoringOptions? options = null) => _options = options ?? new ScoringOptions();

    public ConfidenceScore Score(ScoreInputs inputs)
    {
        var factors = new List<ScoreFactor>();
        double value = BaseReadScore(inputs, factors);

        int final = (int)Math.Round(Math.Clamp(value, 0, 100));
        return new ConfidenceScore(final, Colorize(final), factors);
    }

    private double BaseReadScore(ScoreInputs inputs, List<ScoreFactor> factors)
    {
        long reads = inputs.Index.Usage.TotalReads;
        if (reads == 0)
        {
            factors.Add(new ScoreFactor("no-reads", "0 reads since instance start"));
            return 100;
        }

        double freshness = FreshnessFactor(inputs.Index.Usage.LastRead, inputs.NowUtc);
        double weightedReads = reads * freshness;
        double drop = _options.ReadWeightMultiplier * Math.Log10(1 + weightedReads);
        double baseScore = 100 - Math.Min(100, drop);
        factors.Add(new ScoreFactor("reads",
            $"{reads} reads, freshness {freshness:0.00}, base {baseScore:0}"));
        return baseScore;
    }

    // Recent reads weigh full (1.0); reads age down linearly to MinFreshnessFactor over the window.
    private double FreshnessFactor(DateTime? lastRead, DateTime nowUtc)
    {
        if (lastRead is null) return 1.0;
        double ageDays = Math.Max(0, (nowUtc - lastRead.Value).TotalDays);
        double factor = 1.0 - (ageDays / _options.FreshnessWindowDays);
        return Math.Clamp(factor, _options.MinFreshnessFactor, 1.0);
    }

    private ScoreColor Colorize(int value)
        => value >= _options.GreenThreshold ? ScoreColor.Green
         : value >= _options.OrangeThreshold ? ScoreColor.Orange
         : ScoreColor.Red;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConfidenceScorerBaseTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SmartIndexManager.Core/Scoring/ tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerBaseTests.cs
git commit -m "feat(core): confidence score base read component"
```

---

### Task 7: Confidence scoring, bonuses then caps

**Files:**
- Modify: `src/SmartIndexManager.Core/Scoring/ConfidenceScorer.cs` (extend `Score` to add bonuses then caps)
- Test: `tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerCapsTests.cs`

**Interfaces:**
- Same types as Task 6. `Score` now applies, in order: base, bonuses, caps, clamp.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerCapsTests.cs`:
```csharp
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Scoring;

public class ConfidenceScorerCapsTests
{
    private static readonly DateTime Now = new(2026, 07, 20, 0, 0, 0, DateTimeKind.Utc);

    private static ScoreInputs Build(
        bool fk = false, bool hint = false, bool redundant = false,
        string? filter = null, int uptime = 90, long updates = 0) => new()
    {
        Index = new IndexModel
        {
            Database = "db", Schema = "dbo", Table = "T", Name = "IX",
            Type = IndexType.NonclusteredRowstore, FilterPredicate = filter,
            Usage = new IndexUsageStats(0, 0, 0, updates, null, null)
        },
        InstanceUptimeDays = uptime,
        SupportsForeignKey = fk, ReferencedByHint = hint, IsRedundant = redundant,
        NowUtc = Now
    };

    [Fact]
    public void Short_uptime_caps_at_60_and_turns_orange()
    {
        var score = new ConfidenceScorer().Score(Build(uptime: 5));
        Assert.Equal(60, score.Value);
        Assert.Equal(ScoreColor.Orange, score.Color);
    }

    [Fact]
    public void Fk_support_caps_at_40()
        => Assert.Equal(40, new ConfidenceScorer().Score(Build(fk: true)).Value);

    [Fact]
    public void Filtered_caps_at_50()
        => Assert.Equal(50, new ConfidenceScorer().Score(Build(filter: "Status = 1")).Value);

    [Fact]
    public void Hint_caps_at_10()
        => Assert.Equal(10, new ConfidenceScorer().Score(Build(hint: true)).Value);

    [Fact]
    public void Cap_wins_over_redundancy_bonus()
    {
        // redundant would add +10 but a hint caps at 10; the cap wins
        var score = new ConfidenceScorer().Score(Build(hint: true, redundant: true));
        Assert.Equal(10, score.Value);
    }

    [Fact]
    public void Costly_updates_bonus_cannot_exceed_100()
    {
        var score = new ConfidenceScorer().Score(Build(updates: 5_000_000));
        Assert.Equal(100, score.Value); // base 100 + bonus, clamped to 100
    }

    [Fact]
    public void Lowest_applicable_cap_wins()
    {
        // both FK (40) and filtered (50) apply => 40
        var score = new ConfidenceScorer().Score(Build(fk: true, filter: "x = 1"));
        Assert.Equal(40, score.Value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConfidenceScorerCapsTests"`
Expected: FAIL, caps and bonuses not applied yet.

- [ ] **Step 3: Extend `Score` in `ConfidenceScorer.cs`**

Replace the `Score` method body with:
```csharp
    public ConfidenceScore Score(ScoreInputs inputs)
    {
        var factors = new List<ScoreFactor>();
        double value = BaseReadScore(inputs, factors);

        // Bonuses first.
        if (inputs.IsRedundant)
        {
            value += _options.RedundancyBonus;
            factors.Add(new ScoreFactor("redundant", $"+{_options.RedundancyBonus} redundant with another index"));
        }
        if (inputs.Index.Usage.Updates > 0 && inputs.Index.Usage.TotalReads == 0)
        {
            value += _options.CostlyUpdatesBonus;
            factors.Add(new ScoreFactor("costly-updates",
                $"+{_options.CostlyUpdatesBonus} {inputs.Index.Usage.Updates} updates with 0 reads"));
        }

        // Caps next: a cap always wins over a bonus.
        int cap = 100;
        if (inputs.InstanceUptimeDays < _options.UptimeReliabilityThresholdDays)
        {
            cap = Math.Min(cap, _options.ShortUptimeCap);
            factors.Add(new ScoreFactor("short-uptime",
                $"cap {_options.ShortUptimeCap}, uptime {inputs.InstanceUptimeDays}d below threshold"));
        }
        if (inputs.SupportsForeignKey)
        {
            cap = Math.Min(cap, _options.FkSupportCap);
            factors.Add(new ScoreFactor("fk-support", $"cap {_options.FkSupportCap}, supports a foreign key"));
        }
        if (inputs.Index.FilterPredicate is not null)
        {
            cap = Math.Min(cap, _options.FilteredCap);
            factors.Add(new ScoreFactor("filtered", $"cap {_options.FilteredCap}, filtered index"));
        }
        if (inputs.ReferencedByHint)
        {
            cap = Math.Min(cap, _options.HintCap);
            factors.Add(new ScoreFactor("hint", $"cap {_options.HintCap}, referenced by a hint or plan guide"));
        }

        value = Math.Min(value, cap);
        int final = (int)Math.Round(Math.Clamp(value, 0, 100));
        return new ConfidenceScore(final, Colorize(final), factors);
    }
```

- [ ] **Step 4: Run both scoring test classes**

Run: `dotnet test --filter "FullyQualifiedName~ConfidenceScorer"`
Expected: PASS (base + caps).

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Scoring/ConfidenceScorer.cs tests/SmartIndexManager.Core.Tests/Scoring/ConfidenceScorerCapsTests.cs
git commit -m "feat(core): confidence score bonuses then caps ordering"
```

---

### Task 8: T-SQL DDL generator, structure

**Files:**
- Create: `src/SmartIndexManager.Core/Ddl/DdlResult.cs`, `SqlServerDdlGenerator.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Ddl/DdlStructureTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexColumn`, `SortDirection`.
- Produces:
  - `abstract record DdlResult;`
  - `sealed record DdlSuccess(string Sql) : DdlResult;`
  - `sealed record DdlNotBackupable(string Reason) : DdlResult;`
  - `SqlServerDdlGenerator.Generate(IndexModel) -> DdlResult`

Design note (spec section 4): DDL generation is pure string building with no database dependency, so it lives in the Core as the spec states. A PostgreSQL dialect will be added later alongside its provider.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Ddl/DdlStructureTests.cs`:
```csharp
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Ddl;

public class DdlStructureTests
{
    private static IndexModel Nc(
        IndexColumn[] keys, string[]? includes = null, string? filter = null, bool unique = false) => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Orders_Legacy",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys, IncludedColumns = includes ?? [], FilterPredicate = filter, IsUnique = unique
    };

    private static string Sql(DdlResult r) => Assert.IsType<DdlSuccess>(r).Sql;

    [Fact]
    public void Generates_basic_nonclustered_index()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("CustomerId", SortDirection.Ascending)])));

        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Orders_Legacy]", ddl);
        Assert.Contains("ON [dbo].[Orders] ([CustomerId] ASC)", ddl);
        Assert.EndsWith(";", ddl.TrimEnd());
    }

    [Fact]
    public void Emits_unique_keyword_and_direction()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("OrderDate", SortDirection.Descending)], unique: true)));

        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX", ddl);
        Assert.Contains("[OrderDate] DESC", ddl);
    }

    [Fact]
    public void Emits_include_and_filter()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("CustomerId", SortDirection.Ascending)],
               includes: ["Total", "Qty"], filter: "[Status] = 1")));

        Assert.Contains("INCLUDE ([Total], [Qty])", ddl);
        Assert.Contains("WHERE [Status] = 1", ddl);
    }

    [Fact]
    public void Escapes_closing_bracket_in_identifiers()
    {
        var index = Nc([new IndexColumn("Weird]Name", SortDirection.Ascending)]);
        var ddl = Sql(SqlServerDdlGenerator.Generate(index));
        Assert.Contains("[Weird]]Name] ASC", ddl);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DdlStructureTests"`
Expected: FAIL, generator does not exist.

- [ ] **Step 3: Write `DdlResult` and the generator (structure only; options and eligibility added in Task 9)**

`DdlResult.cs`:
```csharp
namespace SmartIndexManager.Core.Ddl;

public abstract record DdlResult;
public sealed record DdlSuccess(string Sql) : DdlResult;
public sealed record DdlNotBackupable(string Reason) : DdlResult;
```

`SqlServerDdlGenerator.cs`:
```csharp
using System.Text;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Ddl;

public static class SqlServerDdlGenerator
{
    public static DdlResult Generate(IndexModel index)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique) sb.Append("UNIQUE ");
        sb.Append("NONCLUSTERED INDEX ").Append(Quote(index.Name));
        sb.Append(" ON ").Append(Quote(index.Schema)).Append('.').Append(Quote(index.Table));

        sb.Append(" (");
        sb.Append(string.Join(", ", index.KeyColumns.Select(
            c => $"{Quote(c.Name)} {(c.Direction == SortDirection.Ascending ? "ASC" : "DESC")}")));
        sb.Append(')');

        if (index.IncludedColumns.Count > 0)
            sb.Append(" INCLUDE (").Append(string.Join(", ", index.IncludedColumns.Select(Quote))).Append(')');

        if (!string.IsNullOrWhiteSpace(index.FilterPredicate))
            sb.Append(" WHERE ").Append(index.FilterPredicate);

        sb.Append(';');
        return new DdlSuccess(sb.ToString());
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DdlStructureTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Ddl/ tests/SmartIndexManager.Core.Tests/Ddl/DdlStructureTests.cs
git commit -m "feat(core): T-SQL DDL generation for nonclustered indexes"
```

---

### Task 9: DDL options and eligibility (DDL non sauvegardable)

**Files:**
- Modify: `src/SmartIndexManager.Core/Ddl/SqlServerDdlGenerator.cs` (add WITH-options and the not-backupable guards)
- Test: `tests/SmartIndexManager.Core.Tests/Ddl/DdlEligibilityTests.cs`

**Interfaces:**
- Same types. `Generate` now returns `DdlNotBackupable` for partitioned indexes, unsupported compression, and non-nonclustered-rowstore types, and emits a `WITH (...)` clause otherwise.

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Ddl/DdlEligibilityTests.cs`:
```csharp
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Ddl;

public class DdlEligibilityTests
{
    private static IndexModel Base() => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
    };

    [Fact]
    public void Emits_with_options_when_present()
    {
        var index = Base() with
        {
            Options = new IndexOptions
            {
                FillFactor = 80, PadIndex = true, AllowRowLocks = false,
                AllowPageLocks = true, IgnoreDupKey = false, Compression = DataCompression.Page
            }
        };

        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(index)).Sql;

        Assert.Contains("WITH (", ddl);
        Assert.Contains("PAD_INDEX = ON", ddl);
        Assert.Contains("FILLFACTOR = 80", ddl);
        Assert.Contains("ALLOW_ROW_LOCKS = OFF", ddl);
        Assert.Contains("ALLOW_PAGE_LOCKS = ON", ddl);
        Assert.Contains("DATA_COMPRESSION = PAGE", ddl);
    }

    [Fact]
    public void Omits_fillfactor_when_not_set()
    {
        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(Base())).Sql;
        Assert.DoesNotContain("FILLFACTOR", ddl);
    }

    [Fact]
    public void Emits_on_filegroup_when_dataspace_is_set()
    {
        var ddl = Assert.IsType<DdlSuccess>(
            SqlServerDdlGenerator.Generate(Base() with { DataSpace = "FG_Archive" })).Sql;
        Assert.Contains("ON [FG_Archive]", ddl);
    }

    [Fact]
    public void Omits_on_clause_on_default_filegroup()
    {
        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(Base())).Sql;
        // Base() has no DataSpace; only the ON <schema>.<table> target clause is present
        Assert.Equal(1, Occurrences(ddl, " ON "));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    [Fact]
    public void Partitioned_index_is_not_backupable()
    {
        var result = SqlServerDdlGenerator.Generate(Base() with { IsPartitioned = true });
        var reason = Assert.IsType<DdlNotBackupable>(result).Reason;
        Assert.Contains("partition", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_compression_is_not_backupable()
    {
        var index = Base() with { Options = new IndexOptions { Compression = DataCompression.Unsupported } };
        Assert.IsType<DdlNotBackupable>(SqlServerDdlGenerator.Generate(index));
    }

    [Fact]
    public void Non_nonclustered_rowstore_is_not_backupable()
    {
        var index = Base() with { Type = IndexType.Xml };
        Assert.IsType<DdlNotBackupable>(SqlServerDdlGenerator.Generate(index));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DdlEligibilityTests"`
Expected: FAIL.

- [ ] **Step 3: Add guards and options to `Generate`**

Replace the top of `Generate` (before building the string) with the guards, and append the options clause before the semicolon:
```csharp
    public static DdlResult Generate(IndexModel index)
    {
        if (index.Type != IndexType.NonclusteredRowstore)
            return new DdlNotBackupable($"unsupported index type for DDL generation: {index.Type}");
        if (index.IsPartitioned)
            return new DdlNotBackupable("partitioned index DDL cannot be reconstructed with certainty");
        if (index.Options.Compression == DataCompression.Unsupported)
            return new DdlNotBackupable("unsupported data compression option");

        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique) sb.Append("UNIQUE ");
        sb.Append("NONCLUSTERED INDEX ").Append(Quote(index.Name));
        sb.Append(" ON ").Append(Quote(index.Schema)).Append('.').Append(Quote(index.Table));

        sb.Append(" (");
        sb.Append(string.Join(", ", index.KeyColumns.Select(
            c => $"{Quote(c.Name)} {(c.Direction == SortDirection.Ascending ? "ASC" : "DESC")}")));
        sb.Append(')');

        if (index.IncludedColumns.Count > 0)
            sb.Append(" INCLUDE (").Append(string.Join(", ", index.IncludedColumns.Select(Quote))).Append(')');

        if (!string.IsNullOrWhiteSpace(index.FilterPredicate))
            sb.Append(" WHERE ").Append(index.FilterPredicate);

        sb.Append(BuildOptions(index.Options));

        if (!string.IsNullOrWhiteSpace(index.DataSpace))
            sb.Append(" ON ").Append(Quote(index.DataSpace));

        sb.Append(';');
        return new DdlSuccess(sb.ToString());
    }

    private static string BuildOptions(IndexOptions o)
    {
        var parts = new List<string>
        {
            $"PAD_INDEX = {OnOff(o.PadIndex)}"
        };
        if (o.FillFactor is int ff) parts.Add($"FILLFACTOR = {ff}");
        parts.Add($"IGNORE_DUP_KEY = {OnOff(o.IgnoreDupKey)}");
        parts.Add($"ALLOW_ROW_LOCKS = {OnOff(o.AllowRowLocks)}");
        parts.Add($"ALLOW_PAGE_LOCKS = {OnOff(o.AllowPageLocks)}");
        parts.Add($"DATA_COMPRESSION = {o.Compression.ToString().ToUpperInvariant()}");
        return " WITH (" + string.Join(", ", parts) + ")";
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Ddl"`
Expected: PASS (structure + eligibility).

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Ddl/SqlServerDdlGenerator.cs tests/SmartIndexManager.Core.Tests/Ddl/DdlEligibilityTests.cs
git commit -m "feat(core): DDL options and not-backupable eligibility guards"
```

---

### Task 10: Deletion safety evaluator

**Files:**
- Create: `src/SmartIndexManager.Core/Safety/SafetyInputs.cs`, `SafetyWarning.cs`, `SafetyAssessment.cs`, `DeletionSafetyEvaluator.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Safety/DeletionSafetyEvaluatorTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `DdlResult`, `DdlNotBackupable`.
- Produces:
  - `enum DeletionEligibility { Deletable, NotDeletable }`
  - `SafetyWarning(string Code, string Message)`
  - `SafetyAssessment(DeletionEligibility Eligibility, string? BlockReason, IReadOnlyList<SafetyWarning> Warnings)`
  - `SafetyInputs { IndexModel Index; DdlResult Ddl; bool SupportsForeignKey; bool ReferencedByHint; bool DatabaseInReplicationOrAg; int InstanceUptimeDays; int UptimeReliabilityThresholdDays = 30 }`
  - `DeletionSafetyEvaluator.Evaluate(SafetyInputs) -> SafetyAssessment`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Safety/DeletionSafetyEvaluatorTests.cs`:
```csharp
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using Xunit;

namespace SmartIndexManager.Core.Tests.Safety;

public class DeletionSafetyEvaluatorTests
{
    private static IndexModel Deletable() => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
    };

    private static SafetyInputs Inputs(IndexModel index, DdlResult? ddl = null) => new()
    {
        Index = index,
        Ddl = ddl ?? new DdlSuccess("CREATE NONCLUSTERED INDEX ..."),
        InstanceUptimeDays = 90
    };

    [Fact]
    public void Plain_nonclustered_rowstore_is_deletable()
    {
        var a = DeletionSafetyEvaluator.Evaluate(Inputs(Deletable()));
        Assert.Equal(DeletionEligibility.Deletable, a.Eligibility);
        Assert.Null(a.BlockReason);
        Assert.Empty(a.Warnings);
    }

    [Theory]
    [InlineData(ConstraintKind.PrimaryKey)]
    [InlineData(ConstraintKind.Unique)]
    public void Constraint_backed_indexes_are_not_deletable(ConstraintKind kind)
    {
        var index = Deletable() with { Constraint = kind, IsUnique = kind == ConstraintKind.Unique };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Fact]
    public void Unique_without_constraint_is_not_deletable()
    {
        var index = Deletable() with { IsUnique = true };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Theory]
    [InlineData(IndexType.ClusteredRowstore)]
    [InlineData(IndexType.NonclusteredColumnstore)]
    [InlineData(IndexType.Xml)]
    [InlineData(IndexType.Spatial)]
    [InlineData(IndexType.Hypothetical)]
    public void Excluded_types_are_not_deletable(IndexType type)
    {
        var index = Deletable() with { Type = type };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Fact]
    public void Disabled_view_and_system_indexes_are_not_deletable()
    {
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsDisabled = true })).Eligibility);
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsOnView = true })).Eligibility);
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsOnSystemTable = true })).Eligibility);
    }

    [Fact]
    public void Not_backupable_ddl_blocks_deletion()
    {
        var a = DeletionSafetyEvaluator.Evaluate(
            Inputs(Deletable(), new DdlNotBackupable("partitioned")));
        Assert.Equal(DeletionEligibility.NotDeletable, a.Eligibility);
        Assert.Contains("DDL non sauvegardable", a.BlockReason);
    }

    [Fact]
    public void Guard_rails_surface_as_warnings_but_stay_deletable()
    {
        var a = DeletionSafetyEvaluator.Evaluate(new SafetyInputs
        {
            Index = Deletable() with { FilterPredicate = "Status = 1" },
            Ddl = new DdlSuccess("..."),
            SupportsForeignKey = true,
            ReferencedByHint = true,
            DatabaseInReplicationOrAg = true,
            InstanceUptimeDays = 5
        });

        Assert.Equal(DeletionEligibility.Deletable, a.Eligibility);
        var codes = a.Warnings.Select(w => w.Code).ToHashSet();
        Assert.Contains("fk-support", codes);
        Assert.Contains("filtered", codes);
        Assert.Contains("hint", codes);
        Assert.Contains("replication-ag", codes);
        Assert.Contains("short-uptime", codes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DeletionSafetyEvaluatorTests"`
Expected: FAIL, safety types do not exist.

- [ ] **Step 3: Write the implementation**

`SafetyWarning.cs`:
```csharp
namespace SmartIndexManager.Core.Safety;

public sealed record SafetyWarning(string Code, string Message);
```

`SafetyAssessment.cs`:
```csharp
namespace SmartIndexManager.Core.Safety;

public enum DeletionEligibility { Deletable, NotDeletable }

public sealed record SafetyAssessment(
    DeletionEligibility Eligibility,
    string? BlockReason,
    IReadOnlyList<SafetyWarning> Warnings);
```

`SafetyInputs.cs`:
```csharp
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Safety;

public sealed record SafetyInputs
{
    public required IndexModel Index { get; init; }
    public required DdlResult Ddl { get; init; }
    public bool SupportsForeignKey { get; init; }
    public bool ReferencedByHint { get; init; }
    public bool DatabaseInReplicationOrAg { get; init; }
    public required int InstanceUptimeDays { get; init; }
    public int UptimeReliabilityThresholdDays { get; init; } = 30;
}
```

`DeletionSafetyEvaluator.cs`:
```csharp
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Safety;

public static class DeletionSafetyEvaluator
{
    public static SafetyAssessment Evaluate(SafetyInputs inputs)
    {
        var index = inputs.Index;

        string? block = HardExclusion(index)
            ?? (inputs.Ddl is DdlNotBackupable nb ? $"DDL non sauvegardable: {nb.Reason}" : null);

        if (block is not null)
            return new SafetyAssessment(DeletionEligibility.NotDeletable, block, []);

        var warnings = new List<SafetyWarning>();
        if (inputs.SupportsForeignKey)
            warnings.Add(new SafetyWarning("fk-support", "This index supports a foreign key."));
        if (index.FilterPredicate is not null)
            warnings.Add(new SafetyWarning("filtered", "Filtered index."));
        if (inputs.ReferencedByHint)
            warnings.Add(new SafetyWarning("hint", "Referenced by a hint or plan guide; queries may fail."));
        if (inputs.DatabaseInReplicationOrAg)
            warnings.Add(new SafetyWarning("replication-ag", "Database is in replication or an availability group."));
        if (inputs.InstanceUptimeDays < inputs.UptimeReliabilityThresholdDays)
            warnings.Add(new SafetyWarning("short-uptime",
                $"Instance uptime {inputs.InstanceUptimeDays}d below reliability threshold."));

        return new SafetyAssessment(DeletionEligibility.Deletable, null, warnings);
    }

    private static string? HardExclusion(IndexModel index)
    {
        if (index.Type != IndexType.NonclusteredRowstore)
            return $"index type {index.Type} is never deletable";
        if (index.IsUnique || index.Constraint != ConstraintKind.None)
            return "unique or constraint-backed index";
        if (index.IsDisabled) return "disabled index";
        if (index.IsOnView) return "index on a view";
        if (index.IsOnSystemTable) return "index on a system table";
        return null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DeletionSafetyEvaluatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Safety/ tests/SmartIndexManager.Core.Tests/Safety/DeletionSafetyEvaluatorTests.cs
git commit -m "feat(core): deletion safety evaluator with hard exclusions and guard-rails"
```

---

### Task 11: SQL file header parser

**Files:**
- Create: `src/SmartIndexManager.Core/Sql/AzureSupport.cs`, `SqlFileHeader.cs`, `SqlFileHeaderException.cs`, `SqlFileHeaderParser.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Sql/SqlFileHeaderParserTests.cs`

**Interfaces:**
- Produces:
  - `enum AzureSupport { Supported, Unsupported, Only }`
  - `SqlFileHeader(string Name, Version MinVersion, AzureSupport Azure, IReadOnlyList<string> Columns)`
  - `class SqlFileHeaderException : Exception`
  - `SqlFileHeaderParser.Parse(string fileContent) -> SqlFileHeader`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Sql/SqlFileHeaderParserTests.cs`:
```csharp
using SmartIndexManager.Core.Sql;
using Xunit;

namespace SmartIndexManager.Core.Tests.Sql;

public class SqlFileHeaderParserTests
{
    private const string Valid = """
        -- sim: name=unused-indexes
        -- sim: minversion=11.0
        -- sim: azure=supported
        -- sim: columns=SchemaName,TableName,IndexName,UserSeeks
        SELECT 1;
        """;

    [Fact]
    public void Parses_a_valid_header()
    {
        var header = SqlFileHeaderParser.Parse(Valid);
        Assert.Equal("unused-indexes", header.Name);
        Assert.Equal(new Version(11, 0), header.MinVersion);
        Assert.Equal(AzureSupport.Supported, header.Azure);
        Assert.Equal(new[] { "SchemaName", "TableName", "IndexName", "UserSeeks" }, header.Columns);
    }

    [Fact]
    public void Azure_defaults_to_supported_when_absent()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\n-- sim: columns=A\nSELECT 1;";
        Assert.Equal(AzureSupport.Supported, SqlFileHeaderParser.Parse(content).Azure);
    }

    [Fact]
    public void Missing_name_throws()
    {
        var content = "-- sim: minversion=11.0\n-- sim: columns=A\nSELECT 1;";
        var ex = Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Missing_columns_throws()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\nSELECT 1;";
        Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
    }

    [Fact]
    public void Malformed_version_throws()
    {
        var content = "-- sim: name=x\n-- sim: minversion=abc\n-- sim: columns=A\nSELECT 1;";
        Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SqlFileHeaderParserTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`AzureSupport.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public enum AzureSupport { Supported, Unsupported, Only }
```

`SqlFileHeader.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public sealed record SqlFileHeader(
    string Name,
    Version MinVersion,
    AzureSupport Azure,
    IReadOnlyList<string> Columns);
```

`SqlFileHeaderException.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public sealed class SqlFileHeaderException : Exception
{
    public SqlFileHeaderException(string message) : base(message) { }
}
```

`SqlFileHeaderParser.cs`:
```csharp
namespace SmartIndexManager.Core.Sql;

public static class SqlFileHeaderParser
{
    private const string Prefix = "-- sim:";

    public static SqlFileHeader Parse(string fileContent)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in fileContent.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var body = line[Prefix.Length..].Trim();
            int eq = body.IndexOf('=');
            if (eq <= 0) throw new SqlFileHeaderException($"malformed metadata line: '{line}'");
            pairs[body[..eq].Trim()] = body[(eq + 1)..].Trim();
        }

        string name = Require(pairs, "name");
        string columnsRaw = Require(pairs, "columns");
        var columns = columnsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (columns.Length == 0) throw new SqlFileHeaderException("columns must list at least one column");

        Version minVersion = new(0, 0);
        if (pairs.TryGetValue("minversion", out var mv))
        {
            if (!Version.TryParse(mv, out var parsed))
                throw new SqlFileHeaderException($"invalid minversion: '{mv}'");
            minVersion = parsed;
        }

        var azure = AzureSupport.Supported;
        if (pairs.TryGetValue("azure", out var az))
            azure = az.ToLowerInvariant() switch
            {
                "supported" => AzureSupport.Supported,
                "unsupported" => AzureSupport.Unsupported,
                "only" => AzureSupport.Only,
                _ => throw new SqlFileHeaderException($"invalid azure value: '{az}'")
            };

        return new SqlFileHeader(name, minVersion, azure, columns);
    }

    private static string Require(Dictionary<string, string> pairs, string key)
        => pairs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new SqlFileHeaderException($"missing required metadata: {key}");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SqlFileHeaderParserTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Sql/ tests/SmartIndexManager.Core.Tests/Sql/SqlFileHeaderParserTests.cs
git commit -m "feat(core): parse external SQL file -- sim: headers"
```

---

### Task 12: Manifest model and store

**Files:**
- Create: `src/SmartIndexManager.Core/Persistence/CoreJson.cs`, `Manifest.cs`, `ManifestStore.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs`

**Interfaces:**
- Produces:
  - `CoreJson.Options -> JsonSerializerOptions` (camelCase, string enums, indented) shared by all persistence.
  - `enum DeletionMode { Execute, Script }`
  - `enum IndexDeletionStatus { Dropped, Failed, Scripted }`
  - `ManifestStats`, `ManifestIndexEntry`, `Manifest` records (fields in code below)
  - `ManifestStore.Write(string path, Manifest)`, `Read(string path) -> Manifest`, `MarkRestored(Manifest, string db, string schema, string table, string index, DateTime restoredUtc) -> Manifest`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs`:
```csharp
using SmartIndexManager.Core.Persistence;
using Xunit;

namespace SmartIndexManager.Core.Tests.Persistence;

public class ManifestStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-manifest-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Manifest Sample() => new()
    {
        ToolVersion = "1.0.0",
        CreatedUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
        Server = "PROD01", Operator = "DOMAIN\\rudi", InstanceUptimeDays = 92,
        Mode = DeletionMode.Execute,
        Indexes =
        [
            new ManifestIndexEntry
            {
                Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_Orders_Legacy",
                File = "Sales.dbo.Orders.IX_Orders_Legacy.sql",
                Reason = "0 reads in 92 days", Score = 94,
                Stats = new ManifestStats { Updates = 1200000, SizeMb = 830 },
                Status = IndexDeletionStatus.Dropped
            }
        ]
    };

    [Fact]
    public void Round_trips_with_schema_version()
    {
        var path = Path.Combine(_dir, "manifest.json");
        ManifestStore.Write(path, Sample());

        var text = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("\"status\": \"dropped\"", text);   // camelCase, matches design spec section 9
        Assert.Contains("\"mode\": \"execute\"", text);

        var read = ManifestStore.Read(path);
        Assert.Equal("PROD01", read.Server);
        Assert.Single(read.Indexes);
        Assert.Equal("IX_Orders_Legacy", read.Indexes[0].Index);
    }

    [Fact]
    public void MarkRestored_sets_the_timestamp_on_the_matching_index()
    {
        var when = new DateTime(2026, 07, 21, 8, 0, 0, DateTimeKind.Utc);
        var updated = ManifestStore.MarkRestored(Sample(), "Sales", "dbo", "Orders", "IX_Orders_Legacy", when);
        Assert.Equal(when, updated.Indexes[0].RestoredUtc);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ManifestStoreTests"`
Expected: FAIL.

- [ ] **Step 3: Write `CoreJson`, the records, and the store**

`CoreJson.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.Core.Persistence;

public static class CoreJson
{
    // Enum values are camelCased to match the manifest example in the design spec
    // (section 9 shows "status": "dropped", "mode": "execute"). PropertyNamingPolicy
    // alone does NOT affect enum string values; the converter needs its own policy.
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
```

`Manifest.cs`:
```csharp
namespace SmartIndexManager.Core.Persistence;

public enum DeletionMode { Execute, Script }
public enum IndexDeletionStatus { Dropped, Failed, Scripted }

public sealed record ManifestStats
{
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
    public double SizeMb { get; init; }
}

public sealed record ManifestIndexEntry
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public required string File { get; init; }
    public required string Reason { get; init; }
    public string Comment { get; init; } = "";
    public int Score { get; init; }
    public ManifestStats Stats { get; init; } = new();
    public IndexDeletionStatus Status { get; init; }
    public DateTime? RestoredUtc { get; init; }
}

public sealed record Manifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Tool { get; init; } = "SmartIndexManager";
    public required string ToolVersion { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required string Server { get; init; }
    public required string Operator { get; init; }
    public int InstanceUptimeDays { get; init; }
    public DeletionMode Mode { get; init; }
    public IReadOnlyList<ManifestIndexEntry> Indexes { get; init; } = [];
}
```

`ManifestStore.cs`:
```csharp
using System.Text.Json;

namespace SmartIndexManager.Core.Persistence;

public static class ManifestStore
{
    public static void Write(string path, Manifest manifest)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, CoreJson.Options));
    }

    public static Manifest Read(string path)
        => JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), CoreJson.Options)
           ?? throw new InvalidDataException($"manifest at {path} deserialized to null");

    public static Manifest MarkRestored(
        Manifest manifest, string database, string schema, string table, string index, DateTime restoredUtc)
    {
        var updated = manifest.Indexes.Select(e =>
            Matches(e, database, schema, table, index) ? e with { RestoredUtc = restoredUtc } : e).ToList();
        return manifest with { Indexes = updated };
    }

    private static bool Matches(ManifestIndexEntry e, string db, string schema, string table, string index)
        => string.Equals(e.Database, db, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Schema, schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Index, index, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ManifestStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Persistence/CoreJson.cs src/SmartIndexManager.Core/Persistence/Manifest.cs src/SmartIndexManager.Core/Persistence/ManifestStore.cs tests/SmartIndexManager.Core.Tests/Persistence/ManifestStoreTests.cs
git commit -m "feat(core): manifest model, JSON store, and restore marking"
```

---

### Task 13: Snapshot store, retention, and observed-since

> PREREQUISITE: complete Task 14 (`FileNameSanitizer`) before this task. `SnapshotStore` reuses `FileNameSanitizer.SanitizeComponent` for its `server` and `database` directory names. Task 14 is the only backward dependency in this plan; if executing task-by-task, run Task 14 first, then Task 13.

**Files:**
- Create: `src/SmartIndexManager.Core/Persistence/UsageSnapshot.cs`, `SnapshotStore.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Persistence/SnapshotStoreTests.cs`

**Interfaces:**
- Consumes: `CoreJson`.
- Produces:
  - `SnapshotIndexUsage`, `UsageSnapshot` records
  - `SnapshotStore.Write(string rootDir, UsageSnapshot) -> string path`
  - `SnapshotStore.ReadAll(string rootDir, string server, string database) -> IReadOnlyList<UsageSnapshot>`
  - `SnapshotStore.OldestCaptureUtc(string rootDir, string server, string database) -> DateTime?`
  - `SnapshotStore.PurgeOlderThan(string rootDir, string server, string database, DateTime cutoffUtc) -> int`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Persistence/SnapshotStoreTests.cs`:
```csharp
using SmartIndexManager.Core.Persistence;
using Xunit;

namespace SmartIndexManager.Core.Tests.Persistence;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sim-snap-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static UsageSnapshot Snap(DateTime capturedUtc) => new()
    {
        Server = "PROD01", Database = "Sales", CapturedUtc = capturedUtc, InstanceUptimeDays = 40,
        Indexes = [new SnapshotIndexUsage { Schema = "dbo", Table = "Orders", Index = "IX", Seeks = 5 }]
    };

    [Fact]
    public void Write_then_read_all_round_trips()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        var all = SnapshotStore.ReadAll(_root, "PROD01", "Sales");
        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.Equal(1, s.SchemaVersion));
    }

    [Fact]
    public void Oldest_capture_returns_earliest_timestamp()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
            SnapshotStore.OldestCaptureUtc(_root, "PROD01", "Sales"));
    }

    [Fact]
    public void Purge_removes_captures_older_than_cutoff()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        int purged = SnapshotStore.PurgeOlderThan(_root, "PROD01", "Sales",
            new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, purged);
        Assert.Single(SnapshotStore.ReadAll(_root, "PROD01", "Sales"));
    }

    [Fact]
    public void Oldest_capture_is_null_when_none_exist()
        => Assert.Null(SnapshotStore.OldestCaptureUtc(_root, "PROD01", "Sales"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SnapshotStoreTests"`
Expected: FAIL.

- [ ] **Step 3: Write the records and store**

`UsageSnapshot.cs`:
```csharp
namespace SmartIndexManager.Core.Persistence;

public sealed record SnapshotIndexUsage
{
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
}

public sealed record UsageSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public required string Server { get; init; }
    public required string Database { get; init; }
    public required DateTime CapturedUtc { get; init; }
    public int InstanceUptimeDays { get; init; }
    public IReadOnlyList<SnapshotIndexUsage> Indexes { get; init; } = [];
}
```

`SnapshotStore.cs` (the capture timestamp is the file name, formatted without a colon so it is valid on every filesystem):
```csharp
using System.Text.Json;
using SmartIndexManager.Core.Backup;

namespace SmartIndexManager.Core.Persistence;

public static class SnapshotStore
{
    public static string Write(string rootDir, UsageSnapshot snapshot)
    {
        var dir = DirFor(rootDir, snapshot.Server, snapshot.Database);
        Directory.CreateDirectory(dir);
        var fileName = snapshot.CapturedUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + ".json";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, CoreJson.Options));
        return path;
    }

    public static IReadOnlyList<UsageSnapshot> ReadAll(string rootDir, string server, string database)
    {
        var dir = DirFor(rootDir, server, database);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(p => JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(p), CoreJson.Options))
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.CapturedUtc)
            .ToList();
    }

    public static DateTime? OldestCaptureUtc(string rootDir, string server, string database)
    {
        var all = ReadAll(rootDir, server, database);
        return all.Count == 0 ? null : all[0].CapturedUtc;
    }

    public static int PurgeOlderThan(string rootDir, string server, string database, DateTime cutoffUtc)
    {
        var dir = DirFor(rootDir, server, database);
        if (!Directory.Exists(dir)) return 0;
        int purged = 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").ToList())
        {
            var snap = JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path), CoreJson.Options);
            if (snap is not null && snap.CapturedUtc < cutoffUtc)
            {
                File.Delete(path);
                purged++;
            }
        }
        return purged;
    }

    private static string DirFor(string rootDir, string server, string database)
        => Path.Combine(rootDir, "snapshots",
            FileNameSanitizer.SanitizeComponent(server),
            FileNameSanitizer.SanitizeComponent(database));
}
```

Note: this task references `SmartIndexManager.Core.Backup.FileNameSanitizer` from Task 14 (see the prerequisite callout at the top of this task). Task 14 must be built first.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SnapshotStoreTests"`
Expected: PASS (after `FileNameSanitizer` exists).

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Persistence/UsageSnapshot.cs src/SmartIndexManager.Core/Persistence/SnapshotStore.cs tests/SmartIndexManager.Core.Tests/Persistence/SnapshotStoreTests.cs
git commit -m "feat(core): usage snapshot store with retention and observed-since"
```

---

### Task 14: File-name sanitizer and backup writer

**Files:**
- Create: `src/SmartIndexManager.Core/Backup/FileNameSanitizer.cs`, `BackupWriter.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Backup/FileNameSanitizerTests.cs`, `BackupWriterTests.cs`

**Interfaces:**
- Consumes: `IndexModel`, `IndexUsageStats`.
- Produces:
  - `FileNameSanitizer.SanitizeComponent(string) -> string`
  - `FileNameSanitizer.BuildIndexBackupFileName(string db, string schema, string table, string index) -> string`
  - `BackupHeaderInfo { DateTime DateUtc; string Server; string Database; string Operator; string Reason; string Comment; IndexUsageStats Stats }`
  - `BackupWriter.WriteIndexBackup(string sessionDir, IndexModel index, string ddl, BackupHeaderInfo header) -> string fileName`

Implement this task before Task 13 (SnapshotStore depends on `FileNameSanitizer`).

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Backup/FileNameSanitizerTests.cs`:
```csharp
using SmartIndexManager.Core.Backup;
using Xunit;

namespace SmartIndexManager.Core.Tests.Backup;

public class FileNameSanitizerTests
{
    [Fact]
    public void Clean_components_keep_the_dot_separator()
    {
        var name = FileNameSanitizer.BuildIndexBackupFileName("Sales", "dbo", "Orders", "IX_Orders_Legacy");
        Assert.Equal("Sales.dbo.Orders.IX_Orders_Legacy.sql", name);
    }

    [Fact]
    public void Dots_inside_a_component_are_sanitized_but_separators_remain()
    {
        var name = FileNameSanitizer.BuildIndexBackupFileName("Sales", "dbo", "Orders.2024", "IX_Legacy");
        Assert.Equal("Sales.dbo.Orders_2024.IX_Legacy.sql", name);
    }

    [Theory]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a b", "a_b")]
    [InlineData("[weird]", "_weird_")]
    public void Illegal_characters_become_underscore(string input, string expected)
        => Assert.Equal(expected, FileNameSanitizer.SanitizeComponent(input));
}
```

`tests/SmartIndexManager.Core.Tests/Backup/BackupWriterTests.cs`:
```csharp
using SmartIndexManager.Core.Backup;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Backup;

public class BackupWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-backup-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Writes_ddl_with_a_comment_header_and_returns_the_file_name()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Legacy",
            Type = IndexType.NonclusteredRowstore
        };
        var header = new BackupHeaderInfo
        {
            DateUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            Server = "PROD01", Database = "Sales", Operator = "DOMAIN\\rudi",
            Reason = "0 reads in 92 days", Stats = IndexUsageStats.Empty
        };

        var fileName = BackupWriter.WriteIndexBackup(_dir, index, "CREATE NONCLUSTERED INDEX [IX_Legacy] ...;", header);

        Assert.Equal("Sales.dbo.Orders.IX_Legacy.sql", fileName);
        var content = File.ReadAllText(Path.Combine(_dir, fileName));
        Assert.Contains("-- Server: PROD01", content);
        Assert.Contains("-- Reason: 0 reads in 92 days", content);
        Assert.Contains("-- LastRead (UTC): never", content);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Legacy]", content);
    }

    [Fact]
    public void Colliding_names_get_a_numeric_suffix_and_never_overwrite()
    {
        // Two distinct index names that sanitize to the same file name.
        var a = new IndexModel { Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX.A", Type = IndexType.NonclusteredRowstore };
        var b = new IndexModel { Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A", Type = IndexType.NonclusteredRowstore };
        var header = new BackupHeaderInfo
        {
            DateUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            Server = "PROD01", Database = "Sales", Operator = "op", Reason = "r", Stats = IndexUsageStats.Empty
        };

        var first = BackupWriter.WriteIndexBackup(_dir, a, "CREATE ... [IX.A];", header);
        var second = BackupWriter.WriteIndexBackup(_dir, b, "CREATE ... [IX_A];", header);

        Assert.Equal("Sales.dbo.Orders.IX_A.sql", first);
        Assert.Equal("Sales.dbo.Orders.IX_A (2).sql", second);
        Assert.Contains("[IX.A]", File.ReadAllText(Path.Combine(_dir, first)));   // first not overwritten
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Backup"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`FileNameSanitizer.cs`:
```csharp
using System.Text;

namespace SmartIndexManager.Core.Backup;

public static class FileNameSanitizer
{
    // Anything illegal for a filesystem, ambiguous with the '.' separator, or a
    // path separator becomes '_'. The result is intentionally not reversible.
    public static string SanitizeComponent(string component)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '.', '[', ']', '/', '\\', ' ' };
        var sb = new StringBuilder(component.Length);
        foreach (var ch in component)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }

    public static string BuildIndexBackupFileName(string database, string schema, string table, string index)
        => string.Join('.',
            SanitizeComponent(database),
            SanitizeComponent(schema),
            SanitizeComponent(table),
            SanitizeComponent(index)) + ".sql";
}
```

`BackupWriter.cs`:
```csharp
using System.Text;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Backup;

public sealed record BackupHeaderInfo
{
    public required DateTime DateUtc { get; init; }
    public required string Server { get; init; }
    public required string Database { get; init; }
    public required string Operator { get; init; }
    public required string Reason { get; init; }
    public string Comment { get; init; } = "";
    public required IndexUsageStats Stats { get; init; }
}

public static class BackupWriter
{
    public static string WriteIndexBackup(string sessionDir, IndexModel index, string ddl, BackupHeaderInfo header)
    {
        Directory.CreateDirectory(sessionDir);
        var baseName = FileNameSanitizer.BuildIndexBackupFileName(
            index.Database, index.Schema, index.Table, index.Name);
        var fileName = ResolveCollision(sessionDir, baseName);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Date (UTC): {header.DateUtc:O}");
        sb.AppendLine($"-- Server: {header.Server}");
        sb.AppendLine($"-- Database: {header.Database}");
        sb.AppendLine($"-- Index: {index.Schema}.{index.Table}.{index.Name}");
        sb.AppendLine($"-- Operator: {header.Operator}");
        sb.AppendLine($"-- Reason: {header.Reason}");
        if (!string.IsNullOrWhiteSpace(header.Comment))
            sb.AppendLine($"-- Comment: {header.Comment}");
        sb.AppendLine($"-- Stats: seeks={header.Stats.Seeks} scans={header.Stats.Scans} " +
                      $"lookups={header.Stats.Lookups} updates={header.Stats.Updates}");
        sb.AppendLine($"-- LastRead (UTC): {Fmt(header.Stats.LastRead)} " +
                      $"LastWrite (UTC): {Fmt(header.Stats.LastWrite)}");
        sb.AppendLine();
        sb.AppendLine(ddl);

        File.WriteAllText(Path.Combine(sessionDir, fileName), sb.ToString());
        return fileName;   // the manifest 'file' field stores exactly this resolved name
    }

    // Sanitization is not reversible, so two distinct indexes can map to the same
    // base name. A backup .sql is the only recovery artifact, so never overwrite:
    // append " (2)", " (3)", ... before the extension.
    private static string ResolveCollision(string sessionDir, string baseName)
    {
        var path = Path.Combine(sessionDir, baseName);
        if (!File.Exists(path)) return baseName;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        for (int n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!File.Exists(Path.Combine(sessionDir, candidate))) return candidate;
        }
    }

    private static string Fmt(DateTime? value) => value?.ToString("O") ?? "never";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Backup"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Backup/ tests/SmartIndexManager.Core.Tests/Backup/
git commit -m "feat(core): file-name sanitizer and index backup writer"
```

---

### Task 15: Audit log

**Files:**
- Create: `src/SmartIndexManager.Core/Audit/AuditEntry.cs`, `AuditLog.cs`
- Test: `tests/SmartIndexManager.Core.Tests/Audit/AuditLogTests.cs`

**Interfaces:**
- Consumes: `CoreJson`.
- Produces:
  - `enum AuditAction { Drop, Restore, EnableQueryStore, GenerateScript }`
  - `AuditEntry(DateTime TimestampUtc, AuditAction Action, string Server, string Database, string Operator, string Detail)`
  - `AuditLog.Append(string logFilePath, AuditEntry)`, `AuditLog.Read(string logFilePath) -> IReadOnlyList<AuditEntry>`

- [ ] **Step 1: Write the failing test**

`tests/SmartIndexManager.Core.Tests/Audit/AuditLogTests.cs`:
```csharp
using SmartIndexManager.Core.Audit;
using Xunit;

namespace SmartIndexManager.Core.Tests.Audit;

public class AuditLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-audit-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Append_writes_one_json_line_per_entry_and_reads_them_back()
    {
        var path = Path.Combine(_dir, "audit.jsonl");
        AuditLog.Append(path, new AuditEntry(
            new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            AuditAction.Drop, "PROD01", "Sales", "DOMAIN\\rudi", "Dropped IX_Orders_Legacy"));
        AuditLog.Append(path, new AuditEntry(
            new DateTime(2026, 07, 20, 10, 5, 0, DateTimeKind.Utc),
            AuditAction.Restore, "PROD01", "Sales", "DOMAIN\\rudi", "Restored IX_Orders_Legacy"));

        Assert.Equal(2, File.ReadAllLines(path).Length); // one line per entry (JSONL)

        var entries = AuditLog.Read(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.Drop, entries[0].Action);
        Assert.Equal("Restored IX_Orders_Legacy", entries[1].Detail);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AuditLogTests"`
Expected: FAIL.

- [ ] **Step 3: Write the implementation**

`AuditEntry.cs`:
```csharp
namespace SmartIndexManager.Core.Audit;

public enum AuditAction { Drop, Restore, EnableQueryStore, GenerateScript }

public sealed record AuditEntry(
    DateTime TimestampUtc,
    AuditAction Action,
    string Server,
    string Database,
    string Operator,
    string Detail);
```

`AuditLog.cs` (JSONL: one compact JSON object per line, appended):
```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.Core.Audit;

public static class AuditLog
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Append(string logFilePath, AuditEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        var line = JsonSerializer.Serialize(entry, LineOptions);
        File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }

    // A corrupt or truncated line must not stop the rest of the log from being read.
    public static IReadOnlyList<AuditEntry> Read(string logFilePath)
    {
        if (!File.Exists(logFilePath)) return [];
        var entries = new List<AuditEntry>();
        foreach (var line in File.ReadAllLines(logFilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            AuditEntry? entry;
            try { entry = JsonSerializer.Deserialize<AuditEntry>(line, LineOptions); }
            catch (JsonException) { continue; }
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }
}
```

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: PASS, every task's tests green.

- [ ] **Step 5: Commit**

```bash
git add src/SmartIndexManager.Core/Audit/ tests/SmartIndexManager.Core.Tests/Audit/AuditLogTests.cs
git commit -m "feat(core): JSONL audit log append and read"
```

---

## Self-Review

Spec coverage against `docs/specs/2026-07-20-smartindexmanager-design.md`, Core-relevant sections:

- Section 3 (model, capabilities, ProviderProperties): Task 1. The `IIndexProvider` interface itself belongs to the Provider plan; the shared model and capabilities record it consumes are here.
- Section 4 (DDL generated in Core, SQL header contract): Tasks 8, 9 (DDL), Task 11 (header parsing). Writing the actual `sql/sqlserver/*.sql` files belongs to the Provider plan; parsing their contract is here.
- Section 5 (normalization, R1/R2/R3, edge cases): Tasks 2, 3, 4, 5.
- Section 6 (hard exclusions, guard-rails; DDL-not-backupable): Task 10, plus Task 9 for the not-backupable rule. The transactional drop flow (regenerate, write, verify, drop, audit) is orchestration that needs the provider's DROP execution, so it belongs to the Provider plan; its Core building blocks (DDL, backup writer, manifest, audit) are all here.
- Section 7 (score, bonuses then caps, colors): Tasks 6, 7. The freshness-weight calibration is flagged as a follow-up with a working default shipped.
- Section 9 (backup file naming, manifest schema, restore marking): Tasks 12, 14. The restore screen is App; `MarkRestored` and the manifest it edits are here.
- Section 10 (snapshots, retention, observed-since, silent purge): Task 13.
- Section 12 (audit JSONL): Task 15.

Deferred by design to later plans (not gaps): connection management and permissions (section 11), Query Store enable SQL execution (section 11), dry-run report assembly (section 8, needs live diagnostics), the grid and detail UX (section 12), the provider itself and the `sql/sqlserver` files (sections 3, 4).

Placeholder scan: no TBD/TODO. The freshness weight ships with a concrete linear-decay default and is called out as a tuning follow-up, not a placeholder.

Type consistency check: `IndexUsageStats.TotalReads` used by scorer, redundancy keeper choice, and safety; `DdlResult`/`DdlNotBackupable` consumed by safety; `CoreJson.Options` shared by manifest and snapshot; `FileNameSanitizer.SanitizeComponent` consumed by both `BackupWriter` and `SnapshotStore` (ordering note added: build Task 14 before Task 13). No name drift found.

One ordering correction applied inline: Task 14 (`FileNameSanitizer`) must precede Task 13 (`SnapshotStore`), noted in both tasks.
