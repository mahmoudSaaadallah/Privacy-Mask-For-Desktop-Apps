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
using PrivacyMask.App.Controls;
using PrivacyMask.App.Windows;
using PrivacyMask.Windows.Models;
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
            if (args.Length > 0 && !args.Contains("--benchmark"))
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
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
                    session.ShowAdvancedZones();
                    Layout(root, size);
                    Check(Find<DataGrid>(root).Single().Columns.Count == 10, "Advanced fields lost.");
                    Check(session.IsAdvancedZonesPage && !session.HasPendingChanges, "Zone navigation edited settings.");
                    if (screenshots is not null) Render(root, size, scale, System.IO.Path.Combine(screenshots, $"AdvancedZones-{scale:0.00}.png"));
                    session.ShowPrimaryApplicationDetails();
                    Layout(root, size);
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
                session.ShowAdvancedZones();
                session.SelectedApplication!.Zones[0].Width = .8;
                session.SelectedApplication!.MaskIntensityPercent = 80;
                Invoke(window, "Save_Click");
                Check(!window.ViewModel.HasPendingChanges && window.ViewModel.IsApplicationDetailsPage, "Save lost profile navigation.");
                Check(window.ViewModel.IsAdvancedZonesPage, "Save lost advanced navigation.");
                window.ViewModel.SelectedApplication!.MaskIntensityPercent = 20;
                window.ViewModel.SelectedApplication.Zones[0].Width = .2;
                Invoke(window, "CloseToTray_Click");
                Check(window.ViewModel.SelectedApplication?.MaskIntensityPercent == 80, "Discard did not restore the selected profile.");
                Check(window.ViewModel.IsAdvancedZonesPage && window.ViewModel.SelectedApplication!.Zones[0].Width == .8,
                    "Discard lost the advanced editor or failed to restore zone values.");
                Check(window.ViewModel.PanicHelpScope == PanicHelpScope.AllApps, "Discard lost the selected explanation tab.");
                var setup = new PrivacyMask.App.Windows.PanicPinSetupWindow(null);
                ((PinEntry)setup.FindName("NewPinBox")).Password = "12";
                Invoke(setup, "Save_Click");
                Check(setup.CreatedCredential is null && ((TextBlock)setup.FindName("ErrorText")).Text.Length > 0, "Invalid PIN accepted.");
                setup.Close();
                Check(((PinEntry)setup.FindName("NewPinBox")).Password.Length == 0, "Closed PIN input retained.");
                CheckReferenceDialogs(screenshots);
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

    private static void CheckReferenceDialogs(string? screenshots)
    {
        var catalog = new DesktopApplicationCatalog(new DesktopWindowInspector());
        IReadOnlyList<DesktopApplicationCandidate> candidates = [
            new() { DisplayName = "Notepad", ProcessName = "notepad", ExecutablePath = "C:\\Synthetic\\notepad.exe", IsRunning = true },
            new() { DisplayName = "Synthetic editor", ProcessName = "editor", ExecutablePath = "C:\\Synthetic\\editor.exe", IsRunning = true },
            new() { DisplayName = "Synthetic browser", ProcessName = "browser", ExecutablePath = "C:\\Synthetic\\browser.exe", IsRunning = true },
        ];
        var loads = 0;
        var picker = new AddApplicationWindow(catalog, new HashSet<string>(), null, () => { loads++; return candidates; });
        Layout((FrameworkElement)picker.Content, new Size(512, 500));
        var search = (TextBox)picker.FindName("SearchTextBox");
        search.Text = "EDITOR";
        Check(picker.ApplicationsView.Cast<object>().Count() == 1, "Picker search did not filter by process/name.");
        search.Text = "unmatched";
        Check(((TextBlock)picker.FindName("EmptyStateText")).Visibility == Visibility.Visible, "Picker empty state absent.");
        search.Clear();
        Invoke(picker, "Refresh_Click");
        Check(loads == 2 && picker.SelectedApplication?.ProcessName == "notepad", "Refresh/selection failed.");
        Check(((TextBlock)picker.FindName("SelectedTargetText")).Text == "C:\\Synthetic\\notepad.exe", "Selected path lost.");
        CheckDialogLayout(picker, "AddApplication", screenshots, 544, 528);
        picker.Close();

        var relink = new AddApplicationWindow(catalog, new HashSet<string>(), "Existing private editor", () => candidates);
        Check(((FrameworkElement)relink.FindName("ProfileNamePanel")).Visibility == Visibility.Collapsed, "Relink allowed profile renaming.");
        Check(((TextBlock)relink.FindName("HeadingText")).Text == "Relink application", "Relink heading absent.");
        CheckDialogLayout(relink, "RelinkApplication", screenshots, 544, 528);
        relink.Close();

        var onboarding = new OnboardingWindow(true, false);
        Check(onboarding.LaunchAtLogin && !onboarding.StartMinimized, "Onboarding overwrote supplied preferences.");
        ((CheckBox)onboarding.FindName("LaunchAtLoginCheckBox")).IsChecked = false;
        ((CheckBox)onboarding.FindName("StartMinimizedCheckBox")).IsChecked = true;
        Check(!onboarding.LaunchAtLogin && onboarding.StartMinimized, "Onboarding switches failed.");
        CheckDialogLayout(onboarding, "FirstLaunch", screenshots, 584, 568);
        onboarding.Close();

        var create = new PanicPinSetupWindow(null);
        Check(((FrameworkElement)create.FindName("CurrentPinPanel")).Visibility == Visibility.Collapsed, "First creation shows current PIN.");
        var newPin = (PinEntry)create.FindName("NewPinBox");
        var confirm = (PinEntry)create.FindName("ConfirmPinBox");
        newPin.Password = "1234";
        confirm.Password = "5678";
        Invoke(create, "Save_Click");
        Check(create.CreatedCredential is null && confirm.Password.Length == 0, "Mismatched PIN accepted.");
        newPin.Password = "abcd";
        Invoke(create, "Save_Click");
        Check(create.CreatedCredential is null, "Non-digit PIN accepted.");
        newPin.Password = "123456";
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)newPin.FindName("RevealButton");
        toggle.IsChecked = true;
        var visible = (TextBox)newPin.FindName("VisibleInput");
        Check(newPin.IsRevealed && visible.Text == "123456", "Explicit reveal failed.");
        visible.Text = "1234";
        Check(newPin.Password == "1234", "Revealed editing did not synchronize.");
        toggle.IsChecked = false;
        Check(visible.Text.Length == 0 && !newPin.IsRevealed && newPin.Password == "1234", "Hiding PIN retained plaintext or lost masked value.");
        newPin.Clear();
        ((TextBlock)create.FindName("ErrorText")).Text = string.Empty;
        CheckDialogLayout(create, "CreatePin", screenshots, 492, 440);
        create.Close();

        var credential = PanicPinService.Create("1234");
        var change = new PanicPinSetupWindow(credential);
        var current = (PinEntry)change.FindName("CurrentPinBox");
        Check(((FrameworkElement)change.FindName("CurrentPinPanel")).Visibility == Visibility.Visible, "Change omitted current PIN.");
        current.Password = "9999";
        Invoke(change, "Save_Click");
        Check(change.CreatedCredential is null && current.Password.Length == 0, "Incorrect current PIN was accepted/retained.");
        CheckDialogLayout(change, "ChangePin", screenshots, 524, 508);
        current.Password = "1234";
        ((System.Windows.Controls.Primitives.ToggleButton)current.FindName("RevealButton")).IsChecked = true;
        change.Close();
        Check(current.Password.Length == 0 && ((TextBox)current.FindName("VisibleInput")).Text.Length == 0 && !current.IsRevealed,
            "Closed PIN dialog retained visible or masked input.");

        var unlock = new PanicPinUnlockWindow(credential, "Notepad");
        var unlockPin = (PinEntry)unlock.FindName("PinBox");
        unlockPin.Password = "9999";
        Invoke(unlock, "Unlock_Click");
        Check(unlock.DialogResult != true && unlockPin.Password.Length == 0 && ((TextBlock)unlock.FindName("ErrorText")).Text.Contains("Mask remains active"),
            "Wrong PIN unlocked panic or retained input.");
        Check(((TextBlock)unlock.FindName("HeadingText")).Text == "Unlock Notepad", "Unlock target absent.");
        CheckDialogLayout(unlock, "UnlockPanic", screenshots, 492, 440);
        unlock.Close();
        Check(PanicPinService.Verify("1234", credential), "Dialog changed the original credential.");
        Console.WriteLine("Reference dialogs passed: synthetic picker/search/relink, startup switches, PIN validation/reveal/cleanup and bounded layouts.");
    }

    private static void CheckDialogLayout(Window dialog, string name, string? screenshots, double width, double height)
    {
        var root = (FrameworkElement)dialog.Content;
        foreach (var size in new[] { new Size(width, height), new Size(420, 340) })
        {
            Layout(root, size);
            CheckFiniteControls(root);
            foreach (var button in Find<Button>(root).Where(control => control.Visibility == Visibility.Visible && control.ActualWidth > 0 && control.ActualHeight > 0))
            {
                var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(new Point(), button.RenderSize));
                Check(bounds.Left >= -1 && bounds.Right <= size.Width + 1 && bounds.Top >= -1 && bounds.Bottom <= size.Height + 1,
                    $"{name}: action outside viewport at {size}.");
            }
        }
        if (screenshots is not null)
        {
            var size = new Size(width, height);
            Layout(root, size);
            Render(root, size, 1, System.IO.Path.Combine(screenshots, $"{name}.png"));
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
        var backdrop = new DrawingVisual();
        using (var context = backdrop.RenderOpen())
            context.DrawRectangle((Brush)Application.Current.Resources["CanvasBrush"], null, new Rect(new Point(), size));
        bitmap.Render(backdrop);
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
