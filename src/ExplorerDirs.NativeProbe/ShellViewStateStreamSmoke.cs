using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics;
using System.Reflection;

namespace ExplorerDirs.NativeProbe;

internal static class ShellViewStateStreamSmoke
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetViewStateStreamCallback(nint self, uint mode, out nint stream);

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

        VerifyReadWriteMode();
        VerifySnapshotPreservesWriterPosition();
        VerifyClosedHostRejectsStreams();
        VerifyClosedHostRejectsDropRegistration();
        VerifyConcurrentSessionProtection();
        VerifyStoredStateSizeBoundaries();
        VerifyPersistedSessions();
        VerifyCrossProcessPersistence();
        VerifyCrossProcessContention();
        VerifyCrossProcessLockTimeout();

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
        if (args[0] == "--shell-view-state-store-child-race-write")
        {
            WriteBytes(host, bytes);
            File.WriteAllText(Path.Combine(args[1], args[3] + ".ready"), "loaded");
            var release = Path.Combine(args[1], "writers.go");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(release) && wait.Elapsed < TimeSpan.FromSeconds(8)) Thread.Sleep(10);
            if (!File.Exists(release)) throw new TimeoutException("State writer barrier was not released.");
            try { host.Dispose(); Console.WriteLine("race-saved=" + args[3]); return 0; }
            catch (IOException ex) { Console.WriteLine("race-conflict=" + ex.Message); return 3; }
        }
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

    private static void VerifyStoredStateSizeBoundaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new ShellViewStateStore(directory);
        var id = Guid.NewGuid();
        var path = Path.Combine(directory, id.ToString("N") + ".viewstate");
        try
        {
            foreach (var size in new[] { 0, 65_536 })
            {
                var expected = Enumerable.Repeat((byte)0x5A, size).ToArray();
                File.WriteAllBytes(path, expected);
                Check(store.TryLoad(id, out var loaded) && loaded.SequenceEqual(expected),
                    $"stored state reads exactly {size} bytes within the size limit");
            }
            File.WriteAllBytes(path, new byte[65_537]);
            var oversizedRejected = false;
            try { store.TryLoad(id, out _); } catch (InvalidDataException) { oversizedRejected = true; }
            Check(oversizedRejected && new FileInfo(path).Length == 65_537,
                "oversized input is rejected without modifying the saved state");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void VerifyConcurrentSessionProtection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var store = new ShellViewStateStore(directory);
        store.Save(id, [1, 2]);
        try
        {
            using var first = new StreamViewHost(0, [], new ShellViewStateStore(directory), id);
            using var stale = new StreamViewHost(0, [], new ShellViewStateStore(directory), id);
            using var reader = new StreamViewHost(0, [], new ShellViewStateStore(directory), id);
            WriteBytes(first, [3, 4]);
            WriteBytes(stale, [5, 6]);
            first.Dispose();
            Check(ReadBytes(reader, 2).SequenceEqual(new byte[] { 1, 2 }),
                "an opened host keeps its own initial state when another host saves");
            var rejected = false;
            try { stale.Dispose(); } catch (IOException) { rejected = true; }
            Check(rejected && store.TryLoad(id, out var actual) && actual.SequenceEqual(new byte[] { 3, 4 }),
                "a stale host cannot replace a newer persisted session stream on close");
            using (var current = new StreamViewHost(0, [], new ShellViewStateStore(directory), id))
                WriteBytes(current, [7, 8]);
            Check(store.TryLoad(id, out var updated) && updated.SequenceEqual(new byte[] { 7, 8 }),
                "a freshly loaded host can save the next session generation");

            foreach (var initiallyExists in new[] { false, true })
            {
                var changedId = Guid.NewGuid();
                if (initiallyExists) store.Save(changedId, [10]);
                using var changedHost = new StreamViewHost(0, [], new ShellViewStateStore(directory), changedId);
                var changedPath = Path.Combine(directory, changedId.ToString("N") + ".viewstate");
                if (initiallyExists) File.Delete(changedPath);
                else store.Save(changedId, [20]);
                WriteBytes(changedHost, [30]);
                var conflict = false;
                try { changedHost.Dispose(); } catch (IOException) { conflict = true; }
                Check(conflict && (initiallyExists ? !File.Exists(changedPath) :
                        File.ReadAllBytes(changedPath).SequenceEqual(new byte[] { 20 })),
                    $"external {(initiallyExists ? "deletion" : "creation")} is preserved on host close");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void VerifyClosedHostRejectsStreams()
    {
        using var host = new StreamViewHost(0, [1, 2]);
        host.Dispose();
        var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
        try
        {
            var getStream = GetBrowserMethod<GetViewStateStreamCallback>(browser, 12);
            foreach (var mode in new uint[] { 0, 1, 2 })
            {
                var hr = getStream(browser, mode, out var pointer);
                if (pointer != 0) Marshal.Release(pointer);
                Check(hr == unchecked((int)0x80004004) && pointer == 0,
                    $"a disposed COM host rejects mode {mode} without creating a new state stream");
            }
            Check(host.Reads == 0 && host.Writes == 0,
                "retired stream requests do not count as accepted reads or writes");
            host.Dispose();
        }
        finally { Marshal.Release(browser); }
    }

    private static void VerifyClosedHostRejectsDropRegistration()
    {
        using var host = new StreamViewHost(0, []);
        host.Dispose();
        try
        {
            // Zero handles prove retirement is checked before HWND/OLE validation, without creating windows.
            host.RegisterDropTarget(0, 0);
            throw new Exception("A retired host accepted a new drop registration.");
        }
        catch (ObjectDisposedException)
        {
            Check(true, "a retired host rejects drop registration before accessing HWND/OLE resources");
        }
        host.Dispose();
    }

    private static void VerifySnapshotPreservesWriterPosition()
    {
        using var host = new StreamViewHost(0, []);
        Check(host.GetViewStateStream(2, out var pointer) == 0 && pointer != 0,
            "the snapshot-position fixture opens a native read-write memory stream");
        try
        {
            var writer = (IStream)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                writer.Write([1, 2, 3], 3, 0);
                writer.Seek(1, 0, 0);
                Check(host.ReadSavedBytes().SequenceEqual(new byte[] { 1, 2, 3 }),
                    "a snapshot captures the entire stream independently of the writer position");
                writer.Write([9], 1, 0);
                Check(host.ReadSavedBytes().SequenceEqual(new byte[] { 1, 9, 3 }),
                    "reading a snapshot preserves the Shell writer position for its next write");
            }
            finally { Marshal.ReleaseComObject(writer); }
        }
        finally { Marshal.Release(pointer); }
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

    private static void VerifyReadWriteMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var sessionId = Guid.NewGuid();
        var initial = new byte[] { 61, 62, 63, 64 };
        var updated = new byte[] { 71, 72, 73 };
        try
        {
            var store = new ShellViewStateStore(directory);
            using (var host = new StreamViewHost(0, initial, store, sessionId))
            {
                var browser = Marshal.GetComInterfaceForObject(host, typeof(IStreamProbeBrowser));
                try
                {
                    var getViewStateStream = GetBrowserMethod<GetViewStateStreamCallback>(browser, 12);
                    Check(getViewStateStream(browser, 3, out var invalidPointer) == unchecked((int)0x80070057) &&
                      invalidPointer == 0,
                        "the IShellBrowser COM entry rejects an invalid structured-storage access mode");
                    Check(getViewStateStream(browser, 2, out var pointer) == 0 && pointer != 0,
                        "the IShellBrowser COM entry returns a read-write stream");
                    try
                    {
                        var stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
                        try
                        {
                            var existing = new byte[initial.Length];
                            var read = Marshal.AllocCoTaskMem(sizeof(int));
                            try
                            {
                                stream.Read(existing, existing.Length, read);
                                Check(Marshal.ReadInt32(read) == existing.Length && existing.SequenceEqual(initial),
                                    "read-write mode starts with the saved view state");
                            }
                            finally { Marshal.FreeCoTaskMem(read); }
                            stream.Seek(0, 0, 0);
                            var written = Marshal.AllocCoTaskMem(sizeof(int));
                            try
                            {
                                stream.Write(updated, updated.Length, written);
                                Check(Marshal.ReadInt32(written) == updated.Length,
                                    "read-write mode accepts an updated view state");
                            }
                            finally { Marshal.FreeCoTaskMem(written); }
                        }
                        finally { Marshal.ReleaseComObject(stream); }
                    }
                    finally { Marshal.Release(pointer); }
                }
                finally { Marshal.Release(browser); }
            }

            using var restored = new StreamViewHost(0, [], new ShellViewStateStore(directory), sessionId);
            Check(ReadBytes(restored, updated.Length).SequenceEqual(updated),
                "disposing a read-write stream persists its updated session state");
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

    private static void VerifyCrossProcessContention()
    {
        for (var round = 0; round < 3; round++)
        {
            var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
            var id = Guid.NewGuid();
            var store = new ShellViewStateStore(directory);
            store.Save(id, [0]);
            var payloads = new[] { "112233", "445566" };
            var children = payloads.Select(payload => Task.Run(() => RunChild(
                "--shell-view-state-store-child-race-write", directory, id.ToString("N"), payload))).ToArray();
            var release = Path.Combine(directory, "writers.go");
            try
            {
                var wait = Stopwatch.StartNew();
                while (!payloads.All(payload => File.Exists(Path.Combine(directory, payload + ".ready"))) &&
                       wait.Elapsed < TimeSpan.FromSeconds(8) && !children.Any(child => child.IsCompleted))
                    Thread.Sleep(10);
                Check(payloads.All(payload => File.Exists(Path.Combine(directory, payload + ".ready"))),
                    "both independent writers loaded the same baseline before release");
                File.WriteAllText(release, "save");
                var results = Task.WhenAll(children).GetAwaiter().GetResult();
                Check(results.Count(result => result.ExitCode == 0) == 1 &&
                      results.Count(result => result.ExitCode == 3) == 1,
                    $"contention round {round + 1} has exactly one successful process and one rejected writer");
                var winner = Array.FindIndex(results, result => result.ExitCode == 0);
                Check(store.TryLoad(id, out var saved) && Convert.ToHexString(saved) == payloads[winner] &&
                      results[1 - winner].Output.Contains("state changed after this host loaded it", StringComparison.Ordinal),
                    "the persisted bytes belong to the successful writer and the loser reports a conflict");
            }
            finally
            {
                // Release and join both bounded child runners before deleting their fixture.
                File.WriteAllText(release, "cleanup");
                try { Task.WhenAll(children).GetAwaiter().GetResult(); }
                finally { Directory.Delete(directory, recursive: true); }
            }
        }
    }

    private static void VerifyCrossProcessLockTimeout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExplorerDirs.NativeProbe", Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var store = new ShellViewStateStore(directory);
        store.Save(id, [0]);
        var statePath = Path.Combine(directory, id.ToString("N") + ".viewstate");
        using var mutex = new Mutex(false, ExplorerDirs.App.PortableIniWriter.MutexName(statePath));
        var ownsLock = mutex.WaitOne(TimeSpan.FromSeconds(1));
        Check(ownsLock, "the parent holds the same per-path mutex used by the child writer");
        var child = Task.Run(() => RunChild("--shell-view-state-store-child-race-write", directory,
            id.ToString("N"), "99"));
        try
        {
            var ready = Path.Combine(directory, "99.ready");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready) && wait.Elapsed < TimeSpan.FromSeconds(8) && !child.IsCompleted) Thread.Sleep(10);
            Check(File.Exists(ready), "the child loaded state while its save mutex was held");
            File.WriteAllText(Path.Combine(directory, "writers.go"), "save");
            var result = child.GetAwaiter().GetResult();
            Check(result.ExitCode == 3 && result.Output.Contains("being saved by another instance", StringComparison.Ordinal) &&
                  store.TryLoad(id, out var saved) && saved.SequenceEqual(new byte[] { 0 }),
                "a blocked independent writer times out without altering the destination");
            mutex.ReleaseMutex();
            ownsLock = false;
            var retry = RunChild("--shell-view-state-store-child-write", directory, id.ToString("N"), "99");
            Check(retry.ExitCode == 0 && store.TryLoad(id, out saved) && saved.SequenceEqual(new byte[] { 0x99 }),
                "releasing the mutex permits a fresh child writer to save normally");
        }
        finally
        {
            if (ownsLock) mutex.ReleaseMutex();
            File.WriteAllText(Path.Combine(directory, "writers.go"), "cleanup");
            try { child.GetAwaiter().GetResult(); }
            finally { Directory.Delete(directory, recursive: true); }
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

    private static T GetBrowserMethod<T>(nint browser, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(browser);
        var method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
