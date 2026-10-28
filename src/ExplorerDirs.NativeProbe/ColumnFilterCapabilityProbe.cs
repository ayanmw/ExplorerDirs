using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;
using ExplorerDirs.App;

// Runs on PrivateDesktopShellProbe's separate Desktop. This is a capability
// probe only; it does not install a product filter or change saved view state.
internal static class ColumnFilterCapabilityProbe
{
    private static readonly Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    private static readonly Guid BrowserId = new("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6");
    private static readonly Guid FilterSiteId = new("C0A651F5-B48B-11D2-B5ED-006097C686F6");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid ItemTypeTextFormatId = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly SubclassProc HeaderParentProc = ObserveHeaderParent;
    private const uint HdmFirst = 0x1200;
    private const uint HdmGetItemCount = HdmFirst;
    private const uint HdmGetItemW = HdmFirst + 11;
    private const uint HdmSetItemW = HdmFirst + 12;
    private const uint HdmGetItemDropDownRect = HdmFirst + 25;
    private const uint HdfSplitButton = 0x01000000;
    private const uint HdiFormat = 0x0004;
    private const uint WmNotify = 0x004E;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const int HdnDropdown = -318;
    private const nuint HeaderSubclassId = 0xED5711;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ColumnFilter-" + Guid.NewGuid().ToString("N"));
        nint browser = 0, parent = 0, shellView = 0, folderView = 0, filterSite = 0, filterUnknown = 0;
        uint initialized = 0;
        bool filterInstalled = false;
        var filter = new PropertyProbeFilter();
        var headerObserver = new HeaderNotifyObserver();
        int headerCapability = 0;
        try
        {
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "alpha.txt"), "alpha");
            File.WriteAllText(Path.Combine(fixture, "beta.txt"), "beta");
            File.WriteAllText(Path.Combine(fixture, "gamma.bin"), "gamma");

            var browserClass = BrowserClass;
            var browserId = BrowserId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref browserClass, 0, 1, ref browserId, out browser));
            parent = CreateWindowExW(0, "STATIC", "Explorer Dirs column filter probe",
                0x90000000, 20, 20, 820, 620, 0, 0, 0, 0); // Visible only on the isolated probe Desktop.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var bounds = new ProbeRect { Right = 820, Bottom = 620 };
            var settings = new ProbeSettings { Mode = 4 };
            Marshal.ThrowExceptionForHR(At<Initialize>(browser, 3)(browser, parent, ref bounds, ref settings));
            initialized = 1;
            Marshal.ThrowExceptionForHR(At<SetOptions>(browser, 11)(browser, 0x40u | 0x80u));
            Navigate(browser, fixture);
            Pump(500);

            // Use the same Shell column API and ordering path as the application.
            ViewStyleProbe.Run(browser);
            var shellViewId = ShellViewId;
            var folderViewId = FolderViewId;
            Marshal.ThrowExceptionForHR(At<GetView>(browser, 17)(browser, ref shellViewId, out shellView));
            Marshal.ThrowExceptionForHR(At<GetView>(browser, 17)(browser, ref folderViewId, out folderView));
            Marshal.ThrowExceptionForHR(At<GetWindow>(shellView, 3)(shellView, out var viewWindow));
            var columnResult = ShellColumnManager.Capture(folderView, out var columns);
            Console.WriteLine($"column-filter-probe: visible-columns-hr=0x{columnResult:X8}; " +
                $"visible-columns={(columns is null ? "unavailable" : string.Join(",", columns.Select(column => $"{column.Key.FormatId:B}/{column.Key.PropertyId}")))}");
            if (columns is { Count: 3 })
            {
                var reordered = columns.OrderBy(column => column.Key.PropertyId switch
                {
                    4 => 0,  // Type.
                    10 => 1, // Name.
                    12 => 2, // Size.
                    _ => 3
                }).ToArray();
                Marshal.ThrowExceptionForHR(ShellColumnManager.Apply(folderView, reordered));
                Pump(200);
                var reorderedResult = ShellColumnManager.Capture(folderView, out var reorderedColumns);
                Console.WriteLine($"column-filter-probe: reordered-columns-hr=0x{reorderedResult:X8}; " +
                    $"reordered-property-ids={(reorderedColumns is null ? "unavailable" : string.Join(",", reorderedColumns.Select(column => column.Key.PropertyId)))}");
            }

            var header = FindChildByClass(viewWindow, "SysHeader32");
            if (header == 0)
            {
                Console.WriteLine($"column-filter-probe: header=SysHeader32-unavailable; child-classes={ChildClasses(viewWindow)}");
                ReportAutomationHeader(viewWindow);
            }
            else
            {
                headerCapability = ProbeSplitButton(header, headerObserver);
                Console.WriteLine($"column-filter-probe: header-items={SendMessageW(header, HdmGetItemCount, 0, 0)}; " +
                    $"split-button-capability={(headerCapability == 1 ? "retained" : headerCapability == 0 ? "rejected" : "error")}; " +
                    $"dropdown-notifications={headerObserver.DropdownCount}; dropdown-index={headerObserver.LastItemIndex}");
            }

            var setSiteResult = Marshal.QueryInterface(browser, in FilterSiteId, out filterSite);
            Marshal.ThrowExceptionForHR(setSiteResult);
            filterUnknown = Marshal.GetIUnknownForObject(filter);
            var setFilter = At<SetFilter>(filterSite, 3);
            Marshal.ThrowExceptionForHR(setFilter(filterSite, filterUnknown));
            filterInstalled = true;
            Marshal.ThrowExceptionForHR(At<Refresh>(shellView, 8)(shellView));
            var finalCount = WaitForFilter(folderView, filter, TimeSpan.FromSeconds(3));
            Console.WriteLine($"column-filter-probe: filter-callbacks={filter.ShouldShowCalls}/{filter.CompletedShouldShowCalls}; " +
                $"shellfolder2-queries={filter.Folder2QueryCalls}; itemtype-success={filter.PropertySuccessCalls}; " +
                $"itemtype-failures={filter.PropertyFailureCalls}; sample-values={filter.SampleValues}; visible-items={finalCount}");

            Marshal.ThrowExceptionForHR(setFilter(filterSite, 0));
            filterInstalled = false;
            Marshal.ThrowExceptionForHR(At<Refresh>(shellView, 8)(shellView));
            var restoredCount = WaitForCount(folderView, 3, TimeSpan.FromSeconds(3));
            var parentVisible = IsWindowVisible(parent);
            Console.WriteLine($"column-filter-probe: filter-removed=True; items-restored={restoredCount == 3}; " +
                $"foreground-unchanged={GetForegroundWindow() == foreground}; private-desktop-parent-visible={parentVisible}");
            return GetForegroundWindow() == foreground && parentVisible && restoredCount == 3 ? 0 : 1;
        }
        finally
        {
            if (filterInstalled && filterSite != 0)
            {
                _ = At<SetFilter>(filterSite, 3)(filterSite, 0);
                if (shellView != 0) _ = At<Refresh>(shellView, 8)(shellView);
            }
            headerObserver.Dispose();
            if (filterUnknown != 0) Marshal.Release(filterUnknown);
            if (filterSite != 0) Marshal.Release(filterSite);
            if (folderView != 0) Marshal.Release(folderView);
            if (shellView != 0) Marshal.Release(shellView);
            if (initialized != 0) _ = At<Destroy>(browser, 4)(browser);
            if (browser != 0) Marshal.Release(browser);
            // The probe process owns the private Desktop parent HWND until exit.
            try { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            GC.KeepAlive(filter);
        }
    }

    private static int ProbeSplitButton(nint header, HeaderNotifyObserver observer)
    {
        var count = unchecked((int)SendMessageW(header, HdmGetItemCount, 0, 0));
        if (count <= 0) return -1;
        var item = new HeaderItem { Mask = HdiFormat };
        if (SendMessageW(header, HdmGetItemW, 0, ref item) == 0) return -1;
        var originalFormat = item.Format;
        item.Format |= unchecked((int)HdfSplitButton);
        try
        {
            if (SendMessageW(header, HdmSetItemW, 0, ref item) == 0) return 0;
            var readback = new HeaderItem { Mask = HdiFormat };
            if (SendMessageW(header, HdmGetItemW, 0, ref readback) == 0 ||
                (unchecked((uint)readback.Format) & HdfSplitButton) == 0)
                return 0;

            var parent = GetParent(header);
            if (parent == 0 || !observer.Attach(parent)) return 1;
            var rect = new ProbeRect();
            if (SendMessageW(header, HdmGetItemDropDownRect, 0, ref rect) == 0) return 1;
            var points = new[] { new ProbePoint { X = rect.Left, Y = rect.Top }, new ProbePoint { X = rect.Right, Y = rect.Bottom } };
            _ = MapWindowPoints(0, header, points, 2);
            var x = (points[0].X + points[1].X) / 2;
            var y = (points[0].Y + points[1].Y) / 2;
            var position = (nint)((y << 16) | (x & 0xFFFF));
            _ = SendMessageW(header, WmLButtonDown, 1, position);
            _ = SendMessageW(header, WmLButtonUp, 0, position);
            Pump(100);
            return 1;
        }
        finally
        {
            item.Format = originalFormat;
            _ = SendMessageW(header, HdmSetItemW, 0, ref item);
        }
    }

    private static int WaitForFilter(nint view, PropertyProbeFilter filter, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        int count;
        do
        {
            Application.DoEvents();
            count = ReadItemCount(view);
            if (count == 3 && filter.CompletedShouldShowCalls >= 3) return count;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        return count;
    }

    private static int WaitForCount(nint view, int expected, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        int count;
        do
        {
            Application.DoEvents();
            count = ReadItemCount(view);
            if (count == expected) return count;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < until);
        return count;
    }

    private static int ReadItemCount(nint view) => At<ItemCount>(view, 7)(view, 2, out var count) < 0 ? -1 : count;

    private static void Navigate(nint browser, string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { Marshal.ThrowExceptionForHR(At<Browse>(browser, 13)(browser, pidl, 0)); }
        finally { Marshal.FreeCoTaskMem(pidl); }
    }

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static nint FindChildByClass(nint root, string expectedClass)
    {
        nint result = 0;
        _ = EnumChildWindows(root, (window, _) =>
        {
            var value = new StringBuilder(128);
            if (GetClassNameW(window, value, value.Capacity) > 0 &&
                string.Equals(value.ToString(), expectedClass, StringComparison.Ordinal))
            {
                result = window;
                return false;
            }
            return true;
        }, 0);
        return result;
    }

    private static string ChildClasses(nint root)
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        _ = EnumChildWindows(root, (window, _) =>
        {
            var value = new StringBuilder(128);
            if (GetClassNameW(window, value, value.Capacity) > 0) classes.Add(value.ToString());
            return true;
        }, 0);
        return string.Join(",", classes);
    }

    private static void ReportAutomationHeader(nint viewWindow)
    {
        try
        {
            var content = FindChildByClass(viewWindow, "DirectUIHWND");
            var root = AutomationElement.FromHandle(content == 0 ? viewWindow : content);
            var headers = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));
            Console.WriteLine($"column-filter-probe: uia-content={(content == 0 ? "view-window" : "DirectUIHWND")}; " +
                $"uia-headers={headers.Count}");
            for (var index = 0; index < headers.Count; index++)
            {
                var header = headers[index];
                var buttons = header.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton));
                Console.WriteLine($"column-filter-probe: uia-header[{index}]=name:{header.Current.Name}; " +
                    $"class:{header.Current.ClassName}; framework:{header.Current.FrameworkId}; " +
                    $"rect:{header.Current.BoundingRectangle}; split-buttons={buttons.Count}");
                for (var buttonIndex = 0; buttonIndex < buttons.Count; buttonIndex++)
                {
                    var button = buttons[buttonIndex];
                    Console.WriteLine($"column-filter-probe: uia-split[{index},{buttonIndex}]=name:{button.Current.Name}; " +
                        $"class:{button.Current.ClassName}; framework:{button.Current.FrameworkId}; " +
                        $"rect:{button.Current.BoundingRectangle}; offscreen:{button.Current.IsOffscreen}; " +
                        $"focusable:{button.Current.IsKeyboardFocusable}; patterns={string.Join(",", button.GetSupportedPatterns().Select(pattern => pattern.ProgrammaticName))}");
                    if (index == 0 && buttonIndex == 0)
                        Console.WriteLine($"column-filter-probe: first-header-expand-collapse={ProbeExpandCollapse(button)}");
                }
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            Console.WriteLine($"column-filter-probe: uia-header-query=unavailable; error={exception.GetType().Name}");
        }
    }

    private static string ProbeExpandCollapse(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var value) ||
            value is not ExpandCollapsePattern pattern)
            return "unavailable";
        try
        {
            if (pattern.Current.ExpandCollapseState != ExpandCollapseState.Collapsed)
                pattern.Collapse();
            pattern.Expand();
            Pump(100);
            var expandedState = pattern.Current.ExpandCollapseState;
            var popup = FindWindowW("#32768", null);
            string[] popupItems = popup == 0 ? [] : AutomationElement.FromHandle(popup)
                .FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem))
                .Cast<AutomationElement>()
                .Select(item => item.Current.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            pattern.Collapse();
            Pump(50);
            return $"supported; after-expand={expandedState}; popup={(popup == 0 ? "none" : string.Join("|", popupItems))}; " +
                $"after-collapse={pattern.Current.ExpandCollapseState}";
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return $"failed:{exception.GetType().Name}";
        }
    }

    private static nint ObserveHeaderParent(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (message == WmNotify && lParam != 0 && GCHandle.FromIntPtr(data).Target is HeaderNotifyObserver observer)
            observer.Record(lParam);
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    private sealed class PropertyProbeFilter : IShellFolderFilter
    {
        private int _shouldShowCalls, _completedShouldShowCalls, _folder2QueryCalls, _propertySuccessCalls, _propertyFailureCalls;
        private readonly SortedSet<string> _sampleValues = new(StringComparer.OrdinalIgnoreCase);
        public int ShouldShowCalls => Volatile.Read(ref _shouldShowCalls);
        public int CompletedShouldShowCalls => Volatile.Read(ref _completedShouldShowCalls);
        public int Folder2QueryCalls => Volatile.Read(ref _folder2QueryCalls);
        public int PropertySuccessCalls => Volatile.Read(ref _propertySuccessCalls);
        public int PropertyFailureCalls => Volatile.Read(ref _propertyFailureCalls);
        public string SampleValues => string.Join("|", _sampleValues.Take(8));

        public int ShouldShow(nint shellFolder, nint folderPidl, nint itemPidl)
        {
            Interlocked.Increment(ref _shouldShowCalls);
            try
            {
                var key = new ShellColumnManager.NativeKey { FormatId = ItemTypeTextFormatId, PropertyId = 4 };
                var query = Marshal.QueryInterface(shellFolder, in ShellFolder2Id, out var folder2);
                if (query < 0 || folder2 == 0)
                {
                    Interlocked.Increment(ref _propertyFailureCalls);
                    if (folder2 != 0) Marshal.Release(folder2);
                    return 0; // Keep the item visible; a failed property read must fail open.
                }

                Interlocked.Increment(ref _folder2QueryCalls);
                var variant = Marshal.AllocCoTaskMem(IntPtr.Size == 8 ? 24 : 16);
                try
                {
                    Marshal.Copy(new byte[IntPtr.Size == 8 ? 24 : 16], 0, variant, IntPtr.Size == 8 ? 24 : 16);
                    var result = At<GetDetailsEx>(folder2, 17)(folder2, itemPidl, ref key, variant);
                    if (result < 0)
                    {
                        Interlocked.Increment(ref _propertyFailureCalls);
                        return 0;
                    }

                    var value = Marshal.GetObjectForNativeVariant(variant);
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(null)";
                    lock (_sampleValues) _sampleValues.Add(text);
                    Interlocked.Increment(ref _propertySuccessCalls);
                    return 0; // S_OK: the probe never filters fixture items.
                }
                finally
                {
                    _ = VariantClear(variant);
                    Marshal.FreeCoTaskMem(variant);
                    Marshal.Release(folder2);
                }
            }
            catch
            {
                Interlocked.Increment(ref _propertyFailureCalls);
                return 0;
            }
            finally
            {
                Interlocked.Increment(ref _completedShouldShowCalls);
            }
        }

        public int GetEnumFlags(nint shellFolder, nint folderPidl, out nint hwnd, ref uint flags)
        {
            hwnd = 0;
            flags = 0x60; // SHCONTF_FOLDERS | SHCONTF_NONFOLDERS.
            return 0;
        }
    }

    private sealed class HeaderNotifyObserver : IDisposable
    {
        private GCHandle _self;
        private nint _parent;
        internal int DropdownCount { get; private set; }
        internal int LastItemIndex { get; private set; } = -1;

        internal bool Attach(nint parent)
        {
            if (_parent != 0) return _parent == parent;
            _self = GCHandle.Alloc(this);
            if (!SetWindowSubclass(parent, HeaderParentProc, HeaderSubclassId, GCHandle.ToIntPtr(_self)))
            {
                _self.Free();
                return false;
            }
            _parent = parent;
            return true;
        }

        internal void Record(nint headerPointer)
        {
            try
            {
                var notification = Marshal.PtrToStructure<HeaderNotification>(headerPointer);
                if (notification.Header.Code != HdnDropdown) return;
                DropdownCount++;
                LastItemIndex = notification.ItemIndex;
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (_parent != 0 && IsWindow(_parent)) _ = RemoveWindowSubclass(_parent, HeaderParentProc, HeaderSubclassId);
            _parent = 0;
            if (_self.IsAllocated) _self.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ProbeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeSettings { public uint Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct HeaderItem
    {
        public uint Mask;
        public int Width;
        public nint Text;
        public nint Bitmap;
        public int TextLength;
        public int Format;
        public nint LParam;
        public int Image;
        public int Order;
        public uint Type;
        public nint Filter;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyHeader { public nint WindowFrom; public nuint IdFrom; public int Code; }
    [StructLayout(LayoutKind.Sequential)] private struct HeaderNotification { public NotifyHeader Header; public int ItemIndex, Button; public nint Item; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Initialize(nint self, nint parent, ref ProbeRect rect, ref ProbeSettings settings);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Destroy(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOptions(nint self, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetView(nint self, ref Guid iid, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetWindow(nint self, out nint hwnd);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Refresh(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetFilter(nint self, nint filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsEx(nint self, nint itemPidl, ref ShellColumnManager.NativeKey key, nint variant);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("oleaut32.dll")] private static extern int VariantClear(nint variant);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint data);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref HeaderItem item);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref ProbeRect rect);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, [In, Out] ProbePoint[] points, uint count);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
    private delegate bool EnumChildProc(nint hwnd, nint data);
}
