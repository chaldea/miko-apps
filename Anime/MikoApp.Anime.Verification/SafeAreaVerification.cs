using System.Text.Json.Nodes;
using MikoApp.Anime.Services;

namespace MikoApp.Anime.Verification;

internal static class SafeAreaVerification
{
    public static void Run()
    {
        var output = Path.GetFullPath("Anime/artifacts/safe-area-fix");
        Environment.SetEnvironmentVariable("ANIME_OFFLINE", "1");
        Environment.SetEnvironmentVariable("ANIME_DATA_DIR", Path.Combine(output, "state"));
        foreach (var ios in new[] { false, true })
        using (var app = new AppHarness(Path.Combine(output, ios ? "ios" : "android"), ios))
        {
            app.Navigate("/user");
            var initialTop = app.Bounds(app.Find("profile-summary").Single()).Y;
            var top = ios ? 59 : 32;
            var bottom = ios ? 34 : 24;
            app.SetSafeArea(0, top, 0, bottom);
            var profile = app.Bounds(app.Find("profile-summary").Single());
            AppHarness.Require(Math.Abs(profile.Y - initialTop - top) < 1, "Profile moves by the exact status-bar inset");
            CheckBounds(app, top, 0, 0, bottom, 390, 844);
            app.Capture("01-guest-portrait");
            app.ClickClass("profile-summary");
            AppHarness.Require(app.Route == "/login", "Profile login remains clickable below the safe area");
            app.Service<AnimeStore>().ApplySession(new JsonObject { ["user"] = new JsonObject { ["nick_name"] = "追番同学", ["email"] = true, ["account"] = "viewer@example.invalid" } });
            app.Navigate("/user");
            CheckBounds(app, top, 0, 0, bottom, 390, 844);
            app.Capture("02-signed-in-portrait");
            app.Resize(844, 390);
            app.SetSafeArea(44, 0, 44, 21);
            CheckBounds(app, 0, 44, 44, 21, 844, 390);
            app.Capture("03-landscape");
            app.SetSafeArea(0, 0, 0, 0);
            app.Resize(390, 844);
            AppHarness.Require(Math.Abs(app.Bounds(app.Find("profile-summary").Single()).Y - initialTop) < 1, "Desktop has no extra safe-area gap");
            app.Capture("04-without-insets");
        }
        Console.WriteLine("Safe-area verification passed: guest/account, portrait/landscape, Android/iOS and zero insets.");
    }

    private static void CheckBounds(AppHarness app, float top, float left, float right, float bottom, int width, int height)
    {
        var profile = app.Bounds(app.Find("profile-summary").Single());
        var content = app.Bounds(app.Find("ion-content").First());
        var tab = app.Bounds(app.Find("ion-tab-button").First());
        var shortcut = app.Bounds(app.Find("flat-card").First());
        AppHarness.Require(profile.Y >= top && profile.X >= left && profile.X + profile.Width <= width - right + 1, "Profile clears status bar and cutouts");
        AppHarness.Require(content.Y >= profile.Y + profile.Height, "Scrollable content starts after the profile header");
        AppHarness.Require(shortcut.X >= left && shortcut.X + shortcut.Width <= width - right + 1, "Shortcuts clear side cutouts");
        AppHarness.Require(tab.Y + tab.Height <= height - bottom + 1, "Tab controls clear the bottom gesture area");
    }
}
