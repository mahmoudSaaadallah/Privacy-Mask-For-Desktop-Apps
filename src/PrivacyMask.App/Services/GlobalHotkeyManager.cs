using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using PrivacyMask.Core.Models;
using PrivacyMask.Windows.Interop;

namespace PrivacyMask.App.Services;

public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly Dictionary<int, HotkeyAction> _registrations = [];
    private readonly HwndSource _source;
    private int _nextId = 1;

    public GlobalHotkeyManager()
    {
        var parameters = new HwndSourceParameters("PrivacyMask.GlobalHotkeys")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0x800000,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? HotkeyPressed;

    public void RegisterBindings(IEnumerable<HotkeyBinding> bindings)
    {
        ClearRegistrations();

        foreach (var binding in bindings.Where(binding => binding.Enabled && !binding.IsHoldGesture))
        {
            var id = _nextId++;
            if (NativeMethods.RegisterHotKey(_source.Handle, id, ConvertModifiers(binding.Modifiers), (uint)binding.VirtualKey))
            {
                _registrations[id] = binding.Action;
            }
        }
    }

    public void Dispose()
    {
        ClearRegistrations();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void ClearRegistrations()
    {
        foreach (var registration in _registrations.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_source.Handle, registration);
        }

        _registrations.Clear();
        _nextId = 1;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey)
        {
            var id = wParam.ToInt32();
            if (_registrations.TryGetValue(id, out var action))
            {
                HotkeyPressed?.Invoke(action);
                handled = true;
            }
        }

        return nint.Zero;
    }

    private static uint ConvertModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= 0x0001;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= 0x0002;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= 0x0004;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            result |= 0x0008;
        }

        return result;
    }
}
