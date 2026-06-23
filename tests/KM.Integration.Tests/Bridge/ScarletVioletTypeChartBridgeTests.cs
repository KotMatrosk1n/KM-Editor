// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text.Json;
using Google.FlatBuffers;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.HyperspaceBypass;
using KM.Api.Projects;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Formats.SwSh;
using KM.Integration.Tests.Tools;
using KM.SV.TypeChart;
using KM.Tools.Bridge;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class ScarletVioletTypeChartBridgeTests
{
    private const ulong ScarletTitleId = 0x0100A3D008C5C000;
    private const ulong VioletTitleId = 0x01008F6008C5E000;

    public static IEnumerable<object[]> ScarletVioletGames()
    {
        yield return [ProjectGameDto.Scarlet, ScarletTitleId];
        yield return [ProjectGameDto.Violet, VioletTitleId];
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletTypeChartStagesStandaloneMainAndRejectsTrinityOutput(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvTypeChartBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

        var load = Dispatch<LoadTypeChartWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(paths),
            "request-sv-type-chart-load");
        AssertSuccess(load);
        Assert.Equal("available", load.Payload!.Workflow.InstallStatus);
        Assert.Equal(game, load.Payload.Workflow.DetectedGame);
        Assert.Equal("main.ro+0x0082286C", load.Payload.Workflow.ChartOffsetHex);
        Assert.DoesNotContain(load.Payload.Workflow.Types, type => type.Label == "Stellar");

        var values = load.Payload.Workflow.Cells
            .OrderBy(cell => cell.AttackTypeIndex)
            .ThenBy(cell => cell.DefenseTypeIndex)
            .Select(cell => cell.Effectiveness)
            .ToArray();
        values[0] = 0;
        values[(14 * SvTypeChartMainPatcher.TypeCount) + 17] = 2;

        var stage = Dispatch<StageTypeChartResponse>(
            dispatcher,
            KmCommandNames.StageTypeChart,
            new StageTypeChartRequest(paths, Session: null, values),
            "request-sv-type-chart-stage");
        AssertSuccess(stage);
        Assert.Single(stage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.typeChart", stage.Payload.Session.PendingEdits[0].Domain);
        Assert.DoesNotContain(stage.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);

        var validation = Dispatch<ValidateEditSessionResponse>(
            dispatcher,
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(paths, stage.Payload.Session),
            "request-sv-type-chart-validate");
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
                $"request-sv-type-chart-{outputMode}-plan");
            AssertSuccess(romFsPlan);
            Assert.False(romFsPlan.Payload!.ChangePlan.CanApply);
            Assert.Empty(romFsPlan.Payload.ChangePlan.Writes);
            Assert.Contains(
                romFsPlan.Payload.ChangePlan.Diagnostics,
                diagnostic => diagnostic.Message.Contains("outside Scarlet/Violet RomFS output modes", StringComparison.Ordinal));
        }

        var plan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, stage.Payload.Session),
            "request-sv-type-chart-plan");
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
            "request-sv-type-chart-apply");
        AssertSuccess(apply);
        Assert.DoesNotContain(apply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.Contains("exefs/main", apply.Payload.ApplyResult.WrittenFiles);
        Assert.Equal(baseMainBytes, File.ReadAllBytes(baseMainPath));

        var outputMainBytes = File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "exefs", "main"));
        var analysis = SvTypeChartMainPatcher.Analyze(outputMainBytes, ToCore(game));

        Assert.Equal(SvTypeChartMainKind.Modified, analysis.Kind);
        Assert.Equal(SvTypeChartWorkflowService.ToGameOrder(values), analysis.EffectivenessValues);

        var installed = Dispatch<LoadTypeChartWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(paths),
            "request-sv-type-chart-installed-load");
        AssertSuccess(installed);
        Assert.Equal("modified", installed.Payload!.Workflow.InstallStatus);
        Assert.Equal(ProjectFileLayerDto.Layered, installed.Payload.Workflow.Source!.Provenance.SourceLayer);
    }

    [Theory]
    [MemberData(nameof(ScarletVioletGames))]
    public void ScarletVioletTypeChartUninstallPreservesOtherGeneratedMainEdits(
        ProjectGameDto game,
        ulong titleId)
    {
        using var temp = CreateScarletVioletProject(titleId);
        temp.WriteBaseExeFsFile("main", SvTypeChartBridgeFixtures.CreateCompatibleMain(game));
        var paths = temp.Paths with { SelectedGame = game };
        var dispatcher = new ProjectBridgeDispatcher();

        var hyperspaceStage = Dispatch<StageHyperspaceBypassInstallResponse>(
            dispatcher,
            KmCommandNames.StageHyperspaceBypassInstall,
            new StageHyperspaceBypassInstallRequest(paths, Session: null),
            "request-sv-type-chart-preserve-hyperspace-stage");
        AssertSuccess(hyperspaceStage);
        var hyperspacePlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, hyperspaceStage.Payload!.Session),
            "request-sv-type-chart-preserve-hyperspace-plan");
        AssertSuccess(hyperspacePlan);
        Assert.True(hyperspacePlan.Payload!.ChangePlan.CanApply);
        var hyperspaceApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, hyperspaceStage.Payload.Session, hyperspacePlan.Payload.ChangePlan),
            "request-sv-type-chart-preserve-hyperspace-apply");
        AssertSuccess(hyperspaceApply);

        var load = Dispatch<LoadTypeChartWorkflowResponse>(
            dispatcher,
            KmCommandNames.LoadTypeChartWorkflow,
            new LoadTypeChartWorkflowRequest(paths),
            "request-sv-type-chart-preserve-load");
        AssertSuccess(load);
        var values = load.Payload!.Workflow.Cells
            .OrderBy(cell => cell.AttackTypeIndex)
            .ThenBy(cell => cell.DefenseTypeIndex)
            .Select(cell => cell.Effectiveness)
            .ToArray();
        values[0] = 0;

        var typeChartStage = Dispatch<StageTypeChartResponse>(
            dispatcher,
            KmCommandNames.StageTypeChart,
            new StageTypeChartRequest(paths, Session: null, values),
            "request-sv-type-chart-preserve-stage");
        AssertSuccess(typeChartStage);
        var typeChartPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, typeChartStage.Payload!.Session),
            "request-sv-type-chart-preserve-plan");
        AssertSuccess(typeChartPlan);
        Assert.True(typeChartPlan.Payload!.ChangePlan.CanApply);
        var typeChartApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, typeChartStage.Payload.Session, typeChartPlan.Payload.ChangePlan),
            "request-sv-type-chart-preserve-apply");
        AssertSuccess(typeChartApply);

        var outputMainPath = Path.Combine(temp.OutputRootPath, "exefs", "main");
        var outputWithBoth = File.ReadAllBytes(outputMainPath);
        Assert.Equal(SvHyperspaceBypassBridgeFixtures.BypassBranch, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(outputWithBoth));
        Assert.Equal(SvTypeChartMainKind.Modified, SvTypeChartMainPatcher.Analyze(outputWithBoth, ToCore(game)).Kind);

        var uninstallStage = Dispatch<StageTypeChartUninstallResponse>(
            dispatcher,
            KmCommandNames.StageTypeChartUninstall,
            new StageTypeChartUninstallRequest(paths, Session: null),
            "request-sv-type-chart-preserve-uninstall-stage");
        AssertSuccess(uninstallStage);
        Assert.Single(uninstallStage.Payload!.Session.PendingEdits);
        Assert.Equal("workflow.typeChart", uninstallStage.Payload.Session.PendingEdits[0].Domain);
        Assert.Equal("sv-type-chart-v1-uninstall", uninstallStage.Payload.Session.PendingEdits[0].RecordId);

        var uninstallPlan = Dispatch<CreateChangePlanResponse>(
            dispatcher,
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(paths, uninstallStage.Payload.Session),
            "request-sv-type-chart-preserve-uninstall-plan");
        AssertSuccess(uninstallPlan);
        Assert.True(uninstallPlan.Payload!.ChangePlan.CanApply);

        var uninstallApply = Dispatch<ApplyChangePlanResponse>(
            dispatcher,
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(paths, uninstallStage.Payload.Session, uninstallPlan.Payload.ChangePlan),
            "request-sv-type-chart-preserve-uninstall-apply");
        AssertSuccess(uninstallApply);
        Assert.DoesNotContain(uninstallApply.Payload!.ApplyResult.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
        Assert.True(File.Exists(outputMainPath));

        var outputAfterUninstall = File.ReadAllBytes(outputMainPath);
        var analysis = SvTypeChartMainPatcher.Analyze(outputAfterUninstall, ToCore(game));

        Assert.Equal(SvHyperspaceBypassBridgeFixtures.BypassBranch, SvHyperspaceBypassBridgeFixtures.ReadPatchInstruction(outputAfterUninstall));
        Assert.Equal(SvTypeChartMainKind.Vanilla, analysis.Kind);
        Assert.Equal(SvTypeChartMainPatcher.VanillaChartValues, analysis.EffectivenessValues);
    }

    private static TemporaryBridgeProject CreateScarletVioletProject(ulong titleId)
    {
        var temp = TemporaryBridgeProject.Create();
        temp.EnsureScarletVioletSupportFolder();
        temp.WriteBaseRomFsFile("arc/data.trpfd", CreateTrinityDescriptor([]));
        temp.WriteBaseRomFsFile("arc/data.trpfs", "storage");
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(titleId));
        return temp;
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateTrinityDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = KM.Formats.SV.Trinity.FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = KM.Formats.SV.Trinity.FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(KM.Formats.SV.SvTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => KM.Formats.SV.Trinity.FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = KM.Formats.SV.Trinity.FileDescriptor.CreateFilesVector(builder, fileEntries);
        var pack = KM.Formats.SV.Trinity.PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: 123,
            file_count: checked((ulong)virtualPaths.Count));
        var packs = KM.Formats.SV.Trinity.FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = KM.Formats.SV.Trinity.FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        KM.Formats.SV.Trinity.FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static KM.Core.Projects.ProjectGame ToCore(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Scarlet => KM.Core.Projects.ProjectGame.Scarlet,
            ProjectGameDto.Violet => KM.Core.Projects.ProjectGame.Violet,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
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
