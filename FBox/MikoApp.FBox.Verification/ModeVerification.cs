using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Miko.Routing;
using MikoApp.FBox.Services;

namespace MikoApp.FBox.Verification;

internal static class ModeVerification
{
    public static void Run(bool ios)
    {
        var output = Path.GetFullPath("FBox/artifacts/mode-" + (ios ? "ios" : "md"));
        string[] variables = ["FBOX_DATA_DIR", "FBOX_DEMO", "FBOX_API_URL", "FBOX_IDENTITY_URL", "FBOX_CLIENT_ID", "FBOX_CLIENT_SECRET", "FBOX_SIGNALR_VERSION"];
        var original = variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, null);
        Environment.SetEnvironmentVariable("FBOX_DATA_DIR", Path.Combine(output, "state-" + Guid.NewGuid().ToString("N")));
        try { Verify(output, ios); }
        finally { foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, original[variable]); }
    }

    private static void Verify(string output, bool ios)
    {
        var handler = new AuthFixture();
        using var app = new AppHarness(output, ios, b => b.Services.AddSingleton(sp => new FBoxClient(sp.GetRequiredService<FBoxOptions>(), handler)));
        var store = app.Service<FBoxStore>();
        var navigation = app.Service<NavigationManager>();
        app.Pump();
        AppHarness.Require(app.Route == "/login" && !store.Authenticated && !app.Find("ion-tab-button").Any(), "Cold start requires an explicit session");
        app.Capture("01-demo-entry");

        foreach (var route in new[] { "/devices", "/monitor/100", "/account", "/messages", "/settings", "/box-add", "/history/100" })
        {
            app.Navigate(route);
            AppHarness.Require(app.Route == "/login" && !app.Find("ion-tab-button").Any() && !store.Boxes.Any(), "Unauthenticated route blocked: " + route);
        }
        AppHarness.Require(handler.Requests == 0, "Blocked routes never issue API requests");
        app.ClickClass("enter-demo");
        AppHarness.Require(store.Demo && app.Route == "/devices" && store.Boxes.Count() > 1, "Explicit demo entry loads local devices");
        var demoNumber = store.Boxes.First().Number;
        store.FavoriteAsync(store.Boxes.First()).GetAwaiter().GetResult();
        var favorite = store.Boxes.First().Favorite;
        app.Navigate("/account");
        app.ClickText("ion-item", "系统设置");
        AppHarness.Require(app.Route == "/server-settings" && !app.Find("server-api").Any(), $"System settings hide endpoints in demo mode: route={app.Route}; demo={store.Options.Demo}; fields={app.Find("server-api").Count}");
        app.Capture("02-demo-settings");
        app.ClickClass("demo-mode");
        AppHarness.Require(app.Find("server-api").Count == 1, "Switch reveals server fields immediately");
        app.ClickClass("save-settings");
        AppHarness.Require(app.Route == "/server-settings" && store.Options.Demo && store.Demo, "Invalid configuration leaves the active demo unchanged");
        app.Enter("server-api", "https://127.0.0.1:1");
        app.Enter("server-identity", "https://127.0.0.1:1");
        app.Enter("server-client-id", "fbox-test");
        app.Enter("server-client-secret", "session-only-secret");
        app.Capture("03-server-settings");
        app.ClickClass("save-settings");
        AppHarness.Require(app.Route == "/login" && !store.Authenticated && !store.Options.Demo && !app.Find("enter-demo").Any(), "Saving real mode requires login");
        AppHarness.Require(navigation.History.Count == 1 && !app.PressSystemBack(), "Login cannot return to the old protected history");
        var persisted = new FBoxOptions();
        AppHarness.Require(!persisted.Demo && persisted.ApiUrl == "https://127.0.0.1:1" && persisted.ClientId == "fbox-test", "Mode and endpoints survive reconstruction");
        AppHarness.Require(persisted.ClientSecret == "" && !File.ReadAllText(Path.Combine(persisted.DataDirectory, "app-settings.json")).Contains("session-only-secret"), "Secrets are excluded from ordinary settings storage");
        using (var realRestart = new AppHarness(Path.Combine(output, "real-restart"), ios))
        {
            realRestart.Pump();
            AppHarness.Require(realRestart.Route == "/login" && realRestart.Find("auth-user").Count == 1 && !realRestart.Find("enter-demo").Any() && !realRestart.Service<FBoxStore>().Authenticated, "Real mode survives restart and still requires login");
        }
        app.Capture("04-real-login");
        app.Resize(360, 740); app.SetSafeArea(0, 47, 0, 34); app.Capture("05-small-login");
        app.ClickClass("auth-settings");
        AppHarness.Require(app.Route == "/server-settings", "Login settings icon opens configuration");
        app.ClickClass("back-button");
        AppHarness.Require(app.Route == "/login", "Settings back returns to login");
        app.SetSafeArea(0, 0, 0, 0); app.Resize(390, 844);
        app.Enter("auth-user", "test-user"); app.Enter("auth-password", "incorrect");
        app.ClickClass("auth-submit");
        PumpUntil(app, () => !store.Busy);
        AppHarness.Require(app.Route == "/login" && !store.Authenticated && !app.Find("ion-tab-button").Any(), "Rejected login cannot enter the app");
        app.Navigate("/devices");
        AppHarness.Require(app.Route == "/login", "Rejected login cannot bypass the gate through navigation");
        handler.AcceptLogin = true;
        app.Enter("auth-user", "test-user"); app.Enter("auth-password", "fixture-password");
        app.ClickClass("auth-submit");
        PumpUntil(app, () => !store.Busy, 200);
        AppHarness.Require(app.Route == "/devices" && store.Authenticated && !store.Demo && store.Boxes.Single().Number == "123456789012", "Only a successful real login opens the real homepage");
        AppHarness.Require(navigation.History.Count == 1, "Successful login clears authentication pages from history");
        app.Capture("06-real-home");
        handler.ExpireSession = true;
        app.ClickText("ion-button", "刷新");
        PumpUntil(app, () => !store.Busy);
        AppHarness.Require(app.Route == "/login" && !store.Authenticated && !app.Find("ion-tab-button").Any() && !store.Boxes.Any() && !store.Realtime.Connected, "Expired session returns to login and clears protected state");
        app.ClickClass("auth-settings"); app.ClickClass("demo-mode"); app.ClickClass("save-settings");
        AppHarness.Require(app.Route == "/login" && store.Options.Demo && !store.Authenticated, "Demo setting enables local entry without auto-authentication");
        var requestCount = handler.Requests;
        app.ClickClass("enter-demo");
        AppHarness.Require(store.Demo && store.Boxes.First(b => b.Number == demoNumber).Favorite == favorite, "Returning to demo restores its separate saved state");
        app.ClickText("device-row", "9468-vnc"); app.ClickClass("back-button");
        foreach (var route in new[] { "/remote/100", "/vnc-connect" })
        {
            app.Navigate(route);
            AppHarness.Require(app.Text.Contains("演示模式不连接远程设备"), "Demo blocks remote connection: " + route);
        }
        AppHarness.Require(!store.RunAsync(() => store.Client.GetAsync("/api/client/v2/box/grouped")).GetAwaiter().GetResult(), "Transport rejects requests while demo mode is enabled");
        AppHarness.Require(handler.Requests == requestCount && !store.Realtime.Connected, "Demo navigation stays offline");
        app.Navigate("/account"); app.ClickText("ion-item", "退出登录");
        AppHarness.Require(app.Route == "/login" && !store.Authenticated && store.Options.Demo, "Logout ends the demo session without changing the preference");
        using var restarted = new AppHarness(Path.Combine(output, "restart"), ios);
        restarted.Pump();
        AppHarness.Require(restarted.Route == "/login" && restarted.Service<FBoxStore>().Options.Demo && !restarted.Service<FBoxStore>().Authenticated, "Restart retains demo preference but never creates a session");
        Console.WriteLine($"PASS: mode persistence, validation, offline isolation, login settings, failed/successful login, route guards, expiration and logout ({(ios ? "ios" : "md")}).");
    }

    private static void PumpUntil(AppHarness app, Func<bool> done, int attempts = 50)
    {
        for (var i = 0; i < attempts && !done(); i++) app.Pump(5);
        app.Pump();
        AppHarness.Require(done(), "Timed out waiting for authentication flow");
    }

    private sealed class AuthFixture : HttpMessageHandler
    {
        public bool AcceptLogin, ExpireSession;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/core/connect/token")
                return Task.FromResult(AcceptLogin && !ExpireSession
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "fixture-token", refresh_token = "fixture-refresh" }) }
                    : new HttpResponseMessage(HttpStatusCode.BadRequest));
            if (ExpireSession) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            AppHarness.Require(request.Headers.Authorization?.Parameter == "fixture-token", "Protected API carries the authenticated token");
            AppHarness.Require(path == "/api/client/v2/box/grouped", "Unexpected endpoint: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"id":1,"name":"Real service","boxRegs":[{"id":9,"boxNo":"123456789012","alias":"Service device","connState":1}]}]""") });
        }
    }
}
