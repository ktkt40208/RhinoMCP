using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RhMcp;

// Discovery result for one definition; kept separate so AgentDefinition stays free of
// computed/probed state.
internal sealed record ResolvedAgent(AgentDefinition Definition, bool Available);

// Seeds built-in agent definitions, overlays custom entries from settings, probes each
// definition's search paths for availability, and resolves the active agent (first
// Enabled && Available, Claude-default-first). Re-runnable on load and on settings change.
internal static class AgentRegistry
{
    private static IReadOnlyList<ResolvedAgent> ChainBacking { get; set; } = [];

    public static IReadOnlyList<ResolvedAgent> Chain => ChainBacking;

    public static void Refresh() =>
        ChainBacking = AISettings.GetAgents()
            .Select(static def => new ResolvedAgent(def, ProbeAvailable(def)))
            .ToArray();

    // The built-in definitions in chain order (Claude first, Codex second), reproducing
    // today's hardcoded install-dir candidates and empty model/args so out-of-the-box launch
    // behavior is unchanged. AISettings.GetAgents re-seeds from here on every read.
    public static IReadOnlyList<AgentDefinition> Builtins() =>
    [
        Builtin("claude", AgentAdapter.Claude, "claude"),
        Builtin("codex", AgentAdapter.Codex, "codex"),
    ];

    private static AgentDefinition Builtin(string name, AgentAdapter adapter, string command) =>
        new(name, adapter, command, DefaultSearchPaths(command), string.Empty, [], string.Empty, true, true);

    // The exact candidate ordering CliAgent.TryResolveCommand used before this refactor:
    // ~/.local/bin/<cmd>, /opt/homebrew/bin/<cmd>, /usr/local/bin/<cmd>. Last match wins.
    public static IReadOnlyList<string> DefaultSearchPaths(string command)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(home, ".local", "bin", command),
            $"/opt/homebrew/bin/{command}",
            $"/usr/local/bin/{command}",
        ];
    }

    private static bool ProbeAvailable(AgentDefinition def) => def.SearchPaths.Any(File.Exists);

    // First Enabled && Available in chain order, then the configured default, then the
    // first enabled+available built-in. Falls back to nothing only when discovery is empty.
    public static bool TryResolveActive(out AgentDefinition def)
    {
        string preferred = AISettings.DefaultAgentName;
        ResolvedAgent[] usable = ChainBacking.Where(static r => r.Definition.Enabled && r.Available).ToArray();

        ResolvedAgent? match = usable.FirstOrDefault(r => r.Definition.Name == preferred)
            ?? usable.FirstOrDefault();
        if (match is not null)
        {
            def = match.Definition;
            return true;
        }

        def = default!;
        return false;
    }

    public static bool TryGet(string name, out AgentDefinition def)
    {
        ResolvedAgent? match = ChainBacking.FirstOrDefault(r => r.Definition.Name == name);
        if (match is not null)
        {
            def = match.Definition;
            return true;
        }

        def = default!;
        return false;
    }
}
