using System.Globalization;
using System.Runtime.InteropServices;

// Runs outside the live DefView process boundary so provider enumeration can be
// compared without reentering a Shell view while it is processing updates.
internal static class ShellProviderEnumerationChildProbe
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run(Guid classId, string adapterPath, string fallbackRoot, string backingPath, bool configureRoot = true)
    {
        nint adapter = 0;
        nint absolutePidl = 0;
        IShellFolder? folder = null;
        IEnumIdList? items = null;
        try
        {
            if (configureRoot)
            {
                adapter = NativeLibrary.Load(adapterPath);
                var configure = Marshal.GetDelegateForFunctionPointer<ConfigureShellNamespaceRoot>(
                    NativeLibrary.GetExport(adapter, "ConfigureShellNamespaceRoot"));
                Marshal.ThrowExceptionForHR(configure(ref classId, fallbackRoot));
            }

            var parsingName = $"::{{{classId:D}}}\\edpath:{EncodePath(backingPath)}";
            Marshal.ThrowExceptionForHR(SHParseDisplayName(parsingName, 0, out absolutePidl, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToObject(0, absolutePidl, 0, in ShellFolderId, out folder));
            Marshal.ThrowExceptionForHR(folder.EnumObjects(0, 0x20, out items)); // SHCONTF_FOLDERS.

            var count = 0;
            while (true)
            {
                var result = items.Next(1, out var relativePidl, out var fetched);
                if (fetched != 0)
                {
                    count++;
                    Marshal.FreeCoTaskMem(relativePidl);
                }
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                if (fetched == 0) break;
            }

            Console.WriteLine($"provider-enumeration-child: count={count}; source=product-adapter");
            return 0;
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (absolutePidl != 0) Marshal.FreeCoTaskMem(absolutePidl);
            if (adapter != 0) NativeLibrary.Free(adapter);
        }
    }

    private static string EncodePath(string path) => string.Concat(path.Select(character =>
        ((ushort)character).ToString("X4", CultureInfo.InvariantCulture)));

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ConfigureShellNamespaceRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string path);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint owner, nint bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name,
            out uint charactersEaten, out nint relativePidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint owner, uint flags, [MarshalAs(UnmanagedType.Interface)] out IEnumIdList enumerator);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIdList
    {
        [PreserveSig] int Next(uint count, out nint item, out uint fetched);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint item, uint attributes,
        out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parentFolder, nint relativeOrAbsolutePidl, nint bindContext,
        in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);
}
