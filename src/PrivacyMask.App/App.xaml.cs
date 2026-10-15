using System;
using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App;

public partial class App : System.Windows.Application
{
    private ShellCoordinator? _coordinator;
    private AuthenticatedUpdateStartup? _updateStartup;
    private SingleInstanceManager? _singleInstanceManager;
    private readonly ApplicationShutdown _shutdown;
    private bool _shutdownFailureReported;

    public App()
    {
        _shutdown = new ApplicationShutdown(
            DrainAndDisposeAsync,
            () => { _singleInstanceManager?.Dispose(); _singleInstanceManager = null; },
            code => Shutdown(code));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var updateRequested = AuthenticatedUpdateStartup.IsRequested(e.Args);

        try
        {
            using var updateDeadline = updateRequested
                ? new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(55))
                : null;
            if (updateRequested)
            {
                _updateStartup = await AuthenticatedUpdateStartup.OpenAsync(e.Args, updateDeadline!.Token);
            }

            _singleInstanceManager = new SingleInstanceManager();
            if (!_singleInstanceManager.IsPrimaryInstance)
            {
                if (!updateRequested) _singleInstanceManager.SignalExistingInstance();
                await RequestShutdownAsync(updateRequested ? -1 : 0);
                return;
            }

            _coordinator = _updateStartup is null
                ? new ShellCoordinator(Current.Dispatcher, e.Args, () => RequestShutdownAsync())
                : new ShellCoordinator(Current.Dispatcher, [], () => RequestShutdownAsync(), _updateStartup.ShellContext);
            _singleInstanceManager.Listen(() =>
            {
                Current.Dispatcher.BeginInvoke(() => _coordinator?.HandleExternalActivation());
            });
            await AppStartupSequence.RunAsync(
                _coordinator.StartAsync,
                _updateStartup is null ? null : ConfirmUpdateAsync,
                _updateStartup is null ? null : _coordinator.CompleteInteractiveStartupAsync);

            async System.Threading.Tasks.Task ConfirmUpdateAsync()
            {
                await _updateStartup!.SignalReadyAndConfirmCommitAsync(updateDeadline!.Token);
                _updateStartup.Dispose();
                _updateStartup = null;
            }
        }
        catch (Exception exception)
        {
            if (_shutdown.IsRequested)
            {
                await RequestShutdownAsync();
                return;
            }
            if (updateRequested)
            {
                await RequestShutdownAsync(-1);
                return;
            }
            System.Windows.MessageBox.Show(
                $"PrivacyMask could not start.\n\n{exception.Message}",
                "PrivacyMask startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await RequestShutdownAsync(-1);
        }
    }

    private async System.Threading.Tasks.Task DrainAndDisposeAsync()
    {
        if (_coordinator is not null) await _coordinator.DisposeAsync();
        _updateStartup?.Dispose();
        _updateStartup = null;
    }

    private async System.Threading.Tasks.Task RequestShutdownAsync(int exitCode = 0)
    {
        try { await _shutdown.RunAsync(exitCode); }
        catch (Exception)
        {
            if (_shutdownFailureReported) return;
            _shutdownFailureReported = true;
            System.Windows.MessageBox.Show(
                "PrivacyMask could not finish closing safely. Close sensitive application windows before ending its process or signing out.",
                "PrivacyMask could not close", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _coordinator?.DisposeOnExit();
        }
        finally
        {
            _singleInstanceManager?.Dispose();
            _singleInstanceManager = null;
            base.OnExit(e);
        }
    }
}
