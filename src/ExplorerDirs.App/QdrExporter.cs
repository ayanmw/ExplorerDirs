using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExplorerDirs.App;

internal static class QdrExporter
{
    internal const string Prefix = "ExplorerDirs.";
    private sealed record Extension(string Session, List<PinnedTabEntry> Pins,
        Dictionary<int, LayoutProportions>? Proportions = null,
        NavigationTreeWidths? NavigationTreeWidths = null);
    private static readonly Regex BaseKey = new(
        @"^(Q-Dir Version|ViewMode|Focus|Vorschau|tree_mode|Tree[1-4]|SP[0-2]|(?:Dir|Folder|CurPos|ViewMode|ColsAndSort)[1-4](?:_[0-9]{1,2})?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Render(SessionState state, IReadOnlyList<PinnedTabEntry> pins,
        IReadOnlyDictionary<int, LayoutProportions>? proportions = null,
        NavigationTreeWidths? treeWidths = null,
        IReadOnlyDictionary<string, string>? encodedLocations = null)
    {
        Validate(state, pins);
        if (proportions is not null) ValidateProportions(state.Layout, proportions);
        if (treeWidths is not null && !treeWidths.IsValid)
            throw new InvalidDataException("目录树宽度必须在 96 至 600 DIP 之间。");
        var layoutMap = QdrLayoutMap.FromApp(state.Layout);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // The documented plain-text path dialect with the observed 12.79
            // view envelope; the comment below identifies this application's output.
            ["Q-Dir Version"] = "7.84",
            ["ViewMode"] = layoutMap.NativeId.ToString(),
            ["Focus"] = layoutMap.AppToNative[state.ActivePane].ToString(),
            ["tree_mode"] = (state.TreeDisplayMode ?? ExplorerDirs.App.TreeDisplayMode.PerPane) switch
            {
                ExplorerDirs.App.TreeDisplayMode.None => "0",
                ExplorerDirs.App.TreeDisplayMode.Shared => "1",
                _ => "2"
            },
            // Q-Dir has one global preview bit; use the active app tab as the
            // closest base-field representation while the extension keeps all per-tab values.
            ["Vorschau"] = state.Panes[state.ActivePane][state.ActiveTabIndices![state.ActivePane]]
                .PreviewPaneVisible ? "1" : "0"
        };
        for (var pane = 0; pane < 4; pane++)
            values[$"Tree{layoutMap.AppToNative[pane] + 1}"] = state.PaneTreeVisibility?.ElementAtOrDefault(pane) == true ? "1" : "0";
        if (proportions is not null) QdrProportions.Write(values, state.Layout, proportions[state.Layout]);
        for (var pane = 0; pane < 4; pane++)
        {
            var selected = state.ActiveTabIndices![pane];
            for (var tab = 0; tab < state.Panes[pane].Count; tab++)
            {
                var suffix = (layoutMap.AppToNative[pane] + 1).ToString() + (tab == selected ? "" : "_" + tab);
                var location = state.Panes[pane][tab].Path;
                if (encodedLocations is not null && encodedLocations.TryGetValue(location, out var encoded))
                {
                    // Resolve outside this pure renderer; never block UI rendering on Shell/network binding.
                    QdrImporter.DecodePidl(encoded);
                    values["Folder" + suffix] = encoded;
                }
                else values["Dir" + suffix] = ToQdrTextPath(location);
                values["CurPos" + suffix] = tab.ToString();
                var mode = state.Panes[pane][tab].View.Mode;
                // Q-Dir's published view list omits Content; map it to Details in the interoperable base fields.
                values["ViewMode" + suffix] = (mode == 8 ? 4 : mode).ToString();
                var qdrView = mode == 8 ? state.Panes[pane][tab].View with { Mode = 4 } : state.Panes[pane][tab].View;
                var nativeView = QdrViewWriter.Write(qdrView);
                if (nativeView is not null) values["ColsAndSort" + suffix] = nativeView;
            }
        }
        var output = new StringBuilder("; Exported by Explorer Dirs; Q-Dir plain-text fields plus versioned extension.\r\n");
        foreach (var (key, value) in values) output.Append(key).Append('=').Append(value).Append("\r\n");
        var payload = new Extension(SessionStore.Render(state), pins.ToList(), proportions?.ToDictionary(), treeWidths);
        var extensionVersion = treeWidths is not null ? 4 : proportions is null ? 1 :
            proportions.Values.Any(value => value.BottomHorizontal is not null) ? 3 : 2;
        output.Append(Prefix).Append("FormatVersion=").Append(extensionVersion).Append("\r\n")
            .Append(Prefix).Append("BaseHash=").Append(HashBase(values)).Append("\r\n")
            .Append(Prefix).Append("State=").Append(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)))
            .Append("\r\n");
        var text = output.ToString();
        if (Encoding.Unicode.GetByteCount(text) + 2 > QdrImporter.MaximumBytes)
            throw new InvalidDataException("导出状态超过 8 MB，请减少标签后重试。");
        return text;
    }

    internal static void Save(string path, string? expected, string text)
    {
        if (!Path.GetExtension(path).Equals(".qdr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("状态文件必须使用 .qdr 扩展名。");
        PortableIniWriter.WriteIfUnchanged(path, expected, text,
            "目标文件在确认后已被修改，未覆盖；请重新选择导出位置。", Encoding.Unicode, QdrImporter.ReadText);
    }

    internal static QdrImportResult ApplyExtension(Dictionary<string, string> values,
        bool duplicate, QdrImportResult basic)
    {
        if (!values.Keys.Any(key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))) return basic;
        try
        {
            var version = values.GetValueOrDefault(Prefix + "FormatVersion");
            if (duplicate || version is not ("1" or "2" or "3" or "4") ||
                values.GetValueOrDefault(Prefix + "BaseHash") != HashBase(values))
                throw new InvalidDataException();
            var bytes = Convert.FromBase64String(values[Prefix + "State"]);
            var extension = JsonSerializer.Deserialize<Extension>(bytes) ?? throw new InvalidDataException();
            var state = SessionStore.ParseComplete(extension.Session) ?? throw new InvalidDataException();
            Validate(state, extension.Pins);
            if (version is "2" or "3")
            {
                ValidateProportions(state.Layout, extension.Proportions ?? throw new InvalidDataException());
                if (version == "2" && extension.Proportions.Values.Any(value => value.BottomHorizontal is not null))
                    throw new InvalidDataException();
            }
            else if (version == "4")
            {
                if (extension.Proportions is not null) ValidateProportions(state.Layout, extension.Proportions);
            }
            else if (extension.Proportions is not null) throw new InvalidDataException();
            if (version == "4")
            {
                if (extension.NavigationTreeWidths is not { IsValid: true }) throw new InvalidDataException();
            }
            else if (extension.NavigationTreeWidths is not null) throw new InvalidDataException();
            return new(state.Panes, state.ActiveTabIndices!.ToArray(), state.Layout,
                ["已读取 Explorer Dirs 扩展：布局、活动窗格、标签视图、目录树模式/宽度/逐窗格状态、每标签快速筛选、预览/详情窗格、固定标签及新标签默认查看方式。",
                 "不导入主题、窗口位置或启动命令。"], state, extension.Pins, state.ActivePane,
                extension.Proportions ?? basic.Proportions, state.PaneTreeVisibility, state.TreeDisplayMode,
                extension.NavigationTreeWidths);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or JsonException or
            ArgumentException or KeyNotFoundException or NullReferenceException)
        {
            return basic with { Warnings = basic.Warnings.Append(
                "Explorer Dirs 扩展损坏、版本不支持或基础字段已被修改；仅导入可识别的 Q-Dir 字段，扩展状态未恢复。").ToArray() };
        }
    }

    private static void ValidateProportions(int layout, IReadOnlyDictionary<int, LayoutProportions> proportions)
    {
        if (!proportions.ContainsKey(layout) || proportions.Count > LayoutPreset.All.Count ||
            proportions.Any(pair => !LayoutPreset.All.Any(preset => preset.Id == pair.Key) ||
                pair.Value is null || !pair.Value.IsValidFor(LayoutPreset.Find(pair.Key))))
            throw new InvalidDataException("工作区布局比例不完整或无效。");
    }

    private static string HashBase(Dictionary<string, string> values)
    {
        var canonical = string.Join("\n", values.Where(pair => BaseKey.IsMatch(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key.ToUpperInvariant() + "=" + pair.Value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ToQdrTextPath(string identity) =>
        identity.StartsWith("::", StringComparison.Ordinal) ? "shell:" + identity : identity;

    private static void Validate(SessionState state, IReadOnlyList<PinnedTabEntry> pins)
    {
        if (state.Panes.Count != 4 || state.Panes.Any(pane => pane.Count is < 1 or > 100) ||
            !LayoutPreset.All.Any(layout => layout.Id == state.Layout) ||
            state.ActivePane < 0 || state.ActivePane >= LayoutPreset.Find(state.Layout).Slots.Length ||
            state.ActiveTabIndices?.Count != 4 || state.DefaultViewMode is < 1 or > 8)
            throw new InvalidDataException("工作区状态不完整，未导出或恢复扩展。");
        for (var pane = 0; pane < 4; pane++)
        {
            if (state.ActiveTabIndices[pane] < 0 || state.ActiveTabIndices[pane] >= state.Panes[pane].Count)
                throw new InvalidDataException("标签选择状态无效。");
            foreach (var tab in state.Panes[pane])
                if (!ShellLocationIdentity.TryNormalize(tab.Path, out _, out _))
                    throw new InvalidDataException("标签包含不能写入状态文件的路径。");
        }
        var rendered = SessionStore.Render(state);
        var parsed = SessionStore.ParseComplete(rendered);
        if (parsed is null || SessionStore.Render(parsed) != rendered)
            throw new InvalidDataException("标签视图状态不能完整保存。");
        if (pins.Count > 400 || pins.Select(pin => (pin.Pane, pin.Index)).Distinct().Count() != pins.Count ||
            pins.Any(pin => pin.Pane is < 0 or > 3 || pin.Index < 0 ||
                pin.Index >= state.Panes[pin.Pane].Count ||
                !string.Equals(state.Panes[pin.Pane][pin.Index].Path, pin.Path, StringComparison.Ordinal)))
            throw new InvalidDataException("固定标签状态与工作区不一致。");
        // The application always orders pinned tabs first; reject an extension
        // that would silently reorder a claimed exact snapshot on import.
        for (var pane = 0; pane < 4; pane++)
        {
            var indices = pins.Where(pin => pin.Pane == pane).Select(pin => pin.Index).Order().ToArray();
            if (!indices.SequenceEqual(Enumerable.Range(0, indices.Length)))
                throw new InvalidDataException("固定标签顺序无效。");
        }
    }
}
