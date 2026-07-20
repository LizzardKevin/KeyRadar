using KeyRadar.Rules;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class ApplicationRuleMatcherTests
{
    [Theory]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("CHROME.EXE", "chrome")]
    [InlineData("PotPlayerMini64.exe", "potplayer")]
    public void Executable_name_matches_are_case_insensitive(string executableName, string expectedId)
    {
        var result = ApplicationRuleMatcher.FindByExecutableName(
            BuiltInRuleCatalog.Load(),
            executableName);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result.Id);
    }

    [Fact]
    public void Unknown_executable_has_no_rule_match()
    {
        Assert.Null(ApplicationRuleMatcher.FindByExecutableName(BuiltInRuleCatalog.Load(), "unknown.exe"));
    }
}
