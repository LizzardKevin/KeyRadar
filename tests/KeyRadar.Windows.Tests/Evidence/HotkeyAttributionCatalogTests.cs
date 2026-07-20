using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hardware;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class HotkeyAttributionCatalogTests
{
    [Fact]
    public void Windows_system_rule_prevents_occupied_probe_from_being_listed_as_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+F4");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "windows-system",
                target,
                "Close the active window or app",
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.SystemKnown,
                "Microsoft documentation")],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.WindowsKnown, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
    }

    [Fact]
    public void Running_nvidia_rule_prevents_occupied_probe_from_being_listed_as_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+Z");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "nvidia-app",
                target,
                "Open NVIDIA Overlay (default hotkey, configurable)",
                HotkeyScope.Global,
                OwnershipConfidence.OfficialDefault,
                "NVIDIA documentation")],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Probe_stays_unknown_when_no_running_rule_or_local_evidence_exists()
    {
        var target = HotkeyGesture.Parse("Alt+Z");
        var catalog = HotkeyAttributionCatalog.Create([Probe(target)], [], [], []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.OccupiedOwnerUnknown, discovered.Ownership);
        Assert.Contains(target, catalog.UnknownProbeGestures);
    }

    [Theory]
    [InlineData("F12")]
    [InlineData("BrowserBack")]
    [InlineData("VolumeMute")]
    public void Occupied_unknown_noisy_special_key_is_diagnostic_only_and_not_actionable(string text)
    {
        var target = HotkeyGesture.Parse(text);
        var catalog = HotkeyAttributionCatalog.Create([Probe(target)], [], [], []);

        var diagnostic = Assert.Single(catalog.DiagnosticItems);
        Assert.Equal(HotkeyOwnershipStatus.OccupiedOwnerUnknown, diagnostic.Ownership);
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.UnknownProbeGestures);
        Assert.Empty(catalog.ActionableUnknownProbes);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Theory]
    [InlineData("Ctrl+F12")]
    [InlineData("Alt+A")]
    [InlineData("PrintScreen")]
    public void Occupied_unknown_supported_gesture_remains_in_inventory_and_actionable(string text)
    {
        var target = HotkeyGesture.Parse(text);
        var catalog = HotkeyAttributionCatalog.Create([Probe(target)], [], [], []);

        Assert.Equal(target, Assert.Single(catalog.Items).Gesture);
        Assert.Equal(target, Assert.Single(catalog.ActionableUnknownProbes).Gesture);
        Assert.True(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Foreground_rule_and_active_hardware_mapping_keep_bare_function_keys_in_inventory()
    {
        var foregroundF6 = HotkeyGesture.Parse("F6");
        var mappedF12 = HotkeyGesture.Parse("F12");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(foregroundF6), Probe(mappedF12)],
            [new RunningRuleHotkey(
                "chrome",
                foregroundF6,
                "Focus address bar",
                HotkeyScope.Foreground,
                OwnershipConfidence.OfficialDefault,
                "Chrome rule")],
            [],
            [ActiveHardwareProfile(mappedF12)]);

        Assert.Equal(2, catalog.Items.Count);
        Assert.Contains(catalog.Items, item => item.Gesture == foregroundF6 && item.Ownership == HotkeyOwnershipStatus.OfficialDefault);
        Assert.Contains(catalog.Items, item => item.Gesture == mappedF12 && item.Ownership == HotkeyOwnershipStatus.HardwareMappingFound);
        Assert.False(catalog.CanDeepConfirm(foregroundF6));
        Assert.False(catalog.CanDeepConfirm(mappedF12));
    }

    [Theory]
    [InlineData(HotkeyProbeAvailability.AvailableAtScanTime)]
    [InlineData(HotkeyProbeAvailability.SystemReserved)]
    [InlineData(HotkeyProbeAvailability.ProbeError)]
    public void Non_occupied_probe_without_evidence_is_diagnostic_only(HotkeyProbeAvailability availability)
    {
        var target = HotkeyGesture.Parse("BrowserBack");
        var catalog = HotkeyAttributionCatalog.Create([Probe(target, availability)], [], [], []);

        Assert.Single(catalog.DiagnosticItems);
        Assert.Empty(catalog.Items);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Occupied_probe_without_evidence_is_the_only_actionable_unknown_probe()
    {
        var occupied = HotkeyGesture.Parse("Alt+A");
        var available = HotkeyGesture.Parse("F1");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(occupied), Probe(available, HotkeyProbeAvailability.AvailableAtScanTime)],
            [],
            [],
            []);

        var item = Assert.Single(catalog.Items);
        Assert.Equal(occupied, item.Gesture);
        Assert.Equal(HotkeyOwnershipStatus.OccupiedOwnerUnknown, item.Ownership);
        Assert.Equal(occupied, Assert.Single(catalog.ActionableUnknownProbes).Gesture);
        Assert.True(catalog.CanDeepConfirm(occupied));
        Assert.False(catalog.CanDeepConfirm(available));
    }

    [Fact]
    public void Known_occupied_gesture_is_kept_once_under_its_evidence_and_cannot_be_deep_confirmed()
    {
        var target = HotkeyGesture.Parse("Alt+F4");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "windows-system",
                target,
                "Close active window",
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.SystemKnown,
                "Microsoft documentation")],
            [],
            []);

        var item = Assert.Single(catalog.Items);
        Assert.Equal(HotkeyOwnershipStatus.WindowsKnown, item.Ownership);
        Assert.Equal("windows-system", Assert.Single(item.Owners));
        Assert.Empty(catalog.ActionableUnknownProbes);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Latest_probe_result_controls_inventory_actionability()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [
                Probe(target, HotkeyProbeAvailability.Occupied, DateTimeOffset.Parse("2026-07-20T12:00:00Z")),
                Probe(target, HotkeyProbeAvailability.AvailableAtScanTime, DateTimeOffset.Parse("2026-07-20T12:01:00Z")),
            ],
            [],
            [],
            []);

        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.ActionableUnknownProbes);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Equal_time_probes_choose_the_same_conservative_result_regardless_of_input_order()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var scannedAt = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
        var available = Probe(target, HotkeyProbeAvailability.AvailableAtScanTime, scannedAt);
        var occupied = Probe(target, HotkeyProbeAvailability.Occupied, scannedAt);

        var availableFirst = HotkeyAttributionCatalog.Create([available, occupied], [], [], []);
        var occupiedFirst = HotkeyAttributionCatalog.Create([occupied, available], [], [], []);

        Assert.Equal(HotkeyProbeAvailability.Occupied, Assert.Single(availableFirst.Items).Availability);
        Assert.Equal(HotkeyProbeAvailability.Occupied, Assert.Single(occupiedFirst.Items).Availability);
        Assert.Equal(target, Assert.Single(availableFirst.ActionableUnknownProbes).Gesture);
        Assert.Equal(target, Assert.Single(occupiedFirst.ActionableUnknownProbes).Gesture);
    }

    [Fact]
    public void Multiple_running_rule_candidates_remain_known_but_are_marked_possible()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [
                new RunningRuleHotkey("feishu", target, "Screenshot", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "rule"),
                new RunningRuleHotkey("dingtalk", target, "Screenshot", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "rule"),
            ],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
    }

    [Fact]
    public void Occupied_single_suspected_rule_remains_possible_and_can_be_deep_confirmed()
    {
        var target = HotkeyGesture.Parse("Alt+Shift+S");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "sharex",
                target,
                "Capture region",
                HotkeyScope.Global,
                OwnershipConfidence.Suspected,
                "ShareX rule candidate")],
            [],
            []);

        var item = Assert.Single(catalog.Items);
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, item.Ownership);
        Assert.Equal("sharex", Assert.Single(item.Owners));
        Assert.Contains("ShareX rule candidate", item.Evidence);
        Assert.True(catalog.CanDeepConfirm(target));
    }

    [Theory]
    [InlineData(HotkeyProbeAvailability.AvailableAtScanTime)]
    [InlineData(HotkeyProbeAvailability.SystemReserved)]
    [InlineData(HotkeyProbeAvailability.ProbeError)]
    public void Non_occupied_single_suspected_rule_is_not_deep_confirmable(HotkeyProbeAvailability availability)
    {
        var target = HotkeyGesture.Parse("Alt+Shift+S");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target, availability)],
            [new RunningRuleHotkey(
                "sharex",
                target,
                "Capture region",
                HotkeyScope.Global,
                OwnershipConfidence.Suspected,
                "ShareX rule candidate")],
            [],
            []);

        var item = Assert.Single(catalog.Items);
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, item.Ownership);
        Assert.False(catalog.CanDeepConfirm(target));
    }

    [Fact]
    public void Normal_inventory_projects_local_hardware_and_probe_evidence_to_one_foreground_row()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [],
            [new LocalConfigurationHotkey("sharex", target, "Capture region", HotkeyScope.Global, "ShareX local configuration")],
            [ActiveHardwareProfile(target)]);

        var rows = NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("sharex", HotkeyInventoryGroup.ForegroundApplication, Evidence: DeepConfirmationEvidenceKind.LocalConfiguration),
                new HotkeyInventoryOwner("logitech-g-hub", HotkeyInventoryGroup.ActiveHardwareProfile, Evidence: DeepConfirmationEvidenceKind.ActiveHardwareProfile),
            ]);

        var row = Assert.Single(rows);
        Assert.Equal(target, row.Item.Gesture);
        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, row.Group);
        Assert.Equal(HotkeyOwnershipStatus.LocalConfigurationFound, row.Item.Ownership);
        Assert.Equal(HotkeyConflictStatus.HardwareMappingCollision, row.Item.Conflict);
        Assert.Contains("sharex", row.Item.Owners);
        Assert.Contains("logitech-g-hub", row.Item.Owners);
        Assert.Contains("ShareX local configuration", row.Item.Evidence);
        Assert.Contains("RegisterHotKey", string.Join(" ", row.Item.Evidence));
        Assert.Contains(row.DeepConfirmationCandidates, candidate =>
            candidate.OwnerId == "sharex" && candidate.Evidence == DeepConfirmationEvidenceKind.LocalConfiguration);
        Assert.Contains(row.DeepConfirmationCandidates, candidate =>
            candidate.OwnerId == "logitech-g-hub" && candidate.Evidence == DeepConfirmationEvidenceKind.ActiveHardwareProfile);
    }

    [Fact]
    public void Normal_inventory_keeps_secondary_and_ambiguous_owners_in_deep_confirmation_candidates()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [
                new RunningRuleHotkey("foreground-app", target, "Capture", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "Foreground rule"),
                new RunningRuleHotkey("secondary-app", target, "Capture", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "Secondary rule"),
                new RunningRuleHotkey("ambiguous-app", target, "Capture", HotkeyScope.Global, OwnershipConfidence.Suspected, "Ambiguous variant"),
            ],
            [],
            []);

        var row = Assert.Single(NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("foreground-app", HotkeyInventoryGroup.ForegroundApplication, "foreground", "Foreground", DeepConfirmationEvidenceKind.OfficialRule),
                new HotkeyInventoryOwner("secondary-app", HotkeyInventoryGroup.BackgroundApplication, "secondary", "Secondary", DeepConfirmationEvidenceKind.OfficialRule),
                new HotkeyInventoryOwner("ambiguous-app", HotkeyInventoryGroup.ForegroundApplication, "ambiguous", "Ambiguous", DeepConfirmationEvidenceKind.OfficialRule),
            ]));

        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, row.Group);
        Assert.Equal(
            ["ambiguous-app", "foreground-app", "secondary-app"],
            row.DeepConfirmationCandidates.Select(candidate => candidate.OwnerId).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Foreground_projected_row_retains_active_hardware_filter_facet()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [],
            [new LocalConfigurationHotkey("sharex", target, "Capture region", HotkeyScope.Global, "ShareX local configuration")],
            [ActiveHardwareProfile(target)]);

        var row = Assert.Single(NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("sharex", HotkeyInventoryGroup.ForegroundApplication),
                new HotkeyInventoryOwner("logitech-g-hub", HotkeyInventoryGroup.ActiveHardwareProfile),
            ]));

        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, row.Group);
        Assert.True(row.HasActiveHardwareEvidence);
    }

    [Fact]
    public void Search_matches_sanitized_owner_and_evidence_text()
    {
        Assert.True(HotkeyInventorySearch.Matches(
            "Logitech",
            "Alt+A",
            "Capture region",
            "logitech-g-hub G HUB active profile"));
        Assert.True(HotkeyInventorySearch.Matches(
            "profile",
            "Alt+A",
            "Capture region",
            "logitech-g-hub G HUB active profile"));
        Assert.False(HotkeyInventorySearch.Matches(
            "C:\\Users",
            "Alt+A",
            "Capture region",
            "logitech-g-hub G HUB active profile"));
    }

    [Fact]
    public void Deep_confirmation_keeps_only_gesture_matched_local_evidence()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var otherGesture = HotkeyGesture.Parse("Alt+B");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey("sharex", target, "Capture", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "Rule")],
            [],
            []);

        var row = Assert.Single(NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("sharex", HotkeyInventoryGroup.ForegroundApplication),
                new HotkeyInventoryOwner(
                    "sharex",
                    HotkeyInventoryGroup.ForegroundApplication,
                    Evidence: DeepConfirmationEvidenceKind.LocalConfiguration,
                    TargetGesture: otherGesture),
            ]));

        Assert.DoesNotContain(row.DeepConfirmationCandidates, candidate =>
            candidate.Evidence == DeepConfirmationEvidenceKind.LocalConfiguration);
    }

    [Fact]
    public void Normal_inventory_uses_stable_priority_and_keeps_known_gestures_out_of_unknown()
    {
        var altF4 = HotkeyGesture.Parse("Alt+F4");
        var altZ = HotkeyGesture.Parse("Alt+Z");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(altF4), Probe(altZ)],
            [
                new RunningRuleHotkey("windows-system", altF4, "Close active window", HotkeyScope.WindowsSystem, OwnershipConfidence.SystemKnown, "Microsoft documentation"),
                new RunningRuleHotkey("nvidia-app", altZ, "Open overlay", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "NVIDIA documentation"),
            ],
            [],
            []);

        var rows = NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("nvidia-app", HotkeyInventoryGroup.BackgroundApplication),
                new HotkeyInventoryOwner("windows-system", HotkeyInventoryGroup.WindowsSystem),
            ]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(HotkeyInventoryGroup.WindowsSystem, Assert.Single(rows, row => row.Item.Gesture == altF4).Group);
        Assert.Equal(HotkeyInventoryGroup.BackgroundApplication, Assert.Single(rows, row => row.Item.Gesture == altZ).Group);
        Assert.DoesNotContain(rows, row => row.Group == HotkeyInventoryGroup.UnknownOccupied);
    }

    [Fact]
    public void Windows_local_configuration_is_projected_with_the_windows_system_group()
    {
        var target = HotkeyGesture.Parse("PrintScreen");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey("windows-system", target, "Screenshot", HotkeyScope.WindowsSystem, OwnershipConfidence.SystemKnown, "Windows rule")],
            [new LocalConfigurationHotkey("windows-system", target, "Open snipping tool", HotkeyScope.WindowsSystem, "Current user setting")],
            []);

        var row = Assert.Single(NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [
                new HotkeyInventoryOwner("windows-system", HotkeyInventoryGroup.WindowsSystem, "windows-system", Evidence: DeepConfirmationEvidenceKind.LocalConfiguration),
                new HotkeyInventoryOwner("windows-system", HotkeyInventoryGroup.WindowsSystem, "windows-system", Evidence: DeepConfirmationEvidenceKind.OfficialRule),
            ]));

        Assert.Equal(HotkeyInventoryGroup.WindowsSystem, row.Group);
        Assert.Equal("windows-system", row.PrimaryGroupId);
        Assert.Equal(HotkeyOwnershipStatus.LocalConfigurationFound, row.Item.Ownership);
        Assert.Contains(row.Item.Evidence, evidence => evidence == "Current user setting");
    }

    [Fact]
    public void Windows_only_PrintScreen_keeps_windows_rule_and_local_configuration_candidates()
    {
        var target = HotkeyGesture.Parse("PrintScreen");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey("windows-system", target, "Screenshot", HotkeyScope.WindowsSystem, OwnershipConfidence.SystemKnown, "Windows rule")],
            [new LocalConfigurationHotkey("windows-system", target, "Open snipping tool", HotkeyScope.WindowsSystem, "Windows setting")],
            []);

        var row = Assert.Single(NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            WindowsOnlyPrintScreenOwners()));

        Assert.Equal(HotkeyInventoryGroup.WindowsSystem, row.Group);
        Assert.Equal("windows-system", row.PrimaryGroupId);
        Assert.Equal(2, row.DeepConfirmationCandidates.Count);
        Assert.Contains(row.DeepConfirmationCandidates, candidate => candidate.Evidence == DeepConfirmationEvidenceKind.OfficialRule);
        Assert.Contains(row.DeepConfirmationCandidates, candidate => candidate.Evidence == DeepConfirmationEvidenceKind.LocalConfiguration);
        Assert.Contains("Windows rule", row.Item.Evidence);
        Assert.Contains("Windows setting", row.Item.Evidence);
    }

    [Fact]
    public void Windows_PrintScreen_with_a_foreground_app_binding_projects_to_the_foreground_group()
    {
        var row = ProjectPrintScreenWithForegroundBinding(ForegroundPrintScreenOwners());

        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, row.Group);
        Assert.Equal("sharex", row.PrimaryGroupId);
        Assert.Equal(HotkeyConflictStatus.DefiniteConflict, row.Item.Conflict);
        Assert.Equal(3, row.DeepConfirmationCandidates.Count);
        Assert.Contains(row.DeepConfirmationCandidates, candidate => candidate.OwnerId == "windows-system" && candidate.Evidence == DeepConfirmationEvidenceKind.OfficialRule);
        Assert.Contains(row.DeepConfirmationCandidates, candidate => candidate.OwnerId == "windows-system" && candidate.Evidence == DeepConfirmationEvidenceKind.LocalConfiguration);
        Assert.Contains(row.DeepConfirmationCandidates, candidate => candidate.OwnerId == "sharex" && candidate.Evidence == DeepConfirmationEvidenceKind.LocalConfiguration);
        Assert.Contains("Windows rule", row.Item.Evidence);
        Assert.Contains("Windows setting", row.Item.Evidence);
        Assert.Contains("ShareX setting", row.Item.Evidence);
    }

    [Fact]
    public void Windows_and_foreground_PrintScreen_projection_is_independent_of_owner_input_order()
    {
        var first = ProjectPrintScreenWithForegroundBinding(ForegroundPrintScreenOwners());
        var reversed = ProjectPrintScreenWithForegroundBinding(ForegroundPrintScreenOwners().Reverse().ToArray());

        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, first.Group);
        Assert.Equal("sharex", first.PrimaryGroupId);
        Assert.Equal(first.Group, reversed.Group);
        Assert.Equal(first.PrimaryOwnerId, reversed.PrimaryOwnerId);
        Assert.Equal(first.PrimaryGroupId, reversed.PrimaryGroupId);
        Assert.Equal(first.DeepConfirmationCandidates, reversed.DeepConfirmationCandidates);
    }

    private static ProjectedHotkeyInventoryRow ProjectPrintScreenWithForegroundBinding(
        IReadOnlyList<HotkeyInventoryOwner> owners)
    {
        var target = HotkeyGesture.Parse("PrintScreen");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey("windows-system", target, "Screenshot", HotkeyScope.WindowsSystem, OwnershipConfidence.SystemKnown, "Windows rule")],
            [
                new LocalConfigurationHotkey("windows-system", target, "Open snipping tool", HotkeyScope.WindowsSystem, "Windows setting"),
                new LocalConfigurationHotkey("sharex", target, "Capture screen", HotkeyScope.Global, "ShareX setting"),
            ],
            []);

        return Assert.Single(NormalHotkeyInventoryProjector.Project(catalog.Items, owners));
    }

    private static IReadOnlyList<HotkeyInventoryOwner> WindowsOnlyPrintScreenOwners() =>
    [
        new HotkeyInventoryOwner("windows-system", HotkeyInventoryGroup.WindowsSystem, "windows-system", Evidence: DeepConfirmationEvidenceKind.OfficialRule),
        new HotkeyInventoryOwner("windows-system", HotkeyInventoryGroup.WindowsSystem, "windows-system", Evidence: DeepConfirmationEvidenceKind.LocalConfiguration),
    ];

    private static IReadOnlyList<HotkeyInventoryOwner> ForegroundPrintScreenOwners() =>
    [
        .. WindowsOnlyPrintScreenOwners(),
        new HotkeyInventoryOwner("sharex", HotkeyInventoryGroup.ForegroundApplication, "sharex", Evidence: DeepConfirmationEvidenceKind.LocalConfiguration),
    ];

    private static HotkeyProbeResult Probe(
        HotkeyGesture gesture,
        HotkeyProbeAvailability availability = HotkeyProbeAvailability.Occupied,
        DateTimeOffset? scannedAtUtc = null) => new(
        gesture,
        availability,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        scannedAtUtc ?? DateTimeOffset.UtcNow);

    private static HardwareProfileDescriptor ActiveHardwareProfile(HotkeyGesture gesture) => new(
        "logitech-g-hub",
        "Logitech G Keyboard",
        "Desktop Profile",
        HardwareProfileReadStatus.Active,
        false,
        null,
        [new HardwareMapping("G2", HardwareMappingTargetKind.Hotkey, gesture, gesture.ToString(), true)],
        "G HUB active profile");
}
