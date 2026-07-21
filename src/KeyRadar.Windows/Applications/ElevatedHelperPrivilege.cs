using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Applications;

public static partial class ElevatedHelperPrivilege
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevelInformationClass = 25;
    private const int ErrorInsufficientBuffer = 122;
    private const uint HighIntegrityRid = 0x3000;

    public static bool IsHighIntegrity(uint integrityRid) => integrityRid >= HighIntegrityRid;

    public static bool IsCurrentProcessHighIntegrity()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            _ = GetTokenInformation(token, TokenIntegrityLevelInformationClass, nint.Zero, 0, out var length);
            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || length <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevelInformationClass, buffer, length, out _))
                {
                    return false;
                }

                var sid = Marshal.ReadIntPtr(buffer);
                var subAuthorityCount = GetSidSubAuthorityCount(sid);
                if (subAuthorityCount == nint.Zero)
                {
                    return false;
                }

                var count = Marshal.ReadByte(subAuthorityCount);
                if (count == 0)
                {
                    return false;
                }

                var integrityRid = GetSidSubAuthority(sid, (uint)(count - 1));
                return integrityRid != nint.Zero && IsHighIntegrity((uint)Marshal.ReadInt32(integrityRid));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        nint tokenHandle,
        int informationClass,
        nint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial nint GetSidSubAuthority(nint sid, uint subAuthorityIndex);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
