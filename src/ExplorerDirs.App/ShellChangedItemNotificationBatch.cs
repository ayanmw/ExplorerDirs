using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed class ShellChangedItemNotificationBatch
{
    private readonly object _gate = new();
    private readonly int _maximumDistinctPaths;
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _orderedPaths = [];
    private bool _dispatchPending;
    private bool _requiresFullRefresh;
    private long _lastRecordedAt;

    internal ShellChangedItemNotificationBatch(int maximumDistinctPaths = 128)
    {
        if (maximumDistinctPaths <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDistinctPaths));
        _maximumDistinctPaths = maximumDistinctPaths;
    }

    internal bool TryRecordForDispatch(string path, out bool requiresFullRefresh)
    {
        lock (_gate)
        {
            _lastRecordedAt = Stopwatch.GetTimestamp();
            requiresFullRefresh = _requiresFullRefresh;
            if (_requiresFullRefresh) return false;
            if (!_paths.Add(path)) return false;
            if (_orderedPaths.Count == _maximumDistinctPaths)
            {
                _paths.Clear();
                _orderedPaths.Clear();
                _requiresFullRefresh = true;
                _dispatchPending = true;
                requiresFullRefresh = true;
                return false;
            }

            _orderedPaths.Add(path);
            if (_dispatchPending) return false;
            _dispatchPending = true;
            return true;
        }
    }

    internal bool TryTakeAfterQuietPeriod(TimeSpan quietPeriod, out IReadOnlyList<string>? paths,
        out bool requiresFullRefresh)
    {
        lock (_gate)
        {
            paths = null;
            requiresFullRefresh = false;
            if (!_dispatchPending || Stopwatch.GetElapsedTime(_lastRecordedAt) < quietPeriod) return false;

            requiresFullRefresh = _requiresFullRefresh;
            if (!_requiresFullRefresh) paths = _orderedPaths.ToArray();
            _paths.Clear();
            _orderedPaths.Clear();
            _requiresFullRefresh = false;
            _dispatchPending = false;
            return true;
        }
    }
}

internal static class ShellChangedItemNotification
{
    private const int MaxPath = 260;
    private const uint ShellItemUpdated = 0x00002000;
    private const uint ShellDirectoryUpdated = 0x00001000;
    private const uint PathAndFlushNoWait = 0x00002005;
    private const uint PidlAndFlushNoWait = 0x00002000;
    private static readonly Guid BasicPropertyFormat = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");

    internal static bool ShouldNotifyPath(string path) =>
        Path.IsPathFullyQualified(path) && path.Length < MaxPath;

    internal static bool RequiresDirectoryUpdate(ViewState view) =>
        view.SortColumns.Any(sort => IsChangedMetadataKey(sort.Key)) ||
        view.GroupKey is { } group && IsChangedMetadataKey(group);

    internal static void NotifyChangedItem(string path) =>
        SHChangeNotify(ShellItemUpdated, PathAndFlushNoWait, path, IntPtr.Zero);

    internal static void NotifyChangedDirectory(string path) =>
        SHChangeNotify(ShellDirectoryUpdated, PathAndFlushNoWait, path, IntPtr.Zero);

    internal static void NotifyChangedDirectory(IntPtr absolutePidl) =>
        SHChangeNotifyPidl(ShellDirectoryUpdated, PidlAndFlushNoWait, absolutePidl, IntPtr.Zero);

    private static bool IsChangedMetadataKey(PropertyKeyState key) =>
        key.FormatId == BasicPropertyFormat && key.PropertyId is 12 or 13 or 14 or 15;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    private static extern void SHChangeNotifyPidl(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
