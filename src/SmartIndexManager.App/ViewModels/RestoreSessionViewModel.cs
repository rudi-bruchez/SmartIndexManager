using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Restore;

namespace SmartIndexManager.App.ViewModels;

public sealed class RestoreSessionViewModel
{
    public RestoreSession Session { get; }
    public string Title { get; }
    public List<ManifestIndexEntry> Entries { get; }

    public RestoreSessionViewModel(RestoreSession session)
    {
        Session = session;
        Title = session.Manifest.CreatedUtc.ToString("yyyy-MM-dd HH:mm");
        Entries = session.Entries.ToList();
    }
}
