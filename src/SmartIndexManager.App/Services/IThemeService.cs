namespace SmartIndexManager.App.Services;

public enum ThemeVariantKind { Light, Dark }

public interface IThemeService
{
    ThemeVariantKind Current { get; }
    void Toggle();
}
