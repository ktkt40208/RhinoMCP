using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Rhino.DocObjects;

namespace RhMcp.Tools;

// Wave-2 typed-geometry tool ported from jingcheng-chen/rhinomcp `create_object`.
// This is the crux of the hybrid: jingcheng's discriminated `params` union ports
// onto mcneel as a single additive [McpServerTool] using an open
// Dictionary<string, JsonElement> argument — the router codegen maps that to its
// OpenObjectType and round-trips it through ParameterBinder, so NO core plumbing
// edits are needed and mcneel's deliberately-shallow schema design is respected.
//
// Router-codegen constraints learned from the live build (RouterToolGenerator):
//  * The generated proxy emits each parameter name VERBATIM, so a C# keyword name
//    like `@params` produces an illegal proxy — use a plain identifier (`parameters`).
//  * Only a fixed PassThroughTypes set survives as-is (string/string?/string[]/int[]/
//    bool/int/long/double/float and their `?` scalar forms). NOTABLY double[] and
//    nullable arrays (int[]?, double[]?) are NOT passthrough — they silently become
//    open objects. So vectors/colors must be either a passthrough scalar (we take
//    color as a hex string, matching SetLayerMaterialTool) or carried inside the
//    `parameters` open-object. Transforms (translate/rotate/scale, all double[]) are
//    intentionally deferred to a future modify_object port for this reason.
//  * Keep [Description] a SINGLE string literal (the generator drops concatenations).
[McpServerToolType]
public static class CreateObjectTool
{
    [McpServerTool(Name = "create_object", Title = "Create Object", ReadOnly = false, Destructive = true)]
    [Description("Create one geometric object in the active document. 'type' is one of POINT, LINE, POLYLINE, CIRCLE, ARC, ELLIPSE, CURVE, BOX, SPHERE, CONE, CYLINDER, SURFACE. 'parameters' carries the type-specific geometry: POINT {x,y,z}; LINE {start:[x,y,z],end:[x,y,z]}; POLYLINE {points:[[x,y,z],...]}; CIRCLE {center:[x,y,z],radius}; ARC {center:[x,y,z],radius,angle(deg)}; ELLIPSE {center:[x,y,z],radius_x,radius_y}; CURVE {points:[[x,y,z],...],degree(default 3)}; BOX {width,length,height}; SPHERE {radius}; CONE {radius,height,cap(default true)}; CYLINDER {radius,height,cap(default true)}; SURFACE {count:[u,v],points:[[x,y,z],...],degree:[u,v] optional,closed:[bool,bool] optional}. Optional name and color (hex like '#FF0000' or a known color name). Returns JSON with the new object id.")]
    public static string CreateObject(
        RhinoDoc doc,
        [Description("Geometry type: POINT LINE POLYLINE CIRCLE ARC ELLIPSE CURVE BOX SPHERE CONE CYLINDER SURFACE")] string type,
        [Description("Type-specific geometry parameters; see the tool description for per-type keys")] Dictionary<string, JsonElement>? parameters = null,
        [Description("Optional object name")] string? name = null,
        [Description("Optional display color: hex like '#FF0000' or a known color name")] string? color = null)
    {
        var p = parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        Guid id;
        try
        {
            id = GeometryFactory.Create(doc, type, p);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = ex.Message, type });
        }

        if (id == Guid.Empty)
            return JsonSerializer.Serialize(new { success = false, error = $"Failed to create {type} (invalid geometry?)", type });

        var parsedColor = ParseColor(color);
        if (color is not null && parsedColor is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Could not parse color: {color}", type });

        var obj = doc.Objects.FindId(id);
        if (obj is not null && (name is not null || parsedColor is not null))
        {
            if (name is not null)
                obj.Attributes.Name = name;
            if (parsedColor is not null)
            {
                obj.Attributes.ObjectColor = parsedColor.Value;
                obj.Attributes.ColorSource = ObjectColorSource.ColorFromObject;
            }
            obj.CommitChanges();
        }

        doc.Views.Redraw();
        return JsonSerializer.Serialize(new
        {
            success = true,
            id = id.ToString(),
            type,
            name = name ?? string.Empty,
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
