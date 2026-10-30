using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal sealed class RecentFavoritePathStore(string path)
{
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    internal string? LoadWarning { get; private set; }

    internal static string PathForSession(string sessionPath) =>
        Path.ChangeExtension(sessionPath, ".RecentFavorite.ini");

    internal string? Load()
    {
        _lastSeenContent = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return null;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        string? favoritePath = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[RecentFavorite]", StringComparison.OrdinalIgnoreCase);
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
                    if (!BigInteger.TryParse(value, out var parsed) || version >= 0) complete = false;
                    else version = parsed;
                }
                else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                {
                    if (favoritePath is not null || value.Length == 0 || value.Contains('\r')) complete = false;
                    else favoritePath = value;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "最近收藏配置格式高于当前版本；已停止写入原文件。";
                return null;
            }
            if (version != 1 || favoritePath is null || !seenSection) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete) return favoritePath;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "最近收藏配置不完整；已备份原文件并清除本次标题记录。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "最近收藏配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return null;
    }

    internal void Save(string favoritePath)
    {
        if (string.IsNullOrWhiteSpace(favoritePath) || favoritePath.Contains('\r') || favoritePath.Contains('\n'))
            throw new ArgumentException("收藏路径为空或包含换行符。", nameof(favoritePath));
        if (!_canOverwrite) throw new IOException("最近收藏配置原文件受到保护，已停止覆盖：" + path);
        var content = "; Explorer Dirs per-window session state. Close the app before editing." + Environment.NewLine +
            "[RecentFavorite]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            "Path=" + favoritePath + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "最近收藏配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
