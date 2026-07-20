using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Updater.Updates;

namespace KeyRadar;

internal static class RuntimeRuleCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<ApplicationRuleSet> _current = [];
    private static string? _activeVersion;
    private static string _statusMessage = "规则尚未加载。";
    private static bool _isAvailable;

    public static IReadOnlyList<ApplicationRuleSet> Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    public static string DataDirectory
    {
        get
        {
            var portableDirectory = Path.Combine(AppContext.BaseDirectory, "data");
            return Directory.Exists(portableDirectory)
                ? portableDirectory
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KeyRadar");
        }
    }

    public static string RulesDirectory => Path.Combine(DataDirectory, "rules");

    public static string? ActiveVersion
    {
        get
        {
            lock (Gate)
            {
                return _activeVersion;
            }
        }
    }

    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
            {
                return _isAvailable;
            }
        }
    }

    public static string StatusMessage
    {
        get
        {
            lock (Gate)
            {
                return _statusMessage;
            }
        }
    }

    public static RulePackReadResult? Reload()
    {
        var activePackPath = Path.Combine(RulesDirectory, "active.krpack");
        var applicationVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var bundledPackPath = Path.Combine(
            AppContext.BaseDirectory,
            $"KeyRadar-Rules-v{applicationVersion}.krpack");
        var result = OfficialRulePackLoader.Load(
            activePackPath,
            bundledPackPath,
            OfficialReleaseKey.GetBytes());

        lock (Gate)
        {
            _current = result.IsSuccess ? result.Pack!.Applications : [];
            _activeVersion = result is { IsSuccess: true } ? result.Pack!.Version : null;
            _isAvailable = result.IsSuccess;
            _statusMessage = result.IsSuccess
                ? $"已加载官方签名规则包 {result.Pack!.Version}。"
                : result.Message;
        }

        return result;
    }
}
