using System.IO;
using System.Threading.Tasks;

namespace RhMcp;

// Routes command-line entries that start with " to Claude.
//
// Rhino rejects " as a command name and exposes no pre-parse hook, so rather than intercept
// keystrokes (which fire for every text field and misfire) we read the command history *after*
// the parser handles the entry. A leading-" entry shows up verbatim as a "Command: "..." line,
// which only happens for real command-line submissions — never from other text inputs — and
// preserves spaces exactly.
internal static class CommandInterceptor
{
    const char Sigil = '"';
    const string RoutedMarker = "[claude]";

    // Rhino echoes a submitted command-line entry as `Command: <text>`. Anchoring on
    // this prefix is what stops our own output (echoes, agent text, error JSON — any of
    // which can contain a ") from being re-captured and routed in a loop.
    const string CommandPrefix = "Command: ";

    // How far into the command history we've already scanned.
    static int HistoryCursor { get; set; }

    // Whether the idle watcher is currently subscribed.
    public static bool Attached { get; private set; }

    static string History => RhinoApp.CommandHistoryWindowText ?? string.Empty;

    public static void Attach()
    {
        if (Attached)
            return;
        HistoryCursor = History.Length;
        RhinoApp.Idle += OnIdle;
        Attached = true;
    }

    public static void Detach()
    {
        if (!Attached)
            return;
        RhinoApp.Idle -= OnIdle;
        Attached = false;
    }

    static void OnIdle(object? sender, EventArgs e)
    {
        string history = History;
        if (history.Length < HistoryCursor)   // history was cleared out from under us
        {
            HistoryCursor = history.Length;
            return;
        }
        if (history.Length == HistoryCursor)
            return;

        // Advance the cursor past the new text before routing, so our own echoed output lands
        // in the next scan instead of re-triggering this one.
        string fresh = history.Substring(HistoryCursor);
        HistoryCursor = history.Length;

        foreach (string line in fresh.Split('\n'))
        {
            string? request = ExtractRequest(line);
            if (request != null)
                Route(request);
        }
    }

    // The request text from a submitted `Command: "..."` line, or null if the line
    // isn't a sigil-led command submission (so plugin/agent output is never routed).
    static string? ExtractRequest(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith(CommandPrefix, StringComparison.Ordinal))
            return null;
        string entry = trimmed.Substring(CommandPrefix.Length).TrimStart();
        if (entry.Length == 0 || entry[0] != Sigil)
            return null;
        string request = entry.Substring(1).Trim();
        return request.Length > 0 ? request : null;
    }

    // Hands a captured request to the ACP agent for the active document.
    public static void Route(string request)
    {
        RhinoApp.WriteLine($"{RoutedMarker} {request}");

        RhinoDoc? doc = RhinoDoc.ActiveDoc;
        int? port = doc is null ? null : RhinoMcpHost.PortFor(doc);
        if (port is null)
        {
            RhinoApp.WriteLine($"{RoutedMarker} no MCP server is running for this document.");
            return;
        }

        string url = $"http://localhost:{port.Value}/";
        string cwd = !string.IsNullOrEmpty(doc!.Path)
            ? Path.GetDirectoryName(doc.Path) ?? Path.GetTempPath()
            : Path.GetTempPath();

        _ = AcpAgent.Instance.PromptAsync(request, url, cwd).ContinueWith(
            t => RhinoApp.WriteLine($"{RoutedMarker} error: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
