using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionManagerViewModel : ViewModelBase
{
    private readonly IConnectionStore _store;
    private readonly IAuthAvailability _auth;

    [ObservableProperty] private ConnectionProfile? _selected;
    [ObservableProperty] private string _databasesText = "";
    [ObservableProperty] private ConnectionEditorViewModel? _editor;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ConnectionProfile? ProfileToConnect { get; private set; }

    public ConnectionManagerViewModel(IConnectionStore store, IAuthAvailability auth)
    {
        _store = store;
        _auth = auth;
        foreach (var p in _store.Load()) Profiles.Add(p);
    }

    public IReadOnlyList<string> SelectedDatabases =>
        DatabasesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Keep a mutable editor bound to whatever profile is selected so the view can edit it.
    partial void OnSelectedChanged(ConnectionProfile? value)
        => Editor = value is null ? null : EditorFor(value);

    public ConnectionEditorViewModel NewEditor() => new(_auth);

    public ConnectionEditorViewModel EditorFor(ConnectionProfile profile) => ConnectionEditorViewModel.FromProfile(profile, _auth);

    [RelayCommand]
    private void Add()
    {
        var name = UniqueDefaultName();
        var profile = new ConnectionProfile
        {
            Name = name,
            Server = "",
            Auth = _auth.IsAvailable(AuthMode.WindowsIntegrated)
                ? AuthMode.WindowsIntegrated
                : AuthMode.SqlLogin,
        };
        Profiles.Add(profile);
        Selected = profile;
    }

    [RelayCommand]
    private void Save()
    {
        if (Editor is null) return;
        var profile = Editor.ToProfile();
        Upsert(profile);
        Selected = Profiles.FirstOrDefault(p => p.Name == profile.Name);
    }

    [RelayCommand]
    private void Connect()
    {
        if (Selected is null) return;
        ProfileToConnect = Selected;
        OnPropertyChanged(nameof(ProfileToConnect));
    }

    [RelayCommand]
    private void Delete(ConnectionProfile profile)
    {
        Profiles.Remove(profile);
        if (ReferenceEquals(Selected, profile)) Selected = null;
        _store.Save(Profiles.ToList());
    }

    public void Upsert(ConnectionProfile profile)
    {
        var existing = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
        else Profiles.Add(profile);
        _store.Save(Profiles.ToList());
    }

    private string UniqueDefaultName()
    {
        var i = 1;
        while (Profiles.Any(p => p.Name == $"New connection {i}")) i++;
        return $"New connection {i}";
    }
}
