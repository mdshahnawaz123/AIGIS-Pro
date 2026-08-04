using System.Globalization;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Version and compatibility rules for the plugin contract.
/// </summary>
/// <remarks>
/// The host refuses to load a plugin built against an incompatible SDK major version rather than
/// letting it fail later with a <see cref="MissingMethodException"/> that no user can diagnose.
/// </remarks>
public static class PluginSdk
{
    /// <summary>The SDK contract version implemented by this assembly.</summary>
    public const string Version = "1.0";

    /// <summary>The major component of <see cref="Version"/>.</summary>
    public const int MajorVersion = 1;

    /// <summary>The minor component of <see cref="Version"/>.</summary>
    public const int MinorVersion = 0;

    /// <summary>
    /// Determines whether a plugin declaring the supplied SDK version can be loaded.
    /// </summary>
    /// <remarks>
    /// The rule is the usual one for a contract assembly: the major version must match exactly,
    /// and the plugin's minor version must not exceed the host's, because a newer minor may use
    /// contract members this host does not have.
    /// </remarks>
    /// <param name="declaredSdkVersion">The <c>sdkVersion</c> value from the plugin manifest.</param>
    /// <param name="reason">The reason for rejection, when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the plugin is compatible.</returns>
    public static bool IsCompatible(string? declaredSdkVersion, out string reason)
    {
        if (string.IsNullOrWhiteSpace(declaredSdkVersion))
        {
            reason = "The manifest does not declare 'sdkVersion'.";
            return false;
        }

        string[] parts = declaredSdkVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int major))
        {
            reason = $"'{declaredSdkVersion}' is not a valid SDK version.";
            return false;
        }

        int minor = 0;

        if (parts.Length > 1 &&
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor))
        {
            reason = $"'{declaredSdkVersion}' is not a valid SDK version.";
            return false;
        }

        if (major != MajorVersion)
        {
            reason = $"The plugin targets SDK {declaredSdkVersion}; this host implements SDK {Version}.";
            return false;
        }

        if (minor > MinorVersion)
        {
            reason = $"The plugin targets SDK {declaredSdkVersion}, which is newer than this host's {Version}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
