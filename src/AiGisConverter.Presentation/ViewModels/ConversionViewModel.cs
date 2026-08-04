using System;
using System.IO;
using System.Threading;
using System.Collections.ObjectModel;
using AiGisConverter.Application.Services.Batch;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>Running a conversion, watching it, and stopping it.</summary>
public sealed partial class ConversionViewModel : ObservableObject, IDisposable
{
    private readonly IBatchConversionService _batch;
    private readonly ProjectViewModel _project;
    private readonly QaQcViewModel _qaQc;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ConversionViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private int _maxConcurrency = 2;

    [ObservableProperty]
    private bool _continueOnError = true;

    /// <summary>Initializes a new instance of the <see cref="ConversionViewModel"/> class.</summary>
    /// <param name="batch">Runs the conversion.</param>
    /// <param name="project">Supplies the project to convert.</param>
    /// <param name="qaQc">Receives the findings afterwards.</param>
    /// <param name="dispatcher">Marshals progress onto the interface thread.</param>
    /// <param name="logger">Logger for the view model.</param>
    public ConversionViewModel(
        IBatchConversionService batch,
        ProjectViewModel project,
        QaQcViewModel qaQc,
        IUiDispatcher dispatcher,
        ILogger<ConversionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _batch = batch;
        _project = project;
        _qaQc = qaQc;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>Gets the runs produced by the most recent conversion.</summary>
    public ObservableCollection<ConversionRun> Runs { get; } = [];

    /// <summary>Gets the drawings that did not convert, with the reason.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    /// <summary>Gets a value indicating whether a conversion can be started.</summary>
    public bool CanStart => !IsRunning && _project.CanConvert;

    /// <summary>Runs the conversion.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Runs.Clear();
        Failures.Clear();

        IsRunning = true;
        Status = "Preparing...";
        Progress = 0d;
        Completed = 0;

        _cancellation = new CancellationTokenSource();

        try
        {
            ConversionProject project = _project.BuildProject();
            Total = project.Jobs.Count;

            Progress<BatchProgress> progress = new(OnProgress);

            BatchResult result = await _batch.ConvertAsync(
                project,
                _project.OutputFolder!,
                new BatchOptions(MaxConcurrency, ContinueOnError),
                progress,
                _cancellation.Token).ConfigureAwait(true);

            foreach (ConversionRun run in result.Succeeded)
            {
                Runs.Add(run);
            }

            foreach ((_, string location, Domain.Common.Error error) in result.Failed)
            {
                Failures.Add($"{Path.GetFileName(location)}: {error.Message}");
            }

            _qaQc.LoadFromRuns(result.Succeeded);

            Status = result.IsCompleteSuccess
                ? $"Converted {result.Succeeded.Count} of {result.Total} drawings in {result.Duration.TotalSeconds:F1} s."
                : $"Converted {result.Succeeded.Count} of {result.Total}; {result.Failed.Count} failed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            // Configuration typed into the form reaches the domain's factories here, so an
            // unparseable coordinate system surfaces as a message rather than a crash.
            _logger.LogError(ex, "The conversion could not be started.");
            Status = $"Could not start: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            Progress = 1d;

            _cancellation?.Dispose();
            _cancellation = null;

            StartCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Stops the conversion.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        Status = "Cancelling...";
        _cancellation?.Cancel();
    }

    /// <summary>Applies batch progress, on the interface thread.</summary>
    private void OnProgress(BatchProgress report) => _dispatcher.Post(() =>
    {
        Completed = report.Completed;
        Total = report.Total;
        Progress = report.Total == 0 ? 0d : (double)report.Completed / report.Total;
        Status = $"[{report.Completed}/{report.Total}] {Path.GetFileName(report.CurrentFile)}";
    });

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation?.Dispose();
    }
}
