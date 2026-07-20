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
- Single-source rules: every formal shortcut rule lives in `rules/*.json` and is
  delivered only through a signed `.krpack`; there is no compiled C# fallback.

## KeyRadar v1

- Groups shortcuts into collapsible application tags. Each application header has
  one **Go to application** action; shortcut rows do not repeat it.
- Shows Windows, foreground, and background shortcuts with confidence and evidence.
- Expands conflicts automatically and keeps uncertain ownership visibly uncertain.
- Shows a translucent, always-on-top foreground overlay without taking focus.
- Deep-confirms one selected global shortcut for at most 30 seconds while the
  original shortcut continues to execute normally.
- Recognizes 50 initial Windows applications plus Windows system shortcuts.
- Updates the portable application and official rule pack only after a user click.
- Loads the signed rule pack bundled beside `KeyRadar.exe` on first launch, and
  keeps one previous verified pack only for an explicit rollback.
- Shows an explicit “rules unavailable” error if the pack is missing, damaged,
  has the wrong identity, or fails signature verification.

## Install and use

1. Download `KeyRadar-v1.0.0-windows-x64.zip` from [GitHub Releases](https://github.com/LizzardKevin/KeyRadar/releases).
2. Extract the complete ZIP. Keep `KeyRadar-Rules-v1.0.0.krpack` beside
   `KeyRadar.exe`, then run `KeyRadar.exe`. No installer, service, tray process,
   scheduled task, or account is created.
3. Press a shortcut normally or search for it. KeyRadar observes modifier
   combinations but does not suppress or synthesize input.
4. Expand an application to inspect its shortcuts. Use the single header-level
   **Go to application** button when you want to bring that app forward.
5. Close the main window to exit KeyRadar and every helper process.

Windows 10 22H2 and Windows 11 x64 are supported. The x64 application includes
both x64 and x86 observation helpers for WoW64 applications.

## Build from source

Install the .NET 10 SDK and Visual Studio Build Tools with the Windows C++
toolchain, then run:

```powershell
dotnet restore KeyRadar.sln --locked-mode
dotnet build KeyRadar.sln -c Release --no-restore
dotnet test KeyRadar.sln -c Release --no-build
./eng/Build-Native.ps1 -Configuration Release
```

Signed release reproduction additionally requires the project Ed25519 private
release key in `KEYRADAR_ED25519_PRIVATE_KEY`; the key is never committed.
The release build validates the 51 source documents, signs the official pack,
and places that same `.krpack` both inside the application ZIP and among the
standalone Release assets.

## Documentation

- [Product design](docs/superpowers/specs/2026-07-20-keyradar-design.md)
- [Implementation plan](docs/superpowers/plans/2026-07-20-keyradar-v1.md)
- [Privacy](docs/PRIVACY.md)
- [Rule-pack authoring](docs/RULE_PACKS.md)
- [Release testing](docs/RELEASE_TESTING.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)

## License

[MIT](LICENSE)
