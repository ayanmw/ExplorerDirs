using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ExplorerDirs.App;

// Hosts the Windows Shell folder view. Its hit testing, view modes, sorting,
// grouping, file operations and context menu all belong to the Shell view.
internal sealed class ExplorerBrowserHost : HwndHost, ExplorerBrowserHost.IExplorerBrowserEvents,
    IColumnValueCaptureSource
{
    private static readonly Guid BrowserClassId = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid FolderView2Id = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    private static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private static readonly Guid ShellItemId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private const uint WmCommand = 0x0111;
    private const uint BrowseParent = 0x2000;
    private const uint BrowseBack = 0x4000;
    private const uint BrowseForward = 0x8000;
    private const uint AncestorRootOwner = 3;
    // Standard Shell view command IDs from shlobj.h; the Shell executes the operations.
    private const int ShellCut = 0x7018;
    private const int ShellCopy = 0x7019;
    private const int ShellPaste = 0x701A;
    private const int ShellUndo = 0x701B;
    private const int ShellRedo = 0x701C;
    private const int ShellSelectAll = 0x7021;
    private static readonly string? FocusTracePath = Environment.GetEnvironmentVariable("EXPLORER_DIRS_FOCUS_TRACE");
    private IExplorerBrowser? _browser;
    private bool _folderHeaderWarningReported;
    private QuickFilterSettings _quickFilterSettings = QuickFilterSettings.Empty;
    private ShellFolderFilter? _quickFilter;
    private HiddenItemsPreference _hiddenItemsPreference = HiddenItemsPreference.FollowWindows;
    private bool _selectionCheckBoxes;
    private bool? _fullRowSelection;
    private bool _disableGrouping;
    private DispatcherTimer? _quickFilterGroupRestoreTimer;
    private PendingQuickFilterGroupRestore? _pendingQuickFilterGroupRestore;
    private (PropertyKeyState Group, bool Ascending)? _quickFilterGroupToRestore;
    private long _quickFilterRequestVersion;
    private const int QuickFilterGroupRestoreTimeoutMilliseconds = 5000;
    private ShellHistoryEvents? _historyEvents;
    private bool _viewRestoreFailed;
    private IntPtr _hostWindow;
    private IntPtr _viewWindow;
    private readonly List<IntPtr> _subclassedWindows = new();
    private bool _nativeThemeRefreshQueued;
    private readonly ViewStyleWheelAccumulator _ctrlWheelAccumulator = new();
    private readonly Dictionary<uint, int> _nativeUserMessages = new();
    private readonly Dictionary<IntPtr, string> _nativeViewWindowClasses = new();
    private readonly Dictionary<(uint Message, string WindowClass), (int Samples, long HostTotalTicks, long HostMaxTicks, long ShellTotalTicks, long ShellMaxTicks)> _nativeViewMessageTimings = new();
    private readonly List<string> _nativeViewMessageSlowTimeline = new();
    private readonly DiagnosticStageTiming _directoryQueueTiming = new();
    private readonly DiagnosticStageTiming _directoryQueueDispatchTiming = new();
    private readonly DiagnosticStageTiming _directoryChangeTimerTickTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryQueueDrainTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryCountCheckTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryRefreshCallTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryRefreshGetViewTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryRefreshSelectionCaptureTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryNativeRefreshTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryRefreshCompletionTiming = new(sampleStride: 1);
    private readonly List<string> _directoryRefreshCallTimeline = new();
    private readonly DiagnosticStageTiming _directoryCreateNotificationTiming = new(sampleStride: 1);
    private readonly DiagnosticStageTiming _directoryDeleteNotificationTiming = new(sampleStride: 1);
    private readonly List<string> _nativeUserMessageSamples = new();
    private readonly List<string> _shellSelectionCallbackSamples = new();
    private readonly List<string> _shellSelectionRestoreSamples = new();
    private readonly bool _traceNativeUserMessages = Environment.GetEnvironmentVariable("EXPLORER_DIRS_SHELL_CALLBACK_TRACE") == "1";
    private long _nativeUserMessageTraceStart = Stopwatch.GetTimestamp();
    private readonly SubclassProc _subclassProc;
    private readonly DirectoryNavigationGate _navigationGate = new();
    // Shell parsing can pump STA messages. Invalidate preparation before any
    // newer request, including one still waiting for a directory probe.
    private long _navigationRequestVersion;
    private long _nativeNavigationRequestVersion;
    private long? _failedNavigationRequestVersion;
    private readonly ShellNavigationState _location;
    private readonly ShellBrowserSite _browserSite = new();
    private readonly ShellSearchHistory _searchHistory = new();
    private readonly record struct IssuedNavigationRequest(int Count, long RequestVersion);
    private readonly Dictionary<string, IssuedNavigationRequest> _issuedBrowserPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IssuedNavigationRequest> _issuedSearches = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _focusRestoreTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private FocusRestoreRequest? _pendingFileOperationFocusRestore;
    private const double FileOperationFocusRestoreWindowMilliseconds = 1500;
    private static readonly TimeSpan ShellPropertySizeRefreshInterval = TimeSpan.FromSeconds(5);
    private ShellSelectionSizeCacheKey? _selectionSizeCacheKey;
    private ulong? _selectionSizeCacheBytes;
    private long _selectionSizeCacheReadAt;
    private ShellFolderSizeCacheKey? _folderSizeCacheKey;
    private ulong? _folderSizeCacheBytes;
    private long _folderSizeCacheReadAt;
    private readonly ShellHeaderPresetMenuGate _headerPresetMenuGate = new();
    private IReadOnlyList<ShellColumnHeaderArrowTarget> _columnHeaderArrowTargets = [];
    private long _columnHeaderArrowTargetsGeneration = -1;
    private bool _columnHeaderGeometryRefreshQueued;
    private bool _columnFilterArrowMouseDown;
    private DispatcherTimer? _newFolderRenameTimer;
    private DispatcherTimer? _createdFileSelectionTimer;
    private List<IntPtr>? _createdFileSelectionPidls;
    private CreatedFileSelectionRequest? _createdFileSelectionRequest;
    private int _createdFileSelectionAttempts;
    private FolderCreationObservation? _newFolderObservation;
    private ShellNewFolderRefreshGate? _newFolderRefreshGate;
    private ShellNewFolderEditGate? _newFolderEditGate;
    private long _newFolderRequestVersion;
    private FileSystemWatcher? _directoryChangeWatcher;
    private readonly ShellDirectoryWatcherEventGate<FileSystemWatcher> _directoryChangeWatcherEventGate = new();
    private DispatcherTimer? _directoryChangeWatcherRecoveryTimer;
    private ShellDirectoryWatcherRecovery? _directoryChangeWatcherRecovery;
    private DispatcherTimer? _directoryChangeStartTimer;
    private DispatcherTimer? _directoryChangeTimer;
    private DispatcherTimer? _directoryChangeCreateNotificationTimer;
    private DispatcherTimer? _directoryChangeDeleteNotificationTimer;
    private DispatcherTimer? _directoryChangeChangedItemNotificationTimer;
    private ShellDirectoryChangeBatch? _directoryChangeBatch;
    private ShellDirectoryChangeQueue? _directoryChangeQueue;
    private ShellCreateNotificationBatch? _directoryChangeCreateNotificationBatch;
    private ShellDeleteNotificationBatch? _directoryChangeDeleteNotificationBatch;
    private ShellChangedItemNotificationBatch? _directoryChangeChangedItemNotificationBatch;
    private bool _directoryRefreshInProgress;
    private bool _directorySelectionRestorePending;
    private DispatcherTimer? _deferredRefreshSelectionRestoreTimer;
    private ShellViewSelectionSnapshot? _deferredRefreshSelectionSnapshot;
    private DispatcherTimer? _naturalUpdateSelectionVerificationTimer;
    private ShellViewSelectionSnapshot? _naturalUpdateSelectionVerificationSnapshot;
    private long _naturalUpdateSelectionVerificationStartedAt;
    private readonly ShellSelectionRecoverySuppression _selectionRecoverySuppression = new();
    private long _nativeUserInputVersion;
    private Task<string?>? _middleClickFolderProbe;
    private string? _middleClickParentPath;
    private bool _middleClickParentIsFileSystem;
    private long _middleClickNavigationVersion;
    private long _middleClickInputVersion;
    private long _settledNativeUserInputVersion;
    private DispatcherOperation? _nativeInputSelectionCapture;
    private ShellViewSelectionSnapshot? _lastNativeSelection;
    private long _lastNativeSelectionInputVersion;
    private long _lastNativeSelectionNavigationVersion;
    private string? _lastNativeSelectionPath;
    internal long NativeSelectionRestoreMilliseconds { get; private set; } = -1;
    private bool _directoryChangeRecheckPending;
    private int _directoryChangeStarting;
    private long _directoryChangeVersion;
    private long _directoryChangeLastEventAt;
    private int _directoryChangeCreatedEvents;
    private int _directoryChangeNativeCreateNotifications;
    private int _directoryChangeNativeFolderDeleteNotifications;
    private int _directoryChangeFolderDeleteLookupCount;
    private string _directoryChangeLastFolderDeleteNotificationResult = "not-attempted";
    private int _directoryChangeDeletedEvents;
    private long _directoryChangeFallbackStartedAt;
    private long _directoryChangeFallbackCompletedAt;
    private long _directoryChangeNativeCreateNotificationStartedAt;
    private long _directoryChangeNativeCreateNotificationCompletedAt;
    private string _directoryChangeLastCreateNotificationResult = "not-attempted";
    private uint _cookie;
    private bool _advised;
    private bool _hasNavigated;
    private readonly ShellViewReadiness _viewReadiness = new();
    private string? _completedDiskDirectory;
    private bool _locationUnconfirmed;
    private string? _retryPath;
    private bool _focusWhenReady;
    private bool _restoreFocusAfterNavigation;
    private IntPtr _lastContentFocusWindow;
    private ViewState _viewState;
    private ExplorerShellPane _visibleShellPane;

    public string CurrentPath => _location.DirectoryPath;
    internal long ViewGeneration => _viewReadiness.Generation;
    internal int AccumulateCtrlWheel(int delta) => _ctrlWheelAccumulator.Add(delta, _viewReadiness.Generation);
    internal bool IsFileSystemLocation => _location.IsFileSystemLocation;
    internal FileViewExportSnapshot CaptureFileViewExportSnapshot()
    {
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) throw new InvalidOperationException("当前文件视图尚未准备完成。");
        try { return ShellFileViewExportCapture.Capture(view, quickFilterActive: !_location.IsSearchActive && _quickFilterSettings.HasAnyFilter); }
        finally { Marshal.Release(view); }
    }

    internal ShellFileViewExportCapture.ColumnValueCapture BeginColumnValueCapture(PropertyKeyState key)
    {
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) throw new InvalidOperationException("当前文件视图尚未准备完成。");
        try
        {
            var capture = ShellFileViewExportCapture.BeginColumnValueCapture(view, key,
                quickFilterActive: !_location.IsSearchActive && _quickFilterSettings.HasAnyFilter);
            capture.Source = this;
            capture.Generation = _viewReadiness.Generation;
            return capture;
        }
        finally { Marshal.Release(view); }
    }

    internal bool IsCurrentColumnValueCapture(ShellFileViewExportCapture.ColumnValueCapture capture) =>
        ReferenceEquals(capture.Source, this) && capture.Generation == _viewReadiness.Generation &&
        _browser is not null && IsVisible && !_viewReadiness.Pending;

    bool IColumnValueCaptureSource.IsCurrentColumnValueCapture(
        ShellFileViewExportCapture.ColumnValueCapture capture) => IsCurrentColumnValueCapture(capture);

    internal ProgramStarterPaneContext CaptureProgramStarterPaneContext()
    {
        if (_browser is null || !IsVisible || _viewReadiness.Pending)
            throw new InvalidOperationException("当前文件视图尚未准备完成。");
        return new ProgramStarterPaneContext(CurrentPath,
            CaptureProgramStarterItemPaths(1), CaptureProgramStarterItemPaths(2));
    }

    internal IReadOnlyList<string> CaptureSelectedFileSystemPaths()
    {
        if (_browser is null || !IsVisible || _viewReadiness.Pending)
            throw new InvalidOperationException("当前文件视图尚未准备完成。");
        return CaptureProgramStarterItemPaths(1).Where(Path.IsPathFullyQualified).ToArray();
    }

    internal IReadOnlyList<string> CaptureSelectedPathsForClipboard()
    {
        if (_browser is null || !IsVisible || _viewReadiness.Pending)
            throw new InvalidOperationException("当前文件视图尚未准备完成。");
        var paths = CaptureProgramStarterItemPaths(1);
        if (paths.Any(path => !Path.IsPathFullyQualified(path)))
            throw new InvalidOperationException("所选项目包含无法转换为文件系统完整路径的 Shell 项目。");
        return paths;
    }

    internal PhotoPrintSelectionResult CapturePhotoPrintSelection()
    {
        if (_browser is null || !IsVisible || _viewReadiness.Pending)
            throw new InvalidOperationException("当前文件视图尚未准备完成。");
        var viewId = FolderViewId;
        if (_browser.GetCurrentView(ref viewId, out var viewObject) < 0)
            throw new InvalidOperationException("当前 Shell 文件视图暂不可读取。");
        try
        {
            var arrayId = ShellItemArrayId;
            if (viewObject is not IFolderView view || view.Items(1, ref arrayId, out var arrayObject) < 0 ||
                arrayObject is not IShellItemArray items)
                throw new InvalidOperationException("当前 Shell 选择暂不可读取。");
            try
            {
                if (items.GetCount(out var count) < 0 || count > int.MaxValue)
                    throw new InvalidOperationException("当前 Shell 选择暂不可读取。");
                List<PhotoPrintSelectionCandidate> candidates = new((int)count);
                for (uint index = 0; index < count; index++)
                {
                    if (items.GetItemAt(index, out var item) < 0 || item is null)
                        throw new InvalidOperationException("当前 Shell 选择快照不完整。");
                    try
                    {
                        var attributesResult = item.GetAttributes(ShellItemKind.RequestedAttributes, out var attributes);
                        var isFileSystem = attributesResult >= 0 &&
                            (attributes & 0x40000000) != 0 && (attributes & 0x00400000) == 0;
                        var path = GetShellItemPath(item, 0x80058000);
                        var displayName = path is not null
                            ? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path))
                            : GetShellItemPath(item, 0) ?? "(未知项目)";
                        candidates.Add(new PhotoPrintSelectionCandidate(displayName,
                            isFileSystem ? path : null, isFileSystem,
                            attributesResult >= 0 && (attributes & 0x20000000) != 0));
                    }
                    finally
                    {
                        if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                    }
                }
                return PhotoPrintSelection.Filter(candidates);
            }
            finally
            {
                if (Marshal.IsComObject(items)) Marshal.ReleaseComObject(items);
            }
        }
        finally
        {
            if (Marshal.IsComObject(viewObject)) Marshal.ReleaseComObject(viewObject);
        }
    }

    private IReadOnlyList<string> CaptureProgramStarterItemPaths(uint flags)
    {
        var viewId = FolderViewId;
        if (_browser is null || _browser.GetCurrentView(ref viewId, out var viewObject) < 0)
            throw new InvalidOperationException("当前 Shell 文件视图暂不可读取。");
        try
        {
            var arrayId = ShellItemArrayId;
            if (viewObject is not IFolderView view || view.Items(flags, ref arrayId, out var arrayObject) < 0 ||
                arrayObject is not IShellItemArray items)
                throw new InvalidOperationException("当前 Shell 文件项暂不可读取。");
            try
            {
                if (items.GetCount(out var count) < 0 || count > int.MaxValue)
                    throw new InvalidOperationException("当前 Shell 文件项数量暂不可读取。");
                List<string> paths = new((int)count);
                for (uint index = 0; index < count; index++)
                {
                    if (items.GetItemAt(index, out var item) < 0 || item is null)
                        throw new InvalidOperationException("当前 Shell 文件项快照不完整。");
                    try
                    {
                        var path = GetShellItemPath(item, 0x80058000) ?? GetShellItemPath(item, 0x80028000);
                        paths.Add(path ?? string.Empty);
                    }
                    finally
                    {
                        if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                    }
                }
                return paths;
            }
            finally
            {
                if (Marshal.IsComObject(items)) Marshal.ReleaseComObject(items);
            }
        }
        finally
        {
            if (Marshal.IsComObject(viewObject)) Marshal.ReleaseComObject(viewObject);
        }
    }

    private static string? GetShellItemPath(IShellItem item, uint displayNameKind)
    {
        if (item.GetDisplayName(displayNameKind, out var name) < 0 || name == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }
    internal FileViewPrintSnapshot CaptureFileViewPrintSnapshot()
    {
        var style = ReadViewStyle() ?? throw new InvalidOperationException("当前文件视图尚未准备完成。");
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) throw new InvalidOperationException("当前文件视图尚未准备完成。");
        try
        {
            return ShellFileViewExportCapture.CaptureForPrint(view,
                quickFilterActive: !_location.IsSearchActive && _quickFilterSettings.HasAnyFilter,
                mode: style.Mode, iconSize: style.IconSize);
        }
        finally { Marshal.Release(view); }
    }
    internal string NativeUserMessageSnapshot => string.Join(',', _nativeUserMessages
        .OrderBy(pair => pair.Key).Select(pair => $"{pair.Key:X}:{pair.Value}")) +
        "; samples=" + string.Join('|', _nativeUserMessageSamples);
    internal string LastShellRefreshStatus { get; private set; } = "not-started";
    internal string LastDirectoryChangeWatcherError { get; private set; } = "none";
    internal string LastQuickFilterStatus { get; private set; } = "not-started";
    internal string NativeViewMessageTimingSnapshot => string.Join('|', _nativeViewMessageTimings.OrderByDescending(pair => pair.Value.ShellMaxTicks)
        .ThenByDescending(pair => pair.Value.HostMaxTicks).Select(pair =>
            $"{pair.Key.Message:X}/{pair.Key.WindowClass}:samples={pair.Value.Samples},host-total-ms={TicksToMilliseconds(pair.Value.HostTotalTicks):F2},host-max-ms={TicksToMilliseconds(pair.Value.HostMaxTicks):F2},shell-total-ms={TicksToMilliseconds(pair.Value.ShellTotalTicks):F2},shell-max-ms={TicksToMilliseconds(pair.Value.ShellMaxTicks):F2}"));
    internal string NativeViewMessageSlowTimelineSnapshot => string.Join('|', _nativeViewMessageSlowTimeline);
    internal string DirectoryRefreshCallTimelineSnapshot => string.Join('|', _directoryRefreshCallTimeline);
    internal string DirectoryChangeStageTimingSnapshot => string.Join('|', new[]
    {
        $"watcher-queue={_directoryQueueTiming.Snapshot}",
        $"dispatcher-queue={_directoryQueueDispatchTiming.Snapshot}",
        $"directory-timer-tick={_directoryChangeTimerTickTiming.Snapshot}",
        $"directory-queue-drain={_directoryQueueDrainTiming.Snapshot}",
        $"count-check={_directoryCountCheckTiming.Snapshot}",
        $"refresh-call={_directoryRefreshCallTiming.Snapshot}",
        $"refresh-get-view={_directoryRefreshGetViewTiming.Snapshot}",
        $"refresh-selection-capture={_directoryRefreshSelectionCaptureTiming.Snapshot}",
        $"native-refresh={_directoryNativeRefreshTiming.Snapshot}",
        $"refresh-completion={_directoryRefreshCompletionTiming.Snapshot}",
        $"create-notification={_directoryCreateNotificationTiming.Snapshot}",
        $"delete-notification={_directoryDeleteNotificationTiming.Snapshot}"
    });
    internal string ShellSelectionCallbackTimelineSnapshot => string.Join('|', _shellSelectionCallbackSamples);
    internal string ShellSelectionRestoreTimelineSnapshot => string.Join('|', _shellSelectionRestoreSamples);
    internal void ResetDiagnosticTrace()
    {
        if (!_traceNativeUserMessages) return;
        _nativeUserMessages.Clear();
        _nativeViewMessageTimings.Clear();
        _nativeViewMessageSlowTimeline.Clear();
        _nativeViewWindowClasses.Clear();
        _nativeUserMessageSamples.Clear();
        _shellSelectionCallbackSamples.Clear();
        _shellSelectionRestoreSamples.Clear();
        _directoryQueueTiming.Reset();
        _directoryQueueDispatchTiming.Reset();
        _directoryChangeTimerTickTiming.Reset();
        _directoryQueueDrainTiming.Reset();
        _directoryCountCheckTiming.Reset();
        _directoryRefreshCallTiming.Reset();
        _directoryRefreshGetViewTiming.Reset();
        _directoryRefreshSelectionCaptureTiming.Reset();
        _directoryNativeRefreshTiming.Reset();
        _directoryRefreshCallTimeline.Clear();
        _directoryRefreshCompletionTiming.Reset();
        _directoryCreateNotificationTiming.Reset();
        _directoryDeleteNotificationTiming.Reset();
        _browserSite.ResetCallbackTimings();
        _nativeUserMessageTraceStart = Stopwatch.GetTimestamp();
        _directoryChangeFallbackStartedAt = 0;
        _directoryChangeFallbackCompletedAt = 0;
        _directoryChangeNativeCreateNotificationStartedAt = 0;
        _directoryChangeNativeCreateNotificationCompletedAt = 0;
    }
    public string? SearchQuery => _location.SearchQuery;
    internal string? DiskSpaceDirectory => IsVisible && !_viewReadiness.Pending && _browser is not null
        ? _completedDiskDirectory : null;
    private bool _viewCaptureFailed;
    public bool ViewCaptureFailed
    {
        get => _viewCaptureFailed || _viewRestoreFailed;
        private set => _viewCaptureFailed = value;
    }
    internal int DirectoryChangeFallbackRefreshCount { get; private set; }
    // Hidden stress diagnostics: distinguish Shell selection loss before Refresh
    // from a snapshot/restore failure inside our fallback path.
    internal int DirectoryChangeLastRefreshSelectedCount { get; private set; } = -1;
    internal bool DirectoryChangeLastRefreshHadFocus { get; private set; }
    internal long DirectoryChangeLastRefreshRestoreMilliseconds { get; private set; } = -1;
    internal int DirectoryChangeLastRefreshViewCountAtRestore { get; private set; } = -1;
    internal bool DirectoryChangeLastRefreshSelectionRestored { get; private set; }
    internal bool DirectoryChangeLastRefreshFocusRestored { get; private set; }
    internal int? DirectoryChangeLastExpectedCount { get; private set; }
    internal int? DirectoryChangeLastActualCount { get; private set; }
    internal bool DirectoryChangeLastForcedRefresh { get; private set; }
    internal string DirectoryChangeFallbackTimelineSnapshot => _directoryChangeFallbackStartedAt == 0
        ? "none"
        : $"start={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, _directoryChangeFallbackStartedAt).TotalMilliseconds:F1}ms;complete={(_directoryChangeFallbackCompletedAt == 0 ? "pending" : $"{Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, _directoryChangeFallbackCompletedAt).TotalMilliseconds:F1}ms")}";
    internal string DirectoryChangeNativeCreateNotificationTimelineSnapshot => _directoryChangeNativeCreateNotificationStartedAt == 0
        ? "none"
        : $"start={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, _directoryChangeNativeCreateNotificationStartedAt).TotalMilliseconds:F1}ms;complete={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, _directoryChangeNativeCreateNotificationCompletedAt).TotalMilliseconds:F1}ms";
    internal string DirectoryChangeLastCreateNotificationResult => _directoryChangeLastCreateNotificationResult;
    internal int? DirectoryChangeInitialCount { get; private set; }
    internal int DirectoryChangeStartSampleCount { get; private set; }
    internal int DirectoryChangeCreatedEventCount => Volatile.Read(ref _directoryChangeCreatedEvents);
    internal int DirectoryChangeNativeCreateNotificationCount => Volatile.Read(ref _directoryChangeNativeCreateNotifications);
    internal int DirectoryChangeNativeFolderDeleteNotificationCount => Volatile.Read(ref _directoryChangeNativeFolderDeleteNotifications);
    internal int DirectoryChangeFolderDeleteLookupCount => Volatile.Read(ref _directoryChangeFolderDeleteLookupCount);
    internal string DirectoryChangeLastFolderDeleteNotificationResult => _directoryChangeLastFolderDeleteNotificationResult;
    internal int DirectoryChangeDeletedEventCount => Volatile.Read(ref _directoryChangeDeletedEvents);
    public event EventHandler<ShellNavigationNotice>? Navigated;
    public event EventHandler? NavigationStarting;
    public event EventHandler? NavigationAvailabilityChanged;
    public event EventHandler<string?>? ShellStatusTextChanged;
    internal event EventHandler? SelectionContextChanged;
    internal event Action<QuickFilterSettings>? QuickFilterChanged;
    internal event Action<ShellMenuPoint>? HeaderPresetMenuRequested;
    internal event Action<PropertyKeyState, string, ShellMenuPoint>? ColumnValueFilterMenuRequested;
    internal bool CanGoBack => CanNavigateHistory(true);
    internal bool CanGoForward => CanNavigateHistory(false);
    public event EventHandler<ShellNavigationNotice>? SearchNavigated;
    public event EventHandler<ShellNavigationNotice>? NavigationFailed;
    internal bool IsCurrentNavigationRequest(long? version) => version == _navigationRequestVersion;
    internal bool CanCompleteNavigationRequest(long? version) =>
        IsCurrentNavigationRequest(version) && version != _failedNavigationRequestVersion;

    private void ReportNavigationFailure(string message, long? requestVersion)
    {
        if (IsCurrentNavigationRequest(requestVersion))
        {
            // A late success from an ambiguous same-target batch must not
            // turn an already failed submission into an input completion.
            _failedNavigationRequestVersion = requestVersion;
            _focusWhenReady = false;
            _restoreFocusAfterNavigation = false;
        }
        NavigationFailed?.Invoke(this, new(message, requestVersion));
    }
    public event EventHandler<string>? MenuError;
    internal event EventHandler<string>? FolderMiddleClicked;
    public event EventHandler? Activated;
    public Func<ExplorerShortcut, bool>? ShortcutHandler { get; set; }
    public Func<int, bool>? CtrlWheelHandler { get; set; }
    public Func<bool>? MayRestoreFocus { get; set; }
    public Func<bool>? MayContinueFileOperationFocusRestore { get; set; }
    internal QuickFilterSettings QuickFilter => _quickFilterSettings;
    internal HiddenItemsPreference HiddenItemsPreference => _hiddenItemsPreference;
    internal bool PreviewPaneVisible => _visibleShellPane == ExplorerShellPane.Preview;
    internal bool DetailsPaneVisible => _visibleShellPane == ExplorerShellPane.Details;

    public sealed record MenuAction(string Label, Action Action);
    public sealed record SelectedItem(string Path, bool IsFileSystemFolder);

    public ExplorerBrowserHost(string initialPath, ViewState initialView)
        : this(initialPath, initialView, QuickFilterSettings.Empty, false) { }

    internal ExplorerBrowserHost(string initialPath, ViewState initialView, bool showPreviewPane)
        : this(initialPath, initialView, QuickFilterSettings.Empty, showPreviewPane) { }

    internal ExplorerBrowserHost(string initialPath, ViewState initialView, QuickFilterSettings quickFilter,
        bool showPreviewPane = false, bool showDetailsPane = false,
        HiddenItemsPreference hiddenItemsPreference = HiddenItemsPreference.FollowWindows,
        bool selectionCheckBoxes = false, bool? fullRowSelection = null, bool disableGrouping = false)
    {
        if (showPreviewPane && showDetailsPane)
            throw new ArgumentException("预览窗格和详情窗格不能同时开启。", nameof(showDetailsPane));
        _location = new ShellNavigationState(initialPath);
        _viewState = initialView.Copy();
        _quickFilterSettings = quickFilter;
        _hiddenItemsPreference = hiddenItemsPreference;
        _selectionCheckBoxes = selectionCheckBoxes;
        _fullRowSelection = fullRowSelection;
        _disableGrouping = disableGrouping;
        _visibleShellPane = showDetailsPane ? ExplorerShellPane.Details :
            showPreviewPane ? ExplorerShellPane.Preview : ExplorerShellPane.None;
        _browserSite.VisibleShellPane = _visibleShellPane;
        _subclassProc = ViewWindowProc;
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueNativeThemeRefresh(); };
        _browserSite.SelectionChanged += OnShellSelectionChanged;
        _focusRestoreTimer.Tick += (_, _) =>
        {
            if (_pendingFileOperationFocusRestore is { } request)
                TryRestoreFileOperationFocus(request);
            else
                _focusRestoreTimer.Stop();
        };
    }

    internal bool SetPreviewPaneVisible(bool visible)
    {
        return SetVisibleShellPane(visible ? ExplorerShellPane.Preview :
            _visibleShellPane == ExplorerShellPane.Preview ? ExplorerShellPane.None : _visibleShellPane);
    }

    internal int SetSelectionCheckBoxes(bool enabled)
    {
        _selectionCheckBoxes = enabled;
        if (_browser is null || _viewWindow == IntPtr.Zero) return 0;
        var viewId = ShellViewId;
        var viewResult = _browser.GetCurrentView(ref viewId, out var viewObject);
        if (viewResult < 0 || viewObject is null)
        {
            return viewResult < 0 ? viewResult : unchecked((int)0x80004005);
        }
        try
        {
            var view = Marshal.GetIUnknownForObject(viewObject);
            try { return ShellFolderViewOptions.SetCheckSelect(view, enabled); }
            finally { Marshal.Release(view); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    internal void RefreshTheme()
    {
        NativeShellTheme.Apply(_hostWindow);
        var browser = _browser;
        if (browser is null) return;
        var id = ShellViewId;
        if (browser.GetCurrentView(ref id, out var viewObject) < 0 || viewObject is null) return;
        try
        {
            if (!ReferenceEquals(browser, _browser)) return;
            var view = Marshal.GetIUnknownForObject(viewObject);
            try { NativeShellTheme.ApplyView(view); }
            finally { Marshal.Release(view); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    internal int SetFullRowSelection(bool? enabled)
    {
        _fullRowSelection = enabled;
        if (enabled is not { } value || _browser is null || _viewWindow == IntPtr.Zero) return 0;
        var viewId = ShellViewId;
        var viewResult = _browser.GetCurrentView(ref viewId, out var viewObject);
        if (viewResult < 0 || viewObject is null)
            return viewResult < 0 ? viewResult : unchecked((int)0x80004005);
        try
        {
            var view = Marshal.GetIUnknownForObject(viewObject);
            try { return ShellFolderViewOptions.SetFullRowSelect(view, value); }
            finally { Marshal.Release(view); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    internal int SetGroupingDisabled(bool disabled)
    {
        if (_disableGrouping == disabled) return 0;
        var browser = _browser;
        if (browser is null || _viewWindow == IntPtr.Zero)
        {
            _disableGrouping = disabled;
            return 0;
        }

        if (disabled)
        {
            try { CaptureViewState(); }
            catch (Exception) { return unchecked((int)0x80004005); }
            if (ViewCaptureFailed) return unchecked((int)0x80004005);
            CancelPendingQuickFilterGroupRestore();
        }

        var generation = _viewReadiness.Generation;
        bool Current() => ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation;
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return unchecked((int)0x80004005);
        try
        {
            var target = new NativeViewStateTarget(view, Current);
            if (disabled)
            {
                var result = target.SetGrouping(null, ascending: false);
                if (result < 0 || !Current()) return result < 0 ? result : unchecked((int)0x80004005);
                result = ShellFolderViewOptions.SetNoGrouping(view, disabled: true);
                if (result < 0)
                {
                    if (Current()) _ = target.SetGrouping(_viewState.GroupKey, _viewState.GroupAscending);
                    return result;
                }
            }
            else
            {
                var result = ShellFolderViewOptions.SetNoGrouping(view, disabled: false);
                if (result < 0 || !Current()) return result < 0 ? result : unchecked((int)0x80004005);
                result = target.SetGrouping(_viewState.GroupKey, _viewState.GroupAscending);
                if (result < 0)
                {
                    if (Current()) _ = ShellFolderViewOptions.SetNoGrouping(view, disabled: true);
                    return result;
                }
            }
            if (!Current()) return unchecked((int)0x80004005);
            _disableGrouping = disabled;
            return 0;
        }
        finally { Marshal.Release(view); }
    }

    internal bool SetDetailsPaneVisible(bool visible)
    {
        return SetVisibleShellPane(visible ? ExplorerShellPane.Details :
            _visibleShellPane == ExplorerShellPane.Details ? ExplorerShellPane.None : _visibleShellPane);
    }

    private bool SetVisibleShellPane(ExplorerShellPane pane)
    {
        if (_visibleShellPane == pane) return true;
        var previous = _visibleShellPane;
        _browserSite.VisibleShellPane = pane;
        if (_browser is not null)
        {
            var options = 0x40u | 0x80u | (pane != ExplorerShellPane.None ? 0x2u : 0u); // EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE | EBO_SHOWFRAMES
            var hr = _browser.SetOptions(options);
            if (hr < 0)
            {
                _browserSite.VisibleShellPane = previous;
                return false;
            }
        }
        _visibleShellPane = pane;
        return true;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // Child/visible plus child/sibling clipping: the blank backdrop must not paint over Shell HWNDs.
        _hostWindow = CreateWindowEx(0, "STATIC", "", 0x56000000,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hostWindow == IntPtr.Zero)
            throw new InvalidOperationException("无法创建 Shell 视图宿主窗口。");

        try
        {
            // COM activation may pump messages before Initialize; theme the
            // visible native container before entering that initialization.
            NativeShellTheme.PrepareHost(_hostWindow);
            var browserType = Type.GetTypeFromCLSID(BrowserClassId, true)!;
            _browser = (IExplorerBrowser)Activator.CreateInstance(browserType)!;
            var browserUnknown = Marshal.GetIUnknownForObject(_browser);
            try
            {
                _historyEvents = ShellHistoryEvents.Subscribe(browserUnknown, QueueNavigationAvailability,
                    QueueShellStatusTextChanged);
                ReportFolderHeaderFailure(ShellFolderViewOptions.EnableCustomPosition(browserUnknown));
            }
            finally { Marshal.Release(browserUnknown); }
            ShellBrowserSite.SetSite(_browser, _browserSite);
            var rect = ClientRect();
            var settings = new FolderSettings { ViewMode = _viewState.Mode, Flags = 0 };
            Check(_browser.Initialize(_hostWindow, ref rect, ref settings));
            Check(_browser.SetOptions(0x40 | 0x80 | (_visibleShellPane != ExplorerShellPane.None ? 0x2u : 0u))); // EBO_NOBORDER | EBO_NOPERSISTVIEWSTATE | EBO_SHOWFRAMES
            Check(_browser.Advise(this, out _cookie));
            _advised = true;
            UpdateQuickFilterAttachment(searchActive: false);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_browser is null) return;
                try { Navigate(CurrentPath); }
                catch (Exception ex) { ReportNavigationFailure(ex.Message, _navigationRequestVersion); }
            }));
        }
        catch
        {
            DisposeBrowser();
            DestroyWindow(_hostWindow);
            _hostWindow = IntPtr.Zero;
            throw;
        }

        return new HandleRef(this, _hostWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // WPF parks/reparents an existing HWND when tabs move between panes;
        // it does not rebuild it. Always clean up here when the host is disposed.
        CancelCreatedFileSelection();
        CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: false);
        DisposeBrowser();
        DestroyWindow(hwnd.Handle);
        _hostWindow = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_browser is not null)
        {
            var rect = ClientRect();
            _browser.SetRect(IntPtr.Zero, ref rect);
        }
    }

    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
    {
        if (FocusTracePath is not null && msg.message is 0x0100 or 0x0104 && IsTraceKey(msg.wParam.ToInt64()))
            Trace($"translate key={msg.wParam.ToInt64():X} mods={modifiers} focus={FocusClass()} hwnd={GetFocus()} item={FolderFocusState()}");
        if (_browser is null || _viewWindow == IntPtr.Zero ||
            (msg.message != 0x0100 && msg.message != 0x0104))
            return base.TranslateAcceleratorCore(ref msg, modifiers);

        var focused = GetFocus();
        if (focused == IntPtr.Zero || (focused != _viewWindow && !IsChild(_viewWindow, focused)))
            return base.TranslateAcceleratorCore(ref msg, modifiers);
        var focusedClass = new StringBuilder(128);
        GetClassName(focused, focusedClass, focusedClass.Capacity);
        if (focusedClass.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
        {
            // F2 + arrows escaped the native editor: returning false lets WPF
            // navigate to a neighbouring HwndHost and ends the rename. Home/End
            // and paging keys need the same protection. Dispatch once to the
            // focused editor, retaining Ctrl/Shift text selection.
            if (ShellRenameKeyRouting.ShouldForwardNavigationKeyDown(
                    msg.message, unchecked((int)msg.wParam.ToInt64())))
            {
                SendMessage(focused, (uint)msg.message, msg.wParam, msg.lParam);
                Trace($"rename-navigation-forward key={msg.wParam.ToInt64():X} mods={modifiers} " +
                    $"host={_hostWindow} view={_viewWindow} editor={focused}/{focusedClass} focusAfter={GetFocus()}");
                return true;
            }
            return base.TranslateAcceleratorCore(ref msg, modifiers);
        }

        var inputBrowser = _browser;
        var inputView = _viewWindow;
        var inputVersion = _viewReadiness.NavigationVersion;
        bool SameVisit() => ReferenceEquals(inputBrowser, _browser) && inputView != 0 &&
            inputView == _viewWindow && inputVersion == _viewReadiness.NavigationVersion;
        bool SameInputTarget() => SameVisit() && GetFocus() == focused;

        if (msg.message == 0x0100 || msg.message == 0x0104)
        {
            var shortcut = KeyboardShortcuts.Resolve(unchecked((int)msg.wParam.ToInt64()), (int)modifiers);
            if (shortcut != ExplorerShortcut.None && ShortcutHandler?.Invoke(shortcut) == true)
                return true;
        }

        if (inputBrowser is null || !SameInputTarget()) return true;
        var iid = ShellViewId;
        if (inputBrowser.GetCurrentView(ref iid, out var viewObject) < 0)
            return SameInputTarget() ? base.TranslateAcceleratorCore(ref msg, modifiers) : true;
        int shellResult;
        try
        {
            if (!SameInputTarget()) return true;
            var nativeMessage = new NativeMessage
            {
                Hwnd = msg.hwnd,
                Message = msg.message,
                WParam = msg.wParam,
                LParam = msg.lParam,
                Time = (uint)msg.time,
                PointX = msg.pt_x,
                PointY = msg.pt_y
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMessage>());
            try
            {
                Marshal.StructureToPtr(nativeMessage, pointer, false);
                shellResult = ((IShellView)viewObject).TranslateAccelerator(pointer);
                if (FocusTracePath is not null && IsTraceKey(msg.wParam.ToInt64()))
                    Trace($"shell result={shellResult:X8} focus={FocusClass()} item={FolderFocusState()}");
                if (shellResult == 0)
                {
                    // A handled operation may legitimately move native focus.
                    // Keep its existing restore path, but never target a new visit.
                    if (SameVisit()) QueueFocusRestoreAfterFileKey(msg, modifiers);
                    return true;
                }
                if (!SameInputTarget()) return true;
                if (ShellNavigationKeyRouting.ShouldForward(msg.message,
                        unchecked((int)msg.wParam.ToInt64()), shellResult,
                        IsFileContentInputWindow(focused), IsColumnHeaderInputWindow(focused),
                        focusedClass.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase)))
                {
                    // When Shell declines an arrow, Home/End or paging key,
                    // HwndHost's base route can treat it as WPF focus navigation.
                    // Keep it in the focused native file-content control.
                    SendMessage(focused, (uint)msg.message, msg.wParam, msg.lParam);
                    Trace($"untranslated-navigation-forward key={msg.wParam.ToInt64():X} focus={FocusClass()} hwnd={focused}");
                    return true;
                }
                if (ShellAutoColumnWidthRouting.ShouldDispatchToListView(
                        msg.message, unchecked((int)msg.wParam.ToInt64()), (int)modifiers, shellResult))
                {
                    IntPtr listView = IntPtr.Zero;
                    EnumChildWindows(_viewWindow, (child, _) =>
                    {
                        var className = new StringBuilder(32);
                        GetClassName(child, className, className.Capacity);
                        if (className.ToString() != "SysListView32") return true;
                        listView = child;
                        return false;
                    }, IntPtr.Zero);
                    if (listView != IntPtr.Zero)
                    {
                        SendMessage(listView, (uint)msg.message, msg.wParam, msg.lParam);
                        Trace($"auto-fit-columns forwarded to native ListView; focus={FocusClass()}");
                        return true;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
        // COM translation/release can pump a navigation or focus change. An old
        // key must not issue a file command against the newly focused directory.
        if (!SameInputTarget()) return true;
        if (msg.message == 0x0100)
        {
            var command = ShellFileKeyboardActions.ResolveFallback(msg.message,
                unchecked((int)msg.wParam.ToInt64()), (int)modifiers, shellResult) switch
            {
                ShellFileKeyboardAction.SelectAll => ShellSelectAll,
                ShellFileKeyboardAction.Copy => ShellCopy,
                ShellFileKeyboardAction.Paste => ShellPaste,
                ShellFileKeyboardAction.Cut => ShellCut,
                ShellFileKeyboardAction.Undo => ShellUndo,
                ShellFileKeyboardAction.Redo => ShellRedo,
                _ => 0
            };
            if (command != 0)
            {
                SendMessage(_viewWindow, WmCommand, (IntPtr)command, IntPtr.Zero);
                Trace($"fallback command={command:X} focus={FocusClass()}");
                if (SameVisit()) QueueFocusRestore();
                return true;
            }
        }
        if (shellResult == 1) QueueFocusRestoreAfterFileKey(msg, modifiers);
        return base.TranslateAcceleratorCore(ref msg, modifiers);
    }

    protected override bool TabIntoCore(TraversalRequest request) => FocusView();

    public bool FocusView()
    {
        if (!IsVisible) return false;
        if (_browser is null || _viewWindow == IntPtr.Zero)
        {
            _focusWhenReady = true;
            return true;
        }

        var target = FindContentFocusWindow();
        if (target == IntPtr.Zero) return false;
        Trace($"focus-view before={FocusClass()} target={target}");
        // An explicit focus while navigation is pending can still target the old
        // view. Keep that intent until the destination view has completed.
        _focusWhenReady = _viewReadiness.Pending;
        var focused = GetFocus();
        if (focused != IntPtr.Zero && IsChild(_viewWindow, focused) &&
            WindowClass(focused).Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return true; // Shell rename field owns its editing keys and focus.
        var focusWithinContentWindow = focused != IntPtr.Zero &&
            (focused == target || IsChild(target, focused));
        if (ShellFocusRestoreGate.ShouldFocusContentWindow(focusWithinContentWindow,
                IsColumnHeaderFocused()))
            SetFocus(target);
        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) >= 0)
        {
            try { ((IShellView)viewObject).UIActivate(2); } // SVUIA_ACTIVATE_FOCUS
            finally { Marshal.ReleaseComObject(viewObject); }
        }
        FocusFolderItem();
        focused = GetFocus();
        Trace($"focus-view after={FocusClass()} hwnd={focused}");
        return focused == target || IsChild(target, focused);
    }

    public bool HasNativeFocus()
    {
        var focused = GetFocus();
        return focused != IntPtr.Zero && _viewWindow != IntPtr.Zero &&
            (focused == _viewWindow || IsChild(_viewWindow, focused));
    }

    public bool IsRenameEditorFocused()
    {
        var focused = GetFocus();
        return focused != IntPtr.Zero && _viewWindow != IntPtr.Zero &&
            IsChild(_viewWindow, focused) &&
            WindowClass(focused).Contains("Edit", StringComparison.OrdinalIgnoreCase);
    }

    internal bool ContainsNativeWindow(nint hwnd) => hwnd != 0 &&
        ((_hostWindow != 0 && (hwnd == _hostWindow || IsChild(_hostWindow, hwnd))) ||
         (_viewWindow != 0 && (hwnd == _viewWindow || IsChild(_viewWindow, hwnd))));

    public bool HasColumnHeader() => FindNativeColumnHeader() != 0 || FindColumnHeader() is not null;

    public bool IsColumnHeaderFocused()
    {
        if (!HasNativeFocus()) return false;
        var nativeHeader = FindNativeColumnHeader();
        if (nativeHeader != 0 && GetFocus() == nativeHeader) return true;
        try
        {
            var element = AutomationElement.FocusedElement;
            return element?.Current.ControlType == ControlType.SplitButton &&
                TreeWalker.ControlViewWalker.GetParent(element)?.Current.ControlType == ControlType.Header;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public bool FocusColumnHeader()
    {
        if (!IsVisible) return false;
        var nativeHeader = FindNativeColumnHeader();
        if (nativeHeader != 0) return FocusNativeColumnHeader(nativeHeader);
        var header = FindColumnHeader();
        if (header is null) return false;
        try
        {
            header.SetFocus();
            return IsColumnHeaderFocused();
        }
        catch (ElementNotAvailableException) { return false; }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private nint FindNativeColumnHeader()
    {
        var content = FindContentFocusWindow();
        if (content == 0 || WindowClass(content) != "SysListView32") return 0;
        var header = SendMessage(content, 0x101F, 0, 0); // LVM_GETHEADER
        return header != 0 && IsWindowVisible(header) ? header : 0;
    }

    private static bool FocusNativeColumnHeader(nint header)
    {
        if (header == 0 || !IsWindow(header)) return false;
        // Visible Shell headers can report IsKeyboardFocusable=false through UIA.
        // Use the native Header contract instead of silently skipping this Tab stop.
        var item = SendMessage(header, 0x121B, 0, 0); // HDM_GETFOCUSEDITEM
        if (item.ToInt64() < 0) item = 0;
        SetFocus(header);
        return SendMessage(header, 0x121C, 0, item) != 0 && GetFocus() == header; // HDM_SETFOCUSEDITEM
    }

    private AutomationElement? FindColumnHeader()
    {
        var content = FindContentFocusWindow();
        if (content == IntPtr.Zero) return null;
        try
        {
            var root = AutomationElement.FromHandle(content);
            var group = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            return group?.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton));
        }
        catch (ElementNotAvailableException) { return null; }
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private IReadOnlyList<ShellColumnHeaderArrowTarget> CaptureColumnHeaderArrowTargets()
    {
        var content = FindContentFocusWindow();
        if (content == 0) return [];
        try
        {
            var root = AutomationElement.FromHandle(content);
            var header = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            var splitButtons = header?.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton));
            if (splitButtons is null || splitButtons.Count == 0) return [];

            var view = GetFolderView2Pointer();
            if (view == 0) return [];
            try
            {
                var result = ShellColumnManager.CaptureForExport(view, out var columns);
                if (result != 0 || columns is null || columns.Count != splitButtons.Count) return [];
                var targets = new ShellColumnHeaderArrowTarget[splitButtons.Count];
                for (var index = 0; index < splitButtons.Count; index++)
                {
                    var element = splitButtons[index];
                    var bounds = element.Current.BoundingRectangle;
                    if (element.Current.IsOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                        return [];
                    targets[index] = new ShellColumnHeaderArrowTarget(bounds, columns[index].Key, columns[index].Name);
                }
                return targets;
            }
            finally { Marshal.Release(view); }
        }
        catch (ElementNotAvailableException) { return []; }
        catch (COMException) { return []; }
        catch (InvalidOperationException) { return []; }
    }

    private void QueueColumnHeaderGeometryRefresh()
    {
        _columnHeaderArrowTargets = [];
        _columnHeaderArrowTargetsGeneration = -1;
        if (_columnHeaderGeometryRefreshQueued || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        _columnHeaderGeometryRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _columnHeaderGeometryRefreshQueued = false;
            if (_browser is null || !IsVisible || _viewReadiness.Pending) return;
            _columnHeaderArrowTargets = CaptureColumnHeaderArrowTargets();
            _columnHeaderArrowTargetsGeneration = _viewReadiness.Generation;
        }));
    }

    private bool TryGetColumnFilterArrowHit(IntPtr hwnd, IntPtr lParam,
        out ShellColumnHeaderArrowTarget target, out ShellMenuPoint screenPoint)
    {
        target = default;
        screenPoint = default;
        if (!IsVisible || _columnHeaderArrowTargetsGeneration != _viewReadiness.Generation ||
            _columnHeaderArrowTargets.Count == 0)
            return false;

        var point = new NativePoint
        {
            X = unchecked((short)(lParam.ToInt64() & 0xFFFF)),
            Y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF))
        };
        if (!ClientToScreen(hwnd, ref point) ||
            !ShellColumnHeaderArrowHitTest.TryFind(_columnHeaderArrowTargets,
                new System.Windows.Point(point.X, point.Y), out target))
            return false;
        screenPoint = new ShellMenuPoint(point.X, point.Y);
        return true;
    }

    private bool IsColumnHeaderPoint(IntPtr hwnd, IntPtr lParam)
    {
        var point = new NativePoint
        {
            X = unchecked((short)(lParam.ToInt64() & 0xFFFF)),
            Y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF))
        };
        return ClientToScreen(hwnd, ref point) &&
            _columnHeaderArrowTargets.Any(target => target.Bounds.Contains(point.X, point.Y));
    }

    private void FocusFolderItem()
    {
        if (_browser is null) return;
        var view = GetFolderView2Pointer();
        if (view == 0) return;
        try
        {
            // UIA can retain a ListItem focus while IFolderView has no focused index.
            // Repair from Shell's focused/marked item and preserve its selected bit.
            // Selection-menu testing exposed that unconditionally selecting the
            // target changes Select None/Invert; RestoreFocusedItem retains its actual state.
            var focusedIsSelected = ShellSelectionCommands.RestoreFocusedItem(view);
            // Only the sole selected item can be the same focused item in UIA.
            if (focusedIsSelected && ReadItemCounts(includeSelectionSize: false)?.Selected == 1)
                FocusSelectedAutomationItem();
            if (FocusTracePath is not null)
                Trace($"focus-item selected={focusedIsSelected} foreground={GetForegroundWindow()} focus={FocusClass()} item={FolderFocusState()}");
        }
        catch (COMException) { } // A failed focus repair must not change the selection.
        finally { Marshal.Release(view); }
    }

    private void FocusSelectedAutomationItem()
    {
        var content = FindContentFocusWindow();
        if (content == IntPtr.Zero) return;
        try
        {
            var root = AutomationElement.FromHandle(content);
            var selected = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true)));
            selected?.SetFocus();
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
    }

    private IntPtr FindContentFocusWindow()
    {
        if (_viewWindow == IntPtr.Zero) return IntPtr.Zero;
        if (_lastContentFocusWindow != IntPtr.Zero && IsWindow(_lastContentFocusWindow) &&
            IsChild(_viewWindow, _lastContentFocusWindow))
            return _lastContentFocusWindow;

        IntPtr content = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        EnumChildWindows(_viewWindow, (child, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() == "DirectUIHWND") content = child;
            else if (name.ToString() == "SysListView32") list = child;
            return true;
        }, IntPtr.Zero);
        return content != IntPtr.Zero ? content : list != IntPtr.Zero ? list : _viewWindow;
    }

    private void QueueFocusRestoreAfterFileKey(MSG msg, ModifierKeys modifiers)
    {
        if (ShellFileKeyboardActions.Resolve(unchecked((int)msg.wParam.ToInt64()), (int)modifiers) != ShellFileKeyboardAction.None ||
            (modifiers == ModifierKeys.Shift && msg.wParam.ToInt64() == 0x2E))
            QueueFocusRestore();
    }

    private void QueueFocusRestore()
    {
        var browser = _browser;
        if (browser is null || _viewWindow == IntPtr.Zero) return;
        var request = new FocusRestoreRequest(browser, CurrentPath, _viewReadiness.NavigationVersion,
            Interlocked.Read(ref _nativeUserInputVersion), Stopwatch.GetTimestamp());
        _pendingFileOperationFocusRestore = request;
        _focusRestoreTimer.Stop();
        _focusRestoreTimer.Interval = TimeSpan.FromMilliseconds(250);
        _focusRestoreTimer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => TryRestoreFileOperationFocus(request)));
    }

    private void TryRestoreFileOperationFocus(FocusRestoreRequest request)
    {
        if (!ReferenceEquals(_pendingFileOperationFocusRestore, request)) return;
        var window = Window.GetWindow(this);
        var focusStillDesired =
            (MayContinueFileOperationFocusRestore?.Invoke() ?? MayRestoreFocus?.Invoke() == true) && IsVisible;
        var windowActive = window?.IsActive == true;
        var retryWindowStart = ShellFocusRestoreGate.ResolveRetryWindowStartTimestamp(
            request.StartedAt, Stopwatch.GetTimestamp(), windowActive);
        if (retryWindowStart != request.StartedAt)
        {
            request = request with { StartedAt = retryWindowStart };
            _pendingFileOperationFocusRestore = request;
        }
        var sameView = ReferenceEquals(request.Browser, _browser) &&
            request.NavigationVersion == _viewReadiness.NavigationVersion &&
            string.Equals(request.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
        var userInputUnchanged = request.InputVersion == Interlocked.Read(ref _nativeUserInputVersion);
        var withinRetryWindow = Stopwatch.GetElapsedTime(request.StartedAt).TotalMilliseconds <
            FileOperationFocusRestoreWindowMilliseconds;
        var canContinue = ShellFocusRestoreGate.CanContinue(focusStillDesired, sameView,
            userInputUnchanged, withinRetryWindow);
        if (!canContinue)
        {
            CompleteFileOperationFocusRestore(request);
            return;
        }

        var shellViewActivationAttempted = false;
        if (ShellFocusRestoreGate.ShouldActivateShellViewForOperationRestore(canContinue, windowActive,
                request.ShellActivationAttempted, IsRenameEditorFocused()))
        {
            // The content HWND and focused item can look correct while the Shell view is not UI-activated;
            // a user's Alt+Tab round trip reactivates it, so do that once after file operations too.
            request = request with { ShellActivationAttempted = true };
            _pendingFileOperationFocusRestore = request;
            shellViewActivationAttempted = true;
            if (FocusTracePath is not null) Trace("focus-restore shell-view-activation-attempt");
            RestoreContentFocusIfNeeded();
        }

        var fileContentFocused = windowActive && IsFileContentFocused();
        var shouldAttemptRestore = ShellFocusRestoreGate.ShouldAttemptRestore(canContinue,
            windowActive, fileContentFocused);
        if (FocusTracePath is not null)
            Trace($"focus-restore-poll desired={focusStillDesired} window-active={windowActive} same-view={sameView} input-unchanged={userInputUnchanged} " +
                $"within-window={withinRetryWindow} content-focused={fileContentFocused} attempt={shouldAttemptRestore} " +
                $"elapsed-ms={Stopwatch.GetElapsedTime(request.StartedAt).TotalMilliseconds:F0}");
        if (shouldAttemptRestore && !shellViewActivationAttempted)
            RestoreContentFocusIfNeeded();

        if (ReferenceEquals(_pendingFileOperationFocusRestore, request))
            _focusRestoreTimer.Interval = TimeSpan.FromMilliseconds(100);
    }

    private bool IsFileContentFocused()
    {
        var focused = GetFocus();
        var content = FindContentFocusWindow();
        var hasNativeFocus = focused != IntPtr.Zero && _viewWindow != IntPtr.Zero &&
            (focused == _viewWindow || IsChild(_viewWindow, focused));
        var focusWithinContentWindow = focused != IntPtr.Zero && content != IntPtr.Zero &&
            (focused == content || IsChild(content, focused));
        var columnHeaderFocused = hasNativeFocus && IsColumnHeaderFocused();
        var renameEditorFocused = hasNativeFocus && IsRenameEditorFocused();
        // The native rename editor is already the desired keyboard target. Avoid
        // a reentrant Shell focus query while the user is editing its text.
        var (folderHasItems, folderItemFocused) = renameEditorFocused
            ? (true, false)
            : ReadFolderItemFocusState();
        return ShellFocusRestoreGate.IsFileContentFocused(hasNativeFocus, focusWithinContentWindow,
            columnHeaderFocused, folderHasItems, folderItemFocused, renameEditorFocused);
    }

    private (bool HasItems, bool ItemFocused) ReadFolderItemFocusState()
    {
        var browser = _browser;
        var iid = FolderViewId;
        if (browser is null) return (true, false);
        object? viewObject = null;
        try
        {
            if (browser.GetCurrentView(ref iid, out viewObject) < 0)
                return (true, false);
            if (viewObject is not IFolderView view)
                return (true, false);
            var itemCount = ShellItemCounts.ReadAllViewTotal(scope =>
            {
                var result = view.ItemCount(scope, out var count);
                return (result, count);
            });
            if (itemCount is null) return (true, false);
            if (itemCount == 0) return (false, false);
            var result = view.GetFocusedItem(out var focusedIndex);
            return (true, result >= 0 && (uint)focusedIndex < (uint)itemCount.Value);
        }
        catch (COMException) { return (true, false); }
        finally
        {
            if (viewObject is not null) Marshal.ReleaseComObject(viewObject);
        }
    }

    private void CompleteFileOperationFocusRestore(FocusRestoreRequest request)
    {
        if (!ReferenceEquals(_pendingFileOperationFocusRestore, request)) return;
        _pendingFileOperationFocusRestore = null;
        _focusRestoreTimer.Stop();
    }

    private sealed record FocusRestoreRequest(object Browser, string Path, long NavigationVersion,
        long InputVersion, long StartedAt, bool ShellActivationAttempted = false);

    private sealed record PendingQuickFilterGroupRestore(PropertyKeyState Group, bool Ascending,
        int InitialItemCount, ShellFolderFilter? Filter, string Path, long NavigationVersion, long RequestVersion, long StartedAt);

    private void RestoreContentFocusIfNeeded()
    {
        var window = Window.GetWindow(this);
        var mayRestore = MayRestoreFocus?.Invoke() == true;
        var windowActive = window?.IsActive == true;
        var viewVisible = IsVisible;
        var viewReady = _viewWindow != IntPtr.Zero;
        if (!mayRestore || !windowActive || !viewVisible || !viewReady)
        {
            Trace($"restore-skip may-restore={mayRestore} window-active={windowActive} view-visible={viewVisible} view-ready={viewReady} focus={FocusClass()} foreground={GetForegroundWindow()}");
            return;
        }
        var foreground = GetForegroundWindow();
        var windowHandle = new WindowInteropHelper(window).Handle;
        var foregroundOwnedByWindow = foreground != IntPtr.Zero &&
            GetAncestor(foreground, AncestorRootOwner) == windowHandle;
        // A stale WPF IsActive value must not let a delayed Shell operation steal
        // focus from another app or a different Explorer Dirs window.
        if (!ShellFocusRestoreGate.CanRestoreForeground(foreground == IntPtr.Zero, foregroundOwnedByWindow))
        {
            Trace($"restore-skip foreground-owned-by-window={foregroundOwnedByWindow} foreground={foreground}");
            return;
        }
        // Shell file-operation windows can close with no foreground HWND while
        // WPF still regards this window as active. Reassert only in that gap.
        if (foreground == IntPtr.Zero)
            SetForegroundWindow(windowHandle);
        var focused = GetFocus();
        var target = FindContentFocusWindow();
        var targetReady = target != IntPtr.Zero;
        var columnHeaderFocused = targetReady && IsColumnHeaderFocused();
        var focusEligible = targetReady && (focused == target || IsChild(target, focused) || focused == IntPtr.Zero ||
            focused == _viewWindow || !IsChild(_viewWindow, focused) || columnHeaderFocused);
        Trace($"restore-check focus={FocusClass()} target={target} foreground={GetForegroundWindow()} column-header={columnHeaderFocused} focus-eligible={focusEligible}");
        if (targetReady && focusEligible)
        {
            var restored = FocusView();
            Trace($"restore-result restored={restored} focus={FocusClass()} hwnd={GetFocus()} target={FindContentFocusWindow()}");
        }
        else Trace($"restore-skip target-ready={targetReady} column-header={columnHeaderFocused} focus-eligible={focusEligible}");
    }

    public bool GoParent()
    {
        if (!_location.IsSearchActive) return BrowseRelative(BrowseParent);
        Navigate(CurrentPath); // The directory is the parent of its virtual search results.
        return true;
    }
    public bool GoBack() => BrowseRelative(BrowseBack);
    public bool GoForward() => BrowseRelative(BrowseForward);

    public bool CreateNewFolder()
    {
        var browser = _browser;
        if (browser is null) return false;
        CancelPendingNewFolderRename();
        var requestVersion = _newFolderRequestVersion;
        var navigationVersion = _viewReadiness.NavigationVersion;
        var userInputVersion = Interlocked.Read(ref _nativeUserInputVersion);
        bool IsCurrent() => ReferenceEquals(browser, _browser) && browser is not null &&
            requestVersion == _newFolderRequestVersion &&
            _viewReadiness.IsCurrent(navigationVersion) && !_locationUnconfirmed &&
            IsVisible && MayRestoreFocus?.Invoke() == true && Window.GetWindow(this)?.IsActive == true &&
            userInputVersion == Interlocked.Read(ref _nativeUserInputVersion);
        if (!IsCurrent()) return false;
        var parentPath = CurrentPath;
        var observation = FolderCreationObservation.TryStart(parentPath);
        try
        {
            if (!ShellNewFolderRefreshGate.TryFocusForCommand(IsCurrent, FocusView)) return false;
            // Shell extension menu construction can pump messages. The shared
            // command path checks native availability and IsCurrent immediately
            // before invocation, including after lazy New submenu initialization.
            var invokeResult = ShellFileCommands.Invoke(ShellFileCommand.NewFolder, OpenFileCommandMenu, IsCurrent);
            if (invokeResult is null)
            {
                if (IsCurrent()) MenuError?.Invoke(this, "当前位置不支持新建文件夹。");
                return false;
            }
            Trace($"new-folder invoke={invokeResult.Value:X8} focus={FolderFocusState()}");
            Check(invokeResult.Value);
            if (observation is not null && IsCurrent())
            {
                BeginRenameNewFolder(parentPath, observation, navigationVersion, userInputVersion);
                observation = null; // The timer owns and disposes the observation.
            }
            return true;
        }
        catch (Exception ex)
        {
            Trace($"new-folder exception={ex.GetType().Name}:{ex.Message}");
            if (IsCurrent()) MenuError?.Invoke(this, $"新建文件夹失败：{ex.Message}");
            return false;
        }
        finally
        {
            observation?.Dispose();
        }
    }

    internal bool ApplySelectionCommand(ShellSelectionCommand command)
    {
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        var selectionVersion = _browserSite.SelectionVersion;
        bool SameView() => browser is not null && ReferenceEquals(browser, _browser) &&
            generation == _viewReadiness.Generation && !_viewReadiness.Pending && !_locationUnconfirmed &&
            IsVisible && MayRestoreFocus?.Invoke() == true && Window.GetWindow(this)?.IsActive == true &&
            !IsRenameEditorFocused();
        bool Current() => SameView() && selectionVersion == _browserSite.SelectionVersion;
        if (!Current()) return false;
        if (command == ShellSelectionCommand.All)
        {
            var inputVersion = Interlocked.Read(ref _nativeUserInputVersion);
            if (FocusView() && SameView() && inputVersion == Interlocked.Read(ref _nativeUserInputVersion))
            {
                // FocusView can reassert the existing focused PIDL and produce a selection notification;
                // refresh the version only after confirming no newer user input arrived during focus routing.
                selectionVersion = _browserSite.SelectionVersion;
                NoteNativeUserInput();
                if (TrySelectAllViaNativeAccelerator())
                {
                    if (SameView()) QueueFocusRestore();
                    return true;
                }
            }
        }
        nint view = 0;
        try
        {
            view = GetFolderView2Pointer();
            if (view == 0 || !ShellSelectionCommands.Apply(view, command, Current, SameView)) return false;
            // Apply may pump messages; never restore focus to a tab left meanwhile.
            if (SameView()) QueueFocusRestore();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            if (SameView()) MenuError?.Invoke(this, "选择操作未能完成，请检查当前选区后重试。");
            return false;
        }
        finally { if (view != 0) Marshal.Release(view); }
    }

    internal bool TrySelectAllViaNativeAccelerator()
    {
        if (_browser is null || _viewWindow == IntPtr.Zero) return false;
        var focused = GetFocus();
        if (focused == IntPtr.Zero || (focused != _viewWindow && !IsChild(_viewWindow, focused))) return false;

        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState)) return false;
        var originalKeyboardState = keyboardState.ToArray();
        keyboardState[0x11] |= 0x80; // Ctrl is scoped to this UI thread and restored immediately after Shell translation.
        if (!SetKeyboardState(keyboardState)) return false;
        try
        {
            var iid = ShellViewId;
            var result = _browser.GetCurrentView(ref iid, out var viewObject);
            try
            {
                if (result < 0 || viewObject is null) return false;
                var message = new NativeMessage
                {
                    Hwnd = focused,
                    Message = 0x0100,
                    WParam = (IntPtr)0x41,
                    LParam = (IntPtr)1
                };
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMessage>());
                try
                {
                    Marshal.StructureToPtr(message, pointer, false);
                    return ((IShellView)viewObject).TranslateAccelerator(pointer) == 0;
                }
                finally { Marshal.FreeHGlobal(pointer); }
            }
            finally { if (viewObject is not null) Marshal.ReleaseComObject(viewObject); }
        }
        finally
        {
            if (!SetKeyboardState(originalKeyboardState))
                throw new InvalidOperationException("Could not restore the UI thread keyboard state after Shell Select All.");
        }
    }

    internal Dictionary<ShellFileCommand, bool> QueryFileCommands()
    {
        var generation = _viewReadiness.Generation;
        var selectionVersion = _browserSite.SelectionVersion;
        try
        {
            return ShellFileCommands.Query(OpenFileCommandMenu, () =>
                generation == _viewReadiness.Generation && selectionVersion == _browserSite.SelectionVersion &&
                IsVisible && !_viewReadiness.Pending && _browser is not null);
        }
        catch (Exception)
        {
            return ShellFileCommands.All.ToDictionary(info => info.Command, _ => false);
        }
    }

    internal bool InvokeFileCommand(ShellFileCommand command)
    {
        if (command == ShellFileCommand.NewFolder) return CreateNewFolder();
        try
        {
            var generation = _viewReadiness.Generation;
            var selectionVersion = _browserSite.SelectionVersion;
            var result = ShellFileCommands.Invoke(command, OpenFileCommandMenu, () =>
                generation == _viewReadiness.Generation && selectionVersion == _browserSite.SelectionVersion &&
                IsVisible && !_viewReadiness.Pending && MayRestoreFocus?.Invoke() == true &&
                Window.GetWindow(this)?.IsActive == true);
            if (result is null)
            {
                MenuError?.Invoke(this, "当前选区或位置不支持此命令。");
                return false;
            }
            Check(result.Value);
            QueueFocusRestore();
            return true; // Accepted by Shell; not a file-operation completion report.
        }
        catch (Exception ex)
        {
            MenuError?.Invoke(this, $"Shell 命令未能执行：{ex.Message}");
            return false;
        }
    }

    internal bool NotifyCreatedClipboardFile(string path)
    {
        if (_browser is null || !_location.IsFileSystemLocation || _location.IsSearchActive || _viewReadiness.Pending ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? string.Empty),
                Path.TrimEndingDirectorySeparator(CurrentPath), StringComparison.OrdinalIgnoreCase)) return false;

        CancelCreatedFileSelection();
        var request = CaptureFileSystemSelectionRequest();
        if (SHParseDisplayName(path, IntPtr.Zero, out var absolutePidl, 0, out _) < 0)
        {
            if (absolutePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(absolutePidl);
            ShellCreatedItemNotification.NotifyCreatedFile(path);
            return false;
        }
        try
        {
            if (!ShellCreatedItemNotification.NotifyCreatedFile(path, absolutePidl)) return false;
            // Shell calls can pump messages. Do not replace a newer selection request created reentrantly.
            if (_createdFileSelectionRequest is not null || !IsCreatedFileSelectionRequestCurrent(request)) return true;
            return QueueFileSystemSelection(absolutePidl, request);
        }
        finally
        {
            if (absolutePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(absolutePidl);
        }
    }

    internal bool QueueFileSystemSelection(string path)
    {
        return QueueFileSystemSelections([path]);
    }

    internal bool QueueFileSystemSelections(IReadOnlyList<string> paths)
    {
        if (_browser is null || !_location.IsFileSystemLocation || _location.IsSearchActive ||
            paths.Count == 0) return false;
        List<IntPtr> pidls = [];
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(path) ||
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? string.Empty),
                    Path.TrimEndingDirectorySeparator(CurrentPath), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
                return false;
            }
            if (SHParseDisplayName(path, IntPtr.Zero, out var absolutePidl, 0, out _) < 0)
            {
                if (absolutePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(absolutePidl);
                foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
                return false;
            }
            pidls.Add(absolutePidl);
        }
        try
        {
            CancelCreatedFileSelection();
            return QueueFileSystemSelections(pidls, CaptureFileSystemSelectionRequest());
        }
        finally { foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl); }
    }

    private CreatedFileSelectionRequest CaptureFileSystemSelectionRequest() =>
        new(_browser!, CurrentPath, _viewReadiness.NavigationVersion,
            _viewReadiness.Generation, _browserSite.SelectionVersion, _nativeUserInputVersion);

    private bool QueueFileSystemSelection(IntPtr absolutePidl, CreatedFileSelectionRequest request)
        => QueueFileSystemSelections([absolutePidl], request);

    private bool QueueFileSystemSelections(IReadOnlyList<IntPtr> absolutePidls,
        CreatedFileSelectionRequest request)
    {
        // Shell calls can pump messages. Keep the request tied to its original tab state.
        if (absolutePidls.Count == 0 || !IsCreatedFileSelectionRequestCurrent(request)) return false;
        List<IntPtr> clones = new(absolutePidls.Count);
        foreach (var absolutePidl in absolutePidls)
        {
            var clone = ILClone(absolutePidl);
            if (clone == IntPtr.Zero)
            {
                foreach (var pidl in clones) Marshal.FreeCoTaskMem(pidl);
                return false;
            }
            clones.Add(clone);
        }
        _createdFileSelectionPidls = clones;
        _createdFileSelectionRequest = request;
        _createdFileSelectionAttempts = 0;
        _createdFileSelectionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _createdFileSelectionTimer.Tick += CreatedFileSelectionTimer_Tick;
        _createdFileSelectionTimer.Start();
        return true;
    }

    private void CreatedFileSelectionTimer_Tick(object? sender, EventArgs e)
    {
        var request = _createdFileSelectionRequest;
        var pidls = _createdFileSelectionPidls;
        if (request is null || pidls is not { Count: > 0 } ||
            !IsCreatedFileSelectionRequestCurrent(request) || !IsVisible ||
            Window.GetWindow(this)?.IsActive != true || MayRestoreFocus?.Invoke() != true ||
            IsRenameEditorFocused())
        {
            CancelCreatedFileSelection(request);
            return;
        }

        _createdFileSelectionAttempts++;
        if (!_viewReadiness.Pending)
        {
            var iid = ShellViewId;
            if (_browser!.GetCurrentView(ref iid, out var viewObject) >= 0)
            {
                try
                {
                    if (!IsCreatedFileSelectionRequestCurrent(request))
                    {
                        CancelCreatedFileSelection(request);
                        return;
                    }
                    var batch = ApplyFileSystemSelectionBatch((IShellView)viewObject, pidls, () =>
                    {
                        return ReferenceEquals(_createdFileSelectionRequest, request) &&
                        IsCreatedFileSelectionLocationCurrent(request) &&
                        request.InputVersion == _nativeUserInputVersion && MayRestoreFocus?.Invoke() == true &&
                        Window.GetWindow(this)?.IsActive == true;
                    });
                    if (batch.Complete)
                    {
                        var stillOwnsRequest = ReferenceEquals(_createdFileSelectionRequest, request);
                        CancelCreatedFileSelection(request);
                        if (stillOwnsRequest && IsCreatedFileSelectionLocationCurrent(request) &&
                            request.InputVersion == _nativeUserInputVersion && MayRestoreFocus?.Invoke() == true &&
                            Window.GetWindow(this)?.IsActive == true)
                            FocusView();
                        return;
                    }
                    // Do not retry a partially applied batch after our own selection notifications change the
                    // Shell selection version; a failed first item remains eligible for the normal retry path.
                    if (batch.AppliedAny) CancelCreatedFileSelection(request);
                }
                finally { Marshal.ReleaseComObject(viewObject); }
            }
        }

        // A reentrant create may have replaced this request while Shell handled SelectItem.
        if (!ReferenceEquals(_createdFileSelectionRequest, request)) return;

        if (_createdFileSelectionAttempts >= 20) CancelCreatedFileSelection(request);
    }

    private static (bool Complete, bool AppliedAny) ApplyFileSystemSelectionBatch(IShellView shellView,
        IReadOnlyList<IntPtr> absolutePidls, Func<bool> mayContinue)
    {
        var appliedAny = false;
        for (var index = 0; index < absolutePidls.Count; index++)
        {
            if (!mayContinue()) return (false, appliedAny);
            // First select clears prior selection; remaining items join the batch.
            var childPidl = ILClone(ILFindLastID(absolutePidls[index]));
            if (childPidl == IntPtr.Zero) return (false, appliedAny);
            int result;
            try { result = shellView.SelectItem(childPidl, index == 0 ? 0x1du : 0x9u); }
            finally { Marshal.FreeCoTaskMem(childPidl); }
            if (result < 0) return (false, appliedAny);
            appliedAny = true;
            if (!mayContinue()) return (false, true);
        }
        return (true, appliedAny);
    }

    private bool IsCreatedFileSelectionRequestCurrent(CreatedFileSelectionRequest request) =>
        request.IsCurrent(_browser, CurrentPath, _location.IsFileSystemLocation, _location.IsSearchActive,
            _viewReadiness.Pending, _viewReadiness.NavigationVersion, _viewReadiness.Generation,
            _browserSite.SelectionVersion, _nativeUserInputVersion);

    private bool IsCreatedFileSelectionLocationCurrent(CreatedFileSelectionRequest request) =>
        request.IsLocationCurrent(_browser, CurrentPath, _location.IsFileSystemLocation, _location.IsSearchActive,
            _viewReadiness.Pending, _viewReadiness.NavigationVersion, _viewReadiness.Generation);

    private void CancelCreatedFileSelection(CreatedFileSelectionRequest? expected = null)
    {
        if (expected is not null && !ReferenceEquals(_createdFileSelectionRequest, expected)) return;
        if (_createdFileSelectionTimer is not null)
        {
            _createdFileSelectionTimer.Stop();
            _createdFileSelectionTimer.Tick -= CreatedFileSelectionTimer_Tick;
            _createdFileSelectionTimer = null;
        }
        if (_createdFileSelectionPidls is { } pidls)
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            _createdFileSelectionPidls = null;
        }
        _createdFileSelectionRequest = null;
        _createdFileSelectionAttempts = 0;
    }

    private IShellCommandMenu? OpenFileCommandMenu(bool background)
    {
        if (!IsVisible || _viewReadiness.Pending || _browser is null || _viewWindow == 0)
            return null;
        // Menu availability needs a selection count, not aggregated file metadata.
        // Keep selection-size property queries on the status-bar read path.
        if (!background && ReadItemCounts(includeSelectionSize: false) is not { Selected: > 0 }) return null;
        var viewId = ShellViewId;
        if (_browser.GetCurrentView(ref viewId, out var viewObject) < 0) return null;
        try
        {
            var contextId = ContextMenuId;
            var hr = ((IShellView)viewObject).GetItemObject(background ? 0u : 1u, ref contextId, out var context);
            if (hr < 0)
            {
                if (context is not null) Marshal.ReleaseComObject(context);
                return null;
            }
            return new NativeShellCommandMenu(context,
                Window.GetWindow(this) is { } window ? new WindowInteropHelper(window).Handle : _hostWindow);
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private void BeginRenameNewFolder(string parentPath, FolderCreationObservation observation, long navigationVersion,
        long userInputVersion)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _newFolderObservation = observation;
        _newFolderRefreshGate = new ShellNewFolderRefreshGate();
        _newFolderEditGate = new ShellNewFolderEditGate();
        _newFolderRenameTimer = timer;
        var attempts = 0;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_newFolderObservation, observation)) return;
            var finished = TryRenameNewFolder(parentPath, observation, navigationVersion, userInputVersion);
            // A nested new-folder command can replace this observer during COM.
            // The old callback must not cancel or report for the new operation.
            if (!ReferenceEquals(_newFolderObservation, observation)) return;
            if (finished)
            {
                CancelPendingNewFolderRename();
                return;
            }
            if (++attempts < 12) return;
            MenuError?.Invoke(this, "未能确认新建文件夹的 Shell 焦点；请检查创建结果并手动重命名。");
            CancelPendingNewFolderRename();
        };
        timer.Start();
    }

    private bool TryRenameNewFolder(string parentPath, FolderCreationObservation observation, long navigationVersion,
        long userInputVersion)
    {
        var browser = _browser;
        if (browser is null) return true;
        bool IsCurrent() => ReferenceEquals(browser, _browser) && CurrentPath == parentPath &&
            ReferenceEquals(_newFolderObservation, observation) &&
            _viewReadiness.IsCurrent(navigationVersion) && !_locationUnconfirmed &&
            IsVisible && MayRestoreFocus?.Invoke() == true && Window.GetWindow(this)?.IsActive == true &&
            userInputVersion == Interlocked.Read(ref _nativeUserInputVersion);
        if (!IsCurrent()) return true;
        var renameEditorFocused = IsRenameEditorFocused();
        if (_newFolderEditGate?.IsComplete(renameEditorFocused) == true) return true;
        if (_newFolderEditGate?.ShouldWaitForEditor(renameEditorFocused) == true) return false;
        if (renameEditorFocused) return true;
        try
        {
            var snapshot = observation.Snapshot();
            if (snapshot.Failed)
            {
                MenuError?.Invoke(this, "目录监听失效；若文件夹已创建，请手动重命名。");
                return true;
            }
            if (snapshot.Directories.Count == 0) return false;
            if (snapshot.Directories.Count > 1)
            {
                MenuError?.Invoke(this, "同时创建了多个文件夹，未自动进入重命名。请手动选择目标文件夹。");
                return true;
            }
            var created = snapshot.Directories[0];
            var selectionVersion = _browserSite.SelectionVersion;
            var focusedPath = GetFocusedItemPath(parentPath);
            if (!IsCurrent()) return true;
            Trace($"new-folder focused-match={string.Equals(focusedPath, created, StringComparison.OrdinalIgnoreCase)}");
            if (!string.Equals(focusedPath, created, StringComparison.OrdinalIgnoreCase))
            {
                if (_newFolderRefreshGate?.TryRequestRefresh(snapshot, focusedPath, IsCurrent(),
                        userInputVersion == Interlocked.Read(ref _nativeUserInputVersion)) == true)
                {
                    // The directory watcher starts after the Shell count settles, so a fast
                    // NewFolder can precede it; refresh once only after the dedicated observer
                    // confirms one directory and only while the user has not acted again.
                    Trace("new-folder refreshing stale Shell view after observed creation");
                    Refresh();
                    // Wait one cycle for enumeration; a later attempt must select the observed PIDL
                    // itself because some Shell command paths leave the old item focused.
                    return false;
                }
            }
            Check(SHParseDisplayName(created, IntPtr.Zero, out var absolutePidl, 0, out _));
            try
            {
                if (!IsCurrent()) return true;
                var childPidl = ILFindLastID(absolutePidl);
                var iid = ShellViewId;
                if (browser.GetCurrentView(ref iid, out var viewObject) < 0) return false;
                try
                {
                    if (!IsCurrent() || selectionVersion != _browserSite.SelectionVersion ||
                        IsRenameEditorFocused()) return true;
                    var result = ((IShellView)viewObject).SelectItem(childPidl, 0x1f);
                    Trace($"new-folder edit={result:X8}");
                    if (result < 0) return false;
                    _newFolderEditGate?.MarkEditAccepted();
                    return _newFolderEditGate?.IsComplete(IsRenameEditorFocused()) ?? false;
                }
                finally { Marshal.ReleaseComObject(viewObject); }
            }
            finally { Marshal.FreeCoTaskMem(absolutePidl); }
        }
        catch (Exception ex)
        {
            if (IsCurrent()) MenuError?.Invoke(this, $"新建文件夹后无法进入重命名：{ex.Message}");
            return true;
        }
    }

    private void CancelPendingNewFolderRename()
    {
        _newFolderRequestVersion++;
        _newFolderRenameTimer?.Stop();
        _newFolderRenameTimer = null;
        _newFolderObservation?.Dispose();
        _newFolderObservation = null;
        _newFolderRefreshGate = null;
        _newFolderEditGate = null;
    }

    private string? GetFocusedItemPath(string parentPath)
    {
        if (_browser is null) return null;
        var iid = FolderViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return null;
        try
        {
            var view = (IFolderView)viewObject;
            if (view.GetFocusedItem(out var index) < 0 || index < 0 ||
                view.Item(index, out var childPidl) < 0 || childPidl == IntPtr.Zero)
                return null;
            try
            {
                if (SHParseDisplayName(parentPath, IntPtr.Zero, out var parentPidl, 0, out _) < 0)
                    return null;
                try
                {
                    var absolutePidl = ILCombine(parentPidl, childPidl);
                    if (absolutePidl == IntPtr.Zero) return null;
                    try
                    {
                        if (SHGetNameFromIDList(absolutePidl, 0x80058000, out var pathPtr) < 0)
                            return null;
                        try { return Marshal.PtrToStringUni(pathPtr); }
                        finally { Marshal.FreeCoTaskMem(pathPtr); }
                    }
                    finally { Marshal.FreeCoTaskMem(absolutePidl); }
                }
                finally { Marshal.FreeCoTaskMem(parentPidl); }
            }
            finally { Marshal.FreeCoTaskMem(childPidl); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private bool BrowseRelative(uint flag)
    {
        if (_browser is null) return false;
        if (flag == BrowseBack && !CanGoBack || flag == BrowseForward && !CanGoForward) return false;
        _navigationRequestVersion++;
        var hr = _browser.BrowseToIDList(IntPtr.Zero, flag);
        if (hr < 0 && hr != unchecked((int)0x80004005))
            ReportNavigationFailure($"目录导航失败：0x{hr:X8}", _navigationRequestVersion);
        return hr >= 0;
    }

    private bool CanNavigateHistory(bool back) => _browser is not null &&
        (_historyEvents?.CanNavigate(back, _viewReadiness.Pending, _locationUnconfirmed) ??
         (!_viewReadiness.Pending && !_locationUnconfirmed));

    private void QueueNavigationAvailability()
    {
        var owner = _historyEvents;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            // Native notifications carry no request identity. Read current
            // readiness and tab state at delivery, never a captured old value.
            if (_browser is not null && ReferenceEquals(_historyEvents, owner))
                NavigationAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void QueueShellStatusTextChanged(string? text)
    {
        var owner = _historyEvents;
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_browser is not null && ReferenceEquals(_historyEvents, owner))
                ShellStatusTextChanged?.Invoke(this, text);
        }));
    }

    internal string? ShellStatusText => _historyEvents?.StatusText;

    public void Navigate(string path)
    {
        _navigationRequestVersion++;
        _navigationGate.Cancel();
        path = AddressInputResolver.Resolve(path,
            _location.IsFileSystemLocation ? _location.DirectoryPath : null);
        if (string.IsNullOrWhiteSpace(path))
        {
            Refresh();
            return;
        }
        if (!ShellLocationIdentity.TryNormalize(path, out path, out var isFileSystemPath))
        {
            ReportNavigationFailure("路径不是可识别的文件系统位置或受支持的 Shell 文件夹。", _navigationRequestVersion);
            return;
        }
        if (_browser is null)
        {
            _location.CompleteDirectory(path, isFileSystemPath);
            _retryPath = path;
            return;
        }

        _retryPath = path;
        _ = NavigateWhenAvailableAsync(path, isFileSystemPath);
    }

    public void Search(string query, string? scopePath = null) => _ = SearchWhenAvailableAsync(query, scopePath);

    private async Task SearchWhenAvailableAsync(string query, string? scopePath)
    {
        var request = ++_navigationRequestVersion;
        _navigationGate.Cancel();
        if (!_location.CanSearchCurrentLocation)
        {
            ReportNavigationFailure("当前 Shell 位置暂不支持按目录搜索。", request);
            return;
        }
        var browser = _browser;
        if (_browser is null)
        {
            ReportNavigationFailure("搜索视图尚未就绪。", request);
            return;
        }
        bool IsCurrentRequest() => request == _navigationRequestVersion && ReferenceEquals(browser, _browser);
        try
        {
            var uri = ShellSearchLocation.BuildUri(scopePath ?? CurrentPath, query);
            var pidl = ShellSearchLocation.ParsePidl(uri);
            try
            {
                if (!IsCurrentRequest()) return;
                _searchHistory.Remember(pidl, uri);
                if (!IsCurrentRequest())
                {
                    // Remember may pump Shell callbacks; a superseded request not yet submitted to BrowseToIDList is not TravelLog history.
                    _searchHistory.Forget(pidl, uri);
                    return;
                }
                _retryPath = null;
                _focusWhenReady = true;
                TrackIssuedSearch(uri);
                try
                {
                    var result = await BrowseWhenAvailableAsync(browser!, pidl, IsCurrentRequest);
                    if (result is null || result.Value < 0)
                        _searchHistory.Forget(pidl, uri);
                    if (result is null)
                    {
                        if (ReferenceEquals(browser, _browser)) CompleteIssuedSearch(uri);
                        return;
                    }
                    Check(result.Value);
                }
                catch
                {
                    if (ReferenceEquals(browser, _browser)) CompleteIssuedSearch(uri);
                    _searchHistory.Forget(pidl, uri);
                    throw;
                }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        catch (Exception ex)
        {
            if (!IsCurrentRequest()) return;
            _focusWhenReady = false;
            ReportNavigationFailure($"无法搜索指定范围：{ex.Message}", request);
        }
    }

    private async Task NavigateWhenAvailableAsync(string path, bool isFileSystemPath)
    {
        var request = _navigationRequestVersion;
        var probe = await _navigationGate.ProbeAsync(path,
            _ => isFileSystemPath ? DirectoryProbe.Check(path) : DirectoryProbeStatus.Unavailable);
        if (!_navigationGate.IsCurrent(probe) || _browser is null) return;
        nint pidl = 0;
        try
        {
            if (probe.CanNavigate)
                Check(SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _));
            else if (!ShellLocationIdentity.TryGetFolderPidl(path, out pidl))
            {
                if (_navigationGate.IsCurrent(probe) && _browser is not null)
                    ReportNavigationFailure("目录不存在、不可访问或设备离线；可点击刷新重试。", request);
                return;
            }

            // Inputs such as shell:Downloads can resolve to a filesystem path,
            // while virtual folders have no filesystem path at all. Register
            // the same confirmed identity used by navigation callbacks.
            var issuedPath = ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var canonicalPath, out _)
                ? canonicalPath : path;
            // A newer request may cancel this probe while Shell parses or
            // reads the folder identity; do not register stale preparation.
            if (!_navigationGate.IsCurrent(probe) || _browser is null) return;
            TrackIssuedPath(issuedPath);
            int? result;
            var browser = _browser;
            try
            {
                result = await BrowseWhenAvailableAsync(browser, pidl,
                    () => _navigationGate.IsCurrent(probe) && ReferenceEquals(browser, _browser));
            }
            catch
            {
                if (ReferenceEquals(browser, _browser)) CompleteIssuedPath(issuedPath);
                throw;
            }
            if ((result is null || result < 0) && ReferenceEquals(browser, _browser)) CompleteIssuedPath(issuedPath);
            if (result is not null) Check(result.Value);
        }
        catch (Exception ex)
        {
            if (_navigationGate.IsCurrent(probe) && _browser is not null)
                ReportNavigationFailure($"无法打开目录：{ex.Message}", request);
        }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }

    // ExplorerBrowser rejects a second Browse while a search is starting with
    // ERROR_BUSY. Yield the STA pump and retain only the still-current request;
    // this retries navigation acceptance, never refreshes directory contents.
    private static async Task<int?> BrowseWhenAvailableAsync(IExplorerBrowser browser, IntPtr pidl, Func<bool> isCurrent)
    {
        const int busy = unchecked((int)0x800700AA);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (isCurrent())
        {
            var result = browser.BrowseToIDList(pidl, 0);
            if (result != busy || clock.ElapsedMilliseconds >= 5000) return result;
            await Task.Delay(50);
        }
        return null;
    }

    public ViewState CaptureViewState()
    {
        try { return CaptureViewStateCore(); }
        catch (Exception)
        {
            ViewCaptureFailed = true;
            throw;
        }
    }

    private ViewState CaptureViewStateCore()
    {
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        bool IsCurrentCapture() => ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation;
        if (_locationUnconfirmed)
        {
            ViewCaptureFailed = true;
            return _viewState.Copy();
        }
        if (_location.IsSearchActive) return _viewState.Copy();
        if (_viewRestoreFailed)
        {
            ViewCaptureFailed = true;
            return _viewState.Copy();
        }
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero)
        {
            if (_hasNavigated || !IsCurrentCapture()) ViewCaptureFailed = true;
            return _viewState.Copy();
        }
        try
        {
            var captured = ShellViewStateCapture.Capture(_viewState,
                new NativeViewStateSource(view), out var complete, IsCurrentCapture);
            ViewCaptureFailed = !complete;
            if (complete)
            {
                captured = PreserveQuickFilterGroup(captured);
                captured = ShellGroupingPolicy.PreserveCachedGrouping(captured, _viewState, _disableGrouping);
                _viewState = captured;
            }
            return _viewState.Copy();
        }
        finally { Marshal.Release(view); }
    }

    public ViewState CachedViewState() => _viewState.Copy();

    internal ShellItemCounts? ReadItemCounts(bool includeSelectionSize = true, bool includeFolderSize = false)
        => ReadItemCountsCore(includeSelectionSize, includeFolderSize, requireVisible: true);

    private int? ReadDirectoryMonitorItemCount() =>
        ReadItemCountsCore(includeSelectionSize: false, includeFolderSize: false, requireVisible: false)?.Total;

    private ShellItemCounts? ReadItemCountsCore(bool includeSelectionSize, bool includeFolderSize, bool requireVisible)
    {
        if ((requireVisible && !IsVisible) || _viewReadiness.Pending || _browser is null || _viewWindow == IntPtr.Zero)
            return null;
        object? viewObject = null;
        var generation = _viewReadiness.Generation;
        var selectionVersion = _browserSite.SelectionVersion;
        try
        {
            var iid = FolderViewId;
            if (_browser.GetCurrentView(ref iid, out viewObject) != 0 || viewObject is not IFolderView view)
                return null;
            var counts = ShellItemCounts.Read(flags =>
            {
                var result = view.ItemCount(flags, out var count);
                return (result, count);
            });
            ShellSelectionSizeCacheKey? sizeKey = null;
            ulong? selectionBytes = null;
            var readSelectionSize = false;
            if (includeSelectionSize && counts is { Selected: > 0 } selectedCounts)
            {
                sizeKey = new ShellSelectionSizeCacheKey(generation, selectionVersion, selectedCounts.Selected);
                var age = _selectionSizeCacheKey == sizeKey
                    ? Stopwatch.GetElapsedTime(_selectionSizeCacheReadAt)
                    : Timeout.InfiniteTimeSpan;
                readSelectionSize = ShellSelectionSizeRefreshGate.ShouldRead(_selectionSizeCacheKey,
                    sizeKey.Value, age, ShellPropertySizeRefreshInterval);
                selectionBytes = readSelectionSize
                    ? ReadSelectionSize(view, selectedCounts.Selected)
                    : _selectionSizeCacheBytes;
            }
            ShellFolderSizeCacheKey? folderSizeKey = null;
            ulong? folderBytes = null;
            var readFolderSize = false;
            if (includeFolderSize && counts is { Selected: 0, Total: > 0 } folderCounts)
            {
                folderSizeKey = new ShellFolderSizeCacheKey(generation, folderCounts.Total);
                var age = _folderSizeCacheKey == folderSizeKey
                    ? Stopwatch.GetElapsedTime(_folderSizeCacheReadAt)
                    : Timeout.InfiniteTimeSpan;
                readFolderSize = _folderSizeCacheKey != folderSizeKey || age >= ShellPropertySizeRefreshInterval;
                folderBytes = readFolderSize
                    ? ReadItemArraySize(view, 2, folderCounts.Total)
                    : _folderSizeCacheBytes;
            }
            // A Shell COM call may pump messages and complete another navigation.
            if (generation != _viewReadiness.Generation || _viewReadiness.Pending ||
                selectionVersion != _browserSite.SelectionVersion) return null;
            if (includeSelectionSize)
            {
                if (sizeKey is { } key && counts is not null)
                {
                    if (readSelectionSize)
                    {
                        _selectionSizeCacheKey = key;
                        _selectionSizeCacheBytes = selectionBytes;
                        _selectionSizeCacheReadAt = Stopwatch.GetTimestamp();
                    }
                    counts = counts with { KnownSelectionBytes = selectionBytes };
                }
                else
                {
                    _selectionSizeCacheKey = null;
                    _selectionSizeCacheBytes = null;
                    _selectionSizeCacheReadAt = 0;
                }
            }
            if (folderSizeKey is { } folderKey && counts is not null)
            {
                if (readFolderSize)
                {
                    _folderSizeCacheKey = folderKey;
                    _folderSizeCacheBytes = folderBytes;
                    _folderSizeCacheReadAt = Stopwatch.GetTimestamp();
                }
                counts = counts with { KnownFolderBytes = folderBytes };
            }
            return counts;
        }
        catch (COMException) { return null; }
        catch (InvalidComObjectException) { return null; }
        finally { if (viewObject is not null) Marshal.ReleaseComObject(viewObject); }
    }

    private static ulong? ReadSelectionSize(IFolderView view, int expectedCount) =>
        ReadItemArraySize(view, 1, expectedCount);

    private static ulong? ReadItemArraySize(IFolderView view, uint scope, int expectedCount)
    {
        object? array = null;
        nint unknown = 0, items = 0;
        try
        {
            var id = ShellItemArrayId;
            if (view.Items(scope, ref id, out array) != 0 || array is null) return null;
            unknown = Marshal.GetIUnknownForObject(array);
            if (Marshal.QueryInterface(unknown, in id, out items) != 0) return null;
            return ShellSelectionSize.Read(items, expectedCount);
        }
        catch (COMException) { return null; }
        catch (InvalidComObjectException) { return null; }
        finally
        {
            if (items != 0) Marshal.Release(items);
            if (unknown != 0) Marshal.Release(unknown);
            if (array is not null) Marshal.ReleaseComObject(array);
        }
    }

    private readonly record struct ShellFolderSizeCacheKey(long ViewGeneration, int ItemCount);

    internal (uint Mode, int IconSize)? ReadViewStyle()
    {
        if (!IsVisible || _viewReadiness.Pending || _browser is null || IsRenameEditorFocused())
            return null;
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        var view = IntPtr.Zero;
        try
        {
            view = GetFolderView2Pointer();
            // Getting the COM view itself may pump a navigation callback.
            if (view == IntPtr.Zero || !ReferenceEquals(browser, _browser) ||
                generation != _viewReadiness.Generation) return null;
            var result = GetMethod<GetViewModeAndIconSizeDelegate>(view, 36)(view, out var mode, out var size);
            return result == 0 && mode is >= 1 and <= 8 && size > 0 &&
                ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation && !_viewReadiness.Pending && IsVisible
                ? (mode, size) : null;
        }
        catch (COMException) { return null; }
        catch (InvalidComObjectException) { return null; }
        finally { if (view != IntPtr.Zero) Marshal.Release(view); }
    }

    public bool SetViewStyle(ViewStyle style)
    {
        if (!IsVisible || _viewReadiness.Pending) return false;
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        bool Current() => ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation;
        var view = IntPtr.Zero;
        try
        {
            view = GetFolderView2Pointer();
            if (view == IntPtr.Zero)
            {
                if (Current()) MenuError?.Invoke(this, "文件视图尚未就绪，未切换查看方式。");
                return false;
            }
            // Change only mode/size: do not reapply cached sorting or grouping.
            if (!ShellViewStateRestore.ApplyMode(style.Mode, style.IconSize, new NativeViewStateTarget(view, Current),
                    result => MenuError?.Invoke(this, $"无法切换查看方式：0x{result:X8}"), Current)) return false;
        }
        catch (COMException ex)
        {
            if (Current()) MenuError?.Invoke(this, $"无法切换查看方式：0x{ex.HResult:X8}");
            return false;
        }
        finally { if (view != IntPtr.Zero) Marshal.Release(view); }

        // Read back what Shell actually accepted. Search views remain runtime-only.
        if (!Current()) return false;
        try { CaptureViewState(); }
        catch (Exception)
        {
            if (Current()) MenuError?.Invoke(this, "查看方式已切换，但暂时无法读取完整视图配置；会话保存将等待重试。");
        }
        return Current();
    }

    public bool ApplyViewPreset(ViewPreset preset)
    {
        if (!IsVisible || _viewReadiness.Pending) return false;
        var validated = preset.NormalizeAndValidate();
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        bool Current() => ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation;
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero)
        {
            if (Current()) MenuError?.Invoke(this, "文件视图尚未就绪，未应用查看预设。");
            return false;
        }

        try
        {
            if (!Current()) return false;
            // Do not capture a partial preset over the previous complete state.
            // Native setters can pump a navigation callback, just like getters.
            _viewRestoreFailed = true;
            var applied = ShellViewStateRestore.ApplyPreset(validated, new NativeViewStateTarget(view, Current), (step, result) =>
            {
                var label = step == ShellViewRestoreStep.Mode ? "查看方式" : "列设置";
                MenuError?.Invoke(this, $"查看预设的{label}未能完整应用；会话保存将等待重新读取。0x{result:X8}");
            }, Current);
            if (!Current()) return false;
            _viewRestoreFailed = !applied;
            if (!applied) return false;
        }
        catch (COMException ex)
        {
            if (Current()) MenuError?.Invoke(this, $"无法应用查看预设：0x{ex.HResult:X8}");
            return false;
        }
        finally { Marshal.Release(view); }

        try
        {
            if (!Current()) return false;
            CaptureViewState();
            return Current() && !ViewCaptureFailed;
        }
        catch (Exception)
        {
            if (Current()) MenuError?.Invoke(this, "查看预设已发送给 Shell，但当前视图状态无法完整读取；会话保存将等待重试。");
            return false;
        }
    }

    private void ApplyViewState()
    {
        // All saved fields need protection, not only custom columns. Otherwise
        // a mode/sort/group failure can be captured over the user's settings.
        _viewRestoreFailed = true;
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        var saved = ShellGroupingPolicy.ForNativeRestore(_viewState.Copy(), _disableGrouping);
        var filterRequest = _quickFilterRequestVersion;
        bool Current() => ReferenceEquals(browser, _browser) && generation == _viewReadiness.Generation &&
            filterRequest == _quickFilterRequestVersion;
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return;
        try
        {
            var restored = ShellViewStateRestore.Restore(saved, new NativeViewStateTarget(view, Current), (step, result) =>
            {
                var label = step switch
                {
                    ShellViewRestoreStep.Mode => "查看方式", ShellViewRestoreStep.Columns => "列设置",
                    ShellViewRestoreStep.Sorting => "排序方式", _ => "分组方式"
                };
                MenuError?.Invoke(this, $"{label}恢复失败，暂未保存会话；请重新打开目录后重试。0x{result:X8}");
            }, Current);
            if (Current())
            {
                _viewRestoreFailed = !restored;
                if (restored) _quickFilterGroupToRestore = null;
            }
        }
        finally { Marshal.Release(view); }
    }

    private sealed class NativeViewStateTarget(nint view, Func<bool> isCurrent) : IShellViewStateTarget
    {
        public int SetMode(uint mode, int iconSize) =>
            GetMethod<SetViewModeAndIconSizeDelegate>(view, 35)(view, mode, iconSize);
        public int SetColumns(IReadOnlyList<DisplayColumnState> columns) => ShellColumnManager.Apply(view, columns, isCurrent);
        public int SetSorting(IReadOnlyList<SortColumnState> columns)
        {
            var native = columns.Select(sort => new NativeSortColumn
            {
                Key = new NativePropertyKey { FormatId = sort.Key.FormatId, PropertyId = sort.Key.PropertyId },
                Direction = sort.Direction
            }).ToArray();
            return GetMethod<SetSortColumnsDelegate>(view, 27)(view, native, native.Length);
        }
        public int SetGrouping(PropertyKeyState? key, bool ascending)
        {
            var native = key is null ? default : new NativePropertyKey { FormatId = key.FormatId, PropertyId = key.PropertyId };
            return GetMethod<SetGroupByDelegate>(view, 17)(view, ref native, ascending ? 1 : 0);
        }
    }

    private IntPtr GetFolderView2Pointer()
    {
        if (_browser is null) return IntPtr.Zero;
        var iid = FolderView2Id;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return IntPtr.Zero;
        try
        {
            var unknown = Marshal.GetIUnknownForObject(viewObject);
            try { return Marshal.QueryInterface(unknown, in iid, out var pointer) >= 0 ? pointer : IntPtr.Zero; }
            finally { Marshal.Release(unknown); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private sealed class NativeViewStateSource(IntPtr view) : IShellViewStateSource
    {
        public int GetDisplayColumns(out IReadOnlyList<DisplayColumnState>? columns) =>
            ShellColumnManager.Capture(view, out columns);

        public int GetMode(out uint mode, out int iconSize) =>
            GetMethod<GetViewModeAndIconSizeDelegate>(view, 36)(view, out mode, out iconSize);

        public int GetSortColumnCount(out int count) =>
            GetMethod<GetSortColumnCountDelegate>(view, 26)(view, out count);

        public int GetSortColumns(int count, out IReadOnlyList<SortColumnState> columns)
        {
            var nativeColumns = new NativeSortColumn[count];
            var result = GetMethod<GetSortColumnsDelegate>(view, 28)(view, nativeColumns, count);
            columns = result < 0 ? [] : nativeColumns.Select(column => new SortColumnState(
                new PropertyKeyState(column.Key.FormatId, column.Key.PropertyId), column.Direction)).ToArray();
            return result;
        }

        public int GetGroupBy(out PropertyKeyState? key, out bool ascending)
        {
            var result = GetMethod<GetGroupByDelegate>(view, 18)(view, out var nativeKey, out var nativeAscending);
            key = result >= 0 && nativeKey.FormatId != Guid.Empty
                ? new PropertyKeyState(nativeKey.FormatId, nativeKey.PropertyId) : null;
            ascending = result >= 0 && nativeAscending != 0;
            return result;
        }
    }

    public void Refresh()
    {
        ShellDirectoryRefreshRecovery.TryInvoke(() => Refresh(afterRefresh: null), error =>
        {
            LastShellRefreshStatus = $"refresh-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
            MenuError?.Invoke(this, $"Shell 文件视图刷新失败（0x{error.HResult:X8}）。请稍后重试。");
        });
    }

    internal bool SetQuickFilter(QuickFilterSettings settings)
    {
        var requestVersion = ++_quickFilterRequestVersion;
        var browser = _browser;
        var generation = _viewReadiness.Generation;
        bool Current() => requestVersion == _quickFilterRequestVersion && ReferenceEquals(browser, _browser) &&
            generation == _viewReadiness.Generation;
        var previousSettings = _quickFilterSettings;
        var previousFilter = _quickFilter;
        var previousFilterActive = _browserSite.ShellFolderFilterActive;
        var attachmentAttempted = false;
        LastQuickFilterStatus = $"start pattern={settings.Pattern},exclude={settings.ExcludeMatches},folders={settings.FilterFolders}";
        var pendingRestore = _pendingQuickFilterGroupRestore;
        CancelPendingQuickFilterGroupRestore();
        var searchActive = _location.IsSearchActive;
        PropertyKeyState? group = pendingRestore?.Group ?? _quickFilterGroupToRestore?.Group;
        var groupAscending = pendingRestore?.Ascending ?? _quickFilterGroupToRestore?.Ascending ?? false;
        var groupCleared = pendingRestore is not null;
        IDisposable? groupSelectionSuppression = pendingRestore is not null
            ? _selectionRecoverySuppression.Acquire(_viewReadiness.NavigationVersion) : null;
        try
        {
            var hasGroup = !_disableGrouping && !searchActive && (pendingRestore is not null || _quickFilterGroupToRestore is not null ||
                settings.HasAnyFilter && TryReadGroup(out group, out groupAscending) && group is not null);
            var initialItemCount = pendingRestore?.InitialItemCount ?? ReadDirectoryMonitorItemCount() ?? 0;
            if (!Current()) return false;
            // Clearing a Shell group can pump selection callbacks before Refresh takes its own suppression lease.
            // Keep the last confirmed user selection authoritative across that transition.
            groupSelectionSuppression ??= hasGroup
                ? _selectionRecoverySuppression.Acquire(_viewReadiness.NavigationVersion)
                : null;
            if (hasGroup && group is not null) RememberQuickFilterGroup(group, groupAscending);
            // Attach the filter while the Shell view is ungrouped; DefView can produce an empty item list otherwise.
            groupCleared = pendingRestore is not null || hasGroup && TrySetGroup(null, ascending: false, Current);
            if (!Current()) return false;
            var groupDiagnostic = ReadCurrentGroupDiagnostic();
            if (!Current()) return false;
            LastQuickFilterStatus += $"; grouped={hasGroup},clear-result={groupCleared},current-group={groupDiagnostic}";
            _quickFilterSettings = settings;
            _quickFilter = null;
            attachmentAttempted = true;
            UpdateQuickFilterAttachment(searchActive);
            if (!Current()) return false;
            LastQuickFilterStatus += $"; filter-attached={_browserSite.ShellFolderFilterActive}";
            QuickFilterChanged?.Invoke(_quickFilterSettings);
            if (!Current()) return false;
            // An uncreated tab keeps this preference for BuildWindowCore; there
            // is no native view to refresh yet.
            if (!searchActive && browser is not null)
                Refresh(groupCleared && group is not null
                    ? () =>
                    {
                        if (requestVersion == _quickFilterRequestVersion)
                            QueueQuickFilterGroupRestore(group, groupAscending, initialItemCount, requestVersion);
                    }
                    : null);
            return Current();
        }
        catch (Exception original)
        {
            if (!Current()) return false;
            // A rejected attachment or refresh must not leave the new settings
            // eligible for session/history saving. Reapply the previous filter;
            // native failure does not guarantee that no state changed.
            _quickFilterSettings = previousSettings;
            _quickFilter = previousFilter;
            _browserSite.ShellFolderFilterActive = previousFilterActive;
            var failures = new List<Exception> { original };
            try { if (attachmentAttempted) UpdateQuickFilterAttachment(searchActive); }
            catch (Exception ex) { failures.Add(new InvalidOperationException("恢复原筛选失败。", ex)); }
            if (!Current()) return false;
            try
            {
                if (groupCleared && group is not null)
                {
                    if (!TrySetGroup(group, groupAscending, Current))
                        failures.Add(new InvalidOperationException("恢复原分组失败。"));
                    else if (Current()) _quickFilterGroupToRestore = null;
                }
            }
            catch (Exception ex) { failures.Add(new InvalidOperationException("恢复原分组失败。", ex)); }
            if (!Current()) return false;
            CancelPendingQuickFilterGroupRestore();
            var rollbackVersion = ++_quickFilterRequestVersion;
            try { QuickFilterChanged?.Invoke(previousSettings); }
            catch (Exception ex) { failures.Add(new InvalidOperationException("恢复筛选界面失败。", ex)); }
            if (rollbackVersion != _quickFilterRequestVersion || !ReferenceEquals(browser, _browser) ||
                generation != _viewReadiness.Generation) return false;
            LastQuickFilterStatus += failures.Count == 1 ? "; previous-filter-restored" : "; rollback-incomplete";
            if (failures.Count > 1)
                throw new AggregateException("筛选失败，恢复原状态也未完整完成。", failures);
            throw;
        }
        finally { groupSelectionSuppression?.Dispose(); }
    }

    internal void SetHiddenItemsPreference(HiddenItemsPreference preference)
    {
        var changed = _hiddenItemsPreference != preference;
        if (!changed) return;
        _hiddenItemsPreference = preference;
        _quickFilter = null;
        if (_browser is null) return;
        UpdateQuickFilterAttachment(_location.IsSearchActive);
        if (!HiddenItemsPreferencePolicy.ShouldRefreshExistingView(changed, _viewWindow != IntPtr.Zero)) return;
        Refresh();
    }

    internal void RefreshForWindowsHiddenItemsChange()
    {
        if (_viewWindow != IntPtr.Zero) Refresh();
    }

    private void QueueQuickFilterGroupRestore(PropertyKeyState group, bool ascending, int initialItemCount, long requestVersion)
    {
        if (requestVersion != _quickFilterRequestVersion) return;
        CancelPendingQuickFilterGroupRestore();
        RememberQuickFilterGroup(group, ascending);
        if (_disableGrouping) return;
        var pending = new PendingQuickFilterGroupRestore(group, ascending, initialItemCount,
            _quickFilter, _location.DirectoryPath, _viewReadiness.NavigationVersion, requestVersion, Stopwatch.GetTimestamp());
        _pendingQuickFilterGroupRestore = pending;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _quickFilterGroupRestoreTimer = timer;
        timer.Tick += (_, _) => AdvanceQuickFilterGroupRestore(pending, timer);
        timer.Start();
        AdvanceQuickFilterGroupRestore(pending, timer);
    }

    private void AdvanceQuickFilterGroupRestore(PendingQuickFilterGroupRestore pending, DispatcherTimer timer)
    {
        bool VisitCurrent() => _quickFilterRequestVersion == pending.RequestVersion &&
            _viewReadiness.NavigationVersion == pending.NavigationVersion &&
            string.Equals(CurrentPath, pending.Path, StringComparison.OrdinalIgnoreCase);
        if (!ReferenceEquals(_pendingQuickFilterGroupRestore, pending) ||
            !ReferenceEquals(_quickFilterGroupRestoreTimer, timer)) return;
        if (!VisitCurrent())
        {
            CancelPendingQuickFilterGroupRestore();
            LastQuickFilterStatus += "; stale group restore cancelled after navigation";
            return;
        }
        if (_disableGrouping)
        {
            CancelPendingQuickFilterGroupRestore();
            return;
        }

        var callbacks = pending.Filter is null ? -1 : pending.Filter.ShouldShowCalls;
        var rows = ReadDirectoryMonitorItemCount() ?? 0;
        // Count queries can pump COM callbacks and retire this timer/visit.
        if (!VisitCurrent() || !ReferenceEquals(_pendingQuickFilterGroupRestore, pending) ||
            !ReferenceEquals(_quickFilterGroupRestoreTimer, timer)) return;
        var decision = QuickFilterEnumerationGate.Decide(pending.InitialItemCount, callbacks, rows,
            Stopwatch.GetElapsedTime(pending.StartedAt), TimeSpan.FromMilliseconds(QuickFilterGroupRestoreTimeoutMilliseconds));
        if (decision == QuickFilterEnumerationDecision.WaitForEnumeration) return;
        timer.Stop();
        _quickFilterGroupRestoreTimer = null;
        _pendingQuickFilterGroupRestore = null;

        if (decision == QuickFilterEnumerationDecision.RevertFilter && pending.Filter is not null)
        {
            LastQuickFilterStatus += "; Shell enumeration timed out; reverted filter to avoid an empty view";
            _quickFilterSettings = QuickFilterSettings.Empty;
            _quickFilter = null;
            try { UpdateQuickFilterAttachment(searchActive: false); }
            catch (Exception ex) { if (VisitCurrent()) MenuError?.Invoke(this, $"超时后恢复文件视图失败：{ex.Message}"); }
            if (!VisitCurrent()) return;
            QuickFilterChanged?.Invoke(_quickFilterSettings);
            if (!VisitCurrent()) return;
            MenuError?.Invoke(this, "Shell 文件视图未完成筛选刷新，已清除筛选并恢复显示全部项目。");
            if (!VisitCurrent()) return;
            try
            {
                Refresh(() =>
                {
                    if (pending.RequestVersion == _quickFilterRequestVersion)
                        QueueQuickFilterGroupRestore(pending.Group, pending.Ascending, pending.InitialItemCount,
                            pending.RequestVersion);
                });
            }
            catch (Exception ex) { if (VisitCurrent()) MenuError?.Invoke(this, $"筛选恢复刷新失败：0x{ex.HResult:X8}"); }
            return;
        }

        var restored = TrySetGroup(pending.Group, pending.Ascending, VisitCurrent);
        if (!VisitCurrent()) return;
        if (restored) _quickFilterGroupToRestore = null;
        var currentRows = ReadDirectoryMonitorItemCount();
        if (!VisitCurrent()) return;
        var currentGroup = ReadCurrentGroupDiagnostic();
        if (!VisitCurrent()) return;
        LastQuickFilterStatus += $"; restore-group={restored},rows={currentRows},current-group={currentGroup}";
    }

    private void RememberQuickFilterGroup(PropertyKeyState group, bool ascending)
    {
        // Filtering temporarily clears native grouping. Keep the confirmed intent
        // even when navigation cancels its timer, so save/copy never store "None".
        _quickFilterGroupToRestore = (group, ascending);
        _viewState = _viewState with { GroupKey = group, GroupAscending = ascending };
    }

    private ViewState PreserveQuickFilterGroup(ViewState captured)
    {
        if (_quickFilterGroupToRestore is not { } group) return captured;
        if (captured.GroupKey is not null &&
            (captured.GroupKey != group.Group || captured.GroupAscending != group.Ascending))
        {
            // Native state has moved beyond the temporary "None" phase. Honor
            // the newly observed group/direction instead of restoring over it.
            CancelPendingQuickFilterGroupRestore();
            _quickFilterGroupToRestore = null;
            _quickFilterRequestVersion++;
            return captured;
        }
        return captured with { GroupKey = group.Group, GroupAscending = group.Ascending };
    }

    private void CancelPendingQuickFilterGroupRestore()
    {
        _quickFilterGroupRestoreTimer?.Stop();
        _quickFilterGroupRestoreTimer = null;
        _pendingQuickFilterGroupRestore = null;
    }

    private string ReadCurrentGroupDiagnostic() => TryReadGroup(out var key, out var ascending)
        ? $"{key?.FormatId.ToString("B") ?? "none"}/{key?.PropertyId.ToString() ?? "-"},asc={ascending}"
        : "unavailable";

    private bool TryReadGroup(out PropertyKeyState? group, out bool ascending)
    {
        group = null;
        ascending = false;
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return false;
        try
        {
            if (GetMethod<GetGroupByDelegate>(view, 18)(view, out var key, out var direction) < 0) return false;
            if (key.FormatId != Guid.Empty) group = new PropertyKeyState(key.FormatId, key.PropertyId);
            ascending = direction != 0;
            return true;
        }
        finally { Marshal.Release(view); }
    }

    private bool TrySetGroup(PropertyKeyState? group, bool ascending, Func<bool> isCurrent)
    {
        if (!isCurrent()) return false;
        var view = GetFolderView2Pointer();
        if (view == IntPtr.Zero) return false;
        try
        {
            if (!isCurrent()) return false;
            var key = group is null ? default : new NativePropertyKey
                { FormatId = group.FormatId, PropertyId = group.PropertyId };
            var result = GetMethod<SetGroupByDelegate>(view, 17)(view, ref key, ascending ? 1 : 0);
            if (!isCurrent()) return false;
            if (result < 0) MenuError?.Invoke(this, $"调整筛选分组顺序失败：0x{result:X8}");
            return result >= 0;
        }
        finally { Marshal.Release(view); }
    }

    private void UpdateQuickFilterAttachment(bool searchActive)
    {
        if (_browser is null) return;
        var applyQuickFilter = ShellFolderFilter.ShouldApplyQuickFilter(searchActive, _quickFilterSettings.HasAnyFilter);
        var applyFolderFilter = applyQuickFilter || _hiddenItemsPreference != HiddenItemsPreference.FollowWindows;
        var filter = ShellFolderFilter.ShouldAttachFilter(_quickFilterSettings.HasAnyFilter, searchActive,
            _hiddenItemsPreference)
            ? _quickFilter ??= new ShellFolderFilter(
                applyQuickFilter ? _quickFilterSettings : QuickFilterSettings.Empty, _hiddenItemsPreference)
            : null;
        // DefView bypasses ShouldShow when NOINCLUDEITEM is set. Explicit hidden-item
        // preferences also need the callback even when no text/column filter is active.
        _browserSite.ShellFolderFilterActive = applyFolderFilter;
        if (filter is null) _quickFilter = null;
        var browserUnknown = Marshal.GetIUnknownForObject(_browser);
        try
        {
            var filterUnknown = filter is null ? IntPtr.Zero : Marshal.GetIUnknownForObject(filter);
            try { ShellFilterAttachment.Apply(browserUnknown, filterUnknown); }
            finally { if (filterUnknown != IntPtr.Zero) Marshal.Release(filterUnknown); }
        }
        finally { Marshal.Release(browserUnknown); }
    }

    private void Refresh(Action? afterRefresh)
    {
        if (_browser is null)
        {
            LastShellRefreshStatus = "browser-unavailable";
            throw new InvalidOperationException("ExplorerBrowser is unavailable for refresh.");
        }
        if (_retryPath is { } retryPath)
        {
            LastShellRefreshStatus = $"retry-navigation:{retryPath}";
            Navigate(retryPath);
            return;
        }
        var request = new ShellRefreshRequest(_browser, _viewReadiness.NavigationVersion, CurrentPath);
        bool IsCurrentRefreshRequest() => request.IsCurrent(_browser, _viewReadiness.NavigationVersion, CurrentPath);
        var browser = (IExplorerBrowser)request.Browser;
        var iid = ShellViewId;
        object? viewObject = null;
        var getViewStartedAt = _traceNativeUserMessages ? _directoryRefreshGetViewTiming.Start() : 0;
        int currentViewResult;
        try { currentViewResult = browser.GetCurrentView(ref iid, out viewObject); }
        catch when (!IsCurrentRefreshRequest()) { return; }
        finally { _directoryRefreshGetViewTiming.Stop(getViewStartedAt); }
        if (!IsCurrentRefreshRequest())
        {
            if (currentViewResult >= 0 && viewObject is not null) Marshal.ReleaseComObject(viewObject);
            return;
        }
        if (currentViewResult < 0)
        {
            LastShellRefreshStatus = $"GetCurrentView=0x{currentViewResult:X8}";
            throw ShellDirectoryRefreshRecovery.FailureForHResult(currentViewResult, "GetCurrentView")!;
        }
        try
        {
            var path = request.Path;
            var navigationVersion = request.NavigationVersion;
            var inputVersion = Interlocked.Read(ref _nativeUserInputVersion);
            ShellViewSelectionSnapshot? selection;
            var hasCachedSelection = _lastNativeSelection is not null;
            var cachedSelectionIsCurrent = ShellSelectionRestoreGate.ShouldUseRefreshSnapshot(
                hasCachedSelection,
                _lastNativeSelectionNavigationVersion == navigationVersion &&
                    string.Equals(_lastNativeSelectionPath, path, StringComparison.OrdinalIgnoreCase),
                _lastNativeSelectionInputVersion == inputVersion);
            if (cachedSelectionIsCurrent)
            {
                // Preserve the confirmed user snapshot for later native selection-change recovery.
                selection = _lastNativeSelection!.Clone();
            }
            else
            {
                var selectionCaptureStartedAt = _traceNativeUserMessages ? _directoryRefreshSelectionCaptureTiming.Start() : 0;
                try
                {
                    var folderView = GetFolderView2Pointer();
                    try { selection = folderView == IntPtr.Zero ? null : ShellViewSelectionSnapshot.Capture(folderView); }
                    finally { if (folderView != IntPtr.Zero) Marshal.Release(folderView); }
                }
                finally { _directoryRefreshSelectionCaptureTiming.Stop(selectionCaptureStartedAt); }
            }
            if (!IsCurrentRefreshRequest())
            {
                selection?.Dispose();
                return;
            }
            if (!cachedSelectionIsCurrent) ClearLastNativeSelection();
            DirectoryChangeLastRefreshSelectedCount = selection?.SelectedCount ?? -1;
            DirectoryChangeLastRefreshHadFocus = selection?.HasFocusedItem ?? false;

            var shellView = (IShellView)viewObject;
            if (!IsCurrentRefreshRequest())
            {
                selection?.Dispose();
                return;
            }
            var selectionRecoverySuppression = _selectionRecoverySuppression.Acquire(navigationVersion);
            int refreshResult;
            var nativeRefreshStartedAt = _traceNativeUserMessages ? _directoryNativeRefreshTiming.Start() : 0;
            try { refreshResult = shellView.Refresh(); }
            catch when (!IsCurrentRefreshRequest())
            {
                selectionRecoverySuppression.Dispose();
                selection?.Dispose();
                return;
            }
            catch
            {
                selectionRecoverySuppression.Dispose();
                selection?.Dispose();
                throw;
            }
            finally { _directoryNativeRefreshTiming.Stop(nativeRefreshStartedAt); }
            if (!IsCurrentRefreshRequest())
            {
                selectionRecoverySuppression.Dispose();
                selection?.Dispose();
                return;
            }
            if (refreshResult < 0)
            {
                LastShellRefreshStatus = $"IShellView.Refresh=0x{refreshResult:X8}";
                selectionRecoverySuppression.Dispose();
                selection?.Dispose();
                throw ShellDirectoryRefreshRecovery.FailureForHResult(refreshResult, "IShellView.Refresh")!;
            }

            // Shell rebuilds the item list asynchronously after Refresh. Run
            // completion after the same dispatcher phase used for selection restore.
            var selectionOwnershipTransferred = false;
            var completionCleaned = 0;
            void FinishRefreshCompletion()
            {
                if (Interlocked.Exchange(ref completionCleaned, 1) != 0) return;
                selectionRecoverySuppression.Dispose();
                if (!selectionOwnershipTransferred) selection?.Dispose();
            }
            DispatcherOperation restore;
            try
            {
                restore = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        ShellRefreshCompletion.Run(() =>
                        {
                            if (!IsCurrentRefreshRequest()) return;
                            var rows = ReadDirectoryMonitorItemCount();
                            if (!IsCurrentRefreshRequest()) return;
                            LastShellRefreshStatus = $"refreshed; rows={rows}";
                            if (selection is not null && inputVersion == Interlocked.Read(ref _nativeUserInputVersion))
                            {
                                DirectoryChangeLastRefreshViewCountAtRestore = ReadDirectoryMonitorItemCount() ?? -1;
                                if (!IsCurrentRefreshRequest()) return;
                                var restoreClock = Stopwatch.StartNew();
                                var restored = RestoreSelectionAfterRefresh(selection, path, navigationVersion,
                                    allowSelectedFocusFallback: false, source: "refresh");
                                restoreClock.Stop();
                                if (!IsCurrentRefreshRequest()) return;
                                DirectoryChangeLastRefreshRestoreMilliseconds = restoreClock.ElapsedMilliseconds;
                                DirectoryChangeLastRefreshSelectionRestored = restored.SelectionRestored;
                                DirectoryChangeLastRefreshFocusRestored = restored.FocusRestored;
                                if (!restored.SelectionRestored || !restored.FocusRestored)
                                    selectionOwnershipTransferred = ScheduleDeferredRefreshSelectionRestore(
                                        selection, path, navigationVersion, inputVersion, restored.SelectionRestored);
                            }
                            else if (selection is not null)
                            {
                                DirectoryChangeLastRefreshSelectionRestored = false;
                                DirectoryChangeLastRefreshFocusRestored = false;
                            }
                        }, IsCurrentRefreshRequest, afterRefresh, error =>
                        {
                            LastShellRefreshStatus = $"refresh-completion-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
                            MenuError?.Invoke(this, $"Shell 文件视图刷新完成失败（0x{error.HResult:X8}）。");
                        });
                    }
                    finally
                    {
                        FinishRefreshCompletion();
                    }
                }));
            }
            catch
            {
                FinishRefreshCompletion();
                throw;
            }
            restore.Aborted += (_, _) => FinishRefreshCompletion();
            if (restore.Status == DispatcherOperationStatus.Aborted)
            {
                FinishRefreshCompletion();
                if (IsCurrentRefreshRequest())
                {
                    LastShellRefreshStatus = "dispatcher-completion-aborted";
                    ShellRefreshCompletion.Run(static () => { }, IsCurrentRefreshRequest, afterRefresh, error =>
                    {
                        LastShellRefreshStatus = $"refresh-completion-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
                        MenuError?.Invoke(this, $"Shell 文件视图刷新完成失败（0x{error.HResult:X8}）。");
                    });
                }
            }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private (bool SelectionRestored, bool FocusRestored) RestoreSelectionAfterRefresh(
        ShellViewSelectionSnapshot selection, string path, long navigationVersion,
        bool allowSelectedFocusFallback, string source, bool restoreSelection = true, bool restoreFocus = true,
        bool allowEmptyViewSelectionRestore = false)
    {
        try
        {
            if (_browser is null || _viewReadiness.Pending ||
                navigationVersion != _viewReadiness.NavigationVersion ||
                !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
                return (false, false);

            var iid = ShellViewId;
            if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return (false, false);
            try
            {
                if (!allowEmptyViewSelectionRestore && selection.SelectedCount > 0 &&
                    viewObject is IFolderView folderView)
                {
                    var itemCountResult = folderView.ItemCount(2, out var visibleItemCount);
                    var sameViewAfterCount = _browser is not null && !_viewReadiness.Pending &&
                        navigationVersion == _viewReadiness.NavigationVersion &&
                        string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
                    if (!sameViewAfterCount) return (false, false);
                    if (itemCountResult == 0 &&
                        ShellSelectionRestoreGate.ShouldDeferSelectionRestore(true, visibleItemCount))
                    {
                        if (_traceNativeUserMessages)
                            TraceShellSelectionRestore(source, "selection-restore-deferred-empty-view count=0");
                        return (false, false);
                    }
                }

                var unknown = Marshal.GetIUnknownForObject(viewObject);
                try
                {
                    var result = Marshal.QueryInterface(unknown, in ShellViewId, out var viewPointer);
                    if (result < 0 || viewPointer == IntPtr.Zero) return (false, false);
                    try
                    {
                        return selection.Restore(viewPointer,
                            _traceNativeUserMessages ? step => TraceShellSelectionRestore(source, step) : null,
                            allowSelectedFocusFallback, restoreSelection, restoreFocus);
                    }
                    finally { Marshal.Release(viewPointer); }
                }
                finally { Marshal.Release(unknown); }
            }
            finally { Marshal.ReleaseComObject(viewObject); }
        }
        catch (COMException) { return (false, false); }
    }

    private string? ReadSelectionTargetReadiness(ShellViewSelectionSnapshot selection, string path,
        long navigationVersion, string? knownReady, out int queryCount)
    {
        queryCount = 0;
        try
        {
            if (_browser is null || _viewReadiness.Pending ||
                navigationVersion != _viewReadiness.NavigationVersion ||
                !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
                return null;
            var iid = ShellViewId;
            if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return null;
            try
            {
                var unknown = Marshal.GetIUnknownForObject(viewObject);
                try
                {
                    if (Marshal.QueryInterface(unknown, in ShellViewId, out var viewPointer) < 0 || viewPointer == IntPtr.Zero)
                        return null;
                    try
                    {
                        var readiness = selection.ReadTargetReadiness(viewPointer, knownReady, out queryCount);
                        return navigationVersion == _viewReadiness.NavigationVersion && !_viewReadiness.Pending &&
                               string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase)
                            ? readiness
                            : null;
                    }
                    finally { Marshal.Release(viewPointer); }
                }
                finally { Marshal.Release(unknown); }
            }
            finally { Marshal.ReleaseComObject(viewObject); }
        }
        catch (COMException) { return null; }
    }

    private bool ScheduleDeferredRefreshSelectionRestore(
        ShellViewSelectionSnapshot selection, string path, long navigationVersion, long inputVersion,
        bool selectionAlreadyRestored)
    {
        if (_deferredRefreshSelectionSnapshot is not null) return false;

        // ItemCount can reach its target before the refreshed Shell view can enumerate the saved focus PIDL.
        const int retryWindowMilliseconds = 1500;
        var startedAt = Stopwatch.GetTimestamp();
        var lastSelectionVersion = _browserSite.SelectionVersion;
        string? lastSelectionReadiness = null;
        var retrySelectionForShellChange = false;
        long lastSelectionRestoreAttemptAt = 0;
        var selectionRestored = selectionAlreadyRestored || selection.SelectedCount == 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _deferredRefreshSelectionSnapshot = selection;
        _deferredRefreshSelectionRestoreTimer = timer;
        _directorySelectionRestorePending = true;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(timer, _deferredRefreshSelectionRestoreTimer)) return;

            var sameView = _browser is not null && !_viewReadiness.Pending &&
                navigationVersion == _viewReadiness.NavigationVersion &&
                string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
            var userInputUnchanged = inputVersion == Interlocked.Read(ref _nativeUserInputVersion);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var withinRetryWindow = elapsed < TimeSpan.FromMilliseconds(retryWindowMilliseconds);
            if (!ShellSelectionRestoreGate.CanRetryDeferredRestore(sameView, userInputUnchanged, withinRetryWindow))
            {
                if (sameView && userInputUnchanged)
                {
                    var finalSelectionRestore = ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(
                        selectionRestored, readinessChanged: false, selectionVersionChanged: false, finalAttempt: true);
                    var fallback = RestoreSelectionAfterRefresh(selection, path, navigationVersion,
                        allowSelectedFocusFallback: true, source: "deferred-fallback",
                        restoreSelection: finalSelectionRestore,
                        allowEmptyViewSelectionRestore: true);
                    DirectoryChangeLastRefreshSelectionRestored = fallback.SelectionRestored;
                    DirectoryChangeLastRefreshFocusRestored = fallback.FocusRestored;
                }
                CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: true);
                return;
            }

            var currentSelectionVersion = _browserSite.SelectionVersion;
            var selectionVersionChanged = currentSelectionVersion != lastSelectionVersion;
            lastSelectionReadiness = ShellSelectionRestoreGate.ReadinessForSelectionVersion(
                lastSelectionReadiness, selectionVersionChanged);
            if (selectionVersionChanged)
            {
                lastSelectionVersion = currentSelectionVersion;
                selectionRestored = false;
                retrySelectionForShellChange = true;
                timer.Start();
                return;
            }

            var readinessQueryCount = 0;
            string? readiness;
            if (selection.SelectedCount == 0) readiness = string.Empty;
            else if (ShellSelectionRestoreGate.ShouldQuerySelectionReadiness(selectionRestored, selection.SelectedCount))
                readiness = ReadSelectionTargetReadiness(selection, path, navigationVersion, lastSelectionReadiness,
                    out readinessQueryCount);
            else readiness = lastSelectionReadiness;
            var selectionVersionAfterReadiness = _browserSite.SelectionVersion;
            if (ShellSelectionRestoreGate.ShouldDiscardReadinessObservation(
                    currentSelectionVersion, selectionVersionAfterReadiness))
            {
                // A Shell callback can reenter while querying COM. Do not apply or cache
                // PIDL readiness observed across two different selection generations.
                lastSelectionReadiness = null;
                lastSelectionVersion = selectionVersionAfterReadiness;
                selectionRestored = false;
                retrySelectionForShellChange = true;
                timer.Start();
                return;
            }
            var readinessChanged = readiness is null || lastSelectionReadiness is null ||
                !string.Equals(readiness, lastSelectionReadiness, StringComparison.Ordinal);
            lastSelectionReadiness = readiness;

            var restoreClock = Stopwatch.StartNew();
            var retryIntervalElapsed = ShellSelectionRestoreGate.ShouldRetryStableReadiness(
                selectionRestored, readiness,
                lastSelectionRestoreAttemptAt != 0 &&
                    Stopwatch.GetElapsedTime(lastSelectionRestoreAttemptAt) >= TimeSpan.FromMilliseconds(100));
            var restoreSelection = ShellSelectionRestoreGate.ShouldAttemptSelectionRestore(
                selectionRestored, readinessChanged, retrySelectionForShellChange,
                finalAttempt: false, retryIntervalElapsed: retryIntervalElapsed);
            retrySelectionForShellChange = false;
            if (_traceNativeUserMessages)
                TraceShellSelectionRestore("deferred-refresh",
                    $"selection-target-readiness state={readiness ?? "unavailable"},queries={readinessQueryCount},changed={readinessChanged},restore={restoreSelection}");
            var restoreFocus = ShellSelectionRestoreGate.ShouldRestoreFocus(
                selectionRestored, selection.SelectedCount == 0, selection.HasFocusedItem);
            if (!restoreSelection && !restoreFocus)
            {
                timer.Start();
                return;
            }
            var restored = RestoreSelectionAfterRefresh(selection, path, navigationVersion,
                allowSelectedFocusFallback: false, source: "deferred-refresh",
                restoreSelection: restoreSelection, restoreFocus: restoreFocus);
            if (restoreSelection) lastSelectionRestoreAttemptAt = Stopwatch.GetTimestamp();
            restoreClock.Stop();
            DirectoryChangeLastRefreshRestoreMilliseconds = restoreClock.ElapsedMilliseconds;
            DirectoryChangeLastRefreshSelectionRestored = restored.SelectionRestored;
            DirectoryChangeLastRefreshFocusRestored = restored.FocusRestored;
            if (restored.SelectionRestored || selection.SelectedCount == 0)
            {
                // A stable selection generation makes another native batch restore redundant during focus-only retries.
                selectionRestored = true;
                lastSelectionVersion = _browserSite.SelectionVersion;
            }
            if (selectionRestored && restored.FocusRestored)
            {
                CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: true);
                return;
            }

            timer.Start();
        };
        timer.Start();
        return true;
    }

    private void CancelDeferredRefreshSelectionRestore(bool restartDirectoryMonitor)
    {
        var hadPendingRestore = _directorySelectionRestorePending ||
            _deferredRefreshSelectionRestoreTimer is not null || _deferredRefreshSelectionSnapshot is not null;
        if (!hadPendingRestore) return;
        var timer = _deferredRefreshSelectionRestoreTimer;
        _deferredRefreshSelectionRestoreTimer = null;
        timer?.Stop();
        var selection = _deferredRefreshSelectionSnapshot;
        _deferredRefreshSelectionSnapshot = null;
        selection?.Dispose();
        _directorySelectionRestorePending = false;
        if (restartDirectoryMonitor && _directoryChangeTimer is { } directoryTimer)
        {
            directoryTimer.Interval = TimeSpan.FromMilliseconds(250);
            directoryTimer.Stop();
            directoryTimer.Start();
        }
    }

    private void OnShellSelectionChanged(IntPtr shellView)
    {
        TraceShellSelectionCallback(shellView);
        if (_browser is null || _selectionRecoverySuppression.IsSuppressed(_viewReadiness.NavigationVersion) || _viewReadiness.Pending ||
            !ShellSelectionRestoreGate.ShouldObserveSelectionCallback(_directorySelectionRestorePending, GetKeyState(1) < 0) ||
            Interlocked.Read(ref _nativeUserInputVersion) != Interlocked.Read(ref _settledNativeUserInputVersion)) return;
        if (Marshal.QueryInterface(shellView, in FolderView2Id, out var folderView) < 0 || folderView == IntPtr.Zero)
            return;

        ShellViewSelectionSnapshot? snapshot;
        try { snapshot = ShellViewSelectionSnapshot.Capture(folderView); }
        finally { Marshal.Release(folderView); }
        if (snapshot is null) return;
        SelectionContextChanged?.Invoke(this, EventArgs.Empty);

        if (snapshot.SelectedCount > 0)
        {
            var currentInputVersion = Interlocked.Read(ref _nativeUserInputVersion);
            var sameView = _lastNativeSelectionNavigationVersion == _viewReadiness.NavigationVersion &&
                string.Equals(_lastNativeSelectionPath, CurrentPath, StringComparison.OrdinalIgnoreCase);
            var inputUnchanged = _lastNativeSelectionInputVersion == currentInputVersion;
            var previousSnapshot = _lastNativeSelection;
            var previousSelectedCount = previousSnapshot?.SelectedCount ?? 0;
            var previousUnselectedFocusBecameSelected = previousSnapshot?.WasUnselectedFocusedItemAddedTo(snapshot) == true;
            var keepExistingSnapshot = ShellSelectionRestoreGate.ShouldKeepExistingSnapshot(sameView,
                inputUnchanged, previousSelectedCount, snapshot.SelectedCount, previousUnselectedFocusBecameSelected);
            TraceShellSelectionRestore("snapshot", $"decision={(keepExistingSnapshot ? "keep" : "replace")},previous={previousSelectedCount},current={snapshot.SelectedCount},same-view={sameView},input-unchanged={inputUnchanged},previous-unselected-focus-became-selected={previousUnselectedFocusBecameSelected},current-focus-selected={snapshot.FocusedItemWasSelected}");
            if (keepExistingSnapshot)
            {
                // Shell can publish an intermediate subset or temporarily select the prior unselected focus item
                // while rebuilding a large view; keep the confirmed selection through either transition.
                snapshot.Dispose();
                return;
            }

            ClearLastNativeSelection();
            _lastNativeSelection = snapshot;
            _lastNativeSelectionInputVersion = currentInputVersion;
            _lastNativeSelectionNavigationVersion = _viewReadiness.NavigationVersion;
            _lastNativeSelectionPath = CurrentPath;
            return;
        }

        snapshot.Dispose();
        var previous = _lastNativeSelection;
        var inputVersion = _lastNativeSelectionInputVersion;
        var navigationVersion = _lastNativeSelectionNavigationVersion;
        var path = _lastNativeSelectionPath;
        if (!ShellSelectionRestoreGate.ShouldRetainSnapshotAfterEmptyCallback(previous is not null,
                previous?.SelectedCount ?? 0, inputVersion, Interlocked.Read(ref _nativeUserInputVersion)))
        {
            ClearLastNativeSelection();
            return;
        }
        var confirmedSelection = previous!;
        TraceShellSelectionRestore("snapshot", $"decision=empty-restore,previous={confirmedSelection.SelectedCount}");

        // Keep the confirmed snapshot cached while Shell emits transient empty or partial selection callbacks
        // during refresh. The async attempt owns a clone so later input or replacement can safely dispose it.
        var restoreSnapshot = confirmedSelection.Clone();
        var restore = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                if (_browser is null || _viewReadiness.Pending ||
                    navigationVersion != _viewReadiness.NavigationVersion ||
                    Interlocked.Read(ref _nativeUserInputVersion) != inputVersion ||
                    !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
                    return;

                var currentFolderView = GetFolderView2Pointer();
                if (currentFolderView == IntPtr.Zero) return;
                try
                {
                    using var currentSelection = ShellViewSelectionSnapshot.Capture(currentFolderView);
                    if (currentSelection is null || currentSelection.SelectedCount != 0) return;
                }
                finally { Marshal.Release(currentFolderView); }

                var iid = ShellViewId;
                if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return;
                try
                {
                    var unknown = Marshal.GetIUnknownForObject(viewObject);
                    try
                    {
                        if (Marshal.QueryInterface(unknown, in ShellViewId, out var currentView) < 0 || currentView == IntPtr.Zero)
                            return;
                        try
                        {
                            var currentNavigationVersion = _viewReadiness.NavigationVersion;
                            var wasSuppressed = _selectionRecoverySuppression.IsSuppressed(currentNavigationVersion);
                            using (_selectionRecoverySuppression.Acquire(currentNavigationVersion))
                            {
                                TraceShellSelectionRestore("native", $"restore-snapshot selected={restoreSnapshot.SelectedCount}");
                                var restoreClock = Stopwatch.StartNew();
                                restoreSnapshot.Restore(currentView, _traceNativeUserMessages ? step => TraceShellSelectionRestore("native", step) : null);
                                restoreClock.Stop();
                                NativeSelectionRestoreMilliseconds = restoreClock.ElapsedMilliseconds;
                            }
                            if (!wasSuppressed) OnShellSelectionChanged(currentView);
                        }
                        finally { Marshal.Release(currentView); }
                    }
                    finally { Marshal.Release(unknown); }
                }
                finally { Marshal.ReleaseComObject(viewObject); }
            }
            catch (COMException) { }
            finally { restoreSnapshot.Dispose(); }
        }));
        if (restore.Status == DispatcherOperationStatus.Aborted) restoreSnapshot.Dispose();
    }

    private void TraceShellSelectionCallback(IntPtr shellView)
    {
        if (!_traceNativeUserMessages) return;
        var selectedCount = -1;
        var focused = false;
        try
        {
            if (Marshal.QueryInterface(shellView, in FolderView2Id, out var folderView) >= 0 && folderView != IntPtr.Zero)
            {
                try
                {
                    using var snapshot = ShellViewSelectionSnapshot.Capture(folderView);
                    if (snapshot is not null)
                    {
                        selectedCount = snapshot.SelectedCount;
                        focused = snapshot.HasFocusedItem;
                    }
                }
                finally { Marshal.Release(folderView); }
            }
        }
        catch (COMException) { }

        if (_shellSelectionCallbackSamples.Count == 64) _shellSelectionCallbackSamples.RemoveAt(0);
        _shellSelectionCallbackSamples.Add($"t={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart).TotalMilliseconds:F1}ms,ticks={Stopwatch.GetTimestamp()},selected={selectedCount},focused={focused},saved={_lastNativeSelection?.SelectedCount ?? 0},suppressed={_selectionRecoverySuppression.IsSuppressed(_viewReadiness.NavigationVersion)},pending={_viewReadiness.Pending},input={Interlocked.Read(ref _nativeUserInputVersion)}/{Interlocked.Read(ref _settledNativeUserInputVersion)}");
    }

    private void TraceShellSelectionRestore(string source, string step)
    {
        // A source review found these diagnostic strings were captured even when tracing was disabled.
        if (!_traceNativeUserMessages) return;
        if (_shellSelectionRestoreSamples.Count == 128) _shellSelectionRestoreSamples.RemoveAt(0);
        _shellSelectionRestoreSamples.Add($"t={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart).TotalMilliseconds:F1}ms,ticks={Stopwatch.GetTimestamp()},source={source},{step}");
    }

    private void TraceDirectoryRefreshCall(long startedAt)
    {
        if (!_traceNativeUserMessages || startedAt == 0) return;
        var endedAt = Stopwatch.GetTimestamp();
        if (_directoryRefreshCallTimeline.Count == 64) _directoryRefreshCallTimeline.RemoveAt(0);
        _directoryRefreshCallTimeline.Add($"start={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, startedAt).TotalMilliseconds:F1}ms,start-ticks={startedAt}," +
            $"end={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, endedAt).TotalMilliseconds:F1}ms,end-ticks={endedAt}," +
            $"duration={Stopwatch.GetElapsedTime(startedAt, endedAt).TotalMilliseconds:F1}ms");
    }

    private void ClearLastNativeSelection()
    {
        _lastNativeSelection?.Dispose();
        _lastNativeSelection = null;
        _lastNativeSelectionPath = null;
    }

    private void NoteNativeUserInput(IntPtr inputHwnd = default)
    {
        CancelNaturalUpdateSelectionVerification();
        CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: true);
        var inputVersion = Interlocked.Increment(ref _nativeUserInputVersion);
        if (_pendingFileOperationFocusRestore is { } pendingRestore)
        {
            var sameView = ReferenceEquals(pendingRestore.Browser, _browser) &&
                pendingRestore.NavigationVersion == _viewReadiness.NavigationVersion &&
                string.Equals(pendingRestore.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
            var focusStillDesired = MayRestoreFocus?.Invoke() == true && IsVisible &&
                Window.GetWindow(this)?.IsActive == true;
            // A file-content key can arrive while the Shell still has a sibling or stale focus HWND.
            // Do not let that first attempted navigation key invalidate the recovery that should make it work.
            if (ShellFocusRestoreGate.ShouldKeepPendingRestoreAfterNativeInput(
                    IsFileContentInputWindow(inputHwnd), IsColumnHeaderInputWindow(inputHwnd), sameView,
                    focusStillDesired))
                _pendingFileOperationFocusRestore = pendingRestore with { InputVersion = inputVersion };
        }
        ClearLastNativeSelection();
        _nativeInputSelectionCapture?.Abort();
        var path = CurrentPath;
        var navigationVersion = _viewReadiness.NavigationVersion;
        _nativeInputSelectionCapture = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _nativeInputSelectionCapture = null;
            if (_browser is null || _selectionRecoverySuppression.IsSuppressed(_viewReadiness.NavigationVersion) || _viewReadiness.Pending ||
                inputVersion != Interlocked.Read(ref _nativeUserInputVersion) ||
                navigationVersion != _viewReadiness.NavigationVersion ||
                !string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase)) return;

            if (GetKeyState(1) < 0) return; // Wait for WM_LBUTTONUP before caching the final mouse selection.
            var folderView = GetFolderView2Pointer();
            if (folderView == IntPtr.Zero) return;
            ShellViewSelectionSnapshot? snapshot;
            try { snapshot = ShellViewSelectionSnapshot.Capture(folderView); }
            finally { Marshal.Release(folderView); }
            if (inputVersion != Interlocked.Read(ref _nativeUserInputVersion))
            {
                snapshot?.Dispose();
                return;
            }

            Interlocked.Exchange(ref _settledNativeUserInputVersion, inputVersion);
            if (snapshot is null) return;
            if (snapshot.SelectedCount == 0)
            {
                snapshot.Dispose();
                return;
            }
            ClearLastNativeSelection();
            _lastNativeSelection = snapshot;
            _lastNativeSelectionInputVersion = inputVersion;
            _lastNativeSelectionNavigationVersion = navigationVersion;
            _lastNativeSelectionPath = path;
        }));
    }

    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    private bool IsFileContentInputWindow(IntPtr hwnd)
    {
        var content = FindContentFocusWindow();
        return hwnd != IntPtr.Zero && content != IntPtr.Zero &&
            (hwnd == content || IsChild(content, hwnd));
    }

    private static bool IsColumnHeaderInputWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && WindowClass(hwnd).Equals("SysHeader32", StringComparison.OrdinalIgnoreCase);

    private void BeginMiddleClickFolderProbe()
    {
        _middleClickFolderProbe = null;
        if (_browser is null || _location.IsSearchActive || _viewWindow == IntPtr.Zero) return;
        _middleClickParentPath = CurrentPath;
        _middleClickParentIsFileSystem = _location.IsFileSystemLocation;
        _middleClickNavigationVersion = _viewReadiness.NavigationVersion;
        _middleClickInputVersion = Interlocked.Read(ref _nativeUserInputVersion);
        _middleClickFolderProbe = GetCursorPos(out var point)
            ? ProbeFolderAtPoint(new ShellMenuPoint(point.X, point.Y), _middleClickParentPath,
                _middleClickParentIsFileSystem, _viewWindow)
            : Task.FromResult<string?>(null);
    }

    private void CompleteMiddleClickFolderProbe()
    {
        var probe = _middleClickFolderProbe;
        var parentPath = _middleClickParentPath;
        var navigationVersion = _middleClickNavigationVersion;
        var inputVersion = _middleClickInputVersion;
        var viewWindow = _viewWindow;
        _middleClickFolderProbe = null;
        _middleClickParentPath = null;
        _middleClickParentIsFileSystem = false;
        if (probe is null || parentPath is null) return;
        _ = probe.ContinueWith(completed =>
        {
            var folderPath = completed.Status == TaskStatus.RanToCompletion ? completed.Result : null;
            if (folderPath is null) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!ShellMiddleClickTarget.IsCurrentRequest(parentPath, navigationVersion, inputVersion,
                        viewWindow, CurrentPath, _viewReadiness.NavigationVersion,
                        Interlocked.Read(ref _nativeUserInputVersion), _viewWindow,
                        browserAvailable: _browser is not null, searchActive: _location.IsSearchActive)) return;
                FolderMiddleClicked?.Invoke(this, folderPath);
            }));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void StartDirectoryChangeMonitor(string path, long navigationVersion)
    {
        StopDirectoryChangeMonitor();
        LastDirectoryChangeWatcherError = "none";
        DirectoryChangeInitialCount = null;
        DirectoryChangeStartSampleCount = 0;
        if (_location.IsSearchActive || !IsSupportedDirectoryChangePath(path)) return;
        var version = _directoryChangeVersion;
        var queue = new ShellDirectoryChangeQueue();
        var request = new ShellDirectoryMonitorRequest(version, navigationVersion, path, queue);
        var createNotificationBatch = new ShellCreateNotificationBatch();
        var deleteNotificationBatch = new ShellDeleteNotificationBatch();
        var changedItemNotificationBatch = new ShellChangedItemNotificationBatch();
        var watcherRecovery = new ShellDirectoryWatcherRecovery();
        _directoryChangeQueue = queue;
        _directoryChangeCreateNotificationBatch = createNotificationBatch;
        _directoryChangeDeleteNotificationBatch = deleteNotificationBatch;
        _directoryChangeChangedItemNotificationBatch = changedItemNotificationBatch;
        _directoryChangeWatcherRecovery = watcherRecovery;
        Interlocked.Exchange(ref _directoryChangeCreatedEvents, 0);
        Interlocked.Exchange(ref _directoryChangeDeletedEvents, 0);
        Interlocked.Exchange(ref _directoryChangeLastEventAt, 0);
        Interlocked.Exchange(ref _directoryChangeNativeCreateNotifications, 0);
        _directoryChangeNativeCreateNotificationStartedAt = 0;
        _directoryChangeNativeCreateNotificationCompletedAt = 0;
        _directoryChangeLastCreateNotificationResult = "not-attempted";
        Interlocked.Exchange(ref _directoryChangeNativeFolderDeleteNotifications, 0);
        Interlocked.Exchange(ref _directoryChangeFolderDeleteLookupCount, 0);
        _directoryChangeLastFolderDeleteNotificationResult = "not-attempted";
        Interlocked.Exchange(ref _directoryChangeStarting, 1);
        StartDirectoryChangeWatcher(path, version, navigationVersion, queue, createNotificationBatch,
            deleteNotificationBatch, changedItemNotificationBatch, watcherRecovery, started =>
            {
                if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue,
                        watcherRecovery)) return;
                if (!started)
                {
                    ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                        createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch,
                        watcherRecovery, expectedWatcher: null);
                    return;
                }

                StartDirectoryChangeMonitorSampling(path, navigationVersion, request, queue);
            });
    }

    private void StartDirectoryChangeMonitorSampling(string path, long navigationVersion,
        ShellDirectoryMonitorRequest request, ShellDirectoryChangeQueue queue)
    {
        var startup = new ShellDirectoryMonitorStartup();
        var retries = 0;
        var startTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _directoryChangeStartTimer = startTimer;
        void StopIfCurrentStartTimer()
        {
            startTimer.Stop();
            if (!ReferenceEquals(startTimer, _directoryChangeStartTimer)) return;
            _directoryChangeStartTimer = null;
            if (ReferenceEquals(queue, _directoryChangeQueue)) StopDirectoryChangeMonitor();
        }
        startTimer.Tick += (_, _) =>
        {
            if (!request.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
                    _viewReadiness.IsCurrent(navigationVersion), _completedDiskDirectory, _directoryChangeQueue))
            {
                StopIfCurrentStartTimer();
                return;
            }
            retries++;
            DirectoryChangeStartSampleCount = retries;
            if (!ShellDirectoryMonitorStartup.TryReadCount(ReadDirectoryMonitorItemCount, out var sampledCount, error =>
                {
                    if (request.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
                            _viewReadiness.IsCurrent(navigationVersion), _completedDiskDirectory, _directoryChangeQueue))
                        LastShellRefreshStatus = $"directory-start-sample-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
                }))
            {
                if (!request.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
                        _viewReadiness.IsCurrent(navigationVersion), _completedDiskDirectory, _directoryChangeQueue))
                {
                    StopIfCurrentStartTimer();
                    return;
                }
                startup.Observe(null);
                startTimer.Interval = TimeSpan.FromMilliseconds(startup.NextIntervalMilliseconds);
                return;
            }
            // Reading IFolderView can reenter navigation; reject a stale sample before
            // it can install a batch/timer over the new directory's monitor.
            if (!request.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
                    _viewReadiness.IsCurrent(navigationVersion), _completedDiskDirectory, _directoryChangeQueue))
            {
                StopIfCurrentStartTimer();
                return;
            }
            if (startup.Observe(sampledCount) is { } initialCount)
            {
                startTimer.Stop();
                _directoryChangeStartTimer = null;
                DirectoryChangeInitialCount = initialCount;
                StartDirectoryChangeMonitorCore(path, navigationVersion, initialCount, queue);
            }
            else startTimer.Interval = TimeSpan.FromMilliseconds(startup.NextIntervalMilliseconds);
        };
        startTimer.Start();
    }

    private async void StartDirectoryChangeWatcher(string path, long version, long navigationVersion,
        ShellDirectoryChangeQueue queue, ShellCreateNotificationBatch createNotificationBatch,
        ShellDeleteNotificationBatch deleteNotificationBatch,
        ShellChangedItemNotificationBatch changedItemNotificationBatch,
        ShellDirectoryWatcherRecovery recovery, Action<bool> completed)
    {
        var started = await ShellDirectoryWatcherStartup.StartAsync(
            () => CreateDirectoryChangeWatcher(path, version, navigationVersion, queue,
                createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch, recovery),
            watcher => watcher.EnableRaisingEvents = true,
            () => IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery),
            watcher =>
            {
                _directoryChangeWatcherEventGate.SetCurrent(watcher);
                _directoryChangeWatcher = watcher;
            },
            watcher =>
            {
                if (!ReferenceEquals(_directoryChangeWatcher, watcher)) return;
                _directoryChangeWatcherEventGate.SetCurrent(null);
                _directoryChangeWatcher = null;
            },
            DisposeDirectoryChangeWatcher);
        if (IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery))
            ShellDirectoryWatcherStartup.TryNotifyCompleted(completed, started, error =>
            {
                if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery))
                    return;
                LastShellRefreshStatus = $"directory-watcher-completion-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
                recovery.Complete(recovered: false);
                ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                    createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch,
                    recovery, expectedWatcher: _directoryChangeWatcher);
            });
    }

    private FileSystemWatcher CreateDirectoryChangeWatcher(string path, long version, long navigationVersion,
        ShellDirectoryChangeQueue queue, ShellCreateNotificationBatch createNotificationBatch,
        ShellDeleteNotificationBatch deleteNotificationBatch,
        ShellChangedItemNotificationBatch changedItemNotificationBatch,
        ShellDirectoryWatcherRecovery recovery)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = false,
            NotifyFilter = ShellDirectoryChangeMonitorOptions.NotifyFilter,
            InternalBufferSize = 16 * 1024
        };
        Action<Exception> onEventFailure = error =>
        {
            System.Diagnostics.Trace.TraceError(
                $"Explorer Dirs: directory watcher callback failed; hr=0x{error.HResult:X8}");
            ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch, recovery, watcher);
        };
        watcher.Created += (_, eventArgs) =>
        {
            _directoryChangeWatcherEventGate.TryInvoke(watcher, () =>
            {
                Interlocked.Increment(ref _directoryChangeCreatedEvents);
                var change = ShellDirectoryChangeMonitorOptions.ForCreated();
                QueueDirectoryChange(version, queue, change.Delta,
                    change.ForceRefresh || Volatile.Read(ref _directoryChangeStarting) != 0);
                QueueCreatedItemNotification(version, queue, eventArgs.FullPath, createNotificationBatch);
            }, onEventFailure);
        };
        watcher.Deleted += (_, eventArgs) =>
        {
            _directoryChangeWatcherEventGate.TryInvoke(watcher, () =>
            {
                Interlocked.Increment(ref _directoryChangeDeletedEvents);
                var change = ShellDirectoryChangeMonitorOptions.ForDeleted();
                QueueDirectoryChange(version, queue, change.Delta,
                    change.ForceRefresh || Volatile.Read(ref _directoryChangeStarting) != 0);
                QueueDeletedItemNotification(version, queue, deleteNotificationBatch, eventArgs.FullPath);
            }, onEventFailure);
        };
        watcher.Changed += (_, eventArgs) =>
        {
            _directoryChangeWatcherEventGate.TryInvoke(watcher, () =>
            {
                var change = ShellDirectoryChangeMonitorOptions.ForChanged();
                QueueChangedItemNotification(version, queue, changedItemNotificationBatch, eventArgs.FullPath,
                    change.Delta);
            }, onEventFailure);
        };
        watcher.Renamed += (_, _) =>
        {
            _directoryChangeWatcherEventGate.TryInvoke(watcher, () =>
            {
                var change = ShellDirectoryChangeMonitorOptions.ForRenamed();
                QueueDirectoryChange(version, queue, change.Delta, change.ForceRefresh);
            }, onEventFailure);
        };
        watcher.Error += (_, eventArgs) =>
        {
            _directoryChangeWatcherEventGate.TryInvoke(watcher, () =>
            {
                var error = eventArgs.GetException();
                RecordDirectoryChangeWatcherError(path, version, navigationVersion, queue, recovery, watcher, error);
                var change = ShellDirectoryChangeMonitorOptions.ForError();
                QueueDirectoryChange(version, queue, change.Delta, change.ForceRefresh);
                ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                    createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch, recovery, watcher);
            }, onEventFailure);
        };
        return watcher;
    }

    private void RecordDirectoryChangeWatcherError(string path, long version, long navigationVersion,
        ShellDirectoryChangeQueue queue, ShellDirectoryWatcherRecovery recovery, FileSystemWatcher watcher,
        Exception error)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery) ||
                    !ReferenceEquals(_directoryChangeWatcher, watcher)) return;

                // Keep overflow and other watcher failures distinguishable even when recreation succeeds.
                LastDirectoryChangeWatcherError = $"{error.GetType().Name},hr=0x{error.HResult:X8}";
                LastShellRefreshStatus = $"directory-watcher-error={LastDirectoryChangeWatcherError}";
            }));
        }
        catch (InvalidOperationException)
        {
            // The owning Dispatcher can shut down between the check and enqueue.
        }
    }

    private static void DisposeDirectoryChangeWatcher(FileSystemWatcher watcher)
    {
        try { watcher.EnableRaisingEvents = false; }
        catch (ObjectDisposedException) { }
        finally { watcher.Dispose(); }
    }

    private void ScheduleDirectoryChangeWatcherRecovery(string path, long version, long navigationVersion,
        ShellDirectoryChangeQueue queue, ShellCreateNotificationBatch createNotificationBatch,
        ShellDeleteNotificationBatch deleteNotificationBatch,
        ShellChangedItemNotificationBatch changedItemNotificationBatch,
        ShellDirectoryWatcherRecovery recovery,
        FileSystemWatcher? expectedWatcher)
    {
        if (!recovery.TryBegin(out var retryDelay)) return;
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                DispatcherTimer? timer = null;
                ShellDirectoryWatcherRecovery.TryRunCallback(() =>
                {
                    if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery) ||
                        !ReferenceEquals(_directoryChangeWatcher, expectedWatcher))
                    {
                        recovery.Complete(recovered: false);
                        return;
                    }

                    timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = retryDelay };
                    _directoryChangeWatcherRecoveryTimer = timer;
                    timer.Tick += (_, _) => ShellDirectoryWatcherRecovery.TryRunCallback(() =>
                    {
                        timer.Stop();
                        if (!ReferenceEquals(timer, _directoryChangeWatcherRecoveryTimer)) return;
                        _directoryChangeWatcherRecoveryTimer = null;
                        if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery) ||
                            !ReferenceEquals(_directoryChangeWatcher, expectedWatcher))
                        {
                            recovery.Complete(recovered: _directoryChangeWatcher is not null);
                            return;
                        }

                        if (_directoryChangeWatcher is { } failedWatcher)
                        {
                            _directoryChangeWatcherEventGate.SetCurrent(null);
                            _directoryChangeWatcher = null;
                            _ = ShellDirectoryWatcherStartup.DisposeSafelyAsync(
                                () => DisposeDirectoryChangeWatcher(failedWatcher));
                        }

                        StartDirectoryChangeWatcher(path, version, navigationVersion, queue,
                            createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch, recovery,
                            recovered =>
                            {
                                if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue,
                                        recovery)) return;
                                recovery.Complete(recovered);
                                if (recovered)
                                {
                                    // Reconcile changes that may have occurred while this watcher was detached.
                                    QueueDirectoryChange(version, queue, delta: 0, forceRefresh: true);
                                }
                                else
                                {
                                    ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                                        createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch,
                                        recovery, expectedWatcher: null);
                                }
                            });
                    }, error => RecoverScheduledWatcherAttempt(timer, error));
                    timer.Start();
                }, error => RecoverScheduledWatcherAttempt(timer, error));
            }));
        }
        catch (Exception)
        {
            // If dispatch failed transiently, retry from the thread pool after backoff.
            // A shutting-down dispatcher cannot accept a retry, so leave only state cleanup.
            recovery.Complete(recovered: false);
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            _ = ShellDirectoryWatcherRecovery.RetryAfterDispatchFailureAsync(retryDelay, () =>
                ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                    createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch,
                    recovery, expectedWatcher));
        }

        void RecoverScheduledWatcherAttempt(DispatcherTimer? timer, Exception error)
        {
            recovery.CompleteFailedAttempt(() =>
            {
                if (timer is null) return;
                try { timer.Stop(); }
                finally
                {
                    if (ReferenceEquals(timer, _directoryChangeWatcherRecoveryTimer))
                        _directoryChangeWatcherRecoveryTimer = null;
                }
            });
            if (!IsDirectoryChangeWatcherRecoveryCurrent(path, version, navigationVersion, queue, recovery)) return;
            LastShellRefreshStatus = $"directory-watcher-recovery-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
            ScheduleDirectoryChangeWatcherRecovery(path, version, navigationVersion, queue,
                createNotificationBatch, deleteNotificationBatch, changedItemNotificationBatch,
                recovery, expectedWatcher: _directoryChangeWatcher);
        }
    }

    private bool IsDirectoryChangeWatcherRecoveryCurrent(string path, long version, long navigationVersion,
        ShellDirectoryChangeQueue queue, ShellDirectoryWatcherRecovery recovery) =>
        version == _directoryChangeVersion && navigationVersion == _viewReadiness.NavigationVersion &&
        _viewReadiness.IsCurrent(navigationVersion) &&
        string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase) &&
        ReferenceEquals(queue, _directoryChangeQueue) && ReferenceEquals(recovery, _directoryChangeWatcherRecovery);

    private void StartDirectoryChangeMonitorCore(string path, long navigationVersion, int initialCount,
        ShellDirectoryChangeQueue queue)
    {
        var batch = new ShellDirectoryChangeBatch(initialCount);
        _directoryChangeBatch = batch;
        var version = _directoryChangeVersion;
        var timer = CreateDirectoryChangeTimer(path, navigationVersion, batch, queue, version);
        _directoryChangeTimer = timer;
        var startupChanges = queue.Drain();
        Interlocked.Exchange(ref _directoryChangeStarting, 0);
        batch.RecordChanges(startupChanges.Delta, startupChanges.ForceRefresh);
        timer.Start();
    }

    private ShellDirectoryMonitorRequest? CaptureCurrentDirectoryMonitorRequest(long version)
    {
        if (version != _directoryChangeVersion || _completedDiskDirectory is not { } path ||
            _directoryChangeQueue is not { } queue) return null;
        var request = new ShellDirectoryMonitorRequest(version, _viewReadiness.NavigationVersion, path, queue);
        return IsCurrentDirectoryMonitorRequest(request) ? request : null;
    }

    private bool IsCurrentDirectoryMonitorRequest(ShellDirectoryMonitorRequest request) =>
        request.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
            _viewReadiness.IsCurrent(request.NavigationVersion), _completedDiskDirectory, _directoryChangeQueue);

    private DispatcherTimer CreateDirectoryChangeTimer(string path, long navigationVersion,
        ShellDirectoryChangeBatch batch, ShellDirectoryChangeQueue queue, long version)
    {
            var monitorRequest = new ShellDirectoryMonitorRequest(version, navigationVersion, path, queue);
            var refreshRecovery = new ShellDirectoryRefreshRecovery();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
        void RecoverRefreshFailure(Exception error)
        {
            if (!_viewReadiness.IsCurrent(navigationVersion) ||
                !string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase) ||
                version != _directoryChangeVersion || !ReferenceEquals(batch, _directoryChangeBatch) ||
                !ReferenceEquals(timer, _directoryChangeTimer))
                return;

            // Refresh can throw before its asynchronous completion callback runs. Keep the
            // monitor retryable and retain a forced refresh request instead of leaving the
            // in-progress guard stuck or accepting a stale count as settled.
            _directoryRefreshInProgress = false;
            _directoryChangeRecheckPending = false;
            batch.Failed();
            var retryDelay = refreshRecovery.RecordFailure();
            try
            {
                LastShellRefreshStatus = $"directory-refresh-exception={error.GetType().Name},hr=0x{error.HResult:X8}";
                timer.Interval = retryDelay;
                timer.Stop();
                timer.Start();
            }
            catch (Exception recoveryError)
            {
                System.Diagnostics.Trace.TraceError(
                    $"Explorer Dirs: directory monitor timer restart failed; hr=0x{recoveryError.HResult:X8}");
                ScheduleRefreshTimerRetry(retryDelay);
            }
        }

        void ScheduleRefreshTimerRetry(TimeSpan retryDelay)
        {
            _ = ShellDirectoryRefreshRecovery.RetryAfterRecoveryFailureAsync(retryDelay, () =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_viewReadiness.IsCurrent(navigationVersion) ||
                        !string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase) ||
                        version != _directoryChangeVersion || !ReferenceEquals(batch, _directoryChangeBatch) ||
                        !ReferenceEquals(timer, _directoryChangeTimer)) return;

                    ShellDirectoryRefreshRecovery.TryRunMonitorTick(() =>
                    {
                        timer.Stop();
                        timer.Interval = retryDelay;
                        timer.Start();
                    }, RecoverRefreshFailure);
                }));
            }, retryError =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
                System.Diagnostics.Trace.TraceError(
                    $"Explorer Dirs: directory monitor retry dispatch failed; hr=0x{retryError.HResult:X8}");
                ScheduleRefreshTimerRetry(refreshRecovery.RecordFailure());
            });
        }

            timer.Tick += (_, _) =>
            {
                ShellDirectoryRefreshRecovery.TryRunMonitorTick(() =>
                {
                    var timerTickStartedAt = _traceNativeUserMessages ? _directoryChangeTimerTickTiming.Start() : 0;
                    try
                    {
                        timer.Stop();
                        if (!_viewReadiness.IsCurrent(navigationVersion) ||
                            !string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase) ||
                            !ReferenceEquals(_directoryChangeBatch, batch)) return;
                        if (_directoryRefreshInProgress || _directorySelectionRestorePending) return;
                        var queueDrainStartedAt = _traceNativeUserMessages ? _directoryQueueDrainTiming.Start() : 0;
                        ShellDirectoryChangeBatchData queuedChanges;
                        try { queuedChanges = queue.Drain(); }
                        finally { _directoryQueueDrainTiming.Stop(queueDrainStartedAt); }
                        batch.RecordChanges(queuedChanges.Delta, queuedChanges.ForceRefresh);
                        var countCheckStartedAt = _traceNativeUserMessages ? _directoryCountCheckTiming.Start() : 0;
                        var hasPendingDirectoryChanges = batch.HasPendingChanges;
                        int? actualCount;
                        bool shouldDefer;
                        try
                        {
                            if (!ShellDirectoryMonitorCountRead.TryRead(ReadDirectoryMonitorItemCount, () =>
                                monitorRequest.IsCurrent(_directoryChangeVersion, _viewReadiness.NavigationVersion,
                                    _viewReadiness.IsCurrent(navigationVersion), _completedDiskDirectory,
                                    _directoryChangeQueue), out actualCount)) return;
                            var lastEventAt = Interlocked.Read(ref _directoryChangeLastEventAt);
                            var largeBurstActive = lastEventAt != 0 &&
                                Stopwatch.GetElapsedTime(lastEventAt) < TimeSpan.FromMilliseconds(750);
                            shouldDefer = batch.ShouldDeferRefresh(actualCount, _directoryChangeRecheckPending, largeBurstActive);
                        }
                        finally { _directoryCountCheckTiming.Stop(countCheckStartedAt); }
                        if (shouldDefer)
                        {
                            _directoryChangeRecheckPending = true;
                            timer.Interval = TimeSpan.FromMilliseconds(500);
                            timer.Start();
                            return;
                        }
                        _directoryChangeRecheckPending = false;
                        timer.Interval = TimeSpan.FromMilliseconds(250);
                        var expectedCount = batch.ExpectedCountAfterChanges;
                        var forceRefresh = batch.ForceRefresh;
                        var shouldRefresh = batch.Complete(actualCount);
                        if (!shouldRefresh && hasPendingDirectoryChanges)
                        {
                            // Shell may add the focused row to selection during a natural update without a final selection callback.
                            RestoreSelectionAfterNaturalDirectoryUpdate(path, navigationVersion);
                        }
                        else if (shouldRefresh)
                        {
                            DirectoryChangeLastExpectedCount = expectedCount;
                            DirectoryChangeLastActualCount = actualCount;
                            DirectoryChangeLastForcedRefresh = forceRefresh;
                            DirectoryChangeFallbackRefreshCount++;
                            if (_traceNativeUserMessages) _directoryChangeFallbackStartedAt = Stopwatch.GetTimestamp();
                            _directoryRefreshInProgress = true;
                            var refreshCallStartedAt = _traceNativeUserMessages ? _directoryRefreshCallTiming.Start() : 0;
                            try { Refresh(() =>
                            {
                                var completionStartedAt = _traceNativeUserMessages ? _directoryRefreshCompletionTiming.Start() : 0;
                                try
                                {
                                    if (!_viewReadiness.IsCurrent(navigationVersion) ||
                                        !string.Equals(_completedDiskDirectory, path, StringComparison.OrdinalIgnoreCase) ||
                                        !ReferenceEquals(batch, _directoryChangeBatch) ||
                                        !ReferenceEquals(timer, _directoryChangeTimer)) return;
                                    // Refresh is asynchronous; drain queued watcher deltas before reconciling
                                    // the count so ContextIdle cannot apply them after a new baseline is set.
                                    batch.CompleteRefresh(queue, ReadDirectoryMonitorItemCount, () =>
                                        IsCurrentDirectoryMonitorRequest(monitorRequest) &&
                                        ReferenceEquals(batch, _directoryChangeBatch) &&
                                        ReferenceEquals(timer, _directoryChangeTimer));
                                    // Reading the count may have reentered navigation. A retired refresh
                                    // callback must not clear state belonging to a newer monitor.
                                    if (!IsCurrentDirectoryMonitorRequest(monitorRequest) ||
                                        !ReferenceEquals(batch, _directoryChangeBatch) ||
                                        !ReferenceEquals(timer, _directoryChangeTimer)) return;
                                    refreshRecovery.RecordSuccess();
                                    if (_traceNativeUserMessages) _directoryChangeFallbackCompletedAt = Stopwatch.GetTimestamp();
                                    _directoryRefreshInProgress = false;
                                    _directoryChangeRecheckPending = false;
                                    timer.Interval = TimeSpan.FromMilliseconds(250);
                                    timer.Stop();
                                    timer.Start();
                                }
                                catch (Exception ex) { RecoverRefreshFailure(ex); }
                                finally { _directoryRefreshCompletionTiming.Stop(completionStartedAt); }
                            }); }
                            catch (Exception ex) { RecoverRefreshFailure(ex); }
                            finally
                            {
                                _directoryRefreshCallTiming.Stop(refreshCallStartedAt);
                                TraceDirectoryRefreshCall(refreshCallStartedAt);
                            }
                        }
                        if (!shouldRefresh && batch.ConsumeTransientEmptyViewRecheck())
                        {
                            timer.Interval = TimeSpan.FromMilliseconds(500);
                            timer.Start();
                        }
                    }
                    finally { _directoryChangeTimerTickTiming.Stop(timerTickStartedAt); }
                }, RecoverRefreshFailure);
            };
        return timer;
    }

    private void RestoreSelectionAfterNaturalDirectoryUpdate(string path, long navigationVersion)
    {
        var snapshot = _lastNativeSelection;
        var inputVersion = Interlocked.Read(ref _nativeUserInputVersion);
        if (snapshot is null || snapshot.SelectedCount == 0 ||
            !ShellSelectionRestoreGate.ShouldUseRefreshSnapshot(
                hasSnapshot: true,
                sameView: navigationVersion == _lastNativeSelectionNavigationVersion &&
                    string.Equals(path, _lastNativeSelectionPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase),
                userInputUnchanged: inputVersion == _lastNativeSelectionInputVersion))
            return;

        using var selection = snapshot.Clone();
        using var selectionRecoverySuppression = _selectionRecoverySuppression.Acquire(navigationVersion);
        RestoreSelectionAfterRefresh(selection, path, navigationVersion,
            allowSelectedFocusFallback: false, source: "directory-update");
        // Shell can clear a restored selection after its last watcher callback without another selection notification.
        ScheduleNaturalUpdateSelectionVerification(snapshot, path, navigationVersion, inputVersion);
    }

    private void ScheduleNaturalUpdateSelectionVerification(ShellViewSelectionSnapshot selection, string path,
        long navigationVersion, long inputVersion)
    {
        CancelNaturalUpdateSelectionVerification();
        const int verificationWindowMilliseconds = 3000;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _naturalUpdateSelectionVerificationSnapshot = selection.Clone();
        _naturalUpdateSelectionVerificationStartedAt = Stopwatch.GetTimestamp();
        _naturalUpdateSelectionVerificationTimer = timer;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(timer, _naturalUpdateSelectionVerificationTimer)) return;
            var elapsed = Stopwatch.GetElapsedTime(_naturalUpdateSelectionVerificationStartedAt);
            var sameView = _browser is not null && !_viewReadiness.Pending &&
                navigationVersion == _viewReadiness.NavigationVersion &&
                string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
            var userInputUnchanged = inputVersion == Interlocked.Read(ref _nativeUserInputVersion);
            if (!ShellSelectionRestoreGate.ShouldVerifyNaturalUpdateSelection(
                    _naturalUpdateSelectionVerificationSnapshot is not null, sameView, userInputUnchanged,
                    elapsed < TimeSpan.FromMilliseconds(verificationWindowMilliseconds)))
            {
                CancelNaturalUpdateSelectionVerification();
                return;
            }

            var currentSelectedCount = ReadCurrentShellSelectionCount();
            var pendingSelection = _naturalUpdateSelectionVerificationSnapshot;
            if (pendingSelection is not null && currentSelectedCount is { } count &&
                ShellSelectionRestoreGate.ShouldRestoreNaturalUpdateSelection(pendingSelection.SelectedCount, count))
            {
                using var suppression = _selectionRecoverySuppression.Acquire(navigationVersion);
                RestoreSelectionAfterRefresh(pendingSelection, path, navigationVersion,
                    allowSelectedFocusFallback: false, source: "natural-update-verification");
            }

            if (elapsed >= TimeSpan.FromMilliseconds(verificationWindowMilliseconds))
                CancelNaturalUpdateSelectionVerification();
        };
        timer.Start();
    }

    private int? ReadCurrentShellSelectionCount()
    {
        if (_browser is null) return null;
        var iid = ShellViewId;
        object? viewObject = null;
        try
        {
            if (_browser.GetCurrentView(ref iid, out viewObject) < 0 ||
                viewObject is not IFolderView folderView || folderView.ItemCount(1, out var count) < 0)
                return null;
            return count;
        }
        catch (COMException) { return null; }
        finally { if (viewObject is not null) Marshal.ReleaseComObject(viewObject); }
    }

    private void CancelNaturalUpdateSelectionVerification()
    {
        _naturalUpdateSelectionVerificationTimer?.Stop();
        _naturalUpdateSelectionVerificationTimer = null;
        _naturalUpdateSelectionVerificationSnapshot?.Dispose();
        _naturalUpdateSelectionVerificationSnapshot = null;
        _naturalUpdateSelectionVerificationStartedAt = 0;
    }

    private void QueueCreatedItemNotification(long version, ShellDirectoryChangeQueue queue, string path,
        ShellCreateNotificationBatch batch)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!batch.TryRecordForDispatch(path)) return;
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version != _directoryChangeVersion || !ReferenceEquals(batch, _directoryChangeCreateNotificationBatch))
                {
                    _directoryChangeLastCreateNotificationResult = "stale-dispatch";
                    return;
                }
                if (batch.TakeForDispatcher() is null)
                {
                    _directoryChangeLastCreateNotificationResult = "empty-dispatch-batch";
                    return;
                }
                if (_directoryChangeCreateNotificationTimer is null)
                {
                    var timer = new DispatcherTimer(DispatcherPriority.ContextIdle)
                    {
                        Interval = TimeSpan.FromMilliseconds(60)
                    };
                    _directoryChangeCreateNotificationTimer = timer;
                    timer.Tick += (_, _) => ShellDirectoryNotificationRecovery.TryRunTimerTick(() =>
                    {
                        timer.Stop();
                        if (!ReferenceEquals(timer, _directoryChangeCreateNotificationTimer))
                        {
                            _directoryChangeLastCreateNotificationResult = "stale-timer";
                            return;
                        }
                        if (!batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(60), out var createdPath))
                        {
                            timer.Start();
                            return;
                        }
                        _directoryChangeCreateNotificationTimer = null;
                        if (version != _directoryChangeVersion)
                        {
                            _directoryChangeLastCreateNotificationResult = "stale-version";
                            return;
                        }
                        var monitorRequest = CaptureCurrentDirectoryMonitorRequest(version);
                        if (monitorRequest is null)
                        {
                            _directoryChangeLastCreateNotificationResult = "stale-monitor";
                            return;
                        }
                        if (createdPath is null)
                        {
                            _directoryChangeLastCreateNotificationResult = "empty-path-batch";
                            return;
                        }
                        if (_directoryChangeBatch?.ExpectedCountAfterChanges is not { } expectedCount)
                        {
                            _directoryChangeLastCreateNotificationResult = "expected-count-unavailable";
                            return;
                        }
                        if (!ShellDirectoryMonitorCountRead.TryRead(ReadDirectoryMonitorItemCount,
                                () => IsCurrentDirectoryMonitorRequest(monitorRequest), out var actualCount))
                        {
                            _directoryChangeLastCreateNotificationResult = "stale-count-read";
                            return;
                        }
                        if (actualCount is not { } actual)
                        {
                            _directoryChangeLastCreateNotificationResult = $"actual-count-unavailable;expected={expectedCount}";
                            return;
                        }
                        if (actual >= expectedCount)
                        {
                            _directoryChangeLastCreateNotificationResult = $"already-current;expected={expectedCount};actual={actual}";
                            return;
                        }
                        _directoryChangeNativeCreateNotificationStartedAt = Stopwatch.GetTimestamp();
                        var notificationStartedAt = _traceNativeUserMessages ? _directoryCreateNotificationTiming.Start() : 0;
                        bool notified;
                        try { notified = ShellCreatedItemNotification.NotifyCreatedFile(createdPath); }
                        finally { _directoryCreateNotificationTiming.Stop(notificationStartedAt); }
                        if (notified)
                        {
                            _directoryChangeNativeCreateNotificationCompletedAt = Stopwatch.GetTimestamp();
                            Interlocked.Increment(ref _directoryChangeNativeCreateNotifications);
                            _directoryChangeLastCreateNotificationResult = $"notified;expected={expectedCount};actual={actual}";
                        }
                        else _directoryChangeLastCreateNotificationResult = $"path-rejected;expected={expectedCount};actual={actual}";
                    }, error =>
                    {
                        timer.Stop();
                        if (ReferenceEquals(timer, _directoryChangeCreateNotificationTimer))
                            _directoryChangeCreateNotificationTimer = null;
                        System.Diagnostics.Trace.TraceError(
                            $"Explorer Dirs: create notification timer failed; hr=0x{error.HResult:X8}");
                        QueueDirectoryChange(version, queue, delta: 0, forceRefresh: true);
                    });
                }
                _directoryChangeCreateNotificationTimer.Stop();
                _directoryChangeCreateNotificationTimer.Start();
            }));
        }
        catch (InvalidOperationException)
        {
            // The watcher can race dispatcher shutdown after its initial check.
        }
    }

    private void QueueDirectoryChange(long version, ShellDirectoryChangeQueue queue, int delta, bool forceRefresh)
    {
        var queueStartedAt = _traceNativeUserMessages ? _directoryQueueTiming.Start() : 0;
        try
        {
            if (version != Interlocked.Read(ref _directoryChangeVersion) || !ReferenceEquals(queue, _directoryChangeQueue) ||
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Interlocked.Exchange(ref _directoryChangeLastEventAt, Stopwatch.GetTimestamp());
            if (!queue.Enqueue(delta, forceRefresh)) return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    var dispatchStartedAt = _traceNativeUserMessages ? _directoryQueueDispatchTiming.Start() : 0;
                    try
                    {
                        if (version != _directoryChangeVersion || !ReferenceEquals(queue, _directoryChangeQueue))
                        {
                            queue.Invalidate();
                            return;
                        }
                        // During baseline sampling the watcher is already active. Leave its
                        // coalesced startup delta queued until the stable count creates a batch.
                        if (_directoryChangeBatch is not { } batch || _directoryChangeTimer is not { } timer)
                            return;
                        var changes = queue.DrainWhileDispatchPending();
                        batch.RecordChanges(changes.Delta, changes.ForceRefresh);
                        timer.Stop();
                        timer.Start();
                    }
                    finally { _directoryQueueDispatchTiming.Stop(dispatchStartedAt); }
                }));
            }
            catch (InvalidOperationException) { queue.Invalidate(); }
        }
        finally { _directoryQueueTiming.Stop(queueStartedAt); }
    }

    private void QueueChangedItemNotification(long version, ShellDirectoryChangeQueue queue,
        ShellChangedItemNotificationBatch batch, string path, int delta)
    {
        if (!ShellChangedItemNotification.ShouldNotifyPath(path))
        {
            QueueDirectoryChange(version, queue, delta, forceRefresh: true);
            return;
        }

        var dispatch = batch.TryRecordForDispatch(path, out _);
        QueueDirectoryChange(version, queue, delta, forceRefresh: false);
        if (!dispatch) return;

        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version != _directoryChangeVersion ||
                    !ReferenceEquals(batch, _directoryChangeChangedItemNotificationBatch)) return;

                if (_directoryChangeChangedItemNotificationTimer is null)
                {
                    var timer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    _directoryChangeChangedItemNotificationTimer = timer;
                    timer.Tick += (_, _) => ShellDirectoryNotificationRecovery.TryRunTimerTick(() =>
                    {
                        timer.Stop();
                        if (!ReferenceEquals(timer, _directoryChangeChangedItemNotificationTimer) ||
                            version != _directoryChangeVersion ||
                            !ReferenceEquals(batch, _directoryChangeChangedItemNotificationBatch)) return;
                        if (!batch.TryTakeAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var paths,
                                out var requiresRefresh))
                        {
                            timer.Start();
                            return;
                        }

                        _directoryChangeChangedItemNotificationTimer = null;
                        if (requiresRefresh)
                        {
                            QueueDirectoryChange(version, queue, delta: 0, forceRefresh: true);
                            return;
                        }
                        foreach (var changedPath in paths!)
                            ShellChangedItemNotification.NotifyChangedItem(changedPath);
                        var needsDirectorySortUpdate = true;
                        try
                        {
                            var currentView = CaptureViewState();
                            needsDirectorySortUpdate = ViewCaptureFailed ||
                                ShellChangedItemNotification.RequiresDirectoryUpdate(currentView);
                        }
                        catch (Exception)
                        {
                            // If the active sort/group cannot be read, keep the
                            // Changed batch correct with one directory update.
                        }
                        if (needsDirectorySortUpdate)
                        {
                            foreach (var directory in paths!.Select(Path.GetDirectoryName)
                                         .Where(directory => !string.IsNullOrWhiteSpace(directory))
                                         .Distinct(StringComparer.OrdinalIgnoreCase))
                                ShellChangedItemNotification.NotifyChangedDirectory(directory!);
                        }
                    }, error =>
                    {
                        timer.Stop();
                        if (ReferenceEquals(timer, _directoryChangeChangedItemNotificationTimer))
                            _directoryChangeChangedItemNotificationTimer = null;
                        System.Diagnostics.Trace.TraceError(
                            $"Explorer Dirs: changed notification timer failed; hr=0x{error.HResult:X8}");
                        QueueDirectoryChange(version, queue, delta: 0, forceRefresh: true);
                    });
                }
                _directoryChangeChangedItemNotificationTimer.Stop();
                _directoryChangeChangedItemNotificationTimer.Start();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void QueueDeletedItemNotification(long version, ShellDirectoryChangeQueue queue,
        ShellDeleteNotificationBatch batch, string path)
    {
        // Watcher callbacks run on ThreadPool threads. Coalesce before posting
        // to the STA so a large delete burst cannot flood its ContextIdle queue.
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (!batch.TryRecordForDispatch(path)) return;
        try
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version != _directoryChangeVersion || !ReferenceEquals(batch, _directoryChangeDeleteNotificationBatch) ||
                    batch.TakeForDispatcher() is null) return;
                if (_directoryChangeDeleteNotificationTimer is null)
                {
                    var timer = new DispatcherTimer(DispatcherPriority.ContextIdle)
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    _directoryChangeDeleteNotificationTimer = timer;
                    timer.Tick += (_, _) => ShellDirectoryNotificationRecovery.TryRunTimerTick(() =>
                    {
                        timer.Stop();
                        if (!ReferenceEquals(timer, _directoryChangeDeleteNotificationTimer)) return;
                        if (version != _directoryChangeVersion || !ReferenceEquals(batch, _directoryChangeDeleteNotificationBatch))
                        {
                            _directoryChangeDeleteNotificationTimer = null;
                            return;
                        }
                        if (!batch.TryTakeLatestAfterQuietPeriod(TimeSpan.FromMilliseconds(250), out var deletedPath))
                        {
                            if (batch.TakeForDispatcher() is not null)
                                timer.Start();
                            else
                                _directoryChangeDeleteNotificationTimer = null;
                            return;
                        }
                        _directoryChangeDeleteNotificationTimer = null;
                        var monitorRequest = CaptureCurrentDirectoryMonitorRequest(version);
                        if (deletedPath is null || monitorRequest is null ||
                            _directoryChangeBatch?.ExpectedCountAfterChanges is not { } expectedCount ||
                            _browser is null)
                            return;
                        if (!ShellDirectoryMonitorCountRead.TryRead(ReadDirectoryMonitorItemCount,
                                () => IsCurrentDirectoryMonitorRequest(monitorRequest), out var actualCount) ||
                            actualCount is not { } actual || actual == expectedCount)
                            return;

                        if (!ShellDeletedItemNotification.CanScanViewForDeletedFolder(actualCount, expectedCount))
                        {
                            _directoryChangeLastFolderDeleteNotificationResult = "SkippedLargeView";
                            return;
                        }

                        Interlocked.Increment(ref _directoryChangeFolderDeleteLookupCount);
                        var folderView = GetFolderView2Pointer();
                        if (folderView == IntPtr.Zero) return;
                        try
                        {
                            var notificationStartedAt = _traceNativeUserMessages ? _directoryDeleteNotificationTiming.Start() : 0;
                            ShellDeletedItemNotificationResult result;
                            try
                            {
                                result = ShellDeletedItemNotification.NotifyDeletedFolder(folderView, deletedPath,
                                    _traceNativeUserMessages
                                        ? step => TraceShellSelectionRestore("delete-identity", step)
                                        : null);
                            }
                            finally { _directoryDeleteNotificationTiming.Stop(notificationStartedAt); }
                            _directoryChangeLastFolderDeleteNotificationResult = result.ToString();
                            if (result == ShellDeletedItemNotificationResult.Notified)
                                Interlocked.Increment(ref _directoryChangeNativeFolderDeleteNotifications);
                        }
                        finally { Marshal.Release(folderView); }
                    }, error =>
                    {
                        timer.Stop();
                        if (ReferenceEquals(timer, _directoryChangeDeleteNotificationTimer))
                            _directoryChangeDeleteNotificationTimer = null;
                        System.Diagnostics.Trace.TraceError(
                            $"Explorer Dirs: delete notification timer failed; hr=0x{error.HResult:X8}");
                        QueueDirectoryChange(version, queue, delta: 0, forceRefresh: true);
                    });
                }
                _directoryChangeDeleteNotificationTimer.Stop();
                _directoryChangeDeleteNotificationTimer.Start();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void StopDirectoryChangeMonitor()
    {
        Interlocked.Exchange(ref _directoryChangeStarting, 0);
        CancelNaturalUpdateSelectionVerification();
        CancelDeferredRefreshSelectionRestore(restartDirectoryMonitor: false);
        _directoryChangeVersion++;
        Interlocked.Exchange(ref _directoryChangeLastEventAt, 0);
        _directoryChangeStartTimer?.Stop();
        _directoryChangeStartTimer = null;
        _directoryChangeWatcherRecoveryTimer?.Stop();
        _directoryChangeWatcherRecoveryTimer = null;
        _directoryChangeWatcherRecovery?.Retire();
        _directoryChangeWatcherRecovery = null;
        _directoryChangeTimer?.Stop();
        _directoryChangeTimer = null;
        _directoryChangeCreateNotificationTimer?.Stop();
        _directoryChangeCreateNotificationTimer = null;
        _directoryChangeDeleteNotificationTimer?.Stop();
        _directoryChangeDeleteNotificationTimer = null;
        _directoryChangeChangedItemNotificationTimer?.Stop();
        _directoryChangeChangedItemNotificationTimer = null;
        _directoryChangeBatch = null;
        _directoryRefreshInProgress = false;
        _directoryChangeRecheckPending = false;
        _directoryChangeCreateNotificationBatch = null;
        _directoryChangeDeleteNotificationBatch = null;
        _directoryChangeChangedItemNotificationBatch = null;
        _directoryChangeQueue?.Invalidate();
        _directoryChangeQueue = null;
        var watcher = _directoryChangeWatcher;
        _directoryChangeWatcherEventGate.SetCurrent(null);
        _directoryChangeWatcher = null;
        if (watcher is not null)
            _ = ShellDirectoryWatcherStartup.DisposeSafelyAsync(() => DisposeDirectoryChangeWatcher(watcher));
    }

    private static bool IsSupportedDirectoryChangePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        GetDriveType(Path.GetPathRoot(path) ?? string.Empty) is 3 or 4;

    private Rect ClientRect()
    {
        var source = PresentationSource.FromVisual(this);
        var point = new System.Windows.Point(ActualWidth, ActualHeight);
        if (source?.CompositionTarget is { } target)
            point = target.TransformToDevice.Transform(point);
        return new Rect
        {
            Right = Math.Max(1, (int)Math.Ceiling(point.X)),
            Bottom = Math.Max(1, (int)Math.Ceiling(point.Y))
        };
    }

    private void DisposeBrowser()
    {
        _quickFilterRequestVersion++;
        CancelPendingQuickFilterGroupRestore();
        _quickFilterGroupToRestore = null;
        ClearLastNativeSelection();
        _nativeInputSelectionCapture?.Abort();
        _nativeInputSelectionCapture = null;
        StopDirectoryChangeMonitor();
        _navigationRequestVersion++;
        var history = _historyEvents;
        _historyEvents = null; // Invalidate already queued availability updates.
        history?.Dispose();
        _completedDiskDirectory = null;
        _viewReadiness.Reset();
        _navigationGate.Cancel();
        CancelPendingNewFolderRename();
        _issuedBrowserPaths.Clear();
        _issuedSearches.Clear();
        // Destroying the browser also destroys its Shell TravelLog; old search PIDL aliases cannot be used after recreation.
        _searchHistory.Clear();
        _pendingFileOperationFocusRestore = null;
        _focusRestoreTimer.Stop();
        if (_browser is null) return;
        try { UpdateQuickFilterAttachment(searchActive: true); }
        catch (COMException) { /* The browser may already have released its filter site. */ }
        DetachViewWindow();
        if (_advised) { _browser.Unadvise(_cookie); _advised = false; }
        _browser.Destroy();
        try { ShellBrowserSite.SetSite(_browser, null); }
        catch (COMException) { /* The browser may already have released its site. */ }
        Marshal.ReleaseComObject(_browser);
        _browser = null;
    }

    int IExplorerBrowserEvents.OnNavigationPending(IntPtr pidl)
    {
        _quickFilterRequestVersion++;
        CancelPendingQuickFilterGroupRestore();
        ClearLastNativeSelection();
        _nativeInputSelectionCapture?.Abort();
        _nativeInputSelectionCapture = null;
        StopDirectoryChangeMonitor();
        QueueNativeThemeRefresh();
        var identity = NavigationIdentity(pidl);
        var targetPath = FileSystemPath(pidl);
        var isSearchTarget = _searchHistory.TryResolve(pidl, out _);
        if (isSearchTarget)
        {
            try { UpdateQuickFilterAttachment(searchActive: true); }
            catch (Exception ex) { MenuError?.Invoke(this, $"停用搜索视图筛选失败：{ex.Message}"); }
        }
        else if (targetPath is not null && _quickFilterSettings.HasAnyFilter)
        {
            // GetViewFlags is queried while DefView creates the target view. Re-enable
            // filtering before that transition; doing so only after completion is too late.
            try { UpdateQuickFilterAttachment(searchActive: false); }
            catch (Exception ex) { MenuError?.Invoke(this, $"为目标目录启用快速筛选失败：{ex.Message}"); }
        }
        var issuedVersion = IssuedRequestVersion(identity);
        var issuedByApp = issuedVersion.HasValue;
        // An app-issued folder/search can start after a later directory probe.
        // Its delayed callback must not cancel that newer request as user input.
        if (!issuedByApp)
        {
            _navigationRequestVersion++;
            _navigationGate.Cancel();
        }
        _nativeNavigationRequestVersion = issuedVersion ?? _navigationRequestVersion;
        _viewReadiness.Begin(identity);
        QueueNavigationAvailability();
        NavigationStarting?.Invoke(this, EventArgs.Empty);
        var focused = GetFocus();
        _restoreFocusAfterNavigation = _viewWindow != IntPtr.Zero &&
            (focused == _viewWindow || IsChild(_viewWindow, focused));
        if (_hasNavigated && !_location.IsSearchActive)
        {
            try { CaptureViewState(); }
            catch (Exception ex) { MenuError?.Invoke(this, $"读取视图配置失败：{ex.Message}"); }
        }
        return 0;
    }
    int IExplorerBrowserEvents.OnViewCreated(IntPtr view)
    {
        try
        {
            NativeShellTheme.ApplyView(view);
            var shellView = (IShellView)Marshal.GetObjectForIUnknown(view);
            try
            {
                ReportFolderHeaderFailure(ShellFolderViewOptions.EnableAllViewHeaders(view));
                var checkSelectResult = ShellFolderViewOptions.SetCheckSelect(view, _selectionCheckBoxes);
                if (checkSelectResult < 0)
                    MenuError?.Invoke(this, $"Shell 未能应用复选框选择设置（0x{checkSelectResult:X8}）。");
                if (_fullRowSelection is { } fullRowSelection)
                {
                    var fullRowResult = ShellFolderViewOptions.SetFullRowSelect(view, fullRowSelection);
                    if (fullRowResult < 0)
                        MenuError?.Invoke(this, $"Shell 未能应用整行选择设置（0x{fullRowResult:X8}）。");
                }
                var noGroupingResult = ShellFolderViewOptions.SetNoGrouping(view, _disableGrouping);
                if (noGroupingResult < 0)
                    MenuError?.Invoke(this, $"Shell 未能应用全局分组设置（0x{noGroupingResult:X8}）。");
                if (shellView.GetWindow(out var hwnd) >= 0 && hwnd != IntPtr.Zero)
                {
                    AttachViewWindow(hwnd);
                    NativeShellTheme.Apply(hwnd);
                    QueueNativeThemeRefresh();
                    QueueColumnHeaderGeometryRefresh();
                }
            }
            finally { Marshal.ReleaseComObject(shellView); }
        }
        catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
        return 0;
    }

    private void ReportFolderHeaderFailure(int result)
    {
        if (result >= 0 || _folderHeaderWarningReported) return;
        _folderHeaderWarningReported = true;
        MenuError?.Invoke(this, $"Shell 未能启用所有查看方式的列标题（0x{result:X8}）。");
    }

    int IExplorerBrowserEvents.OnNavigationComplete(IntPtr pidl)
    {
        QueueNativeThemeRefresh();
        var identity = NavigationIdentity(pidl);
        var requestVersion = RetireNavigationRequest(identity);
        var publish = _viewReadiness.Complete(identity);
        QueueNavigationAvailability();
        var navigationVersion = _viewReadiness.NavigationVersion;
        // Retire bookkeeping for every callback, but publish only the current
        // settled visit. An older completion must not overwrite address/capacity.
        if (!publish || !_viewReadiness.IsCurrent(navigationVersion)) return 0;
        if (_viewReadiness.RequiresLocationRead)
        {
            if (!ReconcileCurrentLocation(navigationVersion, requestVersion, clearRetry: true) &&
                _viewReadiness.IsCurrent(navigationVersion) && IsCurrentNavigationRequest(requestVersion))
                ReportNavigationFailure("当前 Shell 目录位置暂时无法确认，请刷新重试。", requestVersion);
            return 0;
        }
        return PublishNavigationLocation(pidl, navigationVersion, requestVersion, clearRetry: true);
    }

    private int PublishNavigationLocation(IntPtr pidl, long navigationVersion, long? requestVersion, bool clearRetry)
    {
        if (!_viewReadiness.IsCurrent(navigationVersion)) return 0;
        if (ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var path, out var isFileSystemLocation))
        {
            var returningFromSearch = _location.IsSearchActive;
            _locationUnconfirmed = false;
            _location.CompleteDirectory(path, isFileSystemLocation);
            _completedDiskDirectory = isFileSystemLocation ? path : null;
            try
            {
                UpdateQuickFilterAttachment(searchActive: false);
                if (returningFromSearch && _quickFilterSettings.HasAnyFilter) Refresh();
            }
            catch (Exception ex) { MenuError?.Invoke(this, $"应用快速筛选失败：{ex.Message}"); }
            _hasNavigated = true;
            if (clearRetry && CanCompleteNavigationRequest(requestVersion))
                _retryPath = null;
            try { AttachCurrentViewWindow(); }
            catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_viewReadiness.IsCurrent(navigationVersion) || CurrentPath != path || _location.IsSearchActive) return;
                try { ApplyViewState(); }
                catch (Exception ex) { MenuError?.Invoke(this, $"恢复视图配置失败：{ex.Message}"); }
                if (!_viewReadiness.IsCurrent(navigationVersion)) return;
                QueueColumnHeaderGeometryRefresh();
                if (isFileSystemLocation) StartDirectoryChangeMonitor(path, navigationVersion);
                else StopDirectoryChangeMonitor();
                if (CanCompleteNavigationRequest(requestVersion)) RestoreNavigationFocus(navigationVersion);
            }));
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_viewReadiness.IsCurrent(navigationVersion) && CurrentPath == path && !_location.IsSearchActive)
                    Navigated?.Invoke(this, new(path, requestVersion));
            }));
            return 0;
        }
        _completedDiskDirectory = null;
        if (_searchHistory.TryResolve(pidl, out var searchUri) &&
            _viewReadiness.IsCurrent(navigationVersion) &&
            _location.TryCompleteSearch(searchUri))
        {
            try { UpdateQuickFilterAttachment(searchActive: true); }
            catch (Exception ex) { MenuError?.Invoke(this, $"切换搜索视图筛选状态失败：{ex.Message}"); }
            _locationUnconfirmed = false;
            _hasNavigated = true;
            if (clearRetry && CanCompleteNavigationRequest(requestVersion)) _retryPath = null;
            try { AttachCurrentViewWindow(); }
            catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_viewReadiness.IsCurrent(navigationVersion) || _location.SearchUri != searchUri) return;
                if (CanCompleteNavigationRequest(requestVersion)) RestoreNavigationFocus(navigationVersion);
            }));
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_viewReadiness.IsCurrent(navigationVersion) && _location.SearchUri == searchUri)
                    SearchNavigated?.Invoke(this, new(_location.SearchQuery!, requestVersion));
            }));
            return 0;
        }
        // The view is neither a supported directory nor one of our searches.
        if (_viewReadiness.IsCurrent(navigationVersion)) _locationUnconfirmed = true;
        return 0;
    }

    private bool ReconcileCurrentLocation(long navigationVersion, long? requestVersion, bool clearRetry)
    {
        nint unknown = 0, actual = 0;
        try
        {
            if (_browser is null) return false;
            unknown = Marshal.GetIUnknownForObject(_browser);
            actual = ShellCurrentLocation.ReadPidl(unknown);
            if (actual == 0 || !_viewReadiness.IsCurrent(navigationVersion)) return false;
            _locationUnconfirmed = true;
            PublishNavigationLocation(actual, navigationVersion, requestVersion, clearRetry: clearRetry);
            return !_locationUnconfirmed && _viewReadiness.IsCurrent(navigationVersion);
        }
        catch (COMException) { return false; }
        catch (InvalidComObjectException) { return false; }
        finally
        {
            if (actual != 0) Marshal.FreeCoTaskMem(actual);
            if (unknown != 0) Marshal.Release(unknown);
            // If native location is unavailable, never label the previous
            // directory's capacity as belonging to this newly settled batch.
            if (actual == 0 && _viewReadiness.IsCurrent(navigationVersion))
            {
                _completedDiskDirectory = null;
                _locationUnconfirmed = true;
                ViewCaptureFailed = true;
            }
        }
    }

    private static string NormalizePathKey(string path) => Path.TrimEndingDirectorySeparator(path);
    private string? NavigationIdentity(IntPtr pidl) =>
        _searchHistory.TryResolve(pidl, out var searchUri) ? searchUri :
        ShellLocationIdentity.TryGetPersistedIdentity(pidl, out var identity, out _) ? identity : null;

    private void TrackIssuedPath(string path)
    {
        // Keep retries in the callback identity too, so a successful alias
        // navigation stops retrying and an older failure cannot replace it.
        _retryPath = path;
        var key = NormalizePathKey(path);
        _issuedBrowserPaths[key] = new(_issuedBrowserPaths.GetValueOrDefault(key).Count + 1, _navigationRequestVersion);
        if (_issuedBrowserPaths.Count > 32)
            _issuedBrowserPaths.Remove(_issuedBrowserPaths.Keys.First());
    }

    private void CompleteIssuedPath(string path)
    {
        var key = NormalizePathKey(path);
        if (!_issuedBrowserPaths.TryGetValue(key, out var request)) return;
        if (request.Count <= 1) _issuedBrowserPaths.Remove(key);
        else _issuedBrowserPaths[key] = request with { Count = request.Count - 1 };
    }

    private void CompleteIssuedSearch(string? identity)
    {
        if (identity is null || !_issuedSearches.TryGetValue(identity, out var request)) return;
        if (request.Count <= 1) _issuedSearches.Remove(identity);
        else _issuedSearches[identity] = request with { Count = request.Count - 1 };
    }

    private void TrackIssuedSearch(string uri) =>
        _issuedSearches[uri] = new(_issuedSearches.GetValueOrDefault(uri).Count + 1, _navigationRequestVersion);

    private long? IssuedRequestVersion(string? identity)
    {
        if (identity is null) return null;
        if (_issuedBrowserPaths.TryGetValue(NormalizePathKey(identity), out var directory)) return directory.RequestVersion;
        return _issuedSearches.TryGetValue(identity, out var search) ? search.RequestVersion : null;
    }

    private long? RetireNavigationRequest(string? identity)
    {
        // Native callbacks identify a target, not an individual submission.
        // Preserve the latest registered input number for the counted batch;
        // no partial batch may acknowledge input. Readiness still reconciles
        // the actual Shell location after repeated native targets settle.
        if (identity is not null)
        {
            if (_issuedBrowserPaths.TryGetValue(NormalizePathKey(identity), out var directory))
            {
                CompleteIssuedPath(identity);
                return directory.Count == 1 ? directory.RequestVersion : null;
            }
            if (_issuedSearches.TryGetValue(identity, out var search))
            {
                CompleteIssuedSearch(identity);
                return search.Count == 1 ? search.RequestVersion : null;
            }
        }
        return _nativeNavigationRequestVersion;
    }

    private static string? FileSystemPath(IntPtr pidl)
    {
        if (pidl == IntPtr.Zero || SHGetNameFromIDList(pidl, 0x80058000, out var pointer) < 0)
            return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private void RestoreNavigationFocus(long navigationVersion)
    {
        if (!_viewReadiness.IsCurrent(navigationVersion)) return;
        var requested = _focusWhenReady || _restoreFocusAfterNavigation;
        // Retire this visit's intent before native activation can reenter. A
        // hidden tab or a user who moved to another input must not leave stale
        // intent for a later visit. Explicit tab/window activation requests focus anew.
        _focusWhenReady = false;
        _restoreFocusAfterNavigation = false;
        if (requested && !IsRenameEditorFocused() && !IsColumnHeaderFocused())
            RestoreContentFocusIfNeeded();
    }

    private void AttachCurrentViewWindow()
    {
        if (_browser is null) return;
        var iid = ShellViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return;
        try
        {
            if (((IShellView)viewObject).GetWindow(out var hwnd) >= 0 && hwnd != IntPtr.Zero)
                AttachViewWindow(hwnd);
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private void QueueNativeThemeRefresh()
    {
        if (_nativeThemeRefreshQueued) return;
        _nativeThemeRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _nativeThemeRefreshQueued = false;
            // Scrollbar children may not exist yet during OnViewCreated.
            if (_browser is not null && _hostWindow != IntPtr.Zero)
                NativeShellTheme.Apply(_hostWindow);
        }));
    }

    private void AttachViewWindow(IntPtr hwnd)
    {
        if (hwnd == _viewWindow) return;
        DetachViewWindow();
        _viewWindow = hwnd;
        var current = hwnd;
        for (var depth = 0; current != IntPtr.Zero && depth < 12; current = GetParent(current), depth++)
        {
            SubclassWindow(current);
            if (current == _hostWindow) break;
        }
        EnumChildWindows(hwnd, (child, _) =>
        {
            SubclassWindow(child);
            return true;
        }, IntPtr.Zero);
        if (_subclassedWindows.Count == 0)
            MenuError?.Invoke(this, "Shell 菜单消息钩子未能安装。");
    }

    private void SubclassWindow(IntPtr hwnd)
    {
        if (_subclassedWindows.Contains(hwnd)) return;
        if (SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero))
            _subclassedWindows.Add(hwnd);
    }

    private void DetachViewWindow()
    {
        foreach (var hwnd in _subclassedWindows)
        {
            RemoveWindowSubclass(hwnd, _subclassProc, 1);
            // Right-click diagnostics cache class names by HWND; retired view handles must not accumulate across navigation.
            _nativeViewWindowClasses.Remove(hwnd);
        }
        _subclassedWindows.Clear();
        _viewWindow = IntPtr.Zero;
        _lastContentFocusWindow = IntPtr.Zero;
        _columnHeaderArrowTargets = [];
        _columnHeaderArrowTargetsGeneration = -1;
        _columnFilterArrowMouseDown = false;
    }

    private IntPtr ViewWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data)
    {
        // Diagnostic stress runs time every message so a rare slow Shell dispatch cannot hide between samples.
        var timingSample = _traceNativeUserMessages;
        var timingClass = timingSample ? GetDiagnosticWindowClass(hwnd) : string.Empty;
        var timingStartedAt = timingSample ? Stopwatch.GetTimestamp() : 0;
        try
        {
            // Finish the mouse gesture before caching selection; a button-down snapshot can be an intermediate range.
            if (message is 0x0100 or 0x0104 or 0x0111 or 0x0201 or 0x0202 or 0x0203 or 0x0204 or 0x0207)
                NoteNativeUserInput(hwnd);
            if (message == 0x020A && (wParam.ToInt64() & 0x0008) != 0 && CtrlWheelHandler is { } wheelHandler)
            {
                var delta = unchecked((short)(wParam.ToInt64() >> 16));
                if (delta != 0)
                {
                    var notches = _ctrlWheelAccumulator.Add(delta, _viewReadiness.Generation);
                    if (notches == 0) return IntPtr.Zero;
                    var wheelBrowser = _browser;
                    var wheelView = _viewWindow;
                    var wheelGeneration = _viewReadiness.Generation;
                    var viewStyleApplied = wheelHandler(notches * 120);
                    // A setter may navigate or close this host while pumping COM.
                    // Never forward the old accumulated wheel to its replacement.
                    var targetCurrent = ReferenceEquals(wheelBrowser, _browser) && wheelView == _viewWindow &&
                        wheelGeneration == _viewReadiness.Generation && IsVisible;
                    var fallbackDelta = ViewStyleWheelDispatch.GetFallbackDelta(notches, viewStyleApplied, targetCurrent);
                    if (fallbackDelta == 0) return IntPtr.Zero;

                    // The app consumed earlier fractional messages, so forward the accumulated
                    // notch count rather than only this message's fraction if its switch failed.
                    var forwardedWParam = (wParam.ToInt64() & 0xFFFF) | ((long)(short)fallbackDelta << 16);
                    return DefSubclassProc(hwnd, message, new IntPtr(forwardedWParam), lParam);
                }
            }
            // Diagnostic probes can compare message parameters with item-count
            // changes; normal runs do not collect them. Bound samples per host.
            if (_traceNativeUserMessages && message >= 0x0400)
            {
                _nativeUserMessages[message] = _nativeUserMessages.GetValueOrDefault(message) + 1;
                if (_nativeUserMessageSamples.Count < 64)
                    _nativeUserMessageSamples.Add($"{Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart).TotalMilliseconds:F1}ms/{message:X}/{WindowClass(hwnd)}/{wParam.ToInt64():X}/{lParam.ToInt64():X}");
            }
            if (message == 0x0100 && IsTraceKey(wParam.ToInt64()))
                Trace($"native key={wParam.ToInt64():X} hwnd={hwnd} class={WindowClass(hwnd)} focus={FocusClass()}");
            if (message is 0x0007 or 0x0008)
                Trace($"native focus-message={message:X} hwnd={hwnd} class={WindowClass(hwnd)} next={wParam}");
            if (message == 0x020A && !string.IsNullOrEmpty(FocusTracePath) &&
                (wParam.ToInt64() & 0x0008) != 0) // WM_MOUSEWHEEL + MK_CONTROL
            {
                // Observe each native hop without consuming or forwarding it.
                // Compare child/host hops to establish the actual route.
                var delta = unchecked((short)(wParam.ToInt64() >> 16));
                Trace($"wheel ctrl delta={delta} hwnd={hwnd} class={WindowClass(hwnd)} host={hwnd == _hostWindow} view={_viewWindow}");
                QueueWheelViewTrace();
            }
            switch (message)
            {
                case 0x0005: // WM_SIZE
                case 0x0047: // WM_WINDOWPOSCHANGED
                    QueueColumnHeaderGeometryRefresh();
                    break;
                case 0x0007: // WM_SETFOCUS
                    var focusedClass = new StringBuilder(64);
                    GetClassName(hwnd, focusedClass, focusedClass.Capacity);
                    if (focusedClass.ToString() is "DirectUIHWND" or "SysListView32")
                        _lastContentFocusWindow = hwnd;
                    // Modal Shell dialogs can restore focus to an unrelated host's
                    // Static container. Only actual file-view focus selects a pane.
                    var contentFocus = FindContentFocusWindow();
                    if (contentFocus != IntPtr.Zero && (hwnd == contentFocus || IsChild(contentFocus, hwnd)))
                        Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0203: // WM_LBUTTONDBLCLK
                    _headerPresetMenuGate.Reset();
                    Activated?.Invoke(this, EventArgs.Empty);
                    if (TryGetColumnFilterArrowHit(hwnd, lParam, out var columnTarget, out var columnPoint))
                    {
                        _columnFilterArrowMouseDown = true;
                        var browser = _browser;
                        var viewWindow = _viewWindow;
                        var generation = _viewReadiness.Generation;
                        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        {
                            if (ReferenceEquals(browser, _browser) && viewWindow == _viewWindow &&
                                generation == _viewReadiness.Generation && IsVisible)
                                ColumnValueFilterMenuRequested?.Invoke(columnTarget.Key,
                                    columnTarget.DisplayName, columnPoint);
                        }));
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0202: // WM_LBUTTONUP
                    if (_columnFilterArrowMouseDown)
                    {
                        _columnFilterArrowMouseDown = false;
                        return IntPtr.Zero;
                    }
                    if (IsColumnHeaderPoint(hwnd, lParam)) QueueColumnHeaderGeometryRefresh();
                    break;
                case 0x0207: // WM_MBUTTONDOWN
                    _headerPresetMenuGate.Reset();
                    Activated?.Invoke(this, EventArgs.Empty);
                    BeginMiddleClickFolderProbe();
                    break;
                case 0x0208: // WM_MBUTTONUP
                    CompleteMiddleClickFolderProbe();
                    break;
                case 0x0204: // WM_RBUTTONDOWN
                    Activated?.Invoke(this, EventArgs.Empty);
                    if (_headerPresetMenuGate.BeginRightClick(
                        unchecked((nuint)wParam.ToInt64()), GetDiagnosticWindowClass(hwnd)))
                        break;
                    break;
                case 0x007B: // WM_CONTEXTMENU; (-1,-1) means keyboard invocation.
                    if (_headerPresetMenuGate.TryConsume(
                        ShellMenuTargetTracker.DecodeContextPoint(lParam), out var headerMenuPoint))
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background,
                            new Action(() => HeaderPresetMenuRequested?.Invoke(headerMenuPoint)));
                        return IntPtr.Zero;
                    }
                    break;
                case 0x0100: // WM_KEYDOWN
                case 0x0104: // WM_SYSKEYDOWN
                    _headerPresetMenuGate.Reset();
                    break;
                case 0x0211: // WM_ENTERMENULOOP
                    NativeShellTheme.BeginMenu(hwnd);
                    break;
                case 0x0210: // WM_PARENTNOTIFY: apply theme to newly created native children.
                    if ((wParam.ToInt64() & 0xFFFF) == 1)
                    {
                        QueueNativeThemeRefresh();
                        QueueColumnHeaderGeometryRefresh();
                    }
                    break;
                case 0x011F: // WM_MENUSELECT: style only this thread's native popup frames.
                    NativeShellTheme.ApplyMenuFramesFromOwner();
                    break;
                case 0x0121: // WM_ENTERIDLE after the native popup HWND has been created.
                    if (wParam == 2) NativeShellTheme.ApplyMenuFramesFromOwner(); // MSGF_MENU
                    break;
                case 0x004E: // WM_NOTIFY: native file-information tips are created lazily.
                    NativeShellTheme.ApplyTooltip(lParam);
                    break;
                case 0x0212: // WM_EXITMENULOOP
                    NativeShellTheme.EndMenu();
                    _headerPresetMenuGate.Reset();
                    break;
                case 0x0082: // WM_NCDESTROY
                    if (hwnd == _viewWindow) _viewWindow = IntPtr.Zero;
                    _nativeViewWindowClasses.Remove(hwnd);
                    break;
            }
        }
        catch (Exception ex) { MenuError?.Invoke(this, ex.Message); }
        var beforeShell = timingSample ? Stopwatch.GetTimestamp() : 0;
        // Some Shell menus use an internal owner that does not forward
        // WM_ENTERMENULOOP through our view subclass. Observe the dispatch before
        // Shell can create/show its popup, and balance it even on failure.
        var contextDispatch = message is 0x007B or 0x0205; // WM_CONTEXTMENU / WM_RBUTTONUP
        if (contextDispatch) NativeShellTheme.BeginMenu(hwnd);
        IntPtr result;
        try { result = DefSubclassProc(hwnd, message, wParam, lParam); }
        finally { if (contextDispatch) NativeShellTheme.EndMenu(); }
        if (timingSample)
        {
            var shellElapsed = Stopwatch.GetTimestamp() - beforeShell;
            var hostElapsed = beforeShell - timingStartedAt;
            var timingKey = (message, timingClass);
            var previous = _nativeViewMessageTimings.GetValueOrDefault(timingKey);
            _nativeViewMessageTimings[timingKey] = (
                previous.Samples + 1,
                previous.HostTotalTicks + hostElapsed,
                Math.Max(previous.HostMaxTicks, hostElapsed),
                previous.ShellTotalTicks + shellElapsed,
                Math.Max(previous.ShellMaxTicks, shellElapsed));
            if (shellElapsed >= Stopwatch.Frequency / 100 && _nativeViewMessageSlowTimeline.Count < 256)
            {
                var shellEnd = beforeShell + shellElapsed;
                _nativeViewMessageSlowTimeline.Add($"start={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, beforeShell).TotalMilliseconds:F1}ms,end={Stopwatch.GetElapsedTime(_nativeUserMessageTraceStart, shellEnd).TotalMilliseconds:F1}ms,msg={message:X}/{timingClass},hwnd={hwnd:X},w={wParam.ToInt64():X},l={lParam.ToInt64():X},shell-ms={TicksToMilliseconds(shellElapsed):F1}");
            }
        }
        return result;
    }

    private static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private string GetDiagnosticWindowClass(IntPtr hwnd)
    {
        if (_nativeViewWindowClasses.TryGetValue(hwnd, out var windowClass)) return windowClass;
        windowClass = WindowClass(hwnd);
        _nativeViewWindowClasses[hwnd] = windowClass;
        return windowClass;
    }

    private sealed class DiagnosticStageTiming
    {
        private readonly int _sampleMask;
        private long _calls;
        private long _samples;
        private long _sampledTicks;
        private long _maxTicks;

        internal DiagnosticStageTiming(int sampleStride = 64)
        {
            if (sampleStride < 1 || (sampleStride & (sampleStride - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(sampleStride), "Sample stride must be a power of two.");
            _sampleMask = sampleStride - 1;
        }

        internal string Snapshot
        {
            get
            {
                var samples = Interlocked.Read(ref _samples);
                var sampledTicks = Interlocked.Read(ref _sampledTicks);
                var maxTicks = Interlocked.Read(ref _maxTicks);
                return $"calls={Interlocked.Read(ref _calls)},samples={samples},sampled-ms={TicksToMilliseconds(sampledTicks):F3},max-ms={TicksToMilliseconds(maxTicks):F3}";
            }
        }

        internal long Start()
        {
            var call = Interlocked.Increment(ref _calls);
            return ((call - 1) & _sampleMask) == 0 ? Stopwatch.GetTimestamp() : 0;
        }

        internal void Stop(long startedAt)
        {
            if (startedAt == 0) return;
            var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            Interlocked.Increment(ref _samples);
            Interlocked.Add(ref _sampledTicks, elapsedTicks);
            long observed;
            do
            {
                observed = Interlocked.Read(ref _maxTicks);
                if (elapsedTicks <= observed) return;
            } while (Interlocked.CompareExchange(ref _maxTicks, elapsedTicks, observed) != observed);
        }

        internal void Reset()
        {
            Interlocked.Exchange(ref _calls, 0);
            Interlocked.Exchange(ref _samples, 0);
            Interlocked.Exchange(ref _sampledTicks, 0);
            Interlocked.Exchange(ref _maxTicks, 0);
        }
    }

    private Task<string?> ProbeFolderAtPoint(ShellMenuPoint point, string parentPath,
        bool parentIsFileSystem, IntPtr viewWindow)
    {
        return Task.Run(() =>
        {
            try
            {
                var itemName = HitTestListItemName(point, viewWindow);
                if (!parentIsFileSystem)
                    return ShellFolderChildIdentity.TryResolveChildFolder(parentPath, itemName, out var identity)
                        ? identity
                        : null;
                if (!ShellMiddleClickTarget.TryGetChildPath(parentPath, itemName, out var candidate)) return null;
                var itemId = ShellItemId;
                if (SHCreateItemFromParsingName(candidate, IntPtr.Zero, ref itemId, out var item) < 0)
                    return null;
                try
                {
                    if (item.GetAttributes(ShellItemKind.RequestedAttributes, out var attributes) < 0 ||
                        !ShellItemKind.IsFileSystemFolder(attributes) ||
                        item.GetDisplayName(0x80058000, out var pathPointer) < 0) return null;
                    try
                    {
                        var path = Marshal.PtrToStringUni(pathPointer);
                        return path is not null && ShellMiddleClickTarget.IsResolvedChild(parentPath, itemName, path)
                            ? path : null;
                    }
                    finally { Marshal.FreeCoTaskMem(pathPointer); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            catch { return null; }
        });
    }

    private static string? HitTestListItemName(ShellMenuPoint point, IntPtr viewWindow)
    {
        try
        {
            var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
            string? itemName = null;
            var list = false;
            var owned = false;
            var headerOrScrollBar = false;
            for (var depth = 0; element is not null && depth < 32; depth++)
            {
                var type = element.Current.ControlType;
                if (itemName is null && (type == ControlType.ListItem || type == ControlType.DataItem))
                    itemName = element.Current.Name;
                list |= type == ControlType.List;
                headerOrScrollBar |= type == ControlType.Header || type == ControlType.ScrollBar;
                var handle = new IntPtr(element.Current.NativeWindowHandle);
                owned |= handle != IntPtr.Zero &&
                    (handle == viewWindow || IsChild(viewWindow, handle));
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            return owned && list && !headerOrScrollBar ? itemName : null;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (COMException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal SelectedItem? GetSelectedItem()
    {
        if (_browser is null) return null;
        var folderViewId = FolderViewId;
        if (_browser.GetCurrentView(ref folderViewId, out var viewObject) < 0) return null;
        try
        {
            var folderView = (IFolderView)viewObject;
            var arrayId = ShellItemArrayId;
            if (folderView.Items(1, ref arrayId, out var arrayObject) < 0) return null;
            try
            {
                var array = (IShellItemArray)arrayObject;
                if (array.GetCount(out var count) < 0 || count != 1 || array.GetItemAt(0, out var item) < 0)
                    return null;
                try
                {
                    if (item.GetDisplayName(0x80058000, out var pathPtr) < 0) return null;
                    try
                    {
                        var path = Marshal.PtrToStringUni(pathPtr);
                        if (string.IsNullOrWhiteSpace(path)) return null;
                        var result = item.GetAttributes(ShellItemKind.RequestedAttributes, out var attributes);
                        return new SelectedItem(path, result >= 0 && ShellItemKind.IsFileSystemFolder(attributes));
                    }
                    finally { Marshal.FreeCoTaskMem(pathPtr); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            finally { Marshal.ReleaseComObject(arrayObject); }
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    internal string? GetSelectedFolderLocation()
    {
        if (_browser is null || _viewReadiness.Pending) return null;
        var folderViewId = FolderViewId;
        if (_browser.GetCurrentView(ref folderViewId, out var viewObject) < 0 || viewObject is not IFolderView folderView)
        {
            if (viewObject is not null && Marshal.IsComObject(viewObject)) Marshal.ReleaseComObject(viewObject);
            return null;
        }

        try
        {
            var arrayId = ShellItemArrayId;
            if (folderView.Items(1, ref arrayId, out var arrayObject) < 0 || arrayObject is not IShellItemArray items)
            {
                if (arrayObject is not null && Marshal.IsComObject(arrayObject)) Marshal.ReleaseComObject(arrayObject);
                return null;
            }
            try
            {
                if (items.GetCount(out var count) < 0 || count != 1 || items.GetItemAt(0, out var item) < 0)
                    return null;
                try
                {
                    if (item.GetAttributes(0x20000000, out var attributes) < 0 ||
                        item.GetDisplayName(0x80028000, out var name) < 0 || name == IntPtr.Zero) return null;
                    try
                    {
                        return ShellSelectionFolderResolver.TryResolve(Marshal.PtrToStringUni(name), attributes, out var identity)
                            ? identity
                            : null;
                    }
                    finally { Marshal.FreeCoTaskMem(name); }
                }
                finally { if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
            }
            finally { if (Marshal.IsComObject(arrayObject)) Marshal.ReleaseComObject(arrayObject); }
        }
        catch (COMException) { return null; }
        finally { if (Marshal.IsComObject(viewObject)) Marshal.ReleaseComObject(viewObject); }
    }

    internal bool OpenSelectedFolderInNewTab(Action<string> openPath)
    {
        var identity = GetSelectedFolderLocation();
        if (identity is null) return false;
        openPath(identity);
        return true;
    }

    internal string? GetSelectedFolderSearchScope() => GetSelectedItem() is { IsFileSystemFolder: true } selected
        ? selected.Path
        : null;
    int IExplorerBrowserEvents.OnNavigationFailed(IntPtr pidl)
    {
        QueueNativeThemeRefresh();
        var identity = NavigationIdentity(pidl);
        var requestVersion = RetireNavigationRequest(identity);
        var publish = _viewReadiness.Fail(identity);
        QueueNavigationAvailability();
        var navigationVersion = _viewReadiness.NavigationVersion;
        if (!publish || !_viewReadiness.IsCurrent(navigationVersion)) return 0;
        var reconcile = _viewReadiness.RequiresLocationRead;
        if (reconcile)
        {
            // A mixed success/failure batch has no per-request token. Its last
            // callback cannot establish which folder the native view retained.
            ReconcileCurrentLocation(navigationVersion, requestVersion, clearRetry: false);
            if (!_viewReadiness.IsCurrent(navigationVersion)) return 0;
        }
        var isSearch = _searchHistory.TryResolve(pidl, out _);
        if (!_viewReadiness.IsCurrent(navigationVersion)) return 0;
        if (isSearch && IsCurrentNavigationRequest(requestVersion)) _focusWhenReady = false;
        void ReportFailure()
        {
            if (_viewReadiness.IsCurrent(navigationVersion) && IsCurrentNavigationRequest(requestVersion))
                ReportNavigationFailure(_locationUnconfirmed
                    ? "当前 Shell 目录位置暂时无法确认，请刷新重试。"
                    : isSearch ? "无法打开搜索结果。" : "无法打开目录。", requestVersion);
        }
        // Reconciliation queues a location update which also resets status text.
        // Queue the error after it so the older status cannot erase the failure.
        if (reconcile) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReportFailure));
        else ReportFailure();
        return 0;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static bool IsTraceKey(long key) => key is 0x09 or 0x25 or 0x26 or 0x27 or 0x28 or 0x43 or 0x56 or 0x2E or 0x0D;

    private static string FocusClass() => WindowClass(GetFocus());

    private string FolderFocusState()
    {
        if (_browser is null) return "unavailable";
        var iid = FolderViewId;
        if (_browser.GetCurrentView(ref iid, out var viewObject) < 0) return "no-view";
        try
        {
            var view = (IFolderView)viewObject;
            var focused = view.GetFocusedItem(out var focusIndex) >= 0 ? focusIndex.ToString() : "none";
            var marked = view.GetSelectionMarkedItem(out var markedIndex) >= 0 ? markedIndex.ToString() : "none";
            var selected = view.ItemCount(1, out var count) >= 0 ? count.ToString() : "unknown";
            return $"focused:{focused},marked:{marked},selected:{selected}";
        }
        finally { Marshal.ReleaseComObject(viewObject); }
    }

    private static string WindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "none";
        var name = new StringBuilder(80);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private static void Trace(string message)
    {
        if (string.IsNullOrEmpty(FocusTracePath)) return;
        try { File.AppendAllText(FocusTracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { /* Diagnostic output must not affect file operations. */ }
    }

    private bool _wheelTraceQueued;
    private void QueueWheelViewTrace()
    {
        if (_wheelTraceQueued || Dispatcher.HasShutdownStarted) return;
        _wheelTraceQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _wheelTraceQueued = false;
            var view = IntPtr.Zero;
            try
            {
                view = GetFolderView2Pointer();
                if (view == IntPtr.Zero) { Trace("wheel view-unavailable"); return; }
                var hr = GetMethod<GetViewModeAndIconSizeDelegate>(view, 36)(view, out var mode, out var size);
                Trace($"wheel after view={_viewWindow} result={hr:X8} mode={mode} size={size}");
            }
            catch (Exception ex) { Trace($"wheel read-failed={ex.HResult:X8}"); }
            finally { if (view != IntPtr.Zero) Marshal.Release(view); }
        }));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FolderSettings { public uint ViewMode, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePropertyKey { public Guid FormatId; public uint PropertyId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSortColumn { public NativePropertyKey Key; public int Direction; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public int Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInvokeCommand
    {
        public uint Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetViewModeAndIconSizeDelegate(IntPtr instance, out uint mode, out int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetViewModeAndIconSizeDelegate(IntPtr instance, uint mode, int iconSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSortColumnCountDelegate(IntPtr instance, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSortColumnsDelegate(IntPtr instance, [Out] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetSortColumnsDelegate(IntPtr instance, [In] NativeSortColumn[] columns, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetGroupByDelegate(IntPtr instance, out NativePropertyKey key, out int ascending);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetGroupByDelegate(IntPtr instance, ref NativePropertyKey key, int ascending);

    [ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerBrowser
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref Rect rect, ref FolderSettings settings);
        [PreserveSig] int Destroy();
        [PreserveSig] int SetRect(IntPtr hdwp, ref Rect rect);
        [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int SetFolderSettings(ref FolderSettings settings);
        [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] IExplorerBrowserEvents events, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOptions(uint flags);
        [PreserveSig] int GetOptions(out uint flags);
        [PreserveSig] int BrowseToIDList(IntPtr pidl, uint flags);
        [PreserveSig] int BrowseToObject(IntPtr value, uint flags);
        [PreserveSig] int FillFromObject(IntPtr value, uint flags);
        [PreserveSig] int RemoveAll();
        [PreserveSig] int GetCurrentView(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComVisible(true), Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerBrowserEvents
    {
        [PreserveSig] int OnNavigationPending(IntPtr pidl);
        [PreserveSig] int OnViewCreated(IntPtr shellView);
        [PreserveSig] int OnNavigationComplete(IntPtr pidl);
        [PreserveSig] int OnNavigationFailed(IntPtr pidl);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp(bool enter);
        [PreserveSig] int TranslateAccelerator(IntPtr message);
        [PreserveSig] int EnableModeless(bool enable);
        [PreserveSig] int UIActivate(uint state);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr hwnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(IntPtr settings);
        [PreserveSig] int AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr parameter);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidl, uint flags);
        [PreserveSig] int GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint firstId, uint lastId, uint flags);
        [PreserveSig] int InvokeCommand(ref NativeInvokeCommand command);
        [PreserveSig] int GetCommandString(nuint commandId, uint flags, IntPtr reserved, IntPtr name, uint length);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object array);
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(IntPtr pidl, IntPtr point);
        [PreserveSig] int GetSpacing(IntPtr point);
        [PreserveSig] int GetDefaultSpacing(IntPtr point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int index, uint flags);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetPropertyStore(uint flags, ref Guid iid, out IntPtr store);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr list);
        [PreserveSig] int GetAttributes(uint flags, uint mask, out uint attributes);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint requested, out uint attributes);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data);
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, IntPtr data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] keyboardState);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState([In] byte[] keyboardState);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximum);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributes, out uint receivedAttributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string rootPath);
    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")]
    private static extern IntPtr ILClone(IntPtr pidl);
    [DllImport("shell32.dll")]
    private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint displayNameType, out IntPtr name);
}
