using System.Runtime.InteropServices;
using System.Diagnostics;

public static partial class SyntheticShellSortProbe
{
    private static readonly Guid ProjectedNotificationBridgeId = new("B0B7756F-4BDD-479F-B52C-18C4EC55258D");

    private static Action VerifyProjectedNotificationBridge(nint shell, nint list, string fixture, int fillerCount)
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
            var metadataCounts = At<ReadProjectedMetadataCounts>(bridge, 6);
            Check(metadataCounts(bridge, out var roots, out var facts));
            if (roots != 1 || facts != fillerCount + 4)
                throw new InvalidOperationException($"Expected one observed root and complete metadata facts, got {roots}/{facts}.");
            var acknowledge = At<RemoveProjectedChild>(bridge, 7);
            uint retainedFacts = (uint)(fillerCount + 4);
            foreach (var (name, directory, expected) in new[] { ("folder-empty", true, 3), ("file-2048.bin", false, 2) })
            {
                var path = Path.Combine(fixture, name);
                if (directory) Directory.Delete(path); else File.Delete(path);
                PumpBaseline();
                if (!Names().Contains(name)) throw new InvalidOperationException("Baseline already removed the row; bridge was not isolated.");
                Console.WriteLine($"projected-notification-baseline: item={name}; stale-row-retained=True; physical-subscription-paused=True");
                Check(remove(bridge, name));
                Check(metadataCounts(bridge, out roots, out facts));
                if (facts != retainedFacts) throw new InvalidOperationException("Unconfirmed notification discarded its item type.");
                PumpUntil(() => Names().Length == expected + fillerCount && !Names().Contains(name));
                Check(acknowledge(bridge, name));
                retainedFacts--;
                AssertRetainedSelection();
                Check(enumerationCount(bridge, out var actualEnumerations));
                if (actualEnumerations != initialEnumerations)
                    throw new InvalidOperationException("Projected deletion re-enumerated the native folder.");
            }
            if (remove(bridge, "never-observed") != unchecked((int)0x80070490))
                throw new InvalidOperationException("Deletion bridge guessed an unobserved child type.");
            Check(metadataCounts(bridge, out roots, out facts));
            if (roots != 1 || facts != fillerCount + 2)
                throw new InvalidOperationException("Deleted child metadata was not retired.");
            var recreated = Path.Combine(fixture, "folder-empty");
            Directory.CreateDirectory(recreated);
            if (remove(bridge, "folder-empty") != 1 || !Directory.Exists(recreated))
                throw new InvalidOperationException("Deletion bridge did not reject a recreated path.");
            if (remove(bridge, "..") != unchecked((int)0x80070057) ||
                remove(bridge, "sub/child") != unchecked((int)0x80070057))
                throw new InvalidOperationException("Deletion bridge accepted a non-child leaf.");
            AssertRetainedSelection();
            Console.WriteLine("projected-notification-bridge: PASS file/folder rows removed; unrelated selection/focus retained; authoritative child types; unknown/recreated/invalid leaf rejected; no Refresh invoked; no EnumObjects; filler-count=" + fillerCount);
            Marshal.AddRef(bridge);
            var retainedBridge = bridge;
            return () =>
            {
                try
                {
                    Check(metadataCounts(retainedBridge, out var closedRoots, out var closedFacts));
                    if (closedRoots != 0 || closedFacts != 0 || IsWindow(list))
                        throw new InvalidOperationException("Closed view retained child metadata or a native list window.");
                    if (remove(retainedBridge, "folder-12288") != unchecked((int)0x800401FD))
                        throw new InvalidOperationException("Retired native view accepted an incremental notification.");
                    Console.WriteLine("projected-notification-lifecycle: PASS roots=0; facts=0; list-destroyed=True; retired-capability-rejected=True");
                }
                finally { Marshal.Release(retainedBridge); }
            };
        }
        finally
        {
            if (bridge != 0) Marshal.Release(bridge);
            Marshal.Release(folderView);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadProjectedMetadataCounts(nint self, out uint roots, out uint facts);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadProjectedEnumerations(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetProjectedBridge(nint self, in Guid iid, out nint bridge);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PauseProjectedPhysicalNotifications(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RemoveProjectedChild(nint self, [MarshalAs(UnmanagedType.LPWStr)] string leaf);
}
