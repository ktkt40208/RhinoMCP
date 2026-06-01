using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using RhMcp.Router;
using RhMcp.Router.Tools;
using Xunit;

namespace RhMcp.Router.Tests;

// Guards the spawn_slot-specific exception arms that SpawnDiagnostics deliberately
// declines. The status-code HttpRequestException case is a regression guard: the Mac
// listener fan-out (RhinoControlClient.SpawnListenerAsync) can throw a non-2xx
// HttpRequestException that IsConnectionFailure rejects, so SpawnDiagnostics returns
// false and this local arm must still classify it as existing_rhino_unreachable rather
// than dropping it into the generic `unexpected` bucket.
public sealed class SpawnSlotToolDiagnoseTests
{
    private static RhinoCrashReportFinder Finder => new(NullLogger<RhinoCrashReportFinder>.Instance);

    [Fact]
    public void Status_code_http_failure_classifies_as_existing_rhino_unreachable()
    {
        // Non-connection HTTP failure: a 5xx from the control endpoint during the
        // Mac fan-out. IsConnectionFailure is false, so SpawnDiagnostics declines it.
        HttpRequestException hre = new("Control call _router_spawn_listener returned HTTP 500: boom");
        Assert.False(SpawnDiagnostics.TryClassify(hre, Finder, out _));

        ErrorInfo error = SpawnSlotTool.Diagnose(hre, Finder);

        Assert.Equal("existing_rhino_unreachable", error.Code);
        Assert.Contains("Call spawn_slot again", error.Message);
        Assert.Contains("HTTP 500", error.Message);
    }

    [Fact]
    public void Connection_level_http_failure_keeps_the_shared_classification()
    {
        // The connection-level shape is owned by SpawnDiagnostics; the local arm must
        // not shadow it. Same code, but the shared base message (not the status-code one).
        HttpRequestException hre = new(
            HttpRequestError.ConnectionError, "connection refused", inner: new SocketException());

        ErrorInfo error = SpawnSlotTool.Diagnose(hre, Finder);

        Assert.Equal("existing_rhino_unreachable", error.Code);
        Assert.Contains("stale slot has been pruned", error.Message);
        Assert.Contains("Call spawn_slot again", error.Message);
    }

    [Fact]
    public void Cancellation_classifies_as_cancelled()
    {
        ErrorInfo error = SpawnSlotTool.Diagnose(new OperationCanceledException(), Finder);

        Assert.Equal("cancelled", error.Code);
    }

    [Fact]
    public void Unknown_exception_falls_through_to_unexpected()
    {
        ErrorInfo error = SpawnSlotTool.Diagnose(new ArgumentNullException("arg"), Finder);

        Assert.Equal("unexpected", error.Code);
    }
}
