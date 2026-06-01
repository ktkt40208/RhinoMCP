using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using RhMcp.Router;
using Xunit;

namespace RhMcp.Router.Tests;

// Locks in the single-source-of-truth seam both spawn callers (SpawnSlotTool and
// ProxyDispatcher) now share. If the spawn-pipeline exception->code map drifts,
// these break — they previously couldn't, because each caller had its own copy.
public class SpawnDiagnosticsTests
{
    private static RhinoCrashReportFinder Finder => new(NullLogger<RhinoCrashReportFinder>.Instance);

    [Fact]
    public void FileNotFound_classifies_as_rhino_not_installed()
    {
        bool ok = SpawnDiagnostics.TryClassify(
            new FileNotFoundException("Rhino 9 is not installed."), Finder, out SpawnDiagnostics.SpawnDiagnosis d);

        Assert.True(ok);
        Assert.Equal("rhino_not_installed", d.Code);
        // Base message carries the diagnosis; the next-action suffix is the caller's.
        Assert.Equal("Rhino 9 is not installed.", d.BaseMessage);
    }

    [Fact]
    public void Timeout_classifies_as_startup_timeout_with_shared_advice()
    {
        bool ok = SpawnDiagnostics.TryClassify(
            new TimeoutException("Rhino didn't start in time."), Finder, out SpawnDiagnostics.SpawnDiagnosis d);

        Assert.True(ok);
        Assert.Equal("startup_timeout", d.Code);
        Assert.Contains("license, EULA, or update dialog", d.BaseMessage);
    }

    [Fact]
    public void PlatformNotSupported_classifies_as_unsupported_platform()
    {
        bool ok = SpawnDiagnostics.TryClassify(
            new PlatformNotSupportedException("WIP not on this OS."), Finder, out SpawnDiagnostics.SpawnDiagnosis d);

        Assert.True(ok);
        Assert.Equal("unsupported_platform", d.Code);
        Assert.Equal("WIP not on this OS.", d.BaseMessage);
    }

    [Fact]
    public void Connection_level_http_failure_classifies_as_existing_rhino_unreachable()
    {
        HttpRequestException hre = new(
            HttpRequestError.ConnectionError, "connection refused", inner: new SocketException());

        bool ok = SpawnDiagnostics.TryClassify(hre, Finder, out SpawnDiagnostics.SpawnDiagnosis d);

        Assert.True(ok);
        Assert.Equal("existing_rhino_unreachable", d.Code);
        Assert.Contains("stale slot has been pruned", d.BaseMessage);
    }

    [Fact]
    public void Non_connection_http_failure_is_not_a_shared_shape()
    {
        // HTTP 5xx from the plugin: Rhino is alive, the request failed. Each caller
        // handles this itself, so the shared classifier must decline it.
        HttpRequestException hre = new("Child Rhino returned HTTP 500");

        bool ok = SpawnDiagnostics.TryClassify(hre, Finder, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(Exception))]
    public void Caller_specific_shapes_are_declined(Type exceptionType)
    {
        Exception ex = (Exception)Activator.CreateInstance(exceptionType)!;

        bool ok = SpawnDiagnostics.TryClassify(ex, Finder, out _);

        Assert.False(ok);
    }
}
