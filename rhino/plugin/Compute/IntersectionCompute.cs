using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Rhino.Geometry;
using Rhino.Collections;
using Rhino.Geometry.Intersect;

namespace Rhino.Compute.Intersect;

// Copied From : https://github.com/mcneel/compute.rhino3d/blob/8.x/src/compute.geometry/RhinoCompute.cs
// NOTE : Should not be enabled on Mac
internal static class IntersectionCompute
{
  static string ApiAddress([CallerMemberName] string caller = null)
  {
    return ComputeServer.ApiAddress(typeof(Intersection), caller);
  }

  /// <summary>
  /// Intersects a curve with an (infinite) plane.
  /// </summary>
  /// <param name="curve">Curve to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <param name="tolerance">Tolerance to use during intersection.</param>
  /// <returns>A list of intersection events or null if no intersections were recorded.</returns>
  public static CurveIntersections CurvePlane(Curve curve, Plane plane, double tolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, plane, tolerance);
  }

  /// <summary>
  /// Intersects a curve with an (infinite) plane.
  /// </summary>
  /// <param name="curve">Curve to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <param name="tolerance">Tolerance to use during intersection.</param>
  /// <returns>A list of intersection events or null if no intersections were recorded.</returns>
  public static CurveIntersections CurvePlane(Remote<Curve> curve, Plane plane, double tolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, plane, tolerance);
  }

  /// <summary>
  /// Intersects a mesh with an (infinite) plane.
  /// </summary>
  /// <param name="mesh">Mesh to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
  public static Polyline[] MeshPlane(Mesh mesh, Plane plane)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
  }

  /// <summary>
  /// Intersects a mesh with an (infinite) plane.
  /// </summary>
  /// <param name="mesh">Mesh to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
  public static Polyline[] MeshPlane(Remote<Mesh> mesh, Plane plane)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, plane);
  }

  /// <summary>
  /// Intersects a mesh with a collection of (infinite) planes.
  /// </summary>
  /// <param name="mesh">Mesh to intersect.</param>
  /// <param name="planes">Planes to intersect with.</param>
  /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
  /// <exception cref="ArgumentNullException">If planes is null.</exception>
  public static Polyline[] MeshPlane(Mesh mesh, IEnumerable<Plane> planes)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, planes);
  }

  /// <summary>
  /// Intersects a mesh with a collection of (infinite) planes.
  /// </summary>
  /// <param name="mesh">Mesh to intersect.</param>
  /// <param name="planes">Planes to intersect with.</param>
  /// <returns>An array of polylines describing the intersection loops or null (Nothing in Visual Basic) if no intersections could be found.</returns>
  /// <exception cref="ArgumentNullException">If planes is null.</exception>
  public static Polyline[] MeshPlane(Remote<Mesh> mesh, IEnumerable<Plane> planes)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), mesh, planes);
  }

  /// <summary>
  /// Intersects a Brep with an (infinite) plane.
  /// </summary>
  /// <param name="brep">Brep to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <param name="tolerance">Tolerance to use for intersections.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool BrepPlane(Brep brep, Plane plane, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, plane, tolerance);
  }

  /// <summary>
  /// Intersects a Brep with an (infinite) plane.
  /// </summary>
  /// <param name="brep">Brep to intersect.</param>
  /// <param name="plane">Plane to intersect with.</param>
  /// <param name="tolerance">Tolerance to use for intersections.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool BrepPlane(Remote<Brep> brep, Plane plane, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, plane, tolerance);
  }

  /// <summary>
  /// Finds the places where a curve intersects itself. 
  /// </summary>
  /// <param name="curve">Curve for self-intersections.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches itself to within tolerance, 
  /// an intersection is assumed.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveSelf(Curve curve, double tolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, tolerance);
  }

  /// <summary>
  /// Finds the places where a curve intersects itself. 
  /// </summary>
  /// <param name="curve">Curve for self-intersections.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches itself to within tolerance, 
  /// an intersection is assumed.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveSelf(Remote<Curve> curve, double tolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, tolerance);
  }

  /// <summary>
  /// Finds the intersections between two curves. 
  /// </summary>
  /// <param name="curveA">First curve for intersection.</param>
  /// <param name="curveB">Second curve for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_intersectcurves.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_intersectcurves.cs' lang='cs'/>
  /// <code source='examples\py\ex_intersectcurves.py' lang='py'/>
  /// </example>
  public static CurveIntersections CurveCurve(Curve curveA, Curve curveB, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curveA, curveB, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Finds the intersections between two curves. 
  /// </summary>
  /// <param name="curveA">First curve for intersection.</param>
  /// <param name="curveB">Second curve for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_intersectcurves.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_intersectcurves.cs' lang='cs'/>
  /// <code source='examples\py\ex_intersectcurves.py' lang='py'/>
  /// </example>
  public static CurveIntersections CurveCurve(Remote<Curve> curveA, Remote<Curve> curveB, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curveA, curveB, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a curve and an infinite line. 
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="line">Infinite line to intersect.</param>
  /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveLine(Curve curve, Line line, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, line, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a curve and an infinite line. 
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="line">Infinite line to intersect.</param>
  /// <param name="tolerance">Intersection tolerance. If the curves approach each other to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveLine(Remote<Curve> curve, Line line, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, line, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a curve and a surface.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="surface">Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_curvesurfaceintersect.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_curvesurfaceintersect.cs' lang='cs'/>
  /// <code source='examples\py\ex_curvesurfaceintersect.py' lang='py'/>
  /// </example>
  public static CurveIntersections CurveSurface(Curve curve, Surface surface, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, surface, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a curve and a surface.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="surface">Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_curvesurfaceintersect.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_curvesurfaceintersect.cs' lang='cs'/>
  /// <code source='examples\py\ex_curvesurfaceintersect.py' lang='py'/>
  /// </example>
  public static CurveIntersections CurveSurface(Remote<Curve> curve, Remote<Surface> surface, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, surface, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a sub-curve and a surface.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
  /// <param name="surface">Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveSurface(Curve curve, Interval curveDomain, Surface surface, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, curveDomain, surface, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a sub-curve and a surface.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="curveDomain">Domain of sub-curve to take into consideration for Intersections.</param>
  /// <param name="surface">Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance. If the curve approaches the surface to within tolerance, an intersection is assumed.</param>
  /// <param name="overlapTolerance">The tolerance with which the curves are tested.</param>
  /// <returns>A collection of intersection events.</returns>
  public static CurveIntersections CurveSurface(Remote<Curve> curve, Interval curveDomain, Remote<Surface> surface, double tolerance, double overlapTolerance)
  {
    return ComputeServer.Post<CurveIntersections>(ApiAddress(), curve, curveDomain, surface, tolerance, overlapTolerance);
  }

  /// <summary>
  /// Intersects a curve with a Brep. This function returns the 3D points of intersection
  /// and 3D overlap curves. If an error occurs while processing overlap curves, this function 
  /// will return false, but it will still provide partial results.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="brep">Brep for intersection.</param>
  /// <param name="tolerance">Fitting and near miss tolerance.</param>
  /// <param name="overlapCurves">The overlap curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_elevation.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_elevation.cs' lang='cs'/>
  /// <code source='examples\py\ex_elevation.py' lang='py'/>
  /// </example>
  public static bool CurveBrep(Curve curve, Brep brep, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, brep, tolerance);
  }

  /// <summary>
  /// Intersects a curve with a Brep. This function returns the 3D points of intersection
  /// and 3D overlap curves. If an error occurs while processing overlap curves, this function 
  /// will return false, but it will still provide partial results.
  /// </summary>
  /// <param name="curve">Curve for intersection.</param>
  /// <param name="brep">Brep for intersection.</param>
  /// <param name="tolerance">Fitting and near miss tolerance.</param>
  /// <param name="overlapCurves">The overlap curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  /// <example>
  /// <code source='examples\vbnet\ex_elevation.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_elevation.cs' lang='cs'/>
  /// <code source='examples\py\ex_elevation.py' lang='py'/>
  /// </example>
  public static bool CurveBrep(Remote<Curve> curve, Remote<Brep> brep, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, brep, tolerance);
  }

  /// <summary>
  /// Intersect a curve with a Brep. This function returns the intersection parameters on the curve.
  /// </summary>
  /// <param name="curve">Curve.</param>
  /// <param name="brep">Brep.</param>
  /// <param name="tolerance">Absolute tolerance for intersections.</param>
  /// <param name="angleTolerance">Angle tolerance in radians.</param>
  /// <param name="t">Curve parameters at intersections.</param>
  /// <returns>True on success, false on failure.</returns>
  public static bool CurveBrep(Curve curve, Brep brep, double tolerance, double angleTolerance, out double[] t)
  {
    return ComputeServer.Post<bool, double[]>(ApiAddress(), out t, curve, brep, tolerance, angleTolerance);
  }

  /// <summary>
  /// Intersect a curve with a Brep. This function returns the intersection parameters on the curve.
  /// </summary>
  /// <param name="curve">Curve.</param>
  /// <param name="brep">Brep.</param>
  /// <param name="tolerance">Absolute tolerance for intersections.</param>
  /// <param name="angleTolerance">Angle tolerance in radians.</param>
  /// <param name="t">Curve parameters at intersections.</param>
  /// <returns>True on success, false on failure.</returns>
  public static bool CurveBrep(Remote<Curve> curve, Remote<Brep> brep, double tolerance, double angleTolerance, out double[] t)
  {
    return ComputeServer.Post<bool, double[]>(ApiAddress(), out t, curve, brep, tolerance, angleTolerance);
  }

  /// <summary>
  /// Intersects a curve with a Brep face.
  /// </summary>
  /// <param name="curve">A curve.</param>
  /// <param name="face">A brep face.</param>
  /// <param name="tolerance">Fitting and near miss tolerance.</param>
  /// <param name="overlapCurves">A overlap curves array argument. This out reference is assigned during the call.</param>
  /// <param name="intersectionPoints">A points array argument. This out reference is assigned during the call.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool CurveBrepFace(Curve curve, BrepFace face, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, face, tolerance);
  }

  /// <summary>
  /// Intersects a curve with a Brep face.
  /// </summary>
  /// <param name="curve">A curve.</param>
  /// <param name="face">A brep face.</param>
  /// <param name="tolerance">Fitting and near miss tolerance.</param>
  /// <param name="overlapCurves">A overlap curves array argument. This out reference is assigned during the call.</param>
  /// <param name="intersectionPoints">A points array argument. This out reference is assigned during the call.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool CurveBrepFace(Remote<Curve> curve, BrepFace face, double tolerance, out Curve[] overlapCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out overlapCurves, out intersectionPoints, curve, face, tolerance);
  }

  /// <summary>
  /// Intersects two Surfaces.
  /// </summary>
  /// <param name="surfaceA">First Surface for intersection.</param>
  /// <param name="surfaceB">Second Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool SurfaceSurface(Surface surfaceA, Surface surfaceB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, surfaceA, surfaceB, tolerance);
  }

  /// <summary>
  /// Intersects two Surfaces.
  /// </summary>
  /// <param name="surfaceA">First Surface for intersection.</param>
  /// <param name="surfaceB">Second Surface for intersection.</param>
  /// <param name="tolerance">Intersection tolerance.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success, false on failure.</returns>
  public static bool SurfaceSurface(Remote<Surface> surfaceA, Remote<Surface> surfaceB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, surfaceA, surfaceB, tolerance);
  }

  /// <summary>
  /// Intersects two Breps.
  /// </summary>
  /// <param name="brepA">First Brep for intersection.</param>
  /// <param name="brepB">Second Brep for intersection.</param>
  /// <param name="tolerance">Intersection tolerance.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success; false on failure.</returns>
  public static bool BrepBrep(Brep brepA, Brep brepB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brepA, brepB, tolerance);
  }

  /// <summary>
  /// Intersects two Breps.
  /// </summary>
  /// <param name="brepA">First Brep for intersection.</param>
  /// <param name="brepB">Second Brep for intersection.</param>
  /// <param name="tolerance">Intersection tolerance.</param>
  /// <param name="intersectionCurves">The intersection curves will be returned here.</param>
  /// <param name="intersectionPoints">The intersection points will be returned here.</param>
  /// <returns>true on success; false on failure.</returns>
  public static bool BrepBrep(Remote<Brep> brepA, Remote<Brep> brepB, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brepA, brepB, tolerance);
  }

  /// <summary>
  /// Intersects a Brep and a Surface.
  /// </summary>
  /// <param name="brep">A brep to be intersected.</param>
  /// <param name="surface">A surface to be intersected.</param>
  /// <param name="tolerance">A tolerance value.</param>
  /// <param name="intersectionCurves">The intersection curves array argument. This out reference is assigned during the call.</param>
  /// <param name="intersectionPoints">The intersection points array argument. This out reference is assigned during the call.</param>
  /// <returns>true on success; false on failure.</returns>
  public static bool BrepSurface(Brep brep, Surface surface, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, surface, tolerance);
  }

  /// <summary>
  /// Intersects a Brep and a Surface.
  /// </summary>
  /// <param name="brep">A brep to be intersected.</param>
  /// <param name="surface">A surface to be intersected.</param>
  /// <param name="tolerance">A tolerance value.</param>
  /// <param name="intersectionCurves">The intersection curves array argument. This out reference is assigned during the call.</param>
  /// <param name="intersectionPoints">The intersection points array argument. This out reference is assigned during the call.</param>
  /// <returns>true on success; false on failure.</returns>
  public static bool BrepSurface(Remote<Brep> brep, Remote<Surface> surface, double tolerance, out Curve[] intersectionCurves, out Point3d[] intersectionPoints)
  {
    return ComputeServer.Post<bool, Curve[], Point3d[]>(ApiAddress(), out intersectionCurves, out intersectionPoints, brep, surface, tolerance);
  }

  /// <summary>
  /// This is an old overload kept for compatibility. Overlaps and near misses are ignored.
  /// </summary>
  /// <param name="meshA">First mesh for intersection.</param>
  /// <param name="meshB">Second mesh for intersection.</param>
  /// <returns>An array of intersection line segments, or null if no intersections were found.</returns>
  public static Line[] MeshMeshFast(Mesh meshA, Mesh meshB)
  {
    return ComputeServer.Post<Line[]>(ApiAddress(), meshA, meshB);
  }

  /// <summary>
  /// This is an old overload kept for compatibility. Overlaps and near misses are ignored.
  /// </summary>
  /// <param name="meshA">First mesh for intersection.</param>
  /// <param name="meshB">Second mesh for intersection.</param>
  /// <returns>An array of intersection line segments, or null if no intersections were found.</returns>
  public static Line[] MeshMeshFast(Remote<Mesh> meshA, Remote<Mesh> meshB)
  {
    return ComputeServer.Post<Line[]>(ApiAddress(), meshA, meshB);
  }

  /// <summary>
  /// Intersects two meshes. Overlaps and near misses are handled. This is an old method kept for compatibility.
  /// </summary>
  /// <param name="meshA">First mesh for intersection.</param>
  /// <param name="meshB">Second mesh for intersection.</param>
  /// <param name="tolerance">A tolerance value. If negative, the positive value will be used.
  /// WARNING! Good tolerance values are in the magnitude of 10^-7, or RhinoMath.SqrtEpsilon*10.</param>
  /// <returns>An array of intersection and overlaps polylines.</returns>
  public static Polyline[] MeshMeshAccurate(Mesh meshA, Mesh meshB, double tolerance)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), meshA, meshB, tolerance);
  }

  /// <summary>
  /// Intersects two meshes. Overlaps and near misses are handled. This is an old method kept for compatibility.
  /// </summary>
  /// <param name="meshA">First mesh for intersection.</param>
  /// <param name="meshB">Second mesh for intersection.</param>
  /// <param name="tolerance">A tolerance value. If negative, the positive value will be used.
  /// WARNING! Good tolerance values are in the magnitude of 10^-7, or RhinoMath.SqrtEpsilon*10.</param>
  /// <returns>An array of intersection and overlaps polylines.</returns>
  public static Polyline[] MeshMeshAccurate(Remote<Mesh> meshA, Remote<Mesh> meshB, double tolerance)
  {
    return ComputeServer.Post<Polyline[]>(ApiAddress(), meshA, meshB, tolerance);
  }

  /// <summary>Finds the first intersection of a ray with a mesh.</summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="ray">A ray to be casted.</param>
  /// <returns>
  /// >= 0.0 parameter along ray if successful.
  /// &lt; 0.0 if no intersection found.
  /// </returns>
  public static double MeshRay(Mesh mesh, Ray3d ray)
  {
    return ComputeServer.Post<double>(ApiAddress(), mesh, ray);
  }

  /// <summary>Finds the first intersection of a ray with a mesh.</summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="ray">A ray to be casted.</param>
  /// <returns>
  /// >= 0.0 parameter along ray if successful.
  /// &lt; 0.0 if no intersection found.
  /// </returns>
  public static double MeshRay(Remote<Mesh> mesh, Ray3d ray)
  {
    return ComputeServer.Post<double>(ApiAddress(), mesh, ray);
  }

  /// <summary>Finds the first intersection of a ray with a mesh.</summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="ray">A ray to be casted.</param>
  /// <param name="meshFaceIndices">faces on mesh that ray intersects.</param>
  /// <returns>
  /// >= 0.0 parameter along ray if successful.
  /// &lt; 0.0 if no intersection found.
  /// </returns>
  /// <remarks>
  /// The ray may intersect more than one face in cases where the ray hits
  /// the edge between two faces or the vertex corner shared by multiple faces.
  /// </remarks>
  public static double MeshRay(Mesh mesh, Ray3d ray, out int[] meshFaceIndices)
  {
    return ComputeServer.Post<double, int[]>(ApiAddress(), out meshFaceIndices, mesh, ray);
  }

  /// <summary>Finds the first intersection of a ray with a mesh.</summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="ray">A ray to be casted.</param>
  /// <param name="meshFaceIndices">faces on mesh that ray intersects.</param>
  /// <returns>
  /// >= 0.0 parameter along ray if successful.
  /// &lt; 0.0 if no intersection found.
  /// </returns>
  /// <remarks>
  /// The ray may intersect more than one face in cases where the ray hits
  /// the edge between two faces or the vertex corner shared by multiple faces.
  /// </remarks>
  public static double MeshRay(Remote<Mesh> mesh, Ray3d ray, out int[] meshFaceIndices)
  {
    return ComputeServer.Post<double, int[]>(ApiAddress(), out meshFaceIndices, mesh, ray);
  }

  /// <summary>
  /// Finds the intersection of a mesh and a polyline.
  /// </summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="curve">A polyline curves to intersect.</param>
  /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
  /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
  public static Point3d[] MeshPolyline(Mesh mesh, PolylineCurve curve, out int[] faceIds)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
  }

  /// <summary>
  /// Finds the intersection of a mesh and a polyline.
  /// </summary>
  /// <param name="mesh">A mesh to intersect.</param>
  /// <param name="curve">A polyline curves to intersect.</param>
  /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
  /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
  public static Point3d[] MeshPolyline(Remote<Mesh> mesh, PolylineCurve curve, out int[] faceIds)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, curve);
  }

  /// <summary>
  /// Finds the intersection of a mesh and a line
  /// </summary>
  /// <param name="mesh">A mesh to intersect</param>
  /// <param name="line">The line to intersect with the mesh</param>
  /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
  /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
  public static Point3d[] MeshLine(Mesh mesh, Line line, out int[] faceIds)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
  }

  /// <summary>
  /// Finds the intersection of a mesh and a line
  /// </summary>
  /// <param name="mesh">A mesh to intersect</param>
  /// <param name="line">The line to intersect with the mesh</param>
  /// <param name="faceIds">The indices of the intersecting faces. This out reference is assigned during the call.</param>
  /// <returns>An array of points: one for each face that was passed by the faceIds out reference.</returns>
  public static Point3d[] MeshLine(Remote<Mesh> mesh, Line line, out int[] faceIds)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out faceIds, mesh, line);
  }

  /// <summary>
  /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
  /// </summary>
  /// <param name="ray">A ray used in intersection.</param>
  /// <param name="geometry">Only Surface and Brep objects are currently supported. Trims are ignored on Breps.</param>
  /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
  /// <returns>An array of points: one for each surface or Brep face that was hit, or an empty array on failure.</returns>
  /// <exception cref="ArgumentNullException">geometry is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
  public static Point3d[] RayShoot(Ray3d ray, IEnumerable<GeometryBase> geometry, int maxReflections)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), ray, geometry, maxReflections);
  }

  /// <summary>
  /// Computes point intersections that occur when shooting a ray to a collection of surfaces and Breps.
  /// </summary>
  /// <param name="ray">A ray used in intersection.</param>
  /// <param name="geometry">Only Surface and Brep objects are currently supported. Trims are ignored on Breps.</param>
  /// <param name="maxReflections">The maximum number of reflections. This value should be any value between 1 and 1000, inclusive.</param>
  /// <returns>An array of points: one for each surface or Brep face that was hit, or an empty array on failure.</returns>
  /// <exception cref="ArgumentNullException">geometry is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">maxReflections is strictly outside the [1-1000] range.</exception>
  public static Point3d[] RayShoot(Ray3d ray, Remote<IEnumerable<GeometryBase>> geometry, int maxReflections)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), ray, geometry, maxReflections);
  }

  /// <summary>
  /// Projects points onto meshes.
  /// </summary>
  /// <param name="meshes">the meshes to project on to.</param>
  /// <param name="points">the points to project.</param>
  /// <param name="direction">the direction to project.</param>
  /// <param name="tolerance">
  /// Projection tolerances used for culling close points and for line-mesh intersection.
  /// </param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  public static Point3d[] ProjectPointsToMeshes(IEnumerable<Mesh> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), meshes, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto meshes.
  /// </summary>
  /// <param name="meshes">the meshes to project on to.</param>
  /// <param name="points">the points to project.</param>
  /// <param name="direction">the direction to project.</param>
  /// <param name="tolerance">
  /// Projection tolerances used for culling close points and for line-mesh intersection.
  /// </param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  public static Point3d[] ProjectPointsToMeshes(Remote<IEnumerable<Mesh>> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), meshes, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto meshes.
  /// </summary>
  /// <param name="meshes">the meshes to project on to.</param>
  /// <param name="points">the points to project.</param>
  /// <param name="direction">the direction to project.</param>
  /// <param name="tolerance">
  /// Projection tolerances used for culling close points and for line-mesh intersection.
  /// </param>
  /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  /// <example>
  /// <code source='examples\vbnet\ex_projectpointstomeshesex.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_projectpointstomeshesex.cs' lang='cs'/>
  /// <code source='examples\py\ex_projectpointstomeshesex.py' lang='py'/>
  /// </example>
  public static Point3d[] ProjectPointsToMeshesEx(IEnumerable<Mesh> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, meshes, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto meshes.
  /// </summary>
  /// <param name="meshes">the meshes to project on to.</param>
  /// <param name="points">the points to project.</param>
  /// <param name="direction">the direction to project.</param>
  /// <param name="tolerance">
  /// Projection tolerances used for culling close points and for line-mesh intersection.
  /// </param>
  /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  /// <example>
  /// <code source='examples\vbnet\ex_projectpointstomeshesex.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_projectpointstomeshesex.cs' lang='cs'/>
  /// <code source='examples\py\ex_projectpointstomeshesex.py' lang='py'/>
  /// </example>
  public static Point3d[] ProjectPointsToMeshesEx(Remote<IEnumerable<Mesh>> meshes, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, meshes, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto breps.
  /// </summary>
  /// <param name="breps">The breps projection targets.</param>
  /// <param name="points">The points to project.</param>
  /// <param name="direction">The direction to project.</param>
  /// <param name="tolerance">The tolerance used for intersections.</param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  /// <example>
  /// <code source='examples\vbnet\ex_projectpointstobreps.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_projectpointstobreps.cs' lang='cs'/>
  /// <code source='examples\py\ex_projectpointstobreps.py' lang='py'/>
  /// </example>
  public static Point3d[] ProjectPointsToBreps(IEnumerable<Brep> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), breps, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto breps.
  /// </summary>
  /// <param name="breps">The breps projection targets.</param>
  /// <param name="points">The points to project.</param>
  /// <param name="direction">The direction to project.</param>
  /// <param name="tolerance">The tolerance used for intersections.</param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  /// <example>
  /// <code source='examples\vbnet\ex_projectpointstobreps.vb' lang='vbnet'/>
  /// <code source='examples\cs\ex_projectpointstobreps.cs' lang='cs'/>
  /// <code source='examples\py\ex_projectpointstobreps.py' lang='py'/>
  /// </example>
  public static Point3d[] ProjectPointsToBreps(Remote<IEnumerable<Brep>> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance)
  {
    return ComputeServer.Post<Point3d[]>(ApiAddress(), breps, points, direction, tolerance);
  }

  /// <summary>
  /// Projects points onto breps.
  /// </summary>
  /// <param name="breps">The breps projection targets.</param>
  /// <param name="points">The points to project.</param>
  /// <param name="direction">The direction to project.</param>
  /// <param name="tolerance">The tolerance used for intersections.</param>
  /// <param name="indices">Return points[i] is a projection of points[indices[i]]</param>
  /// <returns>
  /// Array of projected points, or null in case of any error or invalid input.
  /// </returns>
  public static Point3d[] ProjectPointsToBrepsEx(IEnumerable<Brep> breps, IEnumerable<Point3d> points, Vector3d direction, double tolerance, out int[] indices)
  {
    return ComputeServer.Post<Point3d[], int[]>(ApiAddress(), out indices, breps, points, direction, tolerance);
  }

  internal class IntersectionEvent
  {
    /// <summary>
    /// All curve intersection events are either a single point or an overlap.
    /// </summary>
    public bool IsPoint { get; set; }

    /// <summary>
    /// All curve intersection events are either a single point or an overlap.
    /// </summary>
    /// <example>
    /// <code source='examples\vbnet\ex_curvesurfaceintersect.vb' lang='vbnet'/>
    /// <code source='examples\cs\ex_curvesurfaceintersect.cs' lang='cs'/>
    /// <code source='examples\py\ex_curvesurfaceintersect.py' lang='py'/>
    /// </example>
    public bool IsOverlap { get; set; }

    /// <summary>
    /// Gets the point on Curve A where the intersection occured. 
    /// If the intersection type is overlap, then this will return the 
    /// start of the overlap region.
    /// </summary>
    public Point3d PointA { get; set; }
    /// <summary>
    /// Gets the end point of the overlap on Curve A. 
    /// If the intersection type is not overlap, this value is meaningless.
    /// </summary>
    public Point3d PointA2 { get; set; }

    /// <summary>
    /// Gets the point on Curve B (or Surface B) where the intersection occured. 
    /// If the intersection type is overlap, then this will return the 
    /// start of the overlap region.
    /// </summary>
    public Point3d PointB { get; set; }
    /// <summary>
    /// Gets the end point of the overlap on Curve B (or Surface B). 
    /// If the intersection type is not overlap, this value is meaningless.
    /// </summary>
    public Point3d PointB2 { get; set; }

    /// <summary>
    /// Gets the parameter on Curve A where the intersection occured. 
    /// If the intersection type is overlap, then this will return the 
    /// start of the overlap region.
    /// </summary>
    public double ParameterA { get; set; }
    /// <summary>
    /// Gets the parameter on Curve B where the intersection occured. 
    /// If the intersection type is overlap, then this will return the 
    /// start of the overlap region.
    /// </summary>
    public double ParameterB { get; set; }

    /// <summary>
    /// Gets the interval on curve A where the overlap occurs. 
    /// If the intersection type is not overlap, this value is meaningless.
    /// </summary>
    public Interval OverlapA { get; set; }

    /// <summary>
    /// Gets the interval on curve B where the overlap occurs. 
    /// If the intersection type is not overlap, this value is meaningless.
    /// </summary>
    public Interval OverlapB { get; set; }
  }

  internal class CurveIntersections : List<IntersectionEvent> { }
}
