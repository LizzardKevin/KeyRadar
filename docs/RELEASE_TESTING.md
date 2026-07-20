# KeyRadar Release Testing

Every stable release must pass the automated CI and native integration suites,
then the manual Windows matrix below. A failed row blocks the stable tag.

## Automated gates

- Locked restore, Release build, and all managed tests.
- Native x64 and x86 build, self-test, and WM_HOTKEY ownership integration test.
- Signed `.krpack`, update manifest, SHA-256 files, and deterministic ZIP creation.
- The portable ZIP contains the same versioned official `.krpack` beside
  `KeyRadar.exe`; first launch succeeds without a network connection.
- Archive traversal, executable/script rule payload, malformed JSON, signature,
  hash, rollback, health-check, and privacy-redaction tests.
- Missing, damaged, wrong-ID, and bad-signature packs produce an explicit
  unavailable state; an invalid active pack never falls back to bundled rules.
- The initial catalog contains exactly 50 distinct applications and a WeChat
  `Alt+A` screenshot rule.

## Manual Windows matrix

| Gate | Windows 10 22H2 | Windows 11 24H2 | Windows 11 25H2 |
| --- | --- | --- | --- |
| Portable launch and Mica/solid fallback | Required | Required | Required |
| Foreground overlay refresh under 250 ms, no focus theft | Required | Required | Required |
| WeChat `Alt+A` owner, function, evidence, conflict and app jump | Required | Required | Required |
| x86/x64 and elevated-target deep confirmation | Required | Required | Required |
| Main-window close exits every KeyRadar process within 2 s | Required | Required | Required |
| 60 s idle CPU below 0.5% of one logical core | Required | Required | Required |
| 200-process first result within 5 s | Required | Required | Required |
| Program update health check and automatic rollback | Required | Required | Required |
| Official rule update/import and explicit previous-pack rollback | Required | Required | Required |
| Diagnostic ZIP privacy inspection | Required | Required | Required |

The project does not claim a matrix row until it has run on that OS image.
Ed25519 release signatures authenticate KeyRadar assets. Windows Authenticode is
separate and is applied only when the maintainer has a trusted code-signing
certificate.
