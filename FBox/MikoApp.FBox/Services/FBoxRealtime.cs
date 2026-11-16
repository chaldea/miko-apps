using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy = Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json.Linq;

namespace MikoApp.FBox.Services;

public sealed class FBoxRealtime(FBoxClient client, FBoxOptions options) : IAsyncDisposable, IDisposable
{
    private Microsoft.AspNetCore.SignalR.Client.HubConnection? _connection;
    private Legacy.HubConnection? _legacy;
    private Timer? _heartbeat;
    public string Status { get; private set; } = "未连接";
    public bool Connected => _connection?.State == HubConnectionState.Connected || _legacy?.State == Legacy.ConnectionState.Connected;
    public event Action<string, JsonNode?>? Received;
    public event Action? Changed;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await StopAsync();
        if (options.Demo || !client.Authenticated) return;
        if (options.SignalRVersion == "legacy")
        {
            _legacy = new Legacy.HubConnection(client.Endpoint("/push/signalr").ToString(), new Dictionary<string, string> { ["at"] = client.AccessToken ?? "", ["cid"] = client.DeviceClientId }, false);
            var hub = _legacy.CreateHubProxy("clientHub");
            hub.On<JArray>("boxConnStateChanged", value => Received?.Invoke("status", JsonNode.Parse(value.ToString())));
            hub.On<long, int>("boxAlarmNotify", (id, count) => Received?.Invoke("alarm", new JsonObject { ["boxId"] = id, ["count"] = count }));
            hub.On<string, JArray, string>("dMonUpdateValue", (_, values, id) => Received?.Invoke("values", new JsonObject { ["boxId"] = long.Parse(id), ["values"] = JsonNode.Parse(values.ToString()) }));
            _legacy.Reconnecting += () => { Status = "正在重新连接"; Changed?.Invoke(); };
            _legacy.Reconnected += () => { Status = "实时连接已恢复，请刷新订阅"; Changed?.Invoke(); };
            _legacy.Closed += () => { Status = "实时连接断开，可手动刷新"; Changed?.Invoke(); };
            try
            {
                await _legacy.Start().WaitAsync(TimeSpan.FromSeconds(25), ct);
                Status = "实时连接正常";
                _heartbeat = new Timer(async _ => { try { await hub.Invoke("heartbeat"); } catch (Exception) { /* Transport reconnect reports its own status. */ } }, null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
            }
            catch (Exception e) when (e is not OperationCanceledException) { Status = "实时连接失败，可手动刷新"; }
            Changed?.Invoke();
            return;
        }
        _connection = new HubConnectionBuilder()
            .WithUrl(client.Endpoint("/push?clientId=" + Uri.EscapeDataString(client.DeviceClientId)), o => o.AccessTokenProvider = () => Task.FromResult(client.AccessToken))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
            .Build();
        _connection.On<JsonElement>("boxConnStateChanged", value => Received?.Invoke("status", JsonNode.Parse(value.GetRawText())));
        _connection.On<long, int>("boxAlarmNotify", (boxId, count) => Received?.Invoke("alarm", new JsonObject { ["boxId"] = boxId, ["count"] = count }));
        _connection.On<string, JsonElement, long>("dMonUpdateValue", (_, values, boxId) => Received?.Invoke("values", new JsonObject { ["boxId"] = boxId, ["values"] = JsonNode.Parse(values.GetRawText()) }));
        _connection.Reconnecting += _ => { Status = "正在重新连接"; Changed?.Invoke(); return Task.CompletedTask; };
        _connection.Reconnected += _ => { Status = "实时连接已恢复，请刷新订阅"; Changed?.Invoke(); return Task.CompletedTask; };
        _connection.Closed += _ => { Status = "实时连接断开，可手动刷新"; Changed?.Invoke(); return Task.CompletedTask; };
        try { await _connection.StartAsync(ct); Status = "实时连接正常"; }
        catch (Exception e) when (e is not OperationCanceledException) { Status = "实时连接失败，可手动刷新"; }
        Changed?.Invoke();
    }

    public async Task StopAsync()
    {
        _heartbeat?.Dispose(); _heartbeat = null;
        _legacy?.Dispose(); _legacy = null;
        if (_connection is not null) { await _connection.DisposeAsync().ConfigureAwait(false); _connection = null; }
        Status = "未连接";
    }

    public async ValueTask DisposeAsync() => await StopAsync();
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
