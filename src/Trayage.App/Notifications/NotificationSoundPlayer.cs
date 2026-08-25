using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Trayage.Core.Audio;

namespace Trayage.App.Notifications;

public static class NotificationSoundPlayer
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    private const uint SndAlias = 0x00010000;
    private const uint SndAsync = 0x00000001;
    private const uint SndNoDefault = 0x00000002;

    private static readonly ConcurrentDictionary<string, byte[]> FileCache = new(StringComparer.OrdinalIgnoreCase);

    public static void Play(string soundName, int volume = 50, ILogger? logger = null)
    {
        if (volume <= 0)
        {
            return;
        }

        volume = Math.Clamp(volume, 0, 100);

        try
        {
            // 1. Check if it is a bundled WAV asset
            var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Notifications", soundName + ".wav");
            if (File.Exists(soundPath))
            {
                PlayWavFile(soundPath, volume);
                return;
            }

            // 2. If it is a system sound alias, try resolving its WAV path from the Windows registry to scale volume
            var sysPath = ResolveSystemSoundPath(soundName);
            if (!string.IsNullOrEmpty(sysPath) && File.Exists(sysPath))
            {
                PlayWavFile(sysPath, volume);
                return;
            }

            // 3. If it is a system alias without a resolved WAV file, fall back to WinMM PlaySound
            if (IsSystemAlias(soundName))
            {
                PlaySound(soundName, IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
                return;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play sound '{SoundName}' at volume {Volume}; falling back to SystemAsterisk.", soundName, volume);
        }

        // Fallback to Asterisk
        try
        {
            var fallbackPath = ResolveSystemSoundPath("SystemAsterisk");
            if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
            {
                PlayWavFile(fallbackPath, volume);
                return;
            }

            PlaySound("SystemAsterisk", IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play default system Asterisk sound.");
        }
    }

    private static bool IsSystemAlias(string soundName) =>
        soundName is "SystemNotification" or "MailBeep" or "SystemAsterisk" or "SystemDefault";

    private static void PlayWavFile(string filePath, int volume)
    {
        if (!FileCache.TryGetValue(filePath, out var rawBytes))
        {
            rawBytes = File.ReadAllBytes(filePath);
            FileCache[filePath] = rawBytes;
        }

        if (volume >= 100)
        {
            using var player = new SoundPlayer(new MemoryStream(rawBytes));
            player.Play();
        }
        else
        {
            var scaledBytes = WavVolumeScaler.ScaleVolume(rawBytes, volume);
            using var player = new SoundPlayer(new MemoryStream(scaledBytes));
            player.Play();
        }
    }

    private static string? ResolveSystemSoundPath(string soundName)
    {
        try
        {
            var registryKeys = soundName switch
            {
                "SystemNotification" => new[] { "Notification.Default", "SystemNotification" },
                "MailBeep" => new[] { "MailBeep" },
                "SystemAsterisk" => new[] { "SystemAsterisk" },
                "SystemDefault" => new[] { ".Default" },
                _ => Array.Empty<string>()
            };

            foreach (var key in registryKeys)
            {
                var val = Registry.GetValue($@"HKEY_CURRENT_USER\AppEvents\Schemes\Apps\.Default\{key}\.Current", "", null) as string;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(val);
                    if (File.Exists(expanded))
                    {
                        return expanded;
                    }
                }
            }
        }
        catch
        {
            // Registry read errors should fail silently and fall back to PlaySound
        }

        return null;
    }
}
