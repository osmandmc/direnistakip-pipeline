using System.Net;
using Pipeline.Contracts;
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
