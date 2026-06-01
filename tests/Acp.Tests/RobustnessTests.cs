using System.Text.Json;
using Acp;

namespace Acp.Tests;

[TestFixture]
public sealed class RobustnessTests
{
    [Test]
    [CancelAfter(10000)]
    public async Task Malformed_lines_are_skipped_and_the_read_loop_survives()
    {
        (IAcpTransport a, IAcpTransport b) = InMemoryTransport.CreatePair();
        FakeAgent agent = new();
        AgentSideConnection agentConn = new(agent, b);
        agent.Client = agentConn;
        agentConn.Start();

        // `a` is raw here (no client connection) so we can inject hostile input directly.
        await a.WriteLineAsync("this is not json");
        await a.WriteLineAsync("");
        await a.WriteLineAsync("[1,2,3]"); // valid JSON, but not an object

        JsonRpcRequest request = new()
        {
            Id = RequestId.Of(1),
            Method = AgentMethods.Initialize,
            Params = JsonSerializer.SerializeToElement(new InitializeRequest { ProtocolVersion = 1 }, AcpJson.Options),
        };
        await a.WriteLineAsync(JsonSerializer.Serialize(request, AcpJson.Options));

        string? line = await a.ReadLineAsync();
        Assert.That(line, Is.Not.Null);
        JsonRpcResponse response = JsonSerializer.Deserialize<JsonRpcResponse>(line!, AcpJson.Options)!;
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Id, Is.EqualTo(RequestId.Of(1)));
    }
}
