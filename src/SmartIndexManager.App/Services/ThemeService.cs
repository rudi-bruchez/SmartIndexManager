namespace SmartIndexManager.App.Services;

public sealed class ThemeService : IThemeService
{
    private readonly string _path;
    public ThemeVariantKind Current { get; private set; }

    public ThemeService(IAppPaths paths)
    {
        _path = Path.Combine(paths.ConfigDir, "theme.txt");
        Current = File.Exists(_path) && File.ReadAllText(_path).Trim() == nameof(ThemeVariantKind.Dark)
            ? ThemeVariantKind.Dark : ThemeVariantKind.Light;
    }

    public void Toggle()
    {
        Current = Current == ThemeVariantKind.Light ? ThemeVariantKind.Dark : ThemeVariantKind.Light;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, Current.ToString());
    }
}
