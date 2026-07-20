using System.Globalization;
using System.Text.RegularExpressions;

namespace KeyRadar.Rules;

public sealed partial class VersionRange
{
    private readonly IReadOnlyList<Comparator> _comparators;

    private VersionRange(string expression, IReadOnlyList<Comparator> comparators)
    {
        Expression = expression;
        _comparators = comparators;
    }

    public string Expression { get; }

    public bool Contains(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _comparators.All(comparator => comparator.Matches(version));
    }

    public static bool TryParse(string? expression, out VersionRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 100)
        {
            return false;
        }

        var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length is 0 or > 4)
        {
            return false;
        }

        var comparators = new List<Comparator>(tokens.Length);
        foreach (var token in tokens)
        {
            var match = ComparatorPattern().Match(token);
            if (!match.Success || !TryParseVersion(match.Groups["version"].Value, out var version))
            {
                return false;
            }

            comparators.Add(new Comparator(match.Groups["operator"].Value, version));
        }

        range = new VersionRange(string.Join(' ', tokens), comparators);
        return true;
    }

    public override string ToString() => Expression;

    private static bool TryParseVersion(string value, out Version version)
    {
        version = null!;
        var segments = value.Split('.');
        if (segments.Length is < 1 or > 4 ||
            segments.Any(segment =>
                segment.Length is 0 or > 9 ||
                !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        if (!Version.TryParse(value, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    [GeneratedRegex("^(?<operator>>=|<=|>|<|==|=)(?<version>[0-9]+(?:\\.[0-9]+){0,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex ComparatorPattern();

    private sealed record Comparator(string Operator, Version Version)
    {
        public bool Matches(Version candidate)
        {
            var comparison = candidate.CompareTo(Version);
            return Operator switch
            {
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                "=" or "==" => comparison == 0,
                _ => false,
            };
        }
    }
}
