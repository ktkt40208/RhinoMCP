using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Rhino.FileIO;

namespace RhMcp;

public static class RhinoMcpHost
{

    private static Dictionary<uint, McpServer> Servers { get; } = new();

    // UI-thread-only!
    private static Timer? _heartbeat;

    // Re-advertise live listeners on this interval. Lets a spuriously-reaped slot 
    // re-adopt on its own instead of staying gone until the user re-runs MCPStart. 
    // Re-dropping a already-adopted listener is a no-op.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    static RhinoMcpHost()
    {
        RhinoDoc.CloseDocument += CloseServer;
    }

    // A doc that owned a listener is closing (File>New/Open, or a plain close). 
    // Stop the server, otherwise the listener is orphaned.
    private static void CloseServer(object? sender, DocumentEventArgs e)
    {
        if (!Servers.Remove(e.DocumentSerialNumber, out McpServer? server))
            return;
        server?.Stop();
        StopHeartbeatIfIdle();
    }

    public static bool HasStarted(RhinoDoc doc) =>
        Servers.TryGetValue(doc.RuntimeSerialNumber, out McpServer? server)
            && (server?.HasStarted ?? false);

    private const int DefaultPort = 10500;
    public static int GetNextPort()
    {
        int nextPort = Servers.Any() ? Servers.Max(s => s.Value.Port) + 1 : DefaultPort;

        try
        {
            System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, nextPort);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return -1;
        }
    }

    public static bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted(doc))
            return true;
        McpServer server = new();
        Servers[doc.RuntimeSerialNumber] = server;

        var ok = server.Start(doc, port);
        if (ok)
        {
            WriteAnnouncement(port);
            EnsureHeartbeat();
        }
        return ok;
    }

    public static void Stop(RhinoDoc doc)
    {
        if (!Servers.Remove(doc.RuntimeSerialNumber, out McpServer? server))
            return;
        server?.Stop();
        StopHeartbeatIfIdle();
    }

    public static bool RestartOnPort(RhinoDoc doc, int port)
    {
        if (port < 1 || port > 65535)
            return false;
        Stop(doc);
        return Start(doc, port);
    }

    // Shared dispatch for both the interactive `MCPStart` command and the
    // hidden `MCPSpawn` autostart path. Writes user-facing status lines.
    public static bool StartOrRestart(RhinoDoc doc, int port, bool quiet = false)
    {
        if (HasStarted(doc))
        {
            if (!RestartOnPort(doc, port))
            {
                if (!quiet)
                {
                    RhinoApp.WriteLine($"[Rhino MCP] Failed to bind port {port}.");
                }
                return false;
            }
            if (!quiet)
            {
                RhinoApp.WriteLine($"[Rhino MCP] Restarted on http://localhost:{port}/");
            }
            return true;
        }

        if (Start(doc, port))
            return true;

        if (!quiet)
        {
            RhinoApp.WriteLine($"[Rhino MCP] MCP server failed to start. Try a different port.");
        }
        return false;
    }


    private static void EnsureHeartbeat()
    {
        _heartbeat ??= new Timer(static _ => Heartbeat(), null, HeartbeatInterval, HeartbeatInterval);
    }

    private static void StopHeartbeatIfIdle()
    {
        if (Servers.Count == 0)
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
        }
    }

    private static void Heartbeat()
    {
        // Heartbeat must be touched from UI thread
        RhinoApp.InvokeOnUiThread(new Action(static () =>
        {
            foreach (McpServer server in Servers.Values)
            {
                if (server.HasStarted)
                    WriteAnnouncement(server.Port);
            }
        }), null);
    }

    // Drop a one-shot announcement into <temp>/rhino-mcp-listeners/ so a router
    // running on this machine can discover and adopt this listener without us
    // having to know whether one is up. The router consumes (probes + deletes)
    // the file on its next scan; if no router ever sees it, temp sweep collects
    // it eventually. See router's RhinoManager.ScanAnnouncements.
    private static void WriteAnnouncement(int port)
    {
        try
        {
            var dir = ListenerDropDir();
            Directory.CreateDirectory(dir);
            var pid = Process.GetCurrentProcess().Id;
            var version = RhinoApp.Version.Major.ToString();
            var path = Path.Combine(dir, $"{pid}-{port}.json");
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(new { v = 1, pid, port, version });
            File.WriteAllText(tmp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to write listener announcement: {ex.Message}");
        }
    }

    // MUST match the router's RouterPaths.ListenersDir, or the router never sees
    // our announcement. Fixed per-user dir, not GetTempPath() (drifts with $TMPDIR).
    private static string ListenerDropDir()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("RHINO_MCP_HOME");
        string root = string.IsNullOrEmpty(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McNeel")
            : overrideRoot;
        return Path.Combine(root, "rhino-mcp", "listeners");
    }

    // Stop the listener bound to the given port and close its associated doc
    // without keeping any save artefacts. Used by the router's control channel
    // on Mac to tear down a single slot without affecting other slots sharing
    // the same Rhino process. The router only calls this for slots it spawned
    // (adopted slots are refused upstream), so discarding the doc is safe.
    //
    // Mac's `_-Close` command matches docs by their on-disk path (see
    // src4/rhino4/commands/cmdFileIO.cpp) and is the only way to programmatically
    // close a non-headless doc — RhinoDoc.Dispose is a no-op for them. We give
    // the doc a temp path via WriteFile so the command can find it, then delete
    // that file once Cocoa's deferred close has run.
    public static bool StopByPort(int port)
    {
        KeyValuePair<uint, McpServer> entry = Servers.FirstOrDefault(kv => kv.Value.Port == port);
        if (entry.Value is null)
            return false;

        Servers.Remove(entry.Key);
        var docSerial = entry.Key;
        entry.Value.Stop();
        StopHeartbeatIfIdle();

        var doc = RhinoDoc.FromRuntimeSerialNumber(docSerial);
        if (doc is null)
            return true;

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"rh-mcp-slot-close-{docSerial}-{Guid.NewGuid():N}.3dm");
        try
        {
            doc.Modified = false;
            doc.WriteFile(tempPath, new FileWriteOptions
            {
                SuppressDialogBoxes = true,
                WriteUserData = true,
                UpdateDocumentPath = true,
            });
            RhinoApp.RunScript(docSerial, $"_-Close \"{tempPath}\"", false);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Slot doc close failed for port {port}: {ex.Message}");
            return true;
        }

        // Mac defers the doc close via Cocoa performSelector:afterDelay:0.1.
        // Wait past that, then delete the temp file. Fire-and-forget — we
        // don't want to block the router's HTTP response.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            try
            { File.Delete(tempPath); }
            catch { /* OS temp sweep will get it */ }
        });

        return true;
    }
}
