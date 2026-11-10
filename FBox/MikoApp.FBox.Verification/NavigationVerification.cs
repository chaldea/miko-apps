using Miko.Routing;
using MikoApp.FBox.Services;

namespace MikoApp.FBox.Verification;

internal static class NavigationVerification
{
    public static void Run(bool ios)
    {
        var output = Path.GetFullPath("FBox/artifacts/navigation-" + (ios ? "ios" : "md"));
        Environment.SetEnvironmentVariable("FBOX_DEMO", "1");
        Environment.SetEnvironmentVariable("FBOX_DATA_DIR", Path.Combine(output, "state-" + Guid.NewGuid().ToString("N")));
        using var app = new AppHarness(output, ios);
        var navigation = app.Service<NavigationManager>();
        var store = app.Service<FBoxStore>();

        void Root(string route = "/devices")
        {
            navigation.NavigateTo(route, NavigationDirection.Root);
            app.Pump();
        }

        // Reproduce the reported path with real pointer input, not a direct navigation-pop call.
        Root();
        app.ClickText("device-row", "9468-vnc");
        AppHarness.Require(app.Route == "/monitor", "Device click opens monitor");
        app.Capture("01-device-open");
        app.ClickClass("back-button");
        var toolbarReturned = app.Route == "/devices";
        Console.WriteLine($"Toolbar back: {toolbarReturned}; route={app.Route}");

        Root();
        app.ClickText("device-row", "9468-vnc");
        var systemConsumed = app.PressSystemBack();
        var systemReturned = systemConsumed && app.Route == "/devices";
        Console.WriteLine($"System back: {systemReturned}; consumed={systemConsumed}; route={app.Route}");
        AppHarness.Require(toolbarReturned && systemReturned, "Device detail must return via toolbar and system back");
        AppHarness.Require(!app.PressSystemBack(), "Root page releases system back to the platform");
        app.Capture("02-returned-devices");

        // Every route that shares PageHeader must be able to pop its history.
        store.Selected = store.Boxes.Single(b => b.Number == "100017120001");
        store.SelectedPoint = store.Selected.Points.First();
        string[] routes = ["box-add", "box-edit", "group-add", "share-add", "share", "monitor", "point", "point-add", "alarm-add", "alarms", "history", "remote", "information", "account-management", "settings", "groups", "notifications", "security", "server-settings", "username", "password", "feedback", "cloud-settings", "cancel-account", "basic-information", "system-settings", "plc-devices", "shared-users", "location", "login", "register", "forgot-password", "privacy", "agreement", "vnc-connect", "scan", "unified-writing"];
        foreach (var route in routes)
        {
            Root(); app.Navigate("/" + route); app.ClickClass("back-button");
            AppHarness.Require(app.Route == "/devices" && navigation.History.Count == 1, "Toolbar pops one page: " + route);
        }

        Root(); app.Navigate("/monitor"); app.Navigate("/information"); app.Navigate("/box-edit");
        app.ClickClass("back-button"); AppHarness.Require(app.Route == "/information", "Nested toolbar return");
        AppHarness.Require(app.PressEscape() && app.Route == "/monitor", "Escape returns one level");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/devices", "Nested system return");

        // A select's modal consumes back before the underlying page does.
        app.Navigate("/box-edit"); app.ClickClass("box-group");
        AppHarness.Require(app.Find("alert-wrapper").Count > 0, "Group picker opened");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/box-edit", "First back closes group picker only");
        AppHarness.Require(app.Find("alert-wrapper").Count == 0, "Group picker dismissed");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/devices", "Next back returns to list");

        // Deep links have no previous entry; each header's DefaultHref still works.
        Root("/box-edit"); app.ClickClass("back-button");
        AppHarness.Require(app.Route == "/devices", "Toolbar fallback without history");
        Root("/point-add");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/monitor", "System back respects custom fallback");
        app.Capture("03-fallback-monitor");
        Console.WriteLine($"PASS: {routes.Length} header routes, toolbar/system/Escape back, nested history, overlay priority and fallback ({(ios ? "ios" : "md")}).");
    }
}
