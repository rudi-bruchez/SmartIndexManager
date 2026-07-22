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
