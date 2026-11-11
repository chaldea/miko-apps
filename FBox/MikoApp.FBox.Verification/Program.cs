using MikoApp.FBox.Verification;
using MikoApp.FBox.Services;

if (args.Contains("--protocol")) { await ProtocolVerification.RunAsync(); return; }
if (args.Contains("--mode")) { ModeVerification.Run(args.Contains("--ios")); return; }
if (args.Contains("--navigation")) { NavigationVerification.Run(args.Contains("--ios")); return; }
var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "FBox/artifacts/verification");
Environment.SetEnvironmentVariable("FBOX_DEMO", "1");
Environment.SetEnvironmentVariable("FBOX_DATA_DIR", Path.Combine(output, "state-" + Guid.NewGuid().ToString("N")));
using var app = new AppHarness(output, ios: args.Contains("--ios"));
app.Pump();
app.ClickClass("enter-demo");
app.Capture("01-devices");
app.Click(app.Find("ion-segment-button")[1]); app.Capture("50-favorites");
app.Click(app.Find("ion-segment-button")[2]); app.Capture("51-alarm-devices");
app.Click(app.Find("ion-segment-button")[0]);
app.Navigate("/box-add"); app.Capture("02-add-device");
app.ClickClass("save-box"); AppHarness.Require(app.Text.Contains("12 位"), "Serial validation");
app.Enter("box-number", "123456789012"); app.Enter("box-password", "demo-only"); app.Enter("box-alias", "验证设备"); app.ClickClass("save-box");
AppHarness.Require(app.Service<FBoxStore>().Boxes.Any(b => b.Number == "123456789012"), "Device added");
var store = app.Service<FBoxStore>();
store.Selected = store.Boxes.Single(b => b.Number == "100017120001");
string[] routes = ["box-edit", "share", "share-add", "messages", "account", "account-management", "settings", "monitor", "point-add", "alarm-add", "alarms", "history", "remote", "information", "groups", "group-add", "notifications", "security", "server-settings", "username", "password", "feedback", "cloud-settings", "cancel-account", "basic-information", "system-settings", "plc-devices", "shared-users", "location", "login", "register", "forgot-password", "privacy", "agreement", "vnc-connect"];
for (var i = 0; i < routes.Length; i++) { app.Navigate("/" + routes[i]); app.Capture($"{i+3:00}-{routes[i]}"); }
app.Navigate("/alarms"); app.ClickText("ion-segment-button", "报警登记"); app.Capture("40-alarm-rules");
app.ClickText("ion-segment-button", "历史报警"); app.Capture("41-alarm-history");
app.Navigate("/history"); app.ClickClass("toggle-chart"); app.Capture("42-history-chart");
app.Navigate("/monitor"); app.ClickClass("point-row"); app.Capture("43-point-detail");
app.Navigate("/point-add"); app.Enter("point-name-input", "测试监控点"); app.ClickClass("save-point"); AppHarness.Require(store.Selected!.Points.Any(p => p.Name == "测试监控点"), "Point saved");
app.Navigate("/alarm-add"); app.Enter("point-name-input", "测试报警项"); app.Enter("alarm-content", "温度过高"); app.ClickClass("save-point"); AppHarness.Require(store.Selected!.Alarms.Any(a => a.Name == "测试报警项"), "Alarm saved");
app.Navigate("/devices"); app.Enter("device-search", "验证设备"); AppHarness.Require(app.Text.Contains("验证设备") && !app.Text.Contains("9468-vnc"), "Search filter"); app.Capture("44-search-results");
app.Navigate("/devices"); app.Enter("device-search", ""); app.ClickClass("select-devices"); app.ClickText("device-row", "9468-vnc"); app.Capture("45-device-actions"); app.ClickText("action-sheet-button", "删除"); app.Capture("46-delete-confirm"); app.ClickText("alert-button", "取消"); AppHarness.Require(store.Boxes.Any(b => b.Title == "9468-vnc"), "Cancelled delete preserves device");
app.Navigate("/box-edit"); app.ClickClass("box-group"); app.Capture("47-group-picker");
app.Navigate("/unified-writing"); app.Capture("52-unified-writing");
var batchBox = store.Boxes.Single(b => b.Number == "100017120001");
batchBox.State = 1; batchBox.Points[0].Writable = true;
app.ClickClass("add-write-group"); app.Enter("write-group-name", "设备写入组"); app.Enter("write-group-keyword", "var0"); app.ClickClass("search-write-targets");
app.Capture("57-write-targets"); app.Click(app.Find("ion-checkbox").Single()); app.ClickClass("save-write-group");
AppHarness.Require(store.State.WriteGroups.Single().Targets.Single().PointId == batchBox.Points[0].Id, "Batch target selection");
app.ClickClass("write-group"); app.Enter("batch-write-value", "1"); app.ClickClass("batch-write"); app.Capture("58-batch-confirm"); app.ClickText("alert-button", "确定");
AppHarness.Require(batchBox.Points[0].Value == "1", "Confirmed batch write"); batchBox.State = 0;
app.Navigate("/history"); app.Click(app.Find("ion-fab-button")[0]); app.Capture("59-history-filter");
app.Navigate("/scan"); app.Capture("53-scanner");
AppHarness.Require(app.Bounds(app.Find("miko-barcode-scanner").Single()).Height >= 400, "Scanner preview has usable height");
app.Navigate("/share"); app.ClickClass("generate-code"); app.Capture("54-share-code");
app.Navigate("/devices"); app.ClickText("ion-tab-button", "添加设备"); app.Capture("55-add-sheet"); app.ClickText("action-sheet-button", "取消");
app.Navigate("/devices"); app.Resize(360,740); app.Capture("48-small-phone");
app.SetSafeArea(0, 47, 0, 34); app.Capture("56-safe-area"); app.SetSafeArea(0,0,0,0);
app.Resize(1024,768); app.Capture("49-desktop");
Console.WriteLine($"PASS: {app.Captures} native captures; device/point/alarm creation, validation, search and delete cancellation.");
