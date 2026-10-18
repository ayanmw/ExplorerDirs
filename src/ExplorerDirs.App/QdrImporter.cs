using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ExplorerDirs.App;

internal sealed record QdrImportResult(List<List<TabSettings>> Panes, int[] SelectedTabs,
    int? Layout, IReadOnlyList<string> Warnings)
{
    public int TabCount => Panes.Sum(pane => pane.Count);
}

internal static class QdrImporter
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly Regex LocationKey = new("^(Dir|Folder)([1-4])(?:_([0-9]{1,2}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static QdrImportResult Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Q-Dir 配置超过 8 MB，未导入。");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = stream.Read(bytes)) > 0)
        {
            if (buffer.Length + read > MaximumBytes) throw new InvalidDataException("Q-Dir 配置过大，未导入。");
            buffer.Write(bytes, 0, read);
        }
        buffer.Position = 0;
        string text;
        try
        {
            using var reader = new StreamReader(buffer, new UTF8Encoding(false, true), true, 1024, true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding((int)GetACP()).GetString(buffer.ToArray());
        }
        return Parse(text, ResolveLocation);
    }

    internal static QdrImportResult Parse(string text, Func<string, bool, string?> resolve)
    {
        if (text.Length > MaximumBytes || text.Contains('\0')) throw new InvalidDataException("不是可读取的 Q-Dir 配置。");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new HashSet<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            // Q-Dir puts metadata on the same line as the current Folder PIDL.
            // Plain Dir paths may legitimately contain &, so never split those.
            var fields = line.StartsWith("Folder", StringComparison.OrdinalIgnoreCase) ? line.Split('&') : [line];
            foreach (var field in fields)
            {
                var equals = field.IndexOf('=');
                if (equals < 1) continue;
                var key = field[..equals].Trim();
                if (values.ContainsKey(key) && key != "UNICODE") warnings.Add("重复字段使用最后一个值。");
                values[key] = field[(equals + 1)..].Trim();
            }
        }
        if (!values.ContainsKey("Q-Dir Version")) throw new InvalidDataException("缺少 Q-Dir 配置标识，未导入。");
        var panes = new List<List<TabSettings>>();
        var selected = new int[4];
        for (var pane = 1; pane <= 4; pane++)
        {
            var entries = new Dictionary<int, (string Suffix, string Raw, bool Encoded)>();
            foreach (var pair in values)
            {
                var match = LocationKey.Match(pair.Key);
                if (!match.Success || int.Parse(match.Groups[2].Value) != pane) continue;
                var index = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : -1;
                var encoded = match.Groups[1].Value.Equals("Folder", StringComparison.OrdinalIgnoreCase);
                if (entries.TryGetValue(index, out var previous) && !previous.Encoded) continue;
                entries[index] = (pane + (index < 0 ? "" : "_" + index), pair.Value, encoded);
            }
            var resolved = new SortedDictionary<int, TabSettings>();
            TabSettings? current = null;
            foreach (var entry in entries)
            {
                string? path;
                try { path = resolve(entry.Value.Raw, entry.Value.Encoded); }
                catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or COMException)
                { path = null; }
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                {
                    warnings.Add("部分路径无效或为暂不支持的虚拟目录，已跳过。");
                    continue;
                }
                var mode = 4u;
                if (values.TryGetValue("ViewMode" + entry.Value.Suffix, out var modeText) &&
                    (!uint.TryParse(modeText, out mode) || mode is < 1 or > 8))
                { mode = 4; warnings.Add("无法识别的查看方式使用详细信息。"); }
                var tab = new TabSettings(path, ViewState.Default(mode));
                if (entry.Key < 0) current = tab;
                else resolved[entry.Key] = tab;
            }
            var currentIndex = -1;
            if (current is not null)
            {
                // Q-Dir omits the active tab's numbered Folder entry in saved files.
                currentIndex = Enumerable.Range(0, 100).FirstOrDefault(index => !entries.ContainsKey(index), -1);
                if (currentIndex >= 0) resolved[currentIndex] = current;
                else warnings.Add("单窗格标签超过 100 个，已跳过超出部分。");
            }
            var ordered = resolved.OrderBy(pair => ReadPosition(values, pane, pair.Key, pair.Key == currentIndex))
                .ThenBy(pair => pair.Key).ToList();
            selected[pane - 1] = Math.Max(0, ordered.FindIndex(pair => pair.Key == currentIndex));
            panes.Add(ordered.Select(pair => pair.Value).ToList());
        }
        int? layout = values.GetValueOrDefault("ViewMode") == "0" ? 4 : null;
        if (layout is null) warnings.Add("此布局编号尚未确认，保留当前布局；可从顶部布局按钮切换。");
        if (values.Keys.Any(key => key.StartsWith("ColsAndSort", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Q-Dir 私有排序、分组、列宽及图标大小数据未导入，使用本程序默认值。");
        warnings.Add("不导入树、预览、颜色、窗口位置、分隔比例及程序启动命令。");
        var result = new QdrImportResult(panes, selected, layout, warnings.ToArray());
        if (result.TabCount == 0) throw new InvalidDataException("未找到可导入的文件系统目录；当前会话未改变。");
        return result;
    }

    private static int ReadPosition(Dictionary<string, string> values, int pane, int index, bool current) =>
        int.TryParse(values.GetValueOrDefault("CurPos" + pane + (current ? "" : "_" + index)), out var position) &&
        position is >= 0 and < 100 ? position : index;

    internal static byte[] DecodePidl(string value)
    {
        if (value.Length is < 6 or > 196608 || value.Length % 3 != 0)
            throw new FormatException("无效的 Shell 路径长度。");
        var bytes = new byte[value.Length / 3];
        for (var i = 0; i < bytes.Length; i++)
            if (value[i * 3] != '%' || !byte.TryParse(value.AsSpan(i * 3 + 1, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out bytes[i])) throw new FormatException("无效的 Shell 路径编码。");
        var offset = 0;
        while (offset + 2 <= bytes.Length)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
            if (size == 0 && offset == bytes.Length - 2 && offset > 0) return bytes;
            if (size < 2 || size > bytes.Length - offset - 2) break;
            offset += size;
        }
        throw new FormatException("Shell 路径结构不完整。");
    }

    internal static string? ResolveLocation(string value, bool encoded)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (encoded)
            {
                var bytes = DecodePidl(value);
                pidl = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, pidl, bytes.Length);
            }
            else
            {
                value = Environment.ExpandEnvironmentVariables(value);
                if (Path.IsPathFullyQualified(value)) return Path.GetFullPath(value);
                if (!value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
                if (SHParseDisplayName(value, IntPtr.Zero, out pidl, 0, out _) < 0) return null;
            }
            var path = new StringBuilder(32768);
            return SHGetPathFromIDListEx(pidl, path, (uint)path.Capacity, 0) ? path.ToString() : null;
        }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(IntPtr pidl, StringBuilder path, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr pidl, uint mask, out uint attributes);
    [DllImport("kernel32.dll")] private static extern uint GetACP();
}
