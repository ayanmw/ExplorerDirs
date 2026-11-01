using System.Runtime.InteropServices;

namespace ExplorerDirs.NativeProbe;

// Loads the production-exports variant without registering a Shell namespace or creating a view.
internal static class ProductShellAdapterBuildSmoke
{
    private static readonly Guid ClassFactoryId = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly Guid PersistFolder2Id = new("1AC3D9F0-175C-11D1-95BE-00609797EA4F");

    internal static int Run()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "ExplorerDirs-ProductAdapterLoad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var classId = Guid.NewGuid();
        nint module = 0;
        var oleInitialized = false;
        try
        {
            var oleResult = OleInitialize(0);
            if (oleResult < 0) Marshal.ThrowExceptionForHR(oleResult);
            oleInitialized = true;
            module = NativeLibrary.Load(FindProductAdapter());
            var configure = Marshal.GetDelegateForFunctionPointer<ConfigureShellNamespaceRoot>(
                NativeLibrary.GetExport(module, "ConfigureShellNamespaceRoot"));
            Marshal.ThrowExceptionForHR(configure(ref classId, fixture));

            var getClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObject>(
                NativeLibrary.GetExport(module, "DllGetClassObject"));
            var factoryId = ClassFactoryId;
            Marshal.ThrowExceptionForHR(getClassObject(ref classId, ref factoryId, out var factory));
            if (factory == 0) throw new InvalidOperationException("The product adapter returned no class factory.");

            nint folder = 0;
            nint persistFolder = 0;
            nint rootPidl = 0;
            try
            {
                var createInstance = At<CreateInstance>(factory, 3);
                var folderId = ShellFolder2Id;
                Marshal.ThrowExceptionForHR(createInstance(factory, 0, ref folderId, out folder));
                if (folder == 0) throw new InvalidOperationException("The product class factory returned no IShellFolder2 instance.");
                var queryInterface = At<QueryInterface>(folder, 0);
                var persistId = PersistFolder2Id;
                Marshal.ThrowExceptionForHR(queryInterface(folder, ref persistId, out persistFolder));
                Marshal.ThrowExceptionForHR(SHParseDisplayName(fixture, 0, out rootPidl, 0, out _));
                Marshal.ThrowExceptionForHR(At<InitializeFolder>(persistFolder, 4)(persistFolder, rootPidl));
                var detailsSize = IntPtr.Size == 8 ? 280 : 272;
                var details = Marshal.AllocHGlobal(detailsSize);
                try
                {
                    for (var offset = 0; offset < detailsSize; offset++) Marshal.WriteByte(details, offset, 0);
                    Marshal.ThrowExceptionForHR(At<GetDetailsOf>(folder, 18)(folder, 0, 1, details));
                    var title = Marshal.PtrToStringUni(Marshal.ReadIntPtr(details, IntPtr.Size == 8 ? 16 : 12));
                    if (title != "Recursive Size")
                        throw new InvalidOperationException($"The product Shell size column title was '{title ?? "<null>"}', not 'Recursive Size'.");
                }
                finally
                {
                    var titlePointer = Marshal.ReadIntPtr(details, IntPtr.Size == 8 ? 16 : 12);
                    if (titlePointer != 0) Marshal.FreeCoTaskMem(titlePointer);
                    Marshal.FreeHGlobal(details);
                }
            }
            finally
            {
                if (rootPidl != 0) Marshal.FreeCoTaskMem(rootPidl);
                if (persistFolder != 0) Marshal.Release(persistFolder);
                if (folder != 0) Marshal.Release(folder);
                Marshal.Release(factory);
            }

            var canUnloadNow = Marshal.GetDelegateForFunctionPointer<DllCanUnloadNow>(
                NativeLibrary.GetExport(module, "DllCanUnloadNow"));
            var canUnloadResult = canUnloadNow();
            Marshal.ThrowExceptionForHR(canUnloadResult);
            if (canUnloadResult != 0) throw new InvalidOperationException("The product adapter still reported live objects after releasing its factory and folder.");
            Console.WriteLine($"product-shell-adapter-build-smoke: generic-config-export=True; class-factory-created=True; shell-folder-created=True; recursive-size-default-column=True; dll-can-unload=True; shell-view-created=False; registry-written=False; class={classId:D}");
            return 0;
        }
        finally
        {
            if (module != 0) NativeLibrary.Free(module);
            if (oleInitialized) OleUninitialize();
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static string FindProductAdapter()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "bin", "native-product-x64", "ExplorerDirs.ShellNamespace.dll");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Run build-product-shell-folder-adapter.ps1 before the product adapter build smoke.");
    }

    private static T At<T>(nint instance, int index) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, index * IntPtr.Size));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int ConfigureShellNamespaceRoot(ref Guid classId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DllGetClassObject(ref Guid classId, ref Guid interfaceId, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DllCanUnloadNow();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryInterface(nint self, ref Guid interfaceId, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateInstance(nint self, nint outer, ref Guid interfaceId, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int InitializeFolder(nint self, nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDetailsOf(nint self, nint item, uint column, nint details);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint attributesOut);
}
