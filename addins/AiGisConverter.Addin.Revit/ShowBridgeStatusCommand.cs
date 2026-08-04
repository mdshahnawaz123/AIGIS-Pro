using System;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Reports whether the AI GIS Converter bridge is listening in this Revit session.
    /// </summary>
    /// <remarks>
    /// Exists so that "the converter cannot see Revit" can be diagnosed from the Revit side. Every
    /// other symptom of a bridge problem looks identical from the converter: a connection timeout,
    /// which is equally consistent with Revit not running, the add-in not installed, and the add-in
    /// installed but unable to open its pipe. This command distinguishes them.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ShowBridgeStatusCommand : IExternalCommand
    {
        /// <inheritdoc />
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            RevitBridgeApplication application = RevitBridgeApplication.Current;

            TaskDialog dialog = new TaskDialog("AI GIS Converter Bridge")
            {
                MainInstruction = Headline(application),
                MainContent = Detail(application),
            };

            dialog.Show();

            return Result.Succeeded;
        }

        private static string Headline(RevitBridgeApplication application)
        {
            if (application == null)
            {
                return "The add-in is not loaded.";
            }

            if (application.Server == null || !application.Server.IsRunning)
            {
                return "The bridge is not listening.";
            }

            return "The bridge is listening.";
        }

        private static string Detail(RevitBridgeApplication application)
        {
            if (application == null)
            {
                return "RevitBridgeApplication.OnStartup did not run. Confirm the .addin manifest "
                    + "points at AiGisConverter.Addin.Revit.dll and that Revit loaded it without "
                    + "error.";
            }

            StringBuilder detail = new StringBuilder();

            detail.AppendLine(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "Host: {0} ({1})",
                    application.HostProductName,
                    application.HostVersion));

            if (application.Server != null)
            {
                detail.AppendLine(
                    string.Format(CultureInfo.CurrentCulture, "Pipe: {0}", application.Server.PipeName));
            }

            if (!string.IsNullOrEmpty(application.StartupError))
            {
                detail.AppendLine();
                detail.AppendLine("The listener could not start:");
                detail.AppendLine(application.StartupError);
                detail.AppendLine();

                // No guessed cause. The chain above names it; asserting one here previously sent a
                // missing-assembly failure to be investigated as a duplicate Revit session.
                detail.AppendLine(
                    "Two causes account for most of these: another Revit session already serving "
                    + "this pipe, or a dependency of the add-in that the runtime could not load "
                    + "from the add-in folder. The chain above distinguishes them.");
            }
            else if (application.Server != null && !string.IsNullOrEmpty(application.Server.LastError))
            {
                detail.AppendLine();
                detail.AppendLine("The listener stopped: " + application.Server.LastError);
            }
            else if (application.Server != null && application.Server.IsRunning)
            {
                detail.AppendLine();
                detail.AppendLine(
                    "The AI GIS Converter should report this Revit session as Healthy. If it does "
                    + "not, the converter is looking at a different pipe name - check "
                    + "hostApplication.pipeName in the Revit plugin manifest.");
            }

            return detail.ToString();
        }
    }
}
