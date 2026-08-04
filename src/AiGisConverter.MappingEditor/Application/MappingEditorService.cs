using System.Collections.Generic;
using AiGisConverter.Business.Classification;
using AiGisConverter.MappingEditor.Business;

namespace AiGisConverter.MappingEditor.Application;

public class MappingEditorService : IMappingEditorService
{
    private readonly ProfileManager _profileManager;

    public MappingEditorService()
    {
        _profileManager = new ProfileManager();
    }

    public IReadOnlyList<MappingProfile> GetProfiles()
    {
        return _profileManager.LoadAllProfiles();
    }

    public void SaveProfile(MappingProfile profile, string filename)
    {
        _profileManager.SaveProfile(profile, filename);
    }

    public MappingProfile CloneProfile(MappingProfile source, string newName, string newProfileId)
    {
        return _profileManager.CloneProfile(source, newName, newProfileId);
    }
}
