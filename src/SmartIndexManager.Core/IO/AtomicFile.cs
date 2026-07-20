namespace SmartIndexManager.Core.IO;

internal static class AtomicFile
{
    internal static void WriteAllText(string path, string contents)
    {
        EnsureDirectory(path);
        var temp = TempPathInSameDirectory(path);
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    internal static bool TryWriteAllText(string path, string contents)
    {
        EnsureDirectory(path);
        var temp = TempPathInSameDirectory(path);
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            TryDelete(temp);
            return false;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private static string TempPathInSameDirectory(string finalPath)
    {
        var dir = Path.GetDirectoryName(finalPath) ?? "";
        return Path.Combine(dir, $".tmp-{Path.GetRandomFileName()}");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
