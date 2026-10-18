using System.Net;

namespace Pipeline.Functions.Http;

/// <summary>Settings for the HTTP client that reads news sites (used by Fetch and the spike).</summary>
public static class NewsHttp
{
    public const string ClientName = "news";

    // Identifies the project honestly; sites can contact or block us by name.
    public const string UserAgent = "direnistakip-pipeline/0.1 (+https://github.com/osmandmc/direnistakip-pipeline)";

    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr-TR,tr;q=0.9");
    }

    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    };
}
