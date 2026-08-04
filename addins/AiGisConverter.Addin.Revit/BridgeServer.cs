using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Bridge.Protocol;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Serves the AI GIS Converter bridge from inside Revit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server half of the contract implemented by <c>NamedPipeBridgeClient</c>. The client
    /// opens a connection per request, writes one line of UTF-8 JSON, reads one line back and
    /// disconnects, so this accepts one connection at a time and returns to listening. A persistent
    /// session would be cheaper, but Revit can be closed by the user mid-call and the converter
    /// would then be blocked on a handle that never completes.
    /// </para>
    /// <para>
    /// Both sides serialise through <see cref="BridgeProtocol.SerializerOptions"/>. Naming policy
    /// and case handling live there precisely so this cannot drift: a local JsonSerializerOptions
    /// here would work until the day someone changed one side.
    /// </para>
    /// <para>
    /// Everything runs on a background thread. Nothing here touches the Revit API, which is safe
    /// only on Revit's own thread &#8212; when document reading arrives it will be marshalled
    /// through an ExternalEvent rather than called from this loop.
    /// </para>
    /// </remarks>
    internal sealed class BridgeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly Func<BridgeRequest, BridgeResponse> _dispatch;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly object _gate = new object();

        private NamedPipeServerStream _current;
        private Task _listener;
        private bool _disposed;

        /// <summary>Initializes a new instance of the <see cref="BridgeServer"/> class.</summary>
        /// <param name="pipeName">The pipe to listen on.</param>
        /// <param name="dispatch">Handles one request and produces the response.</param>
        internal BridgeServer(string pipeName, Func<BridgeRequest, BridgeResponse> dispatch)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("A pipe name is required.", nameof(pipeName));
            }

            _pipeName = pipeName;
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        }

        /// <summary>Gets the pipe this server listens on.</summary>
        internal string PipeName
        {
            get { return _pipeName; }
        }

        /// <summary>Gets a value indicating whether the listener is running.</summary>
        internal bool IsRunning
        {
            get { return _listener != null && !_listener.IsCompleted; }
        }

        /// <summary>Gets the last listener failure, if the loop stopped unexpectedly.</summary>
        internal string LastError { get; private set; }

        /// <summary>Starts listening in the background.</summary>
        internal void Start()
        {
            if (_listener != null)
            {
                return;
            }

            _listener = Task.Run(new Func<Task>(ListenAsync));
        }

        private async Task ListenAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;

                try
                {
                    pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    lock (_gate)
                    {
                        if (_cancellation.IsCancellationRequested)
                        {
                            pipe.Dispose();
                            return;
                        }

                        _current = pipe;
                    }

                    // BeginWaitForConnection rather than WaitForConnectionAsync: the callback form
                    // is available on every .NET Framework release the add-in might run on, and
                    // disposing the stream is what cancels it either way.
                    await Task.Factory
                        .FromAsync(pipe.BeginWaitForConnection, pipe.EndWaitForConnection, null)
                        .ConfigureAwait(false);

                    await ServeAsync(pipe).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown disposed the stream out from under the pending wait. Expected.
                    return;
                }
                catch (IOException)
                {
                    // A client that vanished mid-handshake breaks this connection, not the server.
                }
                catch (Exception exception)
                {
                    // The listener must not die silently: a dead pipe looks exactly like Revit not
                    // running, and the operator would be told to start an application already open.
                    LastError = exception.Message;
                    return;
                }
                finally
                {
                    lock (_gate)
                    {
                        _current = null;
                    }

                    if (pipe != null)
                    {
                        pipe.Dispose();
                    }
                }
            }
        }

        private async Task ServeAsync(NamedPipeServerStream pipe)
        {
            // leaveOpen on both: the finally in ListenAsync owns the stream's lifetime.
            using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
            using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
            {
                writer.AutoFlush = true;

                string line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                BridgeResponse response;

                try
                {
                    BridgeRequest request =
                        JsonSerializer.Deserialize<BridgeRequest>(line, BridgeProtocol.SerializerOptions);

                    response = request == null
                        ? BridgeResponse.Failed(string.Empty, "The request could not be read.")
                        : _dispatch(request);
                }
                catch (JsonException exception)
                {
                    response = BridgeResponse.Failed(string.Empty, "Malformed request: " + exception.Message);
                }
                catch (Exception exception)
                {
                    // A handler that throws must still produce a reply. The client's failure mode
                    // for silence is a timeout, which reads as "Revit is not running" and sends the
                    // operator looking in the wrong place entirely.
                    response = BridgeResponse.Failed(string.Empty, exception.Message);
                }

                string payload = JsonSerializer.Serialize(response, BridgeProtocol.SerializerOptions);

                await writer.WriteLineAsync(payload).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);

                // Let the client finish reading before the stream is torn down.
                pipe.WaitForPipeDrain();
            }
        }

        /// <summary>Stops the listener and releases the pipe.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation.Cancel();

            lock (_gate)
            {
                if (_current != null)
                {
                    // Disposing the stream is what breaks a pending BeginWaitForConnection.
                    try
                    {
                        _current.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    _current = null;
                }
            }

            _cancellation.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
