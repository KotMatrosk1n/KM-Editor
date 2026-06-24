// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Core.Projects;
using KM.Integration.Tests.Tools;
using KM.Tools.Bridge;
using KM.ZA.TypeChart;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class PokemonLegendsZATypeChartBridgeTests
{
    private const ulong PokemonLegendsZATitleId = 0x0100F43008C44000;
    private const int ZaNpdmTitleIdOffset = 0x480;

    [Fact]
    public void PokemonLegendsZATypeChartStagesStandaloneMainAndRejectsTrinityOutput()
    {
        using var temp = CreatePokemonLegendsZAProject();
        temp.WriteBaseExeFsFile("main", ZaTypeChartBridgeFixtures.CreateCompatibleMain());
        var paths = temp.Paths with { SelectedGame = ProjectGameDto.ZA };
        var dispatcher = new ProjectBridgeDispatcher();

        var load = Dispatch<LoadTypeChartWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(paths),
            "request-za-type-chart-load");
        AssertSuccess(load);
        Assert.Equal("available", load.Payload!.Workflow.InstallStatus);
        Assert.Equal(ProjectGameDto.ZA, load.Payload.Workflow.DetectedGame);
        Assert.Equal("main.ro+0x0019F2A4", load.Payload.Workflow.ChartOffsetHex);
        Assert.DoesNotContain(load.Payload.Workflow.Types, type => type.Label == "Stellar");

        var values = load.Payload.Workflow.Cells
            .OrderBy(cell => cell.AttackTypeIndex)
            .ThenBy(cell => cell.DefenseTypeIndex)
            .Select(cell => cell.Effectiveness)
            .ToArray();
        values[0] = 0;
        values[(14 * ZaTypeChartMainPatcher.TypeCount) + 17] = 2;

        var stage = Dispatch<StageTypeChartResponse>(
            dispatcher,
            KmCommandNames.StageTypeChart,
            new StageTypeChartRequest(paths, Session: null, values),
            "request-za-type-chart-stage");
        AssertSuccess(stage);
        Assert.Single(stage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.typeChart", stage.Payload.Session.PendingEdits[0].Domain);
        Assert.Equal("za-type-chart", stage.Payload.Session.PendingEdits[0].RecordId);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validation = Dispatch<ValidateEditSessionResponse>(
            dispatcher,
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(paths, stage.Payload.Session),
            "request-za-type-chart-validate");
        AssertSuccess(validation);
        Assert.True(validation.Payload!.IsValid);

        foreach (var outputMode in new[]
        {
            ChangePlanOutputModeDto.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass,
        })
        {
            var romFsPlan = Dispatch<CreateChangePlanResponse>(
                dispatcher,
                KmCommandNames.CreateChangePlan,
                new CreateChangePlanRequest(paths, stage.Payload.Session, outputMode),
                $"request-za-type-chart-{outputMode}-plan");
            AssertSuccess(romFsPlan);
            Assert.False(romFsPlan.Payload!.ChangePlan.CanApply);
            Assert.Empty(romFsPlan.Payload.ChangePlan.Writes);
            Assert.Contains(
                romFsPlan.Payload.ChangePlan.Diagnostics,
                diagnostic => diagnostic.Message.Contains("outside Pokemon Legends Z-A RomFS output modes", StringComparison.Ordinal));
        }

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, stage.Payload.Session),
            "request-za-type-chart-plan");
        AssertSuccess(plan);
        Assert.True(plan.Payload!.ChangePlan.CanApply);
        var write = Assert.Single(plan.Payload.ChangePlan.Writes);
        Assert.Equal("exefs/main", write.TargetRelativePath);

        var baseMainPath = Path.Combine(temp.BaseExeFsPath, "main");
        var baseMainBytes = File.ReadAllBytes(baseMainPath);

        var apply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, stage.Payload.Session, plan.Payload.ChangePlan),
            "request-za-type-chart-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains("exefs/main", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputMainBytes = File.ReadAllBytes(outputMainPath);
        var analysis = ZaTypeChartMainPatcher.Analyze(outputMainBytes, ProjectGame.ZA);

        Assert.Equal(ZaTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(ZaTypeChartWorkflowService.ToGameOrder(values), analysis.EffectivenessValues);

        var installed = Dispatch<LoadTypeChartWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(paths),
            "request-za-type-chart-installed-load");
        AssertSuccess(installed);
        Assert.Equal("modified", installed.Payload!.Workflow.InstallStatus);
        Assert.Equal(ProjectFileLayerDto.Layered, installed.Payload.Workflow.Source!.Provenance.SourceLayer);

        var uninstallStage = Dispatch<StageTypeChartUninstallResponse>(
            dispatcher,
            KmCommandNames.StageTypeChartUninstall,
            new StageTypeChartUninstallRequest(paths, Session: null),
            "request-za-type-chart-uninstall-stage");
        AssertSuccess(uninstallStage);
        Assert.Single(uninstallStage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.typeChart", uninstallStage.Payload.Session.PendingEdits[0].Domain);
        Assert.Equal("za-type-chart-v1-uninstall", uninstallStage.Payload.Session.PendingEdits[0].RecordId);

        var uninstallPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, uninstallStage.Payload.Session),
            "request-za-type-chart-uninstall-plan");
        AssertSuccess(uninstallPlan);
        Assert.True(uninstallPlan.Payload!.ChangePlan.CanApply);

        var uninstallApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, uninstallStage.Payload.Session, uninstallPlan.Payload.ChangePlan),
            "request-za-type-chart-uninstall-apply");
        AssertSuccess(uninstallApply);
        Assert.DoesNotContain(uninstallApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.False(File.Exists(outputMainPath));
    }

    private static TemporaryBridgeProject CreatePokemonLegendsZAProject()
    {
        var temp = TemporaryBridgeProject.Create();
        temp.EnsurePokemonLegendsZASupportFolder();
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(PokemonLegendsZATitleId));
        temp.WriteBaseRomFsFile("arc/data.trpfd", []);
        temp.WriteBaseRomFsFile("arc/data.trpfs", []);
        return temp;
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[ZaNpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(ZaNpdmTitleIdOffset), titleId);
        return npdm;
    }

    private static BridgeResponse<TPayload> Dispatch<TPayload>(
        ProjectBridgeDispatcher dispatcher,
        string command,
        object payload,
        string requestId)
    {
        var requestJson = JsonSerializer.Serialize(
            new BridgeRequest<object>(command, payload, requestId),
            BridgeJson.SerializerOptions);
        var responseJson = dispatcher.Dispatch(requestJson);
        var response = JsonSerializer.Deserialize<BridgeResponse<TPayload>>(responseJson, BridgeJson.SerializerOptions);
        Assert.NotNull(response);
        return response;
    }

    private static void AssertSuccess<TPayload>(BridgeResponse<TPayload> response)
    {
        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
    }
}
