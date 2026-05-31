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

        uint docSerial = doc.RuntimeSerialNumber;
        AskUserRegistry.Register(docSerial, pending);

        bool attachedToConversation = TryConversation(doc, out Conversation conversation);
        if (attachedToConversation)
            conversation.SetPendingQuestion(pending);

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
            if (attachedToConversation)
                conversation.ClearPendingQuestion(pending);
        }
    }

    private static bool TryConversation(RhinoDoc doc, out Conversation conversation)
    {
        if (AgentHost.TryFor(doc, out IAgent agent) && agent is CliAgent cli)
        {
            conversation = cli.Conversation;
            return true;
        }
        conversation = default!;
        return false;
    }

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
