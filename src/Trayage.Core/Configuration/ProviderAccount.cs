using System.Text.Json.Serialization;
using Trayage.Core.Models;

namespace Trayage.Core.Configuration;

/// <summary>
/// One connected account on one provider. Trayage supports several accounts per provider
/// (a work and a personal GitHub, two Bitbucket workspaces under different logins), so this
/// — not <see cref="ProviderKind"/> — is the unit of identity for tokens, watched
/// repositories, polling, and every inbox item.
/// </summary>
/// <remarks>
/// Deliberately holds no secrets: access/refresh tokens live in the secret store under keys
/// derived from <see cref="Id"/>. See <see cref="Trayage.Core.Security.SecretKeys"/>.
/// </remarks>
public sealed class ProviderAccount
{
    /// <summary>Stable for the life of the account; the key for tokens and inbox-item identity.</summary>
    public required string Id { get; set; }

    public required ProviderKind Provider { get; set; }

    /// <summary>The signed-in login/username as reported by the provider.</summary>
    public string? AccountLogin { get; set; }

    /// <summary>User-chosen label (e.g. "Work"). Falls back to the login when unset.</summary>
    public string? Nickname { get; set; }

    public bool Connected { get; set; }

    /// <summary>When false the account is kept (token and all) but skipped while polling.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base URL of the instance this account lives on. Null means the provider's default
    /// (github.com / bitbucket.org / gitlab.com). Reserved for self-hosted instances.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// "owner/repo" names to surface and toast on for all activity, scoped to this account.
    /// Scoping matters: a repo only one account can see would 404 for the others.
    /// </summary>
    public List<string> WatchedRepositories { get; set; } = new();

    /// <summary>Short label for the account list and inbox rows: the nickname, else the login.</summary>
    [JsonIgnore]
    public string DisplayLabel => Nickname is { Length: > 0 } n
        ? n
        : AccountLogin is { Length: > 0 } l ? l : Provider.DisplayName();

    /// <summary>Qualified label used in toasts and error text (e.g. "GitHub · Work").</summary>
    [JsonIgnore]
    public string QualifiedLabel => $"{Provider.DisplayName()} · {DisplayLabel}";

    public ProviderAccount Clone() => new()
    {
        Id = Id,
        Provider = Provider,
        AccountLogin = AccountLogin,
        Nickname = Nickname,
        Connected = Connected,
        Enabled = Enabled,
        BaseUrl = BaseUrl,
        WatchedRepositories = new List<string>(WatchedRepositories),
    };

    /// <summary>Mints an id for a newly added account. Short enough to read in settings.json.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
