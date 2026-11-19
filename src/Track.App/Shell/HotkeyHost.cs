using System.Diagnostics.CodeAnalysis;
using System.Windows.Interop;
using Track.Core.Display;

namespace Track.App.Shell;

[ExcludeFromCodeCoverage(Justification = "RegisterHotKey and HwndSource are Windows APIs; gesture parsing is tested in Track.Core.")]
internal sealed class HotkeyHost : IDisposable
{
    private const int Id = 0x5452;
    private readonly HwndSource _source;
    private readonly Action _togglePopover;
    private bool _registered;

    public HotkeyHost(Action togglePopover)
    {
        _togglePopover = togglePopover;
        _source = new HwndSource(new HwndSourceParameters("TrackHotkey")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
        });
        _source.AddHook(WindowHook);
    }

    public string Status { get; private set; } = "Hotkey is off.";

    public void Configure(bool enabled, string gesture)
    {
        if (_registered)
        {
            Native.UnregisterHotkey(_source.Handle, Id);
            _registered = false;
        }

        if (!enabled)
        {
            Status = "Hotkey is off.";
            return;
        }

        if (!HotkeyGestureParser.TryParse(gesture, out var binding) || !TryVirtualKey(binding.Key, out var virtualKey))
        {
            Status = "Enter a supported shortcut such as Ctrl+Alt+Shift+T.";
            return;
        }

        var modifiers = Native.HotkeyModNoRepeat;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= Native.HotkeyModControl;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= Native.HotkeyModAlt;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= Native.HotkeyModShift;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Windows)) modifiers |= Native.HotkeyModWindows;
        _registered = Native.RegisterHotkey(_source.Handle, Id, modifiers, virtualKey);
        Status = _registered
            ? $"Hotkey active: {gesture}"
            : $"Hotkey unavailable: {gesture} may already be in use.";
    }

    public void Dispose()
    {
        if (_registered)
        {
            Native.UnregisterHotkey(_source.Handle, Id);
            _registered = false;
        }

        _source.RemoveHook(WindowHook);
        _source.Dispose();
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.HotkeyMessage && wParam.ToInt32() == Id)
        {
            _togglePopover();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryVirtualKey(string key, out uint virtualKey)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = key[0];
            return true;
        }

        if (key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var function) && function is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + function - 1);
            return true;
        }

        virtualKey = key switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "HOME" => 0x24,
            "END" => 0x23,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            _ => 0,
        };
        return virtualKey != 0;
    }
}
