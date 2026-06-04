using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RhMcp.Server;

namespace RhMcp;

internal sealed class McpServer : IDisposable
{
    // Needs to be volatile so main thread sees the write from ContinueWith, which runs on a background thread.
    private volatile WebApplication? _app;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public bool HasStarted => _app is not null;

    public int Port { get; private set; }

    public bool Start(RhinoDoc doc, int port)
    {
        if (HasStarted) return true;
        Port = port;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new RhinoLoggerProvider());
#if DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Information);
#else
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif
            builder.Services.Configure<KestrelServerOptions>(o => o.ListenLocalhost(port));

            builder.Services.AddSingleton(doc);

            var asm = typeof(McpServer).Assembly;

            _app = builder.Build();

            var endpointOptions = new McpEndpointOptions
            {
                ServerName = "rhino-mcp",
                ServerVersion = "0.1.3",
                ToolAssembly = asm,
#if DEBUG
                SurfaceExceptionDetailsToClient = true,
#endif
            };
            _app.MapMcp("/", endpointOptions);

            _cts = new CancellationTokenSource();
            _runTask = _app.RunAsync(_cts.Token);

            // Observe the host task. If we error after a clean start, or are stopped,
            // set back to null so that `HasStarted` is false and next StartOrRestart works.
            _ = _runTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    RhinoApp.WriteLine(
                        $"[Rhino MCP] MCP server on port {port} stopped unexpectedly: {DescribeException(t.Exception!.GetBaseException())}");
                }
                _app = null;
            }, TaskScheduler.Default);

            RhinoApp.WriteLine($"[Rhino MCP] MCP server currently running on http://localhost:{port}/");
            return true;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to start: {DescribeException(ex)}");
            _app = null;
            return false;
        }
    }

    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            parts.Add($"{cur.GetType().FullName}: {cur.Message}");
        return string.Join(" --> ", parts);
    }

    public void Stop()
    {
        WebApplication? app = _app;
        _app = null;
        try { _cts?.Cancel(); } catch { }
        if (app is not null)
        {
            // Await graceful shutdown so we release the listening socket BEFORE
            // any attempted rebind on the same port.
            try { app.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); } catch
            {
                RhinoApp.WriteLine($"[Rhino MCP] Failed to stop MCP server gracefully. Recommend restarting Rhino.");
            }
        }
        try { _cts?.Dispose(); } catch
        {
            RhinoApp.WriteLine($"[Rhino MCP] Failed to dispose CancellationTokenSource");
        }
        _cts = null;
        _runTask = null;
    }

    public void Dispose() => Stop();
}
