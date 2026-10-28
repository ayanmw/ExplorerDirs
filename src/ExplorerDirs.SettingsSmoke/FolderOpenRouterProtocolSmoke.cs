using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using ExplorerDirs.App;

namespace ExplorerDirs.SettingsSmoke;

internal static class FolderOpenRouterProtocolSmoke
{
    private const int RequestIdHandshake = -3;
    private const byte RequestIdHandshakeAccepted = 2;

    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ExplorerDirs.FolderRouterProtocol.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            VerifyHandshakeDoesNotDispatch(Path.Combine(root, "handshake", "ExplorerDirs.ini"));
            VerifyRetryIdempotency(Path.Combine(root, "retry", "ExplorerDirs.ini"),
                Path.Combine(root, "same-folder"));
            VerifyQdrAndActivationIdempotency(root);
            VerifyLegacySender(Path.Combine(root, "legacy", "ExplorerDirs.ini"),
                Path.Combine(root, "legacy-folder"));
            VerifyLegacyPeerFallback(Path.Combine(root, "legacy-peer", "ExplorerDirs.ini"),
                Path.Combine(root, "fallback-folder"));
            Console.WriteLine("folder-open-router-protocol-smoke: idempotent-retry=PASS; legacy-compatibility=PASS; incomplete-body=PASS");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyHandshakeDoesNotDispatch(string profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        var callbackCount = 0;
        FolderOpenRouter? router = null;
        try
        {
            router = FolderOpenRouter.TryStart(profile, _ =>
            {
                Interlocked.Increment(ref callbackCount);
                return Task.FromResult(true);
            }) ?? throw new InvalidOperationException("The request-id protocol router did not start.");
            using var client = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(profile),
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            var marker = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(marker, RequestIdHandshake);
            client.Write(marker);
            client.Flush();
            var response = new byte[1];
            client.ReadExactly(response);
            if (response[0] != RequestIdHandshakeAccepted)
                throw new InvalidOperationException($"Expected request-id handshake marker {RequestIdHandshakeAccepted}, received {response[0]}.");
            if (callbackCount != 0)
                throw new InvalidOperationException("A request must not dispatch before its id and payload arrive.");
        }
        finally
        {
            StopRouterAndWaitForLockRelease(profile, router);
        }
    }

    private static void VerifyRetryIdempotency(string profile, string samePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        var callbackCount = 0;
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FolderOpenRouter? router = null;
        try
        {
            router = FolderOpenRouter.TryStart(profile, async _ =>
            {
                Interlocked.Increment(ref callbackCount);
                await allowFirstCallback.Task;
                callbackFinished.TrySetResult();
                return true;
            }) ?? throw new InvalidOperationException("The retry idempotency router did not start.");
            var requestId = Guid.NewGuid();
            var first = FolderOpenRouter.TryForwardAsync(profile, samePath, TimeSpan.FromMilliseconds(500), requestId)
                .GetAwaiter().GetResult();
            if (first != FolderForwardResult.Uncertain)
                throw new InvalidOperationException($"Expected an uncertain first attempt while callback is pending, received {first}.");
            allowFirstCallback.SetResult(true);
            if (!callbackFinished.Task.Wait(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("The first retry callback did not finish.");
            var retry = FolderOpenRouter.TryForwardAsync(profile, samePath, TimeSpan.FromSeconds(2), requestId)
                .GetAwaiter().GetResult();
            if (retry != FolderForwardResult.Accepted || callbackCount != 1)
                throw new InvalidOperationException("A retry with the same request id was dispatched more than once.");
            var independent = FolderOpenRouter.TryForwardAsync(profile, samePath, TimeSpan.FromSeconds(2), Guid.NewGuid())
                .GetAwaiter().GetResult();
            if (independent != FolderForwardResult.Accepted || callbackCount != 2)
                throw new InvalidOperationException("A distinct launch of the same path was incorrectly deduplicated.");
        }
        finally
        {
            allowFirstCallback.TrySetResult(false);
            StopRouterAndWaitForLockRelease(profile, router);
        }
    }

    private static void VerifyLegacySender(string profile, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        var callbackCount = 0;
        FolderOpenRouter? router = null;
        try
        {
            router = FolderOpenRouter.TryStart(profile, _ =>
            {
                Interlocked.Increment(ref callbackCount);
                return Task.FromResult(true);
            }) ?? throw new InvalidOperationException("The legacy compatibility router did not start.");
            using var client = new NamedPipeClientStream(".", FolderOpenRouter.PipeName(profile),
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            var payload = Encoding.UTF8.GetBytes(Path.GetFullPath(path));
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            client.Write(header);
            client.Write(payload);
            client.Flush();
            var response = new byte[1];
            client.ReadExactly(response);
            if (response[0] != 1 || callbackCount != 1)
                throw new InvalidOperationException("A legacy sender did not reach the request-id receiver.");
        }
        finally
        {
            StopRouterAndWaitForLockRelease(profile, router);
        }
    }

    private static void VerifyQdrAndActivationIdempotency(string root)
    {
        var qdrProfile = Path.Combine(root, "qdr", "ExplorerDirs.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(qdrProfile)!);
        var qdrCount = 0;
        FolderOpenRouter? qdrRouter = null;
        try
        {
            qdrRouter = FolderOpenRouter.TryStart(qdrProfile, _ => Task.FromResult(false),
                openQdr: _ =>
                {
                    Interlocked.Increment(ref qdrCount);
                    return Task.FromResult(true);
                }) ?? throw new InvalidOperationException("The Q-Dir request router did not start.");
            var qdrId = Guid.NewGuid();
            var qdrPath = Path.Combine(root, "workspace.qdr");
            if (FolderOpenRouter.TryForwardQdrAsync(qdrProfile, qdrPath, TimeSpan.FromSeconds(2), qdrId)
                    .GetAwaiter().GetResult() != FolderForwardResult.Accepted ||
                FolderOpenRouter.TryForwardQdrAsync(qdrProfile, qdrPath, TimeSpan.FromSeconds(2), qdrId)
                    .GetAwaiter().GetResult() != FolderForwardResult.Accepted || qdrCount != 1)
                throw new InvalidOperationException("A Q-Dir request retry was not idempotent.");
        }
        finally
        {
            StopRouterAndWaitForLockRelease(qdrProfile, qdrRouter);
        }

        var activationProfile = Path.Combine(root, "activation", "ExplorerDirs.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(activationProfile)!);
        var activationCount = 0;
        FolderOpenRouter? activationRouter = null;
        try
        {
            activationRouter = FolderOpenRouter.TryStart(activationProfile, _ => Task.FromResult(false), () =>
            {
                Interlocked.Increment(ref activationCount);
                return Task.FromResult(true);
            }) ?? throw new InvalidOperationException("The activation request router did not start.");
            var activationId = Guid.NewGuid();
            if (FolderOpenRouter.TryActivateAsync(activationProfile, TimeSpan.FromSeconds(2), activationId)
                    .GetAwaiter().GetResult() != FolderForwardResult.Accepted ||
                FolderOpenRouter.TryActivateAsync(activationProfile, TimeSpan.FromSeconds(2), activationId)
                    .GetAwaiter().GetResult() != FolderForwardResult.Accepted || activationCount != 1)
                throw new InvalidOperationException("An activation request retry was not idempotent.");
        }
        finally
        {
            StopRouterAndWaitForLockRelease(activationProfile, activationRouter);
        }
    }

    private static void VerifyLegacyPeerFallback(string profile, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        using var server = new NamedPipeServerStream(FolderOpenRouter.PipeName(profile),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        var peer = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var header = new byte[sizeof(int)];
            await server.WaitForConnectionAsync(deadline.Token);
            await server.ReadExactlyAsync(header, deadline.Token);
            if (BinaryPrimitives.ReadInt32LittleEndian(header) != RequestIdHandshake)
                throw new InvalidOperationException("A new sender did not begin with the request-id handshake.");
            await server.WriteAsync(new byte[] { 0 }, deadline.Token);
            await server.FlushAsync(deadline.Token);
            server.Disconnect();
            await server.WaitForConnectionAsync(deadline.Token);
            await server.ReadExactlyAsync(header, deadline.Token);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await server.ReadExactlyAsync(payload, deadline.Token);
            if (Encoding.UTF8.GetString(payload) != Path.GetFullPath(path))
                throw new InvalidOperationException("The legacy fallback changed the requested path.");
            await server.WriteAsync(new byte[] { 1 }, deadline.Token);
            await server.FlushAsync(deadline.Token);
        });
        var result = FolderOpenRouter.TryForwardAsync(profile, path, TimeSpan.FromSeconds(3), Guid.NewGuid())
            .GetAwaiter().GetResult();
        peer.GetAwaiter().GetResult();
        if (result != FolderForwardResult.Accepted)
            throw new InvalidOperationException($"Expected an accepted legacy fallback, received {result}.");
    }

    private static void StopRouterAndWaitForLockRelease(string profile, FolderOpenRouter? router)
    {
        router?.Dispose();
        FolderOpenRouter? replacement = null;
        SpinWait.SpinUntil(() =>
        {
            replacement = FolderOpenRouter.TryStart(profile, _ => Task.FromResult(true));
            return replacement is not null;
        }, TimeSpan.FromSeconds(2));
        replacement?.Dispose();
        var lockPath = Path.Combine(Path.GetDirectoryName(profile)!, FolderOpenRouter.PipeName(profile) + ".lock");
        SpinWait.SpinUntil(() =>
        {
            try
            {
                using var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(2));
    }
}
