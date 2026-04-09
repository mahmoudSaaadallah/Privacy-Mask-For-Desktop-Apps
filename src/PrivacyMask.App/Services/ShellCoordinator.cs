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
using PrivacyMask.Windows.Services;
using Application = System.Windows.Application;
using Point = System.Windows.Point;

namespace PrivacyMask.App.Services;

public sealed class ShellCoordinator : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly string[] _args;
    private readonly DefaultSettingsFactory _defaultSettingsFactory;
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

    private AppSettings _settings = new();
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;

    public ShellCoordinator(Dispatcher dispatcher, string[] args)
    {
        _dispatcher = dispatcher;
        _args = args;
        _defaultSettingsFactory = new DefaultSettingsFactory();
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
            Interval = TimeSpan.FromMilliseconds(140),
        };
        _refreshTimer.Tick += RefreshTimerOnTick;

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "PrivacyMask",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        _modeItem = new ToolStripMenuItem("Mode: Standard")
        {
            Enabled = false,
        };
        _toggleProtectionItem = new ToolStripMenuItem("Toggle protection", null, (_, _) => ToggleProtection());
        _panicItem = new ToolStripMenuItem("Panic hide all", null, (_, _) => TogglePanicMode());
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
        var registrationState = _startupRegistrationService.GetState();
        if (registrationState.Enabled)
        {
            _settings.LaunchAtLogin = true;
            _settings.StartMinimized = registrationState.StartMinimized;
        }

        _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);

        RegisterHotkeys();
        UpdateTrayState();
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
        if (_isShuttingDown)
        {
            return ValueTask.CompletedTask;
        }

        _refreshTimer.Stop();
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
        try
        {
            if (_settings.CurrentMode is RuntimeMode.Off or RuntimeMode.Panic)
            {
                _overlayManager.HideAll();
                return;
            }

            var discovery = _windowInspector.Capture();
            var trackedWindows = new List<TrackedWindow>();

            foreach (var snapshot in discovery.Windows)
            {
                var trackedWindow = _windowProfileResolver.Resolve(snapshot, _settings.AppProfiles);
                if (trackedWindow is null)
                {
                    continue;
                }

                if (trackedWindow.Profile.StartupMode == AppActivationMode.FocusAware && !snapshot.IsForeground)
                {
                    continue;
                }

                trackedWindows.Add(trackedWindow);
            }

            var cursorPoint = GetCursorPoint();
            var temporaryRevealHeld = EvaluateTemporaryReveal();
            _overlayManager.Update(trackedWindows, _settings.CurrentMode, temporaryRevealHeld, cursorPoint);
        }
        catch
        {
            _overlayManager.HideAll();
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
        var viewModel = SettingsViewModel.FromModel(_settings, _settingsStore.SettingsPath);
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
        updatedSettings.CurrentMode = _settings.CurrentMode;
        updatedSettings.OnboardingCompleted = true;
        _settings = _defaultSettingsFactory.MergeWithDefaults(updatedSettings);
        _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
        await _settingsStore.SaveAsync(_settings);
        RegisterHotkeys();
        UpdateTrayState();
    }

    private void PreviewSettings(AppSettings previewSettings)
    {
        previewSettings.CurrentMode = _settings.CurrentMode;
        previewSettings.OnboardingCompleted = true;
        _settings = _defaultSettingsFactory.MergeWithDefaults(previewSettings);
    }

    private void RegisterHotkeys()
    {
        _hotkeyManager.RegisterBindings(_settings.GlobalHotkeys);
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
        _settings.CurrentMode = _settings.CurrentMode == RuntimeMode.Off ? RuntimeMode.Standard : RuntimeMode.Off;
        UpdateTrayState();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void TogglePanicMode()
    {
        _settings.CurrentMode = _settings.CurrentMode == RuntimeMode.Panic ? RuntimeMode.Standard : RuntimeMode.Panic;
        UpdateTrayState();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private async Task ToggleLaunchAtLoginAsync()
    {
        _settings.LaunchAtLogin = !_settings.LaunchAtLogin;
        _startupRegistrationService.SetEnabled(_settings.LaunchAtLogin, _settings.StartMinimized);
        await _settingsStore.SaveAsync(_settings);
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        _modeItem.Text = $"Mode: {_settings.CurrentMode}";
        _launchAtLoginItem.Checked = _settings.LaunchAtLogin;
        _toggleProtectionItem.Text = _settings.CurrentMode == RuntimeMode.Off ? "Resume protection" : "Pause protection";
        _panicItem.Checked = _settings.CurrentMode == RuntimeMode.Panic;
        _notifyIcon.Text = _settings.CurrentMode switch
        {
            RuntimeMode.Standard => "PrivacyMask - protecting supported windows",
            RuntimeMode.Off => "PrivacyMask - protection paused",
            RuntimeMode.Panic => "PrivacyMask - panic hide active",
            RuntimeMode.TemporaryReveal => "PrivacyMask - temporary reveal",
            _ => "PrivacyMask",
        };
    }

    private bool EvaluateTemporaryReveal()
    {
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
