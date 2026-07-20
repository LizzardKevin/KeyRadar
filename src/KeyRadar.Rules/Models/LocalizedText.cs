using System.Collections.ObjectModel;

namespace KeyRadar.Rules;

public sealed class LocalizedText
{
    private static readonly StringComparer LocaleComparer = StringComparer.OrdinalIgnoreCase;

    public LocalizedText(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is 0 or > 8)
        {
            throw new ArgumentException("Localized text must contain between one and eight values.", nameof(values));
        }

        var normalized = new Dictionary<string, string>(LocaleComparer);
        foreach (var (locale, value) in values)
        {
            if (!IsLocale(locale) || string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl))
            {
                throw new ArgumentException("Localized text contains an invalid locale or value.", nameof(values));
            }

            normalized.Add(locale, value.Trim());
        }

        Values = new ReadOnlyDictionary<string, string>(normalized);
    }

    public IReadOnlyDictionary<string, string> Values { get; }

    public string Resolve(string? locale)
    {
        if (!string.IsNullOrWhiteSpace(locale) && Values.TryGetValue(locale, out var exact))
        {
            return exact;
        }

        return Values.TryGetValue("zh-CN", out var chinese)
            ? chinese
            : Values.TryGetValue("en-US", out var english)
                ? english
                : Values.OrderBy(item => item.Key, LocaleComparer).First().Value;
    }

    private static bool IsLocale(string value)
    {
        if (value.Length is < 2 or > 16 || value[0] is '-' || value[^1] is '-')
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');
    }
}
