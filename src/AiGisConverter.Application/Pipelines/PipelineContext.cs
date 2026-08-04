using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Application.Pipelines;

/// <summary>
/// The state of one conversion as it passes through the pipeline.
/// </summary>
/// <remarks>
/// A mutable bag, deliberately. Each stage reads what earlier stages produced and adds its own
/// result; threading that through return values would mean a tuple that grows with every stage and
/// a signature nobody can add to without touching all of them.
/// </remarks>
public sealed class PipelineContext
{
    private readonly List<string> _outputPaths = [];
    private readonly Dictionary<string, ClassificationResult> _entityClassifications =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="PipelineContext"/> class.</summary>
    /// <param name="source">The drawing being converted.</param>
    /// <param name="settings">The settings in force.</param>
    /// <param name="run">The run recording this conversion.</param>
    /// <param name="outputDirectory">Where the outputs are written.</param>
    public PipelineContext(
        SourceReference source,
        ConversionSettings settings,
        ConversionRun run,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Source = source;
        Settings = settings;
        Run = run;
        OutputDirectory = outputDirectory;
    }

    /// <summary>Gets the drawing being converted.</summary>
    public SourceReference Source { get; }

    /// <summary>Gets the settings in force.</summary>
    public ConversionSettings Settings { get; }

    /// <summary>Gets the run recording this conversion.</summary>
    public ConversionRun Run { get; }

    /// <summary>Gets the folder the outputs are written into.</summary>
    public string OutputDirectory { get; }

    /// <summary>Gets or sets the conversion profile to apply, or null for the configured default.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Gets or sets the document read from the source.</summary>
    public SourceDocument? Document { get; set; }

    /// <summary>Gets or sets the coordinate system the source is in.</summary>
    public CoordinateSystem? SourceCoordinateSystem { get; set; }

    /// <summary>Gets or sets how the source coordinate system was determined.</summary>
    public CrsDetectionSource CrsSource { get; set; } = CrsDetectionSource.None;

    /// <summary>Gets the accepted feature class assignments, keyed by entity ID.</summary>
    public IReadOnlyDictionary<string, ClassificationResult> EntityClassifications => _entityClassifications;

    /// <summary>Gets or sets the converted datasets.</summary>
    public IReadOnlyList<GisDataset> Datasets { get; set; } = [];

    /// <summary>Gets or sets the validation report.</summary>
    public ValidationReport? Report { get; set; }

    /// <summary>Gets the files written.</summary>
    public IReadOnlyList<string> OutputPaths => _outputPaths;

    /// <summary>Gets the stages that failed but were allowed to.</summary>
    public IList<string> DegradedStages { get; } = [];

    /// <summary>Records the classification assigned to a source element.</summary>
    /// <param name="elementId">The source element ID.</param>
    /// <param name="classification">The classification result.</param>
    public void AssignClass(string elementId, AiGisConverter.Domain.Entities.Ai.ClassificationResult classification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentNullException.ThrowIfNull(classification);

        _entityClassifications[elementId] = classification;
    }

    /// <summary>Records files written by a stage.</summary>
    /// <param name="paths">The paths written.</param>
    public void RecordOutputs(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _outputPaths.AddRange(paths);
    }
}
