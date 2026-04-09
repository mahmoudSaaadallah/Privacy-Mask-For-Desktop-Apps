using System.Windows;
using PrivacyMask.App.Services;

namespace PrivacyMask.App;

public partial class App : System.Windows.Application
{
    private ShellCoordinator? _coordinator;
    private SingleInstanceManager? _singleInstanceManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceManager = new SingleInstanceManager();
        if (!_singleInstanceManager.IsPrimaryInstance)
        {
            _singleInstanceManager.SignalExistingInstance();
            Shutdown();
            return;
        }

        _coordinator = new ShellCoordinator(Current.Dispatcher, e.Args);
        _singleInstanceManager.Listen(() =>
        {
            Current.Dispatcher.BeginInvoke(() => _coordinator?.HandleExternalActivation());
        });
        await _coordinator.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
        }

        _singleInstanceManager?.Dispose();

        base.OnExit(e);
    }
}
