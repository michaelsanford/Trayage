using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.App.ViewModels;

/// <summary>Backs the tray inbox flyout: the current items, refresh, and open actions.</summary>
public sealed partial class InboxViewModel : ObservableObject
{
    private readonly InboxService _inboxService;
    private readonly InboxState _state;
    private readonly ISettingsStore _settings;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public InboxViewModel(InboxService inboxService, InboxState state, ISettingsStore settings)
    {
        _inboxService = inboxService;
        _state = state;
        _settings = settings;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        _state.Changed += OnStateChanged;
        Rebuild();
    }

    /// <summary>Raised when the user clicks the settings button in the flyout.</summary>
    public event Action? OpenSettingsRequested;

    public ObservableCollection<InboxItemViewModel> Items { get; } = new();

    /// <summary>Grouped/sorted view the flyout binds to; shaped by the display settings.</summary>
    public ICollectionView ItemsView { get; }

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            await _inboxService.RefreshAsync(CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private static void OpenItem(InboxItemViewModel? item)
    {
        if (item is not null)
        {
            OpenUrl(item.WebUrl);
        }
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke();

    /// <summary>Opens an https URL in the user's default browser.</summary>
    public static void OpenUrl(string url)
    {
        // Only hand https URLs to the shell. Every link we open (inbox items, OAuth
        // verification/authorize URIs) is https, so this rejects nothing legitimate while
        // refusing to launch a hostile scheme from a malformed provider response.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A bad/blocked URL shouldn't crash the tray app.
        }
    }

    /// <summary>Re-renders the flyout from the cached snapshot when display settings change (no refetch).</summary>
    public void ApplyDisplaySettings() =>
        Application.Current?.Dispatcher.Invoke(Rebuild);

    private void OnStateChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Rebuild);

    private bool? _lastGroupByRepo;

    private void Rebuild()
    {
        var settings = _settings.Load();
        var groupByRepo = settings.GroupByRepository;
        var includeRepo = !groupByRepo;
        var now = DateTimeOffset.UtcNow;
        var recencyWindow = InboxRecency.WindowFor(settings);

        // Index the current wrappers so unchanged items keep their existing instance
        // (no allocation, no rebind) and only genuinely changed slots fire collection events.
        var existing = new Dictionary<(ProviderKind, string), InboxItemViewModel>();
        foreach (var vm in Items)
        {
            existing[vm.Item.Key] = vm;
        }

        var target = new List<InboxItemViewModel>(Items.Count);
        foreach (var item in _state.Items)
        {
            // Hide read items unless the user opted to show them — but always keep a read item
            // that was updated recently, so a thread GitHub's REST API marks read (while the web
            // bell still flags it new) doesn't silently vanish from the list.
            if (!settings.ShowReadItems && !item.IsUnread && !InboxRecency.IsRecent(item, now, recencyWindow))
            {
                continue;
            }

            // Reuse only when the underlying (immutable) item is value-equal and the subtitle
            // layout, which depends on groupByRepo, hasn't flipped since the last render.
            if (groupByRepo == _lastGroupByRepo &&
                existing.TryGetValue(item.Key, out var vm) && vm.Item == item)
            {
                target.Add(vm);
            }
            else
            {
                target.Add(new InboxItemViewModel(item, includeRepoInSubtitle: includeRepo));
            }
        }

        var changed = SyncItems(target);

        var groupingChanged = groupByRepo != _lastGroupByRepo;
        if (groupingChanged)
        {
            using (ItemsView.DeferRefresh())
            {
                ItemsView.GroupDescriptions.Clear();
                ItemsView.SortDescriptions.Clear();
                if (groupByRepo)
                {
                    ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InboxItemViewModel.RepositoryFullName)));
                }
                else
                {
                    // Flat list: newest first, grouped under Today / Yesterday / … recency headers.
                    // Sorting by UpdatedAt descending also fixes the order the buckets appear in.
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.UpdatedAt), ListSortDirection.Descending));
                    ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InboxItemViewModel.TimeBucket)));
                }
            }

            _lastGroupByRepo = groupByRepo;
        }

        // Nothing visible moved; skip the status/notify work too.
        if (!changed && !groupingChanged)
        {
            return;
        }

        var unread = Items.Count(i => i.IsUnread);
        OnPropertyChanged(nameof(IsEmpty));
        StatusText = Items.Count == 0
            ? "You're all caught up."
            : unread == 0
                ? $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")}, all read."
                : $"{unread} item{(unread == 1 ? string.Empty : "s")} need your attention.";
    }

    /// <summary>
    /// Reconciles <see cref="Items"/> to <paramref name="target"/> in place (replace/append/trim)
    /// rather than Clear()+rebuild, so a poll that changes nothing produces no collection events.
    /// Returns whether the collection was modified.
    /// </summary>
    private bool SyncItems(IReadOnlyList<InboxItemViewModel> target)
    {
        var changed = false;

        for (var i = 0; i < target.Count; i++)
        {
            if (i < Items.Count)
            {
                if (!ReferenceEquals(Items[i], target[i]))
                {
                    Items[i] = target[i];
                    changed = true;
                }
            }
            else
            {
                Items.Add(target[i]);
                changed = true;
            }
        }

        while (Items.Count > target.Count)
        {
            Items.RemoveAt(Items.Count - 1);
            changed = true;
        }

        return changed;
    }
}
