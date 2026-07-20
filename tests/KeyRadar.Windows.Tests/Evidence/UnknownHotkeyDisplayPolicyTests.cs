using KeyRadar.Hotkeys;
using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class UnknownHotkeyDisplayPolicyTests
{
    [Theory]
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("F24")]
    [InlineData("BrowserBack")]
    [InlineData("VolumeMute")]
    [InlineData("MediaPlayPause")]
    [InlineData("LaunchApp2")]
    [InlineData("Left")]
    [InlineData("Delete")]
    public void Suppresses_modifier_free_noisy_special_keys(string text) =>
        Assert.True(UnknownHotkeyDisplayPolicy.ShouldSuppress(HotkeyGesture.Parse(text)));

    [Theory]
    [InlineData("Ctrl+F12")]
    [InlineData("Alt+A")]
    [InlineData("Alt+BrowserBack")]
    [InlineData("PrintScreen")]
    public void Retains_modifier_bearing_and_print_screen_gestures(string text) =>
        Assert.False(UnknownHotkeyDisplayPolicy.ShouldSuppress(HotkeyGesture.Parse(text)));
}
