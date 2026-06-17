using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
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

    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private bool _groupByRepository;
    [ObservableProperty] private bool _showReadItems;

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
    private void AddWatchedRepo()
    {
        var repo = NewWatchedRepo.Trim();
        if (repo.Length == 0 || !repo.Contains('/'))
        {
            return;
        }

        if (!WatchedRepositories.Contains(repo, StringComparer.OrdinalIgnoreCase))
        {
            WatchedRepositories.Add(repo);
            Persist();
        }

        NewWatchedRepo = string.Empty;
    }

    [RelayCommand]
    private void RemoveWatchedRepo(string? repo)
    {
        if (repo is not null && WatchedRepositories.Remove(repo))
        {
            Persist();
        }
    }

    partial void OnNotifyReviewRequestsChanged(bool value) => Persist();

    partial void OnNotifyMentionsChanged(bool value) => Persist();

    partial void OnNotifyCiChanged(bool value) => Persist();

    partial void OnNotifyWatchedRepoActivityChanged(bool value) => Persist();

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
        StartWithWindows = AutostartManager.IsEnabled();

        WatchedRepositories.Clear();
        foreach (var repo in s.WatchedRepositories)
        {
            WatchedRepositories.Add(repo);
        }

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
        s.StartWithWindows = StartWithWindows;
        s.Notifications.ReviewRequests = NotifyReviewRequests;
        s.Notifications.MentionsAndAssignments = NotifyMentions;
        s.Notifications.CiStatus = NotifyCi;
        s.Notifications.WatchedRepoActivity = NotifyWatchedRepoActivity;
        s.WatchedRepositories = WatchedRepositories.ToList();
        _settings.Save(s);
    }
}
