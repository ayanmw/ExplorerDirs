using System.Globalization;

namespace ExplorerDirs.App;

// SP0 is the root splitter, SP1/SP2 its two child splitters. Values are
// ten-thousandths of each splitter's usable extent, not absolute pane widths.
internal static class QdrProportions
{
    internal static LayoutProportions? Read(Dictionary<string, string> values, int? layout, ISet<string> warnings)
    {
        if (!Enumerable.Range(0, 3).Any(i => values.ContainsKey("SP" + i))) return null;
        if (layout is null) { warnings.Add("布局未知，未恢复分隔比例。"); return null; }
        var split = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var value = 5000;
            if (values.TryGetValue("SP" + i, out var text) &&
                (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value is <= 0 or >= 10000))
            {
                warnings.Add("分隔比例无效或包含零宽窗格，保留本程序已有比例。");
                return null;
            }
            split[i] = value / 10000d;
        }
        var root = split[0]; var first = split[1]; var second = split[2];
        return layout switch
        {
            1 => LayoutProportions.Equal,
            2 => new(first, 0.5),
            3 => new(0.5, root),
            4 => new(first, root, BottomHorizontal: first == second ? null : second),
            5 => new(root, second),
            6 => new(second, root),
            7 => new(root, first),
            8 => new(first, root),
            9 or 10 => new(0.5, 0.5, new(root * first, root * (1 - first), 1 - root)),
            11 or 12 => new(0.5, 0.5, new(root * first, root * (1 - first), (1 - root) * second, (1 - root) * (1 - second))),
            _ => null
        };
    }

    internal static void Write(Dictionary<string, string> values, int layout, LayoutProportions proportions)
    {
        if (!proportions.IsValidFor(LayoutPreset.Find(layout))) throw new ArgumentException("布局比例无效。");
        var root = 0.5; var first = 0.5; var second = 0.5;
        switch (layout)
        {
            case 2: first = proportions.Horizontal; break;
            case 3: root = proportions.Vertical; break;
            case 4: root = proportions.Vertical; first = proportions.Horizontal; second = proportions.BottomFraction; break;
            case 5: root = proportions.Horizontal; second = proportions.Vertical; break;
            case 6: root = proportions.Vertical; second = proportions.Horizontal; break;
            case 7: root = proportions.Horizontal; first = proportions.Vertical; break;
            case 8: root = proportions.Vertical; first = proportions.Horizontal; break;
            case 9: case 10: case 11: case 12:
                var weights = proportions.StripWeights(layout >= 11 ? 4 : 3);
                var leading = weights[0] + weights[1];
                root = leading / weights.Sum();
                first = weights[0] / leading;
                if (weights.Length == 4) second = weights[2] / (weights[2] + weights[3]);
                break;
        }
        foreach (var (index, fraction) in new[] { root, first, second }.Select((fraction, index) => (index, fraction)))
            // Q-Dir's integer fields cannot represent arbitrarily small positive
            // fractions. Keep a nonzero pane; the extension retains exact values.
            values["SP" + index] = Math.Clamp((int)Math.Round(fraction * 10000, MidpointRounding.AwayFromZero), 1, 9999)
                .ToString(CultureInfo.InvariantCulture);
    }
}
