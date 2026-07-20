using System.IO;
using System.Runtime.InteropServices;
using System.Media;
using Microsoft.Extensions.Logging;

namespace Trayage.App.Notifications;

public static class NotificationSoundPlayer
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    private const uint SndAlias = 0x00010000;
    private const uint SndAsync = 0x00000001;
    private const uint SndNoDefault = 0x00000002;

    public static void Play(string soundName, ILogger? logger = null)
    {
        try
        {
            if (soundName == "SystemNotification" || soundName == "MailBeep" || soundName == "SystemAsterisk" || soundName == "SystemDefault")
            {
                PlaySound(soundName, IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
                return;
            }

            var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Notifications", soundName + ".wav");
            if (File.Exists(soundPath))
            {
                using var player = new SoundPlayer(soundPath);
                player.Play();
                return;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play sound '{SoundName}'; falling back to SystemAsterisk.", soundName);
        }

        // Fallback to Asterisk
        try
        {
            PlaySound("SystemAsterisk", IntPtr.Zero, SndAlias | SndAsync | SndNoDefault);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to play default system Asterisk sound.");
        }
    }
}
