using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Updater.Updates;

namespace KeyRadar;

internal static class RuntimeRuleCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<ApplicationVariantRule> _current = [];
    private static string? _activeVersion;
    private static string _statusMessage = UiText.Pick("规则尚未加载。", "Rules have not been loaded.");
    private static bool _isAvailable;
    private static bool _isUsingDevelopmentFallback;

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

    public static bool IsUsingDevelopmentFallback
    {
        get
        {
            lock (Gate)
            {
                return _isUsingDevelopmentFallback;
            }
        }
    }

    public static string RulePackEvidenceLabel => IsUsingDevelopmentFallback
        ? UiText.Pick("开发规则包 · 未签名 · 仅 Debug", "development rule pack · unsigned · Debug only")
        : UiText.Pick("官方签名规则包", "signed official rule pack");

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
#if DEBUG
        RulePackReadResult? developmentResult = null;
        if (!result.IsSuccess)
        {
            var developmentPackPath = Path.Combine(
                AppContext.BaseDirectory,
                "KeyRadar-Development-Rules.krpack");
            if (File.Exists(developmentPackPath))
            {
                using var developmentStream = File.OpenRead(developmentPackPath);
                developmentResult = RulePackReader.ReadDevelopment(developmentStream);
            }
        }
#endif
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
#if DEBUG
                developmentResult is { IsSuccess: true } ? developmentResult.Pack!.Variants : []);
#else
                []);
#endif
            _activeVersion = result is { IsSuccess: true } ? result.Pack!.Version : null;
            _isAvailable = result.IsSuccess || localResult is { IsSuccess: true }
#if DEBUG
                || developmentResult is { IsSuccess: true };
#else
                ;
#endif
            _isUsingDevelopmentFallback =
#if DEBUG
                developmentResult is { IsSuccess: true };
#else
                false;
#endif
            _statusMessage = localResult is { IsSuccess: false }
                ? UiText.Pick($"本地规则不可用：{localResult.Message}", $"Local rules are unavailable: {localResult.Message}")
                : result.IsSuccess
                    ? UiText.Pick(
                        $"已加载官方签名规则包 {result.Pack!.Version}{(localResult is { IsSuccess: true } ? " · 用户声明规则（未签名）" : string.Empty)}。",
                        $"Loaded signed official rule pack {result.Pack!.Version}{(localResult is { IsSuccess: true } ? " · user-declared rules (unsigned)" : string.Empty)}.")
#if DEBUG
                    : developmentResult is { IsSuccess: true }
                        ? UiText.Pick(
                            $"开发规则包 · 未签名 · 仅 Debug · {developmentResult.Pack!.Version}。正式签名规则包不可用；Release 不会加载此包。",
                            $"Development rule pack · unsigned · Debug only · {developmentResult.Pack!.Version}. Signed official rules are unavailable; Release never loads this pack.")
#endif
                    : localResult is { IsSuccess: true }
                        ? UiText.Pick("官方规则不可用；仅加载用户声明规则（未签名）。", "Official rules are unavailable; only unsigned user-declared rules were loaded.")
                        : UiText.LocalizeExternal(result.Message);
        }

        return result;
    }
}
