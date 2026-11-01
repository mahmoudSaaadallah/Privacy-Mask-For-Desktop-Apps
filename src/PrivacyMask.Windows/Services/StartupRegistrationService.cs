using System;
using Microsoft.Win32;

namespace PrivacyMask.Windows.Services;

public sealed class StartupRegistrationService
{
    private readonly IStartupRegistrationStore _store;
    private readonly string _command;
    private readonly string _minimizedCommand;

    public StartupRegistrationService(string applicationName, string executablePath)
        : this(executablePath, new RegistryStartupRegistrationStore(applicationName))
    {
    }

    internal StartupRegistrationService(string executablePath, IStartupRegistrationStore store)
    {
        _store = store;
        _command = $"\"{executablePath.Trim()}\"";
        _minimizedCommand = _command + " --minimized";
    }

    public StartupRegistrationState GetState()
    {
        var current = (_store.Read() as string)?.Trim();
        if (string.Equals(current, _minimizedCommand, StringComparison.OrdinalIgnoreCase)) return new(true, true);
        if (string.Equals(current, _command, StringComparison.OrdinalIgnoreCase)) return new(true, false);
        return StartupRegistrationState.Disabled;
    }

    public void SetEnabled(bool enabled, bool startMinimized)
    {
        var current = _store.Read();
        if (enabled)
        {
            var command = startMinimized ? _minimizedCommand : _command;
            if (!string.Equals(current as string, command, StringComparison.Ordinal)) _store.Write(command);
        }
        else if (current is not null)
        {
            _store.Delete();
        }
    }
}

internal interface IStartupRegistrationStore
{
    object? Read();
    void Write(string command);
    void Delete();
}

internal sealed class RegistryStartupRegistrationStore(string applicationName) : IStartupRegistrationStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public object? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(applicationName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(applicationName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(applicationName, throwOnMissingValue: false);
    }
}

public readonly record struct StartupRegistrationState(bool Enabled, bool StartMinimized)
{
    public static StartupRegistrationState Disabled => new(false, false);
}
