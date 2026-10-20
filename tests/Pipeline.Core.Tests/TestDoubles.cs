using System.Net;
using Pipeline.Contracts;
using Pipeline.Core.Articles;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests;

// In-memory stand-ins for the storage interfaces. The real Azure implementations are
// tested against emulators in Pipeline.Infrastructure.Tests.

internal sealed class InMemoryLedger : ILedgerStore
{
    public Dictionary<string, LedgerRecord> Records { get; } = [];

    public Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default) =>
        Task.FromResult(Records.TryAdd(record.Id, record));

    public Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default) =>
        Task.FromResult(Records.GetValueOrDefault(articleId));

    public Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default)
    {
        var record = Records[articleId];
        Records[articleId] = record with { Stages = new Dictionary<Stage, StageResult>(record.Stages) { [stage] = result } };
        return Task.CompletedTask;
    }

    public Task SetSummaryAsync(string articleId, string? title, DateTimeOffset? publishedAt, CancellationToken ct = default)
    {
        var record = Records[articleId];
        var date = publishedAt is { } d ? TurkeyTime.Normalize(d) : (DateTimeOffset?)null;
        Records[articleId] = record with
        {
            Title = title ?? record.Title,
            PublishedAt = date ?? record.PublishedAt,
            ListDate = date ?? record.ListDate,
        };
        return Task.CompletedTask;
    }

    public Task SetOutcomeAsync(string articleId, ArticleOutcome outcome, CancellationToken ct = default)
    {
        Records[articleId] = Records[articleId] with { Outcome = outcome with { At = TurkeyTime.Normalize(outcome.At) } };
        return Task.CompletedTask;
    }

    // Same filters and order as the Cosmos query; "next" is simply the offset of the next page.
    public Task<LedgerPage> QueryAsync(LedgerQuery query, int pageSize, string? continuation, CancellationToken ct = default)
    {
        var matches = Records.Values
            .Where(r => r.ListDate is not null)
            .Where(r => query.SourceId is null || r.SourceId == query.SourceId)
            .Where(r => query.From is null || r.ListDate >= query.From)
            .Where(r => query.Before is null || r.ListDate < query.Before)
            .Where(r => query.Status switch
            {
                ArticleStatusFilter.Parsed => r.Stages.TryGetValue(Stage.Parsed, out var p) && p.Status == StageStatus.Succeeded,
                ArticleStatusFilter.Failed => r.Stages.Values.Any(s => s.Status == StageStatus.Failed),
                _ => true,
            })
            .Where(r => query.Outcome switch
            {
                OutcomeFilter.Open => r.Outcome is null,
                OutcomeFilter.Handled => r.Outcome is not null,
                _ => true,
            })
            .Where(r => query.TitleContains is null
                || (r.Title is not null && TitleSearch.Fold(r.Title).Contains(TitleSearch.Fold(query.TitleContains), StringComparison.Ordinal)))
            .OrderByDescending(r => r.ListDate)
            .ToList();
        var offset = continuation is null ? 0 : int.Parse(continuation);
        var items = matches.Skip(offset).Take(pageSize).ToList();
        var next = offset + items.Count < matches.Count ? (offset + items.Count).ToString() : null;
        return Task.FromResult(new LedgerPage(items, next));
    }
}

internal sealed class InMemoryParsed : IParsedStore
{
    public List<ParsedArticle> Saved { get; } = [];

    public Task SaveAsync(ParsedArticle parsed, CancellationToken ct = default)
    {
        Saved.Add(parsed);
        return Task.CompletedTask;
    }

    public Task<ParsedArticle?> GetLatestAsync(string articleId, CancellationToken ct = default) =>
        Task.FromResult(Saved.LastOrDefault(p => p.ArticleId == articleId));
}

internal sealed class InMemoryRawHtml : IRawHtmlStore
{
    public Dictionary<string, string> Files { get; } = [];

    public Task<string> SaveAsync(string sourceId, string articleId, string html, DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        var path = $"{sourceId}/{articleId}/{fetchedAt.UtcTicks}.html.gz";
        Files[path] = html;
        return Task.FromResult(path);
    }

    public Task<string> ReadAsync(string path, CancellationToken ct = default) => Task.FromResult(Files[path]);
}

internal sealed class RecordingQueue : IQueueSender
{
    public List<(string Queue, object? Message)> Sent { get; } = [];

    public Task SendAsync<T>(string queueName, T message, CancellationToken ct = default)
    {
        Sent.Add((queueName, message));
        return Task.CompletedTask;
    }
}

/// <summary>A fake web server: answers every request with the given function.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        var response = respond(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };
}
