using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record NavigationTreeWidths(double Shared, double Pane0, double Pane1, double Pane2, double Pane3)
{
    internal const double Minimum = 96;
    internal const double Maximum = 600;
    internal static NavigationTreeWidths Defaults { get; } = new(190, 160, 160, 160, 160);

    internal bool IsValid => IsWidth(Shared) && IsWidth(Pane0) && IsWidth(Pane1) &&
        IsWidth(Pane2) && IsWidth(Pane3);

    internal double GetPaneWidth(int pane) => pane switch
    {
        0 => Pane0,
        1 => Pane1,
        2 => Pane2,
        3 => Pane3,
        _ => throw new ArgumentOutOfRangeException(nameof(pane))
    };

    internal NavigationTreeWidths WithPaneWidth(int pane, double width) => pane switch
    {
        0 => this with { Pane0 = width },
        1 => this with { Pane1 = width },
        2 => this with { Pane2 = width },
        3 => this with { Pane3 = width },
        _ => throw new ArgumentOutOfRangeException(nameof(pane))
    };

    internal static bool IsWidth(double value) => double.IsFinite(value) && value is >= Minimum and <= Maximum;
}

internal sealed class NavigationTreeWidthsStore(string path)
{
    private static readonly string[] WidthKeys = ["Shared", "Pane0", "Pane1", "Pane2", "Pane3"];
    private string? _lastSeen;
    private bool _canOverwrite = true;

    internal string? LoadWarning { get; private set; }

    internal NavigationTreeWidths Load()
    {
        _lastSeen = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return NavigationTreeWidths.Defaults;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inGeneral = false;
        var inWidths = false;
        var seenGeneral = false;
        var seenWidths = false;
        var complete = true;
        BigInteger version = -1;
        var widths = NavigationTreeWidths.Defaults;
        try
        {
            _lastSeen = File.ReadAllText(path);
            foreach (var raw in _lastSeen.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var section = line[1..^1];
                    inGeneral = section.Equals("General", StringComparison.OrdinalIgnoreCase);
                    inWidths = section.Equals("Widths", StringComparison.OrdinalIgnoreCase);
                    if (inGeneral)
                    {
                        if (seenGeneral) complete = false;
                        seenGeneral = true;
                    }
                    else if (inWidths)
                    {
                        if (seenWidths) complete = false;
                        seenWidths = true;
                    }
                    else complete = false;
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (inGeneral && key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion))
                        complete = false;
                    else
                    {
                        if (version >= 0) complete = false;
                        version = BigInteger.Max(version, parsedVersion);
                    }
                }
                else if (inWidths && WidthKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!values.TryAdd(key, value)) complete = false;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "目录树宽度配置格式高于当前版本；已停止写入原文件。";
                return NavigationTreeWidths.Defaults;
            }

            if (version != 1 || !seenGeneral || !seenWidths) complete = false;
            foreach (var key in WidthKeys)
            {
                if (!values.TryGetValue(key, out var raw)) { complete = false; continue; }
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    !NavigationTreeWidths.IsWidth(value))
                {
                    complete = false;
                    continue;
                }
                widths = key switch
                {
                    "Shared" => widths with { Shared = value },
                    "Pane0" => widths with { Pane0 = value },
                    "Pane1" => widths with { Pane1 = value },
                    "Pane2" => widths with { Pane2 = value },
                    "Pane3" => widths with { Pane3 = value },
                    _ => widths
                };
            }
        }
        catch (Exception) { complete = false; }

        if (complete) return widths;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "目录树宽度配置不完整；已备份原文件并恢复可读取的宽度。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "目录树宽度配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return widths;
    }

    internal void Save(NavigationTreeWidths widths)
    {
        if (!_canOverwrite) throw new IOException("目录树宽度配置原文件受到保护，已停止覆盖。");
        if (!widths.IsValid) throw new ArgumentOutOfRangeException(nameof(widths), "目录树宽度必须在 96 至 600 DIP 之间。");
        var content = new StringBuilder("; Explorer Dirs portable navigation tree widths.\n[General]\nFormatVersion=1\n[Widths]\n")
            .AppendLine("Shared=" + Format(widths.Shared))
            .AppendLine("Pane0=" + Format(widths.Pane0))
            .AppendLine("Pane1=" + Format(widths.Pane1))
            .AppendLine("Pane2=" + Format(widths.Pane2))
            .AppendLine("Pane3=" + Format(widths.Pane3))
            .ToString();
        PortableIniWriter.WriteIfUnchanged(path, _lastSeen, content,
            "目录树宽度配置已被其他实例修改；请重启程序后再保存。");
        _lastSeen = content;
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
