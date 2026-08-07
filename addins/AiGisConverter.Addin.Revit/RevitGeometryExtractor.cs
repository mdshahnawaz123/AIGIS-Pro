using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Extracts a plan footprint from a Revit element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target geometry model is two-dimensional - Point, Line, Polygon - and the wire format is
    /// well-known text. A Revit solid cannot survive that crossing intact, so what travels is its
    /// plan footprint, and the height it sat at travels beside it as an attribute. That is a
    /// deliberate loss, decided once here rather than improvised per element.
    /// </para>
    /// <para>
    /// The footprint is taken from a horizontal face where one exists. For a floor, roof, slab,
    /// beam or column that face <em>is</em> the footprint, concavities and openings in its outline
    /// included, so borrowing it is both cheaper and more faithful than any hull. Only when no
    /// horizontal face exists - a sloped roof, a brace - does it fall back to the convex hull of the
    /// tessellated vertices.
    /// </para>
    /// <para>
    /// Everything returned is in metres. Revit works in decimal feet internally regardless of what
    /// the model displays, and the conversion happens here so that no later stage has to know.
    /// </para>
    /// </remarks>
    internal sealed class RevitGeometryExtractor : IDisposable
    {
        /// <summary>How nearly horizontal a face must be to be treated as a footprint.</summary>
        /// <remarks>
        /// About twenty-five degrees of tilt. Generous, because a slab with a drainage fall is still
        /// a slab, and its top face is still the shape a planner wants to see.
        /// </remarks>
        private const double HorizontalNormalThreshold = 0.9d;

        /// <summary>The largest number of vertices a single ring may carry across the bridge.</summary>
        internal const int DefaultVertexLimit = 512;

        private readonly Options _options;
        private readonly Dictionary<long, List<Footprint.Point2D>> _symbolFootprints =
            new Dictionary<long, List<Footprint.Point2D>>();

        private readonly int _vertexLimit;

        /// <summary>Initializes a new instance of the <see cref="RevitGeometryExtractor"/> class.</summary>
        /// <param name="vertexLimit">The largest number of vertices a ring may carry.</param>
        internal RevitGeometryExtractor(int vertexLimit = DefaultVertexLimit)
        {
            _vertexLimit = vertexLimit < 4 ? DefaultVertexLimit : vertexLimit;

            _options = new Options
            {
                // Medium rather than Fine: Fine multiplies the vertex count of curved and detailed
                // families for a difference no plan footprint can show.
                DetailLevel = ViewDetailLevel.Medium,

                // References are for picking geometry in the UI. Computing them here costs memory
                // for something nothing on this path uses.
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
            };
        }

        /// <summary>Releases the geometry options.</summary>
        /// <remarks>
        /// Revit's <c>Options</c> derives from <c>APIObject</c>, which is disposable and holds
        /// unmanaged state. This one was constructed here, so releasing it is this type's job -
        /// unlike the solids and meshes handed out during traversal, which Revit owns.
        /// </remarks>
        public void Dispose()
        {
            if (_options != null)
            {
                _options.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>Gets how many elements reused a cached family footprint.</summary>
        internal int CacheHits { get; private set; }

        /// <summary>Gets how many rings were reduced to fit the vertex limit.</summary>
        internal int SimplifiedRings { get; private set; }

        /// <summary>
        /// Extracts the plan footprint of an element as well-known text.
        /// </summary>
        /// <param name="element">The element to read.</param>
        /// <param name="outcome">
        /// Receives the geometry kind on success, or the stage, reason and measurements behind a
        /// failure. Always populated, so a null return is never unexplained.
        /// </param>
        /// <returns>The geometry as well-known text, or null when no footprint could be built.</returns>
        internal string Extract(Element element, out GeometryOutcome outcome)
        {
            outcome = new GeometryOutcome { Kind = "Unknown" };

            if (element == null)
            {
                outcome.Fail(GeometryStage.Extractor, GeometryReason.NoGeometryElement, "element was null");
                return null;
            }

            GeometryElement geometry;

            try
            {
                geometry = element.get_Geometry(_options);
            }
            catch (Exception exception)
            {
                outcome.Fail(GeometryStage.Extractor, GeometryReason.OpenFailed, exception.GetType().Name + ": " + exception.Message);
                return null;
            }

            if (geometry == null)
            {
                outcome.Fail(GeometryStage.Extractor, GeometryReason.NoGeometryElement, "get_Geometry returned null");
                return null;
            }

            List<Footprint.Point2D> vertices = new List<Footprint.Point2D>();
            List<Footprint.Point2D> best = null;
            double bestArea = 0d;

            // What the traversal actually met. Without this, "no vertices" is a dead end; with it,
            // the difference between an empty solid, a curve-only shape and an unopened instance is
            // one column in the report.
            GeometrySurvey survey = new GeometrySurvey();

            try
            {
                Collect(geometry, Transform.Identity, vertices, ref best, ref bestArea, element, survey);
            }
            catch (Exception exception)
            {
                outcome.Warning = "Geometry traversal stopped early: " + exception.Message;
            }

            // Decided before the ring logic, because by then the distinction is gone. An element
            // that met no surface is a centreline - a pipe, a duct, a model line - and its convex
            // hull would be a polygon enclosing the run rather than the run itself. A straight run
            // survives the hull anyway, being collinear; a bent one does not, and would have become
            // a triangle over the bend.
            if (!survey.SawSurface && vertices.Count >= 2)
            {
                string centreline = Footprint.ToLineWkt(vertices);

                if (centreline != null)
                {
                    outcome.Kind = "Line";
                    return centreline;
                }
            }

            List<Footprint.Point2D> ring = best;
            bool usedHull = false;

            if (ring == null || Footprint.IsDegenerate(ring))
            {
                // Either no horizontal face anywhere in the element, or one that encloses nothing
                // measurable. The second case used to fall straight through to failure, discarding
                // a vertex set already in hand: any horizontal face beats a starting area of zero,
                // so a chamfer or a sliver could win the ring and lose the element. The hull of
                // everything tessellated is an over-approximation, which is the right direction to
                // be wrong in for a footprint.
                ring = Footprint.ConvexHull(vertices);
                usedHull = true;
            }

            // Degeneracy, not point count. A near-vertical plate projects to a sliver whose hull
            // keeps three or more points - the monotone chain compares cross products against
            // exactly zero, and floating-point noise leaves collinear points standing. Those rings
            // passed this guard, failed the polygon test below, and lost the element, when a
            // linestring was available the whole time. That accounted for every geometry failure
            // in the last export: 393 of 393.
            if (ring == null || Footprint.IsDegenerate(ring))
            {
                if (vertices.Count == 1)
                {
                    outcome.Kind = "Point";
                    return Footprint.ToPointWkt(vertices[0]);
                }

                if (vertices.Count >= 2)
                {
                    // Ordered first. These vertices came from a mesh or a hull, so their sequence
                    // is the tessellator's, not the geometry's, and joining them as they stand
                    // would double back along the sliver and overstate its length.
                    string line = Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(vertices));

                    if (line != null)
                    {
                        outcome.Kind = "Line";
                        return line;
                    }
                }

                // Distinguished so the report stays honest: a ring that was found and could not be
                // used is a different fact from no geometry at all.
                if (ring != null && ring.Count >= 3)
                {
                    outcome.Fail(
                        GeometryStage.FootprintBuilder,
                        GeometryReason.DegenerateRing,
                        "ring=" + ring.Count + " pts, area="
                            + Footprint.Area(ring).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                            + " m2, source=" + (usedHull ? "hull" : "horizontalFace")
                            + ", vertices=" + vertices.Count + ", " + survey.Describe());
                }
                else
                {
                    outcome.Fail(GeometryStage.FootprintBuilder, GeometryReason.NoVertices, survey.Describe());
                }

                return null;
            }

            if (ring.Count > _vertexLimit)
            {
                int before = ring.Count;

                ring = Footprint.Simplify(ring, _vertexLimit);
                SimplifiedRings++;

                outcome.Warning = "The footprint was simplified from " + before + " to " + ring.Count
                    + " vertices to keep the response transportable.";
            }

            string wkt = Footprint.ToPolygonWkt(ring);

            if (wkt == null)
            {
                // Reachable only through simplification: the ring was measurably non-degenerate at
                // the guard above, so a null here means dropping points to meet the vertex limit
                // collapsed it. Reported with the measurements rather than silently dropped,
                // because a ring that survived every earlier test and died to a stride is a fact
                // about the limit, not about the element.
                outcome.Fail(
                    GeometryStage.FootprintBuilder,
                    GeometryReason.DegenerateRing,
                    "ring=" + ring.Count + " pts, area=" + Footprint.Area(ring).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        + " m2, source=" + (usedHull ? "hull" : "horizontalFace")
                        + ", vertices=" + vertices.Count + ", " + survey.Describe());

                return null;
            }

            outcome.Kind = "Polygon";

            return wkt;
        }

        private void Collect(
            GeometryElement geometry,
            Transform transform,
            List<Footprint.Point2D> vertices,
            ref List<Footprint.Point2D> best,
            ref double bestArea,
            Element owner,
            GeometrySurvey survey)
        {
            foreach (GeometryObject item in geometry)
            {
                survey.Seen(item);

                Solid solid = item as Solid;

                if (solid != null)
                {
                    CollectSolid(solid, transform, vertices, ref best, ref bestArea, survey);
                    continue;
                }

                Mesh mesh = item as Mesh;

                if (mesh != null)
                {
                    survey.SawSurface = true;
                    CollectMesh(mesh, transform, vertices);
                    continue;
                }

                GeometryInstance instance = item as GeometryInstance;

                if (instance != null)
                {
                    CollectInstance(instance, vertices, ref best, ref bestArea, owner, survey);
                    continue;
                }

                // Curves are not a lesser form of solid, they are how Revit represents a whole
                // class of element: pipes, ducts, conduit and cable tray at anything below Fine
                // detail, model and property lines, and every line-based family. None of them
                // reached this traversal before, so they arrived with attributes and no geometry -
                // which for a drainage network is most of the network. They contribute vertices
                // only, never a ring: a centreline encloses no plan area, and the line fallback
                // below turns the vertices into the linestring such an element actually is.
                Curve curve = item as Curve;

                if (curve != null)
                {
                    CollectCurve(curve, transform, vertices);
                    continue;
                }

                PolyLine polyLine = item as PolyLine;

                if (polyLine != null)
                {
                    CollectPolyLine(polyLine, transform, vertices);
                    continue;
                }

                Point point = item as Point;

                if (point != null && point.Coord != null)
                {
                    vertices.Add(ToPlan(point.Coord, transform));
                }
            }
        }

        private static void CollectCurve(Curve curve, Transform transform, List<Footprint.Point2D> vertices)
        {
            IList<XYZ> tessellated;

            try
            {
                tessellated = curve.Tessellate();
            }
            catch (Exception)
            {
                // An unbound curve cannot be tessellated. Nothing to record: the survey already
                // counted it, so the report shows a curve was met and yielded no points.
                return;
            }

            if (tessellated == null)
            {
                return;
            }

            foreach (XYZ point in tessellated)
            {
                vertices.Add(ToPlan(point, transform));
            }
        }

        private static void CollectPolyLine(PolyLine polyLine, Transform transform, List<Footprint.Point2D> vertices)
        {
            IList<XYZ> points = polyLine.GetCoordinates();

            if (points == null)
            {
                return;
            }

            foreach (XYZ point in points)
            {
                vertices.Add(ToPlan(point, transform));
            }
        }

        /// <summary>
        /// Reads a family instance's geometry, reusing the symbol's footprint where it is safe to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A model has one door family and five hundred doors. Extracting the symbol once and moving
        /// its footprint is the difference that matters at scale.
        /// </para>
        /// <para>
        /// The cache only applies when the instance sits upright - its transform's Z axis still
        /// points up. A footprint is a projection, and projecting after an arbitrary rotation is not
        /// the same as rotating a projection: a tilted instance would get a footprint of the wrong
        /// shape, silently and plausibly. Those fall back to instance geometry, which is correct
        /// whatever the transform.
        /// </para>
        /// </remarks>
        private void CollectInstance(
            GeometryInstance instance,
            List<Footprint.Point2D> vertices,
            ref List<Footprint.Point2D> best,
            ref double bestArea,
            Element owner,
            GeometrySurvey survey)
        {
            Transform transform = instance.Transform;
            bool upright = transform != null
                && Math.Abs(transform.BasisZ.Z) > 1d - 1e-9d
                && Math.Abs(transform.BasisZ.X) < 1e-9d
                && Math.Abs(transform.BasisZ.Y) < 1e-9d;

            long symbolId = SymbolKey(owner);

            if (upright && symbolId != 0)
            {
                List<Footprint.Point2D> cached;

                if (_symbolFootprints.TryGetValue(symbolId, out cached))
                {
                    CacheHits++;

                    List<Footprint.Point2D> moved = Move(cached, transform);
                    double area = Footprint.Area(moved);

                    vertices.AddRange(moved);

                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = moved;
                    }

                    return;
                }

                // Symbol geometry is in family coordinates, so its footprint can be cached and then
                // moved per instance. Instance geometry could not be cached: it is already placed.
                GeometryElement symbol = instance.GetSymbolGeometry();

                if (symbol != null)
                {
                    List<Footprint.Point2D> symbolVertices = new List<Footprint.Point2D>();
                    List<Footprint.Point2D> symbolBest = null;
                    double symbolArea = 0d;

                    Collect(symbol, Transform.Identity, symbolVertices, ref symbolBest, ref symbolArea, null, survey);

                    List<Footprint.Point2D> footprint = symbolBest ?? Footprint.ConvexHull(symbolVertices);

                    if (footprint != null && footprint.Count >= 3)
                    {
                        _symbolFootprints[symbolId] = footprint;

                        List<Footprint.Point2D> moved = Move(footprint, transform);
                        double area = Footprint.Area(moved);

                        vertices.AddRange(moved);

                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = moved;
                        }

                        return;
                    }
                }
            }

            GeometryElement placed = instance.GetInstanceGeometry();

            if (placed != null)
            {
                // Instance geometry, not symbol geometry: the latter is in family coordinates, and
                // using it would stack every door and window at the family origin.
                Collect(placed, Transform.Identity, vertices, ref best, ref bestArea, owner, survey);
            }
        }

        private void CollectSolid(
            Solid solid,
            Transform transform,
            List<Footprint.Point2D> vertices,
            ref List<Footprint.Point2D> best,
            ref double bestArea,
            GeometrySurvey survey)
        {
            if (solid.Faces == null || solid.Faces.Size == 0)
            {
                survey.EmptySolids++;
                return;
            }

            survey.SawSurface = true;
            survey.SolidFaces += solid.Faces.Size;

            foreach (Face face in solid.Faces)
            {
                PlanarFace planar = face as PlanarFace;

                if (planar != null && Math.Abs(planar.FaceNormal.Z) >= HorizontalNormalThreshold)
                {
                    List<Footprint.Point2D> outline = OuterLoop(planar, transform);

                    if (outline != null)
                    {
                        double area = Footprint.Area(outline);

                        // The largest horizontal face wins. For a wall that is its footprint; for a
                        // beam its plan projection; for a slab with an opening, the outer boundary.
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = outline;
                        }

                        vertices.AddRange(outline);
                        continue;
                    }
                }

                CollectFaceVertices(face, transform, vertices);
            }
        }

        private static List<Footprint.Point2D> OuterLoop(PlanarFace face, Transform transform)
        {
            IList<CurveLoop> loops;

            try
            {
                loops = face.GetEdgesAsCurveLoops();
            }
            catch (Exception)
            {
                return null;
            }

            if (loops == null || loops.Count == 0)
            {
                return null;
            }

            List<Footprint.Point2D> outer = null;
            double outerArea = 0d;

            foreach (CurveLoop loop in loops)
            {
                List<Footprint.Point2D> points = new List<Footprint.Point2D>();

                foreach (Curve curve in loop)
                {
                    IList<XYZ> tessellated = curve.Tessellate();

                    if (tessellated == null)
                    {
                        continue;
                    }

                    foreach (XYZ point in tessellated)
                    {
                        points.Add(ToPlan(point, transform));
                    }
                }

                List<Footprint.Point2D> cleaned = Footprint.RemoveConsecutiveDuplicates(points);
                double area = Footprint.Area(cleaned);

                // The largest loop is the outer boundary; the rest are openings. Holes are dropped
                // rather than emitted, because the geometry model's Polygon carries no interior ring
                // and inventing one would mean changing the domain contract.
                if (area > outerArea)
                {
                    outerArea = area;
                    outer = cleaned;
                }
            }

            return outer;
        }

        private static void CollectFaceVertices(Face face, Transform transform, List<Footprint.Point2D> vertices)
        {
            Mesh mesh;

            try
            {
                mesh = face.Triangulate();
            }
            catch (Exception)
            {
                return;
            }

            CollectMesh(mesh, transform, vertices);
        }

        private static void CollectMesh(Mesh mesh, Transform transform, List<Footprint.Point2D> vertices)
        {
            if (mesh == null || mesh.Vertices == null)
            {
                return;
            }

            foreach (XYZ vertex in mesh.Vertices)
            {
                vertices.Add(ToPlan(vertex, transform));
            }
        }

        private static List<Footprint.Point2D> Move(IList<Footprint.Point2D> footprint, Transform transform)
        {
            List<Footprint.Point2D> moved = new List<Footprint.Point2D>(footprint.Count);

            foreach (Footprint.Point2D point in footprint)
            {
                // Back to feet, through the instance transform, and out to metres again. The
                // footprint was cached in metres; the transform is expressed in Revit's own units.
                XYZ inFeet = new XYZ(
                    point.X / Footprint.MetresPerFoot,
                    point.Y / Footprint.MetresPerFoot,
                    0d);

                XYZ placed = transform.OfPoint(inFeet);

                moved.Add(new Footprint.Point2D(
                    placed.X * Footprint.MetresPerFoot,
                    placed.Y * Footprint.MetresPerFoot));
            }

            return moved;
        }

        private static Footprint.Point2D ToPlan(XYZ point, Transform transform)
        {
            XYZ placed = transform == null || transform.IsIdentity ? point : transform.OfPoint(point);

            return new Footprint.Point2D(
                placed.X * Footprint.MetresPerFoot,
                placed.Y * Footprint.MetresPerFoot);
        }

        private static long SymbolKey(Element element)
        {
            FamilyInstance instance = element as FamilyInstance;

            if (instance == null || instance.Symbol == null)
            {
                return 0L;
            }

            // Hashed rather than read as an integer: ElementId.IntegerValue is deprecated in the
            // 2024 API, and the key only has to be unique within this read.
            return instance.Symbol.UniqueId == null ? 0L : instance.Symbol.UniqueId.GetHashCode();
        }
    }
}
