namespace KeyRadar.Rules.Packs;

public enum RulePackReadError
{
    None,
    ValidationFailed,
    InvalidRule,
    DuplicateApplication,
}

public sealed record RulePackReadResult(
    bool IsSuccess,
    RulePackReadError Error,
    string Message,
    RulePack? Pack)
{
    public static RulePackReadResult Success(RulePack pack) =>
        new(true, RulePackReadError.None, string.Empty, pack);

    public static RulePackReadResult Failure(RulePackReadError error, string message) =>
        new(false, error, message, null);
}
