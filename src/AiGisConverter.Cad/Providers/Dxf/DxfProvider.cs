// ---------------------------------------------------------------------------------------------
// netDxf BOUNDARY FILE (2 of 2). See NetDxfEntityConverter.cs.
// ---------------------------------------------------------------------------------------------

using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Cad.Crs;
using AiGisConverter.Cad.Geometry;
using AiGisConverter.Cad.Options;
using AiGisConverter.Cad.Units;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;

namespace AiGisConverter.Cad.Providers.Dxf;

/// <summary>
/// Reads DXF drawings using netDxf. Always available: it needs no licence and no installed CAD
/// application, which is what makes it the provider the application can rely on.
/// </summary>
public sealed class DxfProvider : ICadProvider
{
    /// <summary>The provider key.</summary>
    public const string ProviderKey = "dxf";

    private readonly IOptionsMonitor<CadOptions> _options;
    private readonly ILogger<DxfProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="DxfProvider"/> class.</summary>
    /// <param name="options">Live CAD reading settings.</param>
    /// <param name="logger">Logger for the provider.</param>
    public DxfProvider(IOptionsMonitor<CadOptions> options, ILogger<DxfProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => ProviderKey;

    /// <inheritdoc />
    public string DisplayName => "AutoCAD DXF";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".dxf"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public Task<CadProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CadProviderAvailability.Available("netDxf (managed, no external SDK)"));

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // netDxf parsing is synchronous and CPU-bound; wrapping it keeps the UI thread free
        // without pretending the underlying library is asynchronous.
        return Task.Run(() => Read(reference, progress, cancellationToken), cancellationToken);
    }

    private Result<SourceDocument> Read(
        SourceReference reference,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(reference.Location))
        {
            return Result.Failure<SourceDocument>(new Error(
                "Cad.FileNotFound",
                $"'{reference.Location}' does not exist."));
        }

        CadOptions options = _options.CurrentValue;
        progress?.Report(new ReadProgress(0d, "Opening drawing..."));

        DxfDocument? drawing;

        try
        {
            drawing = DxfDocument.Load(reference.Location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Result.Failure<SourceDocument>(new Error("Cad.ReadFailed", ex.Message));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            // netDxf reports a malformed or unsupported DXF through several exception types.
            return Result.Failure<SourceDocument>(new Error(
                "Cad.MalformedDrawing",
                $"'{Path.GetFileName(reference.Location)}' could not be parsed as DXF: {ex.Message}"));
        }

        if (drawing is null)
        {
            return Result.Failure<SourceDocument>(new Error(
                "Cad.MalformedDrawing",
                $"'{Path.GetFileName(reference.Location)}' is not a DXF file this reader understands."));
        }

        SourceDocument document = new(reference, ProviderKey);

        ApplyHeader(document, drawing, options);
        ApplyCrsSidecar(document, reference, options);

        HashSet<string> readableLayers = ReadableLayers(document, drawing, options);
        NetDxfEntityConverter converter = new(options, document);

        progress?.Report(new ReadProgress(0.1d, "Reading entities..."));

        int emitted = 0;
        int skippedByLayer = 0;

        foreach (EntityObject entity in drawing.Entities.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string layerName = entity.Layer?.Name ?? "0";

            if (!readableLayers.Contains(layerName))
            {
                skippedByLayer++;
                continue;
            }

            foreach (ConvertedElement converted in converter.Convert(entity, BlockTransform.Identity, depth: 0))
            {
                if (converted.Geometry is null && converted.Kind != GeometryKind.Annotation)
                {
                    continue;
                }

                document.GetOrAddLayer(converted.LayerName).AddElement(converted.ToSourceElement());
                emitted++;

                if (options.MaxElements > 0 && emitted >= options.MaxElements)
                {
                    document.AddWarning(
                        $"Reading stopped at the configured limit of {options.MaxElements:N0} elements. " +
                        "Raise 'Cad:MaxElements' to read the whole drawing.");

                    return Complete(document, progress, emitted, skippedByLayer, reference);
                }

                if (emitted % 5000 == 0)
                {
                    progress?.Report(new ReadProgress(null, $"Read {emitted:N0} elements..."));
                }
            }
        }

        return Complete(document, progress, emitted, skippedByLayer, reference);
    }

    private Result<SourceDocument> Complete(
        SourceDocument document,
        IProgress<ReadProgress>? progress,
        int emitted,
        int skippedByLayer,
        SourceReference reference)
    {
        progress?.Report(new ReadProgress(1d, $"Read {emitted:N0} elements."));

        _logger.LogInformation(
            "Read {ElementCount} elements across {LayerCount} layers from {File} " +
            "({SkippedCount} skipped by layer filter, {WarningCount} warnings).",
            emitted,
            document.Layers.Count,
            Path.GetFileName(reference.Location),
            skippedByLayer,
            document.Warnings.Count);

        return Result.Success(document);
    }

    /// <summary>Reads units and identifying metadata from the drawing header.</summary>
    private static void ApplyHeader(SourceDocument document, DxfDocument drawing, CadOptions options)
    {
        LinearUnit units = DrawingUnitsMapper.FromInsUnits((int)drawing.DrawingVariables.InsUnits);

        if (units == LinearUnit.Unknown && options.AssumedUnits != LinearUnit.Unknown)
        {
            units = options.AssumedUnits;
            document.AddWarning(
                $"The drawing does not declare its units; the configured assumption of " +
                $"{DrawingUnitsMapper.DisplayName(units)} was applied.");
        }
        else if (units == LinearUnit.Unknown)
        {
            document.AddWarning(
                "The drawing does not declare its units and no assumption is configured. " +
                "Distances and tolerances cannot be interpreted reliably.");
        }

        document.Units = DrawingUnitsMapper.DisplayName(units);
        document.SetMetadata("DxfVersion", drawing.DrawingVariables.AcadVer.ToString());
        document.SetMetadata("InsUnits", ((int)drawing.DrawingVariables.InsUnits).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Looks for a <c>.prj</c> sidecar and records what it says.</summary>
    private void ApplyCrsSidecar(SourceDocument document, SourceReference reference, CadOptions options)
    {
        if (!options.ReadCrsSidecar || !SidecarCrsReader.TryRead(reference.Location, out SidecarCrs? sidecar))
        {
            return;
        }

        document.DeclaredCrs = sidecar!.AuthorityCode;
        document.SetMetadata("CrsSidecarPath", sidecar.SidecarPath);
        document.SetMetadata("CrsWkt", sidecar.WellKnownText);

        if (sidecar.AuthorityCode is null)
        {
            document.AddWarning(
                $"A projection sidecar was found at '{sidecar.SidecarPath}' but it declares no " +
                "authority code. The definition has been carried forward for the GIS layer to interpret.");
        }

        _logger.LogInformation(
            "Coordinate system {Crs} read from sidecar {Sidecar}.",
            sidecar.AuthorityCode ?? "(WKT only)",
            sidecar.SidecarPath);
    }

    /// <summary>Decides which layers may contribute entities, and records the layer table.</summary>
    private static HashSet<string> ReadableLayers(SourceDocument document, DxfDocument drawing, CadOptions options)
    {
        HashSet<string> readable = new(StringComparer.OrdinalIgnoreCase);

        foreach (Layer layer in drawing.Layers)
        {
            bool hidden = !layer.IsVisible;
            bool frozen = layer.IsFrozen;

            if ((hidden && !options.IncludeInvisibleLayers) || (frozen && !options.IncludeFrozenLayers))
            {
                continue;
            }

            readable.Add(layer.Name);

            SourceLayer sourceLayer = document.GetOrAddLayer(layer.Name);
            sourceLayer.IsVisible = !hidden;
            sourceLayer.SetMetadata("Frozen", frozen ? "true" : "false");
            sourceLayer.SetMetadata("Colour", layer.Color?.ToString() ?? "unspecified");

            if (layer.Linetype?.Name is { Length: > 0 } linetype)
            {
                sourceLayer.SetMetadata("Linetype", linetype);
            }
        }

        return readable;
    }
}
