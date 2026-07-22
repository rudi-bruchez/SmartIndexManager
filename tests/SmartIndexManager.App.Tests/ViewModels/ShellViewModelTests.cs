using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ShellViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-shell-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class NoDialogs : IDialogService
    {
        public Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm) => Task.CompletedTask;
    }

    private ShellViewModel Build()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.WindowsIntegrated },
            DatabasesText = "Sales"
        };
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var session = new ConnectionSessionViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(provider), paths),
            new NullPasswordPrompt(), connections, new NoDialogs(), new ResxLocalizer());
        var basket = new DeletionBasket();
        var dryRun = new DryRunViewModel(basket, paths, new ResxLocalizer());
        var basketVm = new DeletionBasketViewModel(basket, new DeletionOrchestrator(Path.Combine(_dir, "audit.jsonl")), dryRun, paths, new ResxLocalizer());
        var browse = new BrowseViewModel(new IndexGridViewModel(), basketVm, paths, new ResxLocalizer());
        var restore = new RestoreViewModel(paths, new ResxLocalizer());
        var audit = new AuditViewModel(paths, new ResxLocalizer());
        var settings = new SettingsViewModel(new SettingsService(), paths, new ResxLocalizer());
        return new ShellViewModel(session, browse, basketVm, restore, audit, settings,
            new PermissionStatusViewModel(new ResxLocalizer()), new ThemeService(paths), new ResxLocalizer());
    }

    [Fact]
    public void Default_destination_is_browse_and_current_page_is_the_browse_vm()
    {
        var shell = Build();
        Assert.Equal(5, shell.Destinations.Count);
        Assert.Same(shell.Destinations[0], shell.SelectedDestination);
        Assert.IsType<BrowseViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void Selecting_a_destination_sets_current_page()
    {
        var shell = Build();
        var last = shell.Destinations[^1];   // Settings, by position (title is localized, so assert by position)
        shell.SelectedDestination = last;
        Assert.Same(last.PageViewModel, shell.CurrentPage);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task Connecting_fills_browse_and_updates_permissions()
    {
        var shell = Build();
        await shell.Connection.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(BrowseState.Ready, ((BrowseViewModel)shell.CurrentPage!).State);
        Assert.False(shell.Permissions.UsageAvailable);
    }

    [Fact]
    public void ToggleTheme_flips_and_persists()
    {
        var shell = Build();
        var before = shell.IsDarkTheme;
        shell.ToggleThemeCommand.Execute(null);
        Assert.NotEqual(before, shell.IsDarkTheme);
        Assert.Equal(shell.IsDarkTheme, new ThemeService(new AppPaths(_dir, _dir, _dir)).Current == ThemeVariantKind.Dark);
    }

    [Fact]
    public void Ctor_builds_destinations_without_dry_run_viewmodel()
    {
        var provider = new ServiceCollection().AddAppServices("/tmp/sql/sqlserver").BuildServiceProvider();
        var shell = provider.GetRequiredService<ShellViewModel>();
        Assert.Contains(shell.Destinations, d => d.PageViewModel is DeletionBasketViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is RestoreViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is AuditViewModel);
        Assert.Contains(shell.Destinations, d => d.PageViewModel is SettingsViewModel);
        Assert.DoesNotContain(shell.Destinations, d => d.PageViewModel is DryRunViewModel);
    }
}
