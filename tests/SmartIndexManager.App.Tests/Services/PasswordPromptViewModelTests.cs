using SmartIndexManager.App.Services;
using Xunit;

namespace SmartIndexManager.App.Tests.Services;

public class PasswordPromptViewModelTests
{
    [Fact]
    public async Task Connect_returns_password()
    {
        var vm = new PasswordPromptViewModel("prod");
        vm.Password = "s3cret";
        vm.ConnectCommand.Execute(null);
        Assert.Equal("s3cret", await vm.Result.Task);
    }

    [Fact]
    public async Task Cancel_returns_null()
    {
        var vm = new PasswordPromptViewModel("prod");
        vm.CancelCommand.Execute(null);
        Assert.Null(await vm.Result.Task);
    }
}
