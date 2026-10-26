using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class ShellRefreshCompletionSmoke
{
    internal static void Run()
    {
        var events = new List<string>();
        ShellRefreshCompletion.Run(
            () => events.Add("complete"),
            () => true,
            () => events.Add("continuation"),
            _ => events.Add("error"));
        Check(events.SequenceEqual(["complete", "continuation"]),
            "a successful current completion runs its continuation once afterward");

        events.Clear();
        ShellRefreshCompletion.Run(
            () => throw new InvalidOperationException("readiness query failed"),
            () => true,
            () => events.Add("continuation"),
            _ => events.Add("completion-error"));
        Check(events.SequenceEqual(["completion-error", "continuation"]),
            "a current refresh completion failure is reported and still releases its caller continuation");

        events.Clear();
        var current = true;
        ShellRefreshCompletion.Run(
            () => { current = false; throw new InvalidOperationException("retired view"); },
            () => current,
            () => events.Add("continuation"),
            _ => events.Add("stale-error"));
        Check(events.Count == 0,
            "a completion failure after request retirement neither reports stale error nor continues stale work");

        events.Clear();
        ShellRefreshCompletion.Run(
            static () => { },
            () => true,
            () => throw new InvalidOperationException("continuation failed"),
            _ => events.Add("continuation-error"));
        Check(events.SequenceEqual(["continuation-error"]),
            "a current continuation failure is contained and reported");

        events.Clear();
        ShellRefreshCompletion.Run(
            () => throw new InvalidOperationException("completion failed"),
            () => true,
            () => events.Add("continuation"),
            _ => throw new InvalidOperationException("error observer failed"));
        Check(events.SequenceEqual(["continuation"]),
            "a failing error observer cannot prevent the current request continuation");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine($"PASS {description}");
    }
}
