using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Miko.Core.DomElements;
using Miko.Graphics;
using Miko.Platform.Resources;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

internal static class LiveImageVerification
{
    public static void Run(bool allowUnavailableImages = false)
    {
        var output = Path.GetFullPath("Anime/artifacts/image-fix");
        var cache = Path.Combine(output, "cache-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ANIME_OFFLINE", "0");
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "state"));
        Environment.SetEnvironmentVariable("ANIME_IMAGE_CACHE_DIR", cache);
        var sources = new HashSet<string>();
        var failures = new HashSet<string>();

        using (var app = new AppHarness(output))
        {
            app.Navigate("/home");
            Wait(app, () => app.Service<AnimeStore>().Initialized, "Remote catalog", 65);
            var store = app.Service<AnimeStore>();
            AppHarness.Require(!store.IsPreview, "Live catalog must be remote");
            Capture(app, "01-home", "poster-image", sources, failures, allowUnavailableImages);

            app.ClickText("ion-segment-button", store.Categories.First().Name);
            Capture(app, "02-category", "poster-image", sources, failures, allowUnavailableImages);
            app.Navigate("/search");
            app.Enter("search-query", store.Hot.First().Title);
            app.ClickClass("submit-search");
            Capture(app, "03-search", "poster-image", sources, failures, allowUnavailableImages);
            app.Navigate("/trending");
            Capture(app, "04-ranking", "ranking-image", sources, failures, allowUnavailableImages);
            app.Navigate("/timetable");
            Capture(app, "05-timetable", "poster-image", sources, failures, allowUnavailableImages);
            store.RecordWatch(store.Hot.First().Id, 1);
            app.Navigate("/history");
            Capture(app, "06-history", "library-image", sources, failures, allowUnavailableImages);

            var transport = app.Service<AnimeImageHandler>();
            Console.WriteLine($"Cold cache: {transport.DownloadCount} image downloads / {transport.DiskCacheHits} disk hits.");
            AppHarness.Require(transport.DownloadCount > 0, "Cold start fetched images");
        }

        var offline = new RejectNetworkHandler();
        using (var app = new AppHarness(Path.Combine(output, "cached"), configure: builder =>
        {
            builder.Services.AddSingleton(sp => new HttpClient(new AnimeImageHandler(cache, sp.GetRequiredService<IGraphicsFactory>(), offline)));
        }))
        {
            // A fresh ResourceManager has no decoded images. It must recover every image from disk.
            var loader = app.Service<IImageLoader>();
            foreach (var source in sources)
                AppHarness.Require(loader.LoadAsync(source).GetAwaiter().GetResult() is { Width: > 0, Height: > 0 }, $"Offline disk load: {source}");
            app.Navigate("/home");
            Wait(app, () => app.Service<AnimeStore>().Initialized, "Remote catalog", 65);
            AppHarness.Require(!app.Service<AnimeStore>().IsPreview, "Cached UI still uses remote catalog URLs");
            Capture(app, "07-home-without-image-network", "poster-image", [], [], false);
            AppHarness.Require(offline.Calls == 0, "Warm start must make zero image network requests");
            Console.WriteLine($"Warm cache: {sources.Count} distinct image URLs decoded with {offline.Calls} network requests.");
        }
        Console.WriteLine($"Live image verification: {sources.Count} decoded/cached URLs, {failures.Count} unavailable URLs. Screenshots: {output}");
        foreach (var failure in failures) Console.WriteLine($"UNAVAILABLE: {failure}");
    }

    private static void Capture(AppHarness app, string name, string imageClass, HashSet<string> sources, HashSet<string> failures, bool allowUnavailableImages)
    {
        Wait(app, () => !app.Find("loading-state").Any() && app.Find(imageClass).OfType<ImageElement>().Any(e => e.Source.IsNetwork), name);
        var images = app.Find(imageClass).OfType<ImageElement>().Where(e => e.Source.IsNetwork).ToArray();
        try { Wait(app, () => images.All(e => e.Image is { Width: > 0, Height: > 0 }), $"Decoded posters: {name}"); }
        catch
        {
            app.Capture(name + "-failure");
            var diagnostics = (IResourceDiagnostics)app.Service<IImageLoader>();
            foreach (var missing in images.Where(e => e.Image is null))
            {
                Console.WriteLine($"Missing image: {missing.Source.Raw}");
                foreach (var load in diagnostics.GetNetworkResources().Where(r => r.Url == missing.Source.Raw)) Console.WriteLine(load);
            }
            if (!allowUnavailableImages) throw;
            // Diagnostic mode keeps reporting unavailable third-party hosts, without counting placeholders as images.
            AppHarness.Require(images.Where(e => new Uri(e.Source.Raw).Host.EndsWith(".doubanio.com", StringComparison.OrdinalIgnoreCase)).All(e => e.Image is not null), "All Douban posters must decode");
            AppHarness.Require(images.Any(e => e.Image is not null), "The page must contain decoded remote images");
            foreach (var missing in images.Where(e => e.Image is null)) failures.Add(missing.Source.Raw);
        }
        foreach (var image in images.Where(e => e.Image is not null)) sources.Add(image.Source.Raw);
        // Include the home carousel in the subsequent cache-only startup.
        foreach (var banner in app.Find("banner-image").OfType<ImageElement>())
        {
            Wait(app, () => banner.Image is not null, "Decoded banner");
            if (banner.Source.IsNetwork) sources.Add(banner.Source.Raw);
        }
        app.Capture(name);
        Console.WriteLine($"{name}: {images.Count(e => e.Image is not null)}/{images.Length} remote posters decoded.");
    }

    private static void Wait(AppHarness app, Func<bool> ready, string label, int seconds = 35)
    {
        var elapsed = Stopwatch.StartNew();
        while (!ready() && elapsed.Elapsed.TotalSeconds < seconds) app.Pump(10);
        AppHarness.Require(ready(), $"Timed out: {label}");
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
