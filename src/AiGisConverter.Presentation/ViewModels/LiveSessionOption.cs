namespace AiGisConverter.Presentation.ViewModels;

/// <summary>
/// A host application whose currently open document can be converted without a file.
/// </summary>
/// <remarks>
/// <para>
/// Offered for every loaded plugin that declares a host application in its manifest, because such a
/// plugin is by definition a client for a running application rather than a reader of files. A new
/// bridge plugin therefore appears here by shipping its manifest, with no list to keep in step.
/// </para>
/// <para>
/// <see cref="Label"/> carries the reader's own extension. That is not decoration: the reader
/// catalogue chooses a reader by extension, so the live entry has to look like the thing it will be
/// read by. What stops it being mistaken for a file is the hint that travels with the request,
/// which tells the add-in to read the open document and disregard the name entirely.
/// </para>
/// </remarks>
public sealed class LiveSessionOption
{
    /// <summary>Initializes a new instance of the <see cref="LiveSessionOption"/> class.</summary>
    /// <param name="hostName">The host application, for example <c>Revit</c>.</param>
    /// <param name="extension">The reader's primary extension, including the leading dot.</param>
    /// <param name="readerName">The reader's display name, shown as the tooltip.</param>
    public LiveSessionOption(string hostName, string extension, string readerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        HostName = hostName;
        ReaderName = readerName;
        Label = $"Current {hostName} Session{extension}";
    }

    /// <summary>Gets the host application name.</summary>
    public string HostName { get; }

    /// <summary>Gets the reader that will service the session.</summary>
    public string ReaderName { get; }

    /// <summary>Gets the entry added to the drawing list.</summary>
    public string Label { get; }

    /// <summary>Gets the button caption.</summary>
    public string Caption => $"Current {HostName} session";

    /// <inheritdoc />
    public override string ToString() => Label;
}
