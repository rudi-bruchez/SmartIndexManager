namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

// Skip the whole integration suite when Docker is not reachable.
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerAvailable.Value) Skip = "Docker is not available; skipping integration tests.";
    }
}

public static class DockerAvailable
{
    public static readonly bool Value = Probe();

    private static bool Probe()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker", Arguments = "info", RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false
            });
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch { return false; }
    }
}
