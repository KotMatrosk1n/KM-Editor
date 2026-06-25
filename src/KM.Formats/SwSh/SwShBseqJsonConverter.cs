// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KM.Formats.SwSh;

public static class SwShBseqJsonConverter
{
    private const string FormatName = "KM.SwSh.BSEQ";

    public static string ExportToJson(ReadOnlySpan<byte> data, JsonSerializerOptions? options = null)
    {
        var file = SwShBseqFile.Parse(data);
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = file.Version,
            ["frameCount"] = file.FrameCount,
            ["groupOptionCount"] = file.GroupOptionCount,
        };

        var definitions = new JsonArray();
        foreach (var definition in file.CommandDefinitions)
        {
            var knownName = SwShBseqCommandReference.TryGetCommand(definition.Hash, out var known)
                ? known.Name
                : null;
            definitions.Add(new JsonObject
            {
                ["commandId"] = SwShBseqCommandReference.ToCommandId(definition.Hash),
                ["name"] = knownName,
                ["payloadBytes"] = definition.PayloadLength,
            });
        }

        root["commandDefinitions"] = definitions;

        var commands = new JsonArray();
        foreach (var command in file.Commands)
        {
            commands.Add(ExportCommand(data, command));
        }

        root["commands"] = commands;

        var serializerOptions = options ?? CreateDefaultOptions();
        return root.ToJsonString(serializerOptions);
    }

    public static byte[] ImportFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("BSEQ JSON root must be an object.");

        var version = ReadUInt32(root, "version", SwShBseqFile.ExpectedVersion);
        if (version != SwShBseqFile.ExpectedVersion)
        {
            throw new InvalidDataException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported SwSh BSEQ JSON version {version}."));
        }

        var frameCount = ReadUInt32(root, "frameCount", 0);
        var commandNodes = root["commands"]?.AsArray()
            ?? throw new InvalidDataException("BSEQ JSON must contain a commands array.");
        var groupOptionCount = ReadUInt32(root, "groupOptionCount", InferGroupOptionCount(commandNodes));
        var definitions = ReadCommandDefinitions(root, commandNodes);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("SESD"));
        writer.Write(version);
        writer.Write(0u);
        writer.Write(frameCount);
        writer.Write(groupOptionCount);
        writer.Write((uint)definitions.Count);
        foreach (var definition in definitions)
        {
            writer.Write(definition.Hash);
            writer.Write((uint)definition.PayloadLength);
        }

        foreach (var commandNode in commandNodes)
        {
            var command = commandNode?.AsObject()
                ?? throw new InvalidDataException("BSEQ command entries must be objects.");
            var hash = ReadCommandHash(command);
            var payload = EncodePayload(command, hash, definitions);

            writer.Write(ReadUInt32(command, "startFrame", 0));
            writer.Write(ReadUInt32(command, "endFrame", 0));
            writer.Write(ReadUInt32(command, "groupNumber", 0));
            WriteGroupOptions(writer, command, groupOptionCount);
            writer.Write(hash);
            writer.Write(payload);
        }

        writer.Write(0xFFFFFFFFu);
        writer.Flush();
        return stream.ToArray();
    }

    public static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
        };
    }

    private static JsonObject ExportCommand(ReadOnlySpan<byte> data, SwShBseqCommand command)
    {
        var commandObject = new JsonObject
        {
            ["commandId"] = SwShBseqCommandReference.ToCommandId(command.Hash),
            ["startFrame"] = command.StartFrame,
            ["endFrame"] = command.EndFrame,
            ["groupNumber"] = command.GroupNumber,
        };

        if (SwShBseqCommandReference.TryGetCommand(command.Hash, out var reference))
        {
            commandObject["name"] = reference.Name;
        }

        var groupOptions = new JsonArray();
        foreach (var option in command.GroupOptions)
        {
            groupOptions.Add(new JsonObject
            {
                ["optionId"] = SwShBseqCommandReference.ToCommandId(option.Hash),
                ["value"] = option.Value,
            });
        }

        commandObject["groupOptions"] = groupOptions;

        if (reference is not null
            && reference.PayloadLength == command.PayloadLength
            && reference.Parameters.Sum(parameter => parameter.ByteLength) == command.PayloadLength)
        {
            commandObject["parameters"] = ExportParameters(data.Slice(command.PayloadOffset, command.PayloadLength), reference);
        }
        else
        {
            commandObject["rawPayload"] = Convert.ToHexString(data.Slice(command.PayloadOffset, command.PayloadLength));
        }

        return commandObject;
    }

    private static JsonArray ExportParameters(ReadOnlySpan<byte> payload, SwShBseqCommandReferenceEntry reference)
    {
        var parameters = new JsonArray();
        var offset = 0;
        foreach (var parameter in reference.Parameters)
        {
            var parameterPayload = payload.Slice(offset, parameter.ByteLength);
            parameters.Add(new JsonObject
            {
                ["name"] = parameter.Name,
                ["kind"] = ToJsonKind(parameter.ValueKind),
                ["value"] = DecodeValue(parameterPayload, parameter),
            });
            offset += parameter.ByteLength;
        }

        return parameters;
    }

    private static JsonNode? DecodeValue(ReadOnlySpan<byte> payload, SwShBseqParameterDefinition parameter)
    {
        return parameter.ValueKind switch
        {
            SwShBseqParameterValueKind.Int => BinaryPrimitives.ReadInt32LittleEndian(payload),
            SwShBseqParameterValueKind.Bool => BinaryPrimitives.ReadInt32LittleEndian(payload) != 0,
            SwShBseqParameterValueKind.Float => BinaryPrimitives.ReadSingleLittleEndian(payload),
            SwShBseqParameterValueKind.String => Encoding.UTF8.GetString(payload).TrimEnd('\0'),
            SwShBseqParameterValueKind.Hex => Convert.ToHexString(payload),
            SwShBseqParameterValueKind.ListInt => DecodeIntArray(payload),
            SwShBseqParameterValueKind.ListBool => DecodeBoolArray(payload),
            SwShBseqParameterValueKind.ListFloat => DecodeFloatArray(payload),
            _ => Convert.ToHexString(payload),
        };
    }

    private static JsonArray DecodeIntArray(ReadOnlySpan<byte> payload)
    {
        var values = new JsonArray();
        for (var offset = 0; offset < payload.Length; offset += sizeof(int))
        {
            values.Add(BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int))));
        }

        return values;
    }

    private static JsonArray DecodeBoolArray(ReadOnlySpan<byte> payload)
    {
        var values = new JsonArray();
        for (var offset = 0; offset < payload.Length; offset += sizeof(int))
        {
            values.Add(BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int))) != 0);
        }

        return values;
    }

    private static JsonArray DecodeFloatArray(ReadOnlySpan<byte> payload)
    {
        var values = new JsonArray();
        for (var offset = 0; offset < payload.Length; offset += sizeof(float))
        {
            values.Add(BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, sizeof(float))));
        }

        return values;
    }

    private static IReadOnlyList<SwShBseqCommandDefinition> ReadCommandDefinitions(
        JsonObject root,
        JsonArray commandNodes)
    {
        var definitions = new Dictionary<ulong, SwShBseqCommandDefinition>();
        if (root["commandDefinitions"] is JsonArray definitionNodes)
        {
            foreach (var definitionNode in definitionNodes)
            {
                var definition = definitionNode?.AsObject()
                    ?? throw new InvalidDataException("BSEQ command definition entries must be objects.");
                var hash = SwShBseqCommandReference.ParseCommandId(ReadRequiredString(definition, "commandId"));
                var payloadBytes = ReadInt32(definition, "payloadBytes");
                definitions[hash] = new SwShBseqCommandDefinition(hash, payloadBytes);
            }
        }

        foreach (var commandNode in commandNodes)
        {
            var command = commandNode?.AsObject()
                ?? throw new InvalidDataException("BSEQ command entries must be objects.");
            var hash = ReadCommandHash(command);
            if (definitions.ContainsKey(hash))
            {
                continue;
            }

            var payloadLength = ResolvePayloadLength(command, hash);
            definitions.Add(hash, new SwShBseqCommandDefinition(hash, payloadLength));
        }

        return definitions.Values.ToArray();
    }

    private static byte[] EncodePayload(
        JsonObject command,
        ulong hash,
        IReadOnlyList<SwShBseqCommandDefinition> definitions)
    {
        if (command["parameters"] is JsonArray parameterNodes
            && SwShBseqCommandReference.TryGetCommand(hash, out var reference)
            && reference.Parameters.Sum(parameter => parameter.ByteLength) == reference.PayloadLength)
        {
            return EncodeParameters(parameterNodes, reference);
        }

        if (command["rawPayload"] is JsonNode rawPayloadNode)
        {
            return Convert.FromHexString(rawPayloadNode.GetValue<string>());
        }

        var payloadLength = definitions.First(definition => definition.Hash == hash).PayloadLength;
        return new byte[payloadLength];
    }

    private static byte[] EncodeParameters(JsonArray parameterNodes, SwShBseqCommandReferenceEntry reference)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        for (var index = 0; index < reference.Parameters.Count; index++)
        {
            var parameter = reference.Parameters[index];
            var parameterNode = index < parameterNodes.Count
                ? parameterNodes[index]?.AsObject()
                : null;
            var value = parameterNode?["value"];
            WriteParameterValue(writer, parameter, value);
        }

        writer.Flush();
        var payload = stream.ToArray();
        if (payload.Length != reference.PayloadLength)
        {
            throw new InvalidDataException($"{reference.Name} encoded payload length did not match the command reference.");
        }

        return payload;
    }

    private static void WriteParameterValue(BinaryWriter writer, SwShBseqParameterDefinition parameter, JsonNode? value)
    {
        switch (parameter.ValueKind)
        {
            case SwShBseqParameterValueKind.Int:
            case SwShBseqParameterValueKind.Unknown:
                writer.Write(ReadIntValue(value));
                break;
            case SwShBseqParameterValueKind.Bool:
                writer.Write(ReadBoolValue(value) ? 1 : 0);
                break;
            case SwShBseqParameterValueKind.Float:
                writer.Write(ReadFloatValue(value));
                break;
            case SwShBseqParameterValueKind.String:
                WriteFixedString(writer, value?.GetValue<string>() ?? string.Empty, parameter.ByteLength);
                break;
            case SwShBseqParameterValueKind.Hex:
                WriteFixedBytes(writer, Convert.FromHexString(value?.GetValue<string>() ?? string.Empty), parameter.ByteLength);
                break;
            case SwShBseqParameterValueKind.ListInt:
                WriteIntArray(writer, value as JsonArray, parameter.ByteLength);
                break;
            case SwShBseqParameterValueKind.ListBool:
                WriteBoolArray(writer, value as JsonArray, parameter.ByteLength);
                break;
            case SwShBseqParameterValueKind.ListFloat:
                WriteFloatArray(writer, value as JsonArray, parameter.ByteLength);
                break;
            default:
                WriteFixedBytes(writer, [], parameter.ByteLength);
                break;
        }
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteLength)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteFixedBytes(writer, bytes, byteLength);
    }

    private static void WriteFixedBytes(BinaryWriter writer, byte[] bytes, int byteLength)
    {
        if (bytes.Length > byteLength)
        {
            throw new InvalidDataException("BSEQ parameter value is longer than the parameter byte length.");
        }

        writer.Write(bytes);
        for (var index = bytes.Length; index < byteLength; index++)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteIntArray(BinaryWriter writer, JsonArray? values, int byteLength)
    {
        var count = byteLength / sizeof(int);
        for (var index = 0; index < count; index++)
        {
            writer.Write(ReadIntValue(values is not null && index < values.Count ? values[index] : null));
        }
    }

    private static void WriteBoolArray(BinaryWriter writer, JsonArray? values, int byteLength)
    {
        var count = byteLength / sizeof(int);
        for (var index = 0; index < count; index++)
        {
            writer.Write(ReadBoolValue(values is not null && index < values.Count ? values[index] : null) ? 1 : 0);
        }
    }

    private static void WriteFloatArray(BinaryWriter writer, JsonArray? values, int byteLength)
    {
        var count = byteLength / sizeof(float);
        for (var index = 0; index < count; index++)
        {
            writer.Write(ReadFloatValue(values is not null && index < values.Count ? values[index] : null));
        }
    }

    private static void WriteGroupOptions(BinaryWriter writer, JsonObject command, uint groupOptionCount)
    {
        var optionNodes = command["groupOptions"] as JsonArray;
        for (var index = 0; index < groupOptionCount; index++)
        {
            var option = optionNodes is not null && index < optionNodes.Count
                ? optionNodes[index]?.AsObject()
                : null;
            var hash = option is not null && option["optionId"] is not null
                ? SwShBseqCommandReference.ParseCommandId(ReadRequiredString(option, "optionId"))
                : DefaultGroupOptionHash(index);
            var value = option is not null
                ? ReadUInt32(option, "value", 0)
                : 0u;
            writer.Write(hash);
            writer.Write(value);
        }
    }

    private static ulong DefaultGroupOptionHash(int index)
    {
        return index < SwShBseqCommandReference.GroupOptionHashes.Count
            ? SwShBseqCommandReference.GroupOptionHashes[index]
            : 0ul;
    }

    private static uint InferGroupOptionCount(JsonArray commands)
    {
        var max = 0;
        foreach (var command in commands)
        {
            if (command?.AsObject()["groupOptions"] is JsonArray groupOptions)
            {
                max = Math.Max(max, groupOptions.Count);
            }
        }

        return (uint)max;
    }

    private static int ResolvePayloadLength(JsonObject command, ulong hash)
    {
        if (command["rawPayload"] is JsonNode rawPayloadNode)
        {
            return Convert.FromHexString(rawPayloadNode.GetValue<string>()).Length;
        }

        if (SwShBseqCommandReference.TryGetCommand(hash, out var reference))
        {
            return reference.PayloadLength;
        }

        throw new InvalidDataException("Unknown BSEQ command requires rawPayload or a command definition.");
    }

    private static ulong ReadCommandHash(JsonObject command)
    {
        if (command["commandId"] is JsonNode commandIdNode)
        {
            return SwShBseqCommandReference.ParseCommandId(commandIdNode.GetValue<string>());
        }

        if (command["name"] is JsonNode nameNode
            && SwShBseqCommandReference.TryGetCommand(nameNode.GetValue<string>(), out var reference))
        {
            return reference.Hash;
        }

        throw new InvalidDataException("BSEQ command must include commandId or a known command name.");
    }

    private static string ReadRequiredString(JsonObject value, string propertyName)
    {
        return value[propertyName]?.GetValue<string>()
            ?? throw new InvalidDataException($"BSEQ JSON is missing {propertyName}.");
    }

    private static int ReadInt32(JsonObject value, string propertyName)
    {
        return value[propertyName]?.GetValue<int>()
            ?? throw new InvalidDataException($"BSEQ JSON is missing {propertyName}.");
    }

    private static uint ReadUInt32(JsonObject value, string propertyName, uint defaultValue)
    {
        return value[propertyName] is JsonNode node
            ? node.GetValue<uint>()
            : defaultValue;
    }

    private static int ReadIntValue(JsonNode? value)
    {
        return value is null
            ? 0
            : value.GetValue<int>();
    }

    private static bool ReadBoolValue(JsonNode? value)
    {
        if (value is null)
        {
            return false;
        }

        var element = value.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetInt32() != 0,
            _ => throw new InvalidDataException("BSEQ bool parameter must be true, false, 0, or 1."),
        };
    }

    private static float ReadFloatValue(JsonNode? value)
    {
        return value is null
            ? 0.0f
            : value.GetValue<float>();
    }

    private static string ToJsonKind(SwShBseqParameterValueKind kind)
    {
        return kind switch
        {
            SwShBseqParameterValueKind.Int => "int",
            SwShBseqParameterValueKind.Bool => "bool",
            SwShBseqParameterValueKind.Float => "float",
            SwShBseqParameterValueKind.String => "string",
            SwShBseqParameterValueKind.Hex => "hex",
            SwShBseqParameterValueKind.ListInt => "listInt",
            SwShBseqParameterValueKind.ListBool => "listBool",
            SwShBseqParameterValueKind.ListFloat => "listFloat",
            _ => "unknown",
        };
    }
}
