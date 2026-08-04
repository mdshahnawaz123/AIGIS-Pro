using AiGisConverter.Gis.Spatial.Analysis;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Spatial;

/// <summary>
/// Checks the geodesic maths against published figures. These values were verified numerically
/// before the implementation was written.
/// </summary>
public sealed class GeodeticCalculatorTests
{
    private static readonly GeometryFactory Factory = new();

    private static Polygon Cell(double lon, double lat, double size = 1d) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(lon, lat),
            new Coordinate(lon + size, lat),
            new Coordinate(lon + size, lat + size),
            new Coordinate(lon, lat + size),
            new Coordinate(lon, lat),
        ]));

    [Fact]
    public void Distance_OneDegreeOfLongitudeAtTheEquator_IsAboutOneHundredAndElevenKilometres() =>
        GeodeticCalculator.Distance(0d, 0d, 1d, 0d)
            .Should().BeApproximately(111_195d, 500d);

    [Fact]
    public void Distance_LondonToParis_MatchesThePublishedFigure() =>
        GeodeticCalculator.Distance(-0.1278d, 51.5074d, 2.3522d, 48.8566d)
            .Should().BeApproximately(343_600d, 2_000d, "the published great-circle distance is about 344 km");

    [Fact]
    public void Distance_ShrinksWithTheCosineOfLatitude()
    {
        double equator = GeodeticCalculator.Distance(0d, 0d, 1d, 0d);
        double sixty = GeodeticCalculator.Distance(0d, 60d, 1d, 60d);

        sixty.Should().BeApproximately(equator * Math.Cos(Math.PI / 3d), 100d);
    }

    [Fact]
    public void Distance_IsSymmetric() =>
        GeodeticCalculator.Distance(10d, 20d, 30d, 40d)
            .Should().BeApproximately(GeodeticCalculator.Distance(30d, 40d, 10d, 20d), 1e-6d);

    [Fact]
    public void Distance_ZeroSeparation_IsZero() =>
        GeodeticCalculator.Distance(5d, 5d, 5d, 5d).Should().BeApproximately(0d, 1e-9d);

    [Fact]
    public void Area_OneDegreeCellAtTheEquator_IsAboutTwelveThousandSquareKilometres() =>
        (GeodeticCalculator.Area(Cell(0d, 0d)) / 1e6d)
            .Should().BeApproximately(12_364d, 100d, "the ellipsoidal figure is 12,308; a sphere gives 12,364");

    [Fact]
    public void Area_CellAtSixtyNorth_IsAboutHalfTheEquatorialCell()
    {
        double equator = GeodeticCalculator.Area(Cell(0d, 0d));
        double sixty = GeodeticCalculator.Area(Cell(0d, 60d));

        (sixty / equator).Should().BeApproximately(0.49d, 0.03d);
    }

    [Fact]
    public void Area_NorthernHemisphere_IsExactlyHalfTheSphere()
    {
        Polygon hemisphere = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(-180d, 0d),
            new Coordinate(-90d, 0d),
            new Coordinate(0d, 0d),
            new Coordinate(90d, 0d),
            new Coordinate(180d, 0d),
            new Coordinate(180d, 90d),
            new Coordinate(-180d, 90d),
            new Coordinate(-180d, 0d),
        ]));

        double sphere = 4d * Math.PI * GeodeticCalculator.EarthRadiusMetres * GeodeticCalculator.EarthRadiusMetres;

        (GeodeticCalculator.Area(hemisphere) / sphere)
            .Should().BeApproximately(0.5d, 0.0001d, "this is the strongest single check on the formula");
    }

    [Fact]
    public void Area_HoleIsSubtracted()
    {
        LinearRing shell = Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(2d, 0d),
            new Coordinate(2d, 2d), new Coordinate(0d, 2d), new Coordinate(0d, 0d),
        ]);

        LinearRing hole = Factory.CreateLinearRing(
        [
            new Coordinate(0.5d, 0.5d), new Coordinate(1.5d, 0.5d),
            new Coordinate(1.5d, 1.5d), new Coordinate(0.5d, 1.5d), new Coordinate(0.5d, 0.5d),
        ]);

        double solid = GeodeticCalculator.Area(Factory.CreatePolygon(shell));
        double withHole = GeodeticCalculator.Area(Factory.CreatePolygon(shell, [hole]));

        withHole.Should().BeLessThan(solid);
        (solid - withHole).Should().BeApproximately(GeodeticCalculator.Area(Factory.CreatePolygon(hole)), 1d);
    }

    [Fact]
    public void Area_Point_IsZero() =>
        GeodeticCalculator.Area(Factory.CreatePoint(new Coordinate(1d, 1d))).Should().Be(0d);

    [Fact]
    public void Length_PolygonPerimeter_IncludesHoles()
    {
        LinearRing shell = Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(1d, 0d),
            new Coordinate(1d, 1d), new Coordinate(0d, 1d), new Coordinate(0d, 0d),
        ]);

        LinearRing hole = Factory.CreateLinearRing(
        [
            new Coordinate(0.25d, 0.25d), new Coordinate(0.75d, 0.25d),
            new Coordinate(0.75d, 0.75d), new Coordinate(0.25d, 0.75d), new Coordinate(0.25d, 0.25d),
        ]);

        GeodeticCalculator.Length(Factory.CreatePolygon(shell, [hole]))
            .Should().BeGreaterThan(GeodeticCalculator.Length(Factory.CreatePolygon(shell)));
    }
}
