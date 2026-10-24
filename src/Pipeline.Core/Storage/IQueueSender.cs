namespace Pipeline.Core.Storage;

/// <summary>Hands an article to the next stage (D-014, D-021).</summary>
public interface IQueueSender
{
    Task SendAsync<T>(string queueName, T message, CancellationToken ct = default);

    /// <summary>The message becomes visible only after <paramref name="delay"/> (a stage waiting out a rate limit).</summary>
    Task SendLaterAsync<T>(string queueName, T message, TimeSpan delay, CancellationToken ct = default);
}

/// <summary>Queue names; must match infra/main.bicep.</summary>
public static class QueueNames
{
    public const string ArticleSubmitted = "article-submitted";
    public const string ArticleFetched = "article-fetched";
    public const string ArticleParsed = "article-parsed";
    public const string ArticleRelevant = "article-relevant";
}
