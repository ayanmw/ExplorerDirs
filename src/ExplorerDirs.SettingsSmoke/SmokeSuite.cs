using System.IO;

internal sealed class SmokeSuite(TextWriter output)
{
    private readonly List<string> _failures = [];
    private int _cases;

    internal void Run(string name, Action test)
    {
        _cases++;
        output.WriteLine($"SMOKE START {name}");
        try
        {
            test();
            output.WriteLine($"SMOKE PASS {name}");
        }
        catch (Exception error)
        {
            _failures.Add(name);
            output.WriteLine($"SMOKE FAIL {name}");
            output.WriteLine(error);
        }
    }

    internal int Complete()
    {
        output.WriteLine($"SMOKE SUMMARY: {_cases} cases, {_cases - _failures.Count} passed, {_failures.Count} failed");
        foreach (var name in _failures) output.WriteLine($"FAILED: {name}");
        return _failures.Count == 0 ? 0 : 1;
    }
}
