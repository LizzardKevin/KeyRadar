namespace KeyRadar.Updater.Updates;

public sealed record FileUpdateResult(bool Succeeded, bool RolledBack, string Message)
{
    public static FileUpdateResult Success() => new(true, false, string.Empty);

    public static FileUpdateResult Failure(bool rolledBack, string message) => new(false, rolledBack, message);
}
