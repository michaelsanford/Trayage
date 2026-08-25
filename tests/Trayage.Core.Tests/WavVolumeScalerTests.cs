using Trayage.Core.Audio;

namespace Trayage.Core.Tests;

public sealed class WavVolumeScalerTests
{
    private static byte[] Create16BitPcmWav(short[] samples, int sampleRate = 44100, short channels = 1)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        int dataSize = samples.Length * 2;
        bw.Write(36 + dataSize); // File size - 8
        bw.Write("WAVE"u8);

        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16); // Chunk size
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * 2); // Byte rate
        bw.Write((short)(channels * 2)); // Block align
        bw.Write((short)16); // Bits per sample

        // data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);
        foreach (var sample in samples)
        {
            bw.Write(sample);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ScaleVolume_ReturnsOriginalWhen100Percent()
    {
        var raw = Create16BitPcmWav(new short[] { 1000, -2000, 3000 });
        var scaled = WavVolumeScaler.ScaleVolume(raw, 100);

        Assert.Equal(raw, scaled);
    }

    [Fact]
    public void ScaleVolume_Scales16BitPcmSamplesAccurately()
    {
        var raw = Create16BitPcmWav(new short[] { 10000, -20000, 30000 });
        var scaled = WavVolumeScaler.ScaleVolume(raw, 50);

        // Check header integrity
        Assert.Equal("RIFF"u8.ToArray(), scaled[..4]);
        Assert.Equal("WAVE"u8.ToArray(), scaled[8..12]);

        // Check samples at data chunk (offset 44)
        short s1 = BitConverter.ToInt16(scaled, 44);
        short s2 = BitConverter.ToInt16(scaled, 46);
        short s3 = BitConverter.ToInt16(scaled, 48);

        Assert.Equal(5000, s1);
        Assert.Equal(-10000, s2);
        Assert.Equal(15000, s3);
    }

    [Fact]
    public void ScaleVolume_ScalesToZeroWhen0Percent()
    {
        var raw = Create16BitPcmWav(new short[] { 10000, -20000, 30000 });
        var scaled = WavVolumeScaler.ScaleVolume(raw, 0);

        short s1 = BitConverter.ToInt16(scaled, 44);
        short s2 = BitConverter.ToInt16(scaled, 46);
        short s3 = BitConverter.ToInt16(scaled, 48);

        Assert.Equal(0, s1);
        Assert.Equal(0, s2);
        Assert.Equal(0, s3);
    }

    [Fact]
    public void ScaleVolume_ClampsNegativeValuesCorrectly()
    {
        var raw = Create16BitPcmWav(new short[] { short.MinValue });
        var scaled = WavVolumeScaler.ScaleVolume(raw, 50);

        short s = BitConverter.ToInt16(scaled, 44);
        Assert.Equal(-16384, s);
    }

    [Fact]
    public void ScaleVolume_HandlesInvalidOrShortDataSafely()
    {
        var empty = Array.Empty<byte>();
        var resultEmpty = WavVolumeScaler.ScaleVolume(empty, 50);
        Assert.Empty(resultEmpty);

        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var resultGarbage = WavVolumeScaler.ScaleVolume(garbage, 50);
        Assert.Equal(garbage, resultGarbage);
    }
}
