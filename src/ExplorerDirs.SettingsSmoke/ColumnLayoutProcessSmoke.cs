using System.Diagnostics;
using System.IO;
using System.Reflection;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ColumnLayoutProcessSmoke
{
    private static readonly PropertyKeyState RecursiveSizeKey = new(
        Guid.Parse("7E1F84B7-C052-4E51-8A3C-869254A6B910"), 1);

    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.ColumnLayoutProcess.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "ExplorerDirs.ini");
        try
        {
            var state = new SessionState(2, 4,
            [
                [new TabSettings("C:\\prototype", new ViewState(4, 16,
                    [new SortColumnState(RecursiveSizeKey, -1)], RecursiveSizeKey, false,
                    [new DisplayColumnState(new(Guid.Parse("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10), 240),
                     new DisplayColumnState(RecursiveSizeKey, 176)]))],
                [], [], []
            ], ActivePane: 0, ActiveTabIndices: [0, 0, 0, 0]);
            File.WriteAllText(path, SessionStore.Render(state));

            var assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Could not locate the SettingsSmoke assembly for the child process.");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(assemblyPath);
            start.ArgumentList.Add("--column-layout-process-reader");
            start.ArgumentList.Add(path);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the column-state reader process.");
            if (!child.WaitForExit(15000))
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("The column-state reader process did not finish within 15 seconds.");
            }
            var output = child.StandardOutput.ReadToEnd();
            var error = child.StandardError.ReadToEnd();
            if (child.ExitCode != 0)
                throw new InvalidOperationException($"Child process rejected persisted custom column state. Exit={child.ExitCode}; stdout={output}; stderr={error}");
            Console.WriteLine($"column-layout-process-roundtrip: {output.Trim()}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static void ReadInChildProcess(string path)
    {
        var parsed = SessionStore.ParseComplete(File.ReadAllText(path)) ??
            throw new InvalidDataException("The child process could not parse the persisted session.");
        var tab = parsed.Panes[0][0];
        var view = tab.View;
        if (view.DisplayColumns is not [{ Key: { PropertyId: 10 }, Width: 240 }, { Key: var columnKey, Width: 176 }] ||
            columnKey != RecursiveSizeKey ||
            view.SortColumns is not [{ Key: var sortKey, Direction: -1 }] || sortKey != RecursiveSizeKey ||
            view.GroupKey != RecursiveSizeKey || view.GroupAscending ||
            parsed.Panes[0][0].Path != "C:\\prototype")
            throw new InvalidDataException("Custom column identity, width, sort, group or tab path changed across processes.");

        Console.WriteLine($"key={RecursiveSizeKey}; width=176; sort=-1; group-ascending=False; path={tab.Path}");
    }
}
