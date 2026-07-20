# KeyRadar Privacy Design

KeyRadar has no telemetry, account system, advertising identifier, or automatic
cloud upload. Network access occurs only after the user selects a GitHub program
or rule update action.

Normal scanning does not install a global keyboard hook and does not receive an
ordinary key-event stream. Before each bounded RegisterHotKey probe, KeyRadar
checks whether a physical keyboard key is currently held; it pauses or cancels
instead of storing that key identity. A hotkey combination is captured only when
the user explicitly selects **Record hotkey** in My rules. Ordinary typed text is
never retained.

Diagnostics are created only after the user selects **Export diagnostics**. The
ZIP contains application IDs and display names, executable file names (never full
paths), versions, publishers, architecture, privilege class, presence, and the
hotkey results already visible in KeyRadar. It excludes usernames, window
titles, ordinary key streams, configuration contents, machine identifiers, and
telemetry identifiers. Nothing is uploaded automatically.

Normal mode stores rules, update staging, and diagnostics under
`%LocalAppData%\KeyRadar`. If a `data` directory exists beside `KeyRadar.exe`,
portable mode uses that directory instead. Program updates never replace either
data location. The bundled and updated `.krpack` files contain declarative public
hotkey metadata only; KeyRadar never writes observed ordinary key presses into
them.

An imported hardware profile is copied only after validation and redaction.
Macro contents and launch paths are replaced with fixed placeholders, and the
original selected file path is not retained.
