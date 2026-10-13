using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivacyMask.App;
using PrivacyMask.App.Services;
using PrivacyMask.App.ViewModels;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // Do not initialize PrivacyMask.App.App: no singleton, discovery loop,
            // registration, capture, settings IO or real protection is started.
            var application = new TestApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var path in new[] { "Assets/Brand.xaml", "Styles/DesignSystem.xaml", "Styles/LegacyControls.xaml" })
                application.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri($"/PrivacyMask.App;component/{path}", UriKind.Relative)));
            var settings = new DefaultSettingsFactory().Create();
            for (var index = 0; index < 40; index++)
                settings.AppProfiles.Add(CustomAppProfileFactory.Create($"Synthetic editor {index} with a long profile name", $"synthetic{index}"));
            var session = SettingsViewModel.FromModel(settings, RuntimeMode.Standard, "C:\\Synthetic\\settings.v1.json", new HashSet<HotkeyAction>());
            var window = new MainWindow(session, new DesktopApplicationCatalog(new DesktopWindowInspector()));
            var root = (FrameworkElement)window.Content;
            if (args.Contains("--benchmark"))
            {
                session.SelectedPage = DesktopPage.Overview;
                Layout(root, new Size(1280, 800));
                session.SelectedPage = DesktopPage.Applications;
                Layout(root, new Size(1280, 800));
                session.SelectedPage = DesktopPage.Overview;
                Layout(root, new Size(1280, 800));
                Console.WriteLine("GUI benchmark ready: 42 synthetic profiles, both list and overview initialized.");
                var until = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < until)
                {
                    Drain();
                    Thread.Sleep(100);
                }
            }
            else
            {
                var screenshots = args.Length == 0 ? null : args[0];
                if (screenshots is not null) System.IO.Directory.CreateDirectory(screenshots);
                // DIP equivalents for a 1920x1080 display at four scale factors.
                foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
                {
                    var size = new Size(1920 / scale - 32, 1040 / scale - 32);
                    foreach (var page in Enum.GetValues<DesktopPage>())
                    {
                        session.SelectedPage = page;
                        Layout(root, size);
                        CheckFiniteControls(root);
                        if (screenshots is not null) Render(root, size, scale, System.IO.Path.Combine(screenshots, $"{page}-{scale:0.00}.png"));
                    }
                    session.OpenApplication(session.AppProfiles[2]);
                    Layout(root, size);
                    var combos = Find<ComboBox>(root).Where(item => item.IsVisible || item.ActualWidth > 0).ToList();
                    Check(combos.Any(box => AutomationProperties.GetName(box) == "Mask style" && box.Items.Count >= 4), "Style source failed.");
                    session.SelectedApplication!.StartupMode = AppActivationMode.FocusAware;
                    Layout(root, size);
                    var radios = Find<RadioButton>(root).ToList();
                    var focused = radios.Single(radio => AutomationProperties.GetName(radio) == "Mask only while focused");
                    Check(focused.IsChecked == true, "Activation radio failed to reflect the model.");
                    var always = radios.Single(radio => AutomationProperties.GetName(radio) == "Always mask while visible");
                    always.IsChecked = true;
                    Drain();
                    Check(session.SelectedApplication.StartupMode == AppActivationMode.Manual, "Activation radio failed to update the shared edit session.");
                    session.SetSavedBaseline(session.ToModel());
                    var expander = Find<Expander>(root).Single();
                    expander.IsExpanded = true;
                    Layout(root, size);
                    Check(Find<DataGrid>(root).Single().Columns.Count == 10, "Advanced fields lost.");
                    var detailScroller = (ScrollViewer)window.FindName("ApplicationDetailsPage");
                    Check(detailScroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                        && detailScroller.ExtentWidth <= detailScroller.ViewportWidth + 1,
                        "Primary detail form requires horizontal scrolling.");
                    CheckFiniteControls(root);
                    if (screenshots is not null) Render(root, size, scale, System.IO.Path.Combine(screenshots, $"Details-{scale:0.00}.png"));
                    session.ShowApplicationList();
                    Console.WriteLine($"Six pages and profile detail passed at simulated {scale:P0} DIP layout.");
                }
                Check(session.OverviewAppProfiles.Count == 6, "Dashboard is not bounded.");
                session.SelectedPage = DesktopPage.Security;
                Layout(root, new Size(960, 520));
                var globalHelp = Find<RadioButton>(root).Single(radio => AutomationProperties.GetName(radio) == "Explain global panic");
                globalHelp.IsChecked = true;
                Drain();
                Check(session.PanicHelpScope == PanicHelpScope.AllApps && !session.HasPendingChanges, "Panic explanation changed persisted settings.");
                session.SelectedPage = DesktopPage.Settings;
                Layout(root, new Size(960, 520));
                var minimized = Find<CheckBox>(root).Single(box => AutomationProperties.GetName(box) == "Start minimized to tray");
                var previousStartup = session.StartMinimized;
                minimized.IsChecked = !previousStartup;
                Drain();
                Check(session.ToModel().StartMinimized != previousStartup && session.HasPendingChanges, "Startup switch did not edit the shared session.");
                session.SetSavedBaseline(session.ToModel());
                session.ApplicationSearch = "synthetic17";
                Check(session.FilteredAppProfiles.Count == 1 && !session.HasPendingChanges, "Search changed settings.");
                var saved = AppSettingsCloner.Clone(settings);
                window.SaveRequested += updated => { saved = AppSettingsCloner.Clone(updated); return Task.CompletedTask; };
                window.DiscardRequested += () => window.ReplaceViewModel(SettingsViewModel.FromModel(saved, RuntimeMode.Standard, "test", new HashSet<HotkeyAction>()));
                session.OpenApplication(session.AppProfiles[2]);
                session.SelectedApplication!.MaskIntensityPercent = 80;
                Invoke(window, "Save_Click");
                Check(!window.ViewModel.HasPendingChanges && window.ViewModel.IsApplicationDetailsPage, "Save lost profile navigation.");
                window.ViewModel.SelectedApplication!.MaskIntensityPercent = 20;
                Invoke(window, "CloseToTray_Click");
                Check(window.ViewModel.SelectedApplication?.MaskIntensityPercent == 80, "Discard did not restore the selected profile.");
                Check(window.ViewModel.PanicHelpScope == PanicHelpScope.AllApps, "Discard lost the selected explanation tab.");
                var setup = new PrivacyMask.App.Windows.PanicPinSetupWindow(null);
                ((PasswordBox)setup.FindName("NewPinBox")).Password = "12";
                Invoke(setup, "Save_Click");
                Check(setup.CreatedCredential is null && ((TextBlock)setup.FindName("ErrorText")).Text.Length > 0, "Invalid PIN accepted.");
                setup.Close();
                Check(((PasswordBox)setup.FindName("NewPinBox")).Password.Length == 0, "Closed PIN input retained.");
                Console.WriteLine("Search, selected-profile Save/Discard and invalid-PIN/closed-input checks passed.");
            }
            window.AllowClose();
            window.Close();
            application.Shutdown();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Layout(FrameworkElement root, Size size)
    {
        Drain();
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(size);
        root.Arrange(new Rect(new Point(), size));
        root.UpdateLayout();
    }
    private static void CheckFiniteControls(DependencyObject root)
    {
        foreach (var control in Find<Control>(root))
            Check(double.IsFinite(control.ActualWidth) && double.IsFinite(control.ActualHeight), "Invalid control layout.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, [target, new RoutedEventArgs()]);
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Render(FrameworkElement root, Size size, double scale, string path)
    {
        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}

internal sealed class TestApplication : Application
{
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}
