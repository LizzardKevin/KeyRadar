using KeyRadar.Rules;
using KeyRadar.Rules.Catalog;
using KeyRadar.Rules.Packs;
using KeyRadar.Updater.Updates;

namespace KeyRadar;

internal static class RuntimeRuleCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<ApplicationRuleSet> _current = BuiltInRuleCatalog.Load();

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

    public static RulePackReadResult? Reload()
    {
        var activePackPath = Path.Combine(RulesDirectory, "active.krpack");
        var catalog = SignedRuleCatalogLoader.Load(
            activePackPath,
            OfficialReleaseKey.GetBytes(),
            out var result);
        lock (Gate)
        {
            _current = catalog;
        }

        return result;
    }
}
