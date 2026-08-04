using System.Reflection;
using System.Runtime.Loader;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// A collectible load context holding one plugin and its private dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Two rules make this work, and both matter.
/// </para>
/// <para>
/// <b>Contract assemblies must not be duplicated.</b> If a plugin folder ships its own copy of
/// the SDK, loading it here would produce a second <c>IDataSourceReader</c> type with the same
/// name but a different identity, and every cast at the host boundary would fail with a message
/// claiming a type cannot be converted to itself. Names in the shared list therefore return
/// <see langword="null"/> from <see cref="Load"/>, which sends resolution to the default context.
/// </para>
/// <para>
/// <b>Everything else must be private.</b> An IFC plugin and a point-cloud plugin will sooner or
/// later carry incompatible versions of the same JSON or maths library. Resolving those through
/// the plugin's own <c>.deps.json</c> is what lets both load at once.
/// </para>
/// <para>
/// Native libraries are resolved the same way, which is what allows a LiDAR plugin to ship its own
/// native binaries without colliding with GDAL's.
/// </para>
/// </remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyList<string> _sharedAssemblies;

    /// <summary>Initializes a new instance of the <see cref="PluginLoadContext"/> class.</summary>
    /// <param name="name">Context name, used in diagnostics and memory dumps.</param>
    /// <param name="entryAssemblyPath">Path to the plugin's entry assembly.</param>
    /// <param name="sharedAssemblies">Assembly names resolved from the host. A trailing <c>*</c> is a prefix match.</param>
    public PluginLoadContext(string name, string entryAssemblyPath, IReadOnlyList<string> sharedAssemblies)
        : base(name, isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(sharedAssemblies);

        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _sharedAssemblies = sharedAssemblies;
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || IsShared(assemblyName.Name))
        {
            // Null defers to the default context, preserving a single type identity.
            return null;
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>Determines whether an assembly must resolve from the host.</summary>
    /// <param name="simpleName">The assembly simple name.</param>
    /// <returns><see langword="true"/> when the assembly is shared with the host.</returns>
    private bool IsShared(string simpleName)
    {
        foreach (string entry in _sharedAssemblies)
        {
            if (entry.EndsWith('*'))
            {
                if (simpleName.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(entry, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
