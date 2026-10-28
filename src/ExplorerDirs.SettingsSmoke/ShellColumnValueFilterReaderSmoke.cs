using System.Runtime.InteropServices;
using ExplorerDirs.App;

internal static class ShellColumnValueFilterReaderSmoke
{
    private static readonly Guid ShellFolder2Id = new("93F2F68C-1D1B-11D3-A30E-00C04F79ABD1");
    private static readonly PropertyKeyState FirstKey = new(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 4);
    private static readonly PropertyKeyState SecondKey = new(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);

    internal static void Run()
    {
        using var folder = new CountingShellFolder();
        using var valueFolder = new CountingShellFolder(returnValues: true);
        using var emptyValueFolder = new CountingShellFolder(returnEmptyValues: true);
        var pidl = Marshal.AllocHGlobal(2);
        try
        {
            Marshal.WriteInt16(pidl, 0);
            var filters = new[]
            {
                new ColumnValueFilter(FirstKey, ["TXT 文件"]),
                new ColumnValueFilter(SecondKey, ["1 KB"])
            };

            Require(ShellColumnValueFilterReader.MatchesFilters(folder.Pointer, pidl, filters),
                "unavailable properties fail open instead of rejecting the item");
            Require(folder.QueryInterfaceCalls == 1,
                $"one ShellFolder2 query is shared across the item's columns (actual {folder.QueryInterfaceCalls})");
            Require(folder.GetDetailsExCalls == 2, "each requested property is read once");

            Marshal.WriteInt16(pidl, 0);
            var displayFilters = new[]
            {
                new ColumnValueFilter(FirstKey, ["TXT 文件"]),
                new ColumnValueFilter(SecondKey, ["sample.txt"])
            };
            Require(ShellColumnValueFilterReader.MatchesFilters(valueFolder.Pointer, pidl, displayFilters),
                "matching Shell display values keep the item");
            Require(!ShellColumnValueFilterReader.MatchesFilters(valueFolder.Pointer, pidl,
                    [new ColumnValueFilter(FirstKey, ["BIN 文件"]), displayFilters[1]]),
                "a readable nonmatching Shell display value rejects the item");
            Require(valueFolder.QueryInterfaceCalls == 2 && valueFolder.GetDetailsExCalls == 3,
                "each item uses one ShellFolder2 query and stops reading after a known mismatch");

            Require(ShellColumnValueFilterReader.MatchesFilters(emptyValueFolder.Pointer, pidl,
                    [new ColumnValueFilter(FirstKey, [""])]),
                "a readable VT_EMPTY property matches the blank checklist value");
            Require(!ShellColumnValueFilterReader.MatchesFilters(emptyValueFolder.Pointer, pidl,
                    [new ColumnValueFilter(FirstKey, ["TXT 文件"])]),
                "a readable VT_EMPTY property does not fail open against a nonblank selected value");
        }
        finally { Marshal.FreeHGlobal(pidl); }
        Console.WriteLine("Column value batch smoke: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Column value batch failed: {message}");
    }

    private sealed class CountingShellFolder : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int QueryInterface(nint self, in Guid iid, out nint result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint ReferenceCount(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDetailsEx(nint self, nint childPidl, nint key, nint variant);

        private readonly QueryInterface _queryInterface;
        private readonly ReferenceCount _addRef;
        private readonly ReferenceCount _release;
        private readonly ShellColumnValueFilterReader.GetDetailsEx _getDetailsEx;
        private readonly nint _vtable;
        private readonly bool _returnValues;
        private readonly bool _returnEmptyValues;
        private int _references = 1;

        internal nint Pointer { get; }
        internal int QueryInterfaceCalls { get; private set; }
        internal int GetDetailsExCalls { get; private set; }

        internal CountingShellFolder()
            : this(returnValues: false) { }

        internal CountingShellFolder(bool returnValues = false, bool returnEmptyValues = false)
        {
            _returnValues = returnValues;
            _returnEmptyValues = returnEmptyValues;
            _queryInterface = QueryInterfaceImpl;
            _addRef = _ => (uint)Interlocked.Increment(ref _references);
            _release = _ => (uint)Interlocked.Decrement(ref _references);
            _getDetailsEx = GetDetailsExImpl;
            _vtable = Marshal.AllocHGlobal(18 * nint.Size);
            Pointer = Marshal.AllocHGlobal(nint.Size);
            for (var slot = 0; slot < 18; slot++) Marshal.WriteIntPtr(_vtable, slot * nint.Size, 0);
            Marshal.WriteIntPtr(_vtable, 0, Marshal.GetFunctionPointerForDelegate(_queryInterface));
            Marshal.WriteIntPtr(_vtable, nint.Size, Marshal.GetFunctionPointerForDelegate(_addRef));
            Marshal.WriteIntPtr(_vtable, 2 * nint.Size, Marshal.GetFunctionPointerForDelegate(_release));
            Marshal.WriteIntPtr(_vtable, 17 * nint.Size, Marshal.GetFunctionPointerForDelegate(_getDetailsEx));
            Marshal.WriteIntPtr(Pointer, _vtable);
        }

        private int QueryInterfaceImpl(nint self, in Guid iid, out nint result)
        {
            QueryInterfaceCalls++;
            if (iid != ShellFolder2Id)
            {
                result = 0;
                return unchecked((int)0x80004002); // E_NOINTERFACE
            }
            _ = _addRef(self);
            result = self;
            return 0;
        }

        private int GetDetailsExImpl(nint self, nint childPidl, nint key, nint variant)
        {
            GetDetailsExCalls++;
            if (_returnEmptyValues)
            {
                Marshal.WriteInt16(variant, 0, 0); // VT_EMPTY: a successful, readable blank property.
                return 0;
            }
            if (_returnValues)
            {
                var propertyId = unchecked((uint)Marshal.ReadInt32(key, 16));
                var text = propertyId == FirstKey.PropertyId ? "TXT 文件" : "sample.txt";
                Marshal.WriteInt16(variant, 0, 8); // VT_BSTR
                Marshal.WriteIntPtr(variant, 8, Marshal.StringToBSTR(text));
                return 0;
            }
            return unchecked((int)0x80004005); // E_FAIL: the test measures interface reuse, not property formatting.
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
            Marshal.FreeHGlobal(_vtable);
            GC.KeepAlive(_queryInterface);
            GC.KeepAlive(_addRef);
            GC.KeepAlive(_release);
            GC.KeepAlive(_getDetailsEx);
        }
    }
}
