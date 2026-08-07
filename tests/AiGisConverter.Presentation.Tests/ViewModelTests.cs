using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Hosting;
using AiGisConverter.Presentation.Services;
using AiGisConverter.Presentation.ViewModels;

namespace AiGisConverter.Presentation.Tests;

/// <summary>
/// View-model tests. None of these opens a window.
/// </summary>
/// <remarks>
/// That is the point of <see cref="IUiDispatcher"/> and <see cref="IDialogService"/>: a view model
/// that reached for a <c>Dispatcher</c> or opened a dialog directly could only be exercised by a
/// human clicking through it.
/// </remarks>
public sealed class ViewModelTests
{
    /// <summary>A dispatcher that runs work straight away, as tests have no interface thread.</summary>
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool IsOnUiThread => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();

            return Task.CompletedTask;
        }
    }

    private static IDataSourceReaderCatalog Catalog(params string[] extensions)
    {
        IDataSourceReaderCatalog catalog = Substitute.For<IDataSourceReaderCatalog>();
        catalog.GetSupportedExtensions().Returns(extensions);

        return catalog;
    }

    private static IDialogService Dialogs(IReadOnlyList<string>? drawings = null, string? folder = null)
    {
        IDialogService dialogs = Substitute.For<IDialogService>();
        dialogs.PickDrawings(Arg.Any<IReadOnlyList<string>>()).Returns(drawings ?? []);
        dialogs.PickOutputFolder(Arg.Any<string?>()).Returns(folder);

        return dialogs;
    }

    private static ICrsCatalog CrsCatalog()
    {
        // The selectors only need a catalogue to search; these tests set the identifier directly,
        // so an unavailable catalogue (no proj.db) is exactly the right stand-in.
        ICrsCatalog catalog = Substitute.For<ICrsCatalog>();
        catalog.IsAvailable.Returns(false);
        catalog.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CrsCatalogEntry>>([]));

        return catalog;
    }

    private static ICrsSuggester CrsSuggester()
    {
        ICrsSuggester suggester = Substitute.For<ICrsSuggester>();
        suggester.SuggestAsync(Arg.Any<Domain.ValueObjects.Extent>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CrsSuggestion>>([]));

        return suggester;
    }

    private static ICrsPreferences CrsPreferences()
    {
        ICrsPreferences preferences = Substitute.For<ICrsPreferences>();
        preferences.Recent.Returns([]);
        preferences.Favourites.Returns([]);

        return preferences;
    }

    private static ICrsValidator CrsValidator()
    {
        ICrsValidator validator = Substitute.For<ICrsValidator>();
        validator.ValidateAsync(Arg.Any<CrsValidationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CrsValidationReport([])));

        return validator;
    }

    private static ProjectViewModel Project(
        IReadOnlyList<string>? drawings = null,
        string? folder = null) =>
        new(Catalog(".dxf", ".dwg"), Dialogs(drawings, folder), CrsCatalog(), CrsSuggester(), CrsPreferences(), CrsValidator(), Transformer(), PluginHost(), Capabilities());

    /// <summary>A host with no plugins loaded, so no live session is offered.</summary>
    /// <remarks>
    /// These tests are about the drawing list and the coordinate-system selectors. A plugin host
    /// with nothing in it keeps the live-session path out of their way without pretending it is
    /// absent from the view model.
    /// </remarks>
    private static IPluginHost PluginHost()
    {
        IPluginHost host = Substitute.For<IPluginHost>();
        host.Plugins.Returns([]);

        return host;
    }

    private static ICapabilityRegistry Capabilities()
    {
        ICapabilityRegistry registry = Substitute.For<ICapabilityRegistry>();
        registry.GetCapabilitiesWithSource<IDataSourceReader>().Returns([]);

        return registry;
    }

    private static ICoordinateTransformer Transformer()
    {
        ICoordinateTransformer transformer = Substitute.For<ICoordinateTransformer>();
        transformer.CanTransform(Arg.Any<Domain.ValueObjects.CoordinateSystem>(), Arg.Any<Domain.ValueObjects.CoordinateSystem>())
            .Returns(true);

        return transformer;
    }

    [Fact]
    public void Project_OffersTheExtensionsReadersActuallyClaim() =>
        Project().SupportedExtensions.Should().Contain(".dxf").And.Contain(".dwg",
            "installing a plugin should widen the dialog without anyone editing a list");

    [Fact]
    public void Project_AddDrawings_IgnoresDuplicates()
    {
        ProjectViewModel project = Project([@"C:\a.dxf", @"C:\a.dxf", @"C:\b.dxf"]);

        project.AddDrawingsCommand.Execute(null);

        project.Drawings.Should().HaveCount(2);
    }

    [Fact]
    public void Project_CannotConvertWithoutBothDrawingsAndAnOutputFolder()
    {
        ProjectViewModel project = Project([@"C:\a.dxf"]);
        project.AddDrawingsCommand.Execute(null);

        project.CanConvert.Should().BeFalse("no output folder has been chosen");

        project.OutputFolder = @"C:\out";
        project.CanConvert.Should().BeTrue();
    }

    [Fact]
    public void Project_RemoveDrawing_UpdatesTheList()
    {
        ProjectViewModel project = Project([@"C:\a.dxf", @"C:\b.dxf"]);
        project.AddDrawingsCommand.Execute(null);

        project.RemoveDrawingCommand.Execute(@"C:\a.dxf");

        project.Drawings.Should().ContainSingle().Which.Should().Be(@"C:\b.dxf");
    }

    [Fact]
    public void Project_BuildProject_PassesEverythingThroughTheDomainFactories()
    {
        ProjectViewModel project = Project([@"C:\a.dxf"], @"C:\out");
        project.AddDrawingsCommand.Execute(null);
        project.ChooseOutputFolderCommand.Execute(null);

        project.ProjectName = "Site survey";
        project.OutputCrsSelector.SelectedIdentifier = "EPSG:27700";
        project.ExportFormat = ExportFormat.GeoPackage;
        project.ConfidenceThreshold = 0.8d;

        ConversionProject built = project.BuildProject();

        built.Name.Should().Be("Site survey");
        built.Settings.TargetCoordinateSystem.Code.Should().Be(27700);
        built.Settings.ExportFormats.Should().ContainSingle().Which.Should().Be(ExportFormat.GeoPackage);
        built.Settings.ConfidenceThreshold.Should().BeApproximately(0.8d, 1e-9d);
        built.Jobs.Should().ContainSingle();
    }

    [Fact]
    public void Project_BuildProject_RejectsAnUnparseableCoordinateSystem()
    {
        ProjectViewModel project = Project([@"C:\a.dxf"], @"C:\out");
        project.AddDrawingsCommand.Execute(null);
        project.OutputCrsSelector.SelectedIdentifier = "not a crs";

        Action act = () => project.BuildProject();

        act.Should().Throw<Domain.Exceptions.InvalidCoordinateSystemException>(
            "the domain rejects it, so a script and the window fail the same way");
    }

    [Fact]
    public void Plugins_ListsRejectedOnesToo()
    {
        PluginsViewModel plugins = new();

        plugins.Load(
        [
            Descriptor("good", PluginLoadState.Loaded),
            Descriptor("bad", PluginLoadState.Rejected, "incompatible SDK"),
        ]);

        plugins.Plugins.Should().HaveCount(2,
            "a plugin that silently does not appear is the hardest kind of problem to diagnose");
        plugins.Summary.Should().Contain("1 of 2");
        plugins.Plugins.Should().Contain(p => p.Detail.Contains("incompatible SDK"));
    }

    [Fact]
    public void Plugins_EmptyDiscovery_SaysSo()
    {
        PluginsViewModel plugins = new();
        plugins.Load([]);

        plugins.Summary.Should().Contain("No plugins");
    }

    private static PluginDescriptor Descriptor(string id, PluginLoadState state, string? reason = null) =>
        new(new PluginManifest { Id = id, Name = id, EntryAssembly = $"{id}.dll" }, @"C:\plugins")
        {
            State = state,
            FailureReason = reason,
        };
}
