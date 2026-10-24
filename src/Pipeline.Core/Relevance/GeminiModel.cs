using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pipeline.Core.Relevance;

/// <summary>Configuration section "Gemini". The key comes from Key Vault in Azure and user-secrets locally.</summary>
public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    public string? ApiKey { get; set; }

    /// <summary>
    /// A model name or alias; the exact model that answered is recorded with every assessment. The light model by
    /// default: on the free tier the full Flash was often busy, and the light one found every linked article (findings.md).
    /// </summary>
    public string Model { get; set; } = "gemini-flash-lite-latest";

    /// <summary>
    /// A Key Vault reference App Service could not resolve stays as "@Microsoft.KeyVault(…)": treated as no key, so a
    /// missing secret switches the AI stages off instead of failing every call.
    /// </summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Google's Gemini API (generateContent) asked for a JSON answer at temperature 0. 429 (the free tier allows a few
/// requests a minute and a daily quota) and 503 (the model is overloaded, common on the free tier) →
/// <see cref="ModelRateLimitedException"/>: the article waits and comes back. Other failures throw and the queue retries.
/// </summary>
public sealed class GeminiModel(HttpClient http, GeminiOptions options) : ILanguageModel
{
    private static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>When Gemini says "too many" without saying how long to wait.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(2);

    public bool IsConfigured => options.HasKey;

    public async Task<ModelAnswer> AnswerJsonAsync(string prompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gemini:ApiKey is not set.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, $"models/{Uri.EscapeDataString(options.Model)}:generateContent"))
        {
            Content = JsonContent.Create(new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                generationConfig = new { responseMimeType = "application/json", temperature = 0 },
            }),
        };
        request.Headers.Add("x-goog-api-key", options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ModelRateLimitedException(RetryAfter(response, body), "Gemini: too many requests (" + Message(body) + ")");
        }
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new ModelRateLimitedException(DefaultRetryAfter, "Gemini: busy (" + Message(body) + ")");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini {(int)response.StatusCode}: {Message(body)}", null, response.StatusCode);
        }

        var json = JsonNode.Parse(body);
        var text = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            // Blocked by a safety filter or cut off: no answer to read.
            var reason = json?["candidates"]?[0]?["finishReason"]?.GetValue<string>() ?? json?["promptFeedback"]?["blockReason"]?.GetValue<string>();
            throw new FormatException("Gemini gave no text" + (reason is null ? "." : $" ({reason})."));
        }
        return new ModelAnswer(text, json?["modelVersion"]?.GetValue<string>() ?? options.Model);
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }
        // Gemini's error details carry e.g. { "@type": "…RetryInfo", "retryDelay": "37s" }.
        try
        {
            foreach (var detail in JsonNode.Parse(body)?["error"]?["details"]?.AsArray() ?? [])
            {
                if (detail?["retryDelay"]?.GetValue<string>() is { } delay && delay.EndsWith('s')
                    && double.TryParse(delay[..^1], System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(Math.Ceiling(seconds));
                }
            }
        }
        catch (JsonException)
        {
        }
        return DefaultRetryAfter;
    }

    private static string Message(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body;
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }
}
