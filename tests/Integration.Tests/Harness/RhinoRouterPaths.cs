using System.Runtime.InteropServices;

namespace RhMcp.Integration.Tests.Harness;

internal static class RhinoRouterPaths
{
    public static string ResolveBinary()
    {
        string repoRoot = FindRepoRoot();
        string rid = CurrentRid();
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rhino-mcp-router.exe" : "rhino-mcp-router";
        string path = Path.Combine(repoRoot, "rhino", "plugin", "bin", "R9", "Release", "router", rid, exe);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Router binary not found at {path}. Build the plugin first: " +
                $"`dotnet build rhino/plugin/RhMcp.csproj -c Release -p:RhinoTarget=R9`.",
                path);
        }
        return path;
    }

    // The router reads `Path.GetTempPath()` to decide where state.db and the
    // listener-announcement dir live. Both POSIX and Windows honour an env-var
    // override (TMPDIR / TEMP+TMP), so pointing those at a unique dir gives us
    // a clean slot store and shields us from the user's live router.
    public static Dictionary<string, string?> IsolatedEnv(string tempDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Dictionary<string, string?> { ["TEMP"] = tempDir, ["TMP"] = tempDir };
        }
        return new Dictionary<string, string?> { ["TMPDIR"] = tempDir };
    }

    public static string CreateIsolatedTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rhmcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    private static string CurrentRid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }
        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "rhino", "rhino.slnx")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate repo root from the test binary's directory. " +
            "Expected to find a parent directory containing rhino/rhino.slnx.");
    }
}
