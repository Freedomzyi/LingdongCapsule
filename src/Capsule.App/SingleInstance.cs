using System.Threading;

namespace Capsule.App;

// One session instance; the event carries no arguments, paths or executable commands.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle? activation;
    private RegisteredWaitHandle? registration;
    private int disposed;
    public bool IsPrimary { get; }

    public SingleInstance(string name, Action activated)
    {
        mutex = new Mutex(false, name, out var first);
        IsPrimary = first;
        if (!first) return;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
        registration = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            if (Volatile.Read(ref disposed) == 0) activated();
        }, null, Timeout.Infinite, false);
    }

    public static bool Notify(string name)
    {
        // The primary may still be creating its event immediately after its mutex.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (EventWaitHandle.TryOpenExisting(name + ".Activate", out var handle))
            { using (handle) return handle.Set(); }
            if (attempt < 5) Thread.Sleep(20);
        }
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        registration?.Unregister(null);
        registration = null;
        activation?.Dispose();
        mutex.Dispose();
    }
}
