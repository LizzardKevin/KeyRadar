namespace KeyRadar.Rules.Packs;

public sealed record RulePackValidationResult(bool IsValid, RulePackValidationError Error, string Message)
{
    public static RulePackValidationResult Success() => new(true, RulePackValidationError.None, string.Empty);

    public static RulePackValidationResult Failure(RulePackValidationError error, string message) =>
        new(false, error, message);
}
