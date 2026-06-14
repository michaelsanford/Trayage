# OAuth setup

Trayage talks to GitHub and Bitbucket **as you**, using OAuth. The app author registers an
OAuth application with each provider **once** and ships the resulting client identifiers in
[`src/Trayage.App/appsettings.json`](src/Trayage.App/appsettings.json). End users then
simply click **Connect** in **Settings → Accounts** and sign in as themselves — they never
register anything.

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
