using System.Runtime.InteropServices;
using System.Diagnostics;
using ExplorerDirs.NativeProbe;

public static partial class SyntheticShellSortProbe
{
    private static readonly Guid ProjectedNotificationBridgeId = new("412ED4D4-9750-4525-8C90-345CBDB5FE95");

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
            void RequireApplied(int result)
            {
                if (result != 0) throw new InvalidOperationException($"Projected notification was not applied: 0x{result:X8}.");
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
            var remove = At<NotifyProjectedRemoval>(bridge, 4);
            var enumerationCount = At<ReadProjectedEnumerations>(bridge, 5);
            Check(enumerationCount(bridge, out var initialEnumerations));
            var metadataCounts = At<ReadProjectedMetadataCounts>(bridge, 6);
            Check(metadataCounts(bridge, out var roots, out var facts));
            if (roots != 1 || facts != fillerCount + 4)
                throw new InvalidOperationException($"Expected one observed root and complete metadata facts, got {roots}/{facts}.");
            var acknowledge = At<AcknowledgeProjectedRemoval>(bridge, 7);
            uint retainedFacts = (uint)(fillerCount + 4);
            var retiredTickets = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var ledger = new ProjectedChangeLedger();
            foreach (var (name, directory, expected) in new[] { ("folder-empty", true, 3), ("file-2048.bin", false, 2) })
            {
                var path = Path.Combine(fixture, name);
                if (directory) Directory.Delete(path); else File.Delete(path);
                PumpBaseline();
                if (!Names().Contains(name)) throw new InvalidOperationException("Baseline already removed the row; bridge was not isolated.");
                Console.WriteLine($"projected-notification-baseline: item={name}; stale-row-retained=True; physical-subscription-paused=True");
                var deletion = ledger.Record(ProjectedChangeKind.Deleted, name)!;
                if (!ledger.TryTake(out var dispatched) || !ReferenceEquals(dispatched, deletion) || !ledger.IsCurrent(deletion))
                    throw new InvalidOperationException("Deletion dispatch lost ordered visit work.");
                RequireApplied(remove(bridge, name, out var removalTicket));
                Check(metadataCounts(bridge, out roots, out facts));
                if (facts != retainedFacts) throw new InvalidOperationException("Unconfirmed notification discarded its item type.");
                PumpUntil(() => Names().Length == expected + fillerCount && !Names().Contains(name));
                if (!ledger.IsCurrent(deletion)) throw new InvalidOperationException("Deletion confirmation belongs to retired work.");
                RequireApplied(acknowledge(bridge, name, removalTicket));
                ledger.Release(deletion);
                retiredTickets[name] = removalTicket;
                retainedFacts--;
                AssertRetainedSelection();
                Check(enumerationCount(bridge, out var actualEnumerations));
                if (actualEnumerations != initialEnumerations)
                    throw new InvalidOperationException("Projected deletion re-enumerated the native folder.");
            }
            if (remove(bridge, "never-observed", out _) != unchecked((int)0x80070490))
                throw new InvalidOperationException("Deletion bridge guessed an unobserved child type.");
            Check(metadataCounts(bridge, out roots, out facts));
            if (roots != 1 || facts != fillerCount + 2)
                throw new InvalidOperationException("Deleted child metadata was not retired.");
            var recreated = Path.Combine(fixture, "folder-empty");
            Directory.CreateDirectory(recreated);
            if (remove(bridge, "folder-empty", out _) != 1 || !Directory.Exists(recreated))
                throw new InvalidOperationException("Deletion bridge did not reject a recreated path.");
            if (remove(bridge, "..", out _) != unchecked((int)0x80070057) ||
                remove(bridge, "sub/child", out _) != unchecked((int)0x80070057))
                throw new InvalidOperationException("Deletion bridge accepted a non-child leaf.");
            var arrive = At<RemoveProjectedChild>(bridge, 8);
            var rename = At<RenameProjectedChild>(bridge, 9);
            RequireApplied(arrive(bridge, "folder-empty"));
            if (acknowledge(bridge, "folder-empty", retiredTickets["folder-empty"]) != 1)
                throw new InvalidOperationException("Old deletion confirmation retired a recreated item.");
            PumpUntil(() => Names().Length == fillerCount + 3 && Names().Contains("folder-empty"));
            File.WriteAllBytes(Path.Combine(fixture, "created-file.bin"), new byte[4096]);
            PumpBaseline();
            if (Names().Contains("created-file.bin")) throw new InvalidOperationException("Creation baseline was not isolated.");
            RequireApplied(arrive(bridge, "created-file.bin"));
            PumpUntil(() => Names().Length == fillerCount + 4 && Names().Contains("created-file.bin"));
            File.WriteAllBytes(Path.Combine(fixture, "created-file.bin"), new byte[8192]);
            RequireApplied(arrive(bridge, "created-file.bin"));
            string updatedFileSize = "";
            PumpUntil(() =>
            {
                var names = Names();
                var index = Array.IndexOf(names, "created-file.bin");
                if (index < 0) return false;
                updatedFileSize = ReadColumnTextsForRows(list, 1, names.Length)[index];
                return string.Equals(updatedFileSize.Replace(" ", "").Replace("\u00A0", ""), "8KB", StringComparison.OrdinalIgnoreCase);
            });
            Console.WriteLine($"projected-file-update: exact-size={updatedFileSize}; expected=8 KB");
            AssertRetainedSelection();
            foreach (var (oldName, newName, directory) in new[] {
                ("folder-empty", "renamed-folder", true), ("created-file.bin", "renamed-file.bin", false) })
            {
                if (directory) Directory.Move(Path.Combine(fixture, oldName), Path.Combine(fixture, newName));
                else File.Move(Path.Combine(fixture, oldName), Path.Combine(fixture, newName));
                PumpBaseline();
                if (!Names().Contains(oldName) || Names().Contains(newName))
                    throw new InvalidOperationException("Rename baseline was not isolated.");
                RequireApplied(rename(bridge, oldName, newName, out var renameTicket));
                PumpUntil(() => Names().Length == fillerCount + 4 && !Names().Contains(oldName) && Names().Contains(newName));
                RequireApplied(acknowledge(bridge, oldName, renameTicket));
                AssertRetainedSelection();
            }
            foreach (var (oldName, newName) in new[] { ("renamed-folder", "RENAMED-FOLDER"), ("renamed-file.bin", "RENAMED-FILE.BIN") })
            {
                if (!MoveProjectedCaseOnly(Path.Combine(fixture, oldName), Path.Combine(fixture, newName), 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                RequireApplied(rename(bridge, oldName, newName, out var renameTicket));
                PumpUntil(() => Names().Contains(newName) && !Names().Contains(oldName));
                // The new spelling has a newer fact; an old acknowledgement must not erase it.
                if (acknowledge(bridge, oldName, renameTicket) != 1)
                    throw new InvalidOperationException("Case-only old acknowledgement erased the current item.");
                if (rename(bridge, newName, oldName, out _) != 1)
                    throw new InvalidOperationException("Stale reverse case rename was accepted.");
                AssertRetainedSelection();
            }
            var remaining = fillerCount + 4;
            foreach (var (name, directory) in new[] { ("RENAMED-FOLDER", true), ("RENAMED-FILE.BIN", false) })
            {
                if (directory) Directory.Delete(Path.Combine(fixture, name)); else File.Delete(Path.Combine(fixture, name));
                RequireApplied(remove(bridge, name, out var removalTicket));
                remaining--;
                PumpUntil(() => Names().Length == remaining && !Names().Contains(name));
                RequireApplied(acknowledge(bridge, name, removalTicket));
                retiredTickets[name] = removalTicket;
                AssertRetainedSelection();
            }
            if (arrive(bridge, "folder-*") != unchecked((int)0x80070057))
                throw new InvalidOperationException("Arrival accepted a wildcard instead of a direct child leaf.");
            if (arrive(bridge, "missing-arrival") != unchecked((int)0x80070002))
                throw new InvalidOperationException("Arrival fabricated a missing item.");
            Check(metadataCounts(bridge, out roots, out facts));
            Check(enumerationCount(bridge, out var finalEnumerations));
            if (facts != fillerCount + 2 || roots != 1 || finalEnumerations != initialEnumerations)
                throw new InvalidOperationException("Create/rename/delete lost facts or re-enumerated the folder.");
            Console.WriteLine("projected-arrival-rename: PASS file/folder create, rename, case-only rename and inferred-type delete; no EnumObjects; selection/focus retained");
            AssertRetainedSelection();
            Console.WriteLine("projected-notification-bridge: PASS file/folder rows removed; unrelated selection/focus retained; versioned child types; stale confirmations/unknown/recreated/invalid leaf rejected; no Refresh invoked; no EnumObjects; filler-count=" + fillerCount);
            Marshal.AddRef(bridge);
            var retainedBridge = bridge;
            var pendingWork = ledger.Record(ProjectedChangeKind.Changed, "folder-12288")!;
            return () =>
            {
                try
                {
                    ledger.Invalidate();
                    if (ledger.IsCurrent(pendingWork) || ledger.TryTake(out _) || ledger.Snapshot != (0, 0, true))
                        throw new InvalidOperationException("Destroyed view retained pending managed notification work.");
                    Check(metadataCounts(retainedBridge, out var closedRoots, out var closedFacts));
                    if (closedRoots != 0 || closedFacts != 0 || IsWindow(list))
                        throw new InvalidOperationException("Closed view retained child metadata or a native list window.");
                    if (arrive(retainedBridge, "folder-12288") != unchecked((int)0x800401FD) ||
                        rename(retainedBridge, "folder-12288", "closed-rename", out _) != unchecked((int)0x800401FD))
                        throw new InvalidOperationException("Retired native view accepted arrival or rename.");
                    if (remove(retainedBridge, "folder-12288", out _) != unchecked((int)0x800401FD))
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
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveProjectedCaseOnly(string source, string destination, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RenameProjectedChild(nint self, [MarshalAs(UnmanagedType.LPWStr)] string oldLeaf, [MarshalAs(UnmanagedType.LPWStr)] string newLeaf, out ulong oldTicket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadProjectedMetadataCounts(nint self, out uint roots, out uint facts);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReadProjectedEnumerations(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetProjectedBridge(nint self, in Guid iid, out nint bridge);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int PauseProjectedPhysicalNotifications(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NotifyProjectedRemoval(nint self, [MarshalAs(UnmanagedType.LPWStr)] string leaf, out ulong ticket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int AcknowledgeProjectedRemoval(nint self, [MarshalAs(UnmanagedType.LPWStr)] string leaf, ulong ticket);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RemoveProjectedChild(nint self, [MarshalAs(UnmanagedType.LPWStr)] string leaf);
}
