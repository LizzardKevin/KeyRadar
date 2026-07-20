using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Hardware;

public sealed partial class RawInputHidDeviceSource
{
    private const uint DeviceNameCommand = 0x20000007;

    public unsafe IReadOnlyList<HidDeviceDescriptor> ReadConnected()
    {
        uint count = 0;
        var structureSize = (uint)sizeof(RawInputDeviceList);
        if (GetRawInputDeviceList(null, ref count, structureSize) != 0 || count == 0)
        {
            return [];
        }

        var nativeDevices = new RawInputDeviceList[count];
        fixed (RawInputDeviceList* devicesPointer = nativeDevices)
        {
            if (GetRawInputDeviceList(devicesPointer, ref count, structureSize) == uint.MaxValue)
            {
                return [];
            }
        }

        var devices = new List<HidDeviceDescriptor>();
        foreach (var native in nativeDevices)
        {
            var kind = native.Type switch
            {
                0 => HidDeviceKind.Mouse,
                1 => HidDeviceKind.Keyboard,
                _ => HidDeviceKind.Other,
            };
            if (kind == HidDeviceKind.Other)
            {
                continue;
            }

            uint characterCount = 0;
            _ = GetRawInputDeviceInfoW(native.Device, DeviceNameCommand, null, ref characterCount);
            if (characterCount is 0 or > 4096)
            {
                continue;
            }

            var name = new char[characterCount];
            fixed (char* namePointer = name)
            {
                if (GetRawInputDeviceInfoW(native.Device, DeviceNameCommand, namePointer, ref characterCount) == uint.MaxValue)
                {
                    continue;
                }
            }

            var terminator = Array.IndexOf(name, '\0');
            var rawName = new string(name, 0, terminator >= 0 ? terminator : name.Length);
            var descriptor = HidDeviceIdentityParser.Parse(rawName, kind);
            if (descriptor is not null)
            {
                devices.Add(descriptor);
            }
        }

        return devices
            .DistinctBy(device => (device.VendorId, device.ProductId, device.Kind))
            .ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputDeviceList
    {
        public readonly nint Device;
        public readonly uint Type;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint GetRawInputDeviceList(
        RawInputDeviceList* devices,
        ref uint deviceCount,
        uint structureSize);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static unsafe partial uint GetRawInputDeviceInfoW(
        nint device,
        uint command,
        void* data,
        ref uint dataSize);
}
