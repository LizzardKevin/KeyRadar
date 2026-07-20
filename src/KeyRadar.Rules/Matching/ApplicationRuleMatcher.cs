namespace KeyRadar.Rules;

public static class ApplicationRuleMatcher
{
    public static ApplicationVariantRule? FindByExecutableName(
        IEnumerable<ApplicationVariantRule> variants,
        string executableName) =>
        new ApplicationVariantMatcher()
            .Match(new ApplicationIdentity(executableName), variants)
            .Selected;
}
