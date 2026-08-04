using AiGisConverter.Plugins.Hosting;
using AiGisConverter.QaQc.Abstractions;

namespace AiGisConverter.Composition;

/// <summary>
/// Exposes plugin-contributed <see cref="IValidationRule"/> capabilities to the QA/QC engine.
/// </summary>
/// <remarks>
/// The same adapter shape as <see cref="CapabilityAIProviderSource"/>, and for the same reason:
/// <c>AiGisConverter.QaQc</c> must not reference the plugin host, and the plugin host must not
/// reference QA/QC. A site shipping its own submission checks as a plugin gets them run by the
/// engine with no change to either assembly.
/// </remarks>
public sealed class CapabilityValidationRuleSource : IValidationRuleSource
{
    private readonly ICapabilityRegistry _registry;

    /// <summary>Initializes a new instance of the <see cref="CapabilityValidationRuleSource"/> class.</summary>
    /// <param name="registry">The host capability registry.</param>
    public CapabilityValidationRuleSource(ICapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public IEnumerable<IValidationRule> GetRules() => _registry.GetCapabilities<IValidationRule>();
}
