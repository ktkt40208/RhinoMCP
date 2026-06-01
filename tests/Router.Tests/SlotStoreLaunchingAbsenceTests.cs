using Microsoft.Extensions.Logging.Abstractions;
using RhMcp.Router;
using Xunit;

namespace RhMcp.Router.Tests;

// Regression for the sentinel finding: a launching placeholder INSERT omits port/pid, and
// ReadRow used to map the DBNull columns to 0, conflating "not yet assigned" with a real 0
// and materialising a lying "http://localhost:0" endpoint. Port/Pid are now int?, so a
// launching row carries genuine absence and Endpoint refuses to address an unbound slot.
public sealed class SlotStoreLaunchingAbsenceTests : IDisposable
{
    private string HomeOverride { get; }
    private string? PreviousHome { get; }

    public SlotStoreLaunchingAbsenceTests()
    {
        PreviousHome = Environment.GetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar);
        HomeOverride = Path.Combine(Path.GetTempPath(), "rhmcp-slotstore-launch-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, HomeOverride);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RouterPaths.HomeOverrideEnvVar, PreviousHome);
        try { Directory.Delete(HomeOverride, recursive: true); }
        catch { /* best effort temp cleanup */ }
    }

    private SlotStore NewStore() => new(NullLogger<SlotStore>.Instance);

    [Fact]
    public void Launching_row_has_no_port_or_pid()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);

        ChildRhino? row = store.Get("alpha");
        Assert.NotNull(row);
        Assert.Null(row!.Port);
        Assert.Null(row.Pid);
        Assert.Equal(SlotStatus.Launching, row.Status);
    }

    [Fact]
    public void Launching_row_endpoint_is_not_addressable()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);

        ChildRhino row = store.Get("alpha")!;
        Assert.Throws<InvalidOperationException>(() => row.Endpoint);
    }

    [Fact]
    public void Ready_row_carries_port_pid_and_addressable_endpoint()
    {
        using SlotStore store = NewStore();
        store.Reserve("alpha", "8", routerPid: 1);
        store.MarkReady("alpha", port: 10500, pid: 4321);

        ChildRhino row = store.Get("alpha")!;
        Assert.Equal(10500, row.Port);
        Assert.Equal(4321, row.Pid);
        Assert.Equal("http://localhost:10500", row.Endpoint);
    }
}
