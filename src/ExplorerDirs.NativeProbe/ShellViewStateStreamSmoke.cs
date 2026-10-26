using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics;
using System.Reflection;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewStateStreamSmoke
{
    internal static int Run()
    {
        var initial = new byte[] { 1, 2, 3, 4 };
        var initialSnapshot = (byte[])initial.Clone();
        var saved = new byte[] { 9, 8, 7, 6, 5 };
        using var host = new StreamViewHost(0, initial);
        using var sibling = new StreamViewHost(0, initial);

        initial[0] = 0xFF;
        Check(ReadBytes(host, initialSnapshot.Length).SequenceEqual(initialSnapshot),
            "a host snapshots its initial imported stream");
        Check(ReadBytes(sibling, initialSnapshot.Length).SequenceEqual(initialSnapshot),
            "separate hosts keep independent initial state snapshots");

        Check(host.GetViewStateStream(1, out var writePointer) == 0 && writePointer != 0,
            "write mode returns a stream");
        try
        {
            var writeStream = (IStream)Marshal.GetObjectForIUnknown(writePointer);
            try
            {
                var written = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    writeStream.Write(saved, saved.Length, written);
                    Check(Marshal.ReadInt32(written) == saved.Length, "Shell-written bytes reach the host stream");
                }
                finally { Marshal.FreeCoTaskMem(written); }
            }
            finally { Marshal.ReleaseComObject(writeStream); }
        }
        finally { Marshal.Release(writePointer); }

        Check(host.GetViewStateStream(0, out var readPointer) == 0 && readPointer != 0,
            "read mode returns a stream");
        try
        {
            var readStream = (IStream)Marshal.GetObjectForIUnknown(readPointer);
            try
            {
                var actual = new byte[saved.Length];
                var read = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    readStream.Read(actual, actual.Length, read);
                    Check(Marshal.ReadInt32(read) == actual.Length && actual.SequenceEqual(saved),
                        "a later Shell read receives the previously saved host view state");
                }
                finally { Marshal.FreeCoTaskMem(read); }
            }
            finally { Marshal.ReleaseComObject(readStream); }
        }
        finally { Marshal.Release(readPointer); }

        Check(ReadBytes(sibling, initialSnapshot.Length).SequenceEqual(initialSnapshot),
            "writing one host does not replace another host's initial stream");

        VerifyPersistedSessions();
        VerifyCrossProcessPersistence();

        Console.WriteLine("Shell view state stream smoke: PASS");
        return 0;
    }

    internal static int RunChildCommand(string[] args)
    {
        if (!Guid.TryParseExact(args[2], "N", out var sessionId))
            throw new ArgumentException("Child session ID must be a compact GUID.", nameof(args));
        var bytes = Convert.FromHexString(args[3]);
        var store = new ShellViewStateStore(args[1]);
        using var host = new StreamViewHost(0, [], store, sessionId);
        if (args[0] == "--shell-view-state-store-child-write")
        {
            WriteBytes(host, bytes);
            Console.WriteLine("Child view-state stream saved.");
            return 0;
        }

        var actual = ReadBytes(host, bytes.Length);
        Check(actual.SequenceEqual(bytes), "a separate process reads the exact persisted view-state stream");
        return 0;
    }

    private static void VerifyPersistedSessions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var store = new ShellViewStateStore(directory);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstState = new byte[] { 11, 12, 13 };
        var secondState = new byte[] { 21, 22, 23, 24 };
        var fallback = new byte[] { 31, 32 };
        try
        {
            using (var first = new StreamViewHost(0, fallback, store, firstId))
                WriteBytes(first, firstState);
            using (var second = new StreamViewHost(0, fallback, store, secondId))
                WriteBytes(second, secondState);

            var reopenedStore = new ShellViewStateStore(directory);
            var oversizedRejected = false;
            try { reopenedStore.Save(firstId, new byte[65_537]); }
            catch (InvalidDataException) { oversizedRejected = true; }
            Check(oversizedRejected && reopenedStore.TryLoad(firstId, out var preserved) &&
                  preserved.SequenceEqual(firstState),
                "an oversized replacement is rejected without replacing the existing session state");

            using (var restoredFirst = new StreamViewHost(0, fallback, reopenedStore, firstId))
                Check(ReadBytes(restoredFirst, firstState.Length).SequenceEqual(firstState),
                    "a recreated store and host read the persisted session state");
            using (var restoredSecond = new StreamViewHost(0, fallback, reopenedStore, secondId))
                Check(ReadBytes(restoredSecond, secondState.Length).SequenceEqual(secondState),
                    "another session reads its independent persisted state");
            using (var missing = new StreamViewHost(0, fallback, reopenedStore, Guid.NewGuid()))
                Check(ReadBytes(missing, fallback.Length).SequenceEqual(fallback),
                    "a session without saved state uses its initial fallback");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyCrossProcessPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var sessionId = Guid.NewGuid();
        var firstGeneration = new byte[] { 41, 42, 43, 44, 45 };
        var latestGeneration = new byte[] { 51, 52, 53, 54, 55, 56 };
        try
        {
            var write = RunChild("--shell-view-state-store-child-write", directory,
                sessionId.ToString("N"), Convert.ToHexString(firstGeneration));
            Check(write.ExitCode == 0, $"first writer process saved the session stream: {write.Output}");

            var update = RunChild("--shell-view-state-store-child-write", directory,
                sessionId.ToString("N"), Convert.ToHexString(latestGeneration));
            Check(update.ExitCode == 0, $"later writer process updated the session stream: {update.Output}");

            var read = RunChild("--shell-view-state-store-child-read", directory,
                sessionId.ToString("N"), Convert.ToHexString(latestGeneration));
            Check(read.ExitCode == 0, $"a new process restored the latest session generation: {read.Output}");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static (int ExitCode, string Output) RunChild(params string[] arguments)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Probe assembly path is unavailable."));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated NativeProbe child process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("NativeProbe view-state child process exceeded 15 seconds.");
        }
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
    }

    private static void WriteBytes(StreamViewHost host, byte[] bytes)
    {
        Check(host.GetViewStateStream(1, out var pointer) == 0 && pointer != 0,
            "write mode returns a stream");
        try
        {
            var stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                var written = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    stream.Write(bytes, bytes.Length, written);
                    Check(Marshal.ReadInt32(written) == bytes.Length, "Shell-written bytes reach the host stream");
                }
                finally { Marshal.FreeCoTaskMem(written); }
            }
            finally { Marshal.ReleaseComObject(stream); }
        }
        finally { Marshal.Release(pointer); }
    }

    private static byte[] ReadBytes(StreamViewHost host, int length)
    {
        Check(host.GetViewStateStream(0, out var pointer) == 0 && pointer != 0,
            "read mode returns a stream");
        try
        {
            var stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                var bytes = new byte[length];
                var read = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    stream.Read(bytes, bytes.Length, read);
                    Check(Marshal.ReadInt32(read) == bytes.Length, "initial view-state bytes are complete");
                    return bytes;
                }
                finally { Marshal.FreeCoTaskMem(read); }
            }
            finally { Marshal.ReleaseComObject(stream); }
        }
        finally { Marshal.Release(pointer); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
