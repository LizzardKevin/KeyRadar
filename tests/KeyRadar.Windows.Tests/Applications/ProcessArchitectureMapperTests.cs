using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class ProcessArchitectureMapperTests
{
    [Theory]
    [InlineData(0x014C, 0x8664, ProcessArchitecture.X86)]
    [InlineData(0x8664, 0x8664, ProcessArchitecture.X64)]
    [InlineData(0xAA64, 0xAA64, ProcessArchitecture.Arm64)]
    [InlineData(0x0000, 0x8664, ProcessArchitecture.X64)]
    [InlineData(0x0000, 0xAA64, ProcessArchitecture.Arm64)]
    [InlineData(0x9999, 0x9999, ProcessArchitecture.Unknown)]
    public void Machine_codes_map_to_process_architecture(
        ushort processMachine,
        ushort nativeMachine,
        ProcessArchitecture expected)
    {
        Assert.Equal(expected, ProcessArchitectureMapper.FromMachineCodes(processMachine, nativeMachine));
    }
}
