using System.Text.RegularExpressions;

namespace KeyRadar.Windows.Hardware;

public static partial class HidDeviceIdentityParser
{
    public static HidDeviceDescriptor? Parse(string? rawDeviceName, HidDeviceKind kind)
    {
        if (string.IsNullOrWhiteSpace(rawDeviceName))
        {
            return null;
        }

        var match = VidPidPattern().Match(rawDeviceName);
        if (!match.Success)
        {
            return null;
        }

        var vendorId = match.Groups["vid"].Value.ToUpperInvariant();
        var productId = match.Groups["pid"].Value.ToUpperInvariant();
        var vendorName = vendorId switch
        {
            "046D" => "Logitech",
            "1532" => "Razer",
            "1B1C" => "Corsair",
            _ => $"VID_{vendorId}",
        };
        return new HidDeviceDescriptor(
            vendorId,
            productId,
            vendorName,
            $"VID_{vendorId} · PID_{productId}",
            kind);
    }

    [GeneratedRegex("VID_(?<vid>[0-9A-F]{4}).*PID_(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPidPattern();
}
