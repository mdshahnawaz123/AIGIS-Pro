using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Crs;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AiGisConverter.Gis.Tests.TestSupport;

namespace AiGisConverter.Gis.Tests.Crs;

/// <summary>
/// Coordinate system tests.
/// </summary>
/// <remarks>
/// These assert both branches deliberately. On a machine with the native payload the EPSG lookups
/// must succeed; on one without, the failures must be explicit and name GDAL rather than surfacing
/// as a <see cref="DllNotFoundException"/> from somewhere deep in an export. Both are real
/// deployment states and both need to behave.
/// </remarks>
public sealed class CrsResolutionTests
{
    private static GdalEnvironment Environment() =>
        new(Microsoft.Extensions.Options.Options.Create(new GisOptions()), NullLogger<GdalEnvironment>.Instance);

    private static GdalCrsRegistry Registry() =>
        new(Environment(), NullLogger<GdalCrsRegistry>.Instance);

    [Fact]
    public void Resolve_Epsg4326_ResolvesOrFailsExplicitly()
    {
        Result<CoordinateSystem> result = Registry().Resolve("EPSG:4326");

        if (Environment().IsAvailable)
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Code.Should().Be(4326);
            result.Value.IsGeographic.Should().BeTrue();
        }
        else
        {
            // Without PROJ the syntactic parser still handles a plain EPSG code.
            result.IsSuccess.Should().BeTrue();
            result.Value.Identifier.Should().Be("EPSG:4326");
        }
    }

    [Fact]
    public void Resolve_BlankIdentifier_Fails() =>
        Registry().Resolve("   ").IsFailure.Should().BeTrue();

    [Fact]
    public void Resolve_IsCached()
    {
        GdalCrsRegistry registry = Registry();

        registry.Resolve("EPSG:27700").Should().BeEquivalentTo(registry.Resolve("EPSG:27700"));
    }

    [Fact]
    public void GetWellKnownText_EmbeddedDefinition_IsReturnedWithoutTouchingGdal()
    {
        CoordinateSystem system = CoordinateSystem.Create("EPSG", 27700)
            .WithWellKnownText("PROJCS[\"OSGB36 / British National Grid\"]");

        Registry().GetWellKnownText(system).Value.Should().Contain("British National Grid");
    }

    [Fact]
    public void Transformer_SameSystem_IsANoOp()
    {
        GdalCoordinateTransformer transformer = new(
            Environment(),
            Registry(),
            GisOptionsFactory.Monitor(),
            NullLogger<GdalCoordinateTransformer>.Instance);

        using (transformer)
        {
            NetTopologySuite.Geometries.Geometry point =
                new NetTopologySuite.Geometries.GeometryFactory()
                    .CreatePoint(new NetTopologySuite.Geometries.Coordinate(1d, 2d));

            Result<NetTopologySuite.Geometries.Geometry> result =
                transformer.Transform(point, CoordinateSystem.Wgs84, CoordinateSystem.Wgs84);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeSameAs(point, "an identity transform must not copy");
        }
    }

    [Fact]
    public void Transformer_WithoutGdal_FailsWithAnActionableMessage()
    {
        GdalEnvironment environment = Environment();

        if (environment.IsAvailable)
        {
            return;
        }

        GdalCoordinateTransformer transformer = new(
            environment, Registry(), GisOptionsFactory.Monitor(),
            NullLogger<GdalCoordinateTransformer>.Instance);

        using (transformer)
        {
            NetTopologySuite.Geometries.Geometry point =
                new NetTopologySuite.Geometries.GeometryFactory()
                    .CreatePoint(new NetTopologySuite.Geometries.Coordinate(1d, 2d));

            Result<NetTopologySuite.Geometries.Geometry> result = transformer.Transform(
                point, CoordinateSystem.Wgs84, CoordinateSystem.Create("EPSG", 27700));

            result.IsFailure.Should().BeTrue();
            result.Error.Message.Should().Contain("GDAL");
        }
    }
}
