using Track.Core.Display;

namespace Track.Core.Tests.Display;

public sealed class HotkeyGestureParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+T", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "T")]
    [InlineData("windows+f12", HotkeyModifiers.Windows, "F12")]
    [InlineData("Control + Space", HotkeyModifiers.Control, "SPACE")]
    [InlineData("Alt+Tab", HotkeyModifiers.Alt, "TAB")]
    [InlineData("Alt+Esc", HotkeyModifiers.Alt, "ESC")]
    [InlineData("Alt+Escape", HotkeyModifiers.Alt, "ESCAPE")]
    [InlineData("Alt+Home", HotkeyModifiers.Alt, "HOME")]
    [InlineData("Alt+End", HotkeyModifiers.Alt, "END")]
    [InlineData("Alt+Insert", HotkeyModifiers.Alt, "INSERT")]
    [InlineData("Alt+Delete", HotkeyModifiers.Alt, "DELETE")]
    [InlineData("Alt+Up", HotkeyModifiers.Alt, "UP")]
    [InlineData("Alt+Down", HotkeyModifiers.Alt, "DOWN")]
    [InlineData("Alt+Left", HotkeyModifiers.Alt, "LEFT")]
    [InlineData("Alt+Right", HotkeyModifiers.Alt, "RIGHT")]
    [InlineData("Alt+PageUp", HotkeyModifiers.Alt, "PAGEUP")]
    [InlineData("Alt+PageDown", HotkeyModifiers.Alt, "PAGEDOWN")]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, "F1")]
    [InlineData("Alt+F24", HotkeyModifiers.Alt, "F24")]
    [InlineData("Alt+5", HotkeyModifiers.Alt, "5")]
    public void Parses_supported_modifier_and_key_combinations(string value, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGestureParser.TryParse(value, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl++T")]
    [InlineData("Ctrl+Ctrl+T")]
    [InlineData("Ctrl+T+Y")]
    [InlineData("Ctrl+Unknown")]
    [InlineData("Ctrl+FNO")]
    [InlineData("Alt+F25")]
    [InlineData("Alt+F0")]
    public void Rejects_unsupported_or_incomplete_gestures(string? value)
    {
        Assert.False(HotkeyGestureParser.TryParse(value, out _));
    }
}
