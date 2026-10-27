namespace ExplorerDirs.App;

internal enum ShellViewRestoreStep { Mode, Columns, Sorting, Grouping }

internal interface IShellViewStateTarget
{
    int SetMode(uint mode, int iconSize);
    int SetColumns(IReadOnlyList<DisplayColumnState> columns);
    int SetSorting(IReadOnlyList<SortColumnState> columns);
    int SetGrouping(PropertyKeyState? key, bool ascending);
}

internal static class ShellViewStateRestore
{
    internal static bool ApplyMode(uint mode, int iconSize, IShellViewStateTarget target,
        Action<int> failed, Func<bool> isCurrent)
    {
        if (!isCurrent()) return false;
        var result = target.SetMode(mode, ViewStyle.NativeIconSize(mode, iconSize));
        if (!isCurrent()) return false;
        if (result >= 0) return true;
        failed(result);
        return false;
    }

    internal static bool ApplyPreset(ViewPreset preset, IShellViewStateTarget target,
        Action<ShellViewRestoreStep, int> failed, Func<bool> isCurrent)
    {
        bool Apply(ShellViewRestoreStep step, Func<int> write)
        {
            if (!isCurrent()) return false;
            var result = write();
            if (!isCurrent()) return false;
            if (result < 0)
            {
                failed(step, result);
                return false;
            }
            return true;
        }
        return ApplyMode(preset.Mode, preset.IconSize, target,
                   result => failed(ShellViewRestoreStep.Mode, result), isCurrent) &&
               Apply(ShellViewRestoreStep.Columns, () => target.SetColumns(preset.DisplayColumns));
    }

    internal static bool Restore(ViewState saved, IShellViewStateTarget target,
        Action<ShellViewRestoreStep, int> failed, Func<bool> isCurrent)
    {
        var complete = true;
        bool Apply(ShellViewRestoreStep step, Func<int> write)
        {
            if (!isCurrent()) return false;
            var result = write();
            if (!isCurrent()) return false;
            if (result < 0)
            {
                complete = false;
                failed(step, result);
            }
            return isCurrent();
        }

        if (!Apply(ShellViewRestoreStep.Mode,
                () => target.SetMode(saved.Mode, ViewStyle.NativeIconSize(saved.Mode, saved.IconSize)))) return false;
        if (saved.DisplayColumns is { Count: > 0 } columns &&
            !Apply(ShellViewRestoreStep.Columns, () => target.SetColumns(columns))) return false;
        if (saved.SortColumns.Count > 0 &&
            !Apply(ShellViewRestoreStep.Sorting, () => target.SetSorting(saved.SortColumns))) return false;
        if (!Apply(ShellViewRestoreStep.Grouping,
                () => target.SetGrouping(saved.GroupKey, saved.GroupAscending))) return false;
        return complete;
    }
}
