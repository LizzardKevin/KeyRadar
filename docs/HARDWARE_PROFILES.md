# KeyRadar hardware profile import

KeyRadar does not parse undocumented vendor databases or guess the contents of
encrypted G HUB, Logi Options+, Razer Synapse, or Corsair iCUE profiles. When a
live mapping cannot be read safely, the Settings page can import a declarative
JSON document using the format below.

Imported profiles are always labeled **user-declared · not live-verified**. They
can identify a possible collision, but they never become confirmed ownership
evidence. The profile is shown only while matching hardware or its management
software is present in the current desktop session.

```json
{
  "schemaVersion": 1,
  "softwareId": "logitech-g-hub",
  "deviceName": "Logitech G Keyboard",
  "profileName": "Desktop Profile",
  "isCurrent": true,
  "isOnboardMemory": false,
  "slot": null,
  "mappings": [
    {
      "physicalTrigger": "G2",
      "targetKind": "hotkey",
      "targetGesture": "Alt+A",
      "displayTarget": "Alt+A"
    },
    {
      "physicalTrigger": "G3",
      "targetKind": "macroSequence",
      "targetGesture": null,
      "displayTarget": "content is discarded during import"
    }
  ]
}
```

Supported `targetKind` values are `singleKey`, `hotkey`, `systemCommand`,
`launchApplication`, `applicationAction`, and `macroSequence`.

Privacy and validation rules:

- The selected source file is limited to 1 MiB and 256 mappings.
- `softwareId` and physical triggers accept a bounded identifier character set.
- Hotkey targets must pass KeyRadar's gesture parser.
- Macro contents are replaced with `Macro sequence (content hidden)`.
- Launch paths are replaced with `Launch application (path hidden)`.
- The sanitized copy is stored at
  `%LocalAppData%\KeyRadar\hardware\imported-profile.json`.
- The original selected path is never retained or included in diagnostics.
