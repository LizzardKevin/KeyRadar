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
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;
using KeyRadar.Windows.Hardware;
using KeyRadar.Windows.SystemState;
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
    private readonly ElevatedScanCoordinator _elevatedScanCoordinator = new(
        ProcessElevatedScanLauncher.FromApplicationDirectory(),
        new NamedPipeElevatedScanTransport(),
        TimeSpan.FromSeconds(12));
    private readonly HttpClient _updateHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly UpdateCheckClient _updateClient;
    private readonly RuleUpdateClient _ruleUpdateClient;
    private readonly RunningApplicationConfigurationRegistry _configurationRegistry = new(
        [new ShareXConfigurationReader(), new GreenshotConfigurationReader()]);
    private readonly ImportedHardwareProfileStore _hardwareProfileStore = new();
    private readonly CompletedScanStateStore _completedScanState = new();
    private readonly CompletedScanPublicationCoordinator _completedScanPublication;
    private readonly ScanGenerationCoordinator _scanGenerations = new();
    private IReadOnlyList<ApplicationGroupViewModel> _allGroups = [];
    private IReadOnlyList<ApplicationSnapshot> _latestSnapshots = [];
    private CancellationTokenSource? _scanCancellation;
    private AppPreferences _preferences = new();
    private bool _preferencesReady;
    private bool _filtersReady;

    public ObservableCollection<ApplicationGroupViewModel> FilteredGroups { get; } = [];

    public MainPage()
    {
        _updateClient = new UpdateCheckClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        _ruleUpdateClient = new RuleUpdateClient(_updateHttpClient, OfficialReleaseKey.GetBytes());
        _completedScanPublication = new CompletedScanPublicationCoordinator(_completedScanState);
        InitializeComponent();
        _filtersReady = true;
        _preferences = AppPreferences.Load();
        RequestedTheme = _preferences.ToElementTheme();
        SelectComboItem(LanguageComboBox, _preferences.Language);
        SelectComboItem(ThemeComboBox, _preferences.Theme);
        _preferencesReady = true;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        var scanGeneration = _scanGenerations.Begin();
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellationToken = _scanCancellation.Token;
        ApplyIfCurrent(scanGeneration, () =>
        {
            ScanProgress.IsActive = true;
            ScanStatusText.Text = UiText.Pick("正在枚举运行状态", "Enumerating the current running state");
        });

        IReadOnlyList<ApplicationSnapshot> snapshots;
        IReadOnlyList<HotkeyProbeResult> occupancyResults;
        HardwareEnvironmentSnapshot hardwareEnvironment;
        var elevatedScanStatus = ElevatedScanStatus.Unavailable;
        try
        {
            snapshots = await Task.Run(() => _scanner.Scan(Environment.ProcessId), cancellationToken);
            ApplyIfCurrent(scanGeneration, () =>
                ScanStatusText.Text = UiText.Pick("正在请求管理员扫描", "Requesting administrator scan"));
            var elevatedScan = await _elevatedScanCoordinator.ScanAndMergeAsync(snapshots, cancellationToken);
            snapshots = elevatedScan.Snapshots;
            elevatedScanStatus = elevatedScan.Status;
            var hidDevices = await Task.Run(
                () => new RawInputHidDeviceSource().ReadConnected(),
                cancellationToken);
            hardwareEnvironment = HardwareEnvironmentScanner.Match(
                snapshots.Select(snapshot => snapshot.Process).DistinctBy(process => process.Id).ToArray(),
                hidDevices);
            hardwareEnvironment = AddImportedHardwareProfile(hardwareEnvironment);
            var candidates = StandardGlobalHotkeyCandidateSource.Create();
            var heldExtendedKeys = WindowsHotkeyProbeSafetyGate.CaptureHeldExtendedFunctionKeys();
            var skippedCandidates = candidates
                .Where(candidate => NativeHotkeyMapper.TryMap(candidate, out var virtualKey, out _) && heldExtendedKeys.Contains((int)virtualKey))
                .ToArray();
            var probeCandidates = candidates.Except(skippedCandidates).ToArray();
            var progress = new Progress<HotkeyScanProgress>(item =>
                ApplyIfCurrent(scanGeneration, () =>
                    ScanStatusText.Text = UiText.Pick(
                        $"正在探测标准全局热键 {item.Completed}/{item.Total} · {item.Current}",
                        $"Probing standard global hotkeys {item.Completed}/{item.Total} · {item.Current}")));
            occupancyResults = await Task.Run(async () =>
            {
                using var registrationApi = new Win32HotkeyRegistrationApi();
                var occupancyScanner = new GlobalHotkeyOccupancyScanner(
                    new GlobalHotkeyAvailabilityProbe(registrationApi),
                    new WindowsHotkeyProbeSafetyGate(ignoredVirtualKeys: heldExtendedKeys));
                var scanned = await occupancyScanner.ScanAsync(probeCandidates, progress, cancellationToken);
                var skippedAt = DateTimeOffset.UtcNow;
                var skipped = skippedCandidates.Select(candidate => new HotkeyProbeResult(
                    candidate,
                    HotkeyProbeAvailability.ProbeError,
                    HotkeyProbeMechanism.RegisterHotKeyProbe,
                    HotkeyOwner.Unknown,
                    skippedAt,
                    WindowsHotkeyProbeSafetyGate.PhysicalKeyHeldErrorCode));
                var byGesture = scanned.Concat(skipped).ToDictionary(result => result.Gesture);
                return candidates.Select(candidate => byGesture[candidate]).ToArray();
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ApplyIfCurrent(scanGeneration, () =>
            {
                ScanStatusText.Text = UiText.Pick("扫描已取消", "Scan canceled");
                ScanProgress.IsActive = false;
            });
            return;
        }
        catch (HotkeyScanSafetyException)
        {
            ApplyIfCurrent(scanGeneration, () =>
            {
                ScanStatusText.Text = UiText.Pick(
                    "检测到持续按键或桌面切换，本轮扫描已安全取消；松开按键后可重新扫描",
                    "The scan was safely canceled because a key remained pressed or the desktop changed. Release the key and scan again.");
                DashboardStatusText.Text = ScanStatusText.Text;
                SummaryText.Text = UiText.Pick("扫描已安全取消", "Scan safely canceled");
                ScanProgress.IsActive = false;
            });
            return;
        }

        if (!await ScanCancellationHandler.TryRunAsync(async scanCancellationToken =>
        {
            var catalog = RuntimeRuleCatalog.Current;
            var windowsSession = WindowsSessionStateReader.Read();
            var groups = new List<ApplicationGroupViewModel>();
            var windowsRules = catalog.FirstOrDefault(rule =>
            WindowsSystemHotkeyIdentity.IsSystemApplication(rule.ApplicationId));

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
        var runningVariantsWithPresence = matchedSnapshots
            .SelectMany(item => (item.Match.Selected is not null
                    ? [item.Match.Selected]
                    : item.Match.Candidates.Select(candidate => candidate.Variant))
                .Select(variant => new
                {
                    Running = new RunningApplicationVariant(item.Snapshot.Process, variant),
                    item.Snapshot.Presence,
                }))
            .DistinctBy(item => new RunningApplicationEvidenceIdentity(
                item.Running.Process.Id,
                item.Running.Variant.ApplicationId,
                item.Running.Variant.VariantId).Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allRunningVariants = runningVariantsWithPresence.Select(item => item.Running).ToArray();
        var applicationPresenceByEvidenceIdentity = runningVariantsWithPresence.ToDictionary(
            item => new RunningApplicationEvidenceIdentity(
                item.Running.Process.Id,
                item.Running.Variant.ApplicationId,
                item.Running.Variant.VariantId).Value,
            item => item.Presence,
            StringComparer.OrdinalIgnoreCase);
        var localConfigurations = CurrentStateHotkeyEligibilityPolicy.FilterLocalConfigurationsByOwnerIdentity(
            await _configurationRegistry.ReadAsync(allRunningVariants, scanCancellationToken),
            applicationPresenceByEvidenceIdentity);
        scanCancellationToken.ThrowIfCancellationRequested();
        if (windowsSession.PrintScreenOpensSnippingTool == true)
        {
            localConfigurations = localConfigurations.Append(new LocalConfigurationHotkey(
                WindowsSystemHotkeyIdentity.ApplicationId,
                HotkeyGesture.Parse("PrintScreen"),
                UiText.Pick("打开 Windows 截图工具", "Open Windows Snipping Tool"),
                HotkeyScope.WindowsSystem,
                UiText.Pick("当前用户 Windows 键盘设置", "current-user Windows keyboard settings"))).ToArray();
        }
        var legacyRawRunningRuleHotkeys = (windowsRules?.Hotkeys ?? [])
            .Select(hotkey => new RunningRuleHotkey(
                WindowsSystemHotkeyIdentity.ApplicationId,
                hotkey.Gesture,
                hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                hotkey.Scope,
                hotkey.Confidence,
                string.Join(" · ", hotkey.Sources)))
            .Concat(matchedSnapshots
                .SelectMany(item => item.Match.Selected is not null
                    ? [item.Match.Selected]
                    : item.Match.Candidates.Select(candidate => candidate.Variant))
                .SelectMany(variant => variant.Hotkeys.Select(hotkey => new RunningRuleHotkey(
                    variant.ApplicationId,
                    hotkey.Gesture,
                    hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                    hotkey.Scope,
                    hotkey.Confidence,
                    string.Join(" · ", hotkey.Sources)))))
            .Where(item => WindowsSystemHotkeyIdentity.IsSystemApplication(item.ApplicationId))
            .ToArray();
        var variantRunningRuleHotkeys = matchedSnapshots.SelectMany(item => (item.Match.Selected is not null
                ? [item.Match.Selected]
                : item.Match.Candidates.Select(candidate => candidate.Variant))
            .SelectMany(variant => variant.Hotkeys.Select(hotkey => new RunningRuleHotkey(
                variant.ApplicationId,
                hotkey.Gesture,
                hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                hotkey.Scope,
                hotkey.Confidence,
                string.Join(" ", hotkey.Sources),
                new RunningApplicationEvidenceIdentity(
                    item.Snapshot.Process.Id,
                    variant.ApplicationId,
                    variant.VariantId).Value,
                variant.VariantId)))).ToArray();
        var rawRunningRuleHotkeys = legacyRawRunningRuleHotkeys
            .Where(rule => WindowsSystemHotkeyIdentity.IsSystemApplication(rule.ApplicationId))
            .Concat(variantRunningRuleHotkeys)
            .ToArray();
        var runningRuleHotkeys = rawRunningRuleHotkeys
            .Where(rule => WindowsSystemHotkeyIdentity.IsSystemApplication(rule.ApplicationId))
            .Where(rule => CurrentStateHotkeyEligibilityPolicy.IsWindowsSystemEligible(rule.Scope))
            .Concat(CurrentStateHotkeyEligibilityPolicy.FilterRunningRulesByOwnerIdentity(
                rawRunningRuleHotkeys.Where(rule => !WindowsSystemHotkeyIdentity.IsSystemApplication(rule.ApplicationId)),
                applicationPresenceByEvidenceIdentity))
            .ToArray();
        var attribution = HotkeyAttributionCatalog.Create(
            occupancyResults,
            runningRuleHotkeys,
            localConfigurations,
            hardwareEnvironment.Profiles);
        if (windowsRules is not null || windowsSession.PrintScreenOpensSnippingTool == true)
        {
            groups.Add(CreateWindowsGroup(windowsRules, windowsSession, attribution));
        }
        var matched = matchedSnapshots
            .Where(item => item.Match.Selected is not null)
            .Select(item => new { item.Snapshot, Rules = item.Match.Selected! })
            .GroupBy(item => item.Rules!.ApplicationId, StringComparer.OrdinalIgnoreCase);

        foreach (var applicationProcesses in matched)
        {
            var representative = RunningApplicationSelectionPolicy.SelectRepresentative(
                applicationProcesses.Select(item => new RunningApplicationSelectionCandidate(
                    item.Snapshot.Process.Id,
                    item.Rules!.ApplicationId,
                    item.Rules.VariantId,
                    item.Snapshot.Presence)));
            var selected = applicationProcesses.Single(item =>
                item.Snapshot.Process.Id == representative.ProcessId &&
                item.Rules!.VariantId.Equals(representative.VariantId, StringComparison.Ordinal) &&
                item.Rules.ApplicationId.Equals(representative.ApplicationId, StringComparison.Ordinal));
            var rules = selected.Rules!;
            var process = selected.Snapshot.Process;
            var ownerLabel = rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name);
            var presence = applicationProcesses.Any(item => item.Snapshot.Presence == ApplicationPresence.Foreground)
                ? ApplicationPresence.Foreground
                : ApplicationPresence.Background;
            var selectedIdentity = new RunningApplicationEvidenceIdentity(
                process.Id,
                rules.ApplicationId,
                rules.VariantId).Value;
            var configured = localConfigurations
                .Where(item => item.OwnerIdentity?.Equals(selectedIdentity, StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            var ruleRows = rules.Hotkeys
                .Where(hotkey => CurrentStateHotkeyEligibilityPolicy.IsEligible(presence, hotkey.Scope))
                .Select(hotkey =>
            {
                var local = configured.FirstOrDefault(item => item.Gesture == hotkey.Gesture);
                string? availabilityLabel = null;
                if (hotkey.Scope == HotkeyScope.Global)
                {
                    availabilityLabel = AvailabilityLabel(attribution, hotkey.Gesture);
                }

                return HotkeyRowViewModel.Create(
                    hotkey.Gesture.ToString(),
                    local is null ? hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name) : UiText.LocalizeExternal(local.Function),
                    ownerLabel,
                    local?.Scope ?? hotkey.Scope,
                    local is null ? hotkey.Confidence : OwnershipConfidence.LocalConfiguration,
                    process.Id,
                    availabilityLabel,
                    hotkey.Sources,
                    evidenceLabel: local is null ? null : UiText.Pick("证据：", "Evidence: ") + UiText.LocalizeExternal(local.Evidence));
            });
            var configuredOnlyRows = configured
                .Where(local => rules.Hotkeys.All(hotkey => hotkey.Gesture != local.Gesture))
                .Select(local => HotkeyRowViewModel.Create(
                    local.Gesture.ToString(),
                    UiText.LocalizeExternal(local.Function),
                    ownerLabel,
                    local.Scope,
                    OwnershipConfidence.LocalConfiguration,
                    process.Id,
                    evidenceLabel: UiText.Pick("证据：", "Evidence: ") + UiText.LocalizeExternal(local.Evidence)));

            groups.Add(new ApplicationGroupViewModel(
                rules.ApplicationId,
                rules.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                presence == ApplicationPresence.Foreground ? UiText.Pick("● 前台", "● Foreground") : UiText.Pick("后台", "Background"),
                BuildEvidenceSummary(process),
                presence == ApplicationPresence.Foreground ? "\uE7C4" : "\uE8A7",
                false,
                ruleRows.Concat(configuredOnlyRows).ToArray(),
                process.Id,
                isForeground: presence == ApplicationPresence.Foreground));
        }

        foreach (var ambiguous in matchedSnapshots.Where(item => item.Match.Kind == VariantMatchKind.Ambiguous))
        {
            var process = ambiguous.Snapshot.Process;
            var candidates = ambiguous.Match.Candidates
                .Select(candidate => candidate.Variant)
                .ToArray();
            var ownerLabel = string.Join(" / ", candidates
                .Select(candidate => candidate.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)));
            groups.Add(new ApplicationGroupViewModel(
                $"variant-uncertain-{process.Id}",
                UiText.Pick("变体不确定", "Variant uncertain"),
                ambiguous.Snapshot.Presence == ApplicationPresence.Foreground ? UiText.Pick("● 前台", "● Foreground") : UiText.Pick("后台", "Background"),
                $"{process.ExecutableName} · {UiText.Pick("候选：", "Candidates: ")}{string.Join(" / ", candidates.Select(candidate => candidate.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)))}",
                "\uE9CE",
                true,
                candidates.SelectMany(candidate => candidate.Hotkeys)
                    .Where(hotkey => CurrentStateHotkeyEligibilityPolicy.IsEligible(ambiguous.Snapshot.Presence, hotkey.Scope))
                    .Select(hotkey => HotkeyRowViewModel.Create(
                    hotkey.Gesture.ToString(),
                    hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                    ownerLabel,
                    hotkey.Scope,
                    OwnershipConfidence.Suspected,
                    process.Id,
                    sources: hotkey.Sources)).ToArray(),
                process.Id,
                isForeground: ambiguous.Snapshot.Presence == ApplicationPresence.Foreground));
        }

        if (hardwareEnvironment.Software.Count > 0)
        {
            var hardwareRows = CreateHardwareRows(hardwareEnvironment);
            groups.Add(new ApplicationGroupViewModel(
                "hardware-mappings",
                UiText.Pick("硬件映射", "Hardware mappings"),
                UiText.Pick("当前设备与 Profile", "Current devices and profiles"),
                "G HUB · Logi Options+ · Razer Synapse · Corsair iCUE",
                "\uE7F8",
                hardwareEnvironment.Profiles.Count > 0,
                hardwareRows));
        }

        var unknownOccupied = attribution.ActionableUnknownProbes;
        if (unknownOccupied.Count > 0)
        {
            groups.Add(new ApplicationGroupViewModel(
                "unknown-occupancy",
                UiText.Pick("归属未知", "Owner unknown"),
                UiText.Pick("当前桌面会话", "Current desktop session"),
                UiText.Pick("RegisterHotKey 占用探测 · 不猜测进程归属", "RegisterHotKey occupancy probe · no guessed process ownership"),
                "\uE9CE",
                unknownOccupied.Any(result => result.Availability == HotkeyProbeAvailability.Occupied),
                unknownOccupied.Select(HotkeyRowViewModel.FromProbe).ToArray()));
        }

        var applicationInventoryOwners = matchedSnapshots.SelectMany(item =>
            (item.Match.Selected is not null
                ? [item.Match.Selected]
                : item.Match.Candidates.Select(candidate => candidate.Variant))
            .Select(variant =>
            {
                var identity = new RunningApplicationEvidenceIdentity(
                    item.Snapshot.Process.Id,
                    variant.ApplicationId,
                    variant.VariantId);
                var isAmbiguous = item.Match.Kind == VariantMatchKind.Ambiguous;
                return new HotkeyInventoryOwner(
                    identity.Value,
                    item.Snapshot.Presence == ApplicationPresence.Foreground
                        ? HotkeyInventoryGroup.ForegroundApplication
                        : HotkeyInventoryGroup.BackgroundApplication,
                    isAmbiguous ? $"variant-uncertain-{item.Snapshot.Process.Id}" : variant.ApplicationId,
                    variant.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
                    DeepConfirmationEvidenceKind.OfficialRule,
                    DisplayOwnerId: variant.ApplicationId);
            })).ToArray();
        var inventoryOwners = applicationInventoryOwners
                .Concat(localConfigurations.Select(local =>
                {
                    var owner = applicationInventoryOwners.FirstOrDefault(candidate =>
                        candidate.OwnerId.Equals(local.OwnerIdentity ?? local.ApplicationId, StringComparison.OrdinalIgnoreCase));
                    var isWindowsSystem = WindowsSystemHotkeyIdentity.IsSystemApplication(local.ApplicationId);
                    return new HotkeyInventoryOwner(
                        local.OwnerIdentity ?? local.ApplicationId,
                        isWindowsSystem ? HotkeyInventoryGroup.WindowsSystem : owner?.Group ?? HotkeyInventoryGroup.BackgroundApplication,
                        isWindowsSystem ? WindowsSystemHotkeyIdentity.GroupId : owner?.GroupId,
                        owner?.DisplayName ?? local.ApplicationId,
                        DeepConfirmationEvidenceKind.LocalConfiguration,
                        local.Gesture,
                        local.ApplicationId);
                }))
                .Concat(hardwareEnvironment.Profiles
                    .Where(profile => profile.Status == HardwareProfileReadStatus.Active)
                    .SelectMany(profile => profile.Mappings
                        .Where(mapping => mapping.ParticipatesInConflict && mapping.TargetGesture is not null)
                        .Select(mapping => new HotkeyInventoryOwner(
                            profile.SoftwareId,
                            HotkeyInventoryGroup.ActiveHardwareProfile,
                            "hardware-mappings",
                            profile.DeviceName,
                            profile.IsUserDeclared
                                ? DeepConfirmationEvidenceKind.UserDeclaredHardwareProfile
                                : DeepConfirmationEvidenceKind.ActiveHardwareProfile,
                            mapping.TargetGesture))))
                .Append(new HotkeyInventoryOwner(
                    WindowsSystemHotkeyIdentity.ApplicationId,
                    HotkeyInventoryGroup.WindowsSystem,
                    WindowsSystemHotkeyIdentity.GroupId,
                    UiText.Pick("Windows 系统", "Windows system"),
                    DeepConfirmationEvidenceKind.OfficialRule))
                .ToArray();
        var projectedInventory = NormalHotkeyInventoryProjector.Project(
            attribution.Items,
            inventoryOwners);
        var inventoryRowsByGroup = projectedInventory
            .GroupBy(item => item.PrimaryGroupId ?? item.Group switch
            {
                HotkeyInventoryGroup.WindowsSystem => WindowsSystemHotkeyIdentity.GroupId,
                HotkeyInventoryGroup.ActiveHardwareProfile => "hardware-mappings",
                HotkeyInventoryGroup.UnknownOccupied => "unknown-occupancy",
                _ => "background-evidence",
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HotkeyRowViewModel>)group
                    .Select(item => CreateInventoryRow(item, attribution))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        if (inventoryRowsByGroup.ContainsKey("background-evidence") && groups.All(group => group.Id != "background-evidence"))
        {
            groups.Add(new ApplicationGroupViewModel(
                "background-evidence",
                UiText.Pick("后台证据", "Background evidence"),
                UiText.Pick("当前运行状态", "Current running state"),
                UiText.Pick("已合并的规则或配置证据", "Merged rule or configuration evidence"),
                "\uE8A7",
                false,
                []));
        }
        if (inventoryRowsByGroup.ContainsKey("hardware-mappings") && groups.All(group => group.Id != "hardware-mappings"))
        {
            groups.Add(new ApplicationGroupViewModel(
                "hardware-mappings",
                UiText.Pick("硬件映射", "Hardware mappings"),
                UiText.Pick("当前设备与 Profile", "Current devices and profiles"),
                "G HUB · Logi Options+ · Razer Synapse · Corsair iCUE",
                "\uE7F8",
                false,
                []));
        }

        var completedGroups = groups
            .Select(group => new ApplicationGroupViewModel(
                group.Id,
                group.DisplayName,
                group.PresenceLabel,
                group.EvidenceSummary,
                group.IconGlyph,
                group.IsExpanded,
                inventoryRowsByGroup.GetValueOrDefault(group.Id, []),
                group.ProcessId,
                group.IsForeground))
            .Where(group => group.Hotkeys.Count > 0)
            .OrderBy(group => group.IsForeground
                ? 0
                : group.Id == "hardware-mappings"
                    ? 1
                    : group.ProcessId > 0
                        ? 2
                        : group.Id == "windows-system"
                            ? 3
                            : group.Id == "unknown-occupancy"
                                ? 4
                                : 5)
            .ThenBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var mergedEvidence = attribution.Items;
        var conflicts = mergedEvidence
            .Where(item => item.Conflict != HotkeyConflictStatus.None)
            .ToArray();
        var conflictGestures = conflicts
            .Select(item => item.Gesture.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in completedGroups.Where(group => group.Hotkeys.Any(item => conflictGestures.Contains(item.Gesture))))
        {
            group.IsExpanded = true;
        }

        var definiteConflictCount = conflicts.Count(item => item.Conflict is
            HotkeyConflictStatus.DefiniteConflict or HotkeyConflictStatus.HardwareMappingCollision);
        var possibleConflictCount = conflicts.Length - definiteConflictCount;
        var conflictInfoBarState = conflicts.Length > 0
            ? new ConflictInfoBarState(
                true,
                InfoBarSeverity.Warning,
                definiteConflictCount > 0
                    ? UiText.Pick("发现确定冲突", "Confirmed conflicts found")
                    : UiText.Pick("发现可能冲突", "Possible conflicts found"),
                UiText.Pick(
                    $"确定冲突 {definiteConflictCount} 个 · 可能拦截 {possibleConflictCount} 个。相关应用已自动展开。",
                    $"{definiteConflictCount} confirmed · {possibleConflictCount} possible interceptions. Related apps were expanded automatically."))
            : new ConflictInfoBarState(false, InfoBarSeverity.Informational, string.Empty, string.Empty);

        ApplyIfCurrent(scanGeneration, () =>
            _completedScanPublication.CreateAndPublish(
                () => CompletedScanExportSnapshot.Create(
                    BuildDiagnosticApplications(completedGroups, snapshots),
                    occupancyResults,
                    attribution.DiagnosticItems),
                scanCancellationToken,
                () =>
                {
                    ApplyConflictInfoBar(conflictInfoBarState);

                    _latestSnapshots = snapshots;
                    _allGroups = completedGroups;

                    ApplyFilter(SearchBox.Text);

                    var appCount = completedGroups.Count(group => group.ProcessId > 0);
                    var hotkeyCount = attribution.Items.Count;
                    var totalOccupied = attribution.Items.Count(item => item.Availability == HotkeyProbeAvailability.Occupied);
                    ConflictCountText.Text = conflicts.Length.ToString(System.Globalization.CultureInfo.CurrentCulture);
                    AvailableCountText.Text = totalOccupied.ToString(System.Globalization.CultureInfo.CurrentCulture);
                    ForegroundCountText.Text = completedGroups.Where(group => group.IsForeground).Sum(group => group.Hotkeys.Count).ToString(System.Globalization.CultureInfo.CurrentCulture);
                    UnconfirmedCountText.Text = completedGroups.SelectMany(group => group.Hotkeys).Count(hotkey => hotkey.IsUnconfirmed).ToString(System.Globalization.CultureInfo.CurrentCulture);
                    SummaryText.Text = UiText.Pick(
                        $"识别 {appCount} 个运行中的支持应用 · {hotkeyCount} 个可发现热键 · {totalOccupied} 个标准全局占用",
                        $"{appCount} supported running apps · {hotkeyCount} discoverable hotkeys · {totalOccupied} standard global occupancies");
                    DashboardStatusText.Text = UiText.Pick(
                        $"扫描完成：{totalOccupied} 个标准全局占用；无法安全归属的项目已保留为未知。",
                        $"Scan complete: {totalOccupied} standard global occupancies; items without safe ownership evidence remain unknown.");
                    ScanStatusText.Text = ElevatedScanStatusText(elevatedScanStatus);
                    ScanProgress.IsActive = false;
                    RuleStatusInfoBar.IsOpen = !RuntimeRuleCatalog.IsAvailable || RuntimeRuleCatalog.IsUsingDevelopmentFallback;
                    RuleStatusInfoBar.Severity = RuntimeRuleCatalog.IsUsingDevelopmentFallback
                        ? InfoBarSeverity.Warning
                        : InfoBarSeverity.Error;
                    RuleStatusInfoBar.Title = RuntimeRuleCatalog.IsUsingDevelopmentFallback
                        ? UiText.Pick("开发规则包", "Development rule pack")
                        : UiText.Pick("规则不可用", "Rules unavailable");
                    RuleStatusInfoBar.Message = RuntimeRuleCatalog.StatusMessage;
                }));
        }, cancellationToken))
        {
            ApplyIfCurrent(scanGeneration, () =>
            {
                ScanStatusText.Text = UiText.Pick("扫描已取消", "Scan canceled");
                ScanProgress.IsActive = false;
            });
        }
    }

    private void ApplyConflictInfoBar(ConflictInfoBarState state)
    {
        ConflictInfoBar.IsOpen = state.IsOpen;
        ConflictInfoBar.Severity = state.Severity;
        ConflictInfoBar.Title = state.Title;
        ConflictInfoBar.Message = state.Message;
    }

    private static string ElevatedScanStatusText(ElevatedScanStatus status) => status switch
    {
        ElevatedScanStatus.Succeeded => UiText.Pick("扫描完成", "Scan complete"),
        ElevatedScanStatus.UserDeclined => UiText.Pick("管理员扫描未授权，已完成有限扫描", "Administrator scan not authorized; limited scan completed"),
        _ => UiText.Pick("管理员扫描不可用，已完成有限扫描", "Administrator scan unavailable; limited scan completed"),
    };

    private sealed record ConflictInfoBarState(
        bool IsOpen,
        InfoBarSeverity Severity,
        string Title,
        string Message);

    private static ApplicationGroupViewModel CreateWindowsGroup(
        ApplicationVariantRule? rules,
        WindowsSessionState session,
        HotkeyAttributionCatalog attribution)
    {
        var rows = (rules?.Hotkeys ?? [])
            .Where(hotkey => CurrentStateHotkeyEligibilityPolicy.IsWindowsSystemEligible(hotkey.Scope))
            .Select(hotkey => HotkeyRowViewModel.Create(
            hotkey.Gesture.ToString(),
            hotkey.Function.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name),
            rules?.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? UiText.Pick("Windows 系统", "Windows system"),
            hotkey.Scope,
            hotkey.Confidence,
            processId: 0,
            availabilityLabel: AvailabilityLabel(attribution, hotkey.Gesture),
            sources: hotkey.Sources)).ToList();
        if (session.PrintScreenOpensSnippingTool == true)
        {
            rows.Add(HotkeyRowViewModel.Create(
                "PrintScreen",
                UiText.Pick("打开 Windows 截图工具", "Open Windows Snipping Tool"),
                UiText.Pick("Windows 系统", "Windows system"),
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.LocalConfiguration,
                processId: 0,
                evidenceLabel: UiText.Pick("证据：当前用户 Windows 键盘设置", "Evidence: current-user Windows keyboard settings")));
        }

        return new ApplicationGroupViewModel(
            "windows-system",
            rules?.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? UiText.Pick("Windows 系统", "Windows system"),
            UiText.Pick("系统级", "System level"),
            $"{session.WindowsVersion} · {session.InputLanguage} · {UiText.Pick("默认折叠", "collapsed by default")}",
            "\uE782",
            false,
            rows);
    }

    private static string? AvailabilityLabel(HotkeyAttributionCatalog attribution, HotkeyGesture gesture)
    {
        if (!attribution.TryGet(gesture, out var item) ||
            !NormalHotkeyPresentationPolicy.ShouldShowAvailability(item.Availability))
        {
            return null;
        }

        return item.Availability switch
        {
            HotkeyProbeAvailability.Occupied => UiText.Pick(" · 当前已占用", " · currently occupied"),
            _ => null,
        };
    }

    private static HotkeyRowViewModel CreateInventoryRow(
        ProjectedHotkeyInventoryRow projected,
        HotkeyAttributionCatalog attribution)
    {
        var item = projected.Item;
        var ownerLabel = projected.DeepConfirmationCandidates.Count == 0
            ? UiText.Pick("归属未知", "Owner unknown")
            : string.Join(", ", projected.DeepConfirmationCandidates
                .Select(candidate => candidate.DisplayName)
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
        var ownerEvidence = item.Owners.Count == 0
            ? string.Empty
            : UiText.Pick(" · 归属：", " · Owners: ") + string.Join(", ", projected.DeepConfirmationCandidates
                .Select(candidate => candidate.OwnerId)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        var conflictEvidence = item.Conflict == HotkeyConflictStatus.None
            ? string.Empty
            : UiText.Pick(" · 冲突：", " · Conflict: ") + ConflictLabel(item.Conflict);
        return HotkeyRowViewModel.Create(
            item.Gesture.ToString(),
            UiText.LocalizeExternal(item.Function),
            ownerLabel,
            item.Scope,
            ConfidenceFor(item.Ownership),
            processId: 0,
            availabilityLabel: AvailabilityLabel(attribution, item.Gesture),
            evidenceLabel: UiText.Pick("证据：", "Evidence: ") +
                string.Join(" · ", item.Evidence.Select(UiText.LocalizeExternal)) + ownerEvidence + conflictEvidence,
            isHardware: projected.HasActiveHardwareEvidence,
            searchText: string.Join(" ", projected.DeepConfirmationCandidates
                .Select(candidate => $"{candidate.DisplayName} {candidate.OwnerId}")));
    }

    private static OwnershipConfidence ConfidenceFor(HotkeyOwnershipStatus ownership) => ownership switch
    {
        HotkeyOwnershipStatus.Confirmed => OwnershipConfidence.Confirmed,
        HotkeyOwnershipStatus.LocalConfigurationFound => OwnershipConfidence.LocalConfiguration,
        HotkeyOwnershipStatus.HardwareMappingFound => OwnershipConfidence.HardwareMapping,
        HotkeyOwnershipStatus.WindowsKnown => OwnershipConfidence.SystemKnown,
        HotkeyOwnershipStatus.OfficialDefault => OwnershipConfidence.OfficialDefault,
        HotkeyOwnershipStatus.PossibleOwner => OwnershipConfidence.Suspected,
        _ => OwnershipConfidence.Unknown,
    };

    private bool ApplyIfCurrent(ScanGeneration scanGeneration, Action update) =>
        _scanGenerations.TryApply(scanGeneration, update);

    private static string ConflictLabel(HotkeyConflictStatus conflict) => conflict switch
    {
        HotkeyConflictStatus.DefiniteConflict => UiText.Pick("确定冲突", "definite conflict"),
        HotkeyConflictStatus.PossibleInterception => UiText.Pick("可能拦截", "possible interception"),
        HotkeyConflictStatus.ContextualReuse => UiText.Pick("上下文复用", "contextual reuse"),
        HotkeyConflictStatus.HardwareMappingCollision => UiText.Pick("硬件映射碰撞", "hardware mapping collision"),
        _ => string.Empty,
    };

    private static IReadOnlyList<HotkeyRowViewModel> CreateHardwareRows(HardwareEnvironmentSnapshot environment)
    {
        var rows = new List<HotkeyRowViewModel>();
        foreach (var software in environment.Software)
        {
            var profiles = environment.Profiles
                .Where(profile => profile.SoftwareId.Equals(software.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(profile => profile.Status == HardwareProfileReadStatus.Active)
                .ThenByDescending(profile => profile.Mappings.Count)
                .ToArray();
            var device = environment.Devices.FirstOrDefault(item =>
                software.Id.StartsWith("logi", StringComparison.Ordinal) && item.VendorId == "046D" ||
                software.Id == "razer-synapse" && item.VendorId == "1532" ||
                software.Id == "corsair-icue" && item.VendorId == "1B1C");
            foreach (var profile in profiles.Where(profile => profile.Mappings.Count > 0))
            {
                foreach (var mapping in profile.Mappings)
                {
                    var profileState = profile.Status == HardwareProfileReadStatus.Active
                        ? UiText.Pick("当前生效", "active now")
                        : UiText.Pick("未生效", "inactive");
                    var declaration = profile.IsUserDeclared
                        ? UiText.Pick("用户声明 · 未实时验证", "user-declared · not live-verified")
                        : UiText.Pick("已实时读取", "read from the active profile");
                    var onboard = profile.IsOnboardMemory
                        ? UiText.Pick($" · 板载{(profile.Slot is null ? string.Empty : $"槽位 {profile.Slot}")}", $" · onboard{(profile.Slot is null ? string.Empty : $" slot {profile.Slot}")}")
                        : string.Empty;
                    rows.Add(new HotkeyRowViewModel(
                        mapping.TargetGesture?.ToString() ?? mapping.PhysicalTrigger,
                        $"{mapping.PhysicalTrigger} → {mapping.DisplayTarget}",
                        software.DisplayName,
                        UiText.Pick("硬件映射", "Hardware mapping"),
                        $"{profile.ProfileName} · {profileState}{onboard} · {declaration} · {UiText.LocalizeExternal(profile.Evidence)}",
                        profile.IsUserDeclared
                            ? UiText.Pick("● 用户声明 · 未经官方验证", "● User declared · not officially verified")
                            : UiText.Pick("● 硬件映射已发现", "● Hardware mapping found"),
                        software.ProcessId ?? 0,
                        isGlobal: mapping.ParticipatesInConflict,
                        isUnconfirmed: profile.IsUserDeclared,
                        confidence: profile.IsUserDeclared ? OwnershipConfidence.UserDeclared : OwnershipConfidence.HardwareMapping,
                        isHardware: true));
                }
            }

            if (profiles.All(profile => profile.Mappings.Count == 0))
            {
                var profile = profiles.FirstOrDefault();
                var state = profile is not null
                    ? UiText.Pick("当前 Profile · 无法安全读取", "Current profile · cannot be read safely")
                    : software.IsRunning
                        ? UiText.Pick("管理软件正在运行 · 未检测到匹配设备", "Management software is running · no matching device detected")
                        : UiText.Pick("设备已连接 · 管理软件未运行", "Device connected · management software is not running");
                rows.Add(new HotkeyRowViewModel(
                    device?.ModelName ?? software.DisplayName,
                    state,
                    software.DisplayName,
                    UiText.Pick("硬件映射", "Hardware mapping"),
                    profile is null ? UiText.Pick("证据：运行进程与 HID 厂商/型号", "Evidence: running process and HID vendor/model") : UiText.LocalizeExternal(profile.Evidence),
                    profile is not null ? UiText.Pick("! 无法完整发现", "! Not fully discoverable") : UiText.Pick("○ 当前未生效", "○ Not currently active"),
                    software.ProcessId ?? 0,
                    isHardware: true));
            }
        }

        return rows;
    }

    private static string BuildEvidenceSummary(ProcessDescriptor process)
    {
        var architecture = process.Architecture switch
        {
            ProcessArchitecture.X86 => "x86",
            ProcessArchitecture.X64 => "x64",
            ProcessArchitecture.Arm64 => "ARM64",
            _ => UiText.Pick("架构未知", "architecture unknown"),
        };
        var privilege = process.PrivilegeLevel switch
        {
            ProcessPrivilegeLevel.Elevated => UiText.Pick("管理员", "elevated"),
            ProcessPrivilegeLevel.Standard => UiText.Pick("标准权限", "standard privilege"),
            _ => UiText.Pick("权限未知", "privilege unknown"),
        };
        var publisher = string.IsNullOrWhiteSpace(process.Publisher)
            ? string.IsNullOrWhiteSpace(process.CompanyName) ? null : process.CompanyName.Trim()
            : process.Publisher.Trim();
        var version = string.IsNullOrWhiteSpace(process.Version) ? null : process.Version.Trim();
        var distribution = process.PackageFamilyName is null ? null : "Microsoft Store";

        return string.Join(
            " · ",
            new[] { process.ExecutableName, architecture, privilege, publisher, version, distribution, RuntimeRuleCatalog.RulePackEvidenceLabel }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void ImportHardwareProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var warning = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiText.Pick("导入声明式硬件 Profile", "Import a declarative hardware profile"),
            Content = UiText.Pick(
                "KeyRadar 只读取你选择的 JSON，并仅保留设备、Profile、物理触发键和组合键目标。宏文本与启动路径会被强制隐藏。厂商私有或加密导出格式不会被猜测解析；导入结果标记为“用户声明 · 未实时验证”，不会冒充确定证据。",
                "KeyRadar reads only the JSON you select and retains only the device, profile, physical trigger, and hotkey target. Macro text and launch paths are forcibly hidden. Proprietary or encrypted vendor exports are not guessed; imported results are marked user-declared and not live-verified."),
            PrimaryButtonText = UiText.Pick("选择 JSON", "Choose JSON"),
            CloseButtonText = UiText.Pick("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await warning.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            ((App)Application.Current).GetMainWindowHandle());
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var result = await _hardwareProfileStore.ImportAsync(file.Path, CancellationToken.None);
        if (result.IsSuccess)
        {
            await ScanAsync();
        }

        await ShowUpdateMessageAsync(
            result.IsSuccess
                ? UiText.Pick("硬件 Profile 已导入", "Hardware profile imported")
                : UiText.Pick("无法导入硬件 Profile", "Hardware profile could not be imported"),
            result.IsSuccess
                ? UiText.Pick(
                    "已保存脱敏后的用户声明 Profile。只有对应硬件或管理软件当前存在时才会显示，并作为可能证据参与冲突判断。",
                    "The redacted user-declared profile was saved. It appears only when the matching hardware or management software is present and participates as possible evidence.")
                : UiText.Pick(
                    $"文件未通过 KeyRadar 硬件 Profile Schema v1 校验（{result.Code}）。当前配置保持不变。",
                    $"The file did not pass KeyRadar hardware profile Schema v1 validation ({result.Code}). The current configuration was kept."));
    }

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
            "hotkeys" => UiText.Pick("热键总览", "Hotkey overview"),
            "settings" => UiText.Pick("设置", "Settings"),
            _ => UiText.Pick("雷达总览", "Radar overview"),
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
            Title = UiText.Pick("显示语言已保存", "Display language saved"),
            Content = UiText.Pick("语言将在重新打开 KeyRadar 后完整应用。", "The language will be fully applied after restarting KeyRadar."),
            CloseButtonText = UiText.Pick("确定", "OK"),
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
            await ShowUpdateMessageAsync(
                UiText.Pick("没有可选应用", "No apps available"),
                UiText.Pick("请先完成一次扫描，再从当前运行的应用中选择。", "Complete a scan first, then choose from the currently running apps."));
            return;
        }

        var processBox = new ComboBox { Header = UiText.Pick("当前运行的应用", "Currently running app"), ItemsSource = running, DisplayMemberPath = nameof(ProcessChoice.Display), SelectedIndex = 0 };
        var gestureBox = new TextBox { Header = UiText.Pick("热键", "Hotkey"), IsReadOnly = true, PlaceholderText = UiText.Pick("点击“录入热键”后按下组合", "Select Capture hotkey, then press the combination") };
        var captureButton = new Button { Content = UiText.Pick("录入热键", "Capture hotkey") };
        var functionBox = new TextBox { Header = UiText.Pick("功能名称", "Function name"), PlaceholderText = UiText.Pick("例如：截图", "For example: Screenshot") };
        var scopeBox = new ComboBox { Header = UiText.Pick("作用范围", "Scope"), SelectedIndex = 0 };
        scopeBox.Items.Add(new ComboBoxItem { Content = UiText.Pick("全局", "Global"), Tag = "global" });
        scopeBox.Items.Add(new ComboBoxItem { Content = UiText.Pick("应用内", "In-app"), Tag = "foreground" });
        var note = new TextBlock { Text = UiText.Pick("只保存明确录入的组合键，不保存普通输入。该规则将标记为“用户声明 · 未经官方验证”。", "Only the explicitly captured combination is saved; ordinary input is never saved. The rule is labeled ‘User declared · not officially verified’."), TextWrapping = TextWrapping.Wrap };
        var captureArmed = false;
        captureButton.Click += (_, _) =>
        {
            captureArmed = true;
            captureButton.Content = UiText.Pick("请按组合键…", "Press the combination…");
            gestureBox.Focus(FocusState.Programmatic);
        };
        gestureBox.KeyDown += (_, args) =>
        {
            if (!captureArmed || IsModifierKey(args.Key)) return;
            var gestureText = ComposeGesture(args.Key);
            if (gestureText is null) return;
            gestureBox.Text = gestureText;
            captureArmed = false;
            captureButton.Content = UiText.Pick("重新录入", "Capture again");
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
            Title = UiText.Pick("我的规则", "My rules"),
            Content = content,
            PrimaryButtonText = UiText.Pick("保存用户规则", "Save user rule"),
            CloseButtonText = UiText.Pick("取消", "Cancel"),
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
                Title = UiText.Pick("最新规则包已收录此应用", "This app is already included in the latest rule pack"),
                Content = UiText.Pick("保存后，用户声明规则将优先于官方规则。是否覆盖该应用的官方归属结果？", "After saving, the user-declared rule will take priority over the official rule. Override the official ownership result for this app?"),
                PrimaryButtonText = UiText.Pick("覆盖并保存", "Override and save"),
                CloseButtonText = UiText.Pick("取消", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await overwrite.ShowAsync() != ContentDialogResult.Primary) return;
        }

        var locale = UiText.IsChinese ? "zh-CN" : "en-US";
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
        await ShowUpdateMessageAsync(
            UiText.Pick("用户规则已保存", "User rule saved"),
            UiText.Pick("用户声明 · 未经官方验证。可在设置中导出 local.krpack 进行备份。", "User declared · not officially verified. Export local.krpack from Settings to back it up."));
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
            await ShowUpdateMessageAsync(
                UiText.Pick("用户规则未导入", "User rules were not imported"),
                UiText.Pick("该文件不是有效的本地未签名 KeyRadar 规则包。", "The selected file is not a valid unsigned local KeyRadar rule pack."));
            return;
        }

        var variants = read.Pack!.Variants;
        var hotkeyCount = variants.Sum(variant => variant.Hotkeys.Count);
        var preview = string.Join("\n", variants.Take(12).Select(variant =>
            UiText.Pick(
                $"• {variant.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)}：{variant.Hotkeys.Count} 个热键",
                $"• {variant.DisplayName.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name)}: {variant.Hotkeys.Count} hotkeys")));
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiText.Pick("导入未签名用户规则？", "Import unsigned user rules?"),
            Content = UiText.Pick(
                $"它将识别 {variants.Count} 个应用变体，包含 {hotkeyCount} 个热键。\n\n{preview}\n\n来源：用户分享 · 未签名 · 未经官方验证。导入后将替换当前 local.krpack。",
                $"It identifies {variants.Count} app variants and contains {hotkeyCount} hotkeys.\n\n{preview}\n\nSource: user shared · unsigned · not officially verified. Importing replaces the current local.krpack."),
            PrimaryButtonText = UiText.Pick("导入并替换", "Import and replace"),
            CloseButtonText = UiText.Pick("取消", "Cancel"),
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
            await ShowUpdateMessageAsync(
                UiText.Pick("没有用户规则", "No user rules"),
                UiText.Pick("请先在“我的规则”中保存至少一条用户声明。", "Save at least one user declaration in My rules first."));
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
        var title = Uri.EscapeDataString(UiText.Pick("[候选规则] 应用热键归属", "[Candidate rule] App hotkey ownership"));
        var body = Uri.EscapeDataString(UiText.Pick(
            "请填写：\n- 软件名称、版本与发行渠道：\n- exe 名称与发布者：\n- 热键、功能与范围：\n- 是否修改过软件设置：\n- 官方文档链接或脱敏截图：\n- 冲突现象：\n\n请勿提交普通按键流、用户名、完整窗口标题、本地路径、账号或隐私数据。",
            "Please provide:\n- App name, version, and distribution channel:\n- Executable name and publisher:\n- Hotkey, function, and scope:\n- Whether app settings were changed:\n- Official documentation or a redacted settings screenshot:\n- Observed conflict:\n\nDo not submit ordinary keystrokes, user names, full window titles, local paths, account data, or private information."));
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
        CheckUpdateButtonText.Text = UiText.Pick("检查中…", "Checking…");
        try
        {
            var currentVersion = typeof(MainPage).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var result = await _updateClient.CheckAsync(currentVersion);
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Manifest is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = UiText.Pick($"发现 KeyRadar {result.Manifest.Version}", $"KeyRadar {result.Manifest.Version} is available"),
                    Content = UiText.Pick("签名与下载地址已验证。是否下载并安装？更新器会保留本地 data，失败时自动回滚。", "The signature and download address were verified. Download and install? The updater preserves local data and rolls back automatically on failure."),
                    PrimaryButtonText = UiText.Pick("下载并安装", "Download and install"),
                    CloseButtonText = UiText.Pick("稍后", "Later"),
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await DownloadAndInstallUpdateAsync(result.Manifest);
                }

                return;
            }

            await ShowUpdateMessageAsync(
                result.Status == UpdateCheckStatus.UpToDate ? UiText.Pick("已是最新版本", "Up to date") : UiText.Pick("暂时无法检查更新", "Unable to check for updates"),
                result.Status == UpdateCheckStatus.UpToDate
                    ? UiText.Pick($"当前版本 {currentVersion.ToString(3)} 已是最新版本。", $"Version {currentVersion.ToString(3)} is up to date.")
                    : UiText.Pick("网络、限流、404 或签名校验失败时，KeyRadar 会保留当前版本。请稍后重试。", "KeyRadar keeps the current version after network, rate-limit, 404, or signature failures. Try again later."));
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButtonText.Text = UiText.Pick("检查更新", "Check updates");
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
                    Title = UiText.Pick($"发现规则包 {check.Manifest.Version}", $"Rule pack {check.Manifest.Version} is available"),
                    Content = UiText.Pick("下载后会校验 SHA-256、发布签名和包内每条规则；上一版规则会保留用于回滚。", "After download, SHA-256, release signature, and every rule are validated. The previous rule pack is retained for rollback."),
                    PrimaryButtonText = UiText.Pick("下载并启用", "Download and activate"),
                    CloseButtonText = UiText.Pick("稍后", "Later"),
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await DownloadAndActivateRulesAsync(check.Manifest);
                }

                return;
            }

            await ShowUpdateMessageAsync(
                check.Status == RuleUpdateStatus.UpToDate ? UiText.Pick("规则已是最新版", "Rules are up to date") : UiText.Pick("暂时无法更新规则", "Unable to update rules"),
                check.Status == RuleUpdateStatus.UpToDate
                    ? UiText.Pick($"当前已启用官方规则包 {check.Manifest!.Version}。", $"Official rule pack {check.Manifest!.Version} is active.")
                    : UiText.Pick("断网、限流、404、哈希或签名失败时，KeyRadar 会继续使用当前规则。请稍后重试。", "KeyRadar continues using the current rules after network, rate-limit, 404, hash, or signature failures. Try again later."));
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
                await ShowUpdateMessageAsync(UiText.Pick("规则包验证失败", "Rule pack validation failed"), download.Message);
                return;
            }

            var activation = RulePackStore.Activate(
                candidatePath,
                RuntimeRuleCatalog.RulesDirectory,
                OfficialReleaseKey.GetBytes());
            if (!activation.IsSuccess)
            {
                await ShowUpdateMessageAsync(UiText.Pick("规则包未启用", "Rule pack was not activated"), activation.Message);
                return;
            }

            RuntimeRuleCatalog.Reload();
            await ScanAsync();
            await ShowUpdateMessageAsync(
                UiText.Pick("规则库已更新", "Rule library updated"),
                UiText.Pick($"官方签名规则包 {activation.ActiveVersion} 已启用；上一版可通过“回滚规则”恢复。", $"Signed official rule pack {activation.ActiveVersion} is active; use Roll back official rules to restore the previous pack."));
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
                rollback.IsSuccess ? UiText.Pick("规则已回滚", "Rules rolled back") : UiText.Pick("无法回滚规则", "Unable to roll back rules"),
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
            Title = UiText.Pick("导入官方签名规则包", "Import a signed official rule pack"),
            Content = UiText.Pick("KeyRadar 只读取你选择的 .krpack，并校验内置公钥、包身份、清单哈希和 rules/*.json。EXE、DLL、脚本、未知签名或非官方包都会被拒绝。成功后当前规则会保留为上一版，以便主动回滚。", "KeyRadar reads only the selected .krpack and validates the built-in public key, pack identity, manifest hashes, and rules/*.json. EXE, DLL, scripts, unknown signatures, and non-official packs are rejected. The current pack is retained as the previous version for rollback."),
            PrimaryButtonText = UiText.Pick("选择文件", "Choose file"),
            CloseButtonText = UiText.Pick("取消", "Cancel"),
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
            import.IsSuccess ? UiText.Pick("官方规则已导入", "Official rules imported") : UiText.Pick("规则包未导入", "Rule pack was not imported"),
            import.IsSuccess
                ? UiText.Pick($"官方签名规则包 {import.ActiveVersion} 已启用。", $"Signed official rule pack {import.ActiveVersion} is active.")
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
                Title = UiText.Pick("诊断包已导出", "Diagnostic bundle exported"),
                Content = UiText.Pick($"已生成 {fileName}。包内不含用户名、完整路径、窗口标题或普通按键流。", $"Created {fileName}. It contains no user name, full path, window title, or ordinary keystroke stream."),
                PrimaryButtonText = UiText.Pick("打开所在位置", "Open file location"),
                CloseButtonText = UiText.Pick("完成", "Done"),
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
            await ShowUpdateMessageAsync(
                UiText.Pick("无法导出诊断包", "Unable to export diagnostics"),
                UiText.Pick("KeyRadar 未写入不完整文件。请确认数据目录可写后重试。", "KeyRadar did not leave an incomplete file. Verify that the data directory is writable and try again."));
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }

    private DiagnosticReport BuildDiagnosticReport()
    {
        var completedScan = _completedScanState.Current;
        var version = typeof(MainPage).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        return new DiagnosticReport(
            version,
            RuntimeInformation.OSDescription,
            DateTimeOffset.UtcNow,
            completedScan.Applications)
        {
            ProbeTelemetry = DiagnosticProbeTelemetryProjector.Project(
                completedScan.ProbeResults,
                completedScan.DiagnosticItems),
        };
    }

    private static IReadOnlyList<DiagnosticApplication> BuildDiagnosticApplications(
        IReadOnlyList<ApplicationGroupViewModel> groups,
        IReadOnlyList<ApplicationSnapshot> snapshots) =>
        groups.Select(group =>
        {
            var snapshot = snapshots.FirstOrDefault(item => item.Process.Id == group.ProcessId);
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
        CheckUpdateButtonText.Text = UiText.Pick("正在下载…", "Downloading…");
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
            await ShowUpdateMessageAsync(
                UiText.Pick("下载验证失败", "Download validation failed"),
                UiText.Pick("更新包未通过完整性验证，当前版本未更改。", "The update package failed integrity validation. The current version was not changed."));
            return;
        }

        var extraction = UpdateArchiveExtractor.Extract(archivePath, stagingPath);
        if (!extraction.IsValid)
        {
            await ShowUpdateMessageAsync(
                UiText.Pick("更新包无法使用", "Update package cannot be used"),
                UiText.Pick("更新包结构不安全或不完整，当前版本未更改。", "The update package structure is unsafe or incomplete. The current version was not changed."));
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
            await ShowUpdateMessageAsync(
                UiText.Pick("无法启动更新器", "Unable to start the updater"),
                UiText.Pick("当前版本未更改。请确认解压目录可写后重试。", "The current version was not changed. Verify that the extracted directory is writable and try again."));
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = UiText.Pick("确定", "OK"),
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

    private void HotkeyFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filtersReady) ApplyFilter(SearchBox.Text);
    }

    private void ApplyFilter(string? query)
    {
        FilteredGroups.Clear();
        var normalized = query?.Trim();
        var category = SelectedTag(CategoryFilterComboBox);
        var modifier = SelectedTag(ModifierFilterComboBox);
        var confidence = SelectedTag(ConfidenceFilterComboBox);
        var sortMode = SelectedTag(SortModeComboBox);
        var filtered = new List<ApplicationGroupViewModel>();

        foreach (var group in _allGroups)
        {
            var groupMatchesSearch = !string.IsNullOrWhiteSpace(normalized) &&
                group.DisplayName.Contains(normalized, StringComparison.CurrentCultureIgnoreCase);
            var rows = group.Hotkeys
                .Where(hotkey => groupMatchesSearch ||
                    HotkeyInventorySearch.Matches(normalized, hotkey.Gesture, hotkey.Function, hotkey.SearchText))
                .Where(hotkey => MatchesCategory(group, hotkey, category))
                .Where(hotkey => MatchesModifier(hotkey, modifier))
                .Where(hotkey => MatchesConfidence(hotkey, confidence));
            rows = sortMode switch
            {
                "gesture" => rows.OrderBy(hotkey => hotkey.Gesture, StringComparer.OrdinalIgnoreCase),
                "confidence" => rows.OrderBy(ConfidenceRank).ThenBy(hotkey => hotkey.Gesture, StringComparer.OrdinalIgnoreCase),
                _ => rows,
            };
            var materialized = rows.ToArray();
            if (materialized.Length == 0)
            {
                continue;
            }

            filtered.Add(new ApplicationGroupViewModel(
                group.Id,
                group.DisplayName,
                group.PresenceLabel,
                group.EvidenceSummary,
                group.IconGlyph,
                group.IsExpanded || !string.IsNullOrWhiteSpace(normalized) || category != "all" || modifier != "all" || confidence != "all",
                materialized,
                group.ProcessId,
                group.IsForeground));
        }

        if (sortMode == "gesture")
        {
            filtered = filtered.OrderBy(group => group.Hotkeys[0].Gesture, StringComparer.OrdinalIgnoreCase).ToList();
        }
        else if (sortMode == "confidence")
        {
            filtered = filtered.OrderBy(group => ConfidenceRank(group.Hotkeys[0])).ThenBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        foreach (var group in filtered) FilteredGroups.Add(group);

        EmptyState.Visibility = FilteredGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SelectedTag(ComboBox? comboBox) =>
        comboBox?.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "all";

    private static bool MatchesCategory(
        ApplicationGroupViewModel group,
        HotkeyRowViewModel hotkey,
        string category) => category switch
    {
        "foreground" => group.IsForeground,
        "background" => group.ProcessId > 0 && !group.IsForeground && group.Id != "hardware-mappings",
        "global" => hotkey.IsGlobal,
        "hardware" => hotkey.IsHardware || group.Id == "hardware-mappings",
        "windows" => group.Id == "windows-system",
        "unknown" => group.Id == "unknown-occupancy" || hotkey.Confidence is OwnershipConfidence.Suspected or OwnershipConfidence.Unknown,
        _ => true,
    };

    private static bool MatchesModifier(HotkeyRowViewModel hotkey, string modifier)
    {
        if (modifier == "all") return true;
        if (!HotkeyGesture.TryParse(hotkey.Gesture, out var gesture)) return false;
        return modifier switch
        {
            "win" => gesture.Modifiers.HasFlag(HotkeyModifiers.Windows),
            "alt" => gesture.Modifiers.HasFlag(HotkeyModifiers.Alt),
            "ctrl" => gesture.Modifiers.HasFlag(HotkeyModifiers.Control),
            "shift" => gesture.Modifiers.HasFlag(HotkeyModifiers.Shift),
            "space" => gesture.Key.Equals("Space", StringComparison.OrdinalIgnoreCase),
            "backspace" => gesture.Key.Equals("Backspace", StringComparison.OrdinalIgnoreCase),
            "printscreen" => gesture.Key.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase),
            "functional" => IsFunctionalKey(gesture.Key),
            _ => true,
        };
    }

    private static bool MatchesConfidence(HotkeyRowViewModel hotkey, string confidence) => confidence switch
    {
        "confirmed" => hotkey.Confidence == OwnershipConfidence.Confirmed,
        "local" => hotkey.Confidence is OwnershipConfidence.LocalConfiguration or OwnershipConfidence.HardwareMapping,
        "official" => hotkey.Confidence is OwnershipConfidence.SystemKnown or OwnershipConfidence.Corroborated or OwnershipConfidence.OfficialDefault,
        "unknown" => hotkey.Confidence is null or OwnershipConfidence.Suspected or OwnershipConfidence.Unknown or OwnershipConfidence.UserDeclared,
        _ => true,
    };

    private static int ConfidenceRank(HotkeyRowViewModel hotkey) => hotkey.Confidence switch
    {
        OwnershipConfidence.Confirmed => 0,
        OwnershipConfidence.LocalConfiguration or OwnershipConfidence.HardwareMapping => 1,
        OwnershipConfidence.SystemKnown or OwnershipConfidence.Corroborated => 2,
        OwnershipConfidence.OfficialDefault => 3,
        OwnershipConfidence.UserDeclared => 4,
        OwnershipConfidence.Suspected => 5,
        _ => 6,
    };

    private static bool IsFunctionalKey(string key) =>
        key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24 ||
        key.StartsWith("Browser", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("Media", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("Volume", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("Launch", StringComparison.OrdinalIgnoreCase) ||
        key is "Backspace" or "Tab" or "Enter" or "Esc" or "Space" or "PageUp" or "PageDown" or
            "End" or "Home" or "Left" or "Up" or "Right" or "Down" or "PrintScreen" or "Insert" or "Delete";

    private void JumpToApplication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int processId })
        {
            _ = ApplicationActivator.TryActivate(processId);
        }
    }

    private HardwareEnvironmentSnapshot AddImportedHardwareProfile(HardwareEnvironmentSnapshot snapshot)
    {
        var imported = _hardwareProfileStore.Load();
        if (imported is null ||
            !snapshot.Software.Any(software =>
                software.Id.Equals(imported.SoftwareId, StringComparison.OrdinalIgnoreCase) &&
                (software.IsRunning || software.HasMatchingDevice)))
        {
            return snapshot;
        }

        return snapshot with
        {
            Profiles = snapshot.Profiles
                .Where(profile => !profile.IsUserDeclared)
                .Append(imported)
                .ToArray(),
        };
    }

}
