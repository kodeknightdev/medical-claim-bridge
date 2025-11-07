using System.Diagnostics;
using Xunit;

namespace ClaimBridge.Testing;

/// <summary>Testcontainers tests skip themselves (instead of failing) on machines without a Docker daemon.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable) Skip = "Docker is not available; Testcontainers integration test skipped.";
    }
}

public static class DockerAvailability
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        if (Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS") == "1") return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;
}
