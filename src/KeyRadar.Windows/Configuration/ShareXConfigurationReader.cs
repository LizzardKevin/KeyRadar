using System.Text.Json;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Configuration;

public sealed class ShareXConfigurationReader(Func<string>? configPath = null) : IApplicationConfigurationReader
{
    private const long MaximumConfigBytes = 8 * 1024 * 1024;
    private readonly Func<string> _configPath = configPath ?? (() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ShareX",
        "HotkeysConfig.json"));

    public bool Supports(ApplicationVariantRule variant) =>
        variant.ApplicationId.Equals("sharex", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
        ProcessDescriptor process,
        ApplicationVariantRule variant,
        CancellationToken cancellationToken)
    {
        var path = _configPath();
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is 0 or > MaximumConfigBytes)
            {
                return [];
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("Hotkeys", out var hotkeys) || hotkeys.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<LocalConfigurationHotkey>();
            foreach (var item in hotkeys.EnumerateArray().Take(512))
            {
                if (!TryReadGesture(item, out var gesture)) continue;
                var function = ReadFunction(item);
                results.Add(new LocalConfigurationHotkey(
                    "sharex",
                    gesture,
                    function,
                    HotkeyScope.Global,
                    "ShareX HotkeysConfig.json · 白名单本机配置"));
            }

            return results;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static bool TryReadGesture(JsonElement item, out HotkeyGesture gesture)
    {
        gesture = default;
        if (!item.TryGetProperty("HotkeyInfo", out var info) ||
            !info.TryGetProperty("Hotkey", out var hotkeyValue) ||
            !TryReadInt32(hotkeyValue, out var encoded))
        {
            return false;
        }

        var keyCode = encoded & 0xFFFF;
        var key = MapVirtualKey(keyCode);
        if (key is null) return false;
        var modifiers = HotkeyModifiers.None;
        if ((encoded & 0x20000) != 0) modifiers |= HotkeyModifiers.Control;
        if ((encoded & 0x10000) != 0) modifiers |= HotkeyModifiers.Shift;
        if ((encoded & 0x40000) != 0) modifiers |= HotkeyModifiers.Alt;
        if (info.TryGetProperty("Win", out var win) && win.ValueKind == JsonValueKind.True)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static string ReadFunction(JsonElement item)
    {
        if (!item.TryGetProperty("TaskSettings", out var settings) || !settings.TryGetProperty("Job", out var job))
        {
            return "ShareX 自定义任务";
        }

        if (job.ValueKind == JsonValueKind.String)
        {
            return job.GetString() ?? "ShareX 自定义任务";
        }

        return TryReadInt32(job, out var value) ? value switch
        {
            10 => "全屏截图",
            11 => "活动窗口截图",
            12 => "自定义窗口截图",
            13 => "当前显示器截图",
            14 => "矩形区域截图",
            18 => "上次区域截图",
            24 => "屏幕录制",
            28 => "GIF 屏幕录制",
            _ => $"ShareX 任务 {value}",
        } : "ShareX 自定义任务";
    }

    private static bool TryReadInt32(JsonElement value, out int result) =>
        value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out result)
            : int.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static string? MapVirtualKey(int value)
    {
        if (value is >= 0x41 and <= 0x5A) return ((char)value).ToString();
        if (value is >= 0x30 and <= 0x39) return ((char)value).ToString();
        if (value is >= 0x70 and <= 0x87) return $"F{value - 0x6F}";
        return value switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc", 0x20 => "Space",
            0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
            0xAD => "VolumeMute", 0xAE => "VolumeDown", 0xAF => "VolumeUp",
            0xB0 => "MediaNextTrack", 0xB1 => "MediaPreviousTrack", 0xB2 => "MediaStop", 0xB3 => "MediaPlayPause",
            _ => null,
        };
    }
}
