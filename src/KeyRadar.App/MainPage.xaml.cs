using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyRadar.Diagnostics;
using KeyRadar.Conflicts;
using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Rules.Updates;
using KeyRadar.Hotkeys;
using KeyRadar.Updater.Updates;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;
using KeyRadar.Windows.Hardware;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        _updateClient = new UpdateCheckClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        _ruleUpdateClient = new RuleUpdateClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        InitializeComponent();
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

            groups.Add(new ApplicationGroupViewModel(
                rules.ApplicationId,
                rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                presence == ApplicationPresence.Foreground ? "● 前台" : "后台",
                BuildEvidenceSummary(process),
                presence == ApplicationPresence.Foreground ? "\uE7C4" : "\uE8A7",
                false,
                rules.Hotkeys.Select(hotkey =>
                {
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

                        if (availability?.Availability == HotkeyProbeAvailability.Occupied)
                        {
                            occupiedGlobalHotkeyCount++;
                        }
                    }

                    return HotkeyRowViewModel.Create(
                        hotkey.Gesture.ToString(),
                        hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                        hotkey.Scope,
                        hotkey.Confidence,
                        process.Id,
                        availabilityLabel,
                        hotkey.Sources);
                }).ToArray(),
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
        SummaryText.Text = $"识别 {appCount} 个运行中的支持应用 · {hotkeyCount} 个可发现热键 · {totalOccupied} 个标准全局占用";
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
