namespace KeyRadar.Rules;

public sealed class ApplicationVariantMatcher
{
    public ApplicationVariantMatchResult Match(
        ApplicationIdentity identity,
        IEnumerable<ApplicationVariantRule> variants)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ExecutableName);

        var candidates = variants
            .Where(variant => variant.Match.Executables.Contains(
                identity.ExecutableName,
                StringComparer.OrdinalIgnoreCase))
            .Select(variant => Evaluate(identity, variant))
            .Where(candidate => candidate is not null)
            .Cast<ApplicationVariantCandidate>()
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Variant.ApplicationId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Variant.VariantId, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            return ApplicationVariantMatchResult.NoMatch;
        }

        var bestScore = candidates[0].Score;
        var best = candidates.Where(candidate => candidate.Score == bestScore).ToArray();
        if (best.Length > 1)
        {
            return new ApplicationVariantMatchResult(VariantMatchKind.Ambiguous, null, best);
        }

        var selected = best[0];
        var hasSpecificEvidence = selected.Evidence.Any(evidence =>
            evidence.Signal != "executable" && evidence.State == VariantEvidenceState.Match);
        return new ApplicationVariantMatchResult(
            hasSpecificEvidence ? VariantMatchKind.Exact : VariantMatchKind.Suspected,
            selected.Variant,
            candidates);
    }

    private static ApplicationVariantCandidate? Evaluate(
        ApplicationIdentity identity,
        ApplicationVariantRule variant)
    {
        var score = 10;
        var evidence = new List<VariantMatchEvidence>
        {
            new("executable", VariantEvidenceState.Match, identity.ExecutableName, 10),
        };

        if (!EvaluateList(
                "packageFamilyName",
                identity.PackageFamilyName,
                variant.Match.PackageFamilyNames,
                50,
                evidence,
                ref score))
        {
            return null;
        }

        if (!EvaluatePublisher(identity, variant.Match.Publishers, evidence, ref score))
        {
            return null;
        }

        if (!EvaluateVersion(identity.Version, variant.Match.VersionRange, evidence, ref score))
        {
            return null;
        }

        if (!EvaluateScalar(
                "distribution",
                identity.Distribution,
                variant.Match.Distribution,
                20,
                evidence,
                ref score))
        {
            return null;
        }

        return new ApplicationVariantCandidate(variant, score, evidence);
    }

    private static bool EvaluatePublisher(
        ApplicationIdentity identity,
        IReadOnlyList<string> expected,
        List<VariantMatchEvidence> evidence,
        ref int score)
    {
        if (expected.Count == 0)
        {
            return true;
        }

        if (identity.Publisher is not null && expected.Contains(identity.Publisher, StringComparer.OrdinalIgnoreCase))
        {
            score += 40;
            evidence.Add(new("publisher", VariantEvidenceState.Match, identity.Publisher, 40));
            return true;
        }

        if (identity.CompanyName is not null && expected.Contains(identity.CompanyName, StringComparer.OrdinalIgnoreCase))
        {
            score += 20;
            evidence.Add(new("companyName", VariantEvidenceState.Match, identity.CompanyName, 20));
            return true;
        }

        if (identity.Publisher is null && identity.CompanyName is null)
        {
            evidence.Add(new("publisher", VariantEvidenceState.Missing, "Publisher evidence is unavailable.", 0));
            return true;
        }

        evidence.Add(new("publisher", VariantEvidenceState.Mismatch, "Publisher does not match the variant.", 0));
        return false;
    }

    private static bool EvaluateVersion(
        string? actual,
        VersionRange? expected,
        List<VariantMatchEvidence> evidence,
        ref int score)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null || !TryParseProductVersion(actual, out var parsed))
        {
            evidence.Add(new("version", VariantEvidenceState.Missing, "Version evidence is unavailable.", 0));
            return true;
        }

        if (!expected.Contains(parsed))
        {
            evidence.Add(new("version", VariantEvidenceState.Mismatch, actual, 0));
            return false;
        }

        score += 30;
        evidence.Add(new("version", VariantEvidenceState.Match, actual, 30));
        return true;
    }

    private static bool EvaluateList(
        string signal,
        string? actual,
        IReadOnlyList<string> expected,
        int weight,
        List<VariantMatchEvidence> evidence,
        ref int score)
    {
        if (expected.Count == 0)
        {
            return true;
        }

        if (actual is null)
        {
            evidence.Add(new(signal, VariantEvidenceState.Missing, $"{signal} evidence is unavailable.", 0));
            return true;
        }

        if (!expected.Contains(actual, StringComparer.OrdinalIgnoreCase))
        {
            evidence.Add(new(signal, VariantEvidenceState.Mismatch, actual, 0));
            return false;
        }

        score += weight;
        evidence.Add(new(signal, VariantEvidenceState.Match, actual, weight));
        return true;
    }

    private static bool EvaluateScalar(
        string signal,
        string? actual,
        string? expected,
        int weight,
        List<VariantMatchEvidence> evidence,
        ref int score)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            evidence.Add(new(signal, VariantEvidenceState.Missing, $"{signal} evidence is unavailable.", 0));
            return true;
        }

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new(signal, VariantEvidenceState.Mismatch, actual, 0));
            return false;
        }

        score += weight;
        evidence.Add(new(signal, VariantEvidenceState.Match, actual, weight));
        return true;
    }

    private static bool TryParseProductVersion(string value, out Version version)
    {
        version = null!;
        var numericPrefix = new string(value
            .TakeWhile(character => char.IsAsciiDigit(character) || character == '.')
            .ToArray())
            .TrimEnd('.');
        if (!Version.TryParse(numericPrefix, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }
}
