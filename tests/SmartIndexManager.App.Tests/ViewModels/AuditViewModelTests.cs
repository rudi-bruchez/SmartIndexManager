using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Audit;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class AuditViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-auditvm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task LoadAsync_reads_audit_entries()
    {
        var path = Path.Combine(_dir, "audit.jsonl");
        AuditLog.Append(path, new AuditEntry(DateTime.UtcNow, AuditAction.Drop, "PROD01", "Sales", "op", "detail"));
        var vm = new AuditViewModel(new AppPaths(_dir, _dir, _dir));
        await vm.LoadAsync(CancellationToken.None);
        Assert.Single(vm.Entries);
    }

    [Fact]
    public async Task Filter_narrows_entries()
    {
        var path = Path.Combine(_dir, "audit.jsonl");
        AuditLog.Append(path, new AuditEntry(DateTime.UtcNow, AuditAction.Drop, "PROD01", "Sales", "op", "dropped IX_A"));
        AuditLog.Append(path, new AuditEntry(DateTime.UtcNow, AuditAction.Restore, "PROD01", "HR", "op", "restored IX_B"));
        var vm = new AuditViewModel(new AppPaths(_dir, _dir, _dir));
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(2, vm.Entries.Count);

        vm.Filter = "HR";
        Assert.Single(vm.Entries);
        Assert.Equal("HR", vm.Entries[0].Database);

        vm.Filter = "";
        Assert.Equal(2, vm.Entries.Count);
    }
}
