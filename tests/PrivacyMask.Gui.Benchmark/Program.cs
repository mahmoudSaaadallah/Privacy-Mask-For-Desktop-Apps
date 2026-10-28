using System.Windows;
using System.Windows.Threading;
using PrivacyMask.App;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Services;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var scenario = "Overview";
        var duration = 45;
        var controlStdin = false;
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) return 2;
            if (args[index] == "--scenario" && args[index + 1] is "Overview" or "Applications" or "Settings") scenario = args[index + 1];
            else if (args[index] == "--duration-seconds" && int.TryParse(args[index + 1], out var seconds) && seconds is >= 2 and <= 3600) duration = seconds;
            else if (args[index] == "--control-stdin" && bool.TryParse(args[index + 1], out var control)) controlStdin = control;
            else return 2;
        }
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
        foreach (var page in new[] { DesktopPage.Overview, DesktopPage.Applications, Enum.Parse<DesktopPage>(scenario) })
        {
            model.SelectedPage = page;
            Drain();
            root.Measure(new Size(1280, 800));
            root.Arrange(new Rect(0, 0, 1280, 800));
            root.UpdateLayout();
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            State = "READY", EvidenceKind = "SyntheticHiddenGui", Scenario = scenario, ProfileCount = settings.AppProfiles.Count,
            ProductVersion = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            Distribution = "FrameworkDependent", WidthDip = 1280, HeightDip = 800,
            ProductionRuntimeStarted = false, CaptureStarted = false,
        }));
        // Console's synchronized reader may implement ReadLineAsync synchronously.
        // Never let the control pipe block the WPF dispatcher being measured.
        Task stop = controlStdin ? Task.Run(Console.ReadLine) : Task.Delay(Timeout.Infinite);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < duration && !stop.IsCompleted) { Drain(); Thread.Sleep(100); }
        window.AllowClose();
        window.Close();
        application.Shutdown();
        return 0;
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
