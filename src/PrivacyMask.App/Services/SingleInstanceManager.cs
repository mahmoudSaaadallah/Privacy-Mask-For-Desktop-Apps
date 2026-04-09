using System;
using System.Threading;

namespace PrivacyMask.App.Services;

public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = @"Local\PrivacyMask.Desktop.Singleton";
    private const string ActivationEventName = @"Local\PrivacyMask.Desktop.ShowSettings";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _waitRegistration;

    public SingleInstanceManager()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _activationEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivationEventName);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void SignalExistingInstance()
    {
        _activationEvent.Set();
    }

    public void Listen(Action onActivation)
    {
        _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, _) => ((Action)state!).Invoke(),
            onActivation,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _waitRegistration?.Unregister(null);
        _activationEvent.Dispose();

        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
