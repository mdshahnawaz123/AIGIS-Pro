using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AiGisConverter.Bridge.Protocol;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// The AI GIS Converter add-in. Starts the bridge server when Revit starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The converter runs as a separate application and cannot call the Revit API, which functions
    /// only inside this process. So the relationship is inverted from the usual one: Revit hosts a
    /// small server, and the converter connects to it when it needs something. That is why the
    /// listener starts here at Revit start-up rather than on demand &#8212; by the time the
    /// converter wants to talk, there is nobody to ask to open the door.
    /// </para>
    /// <para>
    /// Start-up must not fail. An add-in that returns <see cref="Result.Failed"/> shows the user a
    /// dialog about a component they did not ask for, in an application they opened to do something
    /// else. A bridge that cannot listen is recorded and reported through the status command
    /// instead.
    /// </para>
    /// </remarks>
    public sealed class RevitBridgeApplication : IExternalApplication, IDisposable
    {
        /// <summary>The host name both halves of the bridge agree on.</summary>
        internal const string HostName = "Revit";

        private const string RibbonTabName = "AI GIS Converter";
        private const string RibbonPanelName = "Bridge";

        /// <summary>
        /// How long a bridge call waits for Revit to become idle and finish the work.
        /// </summary>
        /// <remarks>
        /// Generous, because enumerating a large model takes real time and the alternative to
        /// waiting is failing a read that would have succeeded. Bounded, because Revit is not idle
        /// while a modal dialog is open and an unbounded wait would hold the bridge thread until
        /// someone noticed.
        /// </remarks>
        private static readonly TimeSpan RevitThreadTimeout = TimeSpan.FromMinutes(10);

        private BridgeServer _server;
        private RevitJobQueue _jobs;

        /// <summary>
        /// Teaches the runtime to find this add-in's own dependencies.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Revit loads the add-in assembly by path, but the application base stays Revit's own
        /// install folder. Every dependency this assembly needs is therefore probed for beside
        /// Revit.exe, where it is not, rather than beside the add-in, where it is. Nothing about the
        /// deployment is wrong; the loader is simply looking somewhere else.
        /// </para>
        /// <para>
        /// The failure this prevents is opaque: <c>System.Text.Json</c> fails to load, the static
        /// initialiser of the first type that touches it throws, and what surfaces is a
        /// TypeInitializationException naming a type that has nothing wrong with it.
        /// </para>
        /// <para>
        /// Registered in a static constructor so it is in place before any method body that
        /// references the protocol types is compiled.
        /// </para>
        /// </remarks>
        static RevitBridgeApplication()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAddinFolder;
        }

        private static Assembly ResolveFromAddinFolder(object sender, ResolveEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.Name))
            {
                return null;
            }

            string simpleName;

            try
            {
                simpleName = new AssemblyName(args.Name).Name;
            }
            catch (Exception)
            {
                return null;
            }

            // Satellite assemblies are Revit's business, not this add-in's.
            if (simpleName == null || simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(typeof(RevitBridgeApplication).Assembly.Location);

            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string candidate = Path.Combine(directory, simpleName + ".dll");

            // Only assemblies this add-in actually ships. Answering for anything else would put
            // this handler in the middle of Revit's own resolution, which it has no business being.
            if (!File.Exists(candidate))
            {
                return null;
            }

            try
            {
                return Assembly.LoadFrom(candidate);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Gets the running instance, for the status command.</summary>
        internal static RevitBridgeApplication Current { get; private set; }

        /// <summary>Gets the Revit release this add-in is loaded into, for example <c>2024</c>.</summary>
        internal string HostVersion { get; private set; }

        /// <summary>Gets the full Revit product name.</summary>
        internal string HostProductName { get; private set; }

        /// <summary>Gets the bridge server, or null when it could not be started.</summary>
        internal BridgeServer Server
        {
            get { return _server; }
        }

        /// <summary>Gets the reason the bridge failed to start, when it did.</summary>
        internal string StartupError { get; private set; }

        /// <inheritdoc />
        public Result OnStartup(UIControlledApplication application)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            Current = this;
            HostVersion = application.ControlledApplication.VersionNumber;
            HostProductName = application.ControlledApplication.VersionName;

            // Created before the listener starts: ExternalEvent.Create is itself a Revit API call
            // and is only valid here, in Revit's own start-up context.
            _jobs = new RevitJobQueue();
            _jobs.Initialise();

            CreateRibbon(application);
            StartBridge();

            return Result.Succeeded;
        }

        /// <inheritdoc />
        public Result OnShutdown(UIControlledApplication application)
        {
            Dispose();
            Current = null;

            return Result.Succeeded;
        }

        /// <summary>Stops the bridge server and releases its pipe.</summary>
        /// <remarks>
        /// Revit owns this object and signals the end of its life through
        /// <see cref="OnShutdown"/>, which is where this is called from. It is public and explicit
        /// because the pipe handle held by <see cref="BridgeServer"/> needs one unambiguous owner:
        /// a listener that outlived Revit would keep the pipe name reserved, and the next session
        /// would fail to open its own with an error naming neither cause.
        /// </remarks>
        public void Dispose()
        {
            // The listener first: it is what hands work to the queue, so stopping it in the other
            // order leaves a window where a request is accepted and then cannot be served.
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }

            if (_jobs != null)
            {
                _jobs.Dispose();
                _jobs = null;
            }

            GC.SuppressFinalize(this);
        }

        private void StartBridge()
        {
            try
            {
                _server = new BridgeServer(BridgeProtocol.GetPipeName(HostName), Dispatch);
                _server.Start();
            }
            catch (Exception exception)
            {
                // The whole chain, not just the outer message. A type initializer failure reports
                // only "the type initializer for X threw an exception", and the sentence that
                // actually identifies the problem is one or two levels down.
                StartupError = Describe(exception);
                _server = null;
            }
        }

        /// <summary>
        /// Handles one bridge request.
        /// </summary>
        /// <remarks>
        /// Called on a background thread. Only <c>handshake</c> is answered in this slice; the
        /// methods that read a model need the Revit API and therefore Revit's own thread, so they
        /// are declined explicitly rather than half-implemented. A named refusal is far easier to
        /// act on than a timeout.
        /// </remarks>
        /// <param name="request">The incoming request.</param>
        /// <returns>The response to write back.</returns>
        private BridgeResponse Dispatch(BridgeRequest request)
        {
            if (request == null)
            {
                return BridgeResponse.Failed(string.Empty, "The request was empty.");
            }

            if (string.Equals(request.Method, BridgeMethods.Handshake, StringComparison.OrdinalIgnoreCase))
            {
                return new BridgeResponse
                {
                    Id = request.Id,
                    Success = true,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        // hostVersion is what the converter's health check reports back to the user.
                        { "hostVersion", HostVersion ?? string.Empty },
                        { "hostName", HostName },
                        { "hostProduct", HostProductName ?? string.Empty },
                        { "protocolVersion", BridgeProtocol.Version },
                        { "addinVersion", AddinVersion() },
                    },
                };
            }

            if (string.Equals(request.Method, BridgeMethods.Shutdown, StringComparison.OrdinalIgnoreCase))
            {
                return new BridgeResponse
                {
                    Id = request.Id,
                    Success = false,
                    Error = "The add-in listener is owned by Revit and stops when Revit closes.",
                };
            }

            if (string.Equals(request.Method, BridgeMethods.CanRead, StringComparison.OrdinalIgnoreCase))
            {
                return OnRevitThread(request, application => CanRead(application, request));
            }

            if (string.Equals(request.Method, BridgeMethods.ListOpenDocuments, StringComparison.OrdinalIgnoreCase))
            {
                return OnRevitThread(request, ListOpenDocuments);
            }

            if (string.Equals(request.Method, BridgeMethods.ReadDocument, StringComparison.OrdinalIgnoreCase))
            {
                return OnRevitThread(request, application => ReadDocument(application, request));
            }

            return BridgeResponse.Failed(
                request.Id,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not a method the {1} add-in implements.",
                    request.Method,
                    HostName));
        }

        /// <summary>
        /// Marshals a handler onto Revit's thread and turns any failure into a bridge response.
        /// </summary>
        /// <remarks>
        /// Every failure has to come back as a reply. The client's only other outcome is a timeout,
        /// which it reports as the host being unreachable - so an unhandled fault here would tell
        /// the operator to start an application that is already running.
        /// </remarks>
        /// <param name="request">The request being served.</param>
        /// <param name="work">The handler, which runs on Revit's thread.</param>
        /// <returns>The response to send.</returns>
        private BridgeResponse OnRevitThread(BridgeRequest request, Func<UIApplication, BridgeResponse> work)
        {
            if (_jobs == null || !_jobs.IsReady)
            {
                return BridgeResponse.Failed(
                    request.Id,
                    "The add-in could not marshal the request onto Revit's thread.");
            }

            try
            {
                return _jobs.Run(work, RevitThreadTimeout);
            }
            catch (TimeoutException exception)
            {
                return BridgeResponse.Failed(request.Id, exception.Message);
            }
            catch (Exception exception)
            {
                return BridgeResponse.Failed(request.Id, exception.Message);
            }
        }

        private static Document ResolveDocument(UIApplication application, string location, out string error)
        {
            error = null;

            // An explicit location must match a document already open. This slice reads what Revit
            // has in memory; opening a file from disk is a separate act with its own failure modes
            // (worksets, upgrade prompts, missing links) and does not belong behind a read call.
            if (!string.IsNullOrWhiteSpace(location))
            {
                foreach (Document candidate in application.Application.Documents)
                {
                    if (candidate != null
                        && !candidate.IsFamilyDocument
                        && string.Equals(candidate.PathName, location, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            UIDocument active = application.ActiveUIDocument;

            if (active == null || active.Document == null)
            {
                error = "No document is open in Revit. Open the model and try again.";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(location)
                && !string.Equals(active.Document.PathName, location, StringComparison.OrdinalIgnoreCase))
            {
                error = "'" + location + "' is not open in Revit. The active document is '"
                    + active.Document.Title + "'. Open the requested model, or read the active one.";
                return null;
            }

            return active.Document;
        }

        private static BridgeResponse CanRead(UIApplication application, BridgeRequest request)
        {
            string error;
            Document document = ResolveDocument(application, request.Location, out error);

            return new BridgeResponse
            {
                Id = request.Id,
                Success = true,
                Error = error,
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "canRead", (document != null).ToString(CultureInfo.InvariantCulture) },
                    { "title", document == null ? string.Empty : document.Title },
                },
            };
        }

        private static BridgeResponse ListOpenDocuments(UIApplication application)
        {
            Dictionary<string, string> open =
                RevitDocumentReader.DescribeOpenDocuments(application.Application.Documents.Cast<Document>());

            return new BridgeResponse
            {
                Id = string.Empty,
                Success = true,
                Values = open,
            };
        }

        private static BridgeResponse ReadDocument(UIApplication application, BridgeRequest request)
        {
            string error;
            Document document = ResolveDocument(application, request.Location, out error);

            if (document == null)
            {
                return BridgeResponse.Failed(request.Id, error);
            }

            return new BridgeResponse
            {
                Id = request.Id,
                Success = true,
                Document = RevitDocumentReader.Read(document),
            };
        }

        /// <summary>Flattens an exception and everything it wraps into one readable chain.</summary>
        /// <remarks>
        /// Written after a real failure was reported to the user as a guess. The dialog asserted
        /// that a second Revit session was holding the pipe, when the actual cause - an assembly
        /// that could not be found - was sitting unread in the inner exception. A diagnostic that
        /// speculates is worse than one that says nothing, because it sends someone looking in the
        /// wrong place with confidence.
        /// </remarks>
        /// <param name="exception">The exception to describe.</param>
        /// <returns>The message chain, outermost first.</returns>
        private static string Describe(Exception exception)
        {
            StringBuilder description = new StringBuilder();

            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (description.Length > 0)
                {
                    description.Append(" -> ");
                }

                description.Append(current.GetType().Name);
                description.Append(": ");
                description.Append(current.Message);
            }

            return description.ToString();
        }

        private static string AddinVersion()
        {
            AssemblyName name = typeof(RevitBridgeApplication).Assembly.GetName();

            return name.Version == null ? "1.0.0.0" : name.Version.ToString();
        }

        private static void CreateRibbon(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(RibbonTabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The tab already exists, because another AI GIS Converter add-in created it or
                // Revit was reloaded. Reusing it is the intended outcome.
            }

            RibbonPanel panel = application.CreateRibbonPanel(RibbonTabName, RibbonPanelName);

            PushButtonData button = new PushButtonData(
                "AiGisBridgeStatus",
                "Bridge\nStatus",
                typeof(RevitBridgeApplication).Assembly.Location,
                typeof(ShowBridgeStatusCommand).FullName)
            {
                ToolTip = "Shows whether the AI GIS Converter can connect to this Revit session.",
                LongDescription =
                    "The AI GIS Converter connects to Revit through a named pipe served by this "
                    + "add-in. Use this command to confirm the pipe is listening before starting a "
                    + "conversion.",
            };

            panel.AddItem(button);
        }
    }
}
