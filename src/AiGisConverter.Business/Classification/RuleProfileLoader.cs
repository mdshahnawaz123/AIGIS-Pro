using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Business.Classification;

/// <summary>
/// Loads mapping profiles from external JSON files in a directory structure.
/// </summary>
public sealed class RuleProfileLoader
{
    private readonly ILogger<RuleProfileLoader> _logger;
    private readonly ClassificationEngine _engine;
    private readonly string _baseRulesDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleProfileLoader"/> class.
    /// </summary>
    /// <param name="engine">The engine.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="baseRulesDirectory">The base directory.</param>
    public RuleProfileLoader(ClassificationEngine engine, ILogger<RuleProfileLoader> logger, string baseRulesDirectory = "Rules")
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(logger);
        
        _engine = engine;
        _logger = logger;
        _baseRulesDirectory = baseRulesDirectory;
    }

    /// <summary>
    /// Scans the rules directory and loads all JSON profiles into the ClassificationEngine.
    /// </summary>
    public void LoadProfiles()
    {
        if (!Directory.Exists(_baseRulesDirectory))
        {
            _logger.LogWarning("Rules directory '{RulesDir}' does not exist. No classification rules loaded.", _baseRulesDirectory);
            return;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var files = Directory.GetFiles(_baseRulesDirectory, "*.json", SearchOption.AllDirectories);
        int loadedCount = 0;

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<MappingProfile>(json, jsonOptions);

                if (profile != null)
                {
                    _engine.AddProfile(profile);
                    loadedCount++;
                    _logger.LogInformation("Loaded rule profile '{ProfileName}' ({ProfileId}) with {RuleCount} rules from {FilePath}", 
                        profile.Name, profile.ProfileId, profile.Rules.Count, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load rule profile from {FilePath}", file);
            }
        }

        _logger.LogInformation("Loaded {LoadedCount} rule profiles from {RulesDir}", loadedCount, _baseRulesDirectory);
    }
}
