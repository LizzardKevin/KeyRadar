namespace KeyRadar.Rules.Packs;

public sealed record RulePackStoreResult(bool IsSuccess, string Message, string? ActiveVersion)
{
    public static RulePackStoreResult Success(string version, string message) =>
        new(true, message, version);

    public static RulePackStoreResult Failure(string message) =>
        new(false, message, null);
}
