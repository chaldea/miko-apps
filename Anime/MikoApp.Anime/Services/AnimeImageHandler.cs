using System.Net;
using System.Security.Cryptography;
using System.Text;
using Miko.Graphics;

namespace MikoApp.Anime.Services;

/// <summary>
/// Transport for Miko's ResourceManager: APK image headers and persistent, validated image bytes.
/// ResourceManager still owns decoding, concurrent-request sharing, cancellation and the memory cache.
/// </summary>
public sealed class AnimeImageHandler(string cacheDirectory, IGraphicsFactory graphics, HttpMessageHandler? innerHandler = null)
    : DelegatingHandler(innerHandler ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
{
    private const int MaxImageBytes = 25 * 1024 * 1024;
    private const long MaxCacheBytes = 128L * 1024 * 1024;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _downloadCount, _diskCacheHits;

    public int DownloadCount => Volatile.Read(ref _downloadCount);
    public int DiskCacheHits => Volatile.Read(ref _diskCacheHits);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { IsAbsoluteUri: true } uri ||
            uri.Scheme is not ("http" or "https"))
            return await base.SendAsync(request, ct).ConfigureAwait(false);

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var path = Path.Combine(cacheDirectory, digest + ".imgcache");
        var cached = await ReadCachedAsync(path, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            Interlocked.Increment(ref _diskCacheHits);
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(cached) };
        }

        if (uri.Host.Equals("doubanio.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".doubanio.com", StringComparison.OrdinalIgnoreCase))
        {
            // Recovered from the APK's image header map (pp+0x1fef8).
            request.Headers.TryAddWithoutValidation("Referer", "https://douban.com");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
        }

        Interlocked.Increment(ref _downloadCount);
        var response = await base.SendAsync(request, ct).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength is not > MaxImageBytes)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                // Check actual decoding, not just the Content-Type: even HTTP 200 can contain an error page.
                if (IsImage(bytes)) await WriteCachedAsync(path, bytes, ct).ConfigureAwait(false);
            }
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private bool IsImage(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxImageBytes) return false;
        try
        {
            using var image = graphics.DecodeImage(bytes);
            return image is { Width: > 0, Height: > 0 };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Xml.XmlException)
        { return false; }
    }

    private async Task<byte[]?> ReadCachedAsync(string path, CancellationToken ct)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            if (file.Length <= MaxImageBytes)
            {
                var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                if (IsImage(bytes))
                {
                    try { file.LastWriteTimeUtc = DateTime.UtcNow; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    return bytes;
                }
            }
            // Interrupted/corrupt files must never keep a poster broken after network recovery.
            TryDelete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private async Task WriteCachedAsync(string path, byte[] bytes, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);
                File.Move(tempPath, path, true);
            }
            finally { TryDelete(tempPath); }

            var files = new DirectoryInfo(cacheDirectory).EnumerateFiles("*.imgcache")
                .OrderBy(file => file.LastWriteTimeUtc).ToArray();
            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= MaxCacheBytes) break;
                var length = file.Length;
                if (TryDelete(file.FullName)) total -= length;
            }
        }
        // A read-only/full cache must not prevent the downloaded image from being displayed.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _writeGate.Release(); }
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
