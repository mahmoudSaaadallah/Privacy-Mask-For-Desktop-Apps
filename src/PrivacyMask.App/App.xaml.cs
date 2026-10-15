using System;
using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App;

public partial class App : System.Windows.Application
{
    private ShellCoordinator? _coordinator;
    private SingleInstanceManager? _singleInstanceManager;
    private readonly ApplicationShutdown _shutdown;
    private bool _shutdownFailureReported;

    public App()
    {
        _shutdown = new ApplicationShutdown(
            () => _coordinator?.DisposeAsync().AsTask() ?? System.Threading.Tasks.Task.CompletedTask,
            () => { _singleInstanceManager?.Dispose(); _singleInstanceManager = null; },
            code => Shutdown(code));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceManager = new SingleInstanceManager();
            if (!_singleInstanceManager.IsPrimaryInstance)
            {
                _singleInstanceManager.SignalExistingInstance();
                await RequestShutdownAsync();
                return;
            }

            _coordinator = new ShellCoordinator(Current.Dispatcher, e.Args, () => RequestShutdownAsync());
            _singleInstanceManager.Listen(() =>
            {
                Current.Dispatcher.BeginInvoke(() => _coordinator?.HandleExternalActivation());
            });
            await _coordinator.StartAsync();
        }
        catch (Exception exception)
        {
            if (_shutdown.IsRequested)
            {
                await RequestShutdownAsync();
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
