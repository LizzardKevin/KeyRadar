using KeyRadar.Shortcuts;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class NativeHotkeyMapperTests
{
    [Theory]
    [InlineData("Alt+A", 0x41u, 0x1u)]
    [InlineData("Ctrl+Shift+F12", 0x7Bu, 0x6u)]
    [InlineData("Win+PrintScreen", 0x2Cu, 0x8u)]
    public void TryMap_MapsSupportedGesture(string text, uint expectedKey, uint expectedModifiers)
    {
        var mapped = NativeHotkeyMapper.TryMap(
            ShortcutGesture.Parse(text),
            out var virtualKey,
            out var modifiers);

        Assert.True(mapped);
        Assert.Equal(expectedKey, virtualKey);
        Assert.Equal(expectedModifiers, modifiers);
    }
}
