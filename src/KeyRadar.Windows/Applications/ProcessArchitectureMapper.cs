namespace KeyRadar.Windows.Applications;

public static class ProcessArchitectureMapper
{
    private const ushort I386 = 0x014C;
    private const ushort Amd64 = 0x8664;
    private const ushort Arm64 = 0xAA64;

    public static ProcessArchitecture FromMachineCodes(ushort processMachine, ushort nativeMachine)
    {
        var effectiveMachine = processMachine == 0 ? nativeMachine : processMachine;
        return effectiveMachine switch
        {
            I386 => ProcessArchitecture.X86,
            Amd64 => ProcessArchitecture.X64,
            Arm64 => ProcessArchitecture.Arm64,
            _ => ProcessArchitecture.Unknown,
        };
    }
}
