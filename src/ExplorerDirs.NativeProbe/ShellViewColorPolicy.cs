namespace ExplorerDirs.NativeProbe;

internal sealed class ShellListSelectionSnapshot
{
    private readonly HashSet<nuint> _selectedItems;

    private ShellListSelectionSnapshot(HashSet<nuint> selectedItems) => _selectedItems = selectedItems;

    internal static ShellListSelectionSnapshot Empty() => new([]);

    internal static ShellListSelectionSnapshot FromSelectedIndices(IEnumerable<nuint> indices) =>
        new(indices.ToHashSet());

    internal static ShellListSelectionSnapshot Capture(int itemCount, Func<int, uint> readItemState)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentNullException.ThrowIfNull(readItemState);
        var selectedItems = new HashSet<nuint>();
        for (var index = 0; index < itemCount; index++)
        {
            if ((readItemState(index) & 0x0002) != 0) // LVIS_SELECTED.
                selectedItems.Add((nuint)index);
        }
        return new ShellListSelectionSnapshot(selectedItems);
    }

    internal bool Contains(nuint itemIndex) => _selectedItems.Contains(itemIndex);
}

internal readonly record struct ShellItemDrawColors(uint? Text, uint? Background,
    ShellItemDrawFontStyle FontStyle = default);

internal enum ShellColorRuleDecision
{
    // Q-Dir documents -1 as allowing a later filter to set this property.
    Continue,
    Unset,
    Set
}

internal readonly record struct ShellColorRuleProperty<T>(ShellColorRuleDecision Decision, T Value) where T : struct
{
    internal static ShellColorRuleProperty<T> Continue => new(ShellColorRuleDecision.Continue, default);
    internal static ShellColorRuleProperty<T> Unset => new(ShellColorRuleDecision.Unset, default);
    internal static ShellColorRuleProperty<T> Set(T value) => new(ShellColorRuleDecision.Set, value);
}

internal readonly record struct ShellViewColorRule(
    string Pattern,
    ShellColorRuleProperty<uint> TextColor,
    ShellColorRuleProperty<uint> BackgroundColor,
    ShellColorRuleProperty<bool> Bold,
    ShellColorRuleProperty<bool> Italic,
    ShellColorRuleProperty<bool> Underline,
    ShellColorRuleProperty<bool> Strikeout)
{
    internal static ShellViewColorRule BackgroundOnly(string pattern, ShellColorRuleDecision decision, uint color) =>
        new(pattern, ShellColorRuleProperty<uint>.Continue,
            new ShellColorRuleProperty<uint>(decision, color),
            ShellColorRuleProperty<bool>.Continue, ShellColorRuleProperty<bool>.Continue,
            ShellColorRuleProperty<bool>.Continue, ShellColorRuleProperty<bool>.Continue);
}

internal static class ShellViewColorRuleMatcher
{
    internal static bool MatchesViewBackground(string pattern, bool isActiveView) =>
        pattern.Equals("#BG", StringComparison.OrdinalIgnoreCase) ||
        isActiveView && pattern.Equals("#BG-A", StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(string patterns, string displayName, FileAttributes attributes) =>
        Matches(patterns, displayName,
            isDirectory: (attributes & FileAttributes.Directory) != 0,
            isHidden: (attributes & FileAttributes.Hidden) != 0,
            isReadOnly: (attributes & FileAttributes.ReadOnly) != 0);

    internal static bool Matches(string patterns, string displayName, bool isDirectory, bool isHidden,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(displayName);

        foreach (var rawPattern in patterns.Split(';'))
        {
            var pattern = rawPattern.Trim();
            if (pattern.Length == 0) continue;
            if (pattern.Equals("#DIR", StringComparison.OrdinalIgnoreCase))
            {
                if (isDirectory) return true;
                continue;
            }
            // The public FAQ documents `#`, while this project's local Q-Dir
            // [Filter2] sample uses `#Hidden`; treat both as the same category.
            if (pattern == "#" || pattern.Equals("#Hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (isHidden) return true;
                continue;
            }
            if (pattern.Equals("#RO", StringComparison.OrdinalIgnoreCase))
            {
                if (isReadOnly) return true;
                continue;
            }
            if (!isDirectory && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    pattern, displayName, ignoreCase: true))
                return true;
        }

        return false;
    }
}

internal static class ShellViewColorRuleEvaluator
{
    internal static uint? ResolveBackground(IReadOnlyList<ShellViewColorRule> rules, string displayName,
        FileAttributes attributes) => Resolve(rules,
        rule => ShellViewColorRuleMatcher.Matches(rule.Pattern, displayName, attributes),
        static rule => rule.BackgroundColor);

    internal static uint? ResolveTextColor(IReadOnlyList<ShellViewColorRule> rules, string displayName,
        FileAttributes attributes) => Resolve(rules,
        rule => ShellViewColorRuleMatcher.Matches(rule.Pattern, displayName, attributes),
        static rule => rule.TextColor);

    internal static bool? ResolveFontStyle(IReadOnlyList<ShellViewColorRule> rules, string displayName,
        FileAttributes attributes, Func<ShellViewColorRule, ShellColorRuleProperty<bool>> selectProperty) =>
        Resolve(rules, rule => ShellViewColorRuleMatcher.Matches(rule.Pattern, displayName, attributes), selectProperty);

    internal static uint? ResolveViewBackground(IReadOnlyList<ShellViewColorRule> rules, bool isActiveView) =>
        Resolve(rules, rule => ShellViewColorRuleMatcher.MatchesViewBackground(rule.Pattern, isActiveView),
            static rule => rule.BackgroundColor);

    private static T? Resolve<T>(IReadOnlyList<ShellViewColorRule> rules,
        Func<ShellViewColorRule, bool> matches,
        Func<ShellViewColorRule, ShellColorRuleProperty<T>> selectProperty) where T : struct
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(selectProperty);
        foreach (var rule in rules)
        {
            if (!matches(rule)) continue;

            var property = selectProperty(rule);
            switch (property.Decision)
            {
                case ShellColorRuleDecision.Continue:
                    continue;
                case ShellColorRuleDecision.Unset:
                    return null;
                case ShellColorRuleDecision.Set:
                    return property.Value;
            }
        }

        return null;
    }
}

internal static class ShellViewColorBackgroundPolicy
{
    internal const uint NoColor = 0xFFFFFFFF; // CLR_NONE asks the ListView parent to paint the Shell default.

    internal static bool TryApply(nint listView, uint? color, Func<nint, uint, bool> setBackground)
    {
        ArgumentNullException.ThrowIfNull(setBackground);
        return listView != 0 && setBackground(listView, color ?? NoColor);
    }
}

internal static class ShellViewColorPolicy
{
    internal static bool TryGetColors(nuint itemIndex, ShellListSelectionSnapshot selection,
        IReadOnlyDictionary<nuint, uint> ruleTextColors, IReadOnlyDictionary<nuint, uint> ruleBackgroundColors,
        out ShellItemDrawColors colors,
        IReadOnlyDictionary<nuint, ShellItemDrawFontStyle>? ruleFontStyles = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(ruleTextColors);
        ArgumentNullException.ThrowIfNull(ruleBackgroundColors);
        if (selection.Contains(itemIndex))
        {
            colors = default;
            return false;
        }

        colors = new ShellItemDrawColors(
            ruleTextColors.TryGetValue(itemIndex, out var text) ? text : null,
            ruleBackgroundColors.TryGetValue(itemIndex, out var background) ? background : null,
            ruleFontStyles is not null && ruleFontStyles.TryGetValue(itemIndex, out var fontStyle)
                ? fontStyle : default);
        if (colors.Text is null && colors.Background is null && !colors.FontStyle.HasOverrides)
        {
            colors = default;
            return false;
        }
        return true;
    }
}
