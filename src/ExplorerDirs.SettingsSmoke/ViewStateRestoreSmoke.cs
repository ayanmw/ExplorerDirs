using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ViewStateRestoreSmoke
{
    internal static void Run()
    {
        var key = new PropertyKeyState(Guid.NewGuid(), 1);
        var saved = new ViewState(4, 16, [new(key, -1)], key, false, [new(key, 240)]);
        var ungroupedNative = ShellGroupingPolicy.ForNativeRestore(saved, disabled: true);
        Check(ungroupedNative.GroupKey is null && ungroupedNative.SortColumns.SequenceEqual(saved.SortColumns),
            "global suppression clears grouping only for the native restore request");
        var capturedUngrouped = saved with { GroupKey = null, GroupAscending = true };
        var preserved = ShellGroupingPolicy.PreserveCachedGrouping(capturedUngrouped, saved, disabled: true);
        Check(preserved.GroupKey == saved.GroupKey && preserved.GroupAscending == saved.GroupAscending,
            "capture during global suppression retains the tab's saved grouping intent");
        Check(ShellGroupingPolicy.PreserveCachedGrouping(capturedUngrouped, saved, disabled: false).GroupKey is null,
            "normal capture still accepts an explicitly ungrouped view");
        foreach (var step in Enum.GetValues<ShellViewRestoreStep>())
        {
            var target = new Target { FailedStep = step };
            var failures = new List<ShellViewRestoreStep>();
            var complete = ShellViewStateRestore.Restore(saved, target, (failed, _) => failures.Add(failed), () => true);
            Check(!complete && failures.SequenceEqual([step]), $"{step} failure rejects the complete restore");
            Check(target.Calls.SequenceEqual(Enum.GetValues<ShellViewRestoreStep>()),
                "other saved fields are still attempted after one native failure");
            target.FailedStep = null;
            target.Calls.Clear();
            Check(ShellViewStateRestore.Restore(saved, target, (_, _) => throw new Exception("unexpected failure"), () => true),
                "a successful full retry can release the save protection");

            target.Calls.Clear();
            target.FailedStep = step;
            var current = true;
            target.DuringWrite = called => { if (called == step) current = false; };
            complete = ShellViewStateRestore.Restore(saved, target,
                (_, _) => throw new Exception("retired restore must not report errors for the new visit"), () => current);
            Check(!complete && target.Calls[^1] == step,
                $"navigation during {step} stops writes to the retired view");
        }
        var beforeRead = new Target();
        Check(!ShellViewStateRestore.Restore(saved, beforeRead, (_, _) => { }, () => false) && beforeRead.Calls.Count == 0,
            "an obsolete restore never writes to its target");
        var plain = new Target();
        Check(ShellViewStateRestore.Restore(ViewState.Default(), plain, (_, _) => { }, () => true) &&
            plain.Calls.SequenceEqual([ShellViewRestoreStep.Mode, ShellViewRestoreStep.Grouping]),
            "default views do not invent column or sorting overrides");
        var values = new Target();
        Check(ShellViewStateRestore.Restore(saved, values, (_, _) => { }, () => true) &&
            values.Mode == (saved.Mode, ViewStyle.NativeIconSize(saved.Mode, saved.IconSize)) &&
            values.Columns!.SequenceEqual(saved.DisplayColumns!) && values.Sorts!.SequenceEqual(saved.SortColumns) &&
            values.Group == (saved.GroupKey, saved.GroupAscending),
            "mode, icon size, columns, sorting and group direction reach the target unchanged");
        foreach (var step in new[] { ShellViewRestoreStep.Mode, ShellViewRestoreStep.Sorting, ShellViewRestoreStep.Grouping })
        {
            var noColumns = saved with { DisplayColumns = null };
            Check(!ShellViewStateRestore.Restore(noColumns, new Target { FailedStep = step }, (_, _) => { }, () => true),
                $"{step} failure is protected without any custom columns");
        }
        var preset = new ViewPreset("Details", 4, 16, [new(key, 240)]);
        var modeTarget = new Target();
        Check(ShellViewStateRestore.ApplyMode(2, -1, modeTarget, _ => throw new Exception("unexpected mode error"), () => true) &&
            modeTarget.Calls.SequenceEqual([ShellViewRestoreStep.Mode]) && modeTarget.Mode == (2u, 16),
            "view-style changes normalize small icons and write no sorting, grouping or columns");
        var modeCurrent = true;
        var retiredMode = new Target { FailedStep = ShellViewRestoreStep.Mode, DuringWrite = _ => modeCurrent = false };
        Check(!ShellViewStateRestore.ApplyMode(4, -1, retiredMode,
                _ => throw new Exception("retired mode must not report errors"), () => modeCurrent),
            "a view-style setter that navigates cannot report success or an obsolete error");
        var oldMode = new Target();
        Check(!ShellViewStateRestore.ApplyMode(4, -1, oldMode, _ => { }, () => false) && oldMode.Calls.Count == 0,
            "an obsolete view-style request never calls the native setter");
        var presetTarget = new Target();
        Check(ShellViewStateRestore.ApplyPreset(preset, presetTarget, (_, _) => throw new Exception("unexpected preset failure"), () => true) &&
            presetTarget.Calls.SequenceEqual([ShellViewRestoreStep.Mode, ShellViewRestoreStep.Columns]),
            "a preset changes only mode and columns, retaining native sorting and grouping");
        foreach (var step in new[] { ShellViewRestoreStep.Mode, ShellViewRestoreStep.Columns })
        {
            var current = true;
            var retired = new Target { DuringWrite = called => { if (called == step) current = false; }, FailedStep = step };
            Check(!ShellViewStateRestore.ApplyPreset(preset, retired,
                    (_, _) => throw new Exception("obsolete preset must not publish an error"), () => current) &&
                retired.Calls[^1] == step,
                "navigation during a preset stops subsequent writes and suppresses obsolete errors");
            var failing = new Target { FailedStep = step };
            var failures = new List<ShellViewRestoreStep>();
            Check(!ShellViewStateRestore.ApplyPreset(preset, failing, (failed, _) => failures.Add(failed), () => true) &&
                failures.SequenceEqual([step]) && failing.Calls[^1] == step,
                $"current preset {step} failure stops before later stages");
        }
        var stalePreset = new Target();
        Check(!ShellViewStateRestore.ApplyPreset(preset, stalePreset, (_, _) => { }, () => false) && stalePreset.Calls.Count == 0,
            "a retired preset performs no writes");
        Console.WriteLine("View state restore smoke: PASS (fake native target; no Shell view created)");
    }

    private sealed class Target : IShellViewStateTarget
    {
        internal ShellViewRestoreStep? FailedStep;
        internal Action<ShellViewRestoreStep>? DuringWrite;
        internal List<ShellViewRestoreStep> Calls { get; } = [];
        internal (uint, int) Mode;
        internal IReadOnlyList<DisplayColumnState>? Columns;
        internal IReadOnlyList<SortColumnState>? Sorts;
        internal (PropertyKeyState?, bool) Group;
        private int Write(ShellViewRestoreStep step)
        {
            Calls.Add(step);
            DuringWrite?.Invoke(step);
            return FailedStep == step ? unchecked((int)0x80004005) : 0;
        }
        public int SetMode(uint mode, int iconSize) { Mode = (mode, iconSize); return Write(ShellViewRestoreStep.Mode); }
        public int SetColumns(IReadOnlyList<DisplayColumnState> columns) { Columns = columns; return Write(ShellViewRestoreStep.Columns); }
        public int SetSorting(IReadOnlyList<SortColumnState> columns) { Sorts = columns; return Write(ShellViewRestoreStep.Sorting); }
        public int SetGrouping(PropertyKeyState? key, bool ascending) { Group = (key, ascending); return Write(ShellViewRestoreStep.Grouping); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
