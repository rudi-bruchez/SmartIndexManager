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
                scriptBuilder?.AppendLine($"USE {SqlIdentifier.Quote(index.Database)};");
                scriptBuilder?.AppendLine(SqlServerDdlGenerator.GenerateDropStatement(index.Schema, index.Table, index.Name));
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
        var reads = index.Usage.Seeks + index.Usage.Scans + index.Usage.Lookups;
        var sb = new StringBuilder();
        sb.Append($"{reads} reads, {index.Usage.Updates} updates");
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
}
