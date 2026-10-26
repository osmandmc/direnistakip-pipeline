using System.Collections.Concurrent;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Prompts;

/// <summary>The prompt a stage runs: its version (recorded with every answer) and its text with placeholders.</summary>
public sealed record ActivePrompt(string Version, string Template);

/// <summary>
/// Which prompt the AI stages run (B17): the version activated in the app, or the built-in one from the repository
/// (docs/extraction). Read from the store at most once a minute per prompt, so an activation reaches every instance
/// within a minute.
/// </summary>
public sealed class ActivePrompts(IPromptStore? store, TimeProvider clock)
{
    public const string Relevance = "relevance";
    public const string Extraction = "extraction";

    public static readonly IReadOnlyList<string> Names = [Relevance, Extraction];

    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (ActivePrompt Prompt, DateTimeOffset Until)> _cache = new();

    /// <summary>The repository's prompt.</summary>
    public static ActivePrompt Builtin(string name) => name switch
    {
        Relevance => new ActivePrompt(RelevancePrompt.Version, RelevancePrompt.TemplateText),
        Extraction => new ActivePrompt(ExtractionPrompt.Version, ExtractionPrompt.TemplateText),
        _ => throw new ArgumentException($"Unknown prompt '{name}'.", nameof(name)),
    };

    public async Task<ActivePrompt> GetAsync(string name, CancellationToken ct = default)
    {
        if (store is null)
            return Builtin(name);
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(name, out var cached) && cached.Until > now)
            return cached.Prompt;
        var active = await store.GetActiveAsync(name, ct);
        var prompt = active is null ? Builtin(name) : new ActivePrompt(active.Version, active.Text);
        _cache[name] = (prompt, now + CacheFor);
        return prompt;
    }

    /// <summary>After an activation on this instance: read the store again at the next use.</summary>
    public void Forget(string name) => _cache.TryRemove(name, out _);
}
