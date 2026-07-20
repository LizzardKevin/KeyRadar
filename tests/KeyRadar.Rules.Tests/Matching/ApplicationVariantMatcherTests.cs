namespace KeyRadar.Rules.Tests.Matching;

public sealed class ApplicationVariantMatcherTests
{
    private readonly ApplicationVariantMatcher _matcher = new();

    [Fact]
    public void Specific_publisher_and_version_select_an_exact_variant()
    {
        var result = _matcher.Match(
            new ApplicationIdentity("WeChat.exe", "4.2.0", "Tencent", "Tencent", null, "cn-official"),
            [Variant("cn-desktop", ["Tencent"], ">=4.0 <5.0", "cn-official")]);

        Assert.Equal(VariantMatchKind.Exact, result.Kind);
        Assert.Equal("cn-desktop", result.Selected!.VariantId);
        Assert.Contains(result.Candidates[0].Evidence, item =>
            item.Signal == "publisher" && item.State == VariantEvidenceState.Match);
    }

    [Fact]
    public void Executable_only_match_remains_suspected()
    {
        var result = _matcher.Match(
            new ApplicationIdentity("WeChat.exe"),
            [Variant("generic", [], null, null)]);

        Assert.Equal(VariantMatchKind.Suspected, result.Kind);
        Assert.Equal("generic", result.Selected!.VariantId);
    }

    [Fact]
    public void Equal_candidates_are_reported_as_ambiguous()
    {
        var result = _matcher.Match(
            new ApplicationIdentity("WeChat.exe"),
            [Variant("cn-desktop", [], null, null), Variant("international", [], null, null)]);

        Assert.Equal(VariantMatchKind.Ambiguous, result.Kind);
        Assert.Null(result.Selected);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Known_version_mismatch_excludes_the_variant()
    {
        var result = _matcher.Match(
            new ApplicationIdentity("WeChat.exe", "5.0.0"),
            [Variant("cn-desktop", [], ">=4.0 <5.0", null)]);

        Assert.Equal(VariantMatchKind.None, result.Kind);
    }

    private static ApplicationVariantRule Variant(
        string variantId,
        IReadOnlyList<string> publishers,
        string? versionRange,
        string? distribution)
    {
        VersionRange? range = null;
        if (versionRange is not null)
        {
            Assert.True(VersionRange.TryParse(versionRange, out range));
        }

        return new ApplicationVariantRule(
            "wechat",
            variantId,
            new LocalizedText(new Dictionary<string, string> { ["zh-CN"] = "微信" }),
            new ApplicationMatchRule(["WeChat.exe"], publishers, range, [], distribution),
            []);
    }
}
