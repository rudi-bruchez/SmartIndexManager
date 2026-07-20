using System.Globalization;

namespace SmartIndexManager.App.Localization;

public sealed class ResxLocalizer : ILocalizer
{
    public string this[string key] => Get(key);

    // Missing keys return "[key]" so untranslated gaps are visible in the UI, never a crash.
    public string Get(string key)
        => Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";
}
