using System.Runtime.InteropServices;
using System.Diagnostics;
using ExplorerDirs.App;

// Read-only Shell identity inspection. Does not navigate, invoke or show a view.
internal static class SearchFolderProbe
{
    internal static int EnumerateVirtualSearch(string uri)
    {
        nint pidl = 0, folder = 0, enumerator = 0, child = 0;
        try
        {
            Check(CreateBindCtx(0, out var context));
            try
            {
                context.RegisterObjectParam("Parse Prefer Folder Browsing", new object());
                Check(ParseWithContext(uri, context, out pidl, 0, out _));
            }
            finally { context.ReleaseBoundObjects(); Marshal.ReleaseComObject(context); }
            var folderId = new Guid("000214E6-0000-0000-C000-000000000046");
            Check(SHBindToObject(0, pidl, 0, in folderId, out folder));
            Check(At<EnumObjects>(folder, 4)(folder, 0, 0x60, out enumerator));
            var hr = At<NextItem>(enumerator, 3)(enumerator, 1, out child, out var fetched);
            Console.WriteLine($"result-enumeration: next-hr={hr:X8}; fetched={fetched}; has-item={child != 0}");
            return hr == 0 && fetched == 1 && child != 0 || hr == 1 && fetched == 0 && child == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine($"result-enumeration-failed: {ex.GetType().Name}: {ex.Message}"); return 1; }
        finally
        {
            if (child != 0) Marshal.FreeCoTaskMem(child);
            if (enumerator != 0) Marshal.Release(enumerator);
            if (folder != 0) Marshal.Release(folder);
            if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static bool RunEnumerationWithTimeout(string uri)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "ExplorerDirs.NativeProbe.exe");
        if (!File.Exists(exe)) { Console.WriteLine("result-enumeration=failed (probe apphost unavailable)"); return false; }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--search-enumerate-uri");
        process.StartInfo.ArgumentList.Add(uri);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(8000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Console.WriteLine("result-enumeration=timed-out after 8 seconds; child terminated");
            return false;
        }
        Console.Write(stdout.GetAwaiter().GetResult());
        if (process.ExitCode != 0) Console.WriteLine($"enumeration-child-exit={process.ExitCode}: {stderr.GetAwaiter().GetResult()}");
        return process.ExitCode == 0;
    }

    internal static int ProbeVirtualScopes()
    {
        var foreground = GetForegroundWindow();
        var passed = true;
        var factoryClass = new Guid("14010e02-bbbd-41f0-88e3-eda371216584");
        var factoryInterface = new Guid("a0ffbc28-5482-4366-be27-3e81e78e06c2");
        foreach (var scopeName in new[]
        {
            "shell:Desktop", "shell:MyComputerFolder", "shell:RecycleBinFolder",
            "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
        })
        {
            nint scopePidl = 0, scopeArray = 0, factory = 0, searchPidl = 0, uriPidl = 0;
            try
            {
                Check(SHParseDisplayName(scopeName, 0, out scopePidl, 0, out _));
                Check(SHCreateShellItemArrayFromIDLists(1, new[] { scopePidl }, out scopeArray));
                Check(CoCreateInstance(ref factoryClass, 0, 1, ref factoryInterface, out factory));
                Check(At<SetObject>(factory, 11)(factory, scopeArray));
                Check(At<SetName>(factory, 3)(factory, "Explorer Dirs virtual-scope probe"));
                Check(At<GetPidl>(factory, 14)(factory, out searchPidl));
                passed &= Report($"factory-scope-{scopeName}", searchPidl);

                var uri = "search-ms:query=" + Uri.EscapeDataString("probe") +
                    "&crumb=location:" + Uri.EscapeDataString(scopeName);
                Check(CreateBindCtx(0, out var context));
                try
                {
                    context.RegisterObjectParam("Parse Prefer Folder Browsing", new object());
                    var hr = ParseWithContext(uri, context, out uriPidl, 0, out _);
                    Console.WriteLine($"uri-scope-{scopeName}: parse-hr={hr:X8}; parsed={(hr >= 0 && uriPidl != 0)}");
                    if (hr < 0 || uriPidl == 0) passed = false;
                    else passed &= Report($"uri-scope-{scopeName}", uriPidl);
                }
                finally { context.ReleaseBoundObjects(); Marshal.ReleaseComObject(context); }
                passed &= RunEnumerationWithTimeout(uri);
            }
            catch (Exception ex) { Console.WriteLine($"scope-{scopeName}: {ex.GetType().Name}: {ex.Message}"); passed = false; }
            finally
            {
                if (uriPidl != 0) Marshal.FreeCoTaskMem(uriPidl);
                if (searchPidl != 0) Marshal.FreeCoTaskMem(searchPidl);
                if (factory != 0) Marshal.Release(factory);
                if (scopeArray != 0) Marshal.Release(scopeArray);
                if (scopePidl != 0) Marshal.FreeCoTaskMem(scopePidl);
            }
        }
        if (GetForegroundWindow() != foreground)
            throw new Exception("Virtual-scope probe changed the foreground window.");
        Console.WriteLine($"Virtual-scope probe: {(passed ? "PASS" : "FAIL")}; enumerates at most one result per search URI; this is not a complete result-set or UI claim.");
        return passed ? 0 : 1;
    }

    internal static void Navigate(nint browser, string root, NavigationProbeEvents events)
    {
        var history = new ShellSearchHistory();
        events.InspectLocation = (stage, pidl) => Console.WriteLine($"search-{stage}: original-identity={history.TryResolve(pidl, out _)}");
        foreach (var query in new[] { "three", "five" })
        {
            var uri = ShellSearchLocation.BuildUri(root, query);
            var pidl = ShellSearchLocation.ParsePidl(uri);
            try
            {
                history.Remember(pidl, uri);
                Check(At<Browse>(browser, 13)(browser, pidl, 0));
                AwaitSearch(browser, history, uri, query + ".txt");
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        Check(At<Browse>(browser, 13)(browser, 0, 0x4000));
        AwaitSearch(browser, history, ShellSearchLocation.BuildUri(root, "three"), "three.txt");
        Check(At<Browse>(browser, 13)(browser, 0, 0x8000));
        AwaitSearch(browser, history, ShellSearchLocation.BuildUri(root, "five"), "five.txt");
        Console.WriteLine("search-folders: query results and native back/forward identity passed; no visible window or input.");
    }

    private static void AwaitSearch(nint browser, ShellSearchHistory history, string expectedUri, string expectedName)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var identity = false;
        var names = "unavailable";
        while (clock.ElapsedMilliseconds < 10000)
        {
            System.Windows.Forms.Application.DoEvents();
            identity = false;
            var pidl = ShellCurrentLocation.ReadPidl(browser);
            if (pidl != 0)
            {
                try { identity = history.TryResolve(pidl, out var uri) && uri == expectedUri; }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            names = ReadNames(browser);
            if (identity && names == expectedName)
            {
                Console.WriteLine($"search-result expected={expectedName}; native-identity=True; names={names}");
                return;
            }
            Thread.Sleep(10);
        }
        throw new Exception($"Search did not settle: identity={identity}; names={names}; expected={expectedName}");
    }

    private static string ReadNames(nint browser)
    {
        var viewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        var arrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
        nint view = 0, array = 0;
        try
        {
            if (At<GetInterface>(browser, 17)(browser, ref viewId, out view) != 0 || view == 0 ||
                At<GetItems>(view, 8)(view, 2, ref arrayId, out array) != 0 || array == 0) return "unavailable";
            Check(At<Count>(array, 7)(array, out var count));
            if (count > 8) throw new Exception("Unexpected search fixture size.");
            var names = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                Check(At<ItemAt>(array, 8)(array, i, out var item));
                nint name = 0;
                try
                {
                    Check(At<GetName>(item, 5)(item, 0x80058000, out name));
                    names.Add(Path.GetFileName(Marshal.PtrToStringUni(name)) ?? "missing");
                }
                finally { if (name != 0) Marshal.FreeCoTaskMem(name); Marshal.Release(item); }
            }
            return string.Join('|', names.Order(StringComparer.Ordinal));
        }
        finally { if (array != 0) Marshal.Release(array); if (view != 0) Marshal.Release(view); }
    }
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        nint factory = 0, scopePidl = 0, scope = 0, folderPidl = 0, uriPidl = 0;
        try
        {
            var clsid = new Guid("14010e02-bbbd-41f0-88e3-eda371216584");
            var iid = new Guid("a0ffbc28-5482-4366-be27-3e81e78e06c2");
            Check(CoCreateInstance(ref clsid, 0, 1, ref iid, out factory));
            Check(SHParseDisplayName(root, 0, out scopePidl, 0, out _));
            Check(SHCreateShellItemArrayFromIDLists(1, new[] { scopePidl }, out scope));
            Check(At<SetObject>(factory, 11)(factory, scope));
            Check(At<SetName>(factory, 3)(factory, "Explorer Dirs isolated search identity probe"));
            Check(At<GetPidl>(factory, 14)(factory, out folderPidl));
            Check(SHParseDisplayName(ShellSearchLocation.BuildUri(root, "sample"), 0, out uriPidl, 0, out _));
            Report("factory-with-scope-no-filter", folderPidl);
            Report("parsed-search-ms-with-query", uriPidl);
            Check(CreateBindCtx(0, out var context));
            try
            {
                context.RegisterObjectParam("Parse Prefer Folder Browsing", new object());
                var result = ParseWithContext(ShellSearchLocation.BuildUri(root, "sample"), context, out var preferred, 0, out _);
                Console.WriteLine($"prefer-folder-parse-hr={result:X8}");
                if (result >= 0 && preferred != 0)
                {
                    try
                    {
                        Report("prefer-folder-search-ms", preferred);
                        Check(ParseWithContext(ShellSearchLocation.BuildUri(root, "another-query"), context, out var other, 0, out _));
                        try
                        {
                            ShellSearchLocation.TryGetParsingName(preferred, out var firstName);
                            ShellSearchLocation.TryGetParsingName(other, out var secondName);
                            Console.WriteLine($"different-queries: same-parsing-name={firstName == secondName}; same-pidl={ILIsEqual(preferred, other)}; same-bytes={Bytes(preferred).SequenceEqual(Bytes(other))}");
                        }
                        finally { Marshal.FreeCoTaskMem(other); }
                    }
                    finally { Marshal.FreeCoTaskMem(preferred); }
                }
            }
            finally { context.ReleaseBoundObjects(); Marshal.ReleaseComObject(context); }
            Console.WriteLine("Identity inspection only; different conditions, no result or navigation equivalence claim.");
            return 0;
        }
        finally
        {
            if (uriPidl != 0) Marshal.FreeCoTaskMem(uriPidl);
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            if (scope != 0) Marshal.Release(scope);
            if (scopePidl != 0) Marshal.FreeCoTaskMem(scopePidl);
            if (factory != 0) Marshal.Release(factory);
        }
    }

    private static bool Report(string label, nint pidl)
    {
        var iid = new Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93");
        Check(SHCreateItemFromIDList(pidl, ref iid, out var item));
        try
        {
            var hr = At<GetAttributes>(item, 6)(item, 0x68000000, out var attributes);
            var nameReadable = ShellSearchLocation.TryGetParsingName(pidl, out var name);
            var searchUri = nameReadable && name.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"{label}: attributes-hr={hr:X8}; folder={(attributes & 0x20000000) != 0}; browsable={(attributes & 0x08000000) != 0}; filesystem={(attributes & 0x40000000) != 0}; search-ms-name={searchUri}");
            return hr >= 0 && (attributes & 0x20000000) != 0;
        }
        finally { Marshal.Release(item); }
    }
    private static T At<T>(nint value, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * nint.Size));
    private static byte[] Bytes(nint pidl)
    {
        var size = ILGetSize(pidl);
        if (size is < 2 or > 1048576) throw new Exception("Unexpected search PIDL size.");
        var bytes = new byte[size]; Marshal.Copy(pidl, bytes, 0, bytes.Length); return bytes;
    }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetObject(nint self, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetName(nint self, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetPidl(nint self, out nint pidl);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetAttributes(nint self, uint mask, out uint attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Browse(nint self, nint pidl, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetInterface(nint self, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetItems(nint self, uint flags, ref Guid iid, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Count(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ItemAt(nint self, uint index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumObjects(nint self, nint owner, uint flags, out nint enumerator);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NextItem(nint self, uint count, out nint item, out uint fetched);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(uint reserved, out System.Runtime.InteropServices.ComTypes.IBindCtx context);
    [DllImport("shell32.dll", EntryPoint = "SHParseDisplayName", CharSet = CharSet.Unicode)] private static extern int ParseWithContext(string name, System.Runtime.InteropServices.ComTypes.IBindCtx context, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls, out nint array);
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(nint pidl, ref Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern bool ILIsEqual(nint first, nint second);
    [DllImport("shell32.dll")] private static extern uint ILGetSize(nint pidl);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("shell32.dll")] private static extern int SHBindToObject(nint folder, nint pidl, nint bind, in Guid iid, out nint value);
}
