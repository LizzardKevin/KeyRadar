using System.Text.Json;
using Microsoft.UI.Xaml;

namespace KeyRadar;

internal sealed record AppPreferences(string Language = "system", string Theme = "system")
{
    private static string SettingsPath => Path.Combine(RuntimeRuleCatalog.DataDirectory, "settings.json");

    public static AppPreferences Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllBytes(SettingsPath)) ?? new()
                : new();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(this));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public ElementTheme ToElementTheme() => Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
