namespace Pipeline.Core.Relevance;

/// <summary>
/// An LLM that answers a prompt with JSON (the provider is chosen by configuration; open decision #7). Kept small so
/// another provider is one more adapter.
/// </summary>
public interface ILanguageModel
{
    /// <summary>False when no key is configured: the AI stages then leave articles alone.</summary>
    bool IsConfigured { get; }

    /// <summary>The model's text answer (JSON) and the exact model that gave it.</summary>
    /// <exception cref="ModelRateLimitedException">Too many requests now, the day's quota is used up, or the model is overloaded.</exception>
    Task<ModelAnswer> AnswerJsonAsync(string prompt, CancellationToken ct = default);
}

public sealed record ModelAnswer(string Text, string Model);

/// <summary>The provider asked us to slow down, or is overloaded; the article is tried again after <see cref="RetryAfter"/>.</summary>
public sealed class ModelRateLimitedException(TimeSpan retryAfter, string message) : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
