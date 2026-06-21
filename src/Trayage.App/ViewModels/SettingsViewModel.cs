using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.App.Services;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Notifications;
using Trayage.Core.Providers;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Providers.GitHub;

namespace Trayage.App.ViewModels;

/// <summary>A selectable poll cadence: a display label and its value in seconds.</summary>
public sealed record PollIntervalOption(string Label, int Seconds);

/// <summary>
/// One row in the Bitbucket watched-repo picker: a discovered repository and whether it is
/// currently watched. Flipping <see cref="IsWatched"/> notifies the owning view-model so it
/// can update (and persist) the watched set.
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
/// Drives the Settings window: account connections, notification rules, watched repos,
/// and general options. Changes persist immediately so there is no explicit Save step.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // Official Windows App Runtime download page (the runtime that backs Windows toasts).
    private const string NotificationRuntimeDownloadUrl = "https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads";

    private readonly ISettingsStore _settings;
    private readonly GitHubProvider _gitHub;
    private readonly BitbucketProvider _bitbucket;
    private readonly InboxService _inboxService;
    private readonly IToastNotifier _notifier;
    private bool _loading;

    [ObservableProperty] private bool _notifyReviewRequests;
    [ObservableProperty] private bool _notifyMentions;
    [ObservableProperty] private bool _notifyCi;
    [ObservableProperty] private bool _notifyWatchedRepoActivity;
    [ObservableProperty] private bool _notifyParticipating;

    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private bool _groupByRepository;
    [ObservableProperty] private bool _showReadItems;
    [ObservableProperty] private bool _surfaceRecentlyModified;

    [ObservableProperty] private bool _gitHubConnected;
    [ObservableProperty] private string _gitHubAccountLabel = "Not connected";
    [ObservableProperty] private bool _gitHubBusy;
    [ObservableProperty] private string _gitHubDeviceInstruction = string.Empty;
    [ObservableProperty] private string _gitHubUserCode = string.Empty;

    [ObservableProperty] private bool _bitbucketConnected;
    [ObservableProperty] private string _bitbucketAccountLabel = "Not connected";
    [ObservableProperty] private bool _bitbucketBusy;
    [ObservableProperty] private string _bitbucketStatus = string.Empty;

    [ObservableProperty] private string _newWatchedRepo = string.Empty;
    [ObservableProperty] private string _watchedRepoError = string.Empty;

    [ObservableProperty] private bool _isLoadingRepos;
    [ObservableProperty] private string _repoLoadStatus = string.Empty;
    [ObservableProperty] private string _repoFilter = string.Empty;

    // Guards the IsWatched change handler while we populate the picker programmatically, so
    // pre-checking discovered repos doesn't re-persist or re-fetch.
    private bool _populatingRepos;
    private ICollectionView? _bitbucketRepoView;

    public SettingsViewModel(ISettingsStore settings, GitHubProvider gitHub, BitbucketProvider bitbucket, InboxService inboxService, IToastNotifier notifier)
    {
        _settings = settings;
        _gitHub = gitHub;
        _bitbucket = bitbucket;
        _inboxService = inboxService;
        _notifier = notifier;

        Load();
    }

    /// <summary>Raised when an inbox display option (grouping / show-read) changes.</summary>
    public event Action? InboxDisplayChanged;

    /// <summary>
    /// True when Windows can't deliver toasts on this PC (the Windows App Runtime is
    /// missing). The Notifications pane surfaces a warning and an install link when set.
    /// </summary>
    public bool ToastsUnavailable => !_notifier.IsAvailable;

    /// <summary>
    /// Re-checks toast availability. Called when the window is shown so installing the
    /// runtime and reopening Settings clears the warning without an app restart.
    /// </summary>
    public void RefreshNotificationAvailability() => OnPropertyChanged(nameof(ToastsUnavailable));

    [RelayCommand]
    private static void OpenNotificationRuntimeHelp() => InboxViewModel.OpenUrl(NotificationRuntimeDownloadUrl);

    public ObservableCollection<string> WatchedRepositories { get; } = new();

    /// <summary>Discovered Bitbucket repositories shown as toggles in the picker.</summary>
    public ObservableCollection<WatchedRepoOption> BitbucketRepoOptions { get; } = new();

    /// <summary>Name-filtered view over <see cref="BitbucketRepoOptions"/> for the search box.</summary>
    public ICollectionView BitbucketRepoView => _bitbucketRepoView ??= CreateRepoView();

    private ICollectionView CreateRepoView()
    {
        var view = CollectionViewSource.GetDefaultView(BitbucketRepoOptions);
        view.Filter = o => o is WatchedRepoOption opt
            && (string.IsNullOrWhiteSpace(RepoFilter)
                || opt.FullName.Contains(RepoFilter, StringComparison.OrdinalIgnoreCase));

        // Group into collapsible workspace sections, alphabetical within each.
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WatchedRepoOption.Workspace)));
        view.SortDescriptions.Add(new SortDescription(nameof(WatchedRepoOption.Workspace), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(WatchedRepoOption.DisplayName), ListSortDirection.Ascending));
        return view;
    }

    /// <summary>True while a filter is typed — the picker expands matching groups so results show.</summary>
    public bool RepoFilterActive => !string.IsNullOrWhiteSpace(RepoFilter);

    partial void OnRepoFilterChanged(string value)
    {
        BitbucketRepoView.Refresh();
        OnPropertyChanged(nameof(RepoFilterActive));
    }

    public IReadOnlyList<AppTheme> Themes { get; } = new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark };

    public IReadOnlyList<PollIntervalOption> PollIntervalOptions { get; } = new[]
    {
        new PollIntervalOption("2 minutes", 120),
        new PollIntervalOption("5 minutes", 300),
        new PollIntervalOption("15 minutes", 900),
        new PollIntervalOption("30 minutes", 1800),
        new PollIntervalOption("1 hour", 3600),
    };

    [RelayCommand]
    private async Task ConnectGitHubAsync()
    {
        if (GitHubBusy)
        {
            return;
        }

        GitHubBusy = true;
        GitHubUserCode = string.Empty;
        GitHubDeviceInstruction = "Requesting a device code from GitHub…";
        try
        {
            await _gitHub.ConnectAsync(prompt =>
            {
                GitHubUserCode = prompt.UserCode;
                GitHubDeviceInstruction = $"Enter this code at {prompt.VerificationUri} (opening your browser…):";
                InboxViewModel.OpenUrl(prompt.VerificationUri);
                return Task.CompletedTask;
            }, CancellationToken.None);

            GitHubConnected = _gitHub.IsConnected;
            GitHubAccountLabel = _gitHub.AccountLogin is { } login ? $"Connected as {login}" : "Connected";
            GitHubUserCode = string.Empty;
            GitHubDeviceInstruction = string.Empty;
            _ = _inboxService.RefreshAsync(CancellationToken.None);
        }
        catch (ProviderNotConfiguredException ex)
        {
            GitHubUserCode = string.Empty;
            GitHubDeviceInstruction = ex.Message;
        }
        catch (Exception ex)
        {
            GitHubUserCode = string.Empty;
            GitHubDeviceInstruction = $"Connection failed: {ex.Message}";
        }
        finally
        {
            GitHubBusy = false;
        }
    }

    [RelayCommand]
    private void CopyGitHubCode()
    {
        if (string.IsNullOrEmpty(GitHubUserCode))
        {
            return;
        }

        try
        {
            Clipboard.SetText(GitHubUserCode);
        }
        catch (Exception)
        {
            // Clipboard access can transiently fail; not worth surfacing.
        }
    }

    [RelayCommand]
    private void DisconnectGitHub()
    {
        _gitHub.Disconnect();
        GitHubConnected = false;
        GitHubAccountLabel = "Not connected";
        GitHubUserCode = string.Empty;
        GitHubDeviceInstruction = string.Empty;
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ConnectBitbucketAsync()
    {
        if (BitbucketBusy)
        {
            return;
        }

        BitbucketBusy = true;
        BitbucketStatus = "Opening your browser to authorize Bitbucket…";
        try
        {
            await _bitbucket.ConnectAsync(uri =>
            {
                InboxViewModel.OpenUrl(uri.ToString());
                return Task.CompletedTask;
            }, CancellationToken.None);

            BitbucketConnected = _bitbucket.IsConnected;
            BitbucketAccountLabel = _bitbucket.AccountLogin is { } login ? $"Connected as {login}" : "Connected";
            BitbucketStatus = string.Empty;
            _ = _inboxService.RefreshAsync(CancellationToken.None);
        }
        catch (ProviderNotConfiguredException ex)
        {
            BitbucketStatus = ex.Message;
        }
        catch (Exception ex)
        {
            BitbucketStatus = $"Connection failed: {ex.Message}";
        }
        finally
        {
            BitbucketBusy = false;
        }
    }

    [RelayCommand]
    private void DisconnectBitbucket()
    {
        _bitbucket.Disconnect();
        BitbucketConnected = false;
        BitbucketAccountLabel = "Not connected";
        BitbucketStatus = string.Empty;
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task LoadBitbucketReposAsync()
    {
        if (IsLoadingRepos)
        {
            return;
        }

        if (!BitbucketConnected)
        {
            RepoLoadStatus = "Connect Bitbucket first to load your repositories.";
            return;
        }

        IsLoadingRepos = true;
        RepoLoadStatus = "Loading your Bitbucket repositories…";
        try
        {
            var repos = await _bitbucket.ListAccessibleRepositoriesAsync(CancellationToken.None);

            _populatingRepos = true;
            BitbucketRepoOptions.Clear();
            var watched = new HashSet<string>(WatchedRepositories, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var repo in repos)
            {
                if (seen.Add(repo.FullName))
                {
                    BitbucketRepoOptions.Add(NewOption(repo.FullName, repo.Name, watched.Contains(repo.FullName)));
                }
            }

            // Watched repos discovery didn't return (added manually, or beyond the page cap)
            // still appear, pre-checked, so the picker shows the full watched set.
            foreach (var repo in WatchedRepositories)
            {
                if (seen.Add(repo))
                {
                    BitbucketRepoOptions.Add(NewOption(repo, repo, isWatched: true));
                }
            }

            _populatingRepos = false;
            BitbucketRepoView.Refresh();
            RepoLoadStatus = BitbucketRepoOptions.Count == 0
                ? "No repositories found for this account."
                : $"{BitbucketRepoOptions.Count} repositories — toggle the ones you want to watch.";
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
        Persist();
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }

    private void UnwatchRepo(string fullName)
    {
        var existing = WatchedRepositories.FirstOrDefault(r => string.Equals(r, fullName, StringComparison.OrdinalIgnoreCase));
        if (existing is null || !WatchedRepositories.Remove(existing))
        {
            return;
        }

        Persist();
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }

    /// <summary>Keeps a loaded picker toggle in sync when the watched set changes elsewhere.</summary>
    private void SyncOptionState(string fullName, bool isWatched)
    {
        var option = BitbucketRepoOptions.FirstOrDefault(o => string.Equals(o.FullName, fullName, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            if (isWatched)
            {
                _populatingRepos = true;
                BitbucketRepoOptions.Add(NewOption(fullName, fullName, isWatched: true));
                _populatingRepos = false;
                BitbucketRepoView.Refresh();
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

    partial void OnNotifyReviewRequestsChanged(bool value) => Persist();

    partial void OnNotifyMentionsChanged(bool value) => Persist();

    partial void OnNotifyCiChanged(bool value) => Persist();

    partial void OnNotifyWatchedRepoActivityChanged(bool value) => Persist();

    partial void OnNotifyParticipatingChanged(bool value) => Persist();

    [RelayCommand]
    private static void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo(TrayagePaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer shouldn't be able to crash settings.
        }
    }

    partial void OnPollIntervalSecondsChanged(int value) => Persist();

    partial void OnVerboseLoggingChanged(bool value) => Persist();

    partial void OnGroupByRepositoryChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Persist();
        InboxDisplayChanged?.Invoke();
    }

    partial void OnShowReadItemsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Persist();
        InboxDisplayChanged?.Invoke();
    }

    partial void OnSurfaceRecentlyModifiedChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Persist();
        InboxDisplayChanged?.Invoke();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        AutostartManager.SetEnabled(value);
        Persist();
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_loading)
        {
            return;
        }

        ThemeApplier.Apply(value);
        Persist();
    }

    private void Load()
    {
        _loading = true;
        var s = _settings.Load();

        NotifyReviewRequests = s.Notifications.ReviewRequests;
        NotifyMentions = s.Notifications.MentionsAndAssignments;
        NotifyCi = s.Notifications.CiStatus;
        NotifyWatchedRepoActivity = s.Notifications.WatchedRepoActivity;
        NotifyParticipating = s.Notifications.Participating;

        // Snap a previously-saved cadence that's no longer offered to the nearest option,
        // so the dropdown always shows a valid selection. The On…Changed persist is
        // suppressed during load, so write the migrated value through directly.
        PollIntervalSeconds = PollIntervalOptions.MinBy(o => Math.Abs(o.Seconds - s.PollIntervalSeconds))!.Seconds;
        if (PollIntervalSeconds != s.PollIntervalSeconds)
        {
            s.PollIntervalSeconds = PollIntervalSeconds;
            _settings.Save(s);
        }

        SelectedTheme = s.Theme;
        VerboseLogging = s.VerboseLogging;
        GroupByRepository = s.GroupByRepository;
        ShowReadItems = s.ShowReadItems;
        SurfaceRecentlyModified = s.SurfaceRecentlyModified;
        StartWithWindows = AutostartManager.IsEnabled();

        WatchedRepositories.Clear();
        foreach (var repo in s.WatchedRepositories)
        {
            WatchedRepositories.Add(repo);
        }

        // Seed the picker with the already-watched repos (pre-checked) so they're visible as
        // one unified list before "Load my repositories" is clicked. Loading later clears and
        // re-adds discovered repos plus any watched ones it didn't return, so this never double-ups.
        _populatingRepos = true;
        BitbucketRepoOptions.Clear();
        foreach (var repo in WatchedRepositories)
        {
            BitbucketRepoOptions.Add(NewOption(repo, repo, isWatched: true));
        }

        _populatingRepos = false;

        GitHubConnected = _gitHub.IsConnected;
        GitHubAccountLabel = _gitHub.IsConnected
            ? (_gitHub.AccountLogin is { } login ? $"Connected as {login}" : "Connected")
            : "Not connected";

        BitbucketConnected = _bitbucket.IsConnected;
        BitbucketAccountLabel = _bitbucket.IsConnected
            ? (_bitbucket.AccountLogin is { } bbLogin ? $"Connected as {bbLogin}" : "Connected")
            : "Not connected";

        _loading = false;
    }

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        // Reload first so provider-managed fields (connection state) aren't clobbered.
        var s = _settings.Load();
        s.PollIntervalSeconds = PollIntervalSeconds;
        s.Theme = SelectedTheme;
        s.VerboseLogging = VerboseLogging;
        s.GroupByRepository = GroupByRepository;
        s.ShowReadItems = ShowReadItems;
        s.SurfaceRecentlyModified = SurfaceRecentlyModified;
        s.StartWithWindows = StartWithWindows;
        s.Notifications.ReviewRequests = NotifyReviewRequests;
        s.Notifications.MentionsAndAssignments = NotifyMentions;
        s.Notifications.CiStatus = NotifyCi;
        s.Notifications.WatchedRepoActivity = NotifyWatchedRepoActivity;
        s.Notifications.Participating = NotifyParticipating;
        s.WatchedRepositories = WatchedRepositories.ToList();
        _settings.Save(s);
    }
}
