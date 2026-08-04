namespace AiGisConverter.Gis.Tests.TestSupport;

/// <summary>A temporary directory that removes itself.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace() => Root = Directory.CreateTempSubdirectory("aigis-gis-tests").FullName;

    public string Root { get; }

    public string Path(string fileName) => System.IO.Path.Combine(Root, fileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A writer still holding a handle must not fail the test that already passed.
        }
    }
}
