using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pipeline.Core.Articles;
using Pipeline.Core.Collecting;
using Pipeline.Core.Fetching;
using Pipeline.Core.Parsing;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;
using Pipeline.Core.Submission;
using Pipeline.Functions.Http;
using Pipeline.Infrastructure;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// HTTP clients for news sites: pages for the Fetch stage, feeds for the collector.
builder.Services.AddHttpClient<ArticleFetcher>(NewsHttp.Configure).ConfigurePrimaryHttpMessageHandler(NewsHttp.CreateHandler);
builder.Services.AddHttpClient<FeedCollector>(NewsHttp.Configure).ConfigurePrimaryHttpMessageHandler(NewsHttp.CreateHandler);
builder.Services.AddTransient<FeedCollection>();

// Ledger, raw HTML, parsed records and queues (managed identity in Azure, emulators locally).
builder.Services.AddPipelineStorage(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ArticleSubmission>();

// One parser per enabled source.
builder.Services.AddSingleton<IArticleParser, EvrenselParser>();
builder.Services.AddSingleton<ArticleParsing>();
builder.Services.AddSingleton<ArticleReparsing>();

// Relevance (B9) and Extract (B12) stages: Gemini while testing (the provider is open decision #7), one key, a model
// setting each. Both Flash-Lite by default: on the free tier the full Flash allows only 20 requests a day, Flash-Lite
// 500, enough for both stages. No key → both stages do nothing.
GeminiOptions Gemini(string modelSetting, string fallback) => new()
{
    ApiKey = builder.Configuration["Gemini:ApiKey"],
    Model = builder.Configuration[modelSetting] is { Length: > 0 } model ? model : fallback,
};
var relevanceModel = Gemini("Gemini:Model", new GeminiOptions().Model);
var extractionModel = Gemini("Gemini:ExtractionModel", new GeminiOptions().Model);
builder.Services.AddHttpClient("gemini", http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddSingleton(new Pipeline.Functions.Api.AiModels(relevanceModel.Model, extractionModel.Model));
GeminiModel Model(IServiceProvider sp, GeminiOptions options) =>
    new(sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini"), options);

builder.Services.AddTransient(sp => new RelevanceAssessment(
    sp.GetRequiredService<ILedgerStore>(), sp.GetRequiredService<IParsedStore>(), sp.GetRequiredService<IRelevanceStore>(),
    Model(sp, relevanceModel), sp.GetRequiredService<IQueueSender>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddTransient(sp => new ArticleAssessing(
    sp.GetRequiredService<ILedgerStore>(), sp.GetRequiredService<IParsedStore>(), Model(sp, relevanceModel), sp.GetRequiredService<IQueueSender>()));
builder.Services.AddTransient(sp => new DraftExtraction(
    sp.GetRequiredService<ILedgerStore>(), sp.GetRequiredService<IParsedStore>(), sp.GetRequiredService<IDraftStore>(),
    Model(sp, extractionModel), sp.GetRequiredService<IQueueSender>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new ArticleMetrics(
    sp.GetRequiredService<ILedgerStore>(), sp.GetRequiredService<TimeProvider>(), Model(sp, relevanceModel)));
builder.Services.AddTransient(sp => new ArticleDrafting(
    sp.GetRequiredService<ILedgerStore>(), Model(sp, extractionModel), sp.GetRequiredService<IQueueSender>()));

// Read API (D-009).
builder.Services.AddSingleton<ArticleQueries>();
builder.Services.AddSingleton<ArticleOutcomes>();

var app = builder.Build();

// Running locally (func start sets the Development environment): create what infra/main.bicep creates in Azure, in the
// emulators of docker-compose.yml (F11).
if (builder.Environment.IsDevelopment())
{
    await LocalResources.EnsureAsync(app.Services, builder.Configuration);
}

app.Run();
