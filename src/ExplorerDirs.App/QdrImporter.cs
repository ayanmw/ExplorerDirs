using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ExplorerDirs.App;

internal sealed record QdrImportResult(List<List<TabSettings>> Panes, int[] SelectedTabs,
    int? Layout, IReadOnlyList<string> Warnings, SessionState? ExtendedState = null,
    IReadOnlyList<PinnedTabEntry>? Pins = null, int? ActivePane = null,
    IReadOnlyDictionary<int, LayoutProportions>? Proportions = null,
    IReadOnlyList<bool>? PaneTreeVisibility = null,
    TreeDisplayMode? TreeDisplayMode = null,
    NavigationTreeWidths? NavigationTreeWidths = null,
    QdrTreeSplitState? QdrTreeSplits = null,
    QdrColorMetadata? QdrColorMetadata = null,
    QdrOpaqueMetadata? QdrOpaqueMetadata = null)
{
    public int TabCount => Panes.Sum(pane => pane.Count);
}

internal static class QdrImporter
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly Regex LocationKey = new("^(Dir|Folder)([1-4])(?:_([0-9]{1,2}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RecognizedKey = new(
        "^(?:Q-Dir Version|ViewMode|Focus|Vorschau|tree_mode|Tree[1-4](?:split)?|SP[0-2]|SPT|VorschauPos|useColor|useTreeColor|colorthema|windowplacement|UNICODE|(?:Dir|Folder|CurPos|ViewMode|ColsAndSort)[1-4](?:_[0-9]{1,2})?|ExplorerDirs\\.(?:FormatVersion|BaseHash|State))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExportedKey = new(
        "^(?:Q-Dir Version|ViewMode|Focus|Vorschau|tree_mode|Tree[1-4](?:split)?|useColor|useTreeColor|colorthema|(?:Dir|Folder|CurPos|ViewMode|ColsAndSort)[1-4](?:_[0-9]{1,2})?|ExplorerDirs\\.(?:FormatVersion|BaseHash|State))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NumberedSuffix = new("^(.*?)(?:[1-4])(?:_[0-9]{1,2})?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex SafeFieldName = new("^[A-Za-z][A-Za-z0-9_.-]{0,39}$",
        RegexOptions.CultureInvariant);

    public static QdrImportResult Load(string path) => Parse(ReadText(path), ResolveLocation);

    internal static string ReadText(string path)
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
        return text;
    }

    internal static QdrImportResult Parse(string text, Func<string, bool, string?> resolve)
    {
        if (text.Length > MaximumBytes || text.Contains('\0')) throw new InvalidDataException("不是可读取的 Q-Dir 配置。");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new HashSet<string>();
        var duplicate = false;
        var inRootSection = true;
        var inFilter2 = false;
        var opaqueSections = new List<QdrOpaqueSection>();
        List<string>? currentOpaqueSectionRows = null;
        var filter2RuleCount = 0;
        var filter2Patterns = new List<string>(6);
        var filter2Rows = new List<string>();
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var newline = remaining.IndexOf('\n');
            var rawLine = newline < 0 ? remaining : remaining[..newline];
            remaining = newline < 0 ? ReadOnlySpan<char>.Empty : remaining[(newline + 1)..];
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.IsEmpty || line.StartsWith(";".AsSpan(), StringComparison.Ordinal) ||
                line.StartsWith("#".AsSpan(), StringComparison.Ordinal)) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inRootSection = false;
                inFilter2 = line.Equals("[Filter2]".AsSpan(), StringComparison.OrdinalIgnoreCase);
                currentOpaqueSectionRows = null;
                if (!inFilter2)
                {
                    currentOpaqueSectionRows = [];
                    opaqueSections.Add(new QdrOpaqueSection(line[1..^1].Trim().ToString(), currentOpaqueSectionRows));
                }
                continue;
            }
            if (inFilter2)
            {
                var equals = line.IndexOf('=');
                if (equals > 0 && int.TryParse(line[..equals].Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out _))
                {
                    filter2RuleCount++;
                    filter2Rows.Add(line.ToString());
                    var patternStart = equals + 1;
                    var patternEnd = line[patternStart..].IndexOf('=');
                    if (patternEnd >= 0) patternEnd += patternStart;
                    if (patternEnd > patternStart && filter2Patterns.Count < 6)
                    {
                        var pattern = line[patternStart..patternEnd].Trim();
                        filter2Patterns.Add(pattern.Length <= 64 ? pattern.ToString() : pattern[..61].ToString() + "…");
                    }
                }
                continue;
            }
            if (!inRootSection)
            {
                if (currentOpaqueSectionRows is not null) currentOpaqueSectionRows.Add(line.ToString());
                continue;
            }
            // Q-Dir puts metadata on the same line as the current Folder PIDL.
            // Plain Dir paths may legitimately contain &, so never split those.
            var splitMetadata = line.StartsWith("Folder".AsSpan(), StringComparison.OrdinalIgnoreCase);
            while (true)
            {
                var separator = splitMetadata ? line.IndexOf('&') : -1;
                var field = separator < 0 ? line : line[..separator];
                if (separator >= 0) line = line[(separator + 1)..];
                else splitMetadata = false;
                var equals = field.IndexOf('=');
                if (equals > 0)
                {
                    var key = field[..equals].Trim().ToString();
                    if (values.ContainsKey(key) && !key.Equals("UNICODE", StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        warnings.Add("重复字段使用最后一个值。");
                    }
                    values[key] = field[(equals + 1)..].Trim().ToString();
                }
                if (!splitMetadata) break;
            }
        }
        if (!values.ContainsKey("Q-Dir Version")) throw new InvalidDataException("缺少 Q-Dir 配置标识，未导入。");
        var unsupportedFields = values.Keys.Where(key => !RecognizedKey.IsMatch(key))
            .Select(SummarizeFieldName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (unsupportedFields.Length > 0)
            warnings.Add("检测到未识别字段：" + string.Join("、", unsupportedFields.Take(8)) +
                (unsupportedFields.Length > 8 ? "等" : "") + "。字段会随配置保留，但不会应用；预览不会显示其值。");
        var previewVisible = false;
        if (values.TryGetValue("Vorschau", out var previewText))
        {
            if (previewText == "1") previewVisible = true;
            else if (previewText != "0") warnings.Add("无法识别 Q-Dir 预览字段 Vorschau；导入标签的预览窗格保持关闭。");
        }
        var panes = new List<List<TabSettings>>();
        var selected = new int[4];
        for (var pane = 1; pane <= 4; pane++)
        {
            var entries = new Dictionary<int, (string Suffix, string Raw, bool Encoded)>();
            var ambiguousLocations = new HashSet<int>();
            foreach (var pair in values)
            {
                var match = LocationKey.Match(pair.Key);
                if (!match.Success || int.Parse(match.Groups[2].Value) != pane) continue;
                var index = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : -1;
                if (ambiguousLocations.Contains(index)) continue;
                var encoded = match.Groups[1].Value.Equals("Folder", StringComparison.OrdinalIgnoreCase);
                if (entries.TryGetValue(index, out var previous))
                {
                    if (previous.Encoded != encoded)
                    {
                        // DirN and FolderN are alternate encodings of one location, not separate tabs.
                        // Their conflict has no documented precedence, so never silently redirect the import.
                        entries.Remove(index);
                        ambiguousLocations.Add(index);
                        warnings.Add("同一标签同时包含明文路径与编码 Shell 路径，已跳过该标签。");
                    }
                    continue;
                }
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
                if (string.IsNullOrWhiteSpace(path) ||
                    !ShellLocationIdentity.TryNormalize(path, out var normalizedPath, out _))
                {
                    warnings.Add("部分路径无效或目标不是可用文件夹，已跳过。");
                    continue;
                }
                path = normalizedPath;
                var view = ViewState.Default();
                if (values.TryGetValue("ColsAndSort" + entry.Value.Suffix, out var serialized) &&
                    !QdrViewSettings.TryRead(serialized, out view))
                    warnings.Add("部分标签的排序、分组、列宽及图标大小格式无法识别，使用本程序默认值。");
                if (values.TryGetValue("ViewMode" + entry.Value.Suffix, out var modeText))
                {
                    // Q-Dir FAQ 117 defines this per-tab field using FVM values 1 through FVM_LAST (7).
                    if (!uint.TryParse(modeText, out var mode) || mode is < 1 or > 7)
                    {
                        view = view with { Mode = 4 };
                        warnings.Add("无法识别的查看方式使用详细信息。");
                    }
                    else if (mode == 5)
                    {
                        // The current native Shell view reports FVM_THUMBNAIL as small icons; Large icons retain thumbnails.
                        view = view with { Mode = 1, IconSize = 96 };
                        warnings.Add("Q-Dir 缩略图模式（5）在本机隐藏 Shell 视图中会降级为小图标；已替换为大图标。ExplorerBrowser 实际显示仍待验证。");
                    }
                    else if (mode == 7)
                    {
                        // The current native Shell view reports FVM_THUMBSTRIP as small icons; Tiles are the closest supported mode.
                        view = view with { Mode = 6 };
                        warnings.Add("Q-Dir 缩略胶片模式（7）在本机隐藏 Shell 视图中会降级为小图标；已替换为平铺。ExplorerBrowser 实际显示仍待验证。");
                    }
                    else view = view with { Mode = mode };
                }
                var tab = new TabSettings(path, view, previewPaneVisible: previewVisible);
                if (entry.Key < 0) current = tab;
                else resolved[entry.Key] = tab;
            }
            var currentIndex = -1;
            if (current is not null)
            {
                // Q-Dir omits the active tab's numbered Folder entry in saved files.
                currentIndex = Enumerable.Range(0, 100).FirstOrDefault(index =>
                    !entries.ContainsKey(index) && !ambiguousLocations.Contains(index), -1);
                if (currentIndex >= 0) resolved[currentIndex] = current;
                else warnings.Add("单窗格标签超过 100 个，已跳过超出部分。");
            }
            var ordered = resolved.OrderBy(pair => ReadPosition(values, pane, pair.Key, pair.Key == currentIndex))
                .ThenBy(pair => pair.Key).ToList();
            selected[pane - 1] = Math.Max(0, ordered.FindIndex(pair => pair.Key == currentIndex));
            panes.Add(ordered.Select(pair => pair.Value).ToList());
        }
        var layoutMap = QdrLayoutMap.FromNative(values.GetValueOrDefault("ViewMode"));
        TreeDisplayMode? treeDisplayMode = null;
        if (values.TryGetValue("tree_mode", out var treeModeText))
        {
            treeDisplayMode = treeModeText switch
            {
                "0" => ExplorerDirs.App.TreeDisplayMode.None,
                "1" => ExplorerDirs.App.TreeDisplayMode.Shared,
                "2" => ExplorerDirs.App.TreeDisplayMode.PerPane,
                _ => null
            };
            if (treeDisplayMode is null)
                warnings.Add("Q-Dir tree_mode 值无效；保留当前目录树模式。");
        }
        IReadOnlyList<bool>? paneTreeVisibility = null;
        var treeKeys = Enumerable.Range(1, 4).Select(index => values.TryGetValue($"Tree{index}", out var value)
            ? int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var visible) && visible is 0 or 1
                ? (bool?)(visible == 1) : null
            : null).ToArray();
        if (treeKeys.Any(value => value is null))
        {
            if (treeKeys.Any(value => value is not null) || Enumerable.Range(1, 4).Any(index => values.ContainsKey($"Tree{index}")))
                warnings.Add("Q-Dir 目录树字段不完整或无效；保留当前目录树状态。");
        }
        else if (layoutMap is null)
            warnings.Add("此布局的目录树窗格编号尚未映射；保留当前目录树状态。");
        else
        {
            paneTreeVisibility = layoutMap.AppToNative.Select(index => treeKeys[index]!.Value).ToArray();
            warnings.Add("Q-Dir Tree1～Tree4 的 0/1 目前按逐窗格树开关解释；该映射尚未通过 Q-Dir 可见互操作确认。");
        }
        // FAQ 117 and local Q-Dir workspaces include split defaults when every
        // pane tree is disabled; those values cannot affect this import.
        var qdrTreeSplits = ReadQdrTreeSplits(values, warnings);
        var qdrColorMetadata = ReadQdrColorMetadata(values, filter2Rows, warnings);
        var qdrOpaqueMetadata = ReadQdrOpaqueMetadata(values, opaqueSections, warnings);
        var treeModeExplicitlyDisabled = values.TryGetValue("tree_mode", out var importedTreeMode) && importedTreeMode == "0";
        var everyImportedPaneTreeDisabled = treeKeys.All(value => value == false);
        if (!treeModeExplicitlyDisabled && !everyImportedPaneTreeDisabled &&
            Enumerable.Range(1, 4).Any(index => values.ContainsKey($"Tree{index}split")))
            warnings.Add("Q-Dir Tree1split～Tree4split 的比例单位尚未确认；不会应用目录树宽度。");
        if (values.ContainsKey("SPT"))
            warnings.Add("Q-Dir SPT 字段含义尚未确认；不会应用该字段。");
        if (values.ContainsKey("VorschauPos"))
            warnings.Add("Q-Dir VorschauPos 预览位置字段尚未支持；不会应用该字段。");
        int? layout = layoutMap?.AppId;
        if (layoutMap is not null)
        {
            // The application shows the first N panes; Q-Dir can hide a pane in
            // the middle. Move its tabs AND active index, retaining hidden panes.
            panes = layoutMap.AppToNative.Select(index => panes[index]).ToList();
            selected = layoutMap.AppToNative.Select(index => selected[index]).ToArray();
        }
        if (layout is null) warnings.Add("此布局尚未支持，保留当前布局和原窗格编号；可从顶部布局按钮切换。");
        int? activePane = null;
        if (values.TryGetValue("Focus", out var focusText) && focusText != "-1")
        {
            // Native Focus is zero-based, unlike the Dir1..Dir4 field suffixes.
            // Never activate a hidden pane or an unrelated existing tab when
            // the focused source pane yielded no importable directories.
            var mapped = int.TryParse(focusText, NumberStyles.None, CultureInfo.InvariantCulture, out var nativeFocus) &&
                layoutMap is not null ? layoutMap.AppToNative.ToList().IndexOf(nativeFocus) : -1;
            if (layoutMap is not null && mapped >= 0 &&
                mapped < LayoutPreset.Find(layoutMap.AppId).Slots.Length && panes[mapped].Count > 0)
                activePane = mapped;
            else
                warnings.Add("活动窗格无法映射、当前布局不显示该窗格或其中没有可导入目录；未恢复该活动窗格。");
        }
        var proportions = QdrProportions.Read(values, layout, warnings);
        warnings.Add("窗口位置及程序启动命令不导入。");
        if (qdrColorMetadata is not null)
            warnings.Add("Q-Dir 颜色规则和背景值会保留在配置中，但当前 ExplorerBrowser 视图不会应用它们。");
        if (filter2RuleCount > 0)
        {
            var patternSummary = filter2Patterns.Count == 0 ? "" : "规则模式：" +
                string.Join("、", filter2Patterns) +
                (filter2RuleCount > filter2Patterns.Count ? "等" : "");
            var preservation = qdrColorMetadata?.Filter2Rows.Count == filter2RuleCount
                ? "已保留原始行，当前版本未应用。"
                : "超过安全保留范围，原始行未保留。";
            warnings.Add($"检测到 {filter2RuleCount} 条 Q-Dir Filter2 颜色规则；{preservation}{patternSummary}");
        }
        AddUnsupportedColorSwitchWarning(values, "useColor", warnings);
        AddUnsupportedColorSwitchWarning(values, "useTreeColor", warnings);
        var result = QdrExporter.ApplyExtension(values, duplicate,
            new QdrImportResult(panes, selected, layout, warnings.ToArray(), ActivePane: activePane,
                Proportions: proportions is not null && layout is { } knownLayout
                    ? new Dictionary<int, LayoutProportions> { [knownLayout] = proportions } : null,
                PaneTreeVisibility: paneTreeVisibility, TreeDisplayMode: treeDisplayMode,
                QdrTreeSplits: qdrTreeSplits, QdrColorMetadata: qdrColorMetadata,
                QdrOpaqueMetadata: qdrOpaqueMetadata));
        if (result.TabCount == 0) throw new InvalidDataException("未找到可导入的文件夹；当前会话未改变。");
        return result;
    }

    private static QdrTreeSplitState? ReadQdrTreeSplits(
        IReadOnlyDictionary<string, string> values, ICollection<string> warnings)
    {
        var malformed = false;
        var splits = Enumerable.Range(1, 4).Select(pane =>
        {
            if (!values.TryGetValue($"Tree{pane}split", out var value)) return null;
            if (value.Length is 0 or > 32 ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                malformed = true;
                return null;
            }
            return value;
        }).ToArray();
        if (malformed) warnings.Add("部分 Q-Dir TreeNsplit 值不是有效的非负整数；这些原值不会保留或应用。");
        return splits.Any(value => value is not null)
            ? new QdrTreeSplitState(splits[0], splits[1], splits[2], splits[3]) : null;
    }

    private static QdrColorMetadata? ReadQdrColorMetadata(IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> filter2Rows, ICollection<string> warnings)
    {
        var metadata = new QdrColorMetadata(values.GetValueOrDefault("useColor"),
            values.GetValueOrDefault("useTreeColor"), values.GetValueOrDefault("colorthema"), filter2Rows);
        if (metadata.UseColor is null && metadata.UseTreeColor is null && metadata.ColorTheme is null &&
            metadata.Filter2Rows.Count == 0) return null;
        if (metadata.IsValid) return metadata;
        warnings.Add("Q-Dir 颜色配置超出安全保留范围；未保留其颜色字段。");
        return null;
    }

    private static QdrOpaqueMetadata? ReadQdrOpaqueMetadata(IReadOnlyDictionary<string, string> values,
        IReadOnlyList<QdrOpaqueSection> sections, ICollection<string> warnings)
    {
        var rootFields = values.Where(pair => !ExportedKey.IsMatch(pair.Key))
            .Select(pair => new QdrOpaqueField(pair.Key, pair.Value)).ToArray();
        if (rootFields.Length == 0 && sections.Count == 0) return null;
        var metadata = new QdrOpaqueMetadata(rootFields, sections);
        if (metadata.IsValid)
        {
            if (sections.Count > 0)
                warnings.Add($"已保留 {sections.Count} 个非 Filter2 区段的原始内容；当前不会应用这些区段。");
            return metadata;
        }
        warnings.Add("部分未识别 Q-Dir 字段或区段超出安全保留范围；这些原始内容不会导出。");
        return null;
    }

    private static void AddUnsupportedColorSwitchWarning(
        IReadOnlyDictionary<string, string> values, string key, ICollection<string> warnings)
    {
        if (!values.TryGetValue(key, out var value) || value == "0") return;
        warnings.Add(value == "1"
            ? $"Q-Dir {key} 已启用，但颜色规则格式尚未确认；此开关未应用。"
            : $"Q-Dir {key} 值无法识别；未应用此开关。");
    }

    private static int ReadPosition(Dictionary<string, string> values, int pane, int index, bool current) =>
        int.TryParse(values.GetValueOrDefault("CurPos" + pane + (current ? "" : "_" + index)), out var position) &&
        position is >= 0 and < 100 ? position : index;

    private static string SummarizeFieldName(string key)
    {
        var match = NumberedSuffix.Match(key);
        var name = match.Success ? match.Groups[1].Value : key;
        return SafeFieldName.IsMatch(name) ? name : "（其他字段）";
    }

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
                if (!ShellLocationIdentity.TryNormalize(value, out value, out _) ||
                    !ShellLocationIdentity.TryGetFolderPidl(value, out pidl)) return null;
            }
            return ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var identity, out _)
                ? identity : null;
        }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }
    [DllImport("kernel32.dll")] private static extern uint GetACP();
}
