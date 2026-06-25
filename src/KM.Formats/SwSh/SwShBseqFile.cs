// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShBseqCommandDefinition(
    ulong Hash,
    int PayloadLength);

public sealed record SwShBseqGroupOption(
    ulong Hash,
    uint Value,
    int Offset,
    int ValueOffset);

public sealed record SwShBseqCommand(
    int Offset,
    int StartFrameOffset,
    int EndFrameOffset,
    uint StartFrame,
    uint EndFrame,
    uint GroupNumber,
    IReadOnlyList<SwShBseqGroupOption> GroupOptions,
    ulong Hash,
    int HashOffset,
    int PayloadOffset,
    int PayloadLength);

public sealed record SwShBseqFile(
    uint Version,
    uint FrameCount,
    uint GroupOptionCount,
    IReadOnlyList<SwShBseqCommandDefinition> CommandDefinitions,
    IReadOnlyList<SwShBseqCommand> Commands)
{
    public const uint ExpectedVersion = 4;
    public const int FrameCountOffset = 0x0C;

    private const int HeaderSize = 0x18;
    private const int VersionOffset = 0x04;
    private const int GroupOptionCountOffset = 0x10;
    private const int CommandDefinitionCountOffset = 0x14;
    private const int CommandDefinitionSize = 0x0C;
    private const int CommandFrameHeaderSize = 0x0C;
    private const int GroupOptionSize = 0x0C;
    private const uint CommandTerminator = 0xFFFFFFFF;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SESD");

    public static SwShBseqFile Parse(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "BSEQ SESD header");
        if (!data[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid BSEQ SESD header.");
        }

        var version = ReadU32(data, VersionOffset);
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported SwSh BSEQ SESD version {version}."));
        }

        var frameCount = ReadU32(data, FrameCountOffset);
        var groupOptionCount = ReadU32(data, GroupOptionCountOffset);
        var commandDefinitionCount = ReadU32(data, CommandDefinitionCountOffset);
        var commandTableEnd = HeaderSize + (commandDefinitionCount * (long)CommandDefinitionSize);
        EnsureRange(data, HeaderSize, commandTableEnd - HeaderSize, "BSEQ command definition table");

        var commandDefinitionCountInt = CheckedInt32(commandDefinitionCount, "BSEQ command definition count");
        var groupOptionCountInt = CheckedInt32(groupOptionCount, "BSEQ group option count");

        var definitions = new List<SwShBseqCommandDefinition>(commandDefinitionCountInt);
        var payloadLengths = new Dictionary<ulong, int>(definitions.Capacity);
        for (var index = 0; index < commandDefinitionCountInt; index++)
        {
            var offset = HeaderSize + (index * CommandDefinitionSize);
            var hash = ReadU64(data, offset);
            var payloadLength = CheckedInt32(ReadU32(data, offset + sizeof(ulong)), "BSEQ command payload length");
            if (!payloadLengths.TryAdd(hash, payloadLength))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"Duplicate BSEQ command definition 0x{hash:X16}."));
            }

            definitions.Add(new SwShBseqCommandDefinition(hash, payloadLength));
        }

        var commands = new List<SwShBseqCommand>();
        var commandOffset = CheckedInt32(commandTableEnd, "BSEQ command list offset");
        var optionBytes = groupOptionCount * (long)GroupOptionSize;
        while (commandOffset < data.Length)
        {
            EnsureRange(data, commandOffset, sizeof(uint), "BSEQ command terminator");
            var startFrame = ReadU32(data, commandOffset);
            if (startFrame == CommandTerminator)
            {
                return new SwShBseqFile(version, frameCount, groupOptionCount, definitions, commands);
            }

            var commandHeaderLength = CommandFrameHeaderSize + optionBytes + sizeof(ulong);
            EnsureRange(data, commandOffset, commandHeaderLength, "BSEQ command header");

            var endFrameOffset = commandOffset + sizeof(uint);
            var endFrame = ReadU32(data, endFrameOffset);
            var groupNumber = ReadU32(data, commandOffset + sizeof(uint) * 2);
            var groupOptions = new List<SwShBseqGroupOption>(groupOptionCountInt);
            var cursor = commandOffset + CommandFrameHeaderSize;
            for (var optionIndex = 0; optionIndex < groupOptionCountInt; optionIndex++)
            {
                var optionHash = ReadU64(data, cursor);
                var optionValueOffset = cursor + sizeof(ulong);
                var optionValue = ReadU32(data, optionValueOffset);
                groupOptions.Add(new SwShBseqGroupOption(optionHash, optionValue, cursor, optionValueOffset));
                cursor += GroupOptionSize;
            }

            var commandHashOffset = cursor;
            var commandHash = ReadU64(data, commandHashOffset);
            if (!payloadLengths.TryGetValue(commandHash, out var payloadLength))
            {
                throw new InvalidDataException(
                    string.Create(CultureInfo.InvariantCulture, $"Unknown BSEQ command hash 0x{commandHash:X16}."));
            }

            var payloadOffset = commandHashOffset + sizeof(ulong);
            EnsureRange(data, payloadOffset, payloadLength, "BSEQ command payload");
            commands.Add(
                new SwShBseqCommand(
                    commandOffset,
                    commandOffset,
                    endFrameOffset,
                    startFrame,
                    endFrame,
                    groupNumber,
                    groupOptions,
                    commandHash,
                    commandHashOffset,
                    payloadOffset,
                    payloadLength));
            commandOffset = payloadOffset + payloadLength;
        }

        throw new InvalidDataException("BSEQ command terminator was not found.");
    }

    public IReadOnlyList<SwShBseqCommand> FindCommands(ulong hash)
    {
        return Commands.Where(command => command.Hash == hash).ToArray();
    }

    public SwShBseqCommand GetSingleCommand(ulong hash, string commandName)
    {
        var matches = FindCommands(hash);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException($"{commandName} BSEQ command was not found."),
            _ => throw new InvalidDataException($"{commandName} BSEQ command is ambiguous."),
        };
    }

    public static int ReadInt32Parameter(ReadOnlySpan<byte> data, SwShBseqCommand command, int parameterIndex)
    {
        var offset = GetInt32ParameterOffset(data, command, parameterIndex);
        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
    }

    public static void WriteInt32Parameter(Span<byte> data, SwShBseqCommand command, int parameterIndex, int value)
    {
        var offset = GetInt32ParameterOffset(data, command, parameterIndex);
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, sizeof(int)), value);
    }

    private static int GetInt32ParameterOffset(ReadOnlySpan<byte> data, SwShBseqCommand command, int parameterIndex)
    {
        if (parameterIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameterIndex), "BSEQ parameter index must not be negative.");
        }

        var offset = command.PayloadOffset + (parameterIndex * (long)sizeof(int));
        EnsureRange(data, offset, sizeof(int), "BSEQ command int parameter");
        if (offset + sizeof(int) > command.PayloadOffset + command.PayloadLength)
        {
            throw new InvalidDataException("BSEQ command int parameter exceeds the command payload.");
        }

        return CheckedInt32(offset, "BSEQ command int parameter offset");
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
    }

    private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
    }

    private static int CheckedInt32(uint value, string description)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"{description} is too large.");
        }

        return (int)value;
    }

    private static int CheckedInt32(long value, string description)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"{description} is too large.");
        }

        return (int)value;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, long offset, long length, string description)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException($"{description} is truncated.");
        }
    }
}
