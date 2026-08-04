using System.Collections.Generic;

namespace AiGisConverter.Bridge.Protocol
{
    /// <summary>
    /// The wire form of a source document. Geometry travels as WKT because it is the one
    /// representation every side of this boundary can produce and parse without agreeing on a
    /// binary layout, and because it survives the .NET Framework to .NET 8 crossing unchanged.
    /// </summary>
    public sealed class BridgeDocument
    {
        /// <summary>Gets or sets the source location that was read.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Gets or sets the reader's format key.</summary>
        public string FormatKey { get; set; } = string.Empty;

        /// <summary>Gets or sets the coordinate reference system declared by the source.</summary>
        public string DeclaredCrs { get; set; }

        /// <summary>Gets or sets the linear units declared by the source.</summary>
        public string Units { get; set; }

        /// <summary>Gets or sets the document metadata.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>Gets or sets non-fatal problems encountered while reading.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Gets or sets the layers read.</summary>
        public List<BridgeLayer> Layers { get; set; } = new List<BridgeLayer>();
    }

    /// <summary>The wire form of a layer.</summary>
    public sealed class BridgeLayer
    {
        /// <summary>Gets or sets the layer name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the layer is visible.</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>Gets or sets the layer metadata.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>Gets or sets the elements on the layer.</summary>
        public List<BridgeElement> Elements { get; set; } = new List<BridgeElement>();
    }

    /// <summary>The wire form of an element.</summary>
    public sealed class BridgeElement
    {
        /// <summary>Gets or sets the identifier, stable within the document.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the geometry family name, matching <c>GeometryKind</c>.</summary>
        public string GeometryKind { get; set; } = "Unknown";

        /// <summary>Gets or sets the geometry as well-known text. Null for pure annotation.</summary>
        public string GeometryWkt { get; set; }

        /// <summary>Gets or sets the source-native type name.</summary>
        public string NativeType { get; set; }

        /// <summary>Gets or sets the text carried by the element.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets the element attributes, rendered as strings for the wire.</summary>
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
    }
}
