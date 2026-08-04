using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// An optional interface for plugins that can extract rich semantic meaning and relationship graphs
/// from their underlying source data.
/// </summary>
public interface ISemanticProvider
{
    /// <summary>
    /// Processes a stream of raw source elements into a semantic graph.
    /// </summary>
    /// <param name="elements">The raw elements provided by the generic reader pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A graph containing the enriched features and their relationships.</returns>
    Task<SemanticGraph> ExtractSemanticsAsync(
        IAsyncEnumerable<SourceElement> elements, 
        CancellationToken cancellationToken = default);
}
