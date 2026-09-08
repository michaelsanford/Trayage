using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Providers.GitHub;
using Trayage.Core.Providers.GitLab;

namespace Trayage.App.ViewModels;

/// <summary>
/// One row in a Bitbucket account's watched-repo picker: a discovered repository and whether it
/// is currently watched. Flipping <see cref="IsWatched"/> notifies the owning account view-model
/// so it can update (and persist) that account's watched set.
/// </summary>
public sealed partial class WatchedRepoOption : ObservableObject
{
    private readonly Action<WatchedRepoOption, bool> _onToggled;

    public WatchedRepoOption(string fullName, string displayName, bool isWatched, Action<WatchedRepoOption, bool> onToggled)
    {
        FullName = fullName;
        DisplayName = displayName;
        // "workspace/repo-slug" — the segment before the slash is the workspace the picker groups on.
        var slash = fullName.IndexOf('/');
        Workspace = slash > 0 ? fullName[..slash] : fullName;
        _isWatched = isWatched;
        _onToggled = onToggled;
    }

    public string FullName { get; }

    public string DisplayName { get; }

    /// <summary>Workspace slug, used to group the picker.</summary>
    public string Workspace { get; }

    [ObservableProperty] private bool _isWatched;

    partial void OnIsWatchedChanged(bool value) => _onToggled(this, value);
}

/// <summary>
/// Drives one connected account's card in Settings. Replaces what used to be three
/// near-identical sets of per-provider properties and commands on
/// <see cref="SettingsViewModel"/>: connect, disconnect, copy the device code, rename, pause,
/// and — for Bitbucket — pick which repositories this account watches.
/// </summary>
public sealed partial class ProviderAccountViewModel : ObservableObject
{
    private readonly IInboxProvider _provider;
    private readonly ISettingsStore _settings;
    private readonly InboxService _inboxService;

    // Guards the IsWatched handler while the picker is populated programmatically, so
    // pre-checking discovered repos doesn't re-persist or re-fetch.
    private bool _populatingRepos;

    // Set once discovery has run for this account, so re-expanding the card doesn't re-hit the
    // API; the manual "Refresh" button always reloads.
    private bool _reposAutoLoaded;

    private readonly bool _loading;

    public ProviderAccountViewModel(
        IInboxProvider provider,
        ProviderAccount account,
        ISettingsStore settings,
        InboxService inboxService)
    {
        _provider = provider;
        _settings = settings;
        _inboxService = inboxService;

        AccountId = account.Id;
        Provider = account.Provider;

        _loading = true;
        _nickname = account.Nickname ?? string.Empty;
        _isEnabled = account.Enabled;
        _connected = provider.IsConnected;
        _accountLogin = provider.AccountLogin ?? account.AccountLogin;
        _loading = false;

        foreach (var repo in account.WatchedRepositories)
        {
            WatchedRepositories.Add(repo);
        }

        SeedPickerFromWatched();
    }

    public string AccountId { get; }

    public ProviderKind Provider { get; }

    /// <summary>True for Bitbucket, whose inbox is assembled from per-repository queries.</summary>
    public bool SupportsRepoPicker => Provider == ProviderKind.Bitbucket;

    public string ProviderName => Provider == ProviderKind.Bitbucket
        ? "Bitbucket Cloud"
        : Provider.DisplayName();

    /// <summary>Where this account lives, shown under its name.</summary>
    public string HostLabel => Provider switch
    {
        ProviderKind.GitHub => "github.com",
        ProviderKind.Bitbucket => "bitbucket.org",
        ProviderKind.GitLab => "gitlab.com",
        _ => string.Empty,
    };

    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private string? _accountLogin;
    [ObservableProperty] private bool _busy;

    /// <summary>Progress or error text for the connect flow; empty hides the block.</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>The OAuth device code, for the providers that use the device flow.</summary>
    [ObservableProperty] private string _userCode = string.Empty;

    [ObservableProperty] private bool _isLoadingRepos;
    [ObservableProperty] private string _repoLoadStatus = string.Empty;
    [ObservableProperty] private string _repoFilter = string.Empty;
    [ObservableProperty] private string _newWatchedRepo = string.Empty;
    [ObservableProperty] private string _watchedRepoError = string.Empty;

    /// <summary>Raised when this account changes in a way the inbox or tray should see.</summary>
    public event Action? Changed;

    /// <summary>The label shown on the card and beside inbox items from this account.</summary>
    public string DisplayLabel => Nickname is { Length: > 0 } n
        ? n
        : AccountLogin is { Length: > 0 } l ? l : ProviderName;

    /// <summary>Secondary line on the collapsed card: who, and where.</summary>
    public string SubtitleLabel => Connected
        ? AccountLogin is { Length: > 0 } login ? $"{login} · {HostLabel}" : HostLabel
        : "Not connected";

    public string StatusLabel => !Connected ? "Not connected" : IsEnabled ? "Connected" : "Paused";

    public ObservableCollection<string> WatchedRepositories { get; } = new();

    /// <summary>Discovered repositories shown as toggles in this account's picker.</summary>
    public ObservableCollection<WatchedRepoOption> RepoOptions { get; } = new();

    /// <summary>Name-filtered, workspace-grouped view over <see cref="RepoOptions"/>.</summary>
    public ICollectionView RepoView => field ??= CreateRepoView();

    /// <summary>True while a filter is typed — the picker expands matching groups so results show.</summary>
    public bool RepoFilterActive => !string.IsNullOrWhiteSpace(RepoFilter);

    private ICollectionView CreateRepoView()
    {
        var view = CollectionViewSource.GetDefaultView(RepoOptions);
        view.Filter = o => o is WatchedRepoOption opt
            && (string.IsNullOrWhiteSpace(RepoFilter)
                || opt.FullName.Contains(RepoFilter, StringComparison.OrdinalIgnoreCase));

        // Group into collapsible workspace sections, alphabetical within each.
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WatchedRepoOption.Workspace)));
        view.SortDescriptions.Add(new SortDescription(nameof(WatchedRepoOption.Workspace), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(WatchedRepoOption.DisplayName), ListSortDirection.Ascending));
        return view;
    }

    partial void OnRepoFilterChanged(string value)
    {
        RepoView.Refresh();
        OnPropertyChanged(nameof(RepoFilterActive));
    }

    partial void OnNicknameChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        Mutate(a => a.Nickname = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        NotifyLabels();
        Changed?.Invoke();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Mutate(a => a.Enabled = value);
        OnPropertyChanged(nameof(StatusLabel));
        Refresh();
    }

    partial void OnConnectedChanged(bool value) => NotifyLabels();

    partial void OnAccountLoginChanged(string? value) => NotifyLabels();

    private void NotifyLabels()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(SubtitleLabel));
        OnPropertyChanged(nameof(StatusLabel));
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        UserCode = string.Empty;
        StatusMessage = $"Contacting {ProviderName}…";

        try
        {
            switch (_provider)
            {
                case GitHubProvider gitHub:
                    await gitHub.ConnectAsync(OnDevicePromptAsync, CancellationToken.None);
                    break;
                case GitLabProvider gitLab:
                    await gitLab.ConnectAsync(OnDevicePromptAsync, CancellationToken.None);
                    break;
                case BitbucketProvider bitbucket:
                    StatusMessage = "Opening your browser to authorize Bitbucket…";
                    await bitbucket.ConnectAsync(uri =>
                    {
                        InboxViewModel.OpenUrl(uri.ToString());
                        return Task.CompletedTask;
                    }, CancellationToken.None);
                    break;
                default:
                    throw new InvalidOperationException($"{ProviderName} can't be connected from here.");
            }

            Connected = _provider.IsConnected;
            AccountLogin = _provider.AccountLogin;
            UserCode = string.Empty;
            StatusMessage = string.Empty;
            Refresh();
        }
        catch (ProviderNotConfiguredException ex)
        {
            UserCode = string.Empty;
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            UserCode = string.Empty;
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private Task OnDevicePromptAsync(DeviceCodePrompt prompt)
    {
        UserCode = prompt.UserCode;
        StatusMessage = $"Enter this code at {prompt.VerificationUri} (opening your browser…):";
        InboxViewModel.OpenUrl(prompt.VerificationUri);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Disconnect()
    {
        switch (_provider)
        {
            case GitHubProvider gitHub:
                gitHub.Disconnect();
                break;
            case GitLabProvider gitLab:
                gitLab.Disconnect();
                break;
            case BitbucketProvider bitbucket:
                bitbucket.Disconnect();
                break;
        }

        Connected = false;
        AccountLogin = null;
        UserCode = string.Empty;
        StatusMessage = string.Empty;
        Refresh();
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (string.IsNullOrEmpty(UserCode))
        {
            return;
        }

        try
        {
            Clipboard.SetText(UserCode);
        }
        catch (Exception)
        {
            // Clipboard access can transiently fail; not worth surfacing.
        }
    }

    /// <summary>
    /// Triggers a one-time repository discovery the first time this account's card is opened
    /// while connected. Later expansions are no-ops; "Refresh" always reloads.
    /// </summary>
    public void EnsureReposLoaded()
    {
        if (_reposAutoLoaded || !SupportsRepoPicker || !Connected || IsLoadingRepos)
        {
            return;
        }

        _reposAutoLoaded = true;
        if (LoadReposCommand.CanExecute(null))
        {
            LoadReposCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadReposAsync()
    {
        if (IsLoadingRepos || _provider is not BitbucketProvider bitbucket)
        {
            return;
        }

        if (!Connected)
        {
            RepoLoadStatus = "Connect this account first to load its repositories.";
            return;
        }

        IsLoadingRepos = true;
        RepoLoadStatus = "Loading this account's Bitbucket repositories…";
        try
        {
            var result = await bitbucket.ListAccessibleRepositoriesAsync(CancellationToken.None);

            _populatingRepos = true;
            RepoOptions.Clear();
            var watched = new HashSet<string>(WatchedRepositories, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var repo in result.Repositories)
            {
                if (seen.Add(repo.FullName))
                {
                    RepoOptions.Add(NewOption(repo.FullName, repo.Name, watched.Contains(repo.FullName)));
                }
            }

            // Watched repos discovery didn't return (added manually, or beyond the page cap)
            // still appear, pre-checked, so the picker shows the full watched set.
            foreach (var repo in WatchedRepositories)
            {
                if (seen.Add(repo))
                {
                    RepoOptions.Add(NewOption(repo, repo, isWatched: true));
                }
            }

            _populatingRepos = false;
            RepoView.Refresh();

            // Three distinct outcomes: a degraded fetch (something failed — don't imply the
            // account is empty), a genuinely empty account, or a normal list.
            if (result.Partial)
            {
                RepoLoadStatus = result.Warning ?? "Some repositories may be missing. See logs, or add a repo manually.";
            }
            else if (RepoOptions.Count == 0)
            {
                RepoLoadStatus = "No repositories found for this account.";
            }
            else
            {
                RepoLoadStatus = $"{RepoOptions.Count} repositories — toggle the ones you want to watch.";
            }
        }
        catch (Exception ex)
        {
            _populatingRepos = false;
            RepoLoadStatus = $"Couldn't load repositories: {ex.Message}";
        }
        finally
        {
            IsLoadingRepos = false;
        }
    }

    [RelayCommand]
    private void AddWatchedRepo()
    {
        WatchedRepoError = string.Empty;
        var repo = RepositoryReference.Normalize(NewWatchedRepo);
        if (repo is null)
        {
            WatchedRepoError = "Enter a repository as owner/repo, or paste its Bitbucket URL.";
            return;
        }

        WatchRepo(repo);
        SyncOptionState(repo, isWatched: true);
        NewWatchedRepo = string.Empty;
    }

    [RelayCommand]
    private void RemoveWatchedRepo(string? repo)
    {
        if (repo is null)
        {
            return;
        }

        UnwatchRepo(repo);
        SyncOptionState(repo, isWatched: false);
    }

    private void SeedPickerFromWatched()
    {
        // Seed the picker with already-watched repos (pre-checked) so they're visible as one
        // unified list before discovery runs. Loading later clears and re-adds discovered repos
        // plus any watched ones it didn't return, so this never double-ups.
        _populatingRepos = true;
        RepoOptions.Clear();
        foreach (var repo in WatchedRepositories)
        {
            RepoOptions.Add(NewOption(repo, repo, isWatched: true));
        }

        _populatingRepos = false;
    }

    private WatchedRepoOption NewOption(string fullName, string displayName, bool isWatched)
        => new(fullName, displayName, isWatched, OnRepoToggled);

    private void OnRepoToggled(WatchedRepoOption option, bool isWatched)
    {
        if (_populatingRepos)
        {
            return;
        }

        if (isWatched)
        {
            WatchRepo(option.FullName);
        }
        else
        {
            UnwatchRepo(option.FullName);
        }
    }

    private void WatchRepo(string fullName)
    {
        if (WatchedRepositories.Contains(fullName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        WatchedRepositories.Add(fullName);
        PersistWatched();
    }

    private void UnwatchRepo(string fullName)
    {
        var existing = WatchedRepositories.FirstOrDefault(r => string.Equals(r, fullName, StringComparison.OrdinalIgnoreCase));
        if (existing is null || !WatchedRepositories.Remove(existing))
        {
            return;
        }

        PersistWatched();
    }

    /// <summary>Keeps a loaded picker toggle in sync when the watched set changes elsewhere.</summary>
    private void SyncOptionState(string fullName, bool isWatched)
    {
        var option = RepoOptions.FirstOrDefault(o => string.Equals(o.FullName, fullName, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            if (isWatched)
            {
                _populatingRepos = true;
                RepoOptions.Add(NewOption(fullName, fullName, isWatched: true));
                _populatingRepos = false;
                RepoView.Refresh();
            }

            return;
        }

        if (option.IsWatched != isWatched)
        {
            _populatingRepos = true;
            option.IsWatched = isWatched;
            _populatingRepos = false;
        }
    }

    private void PersistWatched()
    {
        Mutate(a =>
        {
            a.WatchedRepositories.Clear();
            a.WatchedRepositories.AddRange(WatchedRepositories);
        });

        Refresh();
    }

    /// <summary>
    /// Applies a change to this account's persisted row. Re-loads first so a concurrent write
    /// from elsewhere in Settings isn't clobbered.
    /// </summary>
    private void Mutate(Action<ProviderAccount> change)
    {
        var current = _settings.Load();
        if (current.FindAccount(AccountId) is not { } account)
        {
            return;
        }

        change(account);
        _settings.Save(current);
    }

    private void Refresh()
    {
        Changed?.Invoke();
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }
}
