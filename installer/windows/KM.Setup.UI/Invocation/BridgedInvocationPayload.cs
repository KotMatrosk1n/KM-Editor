// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Setup.UI.Invocation;

internal static class BridgedInvocationPayload
{
    private const int MaximumDecodedBytes = 16 * 1024;
    private const int MaximumBase64Characters = ((MaximumDecodedBytes + 2) / 3) * 4;
    private const uint MaximumArgumentCount = 256;
    private const uint SupportedVersion = 1;
    private static readonly byte[] Magic = "KMAR"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryDecode(string base64, out IReadOnlyList<string> arguments)
    {
        arguments = Array.Empty<string>();
        if (base64.Length > MaximumBase64Characters)
        {
            return false;
        }

        byte[] payload;

        try
        {
            payload = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length is < 12 or > MaximumDecodedBytes || !payload.AsSpan(0, 4).SequenceEqual(Magic))
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8, 4));
        if (version != SupportedVersion || count > MaximumArgumentCount)
        {
            return false;
        }

        var decoded = new List<string>((int)count);
        var offset = 12;

        try
        {
            for (var index = 0U; index < count; index++)
            {
                if (offset > payload.Length - sizeof(uint))
                {
                    return false;
                }

                var byteLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, sizeof(uint)));
                offset += sizeof(uint);
                if (byteLength > int.MaxValue || offset > payload.Length - (int)byteLength)
                {
                    return false;
                }

                var argument = StrictUtf8.GetString(payload, offset, (int)byteLength);
                if (argument.Contains('\0'))
                {
                    return false;
                }

                decoded.Add(argument);
                offset += (int)byteLength;
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (offset != payload.Length)
        {
            return false;
        }

        arguments = decoded;
        return true;
    }
}
