using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.Tests.Services;

public class ThemeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-theme-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ThemeService Service() => new(new AppPaths(_dir, _dir, _dir));

    [Fact]
    public void Toggle_flips_and_persists_the_variant()
    {
        var s = Service();
        var first = s.Current;
        s.Toggle();
        Assert.NotEqual(first, s.Current);
        Assert.Equal(s.Current, Service().Current);   // reloaded from disk
    }
}
