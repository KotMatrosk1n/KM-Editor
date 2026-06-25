// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShBseqInspection(
    uint Version,
    uint FrameCount,
    uint GroupOptionCount,
    int CommandDefinitionCount,
    int CommandCount,
    int KnownCommandCount,
    IReadOnlyList<SwShBseqCommandInspection> Commands);

public sealed record SwShBseqCommandInspection(
    int Index,
    string CommandId,
    string Name,
    bool IsKnown,
    uint StartFrame,
    uint EndFrame,
    uint GroupNumber,
    int PayloadOffset,
    int PayloadLength,
    IReadOnlyList<SwShBseqParameterInspection> Parameters);

public sealed record SwShBseqParameterInspection(
    int Index,
    string Name,
    string Kind,
    string Value);

public static class SwShBseqInspector
{
    public static SwShBseqInspection Inspect(ReadOnlySpan<byte> data)
    {
        var file = SwShBseqFile.Parse(data);
        var commands = new SwShBseqCommandInspection[file.Commands.Count];
        for (var index = 0; index < file.Commands.Count; index++)
        {
            commands[index] = InspectCommand(data, file.Commands[index], index);
        }

        return new SwShBseqInspection(
            file.Version,
            file.FrameCount,
            file.GroupOptionCount,
            file.CommandDefinitions.Count,
            file.Commands.Count,
            commands.Count(command => command.IsKnown),
            commands);
    }

    private static SwShBseqCommandInspection InspectCommand(
        ReadOnlySpan<byte> data,
        SwShBseqCommand command,
        int index)
    {
        var isKnown = SwShBseqCommandReference.TryGetCommand(command.Hash, out var reference);
        var parameters = isKnown
            && reference.PayloadLength == command.PayloadLength
            && reference.Parameters.Sum(parameter => parameter.ByteLength) == command.PayloadLength
                ? InspectParameters(data.Slice(command.PayloadOffset, command.PayloadLength), reference)
                : [];

        return new SwShBseqCommandInspection(
            index,
            SwShBseqCommandReference.ToCommandId(command.Hash),
            isKnown ? reference.Name : $"Unknown 0x{command.Hash:X16}",
            isKnown,
            command.StartFrame,
            command.EndFrame,
            command.GroupNumber,
            command.PayloadOffset,
            command.PayloadLength,
            parameters);
    }

    private static IReadOnlyList<SwShBseqParameterInspection> InspectParameters(
        ReadOnlySpan<byte> payload,
        SwShBseqCommandReferenceEntry reference)
    {
        var parameters = new List<SwShBseqParameterInspection>(reference.Parameters.Count);
        var offset = 0;
        for (var index = 0; index < reference.Parameters.Count; index++)
        {
            var parameter = reference.Parameters[index];
            var parameterPayload = payload.Slice(offset, parameter.ByteLength);
            parameters.Add(new SwShBseqParameterInspection(
                index,
                parameter.Name,
                parameter.ValueKind.ToString(),
                FormatValue(parameterPayload, parameter)));
            offset += parameter.ByteLength;
        }

        return parameters;
    }

    private static string FormatValue(ReadOnlySpan<byte> payload, SwShBseqParameterDefinition parameter)
    {
        return parameter.ValueKind switch
        {
            SwShBseqParameterValueKind.Int => BinaryPrimitives.ReadInt32LittleEndian(payload).ToString(CultureInfo.InvariantCulture),
            SwShBseqParameterValueKind.Bool => (BinaryPrimitives.ReadInt32LittleEndian(payload) != 0).ToString(CultureInfo.InvariantCulture),
            SwShBseqParameterValueKind.Float => BinaryPrimitives.ReadSingleLittleEndian(payload).ToString("R", CultureInfo.InvariantCulture),
            SwShBseqParameterValueKind.String => Encoding.UTF8.GetString(payload).TrimEnd('\0'),
            SwShBseqParameterValueKind.Hex => Convert.ToHexString(payload),
            SwShBseqParameterValueKind.ListInt => string.Join(", ", ReadIntValues(payload)),
            SwShBseqParameterValueKind.ListBool => string.Join(", ", ReadBoolValues(payload)),
            SwShBseqParameterValueKind.ListFloat => string.Join(", ", ReadFloatValues(payload)),
            _ => Convert.ToHexString(payload),
        };
    }

    private static string[] ReadIntValues(ReadOnlySpan<byte> payload)
    {
        var values = new string[payload.Length / sizeof(int)];
        for (var offset = 0; offset < payload.Length; offset += sizeof(int))
        {
            values[offset / sizeof(int)] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int))).ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string[] ReadBoolValues(ReadOnlySpan<byte> payload)
    {
        var values = new string[payload.Length / sizeof(int)];
        for (var offset = 0; offset < payload.Length; offset += sizeof(int))
        {
            values[offset / sizeof(int)] = (BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int))) != 0)
                .ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string[] ReadFloatValues(ReadOnlySpan<byte> payload)
    {
        var values = new string[payload.Length / sizeof(float)];
        for (var offset = 0; offset < payload.Length; offset += sizeof(float))
        {
            values[offset / sizeof(float)] = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, sizeof(float))).ToString("R", CultureInfo.InvariantCulture);
        }

        return values;
    }
}
