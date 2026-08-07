using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Plan-space geometry: rings, winding, simplification and well-known text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately free of any Revit type. Everything here is arithmetic on plain coordinates,
    /// which means it can be reasoned about and reviewed without a running copy of Revit - the only
    /// part of this add-in for which that is true.
    /// </para>
    /// <para>
    /// Coordinates are metres. The conversion from Revit's internal decimal feet happens at the
    /// boundary, in <see cref="RevitGeometryExtractor"/>, so nothing downstream of it ever sees feet.
    /// </para>
    /// </remarks>
    internal static class Footprint
    {
        /// <summary>Exact metres per foot. The international foot is defined as this, not rounded to it.</summary>
        internal const double MetresPerFoot = 0.3048d;

        /// <summary>Rings closer than this in plan are treated as the same point.</summary>
        private const double Tolerance = 1e-7d;

        /// <summary>A point in plan, in metres.</summary>
        internal struct Point2D
        {
            /// <summary>Initializes a new instance of the <see cref="Point2D"/> struct.</summary>
            /// <param name="x">Easting, in metres.</param>
            /// <param name="y">Northing, in metres.</param>
            internal Point2D(double x, double y)
            {
                X = x;
                Y = y;
            }

            /// <summary>Gets the easting, in metres.</summary>
            internal double X { get; }

            /// <summary>Gets the northing, in metres.</summary>
            internal double Y { get; }
        }

        /// <summary>Twice the signed area of a ring. Negative means clockwise.</summary>
        /// <param name="ring">The ring. The first and last points need not match.</param>
        /// <returns>Twice the signed area.</returns>
        internal static double TwiceSignedArea(IList<Point2D> ring)
        {
            if (ring == null || ring.Count < 3)
            {
                return 0d;
            }

            double total = 0d;

            for (int i = 0; i < ring.Count; i++)
            {
                Point2D current = ring[i];
                Point2D next = ring[(i + 1) % ring.Count];

                total += (current.X * next.Y) - (next.X * current.Y);
            }

            return total;
        }

        /// <summary>Gets the unsigned plan area of a ring, in square metres.</summary>
        /// <param name="ring">The ring.</param>
        /// <returns>The area.</returns>
        internal static double Area(IList<Point2D> ring)
        {
            return Math.Abs(TwiceSignedArea(ring)) / 2d;
        }

        /// <summary>Removes consecutive duplicate points.</summary>
        /// <param name="points">The points to clean.</param>
        /// <returns>The cleaned run.</returns>
        internal static List<Point2D> RemoveConsecutiveDuplicates(IList<Point2D> points)
        {
            List<Point2D> cleaned = new List<Point2D>();

            if (points == null)
            {
                return cleaned;
            }

            foreach (Point2D point in points)
            {
                if (cleaned.Count == 0 || !SamePoint(cleaned[cleaned.Count - 1], point))
                {
                    cleaned.Add(point);
                }
            }

            // A ring arriving already closed would otherwise keep a duplicate at the seam.
            while (cleaned.Count > 1 && SamePoint(cleaned[0], cleaned[cleaned.Count - 1]))
            {
                cleaned.RemoveAt(cleaned.Count - 1);
            }

            return cleaned;
        }

        /// <summary>
        /// Builds the convex hull of a point set, in plan.
        /// </summary>
        /// <remarks>
        /// The fallback for geometry with no horizontal face to borrow an outline from - a sloped
        /// roof, a brace, a curved railing. A hull is an honest over-approximation: it never omits
        /// part of the element, and it never claims a concavity that is not there, because it claims
        /// no concavity at all. Anything better needs a polygon union, which needs a geometry
        /// library this side of the bridge does not have.
        /// </remarks>
        /// <param name="points">The points to enclose.</param>
        /// <returns>The hull in counter-clockwise order, or an empty list when degenerate.</returns>
        internal static List<Point2D> ConvexHull(IList<Point2D> points)
        {
            List<Point2D> result = new List<Point2D>();

            if (points == null || points.Count < 3)
            {
                return result;
            }

            List<Point2D> sorted = new List<Point2D>(points);

            sorted.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

            // Andrew's monotone chain. O(n log n), dominated by the sort.
            List<Point2D> hull = new List<Point2D>(sorted.Count * 2);

            for (int i = 0; i < sorted.Count; i++)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0d)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(sorted[i]);
            }

            int lowerCount = hull.Count + 1;

            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lowerCount
                    && Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0d)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(sorted[i]);
            }

            // The last point repeats the first.
            if (hull.Count > 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            return hull.Count >= 3 ? hull : result;
        }

        /// <summary>
        /// Reduces a ring to at most <paramref name="maximum"/> points.
        /// </summary>
        /// <remarks>
        /// Every response crosses the bridge as one JSON document with no streaming, so an
        /// unbounded ring from a curved wall or a detailed railing is a transport problem, not just
        /// a rendering one. Points are dropped at a uniform stride, which keeps the overall shape
        /// and the extremes rather than truncating one end of it.
        /// </remarks>
        /// <param name="ring">The ring to reduce.</param>
        /// <param name="maximum">The largest number of points to keep.</param>
        /// <returns>The reduced ring, or the original when it already fits.</returns>
        internal static List<Point2D> Simplify(IList<Point2D> ring, int maximum)
        {
            List<Point2D> reduced = new List<Point2D>();

            if (ring == null)
            {
                return reduced;
            }

            if (maximum < 4 || ring.Count <= maximum)
            {
                reduced.AddRange(ring);
                return reduced;
            }

            double stride = ring.Count / (double)maximum;

            for (double position = 0d; position < ring.Count; position += stride)
            {
                reduced.Add(ring[(int)position]);
            }

            return RemoveConsecutiveDuplicates(reduced);
        }

        /// <summary>Decides whether a ring encloses too little plan area to be a polygon.</summary>
        /// <remarks>
        /// The same test <see cref="ToPolygonWkt"/> applies, exposed so a caller can ask before
        /// committing to a ring rather than discovering it after every alternative has been thrown
        /// away. The two must not drift, so <see cref="ToPolygonWkt"/> calls this rather than
        /// repeating it.
        /// </remarks>
        /// <param name="ring">The ring to test.</param>
        /// <returns><see langword="true"/> when the ring cannot become a polygon.</returns>
        internal static bool IsDegenerate(IList<Point2D> ring)
        {
            List<Point2D> cleaned = RemoveConsecutiveDuplicates(ring);

            return cleaned.Count < 3 || Area(cleaned) <= Tolerance;
        }

        /// <summary>
        /// Writes a ring as an OGC polygon in well-known text.
        /// </summary>
        /// <remarks>
        /// The ring is closed and wound counter-clockwise here, at the last possible moment. Both
        /// are required of a valid exterior ring, and a polygon that fails either is read on the
        /// far side as a geometry whose area is negative or whose boundary does not meet.
        /// </remarks>
        /// <param name="ring">The ring, open or closed, in any winding.</param>
        /// <returns>The polygon text, or null when the ring is degenerate.</returns>
        internal static string ToPolygonWkt(IList<Point2D> ring)
        {
            if (IsDegenerate(ring))
            {
                return null;
            }

            List<Point2D> cleaned = RemoveConsecutiveDuplicates(ring);

            if (TwiceSignedArea(cleaned) < 0d)
            {
                cleaned.Reverse();
            }

            StringBuilder text = new StringBuilder("POLYGON ((");

            for (int i = 0; i < cleaned.Count; i++)
            {
                Append(text, cleaned[i]);
                text.Append(", ");
            }

            // Close the ring by repeating the first vertex.
            Append(text, cleaned[0]);
            text.Append("))");

            return text.ToString();
        }

        /// <summary>Writes a single point as well-known text.</summary>
        /// <param name="point">The point.</param>
        /// <returns>The point text.</returns>
        internal static string ToPointWkt(Point2D point)
        {
            StringBuilder text = new StringBuilder("POINT (");

            Append(text, point);
            text.Append(')');

            return text.ToString();
        }

        /// <summary>
        /// Orders points along the axis they vary most along.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Mesh vertices and hull points arrive in whatever order the tessellator produced them,
        /// which is fine for a hull - the algorithm sorts anyway - and useless for a line. Joining
        /// a near-collinear set in tessellation order gives a linestring that doubles back on
        /// itself, lands on the right ground, and reports a length several times the truth. A
        /// wrong length is worse than no geometry, because nothing downstream can tell.
        /// </para>
        /// <para>
        /// The axis is the principal component of the set. For two dimensions that closes in one
        /// <c>atan2</c> with no iteration and no matrix: the covariance eigenvector lies at half
        /// the angle of <c>(2*Sxy, Sxx - Syy)</c>. Slight curvature survives, because the points
        /// keep their positions and only their order changes.
        /// </para>
        /// </remarks>
        /// <param name="points">The points to order.</param>
        /// <returns>The points, ordered along their dominant axis.</returns>
        internal static List<Point2D> OrderAlongDominantAxis(IList<Point2D> points)
        {
            List<Point2D> ordered = new List<Point2D>();

            if (points == null)
            {
                return ordered;
            }

            ordered.AddRange(points);

            if (ordered.Count < 3)
            {
                // One order is as good as the other, and reversing a two-point line changes nothing
                // a consumer can observe.
                return ordered;
            }

            double centreX = 0d;
            double centreY = 0d;

            foreach (Point2D point in ordered)
            {
                centreX += point.X;
                centreY += point.Y;
            }

            centreX /= ordered.Count;
            centreY /= ordered.Count;

            double sxx = 0d;
            double syy = 0d;
            double sxy = 0d;

            foreach (Point2D point in ordered)
            {
                double dx = point.X - centreX;
                double dy = point.Y - centreY;

                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            double angle = 0.5d * Math.Atan2(2d * sxy, sxx - syy);
            double axisX = Math.Cos(angle);
            double axisY = Math.Sin(angle);

            ordered.Sort((a, b) =>
            {
                double alongA = ((a.X - centreX) * axisX) + ((a.Y - centreY) * axisY);
                double alongB = ((b.X - centreX) * axisX) + ((b.Y - centreY) * axisY);
                int comparison = alongA.CompareTo(alongB);

                if (comparison != 0)
                {
                    return comparison;
                }

                // Ties resolved on the perpendicular, so the order is total and the output is the
                // same on every run. List.Sort is not stable, so leaving ties unresolved would let
                // identical input produce different well-known text.
                double acrossA = ((a.X - centreX) * -axisY) + ((a.Y - centreY) * axisX);
                double acrossB = ((b.X - centreX) * -axisY) + ((b.Y - centreY) * axisX);

                return acrossA.CompareTo(acrossB);
            });

            return ordered;
        }

        /// <summary>Writes a run of points as a well-known text line string.</summary>
        /// <param name="points">The points.</param>
        /// <returns>The line text, or null when fewer than two distinct points remain.</returns>
        internal static string ToLineWkt(IList<Point2D> points)
        {
            List<Point2D> cleaned = RemoveConsecutiveDuplicates(points);

            if (cleaned.Count < 2)
            {
                return null;
            }

            StringBuilder text = new StringBuilder("LINESTRING (");

            for (int i = 0; i < cleaned.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                Append(text, cleaned[i]);
            }

            text.Append(')');

            return text.ToString();
        }

        private static void Append(StringBuilder text, Point2D point)
        {
            // Invariant culture and round-trip precision. The far side of the bridge parses this in
            // its own locale, where a comma decimal separator would silently become a coordinate
            // separator and shift every vertex.
            text.Append(point.X.ToString("R", CultureInfo.InvariantCulture));
            text.Append(' ');
            text.Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
        }

        private static bool SamePoint(Point2D a, Point2D b)
        {
            return Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance;
        }

        private static double Cross(Point2D origin, Point2D a, Point2D b)
        {
            return ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));
        }
    }
}
