using KeyRadar.Hotkeys;

namespace KeyRadar.Core.Tests.Hotkeys;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("shift+control+a", "Ctrl+Shift+A")]
    [InlineData("win + alt + s", "Win+Alt+S")]
    [InlineData("cmd+option+1", "Win+Alt+1")]
    [InlineData("CTRL+ESC", "Ctrl+Esc")]
    public void Parse_normalizes_aliases_order_and_spacing(string input, string expected)
    {
        var gesture = HotkeyGesture.Parse(input);

        Assert.Equal(expected, gesture.ToString());
    }

    [Fact]
    public void Equality_uses_the_normalized_gesture()
    {
        var left = HotkeyGesture.Parse("Alt+A");
        var right = HotkeyGesture.Parse("a + option");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl++A")]
    public void Parse_rejects_gestures_without_exactly_one_primary_key(string input)
    {
        Assert.Throws<FormatException>(() => HotkeyGesture.Parse(input));
    }

    [Fact]
    public void TryParse_returns_false_without_throwing_for_invalid_text()
    {
        Assert.False(HotkeyGesture.TryParse("Ctrl+Alt", out _));
        Assert.False(HotkeyGesture.TryParse(null, out _));
    }
}
