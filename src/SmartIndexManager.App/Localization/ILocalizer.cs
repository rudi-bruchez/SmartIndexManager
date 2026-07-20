namespace SmartIndexManager.App.Localization;

public interface ILocalizer
{
    string this[string key] { get; }
    string Get(string key);
}
