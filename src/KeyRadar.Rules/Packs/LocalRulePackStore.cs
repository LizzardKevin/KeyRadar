namespace KeyRadar.Rules.Packs;

public static class LocalRulePackStore
{
    public const string ActiveFileName = "local.krpack";
    public const string PreviousFileName = "local.previous.krpack";

    public static RulePackStoreResult Import(string candidatePath, string rulesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);

        var candidateFullPath = Path.GetFullPath(candidatePath);
        var rulesRoot = Path.GetFullPath(rulesDirectory);
        try
        {
            var candidate = Read(candidateFullPath);
            if (!candidate.IsSuccess)
            {
                return RulePackStoreResult.Failure(candidate.Message);
            }

            Directory.CreateDirectory(rulesRoot);
            var activePath = Path.Combine(rulesRoot, ActiveFileName);
            var previousPath = Path.Combine(rulesRoot, PreviousFileName);
            var stagedPath = Path.Combine(rulesRoot, $"local-import-{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(candidateFullPath, stagedPath, overwrite: false);
                var staged = Read(stagedPath);
                if (!staged.IsSuccess)
                {
                    return RulePackStoreResult.Failure("The local rule pack changed during import.");
                }

                if (File.Exists(activePath))
                {
                    TryDelete(previousPath);
                    File.Replace(stagedPath, activePath, previousPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(stagedPath, activePath);
                }

                return RulePackStoreResult.Success(
                    staged.Pack!.Version,
                    $"Unsigned local rule pack {staged.Pack.Version} is active.");
            }
            finally
            {
                TryDelete(stagedPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackStoreResult.Failure("The local rule pack could not be imported safely.");
        }
    }

    public static RulePackStoreResult Remove(string rulesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);
        var rulesRoot = Path.GetFullPath(rulesDirectory);
        var activePath = Path.Combine(rulesRoot, ActiveFileName);
        var previousPath = Path.Combine(rulesRoot, PreviousFileName);
        try
        {
            if (!File.Exists(activePath))
            {
                return RulePackStoreResult.Failure("No unsigned local rule pack is active.");
            }

            TryDelete(previousPath);
            File.Move(activePath, previousPath);
            return RulePackStoreResult.Success("none", "The unsigned local rule pack was removed.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackStoreResult.Failure("The local rule pack could not be removed safely.");
        }
    }

    public static RulePackStoreResult Export(string rulesDirectory, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourcePath = Path.Combine(Path.GetFullPath(rulesDirectory), ActiveFileName);
        var destinationFullPath = Path.GetFullPath(destinationPath);
        try
        {
            if (!File.Exists(sourcePath))
            {
                return RulePackStoreResult.Failure("No local rule pack is active.");
            }

            var package = Read(sourcePath);
            if (!package.IsSuccess)
            {
                return RulePackStoreResult.Failure("The active local rule pack is no longer valid.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
            File.Copy(sourcePath, destinationFullPath, overwrite: true);
            return RulePackStoreResult.Success(package.Pack!.Version, "The unsigned local rule pack was exported.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RulePackStoreResult.Failure("The local rule pack could not be exported.");
        }
    }

    private static RulePackReadResult Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return RulePackReader.ReadUnsignedLocal(stream);
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
