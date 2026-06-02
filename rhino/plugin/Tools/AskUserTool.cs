using System.Threading;
using System.Threading.Tasks;

namespace RhMcp.Tools;

[McpServerToolType]
public static class AskUserTool
{
    [McpServerTool("ask_user", "Ask User", true, false)]
    [BackgroundThread]
    [Description("Ask the Rhino user to choose among options when you need a decision you cannot "
        + "make yourself. The question and options appear both inline in the Rhino MCP panel "
        + "(radio for single choice, checkboxes for multi) and on the command line. The user "
        + "answers in either place — clicking the panel, or typing the answer on the command line "
        + "as a \"...\" entry (an option label, its number, or free-form text for \"Other\"). "
        + "Returns { selected: string[], cancelled: bool }. For follow-up questions call this again "
        + "(later questions may depend on earlier answers).")]
    public static async Task<object> AskUser(
        RhinoDoc doc,
        [Description("The question to show the user")] string question,
        [Description("The options to choose from")] string[] options,
        [Description("true = user may select multiple options (checkboxes); "
            + "false = single choice (radio). Default false.")] bool multiSelect = false,
        CancellationToken ct = default)
    {
        options ??= [];
        if (options.Length == 0)
            return "ask_user requires at least one option.";

        AskUserMode mode = multiSelect ? AskUserMode.Multi : AskUserMode.Single;
        PendingQuestion pending = new(question, options, mode);

        // The constructor collapses a literal "Other", so an options:["Other"] array survives the
        // raw length guard above yet leaves no real options. Reject it the same way as an empty array.
        if (pending.Options.Count == 0)
            return "ask_user requires at least one option.";

        uint docSerial = doc.RuntimeSerialNumber;
        AskUserRegistry.Register(docSerial, pending);

        // AgentHost's dictionaries are UI-thread-owned and unsynchronized; this tool body runs
        // off the UI thread ([BackgroundThread]). Resolve the Conversation on the UI thread so the
        // TryFor read can't race a user-driven New/SetActive mutation. Everything after this point
        // (AskUserRegistry, PendingQuestion, RhinoApp.WriteLine, the await) is thread-safe.
        ConversationLookup lookup = await ResolveConversationAsync(doc).ConfigureAwait(false);
        if (lookup.Attached)
            lookup.Conversation.SetPendingQuestion(pending);

        PrintPrompt(pending);

        // An aborted/killed MCP request would otherwise leave the await hanging forever, since
        // q.Task does not observe ctx.RequestAborted on its own.
        using CancellationTokenRegistration _ = ct.Register(() => pending.TryCancel());

        try
        {
            AskUserAnswer answer = await pending.Task.ConfigureAwait(false);
            return new { selected = answer.Selected, cancelled = answer.Cancelled };
        }
        finally
        {
            AskUserRegistry.Clear(docSerial, pending);
            if (lookup.Attached)
                lookup.Conversation.ClearPendingQuestion(pending);
        }
    }

    private readonly record struct ConversationLookup(bool Attached, Conversation Conversation);

    private static Task<ConversationLookup> ResolveConversationAsync(RhinoDoc doc)
    {
        TaskCompletionSource<ConversationLookup> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            try { tcs.SetResult(ResolveConversation(doc)); }
            catch (Exception ex) { tcs.SetException(ex); }
        }), null);
        return tcs.Task;
    }

    // UI-thread only: reads AgentHost's unsynchronized dictionaries.
    private static ConversationLookup ResolveConversation(RhinoDoc doc) =>
        AgentHost.TryFor(doc, out IAgentRunner agent)
            ? new ConversationLookup(true, agent.Conversation)
            : new ConversationLookup(false, default!);

    private static void PrintPrompt(PendingQuestion pending)
    {
        RhinoApp.WriteLine($"[ask] {pending.Question}");
        for (int i = 0; i < pending.Options.Count; i++)
            RhinoApp.WriteLine($"  {i + 1}. {pending.Options[i]}");

        string hint = pending.Mode == AskUserMode.Multi
            ? "Answer with \"<numbers or labels, comma-separated>\" (or \"cancel\")."
            : "Answer with \"<number or label>\" (or \"cancel\").";
        RhinoApp.WriteLine($"  {hint}");
    }
}
