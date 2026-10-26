using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record StripProportions(double First, double Second, double Third, double Fourth = 0)
{
    internal double[] Weights => Fourth == 0 ? [First, Second, Third] : [First, Second, Third, Fourth];
    internal bool IsValid => Weights.All(weight => double.IsFinite(weight) && weight > 0) && double.IsFinite(Weights.Sum());
}

internal sealed record LayoutProportions(double Horizontal, double Vertical, StripProportions? Strip = null,
    double? BottomHorizontal = null)
{
    internal static LayoutProportions Equal { get; } = new(0.5, 0.5);
    internal bool IsValid => double.IsFinite(Horizontal) && Horizontal is > 0 and < 1 &&
        double.IsFinite(Vertical) && Vertical is > 0 and < 1 && (Strip is null || Strip.IsValid) &&
        (BottomHorizontal is null || double.IsFinite(BottomHorizontal.Value) && BottomHorizontal is > 0 and < 1);
    internal double BottomFraction => BottomHorizontal ?? Horizontal;
    internal bool IsValidFor(LayoutPreset layout) => IsValid && (BottomHorizontal is null || layout.Id == 4) && (Strip is null ||
        Math.Max(layout.Rows, layout.Columns) >= 3 && Strip.Weights.Length == Math.Max(layout.Rows, layout.Columns));
    internal double[] StripWeights(int count) => Strip?.Weights ?? Enumerable.Repeat(1d, count).ToArray();
}

// Independent of session versions: older releases leave divider settings alone.
internal sealed class LayoutProportionsStore(string path)
{
    private string? _lastSeen;
    private bool _canOverwrite = true;
    private bool _needsUpgradeBackup;
    internal string? LoadWarning { get; private set; }

    internal Dictionary<int, LayoutProportions> Load()
    {
        _lastSeen = null;
        _canOverwrite = true;
        _needsUpgradeBackup = false;
        LoadWarning = null;
        var result = new Dictionary<int, LayoutProportions>();
        if (!File.Exists(path)) return result;
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? section = null;
        var complete = true;
        BigInteger version = -1;
        try
        {
            _lastSeen = File.ReadAllText(path);
            foreach (var raw in _lastSeen.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var name = line[1..^1];
                    section = new(StringComparer.OrdinalIgnoreCase);
                    if (!sections.TryAdd(name, section)) complete = false;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (section is null || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                // Even damaged duplicate version fields must protect newer data.
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase) &&
                    BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion))
                    version = BigInteger.Max(version, parsedVersion);
                if (!section.TryAdd(key, value)) complete = false;
            }
            if (version > 4)
            {
                _canOverwrite = false;
                LoadWarning = "布局比例配置格式高于当前版本；已停止写入原文件。";
                return result;
            }
            _needsUpgradeBackup = version >= 1 && version <= 3;
            if ((version < 1 || version > 4) || !sections.TryGetValue("General", out var general) ||
                general.Count != 1 || general.GetValueOrDefault("FormatVersion") != version.ToString(CultureInfo.InvariantCulture)) complete = false;
            foreach (var (name, values) in sections)
            {
                if (name.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.StartsWith("Layout", StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(name[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                    !LayoutPreset.All.Any(layout => layout.Id == id) || (version == 1 && id > 6) || (version == 2 && id > 8))
                { complete = false; continue; }
                if (id >= 9)
                {
                    if (values.Count != 1 || !values.TryGetValue("Weights", out var weightsText))
                    { complete = false; continue; }
                    var fields = weightsText.Split(',');
                    var weights = new List<double>();
                    foreach (var field in fields)
                        if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)) weights.Add(weight);
                    var count = Math.Max(LayoutPreset.Find(id).Rows, LayoutPreset.Find(id).Columns);
                    if (weights.Count != fields.Length || weights.Count != count)
                    { complete = false; continue; }
                    var strip = new StripProportions(weights[0], weights[1], weights[2], count == 4 ? weights[3] : 0);
                    var proportions = new LayoutProportions(0.5, 0.5, strip);
                    if (!proportions.IsValidFor(LayoutPreset.Find(id)) || !result.TryAdd(id, proportions)) complete = false;
                    continue;
                }
                double? bottom = null;
                if (values.ContainsKey("BottomHorizontal"))
                {
                    if (version < 4 || id != 4 || !Read(values, "BottomHorizontal", out var parsedBottom))
                    { complete = false; continue; }
                    bottom = parsedBottom;
                }
                if (values.Count != (bottom is null ? 2 : 3) ||
                    !Read(values, "Horizontal", out var horizontal) || !Read(values, "Vertical", out var vertical) ||
                    !new LayoutProportions(horizontal, vertical, BottomHorizontal: bottom).IsValid ||
                    !result.TryAdd(id, new(horizontal, vertical, BottomHorizontal: bottom)))
                    complete = false;
            }
        }
        catch (Exception) { complete = false; }
        if (complete) return result;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "布局比例配置不完整；已备份原文件，无法读取的布局恢复均分。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "布局比例配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return result;
    }

    internal void Save(IReadOnlyDictionary<int, LayoutProportions> proportions)
    {
        if (!_canOverwrite) throw new IOException("布局比例配置原文件受到保护，已停止覆盖。");
        if (proportions.Any(pair => !LayoutPreset.All.Any(layout => layout.Id == pair.Key) || !pair.Value.IsValidFor(LayoutPreset.Find(pair.Key))))
            throw new ArgumentException("布局比例无效。", nameof(proportions));
        var text = new StringBuilder("; Explorer Dirs portable layout proportions.\n[General]\nFormatVersion=4\n");
        foreach (var (id, value) in proportions.OrderBy(pair => pair.Key))
        {
            text.AppendLine($"\n[Layout{id}]");
            if (id >= 9)
            {
                var layout = LayoutPreset.Find(id);
                text.AppendLine("Weights=" + string.Join(',', value.StripWeights(Math.Max(layout.Rows, layout.Columns))
                    .Select(weight => weight.ToString("R", CultureInfo.InvariantCulture))));
                continue;
            }
            text.AppendLine("Horizontal=" + value.Horizontal.ToString("R", CultureInfo.InvariantCulture));
            text.AppendLine("Vertical=" + value.Vertical.ToString("R", CultureInfo.InvariantCulture));
            if (value.BottomHorizontal is { } bottom) text.AppendLine("BottomHorizontal=" + bottom.ToString("R", CultureInfo.InvariantCulture));
        }
        var content = text.ToString();
        if (_needsUpgradeBackup && _lastSeen is not null)
        {
            File.WriteAllText(path + $".before-v4-{Guid.NewGuid():N}.bak", _lastSeen);
            _needsUpgradeBackup = false;
        }
        PortableIniWriter.WriteIfUnchanged(path, _lastSeen, content, "布局比例配置已被其他实例修改；请重启后再保存。");
        _lastSeen = content;
    }

    private static bool Read(Dictionary<string, string> values, string key, out double value) =>
        double.TryParse(values.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
