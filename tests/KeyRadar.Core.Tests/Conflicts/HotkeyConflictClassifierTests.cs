using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Core.Tests.Conflicts;

public sealed class ShortcutConflictClassifierTests
{
    private static readonly ShortcutGesture AltA = ShortcutGesture.Parse("Alt+A");

    [Fact]
    public void Two_confirmed_global_owners_are_a_definite_conflict()
    {
        var bindings = new[]
        {
            Binding("wechat", ShortcutScope.Global, OwnershipConfidence.Confirmed),
            Binding("screen-capture-tool", ShortcutScope.Global, OwnershipConfidence.Confirmed),
        };

        Assert.Equal(ConflictKind.DefiniteConflict, ShortcutConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void A_global_binding_can_intercept_an_application_binding()
    {
        var bindings = new[]
        {
            Binding("wechat", ShortcutScope.Global, OwnershipConfidence.Configuration),
            Binding("photoshop", ShortcutScope.Application, OwnershipConfidence.SystemKnown),
        };

        Assert.Equal(ConflictKind.PossibleInterception, ShortcutConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void Bindings_in_two_application_contexts_are_contextual_reuse()
    {
        var bindings = new[]
        {
            Binding("chrome", ShortcutScope.Application, OwnershipConfidence.SystemKnown),
            Binding("edge", ShortcutScope.Application, OwnershipConfidence.SystemKnown),
        };

        Assert.Equal(ConflictKind.ContextualReuse, ShortcutConflictClassifier.Classify(bindings));
    }

    [Fact]
    public void A_single_binding_has_no_conflict()
    {
        Assert.Equal(
            ConflictKind.None,
            ShortcutConflictClassifier.Classify([Binding("wechat", ShortcutScope.Global, OwnershipConfidence.Configuration)]));
    }

    private static ShortcutBinding Binding(
        string applicationId,
        ShortcutScope scope,
        OwnershipConfidence confidence) =>
        new(applicationId, AltA, scope, confidence);
}
