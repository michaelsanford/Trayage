using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trayage.Core.Inbox;

namespace Trayage.App.ViewModels;

/// <summary>Backs the tray inbox flyout: the current items, refresh, and open actions.</summary>
public sealed partial class InboxViewModel : ObservableObject
{
    private readonly InboxService _inboxService;
    private readonly InboxState _state;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public InboxViewModel(InboxService inboxService, InboxState state)
    {
        _inboxService = inboxService;
        _state = state;
        _state.Changed += OnStateChanged;
        Rebuild();
    }

    /// <summary>Raised when the user clicks the settings button in the flyout.</summary>
    public event Action? OpenSettingsRequested;

    public ObservableCollection<InboxItemViewModel> Items { get; } = new();

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

    private void OnStateChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Rebuild);

    private void Rebuild()
    {
        Items.Clear();
        foreach (var item in _state.Items)
        {
            Items.Add(new InboxItemViewModel(item));
        }

        OnPropertyChanged(nameof(IsEmpty));
        StatusText = Items.Count == 0
            ? "You're all caught up."
            : $"{Items.Count} item{(Items.Count == 1 ? string.Empty : "s")} need your attention.";
    }
}
