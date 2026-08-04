using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGisConverter.Business.Classification;

namespace AiGisConverter.MappingEditor.Business;

public class ProfileManager
{
    private readonly string _rulesDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProfileManager(string rulesDirectory = "Rules")
    {
        _rulesDirectory = rulesDirectory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        if (!Directory.Exists(_rulesDirectory))
        {
            Directory.CreateDirectory(_rulesDirectory);
        }
    }

    public IReadOnlyList<MappingProfile> LoadAllProfiles()
    {
        var profiles = new List<MappingProfile>();
        if (!Directory.Exists(_rulesDirectory))
        {
            return profiles;
        }

        var files = Directory.GetFiles(_rulesDirectory, "*.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            catch (Exception ex)
            {
                // In a real app we'd log this, but we'll rethrow or ignore for the editor depending on strictness
                Console.WriteLine($"Error loading {file}: {ex.Message}");
            }
        }
        return profiles;
    }

    public void SaveProfile(MappingProfile profile, string filename)
    {
        var path = Path.Combine(_rulesDirectory, filename);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path += ".json";
        }
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(path, json);
    }

    public MappingProfile CloneProfile(MappingProfile source, string newName, string newProfileId)
    {
        var json = JsonSerializer.Serialize(source, _jsonOptions);
        var clone = JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions)!;
        clone.Name = newName;
        clone.ProfileId = newProfileId;
        return clone;
    }
}
