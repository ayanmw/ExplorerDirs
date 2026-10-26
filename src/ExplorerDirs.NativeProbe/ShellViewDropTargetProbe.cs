using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Compares Shell drop targets exposed by a direct IShellView against its folder target.
// All transfers are confined to a fresh temporary fixture; this does not use desktop input.
internal static class ShellViewDropTargetProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid FormatEnumeratorId = new("00000103-0000-0000-C000-000000000046");
    private const uint DropEffectCopy = 1;
    private const uint DropEffectMask = 7;
    private const uint SelectAndFocus = 0x1u | 0x4u | 0x10u | 0x40000000u;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NativeProbe", Guid.NewGuid().ToString("N"));
        var sourceFolderPath = Path.Combine(root, "source");
        var targetFolderPath = Path.Combine(root, "target");
        var sourcePath = Path.Combine(sourceFolderPath, "view-target.txt");
        var selectedSourcePath = Path.Combine(sourceFolderPath, "selected-view.txt");
        var registeredSourcePath = Path.Combine(sourceFolderPath, "registered-target.txt");
        var selectedTargetPath = Path.Combine(targetFolderPath, "selected-view.txt");
        var registeredTargetPath = Path.Combine(targetFolderPath, "registered-target.txt");
        var viewTargetPath = Path.Combine(targetFolderPath, "view-target.txt");
        nint parent = 0, sourcePane = 0, targetPane = 0, sourceFolderPidl = 0, sourceItemPidl = 0, targetFolderPidl = 0;
        nint sourceFolder = 0, targetFolder = 0, sourceView = 0, targetView = 0;
        nint sourceViewWindow = 0, targetViewWindow = 0, sourceHostPointer = 0, targetHostPointer = 0, sourceFolderView = 0;
        nint folderDropTarget = 0, viewDropTarget = 0, dataObject = 0, selectedDataObject = 0, childPidls = 0;
        StreamViewHost? sourceHost = null, targetHost = null;
        var sourceCreateAttempted = false;
        var targetCreateAttempted = false;
        try
        {
            Console.WriteLine("shell-view-drop-target-stage: fixture-start");
            Directory.CreateDirectory(sourceFolderPath);
            Directory.CreateDirectory(targetFolderPath);
            File.WriteAllText(sourcePath, "native-shell-drop-fixture");
            File.WriteAllText(selectedSourcePath, "native-shell-selected-view-fixture");
            File.WriteAllText(registeredSourcePath, "native-shell-folder-drop-fixture");
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourceFolderPath, 0, out sourceFolderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourcePath, 0, out sourceItemPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(targetFolderPath, 0, out targetFolderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, sourceFolderPidl, 0, in ShellFolderId, out sourceFolder));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, targetFolderPidl, 0, in ShellFolderId, out targetFolder));
            Console.WriteLine("shell-view-drop-target-stage: shell-folders-bound");

            childPidls = Marshal.AllocHGlobal(IntPtr.Size);
            var relativeSource = ILFindChild(sourceFolderPidl, sourceItemPidl);
            if (relativeSource == 0) throw new InvalidOperationException("The source item is not relative to the source folder.");
            Marshal.WriteIntPtr(childPidls, relativeSource);
            Marshal.ThrowExceptionForHR(SHCreateDataObject(sourceFolderPidl, 1, childPidls, 0, in DataObjectId, out dataObject));

            parent = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated Shell drop target probe",
                0x90000000, 20, 20, 680, 500, 0, 0, 0, 0); // WS_POPUP | WS_VISIBLE; private Desktop only.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            sourcePane = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated Shell drop source pane",
                0x50000000 | 0x02000000 | 0x04000000, 10, 10, 315, 440, parent, 0, 0, 0);
            if (sourcePane == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            targetPane = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated Shell drop target pane",
                0x50000000 | 0x02000000 | 0x04000000, 330, 10, 315, 440, parent, 0, 0, 0);
            if (targetPane == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(sourceFolder, 8)(sourceFolder, sourcePane, ref shellViewId, out sourceView));
            sourceHost = new StreamViewHost(sourcePane, []);
            sourceHost.View = sourceView;
            sourceHostPointer = Marshal.GetComInterfaceForObject(sourceHost, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 305, Bottom = 420 };
            sourceCreateAttempted = true;
            Marshal.ThrowExceptionForHR(At<CreateViewWindow>(sourceView, 9)(sourceView, 0, ref settings, sourceHostPointer, ref bounds, out sourceViewWindow));
            if (sourceViewWindow == 0) throw new InvalidOperationException("Source CreateViewWindow returned no HWND.");
            Console.WriteLine("shell-view-drop-target-stage: source-view-created");
            var folderViewId = FolderViewId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(sourceView, in folderViewId, out sourceFolderView));
            var sourceChildPidl = ILFindChild(sourceFolderPidl, sourceItemPidl);
            if (sourceChildPidl == 0) throw new InvalidOperationException("The selected source item is not a child of its folder.");
            var sourceItemCount = 0;
            var enumerationDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            do
            {
                System.Windows.Forms.Application.DoEvents();
                Marshal.ThrowExceptionForHR(At<ItemCount>(sourceFolderView, 7)(sourceFolderView, 2, out sourceItemCount));
                if (sourceItemCount == 3) break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < enumerationDeadline);
            Console.WriteLine($"shell-view-drop-target-stage: source-items={sourceItemCount}");
            var sourceItemIndex = -1;
            for (var index = 0; index < sourceItemCount; index++)
            {
                var getItemHr = At<GetItem>(sourceFolderView, 6)(sourceFolderView, index, out var itemPidl);
                if (getItemHr < 0 || itemPidl == 0) continue;
                try
                {
                    if (ILIsEqual(sourceChildPidl, itemPidl))
                    {
                        sourceItemIndex = index;
                        break;
                    }
                }
                finally { Marshal.FreeCoTaskMem(itemPidl); }
            }
            if (sourceItemIndex < 0) throw new InvalidOperationException("The Shell view did not enumerate the selected source file.");
            Console.WriteLine($"shell-view-drop-target-stage: source-index={sourceItemIndex}");
            Marshal.ThrowExceptionForHR(At<SelectItem>(sourceFolderView, 15)(sourceFolderView, sourceItemIndex, SelectAndFocus));
            Marshal.ThrowExceptionForHR(At<ItemCount>(sourceFolderView, 7)(sourceFolderView, 1, out var selectedCount));
            if (selectedCount != 1) throw new InvalidOperationException($"Expected one selected source file, observed {selectedCount}.");
            Console.WriteLine("shell-view-drop-target-stage: source-selected");
            var selectedDataObjectId = DataObjectId;
            var selectedDataObjectHr = At<GetItemObject>(sourceView, 15)(sourceView, 1, ref selectedDataObjectId, out selectedDataObject);
            if (selectedDataObjectHr < 0)
                Console.WriteLine($"shell-view-drop-target-candidate: source=selected-view; get-data-object=0x{selectedDataObjectHr:X8}");
            else
                Console.WriteLine("shell-view-drop-target-stage: selected-data-object-acquired");
            if (selectedDataObject != 0) LogDataObjectFormats("selected-view", selectedDataObject);
            LogDataObjectFormats("SHCreateDataObject", dataObject);

            shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(targetFolder, 8)(targetFolder, targetPane, ref shellViewId, out targetView));
            targetHost = new StreamViewHost(targetPane, []);
            targetHost.View = targetView;
            targetHostPointer = Marshal.GetComInterfaceForObject(targetHost, typeof(IStreamProbeBrowser));
            settings = new StreamProbeSettings { Mode = uint.MaxValue };
            bounds = new StreamProbeRect { Right = 305, Bottom = 420 };
            targetCreateAttempted = true;
            Marshal.ThrowExceptionForHR(At<CreateViewWindow>(targetView, 9)(targetView, 0, ref settings, targetHostPointer, ref bounds, out targetViewWindow));
            if (targetViewWindow == 0) throw new InvalidOperationException("Target CreateViewWindow returned no HWND.");
            var dropTargetId = DropTargetId;
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(targetFolder, 8)(targetFolder, targetViewWindow, ref dropTargetId, out folderDropTarget));
            targetHost.RegisterDropTarget(targetViewWindow, folderDropTarget);
            var getItemObjectDropTargetId = DropTargetId;
            var getItemObjectHr = At<GetItemObject>(targetView, 15)(targetView, 0, ref getItemObjectDropTargetId, out viewDropTarget);
            if (getItemObjectHr < 0)
                Console.WriteLine($"shell-view-drop-target-candidate: target=IShellView.GetItemObject; query=0x{getItemObjectHr:X8}");

            var windowResult = At<GetViewWindow>(targetView, 3)(targetView, out var targetWindow);
            Marshal.ThrowExceptionForHR(windowResult);
            if (!GetWindowRect(targetWindow, out var boundsOnScreen))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var point = new PointL { X = boundsOnScreen.Left + Math.Max(1, boundsOnScreen.Right - boundsOnScreen.Left) / 2,
                Y = boundsOnScreen.Top + Math.Max(1, boundsOnScreen.Bottom - boundsOnScreen.Top) / 2 };

            var viewTargetResult = viewDropTarget != 0 && TryTransfer(viewDropTarget, dataObject, point, viewTargetPath, "IShellView.GetItemObject");
            var selectedDataObjectResult = selectedDataObject != 0 && TryTransfer(folderDropTarget, selectedDataObject, point,
                selectedTargetPath, "selected view IDataObject");
            // Keep the registered folder target as a separate candidate with its own Shell data object.
            var secondSourcePath = registeredSourcePath;
            var secondSourcePidl = nint.Zero;
            var secondChildArray = Marshal.AllocHGlobal(IntPtr.Size);
            nint secondDataObject = 0;
            bool folderTargetResult;
            try
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(secondSourcePath, 0, out secondSourcePidl, 0, out _));
                var secondChild = ILFindChild(sourceFolderPidl, secondSourcePidl);
                if (secondChild == 0) throw new InvalidOperationException("The second source item is not relative to the source folder.");
                Marshal.WriteIntPtr(secondChildArray, secondChild);
                Marshal.ThrowExceptionForHR(SHCreateDataObject(sourceFolderPidl, 1, secondChildArray, 0, in DataObjectId, out secondDataObject));
                folderTargetResult = TryTransfer(folderDropTarget, secondDataObject, point, registeredTargetPath,
                    "IShellFolder.CreateViewObject (registered HWND target)");
            }
            finally
            {
                if (secondDataObject != 0) Marshal.Release(secondDataObject);
                if (secondSourcePidl != 0) Marshal.FreeCoTaskMem(secondSourcePidl);
                Marshal.FreeHGlobal(secondChildArray);
            }

            var sourcePreserved = File.Exists(sourcePath) && File.ReadAllText(sourcePath) == "native-shell-drop-fixture" &&
                File.Exists(selectedSourcePath) && File.ReadAllText(selectedSourcePath) == "native-shell-selected-view-fixture" &&
                File.Exists(secondSourcePath) && File.ReadAllText(secondSourcePath) == "native-shell-folder-drop-fixture";
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The isolated drop-target probe changed the user's foreground window.");
            Console.WriteLine($"shell-view-drop-target-probe: view-target-copy={viewTargetResult}; selected-view-data-copy={selectedDataObjectResult}; registered-folder-target-copy={folderTargetResult}; sources-preserved={sourcePreserved}; foreground-unchanged=True; actual-DoDragDrop=False");
            if (!folderTargetResult || !sourcePreserved)
                throw new InvalidOperationException("The registered folder drop target did not perform a non-destructive isolated copy.");
            return 0;
        }
        finally
        {
            if (targetHost is not null && targetViewWindow != 0)
                targetHost.RevokeDropTarget(targetViewWindow);
            if (targetView != 0 && targetCreateAttempted)
                Marshal.ThrowExceptionForHR(At<DestroyViewWindow>(targetView, 10)(targetView));
            if (sourceView != 0 && sourceCreateAttempted)
                Marshal.ThrowExceptionForHR(At<DestroyViewWindow>(sourceView, 10)(sourceView));
            targetHost?.Dispose();
            sourceHost?.Dispose();
            if (targetHostPointer != 0) Marshal.Release(targetHostPointer);
            if (sourceHostPointer != 0) Marshal.Release(sourceHostPointer);
            if (viewDropTarget != 0) Marshal.Release(viewDropTarget);
            if (folderDropTarget != 0) Marshal.Release(folderDropTarget);
            if (selectedDataObject != 0) Marshal.Release(selectedDataObject);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (sourceFolderView != 0) Marshal.Release(sourceFolderView);
            if (sourceView != 0) Marshal.Release(sourceView);
            if (targetView != 0) Marshal.Release(targetView);
            if (sourceFolder != 0) Marshal.Release(sourceFolder);
            if (targetFolder != 0) Marshal.Release(targetFolder);
            if (sourceFolderPidl != 0) Marshal.FreeCoTaskMem(sourceFolderPidl);
            if (sourceItemPidl != 0) Marshal.FreeCoTaskMem(sourceItemPidl);
            if (targetFolderPidl != 0) Marshal.FreeCoTaskMem(targetFolderPidl);
            if (childPidls != 0) Marshal.FreeHGlobal(childPidls);
            if (sourcePane != 0 && !DestroyWindow(sourcePane))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (targetPane != 0 && !DestroyWindow(targetPane))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // Private Desktop top-level windows can block explicit DestroyWindow; the watchdog process owns final cleanup.
            try { Directory.Delete(root, recursive: true); }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }
    }

    private static bool TryTransfer(nint target, nint dataObject, PointL point, string destination, string label)
    {
        var allowedEffect = DropEffectCopy;
        var enterHr = At<DragEnter>(target, 3)(target, dataObject, 0x0001, ref point, ref allowedEffect);
        if (enterHr < 0) throw new COMException($"{label} DragEnter failed", enterHr);
        var enterEffect = allowedEffect & DropEffectMask;
        if (enterEffect == 0 || (enterEffect & ~DropEffectCopy) != 0)
        {
            _ = At<DragLeave>(target, 5)(target);
            Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; effect=0x{allowedEffect:X}");
            return false;
        }

        var overEffect = DropEffectCopy;
        var overHr = At<DragOver>(target, 4)(target, 0x0001, ref point, ref overEffect);
        if (overHr < 0) throw new COMException($"{label} DragOver failed", overHr);
        if ((overEffect & DropEffectMask) != DropEffectCopy)
        {
            _ = At<DragLeave>(target, 5)(target);
            Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; drag-over=0x{overHr:X8}; effect=0x{overEffect:X}");
            return false;
        }

        var dropEffect = overEffect & DropEffectMask;
        var dropHr = At<Drop>(target, 6)(target, dataObject, 0x0001, ref point, ref dropEffect);
        if (dropHr < 0) throw new COMException($"{label} Drop failed", dropHr);
        var copied = File.Exists(destination) && File.ReadAllText(destination).StartsWith("native-shell-", StringComparison.Ordinal);
        var targetFiles = Directory.GetFiles(Path.GetDirectoryName(destination)!).Select(Path.GetFileName).Order().ToArray();
        Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; drag-over=0x{overHr:X8}; drop=0x{dropHr:X8}; effect=0x{dropEffect:X}; copied={copied}; destination-files={string.Join(',', targetFiles)}");
        return copied;
    }

    private static void LogDataObjectFormats(string label, nint dataObject)
    {
        var hr = At<EnumFormatEtc>(dataObject, 8)(dataObject, 1, out var enumerator); // DATADIR_GET.
        if (hr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-formats: source={label}; enum=0x{hr:X8}");
            return;
        }

        try
        {
            var formats = new List<string>();
            while (true)
            {
                var nextHr = At<NextFormatEtc>(enumerator, 3)(enumerator, 1, out var format, out var fetched);
                if (nextHr < 0) Marshal.ThrowExceptionForHR(nextHr);
                if (fetched == 0) break;
                var formatName = new System.Text.StringBuilder(128);
                var nameLength = GetClipboardFormatNameW(format.Format, formatName, formatName.Capacity);
                var name = format.Format == 15 ? "CF_HDROP" : nameLength > 0 ? formatName.ToString() : $"CF_{format.Format}";
                formats.Add($"{name}(aspect=0x{format.Aspect:X},index={format.Index},tymed=0x{format.Medium:X})");
            }
            Console.WriteLine($"shell-view-drop-target-formats: source={label}; formats={string.Join(';', formats)}");
        }
        finally { Marshal.Release(enumerator); }
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)] private struct PointL { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct FormatEtc
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Medium;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings,
        nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemCount(nint self, uint scope, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItem(nint self, int index, out nint itemPidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SelectItem(nint self, int index, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumFormatEtc(nint self, uint direction, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NextFormatEtc(nint self, uint count, out FormatEtc format, out uint fetched);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragEnter(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragOver(nint self, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragLeave(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Drop(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern nint ILFindChild(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll")] private static extern int SHCreateDataObject(nint parent, uint count, nint children, nint sourceDataObject, in Guid iid, out nint dataObject);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatNameW(ushort format, System.Text.StringBuilder name, int maxCount);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
}
