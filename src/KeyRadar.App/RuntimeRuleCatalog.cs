using KeyRadar.Rules;
using KeyRadar.Rules.Catalog;
using KeyRadar.Rules.Packs;
using KeyRadar.Updater.Updates;

namespace KeyRadar;

internal static class RuntimeRuleCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<ApplicationRuleSet> _current = BuiltInRuleCatalog.Load();
    private static string? _activeVersion;
    private static bool _hasLocalPack;

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

    public static bool HasLocalPack
    {
        get
        {
            lock (Gate)
            {
                return _hasLocalPack;
            }
        }
    }

    public static RulePackReadResult? Reload()
    {
        var activePackPath = Path.Combine(RulesDirectory, "active.krpack");
        var catalog = SignedRuleCatalogLoader.Load(
            activePackPath,
            OfficialReleaseKey.GetBytes(),
            out var result);
        var localPath = Path.Combine(RulesDirectory, LocalRulePackStore.ActiveFileName);
        var hasLocalPack = false;
        if (File.Exists(localPath))
        {
            try
            {
                using var stream = File.OpenRead(localPath);
                var local = RulePackReader.ReadUnsignedLocal(stream);
                if (local.IsSuccess)
                {
                    catalog = RuleCatalogComposer.Compose(catalog, local.Pack!.Applications);
                    hasLocalPack = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        lock (Gate)
        {
            _current = catalog;
            _activeVersion = result is { IsSuccess: true } ? result.Pack!.Version : null;
            _hasLocalPack = hasLocalPack;
        }

        return result;
    }
}
