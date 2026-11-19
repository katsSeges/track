namespace Track.Core.Display;

[Flags]
public enum HotkeyModifiers
{
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8,
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key);

public static class HotkeyGestureParser
{
    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(0, "");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var modifiers = (HotkeyModifiers)0;
        var seen = new HashSet<HotkeyModifiers>();
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => (HotkeyModifiers)0,
            };
            if (modifier == 0 || !seen.Add(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        var key = parts[^1].ToUpperInvariant();
        if (!IsSupportedKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1)
        {
            return char.IsAsciiLetterOrDigit(key[0]);
        }

        if (key[0] == 'F' && int.TryParse(key.AsSpan(1), out var function))
        {
            return function is >= 1 and <= 24;
        }

        return key is "SPACE" or "TAB" or "ESC" or "ESCAPE" or "HOME" or "END" or "INSERT" or "DELETE"
            or "UP" or "DOWN" or "LEFT" or "RIGHT" or "PAGEUP" or "PAGEDOWN";
    }
}
