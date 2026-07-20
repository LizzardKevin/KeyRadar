using KeyRadar.Rules;
using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class BuiltInRuleCatalogTests
{
    [Fact]
    public void Catalog_contains_exactly_fifty_distinct_applications()
    {
        var applications = BuiltInRuleCatalog.Load();

        Assert.Equal(50, applications.Count);
        Assert.Equal(50, applications.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(applications, app => Assert.NotEmpty(app.ExecutableNames));
    }

    [Fact]
    public void WeChat_Alt_A_is_available_as_a_screenshot_rule()
    {
        var weChat = Assert.Single(
            BuiltInRuleCatalog.Load(),
            app => app.Id == "wechat");

        var screenshot = Assert.Single(
            weChat.Shortcuts,
            shortcut => shortcut.Gesture == ShortcutGesture.Parse("Alt+A"));

        Assert.Equal("截图", screenshot.Function);
        Assert.Equal(ShortcutScope.Global, screenshot.Scope);
        Assert.Equal(OwnershipConfidence.Configuration, screenshot.Confidence);
    }

    [Fact]
    public void Chrome_contains_the_primary_foreground_navigation_shortcuts()
    {
        var chrome = Assert.Single(BuiltInRuleCatalog.Load(), app => app.Id == "chrome");

        Assert.Contains(chrome.Shortcuts, shortcut =>
            shortcut.Gesture == ShortcutGesture.Parse("Ctrl+L") && shortcut.Function == "定位地址栏");
        Assert.Contains(chrome.Shortcuts, shortcut =>
            shortcut.Gesture == ShortcutGesture.Parse("Ctrl+Shift+T") && shortcut.Function == "恢复关闭的标签页");
    }

    [Fact]
    public void Every_declared_shortcut_has_a_function_and_valid_gesture()
    {
        var shortcuts = BuiltInRuleCatalog.Load().SelectMany(app => app.Shortcuts);

        Assert.All(shortcuts, shortcut =>
        {
            Assert.False(string.IsNullOrWhiteSpace(shortcut.Function));
            Assert.NotEqual(default, shortcut.Gesture);
        });
    }

    [Fact]
    public void Initial_catalog_has_useful_shortcuts_for_at_least_thirty_five_applications()
    {
        var supported = BuiltInRuleCatalog.Load().Count(app => app.Shortcuts.Count > 0);

        Assert.True(supported >= 35, $"Only {supported} applications contain shortcut rules.");
    }

    [Fact]
    public void An_application_does_not_repeat_the_same_gesture_and_scope()
    {
        Assert.All(BuiltInRuleCatalog.Load(), application =>
        {
            var shortcuts = application.Shortcuts
                .Select(shortcut => $"{shortcut.Gesture}|{shortcut.Scope}")
                .ToArray();
            Assert.Equal(shortcuts.Length, shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }
}
