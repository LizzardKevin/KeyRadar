using System.Collections.ObjectModel;
using System.Diagnostics;
using KeyRadar.Conflicts;
using KeyRadar.Rules;
using KeyRadar.Shortcuts;
using KeyRadar.Updater.Updates;
using KeyRadar.Windows.Applications;
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
    private IReadOnlyList<ApplicationGroupViewModel> _allGroups = [];

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        _updateClient = new UpdateCheckClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
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
        var catalog = BuiltInRuleCatalog.Load();
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
                        availabilityLabel);
                }).ToArray()));
        }

        _allGroups = groups
            .OrderBy(group => group.Id != "windows-system")
            .ThenByDescending(group => group.PresenceLabel.Contains("前台", StringComparison.Ordinal))
            .ThenBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

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
}
