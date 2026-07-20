using KeyRadar.Rules.Packs;

namespace KeyRadar.Rules.Catalog;

public static class SignedRuleCatalogLoader
{
    public static IReadOnlyList<ApplicationRuleSet> Load(
        string? activePackPath,
        ReadOnlySpan<byte> publicKeyBytes,
        out RulePackReadResult? packResult)
    {
        var builtIn = BuiltInRuleCatalog.Load();
        packResult = null;
        if (string.IsNullOrWhiteSpace(activePackPath) || !File.Exists(activePackPath))
        {
            return builtIn;
        }

        try
        {
            using var stream = new FileStream(
                activePackPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            packResult = RulePackReader.Read(stream, publicKeyBytes);
            return packResult.IsSuccess
                ? RuleCatalogComposer.Compose(builtIn, packResult.Pack!.Applications)
                : builtIn;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            packResult = RulePackReadResult.Failure(
                RulePackReadError.ValidationFailed,
                "The active rule pack could not be read. Built-in rules remain active.");
            return builtIn;
        }
    }
}
