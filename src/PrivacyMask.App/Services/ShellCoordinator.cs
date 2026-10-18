using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using PrivacyMask.App.ViewModels;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Updates;
using PrivacyMask.Windows.Adapters;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Services;

internal sealed record ShellStartupContext(
    AppSettings Settings,
    string StartupExecutablePath,
    bool StartMinimized,
    bool DeferInteractiveUi,
    bool RequireSuccessfulInitialRefresh);

public sealed class ShellCoordinator : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly string[] _args;
    private readonly Func<Task> _requestShutdown;
    private readonly DefaultSettingsFactory _defaultSettingsFactory;
    private readonly ProtectionStateMachine _protectionStateMachine;
    private readonly ProfileProtectionState _profileProtectionState;
    private readonly ExclusiveHoldGesture _temporaryRevealGesture;
    private readonly JsonSettingsStore _settingsStore;
    private readonly DesktopWindowInspector _windowInspector;
    private readonly DesktopApplicationCatalog _applicationCatalog;
    private readonly WindowProfileResolver _windowProfileResolver;
    private readonly OverlayManager _overlayManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly ToolStripMenuItem _modeItem;
    private readonly ToolStripMenuItem _launchAtLoginItem;
    private readonly ToolStripMenuItem _toggleProtectionItem;
    private readonly ToolStripMenuItem _panicItem;
    private readonly List<TrackedWindow> _trackedWindows = [];
    private readonly Dictionary<nint, List<ScreenRect>> _occludingBoundsByWindow = [];
    private readonly HashSet<nint> _activeOcclusionHandles = [];
    private readonly List<nint> _staleOcclusionHandles = [];
    private readonly ShellOperationQueue _operations = new();
    private readonly UpdateRestartCoordinator _updateRestart;
    private readonly RuntimeUpdateDiscovery _updateDiscovery;
    private readonly UpdateCheckSession _updateChecks;
    private readonly ToolStripMenuItem _checkUpdatesItem;
    private readonly SemaphoreSlim _panicOperationGate = new(1, 1);
    private readonly SemaphoreSlim _profileProtectionOperationGate = new(1, 1);
    private readonly ShellStartupContext? _startupContext;

    private AppSettings _settings = new();
    private AppSettings _savedSettings = new();
    private WindowProcessFilter _windowProcessFilter = new([]);
    private IReadOnlySet<HotkeyAction> _unavailableHotkeyActions = new HashSet<HotkeyAction>();
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;
    private bool _isUpdateSuspended;
    private bool _isDisposed;
    private bool _interactiveStartupCompleted;
    private Task? _disposeTask;

    public ShellCoordinator(Dispatcher dispatcher, string[] args, Func<Task> requestShutdown)
        : this(dispatcher, args, requestShutdown, null)
    {
    }

    internal ShellCoordinator(Dispatcher dispatcher, string[] args, Func<Task> requestShutdown,
        ShellStartupContext? startupContext)
    {
        _dispatcher = dispatcher;
        _args = args;
        _requestShutdown = requestShutdown;
        _startupContext = startupContext;
        _defaultSettingsFactory = new DefaultSettingsFactory();
        _protectionStateMachine = new ProtectionStateMachine();
        _profileProtectionState = new ProfileProtectionState();
        _updateRestart = new(_operations, new(
            () => _protectionStateMachine.CurrentMode == RuntimeMode.Panic || _profileProtectionState.HasAnyPanic,
            () => _mainWindow?.HasUpdateBlockingEdits == true,
            () => _isShuttingDown,
            () => _mainWindow?.ResolveUpdateEditsAsync() ?? Task.FromResult(true),
            ConfirmUpdateRestart,
            SuspendForUpdateAsync,
            ResumeAfterUpdate,
            _requestShutdown));
        _temporaryRevealGesture = new ExclusiveHoldGesture(TimeSpan.FromMilliseconds(500d));
        _settingsStore = new JsonSettingsStore(_defaultSettingsFactory);
        _updateDiscovery = new RuntimeUpdateDiscovery(_settingsStore.SettingsPath);
        _updateChecks = new UpdateCheckSession(_updateDiscovery.CheckAsync,
            history: new UpdateCheckHistoryStore(Path.GetDirectoryName(_settingsStore.SettingsPath)!),
            preflight: _updateDiscovery.Preflight);
        _windowInspector = new DesktopWindowInspector();
        _applicationCatalog = new DesktopApplicationCatalog(_windowInspector);
        _windowProfileResolver = new WindowProfileResolver(
        [
            new WhatsAppWindowAdapter(),
            new TelegramWindowAdapter(),
            new GenericWindowAdapter(),
        ]);
        _overlayManager = new OverlayManager();
        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.HotkeyPressed += HandleHotkeyPressed;

        var applicationPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMask.App.exe");
        var executablePath = startupContext?.StartupExecutablePath
            ?? ManagedStartupPath.Resolve(applicationPath)
            ?? applicationPath;
        _startupRegistrationService = new StartupRegistrationService("PrivacyMask.Desktop", executablePath);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = OverlayRefreshCadence.IdleInterval,
        };
        _refreshTimer.Tick += RefreshTimerOnTick;

        var iconResource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/PrivacyMask.ico"));
        using (var iconStream = iconResource.Stream)
        using (var sourceIcon = new System.Drawing.Icon(iconStream, SystemInformation.SmallIconSize))
        {
            // Own a copy independently of the resource stream for the tray lifetime.
            _trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "PrivacyMask",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        _modeItem = new ToolStripMenuItem("Protection: Active")
        {
            Enabled = false,
        };
        _toggleProtectionItem = new ToolStripMenuItem("Toggle protection", null, (_, _) => ToggleProtection());
        _panicItem = new ToolStripMenuItem("Panic mask all", null, async (_, _) => await ObserveShellActionAsync(ToggleGlobalPanicModeAsync()));
        _launchAtLoginItem = new ToolStripMenuItem("Launch at sign in", null, async (_, _) => await ObserveShellActionAsync(ToggleLaunchAtLoginAsync()))
        {
            CheckOnClick = true,
        };
        _checkUpdatesItem = new ToolStripMenuItem("Check for updates", null,
            async (_, _) => await ObserveShellActionAsync(CheckForUpdatesAsync(showSettings: true)));
        _updateChecks.Status.PropertyChanged += UpdateCheckStatusChanged;

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            _modeItem,
            new ToolStripSeparator(),
            _toggleProtectionItem,
            _panicItem,
            new ToolStripMenuItem("Open settings", null, (_, _) => ShowSettingsWindow()),
            _checkUpdatesItem,
            _launchAtLoginItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, async (_, _) =>
            {
                if (!_updateRestart.IsBusy) await ObserveShellActionAsync(_requestShutdown());
            }),
        ]);
    }

    public async Task StartAsync()
    {
        if (!await _operations.TryRunAsync(StartCoreAsync, requireIdle: true))
            throw new InvalidOperationException("PrivacyMask startup is no longer available.");
    }

    private async Task StartCoreAsync()
    {
        _settings = _startupContext is null
            ? await _settingsStore.LoadAsync()
            : AppSettingsCloner.Clone(_startupContext.Settings);
        if (_isShuttingDown) return;
        RebuildWindowProcessFilter();
        var registrationState = _startupRegistrationService.GetState();
        if (registrationState.Enabled)
        {
            _settings.LaunchAtLogin = true;
            _settings.StartMinimized = registrationState.StartMinimized;
        }

        _savedSettings = AppSettingsCloner.Clone(_settings);
        _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);

        RegisterHotkeys();
        UpdateTrayState();
        RefreshOverlays(_startupContext?.RequireSuccessfulInitialRefresh == true);
        _refreshTimer.Start();

        if (_startupContext?.DeferInteractiveUi == true) return;
        await StartInteractiveUiCoreAsync();
    }

    internal async Task CompleteInteractiveStartupAsync()
    {
        if (!await _operations.TryRunAsync(StartInteractiveUiCoreAsync, requireIdle: true))
            throw new InvalidOperationException("PrivacyMask interactive startup is no longer available.");
    }

    private async Task StartInteractiveUiCoreAsync()
    {
        if (_interactiveStartupCompleted || _isShuttingDown) return;
        _interactiveStartupCompleted = true;
        if (!_settings.OnboardingCompleted)
        {
            await ShowOnboardingAsync();
            if (_settings.OnboardingCompleted) _ = _updateChecks.ScheduleStartupCheck(SavedUpdatePreferences);
            return;
        }

        var shouldStartMinimized = _startupContext?.StartMinimized
            ?? _args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!shouldStartMinimized)
        {
            ShowSettingsWindow();
        }
        _ = _updateChecks.ScheduleStartupCheck(SavedUpdatePreferences);
    }

    private UpdateCheckPreferences SavedUpdatePreferences() => new(_savedSettings.AutomaticUpdateChecks, _savedSettings.UpdateChannel);

    public void HandleExternalActivation()
    {
        ShowSettingsWindow();
    }

    private async Task CheckForUpdatesAsync(bool showSettings)
    {
        _dispatcher.VerifyAccess();
        if (_isShuttingDown || _isUpdateSuspended) return;
        if (showSettings)
        {
            ShowSettingsWindow();
            if (_mainWindow is not null) _mainWindow.ViewModel.SelectedPage = DesktopPage.Settings;
        }
        // Never send a network request for an unsaved editor preview.
        await _updateChecks.CheckAsync(_savedSettings.UpdateChannel);
    }

    private Task CheckForUpdatesFromSettingsAsync() => CheckForUpdatesAsync(showSettings: false);

    private void UpdateCheckStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UpdateStatusViewModel.CanCheck))
            _checkUpdatesItem.Enabled = !_isShuttingDown && !_isUpdateSuspended && _updateChecks.Status.CanCheck;
    }

    // The later Settings/tray update UI will call this only after verified staging.
    // No command line, activation message, timer or normal Exit routes here.
    internal async Task<UpdateRestartResult> ApplyStagedUpdateAsync(string operationId, UpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        _dispatcher.VerifyAccess();
        var verifier = UpdateRuntimePolicy.CreateVerifier();
        if (!verifier.IsConfigured) throw new InvalidOperationException("Signed in-app updates are not configured.");
        using var discovery = new GitHubReleaseClient(verifier);
        try
        {
            return await _updateRestart.RunAsync((assertReady, token) => AuthenticatedUpdateHandoff.RunAsync(
                operationId, channel, _savedSettings.StartMinimized, _settingsStore.SettingsPath,
                assertReady, verifier, discovery, token), cancellationToken);
        }
        catch (UnsafeUpdateResumeException)
        {
            // Keep protection alive and settings frozen. Ordinary Exit remains available for repair.
            if (_notifyIcon.ContextMenuStrip is { } menu)
            {
                menu.Enabled = true;
                foreach (ToolStripItem item in menu.Items) item.Enabled = item.Text == "Exit";
            }
            ShowProtectionTip("The update stopped and settings remain paused. Close sensitive windows, exit PrivacyMask, then start it again to recover.", ToolTipIcon.Error);
            throw;
        }
    }

    private bool ConfirmUpdateRestart() => System.Windows.MessageBox.Show(
        "PrivacyMask will restart to install this update. Masks disappear during restart. Close or minimize sensitive application windows before continuing.",
        "Restart to update PrivacyMask", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
        MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private Task SuspendForUpdateAsync()
    {
        _isUpdateSuspended = true;
        if (_notifyIcon.ContextMenuStrip is { } menu) menu.Enabled = false;
        return Task.WhenAll(_updateChecks.SuspendAsync(), _mainWindow?.SuspendForUpdateAsync() ?? Task.CompletedTask);
    }

    private void ResumeAfterUpdate()
    {
        _isUpdateSuspended = false;
        _updateChecks.Resume();
        if (!_isShuttingDown && _notifyIcon.ContextMenuStrip is { } menu) menu.Enabled = true;
        _mainWindow?.ResumeAfterUpdate();
    }

    public ValueTask DisposeAsync()
    {
        _dispatcher.VerifyAccess();
        return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _isShuttingDown = true;
        _updateChecks.Stop();
        var uiDrain = _mainWindow?.BeginShutdownAsync() ?? Task.CompletedTask;
        if (_notifyIcon.ContextMenuStrip is { } menu) menu.Enabled = false;
        await _operations.StopAsync();
        await _updateChecks.DisposeAsync();
        await uiDrain;
        DisposeResources();
    }

    internal void DisposeOnExit()
    {
        // Last-chance synchronous cleanup for OS/forced shutdown only. It is NOT
        // an awaited clean-exit or update-handoff acknowledgement.
        _isShuttingDown = true;
        _ = _operations.StopAsync();
        DisposeResources();
    }

    private void DisposeResources()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isShuttingDown = true;
        _refreshTimer.Stop();
        _updateChecks.Status.PropertyChanged -= UpdateCheckStatusChanged;
        _updateChecks.Stop();
        _updateDiscovery.Dispose();
        _refreshTimer.Tick -= RefreshTimerOnTick;
        _hotkeyManager.HotkeyPressed -= HandleHotkeyPressed;
        _hotkeyManager.Dispose();
        _overlayManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();

        if (_mainWindow is not null)
        {
            _mainWindow.SaveRequested -= SaveSettingsAsync;
            _mainWindow.PreviewRequested -= PreviewSettings;
            _mainWindow.DiscardRequested -= DiscardPreview;
            _mainWindow.GlobalProtectionRequested -= ToggleProtection;
            _mainWindow.GlobalPanicRequested -= ToggleGlobalPanicModeAsync;
            _mainWindow.CheckForUpdatesRequested -= CheckForUpdatesFromSettingsAsync;
            _mainWindow.AllowClose();
            _mainWindow.Close();
        }

    }

    private void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        RefreshOverlays();
    }

    private void RefreshOverlays(bool propagateFailure = false)
    {
        try
        {
            var currentMode = _protectionStateMachine.CurrentMode;
            var settingsVisible = _mainWindow?.IsVisible == true;
            if (currentMode == RuntimeMode.Off && !settingsVisible)
            {
                _overlayManager.HideAll();
                UpdateRefreshInterval(OverlayRefreshCadence.Select(currentMode, hasCandidateWindows: false));
                return;
            }

            var discovery = _windowInspector.Capture(settingsVisible ? null : _windowProcessFilter);
            _trackedWindows.Clear();
            _activeOcclusionHandles.Clear();

            if (currentMode == RuntimeMode.Off)
            {
                UpdateProfileRuntimeStatuses(discovery, currentMode);
                _overlayManager.HideAll();
                UpdateRefreshInterval(OverlayRefreshCadence.Select(currentMode, hasCandidateWindows: false));
                return;
            }

            foreach (var snapshot in discovery.CandidateWindows)
            {
                var trackedWindow = _windowProfileResolver.Resolve(snapshot, _settings.AppProfiles);
                if (trackedWindow is null)
                {
                    continue;
                }

                var profilePanic = _profileProtectionState.IsPanic(trackedWindow.Profile.ProfileId);
                if (currentMode != RuntimeMode.Panic
                    && !profilePanic
                    && _profileProtectionState.IsPaused(trackedWindow.Profile.ProfileId))
                {
                    continue;
                }

                if (currentMode != RuntimeMode.Panic
                    && !profilePanic
                    && trackedWindow.Profile.StartupMode == AppActivationMode.FocusAware
                    && !snapshot.IsForeground)
                {
                    continue;
                }

                var occludingBounds = GetOccludingBoundsBuffer(snapshot.Handle);
                ResolveOccludingBounds(snapshot, discovery.Windows, occludingBounds);
                trackedWindow.OccludingBounds = occludingBounds;
                _activeOcclusionHandles.Add(snapshot.Handle);
                _trackedWindows.Add(trackedWindow);
            }

            PruneOccludingBoundsBuffers();
            UpdateProfileRuntimeStatuses(discovery, currentMode);

            var cursorPoint = GetCursorPoint();
            var temporaryRevealProfileId = GetTemporaryRevealProfileId();
            _overlayManager.Update(
                _trackedWindows,
                currentMode,
                _profileProtectionState,
                temporaryRevealProfileId,
                cursorPoint);
            UpdateRefreshInterval(OverlayRefreshCadence.Select(
                currentMode,
                settingsVisible ? _trackedWindows.Count > 0 : discovery.CandidateWindows.Count > 0));
        }
        catch
        {
            _overlayManager.HideAll();
            UpdateRefreshInterval(OverlayRefreshCadence.IdleInterval);
            if (propagateFailure) throw;
        }
    }

    private async Task ShowOnboardingAsync()
    {
        var onboarding = new OnboardingWindow(_settings.LaunchAtLogin, _settings.StartMinimized);
        var completed = onboarding.ShowDialog();
        if (completed == true)
        {
            _settings.OnboardingCompleted = true;
            _settings.LaunchAtLogin = onboarding.LaunchAtLogin;
            _settings.StartMinimized = onboarding.StartMinimized;
            _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
            await _settingsStore.SaveAsync(_settings);
            _savedSettings = AppSettingsCloner.Clone(_settings);
            UpdateTrayState();
            ShowSettingsWindow();
        }
    }

    private void ShowSettingsWindow()
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        if (_mainWindow is null)
        {
            var viewModel = CreateSettingsViewModel();
            _mainWindow = new MainWindow(viewModel, _applicationCatalog);
            _mainWindow.SaveRequested += SaveSettingsAsync;
            _mainWindow.PreviewRequested += PreviewSettings;
            _mainWindow.DiscardRequested += DiscardPreview;
            _mainWindow.GlobalProtectionRequested += ToggleProtection;
            _mainWindow.GlobalPanicRequested += ToggleGlobalPanicModeAsync;
            _mainWindow.CheckForUpdatesRequested += CheckForUpdatesFromSettingsAsync;
        }
        else if (!_mainWindow.IsVisible)
        {
            _mainWindow.ReplaceViewModel(CreateSettingsViewModel());
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        RefreshOverlays();
    }

    private void UpdateProfileRuntimeStatuses(WindowDiscoveryResult discovery, RuntimeMode currentMode)
    {
        if (_mainWindow?.IsVisible != true)
        {
            return;
        }

        IReadOnlySet<string> runningProcessNames = discovery.CandidateWindows
            .Select(snapshot => snapshot.ProcessName)
            .Where(processName => !string.IsNullOrWhiteSpace(processName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> protectedProfileIds = _trackedWindows
            .Select(window => window.Profile.ProfileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var profileViewModel in _mainWindow.ViewModel.AppProfiles)
        {
            var profile = _settings.AppProfiles.FirstOrDefault(candidate =>
                string.Equals(candidate.ProfileId, profileViewModel.ProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                continue;
            }

            var executableExists = string.IsNullOrWhiteSpace(profile.ExecutablePath)
                || File.Exists(profile.ExecutablePath);
            var status = ProfileRuntimeStatusResolver.Resolve(
                profile,
                runningProcessNames,
                protectedProfileIds,
                currentMode,
                _profileProtectionState.IsPaused(profile.ProfileId),
                _profileProtectionState.IsPanic(profile.ProfileId),
                executableExists);
            profileViewModel.UpdateRuntimeStatus(status);
        }
    }

    private async Task SaveSettingsAsync(AppSettings updatedSettings)
    {
        // The editor captured this model before dispatch. Never queue a stale
        // whole-file snapshot behind a shortcut/startup/PIN mutation.
        if (!await _operations.TryRunAsync(() => SaveSettingsCoreAsync(updatedSettings), requireIdle: true))
            throw new InvalidOperationException(_operations.IsStopping
                ? "PrivacyMask is closing. These changes were not saved."
                : "Another PrivacyMask action is still finishing. Review your changes and try Save again.");
    }

    private async Task SaveSettingsCoreAsync(AppSettings updatedSettings)
    {
        updatedSettings.CurrentMode = RuntimeMode.Standard;
        updatedSettings.OnboardingCompleted = true;
        var nextSettings = _defaultSettingsFactory.MergeWithDefaults(updatedSettings);
        try
        {
            _startupRegistrationService.SetEnabled(nextSettings.LaunchAtLogin, nextSettings.StartMinimized);
            await _settingsStore.SaveAsync(nextSettings);
        }
        catch
        {
            TryRestoreStartupRegistration();
            throw;
        }

        _settings = nextSettings;
        _savedSettings = AppSettingsCloner.Clone(nextSettings);
        _updateChecks.PreferencesChanged();
        RebuildWindowProcessFilter();
        RegisterHotkeys();
        _mainWindow?.ReplaceViewModel(CreateSettingsViewModel());
        RefreshOverlays();
        UpdateTrayState();
    }

    private void PreviewSettings(AppSettings previewSettings)
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        previewSettings.CurrentMode = RuntimeMode.Standard;
        previewSettings.OnboardingCompleted = true;
        _settings = _defaultSettingsFactory.MergeWithDefaults(previewSettings);
        RebuildWindowProcessFilter();
        RefreshOverlays();
    }

    private void DiscardPreview()
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        _settings = AppSettingsCloner.Clone(_savedSettings);
        _mainWindow?.ReplaceViewModel(CreateSettingsViewModel());
        RebuildWindowProcessFilter();
        RefreshOverlays();
    }

    private SettingsViewModel CreateSettingsViewModel()
    {
        return SettingsViewModel.FromModel(
            _settings,
            _protectionStateMachine.CurrentMode,
            _settingsStore.SettingsPath,
            _unavailableHotkeyActions,
            _updateChecks.Status);
    }

    private void RebuildWindowProcessFilter()
    {
        _windowProcessFilter = new WindowProcessFilter(_settings.AppProfiles);
        _profileProtectionState.Prune(_settings.AppProfiles.Select(profile => profile.ProfileId));
    }

    private void RegisterHotkeys()
    {
        var result = _hotkeyManager.RegisterBindings(_settings.GlobalHotkeys);
        _unavailableHotkeyActions = result.Failures
            .Select(failure => failure.Action)
            .ToHashSet();
        if (!result.HasFailures)
        {
            return;
        }

        var unavailableNames = string.Join(", ", result.Failures.Select(failure => failure.DisplayName));
        _notifyIcon.ShowBalloonTip(
            timeout: 5000,
            tipTitle: "PrivacyMask shortcut unavailable",
            tipText: $"Could not register: {unavailableNames}. Another app may already be using the shortcut.",
            tipIcon: ToolTipIcon.Warning);
    }

    private void HandleHotkeyPressed(HotkeyAction action)
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        switch (action)
        {
            case HotkeyAction.ToggleProtection:
                _ = ObserveShellActionAsync(ToggleOrAddForegroundProfileProtectionAsync());
                break;
            case HotkeyAction.PanicHideAll:
                _ = ObserveShellActionAsync(ToggleForegroundProfilePanicAsync());
                break;
            case HotkeyAction.OpenSettings:
                ShowSettingsWindow();
                break;
            case HotkeyAction.IncreaseBlurStrength:
                _ = ObserveShellActionAsync(AdjustForegroundBlurStrengthAsync(increase: true));
                break;
            case HotkeyAction.DecreaseBlurStrength:
                _ = ObserveShellActionAsync(AdjustForegroundBlurStrengthAsync(increase: false));
                break;
        }
    }

    private async Task AdjustForegroundBlurStrengthAsync(bool increase)
    {
        if (_operations.IsStopping) return;
        var trackedWindow = TryResolveForegroundProfile();
        if (trackedWindow is null)
        {
            ShowBlurStrengthTip(
                "Focus a protected app first, then press Ctrl + Alt + Shift + ↑ or ↓.",
                ToolTipIcon.Info);
            return;
        }

        // Capture focus before queuing: changing focus must not redirect a
        // previously accepted shortcut to a different application.
        var profileId = trackedWindow.Profile.ProfileId;
        if (!await _operations.TryRunAsync(() => AdjustBlurStrengthCoreAsync(profileId, increase))
            && !_operations.IsStopping)
            ShowBlurStrengthTip("Several actions are still finishing. Try the shortcut again shortly.", ToolTipIcon.Info);
    }

    private async Task AdjustBlurStrengthCoreAsync(string profileId, bool increase)
    {
        var nextSettings = AppSettingsCloner.Clone(_savedSettings);
        var nextProfile = nextSettings.AppProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
            && profile.Enabled);
        if (nextProfile is null)
        {
            ShowBlurStrengthTip("The focused app profile is disabled.", ToolTipIcon.Info);
            return;
        }

        var changed = increase
            ? BlurStrengthAdjustment.TryIncrease(nextProfile, out var adjustedStrength)
            : BlurStrengthAdjustment.TryDecrease(nextProfile, out adjustedStrength);
        if (!changed)
        {
            var endpoint = increase ? "100%" : "0%";
            ShowBlurStrengthTip($"{nextProfile.DisplayName} blur is already at {endpoint}.", ToolTipIcon.Info);
            return;
        }

        ApplyBlurStrength(profileId, adjustedStrength);
        RefreshOverlays();
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
            _savedSettings = AppSettingsCloner.Clone(nextSettings);
            _mainWindow?.ViewModel.SetSavedBaseline(_savedSettings);
        }
        catch (Exception exception)
        {
            var savedProfile = _savedSettings.AppProfiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
            if (savedProfile is not null)
            {
                ApplyBlurStrength(profileId, savedProfile.MaskIntensity);
                RefreshOverlays();
            }

            ShowBlurStrengthTip(
                $"The blur changed temporarily but could not be saved. {exception.Message}",
                ToolTipIcon.Error);
        }
    }

    private void ApplyBlurStrength(string profileId, double strength)
    {
        var runtimeProfile = _settings.AppProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        if (runtimeProfile is not null)
        {
            runtimeProfile.MaskIntensity = strength;
        }

        var visibleProfile = _mainWindow?.ViewModel.AppProfiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        if (visibleProfile is not null)
        {
            visibleProfile.MaskIntensity = strength;
        }
    }

    private void ShowBlurStrengthTip(string message, ToolTipIcon icon)
    {
        _notifyIcon.ShowBalloonTip(
            timeout: 3000,
            tipTitle: "PrivacyMask blur strength",
            tipText: message,
            tipIcon: icon);
    }

    private void ToggleProtection()
    {
        if (_isShuttingDown || _isUpdateSuspended) return;
        _protectionStateMachine.ToggleProtection();
        UpdateTrayState();
        RefreshOverlays();
    }

    private async Task ToggleOrAddForegroundProfileProtectionAsync()
    {
        if (_operations.IsStopping || !await _profileProtectionOperationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var foreground = _windowInspector.TryGetWindow(NativeMethods.GetForegroundWindow());
            if (!await _operations.TryRunAsync(() => ToggleOrAddProfileProtectionCoreAsync(foreground))
                && !_operations.IsStopping)
                ShowProtectionTip("Several actions are still finishing. Try the shortcut again shortly.", ToolTipIcon.Info);
        }
        finally
        {
            _profileProtectionOperationGate.Release();
        }
    }

    private async Task ToggleOrAddProfileProtectionCoreAsync(WindowSnapshot? foregroundSnapshot)
    {
        if (_protectionStateMachine.CurrentMode == RuntimeMode.Panic)
        {
            ShowProtectionTip("Disable panic mode before changing protection for one app.", ToolTipIcon.Info);
            return;
        }

        if (_protectionStateMachine.CurrentMode == RuntimeMode.Off)
        {
            ShowProtectionTip("Resume global protection from the tray before changing one app.", ToolTipIcon.Info);
            return;
        }

        if (foregroundSnapshot is null)
        {
            ShowProtectionTip("Focus a visible desktop application, then press Ctrl + Shift + P.", ToolTipIcon.Info);
            return;
        }

        var currentSnapshot = _windowInspector.TryGetWindow(foregroundSnapshot.Handle);
        if (currentSnapshot is null || !string.Equals(currentSnapshot.ProcessName,
                foregroundSnapshot.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            ShowProtectionTip("The selected application window is no longer available. Focus it and try again.", ToolTipIcon.Info);
            return;
        }
        foregroundSnapshot = currentSnapshot;

        var trackedWindow = _windowProfileResolver.Resolve(foregroundSnapshot, _settings.AppProfiles);
        if (trackedWindow is not null)
        {
            ToggleProfileProtection(trackedWindow);
            return;
        }

        var disabledWindow = _windowProfileResolver.Resolve(
            foregroundSnapshot,
            _settings.AppProfiles,
            includeDisabled: true);
        if (disabledWindow is not null)
        {
            await EnableExistingProfileAsync(disabledWindow.Profile);
            return;
        }

        var configuredProcessNames = _settings.AppProfiles
            .Concat(_savedSettings.AppProfiles)
            .SelectMany(profile => profile.WindowMatchers)
            .SelectMany(matcher => matcher.ProcessNames)
            .Where(processName => !string.IsNullOrWhiteSpace(processName))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (!_applicationCatalog.TryCreateFromForegroundWindow(
                foregroundSnapshot,
                configuredProcessNames,
                out var candidate,
                out var failureReason)
            || candidate is null)
        {
            ShowProtectionTip(failureReason, ToolTipIcon.Warning);
            return;
        }

        await AddAndProtectForegroundApplicationAsync(candidate);
    }

    private void ToggleProfileProtection(TrackedWindow trackedWindow)
    {
        if (_profileProtectionState.IsPanic(trackedWindow.Profile.ProfileId))
        {
            ShowProtectionTip(
                $"Disable the panic mask for {trackedWindow.Profile.DisplayName} before pausing its protection.",
                ToolTipIcon.Info);
            return;
        }

        var paused = _profileProtectionState.TogglePause(trackedWindow.Profile.ProfileId);
        RefreshOverlays();
        ShowProtectionTip(
            $"{trackedWindow.Profile.DisplayName} protection {(paused ? "paused" : "resumed")}. Other apps are unchanged.",
            ToolTipIcon.Info);
    }

    private async Task EnableExistingProfileAsync(AppProfile profile)
    {
        var nextSettings = AppSettingsCloner.Clone(_savedSettings);
        var savedProfile = nextSettings.AppProfiles.FirstOrDefault(candidate =>
            string.Equals(candidate.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (savedProfile is null)
        {
            ShowProtectionTip(
                $"Save the pending settings for {profile.DisplayName}, then use the shortcut again.",
                ToolTipIcon.Info);
            return;
        }

        savedProfile.Enabled = true;
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
        }
        catch (Exception exception)
        {
            ShowProtectionTip($"{profile.DisplayName} could not be enabled. {exception.Message}", ToolTipIcon.Error);
            return;
        }

        _savedSettings = AppSettingsCloner.Clone(nextSettings);
        profile.Enabled = true;
        var visibleProfile = _mainWindow?.ViewModel.AppProfiles.FirstOrDefault(candidate =>
            string.Equals(candidate.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (visibleProfile is not null)
        {
            visibleProfile.Enabled = true;
        }

        _mainWindow?.ViewModel.SetSavedBaseline(_savedSettings);

        if (_profileProtectionState.IsPaused(profile.ProfileId))
        {
            _profileProtectionState.TogglePause(profile.ProfileId);
        }

        RebuildWindowProcessFilter();
        RefreshOverlays();
        ShowProtectionTip(
            $"{profile.DisplayName} protection is enabled and its mask is active.",
            ToolTipIcon.Info);
    }

    private async Task AddAndProtectForegroundApplicationAsync(DesktopApplicationCandidate candidate)
    {
        var nextSettings = AppSettingsCloner.Clone(_savedSettings);
        var profile = CustomAppProfileFactory.Create(
            candidate.DisplayName,
            candidate.ProcessName,
            executablePath: candidate.ExecutablePath);
        nextSettings.AppProfiles.Add(profile);
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
        }
        catch (Exception exception)
        {
            ShowProtectionTip($"{candidate.DisplayName} could not be added. {exception.Message}", ToolTipIcon.Error);
            return;
        }

        _savedSettings = AppSettingsCloner.Clone(nextSettings);
        var runtimeProfile = CustomAppProfileFactory.Create(
            profile.DisplayName,
            candidate.ProcessName,
            profile.ProfileId,
            candidate.ExecutablePath);
        _settings.AppProfiles.Add(runtimeProfile);
        if (_mainWindow?.ViewModel.AppProfiles.Any(existing =>
                string.Equals(existing.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase)) == false)
        {
            _mainWindow.ViewModel.AppProfiles.Add(new AppProfileViewModel(runtimeProfile));
        }

        _mainWindow?.ViewModel.SetSavedBaseline(_savedSettings);

        RebuildWindowProcessFilter();
        RefreshOverlays();
        ShowProtectionTip(
            $"{profile.DisplayName} was added with the default blur settings and is now protected.",
            ToolTipIcon.Info);
    }

    private void ShowProtectionTip(string message, ToolTipIcon icon)
    {
        _notifyIcon.ShowBalloonTip(
            timeout: 3000,
            tipTitle: "PrivacyMask protection",
            tipText: message,
            tipIcon: icon);
    }

    private Task ToggleGlobalPanicModeAsync() => RunPanicOperationAsync(ToggleGlobalPanicModeCoreAsync);

    private async Task RunPanicOperationAsync(Func<Task> operation)
    {
        // Hold this single-flight gate from admission, not execution, so key
        // repeat cannot enqueue a second panic toggle while a dialog/save waits.
        if (_operations.IsStopping || !await _panicOperationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!await _operations.TryRunAsync(operation) && !_operations.IsStopping)
                ShowProtectionTip("Several actions are still finishing. Try the panic action again shortly.", ToolTipIcon.Info);
        }
        finally
        {
            _panicOperationGate.Release();
        }
    }

    private async Task ToggleGlobalPanicModeCoreAsync()
    {
        if (!await EnsurePanicPinConfiguredAsync())
        {
            return;
        }

        if (_protectionStateMachine.CurrentMode == RuntimeMode.Panic
            && !RequestPanicUnlock("all protected applications"))
        {
            return;
        }

        _protectionStateMachine.TogglePanic();
        UpdateTrayState();
        RefreshOverlays();
    }

    private Task ToggleForegroundProfilePanicAsync()
    {
        if (_operations.IsStopping) return Task.CompletedTask;
        var profileId = TryResolveForegroundProfile()?.Profile.ProfileId;
        return RunPanicOperationAsync(() => ToggleProfilePanicCoreAsync(profileId));
    }

    private async Task ToggleProfilePanicCoreAsync(string? profileId)
    {
        if (_protectionStateMachine.CurrentMode == RuntimeMode.Panic)
        {
            ShowProtectionTip("Disable the global panic mask from the tray before changing one app.", ToolTipIcon.Info);
            return;
        }

        if (_protectionStateMachine.CurrentMode == RuntimeMode.Off)
        {
            ShowProtectionTip("Resume global protection from the tray before changing one app.", ToolTipIcon.Info);
            return;
        }

        var profile = _settings.AppProfiles.FirstOrDefault(candidate => candidate.Enabled
            && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            ShowProtectionTip("Focus a protected app first, then press Ctrl + Shift + H.", ToolTipIcon.Info);
            return;
        }

        if (!await EnsurePanicPinConfiguredAsync())
        {
            return;
        }

        var isCurrentlyPanic = _profileProtectionState.IsPanic(profile.ProfileId);
        if (isCurrentlyPanic && !RequestPanicUnlock(profile.DisplayName))
        {
            return;
        }

        var panicEnabled = _profileProtectionState.TogglePanic(profile.ProfileId);
        RefreshOverlays();
        ShowProtectionTip(
            $"{profile.DisplayName} panic mask {(panicEnabled ? "enabled" : "disabled")}. Other apps are unchanged.",
            ToolTipIcon.Info);
    }

    private async Task<bool> EnsurePanicPinConfiguredAsync()
    {
        if (PanicPinService.IsCredentialValid(_savedSettings.PanicPin))
        {
            return true;
        }

        var dialog = new PanicPinSetupWindow(currentCredential: null);
        SetDialogOwner(dialog);
        if (dialog.ShowDialog() != true || dialog.CreatedCredential is null)
        {
            return false;
        }

        var nextSettings = AppSettingsCloner.Clone(_savedSettings);
        nextSettings.PanicPin = PanicPinService.Clone(dialog.CreatedCredential);
        try
        {
            await _settingsStore.SaveAsync(nextSettings);
        }
        catch (Exception exception)
        {
            ShowProtectionTip($"The PIN could not be saved. {exception.Message}", ToolTipIcon.Error);
            return false;
        }

        _savedSettings = AppSettingsCloner.Clone(nextSettings);
        _settings.PanicPin = PanicPinService.Clone(nextSettings.PanicPin);
        _mainWindow?.ViewModel.SetPanicPin(nextSettings.PanicPin!);
        _mainWindow?.ViewModel.SetSavedBaseline(_savedSettings);
        return true;
    }

    private bool RequestPanicUnlock(string targetName)
    {
        var credential = _savedSettings.PanicPin;
        if (!PanicPinService.IsCredentialValid(credential))
        {
            ShowProtectionTip("Configure a valid panic unlock PIN before disabling panic mode.", ToolTipIcon.Error);
            return false;
        }

        var dialog = new PanicPinUnlockWindow(credential!, targetName);
        SetDialogOwner(dialog);
        return dialog.ShowDialog() == true;
    }

    private void SetDialogOwner(Window dialog)
    {
        if (_mainWindow?.IsVisible == true)
        {
            dialog.Owner = _mainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    private async Task ToggleLaunchAtLoginAsync()
    {
        if (!await _operations.TryRunAsync(ToggleLaunchAtLoginCoreAsync, requireIdle: true)
            && !_operations.IsStopping)
        {
            // CheckOnClick already changed the menu check; restore saved state.
            UpdateTrayState();
            ShowProtectionTip("Another action is still finishing. Try changing startup again shortly.", ToolTipIcon.Info);
        }
    }

    private async Task ToggleLaunchAtLoginCoreAsync()
    {
        var nextSettings = AppSettingsCloner.Clone(_savedSettings);
        nextSettings.LaunchAtLogin = !_savedSettings.LaunchAtLogin;
        try
        {
            _startupRegistrationService.SetEnabled(nextSettings.LaunchAtLogin, nextSettings.StartMinimized);
            await _settingsStore.SaveAsync(nextSettings);
            _savedSettings = AppSettingsCloner.Clone(nextSettings);
            _settings.LaunchAtLogin = nextSettings.LaunchAtLogin;
            _settings.StartMinimized = nextSettings.StartMinimized;
            if (_mainWindow?.IsVisible == true)
            {
                _mainWindow.ViewModel.LaunchAtLogin = nextSettings.LaunchAtLogin;
            }
        }
        catch (Exception exception)
        {
            TryRestoreStartupRegistration();
            _notifyIcon.ShowBalloonTip(
                timeout: 5000,
                tipTitle: "PrivacyMask could not update startup",
                tipText: exception.Message,
                tipIcon: ToolTipIcon.Error);
        }

        UpdateTrayState();
    }

    private void TryRestoreStartupRegistration()
    {
        try
        {
            _startupRegistrationService.SetEnabled(_savedSettings.LaunchAtLogin, _savedSettings.StartMinimized);
        }
        catch
        {
            // Preserve the original operation error; the next startup will reconcile this state again.
        }
    }

    private void UpdateTrayState()
    {
        var currentMode = _protectionStateMachine.CurrentMode;
        _mainWindow?.ViewModel.UpdateRuntimeMode(currentMode);
        _mainWindow?.ViewModel.SetSavedBaseline(_savedSettings);
        _modeItem.Text = currentMode switch
        {
            RuntimeMode.Standard => "Protection: Active",
            RuntimeMode.Off => "Protection: Paused",
            RuntimeMode.Panic => "Protection: Panic mask",
            _ => "Protection: Active",
        };
        _launchAtLoginItem.Checked = _savedSettings.LaunchAtLogin;
        _toggleProtectionItem.Text = currentMode == RuntimeMode.Off ? "Resume protection" : "Pause protection";
        _toggleProtectionItem.Enabled = currentMode != RuntimeMode.Panic;
        _panicItem.Checked = currentMode == RuntimeMode.Panic;
        _panicItem.Text = currentMode == RuntimeMode.Panic ? "Disable panic mask" : "Panic mask all";
        _notifyIcon.Text = currentMode switch
        {
            RuntimeMode.Standard => "PrivacyMask - protecting supported windows",
            RuntimeMode.Off => "PrivacyMask - protection paused",
            RuntimeMode.Panic => "PrivacyMask - panic mask active",
            RuntimeMode.TemporaryReveal => "PrivacyMask - temporary reveal",
            _ => "PrivacyMask",
        };
    }

    private string? GetTemporaryRevealProfileId()
    {
        if (!_protectionStateMachine.IsTemporaryRevealAllowed)
        {
            _temporaryRevealGesture.Reset();
            return null;
        }

        var holdBinding = _settings.GlobalHotkeys.FirstOrDefault(binding => binding.Action == HotkeyAction.TemporaryRevealHold && binding.Enabled);
        if (holdBinding is null)
        {
            _temporaryRevealGesture.Reset();
            return null;
        }

        var gestureKeyDown = IsHoldBindingDown(holdBinding);
        var anotherKeyDown = gestureKeyDown && _windowInspector.IsAnyOtherKeyboardKeyDown(holdBinding);
        if (!_temporaryRevealGesture.Update(gestureKeyDown, anotherKeyDown, DateTimeOffset.UtcNow))
        {
            return null;
        }

        var trackedWindow = TryResolveForegroundProfile();
        return trackedWindow is not null && !_profileProtectionState.IsPanic(trackedWindow.Profile.ProfileId)
            ? trackedWindow.Profile.ProfileId
            : null;
    }

    private bool IsHoldBindingDown(HotkeyBinding binding)
    {
        return _windowInspector.IsKeyDown(binding.VirtualKey)
            && (!binding.Modifiers.HasFlag(HotkeyModifiers.Control) || _windowInspector.IsKeyDown(0x11))
            && (!binding.Modifiers.HasFlag(HotkeyModifiers.Shift) || _windowInspector.IsKeyDown(0x10))
            && (!binding.Modifiers.HasFlag(HotkeyModifiers.Alt) || _windowInspector.IsKeyDown(0x12))
            && (!binding.Modifiers.HasFlag(HotkeyModifiers.Windows)
                || _windowInspector.IsKeyDown(0x5B)
                || _windowInspector.IsKeyDown(0x5C));
    }

    private TrackedWindow? TryResolveForegroundProfile()
    {
        var foregroundSnapshot = _windowInspector.TryGetWindow(NativeMethods.GetForegroundWindow());
        return foregroundSnapshot is null
            ? null
            : _windowProfileResolver.Resolve(foregroundSnapshot, _settings.AppProfiles);
    }

    private static Point GetCursorPoint()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : new Point();
    }

    private List<ScreenRect> GetOccludingBoundsBuffer(nint handle)
    {
        if (!_occludingBoundsByWindow.TryGetValue(handle, out var bounds))
        {
            bounds = [];
            _occludingBoundsByWindow[handle] = bounds;
        }

        bounds.Clear();
        return bounds;
    }

    private void PruneOccludingBoundsBuffers()
    {
        _staleOcclusionHandles.Clear();
        foreach (var handle in _occludingBoundsByWindow.Keys)
        {
            if (!_activeOcclusionHandles.Contains(handle))
            {
                _staleOcclusionHandles.Add(handle);
            }
        }

        foreach (var handle in _staleOcclusionHandles)
        {
            _occludingBoundsByWindow.Remove(handle);
        }
    }

    private void UpdateRefreshInterval(TimeSpan interval)
    {
        if (_refreshTimer.Interval != interval)
        {
            _refreshTimer.Interval = interval;
        }
    }

    private static void ResolveOccludingBounds(
        WindowSnapshot target,
        IReadOnlyList<DesktopWindowSnapshot> windows,
        List<ScreenRect> destination)
    {
        foreach (var candidate in windows)
        {
            if (candidate.Handle == target.Handle
                || !candidate.IsVisible
                || candidate.IsMinimized
                || candidate.ZOrderIndex >= target.ZOrderIndex
                || !candidate.Bounds.IntersectsWith(target.Bounds))
            {
                continue;
            }

            var intersection = candidate.Bounds.Intersect(target.Bounds);
            if (!intersection.IsEmpty)
            {
                destination.Add(intersection);
            }
        }
    }

    private async Task ObserveShellActionAsync(Task action)
    {
        try { await action; }
        catch (Exception)
        {
            if (!_isDisposed && !_isShuttingDown)
                ShowProtectionTip("The action could not finish. If PrivacyMask cannot close normally, close sensitive windows before exiting Windows.", ToolTipIcon.Error);
        }
    }
}
