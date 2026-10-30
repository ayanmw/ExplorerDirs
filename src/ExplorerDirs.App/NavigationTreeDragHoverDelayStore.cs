using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal static class NavigationTreeDragHoverDelay
{
    internal const int DefaultMilliseconds = 1000;
    internal static readonly int[] AllowedMilliseconds = [200, 500, 1000, 1500, 2000, 3000];
    internal static bool IsAllowed(int milliseconds) => AllowedMilliseconds.Contains(milliseconds);
}

internal sealed class NavigationTreeDragHoverDelayStore(string path)
{
    private const string Section = "NavigationTree";
    private const string Key = "DragHoverExpandDelayMilliseconds";
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    internal string? LoadWarning { get; private set; }

    internal int Load()
    {
        _lastSeenContent = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return NavigationTreeDragHoverDelay.DefaultMilliseconds;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        int? delay = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals($"[{Section}]", StringComparison.OrdinalIgnoreCase);
                    if (!inSection || seenSection) complete = false;
                    seenSection |= inSection;
                    continue;
                }

                var equals = line.IndexOf('=');
                if (!inSection || equals < 1) { complete = false; continue; }
                var keyName = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (keyName.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        complete = false;
                    else
                    {
                        if (version >= 0) complete = false;
                        version = BigInteger.Max(version, parsed);
                    }
                }
                else if (keyName.Equals(Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (delay.HasValue || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                        !NavigationTreeDragHoverDelay.IsAllowed(parsed)) complete = false;
                    else delay = parsed;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "目录树拖入展开延时配置格式高于当前版本；已停止写入原文件。";
                return NavigationTreeDragHoverDelay.DefaultMilliseconds;
            }
            if (version != 1 || !delay.HasValue || !seenSection) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete) return delay!.Value;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "目录树拖入展开延时配置不完整；已备份原文件并恢复默认值。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "目录树拖入展开延时配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return NavigationTreeDragHoverDelay.DefaultMilliseconds;
    }

    internal void Save(int milliseconds)
    {
        if (!NavigationTreeDragHoverDelay.IsAllowed(milliseconds))
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (!_canOverwrite) throw new IOException($"目录树拖入展开延时配置原文件受到保护，已停止覆盖：{path}");
        var content = "; Explorer Dirs portable preference. Close the app before editing." + Environment.NewLine +
            $"[{Section}]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"{Key}={milliseconds.ToString(CultureInfo.InvariantCulture)}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "目录树拖入展开延时配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
