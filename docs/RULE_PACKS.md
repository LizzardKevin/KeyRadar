# KeyRadar Rule Packs

KeyRadar rule packs are declarative ZIP archives with the `.krpack` extension.
They add application identities and known shortcuts without loading executable
code into KeyRadar.

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
  "packId": "com.example.keyradar.rules",
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
UTF-8 bytes of `manifest.json`. Official online packs must validate against a
public key embedded in the application. Locally imported unsigned rules use a
separate workflow and are always labeled “未签名本地规则”.

## Rule document

Rule documents follow [`schemas/keyradar-rule-v1.schema.json`](../schemas/keyradar-rule-v1.schema.json).
Readers are allowlisted and bounded. A rule may identify executables, declare
shortcuts, and name a supported configuration source; it cannot run commands,
load libraries, use environment expansion, or read arbitrary paths.

## Contribution checklist

- Use stable executable names and a unique lowercase application ID.
- Link the vendor documentation or configuration evidence in `sources`.
- State scope and confidence for every shortcut.
- Do not include personal paths, window titles, usernames, or real user config.
- Add or update catalog tests before submitting the rule.
