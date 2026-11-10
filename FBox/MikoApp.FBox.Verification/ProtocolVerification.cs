using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MikoApp.FBox.Models;
using MikoApp.FBox.Services;

namespace MikoApp.FBox.Verification;

internal static class ProtocolVerification
{
    public static async Task RunAsync()
    {
        var options = new FBoxOptions { ApiUrl = "https://api.fbox.invalid", IdentityUrl = "https://id.fbox.invalid", ClientId = "test", Demo = false, DataDirectory = Path.Combine(Path.GetTempPath(), "fbox-verification-" + Guid.NewGuid().ToString("N")) };
        using var handler = new Fixture();
        using var client = new FBoxClient(options, handler);
        using var realtime = new FBoxRealtime(client, options);
        var store = new FBoxStore(options, client, realtime);
        await client.LoginAsync("test+name", "p&=中文");
        AppHarness.Require(handler.LoginBody!.Contains("test%2Bname") && handler.LoginBody.Contains("p%26%3D"), "OAuth form encoding");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.GetAsync("/retry")));
        AppHarness.Require(handler.RefreshCount == 1 && results.All(r => r.Flag("ok")), "Concurrent 401 uses one refresh");
        await store.RefreshAsync();
        var box = store.Boxes.Single();
        AppHarness.Require(box.Number == "123456789012" && box.Online, "Grouped device contract");
        await store.LoadPointsAsync(box);
        AppHarness.Require(!box.Points[0].Writable && box.Points[1].Writable, "Permission bit mask (4 read / 6 read-write)");
        store.ApplyPush("status", JsonNode.Parse("""[{"id":9,"state":0}]"""));
        AppHarness.Require(!box.Online, "Array-shaped connection notifications");
        store.ApplyPush("status", JsonNode.Parse("""[{"id":9,"state":1}]"""));
        store.ApplyPush("values", JsonNode.Parse("""{"boxId":9,"values":[{"id":1,"value":20,"formatValue":"20.00","status":0},{"id":2,"status":2}]}"""));
        AppHarness.Require(box.Points[0].Value == "20.00" && box.Points[1].Value == "读取超时", "Value formatting and failed-read notifications");
        try { await store.WriteAsync(box, box.Points[0], "1"); throw new Exception("Read-only write accepted"); } catch (InvalidOperationException) { }
        box.State = 0;
        try { await store.WriteAsync(box, box.Points[1], "1"); throw new Exception("Offline write accepted"); } catch (InvalidOperationException) { }
        AppHarness.Require(handler.WriteCount == 0, "Blocked writes never reach transport");
        box.State = 1; await store.WriteAsync(box, box.Points[1], "42");
        AppHarness.Require(handler.WriteCount == 1, "Authorized online write");
        await store.LoadHistoryAsync(box, new DateTime(2026,11,1), new DateTime(2026,11,10));
        AppHarness.Require(store.Samples.Count == 2 && store.Samples[1].Value == 47, "History epoch t / channel c response");
        AppHarness.Require(handler.HistoryBody?["ids"]?[0]?.ToString() == "987" && long.TryParse(handler.HistoryBody?.Text("begin"), out _), "History uses channel UID and epoch bounds");
        await store.AddAlarmAsync(box, new() { Name = "Threshold", Message = "High", DataType = 1, Threshold = 42, AddressTagId = 81, GroupId = 7 });
        AppHarness.Require(handler.AlarmBody?[0].Number("condition1") == 1 && handler.AlarmBody?[0].Number("operand1") == 42 && handler.AlarmBody?[0].Number("addrTagUid") == 81 && handler.AlarmBody?[0].Number("alarmGrpId") == 7, "Alarm equality, threshold, tag and group contract");
        var writing = new FBoxWriting(store);
        var targets = await writing.SearchAsync("Write", 1);
        await writing.SaveAsync(new() { Name = "Batch", DataType = 1, Targets = targets });
        AppHarness.Require(handler.GroupBody?["controlOptions"]?[0].Number("duid") == 2 && handler.GroupBody?.Number("type") == 1, "Unified write persists explicit targets");
        handler.FailDelete = true;
        var count = store.Boxes.Count();
        AppHarness.Require(!await store.RunAsync(() => store.DeleteAsync(box)) && store.Boxes.Count() == count, "Failed server deletion retains local device");
        handler.AlwaysUnauthorized = true;
        var before = handler.RetryCount;
        try { await client.GetAsync("/retry"); throw new Exception("Repeated 401 accepted"); } catch (InvalidOperationException) { }
        AppHarness.Require(handler.RetryCount - before == 2 && !client.Authenticated, "Repeated 401 bounded retry and logout");
        await store.EnterDemoAsync();
        var demo = store.Boxes.First(); await store.FavoriteAsync(demo);
        var persisted = new FBoxStore(options, client, realtime);
        AppHarness.Require(persisted.Boxes.First().Favorite == demo.Favorite, "Demo persistence");
        demo.State = 1; demo.Points.Add(new() { Id = 500, Name = "Target", Writable = true });
        var group = new WriteGroup { Targets = [new() { BoxNumber = demo.Number, PointId = 500 }] };
        await writing.WriteAsync(group, "1");
        AppHarness.Require(demo.Points.Single().Value == "1", "Demo batch writes to saved targets");
        demo.State = 0;
        try { await writing.WriteAsync(group, "0"); throw new Exception("Offline batch write accepted"); } catch (InvalidOperationException) { }
        AppHarness.Require(demo.Points.Single().Value == "1", "Offline batch retains previous value");
        Console.WriteLine("PASS: REST contracts, OAuth refresh, permissions, failed mutations, history parsing and persistence.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public string? LoginBody;
        public JsonNode? HistoryBody;
        public JsonNode? AlarmBody, GroupBody;
        public int RefreshCount, WriteCount, RetryCount;
        public bool FailDelete, AlwaysUnauthorized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/core/connect/token")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains("grant_type=refresh_token")) { Interlocked.Increment(ref RefreshCount); await Task.Delay(30, cancellationToken); }
                else LoginBody = body;
                return Json(new { access_token = RefreshCount > 0 ? "fresh" : "expired", refresh_token = "refresh" });
            }
            AppHarness.Require(request.Headers.Contains("X-FBox-ClientId"), "Client identity header");
            if (path == "/retry") { Interlocked.Increment(ref RetryCount); return request.Headers.Authorization?.Parameter != "fresh" || AlwaysUnauthorized ? new(HttpStatusCode.Unauthorized) : Json(new { ok = true }); }
            if (path.EndsWith("/grouped") && path.Contains("/client/")) return Raw("""[{"id":1,"name":"Default","boxRegs":[{"id":9,"boxNo":"123456789012","alias":"Fixture","connState":1,"favorite":false}]}]""");
            if (path.EndsWith("/dmon/grouped")) return Raw("""[{"id":1,"name":"默认组","items":[{"id":1,"name":"Read","privilege":4},{"id":2,"name":"Write","privilege":6}]}]""");
            if (path.EndsWith("/start")) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/alarm"))
            {
                if (request.Method == HttpMethod.Put) { AlarmBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); return new(HttpStatusCode.NoContent); }
                return Raw("[]");
            }
            if (path.EndsWith("/control/dmom/options")) return Raw("""[{"alias":"Fixture","bn":"123456789012","dgn":"Default","dn":"Write","duid":2}]""");
            if (path.EndsWith("/control/group")) { GroupBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); return new(HttpStatusCode.NoContent); }
            if (path.EndsWith("/dmon/value")) { WriteCount++; return new(HttpStatusCode.NoContent); }
            if (path.EndsWith("/hdataitems")) return Raw("""[{"uid":5,"name":"温度","channels":[{"uid":987,"name":"通道1"}]}]""");
            if (path == "/hs/v2/hdata/get") { HistoryBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); return Raw("""{"rows":[{"t":1793694000000,"c":[40]},{"t":1793694005000,"c":[47]}]}"""); }
            if (request.Method == HttpMethod.Delete && FailDelete) return new(HttpStatusCode.Forbidden);
            throw new InvalidOperationException("Unexpected fixture request: " + path);
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        private static HttpResponseMessage Raw(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
