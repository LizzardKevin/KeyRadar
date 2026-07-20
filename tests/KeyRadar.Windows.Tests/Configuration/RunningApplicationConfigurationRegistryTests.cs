using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class RunningApplicationConfigurationRegistryTests
{
    [Fact]
    public async Task Registry_reads_only_matched_running_application_variants()
    {
        var reader = new StubReader("wechat");
        var registry = new RunningApplicationConfigurationRegistry([reader]);
        var running = new[]
        {
            new RunningApplicationVariant(
                new ProcessDescriptor(42, "WeChat", "WeChat.exe"),
                Variant("wechat", "WeChat.exe")),
        };

        var result = await registry.ReadAsync(running, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(42, reader.LastProcessId);
    }

    private static ApplicationVariantRule Variant(string id, string executable) => new(
        id,
        "default",
        new LocalizedText(new Dictionary<string, string> { ["en-US"] = id }),
        new ApplicationMatchRule([executable], [], null, [], null),
        []);

    private sealed class StubReader(string supportedApplicationId) : IApplicationConfigurationReader
    {
        public int Calls { get; private set; }
        public int LastProcessId { get; private set; }

        public bool Supports(ApplicationVariantRule variant) => variant.ApplicationId == supportedApplicationId;

        public Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
            ProcessDescriptor process,
            ApplicationVariantRule variant,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastProcessId = process.Id;
            return Task.FromResult<IReadOnlyList<LocalConfigurationHotkey>>(
            [
                new("wechat", HotkeyGesture.Parse("Ctrl+L"), "锁定", HotkeyScope.Global, "白名单本机配置"),
            ]);
        }
    }
}
