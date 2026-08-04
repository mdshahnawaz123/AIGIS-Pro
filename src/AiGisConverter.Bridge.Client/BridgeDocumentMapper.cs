using System.Globalization;
using AiGisConverter.Bridge.Protocol;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace AiGisConverter.Bridge.Client;

/// <summary>
/// Converts the flat wire form returned by an add-in into the domain's
/// <see cref="SourceDocument"/>.
/// </summary>
/// <remarks>
/// Kept as a separate, static mapper so it is testable against a captured JSON payload without a
/// running copy of AutoCAD or Revit &#8212; which is the only practical way to regression-test
/// this boundary.
/// </remarks>
public static class BridgeDocumentMapper
{
    /// <summary>Maps a wire document onto a domain document.</summary>
    /// <param name="wire">The wire document.</param>
    /// <param name="reference">The reference that was read.</param>
    /// <returns>The mapped document.</returns>
    public static SourceDocument ToSourceDocument(BridgeDocument wire, SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(reference);

        WKTReader reader = new();

        SourceDocument document = new(reference, wire.FormatKey)
        {
            DeclaredCrs = wire.DeclaredCrs,
            Units = wire.Units,
        };

        foreach (KeyValuePair<string, string> pair in wire.Metadata)
        {
            document.SetMetadata(pair.Key, pair.Value);
        }

        foreach (string warning in wire.Warnings)
        {
            document.AddWarning(warning);
        }

        foreach (BridgeLayer wireLayer in wire.Layers)
        {
            SourceLayer layer = new(wireLayer.Name) { IsVisible = wireLayer.IsVisible };

            foreach (KeyValuePair<string, string> pair in wireLayer.Metadata)
            {
                layer.SetMetadata(pair.Key, pair.Value);
            }

            foreach (BridgeElement wireElement in wireLayer.Elements)
            {
                layer.AddElement(MapElement(wireElement, reader, document));
            }

            document.AddLayer(layer);
        }

        return document;
    }

    private static SourceElement MapElement(BridgeElement wire, WKTReader reader, SourceDocument document)
    {
        GeometryKind kind = Enum.TryParse(wire.GeometryKind, ignoreCase: true, out GeometryKind parsed)
            ? parsed
            : GeometryKind.Unknown;

        SourceElement element = new(wire.Id, kind)
        {
            NativeType = wire.NativeType,
            Text = wire.Text,
        };

        if (!string.IsNullOrWhiteSpace(wire.GeometryWkt))
        {
            try
            {
                element.Geometry = reader.Read(wire.GeometryWkt);
            }
            catch (Exception ex) when (ex is ParseException or FormatException or ArgumentException)
            {
                // One malformed element must not abandon an entire drawing. Record and continue.
                document.AddWarning(string.Format(
                    CultureInfo.InvariantCulture,
                    "Element '{0}' returned unreadable geometry and was imported without it: {1}",
                    wire.Id,
                    ex.Message));
            }
        }

        foreach (KeyValuePair<string, string> pair in wire.Attributes)
        {
            element.SetAttribute(pair.Key, pair.Value);
        }

        return element;
    }
}
