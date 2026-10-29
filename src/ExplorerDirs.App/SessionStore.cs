using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace ExplorerDirs.App;

internal sealed record PropertyKeyState(Guid FormatId, uint PropertyId)
{
    public override string ToString() => $"{FormatId:B}:{PropertyId}";

    public static PropertyKeyState? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.LastIndexOf(':');
        return separator > 0 && Guid.TryParse(value[..separator], out var formatId) &&
            uint.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var propertyId)
            ? new PropertyKeyState(formatId, propertyId) : null;
    }
}

internal sealed record SortColumnState(PropertyKeyState Key, int Direction);

internal sealed record ViewState(
    uint Mode,
    int IconSize,
    IReadOnlyList<SortColumnState> SortColumns,
    PropertyKeyState? GroupKey,
    bool GroupAscending,
    IReadOnlyList<DisplayColumnState>? DisplayColumns = null)
{
    public static ViewState Default(uint mode = 4, int iconSize = -1) =>
        new(mode, ViewStyle.NativeIconSize(mode, iconSize), [], null, true);
    public ViewState Copy() => this with
    {
        SortColumns = SortColumns.ToArray(), DisplayColumns = DisplayColumns?.ToArray()
    };
}

internal sealed record TabSettings
{
    public string Path { get; init; }
    public ViewState View { get; init; }
    public QuickFilterSettings QuickFilter { get; init; }
    public bool PreviewPaneVisible { get; init; }
    public bool DetailsPaneVisible { get; init; }

    public TabSettings(string path, ViewState view, QuickFilterSettings? quickFilter = null,
        bool previewPaneVisible = false, bool detailsPaneVisible = false)
    {
        if (previewPaneVisible && detailsPaneVisible)
            throw new ArgumentException("预览窗格和详情窗格不能同时开启。", nameof(detailsPaneVisible));
        Path = path;
        View = view;
        QuickFilter = quickFilter ?? QuickFilterSettings.Empty;
        PreviewPaneVisible = previewPaneVisible;
        DetailsPaneVisible = detailsPaneVisible;
    }
}
internal enum TreeDisplayMode { None, PerPane, Shared }

internal sealed record QdrTreeSplitState(string? Pane1, string? Pane2, string? Pane3, string? Pane4)
{
    internal bool IsValid => Values.All(value => value is null ||
        value.Length is > 0 and <= 32 && int.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out _));
    internal IEnumerable<string?> Values => [Pane1, Pane2, Pane3, Pane4];
}

internal sealed record QdrColorMetadata(string? UseColor, string? UseTreeColor, string? ColorTheme,
    IReadOnlyList<string> Filter2Rows)
{
    private const int MaximumRows = 4096;
    private const int MaximumRowLength = 8192;
    private const int MaximumPayloadLength = 1024 * 1024;

    internal bool IsValid
    {
        get
        {
            if (!ValidValue(UseColor) || !ValidValue(UseTreeColor) || !ValidValue(ColorTheme) ||
                Filter2Rows is null || Filter2Rows.Count > MaximumRows) return false;
            var payloadLength = 0;
            foreach (var row in Filter2Rows)
            {
                var equals = row?.IndexOf('=') ?? -1;
                if (row is null || row.Length is < 2 or > MaximumRowLength ||
                    row.Contains('\r') || row.Contains('\n') || equals < 1 ||
                    !int.TryParse(row.AsSpan(0, equals), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return false;
                payloadLength += Encoding.UTF8.GetByteCount(row);
                if (payloadLength > MaximumPayloadLength) return false;
            }
            var serializedLength = JsonSerializer.SerializeToUtf8Bytes(this).Length;
            return serializedLength <= MaximumPayloadLength;
        }
    }

    private static bool ValidValue(string? value) => value is null ||
        value.Length <= 4096 && !value.Contains('\r') && !value.Contains('\n') && !value.Contains('\0');
}

internal sealed record QdrOpaqueField(string Key, string Value);
internal sealed record QdrOpaqueSection(string Name, IReadOnlyList<string> Rows);
internal sealed record QdrOpaqueMetadata(IReadOnlyList<QdrOpaqueField> RootFields,
    IReadOnlyList<QdrOpaqueSection> Sections)
{
    private const int MaximumRootFields = 4096;
    private const int MaximumSections = 256;
    private const int MaximumRowsPerSection = 4096;
    private const int MaximumLineLength = 8192;
    private const int MaximumPayloadLength = 1024 * 1024;

    internal bool IsValid
    {
        get
        {
            if (RootFields is null || Sections is null || RootFields.Count > MaximumRootFields ||
                Sections.Count > MaximumSections) return false;
            var payloadLength = 0;
            foreach (var opaqueField in RootFields)
            {
                if (opaqueField is null || string.IsNullOrWhiteSpace(opaqueField.Key) || opaqueField.Key.Length > 256 ||
                    opaqueField.Key.Contains('=') || HasLineBreakOrNull(opaqueField.Key) || opaqueField.Value is null ||
                    opaqueField.Value.Length > MaximumLineLength || HasLineBreakOrNull(opaqueField.Value)) return false;
                payloadLength += Encoding.UTF8.GetByteCount(opaqueField.Key) + Encoding.UTF8.GetByteCount(opaqueField.Value);
                if (payloadLength > MaximumPayloadLength) return false;
            }
            foreach (var section in Sections)
            {
                if (section is null || string.IsNullOrWhiteSpace(section.Name) || section.Name.Length > 256 ||
                    section.Name.Contains('[') || section.Name.Contains(']') || HasLineBreakOrNull(section.Name) ||
                    section.Rows is null || section.Rows.Count > MaximumRowsPerSection) return false;
                payloadLength += Encoding.UTF8.GetByteCount(section.Name);
                foreach (var row in section.Rows)
                {
                    if (row is null || row.Length > MaximumLineLength || HasLineBreakOrNull(row)) return false;
                    payloadLength += Encoding.UTF8.GetByteCount(row);
                    if (payloadLength > MaximumPayloadLength) return false;
                }
            }
            return JsonSerializer.SerializeToUtf8Bytes(this).Length <= MaximumPayloadLength;
        }
    }

    private static bool HasLineBreakOrNull(string value) =>
        value.Contains('\r') || value.Contains('\n') || value.Contains('\0');
}

internal sealed record SessionState(int Layout, uint DefaultViewMode, List<List<TabSettings>> Panes,
    int ActivePane = 0, IReadOnlyList<int>? ActiveTabIndices = null, int DefaultIconSize = -1,
    IReadOnlyList<bool>? PaneTreeVisibility = null, TreeDisplayMode? TreeDisplayMode = null,
    QdrTreeSplitState? QdrTreeSplits = null, QdrColorMetadata? QdrColorMetadata = null,
    QdrOpaqueMetadata? QdrOpaqueMetadata = null);

internal sealed class SessionStore
{
    internal const int MaximumTabsPerPane = 100;
    private const int CurrentFormatVersion = 14;
    private const int MaximumColumnFiltersPerTab = 16;
    private const int MaximumColumnFilterValueLength = 512;
    private const int MaximumColumnFilterPayloadLength = 128 * 1024;
    private readonly string _path;
    private readonly string _legacyPath;
    private bool _canOverwrite = true;
    private string? _lastSeenContent;
    private bool _olderVersionBackedUp;

    internal static string? ColumnFilterValidationError(QuickFilterSettings settings)
    {
        if (settings.ColumnFilters.Count > MaximumColumnFiltersPerTab)
            return $"每个标签最多保存 {MaximumColumnFiltersPerTab} 个列值筛选条件。";

        var encodedValuesLength = 0;
        foreach (var filter in settings.ColumnFilters)
        {
            var values = filter.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (filter.Key.FormatId == Guid.Empty || values.Length is 0 or > QuickFilterSettings.MaximumColumnFilterValues ||
                values.Any(value => value.Length > MaximumColumnFilterValueLength || value.Contains('\r') || value.Contains('\n')))
                return "列值筛选条件无效或超过可保存范围。";

            encodedValuesLength += Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(values)).Length;
            if (encodedValuesLength > MaximumColumnFilterPayloadLength)
                return "单个标签的列值筛选配置过大，未覆盖会话文件。";
        }
        return null;
    }

    public string Path => _path;
    public string? LoadWarning { get; private set; }

    public SessionStore(string? path = null, string? legacyPath = null)
    {
        _path = path ?? ResolvePortablePath();
        _legacyPath = legacyPath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
            "ExplorerDirs.Dev",
#else
            "ExplorerDirs",
#endif
            "session.json");
    }

    private static string ResolvePortablePath()
    {
        var executableDirectory = new DirectoryInfo(AppContext.BaseDirectory);
#if DEBUG
        for (var directory = executableDirectory; directory is not null; directory = directory.Parent)
            if (directory.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(directory.FullName, ".state", "ExplorerDirs.ini");
        return System.IO.Path.Combine(executableDirectory.FullName, ".state", "ExplorerDirs.ini");
#else
        if (executableDirectory.Parent?.Name.Equals("releases", StringComparison.OrdinalIgnoreCase) == true &&
            executableDirectory.Parent.Parent is { } projectRoot)
            return System.IO.Path.Combine(projectRoot.FullName, "profile", "ExplorerDirs.ini");
        return System.IO.Path.Combine(executableDirectory.FullName, "profile", "ExplorerDirs.ini");
#endif
    }

    public SessionState? Load()
    {
        LoadWarning = null;
        _canOverwrite = true;
        _lastSeenContent = null;
        _olderVersionBackedUp = false;
        if (File.Exists(_path))
        {
            ProfileParseResult? parsed = null;
            try
            {
                _lastSeenContent = File.ReadAllText(_path);
                parsed = ParseProfile(_lastSeenContent);
                if (parsed.Complete && parsed.State is not null) return parsed.State;
            }
            catch (Exception) { /* Preserve the unreadable file below. */ }
            if (parsed?.NewerFormat == true)
            {
                _canOverwrite = false;
                LoadWarning = "配置格式高于当前版本；已停止写入原配置。";
                return null;
            }
            var backedUp = BackupInvalidProfile();
            var recoveredCount = parsed?.State?.Panes.Sum(pane => pane.Count) ?? 0;
            LoadWarning = backedUp
                ? $"配置不完整；已恢复 {recoveredCount} 个标签并备份原文件。"
                : "配置不完整且备份失败；本次不会覆盖原配置。";
            return parsed?.State;
        }

        // One-way migration leaves the former profile untouched for older releases.
        try
        {
            if (!File.Exists(_legacyPath)) return null;
            var old = JsonSerializer.Deserialize<LegacySessionState>(File.ReadAllText(_legacyPath));
            if (old?.Panes is not { Count: > 0 }) return null;
            var state = new SessionState(old.Layout, 4, old.Panes.Select(pane =>
                pane.Select(path => new TabSettings(path, ViewState.Default())).ToList()).ToList());
            Save(state);
            return state;
        }
        catch (Exception) { return null; }
    }

    private bool BackupInvalidProfile()
    {
        try
        {
            var backup = _path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak";
            File.Copy(_path, backup);
            return true;
        }
        catch (Exception)
        {
            _canOverwrite = false;
            return false;
        }
    }

    public void Save(SessionState state)
    {
        if (!_canOverwrite)
            throw new IOException($"无法备份损坏的配置文件，已停止覆盖：{_path}");
        // New and duplicated tabs must stay within the reader's per-pane bound.
        // Never replace a recoverable session with a file we cannot fully reload.
        if (state.Panes.Any(pane => pane.Count > MaximumTabsPerPane))
            throw new InvalidDataException($"单窗格超过 {MaximumTabsPerPane} 个标签，未覆盖会话配置；请减少标签后再保存。");
        var content = Render(state);
        if (!_olderVersionBackedUp && _lastSeenContent is not null &&
            ParseProfile(_lastSeenContent).Version is >= 1 and < CurrentFormatVersion)
        {
            // Preserve exactly the snapshot we loaded before adding v11 state;
            // concurrent edits are still rejected by WriteIfUnchanged below.
            File.WriteAllText(_path + $".before-v{CurrentFormatVersion}-{Guid.NewGuid():N}.bak", _lastSeenContent);
            _olderVersionBackedUp = true;
        }
        PortableIniWriter.WriteIfUnchanged(_path, _lastSeenContent, content,
            "会话配置已被其他实例修改；请重启程序后再保存。");
        _lastSeenContent = content;
    }

    internal static string Render(SessionState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("; Explorer Dirs portable profile. Close the app before editing.");
        builder.AppendLine("[General]");
        builder.AppendLine($"FormatVersion={CurrentFormatVersion}");
        builder.AppendLine($"Layout={state.Layout}");
        builder.AppendLine($"DefaultViewMode={state.DefaultViewMode}");
        builder.AppendLine($"DefaultIconSize={state.DefaultIconSize}");
        builder.AppendLine($"ActivePane={Math.Clamp(state.ActivePane, 0, 3)}");
        builder.AppendLine($"TreeDisplayMode={state.TreeDisplayMode ?? ExplorerDirs.App.TreeDisplayMode.PerPane}");
        if (state.QdrTreeSplits is { } qdrTreeSplits)
        {
            if (!qdrTreeSplits.IsValid)
                throw new InvalidDataException("保留的 Q-Dir 目录树分隔值无效，未覆盖会话配置。");
            for (var pane = 0; pane < 4; pane++)
                if (qdrTreeSplits.Values.ElementAt(pane) is { } value)
                    builder.AppendLine($"QdrTreeSplit{pane + 1}={value}");
        }
        if (state.QdrColorMetadata is { } qdrColorMetadata)
        {
            if (!qdrColorMetadata.IsValid)
                throw new InvalidDataException("保留的 Q-Dir 颜色元数据无效，未覆盖会话配置。");
            builder.AppendLine("QdrColorMetadata=" + Convert.ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(qdrColorMetadata)));
        }
        if (state.QdrOpaqueMetadata is { } qdrOpaqueMetadata)
        {
            if (!qdrOpaqueMetadata.IsValid)
                throw new InvalidDataException("保留的 Q-Dir 未识别字段或区段超出安全范围，未覆盖会话配置。");
            builder.AppendLine("QdrOpaqueMetadata=" + Convert.ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(qdrOpaqueMetadata)));
        }
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = state.Panes.ElementAtOrDefault(paneIndex) ?? [];
            builder.AppendLine();
            builder.AppendLine($"[Pane{paneIndex}]");
            builder.AppendLine($"TabCount={tabs.Count}");
            builder.AppendLine($"ActiveTab={Math.Clamp(state.ActiveTabIndices?.ElementAtOrDefault(paneIndex) ?? 0,
                0, Math.Max(0, tabs.Count - 1))}");
            builder.AppendLine($"TreeVisible={state.PaneTreeVisibility?.ElementAtOrDefault(paneIndex) ?? false}");
            for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
            {
                var tab = tabs[tabIndex];
                builder.AppendLine();
                builder.AppendLine($"[Pane{paneIndex}.Tab{tabIndex}]");
                builder.AppendLine($"Path={tab.Path}");
                builder.AppendLine($"ViewMode={tab.View.Mode}");
                builder.AppendLine($"IconSize={tab.View.IconSize}");
                builder.AppendLine($"DisplayColumnCount={tab.View.DisplayColumns?.Count ?? 0}");
                if (tab.View.DisplayColumns is { } displayColumns)
                    for (var columnIndex = 0; columnIndex < displayColumns.Count; columnIndex++)
                        builder.AppendLine($"DisplayColumn{columnIndex}={displayColumns[columnIndex].Key},{displayColumns[columnIndex].Width}");
                builder.AppendLine($"SortCount={tab.View.SortColumns.Count}");
                for (var sortIndex = 0; sortIndex < tab.View.SortColumns.Count; sortIndex++)
                {
                    var sort = tab.View.SortColumns[sortIndex];
                    builder.AppendLine($"Sort{sortIndex}={sort.Key},{sort.Direction}");
                }
                builder.AppendLine($"GroupKey={tab.View.GroupKey}");
                builder.AppendLine($"GroupAscending={tab.View.GroupAscending.ToString().ToLowerInvariant()}");
                builder.AppendLine($"FilterPattern={tab.QuickFilter.Pattern}");
                builder.AppendLine($"FilterExcludeMatches={tab.QuickFilter.ExcludeMatches.ToString().ToLowerInvariant()}");
                builder.AppendLine($"FilterFolders={tab.QuickFilter.FilterFolders.ToString().ToLowerInvariant()}");
                var columnFilters = tab.QuickFilter.ColumnFilters;
                if (ColumnFilterValidationError(tab.QuickFilter) is { } filterError)
                    throw new InvalidDataException(filterError);
                builder.AppendLine($"ColumnFilterCount={columnFilters.Count}");
                for (var filterIndex = 0; filterIndex < columnFilters.Count; filterIndex++)
                {
                    var filter = columnFilters[filterIndex];
                    var values = filter.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var encodedValues = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(values));
                    builder.AppendLine($"ColumnFilter{filterIndex}Key={filter.Key}");
                    builder.AppendLine($"ColumnFilter{filterIndex}Values={encodedValues}");
                }
                builder.AppendLine($"PreviewPaneVisible={tab.PreviewPaneVisible.ToString().ToLowerInvariant()}");
                builder.AppendLine($"DetailsPaneVisible={tab.DetailsPaneVisible.ToString().ToLowerInvariant()}");
            }
        }
        return builder.ToString();
    }

    internal static SessionState? Parse(string content) => ParseProfile(content).State;
    internal static SessionState? ParseComplete(string content)
    {
        var parsed = ParseProfile(content);
        return parsed.Complete && !parsed.NewerFormat ? parsed.State : null;
    }

    private sealed record ProfileParseResult(SessionState? State, bool Complete, bool NewerFormat, int Version = 0);

    private static ProfileParseResult ParseProfile(string content)
    {
        var complete = true;
        var newerFormatSeen = false;
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        string? currentSection = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                currentSection = name;
                if (sections.TryGetValue(name, out var existing))
                {
                    complete = false;
                    current = existing;
                }
                else current = sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is not null && equals > 0)
            {
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (currentSection?.Equals("General", StringComparison.OrdinalIgnoreCase) == true &&
                    key.Equals("FormatVersion", StringComparison.OrdinalIgnoreCase) &&
                    BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var declaredVersion) && declaredVersion > CurrentFormatVersion)
                    newerFormatSeen = true;
                if (current.ContainsKey(key)) complete = false;
                current[key] = value;
            }
            else complete = false;
        }

        sections.TryGetValue("General", out var general);
        var version = general is null ? 0 : GetInt(general, "FormatVersion", 0);
        if (newerFormatSeen || version > CurrentFormatVersion) return new ProfileParseResult(null, false, true);
        if (version is < 1 or > CurrentFormatVersion) complete = false;
        var layout = ReadRequiredInt(general, "Layout", 4, 1, version >= 5 ? 12 : version == 4 ? 8 : 6, ref complete);
        var defaultView = ReadRequiredInt(general, "DefaultViewMode", 4, 1, 8, ref complete);
        var defaultIconSize = version >= 3
            ? ReadRequiredInt(general, "DefaultIconSize", -1, -1, 1024, ref complete) : -1;
        if (defaultIconSize == 0) { defaultIconSize = -1; complete = false; }
        var activePane = ReadOptionalInt(general, "ActivePane", 0, 0, 3, ref complete);
        TreeDisplayMode? treeDisplayMode = version >= 10
            ? ReadTreeDisplayMode(general, ref complete)
            : version == 9 ? ExplorerDirs.App.TreeDisplayMode.PerPane : null;
        var qdrTreeSplits = version >= 12 ? ReadQdrTreeSplits(general, ref complete) : null;
        var qdrColorMetadata = version >= 13 ? ReadQdrColorMetadata(general, ref complete) : null;
        var qdrOpaqueMetadata = version >= 14 ? ReadQdrOpaqueMetadata(general, ref complete) : null;
        var panes = new List<List<TabSettings>>();
        var activeTabs = new List<int>();
        var paneTreeVisibility = version >= 9 ? new List<bool>(4) : null;
        for (var paneIndex = 0; paneIndex < 4; paneIndex++)
        {
            var tabs = new List<TabSettings>();
            var paneName = $"Pane{paneIndex}";
            sections.TryGetValue(paneName, out var pane);
            var count = ReadRequiredInt(pane, "TabCount", 0, 0, MaximumTabsPerPane, ref complete);
            var indices = new SortedSet<int>(Enumerable.Range(0, count));
            var tabPrefix = paneName + ".Tab";
            foreach (var sectionName in sections.Keys.Where(name =>
                name.StartsWith(tabPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (!int.TryParse(sectionName[tabPrefix.Length..], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var index) || index < 0 || index >= MaximumTabsPerPane)
                {
                    complete = false;
                    continue;
                }
                if (index >= count) complete = false;
                indices.Add(index);
            }
            foreach (var tabIndex in indices)
            {
                if (!sections.TryGetValue($"{paneName}.Tab{tabIndex}", out var values) ||
                    !values.TryGetValue("Path", out var path) || string.IsNullOrWhiteSpace(path))
                {
                    complete = false;
                    continue;
                }
                if (!ShellLocationIdentity.TryNormalize(path, out path, out _))
                {
                    complete = false;
                    continue;
                }
                var sorts = new List<SortColumnState>();
                var sortCount = ReadRequiredInt(values, "SortCount", 0, 0, 16, ref complete);
                var sortIndices = new SortedSet<int>(Enumerable.Range(0, sortCount));
                foreach (var sortName in values.Keys.Where(key =>
                    key.StartsWith("Sort", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("SortCount", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!int.TryParse(sortName[4..], NumberStyles.None, CultureInfo.InvariantCulture,
                            out var index) || index is < 0 or >= 16 ||
                        !sortName.Equals($"Sort{index}", StringComparison.OrdinalIgnoreCase))
                    {
                        complete = false;
                        continue;
                    }
                    if (index >= sortCount) complete = false;
                    sortIndices.Add(index);
                }
                foreach (var sortIndex in sortIndices)
                {
                    if (!values.TryGetValue($"Sort{sortIndex}", out var serialized))
                    {
                        complete = false;
                        continue;
                    }
                    var separator = serialized.LastIndexOf(',');
                    var key = separator < 0 ? null : PropertyKeyState.Parse(serialized[..separator]);
                    if (key is not null && int.TryParse(serialized[(separator + 1)..], out var direction) &&
                        direction is 1 or -1)
                        sorts.Add(new SortColumnState(key, direction));
                    else complete = false;
                }
                var mode = (uint)ReadRequiredInt(values, "ViewMode", 4, 1, 8, ref complete);
                var iconSize = ReadRequiredInt(values, "IconSize", -1, -1, 1024, ref complete);
                if (!values.TryGetValue("GroupKey", out var groupText)) complete = false;
                var group = PropertyKeyState.Parse(groupText);
                if (!string.IsNullOrEmpty(groupText) && group is null) complete = false;
                if (!values.TryGetValue("GroupAscending", out var ascendingText) ||
                    !bool.TryParse(ascendingText, out var ascending))
                {
                    complete = false;
                    ascending = true;
                }
                var columns = ColumnLayout.Parse(values, version >= 2, ref complete);
                var view = new ViewState(mode, iconSize, sorts, group, ascending, columns);
                var previewPaneVisible = version >= 7
                    ? ReadOptionalBool(values, "PreviewPaneVisible", false, ref complete) : false;
                var detailsPaneVisible = version >= 8
                    ? ReadOptionalBool(values, "DetailsPaneVisible", false, ref complete) : false;
                if (previewPaneVisible && detailsPaneVisible)
                {
                    complete = false;
                    detailsPaneVisible = false;
                }
                tabs.Add(new TabSettings(path, view, ReadQuickFilter(values, version, ref complete),
                    previewPaneVisible, detailsPaneVisible));
            }
            panes.Add(tabs);
            activeTabs.Add(ReadOptionalInt(pane, "ActiveTab", 0, 0, Math.Max(0, tabs.Count - 1),
                ref complete));
            if (version >= 9)
            {
                if (pane is not null && pane.TryGetValue("TreeVisible", out var treeText) &&
                    bool.TryParse(treeText, out var treeVisible))
                    paneTreeVisibility!.Add(treeVisible);
                else
                {
                    paneTreeVisibility!.Add(false);
                    complete = false;
                }
            }
        }
        if ((version is < 1 or > CurrentFormatVersion) && panes.All(pane => pane.Count == 0))
            return new ProfileParseResult(null, false, false);
        return new ProfileParseResult(new SessionState(layout, (uint)defaultView, panes,
            activePane, activeTabs, defaultIconSize, paneTreeVisibility, treeDisplayMode, qdrTreeSplits,
            qdrColorMetadata, qdrOpaqueMetadata), complete, false, version);
    }

    private static QdrOpaqueMetadata? ReadQdrOpaqueMetadata(Dictionary<string, string>? general, ref bool complete)
    {
        if (general is null || !general.TryGetValue("QdrOpaqueMetadata", out var encoded)) return null;
        try
        {
            if (encoded.Length > 1_400_000) throw new InvalidDataException();
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException();
            var metadata = JsonSerializer.Deserialize<QdrOpaqueMetadata>(bytes);
            if (metadata is { IsValid: true }) return metadata;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException or NotSupportedException)
        {
        }
        complete = false;
        return null;
    }

    private static QdrColorMetadata? ReadQdrColorMetadata(Dictionary<string, string>? general, ref bool complete)
    {
        if (general is null || !general.TryGetValue("QdrColorMetadata", out var encoded)) return null;
        try
        {
            if (encoded.Length > 1_400_000) throw new InvalidDataException();
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > 1024 * 1024) throw new InvalidDataException();
            var metadata = JsonSerializer.Deserialize<QdrColorMetadata>(bytes);
            if (metadata is { IsValid: true }) return metadata;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
        {
        }
        complete = false;
        return null;
    }

    private static QdrTreeSplitState? ReadQdrTreeSplits(Dictionary<string, string>? general, ref bool complete)
    {
        if (general is null) return null;
        var values = new string?[4];
        var any = false;
        for (var pane = 0; pane < values.Length; pane++)
        {
            if (!general.TryGetValue($"QdrTreeSplit{pane + 1}", out var value)) continue;
            any = true;
            if (value.Length is > 0 and <= 32 && int.TryParse(value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out _)) values[pane] = value;
            else complete = false;
        }
        return any && values.Any(value => value is not null)
            ? new QdrTreeSplitState(values[0], values[1], values[2], values[3]) : null;
    }

    private static TreeDisplayMode? ReadTreeDisplayMode(Dictionary<string, string>? general, ref bool complete)
    {
        if (general is not null && general.TryGetValue("TreeDisplayMode", out var value) &&
            Enum.TryParse<TreeDisplayMode>(value, ignoreCase: true, out var mode) &&
            Enum.IsDefined(mode)) return mode;
        complete = false;
        return ExplorerDirs.App.TreeDisplayMode.PerPane;
    }

    private static int ReadOptionalInt(Dictionary<string, string>? values, string key, int fallback,
        int minimum, int maximum, ref bool complete) =>
        values is null || !values.ContainsKey(key) ? fallback :
            ReadRequiredInt(values, key, fallback, minimum, maximum, ref complete);

    private static QuickFilterSettings ReadQuickFilter(Dictionary<string, string> values, int version,
        ref bool complete)
    {
        if (version < 6) return QuickFilterSettings.Empty;
        var pattern = values.GetValueOrDefault("FilterPattern", string.Empty);
        var exclude = ReadOptionalBool(values, "FilterExcludeMatches", false, ref complete);
        var folders = ReadOptionalBool(values, "FilterFolders", false, ref complete);
        var columnFilters = version >= 11 ? ReadColumnFilters(values, ref complete) : [];
        return new QuickFilterSettings(pattern, exclude, folders, columnFilters);
    }

    private static IReadOnlyList<ColumnValueFilter> ReadColumnFilters(Dictionary<string, string> values,
        ref bool complete)
    {
        var count = ReadRequiredInt(values, "ColumnFilterCount", 0, 0, MaximumColumnFiltersPerTab, ref complete);
        List<ColumnValueFilter> filters = [];
        var payloadLength = 0;
        for (var index = 0; index < count; index++)
        {
            if (!values.TryGetValue($"ColumnFilter{index}Key", out var keyText) ||
                !values.TryGetValue($"ColumnFilter{index}Values", out var encodedValues))
            {
                complete = false;
                continue;
            }

            payloadLength += encodedValues.Length;
            if (payloadLength > MaximumColumnFilterPayloadLength || encodedValues.Length > MaximumColumnFilterPayloadLength)
            {
                complete = false;
                continue;
            }

            var key = PropertyKeyState.Parse(keyText);
            try
            {
                var decoded = JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(encodedValues));
                if (key is null || key.FormatId == Guid.Empty || decoded is null ||
                    decoded.Length is 0 or > QuickFilterSettings.MaximumColumnFilterValues ||
                    decoded.Any(value => value is null || value.Length > MaximumColumnFilterValueLength ||
                        value.Contains('\r') || value.Contains('\n')))
                {
                    complete = false;
                    continue;
                }
                filters.Add(new ColumnValueFilter(key, decoded));
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                complete = false;
            }
        }
        return filters;
    }

    private static bool ReadOptionalBool(Dictionary<string, string> values, string key, bool fallback,
        ref bool complete)
    {
        if (!values.TryGetValue(key, out var text)) return fallback;
        if (bool.TryParse(text, out var result)) return result;
        complete = false;
        return fallback;
    }

    private static int ReadRequiredInt(Dictionary<string, string>? values, string key, int fallback,
        int minimum, int maximum, ref bool complete)
    {
        if (values is not null && values.TryGetValue(key, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) &&
            result >= minimum && result <= maximum)
            return result;
        complete = false;
        return fallback;
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    private sealed record LegacySessionState(int Layout, List<List<string>> Panes);
}
