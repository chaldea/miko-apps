using MikoApp.FBox.Models;

namespace MikoApp.FBox.Services;

/// <summary>Explicit targets prevent a later name match from expanding a saved write group.</summary>
public sealed class FBoxWriting(FBoxStore store)
{
    private const string Path = "/api/v2/control/";

    public async Task<List<WriteGroup>> GroupsAsync()
    {
        if (store.Demo) return store.State.WriteGroups;
        return (await store.Client.GetAsync(Path + "groups")).Items().Select(g => new WriteGroup
        {
            Id = g.Number("uid"), Name = g.Text("name"), Keyword = g.Text("keyword"), DataType = (int)g.Number("type")
        }).ToList();
    }

    public async Task<List<WriteTarget>> SearchAsync(string keyword, int dataType)
    {
        if (string.IsNullOrWhiteSpace(keyword)) throw new InvalidOperationException("请输入监控点名称。");
        if (store.Demo) return store.Boxes.SelectMany(b => b.Points.Where(p => p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) && p.DataType == dataType && p.Writable)
            .Select(p => new WriteTarget { BoxNumber = b.Number, BoxAlias = b.Title, PointId = p.Id, PointName = p.Name, Group = p.Group })).ToList();
        return (await store.Client.PostAsync(Path + "dmom/options", new { keyword, dataType })).Items().Select(t => new WriteTarget
        {
            BoxNumber = t.Text("bn"), BoxAlias = t.Text("alias"), PointId = t.Number("duid"), PointName = t.Text("dn"), Group = t.Text("dgn")
        }).ToList();
    }

    public async Task SaveAsync(WriteGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name)) throw new InvalidOperationException("请输入分组名称。");
        if (group.Targets.Count == 0) throw new InvalidOperationException("请搜索并勾选至少一个监控点。");
        if (store.Demo)
        {
            if (store.State.WriteGroups.Any(g => g.Name == group.Name)) throw new InvalidOperationException("分组名称已存在。");
            group.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            store.State.WriteGroups.Add(group); store.Save(); return;
        }
        await store.Client.PutAsync(Path + "group", new
        {
            name = group.Name, keyword = group.Keyword, type = group.DataType, memo = "",
            controlOptions = group.Targets.Select(t => new { alias = t.BoxAlias, bn = t.BoxNumber, dgn = t.Group, dn = t.PointName, duid = t.PointId }).ToArray()
        });
    }

    public async Task WriteAsync(WriteGroup group, string input)
    {
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new InvalidOperationException("请输入有效数值。");
        if (group.DataType == 0 && value is not (0 or 1)) throw new InvalidOperationException("位类型只能写入 0 或 1。");
        if (!store.Demo) { await store.Client.PostAsync(Path + "group/write/value", new { uid = group.Id, value }); return; }
        var targets = group.Targets.Select(t =>
        {
            var box = store.Boxes.FirstOrDefault(b => b.Number == t.BoxNumber);
            return (Box: box, Point: box?.Points.FirstOrDefault(p => p.Id == t.PointId));
        }).ToList();
        if (targets.Count == 0 || targets.Any(t => t.Box?.Online != true || t.Point?.Writable != true)) throw new InvalidOperationException("目标设备离线、监控点只读或已删除，未执行写入。");
        foreach (var target in targets) await store.WriteAsync(target.Box!, target.Point!, input);
    }
}
