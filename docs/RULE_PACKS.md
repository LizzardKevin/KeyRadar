# KeyRadar Rule Packs

KeyRadar's official rules have exactly one maintained source: the declarative
JSON documents in the repository's `rules/` directory. Packaging validates
those files and turns them into an Ed25519-signed ZIP archive with the `.krpack`
extension. The application does not contain a parallel C# catalog or a hidden
fallback. The number of application variants is dynamic and is never a release
constant.

## Archive layout

```text
manifest.json
signature.ed25519
rules/
  wechat.json
  chrome.json
```

Only the two root metadata files and JSON files directly or recursively under
`rules/` are accepted. Backslashes, absolute paths, `.`/`..` path segments,
duplicate names, executables, DLLs, and scripts are rejected before import.

The archive is limited to 512 entries and 10 MiB uncompressed. Each rule file
is limited to 512 KiB and must be declared in `manifest.json` with its lowercase
SHA-256 digest.

## Manifest

```json
{
  "schemaVersion": 1,
  "packId": "io.github.lizzardkevin.keyradar.official",
  "version": "1.0.0",
  "files": [
    {
      "path": "rules/wechat.json",
      "sha256": "64-lowercase-hex-characters"
    }
  ]
}
```

`signature.ed25519` contains the Base64-encoded Ed25519 signature over the exact
UTF-8 bytes of `manifest.json`. Every runtime pack must use the official pack ID
and validate against the public key embedded in KeyRadar. Unsigned packs and
packs signed by another key are not importable.

`KeyRadar-v<version>-windows-x64.zip` contains
`KeyRadar-Rules-v<version>.krpack` beside `KeyRadar.exe`. On first launch that
bundled file is the active formal rule version. A later user-initiated rule
update is stored as `data/rules/active.krpack`; the replaced verified pack may
be retained as `previous.krpack` solely for explicit rollback. If an active pack
exists but is invalid, KeyRadar reports the failure and does not silently load
the bundled pack.

## Rule document

Rule documents follow [`schemas/keyradar-rule-v2.schema.json`](../schemas/keyradar-rule-v2.schema.json).
Readers are allowlisted and bounded. A rule may identify executables, declare
hotkeys, and name a supported configuration source; it cannot run commands,
load libraries, use environment expansion, or read arbitrary paths.

Official rules use exactly one verified pack at a time: `active.krpack` when
present, otherwise the version-matched pack bundled with the application. A
separate unsigned `local.krpack` stores user declarations. Runtime precedence is
user local rules, active signed official rules, then the bundled signed pack;
evidence from every matching source is retained. If no official pack can be
verified, the UI displays “规则不可用，请重新下载/导入” and still shows actual
RegisterHotKey occupancy results without fabricating rule ownership.

## Contribution checklist

- Use stable executable names and a unique lowercase application ID.
- Link the vendor documentation or configuration evidence in `sources`.
- State scope and confidence for every hotkey.
- Do not include personal paths, window titles, usernames, or real user config.
- Add or update catalog tests before submitting the rule.
- Keep all Windows system hotkeys in `rules/windows-system.json`; they are not
  compiled into the application.
