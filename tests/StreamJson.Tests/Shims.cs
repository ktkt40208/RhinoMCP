using System.Collections.Generic;

// Headless stand-ins for the few RhinoCommon / plugin-bootstrap surfaces the compiled-in agent
// sources reach for. They reproduce exactly the API the code under test calls (no more), so the
// real StreamJsonAgent / ConversationStore / parsers run unchanged in a RhinoCommon-free assembly.

namespace Rhino
{
    // StreamJsonAgent's only RhinoCommon dependency is the err/parse-error log line. A no-op sink is
    // faithful: the design mandates the parsers stay RhinoApp-free and logging is the runner's err
    // channel only, never an assertion surface.
    internal static class RhinoApp
    {
        public static void WriteLine(string text) { }
    }
}

namespace Rhino.PlugIns
{
    // The slice of PersistentSettings ConversationStore touches: a flat string keystore plus the
    // child-node accessors AISettings.Conversations relies on. In-memory, deterministic, resettable.
    internal sealed class PersistentSettings
    {
        private Dictionary<string, string> Strings { get; } = new();

        public void SetString(string key, string value) => Strings[key] = value;

        public bool TryGetString(string key, out string value) => Strings.TryGetValue(key, out value!);

        public void DeleteItem(string key) => Strings.Remove(key);

        public IEnumerable<string> Keys => new List<string>(Strings.Keys);
    }
}

namespace RhMcp
{
    // The two AISettings members the compiled-in closure reads. Tests assign these directly to
    // script settings-driven behaviour (ResolveMcpServers) and to reset the transcript store.
    internal static class AISettings
    {
        public static string ExtraMcpServersJson { get; set; } = "{\"mcpServers\":{}}";

        public static Rhino.PlugIns.PersistentSettings Conversations { get; set; } = new();

        public static void ResetForTest()
        {
            ExtraMcpServersJson = "{\"mcpServers\":{}}";
            Conversations = new();
        }
    }

    // AgentPrompts.Compose only needs the server-name const; the real RouterMcpConfig also resolves
    // an on-disk router path through RhMcpPlugin (RhinoCommon), which is out of scope for headless
    // parser tests. Shimming just the const keeps AgentPrompts pure.
    internal static class RouterMcpConfig
    {
        internal const string ServerName = "rhino";
    }
}
