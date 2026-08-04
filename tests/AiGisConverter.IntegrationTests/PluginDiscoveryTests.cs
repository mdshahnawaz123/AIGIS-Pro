using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Hosting;
using AiGisConverter.Plugins.Hosting.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Runtime plugin discovery against the real staged output.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a defect that every other test in the solution was blind to. The rule
/// that copies <c>plugin.json</c> into the build output sat in <c>Directory.Build.props</c>, which
/// MSBuild imports before the SDK creates its default item globs, so the <c>Update</c> matched
/// nothing and failed without a word. Thirteen plugins compiled, staged, and were undiscoverable.
/// </para>
/// <para>
/// Nothing caught it. Every reader test constructs its reader directly &#8212;
/// <c>new IfcReader(...)</c> &#8212; so a suite of hundreds of passing tests said nothing about
/// whether a single plugin could be found. These tests close that gap by going the way the
/// application goes: the real <see cref="PluginDiscovery"/>, resolved from a real container,
/// pointed at the real <c>artifacts/plugins</c> folder, reading the manifests that were actually
/// shipped.
/// </para>
/// <para>
/// No reader, provider or plugin instance is constructed here. Discovery is a question about files
/// on disk, and answering it by instantiating the thing under test would defeat the purpose.
/// </para>
/// </remarks>
public sealed class PluginDiscoveryTests
{
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Gets the repository root, located by walking up from the test assembly.</summary>
    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null
                && !Directory.Exists(Path.Combine(directory.FullName, "artifacts", "plugins")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? string.Empty;
        }
    }

    /// <summary>Gets the staged plugin folder the host would scan in a real deployment.</summary>
    private static string StagedPluginRoot => Path.Combine(RepositoryRoot, "artifacts", "plugins");

    /// <summary>Gets the plugin source projects, used to prove nothing was left undeployed.</summary>
    private static string PluginSourceRoot => Path.Combine(RepositoryRoot, "plugins");

    /// <summary>
    /// Resolves the real discovery service from a real container, scanning only the staged folder.
    /// </summary>
    /// <remarks>
    /// <see cref="PluginOptions.SearchPaths"/> is replaced rather than added to. Its defaults
    /// include <c>%LOCALAPPDATA%\AiGisConverter\Plugins</c>, and a developer with a stray plugin
    /// left in their profile would otherwise see a different count than the build agent.
    /// </remarks>
    /// <returns>The container and the discovery service resolved from it.</returns>
    private static (ServiceProvider Provider, IPluginDiscovery Discovery) BuildDiscovery()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddPluginSystem(configuration);

        string staged = StagedPluginRoot;

        services.PostConfigure<PluginOptions>(options =>
        {
            options.SearchPaths.Clear();
            options.SearchPaths.Add(staged);
        });

        ServiceProvider provider = services.BuildServiceProvider();

        return (provider, provider.GetRequiredService<IPluginDiscovery>());
    }

    /// <summary>Reads a manifest straight from disk, independently of the code under test.</summary>
    /// <param name="manifestPath">Path to a <c>plugin.json</c>.</param>
    /// <returns>The parsed manifest.</returns>
    private static PluginManifest ReadManifest(string manifestPath)
    {
        using FileStream stream = File.OpenRead(manifestPath);

        return JsonSerializer.Deserialize<PluginManifest>(stream, ManifestOptions)
            ?? throw new InvalidOperationException($"'{manifestPath}' deserialised to null.");
    }

    /// <summary>Gets every staged folder that contains a manifest.</summary>
    /// <returns>The folder paths.</returns>
    private static IReadOnlyList<string> StagedFoldersWithManifest() =>
        Directory.Exists(StagedPluginRoot)
            ? [.. Directory.EnumerateDirectories(StagedPluginRoot)
                .Where(d => File.Exists(Path.Combine(d, PluginManifest.FileName)))
                .Order(StringComparer.Ordinal)]
            : [];

    /// <summary>Gets the ids declared by every plugin project in the source tree.</summary>
    /// <returns>The plugin identifiers.</returns>
    private static IReadOnlyList<string> SourcePluginIds() =>
        Directory.Exists(PluginSourceRoot)
            ? [.. Directory.EnumerateDirectories(PluginSourceRoot)
                .Select(d => Path.Combine(d, PluginManifest.FileName))
                .Where(File.Exists)
                .Select(m => ReadManifest(m).Id)
                .Order(StringComparer.Ordinal)]
            : [];

    // ---- the folder itself ----------------------------------------------------------------------

    [Fact]
    public void StagedPluginFolder_Exists_AndTheTestIsActuallyPointedAtIt()
    {
        // Guards the test rather than the product: if the root walk ever failed, every assertion
        // below would pass vacuously against an empty folder.
        RepositoryRoot.Should().NotBeEmpty("the repository root must be locatable from the test assembly");
        Directory.Exists(StagedPluginRoot).Should().BeTrue(
            $"'{StagedPluginRoot}' is where the host looks for plugins; build the solution first");
    }

    [Fact]
    public void EveryPluginProject_ShippedItsManifest()
    {
        // The exact regression that started this. A project builds, stages its assembly, and omits
        // the manifest - after which the host cannot tell it is a plugin at all.
        IReadOnlyList<string> projects =
            [.. Directory.EnumerateDirectories(PluginSourceRoot)
                .Where(d => File.Exists(Path.Combine(d, PluginManifest.FileName)))];

        projects.Should().NotBeEmpty("the source tree must contain plugin projects");

        List<string> missing = [];

        foreach (string project in projects)
        {
            string staged = Path.Combine(StagedPluginRoot, Path.GetFileName(project), PluginManifest.FileName);

            if (!File.Exists(staged))
            {
                missing.Add(Path.GetFileName(project));
            }
        }

        missing.Should().BeEmpty(
            "every plugin project must copy plugin.json into artifacts/plugins; missing: "
            + string.Join(", ", missing));
    }

    // ---- discovery ------------------------------------------------------------------------------

    [Fact]
    public async Task Discovery_FindsEveryStagedPlugin()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();
        IReadOnlyList<string> stagedFolders = StagedFoldersWithManifest();

        // A folder holding a manifest that yields no descriptor means the manifest was unreadable.
        // PluginDiscovery logs and skips in that case, so without this the failure stays silent.
        IReadOnlyList<string> discoveredFolders =
            [.. discovered.Select(static d => d.Directory).Order(StringComparer.Ordinal)];

        IReadOnlyList<string> skipped = [.. stagedFolders.Except(discoveredFolders, StringComparer.OrdinalIgnoreCase)];

        skipped.Should().BeEmpty(
            "a staged folder with a manifest that produces no descriptor means the manifest is "
            + "malformed or unreadable; skipped: "
            + string.Join(", ", skipped.Select(Path.GetFileName)));

        discovered.Should().HaveCount(stagedFolders.Count);
    }

    [Fact]
    public async Task Discovery_ReturnsTheExpectedPluginCount()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();
        IReadOnlyList<string> expectedIds = SourcePluginIds();

        // Derived from the source tree rather than hard-coded, so adding a fourteenth plugin does
        // not fail spuriously - while still failing loudly if a plugin stops being deployed. The
        // floor keeps the derivation itself honest: if it ever collapsed to zero, both sides of the
        // comparison would agree and the test would pass while checking nothing.
        expectedIds.Should().HaveCountGreaterThanOrEqualTo(13,
            "the solution had 13 plugin projects when this test was written");

        discovered.Select(static d => d.Id).Order(StringComparer.Ordinal)
            .Should().Equal(expectedIds);
    }

    [Fact]
    public async Task EveryDiscoveredPlugin_IsACandidateForLoading()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();

        // Discovered is the only healthy outcome here: Rejected means an SDK-version or entry-point
        // problem, Disabled means configuration is switching a shipped plugin off unexpectedly.
        IReadOnlyList<string> notCandidates =
            [.. discovered.Where(static d => d.State != PluginLoadState.Discovered)
                .Select(static d => $"{d.Id} [{d.State}] {d.FailureReason}")];

        notCandidates.Should().BeEmpty(
            "every shipped plugin should be loadable: " + string.Join("; ", notCandidates));
    }

    [Fact]
    public async Task DiscoveredMetadata_MatchesTheShippedManifest()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();

        discovered.Should().NotBeEmpty();

        foreach (PluginDescriptor descriptor in discovered)
        {
            // Parsed again here with plain System.Text.Json, so a binding mistake inside discovery
            // shows up as a disagreement rather than being reproduced identically on both sides.
            PluginManifest onDisk = ReadManifest(Path.Combine(descriptor.Directory, PluginManifest.FileName));
            PluginManifest read = descriptor.Manifest;
            string who = Path.GetFileName(descriptor.Directory);

            read.Id.Should().Be(onDisk.Id, $"{who} id");
            read.Name.Should().Be(onDisk.Name, $"{who} name");
            read.Version.Should().Be(onDisk.Version, $"{who} version");
            read.SdkVersion.Should().Be(onDisk.SdkVersion, $"{who} sdkVersion");
            read.EntryAssembly.Should().Be(onDisk.EntryAssembly, $"{who} entryAssembly");
            read.EntryType.Should().Be(onDisk.EntryType, $"{who} entryType");
            read.Isolation.Should().Be(onDisk.Isolation, $"{who} isolation");
            read.Enabled.Should().Be(onDisk.Enabled, $"{who} enabled");
            read.LoadOrder.Should().Be(onDisk.LoadOrder, $"{who} loadOrder");
            read.Capabilities.Should().BeEquivalentTo(onDisk.Capabilities, $"{who} capabilities");

            descriptor.Id.Should().Be(onDisk.Id, $"{who} descriptor id tracks the manifest");
        }
    }

    [Fact]
    public async Task EveryDiscoveredPlugin_DeclaresAtLeastOneCapability()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();

        // A plugin contributing nothing loads successfully and does nothing, which is worse than
        // failing, because there is no symptom to investigate.
        discovered.Should().OnlyContain(d => d.Manifest.Capabilities.Count > 0);
    }

    // ---- assemblies ------------------------------------------------------------------------------

    [Fact]
    public async Task EveryDiscoveredPlugin_HasAnEntryAssemblyThatLoads()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();
        List<string> failures = [];

        foreach (PluginDescriptor descriptor in discovered)
        {
            string assemblyPath = descriptor.GetEntryAssemblyPath();

            if (!File.Exists(assemblyPath))
            {
                failures.Add($"{descriptor.Id}: entry assembly '{assemblyPath}' is not on disk");
                continue;
            }

            // A collectible probe, not a second loader. The host's own PluginLoadContext is
            // internal and applies isolation policy; all this needs to answer is whether the
            // assembly is loadable and its entry type resolvable. Contract assemblies come from
            // the default context, exactly as the shared-assembly list intends.
            AssemblyLoadContext context = new($"probe-{descriptor.Id}", isCollectible: true);
            AssemblyDependencyResolver resolver = new(assemblyPath);

            context.Resolving += (loadContext, name) =>
            {
                string? dependency = resolver.ResolveAssemblyToPath(name);

                return dependency is null ? null : loadContext.LoadFromAssemblyPath(dependency);
            };

            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);

                if (descriptor.Manifest.EntryType is { Length: > 0 } entryType)
                {
                    Type? type = assembly.GetType(entryType, throwOnError: false);

                    if (type is null)
                    {
                        failures.Add($"{descriptor.Id}: entryType '{entryType}' is not in {assembly.GetName().Name}");
                    }
                    else if (!typeof(IPlugin).IsAssignableFrom(type))
                    {
                        failures.Add($"{descriptor.Id}: '{entryType}' does not implement IPlugin");
                    }
                }
            }
            catch (Exception exception) when (exception is BadImageFormatException
                                                  or FileLoadException
                                                  or FileNotFoundException
                                                  or TypeLoadException)
            {
                failures.Add($"{descriptor.Id}: {exception.GetType().Name} - {exception.Message}");
            }
            finally
            {
                context.Unload();
            }
        }

        failures.Should().BeEmpty(
            "every shipped plugin assembly must load and expose its entry type: "
            + string.Join("; ", failures));
    }

    [Fact]
    public async Task EveryEntryAssembly_SitsBesideItsManifest()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();

        // The folder-per-plugin layout is the deployment contract a third-party installer targets.
        // An entry assembly resolved from anywhere else would work here and fail once installed.
        foreach (PluginDescriptor descriptor in discovered)
        {
            string expected = Path.Combine(descriptor.Directory, descriptor.Manifest.EntryAssembly);

            File.Exists(expected).Should().BeTrue(
                $"{descriptor.Id} must ship '{descriptor.Manifest.EntryAssembly}' in its own folder");
        }
    }

    // ---- the Revit plugin specifically -------------------------------------------------------------

    [Fact]
    public async Task RevitPlugin_IsDiscoveredWithItsBridgeConfiguration()
    {
        (ServiceProvider provider, IPluginDiscovery discovery) = BuildDiscovery();
        await using ServiceProvider _ = provider;

        IReadOnlyList<PluginDescriptor> discovered = await discovery.DiscoverAsync();

        PluginDescriptor revit = discovered.Should()
            .ContainSingle(d => d.Id == "aigis.reader.revit").Subject;

        revit.State.Should().Be(PluginLoadState.Discovered);
        revit.Manifest.Isolation.Should().Be(PluginIsolationMode.OutOfProcess,
            "the Revit API only functions inside Revit's own process");

        // The host requirement is what tells the shell to offer "start Revit" rather than failing.
        revit.Manifest.HostApplication.Should().NotBeNull();
        revit.Manifest.HostApplication!.Name.Should().Be("Revit");
        revit.Manifest.HostApplication.PipeName.Should().NotBeNullOrWhiteSpace();
    }
}
