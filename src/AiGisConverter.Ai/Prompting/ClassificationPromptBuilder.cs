using System.Globalization;
using System.Text;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Prompting;

/// <summary>
/// Default <see cref="IChatPromptBuilder"/>. Emits a strict, schema-constrained instruction that
/// asks for JSON only, which is what makes the response parseable across vendors.
/// </summary>
public sealed class ClassificationPromptBuilder : IChatPromptBuilder
{
    private readonly ISubjectDescriptor _descriptor;

    /// <summary>Initializes a new instance of the <see cref="ClassificationPromptBuilder"/> class.</summary>
    /// <param name="descriptor">Renders each subject into the prompt.</param>
    public ClassificationPromptBuilder(ISubjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
    }

    /// <inheritdoc />
    public ChatPrompt Build(AIClassificationRequest request, IReadOnlyList<ClassificationSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subjects);

        return new ChatPrompt(BuildSystemMessage(request.Context), BuildUserMessage(request.Context, subjects));
    }

    private static string BuildSystemMessage(ClassificationContext context)
    {
        StringBuilder builder = new(768);

        builder.AppendLine("You are a GIS data engineer classifying CAD layers into GIS feature classes.");
        builder.AppendLine("You will receive a list of CAD layers, each with its name, dominant geometry type,");
        builder.AppendLine("entity count, referenced block names and sample text values.");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine("1. Assign each layer exactly one label from the ALLOWED LABELS list. Never invent a label.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"2. If no label fits, use \"{context.UnknownLabel}\".");
        builder.AppendLine("3. Respect geometry. Do not assign a polygon-only label to a point layer.");
        builder.AppendLine("4. Layer names often use abbreviations and prefixes. Interpret them, do not match literally.");
        builder.AppendLine("5. confidence is your calibrated probability in [0,1]. Be honest: low confidence is useful.");
        builder.AppendLine("6. Reply with JSON only. No markdown, no code fence, no commentary.");
        builder.AppendLine();
        builder.AppendLine("Response schema:");
        builder.AppendLine("{\"results\":[{\"id\":\"<subject id>\",\"label\":\"<allowed label>\",");
        builder.AppendLine(" \"confidence\":<number 0..1>,\"rationale\":\"<one short sentence>\",");
        builder.AppendLine(" \"alternatives\":[{\"label\":\"<allowed label>\",\"confidence\":<number 0..1>}]}]}");
        builder.AppendLine();
        builder.Append("Return one entry for every id you are given, in the same order.");

        return builder.ToString();
    }

    private string BuildUserMessage(ClassificationContext context, IReadOnlyList<ClassificationSubject> subjects)
    {
        StringBuilder builder = new(1024);

        if (!string.IsNullOrWhiteSpace(context.DomainHint))
        {
            builder.Append("PROJECT DOMAIN: ").AppendLine(context.DomainHint);
        }

        if (!string.IsNullOrWhiteSpace(context.DrawingUnits))
        {
            builder.Append("DRAWING UNITS: ").AppendLine(context.DrawingUnits);
        }

        builder.AppendLine("ALLOWED LABELS:");

        foreach (string label in context.CandidateLabels)
        {
            builder.Append("- ").AppendLine(label);
        }

        builder.AppendLine();
        builder.AppendLine("LAYERS TO CLASSIFY:");

        foreach (ClassificationSubject subject in subjects)
        {
            builder.Append("- id=").Append(subject.Id).Append(" | ").AppendLine(_descriptor.Describe(subject));
        }

        return builder.ToString();
    }
}
