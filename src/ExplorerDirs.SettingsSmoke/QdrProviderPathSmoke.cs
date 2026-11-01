using ExplorerDirs.App;
using System.Globalization;
using System.IO;

internal static class QdrProviderPathSmoke
{
    internal static void Run()
    {
        var classId = Guid.Parse("b0f47a2f-1690-4888-a0d6-cfe4a4abf92f");
        var physicalPath = @"C:\profile-test\nested folder";
        var identity = $"::{{{classId:D}}}\\edpath:{EncodePath(physicalPath)}";
        Check(QdrShellPathCodec.ToQdrTextPath(identity, classId) == physicalPath,
            "the profile's provider path exports as a physical Q-Dir directory");
        Check(QdrShellPathCodec.ToQdrTextPath(identity, Guid.NewGuid()) == "shell:" + identity,
            "unrecognized Shell namespace paths retain the Q-Dir shell prefix");
        Reject(() => QdrShellPathCodec.ToQdrTextPath($"::{{{classId:D}}}\\edpath:invalid", classId),
            "malformed profile provider paths are rejected");
    }

    private static string EncodePath(string path) => string.Concat(path.Select(character =>
        ((int)character).ToString("X4", CultureInfo.InvariantCulture)));

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("QDR provider path smoke accepted " + message);
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException("QDR provider path smoke: " + message);
    }
}
