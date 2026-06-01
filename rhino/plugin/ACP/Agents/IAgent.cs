using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

internal interface IAgent : IDisposable
{
    /// <summary>Name </summary>
    public string Name { get; }

    /// <summary>Prompts the agent with a user message (text + inline attachments).</summary>
    public Task PromptAsync(UserMessage message, string mcpUrl, string cwd);

    /// <summary>Cancels any currently running actions</summary>
    public void Cancel();

    /// <summary>The live transcript for this agent, fed by its event stream.</summary>
    public Conversation Conversation { get; }
}
