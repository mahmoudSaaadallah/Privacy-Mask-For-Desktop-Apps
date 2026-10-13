using System.Windows;
using System.Windows.Threading;
using PrivacyMask.App;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Shared API surface also available at baseline 5e079c9.
        var application = new BenchmarkApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var path in new[] { "Assets/Brand.xaml", "Styles/DesignSystem.xaml", "Styles/LegacyControls.xaml" })
            application.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri($"/PrivacyMask.App;component/{path}", UriKind.Relative)));
        var settings = new DefaultSettingsFactory().Create();
        for (var index = 0; index < 40; index++)
            settings.AppProfiles.Add(CustomAppProfileFactory.Create($"Synthetic editor {index} with a long profile name", $"synthetic{index}"));
        var model = SettingsViewModel.FromModel(settings, RuntimeMode.Standard, "synthetic", new HashSet<HotkeyAction>());
        var window = new MainWindow(model, new DesktopApplicationCatalog(new DesktopWindowInspector()));
        var root = (FrameworkElement)window.Content;
        foreach (var page in new[] { DesktopPage.Overview, DesktopPage.Applications, DesktopPage.Overview })
        {
            model.SelectedPage = page;
            Drain();
            root.Measure(new Size(1280, 800));
            root.Arrange(new Rect(0, 0, 1280, 800));
            root.UpdateLayout();
        }
        Console.WriteLine("READY: 42 synthetic profiles; no production runtime, visible window or capture started.");
        var end = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < end) { Drain(); Thread.Sleep(100); }
        window.AllowClose();
        window.Close();
        application.Shutdown();
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

internal sealed class BenchmarkApplication : Application
{
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}
