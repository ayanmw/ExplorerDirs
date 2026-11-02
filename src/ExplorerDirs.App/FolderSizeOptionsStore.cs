using System.IO;

namespace ExplorerDirs.App;

internal sealed record FolderSizeOptions(bool Enabled = true, FolderSizeSource Source = FolderSizeSource.Auto);

internal sealed class FolderSizeOptionsStore
{
    private readonly string _path;
    private string? _loaded;
    private bool _writable = true;
    internal string? Warning { get; private set; }
    internal FolderSizeOptionsStore(string path) => _path = path;

    internal FolderSizeOptions Load()
    {
        Warning = null; _writable = true; _loaded = null;
        if (!File.Exists(_path)) return new();
        try
        {
            _loaded = File.ReadAllText(_path);
            // Protect a future version even if later fields are malformed or duplicated.
            foreach (var raw in _loaded.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                var split = line.IndexOf('=');
                if (split > 0 && line[..split].Trim().Equals("FormatVersion", StringComparison.OrdinalIgnoreCase) &&
                    System.Numerics.BigInteger.TryParse(line[(split + 1)..].Trim(), out var futureVersion) && futureVersion > 1)
                {
                    _writable = false; Warning = "文件夹大小设置格式高于当前版本，保留原文件。"; return new();
                }
            }
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in _loaded.Split('\n'))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith(';') || line == "[FolderSize]") continue;
                var split = line.IndexOf('=');
                if (split < 1 || !values.TryAdd(line[..split], line[(split + 1)..])) throw new FormatException();
            }
            if (values.Count != 3 || !values.TryGetValue("FormatVersion", out var format) || format != "1" || !values.TryGetValue("Enabled", out var enabled) ||
                !bool.TryParse(enabled, out var show) || !values.TryGetValue("Source", out var source) ||
                !Enum.TryParse<FolderSizeSource>(source, false, out var parsed) || !Enum.IsDefined(parsed))
                throw new FormatException();
            return new(show, parsed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            Warning = "文件夹大小设置无法读取，使用默认值。";
            try
            {
                if (_loaded is null) { _writable = false; return new(); }
                File.WriteAllText(_path + $".invalid-{Guid.NewGuid():N}.bak", _loaded);
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            { _writable = false; }
            return new();
        }
    }

    internal void Save(FolderSizeOptions options)
    {
        if (!_writable) throw new IOException("文件夹大小设置受到保护，不能覆盖。");
        if (!Enum.IsDefined(options.Source)) throw new ArgumentOutOfRangeException(nameof(options));
        var content = $"[FolderSize]\nFormatVersion=1\nEnabled={options.Enabled}\nSource={options.Source}\n";
        PortableIniWriter.WriteIfUnchanged(_path, _loaded, content, "文件夹大小设置已被其他窗口修改，请重新加载。");
        _loaded = content;
    }
}
