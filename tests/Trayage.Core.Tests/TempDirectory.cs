namespace Trayage.Core.Tests;

/// <summary>
/// A throwaway directory under %TEMP% that's deleted, with everything in it, on dispose. Test
/// classes that hand out file-backed stores own one, so nothing is left behind after a run.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string FullPath { get; } =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"trayage-test-{Guid.NewGuid():N}")).FullName;

    /// <summary>A unique file path inside this directory (the file itself isn't created).</summary>
    public string NewFile(string prefix, string extension = ".json") =>
        Path.Combine(FullPath, $"{prefix}-{Guid.NewGuid():N}{extension}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a file still held open shouldn't fail the test that owned it.
        }
    }
}
