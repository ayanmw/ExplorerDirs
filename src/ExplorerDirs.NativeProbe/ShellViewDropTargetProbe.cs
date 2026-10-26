using System.Runtime.InteropServices;
using ExplorerDirs.NativeProbe;

// Compares Shell drop targets exposed by a direct IShellView against its folder target.
// All transfers are confined to a fresh temporary fixture; this does not use desktop input.
internal static class ShellViewDropTargetProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private const uint DropEffectCopy = 1;
    private const uint DropEffectMask = 7;

    internal static int Run()
    {
        var foreground = GetForegroundWindow();
        var root = Path.Combine(Path.GetTempPath(), "ExplorerDirs-NativeProbe", Guid.NewGuid().ToString("N"));
        var sourceFolderPath = Path.Combine(root, "source");
        var targetFolderPath = Path.Combine(root, "target");
        var sourcePath = Path.Combine(sourceFolderPath, "view-target.txt");
        var registeredTargetPath = Path.Combine(targetFolderPath, "folder-target.txt");
        var viewTargetPath = Path.Combine(targetFolderPath, "view-target.txt");
        nint parent = 0, pane = 0, sourceFolderPidl = 0, sourceItemPidl = 0, targetFolderPidl = 0;
        nint sourceFolder = 0, targetFolder = 0, view = 0, viewWindow = 0, hostPointer = 0;
        nint folderDropTarget = 0, viewDropTarget = 0, dataObject = 0, childPidls = 0;
        StreamViewHost? host = null;
        var createAttempted = false;
        try
        {
            Directory.CreateDirectory(sourceFolderPath);
            Directory.CreateDirectory(targetFolderPath);
            File.WriteAllText(sourcePath, "native-shell-drop-fixture");
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourceFolderPath, 0, out sourceFolderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourcePath, 0, out sourceItemPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(targetFolderPath, 0, out targetFolderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, sourceFolderPidl, 0, in ShellFolderId, out sourceFolder));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, targetFolderPidl, 0, in ShellFolderId, out targetFolder));

            childPidls = Marshal.AllocHGlobal(IntPtr.Size);
            var relativeSource = ILFindChild(sourceFolderPidl, sourceItemPidl);
            if (relativeSource == 0) throw new InvalidOperationException("The source item is not relative to the source folder.");
            Marshal.WriteIntPtr(childPidls, relativeSource);
            Marshal.ThrowExceptionForHR(SHCreateDataObject(sourceFolderPidl, 1, childPidls, 0, in DataObjectId, out dataObject));

            parent = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated Shell drop target probe",
                0x90000000, 20, 20, 680, 500, 0, 0, 0, 0); // WS_POPUP | WS_VISIBLE; private Desktop only.
            if (parent == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            pane = CreateWindowExW(0, "STATIC", "Explorer Dirs isolated Shell drop pane",
                0x50000000 | 0x02000000 | 0x04000000, 10, 10, 640, 440, parent, 0, 0, 0);
            if (pane == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var shellViewId = ShellViewId;
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(targetFolder, 8)(targetFolder, pane, ref shellViewId, out view));
            host = new StreamViewHost(pane, []);
            host.View = view;
            hostPointer = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
            var settings = new StreamProbeSettings { Mode = uint.MaxValue };
            var bounds = new StreamProbeRect { Right = 620, Bottom = 420 };
            createAttempted = true;
            Marshal.ThrowExceptionForHR(At<CreateViewWindow>(view, 9)(view, 0, ref settings, hostPointer, ref bounds, out viewWindow));
            if (viewWindow == 0) throw new InvalidOperationException("CreateViewWindow returned no HWND.");
            var dropTargetId = DropTargetId;
            Marshal.ThrowExceptionForHR(At<CreateViewObject>(targetFolder, 8)(targetFolder, viewWindow, ref dropTargetId, out folderDropTarget));
            host.RegisterDropTarget(viewWindow, folderDropTarget);
            var getItemObjectDropTargetId = DropTargetId;
            var getItemObjectHr = At<GetItemObject>(view, 15)(view, 0, ref getItemObjectDropTargetId, out viewDropTarget);
            if (getItemObjectHr < 0)
                Console.WriteLine($"shell-view-drop-target-candidate: target=IShellView.GetItemObject; query=0x{getItemObjectHr:X8}");

            var windowResult = At<GetViewWindow>(view, 3)(view, out var targetWindow);
            Marshal.ThrowExceptionForHR(windowResult);
            if (!GetWindowRect(targetWindow, out var boundsOnScreen))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var point = new PointL { X = boundsOnScreen.Left + Math.Max(1, boundsOnScreen.Right - boundsOnScreen.Left) / 2,
                Y = boundsOnScreen.Top + Math.Max(1, boundsOnScreen.Bottom - boundsOnScreen.Top) / 2 };

            var viewTargetResult = viewDropTarget != 0 && TryTransfer(viewDropTarget, dataObject, point, viewTargetPath, "IShellView.GetItemObject");
            // Keep the registered folder target as a separate candidate with its own Shell data object.
            var secondSourcePath = Path.Combine(sourceFolderPath, "folder-target.txt");
            File.WriteAllText(secondSourcePath, "native-shell-folder-drop-fixture");
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
                File.Exists(secondSourcePath) && File.ReadAllText(secondSourcePath) == "native-shell-folder-drop-fixture";
            if (GetForegroundWindow() != foreground)
                throw new InvalidOperationException("The isolated drop-target probe changed the user's foreground window.");
            Console.WriteLine($"shell-view-drop-target-probe: view-target-copy={viewTargetResult}; registered-folder-target-copy={folderTargetResult}; sources-preserved={sourcePreserved}; foreground-unchanged=True; actual-DoDragDrop=False");
            if (!folderTargetResult || !sourcePreserved)
                throw new InvalidOperationException("The registered folder drop target did not perform a non-destructive isolated copy.");
            return 0;
        }
        finally
        {
            if (host is not null && viewWindow != 0)
                host.RevokeDropTarget(viewWindow);
            if (view != 0 && createAttempted)
                Marshal.ThrowExceptionForHR(At<DestroyViewWindow>(view, 10)(view));
            host?.Dispose();
            if (hostPointer != 0) Marshal.Release(hostPointer);
            if (viewDropTarget != 0) Marshal.Release(viewDropTarget);
            if (folderDropTarget != 0) Marshal.Release(folderDropTarget);
            if (dataObject != 0) Marshal.Release(dataObject);
            if (view != 0) Marshal.Release(view);
            if (sourceFolder != 0) Marshal.Release(sourceFolder);
            if (targetFolder != 0) Marshal.Release(targetFolder);
            if (sourceFolderPidl != 0) Marshal.FreeCoTaskMem(sourceFolderPidl);
            if (sourceItemPidl != 0) Marshal.FreeCoTaskMem(sourceItemPidl);
            if (targetFolderPidl != 0) Marshal.FreeCoTaskMem(targetFolderPidl);
            if (childPidls != 0) Marshal.FreeHGlobal(childPidls);
            if (pane != 0 && !DestroyWindow(pane))
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
        Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; drag-over=0x{overHr:X8}; drop=0x{dropHr:X8}; effect=0x{dropEffect:X}; copied={copied}");
        return copied;
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)] private struct PointL { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewObject(nint self, nint owner, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateViewWindow(nint self, nint previous, ref StreamProbeSettings settings,
        nint browser, ref StreamProbeRect bounds, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyViewWindow(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetViewWindow(nint self, out nint window);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItemObject(nint self, uint item, ref Guid iid, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragEnter(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragOver(nint self, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DragLeave(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Drop(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint bind, out nint pidl, uint attributes, out uint result);
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
    [DllImport("shell32.dll")] private static extern nint ILFindChild(nint parent, nint child);
    [DllImport("shell32.dll")] private static extern int SHCreateDataObject(nint parent, uint count, nint children, nint sourceDataObject, in Guid iid, out nint dataObject);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out Rect bounds);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
}
