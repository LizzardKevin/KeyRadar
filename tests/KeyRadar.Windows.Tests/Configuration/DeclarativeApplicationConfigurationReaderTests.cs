using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class DeclarativeApplicationConfigurationReaderTests
{
    [Fact]
    public async Task Json_virtual_key_array_is_decoded_from_a_declared_selector()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"),
                "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var result = await Reader(root).ReadAsync(Process(), NvidiaVariant(), TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(result);
            Assert.Equal("Alt+I", hotkey.Gesture.ToString());
            Assert.Equal("性能统计叠加层", hotkey.Function);
            Assert.Equal("performance-overlay-toggle", hotkey.CommandId);
            Assert.DoesNotContain(root, hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Json_collection_decodes_winforms_hotkey_and_declared_value_mapping()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "HotkeysConfig.json"),
                "{\"Hotkeys\":[{\"HotkeyInfo\":{\"Hotkey\":393281,\"Win\":false},\"TaskSettings\":{\"Job\":14,\"Secret\":\"private\"}}]}", Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), ShareXVariant(), TestContext.Current.CancellationToken));

            Assert.Equal("Ctrl+Alt+A", hotkey.Gesture.ToString());
            Assert.Equal("矩形区域截图", hotkey.Function);
            Assert.Equal("capture-region", hotkey.CommandId);
            Assert.DoesNotContain("private", hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("R, Shift", "Shift+R")]
    [InlineData("PrintScreen", "PrintScreen")]
    [InlineData("Control, Alt, A", "Ctrl+Alt+A")]
    public async Task Winforms_decoder_accepts_keys_converter_strings(string encoded, string expectedGesture)
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "HotkeysConfig.json"),
                $"{{\"Hotkeys\":[{{\"HotkeyInfo\":{{\"Hotkey\":\"{encoded}\",\"Win\":false}},\"TaskSettings\":{{\"Job\":\"RectangleRegion\"}}}}]}}",
                TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), ShareXVariant(), TestContext.Current.CancellationToken));

            Assert.Equal(expectedGesture, hotkey.Gesture.ToString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Ini_gesture_string_reads_only_declared_key()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(root, "greenshot.ini"),
            ["RegionHotkey=Ctrl + Alt + PrintScreen", "OutputFilePath=C:\\private"], TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), GreenshotVariant(), TestContext.Current.CancellationToken));

            Assert.Equal("Ctrl+Alt+PrintScreen", hotkey.Gesture.ToString());
            Assert.Equal("区域截图", hotkey.Function);
            Assert.DoesNotContain("private", hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Unauthorized_variant_never_reads_configuration_sources()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"), "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", TestContext.Current.CancellationToken);

            var result = await Reader(root).ReadAsync(Process(), NvidiaVariant() with { IsConfigurationReadAuthorized = false }, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0xB0, "MediaNextTrack")]
    [InlineData(0xB1, "MediaPreviousTrack")]
    [InlineData(0xB2, "MediaStop")]
    [InlineData(0xB3, "MediaPlayPause")]
    public async Task Winforms_decoder_preserves_legacy_media_virtual_keys(int virtualKey, string expectedGesture)
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "HotkeysConfig.json"),
                $"{{\"Hotkeys\":[{{\"HotkeyInfo\":{{\"Hotkey\":{virtualKey},\"Win\":false}},\"TaskSettings\":{{\"Job\":14}}}}]}}",
                TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), ShareXVariant(), TestContext.Current.CancellationToken));

            Assert.Equal(expectedGesture, hotkey.Gesture.ToString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Source_entry_budget_is_shared_by_all_collections()
    {
        var root = CreateRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                First = Enumerable.Range(0, 100).Select(_ => new { Gesture = "Alt+A" }),
                Second = Enumerable.Range(0, 100).Select(_ => new { Gesture = "Alt+B" }),
            });
            await File.WriteAllTextAsync(Path.Combine(root, "budget.json"), payload, TestContext.Current.CancellationToken);
            var variant = Variant("budget", "default", [new ConfigurationSourceRule("budget", ConfigurationSourceRoot.Documents,
                "budget.json", ConfigurationSourceFormat.Json, 65536,
                [new ConfigurationEntryRule("first", "Gesture", ConfigurationGestureDecoder.GestureString, Text("First"), HotkeyScope.Global) { CollectionSelector = "First" },
                 new ConfigurationEntryRule("second", "Gesture", ConfigurationGestureDecoder.GestureString, Text("Second"), HotkeyScope.Global) { CollectionSelector = "Second" }])]);

            var result = await Reader(root).ReadAsync(Process(), variant, TestContext.Current.CancellationToken);

            Assert.Equal(128, result.Count);
            Assert.Equal(100, result.Count(item => item.CommandId == "first"));
            Assert.Equal(28, result.Count(item => item.CommandId == "second"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Final_handle_path_outside_allowed_root_is_rejected_without_reopening()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"), "placeholder", TestContext.Current.CancellationToken);
            var accessor = new TestFileAccessor("{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", Path.Combine(root, "..", "escaped", "ShareSettings.json"));

            var result = await new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(), TestContext.Current.CancellationToken);

            Assert.Empty(result);
            Assert.Equal(1, accessor.OpenCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Final_handle_reparse_target_is_rejected_even_when_it_remains_under_the_root()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"), "placeholder", TestContext.Current.CancellationToken);
            var accessor = new TestFileAccessor("{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", Path.Combine(root, "target", "ShareSettings.json"));

            var result = await new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(), TestContext.Current.CancellationToken);

            Assert.Empty(result);
            Assert.Equal(1, accessor.OpenCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Validated_handle_is_read_from_the_same_open_stream()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"), "placeholder", TestContext.Current.CancellationToken);
            var accessor = new TestFileAccessor("{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", Path.Combine(root, "ShareSettings.json"));

            var result = await new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(), TestContext.Current.CancellationToken);

            Assert.Equal("Alt+I", Assert.Single(result).Gesture.ToString());
            Assert.Equal(1, accessor.OpenCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Stream_that_grows_past_the_declared_limit_is_rejected_after_handle_validation()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "ShareSettings.json");
            await File.WriteAllTextAsync(path, "placeholder", TestContext.Current.CancellationToken);
            var content = "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}" + new string(' ', 32);
            var accessor = new StreamAccessor(
                () => new ChunkedReadStream(Encoding.UTF8.GetBytes(content), 1), path);

            var result = await new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(Encoding.UTF8.GetByteCount(content) - 1), TestContext.Current.CancellationToken);

            Assert.Empty(result);
            Assert.Equal(1, accessor.OpenCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Snapshot_at_exactly_the_declared_limit_is_parsed()
    {
        var root = CreateRoot();
        try
        {
            var content = "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}";
            var path = Path.Combine(root, "ShareSettings.json");
            await File.WriteAllTextAsync(path, "placeholder", TestContext.Current.CancellationToken);
            var accessor = new StreamAccessor(
                () => new ChunkedReadStream(Encoding.UTF8.GetBytes(content), 1), path);

            var result = await new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(Encoding.UTF8.GetByteCount(content)), TestContext.Current.CancellationToken);

            Assert.Equal("Alt+I", Assert.Single(result).Gesture.ToString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Bounded_snapshot_checks_cancellation_between_stream_reads()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "ShareSettings.json");
            await File.WriteAllTextAsync(path, "placeholder", TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var accessor = new StreamAccessor(
                () => new ChunkedReadStream(Encoding.UTF8.GetBytes("{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}"), 1, cancellation.Cancel), path);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DeclarativeApplicationConfigurationReader(_ => root, accessor)
                .ReadAsync(Process(), NvidiaVariant(), cancellation.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Trusted_development_pack_decodes_real_sharex_shape_and_replaces_static_defaults()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = CreateRoot();
        var packPath = Path.Combine(Path.GetTempPath(), $"KeyRadar-ShareX-{Guid.NewGuid():N}.krpack");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "ShareX"));
            await File.WriteAllTextAsync(Path.Combine(root, "ShareX", "HotkeysConfig.json"),
                """
                {"Hotkeys":[
                  {"HotkeyInfo":{"Hotkey":"PrintScreen","Win":false},"TaskSettings":{"Job":"PrintScreen"}},
                  {"HotkeyInfo":{"Hotkey":"R, Shift","Win":false},"TaskSettings":{"Job":"RectangleRegion"}},
                  {"HotkeyInfo":{"Hotkey":"Control, Alt, A","Win":false},"TaskSettings":{"Job":"ScreenRecorderGIF"}}
                ]}
                """, TestContext.Current.CancellationToken);
            BuildDevelopmentPack(repositoryRoot, packPath);
            var bytes = await File.ReadAllBytesAsync(packPath, TestContext.Current.CancellationToken);
            using var stream = new MemoryStream(bytes);
            var loaded = RulePackReader.ReadDevelopment(stream, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.True(loaded.IsSuccess, loaded.Message);
            var shareX = Assert.Single(loaded.Pack!.Variants, variant => variant.ApplicationId == "sharex");

            var registry = new RunningApplicationConfigurationRegistry(
                [new DeclarativeApplicationConfigurationReader(_ => root)]);
            var configured = await registry.ReadAsync(
                [new RunningApplicationVariant(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), shareX)],
                TestContext.Current.CancellationToken);

            Assert.Collection(configured.OrderBy(item => item.CommandId),
                item => { Assert.Equal("capture-region", item.CommandId); Assert.Equal("Shift+R", item.Gesture.ToString()); Assert.Equal("矩形区域截图", item.Function); },
                item => { Assert.Equal("capture-screen", item.CommandId); Assert.Equal("PrintScreen", item.Gesture.ToString()); Assert.Equal("全屏截图", item.Function); },
                item => { Assert.Equal("screen-recorder-gif", item.CommandId); Assert.Equal("Ctrl+Alt+A", item.Gesture.ToString()); Assert.Equal("GIF 屏幕录制", item.Function); });

            var owner = new RunningApplicationEvidenceIdentity(42, "sharex", "default");
            var effective = CurrentEffectiveHotkeyProjection.Project(
                [
                    new RunningRuleHotkey("sharex", HotkeyGesture.Parse("PrintScreen"), "Screen", HotkeyScope.Global, OwnershipConfidence.Suspected, "default", owner.Value, owner.VariantId, "capture-screen"),
                    new RunningRuleHotkey("sharex", HotkeyGesture.Parse("Ctrl+PrintScreen"), "Region", HotkeyScope.Global, OwnershipConfidence.Suspected, "default", owner.Value, owner.VariantId, "capture-region"),
                    new RunningRuleHotkey("sharex", HotkeyGesture.Parse("Ctrl+G"), "GIF", HotkeyScope.Global, OwnershipConfidence.Suspected, "default", owner.Value, owner.VariantId, "screen-recorder-gif"),
                ], configured);
            Assert.Empty(effective.Rules);
            Assert.Equal(3, effective.LocalConfigurations.Count);
        }
        finally
        {
            File.Delete(packPath);
            Directory.Delete(root, true);
        }
    }

    private static DeclarativeApplicationConfigurationReader Reader(string root) =>
        new(sourceRoot => root);

    private static ProcessDescriptor Process() => new(42, "NVIDIA App", "NVIDIA Overlay.exe");

    private static ApplicationVariantRule NvidiaVariant(int maxBytes = 4096) => Variant("nvidia-app", "overlay", [
        new ConfigurationSourceRule("nvidia-shortcuts", ConfigurationSourceRoot.LocalAppData, "ShareSettings.json", ConfigurationSourceFormat.Json, maxBytes,
        [new ConfigurationEntryRule("performance-overlay-toggle", "settings.shortcuts.PMOCOverlay", ConfigurationGestureDecoder.VirtualKeyArray, Text("性能统计叠加层"), HotkeyScope.Global)])
    ]);

    private static ApplicationVariantRule ShareXVariant() => Variant("sharex", "default", [
        new ConfigurationSourceRule("sharex-hotkeys", ConfigurationSourceRoot.Documents, "HotkeysConfig.json", ConfigurationSourceFormat.Json, 4096,
        [new ConfigurationEntryRule("custom-hotkey", "HotkeyInfo.Hotkey", ConfigurationGestureDecoder.WinFormsHotkey, Text("自定义任务"), HotkeyScope.Global)
        {
            CollectionSelector = "Hotkeys", WinSelector = "HotkeyInfo.Win", FunctionSelector = "TaskSettings.Job",
            FunctionValues = new Dictionary<string, LocalizedText> { ["14"] = Text("矩形区域截图") },
            CommandIdValues = new Dictionary<string, string> { ["14"] = "capture-region" },
        }])
    ]);

    private static ApplicationVariantRule GreenshotVariant() => Variant("greenshot", "default", [
        new ConfigurationSourceRule("greenshot-hotkeys", ConfigurationSourceRoot.RoamingAppData, "greenshot.ini", ConfigurationSourceFormat.Ini, 4096,
        [new ConfigurationEntryRule("capture-region", "RegionHotkey", ConfigurationGestureDecoder.GestureString, Text("区域截图"), HotkeyScope.Global)])
    ]);

    private static ApplicationVariantRule Variant(string id, string variant, IReadOnlyList<ConfigurationSourceRule> sources) => new(
        id, variant, Text(id), new ApplicationMatchRule(["NVIDIA Overlay.exe"], [], null, [], null), [])
    { ConfigurationSources = sources, IsConfigurationReadAuthorized = true };

    private static LocalizedText Text(string value) => new(new Dictionary<string, string> { ["zh-CN"] = value });
    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"KeyRadar-Config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestFileAccessor(string content, string finalPath) : IConfigurationSourceFileAccessor
    {
        public int OpenCount { get; private set; }

        public Stream OpenRead(string path)
        {
            OpenCount++;
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        public string? GetFinalPath(Stream stream) => finalPath;
    }

    private sealed class StreamAccessor(Func<Stream> open, string finalPath) : IConfigurationSourceFileAccessor
    {
        public int OpenCount { get; private set; }
        public Stream OpenRead(string path) { OpenCount++; return open(); }
        public string? GetFinalPath(Stream stream) => finalPath;
    }

    private sealed class ChunkedReadStream(byte[] content, int chunkSize, Action? afterRead = null) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1; // Represents the pre-read observation of a growing source.
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), content.Length - _position);
            if (count > 0)
            {
                content.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                afterRead?.Invoke();
            }
            return ValueTask.FromResult(count);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyRadar.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("KeyRadar repository root was not found.");
    }

    private static void BuildDevelopmentPack(string repositoryRoot, string outputPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(repositoryRoot, "eng", "Build-DebugRulePack.ps1"), "-RulesDirectory", Path.Combine(repositoryRoot, "rules"), "-OutputPath", outputPath, "-Version", "1.0.0" }) startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{standardOutput}\n{standardError}");
    }
}
