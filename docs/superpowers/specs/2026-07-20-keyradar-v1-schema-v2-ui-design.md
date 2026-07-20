# KeyRadar v1.0.0 Schema v2 and UI Design

Status: Approved design

Date: 2026-07-20

This specification supersedes the rule-model, navigation, localization, and
user-rule portions of `2026-07-20-keyradar-design.md`. The original detection,
privacy, native observation, performance, and process-lifetime requirements
remain in force unless this document explicitly changes them.

## Product and release boundary

- Product, executable, repository, namespaces, storage, and assets remain named
  KeyRadar.
- The product version remains `v1.0.0` until the first public release.
- The rule schema version is independent and advances to version 2.
- The first public release is blocked until the local executable, rule pack,
  interface, and WeChat `Alt+A` case are accepted by the product owner.
- Before that acceptance, work is limited to implementation, local validation,
  commits, and pushes. No GitHub Release or publishing Action may run.

## Terminology

All new product copy, documentation, schema vocabulary, C# types, tests, and
future conversation use one term:

- Chinese: `热键`
- English: `Hotkey` / `Hotkeys`
- Schema collection: `hotkeys`
- Domain types: `HotkeyRule`, `HotkeyGesture`, and equivalent `Hotkey*` names

Legacy terminology is migrated rather than retained as an alias because the
product has not yet shipped.

## Information architecture

The main navigation contains exactly three destinations:

1. 雷达总览 / Radar Overview
2. 热键总览 / Hotkey Overview
3. 设置 / Settings

The former privacy-status card is removed. Privacy remains an invariant, not a
changing navigation status.

### Radar Overview

The page uses a conflict-first dashboard.

- A large hero tile shows the definite-conflict count and the most urgent
  conflict. When the count is zero it becomes a green status tile stating that
  the current state is normal.
- Three compact tiles show available hotkeys, foreground-only hotkeys, and
  unconfirmed hotkeys.
- Each tile opens Hotkey Overview with the corresponding filter applied.
- A small corner action starts a new scan.
- After the dashboard tiles, a distinct `热键总览` title separates the result
  section from Radar Overview. A `查看全部` action opens the full destination.
- The embedded result section retains search and grouped results.

### Hotkey Overview

- Search accepts function names, application names, and normalized gestures.
  Examples include `截图`, `WeChat`, and `Alt+A`.
- A compact, collapsible facet rail filters by scope, application, primary key
  family, confidence, and conflict state.
- Scope facets include Windows, global, background, foreground, and unconfirmed.
- Key-family facets include Win, Alt, Ctrl, Shift, Space, Backspace, function
  keys, media keys, and other recognized keys.
- Results are always grouped into collapsible application-variant tags.
- Groups containing conflicts expand automatically. Other groups start
  collapsed.
- The application activation action appears once in the group header, never on
  every hotkey row.
- Rows show gesture, localized function, scope, confidence, conflict state, and
  a concise evidence summary.
- Ambiguous matches expose a `变体不确定` detail containing candidates, matched
  conditions, missing evidence, and sources.

### Foreground overlay

The existing parallel overlay remains part of v1.0.0. It is translucent,
topmost, draggable, scrollable, position-persistent, and non-activating. It
updates when the foreground application changes and hides for the desktop, lock
screen, KeyRadar itself, and applications without usable rules. It must not
take focus or block a hotkey.

### Settings

Settings contains:

- language and theme;
- application update;
- rule library and `下载最新规则包`;
- My Rules;
- candidate-rule submission;
- diagnostic export.

## Localization and theme

- Product resources use `zh-CN` and `en-US` resource files.
- Language changes apply without restarting where WinUI permits it; pages are
  rebuilt when necessary to avoid mixed-language content.
- Localized rule values are maps keyed by locale.
- If a rule lacks the selected locale, KeyRadar displays the text actually
  supplied by the rule. It does not invent a translation.
- A locally authored or submitted rule may contain one language only and records
  `submittedLocale`.
- Themes are System, Light, and Dark and persist locally.
- Windows 11 uses Mica for the main window and Acrylic for the overlay. Windows
  10 uses opaque theme-aware colors.

## Rule Schema v2

Each official JSON file represents exactly one application variant.

```json
{
  "schemaVersion": 2,
  "applicationId": "wechat",
  "variantId": "cn-desktop",
  "displayName": {
    "zh-CN": "微信",
    "en-US": "WeChat"
  },
  "match": {
    "executables": ["WeChat.exe"],
    "publishers": ["Tencent"],
    "versionRange": ">=4.0 <5.0",
    "packageFamilyNames": [],
    "distribution": "cn-official"
  },
  "hotkeys": [
    {
      "gesture": "Alt+A",
      "function": {
        "zh-CN": "截图",
        "en-US": "Screenshot"
      },
      "scope": "global",
      "confidence": "configuration",
      "sources": ["https://example.invalid/official-document"]
    }
  ]
}
```

`applicationId` and `variantId` are stable identifiers. Display names may
change without changing identity. Official source URLs must be real during rule
migration; the example URL above is illustrative only and cannot enter a pack.

Schema v2 is strictly declarative. Its version-range grammar supports bounded
numeric comparisons and intervals only. It cannot invoke code or arbitrary
expressions.

## Process identity and variant matching

The process inventory attempts to collect:

- executable file name;
- normalized file version;
- Authenticode signer publisher;
- `FileVersionInfo.CompanyName` as weaker publisher evidence;
- Package Family Name when the process has package identity;
- a normalized distribution tag such as official, Store, or portable;
- architecture and privilege information already required by the scanner.

Full install paths may be used ephemerally to inspect a selected process but are
not persisted in rules, logs, or diagnostics.

Variant resolution is deterministic and explainable:

1. Begin with variants whose executable names match.
2. Exclude a variant when a declared condition and a known machine value
   explicitly disagree.
3. Rank remaining evidence by specificity: exact PFN, Authenticode signer,
   version range, distribution tag, then executable name.
4. Missing machine evidence reduces confidence but is not a mismatch.
5. One sufficiently supported best candidate produces a variant match.
6. Multiple comparable candidates produce `变体不确定`, with all candidates and
   evidence retained.
7. Executable-only evidence produces `疑似归属` rather than a forced match.
8. No supported candidate produces `归属未知` and may enter targeted deep
   confirmation.

Windows display language is not a primary match signal. It controls product
copy only unless an application genuinely ships different behavior by locale.

## Layered rule catalogs

KeyRadar uses one parser and one matcher while keeping each source intact.

Priority is:

1. local user rules;
2. latest valid official rule pack;
3. rule pack delivered with the release.

The release contains `KeyRadar-Rules-v1.0.0.krpack`. Runtime storage is:

- `%LocalAppData%\KeyRadar\rules\official\active.krpack`
- `%LocalAppData%\KeyRadar\rules\official\previous.krpack`
- `%LocalAppData%\KeyRadar\rules\local.krpack`

Portable mode places equivalent files under `KeyRadar\data`.

Precedence is evaluated using application identity, normalized gesture, and
compatible scope. An exact variant match is strongest, but a synthetic local
variant does not prevent overlap detection when `applicationId` or the observed
process identity proves that both rules describe the same application.
Non-overlapping entries merge. Overridden entries and their evidence remain
available for difference views and recovery.

There is no C# rule catalog. If no valid pack is available, the product states
that rules are unavailable instead of fabricating results.

## Rule-pack security and update behavior

Official packs require a manifest, per-file SHA-256 digests, Schema v2
validation, and an Ed25519 signature verified by an embedded public key.

All packs, including unsigned imports, reject:

- executables, libraries, scripts, and unknown executable content;
- absolute paths, parent-directory traversal, and duplicate normalized paths;
- excessive compressed size, expanded size, entry count, and nesting;
- undeclared files, invalid JSON, incompatible schemas, and invalid ranges.

Rule updates are user-initiated. The update path downloads to a temporary file,
validates completely, preserves the current pack as `previous.krpack`, switches
atomically, and reloads. Any failure preserves the current working state.

If the latest official pack becomes invalid, KeyRadar uses the release-delivered
pack and shows a persistent prominent warning with `下载最新规则包`. If the
release-delivered pack is also invalid or missing, KeyRadar shows `规则不可用`.
It does not silently present stale data as current.

## My Rules

My Rules is a form-driven editor; raw JSON editing is not required.

The authoring flow is:

1. Select a process from the current inventory.
2. Populate executable name, version, signer publisher, and PFN when available.
3. Enter one explicit hotkey-recording state.
4. Fill function, scope, notes, and text in the current product language.
5. Optionally add the second language.
6. Save atomically to `local.krpack`.

Every local entry displays `用户声明 · 未经官方验证`.

Hotkey recording is a bounded one-shot operation. It begins only after an
explicit click, shows a countdown and cancel action, never blocks the original
function, and clears its in-memory key state on success, cancel, or timeout. It
accepts modifier combinations and an allowlist of standalone functional keys,
including function, Space, Backspace, Escape, and media keys. Standalone ordinary
letters, digits, and text are discarded. No ordinary key stream is logged or
persisted.

Users may edit, disable, recover, export, and import local packs. Destructive
deletion is recoverable before compaction. Import preview lists applications,
variants, hotkeys, signature state, fields read by match conditions, and overlaps
with official rules.

## Official-overlap resolution

When a new official pack includes an application represented by a local rule,
KeyRadar first detects application coverage by `applicationId` or matching
process identity. It then identifies actual hotkey overlaps by normalized
gesture and compatible scope, using an exact variant match as additional
evidence rather than a mandatory condition. It displays a per-application
difference view and asks the user to:

- use the official rule;
- keep My Rule; or
- inspect differences.

`Use official` disables only overlapping local entries. It does not delete them.
Non-overlapping local entries continue to merge. Batch updates may offer
`全部使用官方规则`, but never select it silently. Invalid official data never
replaces local data.

## Candidate-rule submission

The application creates a candidate from a local entry and shows its exact final
JSON before leaving KeyRadar. The candidate may contain one language and records
`submittedLocale`.

The privacy filter rejects or removes ordinary key streams, user names, complete
window titles, local paths, account data, and fields outside the candidate
allowlist. The preview lists application name, version, distribution, executable,
publisher, gesture, function, scope, settings-change status, conflict behavior,
and provided evidence.

After explicit confirmation, KeyRadar opens the repository's structured GitHub
Issue Form in the default browser. Form-field IDs are used as URL query keys to
prefill supported text fields. The product requests no GitHub token and does not
publish through an API. The user reviews the form and attaches any already
redacted screenshots in GitHub.

Maintainers review the issue, add evidence and official translations, create the
official variant JSON, pass validation, and include it in a signed official pack.

References:

- https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/creating-an-issue
- https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-githubs-form-schema

## Migration

The current 50 supported applications and Windows system rules migrate to
Schema v2 JSON. An application may produce multiple variant files, so file count
is not constrained to 51. The supported-application count remains 50, with
Windows outside that count.

Because no public version exists, Schema v1 and its domain model are removed
rather than retained as a compatibility path. A development-machine Schema v1
pack is reported as incompatible and cannot override valid v2 data.

## Validation and acceptance

Local validation and future CI must invoke the same validators.

Required automated coverage includes:

- Schema v2 and localization fallback;
- strict version-range parsing;
- exact, partial, ambiguous, and rejected variant matches;
- publisher-source weighting and missing evidence;
- local, active-official, and release-delivered precedence;
- signature, digest, traversal, content-type, and archive-limit failures;
- invalid-active fallback, persistent warning, and rule-download action;
- local authoring, one-shot recording, import, export, and recovery;
- official-overlap differences and non-destructive choices;
- candidate allowlist, privacy rejection, locale marker, and form URL encoding;
- search, facets, application grouping, and application-level activation;
- Chinese, English, System, Light, and Dark behavior.

Existing release gates remain mandatory: x86/x64 and elevation behavior,
foreground refresh within 250 ms, results within five seconds in a typical
200-process environment, idle CPU below 0.5% of one core, full process exit
within two seconds, privacy-safe diagnostics, Windows 10/11 coverage, and
complete observer unload after targeted confirmation.

The WeChat acceptance case must show `Alt+A · 截图`, WeChat ownership, evidence,
the conflict target, and one application-level activation action.

## Deferred until local acceptance

The implementation may add local validators and packaging scripts, but it does
not create a public release or enable publishing automation. After the product
owner accepts the local executable and initial pack, a separate approval enables
GitHub Actions, signing assets, reproducible packaging, and the first `v1.0.0`
Release.
