// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.Audio;

public sealed record DecodedPcm(float[] Samples, int Channels, int SampleRate);

/// <summary>Decodes bounded, interleaved PTADPCM WEM frames into PCM samples.</summary>
public static class PtAdpcmDecoder
{
    public static DecodedPcm Decode(byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is < 44 or > 32 * 1024 * 1024 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != bytes.Length - 8)
            throw new InvalidDataException("Cry media is not a complete RIFF container.");
        var chunks = new Dictionary<string, (int At, int Size)>();
        for (var at = 12; at < bytes.Length;)
        {
            if (at > bytes.Length - 8) throw new InvalidDataException("Cry media chunk is truncated.");
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)));
            if (size > bytes.Length - at - 8 || !chunks.TryAdd(System.Text.Encoding.ASCII.GetString(bytes, at, 4), (at + 8, size)))
                throw new InvalidDataException("Cry media chunk is invalid or duplicated.");
            at = checked(at + 8 + size + (size & 1));
        }
        if (!chunks.TryGetValue("fmt ", out var format) || format.Size < 16 || !chunks.TryGetValue("data", out var data))
            throw new InvalidDataException("Cry media format or samples are missing.");
        var fmt = bytes.AsSpan(format.At, format.Size);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]); var rate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]));
        var block = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
        if (BinaryPrimitives.ReadUInt16LittleEndian(fmt) != 0x8311 || BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]) != 4
            || channels is < 1 or > 2 || rate is < 6000 or > 192000 || block % channels != 0 || block / channels is not (0x24 or 0x104)
            || data.Size == 0 || data.Size % block != 0)
            throw new InvalidDataException("Cry media uses an unsupported PTADPCM layout.");
        var frameSize = block / channels; var frameSamples = 2 + 2 * (frameSize - 5);
        var sampleCount = checked(data.Size / block * frameSamples);
        if ((long)sampleCount * channels > 16_000_000) throw new InvalidDataException("Decoded cry exceeds its sample budget.");
        var samples = new float[sampleCount * channels];
        int[] magnitudes = [1, 3, 5, 7, 10, 14, 20, 28];
        int[] smallest = [0, 1, 2, 3, 5, 7, 10, 14];
        int[] adjustments = [-1, 0, 0, 0, 1, 1, 2, 2];
        for (var frame = 0; frame < data.Size / block; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var channel = 0; channel < channels; channel++)
            {
                var encoded = bytes.AsSpan(data.At + frame * block + channel * frameSize, frameSize);
                var older = (int)BinaryPrimitives.ReadInt16LittleEndian(encoded); var previous = (int)BinaryPrimitives.ReadInt16LittleEndian(encoded[2..]);
                var index = (int)encoded[4];
                if (index > 12) throw new InvalidDataException("Cry frame predictor index is invalid.");
                var first = frame * frameSamples * channels + channel;
                samples[first] = older / 32767f; samples[first + channels] = previous / 32767f;
                for (var sample = 2; sample < frameSamples; sample++)
                {
                    var nibble = (encoded[5 + (sample - 2) / 2] >> (((sample - 2) & 1) * 4)) & 15;
                    var magnitude = nibble >= 8 ? nibble - 8 : 7 - nibble;
                    var delta = index == 12 ? 0 : index == 0 ? smallest[magnitude] : magnitudes[magnitude] << (index - 1);
                    if (nibble < 8) delta = -delta;
                    var value = Math.Clamp(2 * previous - older + delta, short.MinValue, short.MaxValue);
                    index = index == 12 ? 0 : Math.Clamp(index + adjustments[magnitude], 0, 11);
                    samples[first + sample * channels] = value / 32767f;
                    older = previous; previous = value;
                }
            }
        }
        return new(samples, channels, rate);
    }
}
