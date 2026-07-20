using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Applications;

internal static partial class ProcessMetadataReader
{
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInformationClass = 20;

    public static ProcessDescriptor Read(Process process)
    {
        var executableName = $"{process.ProcessName}.exe";
        string? version = null;
        string? publisher = null;

        try
        {
            var module = process.MainModule;
            executableName = module?.ModuleName ?? executableName;
            if (module?.FileName is { Length: > 0 } executablePath)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
                publisher = versionInfo.CompanyName;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Protected processes expose only their process name.
        }

        return new ProcessDescriptor(
            process.Id,
            process.ProcessName,
            executableName,
            version,
            publisher,
            ReadArchitecture(process),
            ReadPrivilege(process));
    }

    private static ProcessArchitecture ReadArchitecture(Process process)
    {
        try
        {
            return IsWow64Process2(process.Handle, out var processMachine, out var nativeMachine)
                ? ProcessArchitectureMapper.FromMachineCodes(processMachine, nativeMachine)
                : ProcessArchitecture.Unknown;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ProcessArchitecture.Unknown;
        }
    }

    private static ProcessPrivilegeLevel ReadPrivilege(Process process)
    {
        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out var tokenHandle))
            {
                return ProcessPrivilegeLevel.Unknown;
            }

            try
            {
                return GetTokenInformation(
                           tokenHandle,
                           TokenElevationInformationClass,
                           out var elevated,
                           sizeof(int),
                           out _) && elevated != 0
                    ? ProcessPrivilegeLevel.Elevated
                    : ProcessPrivilegeLevel.Standard;
            }
            finally
            {
                _ = CloseHandle(tokenHandle);
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ProcessPrivilegeLevel.Unknown;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWow64Process2(nint processHandle, out ushort processMachine, out ushort nativeMachine);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        nint tokenHandle,
        int informationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
