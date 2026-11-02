using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerDirs.App;

internal static class EverythingSizeSmoke
{
    internal static void Run()
    {
        var reply = BuildReply((@"C:\测试\empty", 0L), (@"C:\测试\unknown", -1L));
        var page = EverythingSizeClient.ParseReply(reply);
        if (page.Status != EverythingSizeStatus.Available || page.Items.Count != 2 ||
            page.Items[0].Bytes != 0 || page.Items[1].Bytes is not null || !page.Items[0].IsDirectory)
            throw new Exception("Everything result parser confused an empty directory with unavailable size.");
        void Invalid(byte[] data)
        {
            if (EverythingSizeClient.ParseReply(data).Status != EverythingSizeStatus.InvalidReply)
                throw new Exception("Malformed Everything result was accepted.");
        }
        Invalid(reply[..^1]);
        var badOffset = reply.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(badOffset.AsSpan(24), uint.MaxValue); Invalid(badOffset);
        var badCount = reply.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(badCount.AsSpan(4), 2049); Invalid(badCount);
        var badFields = reply.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(badFields.AsSpan(12), 4); Invalid(badFields);
        for (var length = 0; length < 20; length++) Invalid(new byte[length]);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            EverythingSizeClient.QueryChildrenAsync(@"C:\", 0, cancelled.Token).GetAwaiter().GetResult();
            throw new Exception("A cancelled Everything query was started.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("Everything size protocol smoke: PASS (Unicode, zero vs unavailable, bounds, malformed replies and pre-cancellation; no window/IPC)");
    }

    internal static void Live(string directory)
    {
        var before = GetForegroundWindow();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var page = EverythingSizeClient.QueryChildrenAsync(directory, 0, CancellationToken.None).GetAwaiter().GetResult();
        if (GetForegroundWindow() != before) throw new Exception("Everything query changed foreground.");
        if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Everything query exceeded bounded wait.");
        Console.WriteLine($"Everything live read-only query: status={page.Status}; rows={page.Items.Count}; total={page.Total}; known-size-rows={page.Items.Count(item => item.Bytes.HasValue)}; elapsed-ms={clock.ElapsedMilliseconds}; foreground-unchanged=True; external-settings-changed=False");
    }

    internal static void LiveFixture()
    {
        var before = GetForegroundWindow();
        var fixture = Path.Combine(AppContext.BaseDirectory, "everything-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "small"));
        Directory.CreateDirectory(Path.Combine(fixture, "large"));
        try
        {
            File.WriteAllBytes(Path.Combine(fixture, "small", "data.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(fixture, "large", "data.bin"), new byte[37]);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            IndexedSizePage page;
            do
            {
                page = EverythingSizeClient.QueryChildrenAsync(fixture, 0, CancellationToken.None).GetAwaiter().GetResult();
                if (GetForegroundWindow() != before) throw new Exception("Everything fixture changed foreground.");
                if (page.Status != EverythingSizeStatus.Available)
                    throw new Exception($"Everything fixture unavailable: {page.Status}.");
                if (page.Items.Count == 2 && page.Items.SingleOrDefault(item => Path.GetFileName(item.Path) == "small")?.Bytes == 5 &&
                    page.Items.SingleOrDefault(item => Path.GetFileName(item.Path) == "large")?.Bytes == 37)
                {
                    Console.WriteLine($"Everything live fixture: PASS; directories=2; sizes=5/37; elapsed-ms={clock.ElapsedMilliseconds}; foreground-unchanged=True");
                    return;
                }
                Thread.Sleep(100);
            } while (clock.Elapsed < TimeSpan.FromSeconds(5));
            throw new Exception($"Everything did not index fixture sizes before deadline: status={page.Status}; rows={page.Items.Count}.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(fixture)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))
                Directory.Delete(fixture, true);
        }
    }

    private static byte[] BuildReply(params (string Path, long Size)[] items)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
        writer.Write((uint)items.Length); writer.Write((uint)items.Length); writer.Write(0u); writer.Write(0x14u); writer.Write(1u);
        for (var index = 0; index < items.Length; index++) { writer.Write(1u); writer.Write(0u); }
        for (var index = 0; index < items.Length; index++)
        {
            var offset = checked((uint)stream.Position);
            writer.Write((uint)items[index].Path.Length); writer.Write(Encoding.Unicode.GetBytes(items[index].Path));
            writer.Write((ushort)0); writer.Write(items[index].Size);
            var end = stream.Position; stream.Position = 24 + index * 8; writer.Write(offset); stream.Position = end;
        }
        return stream.ToArray();
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
