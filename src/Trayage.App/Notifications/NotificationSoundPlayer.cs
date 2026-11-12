using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Trayage.Core.Audio;

namespace Trayage.App.Notifications;

/// <summary>
/// Plays notification sounds at the user's volume. Bundled and system sounds that resolve to a
/// WAV file are volume-scaled in memory; a system alias with no file falls back to WinMM at
/// the system volume.
/// </summary>
public static partial class NotificationSoundPlayer
{
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string sound, IntPtr module, uint flags);

    private const uint SndAlias = 0x00010000;
    private const uint SndAsync = 0x00000001;
    private const uint SndNoDefault = 0x00000002;

    /// <summary>Notifications closer together than this share one sound.</summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(1);

    /// <summary>Volume-scaled WAV bytes per (file, volume): scaling copies the whole file.</summary>
    private static readonly ConcurrentDictionary<(string Path, int Volume), byte[]> ScaledCache = new();

    // The player whose buffer WinMM is currently reading from. Held (not disposed right after
    // Play) so the asynchronous playback's buffer stays alive; replaced by the next sound.
    private static SoundPlayer? _current;
    private static long _lastNotificationTicks;

    /// <summary>
    /// Plays the sound for a notification. Several items arriving in one poll each raise a
    /// notification; they get one sound, not a stutter of restarts.
    /// </summary>
    public static void PlayForNotification(string soundName, int volume, ILogger? logger = null)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Exchange(ref _lastNotificationTicks, now);
        if (now - last < BurstWindow.TotalMilliseconds)
        {
            return;
        }

        Play(soundName, volume, logger);
    }

    /// <summary>Plays <paramref name="soundName"/> immediately (e.g. a preview in Settings).</summary>
    public static void Play(string soundName, int volume = 50, ILogger? logger = null)
    {
        if (volume <= 0)
        {
            return;
        }

        volume = Math.Clamp(volume, 0, 100);

        try
        {
            // A bundled WAV, or a system sound whose WAV the registry names: both can be scaled.
            var path = NotificationSoundCatalog.BundledPath(soundName) ?? ResolveSystemSoundPath(soundName);
            if (path is not null)
            {
                PlayWavFile(path, volume);
                return;
            }

            // A system alias without a resolvable file plays at the system volume.
            if (NotificationSoundCatalog.IsSystemAlias(soundName))
            {
                PlaySound(soundName, IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
                return;
            }

            logger?.LogDebug("Unknown notification sound '{SoundName}'; playing the fallback.", soundName);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play sound '{SoundName}' at volume {Volume}; playing the fallback.", soundName, volume);
        }

        PlayFallback(volume, logger);
    }

    private static void PlayFallback(int volume, ILogger? logger)
    {
        try
        {
            if (ResolveSystemSoundPath(NotificationSoundCatalog.FallbackSound) is { } path)
            {
                PlayWavFile(path, volume);
                return;
            }

            PlaySound(NotificationSoundCatalog.FallbackSound, IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play the fallback notification sound.");
        }
    }

    private static void PlayWavFile(string filePath, int volume)
    {
        var bytes = ScaledCache.GetOrAdd((filePath, volume), static key =>
        {
            var raw = File.ReadAllBytes(key.Path);
            return key.Volume >= 100 ? raw : WavVolumeScaler.ScaleVolume(raw, key.Volume);
        });

        var player = new SoundPlayer(new MemoryStream(bytes, writable: false));
        player.Play();

        // The new sound has replaced the old one in WinMM, so the old buffer can go.
        Interlocked.Exchange(ref _current, player)?.Dispose();
    }

    private static string? ResolveSystemSoundPath(string soundName)
    {
        var registryKeys = soundName switch
        {
            "SystemNotification" => new[] { "Notification.Default", "SystemNotification" },
            "MailBeep" => new[] { "MailBeep" },
            "SystemAsterisk" => new[] { "SystemAsterisk" },
            "SystemDefault" => new[] { ".Default" },
            _ => Array.Empty<string>(),
        };

        try
        {
            foreach (var key in registryKeys)
            {
                var value = Registry.GetValue($@"HKEY_CURRENT_USER\AppEvents\Schemes\Apps\.Default\{key}\.Current", "", null) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(value);
                    if (File.Exists(expanded))
                    {
                        return expanded;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            // Unreadable registry: fall back to WinMM's alias playback.
        }

        return null;
    }
}
