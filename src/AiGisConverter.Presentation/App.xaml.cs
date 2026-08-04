using System;
using System.Windows;
using System.Windows.Threading;
using AiGisConverter.Presentation.Startup;
using AiGisConverter.Presentation.ViewModels;
using AiGisConverter.Presentation.Views.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Presentation;

/// <summary>
/// The WPF application object.
/// </summary>
/// <remarks>
/// Thin by design. Composition happens in <see cref="HostFactory"/> and preparation in
/// <see cref="ApplicationStartup"/>; all this does is open the shell and make sure an unhandled
/// exception is logged and explained rather than closing the window silently.
/// </remarks>
public partial class App : System.Windows.Application
{
    private readonly IServiceProvider _services;
    private readonly StartupOutcome _startup;

    /// <summary>Initializes a new instance of the <see cref="App"/> class.</summary>
    /// <param name="services">The composed container.</param>
    /// <param name="startup">What start-up managed to do.</param>
    public App(IServiceProvider services, StartupOutcome startup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(startup);

        _services = services;
        _startup = startup;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShellViewModel shell = _services.GetRequiredService<ShellViewModel>();
        shell.ApplyStartupOutcome(_startup);

        ShellWindow window = new() { DataContext = shell };
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Reports an exception that reached the dispatcher, and keeps the application alive.
    /// </summary>
    /// <remarks>
    /// Marking it handled is a considered choice. A conversion tool that closes without warning
    /// loses whatever the user was part-way through describing; showing the fault and staying open
    /// lets them save their project and send the log.
    /// </remarks>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services.GetRequiredService<ILogger<App>>()
            .LogError(e.Exception, "An unhandled exception reached the dispatcher.");

        MessageBox.Show(
            $"Something went wrong.\n\n{e.Exception.Message}\n\n" +
            "The application is still running. Save your work and check the log.",
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    /// <summary>Logs an exception from a background thread, which cannot be recovered from.</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _services.GetRequiredService<ILogger<App>>()
                .LogCritical(exception, "An unhandled exception escaped a background thread.");
        }
    }
}
