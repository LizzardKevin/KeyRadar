using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Configuration;

public sealed class GreenshotConfigurationReader(Func<string>? configPath = null) : IApplicationConfigurationReader
{
    private const long MaximumConfigBytes = 2 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> Functions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RegionHotkey"] = "区域截图",
        ["WindowHotkey"] = "窗口截图",
        ["FullscreenHotkey"] = "全屏截图",
        ["LastregionHotkey"] = "上次区域截图",
        ["ClipboardHotkey"] = "从剪贴板创建图像",
    };
    private readonly Func<string> _configPath = configPath ?? (() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenshot",
        "greenshot.ini"));

    public bool Supports(ApplicationVariantRule variant) =>
        variant.ApplicationId.Equals("greenshot", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
        ProcessDescriptor process,
        ApplicationVariantRule variant,
        CancellationToken cancellationToken)
    {
        var path = _configPath();
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is 0 or > MaximumConfigBytes) return [];
            var results = new List<LocalConfigurationHotkey>();
            foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var name = line[..separator].Trim();
                if (!Functions.TryGetValue(name, out var function)) continue;
                var value = line[(separator + 1)..].Trim();
                if (value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                    !HotkeyGesture.TryParse(value.Replace(" ", string.Empty, StringComparison.Ordinal), out var gesture))
                {
                    continue;
                }

                results.Add(new LocalConfigurationHotkey(
                    "greenshot",
                    gesture,
                    function,
                    HotkeyScope.Global,
                    "Greenshot greenshot.ini · 白名单本机配置"));
            }

            return results;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
