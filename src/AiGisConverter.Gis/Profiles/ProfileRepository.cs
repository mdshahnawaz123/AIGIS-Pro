using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGisConverter.Domain.Common;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Profiles;

/// <summary>Supplies conversion profiles by identifier.</summary>
public interface IProfileRepository
{
    /// <summary>Gets every known profile, built-in and user-supplied.</summary>
    /// <returns>The profiles, ordered by identifier.</returns>
    IReadOnlyList<ConversionProfile> GetAll();

    /// <summary>Resolves a profile, applying inheritance.</summary>
    /// <param name="id">The profile identifier.</param>
    /// <returns>The fully resolved profile, or a failure when it is unknown.</returns>
    Result<ConversionProfile> Get(string id);
}

/// <summary>
/// Loads profiles from embedded defaults and from JSON on disk.
/// </summary>
/// <remarks>
/// <para>
/// A user file with the same identifier as a built-in replaces it entirely. That is what lets a
/// site correct a shipped profile &#8212; the Dubai Municipality CRS, say, if the submission
/// specification changes &#8212; without waiting for a release.
/// </para>
/// <para>
/// Repository rather than factory: profiles are stored data with identity, not constructed
/// objects, and the useful operations on them are "list" and "get by id".
/// </para>
/// </remarks>
public sealed class ProfileRepository : IProfileRepository
{
    private const string BuiltInResourcePrefix = "AiGisConverter.Gis.Profiles.BuiltIn.";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, ConversionProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ProfileRepository> _logger;

    /// <summary>Initializes a new instance of the <see cref="ProfileRepository"/> class.</summary>
    /// <param name="options">GIS settings supplying the profile search paths.</param>
    /// <param name="logger">Logger for load diagnostics.</param>
    public ProfileRepository(IOptions<GisOptions> options, ILogger<ProfileRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        LoadBuiltIn();
        LoadFromDisk(options.Value);
    }

    /// <inheritdoc />
    public IReadOnlyList<ConversionProfile> GetAll() =>
        [.. _profiles.Values.OrderBy(static profile => profile.Id, StringComparer.Ordinal)];

    /// <inheritdoc />
    public Result<ConversionProfile> Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result.Failure<ConversionProfile>(new Error("Gis.ProfileNotSpecified", "No profile was named."));
        }

        if (!_profiles.TryGetValue(id, out ConversionProfile? profile))
        {
            return Result.Failure<ConversionProfile>(new Error(
                "Gis.ProfileNotFound",
                $"Profile '{id}' is not known. Available profiles: {string.Join(", ", _profiles.Keys.Order(StringComparer.Ordinal))}."));
        }

        return Resolve(profile, []);
    }

    /// <summary>
    /// Folds a profile's inheritance chain, innermost first.
    /// </summary>
    /// <remarks>
    /// Cycles are detected rather than allowed to recurse: a profile that extends itself, directly
    /// or through a chain, is a configuration error and must say so instead of overflowing.
    /// </remarks>
    private Result<ConversionProfile> Resolve(ConversionProfile profile, HashSet<string> visited)
    {
        if (!visited.Add(profile.Id))
        {
            return Result.Failure<ConversionProfile>(new Error(
                "Gis.ProfileCycle",
                $"Profile '{profile.Id}' takes part in an inheritance cycle."));
        }

        if (string.IsNullOrWhiteSpace(profile.Extends))
        {
            return Result.Success(profile);
        }

        if (!_profiles.TryGetValue(profile.Extends, out ConversionProfile? parent))
        {
            return Result.Failure<ConversionProfile>(new Error(
                "Gis.ProfileParentNotFound",
                $"Profile '{profile.Id}' extends '{profile.Extends}', which does not exist."));
        }

        Result<ConversionProfile> resolvedParent = Resolve(parent, visited);

        return resolvedParent.IsFailure ? resolvedParent : Result.Success(Merge(resolvedParent.Value, profile));
    }

    /// <summary>Overlays a child profile onto its parent. Any value the child states wins.</summary>
    private static ConversionProfile Merge(ConversionProfile parent, ConversionProfile child)
    {
        ConversionProfile merged = new()
        {
            Id = child.Id,
            Name = string.IsNullOrWhiteSpace(child.Name) ? parent.Name : child.Name,
            Description = child.Description ?? parent.Description,
            OutputCrs = child.OutputCrs ?? parent.OutputCrs,
            ExportFormat = child.ExportFormat ?? parent.ExportFormat,
            PrecisionScale = child.PrecisionScale ?? parent.PrecisionScale,
            ChordTolerance = child.ChordTolerance ?? parent.ChordTolerance,
            SimplificationTolerance = child.SimplificationTolerance ?? parent.SimplificationTolerance,
            Naming = child.Naming,
            Geometry = child.Geometry,
            Qa = child.Qa,
        };

        foreach (KeyValuePair<string, string> pair in parent.LayerMapping)
        {
            merged.LayerMapping[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, string> pair in child.LayerMapping)
        {
            merged.LayerMapping[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, string> pair in parent.AttributeMapping)
        {
            merged.AttributeMapping[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, string> pair in child.AttributeMapping)
        {
            merged.AttributeMapping[pair.Key] = pair.Value;
        }

        merged.ExcludedAttributes = [.. parent.ExcludedAttributes.Union(child.ExcludedAttributes, StringComparer.OrdinalIgnoreCase)];

        return merged;
    }

    private void LoadBuiltIn()
    {
        Assembly assembly = typeof(ProfileRepository).Assembly;

        foreach (string name in assembly.GetManifestResourceNames()
            .Where(static n => n.StartsWith(BuiltInResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            try
            {
                ConversionProfile? profile = JsonSerializer.Deserialize<ConversionProfile>(stream, SerializerOptions);

                if (profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
                {
                    _profiles[profile.Id] = profile;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Built-in profile resource {Resource} is malformed.", name);
            }
        }

        _logger.LogInformation("Loaded {Count} built-in conversion profiles.", _profiles.Count);
    }

    private void LoadFromDisk(GisOptions options)
    {
        foreach (string searchPath in options.ProfileSearchPaths)
        {
            string root = Environment.ExpandEnvironmentVariables(searchPath);

            if (!Path.IsPathRooted(root))
            {
                root = Path.Combine(AppContext.BaseDirectory, root);
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    ConversionProfile? profile = JsonSerializer.Deserialize<ConversionProfile>(stream, SerializerOptions);

                    if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
                    {
                        continue;
                    }

                    bool replaced = _profiles.ContainsKey(profile.Id);
                    _profiles[profile.Id] = profile;

                    _logger.LogInformation(
                        replaced
                            ? "Profile {ProfileId} from {File} replaced the built-in of the same name."
                            : "Loaded profile {ProfileId} from {File}.",
                        profile.Id,
                        file);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Profile file {File} is malformed and was ignored.", file);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Profile file {File} could not be read.", file);
                }
            }
        }
    }
}
