using RhMcp.Resources;

using Grasshopper;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GH1_StartTool
{

    [McpServerTool(Name = "g1_start")]
    [Description("Starts Grasshopper")]
    public static string Launch(RhinoDoc doc)
    {
        try
        {
            RhinoApp.RunScript(doc.RuntimeSerialNumber, "_Grasshopper", true);
            return Verify();
        }
        catch (Exception ex)
        {
            return $"g1_start threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static string Verify() => Instances.ActiveCanvas is not null ? "Opened Grasshopper" : "Failure opening Grasshopper";

}
