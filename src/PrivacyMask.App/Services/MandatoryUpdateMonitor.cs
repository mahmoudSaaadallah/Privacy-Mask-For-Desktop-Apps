using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace PrivacyMask.App.Services;

/// <summary>One dispatcher timer plus reconnect hints, not a separate Internet probe.</summary>
internal sealed class MandatoryUpdateMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MandatoryUpdateController _controller;
    private readonly DispatcherTimer _timer;
    private bool _started;
    private bool _disposed;
    private bool _queued;
    private int _networkQueued;
    private bool _availabilitySubscribed;
    private bool _addressSubscribed;
    internal bool IsRunning => _started && !_disposed;

    internal MandatoryUpdateMonitor(Dispatcher dispatcher, MandatoryUpdateController controller)
    {
        _dispatcher = dispatcher;
        _controller = controller;
        _timer = new(DispatcherPriority.Background, dispatcher);
        _timer.Tick += Tick;
    }

    internal void Start()
    {
        if (_started || _disposed || !_controller.Enabled) return;
        _started = true;
        // Missing network notifications must not prevent privacy startup or the
        // timer fallback. HTTPS itself remains the reachability authority.
        try { NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged; _availabilitySubscribed = true; }
        catch (NetworkInformationException) { }
        try { NetworkChange.NetworkAddressChanged += AddressChanged; _addressSubscribed = true; }
        catch (NetworkInformationException) { }
        Pulse();
    }

    internal void Pulse()
    {
        _dispatcher.VerifyAccess();
        if (!_started || _disposed || _queued) return;
        _queued = true;
        _timer.Stop();
        _dispatcher.BeginInvoke(async () =>
        {
            if (_disposed) { _queued = false; return; }
            try { await _controller.PulseAsync(); }
            finally
            {
                _queued = false;
                if (!_disposed) { _timer.Interval = _controller.NextDelay; _timer.Start(); }
            }
        }, DispatcherPriority.Background);
    }

    private void Tick(object? sender, EventArgs args) => Pulse();
    private void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => QueueNetworkHint();
    private void AddressChanged(object? sender, EventArgs args) => QueueNetworkHint();
    private void QueueNetworkHint()
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished || Interlocked.Exchange(ref _networkQueued, 1) != 0) return;
        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _networkQueued, 0);
                if (!_disposed) Pulse();
            }, DispatcherPriority.Background);
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _networkQueued, 0); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Stop();
        _timer.Stop();
        _timer.Tick -= Tick;
        if (_availabilitySubscribed) NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
        if (_addressSubscribed) NetworkChange.NetworkAddressChanged -= AddressChanged;
    }
}
