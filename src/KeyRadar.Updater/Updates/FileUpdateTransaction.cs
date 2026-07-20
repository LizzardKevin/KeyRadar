namespace KeyRadar.Updater.Updates;

public static class FileUpdateTransaction
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static FileUpdateResult Apply(string sourceDirectory, string targetDirectory, string backupDirectory)
    {
        var source = ResolveExistingDirectory(sourceDirectory, nameof(sourceDirectory));
        var target = ResolveExistingDirectory(targetDirectory, nameof(targetDirectory));
        var backup = Path.GetFullPath(backupDirectory);

        EnsureDistinctPaths(source, target, backup);
        if (Directory.Exists(backup) || File.Exists(backup))
        {
            throw new ArgumentException("The backup path must not already exist.", nameof(backupDirectory));
        }

        var updateFiles = Directory
            .EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => new UpdateFile(path, GetSafeRelativePath(source, path)))
            .Where(file => !IsPreservedDataPath(file.RelativePath))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var replacedFiles = new List<UpdateFile>();
        var createdFiles = new List<string>();

        try
        {
            Directory.CreateDirectory(backup);
            foreach (var updateFile in updateFiles)
            {
                var destination = ResolveWithin(target, updateFile.RelativePath);
                if (!File.Exists(destination))
                {
                    continue;
                }

                var backupPath = ResolveWithin(backup, updateFile.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, overwrite: false);
            }

            foreach (var updateFile in updateFiles)
            {
                var destination = ResolveWithin(target, updateFile.RelativePath);
                var existed = File.Exists(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(updateFile.SourcePath, destination, overwrite: true);

                if (existed)
                {
                    replacedFiles.Add(updateFile);
                }
                else
                {
                    createdFiles.Add(destination);
                }
            }

            return FileUpdateResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var rolledBack = TryRollback(target, backup, replacedFiles, createdFiles);
            return FileUpdateResult.Failure(rolledBack, "The update could not be applied; the previous version was restored.");
        }
    }

    public static bool Rollback(string sourceDirectory, string targetDirectory, string backupDirectory)
    {
        var source = ResolveExistingDirectory(sourceDirectory, nameof(sourceDirectory));
        var target = ResolveExistingDirectory(targetDirectory, nameof(targetDirectory));
        var backup = ResolveExistingDirectory(backupDirectory, nameof(backupDirectory));
        EnsureDistinctPaths(source, target, backup);

        try
        {
            var updateFiles = Directory
                .EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Select(path => new UpdateFile(path, GetSafeRelativePath(source, path)))
                .Where(file => !IsPreservedDataPath(file.RelativePath))
                .OrderByDescending(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var updateFile in updateFiles)
            {
                var destination = ResolveWithin(target, updateFile.RelativePath);
                var backupPath = ResolveWithin(backup, updateFile.RelativePath);
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, overwrite: true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryRollback(
        string target,
        string backup,
        IEnumerable<UpdateFile> replacedFiles,
        IEnumerable<string> createdFiles)
    {
        try
        {
            foreach (var createdFile in createdFiles)
            {
                if (File.Exists(createdFile))
                {
                    File.Delete(createdFile);
                }
            }

            foreach (var replacedFile in replacedFiles.Reverse())
            {
                var backupPath = ResolveWithin(backup, replacedFile.RelativePath);
                var destination = ResolveWithin(target, replacedFile.RelativePath);
                File.Copy(backupPath, destination, overwrite: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ResolveExistingDirectory(string path, string parameterName)
    {
        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"The directory supplied for {parameterName} does not exist.");
        }

        return resolved;
    }

    private static void EnsureDistinctPaths(params string[] paths)
    {
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new ArgumentException("Source, target, and backup paths must be distinct.");
        }

        if (paths.Any(path => Path.GetPathRoot(path)?.Equals(path, PathComparison) == true))
        {
            throw new ArgumentException("A filesystem root cannot be used for an update transaction.");
        }
    }

    private static string GetSafeRelativePath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("An update file resolves outside the package root.");
        }

        return relativePath;
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new InvalidDataException("An update path resolves outside the intended directory.");
        }

        return resolved;
    }

    private static bool IsPreservedDataPath(string relativePath)
    {
        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return firstSegment.Equals("data", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UpdateFile(string SourcePath, string RelativePath);
}
