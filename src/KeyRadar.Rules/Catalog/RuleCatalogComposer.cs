namespace KeyRadar.Rules.Catalog;

public static class RuleCatalogComposer
{
    public static IReadOnlyList<ApplicationRuleSet> Compose(
        params IReadOnlyList<ApplicationRuleSet>[] catalogsInAscendingPriority)
    {
        ArgumentNullException.ThrowIfNull(catalogsInAscendingPriority);

        var orderedIds = new List<string>();
        var applications = new Dictionary<string, ApplicationRuleSet>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogsInAscendingPriority)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            foreach (var application in catalog)
            {
                if (!applications.TryGetValue(application.Id, out var lowerPriority))
                {
                    applications.Add(application.Id, application);
                    orderedIds.Add(application.Id);
                    continue;
                }

                applications[application.Id] = Merge(lowerPriority, application);
            }
        }

        return orderedIds.Select(id => applications[id]).ToArray();
    }

    private static ApplicationRuleSet Merge(
        ApplicationRuleSet lowerPriority,
        ApplicationRuleSet higherPriority)
    {
        var executables = higherPriority.ExecutableNames
            .Concat(lowerPriority.ExecutableNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lowerShortcuts = lowerPriority.Shortcuts.ToDictionary(ShortcutKey.From);
        var shortcuts = new List<ShortcutRule>(
            higherPriority.Shortcuts.Count + lowerPriority.Shortcuts.Count);

        foreach (var shortcut in higherPriority.Shortcuts)
        {
            var key = ShortcutKey.From(shortcut);
            if (lowerShortcuts.Remove(key, out var lowerShortcut))
            {
                shortcuts.Add(shortcut with
                {
                    Sources = shortcut.Sources
                        .Concat(lowerShortcut.Sources)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                });
            }
            else
            {
                shortcuts.Add(shortcut);
            }
        }

        shortcuts.AddRange(lowerPriority.Shortcuts.Where(shortcut =>
            lowerShortcuts.ContainsKey(ShortcutKey.From(shortcut))));
        return new ApplicationRuleSet(
            higherPriority.Id,
            higherPriority.DisplayName,
            executables,
            shortcuts);
    }

    private readonly record struct ShortcutKey(string Gesture, Conflicts.ShortcutScope Scope)
    {
        public static ShortcutKey From(ShortcutRule rule) =>
            new(rule.Gesture.ToString().ToUpperInvariant(), rule.Scope);
    }
}
