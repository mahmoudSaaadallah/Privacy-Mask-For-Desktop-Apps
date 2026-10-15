using System;
using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App;

public partial class App : System.Windows.Application
{
    private ShellCoordinator? _coordinator;
    private SingleInstanceManager? _singleInstanceManager;
    private readonly ApplicationShutdown _shutdown;

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
                await _shutdown.RunAsync();
                return;
            }

            _coordinator = new ShellCoordinator(Current.Dispatcher, e.Args, () => _shutdown.RunAsync());
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
                await _shutdown.RunAsync();
                return;
            }
            System.Windows.MessageBox.Show(
                $"PrivacyMask could not start.\n\n{exception.Message}",
                "PrivacyMask startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await _shutdown.RunAsync(-1);
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
