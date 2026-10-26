using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ExplorerDirs.App;

internal enum CrashEventKind
{
    DispatcherUnhandled,
    AppDomainUnhandled,
    UnobservedTask
}

internal sealed class CrashLog
{
    private static readonly TimeSpan TemporaryRetention = TimeSpan.FromDays(1);
    private readonly string _directory;
    private readonly Action<string, string> _writeTemporaryText;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<Exception, object> _written = new();

    public CrashLog(string directory, Action<string, string>? writeTemporaryText = null)
    {
        _directory = directory;
        _writeTemporaryText = writeTemporaryText ?? ((path, contents) =>
            File.WriteAllText(path, contents, new UTF8Encoding(false)));
        RemoveStaleTemporaryReports();
    }

    private void RemoveStaleTemporaryReports()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TemporaryRetention;
            foreach (var path in Directory.EnumerateFiles(_directory, "managed-*.json.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // Old temporary diagnostics are optional; never block startup on cleanup.
        }
    }

    public string? TryWrite(CrashEventKind kind, Exception? exception)
    {
        // Exception messages, data, source paths, environment values and file names
        // are deliberately excluded. Shell extensions can run in this process.
        string? temporaryPath = null;
        try
        {
            lock (_gate)
            {
                if (exception is not null && _written.TryGetValue(exception, out _)) return null;
                var report = new CrashReport(
                    DateTimeOffset.UtcNow,
                    kind.ToString(),
                    Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                    Environment.Version.ToString(),
                    Environment.OSVersion.Version.ToString(),
                    Environment.Is64BitProcess,
                    CollectExceptions(exception));
                Directory.CreateDirectory(_directory);
                var filename = $"managed-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json";
                var path = Path.Combine(_directory, filename);
                temporaryPath = Path.Combine(_directory, filename + ".tmp");
                _writeTemporaryText(temporaryPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                File.Move(temporaryPath, path);
                if (exception is not null) _written.Add(exception, new object());
                return path;
            }
        }
        catch (Exception)
        {
            // A failure in the diagnostic path must not mask the original crash.
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception) { }
            }
            return null;
        }
    }

    private static IReadOnlyList<CrashException> CollectExceptions(Exception? exception)
    {
        var result = new List<CrashException>();
        if (exception is null) return result;
        var pending = new Queue<Exception>();
        pending.Enqueue(exception);
        while (pending.Count > 0 && result.Count < 4)
        {
            var current = pending.Dequeue();
            result.Add(new CrashException(SafeIdentifier(current.GetType().FullName),
                $"0x{current.HResult:X8}", GetFrames(current)));
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) pending.Enqueue(inner);
            }
            else if (current.InnerException is { } inner)
                pending.Enqueue(inner);
        }
        return result;
    }

    private static IReadOnlyList<string> GetFrames(Exception exception)
    {
        try
        {
            return new StackTrace(exception, false).GetFrames()?.Take(32).Select(frame =>
            {
                var method = frame.GetMethod();
                return $"{SafeIdentifier(method?.DeclaringType?.FullName)}.{SafeIdentifier(method?.Name)}";
            }).ToArray() ?? [];
        }
        catch (Exception) { return []; }
    }

    private static string SafeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        if (value.IndexOfAny(['\\', '/', ':', '\r', '\n']) >= 0) return "redacted";
        return value.Length > 160 ? value[..160] : value;
    }

    private sealed record CrashReport(DateTimeOffset UtcTime, string Event,
        string AppVersion, string RuntimeVersion, string OsVersion, bool Is64BitProcess,
        IReadOnlyList<CrashException> Exceptions);

    private sealed record CrashException(string Type, string HResult, IReadOnlyList<string> Frames);
}
