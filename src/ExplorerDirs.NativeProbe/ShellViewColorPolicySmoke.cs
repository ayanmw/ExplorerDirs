namespace ExplorerDirs.NativeProbe;

internal static class ShellViewColorPolicySmoke
{
    internal static int Run()
    {
        var appliedColors = new List<uint>();
        Check(ShellViewColorBackgroundPolicy.TryApply(1, 0x00112233,
                (_, color) => { appliedColors.Add(color); return true; }) &&
              appliedColors.SequenceEqual(new uint[] { 0x00112233 }),
            "a resolved view background is sent to the native list view unchanged");
        Check(ShellViewColorBackgroundPolicy.TryApply(1, null,
                (_, color) => { appliedColors.Add(color); return true; }) &&
              appliedColors.Last() == ShellViewColorBackgroundPolicy.NoColor,
            "an unresolved view background restores the parent-painted Shell default");
        Check(!ShellViewColorBackgroundPolicy.TryApply(0, 0x00112233,
                (_, _) => throw new InvalidOperationException("A missing view window must not be called.")),
            "a missing native list view is rejected without sending a message");

        ShellViewReferenceRetirementSmoke.Run();
        ShellViewNavigationHistorySmoke.Run();
        ShellViewColorBrowserHostSmoke.Run();
        ShellViewColorFontSmoke.Run();
        Check(ShellViewColorRuleMatcher.Matches("*.dok;*.txt;*.rtf;", "notes.TXT", isDirectory: false, isHidden: false),
            "semicolon-separated extension patterns match case-insensitively");
        Check(ShellViewColorRuleMatcher.Matches("*.mp*", "track.mp3", isDirectory: false, isHidden: false),
            "wildcard patterns match more than one extension character");
        Check(!ShellViewColorRuleMatcher.Matches("*.txt", "folder.txt", isDirectory: true, isHidden: false),
            "extension patterns do not classify directories as files");
        Check(ShellViewColorRuleMatcher.Matches("#DIR", "folder", isDirectory: true, isHidden: false),
            "the documented #DIR token matches folders");
        Check(ShellViewColorRuleMatcher.Matches("#", ".hidden", isDirectory: false, isHidden: true),
            "the documented # token matches hidden items");
        Check(ShellViewColorRuleMatcher.Matches("#Hidden", ".hidden", isDirectory: false, isHidden: true),
            "the Q-Dir INI #Hidden alias matches hidden items");
        Check(!ShellViewColorRuleMatcher.Matches("#Hidden", "visible.txt", isDirectory: false, isHidden: false),
            "the Q-Dir INI #Hidden alias does not match visible items");
        Check(!ShellViewColorRuleMatcher.Matches("#", "visible.txt", isDirectory: false, isHidden: false),
            "the hidden token does not match visible items");
        Check(ShellViewColorRuleMatcher.Matches("#RO", "locked.txt", isDirectory: false, isHidden: false, isReadOnly: true),
            "the documented #RO token matches read-only items");
        Check(!ShellViewColorRuleMatcher.Matches("#RO", "writable.txt", isDirectory: false, isHidden: false, isReadOnly: false),
            "the read-only token does not match writable items");
        Check(ShellViewColorRuleMatcher.Matches("#RO", "locked.txt", FileAttributes.ReadOnly),
            "read-only filesystem attributes reach the documented #RO rule");
        Check(ShellViewColorRuleMatcher.Matches("#", ".hidden", FileAttributes.Hidden),
            "hidden filesystem attributes reach the documented hidden rule");
        Check(!ShellViewColorRuleMatcher.Matches(" ; ; ", "visible.txt", isDirectory: false, isHidden: false),
            "empty pattern segments do not match every item");

        var continueThenSet = new[]
        {
            ShellViewColorRule.BackgroundOnly("*.txt", ShellColorRuleDecision.Continue, 0x00000000),
            ShellViewColorRule.BackgroundOnly("report*", ShellColorRuleDecision.Set, 0x00112233)
        };
        Check(ShellViewColorRuleEvaluator.ResolveBackground(continueThenSet, "report.txt", FileAttributes.Normal) == 0x00112233,
            "a matching continue rule allows a later matching rule to set the color");
        var lockedUnset = new[]
        {
            ShellViewColorRule.BackgroundOnly("*.txt", ShellColorRuleDecision.Unset, 0x00000000),
            ShellViewColorRule.BackgroundOnly("report*", ShellColorRuleDecision.Set, 0x00112233)
        };
        Check(ShellViewColorRuleEvaluator.ResolveBackground(lockedUnset, "report.txt", FileAttributes.Normal) is null,
            "a matching locked-unset rule prevents a later rule from setting the color");
        var lockedSet = new[]
        {
            ShellViewColorRule.BackgroundOnly("*.txt", ShellColorRuleDecision.Set, 0x00AABBCC),
            ShellViewColorRule.BackgroundOnly("report*", ShellColorRuleDecision.Set, 0x00112233)
        };
        Check(ShellViewColorRuleEvaluator.ResolveBackground(lockedSet, "report.txt", FileAttributes.Normal) == 0x00AABBCC,
            "the first matching locked-set rule determines the color");
        var nonMatching = new[]
        {
            ShellViewColorRule.BackgroundOnly("*.jpg", ShellColorRuleDecision.Unset, 0x00000000),
            ShellViewColorRule.BackgroundOnly("*.txt", ShellColorRuleDecision.Continue, 0x00000000)
        };
        Check(ShellViewColorRuleEvaluator.ResolveBackground(nonMatching, "report.txt", FileAttributes.Normal) is null,
            "nonmatching and final continue rules leave the color unset");
        var independentProperties = new[]
        {
            new ShellViewColorRule("report.txt",
                ShellColorRuleProperty<uint>.Continue, ShellColorRuleProperty<uint>.Set(0x00AABBCC),
                ShellColorRuleProperty<bool>.Set(true), ShellColorRuleProperty<bool>.Continue,
                ShellColorRuleProperty<bool>.Continue, ShellColorRuleProperty<bool>.Continue),
            new ShellViewColorRule("#RO",
                ShellColorRuleProperty<uint>.Set(0x00010203), ShellColorRuleProperty<uint>.Continue,
                ShellColorRuleProperty<bool>.Continue, ShellColorRuleProperty<bool>.Set(true),
                ShellColorRuleProperty<bool>.Unset, ShellColorRuleProperty<bool>.Continue)
        };
        Check(ShellViewColorRuleEvaluator.ResolveBackground(independentProperties, "report.txt", FileAttributes.ReadOnly) == 0x00AABBCC &&
              ShellViewColorRuleEvaluator.ResolveTextColor(independentProperties, "report.txt", FileAttributes.ReadOnly) == 0x00010203,
            "background and text colors resolve independently across the same ordered rules");
        Check(ShellViewColorRuleEvaluator.ResolveFontStyle(independentProperties, "report.txt", FileAttributes.ReadOnly,
                  static rule => rule.Bold) == true &&
              ShellViewColorRuleEvaluator.ResolveFontStyle(independentProperties, "report.txt", FileAttributes.ReadOnly,
                  static rule => rule.Italic) == true &&
              ShellViewColorRuleEvaluator.ResolveFontStyle(independentProperties, "report.txt", FileAttributes.ReadOnly,
                  static rule => rule.Underline) is null &&
              ShellViewColorRuleEvaluator.ResolveFontStyle(independentProperties, "report.txt", FileAttributes.ReadOnly,
                  static rule => rule.Strikeout) is null,
            "bold, italic, underline, and strikeout each have independent continuation states");

        Check(!ShellViewColorRuleMatcher.Matches("#BG", "report.txt", FileAttributes.Normal) &&
              !ShellViewColorRuleMatcher.Matches("#BG-A", "report.txt", FileAttributes.Normal),
            "view-background tokens are not treated as file-name patterns");
        var viewBackgroundRules = new[]
        {
            ShellViewColorRule.BackgroundOnly("#BG-A", ShellColorRuleDecision.Set, 0x00112233),
            ShellViewColorRule.BackgroundOnly("#BG", ShellColorRuleDecision.Set, 0x00AABBCC)
        };
        Check(ShellViewColorRuleEvaluator.ResolveViewBackground(viewBackgroundRules, isActiveView: true) == 0x00112233 &&
              ShellViewColorRuleEvaluator.ResolveViewBackground(viewBackgroundRules, isActiveView: false) == 0x00AABBCC,
            "#BG-A applies only to the active view while #BG supplies the other view background");
        var continuingViewBackgroundRules = new[]
        {
            ShellViewColorRule.BackgroundOnly("#BG-A", ShellColorRuleDecision.Continue, 0x00000000),
            ShellViewColorRule.BackgroundOnly("#BG", ShellColorRuleDecision.Set, 0x00AABBCC)
        };
        Check(ShellViewColorRuleEvaluator.ResolveViewBackground(continuingViewBackgroundRules, isActiveView: true) == 0x00AABBCC,
            "an active-view background rule can continue to the shared background rule");
        var lockedViewBackgroundRules = new[]
        {
            ShellViewColorRule.BackgroundOnly("#BG", ShellColorRuleDecision.Set, 0x00AABBCC),
            ShellViewColorRule.BackgroundOnly("#BG-A", ShellColorRuleDecision.Set, 0x00112233)
        };
        Check(ShellViewColorRuleEvaluator.ResolveViewBackground(lockedViewBackgroundRules, isActiveView: true) == 0x00AABBCC,
            "view-background rules obey the configured high-to-low priority order");

        // DefView reported 0x201 for every row even when LVIS_SELECTED was clear.
        uint[] listViewStates = [0, 0x0002, 0x0201, 0x0202];
        var selection = ShellListSelectionSnapshot.Capture(listViewStates.Length, index => listViewStates[index]);
        if (selection.Contains(0) || !selection.Contains(1) || selection.Contains(2) || !selection.Contains(3))
            throw new InvalidOperationException("LVIS_SELECTED snapshot did not distinguish actual selection bits from DefView draw state.");

        var ruleColors = new Dictionary<nuint, uint>
        {
            [0] = 0x00F0E0D0,
            [1] = 0x00D0F0E0,
            [2] = 0x00A0B0C0,
            [3] = 0x00112233
        };
        var ruleTextColors = new Dictionary<nuint, uint>
        {
            [0] = 0x00010203,
            [2] = 0x00040506,
            [4] = 0x00070809
        };
        var ruleFontStyles = new Dictionary<nuint, ShellItemDrawFontStyle>
        {
            [0] = new(true, null, true, null),
            [1] = new(false, false, false, false)
        };
        if (!ShellViewColorPolicy.TryGetColors(0, selection, ruleTextColors, ruleColors, out var ordinary) ||
            ordinary != new ShellItemDrawColors(0x00010203, 0x00F0E0D0))
            throw new InvalidOperationException("An unselected row did not receive its configured colors.");
        if (!ShellViewColorPolicy.TryGetColors(0, selection, ruleTextColors, ruleColors, out var styled,
                ruleFontStyles) || styled.FontStyle != new ShellItemDrawFontStyle(true, null, true, null))
            throw new InvalidOperationException("An unselected row did not receive its configured font styles.");
        if (ShellViewColorPolicy.TryGetColors(1, selection, ruleTextColors, ruleColors, out _, ruleFontStyles))
            throw new InvalidOperationException("A selected row was assigned Q-Dir font styles instead of native Shell drawing.");
        if (ShellViewColorPolicy.TryGetColors(1, selection, ruleTextColors, ruleColors, out _))
            throw new InvalidOperationException("A selected row was assigned rule colors instead of native Shell colors.");
        if (!ShellViewColorPolicy.TryGetColors(2, selection, ruleTextColors, ruleColors, out var defViewFlag) ||
            defViewFlag != new ShellItemDrawColors(0x00040506, 0x00A0B0C0))
            throw new InvalidOperationException("A CDIS_SELECTED-only draw flag incorrectly suppressed an unselected row color.");
        if (ShellViewColorPolicy.TryGetColors(3, selection, ruleTextColors, ruleColors, out _))
            throw new InvalidOperationException("A row with LVIS_SELECTED was assigned rule colors.");
        if (!ShellViewColorPolicy.TryGetColors(4, selection, ruleTextColors, ruleColors, out var textOnly) ||
            textOnly != new ShellItemDrawColors(0x00070809, null))
            throw new InvalidOperationException("A text-only rule replaced the native background color.");
        if (ShellViewColorPolicy.TryGetColors(9, selection, ruleTextColors, ruleColors, out _))
            throw new InvalidOperationException("An unmatched row should retain native Shell colors.");

        Console.WriteLine("shell-view-color-policy-smoke: q-dir-name-folder-hidden-patterns=True; q-dir-rule-priority=True; independent-properties=True; view-background-active-rules=True; selected=1,3; defview-only-selected-bit=ignored; configured-properties-only=True; native-unmatched-preserved=True; font-style-policy=True");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Shell view color rule failed: {message}.");
    }
}
