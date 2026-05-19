using System.Runtime.InteropServices;

namespace RhMcp.Router;

// Resolves a full path to Rhino.exe (Windows) or the Rhinoceros binary (macOS)
// for a given version string. Versions: "8" | "9" | "WIP".
public class RhinoLocator
{
    public string ResolveRhinoExe(string version)
    {
        if (TryResolve(version, out string path))
            return path;

        throw new FileNotFoundException(
            $"Could not locate Rhino executable for version '{version}'. " +
            $"Installed versions found: {string.Join(", ", ListInstalledVersions())}");
    }

    private bool TryResolve(string version, out string path)
    {
        path = string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Rhino 9 currently ships only as WIP, so "9" falls back to the
            // WIP install dir when no released Rhino 9 is present. Once a
            // real Rhino 9 ships, the first lookup will hit and the fallback
            // becomes dead code we can drop.
            // TODO: also try registry-based lookup if Program Files paths miss.
            string[] dirs = version switch
            {
                "8" => new[] { @"C:\Program Files\Rhino 8" },
                "9" => new[] { @"C:\Program Files\Rhino 9", @"C:\Program Files\Rhino 9 WIP" },
                "WIP" => new[] { @"C:\Program Files\Rhino 9 WIP" },
                _ => Array.Empty<string>()
            };
            foreach (string dir in dirs)
            {
                string candidate = Path.Combine(dir, "System", "Rhino.exe");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // See Windows branch — "9" falls back to RhinoWIP.app while Rhino 9
            // is still shipping as WIP.
            string[] appNames = version switch
            {
                "8" => new[] { "Rhino 8.app" },
                "9" => new[] { "Rhino 9.app", "RhinoWIP.app" },
                "WIP" => new[] { "RhinoWIP.app" },
                _ => Array.Empty<string>()
            };
            // Without this guard an unknown version resolves to "/Applications/",
            // which Directory.Exists trivially confirms — spawn then attempts
            // `open -a /Applications/` and we burn the full 60s startup timeout
            // before reporting failure. Fail fast with rhino_not_installed instead.
            foreach (string appName in appNames)
            {
                string candidate = $"/Applications/{appName}";
                if (Directory.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    public IEnumerable<string> ListInstalledVersions()
    {
        foreach (string v in new[] { "8", "9", "WIP" })
        {
            if (TryResolve(v, out _))
                yield return v;
        }
    }
}
