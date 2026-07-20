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

    [Fact]
    public async Task Registry_preserves_process_and_variant_provenance_when_only_second_variant_is_supported()
    {
        var reader = new VariantReader("second");
        var registry = new RunningApplicationConfigurationRegistry([reader]);
        var process = new ProcessDescriptor(42, "WeChat", "WeChat.exe");

        var result = await registry.ReadAsync(
            [
                new RunningApplicationVariant(process, Variant("wechat", "WeChat.exe", "first")),
                new RunningApplicationVariant(process, Variant("wechat", "WeChat.exe", "second")),
            ],
            TestContext.Current.CancellationToken);

        var configuration = Assert.Single(result);
        Assert.Equal("42\u001Fwechat\u001Fsecond", configuration.OwnerIdentity);
        Assert.Equal("second", configuration.VariantId);
    }

    private static ApplicationVariantRule Variant(string id, string executable, string variantId = "default") => new(
        id,
        variantId,
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

    private sealed class VariantReader(string supportedVariantId) : IApplicationConfigurationReader
    {
        public bool Supports(ApplicationVariantRule variant) => variant.VariantId == supportedVariantId;

        public Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
            ProcessDescriptor process,
            ApplicationVariantRule variant,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LocalConfigurationHotkey>>(
            [new("wechat", HotkeyGesture.Parse("Ctrl+L"), "Lock", HotkeyScope.Global, "local")]);
    }
}
