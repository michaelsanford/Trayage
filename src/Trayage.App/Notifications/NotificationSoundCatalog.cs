using System.IO;
using System.Text.RegularExpressions;

namespace Trayage.App.Notifications;

/// <summary>A notification sound as offered in Settings: what's shown, and what's stored.</summary>
public sealed record NotificationSoundOption(string Label, string Value);

/// <summary>
/// The single list of notification sounds: the WAVs bundled under
/// <c>Assets\Notifications</c> plus the Windows system-sound aliases. Both the Settings picker
/// and <see cref="NotificationSoundPlayer"/> read it, so they can't disagree about which values
/// are valid.
/// </summary>
public static partial class NotificationSoundCatalog
{
    /// <summary>
    /// What plays when the stored sound is unknown or can't be played, and therefore also what
    /// Settings shows in that case. Matches <c>NotificationSettings.Sound</c>'s default.
    /// </summary>
    public const string FallbackSound = "SystemAsterisk";

    public static string AssetsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Notifications");

    private static readonly NotificationSoundOption[] SystemSounds =
    [
        new("System Notification", "SystemNotification"),
        new("System Mail Beep", "MailBeep"),
        new("System Asterisk", "SystemAsterisk"),
        new("System Default Beep", "SystemDefault"),
    ];

    private static readonly Lazy<IReadOnlyList<NotificationSoundOption>> AllOptions = new(LoadOptions);

    /// <summary>Bundled sounds (alphabetical, "Third" chimes high → low), then the system sounds.</summary>
    public static IReadOnlyList<NotificationSoundOption> Options => AllOptions.Value;

    public static bool IsSystemAlias(string soundName) => SystemSounds.Any(s => s.Value == soundName);

    /// <summary>The bundled WAV for <paramref name="soundName"/>, or null when it isn't a bundled sound.</summary>
    public static string? BundledPath(string soundName)
    {
        // Values come from settings.json; never let one walk out of the assets folder.
        if (soundName.Length == 0 || soundName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var path = Path.Combine(AssetsDirectory, soundName + ".wav");
        return File.Exists(path) ? path : null;
    }

    /// <summary><paramref name="stored"/> if it's a known sound, otherwise <see cref="FallbackSound"/>.</summary>
    public static string Normalize(string? stored) =>
        stored is not null && Options.Any(o => o.Value == stored) ? stored : FallbackSound;

    private static IReadOnlyList<NotificationSoundOption> LoadOptions()
    {
        var bundled = new List<NotificationSoundOption>();
        try
        {
            if (Directory.Exists(AssetsDirectory))
            {
                bundled.AddRange(Directory.GetFiles(AssetsDirectory, "*.wav")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Select(name => new NotificationSoundOption(SplitCamelCase(name), name))
                    .OrderBy(o => SortKey(o.Label), StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No bundled sounds; the system sounds still work.
        }

        return [.. bundled, .. SystemSounds];
    }

    // The three "Third" chimes read best high → mid → low, not alphabetically.
    private static string SortKey(string label) => label switch
    {
        "Third High" => "Third 1",
        "Third Mid" => "Third 2",
        "Third Low" => "Third 3",
        _ => label,
    };

    /// <summary>"SecretCricket" → "Secret Cricket".</summary>
    private static string SplitCamelCase(string input) => CapitalLetter().Replace(input, " $1").Trim();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex CapitalLetter();
}
