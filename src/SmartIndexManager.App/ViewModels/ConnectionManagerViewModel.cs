using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionManagerViewModel : ViewModelBase
{
    private readonly IConnectionStore _store;
    private readonly IAuthAvailability _auth;

    [ObservableProperty] private ConnectionProfile? _selected;
    [ObservableProperty] private string _databasesText = "";

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ConnectionManagerViewModel(IConnectionStore store, IAuthAvailability auth)
    {
        _store = store;
        _auth = auth;
        foreach (var p in _store.Load()) Profiles.Add(p);
    }

    public IReadOnlyList<string> SelectedDatabases =>
        DatabasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public ConnectionEditorViewModel NewEditor() => new(_auth);

    public ConnectionEditorViewModel EditorFor(ConnectionProfile profile) => ConnectionEditorViewModel.FromProfile(profile, _auth);

    [RelayCommand]
    private void Delete(ConnectionProfile profile)
    {
        Profiles.Remove(profile);
        _store.Save(Profiles.ToList());
    }

    public void Upsert(ConnectionProfile profile)
    {
        var existing = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
        else Profiles.Add(profile);
        _store.Save(Profiles.ToList());
    }
}
