using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pipeline.Core.Fetching;
using Pipeline.Core.Submission;
using Pipeline.Functions.Http;
using Pipeline.Infrastructure;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// HTTP client for news sites: named (spike) and typed for the Fetch stage.
builder.Services.AddHttpClient(NewsHttp.ClientName, NewsHttp.Configure).ConfigurePrimaryHttpMessageHandler(NewsHttp.CreateHandler);
builder.Services.AddHttpClient<ArticleFetcher>(NewsHttp.Configure).ConfigurePrimaryHttpMessageHandler(NewsHttp.CreateHandler);

// Ledger, raw HTML, parsed records and queues (managed identity in Azure, emulators locally).
builder.Services.AddPipelineStorage(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ArticleSubmission>();

builder.Build().Run();
