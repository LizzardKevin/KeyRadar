using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class NormalHotkeyPresentationPolicyTests
{
    [Theory]
    [InlineData(HotkeyProbeAvailability.AvailableAtScanTime)]
    [InlineData(HotkeyProbeAvailability.SystemReserved)]
    [InlineData(HotkeyProbeAvailability.ProbeError)]
    public void Does_not_expose_raw_non_occupancy_probe_state_in_the_curated_inventory(
        HotkeyProbeAvailability availability) =>
        Assert.False(NormalHotkeyPresentationPolicy.ShouldShowAvailability(availability));

    [Fact]
    public void Retains_occupied_state_for_the_curated_unknown_occupancy_entry() =>
        Assert.True(NormalHotkeyPresentationPolicy.ShouldShowAvailability(HotkeyProbeAvailability.Occupied));

    [Fact]
    public void Does_not_expose_a_missing_probe_state_in_the_curated_inventory() =>
        Assert.False(NormalHotkeyPresentationPolicy.ShouldShowAvailability(null));
}
