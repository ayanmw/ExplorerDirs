using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace ExplorerDirs.App;

internal sealed record PinnedTabEntry(int Pane, int Index, string Path);

internal sealed class PinnedTabsStore
{
    private readonly string _path;
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string? LoadWarning { get; private set; }

    public PinnedTabsStore(string path) => _path = path;

    public IReadOnlyList<PinnedTabEntry> Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_path)) return [];

        var entries = new List<PinnedTabEntry>();
        var complete = true;
        var inPins = false;
        var seenPins = false;
        BigInteger version = -1;
        var count = -1;
        var rawEntries = new Dictionary<int, string>();
        try
        {
            _lastSeenContent = File.ReadAllText(_path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inPins = line.Equals("[Pins]", StringComparison.OrdinalIgnoreCase);
                    if (!inPins || seenPins) complete = false;
                    seenPins |= inPins;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (!inPins || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) complete = false;
                    else { if (version >= 0) complete = false; version = BigInteger.Max(version, parsed); }
                }
                else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
                {
                    if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 0 || count > 400)
                        complete = false;
                }
                else if (key.StartsWith("Entry", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(key[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                         index >= 0 && index < 400)
                {
                    if (!rawEntries.TryAdd(index, value)) complete = false;
                }
                else complete = false;
            }
            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "固定标签配置格式高于当前版本；已停止写入原文件。";
                return [];
            }
            if (version != 1 || !seenPins || count != rawEntries.Count) complete = false;
            foreach (var (index, value) in rawEntries.OrderBy(pair => pair.Key))
            {
                if (index >= count) complete = false;
                var fields = value.Split('|');
                if (fields.Length != 3 ||
                    !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pane) || pane is < 0 or > 3 ||
                    !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tab) || tab is < 0 or > 99)
                { complete = false; continue; }
                try
                {
                    var path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(fields[2]));
                    if (string.IsNullOrWhiteSpace(path)) { complete = false; continue; }
                    entries.Add(new PinnedTabEntry(pane, tab, path));
                }
                catch (Exception) { complete = false; }
            }
            if (entries.GroupBy(entry => (entry.Pane, entry.Index)).Any(group => group.Count() > 1)) complete = false;
        }
        catch (Exception) { complete = false; }
        if (complete) return entries;

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "固定标签配置不完整；已备份原文件并恢复可读取的设置。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "固定标签配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return entries;
    }

    public void Save(IReadOnlyList<PinnedTabEntry> entries)
    {
        if (!_canOverwrite) throw new IOException($"固定标签配置原文件受到保护，已停止覆盖：{_path}");
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs pinned tabs. Close the app before editing.");
        builder.AppendLine("[Pins]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={entries.Count}");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            builder.AppendLine($"Entry{index}={entry.Pane}|{entry.Index}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Path))}");
        }
        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "固定标签配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }

    public static IReadOnlySet<int> MatchPane(IReadOnlyList<TabSettings> tabs,
        IReadOnlyList<PinnedTabEntry> entries, int pane)
    {
        return entries.Where(entry => entry.Pane == pane && entry.Index < tabs.Count &&
            string.Equals(entry.Path, tabs[entry.Index].Path, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Index).ToHashSet();
    }
}
