using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ExplorerDirs.App;

namespace ExplorerDirs.NativeProbe;

internal sealed record ShellListViewRowScanResult(
    ShellFileSystemIdentity Identity,
    RecursiveDirectoryScanResult Summary);

// Enumerate from the opened object, not by reopening its original path. This keeps a scan result
// attached to the directory that was actually read when that path is renamed or replaced.
internal static class HandleBoundRecursiveDirectoryScanner
{
    private const int DirectoryBufferSize = 64 * 1024;
    private const int FileIdInfoClass = 18;
    private const int FileIdExtdDirectoryInfoClass = 0x13;
    private const int FileIdExtdDirectoryRestartInfoClass = 0x14;
    private const int FileAttributeTagInfoClass = 9;
    private const int ErrorNoMoreFiles = 18;
    private const uint FileListDirectory = 0x0001;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int EntryFixedSize = 88;
    private const int EntryFileIdOffset = 72;
    private const int EntryFileAttributesOffset = 56;
    private const int EntryEndOfFileOffset = 40;
    private const int EntryFileNameLengthOffset = 60;
    private const uint FileNameBufferInitialSize = 512;
    private const uint FileNameBufferMaximumSize = 32768;

    internal static IReadOnlyDictionary<string, ShellListViewRowScanResult> Scan(
        IEnumerable<string> requestedPaths,
        CancellationToken cancellationToken,
        Action<string, ShellFileSystemIdentity>? afterRootOpened = null,
        Action<string, string>? beforeChildOpen = null,
        Action<string, ShellFileSystemIdentity, RecursiveDirectoryScanResult>? afterRootScannedBeforeReturn = null)
    {
        ArgumentNullException.ThrowIfNull(requestedPaths);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in requestedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            paths.Add(Path.GetFullPath(path));
        }

        var results = new Dictionary<string, ShellListViewRowScanResult>(
            paths.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = OpenDirectory(path);
            if (root is null) continue;
            using (root.Handle)
            {
                afterRootOpened?.Invoke(path, root.Identity);
                if ((root.Attributes & FileAttributeReparsePoint) != 0)
                {
                    var skipped = new RecursiveDirectoryScanResult(0, 0, 0, 1);
                    afterRootScannedBeforeReturn?.Invoke(path, root.Identity, skipped);
                    results.Add(path, new(root.Identity, skipped));
                    continue;
                }

                var summary = ScanOpenedDirectory(root.Handle, root.Identity, path, cancellationToken, beforeChildOpen);
                afterRootScannedBeforeReturn?.Invoke(path, root.Identity, summary);
                results.Add(path, new(root.Identity, summary));
            }
        }
        return results;
    }

    internal static IReadOnlyDictionary<string, ShellListViewRowScanResult> ScanDirectoryTree(
        string rootPath,
        IEnumerable<string> requestedPaths,
        CancellationToken cancellationToken,
        Action<string, string>? beforeChildOpen = null)
    {
        ArgumentNullException.ThrowIfNull(requestedPaths);
        cancellationToken.ThrowIfCancellationRequested();
        rootPath = Path.GetFullPath(rootPath);
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in requestedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requested.Add(Path.GetFullPath(path));
        }

        var results = new Dictionary<string, ShellListViewRowScanResult>(requested.Count,
            StringComparer.OrdinalIgnoreCase);
        var root = OpenDirectory(rootPath);
        if (root is null) return results;
        using (root.Handle)
        {
            if ((root.Attributes & FileAttributeReparsePoint) != 0)
            {
                if (requested.Contains(rootPath))
                    results[rootPath] = new(root.Identity, new(0, 0, 0, 1));
                return results;
            }
            ScanOpenedDirectory(root.Handle, root.Identity, rootPath, cancellationToken,
                beforeChildOpen, requested, results);
        }
        return results;
    }

    private static RecursiveDirectoryScanResult ScanOpenedDirectory(
        SafeFileHandle rootHandle, ShellFileSystemIdentity rootIdentity, string originalPath,
        CancellationToken cancellationToken, Action<string, string>? beforeChildOpen,
        HashSet<string>? requestedPaths = null,
        Dictionary<string, ShellListViewRowScanResult>? requestedResults = null)
    {
        var frames = new Stack<DirectoryFrame>();
        RecursiveDirectoryScanResult? completedRoot = null;
        frames.Push(new(rootHandle, rootIdentity, ToExtendedPath(TryGetPath(rootHandle) ?? originalPath)));
        try
        {
            while (frames.TryPeek(out var frame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DirectoryEntry entry;
                try
                {
                    if (!frame.TryReadNext(out entry))
                    {
                        CompleteCurrentDirectory();
                        continue;
                    }
                }
                catch (Win32Exception)
                {
                    frame.Unreadable = checked(frame.Unreadable + 1);
                    CompleteCurrentDirectory();
                    continue;
                }

                if (entry.Name is "." or "..") continue;
                if ((entry.Attributes & FileAttributeDirectory) != 0)
                {
                    var currentDirectoryPath = frame.Path;
                    var childPath = Path.Combine(currentDirectoryPath, entry.Name);
                    var normalizedChildPath = NormalizeHandlePath(childPath);
                    if ((entry.Attributes & FileAttributeReparsePoint) != 0 ||
                        entry.FileIdLow == 0 && entry.FileIdHigh == 0)
                    {
                        frame.Unreadable = checked(frame.Unreadable + 1);
                        if ((entry.Attributes & FileAttributeReparsePoint) != 0 &&
                            requestedPaths?.Contains(normalizedChildPath) == true)
                        {
                            var identity = new ShellFileSystemIdentity(
                                frame.Identity.VolumeSerialNumber, entry.FileIdLow, entry.FileIdHigh);
                            requestedResults![normalizedChildPath] = new(identity, new(0, 0, 0, 1));
                        }
                        continue;
                    }

                    beforeChildOpen?.Invoke(NormalizeHandlePath(currentDirectoryPath), normalizedChildPath);
                    var child = OpenDirectory(childPath);
                    var expectedChildIdentity = new ShellFileSystemIdentity(
                        frame.Identity.VolumeSerialNumber, entry.FileIdLow, entry.FileIdHigh);
                    // Re-resolve the parent path only when its cached path no longer opens the enumerated child.
                    if ((child is null || child.Identity != expectedChildIdentity ||
                         (child.Attributes & FileAttributeReparsePoint) != 0))
                    {
                        var currentParentPath = ToExtendedPath(TryGetPath(frame.Handle) ?? frame.Path);
                        if (!string.Equals(NormalizeHandlePath(currentParentPath),
                                NormalizeHandlePath(frame.Path), StringComparison.OrdinalIgnoreCase))
                        {
                            child?.Handle.Dispose();
                            frame.Path = currentParentPath;
                            currentDirectoryPath = currentParentPath;
                            childPath = Path.Combine(currentDirectoryPath, entry.Name);
                            normalizedChildPath = NormalizeHandlePath(childPath);
                            child = OpenDirectory(childPath);
                        }
                    }

                    if (child is null)
                    {
                        frame.Unreadable = checked(frame.Unreadable + 1);
                        continue;
                    }
                    if (child.Identity != expectedChildIdentity ||
                        (child.Attributes & FileAttributeReparsePoint) != 0)
                    {
                        child.Handle.Dispose();
                        frame.Unreadable = checked(frame.Unreadable + 1);
                        continue;
                    }

                    try
                    {
                        frames.Push(new(child.Handle, child.Identity,
                            ToExtendedPath(TryGetPath(child.Handle) ?? childPath)));
                    }
                    catch
                    {
                        child.Handle.Dispose();
                        throw;
                    }
                    continue;
                }

                if ((entry.Attributes & FileAttributeReparsePoint) != 0 || entry.EndOfFile < 0)
                {
                    frame.Unreadable = checked(frame.Unreadable + 1);
                    continue;
                }
                frame.Bytes = checked(frame.Bytes + (ulong)entry.EndOfFile);
                frame.Files = checked(frame.Files + 1);
            }
        }
        finally
        {
            while (frames.TryPop(out var frame)) frame.Dispose();
        }
        return completedRoot ?? new(0, 0, 0, 1);

        void CompleteCurrentDirectory()
        {
            var completed = frames.Pop();
            var summary = completed.ToSummary();
            var normalizedPath = NormalizeHandlePath(completed.Path);
            if (requestedPaths?.Contains(normalizedPath) == true)
                requestedResults![normalizedPath] = new(completed.Identity, summary);
            completed.Dispose();
            if (frames.TryPeek(out var parent))
            {
                parent.Bytes = checked(parent.Bytes + summary.Bytes);
                parent.Files = checked(parent.Files + summary.Files);
                parent.Directories = checked(parent.Directories + 1 + summary.Directories);
                parent.Unreadable = checked(parent.Unreadable + summary.Unreadable);
            }
            else completedRoot = summary;
        }
    }

    private static string NormalizeHandlePath(string path)
    {
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            return "\\\\" + path[8..];
        if (path.Length >= 6 && path.StartsWith("\\\\?\\", StringComparison.Ordinal) &&
            char.IsAsciiLetter(path[4]) && path[5] == ':')
            return path[4..];
        return path;
    }

    private static string ToExtendedPath(string path)
    {
        // Keep recursive CreateFileW opens valid after a fixture or user path exceeds MAX_PATH.
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal)) return path;
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
            return "\\\\?\\UNC\\" + path.TrimStart('\\');
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar))
            return "\\\\?\\" + path;
        return path;
    }

    private static OpenDirectoryResult? OpenDirectory(string path)
    {
        var handle = CreateFileW(ToExtendedPath(path), FileListDirectory | FileReadAttributes, FileShareAll,
            0, OpenExisting, FileFlagOpenReparsePoint | FileFlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, out FileIdInfo id, (uint)Marshal.SizeOf<FileIdInfo>()) ||
            !GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, out FileAttributeTagInfo attributes,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            handle.Dispose();
            return null;
        }
        if ((attributes.FileAttributes & FileAttributeDirectory) == 0)
        {
            handle.Dispose();
            return null;
        }

        return new(handle,
            new ShellFileSystemIdentity(id.VolumeSerialNumber, id.FileIdLow, id.FileIdHigh),
            attributes.FileAttributes);
    }

    private static string? TryGetPath(SafeFileHandle handle)
    {
        var capacity = FileNameBufferInitialSize;
        while (capacity <= FileNameBufferMaximumSize)
        {
            var buffer = new System.Text.StringBuilder(checked((int)capacity));
            var length = GetFinalPathNameByHandleW(handle, buffer, capacity, 0);
            if (length == 0) return null;
            if (length < capacity) return buffer.ToString();
            capacity = length + 1;
        }
        return null;
    }

    private sealed class DirectoryFrame : IDisposable
    {
        private readonly nint _buffer;
        private readonly int _bufferSize;
        private bool _needsFetch = true;
        private bool _started;
        private int _offset;
        private bool _hasMoreInBuffer;

        internal DirectoryFrame(SafeFileHandle handle, ShellFileSystemIdentity identity, string path)
        {
            Handle = handle;
            Identity = identity;
            Path = path;
            _bufferSize = DirectoryBufferSize;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }

        internal SafeFileHandle Handle { get; }
        internal ShellFileSystemIdentity Identity { get; }
        internal string Path { get; set; }
        internal ulong Bytes;
        internal long Files;
        internal long Directories;
        internal long Unreadable;

        internal bool TryReadNext(out DirectoryEntry entry)
        {
            if (_needsFetch)
            {
                var informationClass = _started
                    ? FileIdExtdDirectoryInfoClass : FileIdExtdDirectoryRestartInfoClass;
                if (!GetFileInformationByHandleEx(Handle, informationClass, _buffer, (uint)_bufferSize))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreFiles)
                    {
                        entry = default;
                        return false;
                    }
                    throw new Win32Exception(error,
                        "Could not enumerate an opened directory handle with FileIdExtdDirectoryInfo.");
                }
                _started = true;
                _offset = 0;
                _needsFetch = false;
                _hasMoreInBuffer = true;
            }

            if (!_hasMoreInBuffer)
            {
                _needsFetch = true;
                return TryReadNext(out entry);
            }

            var nextOffset = unchecked((uint)Marshal.ReadInt32(_buffer, _offset));
            var attributes = unchecked((uint)Marshal.ReadInt32(_buffer, _offset + EntryFileAttributesOffset));
            var fileNameLength = unchecked((uint)Marshal.ReadInt32(_buffer, _offset + EntryFileNameLengthOffset));
            var minimumLength = checked((uint)EntryFixedSize + fileNameLength);
            if ((fileNameLength & 1) != 0 || fileNameLength > _bufferSize - EntryFixedSize ||
                nextOffset != 0 && (nextOffset < minimumLength || nextOffset > _bufferSize - _offset))
                throw new InvalidDataException("The Shell directory handle returned an invalid extended entry.");

            var name = Marshal.PtrToStringUni(_buffer + _offset + EntryFixedSize,
                checked((int)(fileNameLength / sizeof(char)))) ?? string.Empty;
            var endOfFile = Marshal.ReadInt64(_buffer, _offset + EntryEndOfFileOffset);
            var fileIdLow = unchecked((ulong)Marshal.ReadInt64(_buffer, _offset + EntryFileIdOffset));
            var fileIdHigh = unchecked((ulong)Marshal.ReadInt64(_buffer, _offset + EntryFileIdOffset + sizeof(long)));
            entry = new(name, attributes, endOfFile, fileIdLow, fileIdHigh);

            if (nextOffset == 0) _hasMoreInBuffer = false;
            else _offset = checked(_offset + (int)nextOffset);
            return true;
        }

        internal RecursiveDirectoryScanResult ToSummary() =>
            new(Bytes, Files, Directories, Unreadable);

        public void Dispose()
        {
            Marshal.FreeHGlobal(_buffer);
            Handle.Dispose();
        }
    }

    private sealed record OpenDirectoryResult(SafeFileHandle Handle, ShellFileSystemIdentity Identity, uint Attributes);
    private readonly record struct DirectoryEntry(string Name, uint Attributes, long EndOfFile,
        ulong FileIdLow, ulong FileIdHigh);

    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileIdInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileAttributeTagInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        nint fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, System.Text.StringBuilder path,
        uint pathLength, uint flags);
}
