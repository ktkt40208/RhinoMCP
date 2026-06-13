using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

// Wave-2 tool ported from jingcheng-chen/rhinomcp `modify_object`. Rename, recolor,
// toggle visibility, and transform an existing object by id. Demonstrates the
// router-codegen workaround for vector inputs: translation/rotation/scale are
// double[] which are NOT in the generator's PassThroughTypes, so they are carried
// inside an open-object 'transform' Dictionary<string,JsonElement> (which IS handled)
// rather than as top-level array args. name/color stay as passthrough scalars.
[McpServerToolType]
public static class ModifyObjectTool
{
    [McpServerTool(Name = "modify_object", Title = "Modify Object", ReadOnly = false, Destructive = true)]
    [Description("Modify an existing object by id (GUID). Optionally set new_name; set new_color (hex like '#FF0000' or a known color name); set visible (true/false); and/or apply a transform via the 'transform' object whose keys are any of translation:[x,y,z], rotation:[x,y,z] (radians, about the object's bounding-box center), scale:[x,y,z] (about the bounding-box center). Returns JSON with the (possibly new) object id. Use list_objects/get_selection to find ids.")]
    public static string ModifyObject(
        RhinoDoc doc,
        [Description("Object id (GUID) to modify")] string id,
        [Description("Optional new name")] string? new_name = null,
        [Description("Optional new display color: hex like '#FF0000' or a known color name")] string? new_color = null,
        [Description("Optional visibility toggle")] bool? visible = null,
        [Description("Optional transform: object with any of translation/rotation/scale as [x,y,z]")] Dictionary<string, JsonElement>? transform = null)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { success = false, error = $"Invalid GUID: {id}" });

        var obj = doc.Objects.FindId(guid);
        if (obj is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Object not found: {id}" });

        var attributesModified = false;

        if (new_name is not null)
        {
            obj.Attributes.Name = new_name;
            attributesModified = true;
        }

        if (new_color is not null)
        {
            var parsed = ParseColor(new_color);
            if (parsed is null)
                return JsonSerializer.Serialize(new { success = false, error = $"Could not parse color: {new_color}" });
            obj.Attributes.ObjectColor = parsed.Value;
            obj.Attributes.ColorSource = ObjectColorSource.ColorFromObject;
            attributesModified = true;
        }

        if (attributesModified)
            doc.Objects.ModifyAttributes(obj, obj.Attributes, quiet: true);

        if (visible.HasValue)
        {
            if (visible.Value) doc.Objects.Show(obj, ignoreLayerMode: false);
            else doc.Objects.Hide(obj, ignoreLayerMode: false);
        }

        var resultId = guid;
        if (transform is not null && obj.Geometry is not null)
        {
            var center = obj.Geometry.GetBoundingBox(true).Center;
            var xform = Transform.Identity;

            if (TryVec(transform, "scale", out var s))
            {
                var plane = Plane.WorldXY;
                plane.Origin = center;
                xform = Transform.Scale(plane, s[0], s[1], s[2]) * xform;
            }
            if (TryVec(transform, "rotation", out var r))
            {
                var rot = Transform.Rotation(r[2], Vector3d.ZAxis, center)
                          * Transform.Rotation(r[1], Vector3d.YAxis, center)
                          * Transform.Rotation(r[0], Vector3d.XAxis, center);
                xform = rot * xform;
            }
            if (TryVec(transform, "translation", out var t))
                xform = Transform.Translation(t[0], t[1], t[2]) * xform;

            if (!xform.Equals(Transform.Identity))
            {
                var newId = doc.Objects.Transform(guid, xform, deleteOriginal: true);
                if (newId != Guid.Empty)
                    resultId = newId;
            }
        }

        doc.Views.Redraw();
        return JsonSerializer.Serialize(new { success = true, id = resultId.ToString() });
    }

    private static bool TryVec(Dictionary<string, JsonElement> d, string key, out double[] v)
    {
        v = [];
        if (!d.TryGetValue(key, out var e))
            return false;
        var a = e.Deserialize<double[]>();
        if (a is null || a.Length < 3)
            return false;
        v = a;
        return true;
    }

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
