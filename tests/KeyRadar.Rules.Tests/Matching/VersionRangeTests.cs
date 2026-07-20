namespace KeyRadar.Rules.Tests.Matching;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData(">=4.0 <5.0", "4.0", true)]
    [InlineData(">=4.0 <5.0", "4.9.9", true)]
    [InlineData(">=4.0 <5.0", "5.0", false)]
    [InlineData(">4.0 <=4.2", "4.0", false)]
    [InlineData(">4.0 <=4.2", "4.2", true)]
    public void Bounded_numeric_comparisons_are_supported(string expression, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(expression, out var range));
        Assert.Equal(expected, range.Contains(Version.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("4.*")]
    [InlineData("System.IO.File.Delete('*')")]
    [InlineData(">=4.0 || <3.0")]
    public void Arbitrary_or_ambiguous_expressions_are_rejected(string expression)
    {
        Assert.False(VersionRange.TryParse(expression, out _));
    }
}
