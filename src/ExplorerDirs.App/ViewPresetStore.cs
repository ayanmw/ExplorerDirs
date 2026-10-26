using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ExplorerDirs.App;

internal sealed record ViewPreset(string Name, uint Mode, int IconSize,
    IReadOnlyList<DisplayColumnState> DisplayColumns)
{
    public ViewState ApplyTo(ViewState current) => current with
    {
        Mode = Mode,
        IconSize = ViewStyle.NativeIconSize(Mode, IconSize),
        DisplayColumns = DisplayColumns.ToArray()
    };

    public ViewPreset NormalizeAndValidate()
    {
        if (Name is null || DisplayColumns is null || DisplayColumns.Any(column => column is null))
            throw new ArgumentException("预设名称或显示列配置缺失。");
        var name = Name.Trim();
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl))
            throw new ArgumentException("预设名称需为 1 到 64 个可见字符。", nameof(Name));
        if (Mode is < 1 or > 8 || IconSize is 0 or < -1 or > 65535)
            throw new ArgumentException("查看方式或图标大小无效。", nameof(Mode));
        if (!ColumnLayout.IsValid(DisplayColumns))
            throw new ArgumentException("显示列配置为空或无效。", nameof(DisplayColumns));
        return this with { Name = name, DisplayColumns = DisplayColumns.ToArray() };
    }
}

internal sealed class ViewPresetStore(string path)
{
    private const int CurrentFormatVersion = 1;
    private readonly string _path = Path.GetFullPath(path);
    private IReadOnlyList<ViewPreset> _presets = [];
    private string? _lastSeenContent;
    private bool _loaded;
    private bool _canOverwrite = true;

    public IReadOnlyList<ViewPreset> Presets => _presets;
    public string? LoadWarning { get; private set; }

    public IReadOnlyList<ViewPreset> Load()
    {
        _loaded = true;
        _canOverwrite = true;
        LoadWarning = null;
        _lastSeenContent = null;
        _presets = [];
        if (!File.Exists(_path)) return _presets;

        try
        {
            _lastSeenContent = File.ReadAllText(_path);
            var values = ParseIni(_lastSeenContent);
            if (!values.TryGetValue("FormatVersion", out var versionText) ||
                !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                return RecoverInvalid("查看预设配置格式无效；已备份原文件，当前不加载预设。");
            if (version > CurrentFormatVersion)
            {
                _canOverwrite = false;
                LoadWarning = "查看预设配置格式高于当前版本；已停止写入原文件。";
                return _presets;
            }
            if (version != CurrentFormatVersion || values.Count != 2 ||
                !values.TryGetValue("Items", out var encodedItems))
                return RecoverInvalid("查看预设配置不完整；已备份原文件，当前不加载预设。");

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedItems));
            var loaded = JsonSerializer.Deserialize<List<ViewPreset>>(json)
                ?? throw new FormatException("预设数据为空。");
            var validated = loaded.Select(preset => (preset ?? throw new FormatException("预设项为空。"))
                .NormalizeAndValidate()).ToArray();
            if (validated.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != validated.Length)
                throw new FormatException("预设名称重复。");
            _presets = validated;
            return _presets;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or
                                   JsonException or ArgumentException)
        {
            return RecoverInvalid("查看预设配置无法读取；已备份原文件，当前不加载预设。");
        }
    }

    public void SavePreset(ViewPreset preset)
    {
        EnsureLoaded();
        var validated = preset.NormalizeAndValidate();
        var next = _presets.Where(existing =>
            !existing.Name.Equals(validated.Name, StringComparison.OrdinalIgnoreCase)).Append(validated).ToArray();
        Save(next);
    }

    public bool DeletePreset(string name)
    {
        EnsureLoaded();
        var next = _presets.Where(preset =>
            !preset.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (next.Length == _presets.Count) return false;
        Save(next);
        return true;
    }

    private void EnsureLoaded()
    {
        if (!_loaded) Load();
        if (!_canOverwrite)
            throw new IOException($"查看预设配置原文件受到保护，已停止覆盖：{_path}");
    }

    private void Save(IReadOnlyList<ViewPreset> presets)
    {
        var validated = presets.Select(preset => preset.NormalizeAndValidate()).ToArray();
        if (validated.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != validated.Length)
            throw new ArgumentException("预设名称不能重复。", nameof(presets));
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(validated)));
        var content = "; Explorer Dirs named view presets. Close the app before editing." + Environment.NewLine +
            "[ViewPresets]" + Environment.NewLine +
            $"FormatVersion={CurrentFormatVersion}" + Environment.NewLine +
            $"Items={payload}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "查看预设配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
        _presets = validated;
    }

    private IReadOnlyList<ViewPreset> RecoverInvalid(string warning)
    {
        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = warning;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canOverwrite = false;
            LoadWarning = "查看预设配置无法读取且备份失败；本次不会覆盖原文件。";
        }
        return _presets;
    }

    private static Dictionary<string, string> ParseIni(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;
        var seenSection = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line.Equals("[ViewPresets]", StringComparison.OrdinalIgnoreCase);
                if (!inSection || seenSection) throw new FormatException("Unexpected INI section.");
                seenSection = true;
                continue;
            }
            var separator = line.IndexOf('=');
            if (!inSection || separator < 1 ||
                !values.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim()))
                throw new FormatException("Unexpected or repeated INI value.");
        }
        if (!seenSection) throw new FormatException("Missing INI section.");
        return values;
    }
}
