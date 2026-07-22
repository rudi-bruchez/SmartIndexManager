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
            // Already-restored entries are idempotent; skip them even if the caller passes them.
            if (entry.Status == IndexDeletionStatus.Restored)
                continue;

            try
            {
                var filePath = Path.Combine(session.Directory, entry.File);
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Backup file not found: {entry.File}");

                if (await provider.IndexExistsAsync(entry.Database, entry.Schema, entry.Table, entry.Index, cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException($"Index {entry.Schema}.{entry.Table}.{entry.Index} already exists.");

                // Table-existence pre-check: the backup DDL references a schema/table, and replaying it
                // against a missing table would fail. The companion script sql/sqlserver/table-exists.sql
                // is available for providers that expose a TableExists check; until then ExecuteDdlAsync
                // surfaces the missing-table error from the server.
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
