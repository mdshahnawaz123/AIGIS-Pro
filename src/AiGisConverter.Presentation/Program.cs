using System;
using System.Threading.Tasks;
using AiGisConverter.Presentation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AiGisConverter.Presentation;

/// <summary>
/// The application entry point.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than generated from <c>App.xaml</c>, because the generated entry point
/// cannot be asynchronous and cannot own an <see cref="IHost"/>. Start-up here has to await plugin
/// loading and a database migration before the first window appears, and both are genuinely
/// asynchronous.
/// </para>
/// <para>
/// Everything before the window is wrapped so that a failure during composition produces a message
/// and a log entry rather than a silent exit, which is what a WPF application otherwise does when
/// it throws before its dispatcher starts.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the application.</summary>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main()
    {
        IHost? host = null;

        try
        {
            host = HostFactory.Create();

            Log.Logger = host.Services.GetRequiredService<ILogger>();
            Log.Information("AI GIS Converter starting.");

            // Blocking on start-up is deliberate: there is no window yet, so there is no dispatcher
            // to deadlock against, and the shell must not open before its dependencies are ready.
            ApplicationStartup startup = host.Services.GetRequiredService<ApplicationStartup>();
            StartupOutcome outcome = startup.RunAsync().GetAwaiter().GetResult();

            App app = new(host.Services, outcome);
            app.InitializeComponent();

            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The application failed to start.");

            System.Windows.MessageBox.Show(
                $"AI GIS Converter could not start.\n\n{ex.Message}\n\nSee the log for details.",
                "Startup failure",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
            host?.Dispose();
        }
    }
}
