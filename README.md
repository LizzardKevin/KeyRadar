# KeyRadar · 键位雷达

> 扫描全局，发现每一次占用。

KeyRadar is a Windows hotkey ownership and conflict diagnostic tool. It scans
the shortcuts exposed by Windows and currently running applications, explains
where each result came from, and keeps uncertain results explicitly uncertain.

## Product principles

- On demand: closing the main window exits every KeyRadar component.
- Passive by default: KeyRadar never synthesizes or suppresses keyboard input.
- Evidence first: ownership and conflicts carry a visible confidence level.
- Local by default: no telemetry, accounts, or background uploads.
- Extensible: signed `.krpack` rule packs add application knowledge without
  executing third-party code.

## Planned v1 experience

- A grouped global/background shortcut inventory with conflicts expanded first.
- A translucent, always-on-top foreground shortcut window that never steals focus.
- Targeted deep confirmation for one unknown shortcut at a time.
- Initial profiles for 50 Windows applications plus Windows system shortcuts.
- Portable x64 releases and user-initiated updates from GitHub Releases.

## Status

KeyRadar v1.0 is under active development on `codex/keyradar-v1`.

## Documentation

- [Product design](docs/superpowers/specs/2026-07-20-keyradar-design.md)
- [Implementation plan](docs/superpowers/plans/2026-07-20-keyradar-v1.md)
- [Privacy](docs/PRIVACY.md)
- [Rule-pack authoring](docs/RULE_PACKS.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)

## License

[MIT](LICENSE)
