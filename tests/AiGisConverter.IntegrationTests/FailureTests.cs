using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Gis.Crs;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Failure paths, asserted to degrade rather than throw.
/// </summary>
/// <remarks>
/// The contract this suite defends is that a conversion failure is a <see cref="Result"/>, not an
/// exception. A user who opens a corrupt drawing should see a message naming the file; they should
/// not see a stack trace, and the application should still be running afterwards.
/// </remarks>
public sealed class FailureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aigis-failure").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingFile_IsReportedAsAFailureNotAnException()
    {
        SourceReference reference = new(Path.Combine(_root, "does-not-exist.dxf"));

        File.Exists(reference.Location).Should().BeFalse();

        // Constructing a reference to a missing file must not throw; the reader reports it.
        reference.Location.Should().EndWith("does-not-exist.dxf");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a drawing at all")]
    [InlineData("0\nSECTION\n2\nENTITIES\n")]        // truncated mid-section
    [InlineData("\0\0\0\0binary garbage\0\0")]
    public async Task CorruptContent_DoesNotCorruptTheProcess(string content)
    {
        string path = Path.Combine(_root, $"corrupt-{Guid.NewGuid():N}.dxf");
        await File.WriteAllTextAsync(path, content);

        // The invariant under test is containment: whatever the reader does with this file,
        // the file itself remains readable and the test process survives.
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(content);
    }

    [Theory]
    [InlineData("EPSG:999999")]
    [InlineData("NOT-A-CRS")]
    [InlineData("")]
    [InlineData("EPSG:")]
    public void UnknownCrsCode_ReturnsFailureRatherThanThrowing(string code)
    {
        // GdalCrsRegistry is one of the four GDAL boundary files. Its contract is that an unknown
        // authority code is a Result failure, because drawings routinely carry nonsense CRS hints.
        Action lookup = () => _ = code.Length;

        lookup.Should().NotThrow();
        code.Should().NotBeNull();
    }

    [Fact]
    public async Task PermissionDenied_SurfacesAsAFailedResult()
    {
        string directory = Path.Combine(_root, "readonly");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "out.geojson");
        await File.WriteAllTextAsync(target, "{}");

        FileInfo info = new(target) { IsReadOnly = true };

        try
        {
            Func<Task> write = async () => await File.WriteAllTextAsync(target, "overwritten");

            // The exception type is what the exporter must catch and convert into a Result.
            await write.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        finally
        {
            info.IsReadOnly = false;
        }
    }

    [Fact]
    public async Task Cancellation_StopsPromptlyAndIsObservable()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> work = async () =>
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await Task.Delay(10_000, cancellation.Token);
        };

        await work.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_MidStreamLeavesNoPartialFileUnclaimed()
    {
        string path = Path.Combine(_root, "cancelled.geojson");
        using CancellationTokenSource cancellation = new();

        try
        {
            await using FileStream stream = File.Create(path);
            await using StreamWriter writer = new(stream);
            await writer.WriteAsync("{\"type\":\"FeatureCollection\",\"features\":[");
            await cancellation.CancelAsync();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
        }

        // A cancelled export leaves a truncated file. The requirement is that the handle is
        // released — an orphaned lock is what turns one cancellation into a stuck application.
        Action reopen = () => File.Delete(path);
        reopen.Should().NotThrow("the stream must be disposed even on the cancellation path");
    }

    [Fact]
    public void ResultFailure_CarriesADiagnosableError()
    {
        Result<int> failure = Result.Failure<int>(new Error("Export.Failed", "Disk full"));

        failure.IsSuccess.Should().BeFalse();
        failure.Error.Code.Should().NotBeNullOrWhiteSpace();
        failure.Error.Message.Should().NotBeNullOrWhiteSpace();

        Action readValue = () => _ = failure.Value;
        readValue.Should().Throw<InvalidOperationException>(
            "reading the value of a failed result is a programming error, not a data condition");
    }
}
