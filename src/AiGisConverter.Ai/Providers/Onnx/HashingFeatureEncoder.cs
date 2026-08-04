using System.Text;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Turns a subject into a fixed-length float vector using character n-gram feature hashing.
/// </summary>
/// <remarks>
/// <para>
/// Feature hashing is used rather than a learned tokeniser because CAD layer names are short,
/// abbreviation-heavy and outside any natural-language vocabulary. Character n-grams degrade
/// gracefully on names such as <c>C-STRM-PIPE-EX</c>, and hashing needs no vocabulary file to ship
/// alongside the model.
/// </para>
/// <para>
/// The hash must match the one used at training time. It is deliberately a stable FNV-1a rather
/// than <see cref="string.GetHashCode()"/>, which is randomised per process.
/// </para>
/// </remarks>
public sealed class HashingFeatureEncoder
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;
    private const int MinimumGram = 3;
    private const int MaximumGram = 5;

    private readonly ISubjectDescriptor _descriptor;

    /// <summary>Initializes a new instance of the <see cref="HashingFeatureEncoder"/> class.</summary>
    /// <param name="descriptor">Renders the subject into the text that is hashed.</param>
    public HashingFeatureEncoder(ISubjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
    }

    /// <summary>Encodes a subject into an L2-normalised feature vector.</summary>
    /// <param name="subject">The subject to encode.</param>
    /// <param name="dimension">The vector length, which must match the model's input dimension.</param>
    /// <returns>The feature vector.</returns>
    public float[] Encode(ClassificationSubject subject, int dimension)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);

        float[] vector = new float[dimension];
        string text = Normalise(_descriptor.Describe(subject));

        for (int gram = MinimumGram; gram <= MaximumGram; gram++)
        {
            for (int offset = 0; offset + gram <= text.Length; offset++)
            {
                uint hash = Hash(text.AsSpan(offset, gram));
                int index = (int)(hash % (uint)dimension);

                // The sign bit acts as a signed hash, which reduces collision bias.
                vector[index] += (hash & 0x80000000) == 0 ? 1f : -1f;
            }
        }

        return Normalise(vector);
    }

    private static string Normalise(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append(' ');

        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        builder.Append(' ');
        return builder.ToString();
    }

    private static uint Hash(ReadOnlySpan<char> value)
    {
        uint hash = FnvOffsetBasis;

        foreach (char character in value)
        {
            hash ^= character;
            hash *= FnvPrime;
        }

        return hash;
    }

    private static float[] Normalise(float[] vector)
    {
        double sumOfSquares = 0d;

        foreach (float component in vector)
        {
            sumOfSquares += component * component;
        }

        if (sumOfSquares <= double.Epsilon)
        {
            return vector;
        }

        float magnitude = (float)Math.Sqrt(sumOfSquares);

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }

        return vector;
    }
}
