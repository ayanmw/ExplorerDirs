using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ExplorerDirs.App;

internal static class PortableIniWriter
{
    internal static string MutexName(string path)
    {
        var normalized = Path.GetFullPath(path).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return @"Local\ExplorerDirs.Profile." + Convert.ToHexString(hash[..16]);
    }

    public static void WriteIfUnchanged(string path, string? expected, string content, string conflictMessage,
        Encoding? encoding = null, Func<string, string>? readExisting = null)
    {
        var fullPath = Path.GetFullPath(path);
        using var mutex = new Mutex(false, MutexName(fullPath));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(500)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("配置正在由其他实例写入，请稍后重试。");

            var current = File.Exists(fullPath) ? (readExisting ?? File.ReadAllText)(fullPath) : null;
            if (current != expected) throw new IOException(conflictMessage);
            if (current == content) return;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temp = fullPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, content, encoding ?? new UTF8Encoding(false));
                File.Move(temp, fullPath, true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
