// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.FairyGymBoosts;
using KM.Api.Projects;
using KM.Formats.SwSh;
using KM.SwSh.FairyGymBoosts;
using KM.Tools.Bridge;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class FairyGymBoostsBridgeTests
{
    private const int FileLength = 0x4A10;
    private const int PayloadOffset = 0x1550;
    private const ulong FillerCommandHash = 0x1020304050607080;
    private const int FillerPayloadLength = 0x14F8;

    [Fact]
    public void DispatcherMapsVerifiedWorkflowAndCanonicalStagingContract()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseExeFsFile("main", "base-main");
        var npdm = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(
            npdm.AsSpan(0x290, sizeof(ulong)),
            0x0100ABF008968000UL);
        temp.WriteBaseExeFsFile("main.npdm", npdm);
        foreach (var (relativePath, slots) in GetSources())
        {
            temp.WriteBaseRomFsFile(
                relativePath["romfs/".Length..],
                CreateBseq(slots.First, slots.Second));
        }

        var paths = temp.Paths with { SelectedGame = ProjectGameDto.Sword };
        var dispatcher = new ProjectBridgeDispatcher();
        var loadResponse = Dispatch<LoadFairyGymBoostsWorkflowRequest, LoadFairyGymBoostsWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadFairyGymBoostsWorkflow,
            new LoadFairyGymBoostsWorkflowRequest(paths));

        Assert.Null(loadResponse.Error);
        var workflow = Assert.IsType<FairyGymBoostsWorkflowDto>(loadResponse.Payload?.Workflow);
        Assert.Equal(ProjectGameDto.Sword, workflow.DetectedGame);
        Assert.Equal(96, workflow.Stats.OwnedByteCount);
        Assert.Equal(6, workflow.Stats.SourceFileCount);
        Assert.All(workflow.Sources, source =>
        {
            Assert.Equal("available", source.Status);
            Assert.Equal("0x00001550", source.PayloadOffsetHex);
            Assert.Equal("0x00001550-0x0000155F", source.OwnedRangeHex);
        });
        Assert.All(
            workflow.Trainers.SelectMany(trainer => trainer.Boosts),
            boost => Assert.True(boost.IsAvailable));

        var selections = workflow.Trainers
            .SelectMany(trainer => trainer.Boosts)
            .Select(boost => new FairyGymBoostSelectionDto(
                boost.BoostId,
                boost.BoostId == "annette-weakness-poison" ? 2 : boost.EffectId,
                boost.ResultKind))
            .ToArray();
        var stageResponse = Dispatch<StageFairyGymBoostsRequest, StageFairyGymBoostsResponse>(
            dispatcher,
            KmCommandNames.StageFairyGymBoosts,
            new StageFairyGymBoostsRequest(paths, Session: null, selections));

        Assert.Null(stageResponse.Error);
        var payload = Assert.IsType<StageFairyGymBoostsResponse>(stageResponse.Payload);
        Assert.DoesNotContain(
            payload.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        var edit = Assert.Single(payload.Session.PendingEdits);
        Assert.Equal("workflow.fairyGymBoosts", edit.Domain);
        Assert.Equal("fairy-gym-boosts", edit.RecordId);
        Assert.Equal("boostSelections", edit.Field);
        Assert.Equal(12, edit.NewValue!.Split(';').Length);
        Assert.Equal(6, edit.Sources.Count(source => source.Layer == FileLayerDto.Base));
        Assert.Single(edit.Sources, source => source.Layer == FileLayerDto.Pending);
    }

    private static BridgeResponse<TResponse> Dispatch<TRequest, TResponse>(
        ProjectBridgeDispatcher dispatcher,
        string command,
        TRequest payload)
    {
        var request = new BridgeRequest<TRequest>(command, payload, "fairy-gym-test");
        var responseJson = dispatcher.Dispatch(JsonSerializer.Serialize(
            request,
            BridgeJson.SerializerOptions));
        return JsonSerializer.Deserialize<BridgeResponse<TResponse>>(
            responseJson,
            BridgeJson.SerializerOptions)!;
    }

    private static IReadOnlyList<(string RelativePath, (Slot First, Slot Second) Slots)> GetSources()
    {
        return
        [
            (SwShFairyGymBoostsWorkflowService.AnnetteSequencePath, (new Slot(1, 1), new Slot(1, 1))),
            (SwShFairyGymBoostsWorkflowService.TeresaSequencePath, (new Slot(5, 2), new Slot(5, 1))),
            (SwShFairyGymBoostsWorkflowService.TheodoraSequencePath, (new Slot(3, 2), new Slot(3, 1))),
            (SwShFairyGymBoostsWorkflowService.OpalNicknameSequencePath, (new Slot(6, 2), new Slot(6, 1))),
            (SwShFairyGymBoostsWorkflowService.OpalColorSequencePath, (new Slot(4, 2), new Slot(4, 1))),
            (SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath, (new Slot(2, 1), new Slot(2, 2))),
        ];
    }

    private static byte[] CreateBseq(Slot first, Slot second)
    {
        var data = new byte[FileLength];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 1);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 2);
        WriteU64(data, 0x18, FillerCommandHash);
        WriteU32(data, 0x20, FillerPayloadLength);
        WriteU64(data, 0x24, SwShBseqKnownCommands.SpecialQuizResult);
        WriteU32(data, 0x2C, SwShBseqKnownCommands.SpecialQuizResultPayloadLength);
        WriteU64(data, 0x3C, FillerCommandHash);
        WriteU64(data, 0x44 + FillerPayloadLength + 0x0C, SwShBseqKnownCommands.SpecialQuizResult);
        WriteSlot(data, 1, first);
        WriteSlot(data, 2, second);
        WriteSlot(data, 3, new Slot(unchecked((int)0x11223344), unchecked((int)0x55667788)));
        WriteU32(data, PayloadOffset + SwShBseqKnownCommands.SpecialQuizResultPayloadLength, 0xFFFFFFFF);
        data[0x3000] = 0xA5;
        return data;
    }

    private static void WriteSlot(byte[] data, int answerChoice, Slot slot)
    {
        var offset = PayloadOffset + ((answerChoice - 1) * 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), slot.EffectId);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + sizeof(int)), slot.ResultValue);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);
    }

    private sealed record Slot(int EffectId, int ResultValue);
}
