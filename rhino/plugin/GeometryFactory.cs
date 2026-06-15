using System.Text.Json;
using Rhino;
using Rhino.Geometry;

namespace RhMcp.Tools;

// Shared geometry construction used by create_object and create_objects.
// Lives OUTSIDE /plugin/Tools/ so the router source generator never treats it as
// a tool file (it has no [McpServerTool] anyway, but keeping it out of Tools/ is
// the clean guarantee). Pure RhinoCommon + System.Text.Json; no MCP plumbing.
internal static class GeometryFactory
{
    // Build one object of `type` from a type-specific parameter dictionary and add
    // it to the document. Returns the new object's id (Guid.Empty on add failure).
    public static Guid Create(RhinoDoc doc, string type, Dictionary<string, JsonElement> p)
    {
        switch (type.ToUpperInvariant())
        {
            case "POINT":
                return doc.Objects.AddPoint(new Point3d(Num(p, "x"), Num(p, "y"), Num(p, "z")));
            case "LINE":
                return doc.Objects.AddLine(Pt(p, "start"), Pt(p, "end"));
            case "POLYLINE":
                return doc.Objects.AddPolyline(PtList(p, "points"));
            case "CIRCLE":
                return doc.Objects.AddCircle(new Circle(Pt(p, "center"), Num(p, "radius")));
            case "ARC":
                return doc.Objects.AddArc(new Arc(
                    new Plane(Pt(p, "center"), Vector3d.ZAxis),
                    Num(p, "radius"),
                    Num(p, "angle") * Math.PI / 180.0));
            case "ELLIPSE":
                return doc.Objects.AddCurve(new Ellipse(
                    new Plane(Pt(p, "center"), Vector3d.ZAxis),
                    Num(p, "radius_x"), Num(p, "radius_y")).ToNurbsCurve());
            case "CURVE":
            {
                var cpts = PtList(p, "points");
                var degree = p.TryGetValue("degree", out var d) ? Int(d) : Math.Min(3, cpts.Count - 1);
                var curve = Curve.CreateControlPointCurve(cpts, Math.Max(1, degree))
                    ?? throw new InvalidOperationException("Cannot create a NURBS curve from the given control points");
                return doc.Objects.AddCurve(curve);
            }
            case "BOX":
            {
                double w = Num(p, "width"), l = Num(p, "length"), h = Num(p, "height");
                var box = new Box(Plane.WorldXY,
                    new Interval(-w / 2, w / 2), new Interval(-l / 2, l / 2), new Interval(-h / 2, h / 2));
                return doc.Objects.AddBrep(box.ToBrep());
            }
            case "SPHERE":
                return doc.Objects.AddBrep(new Sphere(Point3d.Origin, Num(p, "radius")).ToBrep());
            case "CONE":
            {
                var cap = !p.TryGetValue("cap", out var cj) || Bool(cj);
                return doc.Objects.AddBrep(Brep.CreateFromCone(
                    new Cone(Plane.WorldXY, Num(p, "height"), Num(p, "radius")), cap));
            }
            case "CYLINDER":
            {
                var cap = !p.TryGetValue("cap", out var cj) || Bool(cj);
                var cyl = new Cylinder(new Circle(Plane.WorldXY, Num(p, "radius")), Num(p, "height"));
                return doc.Objects.AddBrep(cyl.ToBrep(cap, cap));
            }
            case "SURFACE":
            {
                var count = p["count"].Deserialize<int[]>() ?? throw new InvalidOperationException("SURFACE requires 'count'");
                var spts = PtList(p, "points");
                var deg = p.TryGetValue("degree", out var dg) ? dg.Deserialize<int[]>()! : [3, 3];
                var closed = p.TryGetValue("closed", out var cl) ? cl.Deserialize<bool[]>()! : [false, false];
                var srf = NurbsSurface.CreateThroughPoints(spts, count[0], count[1], deg[0], deg[1], closed[0], closed[1])
                    ?? throw new InvalidOperationException("Cannot create a NURBS surface from the given points");
                return doc.Objects.AddSurface(srf);
            }
            default:
                throw new ArgumentException($"Unknown type: {type}");
        }
    }

    // --- JsonElement extraction helpers (default options; primitive/array shapes
    //     need no naming policy). Tolerant of integers written as 3 or 3.0. ---
    public static double Num(Dictionary<string, JsonElement> p, string key) =>
        p.TryGetValue(key, out var e) ? e.GetDouble() : throw new ArgumentException($"Missing parameter: {key}");

    public static int Int(JsonElement e) => e.TryGetInt32(out var v) ? v : (int)e.GetDouble();

    public static bool Bool(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        _ => false,
    };

    public static Point3d Pt(Dictionary<string, JsonElement> p, string key)
    {
        if (!p.TryGetValue(key, out var e))
            throw new ArgumentException($"Missing parameter: {key}");
        var a = e.Deserialize<double[]>() ?? throw new ArgumentException($"Parameter '{key}' must be [x, y, z]");
        if (a.Length < 3)
            throw new ArgumentException($"Parameter '{key}' must have 3 components");
        return new Point3d(a[0], a[1], a[2]);
    }

    public static List<Point3d> PtList(Dictionary<string, JsonElement> p, string key)
    {
        if (!p.TryGetValue(key, out var e))
            throw new ArgumentException($"Missing parameter: {key}");
        var rows = e.Deserialize<double[][]>() ?? throw new ArgumentException($"Parameter '{key}' must be a list of [x, y, z]");
        return rows.Select(a => new Point3d(a[0], a[1], a[2])).ToList();
    }
}
