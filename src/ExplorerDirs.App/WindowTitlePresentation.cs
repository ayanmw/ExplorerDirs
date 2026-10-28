using System.Globalization;
using System.IO;
using System.Numerics;

namespace ExplorerDirs.App;

internal enum WindowTitleMode
{
    None,
    FullPath,
    FolderName,
    SelectedItemPath
}

internal static class WindowTitlePresentation
{
    internal static string Format(string applicationTitle, WindowTitleMode mode, string currentFolder,
        string? selectedItemPath)
    {
        var detail = mode switch
        {
            WindowTitleMode.FullPath => currentFolder,
            WindowTitleMode.FolderName => FolderName(currentFolder),
            WindowTitleMode.SelectedItemPath => selectedItemPath,
            _ => null
        };
        return string.IsNullOrWhiteSpace(detail) ? applicationTitle : $"{applicationTitle} — {detail}";
    }

    private static string FolderName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0) return path;
        var root = Path.GetPathRoot(path);
        if (root is not null && trimmed.Equals(root.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return root;
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : Path.GetPathRoot(path) ?? path;
    }
}

internal sealed class WindowTitlePreferenceStore(string path)
{
    private string? _lastSeenContent;
    private bool _canOverwrite = true;

    internal string? LoadWarning { get; private set; }

    internal WindowTitleMode Load()
    {
        _lastSeenContent = null;
        _canOverwrite = true;
        LoadWarning = null;
        if (!File.Exists(path)) return WindowTitleMode.None;

        var complete = true;
        var inSection = false;
        var seenSection = false;
        BigInteger version = -1;
        WindowTitleMode? mode = null;
        try
        {
            _lastSeenContent = File.ReadAllText(path);
            foreach (var raw in _lastSeenContent.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = line.Equals("[WindowTitle]", StringComparison.OrdinalIgnoreCase);
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
                    if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                        version >= 0) complete = false;
                    else version = parsed;
                }
                else if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode.HasValue || !Enum.TryParse(value, true, out WindowTitleMode parsed) || !Enum.IsDefined(parsed))
                        complete = false;
                    else mode = parsed;
                }
                else complete = false;
            }

            if (version > 1)
            {
                _canOverwrite = false;
                LoadWarning = "标题栏配置格式高于当前版本；已停止写入原文件。";
                return WindowTitleMode.None;
            }
            if (version != 1 || !mode.HasValue || !seenSection) complete = false;
        }
        catch (Exception) { complete = false; }

        if (complete) return mode!.Value;
        try
        {
            File.Copy(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            LoadWarning = "标题栏配置不完整；已备份原文件并恢复默认显示。";
        }
        catch (Exception)
        {
            _canOverwrite = false;
            LoadWarning = "标题栏配置不完整且备份失败；本次不会覆盖原文件。";
        }
        return mode ?? WindowTitleMode.None;
    }

    internal void Save(WindowTitleMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_canOverwrite) throw new IOException($"标题栏配置原文件受到保护，已停止覆盖：{path}");
        var content = "; Explorer Dirs portable preference. Close the app before editing." + Environment.NewLine +
            "[WindowTitle]" + Environment.NewLine +
            "FormatVersion=1" + Environment.NewLine +
            $"Mode={mode}" + Environment.NewLine;
        PortableIniWriter.WriteIfUnchanged(path, _lastSeenContent, content,
            "标题栏配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }
}
