using Rhino.FileIO;

namespace RhMcp.Tools;

// TODO : Close doc should not spawn a rhino to close it
[McpServerToolType]
public static class CloseDocTool
{
    [McpServerTool(Name = "close_doc")]
    [Description("Close the current Rhino document. If path is given, save to that .3dm path first; otherwise discard unsaved changes.")]
    public static string CloseDoc(
        RhinoDoc doc,
        [Description("Optional absolute .3dm path to save to before closing. Omit to close without saving.")] string? path = null)
    {
        bool hasPath = !string.IsNullOrWhiteSpace(path);
        if (!hasPath)
        {
            path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rhmcp-close-{Guid.NewGuid():N}.3dm");
        }

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            throw new System.IO.DirectoryNotFoundException($"Directory does not exist: {dir}");

        doc.Modified = false;
        RhinoApp.RunScript(doc.RuntimeSerialNumber, $"_-Close {path}", false);
        return hasPath ? $"Document saved to {path} and closed." : "Document closed without saving.";
    }
}
