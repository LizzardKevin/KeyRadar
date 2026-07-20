using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Updater.Updates;

namespace KeyRadar;

internal static class RuntimeRuleCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<ApplicationVariantRule> _current = [];
    private static string? _activeVersion;
    private static string _statusMessage = "规则尚未加载。";
    private static bool _isAvailable;

    public static IReadOnlyList<ApplicationVariantRule> Current
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
        RulePackReadResult? localResult = null;
        var localPackPath = Path.Combine(RulesDirectory, "local.krpack");
        if (File.Exists(localPackPath))
        {
            using var localStream = File.OpenRead(localPackPath);
            localResult = RulePackReader.ReadLocal(localStream);
        }

        lock (Gate)
        {
            _current = LayeredRuleCatalog.Resolve(
                localResult is { IsSuccess: true } ? localResult.Pack!.Variants : [],
                result.IsSuccess ? result.Pack!.Variants : [],
                []);
            _activeVersion = result is { IsSuccess: true } ? result.Pack!.Version : null;
            _isAvailable = result.IsSuccess || localResult is { IsSuccess: true };
            _statusMessage = localResult is { IsSuccess: false }
                ? $"本地规则不可用：{localResult.Message}"
                : result.IsSuccess
                    ? $"已加载官方签名规则包 {result.Pack!.Version}{(localResult is { IsSuccess: true } ? " · 用户声明规则（未签名）" : string.Empty)}。"
                    : localResult is { IsSuccess: true }
                        ? "官方规则不可用；仅加载用户声明规则（未签名）。"
                        : result.Message;
        }

        return result;
    }
}
