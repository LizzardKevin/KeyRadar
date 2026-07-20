namespace KeyRadar.Conflicts;

public static class ShortcutConflictClassifier
{
    public static ConflictKind Classify(IReadOnlyCollection<ShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        if (bindings.Count < 2)
        {
            return ConflictKind.None;
        }

        var systemWide = bindings
            .Where(binding => binding.Scope is ShortcutScope.Global or ShortcutScope.WindowsSystem)
            .ToArray();

        if (systemWide.Count(binding => binding.Confidence is OwnershipConfidence.Confirmed) >= 2)
        {
            return ConflictKind.DefiniteConflict;
        }

        if (systemWide.Length > 0)
        {
            return ConflictKind.PossibleInterception;
        }

        return bindings
            .Select(binding => binding.ApplicationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any()
            ? ConflictKind.ContextualReuse
            : ConflictKind.None;
    }
}
