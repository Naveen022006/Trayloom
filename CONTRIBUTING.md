# Contributing to Trayloom

Everyone is welcome to report bugs, suggest features, improve documentation and translations, or submit code.
You do not need permission to fork the repository or open a pull request. For a large change, opening an issue first
helps agree on scope before investing time.

## License and ownership

Trayloom is licensed under **GPL-3.0-or-later**; see [LICENSE](LICENSE). You may use, study, modify, share and sell
copies under that license. When distributing covered versions, preserve license and copyright notices, identify
modifications, and provide the corresponding source as required by the GPL. Private modifications do not have to
be published. This summary does not replace the license.

Contributions are submitted under GPL-3.0-or-later. Contributors retain their copyright; no copyright assignment
or separate contributor license agreement is required. Submit only work you have the right to contribute and
preserve upstream attribution. Third-party code and assets need compatible licenses and their required notices.
Trayloom builds on [Vorssaint](https://github.com/vorssaint/vorssaint-utils); do not remove its existing attribution.

## Make a pull request

1. Fork [Trayloom](https://github.com/Naveen022006/Trayloom) and clone your fork.
2. Create a branch from the latest `main` for one focused change.
3. Make the change, build it, and run relevant checks below.
4. Commit and push to your fork, then open a pull request against this repository's `main` branch.
5. Explain the problem, the resulting behavior, and what you tested. Draft PRs are welcome.

Maintainer **Naveen022006** reviews and decides what merges and ships. A public repository allows everyone to
fork and propose changes; it does not give everyone direct push or release access. Acceptance is not guaranteed.
Follow the [Code of Conduct](CODE_OF_CONDUCT.md). Report vulnerabilities privately using [SECURITY.md](SECURITY.md).

## Build and verify on Windows

Use Windows 10 build 19041 or newer, or Windows 11, with the .NET 9 SDK and Git installed.
From the repository root:

```powershell
dotnet restore Trayloom.sln
dotnet build Trayloom.sln -c Release --no-restore -m:1
```

To run a development copy without sharing the installed app's settings or single-instance channel:

```powershell
$env:TRAYLOOM_DATA = Join-Path $env:TEMP ('Trayloom-dev-' + [guid]::NewGuid().ToString('N'))
$env:TRAYLOOM_INSTANCE = 'contributor-dev'
$env:TRAYLOOM_NO_INSTALL_PROMPT = '1'
dotnet run --project src/Trayloom.App
```

Use a dedicated PowerShell session and quit the development copy when finished. Data isolation does not isolate
Windows effects: test only relevant tools and restore changed system state. Do not automate input injection or
change Windows security settings to bypass unsigned-build restrictions.

Read [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for development flags and
[TESTING.md](TESTING.md) for manual checks. Console harnesses under `tests/` cover specific areas; run the relevant
harness after reviewing its instructions and side effects. The solution build alone does not run those harnesses.
For UI changes, check keyboard navigation, light/dark themes and Windows display scaling; describe the combinations tested.
Sanitized screenshots of the development UI may be attached to PRs, but keep screenshots and personal data out of commits.

Build Windows release assets with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

The outputs are in ignored `dist/`. Builds are unsigned. Release maintainers must include license and third-party
notices and make the corresponding source for distributed binaries available under the applicable licenses.

## Project conventions

- Read [ARCHITECTURE_V2.md](ARCHITECTURE_V2.md), [specs/CONTRACTS.md](specs/CONTRACTS.md), and the relevant feature spec.
- Match surrounding C# style, preserve SPDX headers, and use the localization helpers for visible strings.
- Features must undo their side effects in `Stop()` and must not leave other applications' windows hidden.
- Discuss new dependencies before adding them. Avoid unrelated formatting and generated build files.
- Never commit credentials, tokens, settings, clipboard contents, captures or personal logs. Redact reports before posting.
- Describe limitations honestly; distinguish automated checks from manual checks you actually performed.
