using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    // No window or input: compare the projection's drag capabilities with its backing folder.
    internal static int RunProductDropAttributes(string adapterPath, bool transfer = false)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-DropAttributes-" + Guid.NewGuid().ToString("N"));
        nint root = 0, physicalRoot = 0, folder = 0, physical = 0;
        ProfileShellNamespaceOwner? owner = null;
        try
        {
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "start.bat"), "@rem drag fixture; never executed\r\n");
            var classId = Guid.NewGuid();
            owner = ProfileShellNamespaceOwner.Start(classId, fixture, Path.GetFullPath(adapterPath));
            Check(SHParseDisplayName(CreatePathIdentityParsingName(classId, fixture), 0, out root, 0, out _));
            Check(SHParseDisplayName(fixture, 0, out physicalRoot, 0, out _));
            Check(BindProductDisplayFolder(0, root, 0, in ShellFolder2Id, out folder));
            Check(BindProductDisplayFolder(0, physicalRoot, 0, in ShellFolder2Id, out physical));
            const uint capabilities = 0x00000177; // COPY/MOVE/LINK/RENAME/DELETE/PROPSHEET/DROPTARGET
            var expected = capabilities;
            Check(At<DropAttributes>(physical, 9)(physical, 0, 0, ref expected));
            expected &= capabilities;
            var actual = capabilities;
            Check(At<DropAttributes>(folder, 9)(folder, 0, 0, ref actual));
            Console.WriteLine($"product-drop-root-attributes: physical=0x{expected:X}; projected=0x{actual:X}");
            if (actual != expected) throw new InvalidOperationException("Projected background lost backing-folder drag capabilities.");
            // Requested masks are in/out; never return unrelated FOLDER bits.
            actual = 0;
            Check(At<DropAttributes>(folder, 9)(folder, 0, 0, ref actual));
            if (actual != 0) throw new InvalidOperationException("GetAttributesOf ignored its requested mask.");
            var shellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Check(DropCreateShellItem(root, in shellItemId, out var rootItem));
            try
            {
                Check(At<DropItemAttributes>(rootItem, 6)(rootItem, capabilities, out actual));
                if ((actual & capabilities) != expected)
                    throw new InvalidOperationException($"Path-marker Shell item lost capabilities: 0x{actual:X}, expected 0x{expected:X}.");
                Console.WriteLine($"product-drop-path-marker-attributes: 0x{actual:X}");
            }
            finally { Marshal.Release(rootItem); }
            VerifyProductDragSource(folder, Path.Combine(fixture, "start.bat"));
            if (transfer)
            {
                Check(At<ParseDisplayName>(folder, 3)(folder, 0, 0, "start.bat", 0, out var child, 0));
                var children = Marshal.AllocHGlobal(nint.Size);
                try
                {
                    Marshal.WriteIntPtr(children, child);
                    foreach (var (keys, effect, name) in new[] { (8u, 1u, "copy"), (12u, 4u, "link") })
                    {
                        var destination = Path.Combine(fixture, name);
                        Directory.CreateDirectory(destination);
                        nint destinationRoot = 0, destinationFolder = 0, target = 0, data = 0;
                        try
                        {
                            Check(SHParseDisplayName(CreatePathIdentityParsingName(classId, destination), 0, out destinationRoot, 0, out _));
                            Check(BindProductDisplayFolder(0, destinationRoot, 0, in ShellFolder2Id, out destinationFolder));
                            Check(At<DropUiObject>(folder, 10)(folder, 0, 1, children, in DataObjectId, 0, out data));
                            var dropId = new Guid("00000122-0000-0000-C000-000000000046");
                            Check(At<DropViewObject>(destinationFolder, 8)(destinationFolder, 0, in dropId, out target));
                            var point = new DropPoint();
                            uint allowed = 7;
                            Check(At<DropEnter>(target, 3)(target, data, keys | 1, ref point, ref allowed));
                            if (allowed != effect)
                            {
                                _ = At<DropLeave>(target, 5)(target);
                                throw new InvalidOperationException($"{name}: native target returned effect {allowed}, expected {effect}.");
                            }
                            Check(At<DropOver>(target, 4)(target, keys | 1, ref point, ref allowed));
                            Check(At<DropEnter>(target, 6)(target, data, keys, ref point, ref allowed));
                            var deadline = DateTime.UtcNow.AddSeconds(5);
                            while (Directory.GetFiles(destination).Length == 0 && DateTime.UtcNow < deadline)
                                Thread.Sleep(20);
                            if (effect == 1)
                            {
                                if (File.ReadAllText(Path.Combine(destination, "start.bat")) != File.ReadAllText(Path.Combine(fixture, "start.bat")))
                                    throw new InvalidOperationException("Native Ctrl copy content mismatch.");
                            }
                            else
                            {
                                var shortcut = Directory.GetFiles(destination, "*.lnk").Single();
                                var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
                                try
                                {
                                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(shortcut, 0);
                                    var path = new System.Text.StringBuilder(32768);
                                    Check(((DropShellLink)link).GetPath(path, path.Capacity, 0, 0));
                                    if (!string.Equals(path.ToString(), Path.Combine(fixture, "start.bat"), StringComparison.OrdinalIgnoreCase))
                                        throw new InvalidOperationException("Native shortcut target mismatch.");
                                }
                                finally { Marshal.FinalReleaseComObject(link); }
                            }
                            if (!File.Exists(Path.Combine(fixture, "start.bat"))) throw new InvalidOperationException("Copy/link removed source.");
                            Console.WriteLine($"product-drop-transfer: {name}; native-effect={effect}; content/target verified; source preserved");
                        }
                        finally
                        {
                            if (target != 0) Marshal.Release(target);
                            if (data != 0) Marshal.Release(data);
                            if (destinationFolder != 0) Marshal.Release(destinationFolder);
                            if (destinationRoot != 0) Marshal.FreeCoTaskMem(destinationRoot);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(children); Marshal.FreeCoTaskMem(child); }
            }
            Console.WriteLine($"product-drop-attributes: passed; native-transfer={transfer}; direct COM is not physical OLE routing acceptance");
            return 0;
        }
        finally
        {
            if (physical != 0) Marshal.Release(physical);
            if (folder != 0) Marshal.Release(folder);
            if (physicalRoot != 0) Marshal.FreeCoTaskMem(physicalRoot);
            if (root != 0) Marshal.FreeCoTaskMem(root);
            owner?.Dispose();
            Directory.Delete(fixture, true);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DropAttributes(nint self, uint count, nint children, ref uint attributes);
    private static void VerifyProductDragSource(nint folder, string expectedPath)
    {
        Check(At<ParseDisplayName>(folder, 3)(folder, 0, 0, "start.bat", 0, out var child, 0));
        var children = Marshal.AllocHGlobal(nint.Size);
        nint data = 0;
        try
        {
            Marshal.WriteIntPtr(children, child);
            uint attributes = 7; // Native source must advertise copy, move and link.
            Check(At<DropAttributes>(folder, 9)(folder, 1, children, ref attributes));
            if ((attributes & 7) != 7) throw new InvalidOperationException("Source lost copy/move/link capabilities.");
            Check(At<DropUiObject>(folder, 10)(folder, 0, 1, children, in DataObjectId, 0, out data));
            var wrapper = Marshal.GetObjectForIUnknown(data);
            try
            {
                var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
                {
                    cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                    lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                };
                ((System.Runtime.InteropServices.ComTypes.IDataObject)wrapper).GetData(ref format, out var medium);
                try
                {
                    var path = new System.Text.StringBuilder(32768);
                    if (DropQueryFile(medium.unionmember, uint.MaxValue, null, 0) != 1 ||
                        DropQueryFile(medium.unionmember, 0, path, (uint)path.Capacity) == 0 ||
                        !string.Equals(path.ToString(), expectedPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Projected source did not provide the physical CF_HDROP path.");
                }
                finally { DropReleaseMedium(ref medium); }
            }
            finally { Marshal.ReleaseComObject(wrapper); }
            Console.WriteLine("product-drag-source: copy/move/link capabilities; native IDataObject physical CF_HDROP verified");
        }
        finally
        {
            if (data != 0) Marshal.Release(data);
            Marshal.FreeHGlobal(children);
            Marshal.FreeCoTaskMem(child);
        }
    }
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
    private static extern uint DropQueryFile(nint drop, uint index, System.Text.StringBuilder? path, uint length);
    [DllImport("ole32.dll", EntryPoint = "ReleaseStgMedium")]
    private static extern void DropReleaseMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromIDList", PreserveSig = true)]
    private static extern int DropCreateShellItem(nint pidl, in Guid iid, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropItemAttributes(nint self, uint requested, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropUiObject(nint self, nint owner, uint count, nint children, in Guid iid, nint reserved, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropViewObject(nint self, nint owner, in Guid iid, out nint value);
    [StructLayout(LayoutKind.Sequential)] private struct DropPoint { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropEnter(nint self, nint data, uint keys, ref DropPoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropOver(nint self, uint keys, ref DropPoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DropLeave(nint self);
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DropShellLink
    {
        [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int length, nint findData, uint flags);
    }
}
