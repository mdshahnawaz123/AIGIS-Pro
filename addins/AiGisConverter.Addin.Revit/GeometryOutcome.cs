namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Why an element produced the geometry it did - including none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A footprint that fails to build is not an error to be logged and forgotten. It is a fact
    /// about one element, and the only way to act on four hundred of them is to know which stage
    /// each one stopped at and why. So the reason travels with the element, into the export, where
    /// it can be grouped and counted with the same tools that found the problem.
    /// </para>
    /// <para>
    /// Deliberately strings rather than an enum: they cross the bridge as attribute values, and an
    /// enum would only have to be flattened into these on the way.
    /// </para>
    /// </remarks>
    internal struct GeometryOutcome
    {
        /// <summary>The geometry kind produced, matching the domain's GeometryKind names.</summary>
        internal string Kind;

        /// <summary>The stage that ended the attempt, or null when geometry was produced.</summary>
        internal string Stage;

        /// <summary>Why the attempt ended, or null when geometry was produced.</summary>
        internal string Reason;

        /// <summary>Measurements that make the reason actionable - counts, areas, object kinds.</summary>
        internal string Detail;

        /// <summary>A non-fatal note about geometry that was produced anyway.</summary>
        internal string Warning;

        /// <summary>Records the stage and reason an attempt ended.</summary>
        /// <param name="stage">The stage reached.</param>
        /// <param name="reason">The reason it stopped.</param>
        /// <param name="detail">Supporting measurements.</param>
        internal void Fail(string stage, string reason, string detail)
        {
            Stage = stage;
            Reason = reason;
            Detail = detail;
        }
    }

    /// <summary>The pipeline stages a footprint attempt can stop at.</summary>
    internal static class GeometryStage
    {
        /// <summary>Opening the element's geometry through the Revit API.</summary>
        internal const string Extractor = "GeometryExtractor";

        /// <summary>Turning collected vertices into a ring.</summary>
        internal const string FootprintBuilder = "FootprintBuilder";
    }

    /// <summary>The reasons a footprint attempt can stop.</summary>
    internal static class GeometryReason
    {
        /// <summary>get_Geometry threw.</summary>
        internal const string OpenFailed = "GeometryOpenThrew";

        /// <summary>get_Geometry returned null: the element carries no geometry at all.</summary>
        internal const string NoGeometryElement = "NoGeometryElement";

        /// <summary>The geometry was walked but yielded no usable vertex.</summary>
        internal const string NoVertices = "NoVerticesCollected";

        /// <summary>A ring was found, but it encloses no area in plan.</summary>
        internal const string DegenerateRing = "RingHasNoPlanArea";
    }
}
