namespace Trayage.Core.Audio;

/// <summary>
/// Scales the volume of uncompressed WAV (PCM / IEEE Float) audio in memory.
/// </summary>
public static class WavVolumeScaler
{
    private static readonly byte[] RiffHeader = "RIFF"u8.ToArray();
    private static readonly byte[] WaveHeader = "WAVE"u8.ToArray();
    private static readonly byte[] FmtHeader = "fmt "u8.ToArray();
    private static readonly byte[] DataHeader = "data"u8.ToArray();

    /// <summary>
    /// Scales the amplitude of audio samples in <paramref name="wavData"/> by <paramref name="volumePercent"/>.
    /// </summary>
    /// <param name="wavData">Raw WAV file byte content.</param>
    /// <param name="volumePercent">Target volume percentage between 0 and 100.</param>
    /// <returns>A new byte array with scaled sample amplitudes, or the original data if invalid or 100%.</returns>
    public static byte[] ScaleVolume(ReadOnlySpan<byte> wavData, int volumePercent)
    {
        if (wavData.Length < 44)
        {
            return wavData.ToArray();
        }

        if (!wavData[..4].SequenceEqual(RiffHeader) || !wavData.Slice(8, 4).SequenceEqual(WaveHeader))
        {
            return wavData.ToArray();
        }

        volumePercent = Math.Clamp(volumePercent, 0, 100);
        if (volumePercent == 100)
        {
            return wavData.ToArray();
        }

        short formatTag = 0;
        short bitsPerSample = 0;
        int dataOffset = -1;
        int dataSize = 0;

        var offset = 12;
        while (offset + 8 <= wavData.Length)
        {
            var chunkId = wavData.Slice(offset, 4);
            var chunkSize = BitConverter.ToInt32(wavData.Slice(offset + 4, 4));
            var chunkDataOffset = offset + 8;

            if (chunkSize < 0)
            {
                break;
            }

            if (chunkId.SequenceEqual(FmtHeader) && chunkSize >= 16 && chunkDataOffset + 16 <= wavData.Length)
            {
                formatTag = BitConverter.ToInt16(wavData.Slice(chunkDataOffset, 2));
                bitsPerSample = BitConverter.ToInt16(wavData.Slice(chunkDataOffset + 14, 2));
            }
            else if (chunkId.SequenceEqual(DataHeader))
            {
                dataOffset = chunkDataOffset;
                dataSize = Math.Min(chunkSize, wavData.Length - dataOffset);
            }

            var paddedSize = (chunkSize + 1) & ~1;
            offset += 8 + paddedSize;
            if (offset < 0)
            {
                break;
            }
        }

        if (dataOffset < 0 || dataSize <= 0)
        {
            return wavData.ToArray();
        }

        var result = wavData.ToArray();
        var factor = volumePercent / 100.0f;

        if (formatTag == 1 && bitsPerSample == 16) // 16-bit PCM
        {
            var end = dataOffset + dataSize;
            for (var i = dataOffset; i + 1 < end; i += 2)
            {
                short sample = BitConverter.ToInt16(result, i);
                var scaled = (int)Math.Round(sample * factor);
                var clamped = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
                result[i] = (byte)(clamped & 0xFF);
                result[i + 1] = (byte)((clamped >> 8) & 0xFF);
            }
        }
        else if (formatTag == 1 && bitsPerSample == 8) // 8-bit PCM
        {
            var end = dataOffset + dataSize;
            for (var i = dataOffset; i < end; i++)
            {
                var sample = result[i] - 128;
                var scaled = (int)Math.Round(sample * factor);
                result[i] = (byte)(Math.Clamp(scaled, -128, 127) + 128);
            }
        }
        else if (formatTag == 3 && bitsPerSample == 32) // 32-bit IEEE Float
        {
            var end = dataOffset + dataSize;
            for (var i = dataOffset; i + 3 < end; i += 4)
            {
                var sample = BitConverter.ToSingle(result, i);
                var scaled = sample * factor;
                var sampleBytes = BitConverter.GetBytes(scaled);
                sampleBytes.CopyTo(result, i);
            }
        }

        return result;
    }
}
