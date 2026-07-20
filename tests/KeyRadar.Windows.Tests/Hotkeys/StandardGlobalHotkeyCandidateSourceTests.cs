using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Hotkeys;

public sealed class StandardGlobalHotkeyCandidateSourceTests
{
    [Fact]
    public void Candidates_cover_safe_standard_keys_without_bare_text_or_secure_attention()
    {
        var candidates = StandardGlobalHotkeyCandidateSource.Create().ToHashSet();

        Assert.Contains(HotkeyGesture.Parse("Alt+A"), candidates);
        Assert.Contains(HotkeyGesture.Parse("Ctrl+Shift+F24"), candidates);
        Assert.Contains(HotkeyGesture.Parse("F1"), candidates);
        Assert.Contains(HotkeyGesture.Parse("MediaPlayPause"), candidates);
        Assert.DoesNotContain(HotkeyGesture.Parse("A"), candidates);
        Assert.DoesNotContain(HotkeyGesture.Parse("1"), candidates);
        Assert.DoesNotContain(HotkeyGesture.Parse("Ctrl+Alt+Delete"), candidates);
        Assert.DoesNotContain(candidates, candidate => candidate.Modifiers.HasFlag(HotkeyModifiers.Windows));
    }
}
