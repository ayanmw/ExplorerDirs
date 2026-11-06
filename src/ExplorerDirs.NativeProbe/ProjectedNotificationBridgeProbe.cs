using System.Runtime.InteropServices;
using System.Diagnostics;

public static partial class SyntheticShellSortProbe
{
    private static readonly Guid ProjectedNotificationBridgeId = new("A6FB5E8E-8BE5-48E7-98B2-1C8D282D2033");

    private static void VerifyProjectedNotificationBridge(nint shell, nint list, string fixture, int fillerCount)
    {
        Check(Marshal.QueryInterface(shell, in FolderView2Id, out var folderView));
        nint bridge = 0;
        try
        {
            Check(At<GetProjectedBridge>(folderView, 5)(folderView, in ProjectedNotificationBridgeId, out bridge));
            Check(ShellFolderViewOptions.SetNoEnumRefresh(folderView, true));
            Check(At<PauseProjectedPhysicalNotifications>(bridge, 3)(bridge));
            string[] Names() => ReadColumnTextsForRows(list, 0, unchecked((int)SendMessageW(list, 0x1004, 0, 0)));
            void PumpUntil(Func<bool> condition)
            {
                var clock = Stopwatch.StartNew();
                do
                {
                    System.Windows.Forms.Application.DoEvents();
                    if (condition()) return;
                    Thread.Sleep(10);
                } while (clock.Elapsed < TimeSpan.FromSeconds(5));
                throw new TimeoutException("Projected child removal did not reconcile the native rows.");
            }
            void PumpBaseline()
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 300)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
            }
            void AssertRetainedSelection()
            {
                var index = Array.IndexOf(Names(), "folder-25600");
                if (index < 0 || (unchecked((uint)SendMessageW(list, 0x102C, index, 3)) & 3) != 3)
                    throw new InvalidOperationException("Incremental deletion lost the unaffected selected/focused item.");
            }
            SetListViewState(list, -1, 0, LvisSelected | LvisFocused);
            SetListViewState(list, Array.IndexOf(Names(), "folder-25600"), LvisSelected | LvisFocused, LvisSelected | LvisFocused);
            AssertRetainedSelection();
            var remove = At<RemoveProjectedChild>(bridge, 4);
            var enumerationCount = At<ReadProjectedEnumerations>(bridge, 5);
            Check(enumerationCount(bridge, out var initialEnumerations));
            foreach (var (name, directory, expected) in new[] { ("folder-empty", true, 3), ("file-2048.bin", false, 2) })
            {
                var path = Path.Combine(fixture, name);
                if (directory) Directory.Delete(path); else File.Delete(path);
                PumpBaseline();
                if (!Names().Contains(name)) throw new InvalidOperationException("Baseline already removed the row; bridge was not isolated.");
                Console.WriteLine($"projected-notification-baseline: item={name}; stale-row-retained=True; physical-subscription-paused=True");
                Check(remove(bridge, name, directory ? 1 : 0));
                PumpUntil(() => Names().Length == expected + fillerCount && !Names().Contains(name));
                AssertRetainedSelection();
                Check(enumerationCount(bridge, out var actualEnumerations));
                if (actualEnumerations != initialEnumerations)
                    throw new InvalidOperationException("Projected deletion re-enumerated the native folder.");
            }
            var recreated = Path.Combine(fixture, "folder-empty");
            Directory.CreateDirectory(recreated);
            if (remove(bridge, "folder-empty", 1) != 1 || !Directory.Exists(recreated))
                throw new InvalidOperationException("Deletion bridge did not reject a recreated path.");
            if (remove(bridge, "..", 1) != unchecked((int)0x80070057) ||
                remove(bridge, "sub/child", 0) != unchecked((int)0x80070057))
                throw new InvalidOperationException("Deletion bridge accepted a non-child leaf.");
            AssertRetainedSelection();
            Console.WriteLine("projected-notification-bridge: PASS file/folder rows removed; unrelated selection/focus retained; recreated path and invalid leaf rejected; no Refresh invoked; no EnumObjects; filler-count=" + fillerCount);
        }
        finally
        {
            if (bridge != 0) Marshal.Release(bridge);
            Marshal.Release(folderView);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadProjectedEnumerations(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetProjectedBridge(nint self, in Guid iid, out nint bridge);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PauseProjectedPhysicalNotifications(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RemoveProjectedChild(nint self, [MarshalAs(UnmanagedType.LPWStr)] string leaf, int directory);
}
