using System.Collections.ObjectModel;
using KeyRadar.Conflicts;
using KeyRadar.Rules;
using KeyRadar.Shortcuts;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Hotkeys;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KeyRadar;

public sealed partial class MainPage : Page
{
    private readonly RunningApplicationScanner _scanner =
        new(new SystemProcessSource(), new Win32WindowSource());
    private IReadOnlyList<ApplicationGroupViewModel> _allGroups = [];

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
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
                $"{process.ExecutableName} · 内置规则证据",
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

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
