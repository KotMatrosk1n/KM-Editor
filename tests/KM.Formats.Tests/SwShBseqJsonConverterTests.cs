// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShBseqJsonConverterTests
{
    [Fact]
    public void CommandReferenceIncludesSpecialQuizResultAndGroupOptions()
    {
        Assert.Equal(385, SwShBseqCommandReference.Commands.Count);
        Assert.Equal(32, SwShBseqCommandReference.GroupOptionHashes.Count);

        var command = SwShBseqCommandReference.GetCommand(SwShBseqKnownCommands.SpecialQuizResult);

        Assert.Equal("SpecialQuizResult", command.Name);
        Assert.Equal(24, command.PayloadLength);
        Assert.Collection(
            command.Parameters,
            parameter => Assert.Equal("type1", parameter.Name),
            parameter => Assert.Equal("result1", parameter.Name),
            parameter => Assert.Equal("type2", parameter.Name),
            parameter => Assert.Equal("result2", parameter.Name),
            parameter => Assert.Equal("type3", parameter.Name),
            parameter => Assert.Equal("result3", parameter.Name));
    }

    [Fact]
    public void CommandReferenceCoversHarvestedPbseqtoolMetadata()
    {
        Assert.Equal(385, SwShBseqCommandReference.Commands.Count);
        Assert.Equal(32, SwShBseqCommandReference.GroupOptionHashes.Count);
        Assert.Equal(
            SwShBseqCommandReference.Commands.Count,
            SwShBseqCommandReference.Commands.Select(command => command.Hash).Distinct().Count());
        Assert.Equal(
            SwShBseqCommandReference.GroupOptionHashes.Count,
            SwShBseqCommandReference.GroupOptionHashes.Distinct().Count());

        foreach (var command in SwShBseqCommandReference.Commands)
        {
            Assert.Same(command, SwShBseqCommandReference.CommandsByHash[command.Hash]);
            Assert.Equal(command.PayloadLength, command.Parameters.Sum(parameter => parameter.ByteLength));
            Assert.Equal(command.Hash, SwShBseqCommandReference.ParseCommandId(SwShBseqCommandReference.ToCommandId(command.Hash)));

            Assert.True(SwShBseqCommandReference.TryGetCommand(command.Name, out var byName));
            Assert.Same(command, byName);
            foreach (var alias in command.Aliases)
            {
                Assert.True(SwShBseqCommandReference.TryGetCommand(alias, out var byAlias));
                Assert.Same(command, byAlias);
            }

            foreach (var parameter in command.Parameters)
            {
                Assert.Contains(parameter.Name, parameter.Aliases);
                Assert.True(parameter.ByteLength >= 0);
            }
        }

        foreach (var groupOptionHash in SwShBseqCommandReference.GroupOptionHashes)
        {
            Assert.Equal(
                groupOptionHash,
                SwShBseqCommandReference.ParseCommandId(SwShBseqCommandReference.ToCommandId(groupOptionHash)));
        }

        Assert.True(SwShBseqCommandReference.TryGetCommand("SpecialChainAttackDefine", out var commandAlias));
        Assert.Equal("SpecialChainAttakDefine", commandAlias.Name);

        var shadowBoxSet = SwShBseqCommandReference.GetCommand(0x86DB5E4CC1E40197);
        var relative = Assert.Single(
            shadowBoxSet.Parameters,
            parameter => parameter.Aliases.Contains("relative", StringComparer.Ordinal));
        Assert.Equal("rerative", relative.Name);
        Assert.Equal(SwShBseqParameterValueKind.Bool, relative.ValueKind);
    }

    [Fact]
    public void ExportToJsonDecodesKnownCommandParameters()
    {
        var bseq = CreateSpecialQuizResultBseq();

        var json = SwShBseqJsonConverter.ExportToJson(bseq);
        var root = JsonNode.Parse(json)!.AsObject();
        var command = root["commands"]!.AsArray()[0]!.AsObject();
        var parameters = command["parameters"]!.AsArray();

        Assert.Equal("SpecialQuizResult", command["name"]!.GetValue<string>());
        Assert.Equal("type1", parameters[0]!["name"]!.GetValue<string>());
        Assert.Equal(6, parameters[0]!["value"]!.GetValue<int>());
        Assert.Equal("result1", parameters[1]!["name"]!.GetValue<string>());
        Assert.Equal(2, parameters[1]!["value"]!.GetValue<int>());
    }

    [Fact]
    public void InspectSummarizesKnownCommandParametersForEditorSurfaces()
    {
        var inspection = SwShBseqInspector.Inspect(CreateSpecialQuizResultBseq());

        Assert.Equal(1, inspection.CommandCount);
        Assert.Equal(1, inspection.KnownCommandCount);
        var command = Assert.Single(inspection.Commands);
        Assert.Equal("SpecialQuizResult", command.Name);
        Assert.Equal("6", command.Parameters[0].Value);
        Assert.Equal("2", command.Parameters[1].Value);
    }

    [Fact]
    public void ImportFromJsonAppliesEditedKnownCommandParameters()
    {
        var root = JsonNode.Parse(SwShBseqJsonConverter.ExportToJson(CreateSpecialQuizResultBseq()))!.AsObject();
        var command = root["commands"]!.AsArray()[0]!.AsObject();
        command["parameters"]!.AsArray()[0]!["value"] = 5;
        command["parameters"]!.AsArray()[1]!["value"] = 1;

        var imported = SwShBseqJsonConverter.ImportFromJson(root.ToJsonString());
        var file = SwShBseqFile.Parse(imported);
        var specialQuiz = Assert.Single(file.Commands);

        Assert.Equal(5, SwShBseqFile.ReadInt32Parameter(imported, specialQuiz, 0));
        Assert.Equal(1, SwShBseqFile.ReadInt32Parameter(imported, specialQuiz, 1));
    }

    [Fact]
    public void ImportFromJsonResolvesKnownCommandByNameWhenCommandIdIsOmitted()
    {
        var bseq = CreateSpecialQuizResultBseq();
        var root = JsonNode.Parse(SwShBseqJsonConverter.ExportToJson(bseq))!.AsObject();
        root.Remove("commandDefinitions");
        var command = root["commands"]!.AsArray()[0]!.AsObject();
        command.Remove("commandId");

        var imported = SwShBseqJsonConverter.ImportFromJson(root.ToJsonString());

        Assert.Equal(bseq, imported);
    }

    [Fact]
    public void UnknownCommandPayloadRoundTripsThroughRawPayload()
    {
        var bseq = CreateUnknownCommandBseq();

        var json = SwShBseqJsonConverter.ExportToJson(bseq);
        var root = JsonNode.Parse(json)!.AsObject();
        var command = root["commands"]!.AsArray()[0]!.AsObject();

        Assert.Null(command["parameters"]);
        Assert.Equal("01020304", command["rawPayload"]!.GetValue<string>());
        Assert.Equal(bseq, SwShBseqJsonConverter.ImportFromJson(json));
    }

    private static byte[] CreateSpecialQuizResultBseq()
    {
        var data = new byte[0x54];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 100);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, SwShBseqKnownCommands.SpecialQuizResult);
        WriteU32(data, 0x20, SwShBseqKnownCommands.SpecialQuizResultPayloadLength);
        WriteU64(data, 0x30, SwShBseqKnownCommands.SpecialQuizResult);
        WriteI32(data, 0x38, 6);
        WriteI32(data, 0x3C, 2);
        WriteI32(data, 0x40, 6);
        WriteI32(data, 0x44, 1);
        WriteI32(data, 0x48, 0);
        WriteI32(data, 0x4C, 0);
        WriteU32(data, 0x50, 0xFFFFFFFF);
        return data;
    }

    private static byte[] CreateUnknownCommandBseq()
    {
        const ulong commandHash = 0x1122334455667788;
        var data = new byte[0x40];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 100);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, commandHash);
        WriteU32(data, 0x20, 4);
        WriteU64(data, 0x30, commandHash);
        data[0x38] = 1;
        data[0x39] = 2;
        data[0x3A] = 3;
        data[0x3B] = 4;
        WriteU32(data, 0x3C, 0xFFFFFFFF);
        return data;
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteI32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }
}
