using System.Text.Json.Nodes;

namespace MikoApp.FBox.Models;

public sealed class BoxGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = "Default";
    public List<Box> Boxes { get; set; } = [];
}

public sealed class Box
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Favorite { get; set; }
    public int State { get; set; }
    public int AlarmCount { get; set; }
    public string Network { get; set; } = "以太网";
    public string Memo { get; set; } = "";
    public string Address { get; set; } = "";
    public List<MonitorPoint> Points { get; set; } = [];
    public List<AlarmRule> Alarms { get; set; } = [];
    public bool Online => State == 1;
    public string Title => string.IsNullOrWhiteSpace(Alias) ? Number : Alias;
}

public sealed class MonitorPoint
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Group { get; set; } = "默认组";
    public string Register { get; set; } = "LW";
    public int Address { get; set; }
    public string? Value { get; set; }
    public bool Writable { get; set; }
    public string Memo { get; set; } = "";
    public int DataType { get; set; }
    public string TrueLabel { get; set; } = "1";
    public string FalseLabel { get; set; } = "0";
    public long AddressTagId { get; set; }
}

public sealed class AlarmRule
{
    public long Id { get; set; }
    public string Name { get; set; } = "报警";
    public string Message { get; set; } = "测试报警";
    public string Register { get; set; } = "LB";
    public int Address { get; set; }
    public bool On { get; set; } = true;
    public bool Active { get; set; }
    public int DataType { get; set; }
    public long AddressTagId { get; set; }
    public long GroupId { get; set; }
    public double Threshold { get; set; }
}

public sealed record HistorySample(DateTime Time, double Value);
public sealed record HistoryChannel(long ItemId, long Id, string Name);

public sealed class LocalState
{
    public List<BoxGroup> Groups { get; set; } = [];
    public string Username { get; set; } = "演示用户";
    public bool Notifications { get; set; } = true;
    public bool AlarmNotifications { get; set; } = true;
    public bool Sound { get; set; } = true;
    public List<string> Feedback { get; set; } = [];
    public List<WriteGroup> WriteGroups { get; set; } = [];
}

public sealed class WriteGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Keyword { get; set; } = "";
    public int DataType { get; set; }
    public List<WriteTarget> Targets { get; set; } = [];
}

public sealed class WriteTarget
{
    public string BoxNumber { get; set; } = "";
    public string BoxAlias { get; set; } = "";
    public long PointId { get; set; }
    public string PointName { get; set; } = "";
    public string Group { get; set; } = "";
    public bool Selected { get; set; }
}

public static class JsonValues
{
    public static string Text(this JsonNode? n, string key, string fallback = "") => n?[key]?.ToString() ?? fallback;
    public static long Number(this JsonNode? n, string key) => long.TryParse(n.Text(key), out var v) ? v : 0;
    public static bool Flag(this JsonNode? n, string key) => n.Text(key) is "true" or "True" or "1";
    public static IEnumerable<JsonNode> Items(this JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
}
