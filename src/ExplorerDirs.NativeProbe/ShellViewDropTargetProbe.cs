using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ExplorerDirs.NativeProbe;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

// Compares Shell drop targets exposed by a direct IShellView against its folder target.
// The default path uses direct COM calls in the Private Desktop; visible OLE drag input requires explicit opt-in.
internal static class ShellViewDropTargetProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");
    private static readonly Guid AsyncDataObjectId = new("3D8B0590-F691-11D2-8EA9-006097DF5BD4");
    private static readonly Guid FormatEnumeratorId = new("00000103-0000-0000-C000-000000000046");
    private const uint DropEffectCopy = 1;
    private const uint DropEffectMask = 7;
    private const uint SelectAndFocus = 0x1u | 0x4u | 0x10u | 0x40000000u;
    private const int DragDropSDrop = 0x00040100;
    private const int DragDropSCancel = 0x00040101;

    internal static int Run() => RunCore(interactiveOleCopy: false);

    internal static int RunInteractiveOleCopy() => RunCore(interactiveOleCopy: true);

    private static int RunCore(bool interactiveOleCopy)
    {
        if (interactiveOleCopy && Console.IsInputRedirected)
            throw new InvalidOperationException("The interactive OLE drop probe requires an attached console for its explicit start prompt.");

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
        nint parent = 0, sourcePane = 0, targetPane = 0, sourceFolderPidl = 0, sourceItemPidl = 0,
            selectedSourcePidl = 0, targetFolderPidl = 0;
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
            Marshal.ThrowExceptionForHR(SHParseDisplayName(selectedSourcePath, 0, out selectedSourcePidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(targetFolderPath, 0, out targetFolderPidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, sourceFolderPidl, 0, in ShellFolderId, out sourceFolder));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, targetFolderPidl, 0, in ShellFolderId, out targetFolder));
            Console.WriteLine("shell-view-drop-target-stage: shell-folders-bound");

            childPidls = Marshal.AllocHGlobal(IntPtr.Size);
            var relativeSource = ILFindChild(sourceFolderPidl, sourceItemPidl);
            if (relativeSource == 0) throw new InvalidOperationException("The source item is not relative to the source folder.");
            Marshal.WriteIntPtr(childPidls, relativeSource);
            Marshal.ThrowExceptionForHR(SHCreateDataObject(sourceFolderPidl, 1, childPidls, 0, in DataObjectId, out dataObject));

            parent = CreateWindowExW(0, "STATIC", interactiveOleCopy
                    ? "Explorer Dirs interactive OLE drop probe (temporary files only)"
                    : "Explorer Dirs isolated Shell drop target probe",
                0x90000000, 20, 20, 680, 500, 0, 0, 0, 0); // WS_POPUP | WS_VISIBLE; interactive mode is CLI opt-in.
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
            // Keep the selected PIDL paired with its expected target name; a mismatch once made a successful copy look like a failure.
            var selectedChildPidl = ILFindChild(sourceFolderPidl, selectedSourcePidl);
            if (selectedChildPidl == 0) throw new InvalidOperationException("The selected source item is not a child of its folder.");
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
                    if (ILIsEqual(selectedChildPidl, itemPidl))
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
            if (selectedDataObject != 0) LogDataObjectPayload("selected-view", selectedDataObject);
            LogDataObjectPayload("SHCreateDataObject", dataObject);
            if (selectedDataObject != 0) LogFileContentsPayload("selected-view", selectedDataObject);
            LogFileContentsPayload("SHCreateDataObject", dataObject);
            if (selectedDataObject != 0) LogGlobalPayload("selected-view", selectedDataObject, "FileName");
            LogGlobalPayload("SHCreateDataObject", dataObject, "FileName");
            if (selectedDataObject != 0) LogGlobalPayload("selected-view", selectedDataObject, "FileNameW");
            LogGlobalPayload("SHCreateDataObject", dataObject, "FileNameW");
            if (selectedDataObject != 0) LogGlobalPayload("selected-view", selectedDataObject, "FileGroupDescriptorW");
            LogGlobalPayload("SHCreateDataObject", dataObject, "FileGroupDescriptorW");
            if (selectedDataObject != 0) LogGlobalPayload("selected-view", selectedDataObject, "ZoneIdentifier");
            LogGlobalPayload("SHCreateDataObject", dataObject, "ZoneIdentifier");
            foreach (var formatName in new[] { "Preferred DropEffect", "Performed DropEffect", "Logical Performed DropEffect", "InShellDragLoop" })
            {
                if (selectedDataObject != 0) LogOptionalDropFormat("selected-view", selectedDataObject, formatName);
                LogOptionalDropFormat("SHCreateDataObject", dataObject, formatName);
            }
            if (selectedDataObject != 0) LogShellIdListPayload("selected-view", selectedDataObject);
            LogShellIdListPayload("SHCreateDataObject", dataObject);

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

            if (interactiveOleCopy)
            {
                System.Windows.Forms.Application.DoEvents();
                Console.WriteLine("Move the pointer over the selected source file without clicking. Press Enter here, then press and drag the left mouse button into the target pane; release there. Escape cancels.");
                if (Console.ReadLine() is null)
                    throw new InvalidOperationException("The interactive OLE drop probe did not receive its explicit start confirmation.");

                var dataObjectRcw = Marshal.GetObjectForIUnknown(selectedDataObject) as ComDataObject ??
                    throw new InvalidOperationException("The selected Shell IDataObject could not be marshaled to the OLE interface.");
                int dragResult;
                uint performedEffect;
                try
                {
                    dragResult = ShellViewDragSource.RunOleLoop(dataObjectRcw, DropEffectCopy, 0x0001, out performedEffect);
                }
                finally { _ = Marshal.ReleaseComObject(dataObjectRcw); }

                System.Windows.Forms.Application.DoEvents();
                var copied = File.Exists(selectedTargetPath) &&
                    File.ReadAllText(selectedTargetPath) == "native-shell-selected-view-fixture";
                var sourceStillExists = File.Exists(selectedSourcePath) &&
                    File.ReadAllText(selectedSourcePath) == "native-shell-selected-view-fixture";
                Console.WriteLine($"shell-view-ole-drop-copy: result=0x{dragResult:X8}; effect=0x{performedEffect:X}; copied={copied}; source-preserved={sourceStillExists}; interactive-input=True");

                if (dragResult == DragDropSCancel) return 0;
                if (dragResult < 0) Marshal.ThrowExceptionForHR(dragResult);
                return dragResult == DragDropSDrop && performedEffect == DropEffectCopy && copied && sourceStillExists ? 0 : 1;
            }

            var viewTargetResult = viewDropTarget != 0 && TryTransfer(viewDropTarget, dataObject, point,
                viewTargetPath, "IShellView.GetItemObject", "native-shell-drop-fixture");
            // OLE invokes Drop after release; compare the selected-view object with its actual button-up state.
            var selectedDataObjectResult = selectedDataObject != 0 && TryTransfer(folderDropTarget, selectedDataObject, point,
                selectedTargetPath, "selected view IDataObject", "native-shell-selected-view-fixture", dropKeyState: 0);
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
                    "IShellFolder.CreateViewObject (registered HWND target)", "native-shell-folder-drop-fixture");
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
            if (!folderTargetResult || !selectedDataObjectResult || !sourcePreserved)
                throw new InvalidOperationException("An isolated source or destination Shell IDataObject copy failed, or a source was not preserved.");
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
            if (selectedSourcePidl != 0) Marshal.FreeCoTaskMem(selectedSourcePidl);
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

    private static bool TryTransfer(nint target, nint dataObject, PointL point, string destination, string label,
        string expectedContents, uint dropKeyState = 0x0001)
    {
        LogAsyncOperationState(label, dataObject, "before-drag-enter");
        var allowedEffect = DropEffectCopy;
        var enterHr = At<DragEnter>(target, 3)(target, dataObject, 0x0001, ref point, ref allowedEffect);
        if (enterHr < 0) throw new COMException($"{label} DragEnter failed", enterHr);
        LogAsyncOperationState(label, dataObject, "after-drag-enter");
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
        LogAsyncOperationState(label, dataObject, "after-drag-over");
        if ((overEffect & DropEffectMask) != DropEffectCopy)
        {
            _ = At<DragLeave>(target, 5)(target);
            Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; drag-over=0x{overHr:X8}; effect=0x{overEffect:X}");
            return false;
        }

        var dropEffect = overEffect & DropEffectMask;
        var dropHr = At<Drop>(target, 6)(target, dataObject, dropKeyState, ref point, ref dropEffect);
        if (dropHr < 0) throw new COMException($"{label} Drop failed", dropHr);
        LogAsyncOperationState(label, dataObject, "after-drop");
        var copied = File.Exists(destination) && File.ReadAllText(destination) == expectedContents;
        var targetFiles = Directory.GetFiles(Path.GetDirectoryName(destination)!).Select(Path.GetFileName).Order().ToArray();
        Console.WriteLine($"shell-view-drop-target-candidate: target={label}; drag-enter=0x{enterHr:X8}; drag-over=0x{overHr:X8}; drop-key-state=0x{dropKeyState:X}; drop=0x{dropHr:X8}; effect=0x{dropEffect:X}; copied={copied}; destination-files={string.Join(',', targetFiles)}");
        return copied;
    }

    private static void LogAsyncOperationState(string label, nint dataObject, string stage)
    {
        var asyncDataObjectId = AsyncDataObjectId;
        var asyncHr = Marshal.QueryInterface(dataObject, in asyncDataObjectId, out var asyncDataObject);
        if (asyncHr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-async-stage: source={label}; stage={stage}; supported=False; query=0x{asyncHr:X8}");
            return;
        }

        try
        {
            var modeHr = At<GetAsyncMode>(asyncDataObject, 4)(asyncDataObject, out var asyncMode);
            var operationHr = At<InAsyncOperation>(asyncDataObject, 6)(asyncDataObject, out var inOperation);
            Console.WriteLine($"shell-view-drop-target-async-stage: source={label}; stage={stage}; mode-hr=0x{modeHr:X8}; async-mode={asyncMode}; operation-hr=0x{operationHr:X8}; in-operation={inOperation}");
        }
        finally { Marshal.Release(asyncDataObject); }
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

    private static void LogDataObjectPayload(string label, nint dataObject)
    {
        var asyncDataObjectId = AsyncDataObjectId;
        var asyncHr = Marshal.QueryInterface(dataObject, in asyncDataObjectId, out var asyncDataObject);
        if (asyncHr >= 0)
        {
            try
            {
                var modeHr = At<GetAsyncMode>(asyncDataObject, 4)(asyncDataObject, out var asyncMode);
                var operationHr = At<InAsyncOperation>(asyncDataObject, 6)(asyncDataObject, out var inOperation);
                Console.WriteLine($"shell-view-drop-target-async: source={label}; supported=True; mode-hr=0x{modeHr:X8}; async-mode={asyncMode}; operation-hr=0x{operationHr:X8}; in-operation={inOperation}");
            }
            finally { Marshal.Release(asyncDataObject); }
        }
        else Console.WriteLine($"shell-view-drop-target-async: source={label}; supported=False; query=0x{asyncHr:X8}");

        var format = new FormatEtc { Format = 15, Aspect = 1, Index = -1, Medium = 1 }; // CF_HDROP / TYMED_HGLOBAL.
        var getDataHr = At<GetData>(dataObject, 3)(dataObject, ref format, out var medium);
        if (getDataHr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-hdrop: source={label}; get=0x{getDataHr:X8}");
            return;
        }

        try
        {
            var count = DragQueryFileW(medium.Handle, uint.MaxValue, null, 0);
            var paths = new List<string>();
            for (uint index = 0; index < count; index++)
            {
                var buffer = new System.Text.StringBuilder(1024);
                var length = DragQueryFileW(medium.Handle, index, buffer, (uint)buffer.Capacity);
                if (length > 0) paths.Add(buffer.ToString());
            }
            Console.WriteLine($"shell-view-drop-target-hdrop: source={label}; count={count}; paths={string.Join(';', paths)}");
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    private static void LogShellIdListPayload(string label, nint dataObject)
    {
        var formatId = RegisterClipboardFormatW("Shell IDList Array");
        if (formatId == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var format = new FormatEtc { Format = checked((ushort)formatId), Aspect = 1, Index = -1, Medium = 1 };
        var getDataHr = At<GetData>(dataObject, 3)(dataObject, ref format, out var medium);
        if (getDataHr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-cida: source={label}; get=0x{getDataHr:X8}");
            return;
        }

        nint locked = 0;
        try
        {
            var size = GlobalSize(medium.Handle);
            if (size < 8 || size > int.MaxValue) throw new InvalidDataException($"Invalid CIDA size: {size}.");
            locked = GlobalLock(medium.Handle);
            if (locked == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var childCount = Marshal.ReadInt32(locked);
            if (childCount < 0 || childCount > 1024 || 4L + 4L * (childCount + 1) > (long)size)
                throw new InvalidDataException($"Invalid CIDA child count: {childCount}.");
            var parentOffset = ReadCidaOffset(locked, (long)size, 0);
            var parentPidl = IntPtr.Add(locked, parentOffset);
            var paths = new List<string>(childCount + 1)
            {
                GetFileSystemPath(parentPidl)
            };
            for (var index = 1; index <= childCount; index++)
            {
                var childOffset = ReadCidaOffset(locked, (long)size, index);
                var absolutePidl = ILCombine(parentPidl, IntPtr.Add(locked, childOffset));
                if (absolutePidl == 0) throw new InvalidOperationException("ILCombine failed for a CIDA item.");
                try { paths.Add(GetFileSystemPath(absolutePidl)); }
                finally { Marshal.FreeCoTaskMem(absolutePidl); }
            }
            Console.WriteLine($"shell-view-drop-target-cida: source={label}; parent-and-items={string.Join(';', paths)}");
        }
        finally
        {
            if (locked != 0) _ = GlobalUnlock(medium.Handle);
            ReleaseStgMedium(ref medium);
        }
    }

    private static void LogFileContentsPayload(string label, nint dataObject)
    {
        var formatId = RegisterClipboardFormatW("FileContents");
        if (formatId == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var format = new FormatEtc
        {
            Format = checked((ushort)formatId),
            Aspect = 1,
            Index = 0,
            Medium = 4 // TYMED_ISTREAM, as advertised by both selected-item data objects.
        };
        var getDataHr = At<GetData>(dataObject, 3)(dataObject, ref format, out var medium);
        if (getDataHr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-filecontents: source={label}; get=0x{getDataHr:X8}");
            return;
        }

        var buffer = Marshal.AllocHGlobal(4096);
        try
        {
            using var contents = new MemoryStream();
            while (contents.Length <= 1024 * 1024)
            {
                var readHr = At<ReadStream>(medium.Handle, 3)(medium.Handle, buffer, 4096, out var read);
                if (read > 0)
                {
                    var chunk = new byte[read];
                    Marshal.Copy(buffer, chunk, 0, checked((int)read));
                    contents.Write(chunk);
                }
                if (readHr < 0) throw new COMException($"{label} FileContents IStream::Read failed", readHr);
                if (read == 0) break;
            }

            var bytes = contents.ToArray();
            var sample = System.Text.Encoding.UTF8.GetString(bytes);
            Console.WriteLine($"shell-view-drop-target-filecontents: source={label}; get=0x{getDataHr:X8}; bytes={bytes.Length}; sha256={Convert.ToHexString(SHA256.HashData(bytes))}; utf8-sample={sample}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            ReleaseStgMedium(ref medium);
        }
    }

    private static void LogGlobalPayload(string label, nint dataObject, string formatName)
    {
        var formatId = RegisterClipboardFormatW(formatName);
        if (formatId == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var format = new FormatEtc
        {
            Format = checked((ushort)formatId),
            Aspect = 1,
            Index = -1,
            Medium = 1 // TYMED_HGLOBAL.
        };
        var getDataHr = At<GetData>(dataObject, 3)(dataObject, ref format, out var medium);
        if (getDataHr < 0)
        {
            Console.WriteLine($"shell-view-drop-target-format-data: source={label}; format={formatName}; get=0x{getDataHr:X8}");
            return;
        }

        nint locked = 0;
        try
        {
            var size = GlobalSize(medium.Handle);
            if (size > 1024 * 1024) throw new InvalidDataException($"{formatName} payload exceeds the diagnostic limit: {size} bytes.");
            locked = GlobalLock(medium.Handle);
            if (size > 0 && locked == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var bytes = new byte[checked((int)size)];
            if (bytes.Length > 0) Marshal.Copy(locked, bytes, 0, bytes.Length);
            var prefix = Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64)));
            var text = formatName switch
            {
                "FileName" => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                "FileNameW" => System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0'),
                _ => string.Empty
            };
            Console.WriteLine($"shell-view-drop-target-format-data: source={label}; format={formatName}; get=0x{getDataHr:X8}; bytes={bytes.Length}; sha256={Convert.ToHexString(SHA256.HashData(bytes))}; prefix={prefix}; text={text}");
        }
        finally
        {
            if (locked != 0) _ = GlobalUnlock(medium.Handle);
            ReleaseStgMedium(ref medium);
        }
    }

    private static void LogOptionalDropFormat(string label, nint dataObject, string formatName)
    {
        var formatId = RegisterClipboardFormatW(formatName);
        if (formatId == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var format = new FormatEtc { Format = checked((ushort)formatId), Aspect = 1, Index = -1, Medium = 1 };
        var queryHr = At<QueryGetData>(dataObject, 5)(dataObject, ref format);
        Console.WriteLine($"shell-view-drop-target-optional-format: source={label}; format={formatName}; query=0x{queryHr:X8}");
        // IDataObject::QueryGetData communicates support with S_OK; S_FALSE must not trigger GetData.
        if (queryHr == 0) LogGlobalPayload(label, dataObject, formatName);
    }

    private static int ReadCidaOffset(nint cida, long size, int index)
    {
        var offset = Marshal.ReadInt32(cida, checked(4 + 4 * index));
        if (offset < 0 || offset >= size) throw new InvalidDataException($"CIDA offset {offset} is outside its {size}-byte payload.");
        return offset;
    }

    private static string GetFileSystemPath(nint pidl)
    {
        var hr = SHGetNameFromIDList(pidl, 0x80058000, out var name); // SIGDN_FILESYSPATH.
        Marshal.ThrowExceptionForHR(hr);
        try { return Marshal.PtrToStringUni(name) ?? throw new InvalidDataException("Shell returned an empty parsing path."); }
        finally { Marshal.FreeCoTaskMem(name); }
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
    [StructLayout(LayoutKind.Sequential)] private struct StgMedium
    {
        public uint Tymed;
        public nint Handle;
        public nint ReleaseUnknown;
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
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetData(nint self, ref FormatEtc format, out StgMedium medium);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryGetData(nint self, ref FormatEtc format);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadStream(nint self, nint buffer, uint bytesToRead, out uint bytesRead);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAsyncMode(nint self, [MarshalAs(UnmanagedType.Bool)] out bool asyncMode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InAsyncOperation(nint self, [MarshalAs(UnmanagedType.Bool)] out bool inOperation);
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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(nint drop, uint file, System.Text.StringBuilder? path, uint pathLength);
    [DllImport("shell32.dll")] private static extern nint ILCombine(nint absoluteParent, nint relativeChild);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetNameFromIDList(nint pidl, uint nameKind, out nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string formatName);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref StgMedium medium);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
}
