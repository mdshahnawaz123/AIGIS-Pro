using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Convenience base class for plugins: holds the context, gives a no-op shutdown, and provides
/// the guard clauses most plugins need.
/// </summary>
/// <remarks>
/// Optional. A plugin may implement <see cref="IPlugin"/> directly; nothing in the host requires
/// this type.
/// </remarks>
public abstract class PluginBase : IPlugin
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <summary>Gets the plugin context, available from <see cref="ConfigureAsync"/> onwards.</summary>
    protected IPluginContext? Context { get; private set; }

    /// <summary>Gets the plugin logger, or a null logger before configuration.</summary>
    protected ILogger Logger => Context?.Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <inheritdoc />
    public Task ConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        Context = registration.Context;
        return OnConfigureAsync(registration, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Registers the plugin's capabilities.</summary>
    /// <param name="registration">The registration surface.</param>
    /// <param name="cancellationToken">Token used to cancel loading.</param>
    /// <returns>A task that completes when registration is finished.</returns>
    protected abstract Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken);
}
