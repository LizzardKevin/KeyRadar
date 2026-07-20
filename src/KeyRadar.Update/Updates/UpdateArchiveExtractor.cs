using System.IO.Compression;

namespace KeyRadar.Updater.Updates;

public static class UpdateArchiveExtractor
{
    private const int MaximumEntries = 2048;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private static readonly string[] DisallowedExtensions = [".bat", ".cmd", ".js", ".ps1", ".vbs", ".wsf"];

    public static UpdateArchiveExtractionResult Extract(string archivePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        try
        {
            var archiveFullPath = Path.GetFullPath(archivePath);
            var destinationFullPath = Path.GetFullPath(destinationDirectory);
            if (!File.Exists(archiveFullPath))
            {
                throw new FileNotFoundException("The update archive was not found.", archiveFullPath);
            }

            if (IsRootPath(destinationFullPath) ||
                (Directory.Exists(destinationFullPath) && Directory.EnumerateFileSystemEntries(destinationFullPath).Any()))
            {
                return UpdateArchiveExtractionResult.Failure(
                    UpdateArchiveValidationError.DestinationNotEmpty,
                    "The update staging directory must be empty and cannot be a filesystem root.");
            }

            using var archive = ZipFile.OpenRead(archiveFullPath);
            var structuralResult = ValidateEntries(archive);
            if (!structuralResult.IsValid)
            {
                return structuralResult;
            }

            Directory.CreateDirectory(destinationFullPath);
            var destinationPrefix = destinationFullPath + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(
                    Path.Combine(destinationFullPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return UpdateArchiveExtractionResult.Failure(
                        UpdateArchiveValidationError.UnsafeEntry,
                        "The archive contains a path outside the staging directory.");
                }

                var parent = Path.GetDirectoryName(destinationPath);
                if (parent is not null)
                {
                    Directory.CreateDirectory(parent);
                }

                using var source = entry.Open();
                using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
            }

            return UpdateArchiveExtractionResult.Success();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return UpdateArchiveExtractionResult.Failure(
                UpdateArchiveValidationError.MalformedArchive,
                "The update archive could not be safely extracted.");
        }
    }

    private static UpdateArchiveExtractionResult ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumEntries)
        {
            return UpdateArchiveExtractionResult.Failure(
                UpdateArchiveValidationError.PackageTooLarge,
                "The update archive has no files or exceeds the entry limit.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        var hasApplication = false;
        var hasUpdater = false;
        foreach (var entry in archive.Entries)
        {
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes)
            {
                return UpdateArchiveExtractionResult.Failure(
                    UpdateArchiveValidationError.PackageTooLarge,
                    "The expanded update archive exceeds 1 GiB.");
            }

            if (!paths.Add(entry.FullName) || !IsSafeFileEntry(entry))
            {
                return UpdateArchiveExtractionResult.Failure(
                    UpdateArchiveValidationError.UnsafeEntry,
                    "The update archive contains an unsafe, reserved, duplicate, or script entry.");
            }

            hasApplication |= entry.FullName.Equals("KeyRadar.exe", StringComparison.OrdinalIgnoreCase);
            hasUpdater |= entry.FullName.Equals("KeyRadar.Updater.exe", StringComparison.OrdinalIgnoreCase);
        }

        return hasApplication && hasUpdater
            ? UpdateArchiveExtractionResult.Success()
            : UpdateArchiveExtractionResult.Failure(
                UpdateArchiveValidationError.MissingApplication,
                "The update archive is missing KeyRadar.exe or KeyRadar.Updater.exe.");
    }

    private static bool IsSafeFileEntry(ZipArchiveEntry entry)
    {
        var path = entry.FullName;
        if (string.IsNullOrWhiteSpace(path) ||
            path.EndsWith('/') ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':') ||
            IsSymbolicLink(entry))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..") ||
            segments[0].Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !DisallowedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        return ((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink;
    }

    private static bool IsRootPath(string path) =>
        string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
