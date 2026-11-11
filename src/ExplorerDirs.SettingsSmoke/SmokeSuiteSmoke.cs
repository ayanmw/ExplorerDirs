using System.IO;

internal static class SmokeSuiteSmoke
{
    internal static void Run()
    {
        using var output = new StringWriter();
        var suite = new SmokeSuite(output);
        var ran = new List<string>();
        suite.Run("first failure", () =>
        {
            ran.Add("first");
            try { throw new InvalidOperationException("first detail"); }
            finally { ran.Add("cleanup"); }
        });
        suite.Run("second failure", () => { ran.Add("second"); throw new IOException("second detail"); });
        suite.Run("later success", () => ran.Add("third"));
        if (!ran.SequenceEqual(["first", "cleanup", "second", "third"]) || suite.Complete() != 1)
            throw new Exception("Suite must continue in order after multiple failures, preserve cleanup, and fail overall.");
        var log = output.ToString();
        foreach (var expected in new[] { "first failure", "second failure", "later success", "first detail", "second detail", "InvalidOperationException", "IOException", "3 cases", "2 failed" })
            if (!log.Contains(expected, StringComparison.Ordinal))
                throw new Exception("Suite report is missing " + expected);
        using var successfulOutput = new StringWriter();
        var successful = new SmokeSuite(successfulOutput);
        successful.Run("success", () => { });
        if (successful.Complete() != 0) throw new Exception("A passing suite must return zero.");
    }
}
