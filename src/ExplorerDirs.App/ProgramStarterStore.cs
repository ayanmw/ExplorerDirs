using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ExplorerDirs.App;

internal sealed class ProgramStarterStore(string path)
{
    internal const int MaximumEntries = 200;
    private static readonly JsonSerializerOptions EntryJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly string _path = System.IO.Path.GetFullPath(path);
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string Path => _path;
    public string? LoadWarning { get; private set; }

    public IReadOnlyList<ProgramStarterEntry> Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        if (!File.Exists(_path)) return [];

        var recovered = new List<ProgramStarterEntry>();
        var complete = false;
        try
        {
            var values = new Dictionary<int, ProgramStarterEntry>();
            var count = -1;
            BigInteger version = -1;
            var inSection = false;
            complete = true;
            _lastSeenContent = File.ReadAllText(_path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (inSection) complete = false;
                    inSection = line.Equals("[ProgramStarter]", StringComparison.OrdinalIgnoreCase);
                    if (!inSection) complete = false;
                    continue;
                }

                var equals = line.IndexOf('=');
                if (!inSection || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (version >= 0 || !BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                            out version)) complete = false;
                }
                else if (key.Equals("Count", StringComparison.OrdinalIgnoreCase))
                {
                    if (count >= 0 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                            out count)) complete = false;
                }
                else if (key.StartsWith("Item", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(key[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                         index is >= 0 and < MaximumEntries && !values.ContainsKey(index) &&
                         TryDecodeEntry(value, out var entry))
                    values[index] = entry;
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "程序启动器配置格式高于当前版本；已停止写入原文件。";
                return [];
            }

            if (version != 1 || count is < 0 or > MaximumEntries || count != values.Count ||
                values.Keys.Any(index => index >= count)) complete = false;
            for (var index = 0; index < count && index < MaximumEntries; index++)
                if (!values.ContainsKey(index)) complete = false;
            recovered = values.OrderBy(item => item.Key).Select(item => item.Value).ToList();
            if (recovered.Select(item => item.Id).Distinct().Count() != recovered.Count) complete = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            complete = false;
        }

        if (complete) return recovered;

        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = $"程序启动器配置损坏；已恢复 {recovered.Count} 项并备份原文件。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canOverwrite = false;
            LoadWarning = "程序启动器配置损坏且备份失败；本次不会覆盖原文件。";
        }
        return recovered;
    }

    public void Save(IEnumerable<ProgramStarterEntry> entries)
    {
        if (!_canOverwrite) throw new IOException($"程序启动器原文件受到保护，已停止覆盖：{_path}");
        var items = entries.ToArray();
        if (items.Length > MaximumEntries) throw new IOException($"程序启动器最多保存 {MaximumEntries} 项。");
        if (items.Any(item => item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) ||
                              string.IsNullOrWhiteSpace(item.ExecutablePath)) ||
            items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw new IOException("程序启动器项目必须具有唯一 ID、名称和程序路径。");

        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable Program Starter. Close the app before editing.");
        builder.AppendLine("[ProgramStarter]");
        builder.AppendLine("FormatVersion=1");
        builder.AppendLine($"Count={items.Length}");
        for (var index = 0; index < items.Length; index++)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(items[index], EntryJsonOptions);
            builder.Append("Item").Append(index).Append('=').AppendLine(Convert.ToBase64String(json));
        }

        var content = builder.ToString();
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            $"程序启动器配置已被其他实例修改，请重新启动后再保存：{_path}");
        _lastSeenContent = content;
    }

    private static bool TryDecodeEntry(string value, out ProgramStarterEntry entry)
    {
        entry = null!;
        try
        {
            var candidate = JsonSerializer.Deserialize<ProgramStarterEntry>(Convert.FromBase64String(value), EntryJsonOptions);
            if (candidate is null || candidate.Id == Guid.Empty || string.IsNullOrWhiteSpace(candidate.Name) ||
                string.IsNullOrWhiteSpace(candidate.ExecutablePath)) return false;
            entry = candidate;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }
}
