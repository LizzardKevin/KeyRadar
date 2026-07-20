using System.Text.Json;
using System.Text.Json.Serialization;
using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hardware;

public sealed record ImportedHardwareProfileResult(
    bool IsSuccess,
    string Code,
    HardwareProfileDescriptor? Profile = null);

public sealed class ImportedHardwareProfileStore(string? storagePath = null)
{
    private const long MaximumFileBytes = 1024 * 1024;
    private const int MaximumMappings = 256;
    private readonly string _storagePath = Path.GetFullPath(storagePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyRadar",
        "hardware",
        "imported-profile.json"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<ImportedHardwareProfileResult> ImportAsync(
        string selectedPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        try
        {
            var source = new FileInfo(Path.GetFullPath(selectedPath));
            if (!source.Exists || source.Length is 0 or > MaximumFileBytes)
            {
                return new(false, "file-size");
            }

            await using var stream = new FileStream(
                source.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ImportedProfileDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (!TryConvert(document, out var profile, out var sanitized))
            {
                return new(false, "invalid-format");
            }

            var destinationDirectory = Path.GetDirectoryName(_storagePath)!;
            Directory.CreateDirectory(destinationDirectory);
            var temporaryPath = Path.Combine(destinationDirectory, $"import-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(
                    temporaryPath,
                    JsonSerializer.SerializeToUtf8Bytes(sanitized, JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, _storagePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            return new(true, "ok", profile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, "read-error");
        }
    }

    public HardwareProfileDescriptor? Load()
    {
        try
        {
            var file = new FileInfo(_storagePath);
            if (!file.Exists || file.Length is 0 or > MaximumFileBytes) return null;
            using var stream = new FileStream(_storagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = JsonSerializer.Deserialize<ImportedProfileDocument>(stream, JsonOptions);
            return TryConvert(document, out var profile, out _) ? profile : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool TryConvert(
        ImportedProfileDocument? document,
        out HardwareProfileDescriptor? profile,
        out ImportedProfileDocument? sanitized)
    {
        profile = null;
        sanitized = null;
        if (document is null ||
            document.SchemaVersion != 1 ||
            !IsIdentifier(document.SoftwareId) ||
            !IsDisplayText(document.DeviceName, 120) ||
            !IsDisplayText(document.ProfileName, 120) ||
            document.Mappings is null ||
            document.Mappings.Count is 0 or > MaximumMappings)
        {
            return false;
        }

        var mappings = new List<HardwareMapping>();
        var sanitizedMappings = new List<ImportedMappingDocument>();
        foreach (var mapping in document.Mappings)
        {
            if (!IsPhysicalTrigger(mapping.PhysicalTrigger) ||
                !TryReadTarget(mapping, out var converted, out var sanitizedMapping))
            {
                return false;
            }

            mappings.Add(converted);
            sanitizedMappings.Add(sanitizedMapping);
        }

        var slot = IsDisplayText(document.Slot, 40) ? document.Slot!.Trim() : null;
        profile = new HardwareProfileDescriptor(
            document.SoftwareId!.Trim(),
            document.DeviceName!.Trim(),
            document.ProfileName!.Trim(),
            document.IsCurrent ? HardwareProfileReadStatus.Active : HardwareProfileReadStatus.Inactive,
            document.IsOnboardMemory,
            slot,
            mappings,
            "User-imported declarative hardware profile; not live-verified",
            IsUserDeclared: true);
        sanitized = document with
        {
            SoftwareId = profile.SoftwareId,
            DeviceName = profile.DeviceName,
            ProfileName = profile.ProfileName,
            Slot = slot,
            Mappings = sanitizedMappings,
        };
        return true;
    }

    private static bool TryReadTarget(
        ImportedMappingDocument mapping,
        out HardwareMapping converted,
        out ImportedMappingDocument sanitized)
    {
        converted = default!;
        sanitized = default!;
        HotkeyGesture? gesture = null;
        if (!string.IsNullOrWhiteSpace(mapping.TargetGesture))
        {
            if (!HotkeyGesture.TryParse(mapping.TargetGesture, out var parsed)) return false;
            gesture = parsed;
        }

        if (mapping.TargetKind is HardwareMappingTargetKind.Hotkey or HardwareMappingTargetKind.SingleKey && gesture is null)
        {
            return false;
        }

        var displayTarget = mapping.TargetKind switch
        {
            HardwareMappingTargetKind.Hotkey or HardwareMappingTargetKind.SingleKey => gesture!.Value.ToString(),
            HardwareMappingTargetKind.MacroSequence => "Macro sequence (content hidden)",
            HardwareMappingTargetKind.LaunchApplication => "Launch application (path hidden)",
            _ when IsDisplayText(mapping.DisplayTarget, 120) => mapping.DisplayTarget!.Trim(),
            _ => mapping.TargetKind.ToString(),
        };
        var participates = mapping.TargetKind == HardwareMappingTargetKind.Hotkey && gesture is not null;
        converted = new HardwareMapping(
            mapping.PhysicalTrigger!.Trim(),
            mapping.TargetKind,
            gesture,
            displayTarget,
            participates);
        sanitized = mapping with
        {
            PhysicalTrigger = converted.PhysicalTrigger,
            TargetGesture = gesture?.ToString(),
            DisplayTarget = displayTarget,
        };
        return true;
    }

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 80 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsDisplayText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => !char.IsControl(character));

    private static bool IsPhysicalTrigger(string? value) =>
        IsDisplayText(value, 40) &&
        value!.All(character => char.IsLetterOrDigit(character) || character is ' ' or '+' or '-' or '_' or '.');

    private sealed record ImportedProfileDocument(
        int SchemaVersion,
        string? SoftwareId,
        string? DeviceName,
        string? ProfileName,
        bool IsCurrent,
        bool IsOnboardMemory,
        string? Slot,
        IReadOnlyList<ImportedMappingDocument>? Mappings);

    private sealed record ImportedMappingDocument(
        string? PhysicalTrigger,
        HardwareMappingTargetKind TargetKind,
        string? TargetGesture,
        string? DisplayTarget);
}
