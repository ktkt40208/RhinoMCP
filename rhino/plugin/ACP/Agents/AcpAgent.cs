using System;
using System.Threading;
using System.Threading.Tasks;
using Acp;

namespace RhMcp;

// Drives any ACP agent (native like Gemini, or a future in-process translator) behind the plugin's
// IAgent seam. One ACP session per agent instance: the first prompt initializes the connection and
// opens a session pointed at this doc's rhino MCP server; later prompts reuse it. Streaming arrives
// out-of-band through RhinoAcpClient on the connection's read loop; the prompt response ends the turn.
internal sealed class AcpAgent : IAgent
{
    private AgentDefinition Definition { get; }
    private RhinoAcpClient Client { get; }
    private Func<IAcpClient, string, IAcpAgent> Connect { get; }
    private SemaphoreSlim TurnGate { get; } = new(1, 1);
    private object Gate { get; } = new();

    private IAcpAgent? Connection { get; set; }
    private string SessionId { get; set; } = string.Empty;
    private bool Started { get; set; }

    public AcpAgent(AgentDefinition def, string docTitle, Func<IAcpClient, string, IAcpAgent> connect)
    {
        Definition = def;
        Connect = connect;
        Conversation = new Conversation(Guid.NewGuid(), def.Name, docTitle);
        Client = new RhinoAcpClient(Conversation);
    }

    public string Name => Definition.Name;

    public Conversation Conversation { get; }

    public async Task PromptAsync(UserMessage message, string mcpUrl, string cwd)
    {
        await TurnGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(mcpUrl, cwd).ConfigureAwait(false);
            Conversation.BeginTurn(message.Text);
            await Connection!.SessionPromptAsync(new PromptRequest
            {
                SessionId = SessionId,
                Prompt = AcpMessageMapper.Prompt(message),
            }).ConfigureAwait(false);
        }
        finally
        {
            Conversation.CompleteTurn();
            PersistTurn();
            TurnGate.Release();
        }
    }

    public void Cancel()
    {
        IAcpAgent? connection;
        string sessionId;
        lock (Gate)
        {
            connection = Connection;
            sessionId = SessionId;
        }
        if (connection is not null)
            _ = connection.SessionCancelAsync(new CancelNotification { SessionId = sessionId });
    }

    public void Dispose()
    {
        IAcpAgent? connection;
        lock (Gate)
            connection = Connection;
        (connection as IDisposable)?.Dispose();
        // TurnGate is deliberately not disposed: a turn cancelled by this teardown still runs its
        // finally (TurnGate.Release), and disposing here would race it into ObjectDisposedException.
    }

    private async Task EnsureStartedAsync(string mcpUrl, string cwd)
    {
        if (Started)
            return;

        IAcpAgent connection = Connect(Client, cwd);
        try
        {
            await connection.InitializeAsync(new InitializeRequest { ProtocolVersion = ProtocolConstants.Version }).ConfigureAwait(false);
            NewSessionResponse session = await connection.SessionNewAsync(new NewSessionRequest
            {
                Cwd = cwd,
                McpServers = [new HttpMcpServer { Name = "rhino", Url = mcpUrl, Headers = [] }],
            }).ConfigureAwait(false);

            lock (Gate)
            {
                Connection = connection;
                SessionId = session.SessionId;
                Started = true;
            }
            Conversation.NoteSessionStarted();
        }
        catch
        {
            (connection as IDisposable)?.Dispose();
            throw;
        }
    }

    private void PersistTurn()
    {
        try
        {
            ConversationStore.Save(Conversation);
        }
        catch (Exception)
        {
            // A settings hiccup must never fault the turn the user just ran.
        }
    }
}
