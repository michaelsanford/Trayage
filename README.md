# Trayage

[![CI](https://github.com/michaelsanford/Trayage/actions/workflows/ci.yml/badge.svg)](https://github.com/michaelsanford/Trayage/actions/workflows/ci.yml)
[![PowerShell Lint](https://github.com/michaelsanford/Trayage/actions/workflows/powershell.yml/badge.svg)](https://github.com/michaelsanford/Trayage/actions/workflows/powershell.yml)
[![CodeQL](https://github.com/michaelsanford/Trayage/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/michaelsanford/Trayage/actions/workflows/github-code-scanning/codeql)
[![Release](https://github.com/michaelsanford/Trayage/actions/workflows/release.yml/badge.svg)](https://github.com/michaelsanford/Trayage/actions/workflows/release.yml)
[![Pages](https://github.com/michaelsanford/Trayage/actions/workflows/pages/pages-build-deployment/badge.svg)](https://github.com/michaelsanford/Trayage/actions/workflows/pages/pages-build-deployment)

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows11&logoColor=white)](#requirements)
[![License: MIT](https://img.shields.io/github/license/michaelsanford/Trayage?color=blue)](LICENSE)

[![Build provenance](https://img.shields.io/badge/provenance-attested-success?logo=github)](https://github.com/michaelsanford/Trayage/attestations)
[![SBOM](https://img.shields.io/badge/SBOM-CycloneDX-blueviolet)](#verifying-a-release)
[![Signed](https://img.shields.io/badge/signed-cosign-2ea44f?logo=sigstore&logoColor=white)](#verifying-a-release)

A .NET 10 Windows system-tray app that gathers your **GitHub** and **Bitbucket Cloud**
activity into one unified inbox, lets you jump straight to the relevant page with a
click, and raises native Windows toast notifications for the things you care about.

Trayage is the successor to [bittray](https://github.com/michaelsanford/bittray) — a Go
tray app that watched Bitbucket Server for pull requests needing review. Trayage modernises
the idea on .NET 10 with a Fluent (Windows 11) UI, adds GitHub, and broadens "needs review"
into a configurable inbox.

**Website:** https://michaelsanford.github.io/Trayage/

## Install

Grab the latest build from the [**Releases**](https://github.com/michaelsanford/Trayage/releases)
page: download the `.zip` for your architecture (`win-x64` for most PCs, `win-arm64` for
Arm devices), unzip it anywhere, and run `Trayage.exe`. The build is self-contained — no
.NET runtime install required. It lands in the system tray; there's no main window.

Each release ships with build provenance, a cosign signature, and a CycloneDX SBOM — see
[Verifying a release](#verifying-a-release) if you'd like to check them first.

## Features

- **Unified inbox** in a tray flyout — grouped by repository or as a flat newest-first
  list, with the option to hide already-read items. Click any item to open it in your browser.
- **Sources**
  - **GitHub** via the notifications API — review requests, mentions, assignments, CI
    activity, and watched-repo activity.
  - **Bitbucket Cloud** via pull-request queries — PRs you authored, PRs in watched repos
    where you're a reviewer, and all activity in watched repos.
- **Native toast notifications** with per-class toggles (review requests, mentions &
  assignments, CI/check status) plus **all activity on repositories you choose to watch**.
  Clicking a toast opens the page.
- **Tray icon** — a blue inbox tray (carrying an upward chevron) whose state shows above it:
  a **rising sun** when items are waiting, a plain tray when you're caught up, a grey tray with
  a **?** when nothing is connected, and a red tray with an **✕** when an account is configured
  but has no live session.
- **Settings** window (Accounts, Notifications, Watched repos, General): poll cadence,
  light/dark/system theme, inbox grouping and read-item visibility, verbose logging, and
  "start with Windows".
- **Secure tokens** — OAuth tokens are encrypted at rest with Windows DPAPI; nothing is
  stored in plaintext.

## Requirements

- Windows 10 (1809+) or Windows 11 (the app targets `net10.0-windows10.0.19041.0` and uses
  WPF + WinRT toast APIs, so it builds and runs on Windows only)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build (or the .NET 10 Desktop
  Runtime to run a framework-dependent build)

## Build & run

```powershell
dotnet build                             # builds the solution
dotnet test                              # runs the Trayage.Core unit tests
dotnet run --project src/Trayage.App     # launches the tray app
```

The app runs in the system tray — there is no main window. Left-click the tray icon for the
inbox flyout; right-click for the menu (Open inbox / Refresh / Settings / Quit).

To produce a distributable build for a given architecture:

```powershell
dotnet publish src/Trayage.App -c Release -r win-x64   --self-contained
dotnet publish src/Trayage.App -c Release -r win-arm64 --self-contained
```

## OAuth setup (source builds only)

Released builds ship with OAuth client identifiers baked in, so **end users never configure
anything** — they just open **Settings → Accounts** and click **Connect**. This section
applies only when running from source.

Register one OAuth app per provider (once), then drop the identifiers into a gitignored
`src/Trayage.App/appsettings.local.json` (loaded after, and overriding, the committed
`appsettings.json`):

```json
{
  "GitHub": { "ClientId": "Ov23li…" },
  "Bitbucket": { "Key": "…", "Secret": "…" }
}
```

- **GitHub** — *Settings → Developer settings → OAuth Apps → New OAuth App.* Tick **Enable
  Device Flow**, set any callback URL (unused by the device flow), and copy the **Client ID**.
  The device flow needs no client secret.
- **Bitbucket Cloud** — *Workspace settings → OAuth consumers → Add consumer.* Set the callback
  to exactly `http://localhost:33418/callback`, grant **Account**, **Repositories**, and
  **Pull requests** read access, and copy the **Client ID** (older UIs label it the **Key**)
  and the **Secret**.

| Provider UI field | Config key | Release secret |
| --- | --- | --- |
| GitHub **Client ID** | `GitHub:ClientId` | `GH_OAUTH_CLIENT_ID` |
| Bitbucket **Client ID** / **Key** | `Bitbucket:Key` | `BITBUCKET_OAUTH_KEY` |
| Bitbucket **Secret** | `Bitbucket:Secret` | `BITBUCKET_OAUTH_SECRET` |

Releases inject these in CI from repository **secrets** (right column). The Bitbucket secret
ships embedded in the binary, so it isn't truly confidential — treat it as a public
identifier, as `gh` and other desktop OAuth clients do. Tokens obtained at connect time are
stored separately and DPAPI-encrypted (see [data storage](#where-trayage-stores-data)).

## Where Trayage stores data

Everything lives under `%APPDATA%\Trayage\`:

| File | Contents |
| --- | --- |
| `settings.json` | Non-secret settings (poll interval, theme, watched repos, connection state) |
| `secrets.dat` | OAuth access/refresh tokens, each encrypted with DPAPI (CurrentUser) |
| `logs\trayage.log` | Rolling application log |
| `logs\crash.log` | Unhandled-exception records |

Logs stay on your machine and are never transmitted. Tokens are never written to them, and
Trayage keeps account identifiers — your provider login and user ID — out of the logs on a
best-effort basis.

## Verifying a release

Every tagged release is built in GitHub Actions and published with supply-chain evidence:

- **GitHub build-provenance attestation** for each `.zip` (proves which workflow, commit, and runner produced it).
- **cosign keyless signature** — a `.zip.bundle` next to each archive (Sigstore, GitHub OIDC, no long-lived keys).
- **CycloneDX SBOM** (`trayage-<tag>-sbom.cdx.json`) listing the dependency graph.

### One command

[`Verify-Release.ps1`](Verify-Release.ps1) downloads the release assets and checks all three:

```powershell
./Verify-Release.ps1                 # latest release
./Verify-Release.ps1 -Tag v1.0.0     # a specific tag
```

It needs [`gh`](https://cli.github.com/) and [`cosign`](https://docs.sigstore.dev/cosign/installation/) on `PATH`
(`winget install GitHub.cli sigstore.cosign`); install `CycloneDX.CLI` too for formal SBOM schema validation.

### By hand

```powershell
# 1. Build provenance
gh attestation verify trayage-v1.0.0-win-x64.zip --repo michaelsanford/Trayage

# 2. cosign signature (identity = the release workflow at that tag)
cosign verify-blob `
  --bundle trayage-v1.0.0-win-x64.zip.bundle `
  --certificate-identity "https://github.com/michaelsanford/Trayage/.github/workflows/release.yml@refs/tags/v1.0.0" `
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" `
  trayage-v1.0.0-win-x64.zip
```

## Known limitations

- **Bitbucket mentions and CI/check status are not surfaced.** Bitbucket Cloud has no
  notification inbox and no first-class mention feed, so Trayage assembles its Bitbucket
  inbox from pull-request queries only. Review-request detection requires the repo to be in
  your watched list (there is no cross-repo "PRs I'm reviewing" endpoint).
- **Bitbucket loopback port is fixed** at `33418` to match the consumer callback URL. If
  that port is in use, the connect step will report an error.

## Architecture

| Project | Responsibility |
| --- | --- |
| `Trayage.App` | WPF + WPF-UI shell: tray icon, inbox flyout, Settings window, toast notifier, host/DI |
| `Trayage.Core` | Provider abstraction, inbox aggregation & diffing, polling, notification rules, settings, DPAPI secret store, GitHub & Bitbucket providers |
| `Trayage.Core.Tests` | xUnit tests for the inbox/notification/settings/secret-store logic |

Providers implement `IInboxProvider` and translate their native payloads into a shared
`InboxItem`. `InboxPollingService` refreshes on an interval, the `InboxDiffer` detects new
activity, and the `NotificationRuleEngine` decides what becomes a toast. The first poll
after launch is silent so you aren't flooded with notifications for items already waiting.

### Icons

The Trayage mark and all icon assets are generated from a single glyph definition by
`tools/Build-TrayageIcons.ps1` (PowerShell 7 + GDI+, no external tooling). It emits the
app/`.exe` icon and the four tray-state glyphs (caught-up, unread, disconnected, error) into
`src/Trayage.App/Assets/`, plus the full-colour OAuth tile under `assets/oauth/`.
`assets/trayage-mark.svg` is the committed vector master. Pass `-Preview` to drop a contact
sheet of every variant in `tools/preview/` for review. Re-run the script after changing the glyph.

## Contributing

Issues and pull requests are welcome. For bugs and feature requests, please use the
[issue templates](.github/ISSUE_TEMPLATE) — they pre-fill the details that make triage fast.
Run `dotnet build` and `dotnet test` before opening a PR. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Security

Please report vulnerabilities **privately** — see the [security policy](SECURITY.md). It
covers the Trayage application itself; issues with GitHub, Bitbucket, or their services
should go to those providers.

## Acknowledgements

Trayage stands on the shoulders of [**bittray**](https://github.com/michaelsanford/bittray),
its Go predecessor that watched Bitbucket Server for pull requests needing review a decade ago.

## Disclaimer

Trayage is not affiliated with or endorsed by GitHub or Atlassian.

## License

Trayage is released under the [MIT License](LICENSE).
