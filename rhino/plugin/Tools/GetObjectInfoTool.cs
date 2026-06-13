using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhMcp.Tools;

// Wave-1 query tool: object-level detail, complementing get_document_summary
// (document-level) and list_objects (shallow list). Ported from the
// jingcheng-chen/rhinomcp + mien-bot PR #11 `get_object_info` idea, rewritten as
// a single additive [McpServerTool] with System.Text.Json output. Keep the
// [Description] a single string literal (the current router codegen drops
// concatenated literals — see GetDocumentSummaryTool).
[McpServerToolType]
public static class GetObjectInfoTool
{
    [McpServerTool(Name = "get_object_info", Title = "Get Object Info", ReadOnly = true, Destructive = false)]
    [Description("Detailed information for a single object by id (GUID): name, geometry type, layer, visibility, lock state, display color, world bounding box, attribute user-text key/values, and type-specific metrics — curves report length/degree/closed, breps and extrusions report area/volume/is-solid/face-count, meshes report vertex/face counts, surfaces report area. Use list_objects to find ids and get_document_summary for the whole model. Pure query — does not change selection or viewport.")]
    public static string GetObjectInfo(
        RhinoDoc doc,
        [Description("Object id (GUID) as returned by list_objects / get_selection")] string id)
    {
        if (!Guid.TryParse(id, out var guid))
            return JsonSerializer.Serialize(new { error = $"Invalid GUID: {id}" });

        var obj = doc.Objects.FindId(guid);
        if (obj is null)
            return JsonSerializer.Serialize(new { error = $"Object not found: {id}" });

        var geometry = obj.Geometry;
        var bbox = geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;

        var userStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = obj.Attributes.GetUserStrings();
        foreach (string key in keys)
        {
            var value = obj.Attributes.GetUserString(key);
            if (value is not null)
                userStrings[key] = value;
        }

        return JsonSerializer.Serialize(new
        {
            id = obj.Id.ToString(),
            name = obj.Name ?? string.Empty,
            type = geometry?.GetType().Name ?? "Unknown",
            object_type = obj.ObjectType.ToString(),
            layer = doc.Layers[obj.Attributes.LayerIndex].FullPath,
            visible = obj.Visible,
            locked = obj.IsLocked,
            color = ColorHex(obj),
            bounding_box = bbox.IsValid
                ? new
                {
                    min = Xyz(bbox.Min),
                    max = Xyz(bbox.Max),
                }
                : null,
            user_strings = userStrings,
            metrics = Metrics(geometry),
        });
    }

    // Type-specific measurements, mirroring jingcheng's analyze/get_object_info
    // behaviour. Returned as a loosely-typed dictionary so each geometry kind can
    // contribute the fields that make sense for it.
    private static Dictionary<string, object> Metrics(GeometryBase? geometry)
    {
        var m = new Dictionary<string, object>(StringComparer.Ordinal);
        switch (geometry)
        {
            case Curve curve:
                m["length"] = curve.GetLength();
                m["degree"] = curve.Degree;
                m["is_closed"] = curve.IsClosed;
                m["is_planar"] = curve.IsPlanar();
                break;

            case Brep brep:
                m["face_count"] = brep.Faces.Count;
                m["edge_count"] = brep.Edges.Count;
                m["is_solid"] = brep.IsSolid;
                var brepArea = AreaMassProperties.Compute(brep);
                if (brepArea is not null)
                    m["area"] = brepArea.Area;
                if (brep.IsSolid)
                {
                    var vmp = VolumeMassProperties.Compute(brep);
                    if (vmp is not null)
                        m["volume"] = vmp.Volume;
                }
                break;

            case Extrusion extrusion:
                using (var eb = extrusion.ToBrep())
                {
                    if (eb is not null)
                    {
                        m["face_count"] = eb.Faces.Count;
                        m["is_solid"] = eb.IsSolid;
                        var ea = AreaMassProperties.Compute(eb);
                        if (ea is not null)
                            m["area"] = ea.Area;
                        if (eb.IsSolid)
                        {
                            var ev = VolumeMassProperties.Compute(eb);
                            if (ev is not null)
                                m["volume"] = ev.Volume;
                        }
                    }
                }
                break;

            case Surface surface:
                var surfArea = AreaMassProperties.Compute(surface);
                if (surfArea is not null)
                    m["area"] = surfArea.Area;
                break;

            case Mesh mesh:
                m["vertex_count"] = mesh.Vertices.Count;
                m["face_count"] = mesh.Faces.Count;
                m["is_closed"] = mesh.IsClosed;
                break;

            case Rhino.Geometry.Point point:
                m["location"] = Xyz(point.Location);
                break;
        }

        return m;
    }

    private static object Xyz(Point3d p) => new { x = p.X, y = p.Y, z = p.Z };

    private static string ColorHex(RhinoObject obj)
    {
        var c = obj.Attributes.DrawColor(obj.Document);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
