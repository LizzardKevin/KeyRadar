namespace KeyRadar.Rules.Packs;

public static class RulePackStore
{
    public const string ActiveFileName = "active.krpack";
    public const string PreviousFileName = "previous.krpack";

    public static RulePackStoreResult Activate(
        string candidatePath,
        string rulesDirectory,
        ReadOnlySpan<byte> publicKeyBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);

        var candidateFullPath = Path.GetFullPath(candidatePath);
        var rulesRoot = Path.GetFullPath(rulesDirectory);
        try
        {
            var candidate = Read(candidateFullPath, publicKeyBytes);
            if (!candidate.IsSuccess)
            {
                return RulePackStoreResult.Failure(candidate.Message);
            }

            Directory.CreateDirectory(rulesRoot);
            var activePath = Path.Combine(rulesRoot, ActiveFileName);
            var previousPath = Path.Combine(rulesRoot, PreviousFileName);
            var stagedPath = Path.Combine(rulesRoot, $"activation-{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(candidateFullPath, stagedPath, overwrite: false);
                var staged = Read(stagedPath, publicKeyBytes);
                if (!staged.IsSuccess)
                {
                    return RulePackStoreResult.Failure("The staged rule pack changed during activation.");
                }

                if (File.Exists(activePath))
                {
                    File.Delete(previousPath);
                    File.Replace(stagedPath, activePath, previousPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(stagedPath, activePath);
                }

                return RulePackStoreResult.Success(
                    staged.Pack!.Version,
                    $"Rule pack {staged.Pack.Version} is active.");
            }
            finally
            {
                TryDelete(stagedPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackStoreResult.Failure("The verified rule pack could not be activated safely.");
        }
    }

    public static RulePackStoreResult Rollback(
        string rulesDirectory,
        ReadOnlySpan<byte> publicKeyBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);

        var rulesRoot = Path.GetFullPath(rulesDirectory);
        var activePath = Path.Combine(rulesRoot, ActiveFileName);
        var previousPath = Path.Combine(rulesRoot, PreviousFileName);
        var swappedPath = Path.Combine(rulesRoot, "rollback-swapped.tmp");
        try
        {
            if (!File.Exists(previousPath))
            {
                return RulePackStoreResult.Failure("No previous verified rule pack is available.");
            }

            var previous = Read(previousPath, publicKeyBytes);
            if (!previous.IsSuccess)
            {
                return RulePackStoreResult.Failure("The previous rule pack failed signature validation.");
            }

            File.Delete(swappedPath);
            if (File.Exists(activePath))
            {
                File.Replace(previousPath, activePath, swappedPath, ignoreMetadataErrors: true);
                File.Move(swappedPath, previousPath);
            }
            else
            {
                File.Move(previousPath, activePath);
            }

            return RulePackStoreResult.Success(
                previous.Pack!.Version,
                $"Rolled back to rule pack {previous.Pack.Version}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackStoreResult.Failure("The rule-pack rollback could not be completed safely.");
        }
        finally
        {
            TryDelete(swappedPath);
        }
    }

    private static RulePackReadResult Read(string path, ReadOnlySpan<byte> publicKeyBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.SequentialScan);
        return RulePackReader.Read(stream, publicKeyBytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
