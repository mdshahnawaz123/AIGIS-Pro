using System;
using System.Collections.Generic;
using System.Globalization;

namespace AiGisConverter.Addin.Revit.Tests
{
    /// <summary>
    /// Tests for the add-in's plan-space geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers here are not invented. The last production export lost 393 elements, every one
    /// of them a single-face DirectShape whose plan projection was a sliver: ring of three to seven
    /// points, area between zero and 7.4e-08 m2, against a polygon tolerance of 1e-07. The
    /// degenerate ring in these tests is built to sit in that band.
    /// </para>
    /// <para>
    /// <c>RevitGeometryExtractor</c> cannot be tested here - it binds to the Revit API. What can be
    /// tested is every decision it delegates, which is all of the arithmetic and all of the
    /// geometry. <see cref="BuildFallback"/> composes those calls in the same order the extractor
    /// does, so a change to that order will not be caught here; a change to any of the parts will.
    /// </para>
    /// </remarks>
    public sealed class FootprintTests
    {
        /// <summary>A plate half a metre long and eight nanometres wide: 4e-09 m2 in plan.</summary>
        private static readonly Footprint.Point2D[] DegenerateRing =
        {
            new Footprint.Point2D(0d, 0d),
            new Footprint.Point2D(0.5d, 0d),
            new Footprint.Point2D(0.5d, 8e-9d),
            new Footprint.Point2D(0d, 8e-9d),
        };

        /// <summary>An ordinary two by three metre footprint.</summary>
        private static readonly Footprint.Point2D[] NormalRing =
        {
            new Footprint.Point2D(0d, 0d),
            new Footprint.Point2D(2d, 0d),
            new Footprint.Point2D(2d, 3d),
            new Footprint.Point2D(0d, 3d),
        };

        // ---------------------------------------------------------------- degenerate -> line

        [Fact]
        public void ADegenerateRingIsRecognisedAsSuch()
        {
            Footprint.Area(DegenerateRing).Should().BeApproximately(4e-9d, 1e-12d);
            Footprint.IsDegenerate(DegenerateRing).Should().BeTrue();
        }

        [Fact]
        public void ADegenerateRingProducesNoPolygon()
        {
            Footprint.ToPolygonWkt(DegenerateRing).Should().BeNull();
        }

        [Fact]
        public void ADegenerateRingHasEnoughPointsToPassAPointCountGuard()
        {
            // The defect in one line. The old guard asked "fewer than three points?", the ring had
            // four, so the linestring fallback was skipped and the element was dropped.
            DegenerateRing.Length.Should().BeGreaterThanOrEqualTo(3);
        }

        [Fact]
        public void ADegenerateRingFallsBackToALineString()
        {
            string wkt = BuildFallback(DegenerateRing);

            wkt.Should().StartWith("LINESTRING (");
        }

        [Fact]
        public void TheFallbackLineStringSpansTheRealExtent()
        {
            // The point of ordering. The plate is 0.5 m long, so the linestring must be too.
            Length(BuildFallback(DegenerateRing)).Should().BeApproximately(0.5d, 1e-6d);
        }

        // ---------------------------------------------------------------- ordering

        [Fact]
        public void UnorderedMeshVerticesProduceAnOrderedLineString()
        {
            // Eight vertices of a sliver, shuffled the way a tessellator hands them out.
            List<Footprint.Point2D> scrambled = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(0.3d, 0d),
                new Footprint.Point2D(0d, 8e-9d),
                new Footprint.Point2D(0.5d, 8e-9d),
                new Footprint.Point2D(0.1d, 0d),
                new Footprint.Point2D(0.5d, 0d),
                new Footprint.Point2D(0.3d, 8e-9d),
                new Footprint.Point2D(0d, 0d),
                new Footprint.Point2D(0.1d, 8e-9d),
            };

            List<Footprint.Point2D> ordered = Footprint.OrderAlongDominantAxis(scrambled);

            for (int i = 1; i < ordered.Count; i++)
            {
                ordered[i].X.Should().BeGreaterThanOrEqualTo(ordered[i - 1].X);
            }
        }

        [Fact]
        public void OrderingCollapsesTheOverstatedLength()
        {
            List<Footprint.Point2D> scrambled = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(0d, 0d),
                new Footprint.Point2D(0.5d, 0d),
                new Footprint.Point2D(0.1d, 0d),
                new Footprint.Point2D(0.4d, 0d),
                new Footprint.Point2D(0.2d, 0d),
            };

            double unordered = Length(Footprint.ToLineWkt(scrambled));
            double ordered = Length(Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(scrambled)));

            // Joining them as they arrive walks the plate back and forth for 1.4 m of a 0.5 m
            // element. This is the silently wrong attribute the ordering exists to prevent.
            unordered.Should().BeApproximately(1.4d, 1e-9d);
            ordered.Should().BeApproximately(0.5d, 1e-9d);
        }

        [Fact]
        public void OrderingKeepsEveryPoint()
        {
            List<Footprint.Point2D> input = new List<Footprint.Point2D>(DegenerateRing);

            Footprint.OrderAlongDominantAxis(input).Should().HaveCount(input.Count);
        }

        [Fact]
        public void OrderingFindsAVerticalAxis()
        {
            List<Footprint.Point2D> column = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(0d, 2d),
                new Footprint.Point2D(0d, 0d),
                new Footprint.Point2D(0d, 3d),
                new Footprint.Point2D(0d, 1d),
            };

            List<Footprint.Point2D> ordered = Footprint.OrderAlongDominantAxis(column);

            for (int i = 1; i < ordered.Count; i++)
            {
                ordered[i].Y.Should().BeGreaterThanOrEqualTo(ordered[i - 1].Y);
            }
        }

        [Fact]
        public void OrderingFindsADiagonalAxis()
        {
            List<Footprint.Point2D> diagonal = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(2d, 2d),
                new Footprint.Point2D(0d, 0d),
                new Footprint.Point2D(3d, 3d),
                new Footprint.Point2D(1d, 1d),
            };

            Length(Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(diagonal)))
                .Should().BeApproximately(3d * Math.Sqrt(2d), 1e-9d);
        }

        [Fact]
        public void OrderingIsDeterministic()
        {
            // List.Sort is not stable, so ties have to be broken explicitly or the same input can
            // produce different well-known text on different runs.
            List<Footprint.Point2D> square = new List<Footprint.Point2D>(NormalRing);

            string first = Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(square));
            string second = Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(square));

            second.Should().Be(first);
        }

        [Fact]
        public void OrderingToleratesTrivialInput()
        {
            Footprint.OrderAlongDominantAxis(null).Should().BeEmpty();
            Footprint.OrderAlongDominantAxis(new List<Footprint.Point2D>()).Should().BeEmpty();
            Footprint.OrderAlongDominantAxis(
                new List<Footprint.Point2D> { new Footprint.Point2D(1d, 1d) }).Should().HaveCount(1);
        }

        [Fact]
        public void OrderingToleratesCoincidentPoints()
        {
            List<Footprint.Point2D> same = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(1d, 1d),
                new Footprint.Point2D(1d, 1d),
                new Footprint.Point2D(1d, 1d),
            };

            Footprint.OrderAlongDominantAxis(same).Should().HaveCount(3);
        }

        // ---------------------------------------------------------------- polygon unchanged

        [Fact]
        public void ANormalRingStillProducesAPolygon()
        {
            string wkt = Footprint.ToPolygonWkt(NormalRing);

            wkt.Should().StartWith("POLYGON ((");
            Footprint.IsDegenerate(NormalRing).Should().BeFalse();
        }

        [Fact]
        public void ANormalRingIsUnaffectedByTheFallback()
        {
            // The guard must not divert anything that was already working.
            BuildFallback(NormalRing).Should().Be(Footprint.ToPolygonWkt(NormalRing));
        }

        [Fact]
        public void ANormalRingKeepsItsAreaAndIsClosed()
        {
            Footprint.Area(NormalRing).Should().BeApproximately(6d, 1e-12d);

            string wkt = Footprint.ToPolygonWkt(NormalRing);
            List<Footprint.Point2D> points = Parse(wkt);

            points.Should().HaveCount(NormalRing.Length + 1);
            points[points.Count - 1].X.Should().Be(points[0].X);
            points[points.Count - 1].Y.Should().Be(points[0].Y);
        }

        [Fact]
        public void ANormalRingIsWoundCounterClockwise()
        {
            List<Footprint.Point2D> clockwise = new List<Footprint.Point2D>
            {
                new Footprint.Point2D(0d, 0d),
                new Footprint.Point2D(0d, 3d),
                new Footprint.Point2D(2d, 3d),
                new Footprint.Point2D(2d, 0d),
            };

            Footprint.TwiceSignedArea(Parse(Footprint.ToPolygonWkt(clockwise)))
                .Should().BeGreaterThan(0d);
        }

        // ---------------------------------------------------------------- units

        [Fact]
        public void FeetConvertToMetresExactly()
        {
            Footprint.MetresPerFoot.Should().Be(0.3048d);

            double sideInMetres = 5d * Footprint.MetresPerFoot;

            (sideInMetres * sideInMetres).Should().BeApproximately(2.322576d, 1e-12d);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Composes the calls the extractor makes once it has a candidate ring.</summary>
        private static string BuildFallback(IList<Footprint.Point2D> ring)
        {
            if (ring == null || Footprint.IsDegenerate(ring))
            {
                return Footprint.ToLineWkt(Footprint.OrderAlongDominantAxis(ring));
            }

            return Footprint.ToPolygonWkt(ring);
        }

        private static List<Footprint.Point2D> Parse(string wkt)
        {
            int open = wkt.IndexOf('(');
            int close = wkt.LastIndexOf(')');
            string body = wkt.Substring(open, close - open).Trim('(', ')', ' ');

            List<Footprint.Point2D> points = new List<Footprint.Point2D>();

            foreach (string pair in body.Split(','))
            {
                string[] parts = pair.Trim().Split(' ');

                points.Add(new Footprint.Point2D(
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture)));
            }

            return points;
        }

        private static double Length(string wkt)
        {
            List<Footprint.Point2D> points = Parse(wkt);
            double total = 0d;

            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;

                total += Math.Sqrt((dx * dx) + (dy * dy));
            }

            return total;
        }
    }
}
