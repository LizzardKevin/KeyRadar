using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class NativeHotkeyReprobeServiceTests
{
    [Theory]
    [InlineData("0,0", HotkeyProbeAvailability.AvailableAtScanTime, null)]
    [InlineData("1,1409", HotkeyProbeAvailability.Occupied, 1409)]
    [InlineData("2,1422", HotkeyProbeAvailability.SystemReserved, 1422)]
    [InlineData("3,5", HotkeyProbeAvailability.ProbeError, 5)]
    public void Parses_bounded_native_probe_results(
        string value,
        HotkeyProbeAvailability expectedAvailability,
        int? expectedError)
    {
        Assert.True(NativeHotkeyReprobeService.TryParseResult(value, out var availability, out var error));
        Assert.Equal(expectedAvailability, availability);
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4,0")]
    [InlineData("1")]
    [InlineData("occupied,1409")]
    public void Rejects_invalid_native_probe_results(string value)
    {
        Assert.False(NativeHotkeyReprobeService.TryParseResult(value, out _, out _));
    }
}
