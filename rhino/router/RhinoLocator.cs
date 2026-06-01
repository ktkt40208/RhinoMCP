using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros binary (macOS)
// for a given version string. Versions: "8" | "9" | "WIP".
public static class RhinoLocator
{
    public static string ResolveRhinoExe(string version)
    {
        if (TryResolve(version, out string path))
            return path;

        throw new FileNotFoundException(
            $"Could not locate Rhino executable for version '{version}'. " +
            $"Installed versions found: {string.Join(", ", ListInstalledVersions())}");
    }

    private static bool TryResolve(string version, out string path)
    {
        path = string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IEnumerable<string> directories = Directory.EnumerateDirectories(@$"C:\Program Files\", "Rhino*");
            foreach(string dir in MatchVersionDirectories(directories, version))
            {
                string candidate = Path.Combine(dir, "System", "Rhino.exe");
                // It's unlikely, but not impossible!
                if (!File.Exists(candidate)) continue;
                path = candidate;
                return true;
            }

            if (string.Equals(version, "9", StringComparison.OrdinalIgnoreCase))
            {
                path = @$"C:\Program Files\Rhino 9 WIP\System\Rhino.exe";
                return File.Exists(path);
            }

            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            IEnumerable<string> directories = Directory.EnumerateDirectories(@$"/Applications/", "Rhino*");
            foreach(string rhinoDir in MatchVersionDirectories(directories, version))
            {
                path = rhinoDir;
                return true;
            }

            if (string.Equals(version, "9", StringComparison.OrdinalIgnoreCase))
            {
                path = $"/Applications/RhinoWIP.app";
                return Directory.Exists(path);
            }

            return false;
        }

        return false;
    }

    // Filters install directories down to those matching the requested version.
    // Kept as a pure, internal seam so the version-matching logic (the source of
    // a past inverted-match bug that picked a sibling version) is unit-testable
    // without a real Rhino install on disk.
    internal static IEnumerable<string> MatchVersionDirectories(IEnumerable<string> directories, string version)
    {
        foreach (string dir in directories)
        {
            if (dir.Contains(version, StringComparison.OrdinalIgnoreCase))
                yield return dir;
        }
    }

    public static IEnumerable<string> ListInstalledVersions()
    {
        foreach (string v in new[] { "8", "9", "10", "11", "12", "WIP" })
        {
            if (TryResolve(v, out _))
                yield return v;
        }
    }
}
