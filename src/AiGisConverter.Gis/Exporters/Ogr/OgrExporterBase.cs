// GDAL BOUNDARY FILE (4 of 4). See Gdal/GdalEnvironment.cs.

using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO;
using OSGeo.OGR;
using OSGeo.OSR;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using OgrGeometry = OSGeo.OGR.Geometry;

namespace AiGisConverter.Gis.Exporters.Ogr;

/// <summary>
/// Shared implementation for formats written through OGR.
/// </summary>
/// <remarks>
/// <para>
/// Geometry crosses into native code as WKB. It is the only representation both sides agree on
/// byte for byte, and it avoids the precision loss a WKT round trip would introduce on large
/// projected coordinates.
/// </para>
/// <para>
/// Writes are wrapped in a transaction and committed on the configured interval. For GeoPackage,
/// which is SQLite underneath, an untransacted million-row insert is roughly two orders of
/// magnitude slower because each row is its own commit.
/// </para>
/// </remarks>
public abstract class OgrExporterBase : IStreamingExporter
{
    private readonly GdalEnvironment _environment;
    private readonly ICrsRegistry _crsRegistry;

    /// <summary>Initializes a new instance of the <see cref="OgrExporterBase"/> class.</summary>
    /// <param name="environment">The native library gate.</param>
    /// <param name="crsRegistry">Supplies system definitions.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the concrete exporter.</param>
    protected OgrExporterBase(
        GdalEnvironment environment,
        ICrsRegistry crsRegistry,
        IOptionsMonitor<GisOptions> options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(crsRegistry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _environment = environment;
        _crsRegistry = crsRegistry;
        Options = options;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string FormatKey { get; }

    /// <inheritdoc />
    public abstract ExportFormat Format { get; }

    /// <inheritdoc />
    public abstract string FileExtension { get; }

    /// <inheritdoc />
    public abstract bool SupportsMultipleLayers { get; }

    /// <summary>Gets the OGR driver name, for example <c>GPKG</c>.</summary>
    protected abstract string DriverName { get; }

    /// <summary>Gets the live GIS settings.</summary>
    protected IOptionsMonitor<GisOptions> Options { get; }

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger { get; }

    /// <summary>Gets the data-source creation options passed to the driver.</summary>
    /// <returns>Driver options, in <c>KEY=VALUE</c> form.</returns>
    protected virtual string[] GetDataSourceOptions() => [];

    /// <summary>Gets the layer creation options passed to the driver.</summary>
    /// <param name="request">The export request.</param>
    /// <returns>Layer options, in <c>KEY=VALUE</c> form.</returns>
    protected virtual string[] GetLayerOptions(ExportRequest request) => [];

    /// <summary>Lists every file the format produces alongside the primary one.</summary>
    /// <param name="primaryPath">The main output file.</param>
    /// <returns>The paths written.</returns>
    protected virtual IReadOnlyList<string> GetWrittenPaths(string primaryPath) => [primaryPath];

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> WriteAsync(
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);

        if (!_environment.Ensure())
        {
            return Result.Failure<IReadOnlyList<string>>(new Error(
                "Export.GdalUnavailable",
                $"{FormatKey} export needs the native GDAL libraries. {_environment.FailureReason}"));
        }

        string path = Path.HasExtension(request.OutputPath) ? request.OutputPath : request.OutputPath + FileExtension;
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return await WriteThroughOgrAsync(path, request, features, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CleanUp(path);
            throw;
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException or IOException)
        {
            CleanUp(path);

            return Result.Failure<IReadOnlyList<string>>(new Error(
                "Export.OgrFailure",
                $"The {DriverName} driver failed: {ex.Message}"));
        }
    }

    private async Task<Result<IReadOnlyList<string>>> WriteThroughOgrAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        Driver? driver = OSGeo.OGR.Ogr.GetDriverByName(DriverName);

        if (driver is null)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error(
                "Export.DriverMissing",
                $"The GDAL build in use has no '{DriverName}' driver."));
        }

        // A stale file left over from an earlier run would be appended to rather than replaced.
        foreach (string existing in GetWrittenPaths(path).Where(File.Exists))
        {
            File.Delete(existing);
        }

        using DataSource dataSource = driver.CreateDataSource(path, GetDataSourceOptions());
        using SpatialReference reference = BuildReference(request.CoordinateSystem);

        wkbGeometryType geometryType = MapGeometryType(request.FeatureClass.Geometry);

        Layer layer = dataSource.CreateLayer(
            request.FeatureClass.Name,
            reference,
            geometryType,
            GetLayerOptions(request));

        CreateFields(layer, request);

        WKBWriter wkbWriter = new(ByteOrder.LittleEndian, handleSRID: false);
        StreamingOptions streaming = Options.CurrentValue.Streaming;

        long written = 0;
        layer.StartTransaction();

        await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryWriteFeature(layer, feature, request, wkbWriter))
            {
                request.Context.CountSkipped();
                continue;
            }

            written++;

            if (written % streaming.FlushInterval == 0)
            {
                layer.CommitTransaction();
                layer.StartTransaction();
            }

            if (progress is not null && written % streaming.ProgressInterval == 0)
            {
                progress.Report(new ExportProgress(written, $"Written {written:N0} features..."));
            }
        }

        layer.CommitTransaction();
        dataSource.FlushCache();

        Logger.LogInformation(
            "Wrote {FeatureCount} features to {Path} via the {Driver} driver in {Crs}.",
            written,
            path,
            DriverName,
            request.CoordinateSystem.Identifier);

        progress?.Report(new ExportProgress(written, $"Wrote {written:N0} features."));

        return Result.Success(GetWrittenPaths(path).Where(File.Exists).ToList() is { Count: > 0 } produced
            ? produced
            : (IReadOnlyList<string>)[path]);
    }

    private bool TryWriteFeature(Layer layer, GisFeature feature, ExportRequest request, WKBWriter wkbWriter)
    {
        using FeatureDefn definition = layer.GetLayerDefn();
        using Feature ogrFeature = new(definition);

        foreach (FieldDefinition field in request.Schema.Fields)
        {
            SetField(ogrFeature, field, feature.GetAttribute(field.Name));
        }

        if (feature.Geometry is not null && !feature.Geometry.IsEmpty)
        {
            OgrGeometry? geometry = ToOgr(feature.Geometry, wkbWriter);

            if (geometry is null)
            {
                request.Context.Record(ValidationIssue.Create(
                    IssueSeverity.Error,
                    IssueCategory.Export,
                    "Export.GeometryRejected",
                    $"The {DriverName} driver could not accept the geometry.").ForFeature(feature.Id));

                return false;
            }

            ogrFeature.SetGeometryDirectly(geometry);
        }

        return layer.CreateFeature(ogrFeature) == 0;
    }

    private static OgrGeometry? ToOgr(NtsGeometry geometry, WKBWriter writer)
    {
        try
        {
            return OgrGeometry.CreateFromWkb(writer.Write(geometry));
        }
        catch (Exception ex) when (ex is ApplicationException or ArgumentException)
        {
            return null;
        }
    }

    private static void CreateFields(Layer layer, ExportRequest request)
    {
        foreach (FieldDefinition field in request.Schema.Fields)
        {
            using FieldDefn definition = new(field.Name, MapFieldType(field.DataType));

            if (field.DataType == AttributeDataType.Text && field.MaxLength is > 0 and <= 254)
            {
                definition.SetWidth(field.MaxLength.Value);
            }

            layer.CreateField(definition, approx_ok: 1);
        }
    }

    private static void SetField(Feature feature, FieldDefinition field, AttributeValue value)
    {
        if (value.IsNull)
        {
            feature.SetFieldNull(field.Name);
            return;
        }

        switch (value.RawValue)
        {
            case int number:
                feature.SetField(field.Name, number);
                break;
            case long number:
                feature.SetField(field.Name, number);
                break;
            case double number:
                feature.SetField(field.Name, number);
                break;
            case bool flag:
                feature.SetField(field.Name, flag ? 1 : 0);
                break;
            default:
                feature.SetField(field.Name, value.ToInvariantString());
                break;
        }
    }

    private static FieldType MapFieldType(AttributeDataType type) => type switch
    {
        AttributeDataType.Integer => FieldType.OFTInteger,
        AttributeDataType.Long => FieldType.OFTInteger64,
        AttributeDataType.Double => FieldType.OFTReal,
        AttributeDataType.Boolean => FieldType.OFTInteger,
        AttributeDataType.DateTime => FieldType.OFTDateTime,
        _ => FieldType.OFTString,
    };

    private static wkbGeometryType MapGeometryType(GeometryKind kind) => kind switch
    {
        GeometryKind.Point => wkbGeometryType.wkbPoint,
        GeometryKind.Line => wkbGeometryType.wkbLineString,
        GeometryKind.Polygon => wkbGeometryType.wkbPolygon,
        GeometryKind.Annotation => wkbGeometryType.wkbPoint,
        _ => wkbGeometryType.wkbUnknown,
    };

    private SpatialReference BuildReference(CoordinateSystem coordinateSystem)
    {
        SpatialReference reference = new(string.Empty);
        Result<string> wkt = _crsRegistry.GetWellKnownText(coordinateSystem);

        if (wkt.IsSuccess)
        {
            string definition = wkt.Value;
            reference.ImportFromWkt(ref definition);
        }
        else
        {
            reference.SetFromUserInput(coordinateSystem.Identifier);
        }

        if (!Options.CurrentValue.Crs.UseAuthorityAxisOrder)
        {
            reference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        }

        return reference;
    }

    private void CleanUp(string primaryPath)
    {
        foreach (string path in GetWrittenPaths(primaryPath))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Could not remove the partial output at {Path}.", path);
            }
        }
    }
}
