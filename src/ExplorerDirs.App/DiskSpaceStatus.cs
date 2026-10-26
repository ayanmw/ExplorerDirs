using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace ExplorerDirs.App;

internal sealed record DiskSpaceInfo(ulong AvailableBytes, ulong TotalBytes)
{
    internal int? AvailablePercent => TotalBytes == 0
        ? null
        : (int)Math.Round((decimal)AvailableBytes * 100 / TotalBytes, MidpointRounding.AwayFromZero);
    internal string DisplayText => AvailablePercent is { } percent
        ? $"{percent}% · {ByteSizeFormatter.Format(AvailableBytes)} 可用"
        : $"{ByteSizeFormatter.Format(AvailableBytes)} 可用";
    internal string Description => $"可用 {ByteSizeFormatter.Format(AvailableBytes)} / 总计 {ByteSizeFormatter.Format(TotalBytes)}\nWindows 向当前用户报告的容量";
}
internal sealed record DiskSpaceSnapshot(string? Path, bool Pending, DiskSpaceInfo? Info);

internal sealed class DiskSpaceReader
{
    internal static DiskSpaceReader Shared { get; } = new();
    private readonly Func<string, DiskSpaceInfo?> _query;
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _timeout;

    internal DiskSpaceReader(Func<string, DiskSpaceInfo?>? query = null, int concurrency = 2,
        TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        _query = query ?? ReadNative;
        _slots = new SemaphoreSlim(concurrency, concurrency);
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    internal async Task<DiskSpaceInfo?> ReadAsync(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') ||
            !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return null;
        // Retain the directory, not just its drive root: a mount/junction can
        // refer to a different volume. UNC requests require a trailing slash.
        var directory = path.Replace('/', '\\').TrimEnd('\\') + "\\";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(_timeout);
        try
        {
            await _slots.WaitAsync(deadline.Token).ConfigureAwait(false);
            Task<DiskSpaceInfo?> work;
            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                work = Task.Run(() =>
                {
                    try { return _query(directory); }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                    catch (Win32Exception) { return null; }
                    // Native network I/O cannot be cancelled by cancelling the
                    // await. Keep its slot until the actual call has returned.
                    finally { _slots.Release(); }
                });
            }
            catch { _slots.Release(); throw; }
            return await work.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
    }

    private static DiskSpaceInfo? ReadNative(string path) =>
        GetDiskFreeSpaceExW(path, out var available, out var total, out _) && available <= total
            ? new(available, total) : null;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory,
        out ulong availableToCaller, out ulong totalToCaller, out ulong freeOnVolume);
}

// Access from the owning UI thread; completions resume on that context.
internal sealed class DiskSpaceTracker
{
    private readonly Func<string, CancellationToken, Task<DiskSpaceInfo?>> _read;
    private readonly Func<long> _clock;
    private CancellationTokenSource? _stop;
    private long _generation;
    private long _started;
    private Task _pending = Task.CompletedTask;

    internal DiskSpaceTracker(Func<string, CancellationToken, Task<DiskSpaceInfo?>> read,
        Func<long>? clock = null)
    {
        _read = read;
        _clock = clock ?? (() => Environment.TickCount64);
    }
    internal event Action<DiskSpaceSnapshot>? Changed;
    internal DiskSpaceSnapshot Snapshot { get; private set; } = new(null, false, null);
    internal Task UpdateAsync(string? path)
    {
        if (path is null) { Reset(); return Task.CompletedTask; }
        if (string.Equals(Snapshot.Path, path, StringComparison.OrdinalIgnoreCase) &&
            (!_pending.IsCompleted || _clock() - _started < 10_000))
            return _pending;
        Reset();
        _stop = new CancellationTokenSource();
        _started = _clock();
        Publish(new(path, true, null));
        _pending = ReadAsync(path, _generation, _stop.Token);
        return _pending;
    }

    internal void Reset()
    {
        _generation++;
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
        _pending = Task.CompletedTask;
        Publish(new(null, false, null));
    }

    private async Task ReadAsync(string path, long generation, CancellationToken stop)
    {
        DiskSpaceInfo? result;
        try { result = await _read(path, stop); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch (Exception) { result = null; }
        if (generation == _generation && !stop.IsCancellationRequested)
            Publish(new(path, false, result));
    }

    private void Publish(DiskSpaceSnapshot value)
    {
        if (Snapshot == value) return;
        Snapshot = value;
        Changed?.Invoke(value);
    }
}
