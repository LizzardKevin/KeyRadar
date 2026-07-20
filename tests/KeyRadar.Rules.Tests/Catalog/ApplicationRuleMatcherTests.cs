using KeyRadar.Rules;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class ApplicationRuleMatcherTests
{
    private static readonly IReadOnlyList<ApplicationRuleSet> Rules =
    [
        new("chrome", "Google Chrome", ["chrome.exe"], []),
        new("potplayer", "PotPlayer", ["PotPlayerMini64.exe", "PotPlayerMini.exe"], []),
    ];

    [Theory]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("CHROME.EXE", "chrome")]
    [InlineData("PotPlayerMini64.exe", "potplayer")]
    public void Executable_name_matches_are_case_insensitive(string executableName, string expectedId)
    {
        var result = ApplicationRuleMatcher.FindByExecutableName(
            Rules,
            executableName);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result.Id);
    }

    [Fact]
    public void Unknown_executable_has_no_rule_match()
    {
        Assert.Null(ApplicationRuleMatcher.FindByExecutableName(Rules, "unknown.exe"));
    }
}
