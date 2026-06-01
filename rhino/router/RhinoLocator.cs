using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros app bundle (macOS)
// for a given version token. Accepted tokens are exactly the keys of VersionMap
// below: "8" | "9" | "WIP". "9" and "WIP" are aliases that both resolve to the
// current WIP install (Rhino 9 ships only as a WIP at time of writing); they are
// kept distinct so callers can ask for "the next major" or "whatever WIP" by name.
public static class RhinoLocator
{
    // The single canonical version-token-to-install mapping, shared by both the
    // platform resolve branches and ListInstalledVersions so a token can never
    // mean one thing on disk and another in the advertised list. Each token lists
    // the install folder names to probe, in preference order: the Windows entry
    // is a subfolder of C:\Program Files, the macOS entry an app bundle under
    // /Applications.
    private sealed record VersionInstall(string WindowsFolder, string MacBundle);

    private static IReadOnlyDictionary<string, VersionInstall> VersionMap { get; } =
        new Dictionary<string, VersionInstall>(StringComparer.OrdinalIgnoreCase)
        {
            ["8"] = new VersionInstall("Rhino 8", "Rhino 8.app"),
            ["9"] = new VersionInstall("Rhino 9 WIP", "RhinoWIP.app"),
            ["WIP"] = new VersionInstall("Rhino 9 WIP", "RhinoWIP.app"),
        };

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

        if (!VersionMap.TryGetValue(version, out VersionInstall? install))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string candidate = Path.Combine(@"C:\Program Files", install.WindowsFolder, "System", "Rhino.exe");
            if (!File.Exists(candidate))
                return false;
            path = candidate;
            return true;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string candidate = Path.Combine("/Applications", install.MacBundle);
            if (!Directory.Exists(candidate))
                return false;
            path = candidate;
            return true;
        }

        return false;
    }

    // The version tokens this locator understands, in advertised order. Kept as a
    // pure, internal seam so the token set (the source of a past mismatch between
    // the documented tokens and what was actually advertised) is unit-testable
    // without a real Rhino install on disk.
    internal static IReadOnlyList<string> KnownVersionTokens => [.. VersionMap.Keys];

    public static IEnumerable<string> ListInstalledVersions()
    {
        foreach (string version in KnownVersionTokens)
        {
            if (TryResolve(version, out _))
                yield return version;
        }
    }
}
