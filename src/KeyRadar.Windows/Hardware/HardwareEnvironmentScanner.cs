using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Hardware;

public static class HardwareEnvironmentScanner
{
    private static readonly Definition[] Definitions =
    [
        new("logitech-g-hub", "Logitech G HUB", ["lghub.exe", "lghub_agent.exe"], ["046D"]),
        new("logi-options-plus", "Logi Options+", ["logioptionsplus.exe", "logioptionsplus_agent.exe"], ["046D"]),
        new("razer-synapse", "Razer Synapse", ["RazerAppEngine.exe", "Razer Synapse 3.exe"], ["1532"]),
        new("corsair-icue", "Corsair iCUE", ["iCUE.exe", "iCUE Launcher.exe"], ["1B1C"]),
    ];

    public static HardwareEnvironmentSnapshot Match(
        IReadOnlyList<ProcessDescriptor> processes,
        IReadOnlyList<HidDeviceDescriptor> devices)
    {
        var software = Definitions
            .Select(definition =>
            {
                var process = processes.FirstOrDefault(item => definition.Executables.Contains(
                    item.ExecutableName,
                    StringComparer.OrdinalIgnoreCase));
                var hasDevice = devices.Any(device => definition.VendorIds.Contains(
                    device.VendorId,
                    StringComparer.OrdinalIgnoreCase));
                return new HardwareSoftwareDescriptor(
                    definition.Id,
                    definition.DisplayName,
                    process is not null,
                    hasDevice,
                    process?.Id);
            })
            .Where(item => item.IsRunning || item.HasMatchingDevice)
            .ToArray();

        var profiles = software
            .Where(item => item.IsRunning && item.HasMatchingDevice)
            .Select(item => new HardwareProfileDescriptor(
                item.Id,
                devices.First(device => Definitions
                    .First(definition => definition.Id == item.Id)
                    .VendorIds.Contains(device.VendorId, StringComparer.OrdinalIgnoreCase)).ModelName,
                "当前 Profile",
                HardwareProfileReadStatus.UnableToRead,
                IsOnboardMemory: false,
                Slot: null,
                Mappings: [],
                Evidence: "硬件软件正在运行且设备已连接；厂商 Profile 无安全公开读取接口"))
            .ToArray();

        return new HardwareEnvironmentSnapshot(devices, software, profiles);
    }

    private sealed record Definition(
        string Id,
        string DisplayName,
        IReadOnlyList<string> Executables,
        IReadOnlyList<string> VendorIds);
}
