namespace KeyRadar.Rules.Tests.Models;

public sealed class LocalizedTextTests
{
    [Fact]
    public void Requested_locale_is_used_when_available()
    {
        var text = new LocalizedText(new Dictionary<string, string>
        {
            ["zh-CN"] = "截图",
            ["en-US"] = "Screenshot",
        });

        Assert.Equal("Screenshot", text.Resolve("en-US"));
    }

    [Fact]
    public void Missing_locale_falls_back_to_text_that_was_supplied()
    {
        var text = new LocalizedText(new Dictionary<string, string>
        {
            ["zh-CN"] = "截图",
        });

        Assert.Equal("截图", text.Resolve("en-US"));
    }
}
