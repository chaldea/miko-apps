using Miko.Routing;
using MikoApp.FBox.Models;
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
        app.Pump();
        app.ClickClass("enter-demo");

        void Root(string route = "/devices")
        {
            navigation.NavigateTo(route, NavigationDirection.Root);
            app.Pump();
        }

        // Reproduce the reported path with real pointer input, not a direct navigation-pop call.
        Root();
        app.ClickText("device-row", "9468-vnc");
        AppHarness.Require(app.Route == "/monitor/1", "Device click opens monitor");
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
        string[] routes = ["box-add", "box-edit/100", "group-add", "share-add", "share/100", "monitor/100", "point/100/1", "point-add/100", "alarm-add/100", "alarms/100", "history/100", "remote/100", "information/100", "account-management", "settings", "groups", "notifications", "security", "server-settings", "username", "password", "feedback", "cloud-settings", "cancel-account", "basic-information/100", "system-settings/100", "plc-devices/100", "shared-users/100", "location/100", "register", "forgot-password", "privacy", "agreement", "vnc-connect", "scan", "unified-writing"];
        foreach (var route in routes)
        {
            Root(); app.Navigate("/" + route); app.ClickClass("back-button");
            AppHarness.Require(app.Route == "/devices" && navigation.History.Count == 1, "Toolbar pops one page: " + route);
        }

        Root(); app.Navigate("/monitor/100"); app.Navigate("/information/100"); app.Navigate("/box-edit/100");
        app.ClickClass("back-button"); AppHarness.Require(app.Route == "/information/100", "Nested toolbar return");
        AppHarness.Require(app.PressEscape() && app.Route == "/monitor/100", "Escape returns one level");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/devices", "Nested system return");

        // A select's modal consumes back before the underlying page does.
        app.Navigate("/box-edit/100"); app.ClickClass("box-group");
        AppHarness.Require(app.Find("alert-wrapper").Count > 0, "Group picker opened");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/box-edit/100", "First back closes group picker only");
        AppHarness.Require(app.Find("alert-wrapper").Count == 0, "Group picker dismissed");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/devices", "Next back returns to list");

        // Deep links have no previous entry; each header's DefaultHref still works.
        Root("/box-edit/100"); app.ClickClass("back-button");
        AppHarness.Require(app.Route == "/devices", "Toolbar fallback without history");
        Root("/point-add/100");
        AppHarness.Require(app.PressSystemBack() && app.Route == "/monitor/100", "System back respects custom fallback");
        app.Capture("03-fallback-monitor");
        VerifyParametersAndState(app, navigation, store);
        Console.WriteLine($"PASS: {routes.Length} header routes, toolbar/system/Escape back, nested history, overlay priority and fallback ({(ios ? "ios" : "md")}).");
    }

    private static void VerifyParametersAndState(AppHarness app, NavigationManager navigation, FBoxStore store)
    {
        navigation.NavigateTo("/devices", NavigationDirection.Root); app.Pump();
        app.Navigate("/monitor/100");
        AppHarness.Require(app.Text.Contains("100017120001") && app.Find("point-row").Count == 10, "Direct device route loads its own points");
        app.ClickClass("point-row");
        AppHarness.Require(app.Route == "/point/100/1" && app.Text.Contains("var0"), "Point click supplies both route parameters");
        app.Navigate("/point/100/2");
        AppHarness.Require(app.Text.Contains("var1") && !app.Text.Contains("var0"), "Changing point ID creates the correct detail");
        app.PressSystemBack();
        AppHarness.Require(app.Route == "/point/100/1" && app.Text.Contains("var0"), "Back restores the original point ID");
        app.Navigate("/monitor/1");
        AppHarness.Require(app.Text.Contains("9468-vnc") && app.Find("point-row").Count == 0, "Another device has independent context");
        app.PressSystemBack();
        AppHarness.Require(app.Route == "/point/100/1" && app.Text.Contains("var0"), "Back across devices restores the original device and point");
        app.Navigate("/monitor/100");
        foreach (var tab in new[] { ("报警记录", "/alarms/100"), ("历史记录", "/history/100"), ("远程监控", "/remote/100"), ("更多", "/information/100"), ("数据监控", "/monitor/100") })
        {
            app.ClickText("ion-tab-button", tab.Item1);
            AppHarness.Require(app.Route == tab.Item2 && app.Text.Contains("100017120001"), "Device tab preserves route ID: " + tab.Item2);
        }
        app.Navigate("/information/100"); app.ClickText("ion-item", "设备别名");
        AppHarness.Require(app.Route == "/box-edit/100" && app.InputValue("box-alias") == "100017120001", "Edit link and initialization preserve the device ID");
        app.Enter("box-alias", "Route verification"); app.ClickClass("save-box");
        AppHarness.Require(app.Route == "/information/100" && store.Boxes.Single(b => b.Id == 100).Alias == "Route verification", "Save returns to the edited device");
        app.Navigate("/point/100/99999");
        AppHarness.Require(!app.Find("write-point").Any() && !app.Find("point-name-input").Any(), "Missing point never exposes another point or the add form");

        app.Navigate("/devices");
        var registration = new BoxRegistrationState("123456789012");
        navigation.NavigateTo("/box-add", registration); app.Pump();
        AppHarness.Require(app.InputValue("box-number") == registration.Number, "Navigation state prefills the registration form");
        app.Navigate("/group-add"); app.PressSystemBack();
        AppHarness.Require(ReferenceEquals(navigation.GetState<BoxRegistrationState>(), registration) && app.InputValue("box-number") == registration.Number, "Back restores registration state without consuming it");
        app.Navigate("/devices"); app.Navigate("/box-add");
        AppHarness.Require(navigation.CurrentState is null && string.IsNullOrEmpty(app.InputValue("box-number")), "Manual registration does not reuse a previous scan");
    }
}
