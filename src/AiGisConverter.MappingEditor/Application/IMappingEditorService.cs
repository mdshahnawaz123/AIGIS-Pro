using System.Collections.Generic;
using AiGisConverter.Business.Classification;

namespace AiGisConverter.MappingEditor.Application;

public interface IMappingEditorService
{
    IReadOnlyList<MappingProfile> GetProfiles();
    void SaveProfile(MappingProfile profile, string filename);
    MappingProfile CloneProfile(MappingProfile source, string newName, string newProfileId);
}
