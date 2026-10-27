namespace ExplorerDirs.NativeProbe;

// Probe-only store for native Shell view streams. The GUID is the stable tab/session key supplied by the host.
internal sealed class ShellViewStateStore
{
    private const int MaxStreamBytes = 65_536;
    private readonly string _directory;

    internal ShellViewStateStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    internal bool TryLoad(Guid sessionId, out byte[] bytes)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path))
        {
            bytes = [];
            return false;
        }

        // Inspect and read the same open file; atomic replacement may change
        // the path, but cannot redirect this handle to an unchecked payload.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var length = stream.Length;
        if (length > MaxStreamBytes)
            throw new InvalidDataException("Saved native Shell view state exceeds the probe stream limit.");

        bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("Saved native Shell view state changed while it was being read.");
        return true;
    }

    internal void Save(Guid sessionId, byte[] bytes)
    {
        var exists = TryLoad(sessionId, out var expected);
        Save(sessionId, bytes, exists ? expected : null);
    }

    internal void Save(Guid sessionId, byte[] bytes, byte[]? expected)
    {
        if (bytes.Length > MaxStreamBytes)
            throw new InvalidDataException("Native Shell view state exceeds the probe stream limit.");

        var path = GetPath(sessionId);
        using var mutex = new Mutex(false, ExplorerDirs.App.PortableIniWriter.MutexName(path));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(500)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Native view state is being saved by another instance.");
            var exists = TryLoad(sessionId, out var current);
            if (exists != (expected is not null) || exists && !current.AsSpan().SequenceEqual(expected))
                throw new IOException("Native view state changed after this host loaded it; the newer file was preserved.");
            WriteAtomically(path, bytes);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private void WriteAtomically(string path, byte[] bytes)
    {
        Directory.CreateDirectory(_directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetPath(Guid sessionId) => Path.Combine(_directory, sessionId.ToString("N") + ".viewstate");
}
