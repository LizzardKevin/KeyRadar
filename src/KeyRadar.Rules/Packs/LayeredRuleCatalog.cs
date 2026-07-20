namespace KeyRadar.Rules.Packs;

public static class LayeredRuleCatalog
{
    public static IReadOnlyList<ApplicationVariantRule> Resolve(
        IReadOnlyList<ApplicationVariantRule> userRules,
        IReadOnlyList<ApplicationVariantRule> updatedOfficialRules,
        IReadOnlyList<ApplicationVariantRule> bundledRules)
    {
        var selected = new Dictionary<string, ApplicationVariantRule>(StringComparer.OrdinalIgnoreCase);
        AddMissing(selected, userRules);
        AddMissing(selected, updatedOfficialRules);
        AddMissing(selected, bundledRules);
        return selected.Values
            .OrderBy(rule => rule.ApplicationId, StringComparer.Ordinal)
            .ThenBy(rule => rule.VariantId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddMissing(
        IDictionary<string, ApplicationVariantRule> selected,
        IEnumerable<ApplicationVariantRule> rules)
    {
        foreach (var rule in rules)
        {
            selected.TryAdd($"{rule.ApplicationId}/{rule.VariantId}", rule);
        }
    }
}
