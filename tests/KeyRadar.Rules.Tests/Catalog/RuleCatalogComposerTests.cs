using KeyRadar.Conflicts;
using KeyRadar.Rules.Catalog;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class RuleCatalogComposerTests
{
    [Fact]
    public void Higher_priority_rule_wins_while_sources_and_executables_are_retained()
    {
        var builtIn = new ApplicationRuleSet(
            "wechat",
            "WeChat",
            ["WeChat.exe"],
            [Rule("Alt+A", "Capture", OwnershipConfidence.SystemKnown, "https://built-in.test")]);
        var official = new ApplicationRuleSet(
            "wechat",
            "微信",
            ["Weixin.exe"],
            [Rule("Alt+A", "截图", OwnershipConfidence.Configuration, "https://official.test")]);

        var result = RuleCatalogComposer.Compose([builtIn], [official]);

        var application = Assert.Single(result);
        Assert.Equal("微信", application.DisplayName);
        Assert.Equal(["Weixin.exe", "WeChat.exe"], application.ExecutableNames);
        var shortcut = Assert.Single(application.Shortcuts);
        Assert.Equal("截图", shortcut.Function);
        Assert.Equal(OwnershipConfidence.Configuration, shortcut.Confidence);
        Assert.Equal(["https://official.test", "https://built-in.test"], shortcut.Sources);
    }

    private static ShortcutRule Rule(
        string gesture,
        string function,
        OwnershipConfidence confidence,
        string source) =>
        new(ShortcutGesture.Parse(gesture), function, ShortcutScope.Global, confidence)
        {
            Sources = [source],
        };
}
