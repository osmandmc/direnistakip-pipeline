using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pipeline.Core.Articles;
using Pipeline.Core.Collecting;
using Pipeline.Core.Fetching;
using Pipeline.Core.Parsing;
using Pipeline.Core.Relevance;
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

// Relevance stage (B9): Gemini while testing (the provider is open decision #7). No key → the stage does nothing.
var gemini = new GeminiOptions { ApiKey = builder.Configuration["Gemini:ApiKey"] };
if (builder.Configuration["Gemini:Model"] is { Length: > 0 } geminiModel)
{
    gemini.Model = geminiModel;
}
builder.Services.AddSingleton(gemini);
builder.Services.AddHttpClient<ILanguageModel, GeminiModel>(http => http.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddTransient<RelevanceAssessment>();
builder.Services.AddTransient<ArticleAssessing>();

// Read API (D-009).
builder.Services.AddSingleton<ArticleQueries>();
builder.Services.AddSingleton<ArticleOutcomes>();
builder.Services.AddSingleton<ArticleMetrics>();

builder.Build().Run();
