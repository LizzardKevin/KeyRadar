using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KeyRadar.Windows.SystemState;

public sealed record WindowsSessionState(
    string WindowsVersion,
    string InputLanguage,
    bool? PrintScreenOpensSnippingTool);

public static partial class WindowsSessionStateReader
{
    public static WindowsSessionState Read()
    {
        var keyboardLayout = GetKeyboardLayout(0);
        var languageId = (int)((long)keyboardLayout & 0xFFFF);
        string inputLanguage;
        try
        {
            inputLanguage = CultureInfo.GetCultureInfo(languageId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            inputLanguage = $"LANGID 0x{languageId:X4}";
        }

        return new WindowsSessionState(
            RuntimeInformation.OSDescription,
            inputLanguage,
            ReadPrintScreenSetting());
    }

    private static bool? ReadPrintScreenSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: false);
            return key?.GetValue("PrintScreenKeyForSnippingEnabled") switch
            {
                int value => value != 0,
                string value when int.TryParse(value, out var parsed) => parsed != 0,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);
}
