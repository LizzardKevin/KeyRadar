using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules.Packs;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class LocalRulePackWriterTests
{
    [Fact]
    public void Unsigned_local_pack_round_trips_with_an_explicit_trust_label()
    {
        using var stream = new MemoryStream();
        LocalRulePackWriter.Write(stream, [Variant()]);
        stream.Position = 0;

        var result = RulePackReader.ReadLocal(stream);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(RulePackTrust.UnsignedLocal, result.Pack!.Trust);
        Assert.Equal(OwnershipConfidence.UserDeclared, result.Pack.Variants[0].Hotkeys[0].Confidence);
    }

    [Fact]
    public void Local_pack_cannot_be_loaded_as_an_official_pack()
    {
        using var stream = new MemoryStream();
        LocalRulePackWriter.Write(stream, [Variant()]);
        stream.Position = 0;

        var result = RulePackReader.Read(stream, new byte[32]);

        Assert.False(result.IsSuccess);
    }

    private static ApplicationVariantRule Variant() => new(
        "wechat",
        "user",
        new LocalizedText(new Dictionary<string, string> { ["zh-CN"] = "微信" }),
        new ApplicationMatchRule(["WeChat.exe"], ["Tencent"], null, [], "user"),
        [
            new HotkeyRule(
                HotkeyGesture.Parse("Alt+A"),
                new LocalizedText(new Dictionary<string, string> { ["zh-CN"] = "截图" }),
                HotkeyScope.Global,
                OwnershipConfidence.UserDeclared),
        ]);
}
