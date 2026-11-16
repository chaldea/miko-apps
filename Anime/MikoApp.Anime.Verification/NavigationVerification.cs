using Miko.Routing;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

internal static class NavigationVerification
{
    public static void Run()
    {
        var output = Path.GetFullPath("Anime/artifacts/navigation");
        Environment.SetEnvironmentVariable("ANIME_OFFLINE", "1");
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "state-" + Guid.NewGuid().ToString("N")));
        using var app = new AppHarness(output);
        var navigation = app.Service<NavigationManager>();
        var store = app.Service<AnimeStore>();
        store.InitializeAsync().GetAwaiter().GetResult();
        var titles = store.Hot.Take(2).ToArray();

        // A detail route works without any preceding selection or list page.
        app.Navigate($"/detail/{titles[0].Id}"); app.Pump(50);
        AppHarness.Require(app.Text.Contains(titles[0].Title), "Direct detail route loads the requested title");
        app.Navigate($"/detail/{titles[1].Id}"); app.Pump(50);
        AppHarness.Require(app.Text.Contains(titles[1].Title) && !app.Text.Contains(titles[0].Title), "Changing ID loads a new title");
        AppHarness.Require(navigation.NavigateBack(), "Detail navigation adds a history entry"); app.Pump(50);
        AppHarness.Require(app.Route == $"/detail/{titles[0].Id}" && app.Text.Contains(titles[0].Title), "Back restores the original title");
        app.Capture("detail-restored");

        app.Navigate("/home"); app.Pump(50);
        var card = app.Find("poster-card").First();
        var title = store.Titles.First(t => AppHarness.AllText(card).Contains(t.Title));
        app.Click(card); app.Pump(50);
        AppHarness.Require(app.Route == $"/detail/{title.Id}" && app.Text.Contains(title.Title), "Poster card uses a parameterized route");
        Console.WriteLine("PASS: Anime direct detail routes, title switching, back restoration and poster navigation.");
    }
}
