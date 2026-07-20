# Contributing to KeyRadar

KeyRadar accepts code, documentation, test fixtures, and application rule
contributions. Open an issue before large architectural work.

## Rules must stay declarative

Rule contributions may describe process identity, known shortcuts, and bounded
configuration readers. They may not execute programs, scripts, shell commands,
or dynamically loaded code.

Never include personal configuration files, usernames, window titles, tokens,
or other private data in fixtures. Replace those values with deterministic test
data before committing.

## Development workflow

1. Create a focused branch.
2. Write a failing test for behavioral changes.
3. Implement the smallest change that passes it.
4. Run the complete test and build commands documented in the repository.
5. Keep commits small and explain externally visible behavior in the pull request.

