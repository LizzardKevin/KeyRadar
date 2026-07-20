# Security Policy

## Supported versions

Until v1.0 is released, only the latest commit on the active development branch
receives security fixes. After release, the latest stable version is supported.

## Reporting a vulnerability

Please use GitHub private vulnerability reporting for the KeyRadar repository.
Do not publish proof-of-concept code that exposes users' keyboard input,
configuration files, or update channels before a fix is available.

## Security boundaries

- KeyRadar does not store ordinary typed text.
- Rule packs cannot execute code.
- Updates and official rule packs require both a cryptographic signature and a
  SHA-256 digest.
- Process message observation is off by default and scoped to one user-selected
  shortcut with a fixed timeout.

