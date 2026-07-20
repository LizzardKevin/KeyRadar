using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;

namespace KeyRadar.Core.Tests.Conflicts;

public sealed class HotkeyConflictClassifierTests
{
    private static readonly HotkeyGesture AltA = HotkeyGesture.Parse("Alt+A");

    [Fact]
    public void Two_confirmed_global_owners_are_a_definite_conflict()
    {
        var bindings = new[]
        {
            Binding("wechat", HotkeyScope.Global, OwnershipConfidence.Confirmed),
            Binding("screen-capture-tool", HotkeyScope.Global, OwnershipConfidence.Confirmed),
        };

        Assert.Equal(ConflictKind.DefiniteConflict, HotkeyConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void A_global_binding_can_intercept_an_application_binding()
    {
        var bindings = new[]
        {
            Binding("wechat", HotkeyScope.Global, OwnershipConfidence.LocalConfiguration),
            Binding("photoshop", HotkeyScope.Foreground, OwnershipConfidence.SystemKnown),
        };

        Assert.Equal(ConflictKind.PossibleInterception, HotkeyConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void Bindings_in_two_application_contexts_are_contextual_reuse()
    {
        var bindings = new[]
        {
            Binding("chrome", HotkeyScope.Foreground, OwnershipConfidence.SystemKnown),
            Binding("edge", HotkeyScope.Foreground, OwnershipConfidence.SystemKnown),
        };

        Assert.Equal(ConflictKind.ContextualReuse, HotkeyConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void A_single_binding_has_no_conflict()
    {
        Assert.Equal(
            ConflictKind.None,
            HotkeyConflictClassifier.Classify([Binding("wechat", HotkeyScope.Global, OwnershipConfidence.LocalConfiguration)]));
    }

    private static HotkeyBinding Binding(
        string applicationId,
        HotkeyScope scope,
        OwnershipConfidence confidence) =>
        new(applicationId, AltA, scope, confidence);
}
