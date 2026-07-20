# KeyRadar Privacy Design

KeyRadar has no telemetry, account system, advertising identifier, or automatic
cloud upload. Network access occurs only after the user selects a GitHub program
or rule update action.

Keyboard events are compared in memory only with known shortcut candidates.
Ordinary typed text is not retained. The passive observer ignores injected input
and retains only a normalized modifier combination long enough to update the UI.

Diagnostics are created only after the user selects **Export diagnostics**. The
ZIP contains application IDs and display names, executable file names (never full
paths), versions, publishers, architecture, privilege class, presence, and the
shortcut results already visible in KeyRadar. It excludes usernames, window
titles, ordinary key streams, configuration contents, machine identifiers, and
telemetry identifiers. Nothing is uploaded automatically.

Normal mode stores rules, update staging, and diagnostics under
`%LocalAppData%\KeyRadar`. If a `data` directory exists beside `KeyRadar.exe`,
portable mode uses that directory instead. Program updates never replace either
data location. The bundled and updated `.krpack` files contain declarative public
shortcut metadata only; KeyRadar never writes observed ordinary key presses into
them.
