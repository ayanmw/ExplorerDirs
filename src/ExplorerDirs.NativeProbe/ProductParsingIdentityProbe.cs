using System.Runtime.InteropServices;
using ExplorerDirs.App;

public static partial class SyntheticShellSortProbe
{
    internal static int RunProductParsingIdentity(string adapter)
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ParsingIdentity-" + Guid.NewGuid().ToString("N"));
        nint root = 0, folder = 0, linkChild = 0, directoryChild = 0;
        ProfileShellNamespaceOwner? owner = null;
        try
        {
            Directory.CreateDirectory(fixture);
            var target = Path.Combine(fixture, "launch target.bat");
            File.WriteAllText(target, "@rem fixture only; never executed\r\n");
            var link = Path.Combine(fixture, "快捷启动.lnk");
            Directory.CreateDirectory(Path.Combine(fixture, "快捷启动"));
            SaveParsingIdentityShortcut(link, target);
            var linkWithoutCollision = Path.Combine(fixture, "another link.lnk");
            SaveParsingIdentityShortcut(linkWithoutCollision, target);
            var classId = Guid.NewGuid();
            owner = ProfileShellNamespaceOwner.Start(classId, fixture, Path.GetFullPath(adapter));
            Check(SHParseDisplayName(CreatePathIdentityParsingName(classId, fixture), 0, out root, 0, out _));
            Check(BindProductDisplayFolder(0, root, 0, in ShellFolder2Id, out folder));
            // CF_HDROP must name the shortcut itself, never its target or the
            // different folder whose name matches the shortcut's friendly label.
            VerifyProductDragSource(folder, link);
            VerifyProductDragSource(folder, linkWithoutCollision);
            var parse = At<ParseDisplayName>(folder, 3);
            Check(parse(folder, 0, 0, "快捷启动.lnk", 0, out linkChild, 0));
            Check(parse(folder, 0, 0, "快捷启动", 0, out directoryChild, 0));
            var order = At<ProductItemCompare>(folder, 7)(folder, 0x10000000, linkChild, directoryChild);
            Check(order < 0 ? order : 0);
            if (unchecked((short)order) == 0)
                throw new InvalidOperationException("A shortcut and same-label directory became canonically identical.");
            var children = Marshal.AllocHGlobal(nint.Size);
            try
            {
                uint attributes = 0x20000000; // SFGAO_FOLDER; the shortcut target is a BAT file.
                Marshal.WriteIntPtr(children, linkChild);
                Check(At<DropAttributes>(folder, 9)(folder, 1, children, ref attributes));
                if ((attributes & 0x20000000) != 0)
                    throw new InvalidOperationException($"Shortcut was classified as the same-label directory: attributes=0x{attributes:X}.");
                attributes = 0x20000000;
                Marshal.WriteIntPtr(children, directoryChild);
                Check(At<DropAttributes>(folder, 9)(folder, 1, children, ref attributes));
                if ((attributes & 0x20000000) == 0) throw new InvalidOperationException("Actual directory lost its folder classification.");
            }
            finally { Marshal.FreeHGlobal(children); }
            Console.WriteLine("product-parsing-identity: PASS shortcut physical source and distinct same-label directory; no window or execution");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"product-parsing-identity: failed: {error}");
            return 1;
        }
        finally
        {
            foreach (var pidl in new[] { linkChild, directoryChild, root }) if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
            if (folder != 0) Marshal.Release(folder);
            owner?.Dispose();
            Directory.Delete(fixture, true);
        }
    }

    private static void SaveParsingIdentityShortcut(string destination, string target)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        nint unknown = 0, link = 0, targetPidl = 0;
        try
        {
            unknown = Marshal.GetIUnknownForObject(instance);
            var linkId = new Guid("000214F9-0000-0000-C000-000000000046");
            Check(Marshal.QueryInterface(unknown, in linkId, out link));
            Check(SHParseDisplayName(target, 0, out targetPidl, 0, out _));
            Check(At<IdentitySetLinkPidl>(link, 5)(link, targetPidl));
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)instance).Save(destination, true);
        }
        finally
        {
            if (targetPidl != 0) Marshal.FreeCoTaskMem(targetPidl);
            if (link != 0) Marshal.Release(link);
            if (unknown != 0) Marshal.Release(unknown);
            Marshal.FinalReleaseComObject(instance);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int IdentitySetLinkPidl(nint self, nint pidl);
}
