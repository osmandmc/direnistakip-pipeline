using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Pipeline.Functions.Api;

/// <summary>Unauthenticated liveness check for deployments. Every other endpoint uses function keys (D-025).</summary>
public sealed class HealthFunction
{
    private static readonly string Version =
        typeof(HealthFunction).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    [Function("Health")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req) =>
        new OkObjectResult(new { status = "ok", version = Version });
}
