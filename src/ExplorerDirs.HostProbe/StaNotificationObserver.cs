using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

// A second, never-shown notification recipient. This STA performs no file
// operations or view queries, so its delivery can be compared with the host STA.
internal sealed class StaNotificationObserver : IDisposable
{
    private readonly Thread thread;
    private readonly TaskCompletionSource<(Dispatcher Dispatcher, ChangeNotificationObserver Observer)> ready = new();
    private readonly TaskCompletionSource finished = new();

    internal StaNotificationObserver(string root, Stopwatch clock)
    {
        thread = new Thread(() => Run(root, clock)) { IsBackground = true, Name = "Shell notification comparison" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Registration must complete before the producer is allowed to mutate
        // fixtures. No pumping or synchronous call back into the host is needed.
        ready.Task.GetAwaiter().GetResult();
    }

    internal string Summary
    {
        get
        {
            if (finished.Task.IsCompleted)
            {
                finished.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("Independent notification STA stopped before observation ended.");
            }
            return ready.Task.Result.Observer.Summary;
        }
    }

    private void Run(string root, Stopwatch clock)
    {
        try
        {
            using (var source = new HwndSource(new HwndSourceParameters("ExplorerDirs hidden notification comparison")
            {
                WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1
            }))
            using (var observer = new ChangeNotificationObserver(source, root, clock))
            {
                if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("Notification window became visible.");
                ready.SetResult((Dispatcher.CurrentDispatcher, observer));
                Dispatcher.Run();
                if (IsWindowVisible(source.Handle)) throw new InvalidOperationException("Notification window became visible.");
            }
            finished.SetResult();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            finished.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        if (!finished.Task.IsCompleted)
            ready.Task.Result.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Independent notification STA did not terminate.");
        finished.Task.GetAwaiter().GetResult();
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
}
