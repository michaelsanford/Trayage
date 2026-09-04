using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.App.Notifications;
using Trayage.App.Services;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Notifications;
using Trayage.Core.Providers;

// ReSharper disable UnusedParameterInPartialMethod

namespace Trayage.App.ViewModels;

// ReSharper disable NotAccessedPositionalProperty.Global
/// <summary>A selectable poll cadence: a display label and its value in seconds.</summary>
public sealed record PollIntervalOption(string Label, int Seconds);

public sealed record NotificationStyleOption(string Label, NotificationStyle Style);

public sealed record NotificationSoundOption(string Label, string Value);

/// <summary>An entry in the "Add account" menu: which provider a new account would connect to.</summary>
public sealed record AddAccountOption(string Label, ProviderKind Provider);
// ReSharper restore NotAccessedPositionalProperty.Global

/// <summary>
/// Drives the Settings window: the connected accounts, notification rules, inbox display, and
/// general options. Changes persist immediately so there is no explicit Save step.
/// </summary>
/// <remarks>
/// Per-account state lives on <see cref="ProviderAccountViewModel"/>, one per row of
/// <see cref="TrayageSettings.Accounts"/> — this class only owns settings that apply app-wide.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    // Official Windows App Runtime download page (the runtime that backs Windows toasts).
    private const string NotificationRuntimeDownloadUrl = "https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads";

    private const string ProjectUrl = "https://github.com/michaelsanford/Trayage";

    private readonly ISettingsStore _settings;
    private readonly ProviderRegistry _registry;
    private readonly InboxService _inboxService;
    private readonly IToastNotifier _notifier;
    private bool _loading;

    [ObservableProperty] private bool _notifyReviewRequests;
    [ObservableProperty] private bool _notifyMentions;
    [ObservableProperty] private bool _notifyCi;
    [ObservableProperty] private bool _notifyWatchedRepoActivity;
    [ObservableProperty] private bool _notifyParticipating;
    [ObservableProperty] private NotificationStyle _selectedNotificationStyle;
    [ObservableProperty] private string _selectedNotificationSound = "System Asterisk";
    [ObservableProperty] private int _notificationVolume = 50;

    [ObservableProperty] private int _pollIntervalSeconds;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private bool _groupByRepository;
    [ObservableProperty] private bool _showReadItems;
    [ObservableProperty] private bool _surfaceRecentlyModified;

    public SettingsViewModel(
        ISettingsStore settings,
        ProviderRegistry registry,
        InboxService inboxService,
        IToastNotifier notifier)
    {
        _settings = settings;
        _registry = registry;
        _inboxService = inboxService;
        _notifier = notifier;

        Load();
        LoadAccounts();
    }

    /// <summary>Raised when an inbox display option (grouping / show-read) changes.</summary>
    public event Action? InboxDisplayChanged;

    /// <summary>Raised when accounts are added, removed, renamed, or paused.</summary>
    public event Action? AccountsChanged;

    /// <summary>The connected accounts, in the order they were added.</summary>
    public ObservableCollection<ProviderAccountViewModel> Accounts { get; } = new();

    public bool HasNoAccounts => Accounts.Count == 0;

    public IReadOnlyList<AddAccountOption> AddAccountOptions { get; } = new[]
    {
        new AddAccountOption("GitHub", ProviderKind.GitHub),
        new AddAccountOption("Bitbucket Cloud", ProviderKind.Bitbucket),
        new AddAccountOption("GitLab", ProviderKind.GitLab),
    };

    /// <summary>
    /// True when Windows can't deliver toasts on this PC (the Windows App Runtime is
    /// missing). The Notifications pane surfaces a warning and an install link when set.
    /// </summary>
    public bool ToastsUnavailable => !_notifier.IsAvailable;

    /// <summary>The running build, shown on the About page.</summary>
    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } informational
            ? informational.Split('+')[0]
            : Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Re-checks toast availability. Called when the window is shown so installing the
    /// runtime and reopening Settings clears the warning without an app restart.
    /// </summary>
    public void RefreshNotificationAvailability() => OnPropertyChanged(nameof(ToastsUnavailable));

    [RelayCommand]
    private static void OpenNotificationRuntimeHelp() => InboxViewModel.OpenUrl(NotificationRuntimeDownloadUrl);

    [RelayCommand]
    private static void OpenProject() => InboxViewModel.OpenUrl(ProjectUrl);

    [RelayCommand]
    private static void OpenReleases() => InboxViewModel.OpenUrl($"{ProjectUrl}/releases");

    /// <summary>
    /// Creates an account row, registers a provider for it, and immediately starts its connect
    /// flow — "Add account" and "Connect" are one gesture from the user's point of view.
    /// </summary>
    [RelayCommand]
    private async Task AddAccountAsync(AddAccountOption? option)
    {
        if (option is null)
        {
            return;
        }

        var account = new ProviderAccount
        {
            Id = ProviderAccount.NewId(),
            Provider = option.Provider,
        };

        var current = _settings.Load();
        current.Accounts.Add(account);
        _settings.Save(current);

        var provider = _registry.Add(account);
        var viewModel = NewAccountViewModel(provider, account);
        Accounts.Add(viewModel);
        OnPropertyChanged(nameof(HasNoAccounts));
        AccountsChanged?.Invoke();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        // An abandoned or failed authorization would otherwise leave an empty card behind.
        if (!viewModel.Connected)
        {
            RemoveAccount(viewModel);
        }
    }

    [RelayCommand]
    private void RemoveAccount(ProviderAccountViewModel? account)
    {
        if (account is null)
        {
            return;
        }

        // Disconnecting first revokes the live session; Remove then purges the row and its tokens.
        if (account.Connected)
        {
            account.DisconnectCommand.Execute(null);
        }

        _registry.Remove(account.AccountId);
        Accounts.Remove(account);
        OnPropertyChanged(nameof(HasNoAccounts));
        AccountsChanged?.Invoke();
        _ = _inboxService.RefreshAsync(CancellationToken.None);
    }

    /// <summary>
    /// Kicks off repository discovery for every Bitbucket account. Called when the Accounts page
    /// is shown, so the pickers are populated by the time a card is expanded.
    /// </summary>
    public void EnsureAccountReposLoaded()
    {
        foreach (var account in Accounts)
        {
            account.EnsureReposLoaded();
        }
    }

    private void LoadAccounts()
    {
        Accounts.Clear();
        foreach (var account in _settings.Load().Accounts)
        {
            if (_registry.Find(account.Id) is { } provider)
            {
                Accounts.Add(NewAccountViewModel(provider, account));
            }
        }

        OnPropertyChanged(nameof(HasNoAccounts));
    }

    private ProviderAccountViewModel NewAccountViewModel(IInboxProvider provider, ProviderAccount account)
    {
        var viewModel = new ProviderAccountViewModel(provider, account, _settings, _inboxService);
        viewModel.Changed += () => AccountsChanged?.Invoke();
        return viewModel;
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

    public IReadOnlyList<NotificationStyleOption> NotificationStyleOptions { get; } = new[]
    {
        new NotificationStyleOption("Toast and sound", NotificationStyle.Both),
        new NotificationStyleOption("Toast only", NotificationStyle.ToastOnly),
        new NotificationStyleOption("Sound only", NotificationStyle.AudioOnly)
    };

    private IReadOnlyList<NotificationSoundOption>? _notificationSounds;
    public IReadOnlyList<NotificationSoundOption> NotificationSounds => _notificationSounds ??= LoadAvailableSounds();

    public bool SoundSelectionEnabled => SelectedNotificationStyle != NotificationStyle.ToastOnly;

    private static IReadOnlyList<NotificationSoundOption> LoadAvailableSounds()
    {
        var list = new List<NotificationSoundOption>();
        try
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Notifications");
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.wav");
                var customOptions = files
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Select(name => new NotificationSoundOption(SplitCamelCase(name), name))
                    .ToList();

                string GetSortKey(string label)
                {
                    if (label == "Third High") return "Third 1";
                    if (label == "Third Mid") return "Third 2";
                    if (label == "Third Low") return "Third 3";
                    return label;
                }

                customOptions = customOptions.OrderBy(o => GetSortKey(o.Label), StringComparer.OrdinalIgnoreCase).ToList();
                list.AddRange(customOptions);
            }
        }
        catch
        {
            // Ignore directory search errors
        }

        // Add system sound options at the end
        list.Add(new NotificationSoundOption("System Notification", "SystemNotification"));
        list.Add(new NotificationSoundOption("System Mail Beep", "MailBeep"));
        list.Add(new NotificationSoundOption("System Asterisk", "SystemAsterisk"));
        list.Add(new NotificationSoundOption("System Default Beep", "SystemDefault"));

        return list;
    }

    private static string SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(input, "([A-Z])", " $1").Trim();
    }

    private void PreviewSound(string soundName, int? volume = null)
    {
        NotificationSoundPlayer.Play(soundName, volume ?? NotificationVolume);
    }

    /// <summary>
    /// Commits the volume once the slider is released. The slider binds with
    /// <c>UpdateSourceTrigger=PropertyChanged</c> so the readout tracks the thumb, but writing
    /// settings.json and replaying the preview on every tick would mean a file write per pixel.
    /// </summary>
    public void CommitVolume()
    {
        if (_loading)
        {
            return;
        }

        Persist();
        PreviewSound(SelectedNotificationSound, NotificationVolume);
    }

    partial void OnNotifyReviewRequestsChanged(bool value) => Persist();

    partial void OnNotifyMentionsChanged(bool value) => Persist();

    partial void OnNotifyCiChanged(bool value) => Persist();

    partial void OnNotifyWatchedRepoActivityChanged(bool value) => Persist();

    partial void OnNotifyParticipatingChanged(bool value) => Persist();

    partial void OnSelectedNotificationStyleChanged(NotificationStyle value)
    {
        Persist();
        OnPropertyChanged(nameof(SoundSelectionEnabled));
    }

    partial void OnSelectedNotificationSoundChanged(string value)
    {
        Persist();
        if (!_loading)
        {
            PreviewSound(value, NotificationVolume);
        }
    }

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

        SelectedNotificationStyle = s.Notifications.Style;

        var loadedSound = s.Notifications.Sound;
        if (NotificationSounds.All(o => o.Value != loadedSound))
        {
            loadedSound = NotificationSounds.Any(o => o.Value == "Glass") ? "Glass" : (NotificationSounds.Count > 0 ? NotificationSounds[0].Value : "SystemNotification");
        }
        SelectedNotificationSound = loadedSound;
        NotificationVolume = s.Notifications.Volume;

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

        _loading = false;
    }

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        // Reload first so account-managed fields (connection state, watched repos) aren't clobbered.
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
        s.Notifications.Style = SelectedNotificationStyle;
        s.Notifications.Sound = SelectedNotificationSound;
        s.Notifications.Volume = NotificationVolume;
        _settings.Save(s);
    }
}
