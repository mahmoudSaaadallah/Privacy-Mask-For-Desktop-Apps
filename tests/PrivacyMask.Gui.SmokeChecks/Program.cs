using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
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
                        if (page == DesktopPage.About)
                        {
                            var email = Find<TextBox>((DependencyObject)window.FindName("AboutColumns")).Single(box => AutomationProperties.GetName(box) == "Support email address");
                            Check(email.ExtentWidth <= email.ViewportWidth + 1, "About email text clipped at a scaled layout.");
                        }
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
                CheckHelpAndSidebar(window, root, session);
                CheckAboutReference(window, root, session, screenshots);
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
                CheckAwaitedWindowShutdown();
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

    private static void CheckHelpAndSidebar(MainWindow window, FrameworkElement root, SettingsViewModel session)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(session.ToModel());
        var icon = (Image)window.FindName("SidebarBrandIcon");
        var header = (FrameworkElement)window.FindName("SidebarBrandHeader");
        var expected = (DrawingImage)window.FindResource("AboutShieldSymbol");
        foreach (var page in Enum.GetValues<DesktopPage>())
        {
            session.SelectedPage = page;
            Layout(root, new Size(1000, 880));
            Check(ReferenceEquals(icon.Source, expected) && header.Visibility == Visibility.Visible,
                $"Sidebar branding changes on {page}.");
            var pageRoot = (DependencyObject)window.FindName($"{page}Page");
            var helpControls = Find<Button>(pageRoot).Where(button => InformationHelp.GetText(button) is not null).ToArray();
            foreach (var control in helpControls)
            {
                var tip = (ToolTip)control.ToolTip;
                control.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, null, control)
                    { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
                Check(!tip.IsOpen, $"{page} help opened automatically on focus.");
                Check(AutomationProperties.GetHelpText(control) == InformationHelp.GetText(control), "Help lost accessible text.");
                Check(ToolTipService.GetIsEnabled(control) && ReferenceEquals(ToolTipService.GetToolTip(control), tip)
                    && ReferenceEquals(tip.PlacementTarget, control), "Native hover help registration lost.");
                var hoverOnly = page is DesktopPage.Overview or DesktopPage.Security;
                Check(InformationHelp.GetHoverOnly(control) == hoverOnly, $"Wrong help policy on {page}.");
                if (hoverOnly)
                {
                    foreach (var key in new[] { Key.F1, Key.Enter, Key.Space })
                    {
                        control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new TestInputSource(), Environment.TickCount, key)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        Check(!tip.IsOpen, $"{page} hover-only help opened from {key}.");
                    }
                }
            }
            if (page is DesktopPage.Overview or DesktopPage.Security)
                Check(helpControls.Length > 0, "Target page has no information controls to verify.");
        }
        Check(System.Text.Json.JsonSerializer.Serialize(session.ToModel()) == before && !session.HasPendingChanges,
            "Help/sidebar navigation changed settings.");
        Console.WriteLine("Help/sidebar passed: focus never opens help, privacy pages are hover-only, native hover/accessible text remain and shield identity is constant.");
    }

    private static void CheckAwaitedWindowShutdown()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            MainWindow CreateWindow() => new(SettingsViewModel.FromModel(new DefaultSettingsFactory().Create(),
                RuntimeMode.Standard, "Synthetic settings path", new HashSet<HotkeyAction>()),
                new DesktopApplicationCatalog(new DesktopWindowInspector()));
            var saving = CreateWindow();
            var ioFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = 0;
            var discards = 0;
            saving.SaveRequested += async _ => { saves++; await ioFinished.Task; Check(saving.Dispatcher.CheckAccess(), "Save left the dispatcher."); };
            saving.DiscardRequested += () => discards++;
            saving.ViewModel.StartMinimized = !saving.ViewModel.StartMinimized;
            Invoke(saving, "Save_Click");
            Check(saving.ViewModel.IsSaving && !saving.IsEnabled, "Pending save did not freeze the editor.");
            saving.ViewModel.AppProfiles[0].MaskIntensityPercent = 42;
            Invoke(saving, "CloseToTray_Click");
            saving.Close();
            Check(discards == 0, "Close discarded/replaced a saving view model.");
            var saveDrain = saving.BeginShutdownAsync();
            Check(!saveDrain.IsCompleted, "Shutdown ignored the event handler's save continuation.");
            Invoke(saving, "Save_Click");
            Check(saves == 1, "Shutdown admitted another editor save.");
            ioFinished.SetResult();
            PumpUntil(saveDrain);
            Check(!saving.ViewModel.IsSaving && saving.ViewModel.HasPendingChanges && !saving.IsEnabled,
                "Save cleanup re-enabled a closing window or marked a later change as saved.");
            saving.AllowClose();
            saving.Close();

            var replacing = CreateWindow();
            replacing.SaveRequested += updated =>
            {
                replacing.ReplaceViewModel(SettingsViewModel.FromModel(updated, RuntimeMode.Standard, "Synthetic",
                    new HashSet<HotkeyAction>()));
                replacing.ViewModel.AppProfiles[0].MaskIntensityPercent = 37;
                return Task.CompletedTask;
            };
            replacing.ViewModel.StartMinimized = !replacing.ViewModel.StartMinimized;
            Invoke(replacing, "Save_Click");
            Check(replacing.ViewModel.HasPendingChanges, "Save overwrote the replacement session's authoritative baseline.");
            replacing.AllowClose();
            replacing.Close();

            var panic = CreateWindow();
            var panicFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            panic.GlobalPanicRequested += () => panicFinished.Task;
            Invoke(panic, "GlobalPanic_Click");
            Check(panic.ViewModel.IsRuntimeActionBusy, "Panic event is not tracked.");
            var panicDrain = panic.BeginShutdownAsync();
            Check(!panicDrain.IsCompleted, "Shutdown ignored a pending runtime event.");
            panicFinished.SetResult();
            PumpUntil(panicDrain);
            Check(!panic.ViewModel.IsRuntimeActionBusy && !panic.IsEnabled, "Panic cleanup crossed window disposal.");
            panic.AllowClose();
            panic.Close();

            var queue = new ShellOperationQueue();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new List<int>();
            var first = queue.TryRunAsync(async () =>
            {
                await gate.Task;
                Check(Dispatcher.CurrentDispatcher == Application.Current.Dispatcher, "Mutation lost UI affinity.");
                events.Add(1);
            });
            var second = queue.TryRunAsync(async () =>
            {
                Check(Application.Current.Dispatcher.CheckAccess(), "Queued mutation lost UI affinity.");
                await Task.Yield();
                Check(Application.Current.Dispatcher.CheckAccess(), "Post-write state update lost UI affinity.");
                events.Add(2);
            });
            var shutdown = new ApplicationShutdown(async () => { await queue.StopAsync(); events.Add(3); },
                () => events.Add(4), _ => events.Add(5));
            var exit = shutdown.RunAsync();
            Check(!exit.IsCompleted && events.Count == 0, "Exit raced a pending UI mutation.");
            gate.SetResult();
            PumpUntil(Task.WhenAll(first, second, exit));
            Check(events.SequenceEqual(new[] { 1, 2, 3, 4, 5 }), "Dispatcher shutdown order is invalid.");
            Console.WriteLine("Awaited shutdown passed: saving close guards, event continuations, panic event drain and FIFO dispatcher/singleton/exit ordering.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void PumpUntil(Task task)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5)) Drain();
        Check(task.IsCompleted, "Dispatcher operation failed to finish before its test deadline.");
        task.GetAwaiter().GetResult();
    }

    private static void CheckAboutReference(MainWindow window, FrameworkElement root, SettingsViewModel session, string? screenshots)
    {
        var saved = System.Text.Json.JsonSerializer.Serialize(session.ToModel());
        session.SelectedPage = DesktopPage.About;
        var page = (ScrollViewer)window.FindName("AboutPage");
        var columns = (AdaptivePairPanel)window.FindName("AboutColumns");
        var emailActions = (AdaptivePairPanel)window.FindName("AboutEmailActions");
        var version = Find<TextBlock>(page).Single(text => AutomationProperties.GetName(text) == "PrivacyMask product version");
        Check(version.Text == AboutInformation.VersionText && (string)version.ToolTip == AboutInformation.BuildDescription,
            "About did not display the semantic version or expose its build identity.");
        var shield = (DrawingImage)window.FindResource("AboutShieldSymbol");
        Check(shield.IsFrozen && shield.Drawing.Bounds.Width > 0 && shield.Drawing.Bounds.Height > 0,
            "About shield is not a reusable frozen vector.");
        foreach (var (width, expectColumns, expectEmailRow) in new[] { (1280d, true, true), (1000d, true, false), (860d, false, true) })
        {
            Layout(root, new Size(width, 880));
            Check(columns.IsSideBySide == expectColumns, $"About columns did not adapt at {width}.");
            Check(emailActions.IsSideBySide == expectEmailRow, $"About email actions did not adapt at {width}.");
            Check(page.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && page.ExtentWidth <= page.ViewportWidth + 1,
                $"About page requires horizontal scrolling at {width}.");
            foreach (var action in Find<Button>(columns))
            {
                var bounds = action.TransformToAncestor(columns).TransformBounds(new Rect(new Point(), action.RenderSize));
                Check(bounds.Left >= -1 && bounds.Right <= columns.ActualWidth + 1, $"About action clipped horizontally at {width}.");
            }
            if (screenshots is not null) Render(root, new Size(width, 880), 1, System.IO.Path.Combine(screenshots, $"About-reference-{width:0}.png"));
        }
        var address = Find<TextBox>(columns).Single(box => AutomationProperties.GetName(box) == "Support email address");
        Check(address.IsReadOnly && address.Text == AboutInformation.SupportEmail, "About email is not the authoritative read-only value.");
        var links = Find<Button>(columns).Where(button => button.Tag is string).ToArray();
        Check(links.Count(button => (string)button.Tag == "email") == 1
            && links.Count(button => (string)button.Tag == "github") == 2
            && links.Count(button => (string)button.Tag == "linkedin") == 2, "About contact routes lost.");
        Check(links.All(button => AutomationProperties.GetName(button).Length > 0), "About contact action lost its accessible name.");
        var help = Find<Button>(columns).Single(button => AutomationProperties.GetName(button) == "Privacy and capture limitations");
        Check(InformationHelp.GetText(help)!.Contains("Blur can retain detail"), "About lost full concealment guidance.");
        var feedback = (TextBlock)window.FindName("ContactFeedback");
        Check(feedback.Visibility == Visibility.Collapsed, "Empty contact feedback consumes space.");
        feedback.Text = "Synthetic clipboard unavailable feedback";
        Layout(root, new Size(1000, 880));
        Check(feedback.Visibility == Visibility.Visible, "Contact feedback is hidden after an error.");
        feedback.Text = string.Empty;
        Check(System.Text.Json.JsonSerializer.Serialize(session.ToModel()) == saved && !session.HasPendingChanges,
            "About presentation or navigation edited settings.");
        Console.WriteLine("About reference passed: adaptive card/email layouts, unclipped actions, authoritative contacts, help and non-mutating feedback.");
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
        ((CheckBox)onboarding.FindName("LaunchAtLoginCheckBox")).IsChecked = true;
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
        var unlockRoot = (FrameworkElement)unlock.Content;
        Layout(unlockRoot, new Size(492, 440));
        var inputBottom = unlockPin.TransformToAncestor(unlockRoot).Transform(new Point(0, unlockPin.ActualHeight)).Y;
        var errorTop = ((TextBlock)unlock.FindName("ErrorText")).TransformToAncestor(unlockRoot).Transform(new Point()).Y;
        Check(errorTop >= inputBottom && errorTop - inputBottom <= 28, "Unlock error is separated from its PIN input.");
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

// Routed-event source only: no native window, OS input or production runtime.
internal sealed class TestInputSource : PresentationSource
{
    public override Visual RootVisual { get; set; } = new DrawingVisual();
    public override bool IsDisposed => false;
    protected override CompositionTarget GetCompositionTargetCore() => null!;
}
