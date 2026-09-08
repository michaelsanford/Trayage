using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// One locally-marked-read item. <paramref name="MarkedUpdatedAt"/> is the thread's
/// <see cref="InboxItem.UpdatedAt"/> at the moment it was marked — the mark covers that state
/// of the thread and nothing later, which is how new activity on an already-read item is told
/// apart from the activity the user dismissed. <paramref name="LastSeenUtc"/> is only for
/// pruning: a record whose item stops appearing in snapshots eventually ages out.
/// </summary>
public sealed record ReadMark(DateTimeOffset MarkedUpdatedAt, DateTimeOffset LastSeenUtc);

/// <summary>
/// Stores read marks as JSON at <see cref="TrayagePaths.ReadStateFile"/>. Writes are atomic
/// (temp file + replace), matching <see cref="JsonSettingsStore"/>.
/// </summary>
public sealed class JsonReadStateStore : IReadStateStore
{
    /// <summary>Records for items nobody has seen in this long are dropped.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    /// <summary>
    /// How stale a record's last-seen stamp may get before a save refreshes it. Without a
    /// throttle, every poll would rewrite the file just to touch timestamps; without any
    /// refresh at all, a record for an item still on screen would be pruned after
    /// <see cref="Retention"/>.
    /// </summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ILogger<JsonReadStateStore> _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();

    private Dictionary<string, ReadMark>? _marks;

    public JsonReadStateStore(ILogger<JsonReadStateStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? TrayagePaths.ReadStateFile;
    }

    public string FilePath => _filePath;

    public void MarkRead(IEnumerable<InboxItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        lock (_gate)
        {
            var marks = Load();
            var now = DateTimeOffset.UtcNow;
            var changed = false;

            foreach (var item in items)
            {
                marks[item.StorageKey] = new ReadMark(item.UpdatedAt, now);
                changed = true;
            }

            if (changed)
            {
                Save(marks);
            }
        }
    }

    public IReadOnlyList<InboxItem> Apply(IReadOnlyList<InboxItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        lock (_gate)
        {
            var marks = Load();
            if (marks.Count == 0)
            {
                return items;
            }

            var now = DateTimeOffset.UtcNow;
            var changed = false;

            // Copied only once something actually changes, so the common "nothing marked in this
            // snapshot" path doesn't reallocate the list.
            List<InboxItem>? overlaid = null;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!marks.TryGetValue(item.StorageKey, out var mark))
                {
                    continue;
                }

                if (item.UpdatedAt > mark.MarkedUpdatedAt)
                {
                    // The thread moved on after the mark: this is genuinely new activity, so the
                    // item goes back to whatever the provider says and the mark is spent. Leaving
                    // it would silence a real notification.
                    marks.Remove(item.StorageKey);
                    changed = true;
                    continue;
                }

                overlaid ??= new List<InboxItem>(items);
                overlaid[i] = item with { IsUnread = false, IsExplicitlyRead = true };

                if (now - mark.LastSeenUtc > TouchInterval)
                {
                    marks[item.StorageKey] = mark with { LastSeenUtc = now };
                    changed = true;
                }
            }

            // Prune records whose items have stopped appearing. Records that merely became
            // redundant — GitHub now reporting the item read itself — are deliberately kept:
            // dropping them would lose IsExplicitlyRead, and the recency bridge would then pull
            // the item back on screen for a couple of poll intervals.
            foreach (var key in marks.Where(m => now - m.Value.LastSeenUtc > Retention).Select(m => m.Key).ToList())
            {
                marks.Remove(key);
                changed = true;
            }

            if (changed)
            {
                Save(marks);
            }

            return overlaid ?? items;
        }
    }

    /// <summary>Reads the file once and caches it; this process is the only writer.</summary>
    private Dictionary<string, ReadMark> Load()
    {
        if (_marks is not null)
        {
            return _marks;
        }

        if (!File.Exists(_filePath))
        {
            return _marks = new Dictionary<string, ReadMark>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ReadMark>>(json, SerializerOptions);
            return _marks = loaded is null
                ? new Dictionary<string, ReadMark>(StringComparer.Ordinal)
                : new Dictionary<string, ReadMark>(loaded, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Losing read marks is a cosmetic setback, not worth failing a poll over.
            _logger.LogWarning(ex, "Failed to read read-state from {Path}; starting empty.", _filePath);
            return _marks = new Dictionary<string, ReadMark>(StringComparer.Ordinal);
        }
    }

    private void Save(Dictionary<string, ReadMark> marks)
    {
        _marks = marks;

        try
        {
            var json = JsonSerializer.Serialize(marks, SerializerOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // File.Replace requires the destination to exist; fall back to Move on first save.
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        catch (IOException ex)
        {
            // Keep the in-memory marks: the session stays correct even if the file didn't take.
            _logger.LogWarning(ex, "Failed to write read-state to {Path}.", _filePath);
        }
    }
}
