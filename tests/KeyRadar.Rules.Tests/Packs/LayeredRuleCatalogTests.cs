using KeyRadar.Rules.Packs;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class LayeredRuleCatalogTests
{
    [Fact]
    public void User_variant_overrides_updated_official_and_bundled_variants()
    {
        var bundled = Variant("bundled");
        var updated = Variant("updated");
        var user = Variant("user");

        var result = LayeredRuleCatalog.Resolve([user], [updated], [bundled]);

        Assert.Same(user, Assert.Single(result));
    }

    private static ApplicationVariantRule Variant(string displayName) => new(
        "wechat",
        "cn-desktop",
        new LocalizedText(new Dictionary<string, string> { ["zh-CN"] = displayName }),
        new ApplicationMatchRule(["WeChat.exe"], [], null, [], null),
        []);
}
