using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Rhino.DocObjects;

namespace RhMcp.Tools;

// Wave-2 batch tool ported from jingcheng-chen/rhinomcp `create_objects`.
// Demonstrates the router-codegen pattern for passing an ARRAY of objects: the
// generator collapses any top-level complex array (Foo[]/List<Foo>) to a single
// open object, so the array is instead carried as the "objects" value INSIDE an
// open-object 'request' dictionary. The open-object forwarding round-trips nested
// JSON faithfully (JsonNode.Parse of each value's raw text), so request["objects"]
// arrives as a real JSON array. Each element reuses create_object's per-type schema.
[McpServerToolType]
public static class CreateObjectsTool
{
    [McpServerTool(Name = "create_objects", Title = "Create Objects (batch)", ReadOnly = false, Destructive = true)]
    [Description("Create many objects in one call. Pass 'request' as an object with key 'objects' = an array of specs, each {type, parameters, name?, color?} using exactly the same fields as create_object (type ∈ POINT/LINE/POLYLINE/CIRCLE/ARC/ELLIPSE/CURVE/BOX/SPHERE/CONE/CYLINDER/SURFACE; parameters carries the per-type geometry; color is hex or a known name). Example: {\"objects\":[{\"type\":\"SPHERE\",\"parameters\":{\"radius\":1}},{\"type\":\"BOX\",\"parameters\":{\"width\":2,\"length\":2,\"height\":2},\"name\":\"crate\"}]}. Returns JSON with per-element success/id or error, in input order.")]
    public static string CreateObjects(
        RhinoDoc doc,
        [Description("Batch request: an object with key 'objects' = array of create_object-style specs")] Dictionary<string, JsonElement> request)
    {
        if (!request.TryGetValue("objects", out var objectsEl) || objectsEl.ValueKind != JsonValueKind.Array)
            return JsonSerializer.Serialize(new { success = false, error = "request must contain 'objects': an array of object specs" });

        var results = new List<object>();
        var created = 0;
        var index = -1;

        foreach (var spec in objectsEl.EnumerateArray())
        {
            index++;
            try
            {
                if (spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("each spec must be an object with a string 'type'");
                var type = typeEl.GetString()!;

                var p = spec.TryGetProperty("parameters", out var pe) && pe.ValueKind == JsonValueKind.Object
                    ? pe.Deserialize<Dictionary<string, JsonElement>>() ?? new Dictionary<string, JsonElement>()
                    : new Dictionary<string, JsonElement>();

                var id = GeometryFactory.Create(doc, type, p);
                if (id == Guid.Empty)
                    throw new InvalidOperationException($"failed to create {type} (invalid geometry?)");

                var name = spec.TryGetProperty("name", out var ne) && ne.ValueKind == JsonValueKind.String ? ne.GetString() : null;
                var color = spec.TryGetProperty("color", out var ce) && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;

                var obj = doc.Objects.FindId(id);
                if (obj is not null && (name is not null || color is not null))
                {
                    if (name is not null)
                        obj.Attributes.Name = name;
                    var parsed = ParseColor(color);
                    if (parsed is not null)
                    {
                        obj.Attributes.ObjectColor = parsed.Value;
                        obj.Attributes.ColorSource = ObjectColorSource.ColorFromObject;
                    }
                    obj.CommitChanges();
                }

                created++;
                results.Add(new { index, success = true, id = id.ToString(), type, name = name ?? string.Empty });
            }
            catch (Exception ex)
            {
                results.Add(new { index, success = false, error = ex.Message });
            }
        }

        doc.Views.Redraw();
        return JsonSerializer.Serialize(new
        {
            success = true,
            created,
            total = results.Count,
            results,
        });
    }

    // Hex ("#RRGGBB"/"#AARRGGBB") or known color name; mirrors SetLayerMaterialTool.
    private static Color? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = s.Substring(1);
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                return Color.FromArgb((int)((argb >> 24) & 0xFF), (int)((argb >> 16) & 0xFF), (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));
            return null;
        }
        var named = Color.FromName(s);
        return named.IsKnownColor ? named : null;
    }
}
