using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal enum HiddenItemsPreference
{
    FollowWindows,
    Show,
    Hide
}

internal static class HiddenItemsPreferencePolicy
{
    internal static bool ShouldRefreshExistingView(bool preferenceChanged, bool viewAvailable) =>
        preferenceChanged && viewAvailable;

    internal static bool ShouldRefreshFollowWindowsChange(HiddenItemsPreference preference,
        bool previousWindowsValue, bool currentWindowsValue) =>
        preference == HiddenItemsPreference.FollowWindows && previousWindowsValue != currentWindowsValue;
}

internal sealed class HiddenItemsPreferenceStore(string path)
{
    private readonly string _path = System.IO.Path.GetFullPath(path);
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    public string Path => _path;
    public string? LoadWarning { get; private set; }

    public HiddenItemsPreference Load()
    {
        _lastSeenContent = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(_path)) return HiddenItemsPreference.FollowWindows;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        HiddenItemsPreference? preference = null;
        try
        {
            _lastSeenContent = File.ReadAllText(_path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[HiddenItems]", StringComparison.OrdinalIgnoreCase);
                    if (!inSection || seenSection) complete = false;
                    seenSection |= inSection;
                    continue;
                }
                var equals = line.IndexOf('=');
                if (!inSection || equals < 1) { complete = false; continue; }
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (version >= 0 || !BigInteger.TryParse(value, NumberStyles.None,
                            CultureInfo.InvariantCulture, out version)) complete = false;
                }
                else if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (preference.HasValue || !Enum.TryParse(value, ignoreCase: true, out HiddenItemsPreference parsed) ||
                        !Enum.IsDefined(parsed)) complete = false;
                    else preference = parsed;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "隐藏项目配置格式高于当前版本；已停止写入原文件。";
                return HiddenItemsPreference.FollowWindows;
            }
            if (version != 1 || !preference.HasValue || !seenSection) complete = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            complete = false;
        }

        if (complete) return preference!.Value;
        try
        {
            File.Copy(_path, _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "隐藏项目配置不完整；已备份原文件并恢复跟随 Windows。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "隐藏项目配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return HiddenItemsPreference.FollowWindows;
    }

    public void Save(HiddenItemsPreference preference)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        if (!_canOverwrite) throw new IOException($"隐藏项目配置原文件受到保护，已停止覆盖：{_path}");
        var content = "; Explorer Dirs portable preference. Close the app before editing." + Environment.NewLine +
            "[HiddenItems]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"Mode={preference}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "隐藏项目配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
