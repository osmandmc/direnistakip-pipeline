using System.IO.Compression;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Raw pages in the "raw-html" blob container, gzip-compressed: {source}/{articleId}/{fetchedAt}.html.gz.</summary>
internal sealed class BlobRawHtmlStore(BlobContainerClient container) : IRawHtmlStore
{
    public async Task<string> SaveAsync(
        string sourceId, string articleId, string html, DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        var path = $"{sourceId}/{articleId}/{fetchedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}.html.gz";

        using var buffer = new MemoryStream();
        await using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(html), ct);
        }
        buffer.Position = 0;

        await container.GetBlobClient(path).UploadAsync(buffer, new BlobUploadOptions
        {
            // ContentEncoding lets a browser open the downloaded file directly.
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/html; charset=utf-8", ContentEncoding = "gzip" },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }, // never overwrite (D-003)
        }, ct);
        return path;
    }

    // Names start with the fetch time (UTC, fixed width), so the last name is the latest page.
    public async Task<string?> FindLatestAsync(string sourceId, string articleId, CancellationToken ct = default)
    {
        string? latest = null;
        await foreach (var blob in container.GetBlobsAsync(prefix: $"{sourceId}/{articleId}/", cancellationToken: ct))
        {
            if (latest is null || string.CompareOrdinal(blob.Name, latest) > 0)
            {
                latest = blob.Name;
            }
        }
        return latest;
    }

    public async Task<string> ReadAsync(string path, CancellationToken ct = default)
    {
        var download = await container.GetBlobClient(path).DownloadContentAsync(ct);
        await using var gzip = new GZipStream(download.Value.Content.ToStream(), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
