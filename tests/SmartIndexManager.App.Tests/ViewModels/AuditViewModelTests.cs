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
        var vm = new AuditViewModel(new AppPaths(_dir, _dir, _dir), new ResxLocalizer());
        await vm.LoadAsync(CancellationToken.None);
        Assert.Single(vm.Entries);
    }
}
