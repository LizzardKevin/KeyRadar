using KeyRadar.Hotkeys;
using KeyRadar.Windows.Input;

namespace KeyRadar.Windows.Tests.Input;

public sealed class HotkeyObservationStateTests
{
    [Fact]
    public void Ordinary_text_keys_are_not_observed()
    {
        var state = new HotkeyObservationState();

        var observation = state.Process(0x41, isKeyDown: true);

        Assert.Null(observation);
    }

    [Fact]
    public void Modifier_plus_primary_key_is_observed_without_consuming_text()
    {
        var state = new HotkeyObservationState();
        Assert.Null(state.Process(0xA4, isKeyDown: true));

        var observation = state.Process(0x41, isKeyDown: true);

        Assert.Equal(HotkeyGesture.Parse("Alt+A"), observation);
    }

    [Fact]
    public void Released_modifiers_do_not_leak_into_later_keys()
    {
        var state = new HotkeyObservationState();
        _ = state.Process(0xA2, isKeyDown: true);
        _ = state.Process(0xA2, isKeyDown: false);

        Assert.Null(state.Process(0x41, isKeyDown: true));
    }

    [Fact]
    public void Multiple_modifiers_are_normalized()
    {
        var state = new HotkeyObservationState();
        _ = state.Process(0x5B, isKeyDown: true);
        _ = state.Process(0xA0, isKeyDown: true);

        Assert.Equal(HotkeyGesture.Parse("Win+Shift+S"), state.Process(0x53, isKeyDown: true));
    }
}
