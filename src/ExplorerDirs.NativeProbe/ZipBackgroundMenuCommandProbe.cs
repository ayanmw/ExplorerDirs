using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Queries ZIP-folder background menu labels and canonical verbs without creating
// or showing an IShellView. Each potentially blocking GetCommandString call runs
// in its own child process so a ZIP namespace extension cannot stall the probe.
internal static class ZipBackgroundMenuCommandProbe
{
    private const uint SfgaoFolder = 0x20000000;
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7fff;
    private const int CommandTimeoutMilliseconds = 6000;
    private const int TotalQueryBudgetMilliseconds = 45000;
    private const int MaximumCommands = 64;
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    internal static int Run()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.ZipMenu-{Guid.NewGuid():N}");
        var zipPath = Path.Combine(fixtureRoot, "menu-fixture.zip");
        nint foregroundBefore = GetForegroundWindow();
        var inputBefore = GetLastInputTick();
        try
        {
            Directory.CreateDirectory(fixtureRoot);
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                using (var entry = new StreamWriter(archive.CreateEntry("sample.txt").Open()))
                    entry.Write("temporary ZIP shell menu fixture");
                using (var entry = new StreamWriter(archive.CreateEntry("child-folder/nested.txt").Open()))
                    entry.Write("nested fixture");
            }

            var menuInfo = QueryMenu(zipPath, includeVerbs: false, commandOffset: 0);
            if (menuInfo.HResult < 0 || menuInfo.CommandCount == 0)
                throw new InvalidOperationException($"ZIP background menu query failed: hr=0x{menuInfo.HResult:X8}, commands={menuInfo.CommandCount}.");

            var queryCount = Math.Min(menuInfo.CommandCount, MaximumCommands);
            var deadline = Stopwatch.StartNew();
            var results = new List<string>((int)queryCount);
            for (uint offset = 0; offset < queryCount; offset++)
            {
                if (deadline.ElapsedMilliseconds >= TotalQueryBudgetMilliseconds)
                {
                    results.Add($"{offset}:budget-exhausted");
                    break;
                }

                var result = QueryVerbInChild(zipPath, offset,
                    Math.Min(CommandTimeoutMilliseconds,
                        TotalQueryBudgetMilliseconds - (int)deadline.ElapsedMilliseconds));
                results.Add(result);
                if (GetForegroundWindow() != foregroundBefore || GetLastInputTick() != inputBefore)
                {
                    Console.WriteLine("zip-background-menu: foreground-or-input-changed=True; stopped=True");
                    return 1;
                }
            }

            Console.WriteLine($"zip-background-menu: labels=[{string.Join('|', menuInfo.Labels)}]; commands={menuInfo.CommandCount}; queried={results.Count}; verbs=[{string.Join('|', results)}]; child-timeout-ms={CommandTimeoutMilliseconds}; total-budget-ms={TotalQueryBudgetMilliseconds}; shell-view-created=False; menu-shown=False; commands-invoked=False; foreground-unchanged={GetForegroundWindow() == foregroundBefore}; input-unchanged={GetLastInputTick() == inputBefore}");
            return results.All(static result =>
                !result.EndsWith(":timeout", StringComparison.Ordinal) &&
                !result.EndsWith(":budget-exhausted", StringComparison.Ordinal) &&
                !result.Contains(":error(", StringComparison.Ordinal)) ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"zip-background-menu: {exception.GetType().Name};hr=0x{exception.HResult:X8};detail={exception.Message}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    internal static int RunVerbChild(string zipPath, string offsetText)
    {
        if (!uint.TryParse(offsetText, out var offset)) return 64;
        try
        {
            Console.WriteLine($"stage=menu-query;offset={offset}");
            Console.Out.Flush();
            var result = QueryMenu(zipPath, includeVerbs: true, commandOffset: offset);
            Console.WriteLine($"zip-menu-verb-child: offset={offset};hr=0x{result.VerbHResult:X8};verb={result.Verb ?? "<empty>"}");
            return result.VerbHResult == 0 && !string.IsNullOrWhiteSpace(result.Verb) ? 0 : 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"zip-menu-verb-child: offset={offset};{exception.GetType().Name};hr=0x{exception.HResult:X8};detail={exception.Message}");
            return 1;
        }
    }

    private static string QueryVerbInChild(string zipPath, uint offset, int timeoutMilliseconds)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("The NativeProbe assembly path is unavailable."));
        start.ArgumentList.Add("--zip-background-menu-verb-child");
        start.ArgumentList.Add(zipPath);
        start.ArgumentList.Add(offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the isolated ZIP verb query process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return $"{offset}:timeout";
        }

        Task.WaitAll(stdout, stderr);
        var output = stdout.Result.Trim();
        if (process.ExitCode == 0)
        {
            var marker = "verb=";
            var markerIndex = output.LastIndexOf(marker, StringComparison.Ordinal);
            return markerIndex >= 0 ? $"{offset}:{output[(markerIndex + marker.Length)..]}" : $"{offset}:ok";
        }

        var error = stderr.Result.Trim();
        return $"{offset}:error(exit={process.ExitCode},{error})";
    }

    private static MenuQueryResult QueryMenu(string zipPath, bool includeVerbs, uint commandOffset)
    {
        nint absolutePidl = 0;
        nint folder = 0;
        nint contextMenu = 0;
        nint menu = 0;
        try
        {
            var parseHr = SHParseDisplayName(zipPath, 0, out absolutePidl, SfgaoFolder, out var attributes);
            if (parseHr < 0 || absolutePidl == 0 || (attributes & SfgaoFolder) == 0)
                return new MenuQueryResult(parseHr < 0 ? parseHr : unchecked((int)0x80004005), 0, [], null, parseHr);

            var bindHr = SHBindToObject(0, absolutePidl, 0, in ShellFolderId, out folder);
            if (bindHr < 0 || folder == 0)
                return new MenuQueryResult(bindHr, 0, [], null, bindHr);

            var createHr = At<CreateViewObject>(folder, 8)(folder, 0, in ContextMenuId, out contextMenu);
            if (createHr < 0 || contextMenu == 0)
                return new MenuQueryResult(createHr, 0, [], null, createHr);

            menu = CreatePopupMenu();
            if (menu == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create an in-memory menu.");
            var queryHr = At<QueryContextMenu>(contextMenu, 3)(contextMenu, menu, 0,
                FirstCommand, LastCommand, 0x0010 | 0x0800);
            if (queryHr < 0)
                return new MenuQueryResult(queryHr, 0, [], null, queryHr);

            var count = unchecked((uint)queryHr & 0xffff);
            var labels = ReadMenuLabels(menu);
            if (!includeVerbs) return new MenuQueryResult(queryHr, count, labels, null, 0);
            if (commandOffset >= count)
                return new MenuQueryResult(queryHr, count, labels, null, unchecked((int)0x80070057));

            var buffer = Marshal.AllocHGlobal(512);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var verbHr = At<GetCommandString>(contextMenu, 5)(contextMenu, commandOffset, 4, 0, buffer, 256);
                var verb = verbHr == 0 ? Marshal.PtrToStringUni(buffer) : null;
                return new MenuQueryResult(queryHr, count, labels, verb, verbHr);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            if (contextMenu != 0) Marshal.Release(contextMenu);
            if (folder != 0) Marshal.Release(folder);
            if (absolutePidl != 0) ILFree(absolutePidl);
        }
    }

    private static string[] ReadMenuLabels(nint menu)
    {
        var labels = new List<string>();
        var text = new StringBuilder(256);
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            text.Clear();
            _ = GetMenuStringW(menu, (uint)index, text, text.Capacity, 0x0400);
            var label = text.ToString().Replace("&", string.Empty, StringComparison.Ordinal);
            if (label.Length > 0) labels.Add(label);
        }
        return labels.ToArray();
    }

    private static T At<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private readonly record struct MenuQueryResult(int HResult, uint CommandCount,
        string[] Labels, string? Verb, int VerbHResult);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint TickCount;
    }

    private static uint GetLastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.TickCount : 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateViewObject(nint self, nint owner, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryContextMenu(nint self, nint menu, uint index, uint firstId, uint lastId, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCommandString(nint self, uint offset, uint type, uint reserved, nint buffer, uint cchMax);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHBindToObject(nint parent, nint pidl, nint bindContext, in Guid iid, out nint value);
    [DllImport("shell32.dll")]
    private static extern void ILFree(nint pidl);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(nint menu, uint item, StringBuilder text, int maxCount, uint flags);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
