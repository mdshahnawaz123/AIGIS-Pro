using System.Collections.ObjectModel;
using AiGisConverter.Application.Dtos;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>The quality findings from the most recent conversion.</summary>
/// <remarks>
/// Findings are filtered rather than paged. A conversion that produces more findings than a list
/// can hold has a systematic fault, and the QA engine already caps and says so; adding paging here
/// would make that easier to miss.
/// </remarks>
public sealed partial class QaQcViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<ValidationIssueDto> _all = [];

    [ObservableProperty]
    private IssueSeverity _minimumSeverity = IssueSeverity.Information;

    [ObservableProperty]
    private string _summary = "No conversion has been run yet.";

    [ObservableProperty]
    private bool _hasFindings;

    /// <summary>Initializes a new instance of the <see cref="QaQcViewModel"/> class.</summary>
    /// <param name="scopeFactory">Creates the scope the report repository needs.</param>
    public QaQcViewModel(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <summary>Gets the findings currently shown.</summary>
    public ObservableCollection<ValidationIssueDto> Findings { get; } = [];

    /// <summary>Gets the severities that may be filtered on.</summary>
    public IReadOnlyList<IssueSeverity> Severities { get; } = [.. Enum.GetValues<IssueSeverity>()];

    /// <summary>Loads the findings recorded against a set of runs.</summary>
    /// <param name="runs">The runs to load findings for.</param>
    public void LoadFromRuns(IReadOnlyList<ConversionRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        _all.Clear();

        using IServiceScope scope = _scopeFactory.CreateScope();
        IValidationReportRepository reports =
            scope.ServiceProvider.GetRequiredService<IValidationReportRepository>();

        foreach (ConversionRun run in runs)
        {
            ValidationReport? report = reports.GetForRunAsync(run.Id).GetAwaiter().GetResult();

            if (report is not null)
            {
                _all.AddRange(ConversionMapper.ToDtos(report));
            }
        }

        ApplyFilter();
    }

    /// <summary>Clears the findings.</summary>
    [RelayCommand]
    private void Clear()
    {
        _all.Clear();
        ApplyFilter();
    }

    partial void OnMinimumSeverityChanged(IssueSeverity value) => ApplyFilter();

    private void ApplyFilter()
    {
        Findings.Clear();

        foreach (ValidationIssueDto issue in _all
            .Where(issue => issue.Severity >= MinimumSeverity)
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal))
        {
            Findings.Add(issue);
        }

        HasFindings = Findings.Count > 0;

        Summary = _all.Count == 0
            ? "No findings were recorded."
            : $"{Findings.Count} of {_all.Count} findings at or above {MinimumSeverity}. " +
              $"{_all.Count(static i => i.Severity >= IssueSeverity.Error)} need attention.";
    }
}
