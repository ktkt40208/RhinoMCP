namespace RhMcp.Tools;

[McpServerToolType]
public static class GetSelectionTool
{
    [McpServerTool("get_selection", "Get Selection", true, false)]
    [Description("Return all currently selected objects in Rhino.")]
    public static string GetSelection(RhinoDoc doc)
    {
        var selected = doc.Objects
            .GetSelectedObjects(includeLights: false, includeGrips: false)
            .Select(obj => new
            {
                id = obj.Id.ToString(),
                name = obj.Name ?? "",
                layer = doc.Layers[obj.Attributes.LayerIndex].FullPath,
                type = obj.Geometry?.GetType().Name ?? "Unknown"
            })
            .ToArray();

        return JsonSerializer.Serialize(selected);
    }
}
