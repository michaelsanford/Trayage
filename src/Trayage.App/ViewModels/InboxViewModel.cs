using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;

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

    /// <summary>Opens a URL in the user's default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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

    private void Rebuild()
    {
        var settings = _settings.Load();
        var groupByRepo = settings.GroupByRepository;

        Items.Clear();
        var source = _state.Items.AsEnumerable();
        if (!settings.ShowReadItems)
        {
            source = source.Where(i => i.IsUnread);
        }

        foreach (var item in source)
        {
            Items.Add(new InboxItemViewModel(item, includeRepoInSubtitle: !groupByRepo));
        }

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
                // Flat list: newest first.
                ItemsView.SortDescriptions.Add(new SortDescription(nameof(InboxItemViewModel.UpdatedAt), ListSortDirection.Descending));
            }
        }

        var unread = Items.Count(i => i.IsUnread);
        OnPropertyChanged(nameof(IsEmpty));
        StatusText = Items.Count == 0
            ? "You're all caught up."
            : unread == 0
                ? $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")}, all read."
                : $"{unread} item{(unread == 1 ? string.Empty : "s")} need your attention.";
    }
}
