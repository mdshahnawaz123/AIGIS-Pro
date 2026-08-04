using System.Threading.Channels;
using AiGisConverter.Infrastructure.FileSystem;
using AiGisConverter.Infrastructure.Security;
using AiGisConverter.Infrastructure.Threading;
using AiGisConverter.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Infrastructure.Tests;

public sealed class InfrastructureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aigis-infra-tests").FullName;
    private readonly PhysicalFileSystem _fileSystem = new(NullLogger<PhysicalFileSystem>.Instance);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A handle still open must not fail a test that already passed.
        }
    }

    [Fact]
    public void SystemClock_ReportsUtc()
    {
        DateTimeOffset now = new SystemClock().UtcNow;

        now.Offset.Should().Be(TimeSpan.Zero);
        now.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetAvailablePath_UnusedPath_IsReturnedUnchanged()
    {
        string path = Path.Combine(_root, "output.geojson");

        _fileSystem.GetAvailablePath(path).Should().Be(path);
    }

    [Fact]
    public void GetAvailablePath_TakenPath_GetsACounter()
    {
        string path = Path.Combine(_root, "output.geojson");
        File.WriteAllText(path, "x");

        string available = _fileSystem.GetAvailablePath(path);

        available.Should().NotBe(path);
        available.Should().EndWith("output (1).geojson");
        File.Exists(available).Should().BeFalse();
    }

    [Fact]
    public void GetAvailablePath_SeveralTaken_KeepsCounting()
    {
        File.WriteAllText(Path.Combine(_root, "a.csv"), "x");
        File.WriteAllText(Path.Combine(_root, "a (1).csv"), "x");

        _fileSystem.GetAvailablePath(Path.Combine(_root, "a.csv")).Should().EndWith("a (2).csv");
    }

    [Fact]
    public void CanWriteTo_AWritableDirectory_IsTrueAndLeavesNothingBehind()
    {
        _fileSystem.CanWriteTo(_root).Should().BeTrue();

        Directory.EnumerateFiles(_root).Should().BeEmpty("the probe deletes itself on close");
    }

    [Fact]
    public void CanWriteTo_AnImpossiblePath_IsFalse()
    {
        // Corrected: Path.Combine rejects the null character itself, so the original test threw
        // before CanWriteTo was ever called and proved nothing about it. A path under a file
        // rather than a directory is impossible in a way the method actually has to handle.
        string file = Path.Combine(_root, "occupied.txt");
        File.WriteAllText(file, "x");

        _fileSystem.CanWriteTo(Path.Combine(file, "child.geojson")).Should().BeFalse();
    }

    [Fact]
    public void ResolvePath_RelativePath_IsRootedAtTheApplicationFolder() =>
        Path.IsPathRooted(_fileSystem.ResolvePath("Profiles")).Should().BeTrue();

    [Fact]
    public void ResolvePath_ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("AIGIS_TEST_ROOT", _root);

        try
        {
            _fileSystem.ResolvePath("%AIGIS_TEST_ROOT%").Should().Be(_root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIGIS_TEST_ROOT", null);
        }
    }

    [Fact]
    public void EnumerateFiles_MissingDirectory_ReturnsEmptyRatherThanThrowing() =>
        _fileSystem.EnumerateFiles(Path.Combine(_root, "nope"), "*.dxf").Should().BeEmpty();

    [Fact]
    public void EnumerateFiles_Recursive_FindsNestedMatches()
    {
        string nested = Directory.CreateDirectory(Path.Combine(_root, "sub")).FullName;
        File.WriteAllText(Path.Combine(_root, "a.dxf"), "x");
        File.WriteAllText(Path.Combine(nested, "b.dxf"), "x");
        File.WriteAllText(Path.Combine(nested, "c.txt"), "x");

        _fileSystem.EnumerateFiles(_root, "*.dxf", recursive: true).Should().HaveCount(2);
        _fileSystem.EnumerateFiles(_root, "*.dxf").Should().ContainSingle();
    }

    [Fact]
    public void GetFileSize_MissingFile_IsZero() =>
        _fileSystem.GetFileSize(Path.Combine(_root, "absent.dxf")).Should().Be(0L);

    [Fact]
    public void SecretResolver_ReadsFromTheProcessEnvironment()
    {
        EnvironmentSecretResolver resolver = new();
        Environment.SetEnvironmentVariable("AIGIS_TEST_SECRET", "s3cret");

        try
        {
            resolver.Resolve("AIGIS_TEST_SECRET").Should().Be("s3cret");
            resolver.IsAvailable("AIGIS_TEST_SECRET").Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AIGIS_TEST_SECRET", null);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AIGIS_DEFINITELY_NOT_SET")]
    public void SecretResolver_AbsentSecret_IsNull(string name)
    {
        EnvironmentSecretResolver resolver = new();

        resolver.Resolve(name).Should().BeNull();
        resolver.IsAvailable(name).Should().BeFalse();
    }

    [Fact]
    public async Task BackgroundQueue_RoundTripsWork()
    {
        BackgroundTaskQueue queue = new(NullLogger<BackgroundTaskQueue>.Instance, capacity: 4);
        bool ran = false;

        await queue.EnqueueAsync(_ =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        Func<CancellationToken, ValueTask> work = await queue.DequeueAsync();
        await work(CancellationToken.None);

        ran.Should().BeTrue();
    }

    [Fact]
    public async Task BackgroundQueue_AtCapacity_MakesTheProducerWait()
    {
        // An unbounded queue turns a user holding a button down into an out-of-memory failure.
        BackgroundTaskQueue queue = new(NullLogger<BackgroundTaskQueue>.Instance, capacity: 1);

        await queue.EnqueueAsync(_ => ValueTask.CompletedTask);

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () => await queue.EnqueueAsync(_ => ValueTask.CompletedTask, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BackgroundQueue_AfterComplete_DequeueEnds()
    {
        BackgroundTaskQueue queue = new(NullLogger<BackgroundTaskQueue>.Instance);
        queue.Complete();

        Func<Task> act = async () => await queue.DequeueAsync();

        await act.Should().ThrowAsync<ChannelClosedException>();
    }

    [Fact]
    public async Task BackgroundQueue_Cancellation_StopsAWaitingConsumer()
    {
        BackgroundTaskQueue queue = new(NullLogger<BackgroundTaskQueue>.Instance);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () => await queue.DequeueAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
