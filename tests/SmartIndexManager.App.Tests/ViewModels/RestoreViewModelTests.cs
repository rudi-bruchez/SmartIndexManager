using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Backup;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Settings;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class RestoreViewModelTests
{
    [Fact]
    public async Task LoadAsync_populates_sessions()
    {
        var tempRoot = Directory.CreateTempSubdirectory("sim-restore-load-").FullName;
        try
        {
            var server = "PROD01";
            var sessionDir = CreateSessionDir(tempRoot, server, "2026-07-22T10-00-00");

            var manifest = new Manifest
            {
                ToolVersion = "1.0.0",
                CreatedUtc = new DateTime(2026, 07, 22, 10, 0, 0, DateTimeKind.Utc),
                Server = server,
                Operator = "op",
                InstanceUptimeDays = 100,
                Mode = DeletionMode.Execute,
                Indexes = []
            };
            ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);

            var provider = CreateProvider(server);
            var vm = new RestoreViewModel(CreatePaths(tempRoot), new ResxLocalizer());
            vm.SetProvider(provider);
            await vm.LoadAsync(CancellationToken.None);

            var sessionVm = Assert.Single(vm.Sessions);
            Assert.Equal("2026-07-22 10:00", sessionVm.Title);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreAsync_executes_backup_ddl_and_updates_manifest()
    {
        var tempRoot = Directory.CreateTempSubdirectory("sim-restore-").FullName;
        try
        {
            var server = "PROD01";
            var sessionDir = CreateSessionDir(tempRoot, server, "2026-07-22T10-00-00");
            var backupFile = "Sales.dbo.Orders.IX_A.sql";
            var ddl = "CREATE NONCLUSTERED INDEX [IX_A] ON [Sales].[dbo].[Orders] ([A]);";

            var manifest = new Manifest
            {
                ToolVersion = "1.0.0",
                CreatedUtc = new DateTime(2026, 07, 22, 10, 0, 0, DateTimeKind.Utc),
                Server = server,
                Operator = "op",
                InstanceUptimeDays = 100,
                Mode = DeletionMode.Execute,
                Indexes =
                [
                    new ManifestIndexEntry
                    {
                        Database = "Sales",
                        Schema = "dbo",
                        Table = "Orders",
                        Index = "IX_A",
                        File = backupFile,
                        Reason = "unused",
                        Status = IndexDeletionStatus.Dropped
                    }
                ]
            };
            ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);
            await File.WriteAllTextAsync(Path.Combine(sessionDir, backupFile), ddl);

            var provider = CreateProvider(server);
            var vm = new RestoreViewModel(CreatePaths(tempRoot), new ResxLocalizer());
            vm.SetProvider(provider);
            await vm.LoadAsync(CancellationToken.None);

            var sessionVm = Assert.Single(vm.Sessions);
            await vm.RestoreCommand.ExecuteAsync(sessionVm);

            Assert.Equal("Sales", provider.LastDdlDatabase);
            Assert.Equal(ddl, provider.LastDdlSql);

            var updatedManifest = ManifestStore.Read(Path.Combine(sessionDir, "manifest.json"));
            Assert.Equal(IndexDeletionStatus.Restored, updatedManifest.Indexes[0].Status);
            Assert.NotNull(updatedManifest.Indexes[0].RestoredUtc);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateSessionDir(string tempRoot, string server, string timestamp)
    {
        var serverDir = Path.Combine(tempRoot, FileNameSanitizer.SanitizeComponent(server));
        var sessionDir = Path.Combine(serverDir, timestamp);
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }

    private static IAppPaths CreatePaths(string tempRoot)
        => new AppPaths(tempRoot, tempRoot, "/sql", new AppSettings { DefaultBackupRoot = tempRoot });

    private static FakeIndexProvider CreateProvider(string server)
        => new()
        {
            ServerInfo = new ServerInfo
            {
                ServerName = server,
                ProductVersion = new Version(16, 0),
                Edition = "Developer",
                Platform = ServerPlatform.OnPremises,
                UptimeDays = 100
            },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true }
        };
}
