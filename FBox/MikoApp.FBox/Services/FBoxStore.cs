using System.Text.Json;
using System.Text.Json.Nodes;
using MikoApp.FBox.Models;

namespace MikoApp.FBox.Services;

public sealed class FBoxStore
{
    public FBoxOptions Options { get; }
    public FBoxClient Client { get; }
    public FBoxRealtime Realtime { get; }
    public LocalState State { get; private set; } = new();
    public List<BoxGroup> Groups { get; private set; } = [];
    public Box? Selected { get; set; }
    public MonitorPoint? SelectedPoint { get; set; }
    public long HistoryId { get; set; }
    public long HistoryChannelId { get; set; }
    public List<HistoryChannel> HistoryChannels { get; private set; } = [];
    public string HistoryName { get; set; } = "温度采样";
    public string? ScannedNumber { get; set; }
    public List<HistorySample> Samples { get; private set; } = [];
    public string? Error { get; set; }
    public string? Notice { get; set; }
    public bool Busy { get; private set; }
    public bool Demo => Options.Demo;
    public bool Authenticated => Demo || Client.Authenticated;
    public string Username { get; set; } = "";
    public IEnumerable<Box> Boxes => Groups.SelectMany(g => g.Boxes);
    public event Action? Changed;
    private bool _initialized;
    private SynchronizationContext? _context;

    public FBoxStore(FBoxOptions options, FBoxClient client, FBoxRealtime realtime)
    {
        Options = options; Client = client; Realtime = realtime;
        realtime.Received += ApplyPush;
        realtime.Changed += () => Changed?.Invoke();
        LoadLocal();
    }

    private string StateFile => Path.Combine(Options.DataDirectory, "demo-state.json");
    private void LoadLocal()
    {
        try
        {
            if (File.Exists(StateFile)) State = JsonSerializer.Deserialize<LocalState>(File.ReadAllText(StateFile)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Error = "本地演示数据读取失败，已载入初始数据。"; }
        if (State.Groups.Count == 0) State.Groups = DemoGroups();
        if (Demo) { Groups = State.Groups; Username = State.Username; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Options.DataDirectory);
        var temp = StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, StateFile, true);
        Changed?.Invoke();
    }

    public async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        if (Busy) return false;
        Busy = true; Error = null; Notice = null; Changed?.Invoke();
        try { await action(); Notice = success; return true; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = e is HttpRequestException ? "网络连接失败，请检查服务地址并重试。" : e is TaskCanceledException ? "请求超时，请重试。" : e.Message;
            return false;
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task InitializeAsync()
    {
        _context ??= SynchronizationContext.Current;
        if (_initialized) return;
        _initialized = true;
        if (Authenticated) await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (Demo) { Groups = State.Groups; return; }
        var data = await Client.GetAsync("/api/client/v2/box/grouped?lite=true");
        var groups = data.Items().Select(g => new BoxGroup
        {
            Id = g.Number("id"), Name = g.Text("name"),
            Boxes = g["boxRegs"].Items().Select(b => new Box
            {
                Id = b.Number("id"), Alias = b.Text("alias"), Number = b.Text("boxNo"), Favorite = b.Flag("favorite"),
                State = (int)b.Number("connState"), AlarmCount = (int)b.Number("alarmCount"), Memo = b.Text("memo"), Address = b.Text("address"),
                Network = b.Number("net") == 0 ? "以太网" : "移动网络"
            }).ToList()
        }).ToList();
        Groups = groups;
        if (Selected is not null) Selected = Boxes.FirstOrDefault(b => b.Id == Selected.Id);
    }

    public async Task LoginAsync(string username, string password)
    {
        _context ??= SynchronizationContext.Current;
        await Client.LoginAsync(username, password);
        Options.Demo = false;
        Groups = []; Selected = null; Samples = [];
        Username = username;
        await RefreshAsync();
        await Realtime.StartAsync();
    }

    public async Task EnterDemoAsync()
    {
        await Realtime.StopAsync(); Client.Logout(); Options.Demo = true;
        Groups = State.Groups; Username = State.Username; Selected = null; Samples = [];
        Error = null; Notice = null; Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        await Realtime.StopAsync(); Client.Logout(); Options.Demo = false;
        Groups = []; Selected = null; SelectedPoint = null; Samples = []; Username = "";
        Error = null; Notice = null; Changed?.Invoke();
    }

    public BoxGroup? GroupOf(Box box) => Groups.FirstOrDefault(g => g.Boxes.Contains(box));

    public async Task AddBoxAsync(string number, string password, string alias, string group)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(number, "^[0-9]{12}$")) throw new InvalidOperationException("请输入 12 位设备序列号。");
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("请输入连接密码。");
        if (Boxes.Any(b => b.Number == number)) throw new InvalidOperationException("该设备已添加。");
        var target = Groups.FirstOrDefault(g => g.Name == group) ?? throw new InvalidOperationException("请选择设备分组。");
        if (!Demo)
        {
            await Client.PostAsync("/api/client/v2/box/register", new { boxNo = number, password, alias, groupId = target.Id });
            await RefreshAsync(); return;
        }
        target.Boxes.Add(new() { Id = NextId(), Number = number, Alias = string.IsNullOrWhiteSpace(alias) ? number : alias }); Save();
    }

    public async Task EditBoxAsync(Box box, string alias, string group, string memo)
    {
        if (string.IsNullOrWhiteSpace(alias)) throw new InvalidOperationException("请输入设备别名。");
        var target = Groups.First(g => g.Name == group);
        if (!Demo)
        {
            await Client.PostAsync("/api/client/v2/box/info", new { boxNo = box.Number, alias, groupName = group });
            if (memo != box.Memo) await Client.PostAsync($"/api/client/v2/box/{box.Id}/memo", new { memo });
        }
        var old = GroupOf(box); old?.Boxes.Remove(box); target.Boxes.Add(box);
        box.Alias = alias; box.Memo = memo; if (Demo) Save();
    }

    public async Task FavoriteAsync(Box box)
    {
        if (!Demo) await Client.PostAsync($"/api/client/v2/box/{box.Id}/fav/setting", new { isfav = !box.Favorite });
        box.Favorite = !box.Favorite; if (Demo) Save();
    }

    public async Task DeleteAsync(Box box)
    {
        if (!Demo) await Client.DeleteAsync($"/api/client/v2/box/{box.Id}?isRemoveScript=true");
        GroupOf(box)?.Boxes.Remove(box); if (Selected == box) Selected = null;
        if (Demo) Save();
    }

    public async Task AddGroupAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("分组名称不能为空或重复。");
        if (!Demo) { await Client.PutAsync("/api/client/v2/box/group/mgt", new { name }); await RefreshAsync(); }
        else { Groups.Add(new() { Id = NextId(), Name = name }); Save(); }
    }

    public async Task LoadPointsAsync(Box box)
    {
        if (Demo) return;
        var data = await Client.GetAsync($"/api/v2/box/{box.Id}/dmon/grouped");
        box.Points = data.Items().SelectMany(g => g["items"].Items().Select(p => new MonitorPoint
        {
            Id = p.Number("id"), Name = p.Text("name"), Group = g.Text("name", "默认组"), Register = p.Text("regName"), Address = (int)p.Number("addr"),
            Writable = (p.Number("privilege") & 2) != 0, Memo = p.Text("memo"), DataType = (int)p.Number("dataType"), AddressTagId = p.Number("addrTagUid"), TrueLabel = p["label"].Text("ttext", "1"), FalseLabel = p["label"].Text("ftext", "0")
        })).ToList();
        foreach (var g in data.Items()) await Client.PostAsync($"/api/dmon/group/{g.Number("id")}/start");
    }

    public async Task StopPointsAsync(Box box)
    {
        if (!Demo && Client.Authenticated) await Client.PostAsync($"/api/box/{box.Id}/dmon/stop");
    }

    public async Task AddPointAsync(Box box, MonitorPoint point, bool trafficSaving)
    {
        if (string.IsNullOrWhiteSpace(point.Name)) throw new InvalidOperationException("请输入监控点名称。");
        if (box.Points.Any(p => p.Name == point.Name)) throw new InvalidOperationException("监控点名称已存在。");
        if (!Demo)
        {
            await Client.PutAsync($"/api/v2/box/{box.Id}/dmon", new[] { new { name = point.Name, dmonGrpName = point.Group, regName = point.Register, addr = point.Address, devAlias = "Local", dataType = point.DataType, privilege = point.Writable ? 6 : 4, trafficSaving, memo = point.Memo, addrTagUid = point.AddressTagId, useAddrType = point.AddressTagId > 0 ? 1 : 0, label = new { ttext = point.TrueLabel, ftext = point.FalseLabel } } });
            await LoadPointsAsync(box);
        }
        else { point.Id = NextId(); box.Points.Add(point); Save(); }
    }

    public async Task WriteAsync(Box box, MonitorPoint point, string value)
    {
        if (!box.Online) throw new InvalidOperationException("设备离线，无法写入。");
        if (!point.Writable) throw new InvalidOperationException("该监控点为只读。");
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidOperationException("请输入有效数值。");
        if (!Demo) await Client.PostAsync($"/api/v2/box/{box.Id}/dmon/value", new { id = point.Id, value, type = 0, flag = false });
        point.Value = value; if (Demo) Save();
    }

    public async Task AddAlarmAsync(Box box, AlarmRule alarm)
    {
        if (string.IsNullOrWhiteSpace(alarm.Name) || string.IsNullOrWhiteSpace(alarm.Message)) throw new InvalidOperationException("请填写报警名称与内容。");
        if (!Demo) await Client.PutAsync($"/api/v2/box/{box.Id}/alarm", new[] { new { name = alarm.Name, alarmMsg = alarm.Message, regName = alarm.Register, addr = alarm.Address, devAlias = "Local", dataType = alarm.DataType, alarmGrpId = alarm.GroupId, addrTagUid = alarm.AddressTagId, useAddrType = alarm.AddressTagId > 0 ? 1 : 0, condMethod = 0, condition1 = 1, operand1 = alarm.DataType == 0 ? (alarm.On ? 1 : 0) : alarm.Threshold } });
        if (Demo) { alarm.Id = NextId(); box.Alarms.Add(alarm); Save(); }
        else await LoadAlarmsAsync(box);
    }

    public async Task LoadAlarmsAsync(Box box)
    {
        if (Demo) return;
        var rules = await Client.GetAsync($"/api/v2/box/{box.Id}/alarm");
        box.Alarms = rules.Items().Select(a => new AlarmRule { Id = a.Number("id"), Name = a.Text("name"), Message = a.Text("alarmMsg"), Register = a.Text("regName"), Address = (int)a.Number("addr"), DataType = (int)a.Number("dataType"), On = a.Number("operand1") == 1, Threshold = double.TryParse(a.Text("operand1"), out var threshold) ? threshold : 0, GroupId = a["group"].Number("uid"), AddressTagId = a.Number("addrTagUid") }).ToList();
    }

    public async Task LoadHistoryAsync(Box box, DateTime begin, DateTime end)
    {
        if (end < begin) throw new InvalidOperationException("结束时间不能早于开始时间。");
        Samples = [];
        if (Demo)
        {
            HistoryChannels = [new(1, 1, "温度采样 · 通道1")]; HistoryChannelId = 1; HistoryName = "温度采样";
            double[] values = [81,20,20,42,42,27,27,27,27,27,27,94,94,39,39,39,39,81,81,47,47,47,47,47,47,40];
            Samples = values.Select((v, i) => new HistorySample(new DateTime(2026,11,3,19,49,0).AddSeconds(i * 5), v)).Where(s => s.Time >= begin && s.Time <= end).ToList();
            return;
        }
        var items = await Client.GetAsync($"/api/v2/box/{box.Id}/hdataitems");
        HistoryChannels = items.Items().SelectMany(item => item["channels"].Items().Select(channel => new HistoryChannel(item.Number("uid"), channel.Number("uid"), item.Text("name") + " · " + channel.Text("name")))).ToList();
        var selected = HistoryChannels.FirstOrDefault(c => c.Id == HistoryChannelId) ?? HistoryChannels.FirstOrDefault();
        if (selected is null) return;
        HistoryId = selected.ItemId; HistoryChannelId = selected.Id; HistoryName = selected.Name;
        var data = await Client.PostAsync("/hs/v2/hdata/get", new { ids = new[] { selected.Id }, names = new[] { selected.Name }, begin = new DateTimeOffset(begin).ToUnixTimeMilliseconds().ToString(), end = new DateTimeOffset(end).ToUnixTimeMilliseconds().ToString(), limit = -500, type = 0, format = 0, g = 0, tr = 3 });
        Samples = data?["rows"].Items().Select(r => new HistorySample(DateTimeOffset.FromUnixTimeMilliseconds(r.Number("t")).LocalDateTime, double.TryParse(r["c"]?[0]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN)).Where(s => s.Time != default && double.IsFinite(s.Value)).OrderBy(s => s.Time).ToList() ?? [];
    }

    /// <summary>Apply a decoded hub notification on the UI context captured at initialization.</summary>
    public void ApplyPush(string name, JsonNode? data)
    {
        if (_context is not null && SynchronizationContext.Current != _context) { _context.Post(_ => ApplyPush(name, data), null); return; }
        if (Demo) return;
        if (name == "status")
        {
            foreach (var item in data.Items()) { var b = Boxes.FirstOrDefault(b => b.Id == item.Number("id")); if (b is not null) b.State = (int)item.Number("state"); }
            Changed?.Invoke(); return;
        }
        if (data is not JsonObject) return;
        var box = Boxes.FirstOrDefault(b => b.Id == data.Number("boxId"));
        if (box is not null && name == "alarm") box.AlarmCount = (int)data.Number("count");
        if (box is not null && name == "values") foreach (var item in data["values"].Items())
        {
            var p = box.Points.FirstOrDefault(p => p.Id == item.Number("id"));
            if (p is not null) p.Value = item.Number("status") == 0 ? item.Text("formatValue", item.Text("value")) : item.Number("status") == 2 ? "读取超时" : "读取失败";
        }
        Changed?.Invoke();
    }

    private static long NextId() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static List<BoxGroup> DemoGroups()
    {
        string[] numbers = ["338822080004", "300023100002", "300223090004", "300223110001", "300617060017", "300819120098", "301022070001", "301022111772", "301724120041"];
        var boxes = numbers.Select((n,i) => new Box { Id = i + 1, Number = n, Alias = i == 0 ? "9468-vnc" : n, Favorite = i == 1, AlarmCount = i == 5 ? 1 : 0, Network = i == 4 ? "2G" : "以太网" }).ToList();
        var sample = new Box { Id = 100, Number = "100017120001", Alias = "100017120001", Favorite = true, Points = Enumerable.Range(0,10).Select(i => new MonitorPoint { Id = i+1, Name = "var" + i, Address = i }).ToList(), Alarms = [new() { Id = 1 }] };
        return [new() { Id = 1, Name = "Default", Boxes = boxes }, new() { Id = 2, Name = "他人分享", Boxes = [new() { Id = 101, Number = "300023100008", Alias = "共享设备" }] }, new() { Id = 3, Name = "vbox" }, new() { Id = 4, Name = "SIM", Boxes = [sample] }, new() { Id = 5, Name = "Test" }];
    }
}
