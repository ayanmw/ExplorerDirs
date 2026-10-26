using ExplorerDirs.App;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

internal static class QdrProportionsSmoke
{
    internal static void Run()
    {
        var cases = new (int Native, int App, LayoutProportions Expected, string[] Written)[]
        {
            (11, 1, new(.5, .5), ["5000", "5000", "5000"]),
            (3, 2, new(.25, .5), ["5000", "2500", "5000"]),
            (4, 3, new(.5, .6), ["6000", "5000", "5000"]),
            (0, 4, new(.25, .6), ["6000", "2500", "2500"]),
            (9, 5, new(.6, .4), ["6000", "5000", "4000"]),
            (1, 6, new(.4, .6), ["6000", "5000", "4000"]),
            (10, 7, new(.6, .25), ["6000", "2500", "5000"]),
            (2, 8, new(.25, .6), ["6000", "2500", "5000"]),
            (7, 9, new(.5, .5, new(.15, .45, .4)), ["6000", "2500", "5000"]),
            (8, 10, new(.5, .5, new(.15, .45, .4)), ["6000", "2500", "5000"]),
            (5, 11, new(.5, .5, new(.15, .45, .16, .24)), ["6000", "2500", "4000"]),
            (6, 12, new(.5, .5, new(.15, .45, .16, .24)), ["6000", "2500", "4000"])
        };
        foreach (var sample in cases)
        {
            var input = $"Q-Dir Version=12.79\nViewMode={sample.Native}\nSP0=6000\nSP1=2500\nSP2={(sample.App == 4 ? 2500 : 4000)}\nDir1=C:\\fixture\n";
            var read = Parse(input);
            Check(read.Proportions?.Count == 1 && Close(read.Proportions[sample.App], sample.Expected), "independent native proportion mapping");
            var state = State(sample.App);
            var ratios = new Dictionary<int, LayoutProportions> { [sample.App] = sample.Expected };
            var output = QdrExporter.Render(state, [], ratios);
            for (var i = 0; i < 3; i++) Check(output.Contains($"\r\nSP{i}={sample.Written[i]}\r\n"), "independent native output field");
            var basic = Parse(string.Join('\n', output.Split('\n').Where(line => !line.StartsWith(QdrExporter.Prefix))));
            Check(basic.ExtendedState is null && Close(basic.Proportions![sample.App], sample.Expected), "base-only consumer preserves nominal proportions");
        }
        var unequal = Parse("Q-Dir Version=12.79\nViewMode=0\nSP0=6000\nSP1=2500\nSP2=4000\nDir1=C:\\fixture\n");
        Check(unequal.TabCount == 1 && unequal.Layout == 4 && unequal.Proportions is not null, "unequal quad retains both independent dividers");
        Check(unequal.Proportions![4] == new LayoutProportions(.25, .6, BottomHorizontal: .4), "native root and two child fractions remain independent");
        var quadOutput = QdrExporter.Render(State(4), [], unequal.Proportions);
        Check(quadOutput.Contains("SP0=6000\r\nSP1=2500\r\nSP2=4000\r\n") &&
            quadOutput.Contains("ExplorerDirs.FormatVersion=3"), "unequal quad writes independent native fields and protected extension");
        Check(Parse(quadOutput).Proportions![4] == unequal.Proportions[4] &&
            Parse(string.Join('\n', quadOutput.Split('\n').Where(line => !line.StartsWith("ExplorerDirs.")))).Proportions![4] == unequal.Proportions[4],
            "quad native-only and full extension recover independent fractions");
        Check(Parse(quadOutput.Replace("ExplorerDirs.FormatVersion=3", "ExplorerDirs.FormatVersion=2")).ExtendedState is null,
            "new proportions mislabeled as old extension are rejected");
        foreach (var invalid in new[] { "0", "10000", "-1", "10001", "NaN", "1.5", "999999999999999999999" })
        {
            var read = Parse($"Q-Dir Version=12.79\nViewMode=3\nSP1={invalid}\nDir1=C:\\fixture\n");
            Check(read.TabCount == 1 && read.Proportions is null && read.Warnings.Any(w => w.Contains("分隔比例无效")), "invalid ratios never drop readable tabs");
        }
        Check(Parse("Q-Dir Version=12.79\nViewMode=3\nDir1=C:\\fixture\n").Proportions is null, "old file without SP leaves ratios unchanged");
        Check(Parse("Q-Dir Version=12.79\nViewMode=3\nSP0=6000\nDir1=C:\\fixture\n").Proportions![2] == LayoutProportions.Equal, "missing split field uses native 5000 default");
        Check(Parse("Q-Dir Version=12.79\nViewMode=99\nSP1=3000\nDir1=C:\\fixture\n").Proportions is null, "unknown layout does not apply ratios");

        var all = cases.ToDictionary(sample => sample.App, sample => sample.Expected);
        all[11] = new(.5, .5, new(1.23456789, 3.4, 5.6, 7.8));
        var culture = CultureInfo.CurrentCulture;
        string extended;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); extended = QdrExporter.Render(State(11), [], all); }
        finally { CultureInfo.CurrentCulture = culture; }
        var roundtrip = Parse(extended);
        Check(extended.Contains("ExplorerDirs.FormatVersion=2") && roundtrip.Proportions!.Count == 12 &&
            all.All(pair => roundtrip.Proportions[pair.Key] == pair.Value), "extension retains exact weights and all layout memories");
        Check(Parse(QdrExporter.Render(State(11), [])).ExtendedState is not null, "legacy v1 remains readable");
        var changed = extended.Replace("SP0=", "SP0=1");
        Check(Parse(changed).ExtendedState is null, "native divider edits invalidate stale extensions");
        foreach (var field in new[] { "SP0", "SP1", "SP2" })
        {
            var legacy = QdrExporter.Render(State(2), []);
            Check(Parse(legacy + field + "=3000\r\n").ExtendedState is null, "adding divider field to old envelope invalidates old hash");
        }
        foreach (var version in new[] { "1", "4" })
            Check(Parse(extended.Replace("ExplorerDirs.FormatVersion=2", "ExplorerDirs.FormatVersion=" + version)).ExtendedState is null, "mislabelled or future envelope rejected");
        var encodedLine = extended.Split("\r\n").Single(line => line.StartsWith("ExplorerDirs.State="));
        var payload = JsonNode.Parse(Convert.FromBase64String(encodedLine.Split('=', 2)[1]))!;
        payload["Proportions"]!["11"]!["Strip"]!["First"] = -1;
        var damaged = extended.Replace(encodedLine, "ExplorerDirs.State=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString())));
        Check(Parse(damaged).ExtendedState is null && Parse(damaged).Proportions is not null, "bad extension weights fall back to valid native ratios");
    }

    private static SessionState State(int layout) => new(layout, 4,
        Enumerable.Range(0, 4).Select(_ => new List<TabSettings> { new(@"C:\fixture", ViewState.Default()) }).ToList(), 0, [0, 0, 0, 0]);
    private static QdrImportResult Parse(string text) => QdrImporter.Parse(text, (value, _) => value);
    private static bool Close(LayoutProportions a, LayoutProportions b) => Math.Abs(a.Horizontal - b.Horizontal) < 1e-10 &&
        Math.Abs(a.Vertical - b.Vertical) < 1e-10 && (a.Strip is null && b.Strip is null || a.Strip is not null && b.Strip is not null &&
        a.Strip.Weights.Length == b.Strip.Weights.Length && a.Strip.Weights.Zip(b.Strip.Weights).All(pair => Math.Abs(pair.First - pair.Second) < 1e-10));
    private static void Check(bool value, string message) { if (!value) throw new Exception("QDR proportions: " + message); }
}
