using System;
using Microsoft.Win32;

namespace PrivacyMask.Windows.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _applicationName;
    private readonly string _executablePath;

    public StartupRegistrationService(string applicationName, string executablePath)
    {
        _applicationName = applicationName;
        _executablePath = executablePath;
    }

    public StartupRegistrationState GetState()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var currentValue = runKey?.GetValue(_applicationName) as string;
        if (string.IsNullOrWhiteSpace(currentValue))
        {
            return StartupRegistrationState.Disabled;
        }

        var normalizedPath = _executablePath.Trim();
        var enabled = currentValue.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase);
        var startMinimized = currentValue.Contains("--minimized", StringComparison.OrdinalIgnoreCase);

        return new StartupRegistrationState(enabled, startMinimized);
    }

    public void SetEnabled(bool enabled, bool startMinimized)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var command = startMinimized
                ? $"\"{_executablePath}\" --minimized"
                : $"\"{_executablePath}\"";

            runKey.SetValue(_applicationName, command);
        }
        else
        {
            runKey.DeleteValue(_applicationName, throwOnMissingValue: false);
        }
    }
}

public readonly record struct StartupRegistrationState(bool Enabled, bool StartMinimized)
{
    public static StartupRegistrationState Disabled => new(false, false);
}
