using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Miko.Graphics;
using Miko.Hosting;
using Miko.Platform.Resources;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

internal static class ImageCacheVerification
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "miko-anime-image-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var url = new Uri("https://img9.doubanio.com/view/photo/s_ratio_poster/public/test.webp");
            var imageResource = typeof(App).Assembly.GetManifestResourceNames().First(n => n.StartsWith("MikoApp.Anime.Assets.Demo.image-") && n.EndsWith(".webp"));
            using var stream = typeof(App).Assembly.GetManifestResourceStream(imageResource)!;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var image = buffer.ToArray();
            using var services = MikoAppBuilder.CreateDefault().Services.BuildServiceProvider();
            var graphics = services.GetRequiredService<IGraphicsFactory>();
            var firstUpstream = new StubHandler(HttpStatusCode.OK, "image/webp", image);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, firstUpstream)))
            {
                var manager = new ResourceManager(client);
                var loads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.LoadAsync(url.AbsoluteUri)));
                Require(loads.All(bitmap => bitmap is { Width: > 0, Height: > 0 }), "Miko decoded downloaded images");
                Require(loads.All(bitmap => ReferenceEquals(bitmap, loads[0])), "Concurrent loads share the decoded bitmap");
                Require(ReferenceEquals(loads[0], await manager.LoadAsync(url.AbsoluteUri)), "Repeated load uses memory cache");
                Require(firstUpstream.Calls == 1, "First image request reaches upstream");
                Require(Uri.TryCreate(firstUpstream.Referer, UriKind.Absolute, out var referer) && referer == new Uri("https://douban.com"), "Douban image Referer");
                Require(firstUpstream.UserAgent?.Contains("Chrome/127.0.0.0", StringComparison.Ordinal) == true, "Browser User-Agent");
            }

            var secondUpstream = new StubHandler(HttpStatusCode.ServiceUnavailable, "text/plain", []);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, secondUpstream)))
            {
                var cached = await client.GetByteArrayAsync(url);
                Require(cached.SequenceEqual(image), "Disk-cached image bytes");
                Require(secondUpstream.Calls == 0, "Cached image avoids a repeat download");
            }

            var failedUrl = new Uri("https://img9.doubanio.com/view/photo/test-again.webp");
            var blocked = new StubHandler((HttpStatusCode)418, "text/html", [60, 104, 116, 109, 108, 62]);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, blocked)))
            using (var response = await client.GetAsync(failedUrl))
                Require((int)response.StatusCode == 418, "Failed image response passes through");

            var recovered = new StubHandler(HttpStatusCode.OK, "image/webp", image);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, recovered)))
            {
                var bytes = await client.GetByteArrayAsync(failedUrl);
                Require(bytes.SequenceEqual(image) && recovered.Calls == 1, "Failed image is retried instead of cached");
            }

            foreach (var path in Directory.GetFiles(directory, "*.imgcache")) await File.WriteAllTextAsync(path, "broken image");
            var repair = new StubHandler(HttpStatusCode.OK, "image/webp", image);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, repair)))
                Require((await client.GetByteArrayAsync(url)).SequenceEqual(image) && repair.Calls == 1, "Corrupt cache is downloaded again");

            var invalidImage = new StubHandler(HttpStatusCode.OK, "image/webp", [60, 104, 116, 109, 108, 62]);
            using (var client = new HttpClient(new AnimeImageHandler(directory, graphics, invalidImage)))
            {
                var manager = new ResourceManager(client);
                Require(await manager.LoadAsync("https://other.example.invalid/error.webp") is null, "Invalid image fails decoding");
                Require(await manager.LoadAsync("https://other.example.invalid/error.webp") is null && invalidImage.Calls == 2, "HTTP 200 error page is not cached");
                Require(invalidImage.Referer is null && string.IsNullOrEmpty(invalidImage.UserAgent), "Douban headers stay scoped to its image hosts");
            }

            var unwritable = Path.Combine(directory, "not-a-directory");
            await File.WriteAllTextAsync(unwritable, "fixture");
            using (var client = new HttpClient(new AnimeImageHandler(unwritable, graphics, new StubHandler(HttpStatusCode.OK, "image/webp", image))))
                Require((await client.GetByteArrayAsync(url)).SequenceEqual(image), "Cache write failure does not break image display");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class StubHandler(HttpStatusCode status, string contentType, byte[] bytes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Referer { get; private set; }
        public string? UserAgent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Referer = request.Headers.TryGetValues("Referer", out var values) ? values.FirstOrDefault() : null;
            UserAgent = request.Headers.UserAgent.ToString();
            await Task.Delay(30, cancellationToken);
            var response = new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return response;
        }
    }
}
