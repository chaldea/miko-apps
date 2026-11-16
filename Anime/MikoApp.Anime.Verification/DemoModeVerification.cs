using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Miko.Core.DomElements;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

internal static class DemoModeVerification
{
    public static void Run()
    {
        var output = Path.GetFullPath("Anime/artifacts/demo-mode");
        Environment.SetEnvironmentVariable("ANIME_OFFLINE", null);
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "state-" + Guid.NewGuid().ToString("N")));
        var transport = new RecordingHandler();
        using (var app = new AppHarness(output, configure: builder =>
            builder.Services.AddSingleton(sp => new LanercClient(sp.GetRequiredService<AnimeSettings>(), transport))))
        {
            app.Navigate("/home"); app.Pump(100);
            var store = app.Service<AnimeStore>(); var api = app.Service<LanercClient>();
            AppHarness.Require(store.IsPreview && store.Hot.Count > 0, "Demo is the default");
            AppHarness.Require(store.Titles.Count is >= 12 and <= 35, "Compact complete catalog");
            app.Capture("01-demo-home");
            CheckImages(app);
            foreach (var category in store.Categories)
            {
                app.ClickText("ion-segment-button", category.Name); app.Pump(40);
                app.Capture("02-category-" + category.Id); CheckImages(app);
            }
            var title = store.Hot.First();
            var detail = store.DetailAsync(title.Id, default).GetAwaiter().GetResult();
            AppHarness.Require(detail.Lines.Count > 0 && detail.Lines[0].Episodes.Count > 0, "Real episode structure bundled");
            foreach (var line in detail.Lines)
                foreach (var episode in line.Episodes)
                    AppHarness.Require(api.ResolveAsync(episode.ResourceId).GetAwaiter().GetResult() == SampleMedia.Path, "Every demo episode uses the one sample");
            store.RecordWatch(title.Id, 1);
            store.LoginAsync("viewer@example.invalid", "local", default).GetAwaiter().GetResult();
            store.ToggleFavoriteAsync(title.Id, default).GetAwaiter().GetResult();
            AppHarness.Require(store.IsFavorite(title.Id), "Offline favorite interaction");
            api.SendCommentAsync(title.Id, "本地演示评论").GetAwaiter().GetResult();
            AppHarness.Require(api.CommentsAsync(title.Id).GetAwaiter().GetResult()["vod_comment"].Int("total") == 1, "Local comments");
            foreach (var route in new[] { "/trending", "/timetable", "/history", "/favorites" })
            {
                app.Navigate(route); app.Capture("03-" + route[1..]); CheckImages(app);
            }
            AppHarness.Require(transport.Paths.Count == 0 && app.Service<AnimeImageHandler>().DownloadCount == 0, "All demo screens and interactions make zero API/image network requests");
            app.Navigate("/user"); app.ClickClass("open-settings");
            AppHarness.Require(app.Route == "/settings", "Settings entry navigates");
            app.Capture("04-settings-demo");
            AppHarness.Require(app.Find("server-address").Count == 0, "Server input hidden in demo");
            app.ClickClass("demo-toggle");
            AppHarness.Require(app.Find("server-address").Count == 1, "Server input shown when toggle off");
            app.Capture("05-settings-online");
            app.Enter("server-address", "not-a-server"); app.ClickClass("save-settings");
            AppHarness.Require(store.IsPreview && app.Text.Contains("有效"), "Invalid server leaves active mode unchanged");
            app.Enter("server-address", "https://fixture.invalid/"); app.ClickClass("save-settings");
            AppHarness.Require(!store.IsPreview && !store.Profile.SignedIn && api.Token is null, "Mode switch clears session");
            AppHarness.Require(store.History.Count == 0 && store.Titles.Count == 0, "No demo data leaks into online mode");
            app.Navigate("/home"); app.Pump(50);
            AppHarness.Require(!store.IsPreview && store.Hot.First().Id == 90001, "Online mode renders server response");
            AppHarness.Require(transport.Paths.Contains("fixture.invalid/app/home") && transport.Paths.Contains("fixture.invalid/app/health"), "Configured endpoint is used");
            store.LoginAsync("viewer@example.invalid", "fixture-password", default).GetAwaiter().GetResult();
            app.Navigate("/settings"); app.Enter("server-address", "https://alternate.invalid/discover.json"); app.ClickClass("save-settings");
            AppHarness.Require(api.Token is null && !store.Profile.SignedIn, "Changing server clears old Authorization");
            app.Navigate("/home"); app.Pump(50);
            AppHarness.Require(transport.Paths.Contains("alternate.invalid/discover.json") && transport.Paths.Contains("alternate-api.invalid/app/home"), "Custom discovery JSON controls business endpoint");
            app.Navigate("/settings"); app.Enter("server-address", "https://unavailable.invalid/"); app.ClickClass("save-settings");
            app.Navigate("/home"); app.Pump(30);
            AppHarness.Require(!store.IsPreview && store.Titles.Count == 0 && store.ConnectionError is not null, "Online failure never silently falls back to demo");
            app.Navigate("/settings"); app.ClickClass("demo-toggle"); app.ClickClass("save-settings");
            var count = transport.Paths.Count;
            app.Navigate("/home");
            AppHarness.Require(store.IsPreview && store.History.Any(h => h.AnimeId == title.Id), "Returning to demo restores its history");
            AppHarness.Require(transport.Paths.Count == count, "Returning to demo makes no new requests");
            var reloaded = new AnimeSettings();
            AppHarness.Require(reloaded.DemoMode && reloaded.ServerAddress == "https://unavailable.invalid/", "Settings persist including hidden endpoint");
            app.Navigate("/settings"); app.ClickClass("demo-toggle"); app.ClickClass("reset-server"); app.ClickClass("save-settings");
            AppHarness.Require(new AnimeSettings().DemoMode == false && new AnimeSettings().ServerAddress == AnimeSettings.DefaultServerAddress, "Online mode persists and default server restores");
        }
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "ios-" + Guid.NewGuid().ToString("N")));
        using (var app = new AppHarness(Path.Combine(output, "ios"), ios: true))
        {
            app.SetSafeArea(0, 59, 0, 34); app.Navigate("/settings"); app.Capture("demo");
            app.ClickClass("demo-toggle"); app.Capture("online");
            app.Resize(360, 740); app.Capture("small-online");
        }
        VerifyPendingRequestCancellation(output);
        Console.WriteLine("Demo mode verified: offline network isolation, original image ratios, settings persistence, endpoint discovery, session/history isolation and failed-server behavior.");
    }

    private static void VerifyPendingRequestCancellation(string output)
    {
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "pending-" + Guid.NewGuid().ToString("N")));
        var settings = new AnimeSettings(); settings.Save(false, "https://fixture.invalid/");
        var transport = new RecordingHandler { DelayHome = true };
        using var api = new LanercClient(settings, transport);
        var store = new AnimeStore(api);
        var request = store.InitializeAsync();
        transport.HomeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        store.ApplySettings(true, settings.ServerAddress, settings);
        try { request.GetAwaiter().GetResult(); throw new InvalidOperationException("Old request was not canceled"); }
        catch (OperationCanceledException) { }
        AppHarness.Require(store.IsPreview && store.Hot.All(t => t.Preview) && store.ConnectionError is null, "Canceled online work cannot overwrite demo data");
    }

    private static void CheckImages(AppHarness app)
    {
        foreach (var css in new[] { "poster-image", "banner-image", "ranking-image", "library-image" })
        foreach (var image in app.Find(css).OfType<ImageElement>())
        {
            AppHarness.Require(image.Image is not null, "Bundled image decoded: " + image.Source);
            var rect = app.Bounds(image);
            AppHarness.Require(Math.Abs(rect.Width / rect.Height - (float)image.Image!.Width / image.Image.Height) < .02f, "Image retains natural aspect ratio: " + css);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public bool DelayHome { get; init; }
        public TaskCompletionSource HomeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Paths.Add(uri.Host + uri.AbsolutePath);
            if (DelayHome && uri.AbsolutePath == "/app/home")
            {
                HomeStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            if (uri.Host == "unavailable.invalid") throw new HttpRequestException("Fixture unavailable");
            AppHarness.Require(uri.Host is "fixture.invalid" or "alternate.invalid" or "alternate-api.invalid", "No unexpected remote host");
            AppHarness.Require(!request.Headers.Contains("Authorization"), "No stale session sent to a changed endpoint");
            JsonNode data;
            if (uri.AbsolutePath == "/discover.json") data = new JsonObject { ["domain"] = "https://alternate-api.invalid/" };
            else if (uri.AbsolutePath == "/app/health") data = new JsonObject { ["status"] = true };
            else
            {
                var title = new JsonObject { ["id"] = 90001, ["vod_name"] = "在线番剧", ["vod_pic"] = "res://Assets/poster-placeholder.svg", ["vod_total"] = 1 };
                var result = uri.AbsolutePath switch
                {
                    "/app/home" => new JsonObject { ["hot_list"] = new JsonArray(title), ["vod_list"] = new JsonArray(), ["banner"] = new JsonArray() },
                    "/app/config" => new JsonObject { ["app_config"] = new JsonObject() },
                    "/app/user/login" => new JsonObject { ["token"] = "fixture-session", ["user"] = new JsonObject { ["nick_name"] = "在线用户" } },
                    _ => throw new InvalidOperationException(uri.AbsolutePath)
                };
                data = new JsonObject { ["code"] = 200, ["data"] = result };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
