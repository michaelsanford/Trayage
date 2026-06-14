# OAuth setup

Trayage talks to GitHub and Bitbucket **as you**, using OAuth. The app author registers an
OAuth application with each provider **once**; the resulting client identifiers are supplied
at build/run time and are **not** committed to source (see
[Where the credentials go](#where-the-credentials-go)). End users then simply click
**Connect** in **Settings → Accounts** and sign in as themselves — they never register anything.

Until at least a GitHub Client ID is filled in, the inbox stays empty ("You're all caught
up") because there is no account to query. That is expected, not a fault.

```json
{
  "GitHub": {
    "ClientId": "",
    "Scopes": [ "notifications", "repo", "read:org" ]
  },
  "Bitbucket": {
    "Key": "",
    "Secret": "",
    "Scopes": [ "account", "repository", "pullrequest" ],
    "RedirectUri": "http://localhost:33418/callback"
  }
}
```

## Where the credentials go

The committed `appsettings.json` ships the **structure** (scopes, redirect URI) with the
three credential values left **blank**. The real values are layered in from outside source
control:

| Context | Source of the values |
| --- | --- |
| **Local development** | `src/Trayage.App/appsettings.local.json` — gitignored, copied to the build output, and loaded *after* `appsettings.json` so its values win. Put your GitHub `ClientId`, Bitbucket `Key`, and Bitbucket `Secret` there. |
| **Releases (CI)** | The release workflow injects them into the published `appsettings.json` from repository **secrets**: `GH_OAUTH_CLIENT_ID`, `BITBUCKET_OAUTH_KEY`, `BITBUCKET_OAUTH_SECRET`. |

All three are kept as GitHub Actions **secrets** (not variables) so a fork can't build
releases using your identifiers. Even so, the Bitbucket secret is embedded in the shipped
binary and is therefore **not truly confidential** — see the note under *Bitbucket Cloud* below.

Example `appsettings.local.json`:

```json
{
  "GitHub": { "ClientId": "Ov23li…" },
  "Bitbucket": { "Key": "…", "Secret": "…" }
}
```

## GitHub — OAuth device flow (no client secret)

GitHub supports the OAuth **device flow**, so only a public Client ID is needed; there is no
secret to protect.

1. GitHub → **Settings → Developer settings → OAuth Apps → New OAuth App**.
2. Set any application name and homepage URL. The **Authorization callback URL** can be
   `http://localhost` — it is unused by the device flow, but GitHub requires a value.
3. Tick **Enable Device Flow**.
4. Copy the **Client ID** into `GitHub:ClientId`.

The default scopes (`notifications`, `repo`, `read:org`) cover the notifications inbox,
including notifications from private repositories.

## Bitbucket Cloud — authorization-code flow (loopback redirect)

Bitbucket Cloud does **not** support the device flow, so Trayage uses the OAuth
**authorization-code** flow over a fixed loopback redirect.

1. Bitbucket → **Workspace settings → OAuth consumers → Add consumer**.
2. Set the **Callback URL** to exactly `http://localhost:33418/callback`. This must match
   `Bitbucket:RedirectUri`; the port is fixed so it can match the registered callback.
3. Grant **Account: Read**, **Repositories: Read**, and **Pull requests: Read**.
4. Copy the **Key** into `Bitbucket:Key` and the **Secret** into `Bitbucket:Secret`.

> **On the Bitbucket secret:** the consumer secret ships embedded in the distributed app.
> As with `gh` and other desktop OAuth clients, an embedded secret is not truly
> confidential — treat it as a public identifier, not a sensitive credential.

## Where tokens live

The access and refresh tokens obtained at connect time are **not** written to
`appsettings.json`. They are stored separately under `%APPDATA%\Trayage\secrets.dat`,
encrypted with Windows DPAPI (CurrentUser scope), and are readable only by the same Windows
user on the same machine.
