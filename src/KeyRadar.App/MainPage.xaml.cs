using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KeyRadar.Diagnostics;
using KeyRadar.Conflicts;
using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Rules.Updates;
using KeyRadar.Hotkeys;
using KeyRadar.Updater.Updates;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;
using KeyRadar.Windows.Hardware;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace KeyRadar;

public sealed partial class MainPage : Page
{
    private readonly RunningApplicationScanner _scanner =
        new(new SystemProcessSource(), new Win32WindowSource());
    private readonly HttpClient _updateHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly UpdateCheckClient _updateClient;
    private readonly RuleUpdateClient _ruleUpdateClient;
    private IReadOnlyList<ApplicationGroupViewModel> _allGroups = [];
    private IReadOnlyList<ApplicationSnapshot> _latestSnapshots = [];
    private IReadOnlyList<HotkeyProbeResult> _latestProbeResults = [];
    private CancellationTokenSource? _scanCancellation;
    private AppPreferences _preferences = new();
    private bool _preferencesReady;

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        _updateClient = new UpdateCheckClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        _ruleUpdateClient = new RuleUpdateClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        InitializeComponent();
        _preferences = AppPreferences.Load();
        RequestedTheme = _preferences.ToElementTheme();
        SelectComboItem(LanguageComboBox, _preferences.Language);
        SelectComboItem(ThemeComboBox, _preferences.Theme);
        _preferencesReady = true;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    public void ShowObservedGesture(HotkeyGesture gesture)
    {
        var display = gesture.ToString();
        SearchBox.Text = display;
        ApplyFilter(display);
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;
        ScanProgress.IsActive = true;
        ScanStatusText.Text = "正在枚举运行状态";

        IReadOnlyList<ApplicationSnapshot> snapshots;
        IReadOnlyList<HotkeyProbeResult> occupancyResults;
        HardwareEnvironmentSnapshot hardwareEnvironment;
        try
        {
            snapshots = await Task.Run(() => _scanner.Scan(Environment.ProcessId), cancellationToken);
            var hidDevices = await Task.Run(
                () => new RawInputHidDeviceSource().ReadConnected(),
                cancellationToken);
            hardwareEnvironment = HardwareEnvironmentScanner.Match(
                snapshots.Select(snapshot => snapshot.Process).DistinctBy(process => process.Id).ToArray(),
                hidDevices);
            var candidates = StandardGlobalHotkeyCandidateSource.Create();
            var progress = new Progress<HotkeyScanProgress>(item =>
                ScanStatusText.Text = $"正在探测标准全局热键 {item.Completed}/{item.Total} · {item.Current}");
            occupancyResults = await Task.Run(async () =>
            {
                using var registrationApi = new Win32HotkeyRegistrationApi();
                var occupancyScanner = new GlobalHotkeyOccupancyScanner(
                    new GlobalHotkeyAvailabilityProbe(registrationApi),
                    new WindowsHotkeyProbeSafetyGate());
                return await occupancyScanner.ScanAsync(candidates, progress, cancellationToken);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "扫描已取消";
            ScanProgress.IsActive = false;
            return;
        }

        _latestSnapshots = snapshots;
        _latestProbeResults = occupancyResults;
        var catalog = RuntimeRuleCatalog.Current;
        var occupancyByGesture = occupancyResults.ToDictionary(result => result.Gesture);
        var groups = new List<ApplicationGroupViewModel>();
        var windowsRules = catalog.FirstOrDefault(rule => rule.ApplicationId == "windows-system");
        if (windowsRules is not null)
        {
            groups.Add(CreateWindowsGroup(windowsRules));
        }
        var occupiedGlobalHotkeyCount = 0;

        var variantMatcher = new ApplicationVariantMatcher();
        var matchedSnapshots = snapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Match = variantMatcher.Match(
                    new ApplicationIdentity(
                        snapshot.Process.ExecutableName,
                        snapshot.Process.Version,
                        snapshot.Process.Publisher,
                        snapshot.Process.CompanyName,
                        snapshot.Process.PackageFamilyName,
                        snapshot.Process.Distribution),
                    catalog),
            })
            .ToArray();
        var localConfigurations = await new RunningApplicationConfigurationRegistry(
            [new ShareXConfigurationReader(), new GreenshotConfigurationReader()])
            .ReadAsync(
                matchedSnapshots
                    .Where(item => item.Match.Selected is not null)
                    .Select(item => new RunningApplicationVariant(item.Snapshot.Process, item.Match.Selected!))
                    .ToArray(),
                cancellationToken);
        var matched = matchedSnapshots
            .Where(item => item.Match.Selected is not null)
            .Select(item => new { item.Snapshot, Rules = item.Match.Selected! })
            .GroupBy(item => item.Rules!.ApplicationId, StringComparer.OrdinalIgnoreCase);

        foreach (var applicationProcesses in matched)
        {
            var selected = applicationProcesses
                .OrderByDescending(item => item.Snapshot.Presence == ApplicationPresence.Foreground)
                .First();
            var rules = selected.Rules!;
            var process = selected.Snapshot.Process;
            var presence = applicationProcesses.Any(item => item.Snapshot.Presence == ApplicationPresence.Foreground)
                ? ApplicationPresence.Foreground
                : ApplicationPresence.Background;
            var configured = localConfigurations
                .Where(item => item.ApplicationId.Equals(rules.ApplicationId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var ruleRows = rules.Hotkeys.Select(hotkey =>
            {
                var local = configured.FirstOrDefault(item => item.Gesture == hotkey.Gesture);
                string? availabilityLabel = null;
                if (hotkey.Scope == HotkeyScope.Global)
                {
                    occupancyByGesture.TryGetValue(hotkey.Gesture, out var availability);
                    availabilityLabel = availability?.Availability switch
                    {
                        HotkeyProbeAvailability.Occupied => " · 当前已占用",
                        HotkeyProbeAvailability.AvailableAtScanTime => " · 扫描瞬间可注册",
                        HotkeyProbeAvailability.SystemReserved => " · 系统保留或无法探测",
                        _ => " · 无法探测",
                    };

                    if (availability?.Availability == HotkeyProbeAvailability.Occupied) occupiedGlobalHotkeyCount++;
                }

                return HotkeyRowViewModel.Create(
                    hotkey.Gesture.ToString(),
                    local?.Function ?? hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                    local?.Scope ?? hotkey.Scope,
                    local is null ? hotkey.Confidence : OwnershipConfidence.LocalConfiguration,
                    process.Id,
                    availabilityLabel,
                    hotkey.Sources,
                    evidenceLabel: local is null ? null : $"证据：{local.Evidence}");
            });
            var configuredOnlyRows = configured
                .Where(local => rules.Hotkeys.All(hotkey => hotkey.Gesture != local.Gesture))
                .Select(local => HotkeyRowViewModel.Create(
                    local.Gesture.ToString(),
                    local.Function,
                    local.Scope,
                    OwnershipConfidence.LocalConfiguration,
                    process.Id,
                    evidenceLabel: $"证据：{local.Evidence}"));

            groups.Add(new ApplicationGroupViewModel(
                rules.ApplicationId,
                rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                presence == ApplicationPresence.Foreground ? "● 前台" : "后台",
                BuildEvidenceSummary(process),
                presence == ApplicationPresence.Foreground ? "\uE7C4" : "\uE8A7",
                false,
                ruleRows.Concat(configuredOnlyRows).ToArray(),
                process.Id));
        }

        foreach (var ambiguous in matchedSnapshots.Where(item => item.Match.Kind == VariantMatchKind.Ambiguous))
        {
            var process = ambiguous.Snapshot.Process;
            var candidates = ambiguous.Match.Candidates
                .Select(candidate => candidate.Variant)
                .ToArray();
            groups.Add(new ApplicationGroupViewModel(
                $"variant-uncertain-{process.Id}",
                "变体不确定",
                ambiguous.Snapshot.Presence == ApplicationPresence.Foreground ? "● 前台" : "后台",
                $"{process.ExecutableName} · 候选：{string.Join(" / ", candidates.Select(candidate => candidate.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)))}",
                "\uE9CE",
                true,
                candidates.SelectMany(candidate => candidate.Hotkeys).Select(hotkey => HotkeyRowViewModel.Create(
                    hotkey.Gesture.ToString(),
                    hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                    hotkey.Scope,
                    OwnershipConfidence.Suspected,
                    process.Id,
                    sources: hotkey.Sources)).ToArray(),
                process.Id));
        }

        if (hardwareEnvironment.Software.Count > 0)
        {
            var hardwareRows = hardwareEnvironment.Software.Select(software =>
            {
                var profile = hardwareEnvironment.Profiles.FirstOrDefault(item => item.SoftwareId == software.Id);
                var device = hardwareEnvironment.Devices.FirstOrDefault(item =>
                    software.Id.StartsWith("logi", StringComparison.Ordinal) && item.VendorId == "046D" ||
                    software.Id == "razer-synapse" && item.VendorId == "1532" ||
                    software.Id == "corsair-icue" && item.VendorId == "1B1C");
                var state = profile is not null
                    ? "当前 Profile · 无法安全读取"
                    : software.IsRunning
                        ? "管理软件正在运行 · 未检测到匹配设备"
                        : "设备已连接 · 管理软件未运行";
                return new HotkeyRowViewModel(
                    device?.ModelName ?? software.DisplayName,
                    state,
                    "硬件映射",
                    profile?.Evidence ?? "证据：运行进程与 HID 厂商/型号",
                    profile is not null ? "! 无法完整发现" : "○ 当前未生效",
                    software.ProcessId ?? 0,
                    canDeepConfirm: false);
            }).ToArray();
            groups.Add(new ApplicationGroupViewModel(
                "hardware-mappings",
                "硬件映射",
                "当前设备与 Profile",
                "G HUB · Logi Options+ · Razer Synapse · Corsair iCUE",
                "\uE7F8",
                hardwareEnvironment.Profiles.Count > 0,
                hardwareRows));
        }

        var knownGlobalGestures = groups
            .SelectMany(group => group.Hotkeys)
            .Where(hotkey => hotkey.ScopeLabel == "全局")
            .Select(hotkey => hotkey.Gesture)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownOccupied = occupancyResults
            .Where(result => result.Availability != HotkeyProbeAvailability.AvailableAtScanTime)
            .Where(result => !knownGlobalGestures.Contains(result.Gesture.ToString()))
            .ToArray();
        if (unknownOccupied.Length > 0)
        {
            groups.Add(new ApplicationGroupViewModel(
                "unknown-occupancy",
                "归属未知",
                "当前桌面会话",
                "RegisterHotKey 占用探测 · 不猜测进程归属",
                "\uE9CE",
                unknownOccupied.Any(result => result.Availability == HotkeyProbeAvailability.Occupied),
                unknownOccupied.Select(HotkeyRowViewModel.FromProbe).ToArray()));
        }

        var availableAtScanTime = occupancyResults
            .Where(result => result.Availability == HotkeyProbeAvailability.AvailableAtScanTime)
            .Where(result => !knownGlobalGestures.Contains(result.Gesture.ToString()))
            .ToArray();
        if (availableAtScanTime.Length > 0)
        {
            groups.Add(new ApplicationGroupViewModel(
                "available-at-scan-time",
                "扫描瞬间可注册",
                "仅代表本次扫描",
                "不会据此承诺该组合永久无冲突 · 默认折叠",
                "\uE73E",
                false,
                availableAtScanTime.Select(HotkeyRowViewModel.FromProbe).ToArray()));
        }

        _allGroups = groups
            .OrderBy(group => group.Id != "windows-system")
            .ThenByDescending(group => group.PresenceLabel.Contains("前台", StringComparison.Ordinal))
            .ThenBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var duplicateGestures = _allGroups
            .SelectMany(group => group.Hotkeys.Select(hotkey => new { Group = group, Hotkey = hotkey }))
            .Where(item => item.Hotkey.ScopeLabel == "全局")
            .GroupBy(item => item.Hotkey.Gesture, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Group.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToArray();
        foreach (var duplicate in duplicateGestures)
        {
            foreach (var item in duplicate)
            {
                item.Group.IsExpanded = true;
            }
        }

        ConflictInfoBar.IsOpen = duplicateGestures.Length > 0;
        if (duplicateGestures.Length > 0)
        {
            ConflictInfoBar.Severity = InfoBarSeverity.Warning;
            ConflictInfoBar.Title = "发现确定冲突";
            ConflictInfoBar.Message = $"{duplicateGestures.Length} 个全局组合键被多个运行中应用声明。冲突应用已自动展开。";
        }

        ApplyFilter(SearchBox.Text);

        var appCount = _allGroups.Count(group => group.ProcessId > 0);
        var hotkeyCount = _allGroups.Sum(group => group.Hotkeys.Count);
        var totalOccupied = occupancyResults.Count(result => result.Availability == HotkeyProbeAvailability.Occupied);
        ConflictCountText.Text = duplicateGestures.Length.ToString(System.Globalization.CultureInfo.CurrentCulture);
        AvailableCountText.Text = occupancyResults.Count(result => result.Availability == HotkeyProbeAvailability.AvailableAtScanTime).ToString(System.Globalization.CultureInfo.CurrentCulture);
        ForegroundCountText.Text = _allGroups.Where(group => group.PresenceLabel.Contains("前台", StringComparison.Ordinal)).Sum(group => group.Hotkeys.Count).ToString(System.Globalization.CultureInfo.CurrentCulture);
        UnconfirmedCountText.Text = _allGroups.SelectMany(group => group.Hotkeys).Count(hotkey => hotkey.ConfidenceLabel.Contains("未知", StringComparison.Ordinal) || hotkey.ConfidenceLabel.Contains("疑似", StringComparison.Ordinal)).ToString(System.Globalization.CultureInfo.CurrentCulture);
        SummaryText.Text = $"识别 {appCount} 个运行中的支持应用 · {hotkeyCount} 个可发现热键 · {totalOccupied} 个标准全局占用";
        DashboardStatusText.Text = $"扫描完成：{totalOccupied} 个标准全局占用；无法安全归属的项目已保留为未知。";
        ScanStatusText.Text = "扫描完成；KeyRadar 未发送、拦截或吞掉任何热键";
        ScanProgress.IsActive = false;
        RuleStatusInfoBar.IsOpen = !RuntimeRuleCatalog.IsAvailable;
        RuleStatusInfoBar.Message = RuntimeRuleCatalog.StatusMessage;
    }

    private static ApplicationGroupViewModel CreateWindowsGroup(ApplicationVariantRule rules)
    {
        return new ApplicationGroupViewModel(
            "windows-system",
            rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
            "系统级",
            "官方签名规则包 · 默认折叠",
            "\uE782",
            false,
            rules.Hotkeys.Select(hotkey => HotkeyRowViewModel.Create(
                hotkey.Gesture.ToString(),
                hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                hotkey.Scope,
                hotkey.Confidence,
                processId: 0,
                sources: hotkey.Sources)).ToArray());
    }

    private static string BuildEvidenceSummary(ProcessDescriptor process)
    {
        var architecture = process.Architecture switch
        {
            ProcessArchitecture.X86 => "x86",
            ProcessArchitecture.X64 => "x64",
            ProcessArchitecture.Arm64 => "ARM64",
            _ => "架构未知",
        };
        var privilege = process.PrivilegeLevel switch
        {
            ProcessPrivilegeLevel.Elevated => "管理员",
            ProcessPrivilegeLevel.Standard => "标准权限",
            _ => "权限未知",
        };
        var publisher = string.IsNullOrWhiteSpace(process.Publisher)
            ? string.IsNullOrWhiteSpace(process.CompanyName) ? null : process.CompanyName.Trim()
            : process.Publisher.Trim();
        var version = string.IsNullOrWhiteSpace(process.Version) ? null : process.Version.Trim();
        var distribution = process.PackageFamilyName is null ? null : "Microsoft Store";

        return string.Join(
            " · ",
            new[] { process.ExecutableName, architecture, privilege, publisher, version, distribution, "官方签名规则包" }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private void RadarOverviewNav_Click(object sender, RoutedEventArgs e) => ShowSection("radar");

    private void HotkeyOverviewNav_Click(object sender, RoutedEventArgs e) => ShowSection("hotkeys");

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowSection("settings");

    private void ShowSection(string section)
    {
        RadarOverviewPanel.Visibility = section == "radar" ? Visibility.Visible : Visibility.Collapsed;
        HotkeyOverviewPanel.Visibility = section == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = section == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PageTitleText.Text = section switch
        {
            "hotkeys" => "热键总览",
            "settings" => "设置",
            _ => "雷达总览",
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_preferencesReady || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        _preferences = _preferences with { Theme = theme };
        RequestedTheme = _preferences.ToElementTheme();
        _preferences.Save();
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_preferencesReady || LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        _preferences = _preferences with { Language = language };
        _preferences.Save();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "显示语言已保存 / Language saved",
            Content = "语言将在重新打开 KeyRadar 后完整应用。 / The language will be fully applied after restarting KeyRadar.",
            CloseButtonText = "确定 / OK",
        };
        await dialog.ShowAsync();
    }

    private static void SelectComboItem(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?? comboBox.Items[0];
    }

    private async void OpenMyRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var running = _latestSnapshots
            .Select(snapshot => snapshot.Process)
            .DistinctBy(process => process.Id)
            .OrderBy(process => process.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .Select(process => new ProcessChoice(process))
            .ToArray();
        if (running.Length == 0)
        {
            await ShowUpdateMessageAsync("没有可选应用", "请先完成一次扫描，再从当前运行的应用中选择。");
            return;
        }

        var processBox = new ComboBox { Header = "当前运行的应用", ItemsSource = running, DisplayMemberPath = nameof(ProcessChoice.Display), SelectedIndex = 0 };
        var gestureBox = new TextBox { Header = "热键", IsReadOnly = true, PlaceholderText = "点击“录入热键”后按下组合" };
        var captureButton = new Button { Content = "录入热键" };
        var functionBox = new TextBox { Header = "功能名称", PlaceholderText = "例如：截图" };
        var scopeBox = new ComboBox { Header = "作用范围", SelectedIndex = 0 };
        scopeBox.Items.Add(new ComboBoxItem { Content = "全局", Tag = "global" });
        scopeBox.Items.Add(new ComboBoxItem { Content = "应用内", Tag = "foreground" });
        var note = new TextBlock { Text = "只保存明确录入的组合键，不保存普通输入。该规则将标记为“用户声明 · 未经官方验证”。", TextWrapping = TextWrapping.Wrap };
        var captureArmed = false;
        captureButton.Click += (_, _) =>
        {
            captureArmed = true;
            captureButton.Content = "请按组合键…";
            gestureBox.Focus(FocusState.Programmatic);
        };
        gestureBox.KeyDown += (_, args) =>
        {
            if (!captureArmed || IsModifierKey(args.Key)) return;
            var gestureText = ComposeGesture(args.Key);
            if (gestureText is null) return;
            gestureBox.Text = gestureText;
            captureArmed = false;
            captureButton.Content = "重新录入";
            args.Handled = true;
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 430 };
        content.Children.Add(processBox);
        content.Children.Add(gestureBox);
        content.Children.Add(captureButton);
        content.Children.Add(functionBox);
        content.Children.Add(scopeBox);
        content.Children.Add(note);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "我的规则",
            Content = content,
            PrimaryButtonText = "保存用户规则",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            processBox.SelectedItem is not ProcessChoice selected ||
            !HotkeyGesture.TryParse(gestureBox.Text, out var gesture) ||
            string.IsNullOrWhiteSpace(functionBox.Text))
        {
            return;
        }

        await SaveUserRuleAsync(
            selected.Process,
            gesture,
            functionBox.Text.Trim(),
            scopeBox.SelectedItem is ComboBoxItem { Tag: "global" } ? HotkeyScope.Global : HotkeyScope.Foreground);
    }

    private async Task SaveUserRuleAsync(
        ProcessDescriptor process,
        HotkeyGesture gesture,
        string function,
        HotkeyScope scope)
    {
        var officialMatch = new ApplicationVariantMatcher().Match(
            new ApplicationIdentity(process.ExecutableName, process.Version, process.Publisher, process.CompanyName, process.PackageFamilyName, process.Distribution),
            RuntimeRuleCatalog.Current.Where(rule => rule.VariantId != "user"));
        var applicationId = officialMatch.Selected?.ApplicationId ?? ToIdentifier(Path.GetFileNameWithoutExtension(process.ExecutableName));
        if (officialMatch.Candidates.Count > 0)
        {
            var overwrite = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "最新规则包已收录此应用",
                Content = "保存后，用户声明规则将优先于官方规则。是否覆盖该应用的官方归属结果？",
                PrimaryButtonText = "覆盖并保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await overwrite.ShowAsync() != ContentDialogResult.Primary) return;
        }

        var locale = _preferences.Language == "en-US" ? "en-US" : "zh-CN";
        var variant = new ApplicationVariantRule(
            applicationId,
            "user",
            new LocalizedText(new Dictionary<string, string> { [locale] = officialMatch.Selected?.DisplayName.Resolve(locale) ?? process.ExecutableName }),
            new ApplicationMatchRule(
                [process.ExecutableName],
                string.IsNullOrWhiteSpace(process.Publisher) ? [] : [process.Publisher],
                null,
                string.IsNullOrWhiteSpace(process.PackageFamilyName) ? [] : [process.PackageFamilyName],
                process.Distribution),
            [new HotkeyRule(gesture, new LocalizedText(new Dictionary<string, string> { [locale] = function }), scope, OwnershipConfidence.UserDeclared)]);
        var localPath = Path.Combine(RuntimeRuleCatalog.RulesDirectory, "local.krpack");
        var existing = new List<ApplicationVariantRule>();
        if (File.Exists(localPath))
        {
            using var stream = File.OpenRead(localPath);
            var local = RulePackReader.ReadLocal(stream);
            if (local.IsSuccess) existing.AddRange(local.Pack!.Variants);
        }

        var existingVariant = existing.FirstOrDefault(item => item.ApplicationId.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        if (existingVariant is not null)
        {
            variant = variant with
            {
                Hotkeys = existingVariant.Hotkeys
                    .Where(item => item.Gesture != gesture)
                    .Append(variant.Hotkeys[0])
                    .OrderBy(item => item.Gesture.ToString(), StringComparer.Ordinal)
                    .ToArray(),
            };
        }

        existing.RemoveAll(item => item.ApplicationId.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        existing.Add(variant);
        LocalRulePackWriter.SaveAtomically(localPath, existing);
        RuntimeRuleCatalog.Reload();
        await ScanAsync();
        await ShowUpdateMessageAsync("用户规则已保存", "用户声明 · 未经官方验证。可在设置中导出 local.krpack 进行备份。");
    }

    private async void ImportMyRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".krpack");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).GetMainWindowHandle());
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        RulePackReadResult read;
        await using (var stream = await file.OpenStreamForReadAsync())
        {
            read = RulePackReader.ReadLocal(stream);
        }

        if (!read.IsSuccess)
        {
            await ShowUpdateMessageAsync("用户规则未导入", "该文件不是有效的本地未签名 KeyRadar 规则包。");
            return;
        }

        var variants = read.Pack!.Variants;
        var hotkeyCount = variants.Sum(variant => variant.Hotkeys.Count);
        var preview = string.Join("\n", variants.Take(12).Select(variant =>
            $"• {variant.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)}：{variant.Hotkeys.Count} 个热键"));
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导入未签名用户规则？",
            Content = $"它将识别 {variants.Count} 个应用变体，包含 {hotkeyCount} 个热键。\n\n{preview}\n\n来源：用户分享 · 未签名 · 未经官方验证。导入后将替换当前 local.krpack。",
            PrimaryButtonText = "导入并替换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var localPath = Path.Combine(RuntimeRuleCatalog.RulesDirectory, "local.krpack");
        LocalRulePackWriter.SaveAtomically(localPath, variants);
        RuntimeRuleCatalog.Reload();
        await ScanAsync();
    }

    private async void ExportMyRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var localPath = Path.Combine(RuntimeRuleCatalog.RulesDirectory, "local.krpack");
        if (!File.Exists(localPath))
        {
            await ShowUpdateMessageAsync("没有用户规则", "请先在“我的规则”中保存至少一条用户声明。");
            return;
        }

        var picker = new global::Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = "KeyRadar-My-Rules",
        };
        picker.FileTypeChoices.Add("KeyRadar rule pack", [".krpack"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).GetMainWindowHandle());
        var destination = await picker.PickSaveFileAsync();
        if (destination is null) return;
        await using var source = File.OpenRead(localPath);
        await using var output = await destination.OpenStreamForWriteAsync();
        output.SetLength(0);
        await source.CopyToAsync(output);
        await output.FlushAsync();
    }

    private void SubmitCandidateRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var title = Uri.EscapeDataString("[候选规则] 应用热键归属");
        var body = Uri.EscapeDataString("请填写：\n- 软件名称、版本与发行渠道：\n- exe 名称与发布者：\n- 热键、功能与范围：\n- 是否修改过软件设置：\n- 官方文档链接或脱敏截图：\n- 冲突现象：\n\n请勿提交普通按键流、用户名、完整窗口标题、本地路径、账号或隐私数据。");
        Process.Start(new ProcessStartInfo($"https://github.com/LizzardKevin/KeyRadar/issues/new?template=candidate-rule.yml&title={title}&body={body}") { UseShellExecute = true });
    }

    private static string ToIdentifier(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(normalized) ? "user-application" : normalized;
    }

    private static bool IsModifierKey(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static string? ComposeGesture(VirtualKey key)
    {
        var parts = new List<string>();
        if (IsDown(VirtualKey.Control)) parts.Add("Ctrl");
        if (IsDown(VirtualKey.Shift)) parts.Add("Shift");
        if (IsDown(VirtualKey.Menu)) parts.Add("Alt");
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) parts.Add("Win");
        if (parts.Count == 0) return null;
        var primary = key is >= VirtualKey.Number0 and <= VirtualKey.Number9
            ? ((int)key - (int)VirtualKey.Number0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : key.ToString();
        parts.Add(primary);
        var text = string.Join('+', parts);
        return HotkeyGesture.TryParse(text, out var parsed) ? parsed.ToString() : null;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private sealed record ProcessChoice(ProcessDescriptor Process)
    {
        public string Display => $"{Process.ExecutableName} · PID {Process.Id}";
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButtonText.Text = "检查中…";
        try
        {
            var currentVersion = typeof(MainPage).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var result = await _updateClient.CheckAsync(currentVersion);
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Manifest is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = $"发现 KeyRadar {result.Manifest.Version}",
                    Content = "签名与下载地址已验证。是否下载并安装？更新器会保留本地 data，失败时自动回滚。",
                    PrimaryButtonText = "下载并安装",
                    CloseButtonText = "稍后",
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await DownloadAndInstallUpdateAsync(result.Manifest);
                }

                return;
            }

            await ShowUpdateMessageAsync(
                result.Status == UpdateCheckStatus.UpToDate ? "已是最新版本" : "暂时无法检查更新",
                result.Status == UpdateCheckStatus.UpToDate
                    ? $"当前版本 {currentVersion.ToString(3)} 已是最新版本。"
                    : "网络、限流、404 或签名校验失败时，KeyRadar 会保留当前版本。请稍后重试。");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButtonText.Text = "检查更新";
        }
    }

    private async void UpdateRulesButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateRulesMenuItem.IsEnabled = false;
        try
        {
            var check = await _ruleUpdateClient.CheckAsync(RuntimeRuleCatalog.ActiveVersion);
            if (check.Status == RuleUpdateStatus.UpdateAvailable && check.Manifest is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = $"发现规则包 {check.Manifest.Version}",
                    Content = "下载后会校验 SHA-256、发布签名和包内每条规则；上一版规则会保留用于回滚。",
                    PrimaryButtonText = "下载并启用",
                    CloseButtonText = "稍后",
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await DownloadAndActivateRulesAsync(check.Manifest);
                }

                return;
            }

            await ShowUpdateMessageAsync(
                check.Status == RuleUpdateStatus.UpToDate ? "规则已是最新版" : "暂时无法更新规则",
                check.Status == RuleUpdateStatus.UpToDate
                    ? $"当前已启用官方规则包 {check.Manifest!.Version}。"
                    : "断网、限流、404、哈希或签名失败时，KeyRadar 会继续使用当前规则。请稍后重试。");
        }
        finally
        {
            UpdateRulesMenuItem.IsEnabled = true;
        }
    }

    private async Task DownloadAndActivateRulesAsync(RuleUpdateManifest manifest)
    {
        Directory.CreateDirectory(RuntimeRuleCatalog.RulesDirectory);
        var candidatePath = Path.Combine(
            RuntimeRuleCatalog.RulesDirectory,
            $"candidate-{Guid.NewGuid():N}.krpack");
        try
        {
            var download = await _ruleUpdateClient.DownloadAsync(manifest, candidatePath);
            if (!download.IsSuccess)
            {
                await ShowUpdateMessageAsync("规则包验证失败", download.Message);
                return;
            }

            var activation = RulePackStore.Activate(
                candidatePath,
                RuntimeRuleCatalog.RulesDirectory,
                OfficialReleaseKey.GetBytes());
            if (!activation.IsSuccess)
            {
                await ShowUpdateMessageAsync("规则包未启用", activation.Message);
                return;
            }

            RuntimeRuleCatalog.Reload();
            await ScanAsync();
            await ShowUpdateMessageAsync(
                "规则库已更新",
                $"官方签名规则包 {activation.ActiveVersion} 已启用；上一版可通过“回滚规则”恢复。");
        }
        finally
        {
            TryDeleteFile(candidatePath);
        }
    }

    private async void RollbackRulesButton_Click(object sender, RoutedEventArgs e)
    {
        RollbackRulesMenuItem.IsEnabled = false;
        try
        {
            var rollback = RulePackStore.Rollback(
                RuntimeRuleCatalog.RulesDirectory,
                OfficialReleaseKey.GetBytes());
            if (rollback.IsSuccess)
            {
                RuntimeRuleCatalog.Reload();
                await ScanAsync();
            }

            await ShowUpdateMessageAsync(
                rollback.IsSuccess ? "规则已回滚" : "无法回滚规则",
                rollback.Message);
        }
        finally
        {
            RollbackRulesMenuItem.IsEnabled = true;
        }
    }

    private async void ImportOfficialRulesButton_Click(object sender, RoutedEventArgs e)
    {
        var warning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导入官方签名规则包",
            Content = "KeyRadar 只读取你选择的 .krpack，并校验内置公钥、包身份、清单哈希和 rules/*.json。EXE、DLL、脚本、未知签名或非官方包都会被拒绝。成功后当前规则会保留为上一版，以便主动回滚。",
            PrimaryButtonText = "选择文件",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await warning.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".krpack");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            ((App)Application.Current).GetMainWindowHandle());
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var import = RulePackStore.Activate(
            file.Path,
            RuntimeRuleCatalog.RulesDirectory,
            OfficialReleaseKey.GetBytes());
        if (import.IsSuccess)
        {
            RuntimeRuleCatalog.Reload();
            await ScanAsync();
        }

        await ShowUpdateMessageAsync(
            import.IsSuccess ? "官方规则已导入" : "规则包未导入",
            import.IsSuccess
                ? $"官方签名规则包 {import.ActiveVersion} 已启用。"
                : import.Message);
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            var report = BuildDiagnosticReport();
            var diagnosticsDirectory = Path.Combine(RuntimeRuleCatalog.DataDirectory, "diagnostics");
            var fileName = $"KeyRadar-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip";
            var outputPath = Path.Combine(diagnosticsDirectory, fileName);
            await Task.Run(() => DiagnosticBundleWriter.Write(outputPath, report));

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "诊断包已导出",
                Content = $"已生成 {fileName}。包内不含用户名、完整路径、窗口标题或普通按键流。",
                PrimaryButtonText = "打开所在位置",
                CloseButtonText = "完成",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                startInfo.ArgumentList.Add($"/select,{outputPath}");
                Process.Start(startInfo);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ShowUpdateMessageAsync("无法导出诊断包", "KeyRadar 未写入不完整文件。请确认数据目录可写后重试。");
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }

    private DiagnosticReport BuildDiagnosticReport()
    {
        var applications = _allGroups.Select(group =>
        {
            var snapshot = _latestSnapshots.FirstOrDefault(item => item.Process.Id == group.ProcessId);
            return new DiagnosticApplication(
                group.Id,
                group.DisplayName,
                snapshot?.Process.ExecutableName ?? (group.Id == "windows-system" ? "Windows" : "unknown"),
                snapshot?.Process.Version,
                snapshot?.Process.Publisher,
                snapshot?.Process.Architecture.ToString() ?? "unknown",
                snapshot?.Process.PrivilegeLevel.ToString() ?? "unknown",
                snapshot?.Presence.ToString() ?? "system",
                group.Hotkeys.Select(hotkey => new DiagnosticHotkey(
                    hotkey.Gesture,
                    hotkey.Function,
                    hotkey.ScopeLabel,
                    hotkey.ConfidenceLabel,
                    hotkey.EvidenceLabel)).ToArray());
        }).ToArray();
        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        return new DiagnosticReport(
            version,
            RuntimeInformation.OSDescription,
            DateTimeOffset.UtcNow,
            applications);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateManifest manifest)
    {
        CheckUpdateButtonText.Text = "正在下载…";
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyRadar",
            "updates",
            $"{manifest.Version}-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(updateRoot, manifest.AssetName);
        var stagingPath = Path.Combine(updateRoot, "staging");
        var backupPath = Path.Combine(updateRoot, "backup");

        var download = await _updateClient.DownloadAsync(manifest, archivePath);
        if (!download.IsValid)
        {
            await ShowUpdateMessageAsync("下载验证失败", "更新包未通过完整性验证，当前版本未更改。");
            return;
        }

        var extraction = UpdateArchiveExtractor.Extract(archivePath, stagingPath);
        if (!extraction.IsValid)
        {
            await ShowUpdateMessageAsync("更新包无法使用", "更新包结构不安全或不完整，当前版本未更改。");
            return;
        }

        var updaterPath = Path.Combine(stagingPath, "KeyRadar.Updater.exe");
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = true,
            WorkingDirectory = stagingPath,
        };
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(stagingPath);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("--backup");
        startInfo.ArgumentList.Add(backupPath);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add("KeyRadar.exe");
        startInfo.ArgumentList.Add("--health-marker");
        startInfo.ArgumentList.Add(Path.Combine(updateRoot, "health.ok"));

        try
        {
            Process.Start(startInfo);
            ((App)Application.Current).RequestShutdown();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await ShowUpdateMessageAsync("无法启动更新器", "当前版本未更改。请确认解压目录可写后重试。");
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "确定",
        };
        await dialog.ShowAsync();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= MainPage_Unloaded;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _updateHttpClient.Dispose();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter(sender.Text);
        }
    }

    private void ApplyFilter(string? query)
    {
        FilteredGroups.Clear();
        var normalized = query?.Trim();

        foreach (var group in _allGroups)
        {
            if (string.IsNullOrWhiteSpace(normalized) ||
                group.DisplayName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase) ||
                group.Hotkeys.Any(hotkey =>
                    hotkey.Gesture.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    hotkey.Function.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)))
            {
                FilteredGroups.Add(group);
            }
        }

        EmptyState.Visibility = FilteredGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void JumpToApplication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int processId })
        {
            _ = ApplicationActivator.TryActivate(processId);
        }
    }

    private async void DeepConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HotkeyRowViewModel row } ||
            !HotkeyGesture.TryParse(row.Gesture, out var gesture))
        {
            return;
        }

        ((Button)sender).IsEnabled = false;
        ConflictInfoBar.Severity = InfoBarSeverity.Informational;
        ConflictInfoBar.Title = $"正在深度确认 {row.Gesture}";
        ConflictInfoBar.Message = "正在立即复核占用、运行进程、规则、本机配置与硬件映射；不会模拟或触发该热键。";
        ConflictInfoBar.IsOpen = true;

        var candidates = _allGroups
            .Where(group => group.ProcessId > 0)
            .Where(group => group.Hotkeys.Any(item => item.Gesture.Equals(row.Gesture, StringComparison.OrdinalIgnoreCase)))
            .Select(group => new DeepConfirmationCandidate(
                group.Id,
                group.DisplayName,
                DeepConfirmationEvidenceKind.OfficialRule))
            .ToArray();
        var result = await Task.Run(async () =>
        {
            using var registrationApi = new Win32HotkeyRegistrationApi();
            var service = new ImmediateDeepConfirmationService(
                target => new GlobalHotkeyAvailabilityProbe(registrationApi).Probe(target));
            return await service.ConfirmAsync(
                new ImmediateDeepConfirmationRequest(gesture, candidates),
                CancellationToken.None);
        });

        switch (result.Conclusion)
        {
            case DeepConfirmationConclusion.ConfirmedOwner:
                row.MarkConfirmed();
                ConflictInfoBar.Severity = InfoBarSeverity.Success;
                ConflictInfoBar.Title = "归属已确认";
                ConflictInfoBar.Message = $"{row.Gesture} 已由当前本机配置或生效硬件映射精确确认。";
                break;
            case DeepConfirmationConclusion.PossibleOwner:
                row.MarkPossible();
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "可能归属";
                ConflictInfoBar.Message = $"运行中的候选：{string.Join("、", result.Candidates.Select(candidate => candidate.DisplayName))}。仅有规则吻合，未读取到本机配置证据。";
                break;
            case DeepConfirmationConclusion.OccupiedOwnerUnknown:
                row.MarkOccupiedUnknown();
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "已占用，归属未知";
                ConflictInfoBar.Message = "标准全局探测复核为已占用，但 Windows 没有公开 API 可安全返回注册进程；KeyRadar 不会虚构归属。";
                break;
            default:
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "无法确认";
                ConflictInfoBar.Message = "复核时未能确认占用，或可能涉及私有 Hook、Raw Input、驱动、权限或板载宏。";
                break;
        }

        ((Button)sender).IsEnabled = row.CanDeepConfirm;
    }

    private void RegisterObservedConflict(HotkeyRowViewModel candidate, int ownerProcessId)
    {
        var ownerSnapshot = _latestSnapshots.FirstOrDefault(snapshot => snapshot.Process.Id == ownerProcessId);
        var ownerExecutable = ownerSnapshot?.Process.ExecutableName ?? $"PID {ownerProcessId}";
        var ownerRules = ApplicationRuleMatcher.FindByExecutableName(RuntimeRuleCatalog.Current, ownerExecutable);
        var ownerDisplayName = ownerRules?.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? ownerExecutable;
        var groupId = $"confirmed-owner-{ownerProcessId}";
        var confirmedRow = HotkeyRowViewModel.Create(
            candidate.Gesture,
            $"已确认接收；与候选应用的“{candidate.Function}”冲突",
            HotkeyScope.Global,
            OwnershipConfidence.Confirmed,
            ownerProcessId);

        foreach (var group in _allGroups.Where(group => group.Hotkeys.Contains(candidate)))
        {
            group.IsExpanded = true;
        }

        var ownerGroup = new ApplicationGroupViewModel(
            groupId,
            ownerDisplayName,
            ownerSnapshot?.Presence == ApplicationPresence.Foreground ? "● 前台" : "后台",
            $"按需深度确认 · WM_HOTKEY · PID {ownerProcessId}",
            "\uE8A7",
            true,
            [confirmedRow],
            ownerProcessId);
        _allGroups = _allGroups
            .Where(group => !group.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase))
            .Append(ownerGroup)
            .ToArray();
        ApplyFilter(SearchBox.Text);
    }
}
