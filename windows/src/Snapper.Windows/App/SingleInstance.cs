using Windows.Win32;

namespace Snapper.Windows.App;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Snapper-8C1E2B7A-instance";
    private const string EventName = @"Local\Snapper-8C1E2B7A-open-settings";
    private readonly Mutex mutex;
    private readonly EventWaitHandle signal;
    private RegisteredWaitHandle? listener;
    private bool disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        this.mutex = mutex;
        this.signal = signal;
    }

    public static SingleInstance? TryAcquire()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        try
        {
            Mutex mutex;
            bool createdNew;
            try { mutex = new Mutex(true, MutexName, out createdNew); }
            catch (AbandonedMutexException)
            {
                mutex = Mutex.OpenExisting(MutexName);
                // Reopen and acquire the abandoned mutex if construction did not return its handle.
                try { mutex.WaitOne(); }
                catch (AbandonedMutexException) { }
                createdNew = true;
            }
            if (createdNew) return new SingleInstance(mutex, signal);
            mutex.Dispose();
            signal.Dispose();
            return null;
        }
        catch
        {
            signal.Dispose();
            throw;
        }
    }

    public static void SignalPrimary()
    {
        try
        {
            PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            signal.Set();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Snapper: Could not signal running instance: {error.Message}");
        }
    }

    public void Listen(Action onSignal)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        listener?.Unregister(null);
        listener = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onSignal(), null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        listener?.Unregister(null);
        mutex.ReleaseMutex();
        mutex.Dispose();
        signal.Dispose();
    }
}
