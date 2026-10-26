using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ProgramStarterLauncherSmoke
{
    public static void Run()
    {
        var assembly = typeof(UiPreferencesStore).Assembly;
        var launcherType = assembly.GetType("ExplorerDirs.App.ProgramStarterLauncher", throwOnError: false)
            ?? throw new Exception("ProgramStarterLauncher must prepare and dispatch ordinary-user processes.");
        var commandType = assembly.GetType("ExplorerDirs.App.ProgramStarterCommand", throwOnError: false)
            ?? throw new Exception("ProgramStarterCommand must carry exact argv boundaries.");
        var prepare = launcherType.GetMethod("Prepare", BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("ProgramStarterLauncher.Prepare is required.");
        var start = launcherType.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("ProgramStarterLauncher.Start is required.");
        var root = Path.Combine(AppContext.BaseDirectory, "program-starter-launcher-smoke");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "helper.exe");
            File.WriteAllText(executable, "fixture");
            var working = Path.Combine(root, "working");
            Directory.CreateDirectory(working);

            var relativeExecutable = Path.Combine(root, "relative", "portable-helper.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(relativeExecutable)!);
            File.WriteAllText(relativeExecutable, "fixture");
            var relativeProgramPath = Path.GetRelativePath(AppContext.BaseDirectory, relativeExecutable);
            var relativeWorkingDirectory = Path.GetRelativePath(AppContext.BaseDirectory, working);
            var qdrPreview = QdrProgramImporter.Parse(
                $"[Program]{Environment.NewLine}Portable Tool=\"{relativeProgramPath}\"=={relativeWorkingDirectory}");
            var imported = qdrPreview.Entries.Single();
            var context = new ProgramStarterContext(0,
                [new ProgramStarterPaneContext(@"C:\Pane", [], [])],
                Path.Combine(AppContext.BaseDirectory, "ExplorerDirs.exe"),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory,
                "", "", "");
            var builtImport = ProgramStarterArguments.Build(imported, context);
            Check(builtImport.Success, "relative imported command has valid expanded arguments");
            var importedStartInfo = ProgramStarterLauncher.Prepare(builtImport.Command!);
            Check(importedStartInfo.FileName == Path.GetFullPath(relativeExecutable) &&
                  importedStartInfo.WorkingDirectory == working,
                "an imported Q-Dir row resolves its relative executable and working directory without launching it");

            var command = Activator.CreateInstance(commandType, executable,
                (IReadOnlyList<string>)["one", "two words", "他说 \"你好\"", ""], working)!;
            var info = (ProcessStartInfo)prepare.Invoke(null, [command])!;
            Check(info.FileName == executable && info.WorkingDirectory == working &&
                  !info.UseShellExecute && info.Verb.Length == 0,
                "prepared process uses the configured executable and directory without shell or elevation");
            Check(info.ArgumentList.SequenceEqual(["one", "two words", "他说 \"你好\"", ""]),
                "prepared process preserves each argument boundary including spaces, quotes, and empty values");

            ProcessStartInfo? captured = null;
            var launcher = Activator.CreateInstance(launcherType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [new Func<ProcessStartInfo, bool>(prepared => { captured = prepared; return true; })],
                culture: null)!;
            var started = start.Invoke(launcher, [command])!;
            Check((bool)started.GetType().GetProperty("Started")!.GetValue(started)! && captured is not null &&
                  captured.ArgumentList.SequenceEqual(info.ArgumentList),
                "injected process dispatch receives prepared arguments without launching a real executable");

            var probeOutput = Path.Combine(root, "received-arguments.json");
            var childArguments = new List<string>();
            var processPath = Environment.ProcessPath
                ?? throw new Exception("The current smoke process path is unavailable.");
            if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                childArguments.Add(Assembly.GetEntryAssembly()!.Location);
            childArguments.AddRange(["--program-starter-argv-probe", probeOutput,
                "plain", "two words", "Unicode 中文", "embedded \"quotes\"", "equals=a=b", ""]);
            var realLauncher = Activator.CreateInstance(launcherType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [null], culture: null)!;
            var realCommand = Activator.CreateInstance(commandType, processPath,
                (IReadOnlyList<string>)childArguments, working)!;
            var realStart = start.Invoke(realLauncher, [realCommand])!;
            Check((bool)realStart.GetType().GetProperty("Started")!.GetValue(realStart)!,
                "the real launcher starts the isolated argument-probe child process");
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(probeOutput) && deadline.Elapsed < TimeSpan.FromSeconds(10))
                Thread.Sleep(20);
            var received = File.Exists(probeOutput)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(probeOutput))
                : null;
            Check(received is not null && received.SequenceEqual(childArguments.Skip(
                    childArguments.IndexOf("--program-starter-argv-probe") + 2)),
                "a real child process receives Unicode, quoted, spaced, equals, and empty argv values without boundary changes");

            var badWorking = Activator.CreateInstance(commandType, executable, Array.Empty<string>(),
                Path.Combine(root, "missing"))!;
            var invalid = false;
            try { _ = prepare.Invoke(null, [badWorking]); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException or DirectoryNotFoundException)
            { invalid = true; }
            Check(invalid, "missing working directories are rejected before dispatch");

            var failingLauncher = Activator.CreateInstance(launcherType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: [new Func<ProcessStartInfo, bool>(_ => throw new IOException("fixture failure"))],
                culture: null)!;
            var failure = start.Invoke(failingLauncher, [command])!;
            Check(!(bool)failure.GetType().GetProperty("Started")!.GetValue(failure)! &&
                  !string.IsNullOrWhiteSpace((string?)failure.GetType().GetProperty("Error")!.GetValue(failure)),
                "process start failures are returned as user-readable results");
            Console.WriteLine("Program Starter launcher smoke: PASS");
        }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))
                Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Program Starter launcher: " + message);
        Console.WriteLine("PASS " + message);
    }
}
