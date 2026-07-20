using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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
        string? companyName = null;
        string? packageFamilyName = null;

        try
        {
            var module = process.MainModule;
            executableName = module?.ModuleName ?? executableName;
            if (module?.FileName is { Length: > 0 } executablePath)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
                companyName = versionInfo.CompanyName;
                publisher = ReadAuthenticodePublisher(executablePath);
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
            ReadPrivilege(process),
            companyName,
            packageFamilyName = ReadPackageFamilyName(process),
            packageFamilyName is null ? null : "microsoft-store");
    }

    private static string? ReadAuthenticodePublisher(string executablePath)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No loader API extracts an Authenticode certificate directly from a signed PE file.
            using var certificate = X509Certificate.CreateFromSignedFile(executablePath);
#pragma warning restore SYSLIB0057
            using var certificate2 = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            return certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static unsafe string? ReadPackageFamilyName(Process process)
    {
        try
        {
            uint length = 0;
            var status = GetPackageFamilyName(process.Handle, ref length, null);
            if (status != 122 || length is 0 or > 256)
            {
                return null;
            }

            var value = new char[length];
            fixed (char* valuePointer = value)
            {
                status = GetPackageFamilyName(process.Handle, ref length, valuePointer);
            }

            if (status != 0)
            {
                return null;
            }

            var terminator = Array.IndexOf(value, '\0');
            return new string(value, 0, terminator >= 0 ? terminator : value.Length);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
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

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetPackageFamilyName(
        nint processHandle,
        ref uint packageFamilyNameLength,
        char* packageFamilyName);
}
