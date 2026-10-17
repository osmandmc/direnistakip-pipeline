namespace Pipeline.Functions.Http;

/// <summary>Settings for the HTTP client that reads news sites.</summary>
public static class NewsHttp
{
    public const string ClientName = "news";

    // Identifies the project honestly; sites can contact or block us by name.
    public const string UserAgent = "direnistakip-pipeline/0.1 (+https://github.com/osmandmc/direnistakip-pipeline)";
}
