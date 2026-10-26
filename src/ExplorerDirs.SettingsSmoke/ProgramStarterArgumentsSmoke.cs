using System.IO;
using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterArgumentsSmoke
{
    public static void Run()
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var argumentsType = assembly.GetType("ExplorerDirs.App.ProgramStarterArguments", throwOnError: false)
            ?? throw new Exception("ProgramStarterArguments must expand Q-Dir placeholders into safe argv values.");
        var contextType = assembly.GetType("ExplorerDirs.App.ProgramStarterContext", throwOnError: false)
            ?? throw new Exception("ProgramStarterContext must capture the current pane and its visible items.");
        var paneType = assembly.GetType("ExplorerDirs.App.ProgramStarterPaneContext", throwOnError: false)
            ?? throw new Exception("ProgramStarterPaneContext must identify each pane's directory and file snapshot.");
        var entryType = assembly.GetType("ExplorerDirs.App.ProgramStarterEntry", throwOnError: false)!;
        var build = argumentsType.GetMethod("Build", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("ProgramStarterArguments.Build is required.");

        var panes = Activator.CreateInstance(typeof(List<>).MakeGenericType(paneType))!;
        var directories = new[] { @"C:\Pane One\", @"D:\第二窗格", @"E:\Pane Three", @"F:\Pane Four" };
        for (var index = 0; index < directories.Length; index++)
        {
            var selected = new[] { $@"{(char)('C' + index)}:\资料 文件\选中 {index + 1}.txt" };
            var visible = new[] { selected[0], $@"{(char)('C' + index)}:\资料 文件\第二项 {index + 1}.txt" };
            panes.GetType().GetMethod("Add")!.Invoke(panes,
                [Activator.CreateInstance(paneType, directories[index], selected, visible)]);
        }

        var context = Activator.CreateInstance(contextType, 0, panes, @"D:\Apps\Explorer Dirs\ExplorerDirs.exe",
            @"C:\Windows", @"C:\Windows\System32", @"C:\Users\U\桌面", @"C:\Users\U\AppData\Roaming",
            @"C:\Users\U\Documents") ?? throw new Exception("ProgramStarterContext constructor returned null.");

        object Entry(string executable, string args, string workingDirectory = "") =>
            Activator.CreateInstance(entryType, Guid.NewGuid(), "test", executable, args, workingDirectory)!;
        object Build(string executable, string args, string workingDirectory = "") =>
            build.Invoke(null, [Entry(executable, args, workingDirectory), context])!;
        bool Success(object result) => (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
        object Command(object result) => result.GetType().GetProperty("Command")!.GetValue(result)!;
        IReadOnlyList<string> Argv(object result) => (IReadOnlyList<string>)Command(result)
            .GetType().GetProperty("Arguments")!.GetValue(Command(result))!;
        string Error(object result) => (string?)result.GetType().GetProperty("Error")!.GetValue(result) ?? "";

        var command = Build(@"C:\Tools\helper.exe", "--here \"%curdir%\" %sel_files% %sel_files2% %all_files3%",
            "%curdir4%");
        Check(Success(command), "scalar placeholders and pane-specific file lists build successfully");
        Check(Argv(command).SequenceEqual([
                "--here", directories[0], $@"C:\资料 文件\选中 1.txt",
                $@"D:\资料 文件\选中 2.txt", $@"E:\资料 文件\选中 3.txt", $@"E:\资料 文件\第二项 3.txt"]),
            "active and numbered selected/all-file placeholders expand to separate arguments");
        Check((string)Command(command).GetType().GetProperty("WorkingDirectory")!.GetValue(Command(command))! == directories[3],
            "%curdir4% resolves in the working directory");

        var scalar = Build("%windir%\\notepad.exe",
            "%sysdir% %desktop% %appdat% %personal% %drive% %fdir% %curdir1% %curdir2% %curdir3% %curdir4%");
        Check(Success(scalar) && (string)Command(scalar).GetType().GetProperty("FileName")!.GetValue(Command(scalar))! ==
              @"C:\Windows\notepad.exe" && Argv(scalar).SequenceEqual([
                  @"C:\Windows\System32", @"C:\Users\U\桌面", @"C:\Users\U\AppData\Roaming",
                  @"C:\Users\U\Documents", @"D:\", @"C:\资料 文件", directories[0], directories[1], directories[2], directories[3]]),
            "documented scalar placeholders resolve in executable and arguments");

        var allLists = Build(@"C:\Tools\helper.exe", "%sel_files% %all_files% %sel_files1% %sel_files2% %sel_files3% %sel_files4% %all_files1% %all_files2% %all_files3% %all_files4%");
        Check(Success(allLists) && Argv(allLists).Count == 15,
            "all documented selected/all-file list placeholders support pane suffixes one through four");

        var unsafeList = Build(@"C:\Tools\helper.exe", "prefix%sel_files%suffix");
        Check(!Success(unsafeList) && Error(unsafeList).Contains("独立参数", StringComparison.Ordinal),
            "list placeholders embedded in a larger argument are rejected clearly");
        var unknown = Build(@"C:\Tools\helper.exe", "%not_a_placeholder%");
        Check(!Success(unknown) && Error(unknown).Contains("未知", StringComparison.Ordinal),
            "unknown placeholders are rejected clearly");
        var malformed = Build(@"C:\Tools\helper.exe", "%curdir");
        Check(!Success(malformed) && Error(malformed).Length > 0,
            "malformed percent tokens are rejected");

        var emptyContext = Activator.CreateInstance(contextType, 0,
            EmptySelectedPanes(paneType, directories), @"D:\Apps\Explorer Dirs\ExplorerDirs.exe",
            @"C:\Windows", @"C:\Windows\System32", @"C:\Users\U\桌面", @"C:\Users\U\AppData\Roaming",
            @"C:\Users\U\Documents")!;
        var emptyResult = build.Invoke(null, [Entry(@"C:\Tools\helper.exe", "%sel_files%"), emptyContext])!;
        Check(!Success(emptyResult) && Error(emptyResult).Length > 0,
            "a list placeholder with no selected files reports a validation error");

        var virtualPanes = Activator.CreateInstance(typeof(List<>).MakeGenericType(paneType))!;
        virtualPanes.GetType().GetMethod("Add")!.Invoke(virtualPanes,
            [Activator.CreateInstance(paneType, "::{SHELL}", new[] { "::{SHELL}\\item" }, Array.Empty<string>())]);
        var virtualContext = Activator.CreateInstance(contextType, 0, virtualPanes, @"D:\Apps\Explorer Dirs\ExplorerDirs.exe",
            @"C:\Windows", @"C:\Windows\System32", @"C:\Users\U\桌面", @"C:\Users\U\AppData\Roaming",
            @"C:\Users\U\Documents")!;
        var virtualResult = build.Invoke(null, [Entry(@"C:\Tools\helper.exe", "%sel_files%"), virtualContext])!;
        Check(!Success(virtualResult) && Error(virtualResult).Length > 0,
            "Shell virtual items without filesystem paths cannot be passed as files");
        var virtualWorking = build.Invoke(null, [Entry(@"C:\Tools\helper.exe", ""), virtualContext])!;
        Check(Success(virtualWorking) && (string)Command(virtualWorking).GetType().GetProperty("WorkingDirectory")!
                  .GetValue(Command(virtualWorking))! == @"D:\Apps\Explorer Dirs",
            "virtual Shell locations use the application directory as a valid default working directory");

        var trailing = Build(@"C:\Tools\helper.exe", "\"%curdir%\"", @"C:\Trailing Space\");
        Check(Success(trailing) && Argv(trailing).SequenceEqual([directories[0]]) &&
              (string)Command(trailing).GetType().GetProperty("WorkingDirectory")!.GetValue(Command(trailing))! == @"C:\Trailing Space\",
            "quoted paths with spaces and trailing separators remain a single exact argument");
        var relativeWorkingDirectory = Build(@"C:\Tools\helper.exe", "", Path.Combine("..", "Portable Tools"));
        Check(Success(relativeWorkingDirectory) &&
              (string)Command(relativeWorkingDirectory).GetType().GetProperty("WorkingDirectory")!
                  .GetValue(Command(relativeWorkingDirectory))! == @"D:\Apps\Portable Tools",
            "relative imported working directories resolve from the application directory");
        var quoteTokens = Build(@"C:\Tools\helper.exe", "--label \"他说 \\\"你好\\\"\" \"\"");
        Check(Success(quoteTokens) && Argv(quoteTokens).SequenceEqual(["--label", "他说 \"你好\"", ""]),
            "Windows quote escaping preserves embedded quotes and empty arguments");
        var equalsTokens = Build(@"C:\Tools\helper.exe", "\"--include=*.log\" \"--mode=a=b\"");
        Check(Success(equalsTokens) && Argv(equalsTokens).SequenceEqual(["--include=*.log", "--mode=a=b"]),
            "quoted Q-Dir arguments containing equals signs retain separate argv boundaries");

        Console.WriteLine("Program Starter arguments smoke: PASS");
    }

    private static object EmptySelectedPanes(Type paneType, IReadOnlyList<string> directories)
    {
        var panes = Activator.CreateInstance(typeof(List<>).MakeGenericType(paneType))!;
        for (var index = 0; index < directories.Count; index++)
            panes.GetType().GetMethod("Add")!.Invoke(panes,
                [Activator.CreateInstance(paneType, directories[index], Array.Empty<string>(), Array.Empty<string>())]);
        return panes;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Program Starter arguments: " + message);
        Console.WriteLine("PASS " + message);
    }
}
