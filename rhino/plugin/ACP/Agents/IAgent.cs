using System.Threading;
using System.Threading.Tasks;

namespace RhMcp;

internal interface IAgent : IDisposable
{
    /// <summary>Name </summary>
    public string Name { get; }

    /// <summary>Prompts the agent</summary>
    public Task PromptAsync(string request, string mcpUrl, string cwd);

    /// <summary>Cancels any currently running actions</summary>
    public void Cancel();

}
