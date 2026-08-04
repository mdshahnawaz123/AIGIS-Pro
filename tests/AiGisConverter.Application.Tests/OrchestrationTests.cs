using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Dtos;
using AiGisConverter.Application.Jobs;
using AiGisConverter.Application.Notifications;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Application.Tests;

public sealed class OrchestrationTests
{
    // ---- notifications -----------------------------------------------------------------------

    [Fact]
    public void Notifications_ArePublishedToSubscribers()
    {
        NotificationService service = new(NullLogger<NotificationService>.Instance);
        List<Notification> seen = [];
        service.Published += (_, n) => seen.Add(n);

        service.Publish(new Notification(NotificationLevel.Information, "Converted"));

        seen.Should().ContainSingle();
    }

    [Fact]
    public void Notifications_AHandlerThatThrows_DoesNotFailThePublisher()
    {
        NotificationService service = new(NullLogger<NotificationService>.Instance);
        service.Published += (_, _) => throw new InvalidOperationException("bad toast");

        Action act = () => service.Publish(new Notification(NotificationLevel.Error, "x"));

        act.Should().NotThrow("a misbehaving toast must not fail the conversion that raised it");
    }

    [Fact]
    public void Notifications_HistoryIsBoundedAndNewestFirst()
    {
        NotificationService service = new(NullLogger<NotificationService>.Instance);

        for (int i = 0; i < 600; i++)
        {
            service.Publish(new Notification(NotificationLevel.Information, $"n{i}"));
        }

        IReadOnlyList<Notification> recent = service.GetRecent(5);

        recent.Should().HaveCount(5);
        recent[0].Title.Should().Be("n599");
    }

    // ---- job engine --------------------------------------------------------------------------

    [Fact]
    public async Task JobEngine_RunsQueuedWork()
    {
        await using JobEngine engine = new(NullLogger<JobEngine>.Instance);
        TaskCompletionSource ran = new();

        await engine.EnqueueAsync(new JobDescriptor("job", _ =>
        {
            ran.TrySetResult();
            return Task.CompletedTask;
        }));

        engine.Complete();
        await engine.RunAsync();

        ran.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task JobEngine_AFailingJob_DoesNotStopTheOnesBehindIt()
    {
        await using JobEngine engine = new(NullLogger<JobEngine>.Instance);
        bool secondRan = false;

        await engine.EnqueueAsync(new JobDescriptor("bad", _ => throw new InvalidOperationException("boom")));
        await engine.EnqueueAsync(new JobDescriptor("good", _ =>
        {
            secondRan = true;
            return Task.CompletedTask;
        }));

        engine.Complete();
        await engine.RunAsync();

        secondRan.Should().BeTrue("the contract is that queued work is attempted, not that it succeeds");
    }

    [Fact]
    public async Task JobEngine_AtCapacity_MakesTheProducerWait()
    {
        await using JobEngine engine = new(NullLogger<JobEngine>.Instance, capacity: 1);
        await engine.EnqueueAsync(new JobDescriptor("first", _ => Task.CompletedTask));

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () =>
            await engine.EnqueueAsync(new JobDescriptor("second", _ => Task.CompletedTask), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task JobEngine_Cancellation_StopsTheLoop()
    {
        await using JobEngine engine = new(NullLogger<JobEngine>.Instance);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        await engine.RunAsync(cts.Token);

        engine.IsRunning.Should().BeFalse();
    }

    // ---- DTO mapping -------------------------------------------------------------------------

    [Fact]
    public void Mapper_FlattensAProject()
    {
        ConversionProject project = ConversionProject.Create("Site", ConversionSettings.Default());
        project.AddJob(new SourceReference(@"C:\a.dxf"));

        ProjectSummaryDto dto = ConversionMapper.ToSummary(project);

        dto.Name.Should().Be("Site");
        dto.JobCount.Should().Be(1);
        dto.TargetCrs.Should().Be("EPSG:4326");
        dto.ExportFormats.Should().Contain("GeoJson");
    }

    [Fact]
    public void Mapper_FlattensARunIncludingItsProvenance()
    {
        ConversionProject project = ConversionProject.Create("Site", ConversionSettings.Default());
        ConversionJob job = project.AddJob(new SourceReference(@"C:\a.dxf"));
        ConversionRun run = ConversionRun.Create(job, project.Settings);

        run.Start();
        run.RecordCoordinateSystem(CoordinateSystem.Create("EPSG", 27700), CrsDetectionSource.PrjSidecar);
        run.RecordSourceRead(1_000);
        run.RecordValidation(IssueSeverity.Warning, 3);
        run.RecordOutput(@"C:\out\parcels.geojson");
        run.Complete(950);

        RunSummaryDto dto = ConversionMapper.ToSummary(run);

        dto.Status.Should().Be(ConversionStatus.SucceededWithWarnings,
            "the aggregate derives the status; the mapper only reports it");
        dto.CoordinateSystem.Should().Be("EPSG:27700");
        dto.CrsSource.Should().Be(CrsDetectionSource.PrjSidecar);
        dto.ElementsRead.Should().Be(1_000);
        dto.FeaturesWritten.Should().Be(950);
        dto.OutputPaths.Should().ContainSingle();
    }

    [Fact]
    public void Mapper_FlattensFindingsIncludingLocationAndRemediation()
    {
        ValidationReport report = new(
            new Domain.Common.ConversionRunId(Guid.NewGuid()),
            [
                ValidationIssue.Create(IssueSeverity.Error, IssueCategory.Topology, "T.Overlap", "A overlaps B")
                    .ForFeature("A").At(1d, 2d).WithRemediation("Snap the boundary"),
            ]);

        IReadOnlyList<ValidationIssueDto> dtos = ConversionMapper.ToDtos(report);

        dtos.Should().ContainSingle();
        dtos[0].LocationX.Should().Be(1d);
        dtos[0].Remediation.Should().Be("Snap the boundary");
    }
}
