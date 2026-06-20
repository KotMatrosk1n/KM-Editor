// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsFlatBufferAnimation
{
    public static uint GetFrameRate(byte[] data)
    {
        var frameRateOffset = GetInfoFieldOffset(data, 2);
        return frameRateOffset < 0 ? 0 : ReadU32(data, frameRateOffset);
    }

    public static uint GetKeyFrames(byte[] data)
    {
        var keyFrameOffset = GetInfoFieldOffset(data, 1);
        return keyFrameOffset < 0 ? 0 : ReadU32(data, keyFrameOffset);
    }

    public static void SetKeyFrames(byte[] data, uint keyFrames)
    {
        var keyFrameOffset = GetInfoFieldOffset(data, 1);
        if (keyFrameOffset < 0)
        {
            throw new InvalidDataException("GF animation KeyFrames field not found.");
        }

        WriteU32(data, keyFrameOffset, keyFrames);
    }

    public static void SetFrameRate(byte[] data, uint frameRate)
    {
        var frameRateOffset = GetInfoFieldOffset(data, 2);
        if (frameRateOffset < 0)
        {
            throw new InvalidDataException("GF animation FrameRate field not found.");
        }

        WriteU32(data, frameRateOffset, frameRate);
    }

    private static int GetInfoFieldOffset(byte[] data, int infoFieldIndex)
    {
        var root = ReadUOffset(data, 0);
        var infoOffset = GetTableFieldOffset(data, root, 0);
        if (infoOffset == 0)
        {
            return -1;
        }

        var infoTable = checked(root + infoOffset + ReadUOffset(data, root + infoOffset));
        var fieldOffset = GetTableFieldOffset(data, infoTable, infoFieldIndex);
        return fieldOffset == 0 ? -1 : infoTable + fieldOffset;
    }

    private static int GetTableFieldOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        if (tableOffset < sizeof(int) || tableOffset + sizeof(int) > data.Length)
        {
            return 0;
        }

        var vtableOffset = tableOffset - ReadI32(data, tableOffset);
        if (vtableOffset < 0 || vtableOffset + sizeof(ushort) * 2 > data.Length)
        {
            return 0;
        }

        var vtableLength = ReadU16(data, vtableOffset);
        var entryOffset = 4 + fieldIndex * 2;
        if (entryOffset + 2 > vtableLength || vtableOffset + entryOffset + 2 > data.Length)
        {
            return 0;
        }

        return ReadU16(data, vtableOffset + entryOffset);
    }

    private static int ReadUOffset(byte[] data, int offset)
    {
        return checked((int)ReadU32(data, offset));
    }

    private static int ReadI32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }
}
