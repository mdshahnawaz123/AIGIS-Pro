using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Caching;

/// <summary>
/// Builds a stable cache key from a request, so that identical layer sets classified under
/// identical rules by the same provider are computed once.
/// </summary>
public sealed class AIRequestCacheKeyFactory
{
    /// <summary>Field separator. A control character that cannot occur in a CAD layer name.</summary>
    private const char Separator = '';

    private readonly ISubjectDescriptor _descriptor;

    /// <summary>Initializes a new instance of the <see cref="AIRequestCacheKeyFactory"/> class.</summary>
    /// <param name="descriptor">Renders each subject deterministically.</param>
    public AIRequestCacheKeyFactory(ISubjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
    }

    /// <summary>Creates a cache key.</summary>
    /// <param name="providerKey">Key of the provider that would serve the request.</param>
    /// <param name="request">The request to key.</param>
    /// <returns>The provider key followed by a hex-encoded SHA-256 digest of the request.</returns>
    public string Create(string providerKey, AIClassificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StringBuilder builder = new(1024);
        builder.Append(providerKey).Append(Separator);
        builder.Append(request.Context.DomainHint).Append(Separator);
        builder.Append(request.Context.DrawingUnits).Append(Separator);
        builder.Append(request.Context.UnknownLabel).Append(Separator);

        foreach (string label in request.Context.CandidateLabels)
        {
            builder.Append(label).Append(Separator);
        }

        foreach (ClassificationSubject subject in request.Subjects)
        {
            builder.Append(subject.Id).Append('=').Append(_descriptor.Describe(subject)).Append(Separator);
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{providerKey}:{Convert.ToHexString(digest).ToLowerInvariant()}");
    }
}
