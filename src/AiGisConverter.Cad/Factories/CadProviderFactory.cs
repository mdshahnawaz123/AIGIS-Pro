using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Factories;

/// <summary>Resolves the CAD provider that handles a given drawing.</summary>
public interface ICadProviderFactory
{
    /// <summary>Gets every registered provider.</summary>
    IReadOnlyList<ICadProvider> Providers { get; }

    /// <summary>Finds the provider that claims a source.</summary>
    /// <param name="reference">The drawing to open.</param>
    /// <returns>The provider, or <see langword="null"/> when none claims it.</returns>
    ICadProvider? Resolve(SourceReference reference);

    /// <summary>Finds a provider by key.</summary>
    /// <param name="key">The provider key, matched case-insensitively.</param>
    /// <returns>The provider, or <see langword="null"/> when no provider has that key.</returns>
    ICadProvider? ResolveByKey(string key);
}

/// <summary>
/// Default <see cref="ICadProviderFactory"/>. Selection is by the providers' own
/// <see cref="ICadProvider.CanRead"/>, never by a switch on file extension held here.
/// </summary>
/// <remarks>
/// Registration order decides ties, so a licensed DWG engine registered ahead of the stub takes
/// precedence without either of them knowing the other exists.
/// </remarks>
public sealed class CadProviderFactory : ICadProviderFactory
{
    /// <summary>Initializes a new instance of the <see cref="CadProviderFactory"/> class.</summary>
    /// <param name="providers">Every registered provider.</param>
    public CadProviderFactory(IEnumerable<ICadProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Providers = [.. providers];
    }

    /// <inheritdoc />
    public IReadOnlyList<ICadProvider> Providers { get; }

    /// <inheritdoc />
    public ICadProvider? Resolve(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return Providers.FirstOrDefault(provider => provider.CanRead(reference));
    }

    /// <inheritdoc />
    public ICadProvider? ResolveByKey(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : Providers.FirstOrDefault(provider =>
                string.Equals(provider.Key, key, StringComparison.OrdinalIgnoreCase));
}
