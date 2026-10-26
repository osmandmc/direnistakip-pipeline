using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>Prompt versions edited in the app (B17): every version kept; one active per prompt, or none (the built-in one).</summary>
public interface IPromptStore
{
    /// <exception cref="InvalidOperationException">The version exists already.</exception>
    Task SaveAsync(PromptVersionRecord version, CancellationToken ct = default);

    Task<PromptVersionRecord?> GetAsync(string name, string version, CancellationToken ct = default);

    /// <summary>The prompt's versions, newest first.</summary>
    Task<IReadOnlyList<PromptVersionRecord>> ListAsync(string name, CancellationToken ct = default);

    /// <summary>The active version, or null: the stage runs the built-in prompt.</summary>
    Task<PromptVersionRecord?> GetActiveAsync(string name, CancellationToken ct = default);

    /// <summary>Makes <paramref name="version"/> active (and saves its activation); null goes back to the built-in prompt.</summary>
    Task SetActiveAsync(string name, PromptVersionRecord? version, CancellationToken ct = default);
}
