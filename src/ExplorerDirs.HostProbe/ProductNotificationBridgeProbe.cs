using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

internal static partial class FolderNavigationCallbacksProbe
{
    private static void CheckProductNotificationBridge(string nativeRoot, string physicalRoot, bool lifetimeOnly)
    {
        var folderId = new Guid("93f2f68c-1d1b-11d3-a30e-00c04f79abd1");
        Marshal.ThrowExceptionForHR(SHParseDisplayName(nativeRoot + @"\size-probe", 0, out var item, 0, out _));
        nint folder = 0, callback = 0;
        uint registration = 0, physicalControl = 0;
        var rawNotifications = 0;
        var rawTargets = new List<string?>();
        using var receiver = new HwndSource(new HwndSourceParameters("ExplorerDirs provider bridge observer")
            { ParentWindow = (nint)(-3), WindowStyle = 0, Width = 0, Height = 0 });
        var received = new List<(int Event, string? First, string? Second)>();
        const int notificationMessage = 0x8351;
        receiver.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message == notificationMessage + 1)
            {
                rawNotifications++;
                var shared = BridgeNotificationLock(wParam, unchecked((uint)lParam), out var pointers, out var rawKind);
                nint copy = 0;
                if (shared != 0)
                {
                    try { var raw = Marshal.ReadIntPtr(pointers); if (raw != 0) copy = BridgeClonePidl(raw); }
                    finally { BridgeNotificationUnlock(shared); }
                }
                try { if (rawTargets.Count < 100) rawTargets.Add($"{rawKind:X}:{ParsingName(copy)}"); }
                finally { if (copy != 0) Marshal.FreeCoTaskMem(copy); }
                handled = true; return 0;
            }
            if (message != notificationMessage) return 0;
            handled = true;
            var locked = BridgeNotificationLock(wParam, unchecked((uint)lParam), out var pidls, out var kind);
            if (locked == 0) return 0;
            nint first = 0, second = 0;
            try
            {
                var rawFirst = Marshal.ReadIntPtr(pidls);
                if (rawFirst != 0) first = BridgeClonePidl(rawFirst);
                if ((kind & ~unchecked((int)0x80000000)) is 1 or 0x20000)
                {
                    var rawSecond = Marshal.ReadIntPtr(pidls, nint.Size);
                    if (rawSecond != 0) second = BridgeClonePidl(rawSecond);
                }
            }
            finally { BridgeNotificationUnlock(locked); }
            try
            {
                if (received.Count < 1000)
                    received.Add((kind & ~unchecked((int)0x80000000), ParsingName(first), ParsingName(second)));
            }
            finally
            {
                if (first != 0) Marshal.FreeCoTaskMem(first);
                if (second != 0) Marshal.FreeCoTaskMem(second);
            }
            return 0;
        });
        try
        {
            Ensure(!BridgeIsWindowVisible(receiver.Handle), "notification observer is message-only");
            Marshal.ThrowExceptionForHR(SHBindToParent(item, ref folderId, out folder, out _));
            var callbackId = new Guid("2047e320-f2a9-11ce-ae65-08002b2e1262");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(folder, in callbackId, out callback));
            var invoke = Marshal.GetDelegateForFunctionPointer<ViewCallback>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(callback), 3 * nint.Size));
            int RefCount() { var count = Marshal.AddRef(folder); Marshal.Release(folder); return count; }
            var baseline = RefCount();
            Marshal.ThrowExceptionForHR(SHParseDisplayName(nativeRoot, 0, out var root, 0, out _));
            try
            {
                var entry = new BridgeNotificationEntry { Pidl = root, Recursive = 1 };
                registration = BridgeRegister(receiver.Handle, 0x8002, 0x7fffffff, notificationMessage, 1, ref entry);
                Ensure(registration != 0, "provider observer registration succeeds");
            }
            finally { Marshal.FreeCoTaskMem(root); }

            Marshal.ThrowExceptionForHR(SHParseDisplayName(physicalRoot, 0, out var physicalRootPidl, 0, out _));
            try
            {
                var controlEntry = new BridgeNotificationEntry { Pidl = physicalRootPidl, Recursive = 1 };
                physicalControl = BridgeRegister(receiver.Handle, 0x8002, 0x7fffffff, notificationMessage + 1, 1, ref controlEntry);
                Ensure(physicalControl != 0, "physical control observer registration succeeds");
            }
            finally { Marshal.FreeCoTaskMem(physicalRootPidl); }
            using (var physicalSink = new HwndSource(new HwndSourceParameters("ExplorerDirs backing bridge fixture")
                { ParentWindow = (nint)(-3), WindowStyle = 0, Width = 0, Height = 0 }))
            {
                Ensure(!BridgeIsWindowVisible(physicalSink.Handle), "backing recipient is message-only");
                Marshal.ThrowExceptionForHR(invoke(callback, 15, physicalSink.Handle, 0)); // SFVM_WINDOWCREATED
                Ensure(RefCount() == baseline + 1, "recipient owns exactly one callback lifetime reference");
                Ensure(invoke(callback, 15, physicalSink.Handle, 0) == 1 && RefCount() == baseline + 1,
                    "repeated window-created callback does not duplicate registration or ownership");

                if (!lifetimeOnly)
                {
                    var firstPath = Path.Combine(physicalRoot, "bridge-first.txt");
                    var secondPath = Path.Combine(physicalRoot, "bridge-renamed.txt");
                    File.WriteAllText(firstPath, "one");
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(firstPath, 0, out var firstPidl, 0, out _));
                    try
                    {
                        CheckEvent(2, "bridge-first.txt", null, () => BridgeNotify(2, 0x2000, firstPidl, 0));
                        File.WriteAllText(firstPath, "two");
                        CheckEvent(0x2000, "bridge-first.txt", null, () => BridgeNotify(0x2000, 0x2000, firstPidl, 0));
                        File.Move(firstPath, secondPath);
                        Marshal.ThrowExceptionForHR(SHParseDisplayName(secondPath, 0, out var secondPidl, 0, out _));
                        try
                        {
                            CheckEvent(1, "bridge-first.txt", "bridge-renamed.txt", () => BridgeNotify(1, 0x2000, firstPidl, secondPidl));
                            File.Delete(secondPath);
                            CheckEvent(4, "bridge-renamed.txt", null, () => BridgeNotify(4, 0x2000, secondPidl, 0));
                        }
                        finally { Marshal.FreeCoTaskMem(secondPidl); }
                    }
                    finally { Marshal.FreeCoTaskMem(firstPidl); }
                    var directory = Path.Combine(physicalRoot, "bridge-folder");
                    Directory.CreateDirectory(directory);
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(directory, 0, out var directoryPidl, 0, out _));
                    try
                    {
                        CheckEvent(8, "bridge-folder", null, () => BridgeNotify(8, 0x2000, directoryPidl, 0));
                        Directory.Delete(directory);
                        CheckEvent(0x10, "bridge-folder", null, () => BridgeNotify(0x10, 0x2000, directoryPidl, 0));
                    }
                    finally { Marshal.FreeCoTaskMem(directoryPidl); }

                    void CheckEvent(int kind, string firstName, string? secondName, Action send)
                    {
                        var before = received.Count;
                        send();
                        try { PumpUntil(() => received.Skip(before).Any(entry => entry.Event == kind &&
                            string.Equals(entry.First, nativeRoot + "\\" + firstName, StringComparison.OrdinalIgnoreCase) &&
                            (secondName is null || string.Equals(entry.Second, nativeRoot + "\\" + secondName, StringComparison.OrdinalIgnoreCase)))); }
                        catch (Exception error) { throw new InvalidOperationException($"Notification bridge expected={kind:X}:{firstName}; raw={rawNotifications}({string.Join(";", rawTargets)}); received={string.Join(";", received.Select(entry => $"{entry.Event:X}:{entry.First}->{entry.Second}"))}", error); }
                        Ensure(received.Count < 1000, "physical/provider notification forwarding does not loop");
                    }
                }
            }
            Ensure(RefCount() == baseline, "recipient destruction releases callback ownership without a view cycle");
            Console.WriteLine(lifetimeOnly
                ? "Product notification lifetime: PASS (repeat attach, exact ownership and release; message-only HWNDs, no Shell view)."
                : "Product notification bridge: PASS (create/update/rename/delete/mkdir/rmdir, repeat attach and teardown; two message-only HWNDs, no Shell view).");
        }
        finally
        {
            if (physicalControl != 0) BridgeDeregister(physicalControl);
            if (registration != 0) BridgeDeregister(registration);
            if (callback != 0) Marshal.Release(callback);
            if (folder != 0) Marshal.Release(folder);
            Marshal.FreeCoTaskMem(item);
        }
    }

    private static string? ParsingName(nint pidl)
    {
        if (pidl == 0 || SHGetNameFromIDList(pidl, 0x80028000, out var name) < 0) return null;
        try { return Marshal.PtrToStringUni(name); }
        finally { Marshal.FreeCoTaskMem(name); }
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || clock.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Ensure(condition(), "physical event is forwarded with the corresponding provider item identity");
    }
    [StructLayout(LayoutKind.Sequential)] private struct BridgeNotificationEntry { internal nint Pidl; internal int Recursive; }
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotifyRegister")] private static extern uint BridgeRegister(nint hwnd, int sources, int events, uint message, int count, ref BridgeNotificationEntry entry);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotifyDeregister")] private static extern bool BridgeDeregister(uint registration);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Lock")] private static extern nint BridgeNotificationLock(nint change, uint process, out nint pidls, out int events);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotification_Unlock")] private static extern bool BridgeNotificationUnlock(nint value);
    [DllImport("shell32.dll", EntryPoint = "ILClone")] private static extern nint BridgeClonePidl(nint pidl);
    [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")] private static extern void BridgeNotify(int events, uint flags, nint first, nint second);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool BridgeIsWindowVisible(nint hwnd);
}
