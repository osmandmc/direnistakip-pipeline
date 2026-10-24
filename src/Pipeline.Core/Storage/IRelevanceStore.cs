using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>Relevance assessments (D-003, D-007): saved as new records, never overwritten.</summary>
public interface IRelevanceStore
{
    Task SaveAsync(RelevanceRecord record, CancellationToken ct = default);
}
