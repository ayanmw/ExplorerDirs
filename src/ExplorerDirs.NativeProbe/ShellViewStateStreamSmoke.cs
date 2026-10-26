using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

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

        Console.WriteLine("Shell view state stream smoke: PASS");
        return 0;
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
