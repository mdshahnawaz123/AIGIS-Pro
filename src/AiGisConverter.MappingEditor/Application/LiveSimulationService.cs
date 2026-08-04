using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiGisConverter.Business.Classification;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.MappingEditor.Business;

namespace AiGisConverter.MappingEditor.Application;

public class LiveSimulationService
{
    private readonly RuleSimulator _simulator;
    private readonly IDataSourceReaderCatalog _readerCatalog;
    private IReadOnlyList<SourceElement> _currentElements = new List<SourceElement>();

    public LiveSimulationService(RuleSimulator simulator, IDataSourceReaderCatalog readerCatalog)
    {
        _simulator = simulator;
        _readerCatalog = readerCatalog;

        // Deliberately starts empty. Rule preview runs against the drawing in the current
        // conversion session; inventing sample geometry here is what put fake roads and trees on
        // the map before a drawing had ever been opened.
    }

    /// <summary>
    /// Replaces the elements rules are previewed against with those of the current drawing.
    /// </summary>
    /// <param name="elements">The source elements of the drawing in the active session.</param>
    public void SetElements(IReadOnlyList<SourceElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _currentElements = elements;
    }


    public async Task LoadSourceAsync(string path)
    {
        var reference = new SourceReference(path);
        var reader = _readerCatalog.FindReader(reference);
        if (reader != null)
        {
            var result = await reader.ReadAsync(reference, default);
            if (result.IsSuccess)
            {
                _currentElements = result.Value.Layers.SelectMany(l => l.Elements).ToList();
            }
        }
    }

    public IReadOnlyList<SourceElement> GetElements()
    {
        return _currentElements;
    }

    public IReadOnlyList<SimulationResult> Simulate(MappingProfile profile)
    {
        return _simulator.Simulate(profile, _currentElements);
    }
}
