namespace KeyRadar.Rules;

public static class ApplicationRuleMatcher
{
    public static ApplicationRuleSet? FindByExecutableName(
        IEnumerable<ApplicationRuleSet> applications,
        string executableName)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        return applications.FirstOrDefault(application =>
            application.ExecutableNames.Contains(executableName, StringComparer.OrdinalIgnoreCase));
    }
}
