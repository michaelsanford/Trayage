# Trayage

A .NET 9 Windows system-tray app that gathers your **GitHub** and **Bitbucket Cloud**
activity into one unified inbox, lets you jump straight to the relevant page with a
click, and raises native Windows toast notifications for the things you care about.

Trayage is the successor to [bittray](https://github.com/michaelsanford/bittray) — a Go
tray app that watched Bitbucket Server for pull requests needing review. Trayage modernises
the idea on .NET 9 with a Fluent (Windows 11) UI, adds GitHub, and broadens "needs review"
into a configurable inbox.

## Features

- **Unified inbox** in a tray flyout, grouped by repository, newest first. Click any item
  to open it in your browser.
- **Sources**
  - **GitHub** via the notifications API — review requests, mentions, assignments, CI
    activity, and watched-repo activity.
  - **Bitbucket Cloud** via pull-request queries — PRs you authored, PRs in watched repos
    where you're a reviewer, and all activity in watched repos.
- **Native toast notifications** with per-class toggles (review requests, mentions &
  assignments, CI/check status) plus **all activity on repositories you choose to watch**.
  Clicking a toast opens the page.
- **Tray icon** changes to indicate when items need your attention.
- **Settings** window (Accounts, Notifications, Watched repos, General) with light/dark/system
  theme and "start with Windows".
- **Secure tokens** — OAuth tokens are encrypted at rest with Windows DPAPI; nothing is
  stored in plaintext.

## Requirements

- Windows 10 (1809+) or Windows 11 (the app targets `net9.0-windows10.0.19041.0` and uses
  WPF + WinRT toast APIs, so it builds and runs on Windows only)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build (or the .NET 9 Desktop
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

## Configuration

Trayage authenticates to GitHub and Bitbucket via OAuth. The client identifiers are read
from [`src/Trayage.App/appsettings.json`](src/Trayage.App/appsettings.json) and must be
filled in **once** before accounts can be connected — see
**[OAUTH_SETUP.md](OAUTH_SETUP.md)** for the one-time provider registration steps.

With those in place, open **Settings → Accounts** and click **Connect**.

## Where Trayage stores data

Everything lives under `%APPDATA%\Trayage\`:

| File | Contents |
| --- | --- |
| `settings.json` | Non-secret settings (poll interval, theme, watched repos, connection state) |
| `secrets.dat` | OAuth access/refresh tokens, each encrypted with DPAPI (CurrentUser) |
| `logs\trayage.log` | Rolling application log |
| `logs\crash.log` | Unhandled-exception records |

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

## Acknowledgements

Successor to [bittray](https://github.com/michaelsanford/bittray). Not affiliated with or
endorsed by GitHub or Atlassian.
