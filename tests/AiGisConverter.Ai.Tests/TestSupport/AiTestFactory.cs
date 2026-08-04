using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Tests.TestSupport;

/// <summary>Builds the objects the AI tests need.</summary>
internal static class AiTestFactory
{
    public const string HighConfidenceLabel = "Water Main";
    public const string LowConfidenceLabel = "Stormwater Pipe";

    public static ClassificationSubject Subject(string id = "L1", string name = "C-WTR-MAIN", int entities = 40)
    {
        ClassificationSubject subject = new(id, name);
        subject.SetEntityCount(entities);
        subject.AddGeometry(GeometryKind.Line, entities);
        subject.AddSampleText("DN150");
        subject.AddBlockName("VALVE");
        subject.SetMetadata("Linetype", "DASHED");

        return subject;
    }

    public static IReadOnlyList<ClassificationSubject> Subjects(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Subject($"L{i}", $"C-LAYER-{i}"))];

    public static ClassificationContext Context(params string[] labels) =>
        new(labels.Length > 0 ? labels : [HighConfidenceLabel, LowConfidenceLabel, "Unclassified"])
        {
            DomainHint = "utility network",
        };

    public static ClassificationResult Result(
        string subjectId,
        double confidence,
        string label = HighConfidenceLabel,
        string providerKey = "fake")
    {
        ClassificationResult result = new(subjectId, label, Confidence.Clamp(confidence), providerKey)
        {
            Rationale = "test",
        };

        result.AddAlternative(new ClassificationCandidate(LowConfidenceLabel, Confidence.Clamp(0.2d)));

        return result;
    }

    public static AIClassificationResponse Response(params ClassificationResult[] results) =>
        new(results, "fake", "fake-model-1", AIUsage.Empty);

    public static AIClassificationRequest Request(IReadOnlyList<ClassificationSubject>? subjects = null) =>
        new(subjects ?? [Subject()], Context());

    public static IOptionsMonitor<AiOptions> Options(Action<AiOptions>? configure = null)
    {
        AiOptions options = new();
        configure?.Invoke(options);

        IOptionsMonitor<AiOptions> monitor = Substitute.For<IOptionsMonitor<AiOptions>>();
        monitor.CurrentValue.Returns(options);

        return monitor;
    }
}
