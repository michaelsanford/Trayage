# Security Policy

## Supported Versions

Only the **latest release** of Trayage receives security fixes. Older versions are not patched.

| Version  | Supported |
|----------|-----------|
| Latest   | Yes       |
| < Latest | No        |

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Instead, use [GitHub's private vulnerability reporting](https://github.com/michaelsanford/Trayage/security/advisories/new) to submit a report confidentially.

Include as much detail as you can:

- A description of the vulnerability and its potential impact
- Steps to reproduce or a proof-of-concept
- The Trayage version affected
- Your suggested severity (if you have one)

## Response SLAs

| Milestone       | Target |
|-----------------|--------|
| Acknowledgement | Within **21 days** of a valid report |
| Fix             | **Best effort** — complexity, severity, and maintainer availability determine timeline |

There are no guaranteed fix timelines. Severe, easily exploitable vulnerabilities will be prioritised.

## Scope

This policy covers **the Trayage application itself** — the Windows tray client in this
repository. Trayage is a desktop OAuth client: it signs in *as you* and reads your activity
from GitHub and Bitbucket Cloud. Reports involving the app are in scope, including:

- **Local credential handling** — the DPAPI-encrypted token store under `%APPDATA%\Trayage\`,
  and any path by which tokens could be exposed in plaintext, logs, or to another user.
- **The OAuth flows** — the temporary loopback redirect listener (`http://localhost:33418/…`)
  and the device-flow handling, including request/response validation.
- **Parsing and handling** of data returned by the provider APIs (e.g. malformed or hostile
  API responses causing unsafe behaviour in the app).
- **The app's own code** — the tray UI, settings, notification rules, and inbox logic.
- **The build supply chain** — dependency integrity and the release artifacts (build
  provenance, SBOM, cosign signatures).

### Out of scope

Trayage is only a **client**. The following are **not** in scope here — report them to the
relevant provider, not to this project:

- Vulnerabilities in **GitHub** or **Atlassian/Bitbucket** themselves, their APIs, websites,
  or OAuth services.
- The security of your GitHub or Bitbucket **accounts**, tokens, scopes, or organisation
  settings on those platforms.
- Anything in a **connected service** rather than in the Trayage app.

Trayage cannot fix, and is not responsible for, issues in the third-party services it talks to.

## Bug Bounty

There is **no bug bounty program**. This is a personal open-source project maintained without
commercial backing.

## Credit

Reporters of validated vulnerabilities will be **credited by name (or handle) in the release
notes** and security advisory, unless anonymity is requested.
