using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Calls the native Shell drop target directly with a Shell-created data object.
// This exercises the target's Drop path without DoDragDrop, desktop input or UI.
internal static class ShellDropProbe
{
    private const uint DropEffectCopy = 1;
    private const uint DropEffectMove = 2;
    private const uint DropEffectOperationMask = 7;
    private static readonly Guid ShellViewId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid DropTargetId = new("00000122-0000-0000-C000-000000000046");
    private static readonly Guid DataObjectId = new("0000010E-0000-0000-C000-000000000046");

    internal static string DescribeForeground()
    {
        var hwnd = GetForegroundWindow();
        return $"hwnd=0x{hwnd.ToInt64():X} title=\"{WindowTitle(hwnd)}\" visible={IsWindowVisible(hwnd)}";
    }

    internal static int Copy(Type hostType, object targetHost, string sourceFolder, string targetFolder, IReadOnlyList<string> names)
        => Transfer(hostType, targetHost, sourceFolder, targetFolder, names, keyState: 1, allowedEffects: DropEffectCopy, expectedEffect: DropEffectCopy);

    internal static int Transfer(Type hostType, object targetHost, string sourceFolder, string targetFolder,
        IReadOnlyList<string> names, uint keyState, uint allowedEffects, uint expectedEffect)
    {
        if (names.Count == 0) throw new ArgumentException("A Shell drop needs at least one source item.", nameof(names));
        var sourcePaths = names.Select(name => Path.Combine(sourceFolder, name)).ToArray();
        var targetPaths = names.Select(name => Path.Combine(targetFolder, name)).ToArray();
        var sourceContents = sourcePaths.Select(File.ReadAllText).ToArray();
        var foregroundBefore = GetForegroundWindow();
        if (sourcePaths.Any(path => !File.Exists(path)) || targetPaths.Any(File.Exists))
            throw new InvalidOperationException("Cross-pane drop fixture paths are not isolated.");

        nint sourceFolderPidl = 0, childArray = 0, shellView = 0, shellViewInterface = 0, dropTarget = 0, dataObject = 0;
        var sourcePidls = new nint[sourcePaths.Length];
        object? shellViewObject = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(sourceFolder, 0, out sourceFolderPidl, 0, out _));
            childArray = Marshal.AllocHGlobal(IntPtr.Size * sourcePaths.Length);
            for (var index = 0; index < sourcePaths.Length; index++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(sourcePaths[index], 0, out sourcePidls[index], 0, out _));
                var child = ILFindChild(sourceFolderPidl, sourcePidls[index]);
                if (child == 0) throw new InvalidOperationException("A source item is not a child of its fixture folder.");
                Marshal.WriteIntPtr(childArray, index * IntPtr.Size, child);
            }
            Marshal.ThrowExceptionForHR(SHCreateDataObject(sourceFolderPidl, (uint)sourcePaths.Length, childArray, 0, in DataObjectId, out dataObject));

            var browserObject = hostType.GetField("_browser", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(targetHost) ?? throw new InvalidOperationException("Target browser is unavailable.");
            var browserInterface = hostType.GetNestedType("IExplorerBrowser", System.Reflection.BindingFlags.NonPublic)!;
            var getCurrentView = browserInterface.GetMethod("GetCurrentView")!;
            object?[] viewArguments = [ShellViewId, null];
            Marshal.ThrowExceptionForHR((int)getCurrentView.Invoke(browserObject, viewArguments)!);
            shellViewObject = viewArguments[1] ?? throw new InvalidOperationException("Target Shell view is unavailable.");
            shellView = Marshal.GetIUnknownForObject(shellViewObject);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellView, in ShellViewId, out shellViewInterface));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellViewInterface, in DropTargetId, out dropTarget));

            var viewWindow = Method<GetWindow>(shellViewInterface, 3)(shellViewInterface, out var viewHwnd);
            Marshal.ThrowExceptionForHR(viewWindow);
            GetWindowRect(viewHwnd, out var bounds);
            var point = new PointL { X = bounds.Left + Math.Max(1, bounds.Right - bounds.Left) / 2,
                Y = bounds.Top + Math.Max(1, bounds.Bottom - bounds.Top) / 2 };
            var effect = allowedEffects;
            var enterHr = Method<DragEnter>(dropTarget, 3)(dropTarget, dataObject, keyState, ref point, ref effect);
            Marshal.ThrowExceptionForHR(enterHr);
            var enterOperationEffect = effect & DropEffectOperationMask;
            if (enterOperationEffect == 0 || (enterOperationEffect & ~allowedEffects) != 0)
                throw new InvalidOperationException($"Target returned invalid operation effects {effect} during DragEnter for allowed={allowedEffects} and keyState=0x{keyState:X}.");
            var overEffect = allowedEffects;
            var overHr = Method<DragOver>(dropTarget, 4)(dropTarget, keyState, ref point, ref overEffect);
            Marshal.ThrowExceptionForHR(overHr);
            var overOperationEffect = overEffect & DropEffectOperationMask;
            if (overOperationEffect != expectedEffect) throw new InvalidOperationException($"Target chose operation effect {overOperationEffect} (raw={overEffect}) during DragOver, expected {expectedEffect} for keyState=0x{keyState:X}; enter={enterOperationEffect}.");
            effect = overOperationEffect;
            var dropHr = Method<Drop>(dropTarget, 6)(dropTarget, dataObject, keyState, ref point, ref effect);
            Marshal.ThrowExceptionForHR(dropHr);

            var sourcesRemain = sourcePaths.All(File.Exists);
            var sourceStateMatches = expectedEffect == DropEffectCopy ? sourcesRemain : !sourcesRemain;
            var targetsMatch = targetPaths.Select((path, index) => File.Exists(path) &&
                File.ReadAllText(path) == sourceContents[index]).All(matches => matches);
            // A direct Drop call bypasses DoDragDrop's source/effect handoff; a completed Shell move can return NONE here.
            var dropEffectMatches = effect == expectedEffect || (expectedEffect == DropEffectMove && effect == 0);
            if (!dropEffectMatches || !sourceStateMatches || !targetsMatch)
                throw new InvalidOperationException($"Native Shell drop mismatch for {sourcePaths.Length} fixture item(s): expected effect={expectedEffect}, actual effect={effect}, sources-remain={sourcesRemain}, targets-match={targetsMatch}, enter=0x{enterHr:X8}/{overEffect}, over=0x{overHr:X8}, drop=0x{dropHr:X8}, bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}.");

            var foregroundAfter = GetForegroundWindow();
            Console.WriteLine($"cross-pane-shell-drop item-count={sourcePaths.Length} key-state=0x{keyState:X} effects-allowed=0x{allowedEffects:X} enter=0x{enterOperationEffect:X} over=0x{overOperationEffect:X} sources-remain={sourcesRemain} destination-content-matches=True drop-effect={effect} foreground-before=0x{foregroundBefore.ToInt64():X} foreground-after=0x{foregroundAfter.ToInt64():X} foreground-title=\"{WindowTitle(foregroundAfter)}\" foreground-visible={IsWindowVisible(foregroundAfter)}");
            return 0;
        }
        finally
        {
            if (dataObject != 0) Marshal.Release(dataObject);
            if (dropTarget != 0) Marshal.Release(dropTarget);
            if (shellViewInterface != 0) Marshal.Release(shellViewInterface);
            if (shellView != 0) Marshal.Release(shellView);
            if (shellViewObject is { } viewObject && Marshal.IsComObject(viewObject)) Marshal.ReleaseComObject(viewObject);
            if (childArray != 0) Marshal.FreeHGlobal(childArray);
            foreach (var pidl in sourcePidls) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (sourceFolderPidl != 0) Marshal.FreeCoTaskMem(sourceFolderPidl);
        }
    }

    private static T Method<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * IntPtr.Size));

    private static string WindowTitle(nint hwnd)
    {
        var text = new StringBuilder(256);
        _ = GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DragEnter(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DragOver(nint self, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Drop(nint self, nint dataObject, uint keyState, ref PointL point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetWindow(nint self, out nint hwnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
    [DllImport("shell32.dll")]
    private static extern nint ILFindChild(nint parent, nint child);
    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(nint parent, uint count, nint children, nint sourceDataObject,
        in Guid iid, out nint dataObject);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
