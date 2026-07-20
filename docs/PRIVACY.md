# KeyRadar Privacy Design

KeyRadar has no telemetry, account system, advertising identifier, or automatic
cloud upload. Network access occurs only after the user selects a GitHub program
or rule update action.

Keyboard events are compared in memory only with known shortcut candidates.
Ordinary typed text is not retained. Diagnostic logs contain module names,
application display names, rule versions, and error codes, and are capped at
five files. Exported diagnostics remove usernames, full paths, and window titles
by default and are previewed before writing.

