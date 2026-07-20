using KeyRadar.Windows.Hardware;

namespace KeyRadar.Windows.Tests.Hardware;

public sealed class HidDeviceIdentityParserTests
{
    [Theory]
    [InlineData(@"\\?\HID#VID_046D&PID_C33C&MI_01#7&abc", "046D", "C33C", "Logitech")]
    [InlineData(@"\\?\HID#VID_1532&PID_026B#8&abc", "1532", "026B", "Razer")]
    [InlineData(@"\\?\HID#VID_1B1C&PID_1B7C#9&abc", "1B1C", "1B7C", "Corsair")]
    public void Parser_keeps_only_non_path_device_identity(
        string rawPath,
        string vendorId,
        string productId,
        string vendorName)
    {
        var result = HidDeviceIdentityParser.Parse(rawPath, HidDeviceKind.Keyboard);

        Assert.NotNull(result);
        Assert.Equal(vendorId, result.VendorId);
        Assert.Equal(productId, result.ProductId);
        Assert.Equal(vendorName, result.VendorName);
        Assert.DoesNotContain("\\", result.ModelName, StringComparison.Ordinal);
    }
}
