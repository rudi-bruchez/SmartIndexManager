namespace SmartIndexManager.App.Services;

public interface IConnectionStore
{
    IReadOnlyList<ConnectionProfile> Load();
    void Save(IReadOnlyList<ConnectionProfile> profiles);
}
