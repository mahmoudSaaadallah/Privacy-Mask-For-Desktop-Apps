using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using PrivacyMask.App.ViewModels;
using PrivacyMask.App.Windows;
using PrivacyMask.Core.Models;
using PrivacyMask.Core.Services;
using PrivacyMask.Windows.Adapters;
using PrivacyMask.Windows.Interop;
using PrivacyMask.Windows.Models;
using PrivacyMask.Windows.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Services;

public sealed class ShellCoordinator : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly string[] _args;
    private readonly DefaultSettingsFactory _defaultSettingsFactory;
    private readonly ProtectionStateMachine _protectionStateMachine;
    private readonly JsonSettingsStore _settingsStore;
    private readonly DesktopWindowInspector _windowInspector;
    private readonly WindowProfileResolver _windowProfileResolver;
    private readonly OverlayManager _overlayManager;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _modeItem;
    private readonly ToolStripMenuItem _launchAtLoginItem;
    private readonly ToolStripMenuItem _toggleProtectionItem;
    private readonly ToolStripMenuItem _panicItem;
    private readonly List<TrackedWindow> _trackedWindows = [];
    private readonly Dictionary<nint, List<ScreenRect>> _occludingBoundsByWindow = [];
    private readonly HashSet<nint> _activeOcclusionHandles = [];
    private readonly List<nint> _staleOcclusionHandles = [];

    private AppSettings _settings = new();
    private WindowProcessFilter _windowProcessFilter = new([]);
    private IReadOnlySet<HotkeyAction> _unavailableHotkeyActions = new HashSet<HotkeyAction>();
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;
    private bool _isDisposed;

    public ShellCoordinator(Dispatcher dispatcher, string[] args)
    {
        _dispatcher = dispatcher;
        _args = args;
        _defaultSettingsFactory = new DefaultSettingsFactory();
        _protectionStateMachine = new ProtectionStateMachine();
        _settingsStore = new JsonSettingsStore(_defaultSettingsFactory);
        _windowInspector = new DesktopWindowInspector();
        _windowProfileResolver = new WindowProfileResolver(
        [
            new WhatsAppWindowAdapter(),
            new TelegramWindowAdapter(),
        ]);
        _overlayManager = new OverlayManager();
        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.HotkeyPressed += HandleHotkeyPressed;

        var executablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PrivacyMask.App.exe");
        _startupRegistrationService = new StartupRegistrationService("PrivacyMask.Desktop", executablePath);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = OverlayRefreshCadence.IdleInterval,
        };
        _refreshTimer.Tick += RefreshTimerOnTick;

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "PrivacyMask",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        _modeItem = new ToolStripMenuItem("Protection: Active")
        {
            Enabled = false,
        };
        _toggleProtectionItem = new ToolStripMenuItem("Toggle protection", null, (_, _) => ToggleProtection());
        _panicItem = new ToolStripMenuItem("Panic mask all", null, (_, _) => TogglePanicMode());
        _launchAtLoginItem = new ToolStripMenuItem("Launch at sign in", null, async (_, _) => await ToggleLaunchAtLoginAsync())
        {
            CheckOnClick = true,
        };

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            _modeItem,
            new ToolStripSeparator(),
            _toggleProtectionItem,
            _panicItem,
            new ToolStripMenuItem("Open settings", null, (_, _) => ShowSettingsWindow()),
            _launchAtLoginItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, async (_, _) => await ShutdownAsync()),
        ]);
    }

    public async Task StartAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        RebuildWindowProcessFilter();
        var registrationState = _startupRegistrationService.GetState();
        if (registrationState.Enabled)
        {
            _settings.LaunchAtLogin = true;
            _settings.StartMinimized = registrationState.StartMinimized;
        }

        _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);

        RegisterHotkeys();
        UpdateTrayState();
        RefreshOverlays();
        _refreshTimer.Start();

        if (!_settings.OnboardingCompleted)
        {
            await ShowOnboardingAsync();
            return;
        }

        var shouldStartMinimized = _args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!shouldStartMinimized)
        {
            ShowSettingsWindow();
        }
    }

    public void HandleExternalActivation()
    {
        ShowSettingsWindow();
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        _isShuttingDown = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimerOnTick;
        _hotkeyManager.HotkeyPressed -= HandleHotkeyPressed;
        _hotkeyManager.Dispose();
        _overlayManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose();
            _mainWindow.Close();
        }

        return ValueTask.CompletedTask;
    }

    private void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        RefreshOverlays();
    }

    private void RefreshOverlays()
    {
        try
        {
            var currentMode = _protectionStateMachine.CurrentMode;
            if (currentMode == RuntimeMode.Off)
            {
                _overlayManager.HideAll();
                UpdateRefreshInterval(OverlayRefreshCadence.Select(currentMode, hasCandidateWindows: false));
                return;
            }

            var discovery = _windowInspector.Capture(_windowProcessFilter);
            _trackedWindows.Clear();
            _activeOcclusionHandles.Clear();

            foreach (var snapshot in discovery.CandidateWindows)
            {
                var trackedWindow = _windowProfileResolver.Resolve(snapshot, _settings.AppProfiles);
                if (trackedWindow is null)
                {
                    continue;
                }

                if (currentMode != RuntimeMode.Panic
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

            var cursorPoint = GetCursorPoint();
            var temporaryRevealHeld = EvaluateTemporaryReveal();
            _overlayManager.Update(_trackedWindows, currentMode, temporaryRevealHeld, cursorPoint);
            UpdateRefreshInterval(OverlayRefreshCadence.Select(
                currentMode,
                discovery.CandidateWindows.Count > 0));
        }
        catch
        {
            _overlayManager.HideAll();
            UpdateRefreshInterval(OverlayRefreshCadence.IdleInterval);
        }
    }

    private async Task ShowOnboardingAsync()
    {
        var onboarding = new OnboardingWindow(_settings.StartMinimized);
        var completed = onboarding.ShowDialog();
        if (completed == true)
        {
            _settings.OnboardingCompleted = true;
            _settings.LaunchAtLogin = onboarding.LaunchAtLogin;
            _settings.StartMinimized = onboarding.StartMinimized;
            _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
            await _settingsStore.SaveAsync(_settings);
            UpdateTrayState();
            ShowSettingsWindow();
        }
    }

    private void ShowSettingsWindow()
    {
        var viewModel = SettingsViewModel.FromModel(
            _settings,
            _protectionStateMachine.CurrentMode,
            _settingsStore.SettingsPath,
            _unavailableHotkeyActions);
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(viewModel);
            _mainWindow.SaveRequested += SaveSettingsAsync;
            _mainWindow.PreviewRequested += PreviewSettings;
        }
        else
        {
            _mainWindow.ReplaceViewModel(viewModel);
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task SaveSettingsAsync(AppSettings updatedSettings)
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
        RebuildWindowProcessFilter();
        RegisterHotkeys();
        UpdateTrayState();
    }

    private void PreviewSettings(AppSettings previewSettings)
    {
        previewSettings.CurrentMode = RuntimeMode.Standard;
        previewSettings.OnboardingCompleted = true;
        _settings = _defaultSettingsFactory.MergeWithDefaults(previewSettings);
        RebuildWindowProcessFilter();
    }

    private void RebuildWindowProcessFilter()
    {
        _windowProcessFilter = new WindowProcessFilter(_settings.AppProfiles);
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
        switch (action)
        {
            case HotkeyAction.ToggleProtection:
                ToggleProtection();
                break;
            case HotkeyAction.PanicHideAll:
                TogglePanicMode();
                break;
            case HotkeyAction.OpenSettings:
                ShowSettingsWindow();
                break;
        }
    }

    private void ToggleProtection()
    {
        _protectionStateMachine.ToggleProtection();
        UpdateTrayState();
        RefreshOverlays();
    }

    private void TogglePanicMode()
    {
        _protectionStateMachine.TogglePanic();
        UpdateTrayState();
        RefreshOverlays();
    }

    private async Task ToggleLaunchAtLoginAsync()
    {
        var previousValue = _settings.LaunchAtLogin;
        _settings.LaunchAtLogin = !previousValue;
        try
        {
            _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            _settings.LaunchAtLogin = previousValue;
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
            _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
        }
        catch
        {
            // Preserve the original operation error; the next startup will reconcile this state again.
        }
    }

    private void UpdateTrayState()
    {
        var currentMode = _protectionStateMachine.CurrentMode;
        _modeItem.Text = currentMode switch
        {
            RuntimeMode.Standard => "Protection: Active",
            RuntimeMode.Off => "Protection: Paused",
            RuntimeMode.Panic => "Protection: Panic mask",
            _ => "Protection: Active",
        };
        _launchAtLoginItem.Checked = _settings.LaunchAtLogin;
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

    private bool EvaluateTemporaryReveal()
    {
        if (!_protectionStateMachine.IsTemporaryRevealAllowed)
        {
            return false;
        }

        var holdBinding = _settings.GlobalHotkeys.FirstOrDefault(binding => binding.Action == HotkeyAction.TemporaryRevealHold && binding.Enabled);
        if (holdBinding is null)
        {
            return false;
        }

        if (!_windowInspector.IsKeyDown(holdBinding.VirtualKey))
        {
            return false;
        }

        if (holdBinding.Modifiers.HasFlag(HotkeyModifiers.Control) && !_windowInspector.IsKeyDown(0x11))
        {
            return false;
        }

        if (holdBinding.Modifiers.HasFlag(HotkeyModifiers.Shift) && !_windowInspector.IsKeyDown(0x10))
        {
            return false;
        }

        if (holdBinding.Modifiers.HasFlag(HotkeyModifiers.Alt) && !_windowInspector.IsKeyDown(0x12))
        {
            return false;
        }

        if (holdBinding.Modifiers.HasFlag(HotkeyModifiers.Windows)
            && !_windowInspector.IsKeyDown(0x5B)
            && !_windowInspector.IsKeyDown(0x5C))
        {
            return false;
        }

        return true;
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

    private async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        await DisposeAsync();
        Application.Current.Shutdown();
    }
}
