using AiGisConverter.Data.Context;
using AiGisConverter.Data.Repositories;
using AiGisConverter.Data.Tests.TestSupport;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Events;
using AiGisConverter.Domain.Specifications;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Data.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static ConversionProject Project(string name = "Site survey")
    {
        ConversionProject project = ConversionProject.Create(name, ConversionSettings.Default());
        project.AddJob(new SourceReference(@"C:\site\survey.dxf"));

        return project;
    }

    [Fact]
    public async Task Project_RoundTripsThroughTheDatabase()
    {
        ConversionProject project = Project();
        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ConversionProject? loaded = await reader.Projects
            .Include(p => p.Jobs)
            .FirstOrDefaultAsync(p => p.Id == project.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Site survey");
        loaded.Jobs.Should().ContainSingle("the aggregate is always loaded whole");
        loaded.Jobs[0].Source.Location.Should().Be(@"C:\site\survey.dxf");
    }

    [Fact]
    public async Task TypedIdentifiers_SurviveTheRoundTrip()
    {
        ConversionProject project = Project();
        ProjectId expected = project.Id;

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ConversionProject loaded = await reader.Projects.FirstAsync();

        loaded.Id.Should().Be(expected);
        loaded.Id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Settings_RoundTripAsJson()
    {
        ConversionSettings settings = ConversionSettings
            .Create(CoordinateSystem.Create("EPSG", 27700), [ExportFormat.GeoPackage, ExportFormat.GeoJson])
            .WithConfidenceThreshold(0.8d);

        ConversionProject project = ConversionProject.Create("Projected", settings);
        project.AddJob(new SourceReference(@"C:\a.dxf"));

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ConversionProject loaded = await reader.Projects.FirstAsync();

        loaded.Settings.TargetCoordinateSystem.Code.Should().Be(27700);
        loaded.Settings.ExportFormats.Should().HaveCount(2);
        loaded.Settings.ConfidenceThreshold.Should().BeApproximately(0.8d, 1e-9d);
    }

    [Fact]
    public async Task SourceReferenceHints_RoundTrip()
    {
        SourceReference reference = new(@"C:\model.rvt") { IsLiveSession = true };
        reference.SetHint("view", "Site Plan");

        ConversionProject project = ConversionProject.Create("Revit", ConversionSettings.Default());
        project.AddJob(reference);

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ConversionJob job = await reader.Jobs.FirstAsync();

        job.Source.IsLiveSession.Should().BeTrue();
        job.Source.Hints.Should().ContainKey("view");
        job.Source.Hints["view"].Should().Be("Site Plan");
    }

    [Fact]
    public async Task Run_OutputPathsRoundTripThroughTheBackingField()
    {
        ConversionProject project = Project();
        ConversionRun run = ConversionRun.Create(project.Jobs[0], project.Settings);

        run.Start();
        run.RecordOutput(@"C:\out\parcels.gpkg");
        run.RecordOutput(@"C:\out\parcels.geojson");
        run.Complete(featuresWritten: 42);

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.Context.Runs.AddAsync(run);
        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ConversionRun loaded = await reader.Runs.FirstAsync();

        loaded.OutputPaths.Should().HaveCount(2);
        loaded.FeaturesWritten.Should().Be(42);
        loaded.Status.Should().Be(ConversionStatus.Succeeded);
    }

    [Fact]
    public async Task Specification_IsTranslatedToSqlNotEvaluatedInMemory()
    {
        ConversionProject project = Project();
        ConversionRun failed = ConversionRun.Create(project.Jobs[0], project.Settings);
        failed.Start();
        failed.Fail("deliberate");

        ConversionRun succeeded = ConversionRun.Create(project.Jobs[0], project.Settings);
        succeeded.Start();
        succeeded.Complete(1);

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.Context.Runs.AddRangeAsync(failed, succeeded);
        await _fixture.UnitOfWork.SaveChangesAsync();

        ConversionRunRepository repository = new(_fixture.Context, NullLogger<ConversionRunRepository>.Instance);

        IReadOnlyList<ConversionRun> matches =
            await repository.ListAsync(new RunsWithStatusSpecification(ConversionStatus.Failed));

        matches.Should().ContainSingle();
        matches[0].FailureReason.Should().Be("deliberate");
    }

    [Fact]
    public async Task ValidationReport_RoundTripsAgainstItsRun()
    {
        ConversionProject project = Project();
        ConversionRun run = ConversionRun.Create(project.Jobs[0], project.Settings);
        run.Start();
        run.Complete(1);

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.Context.Runs.AddAsync(run);
        await _fixture.UnitOfWork.SaveChangesAsync();

        ValidationReportRepository reports = new(_fixture.Context);

        await reports.AddAsync(new ValidationReport(run.Id,
        [
            ValidationIssue.Create(IssueSeverity.Error, IssueCategory.Topology, "T.Overlap", "A overlaps B")
                .ForFeature("A").At(1d, 2d),
            ValidationIssue.Create(IssueSeverity.Warning, IssueCategory.Attribute, "A.Sparse", "Mostly empty")
                .ForLayer(LayerName.Create("C-PARCEL")),
        ]));

        await _fixture.UnitOfWork.SaveChangesAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        ValidationReport? loaded = await new ValidationReportRepository(reader).GetForRunAsync(run.Id);

        loaded.Should().NotBeNull();
        loaded!.TotalCount.Should().Be(2);
        loaded.HighestSeverity.Should().Be(IssueSeverity.Error);
        loaded.Issues.Should().Contain(i => i.HasLocation);
        loaded.Issues.Should().Contain(i => i.Layer != null && i.Layer.Value == "C-PARCEL");
    }

    [Fact]
    public async Task SaveChanges_DispatchesDomainEventsAfterTheCommit()
    {
        ConversionProject project = Project();

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        _fixture.Dispatcher.Dispatched.Should().Contain(e => e is ConversionProjectCreated);
        _fixture.Dispatcher.Dispatched.Should().Contain(e => e is ConversionJobAdded);
    }

    [Fact]
    public async Task SaveChanges_ClearsEventsSoASecondSaveDoesNotRaiseThemAgain()
    {
        ConversionProject project = Project();

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.UnitOfWork.SaveChangesAsync();

        int afterFirst = _fixture.Dispatcher.Dispatched.Count;

        project.Rename("Renamed");
        await _fixture.UnitOfWork.SaveChangesAsync();

        _fixture.Dispatcher.Dispatched.Should().HaveCount(afterFirst,
            "a rename raises no event, and the earlier ones must not be replayed");
    }

    [Fact]
    public async Task Transaction_RollbackDiscardsTheWork()
    {
        await using IUnitOfWorkTransaction transaction = await _fixture.UnitOfWork.BeginTransactionAsync();

        await _fixture.Context.Projects.AddAsync(Project("Doomed"));
        await _fixture.UnitOfWork.SaveChangesAsync();
        await transaction.RollbackAsync();

        await using AiGisConverterDbContext reader = _fixture.NewContext();
        (await reader.Projects.CountAsync()).Should().Be(0);
    }

    /// <remarks>
    /// Previously skipped because EF Core's SQLite provider could not translate nullable
    /// <c>DateTimeOffset</c> comparisons in LINQ. Now that <c>PruneAsync</c> uses raw SQL,
    /// the comparison works correctly.
    /// </remarks>
    [Fact]
    public async Task Prune_RemovesOldRunsAndTheirFindings()
    {
        ConversionProject project = Project();
        ConversionRun run = ConversionRun.Create(project.Jobs[0], project.Settings);
        run.Start();
        run.Complete(1);

        await _fixture.Context.Projects.AddAsync(project);
        await _fixture.Context.Runs.AddAsync(run);

        await new ValidationReportRepository(_fixture.Context).AddAsync(new ValidationReport(run.Id,
            [ValidationIssue.Create(IssueSeverity.Warning, IssueCategory.Topology, "T", "x")]));

        await _fixture.UnitOfWork.SaveChangesAsync();

        ConversionRunRepository repository = new(_fixture.Context, NullLogger<ConversionRunRepository>.Instance);

        int pruned = await repository.PruneAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        pruned.Should().Be(1);
        await using AiGisConverterDbContext reader = _fixture.NewContext();

        (await reader.ValidationIssues.CountAsync()).Should().Be(0,
            "findings must not outlive the run they describe");
    }

}
