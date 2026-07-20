using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KeyRadar.Diagnostics;
using KeyRadar.Conflicts;
using KeyRadar.Rules;
using KeyRadar.Rules.Packs;
using KeyRadar.Rules.Updates;
using KeyRadar.Shortcuts;
using KeyRadar.Updater.Updates;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;
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

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        _updateClient = new UpdateCheckClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        _ruleUpdateClient = new RuleUpdateClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    public void ShowObservedGesture(ShortcutGesture gesture)
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
        ScanProgress.IsActive = true;
        ScanStatusText.Text = "正在安全读取窗口、进程与规则证据";

        var snapshots = await Task.Run(() => _scanner.Scan(Environment.ProcessId));
        _latestSnapshots = snapshots;
        var catalog = RuntimeRuleCatalog.Current;
        var availabilityProbe = new GlobalHotkeyAvailabilityProbe(new Win32HotkeyRegistrationApi());
        var groups = new List<ApplicationGroupViewModel> { CreateWindowsGroup() };
        var occupiedGlobalShortcutCount = 0;

        var matched = snapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Rules = ApplicationRuleMatcher.FindByExecutableName(catalog, snapshot.Process.ExecutableName),
            })
            .Where(item => item.Rules is not null)
            .GroupBy(item => item.Rules!.Id, StringComparer.OrdinalIgnoreCase);

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
                rules.Id,
                rules.DisplayName,
                presence == ApplicationPresence.Foreground ? "● 前台" : "后台",
                BuildEvidenceSummary(process),
                presence == ApplicationPresence.Foreground ? "\uE7C4" : "\uE8A7",
                false,
                rules.Shortcuts.Select(shortcut =>
                {
                    string? availabilityLabel = null;
                    if (shortcut.Scope == ShortcutScope.Global)
                    {
                        var availability = availabilityProbe.Probe(shortcut.Gesture);
                        availabilityLabel = availability switch
                        {
                            GlobalHotkeyAvailability.Occupied => " · 当前已占用",
                            GlobalHotkeyAvailability.Available => " · 当前未注册",
                            _ => " · 无法探测",
                        };

                        if (availability == GlobalHotkeyAvailability.Occupied)
                        {
                            occupiedGlobalShortcutCount++;
                        }
                    }

                    return ShortcutRowViewModel.Create(
                        shortcut.Gesture.ToString(),
                        shortcut.Function,
                        shortcut.Scope,
                        shortcut.Confidence,
                        process.Id,
                        availabilityLabel,
                        shortcut.Sources);
                }).ToArray(),
                process.Id));
        }

        _allGroups = groups
            .OrderBy(group => group.Id != "windows-system")
            .ThenByDescending(group => group.PresenceLabel.Contains("前台", StringComparison.Ordinal))
            .ThenBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var duplicateGestures = _allGroups
            .SelectMany(group => group.Shortcuts.Select(shortcut => new { Group = group, Shortcut = shortcut }))
            .Where(item => item.Shortcut.ScopeLabel == "全局")
            .GroupBy(item => item.Shortcut.Gesture, StringComparer.OrdinalIgnoreCase)
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

        var appCount = Math.Max(0, _allGroups.Count - 1);
        var shortcutCount = _allGroups.Sum(group => group.Shortcuts.Count);
        SummaryText.Text = $"识别 {appCount} 个运行中的支持应用 · {shortcutCount} 个可用快捷键 · {occupiedGlobalShortcutCount} 个全局占用";
        ScanStatusText.Text = "被动监听已就绪；按键功能将正常执行";
        ScanProgress.IsActive = false;
    }

    private static ApplicationGroupViewModel CreateWindowsGroup()
    {
        var systemShortcuts = new[]
        {
            SystemShortcut("Win+Shift+S", "打开截图工具"),
            SystemShortcut("Alt+Tab", "切换窗口"),
            SystemShortcut("Win+V", "剪贴板历史记录"),
            SystemShortcut("Win+L", "锁定电脑"),
            SystemShortcut("Win+G", "打开 Xbox Game Bar"),
        };

        return new ApplicationGroupViewModel(
            "windows-system",
            "Windows 系统",
            "系统级",
            "微软系统快捷键规则 · 默认折叠",
            "\uE782",
            false,
            systemShortcuts);
    }

    private static ShortcutRowViewModel SystemShortcut(string gesture, string function) =>
        ShortcutRowViewModel.Create(
            ShortcutGesture.Parse(gesture).ToString(),
            function,
            ShortcutScope.WindowsSystem,
            OwnershipConfidence.SystemKnown,
            processId: 0);

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
        var publisher = string.IsNullOrWhiteSpace(process.Publisher) ? null : process.Publisher.Trim();
        var version = string.IsNullOrWhiteSpace(process.Version) ? null : process.Version.Trim();

        return string.Join(
            " · ",
            new[] { process.ExecutableName, architecture, privilege, publisher, version, "内置规则证据" }
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
        UpdateRulesButton.IsEnabled = false;
        UpdateRulesButtonText.Text = "检查中…";
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
            UpdateRulesButton.IsEnabled = true;
            UpdateRulesButtonText.Text = "更新规则";
        }
    }

    private async Task DownloadAndActivateRulesAsync(RuleUpdateManifest manifest)
    {
        UpdateRulesButtonText.Text = "下载中…";
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
        RollbackRulesButton.IsEnabled = false;
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
            RollbackRulesButton.IsEnabled = true;
        }
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
                group.Shortcuts.Select(shortcut => new DiagnosticShortcut(
                    shortcut.Gesture,
                    shortcut.Function,
                    shortcut.ScopeLabel,
                    shortcut.ConfidenceLabel,
                    shortcut.EvidenceLabel)).ToArray());
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
                group.Shortcuts.Any(shortcut =>
                    shortcut.Gesture.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    shortcut.Function.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)))
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
        if (sender is not Button { Tag: ShortcutRowViewModel row } ||
            !ShortcutGesture.TryParse(row.Gesture, out var gesture))
        {
            return;
        }

        ((Button)sender).IsEnabled = false;
        ConflictInfoBar.Severity = InfoBarSeverity.Informational;
        ConflictInfoBar.Title = $"正在深度确认 {row.Gesture}";
        ConflictInfoBar.Message = "请在 30 秒内真实按下该组合键；原功能会正常执行，KeyRadar 不会拦截。";
        ConflictInfoBar.IsOpen = true;

        using var component = new NativeDeepConfirmationComponent();
        var coordinator = new DeepConfirmationCoordinator(component);
        var result = await coordinator.ConfirmAsync(
            new DeepConfirmationRequest(row.ProcessId, gesture, TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        switch (result)
        {
            case DeepConfirmationResult.Confirmed:
                row.MarkConfirmed();
                ConflictInfoBar.Severity = InfoBarSeverity.Success;
                ConflictInfoBar.Title = "归属已确认";
                ConflictInfoBar.Message = $"{row.Gesture} 的 WM_HOTKEY 接收进程与此应用一致。";
                break;
            case DeepConfirmationResult.TimedOut:
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "未观察到目标快捷键";
                ConflictInfoBar.Message = "30 秒内未收到匹配的 WM_HOTKEY；该应用也可能使用键盘钩子或原始输入。";
                break;
            default:
                if (component.LastObservedProcessId is int observedOwnerPid && observedOwnerPid > 0)
                {
                    RegisterObservedConflict(row, observedOwnerPid);
                }
                ConflictInfoBar.Severity = InfoBarSeverity.Warning;
                ConflictInfoBar.Title = "归属不一致或无法确认";
                ConflictInfoBar.Message = component.LastObservedProcessId is int ownerPid && ownerPid > 0
                    ? $"快捷键由 PID {ownerPid} 的其他进程接收。"
                    : "观察组件未能确认此应用；当前可信度保持不变。";
                break;
        }

        ((Button)sender).IsEnabled = row.CanDeepConfirm;
    }

    private void RegisterObservedConflict(ShortcutRowViewModel candidate, int ownerProcessId)
    {
        var ownerSnapshot = _latestSnapshots.FirstOrDefault(snapshot => snapshot.Process.Id == ownerProcessId);
        var ownerExecutable = ownerSnapshot?.Process.ExecutableName ?? $"PID {ownerProcessId}";
        var ownerRules = ApplicationRuleMatcher.FindByExecutableName(RuntimeRuleCatalog.Current, ownerExecutable);
        var ownerDisplayName = ownerRules?.DisplayName ?? ownerExecutable;
        var groupId = $"confirmed-owner-{ownerProcessId}";
        var confirmedRow = ShortcutRowViewModel.Create(
            candidate.Gesture,
            $"已确认接收；与候选应用的“{candidate.Function}”冲突",
            ShortcutScope.Global,
            OwnershipConfidence.Confirmed,
            ownerProcessId);

        foreach (var group in _allGroups.Where(group => group.Shortcuts.Contains(candidate)))
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
