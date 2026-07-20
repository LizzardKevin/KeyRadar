# KeyRadar v1.0 Design

## Purpose

KeyRadar answers which running application or Windows component owns a shortcut,
what that shortcut does, and how certain the answer is. It never claims that the
Windows public API can enumerate every owner or feature name.

## Experience

The main WinUI 3 window scans on launch and groups system, foreground, and
background shortcuts by application. Groups are collapsed unless they contain a
conflict. Rows show gesture, function, scope, confidence, conflict state,
evidence, and an action that activates the application's recent main window.

A separate translucent topmost window follows the active application without
taking focus. It hides for the desktop, lock screen, KeyRadar itself, and unknown
applications. Windows 11 uses Mica and Acrylic; Windows 10 uses opaque fallback
materials.

## Detection

The default pipeline combines process identity, declarative application rules,
bounded configuration readers, Windows shortcut knowledge, UI Automation, and
non-synthesizing registration probes. A targeted deep-confirm command may load
32-bit and 64-bit message observers only for one selected shortcut. It waits for
one physical press and unloads on success, cancellation, or a 30-second timeout.

Confidence values are Confirmed, ConfigurationFound, SystemKnown, Suspected, and
Unknown. Conflict values are Definite, PossibleInterception, ContextDuplicate,
and None. Evidence remains additive even when newer rules override metadata.

## Rules and updates

Official `.krpack` archives contain a manifest, declarative JSON rules, file
digests, and an Ed25519 signature. They cannot contain executable content.
Unsigned local JSON is supported with a permanent source marker and an import
preview of every permitted path.

Program and rule updates are user initiated and hosted on GitHub Releases. The
updater verifies SHA-256 and Ed25519, swaps files after the app exits, runs a
health check, and rolls back failures. User data is stored in LocalAppData unless
`portable.flag` selects a sibling `data` directory.

## Safety and scope

KeyRadar is x64-only on Windows 10 22H2 and Windows 11, but observes WoW64
applications through an x86 helper. It creates no service, scheduled task, tray
process, telemetry stream, or automatic updater. Closing the main window exits
all components.

