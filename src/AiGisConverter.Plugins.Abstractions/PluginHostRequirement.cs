namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Declares that a plugin's capability depends on a separate host application being installed,
/// because the vendor API only functions inside that application's own process.
/// </summary>
/// <remarks>
/// AutoCAD, Civil 3D and Revit all fall into this category. The plugin loaded by the converter is
/// the bridge client; the code that actually touches the vendor API is an add-in installed into
/// the host application.
/// </remarks>
public sealed class PluginHostRequirement
{
    /// <summary>Gets or sets the host application name, for example <c>Revit</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the minimum supported host release, for example <c>2024</c>.</summary>
    public string? MinimumVersion { get; set; }

    /// <summary>Gets or sets the maximum supported host release.</summary>
    public string? MaximumVersion { get; set; }

    /// <summary>Gets or sets the named pipe used to reach the in-process add-in.</summary>
    public string? PipeName { get; set; }

    /// <summary>
    /// Gets or sets the path to a headless executable that can service requests without a visible
    /// session, for example <c>accoreconsole.exe</c>. Null when only an interactive session works.
    /// </summary>
    public string? HeadlessExecutable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the converter may launch the host application
    /// itself. Left false by default: starting a licensed CAD application unattended is a
    /// decision for the administrator, not the converter.
    /// </summary>
    public bool AllowAutoLaunch { get; set; }
}
