using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pipeline.Core.Articles;
using Pipeline.Core.Fetching;
using Pipeline.Core.Parsing;
using Pipeline.Core.Submission;
using Pipeline.Functions.Http;
using Pipeline.Infrastructure;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// HTTP client for news sites, used by the Fetch stage.
builder.Services.AddHttpClient<ArticleFetcher>(NewsHttp.Configure).ConfigurePrimaryHttpMessageHandler(NewsHttp.CreateHandler);

// Ledger, raw HTML, parsed records and queues (managed identity in Azure, emulators locally).
builder.Services.AddPipelineStorage(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ArticleSubmission>();

// One parser per enabled source.
builder.Services.AddSingleton<IArticleParser, EvrenselParser>();
builder.Services.AddSingleton<ArticleParsing>();

// Read API (D-009).
builder.Services.AddSingleton<ArticleQueries>();
builder.Services.AddSingleton<ArticleOutcomes>();

builder.Build().Run();
