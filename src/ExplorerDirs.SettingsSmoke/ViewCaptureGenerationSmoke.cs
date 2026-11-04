using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ViewCaptureGenerationSmoke
{
    internal static void Run()
    {
        var key = new PropertyKeyState(Guid.NewGuid(), 1);
        var cached = new ViewState(4, 16, [new(key, -1)], key, false, [new(key, 200)]) { MixedSizeSort = true };
        foreach (var changedStep in new[] { "mode", "count", "sorts", "group", "display" })
        {
            var readiness = new ShellViewReadiness();
            readiness.Begin("old");
            readiness.Complete("old");
            var generation = readiness.Generation;
            var reads = new List<string>();
            var source = new FakeShellViewStateSource
            {
                Mode = 3, IconSize = 24, Sorts = [new(key, 1)],
                ReadStep = step =>
                {
                    reads.Add(step);
                    // Model a native COM getter pumping a navigation callback.
                    if (step == changedStep) readiness.Begin("new");
                }
            };
            var captured = ShellViewStateCapture.Capture(cached, source, out var complete,
                () => generation == readiness.Generation);
            Check(!complete && captured.Mode == cached.Mode && captured.IconSize == cached.IconSize &&
                captured.SortColumns.SequenceEqual(cached.SortColumns) && captured.GroupKey == cached.GroupKey &&
                captured.GroupAscending == cached.GroupAscending && captured.DisplayColumns!.SequenceEqual(cached.DisplayColumns!),
                $"navigation during {changedStep} rejects the entire stale view snapshot");
            Check(reads[^1] == changedStep, $"capture stops native reads after {changedStep} changes the visit");
            readiness.Complete("new");
            generation = readiness.Generation;
            source.ReadStep = null;
            captured = ShellViewStateCapture.Capture(cached, source, out complete,
                () => generation == readiness.Generation);
            Check(complete && captured.Mode == 3 && captured.MixedSizeSort, "a stable subsequent visit can capture normally");
        }
        var readCount = 0;
        var staleSource = new FakeShellViewStateSource { ReadStep = _ => readCount++ };
        ShellViewStateCapture.Capture(cached, staleSource, out var staleComplete, () => false);
        Check(!staleComplete && readCount == 0, "an already obsolete capture never queries its native view");
        Console.WriteLine("View capture generation smoke: PASS (synthetic getter reentrancy; no Shell view created)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
