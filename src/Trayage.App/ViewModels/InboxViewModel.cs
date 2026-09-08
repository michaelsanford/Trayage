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

    private DateTime _lastRefreshedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan AutoRefreshThreshold = TimeSpan.FromSeconds(15);

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

    /// <summary>
    /// Called whenever the flyout is shown: immediately re-renders time-dependent recency/buckets
    /// from cache, and triggers a background refresh if data is older than the auto-refresh threshold.
    /// </summary>
    public async Task OnFlyoutOpenedAsync()
    {
        Application.Current?.Dispatcher.Invoke(() => Rebuild(force: true));

        if (!IsRefreshing && DateTime.UtcNow - _lastRefreshedAtUtc >= AutoRefreshThreshold)
        {
            await RefreshAsync();
        }
    }

    private ObservableCollection<InboxItemViewModel> Items { get; } = new();

    /// <summary>Grouped/sorted view the flyout binds to; shaped by the display settings.</summary>
    public ICollectionView ItemsView { get; }

    /// <summary>
    /// The grouping currently applied to <see cref="ItemsView"/>. The flyout reads it to decide
    /// what a group header means — only an owner header carries a provider, for instance.
    /// </summary>
    public InboxGrouping Grouping => _lastGrouping ?? InboxGrouping.Repository;

    /// <summary>
    /// Group headers the user has collapsed, as <see cref="GroupKey"/> values. A collection
    /// rather than a set because the flyout binds to it: the group template watches it for
    /// changes to re-evaluate each group's visibility.
    /// </summary>
    public ObservableCollection<string> CollapsedGroups { get; } = new();

    /// <summary>
    /// Qualifies a group's display name with the grouping that produced it, so collapsing the
    /// "acme" owner group doesn't also collapse an "acme" repository group once the user
    /// switches grouping. Case-insensitive to match the grouping comparison.
    /// </summary>
    public static string GroupKey(InboxGrouping grouping, string? name) =>
        string.Concat(grouping.ToString(), ":", name?.ToLowerInvariant() ?? string.Empty);

    /// <summary>Collapses or expands one group, persisting the change so it survives a restart.</summary>
    [RelayCommand]
    private void ToggleGroup(string? name)
    {
        if (name is null)
        {
            return;
        }

        var key = GroupKey(Grouping, name);
        if (!CollapsedGroups.Remove(key))
        {
            CollapsedGroups.Add(key);
        }

        var settings = _settings.Load();
        settings.CollapsedInboxGroups.Clear();
        settings.CollapsedInboxGroups.AddRange(CollapsedGroups);
        _settings.Save(settings);
    }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Gates the header's mark-everything-read action; nothing unread, nothing to do.</summary>
    public bool HasUnread => Items.Any(i => i.IsUnread);

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
            var result = await _inboxService.RefreshAsync(CancellationToken.None);
            _lastRefreshedAtUtc = DateTime.UtcNow;

            // A provider that failed this refresh means the list the user is looking at is
            // incomplete. Say so rather than letting an unchanged (or emptier) list read as
            // "nothing new". Cleared on the next refresh that succeeds.
            _degradedNotice = result.Failures.Count == 0
                ? null
                : $"Couldn't reach {Join(result.Failures)} — this list may be incomplete.";
            Rebuild(force: true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static string Join(IReadOnlyList<ProviderFailure> failures) =>
        string.Join(" and ", failures.Select(f => f.Label));

    /// <summary>
    /// Opens the item and marks it read: having opened the thread, the user has seen it, and on
    /// GitHub the browser visit would mark it read anyway — this just means the tray badge
    /// doesn't wait a poll to agree.
    /// </summary>
    [RelayCommand]
    private async Task OpenItemAsync(InboxItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        OpenUrl(item.WebUrl);
        await MarkReadAsync(new[] { item.Item }).ConfigureAwait(true);
    }

    /// <summary>Marks one row read.</summary>
    [RelayCommand]
    private async Task MarkRead(InboxItemViewModel? item)
    {
        if (item is not null)
        {
            await MarkReadAsync(new[] { item.Item }).ConfigureAwait(true);
        }
    }

    /// <summary>Marks every item in one group read, addressed by the group's display name.</summary>
    [RelayCommand]
    private async Task MarkGroupRead(string? name)
    {
        if (name is null)
        {
            return;
        }

        var group = ItemsView.Groups?
            .OfType<CollectionViewGroup>()
            .FirstOrDefault(g => string.Equals(g.Name as string, name, StringComparison.OrdinalIgnoreCase));

        if (group is null)
        {
            return;
        }

        await MarkReadAsync(group.Items.OfType<InboxItemViewModel>().Select(i => i.Item).ToList()).ConfigureAwait(true);
    }

    /// <summary>Marks everything currently in the inbox read.</summary>
    [RelayCommand]
    private async Task MarkAllRead() =>
        await MarkReadAsync(Items.Select(i => i.Item).ToList()).ConfigureAwait(true);

    /// <summary>
    /// The one path all four mark-read gestures share. <see cref="InboxService"/> republishes the
    /// snapshot, so the rebuild arrives through <see cref="InboxState.Changed"/> rather than
    /// being forced here.
    /// </summary>
    private async Task MarkReadAsync(IReadOnlyCollection<InboxItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            var result = await _inboxService.MarkAsReadAsync(items, CancellationToken.None).ConfigureAwait(true);

            // Reuse the degraded-notice channel: it already survives re-renders, so a "reconnect
            // this account" prompt doesn't vanish on the next render. Only ever set, never
            // cleared here — clearing would wipe a provider-failure notice a refresh had put
            // there, and the next refresh recomputes the field anyway.
            if (result.Notices.Count > 0)
            {
                _degradedNotice = string.Join(" ", result.Notices);
            }

            Rebuild(force: true);
        }
        catch (Exception ex)
        {
            // The local mark is written before any network call, so the user's action is never
            // lost — this only catches an unexpected failure in the write fan-out.
            Debug.WriteLine($"Marking items read failed: {ex.Message}");
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

    private InboxGrouping? _lastGrouping;

    /// <summary>
    /// Set when the last manual refresh had a failing provider; appended to <see cref="StatusText"/>
    /// on every render so a re-render doesn't drop the warning. Null when everything is healthy.
    /// </summary>
    private string? _degradedNotice;

    /// <summary>
    /// Rebuilds the view's grouping and sorting for one <see cref="InboxGrouping"/>.
    ///
    /// Groups appear in the order of the *first* sort description, so how to group and how to
    /// sort are two halves of one decision. That's why the recency buckets deliberately don't
    /// sort by their key: alphabetically they'd read "Earlier this week, Older, Today,
    /// Yesterday", whereas sorting by UpdatedAt puts both the buckets and the rows inside them
    /// in the order the user expects.
    /// </summary>
    private void ApplyGrouping(InboxGrouping grouping)
    {
        // Providers don't agree on owner casing, and PropertyGroupDescription compares group
        // names as strings — without this, one org can end up under two headers.
        var groupBy = new PropertyGroupDescription(grouping switch
        {
            InboxGrouping.Repository => nameof(InboxItemViewModel.RepositoryFullName),
            InboxGrouping.Owner => nameof(InboxItemViewModel.RepositoryOwner),
            _ => nameof(InboxItemViewModel.TimeBucket),
        })
        {
            StringComparison = StringComparison.OrdinalIgnoreCase,
        };

        using (ItemsView.DeferRefresh())
        {
            ItemsView.GroupDescriptions.Clear();
            ItemsView.SortDescriptions.Clear();

            switch (grouping)
            {
                case InboxGrouping.Repository:
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.RepositoryFullName), ListSortDirection.Ascending));
                    break;

                case InboxGrouping.Owner:
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.RepositoryOwner), ListSortDirection.Ascending));

                    // Keeps one repository's rows together inside an owner group — the readable
                    // half of nesting repositories under owners, without the second level of
                    // headers that would cost too much in a 400px flyout.
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.RepositoryFullName), ListSortDirection.Ascending));
                    break;
            }

            ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.UpdatedAt), ListSortDirection.Descending));
            ItemsView.GroupDescriptions.Add(groupBy);
        }
    }

    /// <summary>
    /// Re-seeds <see cref="CollapsedGroups"/> from the persisted set, keeping only the keys
    /// belonging to the grouping now in effect so the flyout never has to reason about keys
    /// left behind by another grouping.
    /// </summary>
    private void ReloadCollapsedGroups(TrayageSettings settings, InboxGrouping grouping)
    {
        var prefix = string.Concat(grouping.ToString(), ":");
        CollapsedGroups.Clear();
        foreach (var key in settings.CollapsedInboxGroups)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                CollapsedGroups.Add(key);
            }
        }
    }

    /// <summary>
    /// Re-renders from the cached snapshot. This parameterless overload exists so the method
    /// group can be passed straight to <see cref="System.Windows.Threading.Dispatcher.Invoke(Action)"/>:
    /// with only the optional-parameter form, the method group's natural type is
    /// <c>Action&lt;bool&gt;</c>, which silently binds to the <c>Invoke(Delegate, params object[])</c>
    /// overload and throws TargetParameterCountException at runtime.
    /// </summary>
    private void Rebuild() => Rebuild(force: false);

    /// <param name="force">
    /// Re-render the status line even when no item moved — needed when only
    /// <see cref="_degradedNotice"/> changed.
    /// </param>
    private void Rebuild(bool force)
    {
        var settings = _settings.Load();
        var grouping = settings.Grouping;

        // Only name the account when it actually disambiguates — a single-account user should
        // see exactly the subtitle they saw before accounts existed.
        var labelsByAccount = settings.Accounts
            .GroupBy(a => a.Provider)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToDictionary(a => a.Id, a => a.DisplayLabel, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var recencyWindow = InboxRecency.WindowFor(settings);

        // Index the current wrappers so unchanged items keep their existing instance
        // (no allocation, no rebind) and only genuinely changed slots fire collection events.
        var existing = new Dictionary<(ProviderKind, string, string), InboxItemViewModel>();
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
            if (!settings.ShowReadItems && !item.IsUnread && !InboxRecency.ShouldSurfaceRead(item, now, recencyWindow))
            {
                continue;
            }

            // Reuse only when the underlying (immutable) item is value-equal and the subtitle
            // layout, which depends on the grouping, hasn't changed since the last render.
            labelsByAccount.TryGetValue(item.AccountId, out var accountLabel);

            if (grouping == _lastGrouping &&
                existing.TryGetValue(item.Key, out var vm) && vm.Item == item &&
                vm.AccountLabel == accountLabel)
            {
                target.Add(vm);
            }
            else
            {
                target.Add(new InboxItemViewModel(item, grouping, accountLabel));
            }
        }

        var changed = SyncItems(target);

        var groupingChanged = grouping != _lastGrouping;
        if (groupingChanged)
        {
            ApplyGrouping(grouping);
            _lastGrouping = grouping;
            OnPropertyChanged(nameof(Grouping));
            ReloadCollapsedGroups(settings, grouping);
        }
        else if (changed || force)
        {
            ItemsView.Refresh();
        }

        // Nothing visible moved; skip the status/notify work too.
        if (!changed && !groupingChanged && !force)
        {
            return;
        }

        var unread = Items.Count(i => i.IsUnread);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasUnread));
        var status = Items.Count == 0
            ? "You're all caught up."
            : unread == 0
                ? $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")}, all read."
                : $"{unread} item{(unread == 1 ? string.Empty : "s")} need your attention.";

        // "You're all caught up." is exactly the wrong thing to say when a provider is down, so
        // the degraded notice replaces the count rather than trailing it.
        StatusText = _degradedNotice ?? status;
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
