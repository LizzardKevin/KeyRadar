using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hardware;

public enum HidDeviceKind
{
    Keyboard,
    Mouse,
    Other,
}

public sealed record HidDeviceDescriptor(
    string VendorId,
    string ProductId,
    string VendorName,
    string ModelName,
    HidDeviceKind Kind,
    bool IsConnected = true);

public sealed record HardwareSoftwareDescriptor(
    string Id,
    string DisplayName,
    bool IsRunning,
    bool HasMatchingDevice,
    int? ProcessId);

public enum HardwareProfileReadStatus
{
    Active,
    Inactive,
    UnableToRead,
}

public enum HardwareMappingTargetKind
{
    SingleKey,
    Hotkey,
    SystemCommand,
    LaunchApplication,
    ApplicationAction,
    MacroSequence,
}

public sealed record HardwareMapping(
    string PhysicalTrigger,
    HardwareMappingTargetKind TargetKind,
    HotkeyGesture? TargetGesture,
    string DisplayTarget,
    bool ParticipatesInConflict);

public sealed record HardwareProfileDescriptor(
    string SoftwareId,
    string DeviceName,
    string ProfileName,
    HardwareProfileReadStatus Status,
    bool IsOnboardMemory,
    string? Slot,
    IReadOnlyList<HardwareMapping> Mappings,
    string Evidence);

public sealed record HardwareEnvironmentSnapshot(
    IReadOnlyList<HidDeviceDescriptor> Devices,
    IReadOnlyList<HardwareSoftwareDescriptor> Software,
    IReadOnlyList<HardwareProfileDescriptor> Profiles);
