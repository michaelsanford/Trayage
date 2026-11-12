using System.Text;

namespace Trayage.Core.Configuration;

/// <summary>
/// Crash-safe whole-file writes shared by the JSON stores: the new contents go to a sibling
/// temp file, are flushed to disk, and then atomically replace the original. A crash or power
/// loss at any point leaves either the old file or the new one, never a truncated mix.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
    {
        var tempPath = path + ".tmp";

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Utf8NoBom.GetBytes(contents);
            stream.Write(bytes);

            // File.WriteAllText stops at the OS cache; without this, a power cut just after the
            // replace below can leave a zero-length file in place of the real one.
            stream.Flush(flushToDisk: true);
        }

        // File.Replace requires the destination to exist; fall back to Move on first save.
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
