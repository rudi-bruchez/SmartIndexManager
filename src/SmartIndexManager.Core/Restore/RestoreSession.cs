using SmartIndexManager.Core.Persistence;

namespace SmartIndexManager.Core.Restore;

public sealed record RestoreSession(
    string Directory,
    Manifest Manifest,
    IReadOnlyList<ManifestIndexEntry> Entries);
