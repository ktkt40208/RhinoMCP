using Microsoft.Extensions.AI;
using Ngentic;
using Ngentic.NUnit;
using NUnit.Framework;

namespace RhMcp.Integration.Tests;

/// <summary>
/// Smoke test proving the Ngentic submodule is wired correctly. Replace with a real
/// IChatClient + ModelContextProtocol client in ConfigureHarness once the rhino MCP
/// transport adapter is in place.
/// </summary>
[TestFixture]
[McpDependency("rhino")]
public sealed class SmokeTest : AgenticTestBase
{
    protected override void ConfigureHarness()
    {
        InMemoryMcpRegistry registry = new InMemoryMcpRegistry();
        registry.Register("rhino", _ => Task.FromResult<IList<AITool>>(new List<AITool>()));
        UseRegistry(registry);

        // Real wiring goes here: an IChatClient pointed at Anthropic, with the
        // rhino MCP client's tools adapted into AITool instances above.
        Assert.Ignore("Integration harness is wired; awaiting a real IChatClient + MCP transport.");
    }

    [Test]
    public Task Placeholder()
    {
        return Task.CompletedTask;
    }
}
