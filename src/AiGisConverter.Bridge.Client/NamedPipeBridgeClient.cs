using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AiGisConverter.Bridge.Protocol;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Bridge.Client;

/// <summary>
/// Named-pipe implementation of <see cref="IBridgeClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// One connection per request, rather than a persistent session. A CAD add-in can be torn down at
/// any moment by the user closing the application, and a long-lived pipe would leave the converter
/// blocked on a handle that will never complete. Reconnecting per call costs a few milliseconds
/// and makes every failure immediate and local.
/// </para>
/// <para>
/// Messages are newline-delimited UTF-8 JSON, which keeps the add-in side implementable on
/// .NET Framework 4.8 without additional dependencies.
/// </para>
/// </remarks>
public sealed class NamedPipeBridgeClient : IBridgeClient
{
    private readonly string _pipeName;
    private readonly int _connectTimeoutMilliseconds;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="NamedPipeBridgeClient"/> class.</summary>
    /// <param name="hostName">Host application name, for example <c>Revit</c>.</param>
    /// <param name="pipeName">Explicit pipe name, or null to derive it from the host name.</param>
    /// <param name="connectTimeoutMilliseconds">How long to wait for the add-in to accept a connection.</param>
    /// <param name="logger">Logger for bridge diagnostics.</param>
    public NamedPipeBridgeClient(
        string hostName,
        string? pipeName,
        int connectTimeoutMilliseconds,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(logger);

        HostName = hostName;
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? BridgeProtocol.GetPipeName(hostName) : pipeName;
        _connectTimeoutMilliseconds = connectTimeoutMilliseconds;
        _logger = logger;
    }

    /// <inheritdoc />
    public string HostName { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>?> HandshakeAsync(
        CancellationToken cancellationToken = default)
    {
        BridgeRequest request = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = BridgeMethods.Handshake,
        };

        try
        {
            BridgeResponse response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            return response.Success ? response.Values : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BridgeResponse> SendAsync(
        BridgeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using NamedPipeClientStream pipe = new(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(_connectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"The {HostName} add-in did not answer on pipe '{_pipeName}' within " +
                $"{_connectTimeoutMilliseconds} ms. Confirm {HostName} is running and the " +
                "AI GIS Converter add-in is loaded.");
        }

        string payload = JsonSerializer.Serialize(request, BridgeProtocol.SerializerOptions);

        using StreamWriter writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);

        using StreamReader reader = new(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(line))
        {
            return BridgeResponse.Failed(request.Id, $"The {HostName} add-in closed the pipe without replying.");
        }

        BridgeResponse? response =
            JsonSerializer.Deserialize<BridgeResponse>(line, BridgeProtocol.SerializerOptions);

        if (response is null)
        {
            return BridgeResponse.Failed(request.Id, $"The {HostName} add-in returned an unreadable reply.");
        }

        _logger.LogDebug(
            "Bridge call {Method} to {HostName} returned success={Success}.",
            request.Method,
            HostName,
            response.Success);

        return response;
    }
}
