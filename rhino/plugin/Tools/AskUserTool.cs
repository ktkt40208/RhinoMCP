using System.Text;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;

namespace RhMcp.Tools;

[McpServerToolType]
public static class AskUserTool
{
    [McpServerTool("ask_user", "Ask User", true, false)]
    [Description("Ask the Rhino user to choose among options on the command line when you need a "
        + "decision you cannot make yourself. The user picks a listed option, or chooses \"Other\" "
        + "to type a free-form answer. Returns { selected: string[], cancelled: bool }. For "
        + "follow-up questions call this again (later questions may depend on earlier answers).")]
    public static object AskUser(
        [Description("The question to show the user")] string question,
        [Description("The options to choose from")] string[] options,
        [Description("true = user may select multiple options (checkboxes); "
            + "false = single choice (radio). Default false.")] bool multiSelect = false)
    {
        // A get nested inside another command's input loop is unsafe; tell the agent to retry.
        if (Command.InCommand())
            return "Rhino is mid-command (waiting for input). Retry ask_user once it finishes.";

        options ??= [];
        if (options.Length == 0)
            return "ask_user requires at least one option.";

        return multiSelect ? AskMany(question, options) : AskOne(question, options);
    }

    private static object AskOne(string question, string[] options)
    {
        GetString get = new();
        get.SetCommandPrompt(question);

        RhinoApp.WriteLine(question);
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase) { "Other" };
        Dictionary<int, string> byIndex = [];
        foreach (string label in options)
        {
            if (IsOther(label)) continue;   // collapse into the single free-text option below
            string token = ToToken(label, used);
            byIndex[get.AddOption(token)] = label;
            PrintMapping(token, label);
        }
        int otherIndex = get.AddOption("Other");

        GetResult res = get.Get();
        if (res == GetResult.String)
            return Result([get.StringResult()], false);
        if (res != GetResult.Option)
            return Result([], true);

        int index = get.Option().Index;
        if (index == otherIndex)
            return AskOther() is string typed ? Result([typed], false) : Result([], true);
        return Result([byIndex.TryGetValue(index, out string? chosen) ? chosen : ""], false);
    }

    private static object AskMany(string question, string[] options)
    {
        RhinoApp.WriteLine(question);
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase) { "Other", "Done" };
        List<(string Label, string Token, OptionToggle Toggle)> items = [];
        foreach (string label in options)
        {
            if (IsOther(label)) continue;   // collapse into the single free-text option below
            string token = ToToken(label, used);
            items.Add((label, token, new OptionToggle(false, "Off", "On")));
            PrintMapping(token, label);
        }
        List<string> custom = [];

        while (true)
        {
            GetOption go = new();
            go.SetCommandPrompt(question);
            foreach ((string _, string token, OptionToggle toggle) in items)
            {
                OptionToggle t = toggle;
                go.AddOptionToggle(token, ref t);
            }
            int otherIndex = go.AddOption("Other");
            int doneIndex = go.AddOption("Done");

            GetResult res = go.Get();
            if (res == GetResult.Cancel)
                return Result([], true);
            if (res != GetResult.Option)
                break;

            int index = go.Option().Index;
            if (index == doneIndex)
                break;
            if (index == otherIndex && AskOther() is string typed)
                custom.Add(typed);
        }

        List<string> selected = [];
        foreach ((string label, string _, OptionToggle toggle) in items)
            if (toggle.CurrentValue)
                selected.Add(label);
        selected.AddRange(custom);
        return Result(selected, false);
    }

    // Literal capture so a multi-word answer survives verbatim.
    private static string? AskOther()
    {
        GetString get = new();
        get.SetCommandPrompt("Type your answer");
        return get.GetLiteralString() == GetResult.String && !string.IsNullOrWhiteSpace(get.StringResult())
            ? get.StringResult().Trim()
            : null;
    }

    private static object Result(IReadOnlyList<string> selected, bool cancelled) =>
        new { selected, cancelled };

    private static bool IsOther(string? label) =>
        string.Equals(label?.Trim(), "Other", StringComparison.OrdinalIgnoreCase);

    private static void PrintMapping(string token, string label)
    {
        if (!string.Equals(token, label, StringComparison.Ordinal))
            RhinoApp.WriteLine($"  {token} = {label}");
    }

    // Rhino option names must be CamelCase letters/digits starting with a letter, and unique.
    private static string ToToken(string label, HashSet<string> used)
    {
        StringBuilder sb = new();
        bool newWord = true;
        foreach (char c in label)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(newWord ? char.ToUpperInvariant(c) : c);
                newWord = false;
            }
            else
            {
                newWord = true;
            }
        }

        string token = sb.Length > 0 && char.IsLetter(sb[0]) ? sb.ToString() : "Opt" + sb;
        string candidate = token;
        int n = 2;
        while (!used.Add(candidate))
            candidate = $"{token}{n++}";
        return candidate;
    }
}
