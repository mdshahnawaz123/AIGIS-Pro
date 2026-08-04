namespace AiGisConverter.Infrastructure.Security;

/// <summary>
/// Resolves secrets by name, without them ever appearing in configuration.
/// </summary>
/// <remarks>
/// API keys are read from the environment, never from <c>appsettings.json</c>. A key in a
/// configuration file is a key in the repository, in the installer, and in the support bundle
/// somebody emails when the application misbehaves.
/// </remarks>
public interface ISecretResolver
{
    /// <summary>Resolves a secret.</summary>
    /// <param name="name">The environment variable name.</param>
    /// <returns>The value, or <see langword="null"/> when it is not set.</returns>
    string? Resolve(string name);

    /// <summary>Determines whether a secret is available without reading it.</summary>
    /// <param name="name">The environment variable name.</param>
    /// <returns><see langword="true"/> when the variable is set and non-empty.</returns>
    bool IsAvailable(string name);
}

/// <summary>Reads secrets from environment variables.</summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    /// <inheritdoc />
    public string? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Process, then user, then machine. The process scope is what a launcher or a CI runner
        // sets, and it should win over a stale user-level value.
        foreach (EnvironmentVariableTarget target in new[]
        {
            EnvironmentVariableTarget.Process,
            EnvironmentVariableTarget.User,
            EnvironmentVariableTarget.Machine,
        })
        {
            string? value = SafeRead(name, target);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsAvailable(string name) => Resolve(name) is not null;

    /// <summary>Reads one scope, tolerating platforms where user and machine scopes do not exist.</summary>
    private static string? SafeRead(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }
}
