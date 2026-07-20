using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Hardware;

namespace KeyRadar.Windows.Tests.Hardware;

public sealed class HardwareEnvironmentScannerTests
{
    [Fact]
    public void Vendor_software_requires_a_running_process_and_matching_connected_device()
    {
        var devices = new[]
        {
            new HidDeviceDescriptor("046D", "C33C", "Logitech", "VID_046D&PID_C33C", HidDeviceKind.Keyboard),
        };
        var processes = new[]
        {
            new ProcessDescriptor(42, "lghub", "lghub.exe"),
        };

        var result = HardwareEnvironmentScanner.Match(processes, devices);

        var software = Assert.Single(result.Software, item => item.Id == "logitech-g-hub");
        Assert.Equal("logitech-g-hub", software.Id);
        Assert.True(software.IsRunning);
        Assert.True(software.HasMatchingDevice);
    }

    [Fact]
    public void Unsupported_or_inactive_profiles_are_not_invented()
    {
        var result = HardwareEnvironmentScanner.Match(
            [new ProcessDescriptor(42, "photoshop", "Photoshop.exe")],
            []);

        Assert.Empty(result.Software);
        Assert.Empty(result.Profiles);
    }
}
