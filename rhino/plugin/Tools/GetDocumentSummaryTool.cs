using System.Drawing;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

// Wave-1 query tool ported from jingcheng-chen/rhinomcp's `get_document_summary`
// (plugin/Functions/GetDocumentSummary.cs). Kept purely additive: a single new
// [McpServerTool] file, no edits to the MCP plumbing. Newtonsoft JObject/JArray
// in the original is replaced with System.Text.Json anonymous objects, matching
// the convention in the sibling tools (e.g. ListObjectsTool).
[McpServerToolType]
public static class GetDocumentSummaryTool
{
    // NOTE: keep this Description a SINGLE string literal (no "a" + "b" concatenation).
    // The current router source generator (RouterToolGenerator, pre-PR #61) cannot evaluate
    // concatenated literals and emits an empty [Description("")] on the router proxy, hiding
    // the text from the LLM. (PR #61 adds concat support; until it merges, single literal only.)
    [McpServerTool(Name = "get_document_summary", Title = "Get Document Summary", ReadOnly = true, Destructive = false)]
    [Description("Lightweight aggregate summary of the active document: metadata (name, path, units, tolerances, created/modified dates), total object count, per-type counts (POINT/LINE/POLYLINE/CIRCLE/ARC/CURVE/EXTRUSION/BREP/SURFACE/MESH), per-layer counts, model-wide bounding box, and the full layer hierarchy (parent/child nesting with per-layer object counts, color, visibility, lock state). Call this first to understand model composition; use list_objects for individual object details. Pure query — does not change selection or viewport.")]
    public static string GetDocumentSummary(RhinoDoc doc)
    {
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var layerCounts = new Dictionary<int, int>();
        var modelBbox = BoundingBox.Empty;

        foreach (var obj in doc.Objects)
        {
            var objType = NormalizeType(obj);
            typeCounts[objType] = typeCounts.GetValueOrDefault(objType) + 1;

            var layerIndex = obj.Attributes.LayerIndex;
            layerCounts[layerIndex] = layerCounts.GetValueOrDefault(layerIndex) + 1;

            var geometry = obj.Geometry;
            if (geometry is not null)
                modelBbox.Union(geometry.GetBoundingBox(true));
        }

        var objectsByType = typeCounts
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Group by full path (FullPath is unambiguous across nested layers with the same short name).
        var objectsByLayer = layerCounts
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => doc.Layers[kvp.Key].FullPath, kvp => kvp.Value);

        return JsonSerializer.Serialize(new
        {
            meta_data = new
            {
                name = doc.Name ?? string.Empty,
                path = doc.Path ?? string.Empty,
                units = doc.ModelUnitSystem.ToString(),
                tolerance = doc.ModelAbsoluteTolerance,
                angle_tolerance = doc.ModelAngleToleranceDegrees,
                date_created = doc.DateCreated,
                date_modified = doc.DateLastEdited,
            },
            object_count = doc.Objects.Count,
            objects_by_type = objectsByType,
            objects_by_layer = objectsByLayer,
            model_bounding_box = modelBbox.IsValid
                ? new
                {
                    min = Xyz(modelBbox.Min),
                    max = Xyz(modelBbox.Max),
                }
                : null,
            layer_count = doc.Layers.Count,
            layer_hierarchy = BuildLayerHierarchy(doc, layerCounts),
        });
    }

    // Mirrors jingcheng GetDocumentSummary.cs:88-104 — collapses RhinoCommon geometry
    // types into the coarse buckets an LLM reasons about, falling back to the raw
    // ObjectType name for anything unrecognised.
    private static string NormalizeType(RhinoObject obj) => obj.Geometry switch
    {
        Rhino.Geometry.Point => "POINT",
        LineCurve => "LINE",
        PolylineCurve => "POLYLINE",
        ArcCurve arc => arc.Arc.IsCircle ? "CIRCLE" : "ARC",
        Curve => "CURVE",
        Extrusion => "EXTRUSION",
        Brep => "BREP",
        Surface => "SURFACE",
        Mesh => "MESH",
        _ => obj.ObjectType.ToString().ToUpperInvariant(),
    };

    // Mirrors jingcheng GetDocumentSummary.cs:106-150 — two-pass build of the layer
    // tree, returning only root layers (children nested under each parent).
    private static IReadOnlyList<LayerNode> BuildLayerHierarchy(RhinoDoc doc, Dictionary<int, int> layerCounts)
    {
        var nodes = new Dictionary<Guid, LayerNode>();

        foreach (var layer in doc.Layers)
        {
            if (layer.IsDeleted)
                continue;

            nodes[layer.Id] = new LayerNode
            {
                id = layer.Id.ToString(),
                name = layer.Name,
                full_path = layer.FullPath,
                color = ToHex(layer.Color),
                visible = layer.IsVisible,
                locked = layer.IsLocked,
                object_count = layerCounts.GetValueOrDefault(layer.Index),
                parent_id = layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId.ToString(),
            };
        }

        var roots = new List<LayerNode>();
        foreach (var layer in doc.Layers)
        {
            if (layer.IsDeleted || !nodes.TryGetValue(layer.Id, out var node))
                continue;

            if (layer.ParentLayerId != Guid.Empty && nodes.TryGetValue(layer.ParentLayerId, out var parent))
                parent.children.Add(node);
            else
                roots.Add(node);
        }

        return roots;
    }

    private static object Xyz(Point3d p) => new { x = p.X, y = p.Y, z = p.Z };

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // Serialized by System.Text.Json via public fields/properties; `children` stays
    // mutable so the two-pass builder can attach descendants in place.
    private sealed class LayerNode
    {
        public string id { get; init; } = string.Empty;
        public string name { get; init; } = string.Empty;
        public string full_path { get; init; } = string.Empty;
        public string color { get; init; } = string.Empty;
        public bool visible { get; init; }
        public bool locked { get; init; }
        public int object_count { get; init; }
        public string? parent_id { get; init; }
        public List<LayerNode> children { get; } = [];
    }
}
