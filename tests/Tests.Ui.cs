using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

internal static partial class Tests
{
    private static void RunOnSta(Action test, string failureMessage)
    {
        Exception failure = null;
        var thread = new Thread(() => {
            try {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                test();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception(failureMessage, failure);
    }

    private static void WaitForDispatcherTask(Task task)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (sender, args) => { if (task.IsCompleted || elapsed.ElapsedMilliseconds >= 5000) frame.Continue = false; };
        timer.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("UI operation did not finish while pumping the dispatcher.");
        task.GetAwaiter().GetResult();
    }
}
