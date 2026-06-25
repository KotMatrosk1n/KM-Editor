// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SwSh;

public enum SwShBseqParameterValueKind
{
    Unknown,
    Int,
    Bool,
    Float,
    String,
    Hex,
    ListInt,
    ListBool,
    ListFloat,
}

public sealed record SwShBseqParameterDefinition(
    string Name,
    IReadOnlyList<string> Aliases,
    SwShBseqParameterValueKind ValueKind,
    int ByteLength);

public sealed record SwShBseqCommandReferenceEntry(
    ulong Hash,
    string Name,
    IReadOnlyList<string> Aliases,
    int PayloadLength,
    IReadOnlyList<SwShBseqParameterDefinition> Parameters);

public static partial class SwShBseqCommandReference
{
    private static readonly Lazy<IReadOnlyList<SwShBseqCommandReferenceEntry>> CommandsLazy =
        new(CreateCommands);

    private static readonly Lazy<IReadOnlyDictionary<ulong, SwShBseqCommandReferenceEntry>> CommandsByHashLazy =
        new(() => Commands.ToDictionary(command => command.Hash));

    private static readonly Lazy<IReadOnlyDictionary<string, SwShBseqCommandReferenceEntry>> CommandsByNameLazy =
        new(CreateCommandsByName);

    private static readonly Lazy<IReadOnlyList<ulong>> GroupOptionHashesLazy =
        new(CreateGroupOptionHashes);

    public static IReadOnlyList<SwShBseqCommandReferenceEntry> Commands => CommandsLazy.Value;

    public static IReadOnlyDictionary<ulong, SwShBseqCommandReferenceEntry> CommandsByHash => CommandsByHashLazy.Value;

    public static IReadOnlyDictionary<string, SwShBseqCommandReferenceEntry> CommandsByName => CommandsByNameLazy.Value;

    public static IReadOnlyList<ulong> GroupOptionHashes => GroupOptionHashesLazy.Value;

    public static bool TryGetCommand(ulong hash, out SwShBseqCommandReferenceEntry command)
    {
        return CommandsByHash.TryGetValue(hash, out command!);
    }

    public static bool TryGetCommand(string name, out SwShBseqCommandReferenceEntry command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CommandsByName.TryGetValue(name, out command!);
    }

    public static SwShBseqCommandReferenceEntry GetCommand(ulong hash)
    {
        return TryGetCommand(hash, out var command)
            ? command
            : throw new InvalidDataException($"Unknown SwSh BSEQ command 0x{hash:X16}.");
    }

    public static string ToCommandId(ulong hash)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, hash);
        return Convert.ToHexString(bytes);
    }

    public static ulong ParseCommandId(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var bytes = Convert.FromHexString(commandId);
        if (bytes.Length != sizeof(ulong))
        {
            throw new InvalidDataException("BSEQ command id must be exactly 8 bytes.");
        }

        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    private static IReadOnlyDictionary<string, SwShBseqCommandReferenceEntry> CreateCommandsByName()
    {
        var commands = new Dictionary<string, SwShBseqCommandReferenceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in Commands)
        {
            commands.TryAdd(command.Name, command);
            foreach (var alias in command.Aliases)
            {
                commands.TryAdd(alias, command);
            }
        }

        return commands;
    }

    private static partial IReadOnlyList<SwShBseqCommandReferenceEntry> CreateCommands();

    private static partial IReadOnlyList<ulong> CreateGroupOptionHashes();
}
