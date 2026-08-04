using System.Globalization;
using System.Text;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Ai.Features;

/// <summary>
/// Default <see cref="ISubjectDescriptor"/>. Produces a compact, deterministic, single-line
/// rendering of a subject.
/// </summary>
/// <remarks>
/// Determinism matters twice over: it makes cache keys stable across runs, and it keeps prompts
/// byte-identical so that provider comparisons and regression tests are meaningful.
/// </remarks>
public sealed class SubjectDescriptor : ISubjectDescriptor
{
    private const int MaxSampleTexts = 5;
    private const int MaxSampleTextLength = 40;
    private const int MaxBlockNames = 5;

    /// <inheritdoc />
    public string Describe(ClassificationSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        StringBuilder builder = new(256);
        builder.Append("name=").Append(subject.Name);
        builder.Append("; geometry=").Append(subject.GetDominantGeometry());
        builder.Append("; entities=").Append(subject.EntityCount.ToString(CultureInfo.InvariantCulture));

        if (subject.GeometryProfile.Count > 1)
        {
            builder.Append("; mix=");
            bool first = true;

            foreach (KeyValuePair<GeometryKind, int> pair in subject.GeometryProfile.OrderBy(static p => p.Key))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(pair.Key).Append(':').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
        }

        AppendList(builder, "blocks", subject.BlockNames, MaxBlockNames, int.MaxValue);
        AppendList(builder, "text", subject.SampleTexts, MaxSampleTexts, MaxSampleTextLength);

        foreach (KeyValuePair<string, string> pair in subject.Metadata.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append("; ").Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }

    private static void AppendList(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> values,
        int maxItems,
        int maxLength)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.Append("; ").Append(label).Append("=[");

        for (int i = 0; i < Math.Min(maxItems, values.Count); i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            string value = values[i].Replace('\n', ' ').Replace('\r', ' ').Trim();
            builder.Append(value.Length > maxLength ? value[..maxLength] : value);
        }

        if (values.Count > maxItems)
        {
            builder.Append("|+").Append((values.Count - maxItems).ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(']');
    }
}
