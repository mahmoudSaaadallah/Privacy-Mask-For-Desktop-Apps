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
using PrivacyMask.Updates;

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
                CheckUpdateSettings(window, root, session, screenshots);
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

            var updating = CreateWindow();
            var updateSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var updateDiscards = 0;
            updating.SaveRequested += _ => updateSave.Task;
            updating.DiscardRequested += () => updateDiscards++;
            updating.ViewModel.StartMinimized = !updating.ViewModel.StartMinimized;
            Invoke(updating, "Save_Click");
            var updateDrain = updating.SuspendForUpdateAsync();
            Check(!updateDrain.IsCompleted && !updating.IsEnabled, "Update did not wait for the full Save handler.");
            Invoke(updating, "CloseToTray_Click");
            updating.Close();
            Check(updateDiscards == 0, "Update suspension discarded the pending session.");
            updateSave.SetResult();
            PumpUntil(updateDrain);
            Check(!updating.IsEnabled, "Save finally re-enabled a suspended update editor.");
            updating.ResumeAfterUpdate();
            Check(updating.IsEnabled, "Canceled update did not restore the editor.");
            _ = updating.SuspendForUpdateAsync();
            PumpUntil(updating.BeginShutdownAsync());
            updating.ResumeAfterUpdate();
            Check(!updating.IsEnabled, "Update cancellation reopened a closing editor.");
            updating.AllowClose();
            updating.Close();

            var queue = new ShellOperationQueue();
            var checking = CreateWindow();
            var checkFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var checkRequests = 0;
            checking.CheckForUpdatesRequested += () => { checkRequests++; return checkFinished.Task; };
            Invoke(checking, "CheckForUpdates_Click");
            Invoke(checking, "CheckForUpdates_Click");
            var checkDrain = checking.BeginShutdownAsync();
            Check(checkRequests == 1 && !checkDrain.IsCompleted, "Shutdown ignored a pending manual update handler or admitted a duplicate.");
            checkFinished.SetResult();
            PumpUntil(checkDrain);
            checking.AllowClose();
            checking.Close();

            var downloading = CreateWindow();
            var downloadFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadRequests = 0;
            downloading.DownloadUpdateRequested += () => { downloadRequests++; return downloadFinished.Task; };
            Invoke(downloading, "DownloadUpdate_Click");
            Invoke(downloading, "DownloadUpdate_Click");
            Invoke(downloading, "CheckForUpdates_Click");
            var downloadDrain = downloading.BeginShutdownAsync();
            Check(downloadRequests == 1 && !downloadDrain.IsCompleted, "Shutdown ignored a download handler or admitted another action.");
            downloadFinished.SetResult();
            PumpUntil(downloadDrain);
            downloading.AllowClose();
            downloading.Close();

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

    private static void CheckUpdateSettings(MainWindow window, FrameworkElement root, SettingsViewModel session, string? screenshots)
    {
        session.SelectedPage = DesktopPage.Settings;
        var before = System.Text.Json.JsonSerializer.Serialize(session.ToModel());
        var requests = 0;
        Task CheckRequest()
        {
            requests++;
            session.Updates.Begin();
            session.Updates.Complete(new(PrivacyMask.Updates.UpdateCheckStatus.NotConfigured), session.UpdateChannel, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
        window.CheckForUpdatesRequested += CheckRequest;
        Invoke(window, "CheckForUpdates_Click");
        window.CheckForUpdatesRequested -= CheckRequest;
        Check(requests == 1 && session.Updates.StatusText.Contains("not configured"), "Manual update action is disconnected or misrepresents unconfigured trust.");
        // Display-only fixture: never use these bytes for discovery, staging or execution.
        var manifest = new UpdateManifest(1, ManifestRules.AppId, ManifestRules.Repository, "2.0.0", "v2.0.0",
            new string('1', 40), "stable", "win-x64", 19041, 1,
            new(ManifestRules.ArchiveName("2.0.0"), 70 * 1048576, new string('a', 64)),
            [new("PrivacyMask.App.exe", 140 * 1048576, new string('b', 64))], new(1, 13, 13));
        var candidate = new UpdateCandidate(10, 103, GitHubUrls.ReleasePage("v2.0.0"), GitHubUrls.AssetApi(103),
            manifest, default, default, new string('N', 4096));
        session.Updates.Complete(new(UpdateCheckStatus.Available, candidate), session.UpdateChannel, DateTimeOffset.UtcNow);
        var downloads = 0;
        var discards = 0;
        var cancels = 0;
        var installs = 0;
        Task DownloadRequest() { downloads++; return Task.CompletedTask; }
        Task DiscardRequest() { discards++; return Task.CompletedTask; }
        void CancelRequest() { cancels++; }
        void InstallRequest() { installs++; session.Updates.BeginInstall(); }
        window.InstallUpdateRequested += InstallRequest;
        Invoke(window, "InstallUpdate_Click");
        Check(installs == 0, "Install accepted a non-ready download.");
        window.DownloadUpdateRequested += DownloadRequest;
        window.DiscardUpdateRequested += DiscardRequest;
        window.CancelUpdateDownloadRequested += CancelRequest;
        Invoke(window, "DownloadUpdate_Click");
        Invoke(window, "DiscardUpdate_Click");
        Invoke(window, "CancelUpdateDownload_Click");
        window.DownloadUpdateRequested -= DownloadRequest;
        window.DiscardUpdateRequested -= DiscardRequest;
        window.CancelUpdateDownloadRequested -= CancelRequest;
        Check(downloads == 1 && discards == 1 && cancels == 1, "Download/discard/cancel actions are disconnected.");
        foreach (var state in new[] { "available", "downloading", "ready", "installing", "restarting", "cancelled", "cleanup-failed", "repair-required" })
        {
            var card = (Border)window.FindName("UpdatesCard");
            if (state == "downloading")
            {
                session.Updates.BeginDownload();
                session.Updates.DownloadProgress(new(UpdateDownloadPhase.Downloading, 35 * 1048576, 70 * 1048576));
            }
            else if (state == "ready")
            {
                session.Updates.FinishDownload("Verified download ready. Nothing has been installed.", ready: true);
                Invoke(window, "InstallUpdate_Click");
                Invoke(window, "InstallUpdate_Click");
                Check(installs == 1, "Install action is disconnected or admitted a duplicate.");
                Check(window.SuspendForUpdateAsync().IsCompleted, "Update suspension waits for its own install caller.");
                window.ResumeAfterUpdate();
                session.Updates.FinishInstall("Installation cancelled. Verified download retained.");
            }
            else if (state == "installing") session.Updates.BeginInstall();
            else if (state == "restarting") session.Updates.ShowRestarting();
            else if (state == "cancelled") session.Updates.FinishInstall("Installation cancelled. Your download is retained.");
            else if (state == "cleanup-failed") session.Updates.FinishDownload("Download failed. Your application is unchanged.", cleanupIncomplete: true);
            else if (state == "repair-required") session.Updates.FinishInstall("Update recovery needs attention. Close sensitive windows, exit and restart PrivacyMask. Settings stay paused.", repairRequired: true);
            foreach (var width in new[] { 880d, 1000d, 1400d })
            {
                var size = new Size(width, 780);
                Layout(root, size);
                foreach (var button in Find<Button>(card))
                {
                    var name = AutomationProperties.GetName(button);
                    if ((name == "Install verified update now" && session.Updates.CanInstall) ||
                        (name == "Cancel update download" && session.Updates.IsDownloading) ||
                        (name == "Discard verified update download" && session.Updates.CanDiscardDownload))
                        Check(button.Visibility == Visibility.Visible && button.ActualHeight >= 30 && button.ActualWidth >= 80,
                            $"Update action {name} has no usable rendered area at {width} in {state}.");
                }
                foreach (var control in Find<Control>(card))
                {
                    var bounds = control.TransformToAncestor(card).TransformBounds(new Rect(new Point(), control.RenderSize));
                    Check(bounds.Left >= -1 && bounds.Right <= card.ActualWidth + 1, $"Update action clipped horizontally at {width}.");
                }
                if (screenshots is not null) Render(root, size, 1, System.IO.Path.Combine(screenshots, $"Updates-{state}-{width:0}.png"));
            }
            var channel = Find<ComboBox>(card).Single(combo => AutomationProperties.GetName(combo) == "Update release channel");
            var automatic = Find<CheckBox>(card).Single(box => AutomationProperties.GetName(box) == "Automatically check for updates");
            Check(Equals(channel.SelectedItem, session.UpdateChannel) && automatic.IsChecked == session.AutomaticUpdateChecks,
                "Update preferences lost their two-way bindings.");
        }
        window.InstallUpdateRequested -= InstallRequest;
        session.Updates.Complete(new(UpdateCheckStatus.NotConfigured), session.UpdateChannel, DateTimeOffset.UtcNow);
        Check(before == System.Text.Json.JsonSerializer.Serialize(session.ToModel()) && !session.HasPendingChanges,
            "Manual discovery changed the settings edit session.");
        CheckMandatoryUpdatePolicy(candidate);
        Console.WriteLine("Update card passed: connected check/download/cancel/discard/install, no self-drain and bounded ready/restart/cancel/repair layouts with unchanged settings.");
    }

    private static void CheckMandatoryUpdatePolicy(UpdateCandidate candidate)
    {
        var status = new UpdateStatusViewModel();
        status.RequireAutomaticUpdates();
        var settings = new DefaultSettingsFactory().Create();
        settings.AutomaticUpdateChecks = false;
        using var model = SettingsViewModel.FromModel(settings, RuntimeMode.Standard, "C:\\Synthetic\\settings.v1.json", new HashSet<HotkeyAction>(), status);
        var window = new MainWindow(model, new DesktopApplicationCatalog(new DesktopWindowInspector()));
        var root = (FrameworkElement)window.Content;
        model.SelectedPage = DesktopPage.Settings;
        status.Complete(new(UpdateCheckStatus.Available, candidate), status.RequiredChannel, DateTimeOffset.UtcNow);
        status.BeginDownload();
        Layout(root, new Size(1000, 780));
        var channel = Find<ComboBox>(root).Single(control => AutomationProperties.GetName(control) == "Update release channel");
        var automatic = Find<CheckBox>(root).Single(control => AutomationProperties.GetName(control) == "Automatically check for updates");
        Check(!channel.IsEnabled && !automatic.IsEnabled && automatic.IsChecked == true, "Required policy can be disabled or changed through settings.");
        Check(Find<Button>(root).Single(control => AutomationProperties.GetName(control) == "Download update").Visibility == Visibility.Collapsed,
            "Required update unexpectedly requires a Download click.");
        Check(Find<AdaptivePairPanel>(root).Any(panel => panel.Visibility == Visibility.Collapsed &&
            Find<Button>(panel).Any(button => AutomationProperties.GetName(button) == "Cancel update download")),
            "Required update cancellation/actions remain visible.");
        var controller = new MandatoryUpdateController(status, new(() => false, () => throw new Exception("No connectivity inspection"),
            _ => throw new Exception("No check"), () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask, () => null, () => null));
        using var monitor = new MandatoryUpdateMonitor(Dispatcher.CurrentDispatcher, controller);
        monitor.Start();
        Check(!monitor.IsRunning, "Activation-disabled build started an automatic monitor.");
        monitor.Dispose();
        monitor.Pulse();
        var previewStatus = new UpdateStatusViewModel();
        previewStatus.RequireAutomaticUpdates();
        var automaticChecks = 0;
        var previewController = new MandatoryUpdateController(previewStatus, new(() => true, () => true,
            _ =>
            {
                automaticChecks++;
                previewStatus.Complete(new(UpdateCheckStatus.UpToDate), previewStatus.RequiredChannel, DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            }, () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask,
            () => DateTimeOffset.UtcNow.AddHours(24), () => null));
        using (var previewMonitor = new MandatoryUpdateMonitor(Dispatcher.CurrentDispatcher, previewController))
        {
            previewMonitor.Start();
            Drain();
            Check(previewMonitor.IsRunning && automaticChecks == 1, "Enabled Preview did not start its automatic dispatcher check.");
        }
        Check(Find<TextBlock>(root).Any(text => text.Text == AboutInformation.UpdateScheduleSummary),
            "Settings do not disclose the actual compiled update schedule.");
        var expectedCadence = AboutInformation.ProductVersion == "2.0.0-beta.14" ? "10 minutes" : "24 hours";
        Check(AboutInformation.UpdateScheduleSummary.Contains(expectedCadence), "Trial cadence does not match the current version.");
        window.AllowClose();
        window.Close();
        Console.WriteLine("Required update policy passed: locked preferences, hidden cancel/discard actions, enabled Preview startup and no disabled-host monitor.");
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
