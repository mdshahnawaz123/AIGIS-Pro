using AiGisConverter.Application;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Pipelines;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Application.Tests;

/// <summary>
/// The pipeline owns ordering and error recovery, and nothing else. These are the tests for that
/// and only that.
/// </summary>
public sealed class PipelineTests
{
    private static PipelineContext Context()
    {
        ConversionProject project = ConversionProject.Create("Test", ConversionSettings.Default());
        ConversionJob job = project.AddJob(new SourceReference(@"C:\site.dxf"));
        ConversionRun run = ConversionRun.Create(job, project.Settings);
        run.Start();

        return new PipelineContext(job.Source, project.Settings, run, @"C:\out");
    }

    private static ConversionPipeline Pipeline(params IPipelineStage[] stages) =>
        new(stages, NullLogger<ConversionPipeline>.Instance);

    [Fact]
    public async Task ExecuteAsync_RunsStagesInOrderRegardlessOfRegistrationOrder()
    {
        List<string> ran = [];

        ConversionPipeline pipeline = Pipeline(
            new StubStage("third", 300, ran),
            new StubStage("first", 100, ran),
            new StubStage("second", 200, ran));

        Result result = await pipeline.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        ran.Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task ExecuteAsync_RequiredStageFails_StopsThePipeline()
    {
        List<string> ran = [];

        ConversionPipeline pipeline = Pipeline(
            new StubStage("first", 100, ran) { Failure = new Error("X", "deliberate") },
            new StubStage("second", 200, ran));

        Result result = await pipeline.ExecuteAsync(Context());

        result.IsFailure.Should().BeTrue();
        ran.Should().NotContain("second", "a blocking failure must not let later stages run");
    }

    [Fact]
    public async Task ExecuteAsync_OptionalStageFails_DegradesAndContinues()
    {
        List<string> ran = [];
        PipelineContext context = Context();

        ConversionPipeline pipeline = Pipeline(
            new StubStage("classify", 100, ran) { Optional = true, Failure = new Error("X", "model offline") },
            new StubStage("convert", 200, ran));

        Result result = await pipeline.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue("an unreachable model must not abandon a usable conversion");
        ran.Should().Contain("convert");
        context.DegradedStages.Should().ContainSingle().Which.Should().Be("classify");
    }

    [Fact]
    public async Task ExecuteAsync_StageThrows_IsContainedAsAFailure()
    {
        ConversionPipeline pipeline = Pipeline(new ThrowingStage());

        Result result = await pipeline.ExecuteAsync(Context());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Pipeline.StageThrew",
            "a plugin-contributed stage will throw eventually, and it must not unwind the batch");
    }

    [Fact]
    public async Task ExecuteAsync_OptionalStageThrows_StillDegradesRatherThanStops()
    {
        List<string> ran = [];
        PipelineContext context = Context();

        ConversionPipeline pipeline = Pipeline(
            new ThrowingStage { Optional = true },
            new StubStage("after", 900, ran));

        Result result = await pipeline.ExecuteAsync(context);

        result.IsSuccess.Should().BeTrue();
        ran.Should().Contain("after");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressPerStage()
    {
        List<ConversionProgress> reported = [];
        Progress<ConversionProgress> progress = new(reported.Add);

        await Pipeline(new StubStage("a", 100, []), new StubStage("b", 200, []))
            .ExecuteAsync(Context(), progress);

        await Task.Delay(50);

        reported.Should().NotBeEmpty();
        reported.Should().Contain(p => p.StageCount == 2);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = async () => await Pipeline(new StubStage("a", 100, [])).ExecuteAsync(Context(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void OverallFraction_WeightsStagesEqually()
    {
        new ConversionProgress("s", "m", StageIndex: 1, StageCount: 4, StageFraction: 0.5d)
            .OverallFraction.Should().BeApproximately(0.375d, 1e-9d);
    }

    [Fact]
    public void OverallFraction_UnknownStageCount_IsNull() =>
        new ConversionProgress("s", "m").OverallFraction.Should().BeNull(
            "inventing a percentage produces a bar that jumps backwards");

    private sealed class StubStage(string name, int order, IList<string> log) : IPipelineStage
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public bool Optional { get; init; }

        public bool IsOptional => Optional;

        public Error? Failure { get; init; }

        public Task<Result> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
        {
            log.Add(Name);

            return Task.FromResult(Failure is null ? Result.Success() : Result.Failure(Failure));
        }
    }

    private sealed class ThrowingStage : IPipelineStage
    {
        public string Name => "throwing";

        public int Order => 100;

        public bool Optional { get; init; }

        public bool IsOptional => Optional;

        public Task<Result> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("deliberate stage failure");
    }
}
