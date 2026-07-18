// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.BagHook;
using KM.SwSh.ExeFs;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Placement;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyWorkflowServiceTests
{
    private const ulong RareCandyItemHash = 0x1111111111111111;
    private const ulong RoyalCandyItemHash = 0x2222222222222222;
    private const ulong UnrelatedItemHash = 0x3333333333333333;

    private static readonly byte[] RaidUnrelatedMemberData = [0x10, 0x20, 0x30, 0x40];

    private static readonly string[] ExpectedRoyalCandyItemNameTextPaths =
    [
        "romfs/bin/message/English/common/itemname.dat",
        "romfs/bin/message/English/common/itemname_acc.dat",
        "romfs/bin/message/English/common/itemname_acc_classified.dat",
        "romfs/bin/message/English/common/itemname_classified.dat",
        "romfs/bin/message/English/common/itemname_plural.dat",
        "romfs/bin/message/English/common/itemname_plural_classified.dat",
    ];

    [Fact]
    public void LoadBuildsRealPreflightFromProjectFiles()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.Available, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Workflows.Count);
        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        Assert.Equal("Unlimited Royal Candy", unlimited.Name);
        Assert.Equal("available", unlimited.Status);
        Assert.Equal("unlimited", unlimited.Mode);
        Assert.Equal(1128, unlimited.ItemId);
        Assert.Equal(50, unlimited.TemplateItemId);
        Assert.Empty(unlimited.LevelCaps);
        Assert.Equal(ProjectFileLayer.Base, unlimited.Provenance.SourceLayer);
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("Royal Candy with Story Limits", storyLimits.Name);
        Assert.Equal("storyLimits", storyLimits.Mode);
        Assert.Equal(25, storyLimits.LevelCaps.Count);
        Assert.Equal("Hop 004/005/006", storyLimits.LevelCaps[0].Label);
        Assert.Equal(10, storyLimits.LevelCaps[0].LevelCap);
        Assert.Equal("Gordie 135", storyLimits.LevelCaps.Single(cap => cap.LevelCap == 52).Label);
        Assert.Equal("workAtLeast", storyLimits.LevelCaps.Single(cap => cap.LevelCap == 20).ProgressKind);
        Assert.Equal(530, storyLimits.LevelCaps.Single(cap => cap.LevelCap == 20).WorkMinimum);
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":item-data", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(
            workflow.Checks,
            check => check.CheckId.EndsWith(":item-data-stride", StringComparison.Ordinal)
                && check.Status == "Pass"
                && check.Message.Contains("1,129 item id", StringComparison.Ordinal));
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":royal-candy-row", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":message-text-sets", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":game-flavor", StringComparison.Ordinal) && check.Message.Contains("Pokemon Sword", StringComparison.Ordinal));
        Assert.Contains(workflow.Checks, check => check.CheckId.Contains("patch-code-cave", StringComparison.Ordinal) && check.Status == "Pass");
        Assert.Contains(workflow.Outputs, output => output.WorkflowId == unlimited.WorkflowId && output.RelativePath == SwShRoyalCandyWorkflowService.ItemPath);
        var itemNameOutputs = workflow.Outputs
            .Where(output => output.WorkflowId == unlimited.WorkflowId
                && output.RelativePath.Contains("/itemname", StringComparison.OrdinalIgnoreCase))
            .Select(output => output.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedRoyalCandyItemNameTextPaths, itemNameOutputs);
        Assert.Contains(workflow.Outputs, output => output.WorkflowId == unlimited.WorkflowId && output.RelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.Equal(3, workflow.Stats.TotalWorkflowCount);
        Assert.True(workflow.Stats.TotalCheckCount >= 40);
        Assert.Equal(0, workflow.Stats.FailCount);
        Assert.True(workflow.Stats.SourceFileCount >= 10);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void CreateLevelCapsUsesVersionSpecificGymFourLabels()
    {
        var swordCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword");
        var shieldCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Shield");

        var swordGymFour = swordCaps.Single(cap => cap.LevelCap == 42);
        var shieldGymFour = shieldCaps.Single(cap => cap.LevelCap == 42);

        Assert.Equal("Bea 077", swordGymFour.Label);
        Assert.Equal("Allister 078", shieldGymFour.Label);
        Assert.Equal("0xC07B67FC3148B754", swordGymFour.ProgressHash);
        Assert.Equal(swordGymFour.ProgressHash, shieldGymFour.ProgressHash);
    }

    [Fact]
    public void LoadUsesShieldStoryCapLabelsForShieldProjects()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso(ProjectGame.Shield));
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x01008DB008C2C000));
        var project = new ProjectWorkspaceService().Open(temp.Paths with { SelectedGame = ProjectGame.Shield });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Contains(storyLimits.LevelCaps, cap => cap.Label == "Allister 078");
        Assert.DoesNotContain(storyLimits.LevelCaps, cap => cap.Label == "Bea 077");
    }

    [Fact]
    public void LoadReflectsInstalledCustomStoryLevelCaps()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var defaultCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword");
        var customCaps = defaultCaps
            .Select(cap => new SwShRoyalCandyStoryLevelCap(
                LevelCap: cap.LevelCap + 1,
                ProgressHash: ulong.Parse(cap.ProgressHash[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                Label: cap.Label,
                ProgressKind: cap.ProgressKind == "workAtLeast"
                    ? SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                    : SwShRoyalCandyStoryLevelCapProgressKind.Flag,
                WorkMinimum: cap.WorkMinimum ?? 0))
            .ToArray();
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(CreateCompatibleNso(), customCaps));
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("installed", storyLimits.Status);
        Assert.Equal(11, storyLimits.LevelCaps.Single(cap => cap.Slot == 0).LevelCap);
        Assert.Equal(43, storyLimits.LevelCaps.Single(cap => cap.Label == "Bea 077").LevelCap);
        Assert.Equal(91, storyLimits.LevelCaps.Single(cap => cap.Label == "Leon 149/189/190").LevelCap);
    }

    [Fact]
    public void LoadBlocksInstallWhenRequiredInputsAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/placeholder.bin", [0x01]);
        temp.WriteBaseExeFsFile("placeholder.bin", [0x02]);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":item-data", StringComparison.Ordinal) && check.Status == "Fail");
        Assert.Contains(workflow.Checks, check => check.CheckId.EndsWith(":message-text-sets", StringComparison.Ordinal) && check.Status == "Fail");
        Assert.True(workflow.Stats.FailCount >= 8);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.royalCandy");
    }

    [Fact]
    public void LoadMarksMatchingRoyalCandyVariantInstalled()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        var caps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword")
            .Select(cap => new SwShRoyalCandyStoryLevelCap(
                cap.LevelCap,
                ulong.Parse(cap.ProgressHash[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                cap.Label,
                cap.ProgressKind == "workAtLeast"
                    ? SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                    : SwShRoyalCandyStoryLevelCapProgressKind.Flag,
                cap.WorkMinimum ?? 0))
            .ToArray();
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(CreateCompatibleNso(), caps));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Equal("installed", storyLimits.Status);
        Assert.All(
            workflow.Outputs.Where(output => output.WorkflowId == unlimited.WorkflowId),
            output => Assert.Equal("blocked", output.Status));
        Assert.All(
            workflow.Outputs.Where(output => output.WorkflowId == storyLimits.WorkflowId),
            output => Assert.Equal("review", output.Status));
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("Royal Candy with Story Limits is installed", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadFlagsMixedRoyalCandyTextAndExeFsVariants()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        temp.WriteOutputFile("exefs/main", SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(CreateCompatibleNso()));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var unlimited = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited");
        var storyLimits = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits");
        Assert.Equal("blocked", unlimited.Status);
        Assert.Equal("blocked", storyLimits.Status);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("mixed Royal Candy targets", StringComparison.Ordinal)
                && diagnostic.Message.Contains("item text identifies Royal Candy with Story Limits", StringComparison.Ordinal)
                && diagnostic.Message.Contains("exefs/main identifies Unlimited Royal Candy", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadRequiresRoyalCandyTextInSelectedGameTextLanguage()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        WriteLanguageTextSet(temp, "French");
        temp.WriteOutputFile(
            "romfs/bin/message/French/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                CreateCompatibleNso(),
                CreateStoryCaps("Sword")));

        var workflow = new SwShRoyalCandyWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths with { GameTextLanguage = "en" }));

        Assert.Equal(
            "blocked",
            workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits").Status);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("outside the selected English language set", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadBlocksConflictingRoyalCandyTextAcrossLanguages()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        WriteLanguageTextSet(temp, "French");
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        temp.WriteOutputFile(
            "romfs/bin/message/French/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. It can be used repeatedly by compatible Pokemon.")));
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                CreateCompatibleNso(),
                CreateStoryCaps("Sword")));

        var workflow = new SwShRoyalCandyWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths with { GameTextLanguage = "en" }));

        Assert.All(
            workflow.Workflows.Where(record => record.WorkflowId != "royal-candy-uninstall"),
            record => Assert.Equal("blocked", record.Status));
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("conflicting Royal Candy item-text variants across language sets", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadRejectsInstalledStoryLadderWithWrongMilestoneKeys()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile(
            "romfs/bin/message/English/common/iteminfo.dat",
            CreateTextTable(
                1128,
                (1128, "A candy packed with strange energy. Its full power follows the current story limit.")));
        var wrongCaps = CreateStoryCaps("Sword")
            .Select((cap, index) => cap with { ProgressHash = cap.ProgressHash + (ulong)index + 1 })
            .ToArray();
        temp.WriteOutputFile(
            "exefs/main",
            SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(CreateCompatibleNso(), wrongCaps));

        var workflow = new SwShRoyalCandyWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));

        Assert.Equal(
            "blocked",
            workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-story-limits").Status);
        Assert.Contains(
            workflow.Checks,
            check => check.CheckId.EndsWith(":installed-story-ladder", StringComparison.Ordinal)
                && check.Status == "Fail"
                && check.Message.Contains("exact supported story milestones", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadDetectsKnownLayeredOutputsForUninstallReview()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteOutputFile("exefs/main", SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(CreateCompatibleNso()));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        var uninstall = workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-uninstall");
        Assert.Equal("warning", uninstall.Status);
        Assert.Contains(
            workflow.Checks,
            check => check.WorkflowId == uninstall.WorkflowId
                && check.Status == "Warning"
                && check.Message.Contains("partial Royal Candy installation", StringComparison.Ordinal));
        Assert.Contains(
            workflow.Outputs,
            output => output.WorkflowId == uninstall.WorkflowId
                && output.RelativePath == SwShRoyalCandyWorkflowService.ExeFsMainPath
                && output.Provenance.SourceLayer == ProjectFileLayer.Layered);
    }

    [Fact]
    public void ApplyRollsBackEarlierOutputsWhenALaterTargetCannotBeWritten()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(
            temp.Paths,
            workflowId: "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.True(plan.CanApply);

        var failingWrite = plan.Writes
            .Skip(1)
            .Where(candidate => !plan.Writes
                .Where(other => !ReferenceEquals(other, candidate))
                .Any(other => other.Sources.Any(source =>
                    string.Equals(
                        source.RelativePath,
                        candidate.TargetRelativePath,
                        StringComparison.OrdinalIgnoreCase))))
            .Last(write => !File.Exists(OutputPath(temp, write.TargetRelativePath)));
        var failingPath = OutputPath(temp, failingWrite.TargetRelativePath);
        Directory.CreateDirectory(failingPath);
        var originalFiles = plan.Writes.ToDictionary(
            write => write.TargetRelativePath,
            write => File.Exists(OutputPath(temp, write.TargetRelativePath))
                ? File.ReadAllBytes(OutputPath(temp, write.TargetRelativePath))
                : null,
            StringComparer.OrdinalIgnoreCase);

        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);

        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("all output changes were rolled back", StringComparison.Ordinal));
        Assert.Empty(apply.WrittenFiles);
        Assert.True(Directory.Exists(failingPath));
        foreach (var write in plan.Writes)
        {
            var outputPath = OutputPath(temp, write.TargetRelativePath);
            var original = originalFiles[write.TargetRelativePath];
            if (original is null)
            {
                Assert.False(File.Exists(outputPath));
            }
            else
            {
                Assert.Equal(original, File.ReadAllBytes(outputPath));
            }
        }
    }

    [Fact]
    public void LoadUsesSelectedGameTextLanguageAndPlansConcreteAcquisitionArchives()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        WriteLanguageTextSet(temp, "French");
        var paths = temp.Paths with { GameTextLanguage = "fr" };

        var workflow = new SwShRoyalCandyWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal("available", workflow.Workflows.Single(record => record.WorkflowId == "royal-candy-unlimited").Status);
        var textOutputs = workflow.Outputs
            .Where(output => output.WorkflowId == "royal-candy-unlimited"
                && output.RelativePath.Contains("/message/", StringComparison.OrdinalIgnoreCase))
            .Select(output => output.RelativePath)
            .ToArray();
        Assert.NotEmpty(textOutputs);
        Assert.All(textOutputs, path => Assert.Contains("/French/", path, StringComparison.Ordinal));
        Assert.DoesNotContain(textOutputs, path => path.Contains("/English/", StringComparison.Ordinal));
        Assert.Contains(
            workflow.Outputs,
            output => output.WorkflowId == "royal-candy-unlimited"
                && output.RelativePath == SwShRoyalCandyWorkflowService.NestDataPath
                && output.Status == "ready");
        Assert.Contains(
            workflow.Outputs,
            output => output.WorkflowId == "royal-candy-unlimited"
                && output.RelativePath == SwShRoyalCandyWorkflowService.PlacementPath
                && output.Status == "ready");
    }

    [Fact]
    public void AcquisitionPlanBindsOwnershipManifestAndEveryAuthoritativeBaseInput()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);

        Assert.True(plan.CanApply);
        foreach (var relativePath in new[]
        {
            SwShRoyalCandyWorkflowService.ShopDataPath,
            SwShRoyalCandyWorkflowService.NestDataPath,
            SwShRoyalCandyWorkflowService.PlacementPath,
        })
        {
            var acquisitionWrite = Assert.Single(
                plan.Writes,
                write => write.TargetRelativePath == relativePath);
            Assert.Contains(
                acquisitionWrite.Sources,
                source => source.Layer == ProjectFileLayer.Base
                    && source.RelativePath == relativePath);
            Assert.Contains(
                acquisitionWrite.Sources,
                source => source.Layer == ProjectFileLayer.Generated
                    && source.RelativePath == SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        }

        var placementWrite = Assert.Single(
            plan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.PlacementPath);
        Assert.Contains(
            placementWrite.Sources,
            source => source.Layer == ProjectFileLayer.Base
                && source.RelativePath == SwShRoyalCandyWorkflowService.ItemHashPath);

        var manifestWrite = Assert.Single(
            plan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        foreach (var relativePath in new[]
        {
            SwShRoyalCandyWorkflowService.ShopDataPath,
            SwShRoyalCandyWorkflowService.NestDataPath,
            SwShRoyalCandyWorkflowService.PlacementPath,
            SwShRoyalCandyWorkflowService.ItemHashPath,
        })
        {
            Assert.Contains(
                manifestWrite.Sources,
                source => source.Layer == ProjectFileLayer.Base
                    && source.RelativePath == relativePath);
        }
    }

    [Fact]
    public void LegacyShopOnlyUninstallBindsManifestToLegacyBaseWithoutLayeredShop()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var modernBaseShopPath = Path.Combine(
            temp.BaseRomFsPath,
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var legacyBaseShopBytes = File.ReadAllBytes(modernBaseShopPath);
        File.Delete(modernBaseShopPath);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.LegacyShopDataPath["romfs/".Length..],
            legacyBaseShopBytes);
        var outputBeforeInstall = SnapshotOutputTree(temp.OutputRootPath);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertValidAcquisitionOwnershipManifest(temp);

        var legacyLayeredShopPath = OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.LegacyShopDataPath);
        Assert.True(File.Exists(legacyLayeredShopPath));
        File.Delete(legacyLayeredShopPath);
        Assert.False(File.Exists(OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.ShopDataPath)));

        var uninstallStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-uninstall",
            levelCaps: null,
            session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);

        Assert.True(uninstallPlan.CanApply);
        Assert.DoesNotContain(
            uninstallPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.LegacyShopDataPath);
        var manifestWrite = Assert.Single(
            uninstallPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        Assert.Contains(
            manifestWrite.Sources,
            source => source.Layer == ProjectFileLayer.Base
                && source.RelativePath == SwShRoyalCandyWorkflowService.LegacyShopDataPath);
        Assert.DoesNotContain(
            manifestWrite.Sources,
            source => source.Layer == ProjectFileLayer.Base
                && source.RelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);

        var uninstall = service.ApplyChangePlan(
            temp.Paths,
            uninstallStage.Session,
            uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(temp.OutputRootPath, outputBeforeInstall);
    }

    [Fact]
    public void LegacyInstallCannotClaimPreexistingAcquisitionReplacementsWithoutManifest()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var manifestPath = OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        Assert.True(File.Exists(manifestPath));
        File.Delete(manifestPath);
        var raidPath = OutputPath(temp, SwShRoyalCandyWorkflowService.NestDataPath);
        var placementPath = OutputPath(temp, SwShRoyalCandyWorkflowService.PlacementPath);
        var raidBefore = File.ReadAllBytes(raidPath);
        var placementBefore = File.ReadAllBytes(placementPath);

        var refreshStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var refreshPlan = service.CreateChangePlan(temp.Paths, refreshStage.Session);

        Assert.False(refreshPlan.CanApply);
        Assert.Contains(
            refreshPlan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("ownership manifest", StringComparison.OrdinalIgnoreCase));
        var refresh = service.ApplyChangePlan(temp.Paths, refreshStage.Session, refreshPlan);
        Assert.Empty(refresh.WrittenFiles);
        Assert.Equal(raidBefore, File.ReadAllBytes(raidPath));
        Assert.Equal(placementBefore, File.ReadAllBytes(placementPath));
        Assert.False(File.Exists(manifestPath));

        var uninstallStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-uninstall",
            levelCaps: null,
            session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);

        Assert.False(uninstallPlan.CanApply);
        Assert.Equal(
            "blocked",
            uninstallStage.Workflow.Workflows
                .Single(workflow => workflow.WorkflowId == "royal-candy-uninstall")
                .Status);
        foreach (var relativePath in new[]
        {
            SwShRoyalCandyWorkflowService.NestDataPath,
            SwShRoyalCandyWorkflowService.PlacementPath,
        })
        {
            Assert.Contains(
                uninstallStage.Workflow.Outputs,
                output => output.WorkflowId == "royal-candy-uninstall"
                    && output.RelativePath == relativePath
                    && output.Status == "blocked"
                    && output.Description.Contains("ownership manifest", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Contains(
            uninstallStage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("cleanup is blocked", StringComparison.OrdinalIgnoreCase));
        var uninstall = service.ApplyChangePlan(
            temp.Paths,
            uninstallStage.Session,
            uninstallPlan);
        Assert.Empty(uninstall.WrittenFiles);
        Assert.Equal(raidBefore, File.ReadAllBytes(raidPath));
        Assert.Equal(placementBefore, File.ReadAllBytes(placementPath));
        Assert.False(File.Exists(manifestPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOrStaleAcquisitionManifestBlocksWithoutChangingOutput(bool useStaleManifest)
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var manifestPath = OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        if (useStaleManifest)
        {
            var manifest = SwShRoyalCandyAcquisitionOwnershipManifest.Parse(
                File.ReadAllBytes(manifestPath));
            File.WriteAllBytes(
                manifestPath,
                SwShRoyalCandyAcquisitionOwnershipManifest.Write(
                    manifest with { BaseNestSha256 = new string('0', 64) }));
        }
        else
        {
            File.WriteAllBytes(manifestPath, [0x7B, 0x7D]);
        }

        var outputBefore = SnapshotOutputTree(temp.OutputRootPath);
        var refreshStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var refreshPlan = service.CreateChangePlan(temp.Paths, refreshStage.Session);

        Assert.False(refreshPlan.CanApply);
        Assert.Contains(
            refreshStage.Workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("ownership manifest", StringComparison.OrdinalIgnoreCase)
                && diagnostic.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        var refresh = service.ApplyChangePlan(temp.Paths, refreshStage.Session, refreshPlan);
        Assert.Empty(refresh.WrittenFiles);
        AssertOutputTreeMatches(temp.OutputRootPath, outputBefore);
    }

    [Fact]
    public void InstallRefreshAndUninstallReplaceAllVerifiedAcquisitionReferencesExactly()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 1128, 50]))],
                []).Write());
        var outputBeforeInstall = SnapshotOutputTree(temp.OutputRootPath);
        var service = new SwShRoyalCandyEditSessionService();

        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);

        Assert.True(installPlan.CanApply);
        Assert.Contains(
            installPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.NestDataPath);
        Assert.Contains(
            installPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.PlacementPath);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);

        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertInstalledAcquisitionOutputs(temp, [50, 50, 50]);
        AssertValidAcquisitionOwnershipManifest(temp);
        var installedOutputs = SnapshotOutputTree(temp.OutputRootPath);

        var refreshStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        Assert.Equal(
            "installed",
            refreshStage.Workflow.Workflows
                .Single(workflow => workflow.WorkflowId == "royal-candy-unlimited")
                .Status);
        var refreshPlan = service.CreateChangePlan(temp.Paths, refreshStage.Session);
        Assert.True(refreshPlan.CanApply);
        var refresh = service.ApplyChangePlan(temp.Paths, refreshStage.Session, refreshPlan);

        Assert.DoesNotContain(refresh.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(temp.OutputRootPath, installedOutputs);
        AssertInstalledAcquisitionOutputs(temp, [50, 50, 50]);
        AssertValidAcquisitionOwnershipManifest(temp);

        var uninstallStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-uninstall",
            levelCaps: null,
            session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        Assert.True(uninstallPlan.CanApply);
        Assert.Contains(
            uninstallPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.NestDataPath);
        Assert.Contains(
            uninstallPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.PlacementPath);
        Assert.Contains(
            uninstallPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(temp.OutputRootPath, outputBeforeInstall);
        Assert.False(File.Exists(OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath)));
    }

    [Fact]
    public void UninstallRestoresOwnedAcquisitionSlotsAndPreservesUnrelatedArchiveMembers()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        byte[] raidMarker = [0xF0, 0x0D, 0xBA, 0xBE];
        var raidPath = OutputPath(temp, SwShRoyalCandyWorkflowService.NestDataPath);
        var raidPack = SwShGfPackFile.Parse(File.ReadAllBytes(raidPath));
        raidPack.SetFileByName("unrelated.bin", raidMarker);
        File.WriteAllBytes(raidPath, raidPack.Write());

        byte[] placementMarker = [0xCA, 0xFE, 0xD0, 0x0D];
        var placementPath = OutputPath(temp, SwShRoyalCandyWorkflowService.PlacementPath);
        var placementPack = SwShGfPackFile.Parse(File.ReadAllBytes(placementPath));
        placementPack.SetFileByName("ObjectNameHashTable.tbl", placementMarker);
        File.WriteAllBytes(placementPath, placementPack.Write());

        var uninstallStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-uninstall",
            levelCaps: null,
            session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(raidPath));
        var restoredRaidPack = SwShGfPackFile.Parse(File.ReadAllBytes(raidPath));
        Assert.Equal(raidMarker, restoredRaidPack.GetFileByName("unrelated.bin"));
        AssertRaidRewardItems(
            restoredRaidPack,
            expectedDropItems: [50u, 777u],
            expectedBonusItems: [1128u, 50u, 1128u]);

        Assert.True(File.Exists(placementPath));
        var restoredPlacementPack = SwShGfPackFile.Parse(File.ReadAllBytes(placementPath));
        Assert.Equal(placementMarker, restoredPlacementPack.GetFileByName("ObjectNameHashTable.tbl"));
        AssertPlacementItems(
            temp,
            restoredPlacementPack,
            expectedFieldItemHash: RoyalCandyItemHash,
            expectedHiddenItemHashes: [RoyalCandyItemHash, RareCandyItemHash, UnrelatedItemHash]);
    }

    [Fact]
    public void InstalledRefreshRepairsLegacyRemovedShopSlotAndUninstallRestoresVanilla()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 1128, 51]))],
                []).Write());
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var shopPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Equal(
            [50, 50, 51],
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items);
        File.WriteAllBytes(
            shopPath,
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 51]))],
                []).Write());

        var refreshStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-unlimited",
            levelCaps: null,
            session: null);
        Assert.Equal(
            "installed",
            refreshStage.Workflow.Workflows
                .Single(workflow => workflow.WorkflowId == "royal-candy-unlimited")
                .Status);
        var refreshPlan = service.CreateChangePlan(temp.Paths, refreshStage.Session);
        var refresh = service.ApplyChangePlan(temp.Paths, refreshStage.Session, refreshPlan);

        Assert.DoesNotContain(refresh.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var refreshedItems =
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items;
        Assert.Equal([50, 50, 51], refreshedItems);
        Assert.Equal(3, refreshedItems.Count);

        var uninstallStage = service.StageWorkflow(
            temp.Paths,
            "royal-candy-uninstall",
            levelCaps: null,
            session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(shopPath));
    }

    [Fact]
    public void ValidateRejectsMalformedDirectSessionsAndReviewedCapsCannotDrift()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-story-limits", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var duplicated = stage.Session with
        {
            PendingEdits = [stage.Session.PendingEdits[0], stage.Session.PendingEdits[0]],
        };
        var duplicateValidation = service.Validate(temp.Paths, duplicated);
        Assert.False(duplicateValidation.IsValid);
        Assert.Empty(service.CreateChangePlan(temp.Paths, duplicated).Writes);

        var malformed = stage.Session with
        {
            PendingEdits =
            [
                stage.Session.PendingEdits[0] with
                {
                    Field = "mode",
                    NewValue = "storyLimits",
                    Sources = [],
                },
            ],
        };
        Assert.False(service.Validate(temp.Paths, malformed).IsValid);

        var reviewedPlan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.True(reviewedPlan.CanApply);
        var firstCaps = SwShRoyalCandyWorkflowService.CreateLevelCaps("Sword")
            .Select(cap => new SwShRoyalCandyLevelCapSelection(cap.Slot, Math.Min(100, cap.LevelCap + 1)))
            .ToArray();
        var restaged = service.StageWorkflow(
            temp.Paths,
            "royal-candy-story-limits",
            firstCaps,
            stage.Session);
        Assert.DoesNotContain(restaged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var apply = service.ApplyChangePlan(temp.Paths, restaged.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UninstallRestoresOwnedItemMappingAndPreservesUnrelatedItemEdits()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var itemPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemPath);
        var itemHashPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemHashPath);
        var userEditedItems = SwShItemTable.Parse(File.ReadAllBytes(itemPath)).WriteEdits(
            [new SwShItemTableEdit(1, SwShItemTableField.BuyPrice, 777)]);
        File.WriteAllBytes(itemPath, userEditedItems);

        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(itemPath));
        var restoredItems = SwShItemTable.Parse(File.ReadAllBytes(itemPath));
        Assert.Equal(777u, restoredItems.Records[1].BuyPrice);
        Assert.Equal(
            SwShItemTable.Parse(CreateCompactRoyalCandyItemTable()).Records[1128].RawRowIndex,
            restoredItems.Records[1128].RawRowIndex);
        Assert.False(File.Exists(itemHashPath));
    }

    [Fact]
    public void InstallRepairsExactLegacyItemHashNormalizationAndUninstallRemovesIt()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var baseBytes = CreateLegacyNormalizedItemHashBase();
        var exactLegacyOutput = SwShItemHashTable.Parse(baseBytes).Write();
        Assert.NotEqual(baseBytes, exactLegacyOutput);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            baseBytes);

        var itemHashPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemHashPath);
        Directory.CreateDirectory(Path.GetDirectoryName(itemHashPath)!);
        File.WriteAllBytes(itemHashPath, exactLegacyOutput);

        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);

        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(install.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("previously filtered item hash output", StringComparison.Ordinal));
        Assert.Equal(baseBytes, File.ReadAllBytes(itemHashPath));

        // Exercise cleanup's legacy ownership recognition independently of the repaired output.
        File.WriteAllBytes(itemHashPath, exactLegacyOutput);
        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(itemHashPath));
    }

    [Fact]
    public void UninstallPreservesUnrelatedStrictSubsetItemHashOutput()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var baseBytes = new SwShItemHashTable(
        [
            new SwShItemHashEntry(50, 0x1111111111111111),
            new SwShItemHashEntry(777, 0x3333333333333333),
            new SwShItemHashEntry(1128, 0x2222222222222222),
        ]).Write();
        var subsetBytes = new SwShItemHashTable(
        [
            new SwShItemHashEntry(50, 0x1111111111111111),
            new SwShItemHashEntry(1128, 0x2222222222222222),
        ]).Write();
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            baseBytes);

        var itemHashPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemHashPath);
        Directory.CreateDirectory(Path.GetDirectoryName(itemHashPath)!);
        File.WriteAllBytes(itemHashPath, subsetBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(subsetBytes, File.ReadAllBytes(itemHashPath));

        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(itemHashPath));
        Assert.Equal(subsetBytes, File.ReadAllBytes(itemHashPath));
    }

    [Fact]
    public void ApplyRejectsReviewedFreshInstallWhenBaseItemDataDrifts()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var baseItemPath = Path.Combine(
            temp.BaseRomFsPath,
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var layeredItemBytes = SwShItemTable.Parse(File.ReadAllBytes(baseItemPath)).WriteEdits(
            [new SwShItemTableEdit(1, SwShItemTableField.BuyPrice, 555)]);
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ItemPath, layeredItemBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var reviewedPlan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.True(reviewedPlan.CanApply);
        var itemWrite = Assert.Single(
            reviewedPlan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ItemPath);
        Assert.Contains(
            itemWrite.Sources,
            source => source.Layer == ProjectFileLayer.Base
                && source.RelativePath == SwShRoyalCandyWorkflowService.ItemPath);
        Assert.Contains(
            itemWrite.Sources,
            source => source.Layer == ProjectFileLayer.Layered
                && source.RelativePath == SwShRoyalCandyWorkflowService.ItemPath);

        var driftedBaseBytes = SwShItemTable.Parse(File.ReadAllBytes(baseItemPath)).WriteEdits(
            [new SwShItemTableEdit(2, SwShItemTableField.BuyPrice, 777)]);
        File.WriteAllBytes(baseItemPath, driftedBaseBytes);

        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(driftedBaseBytes, File.ReadAllBytes(baseItemPath));
        Assert.Equal(
            layeredItemBytes,
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.ItemPath)));
    }

    [Fact]
    public void FreshInstallRejectsChangedBaseItem1128OwnerSetWithoutAppending()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var baseBytes = CreateCompactRoyalCandyItemTable();
        var originalTable = SwShItemTable.Parse(baseBytes);
        var targetRowIndex = checked((ushort)originalTable.Records[1128].RawRowIndex);
        var item1RowIndex = checked((ushort)originalTable.Records[1].RawRowIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(
            baseBytes.AsSpan(0x44 + sizeof(ushort)),
            targetRowIndex);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            baseBytes);

        var layeredBytes = baseBytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            layeredBytes.AsSpan(0x44 + sizeof(ushort)),
            item1RowIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(
            layeredBytes.AsSpan(0x44 + (2 * sizeof(ushort))),
            targetRowIndex);
        var baseOwners = SwShItemTable.Parse(baseBytes).Records[1128].SharedItemIds;
        var layeredOwners = SwShItemTable.Parse(layeredBytes).Records[1128].SharedItemIds;
        Assert.Equal(baseOwners.Count, layeredOwners.Count);
        Assert.False(baseOwners.ToHashSet().SetEquals(layeredOwners));
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ItemPath, layeredBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);

        Assert.False(plan.CanApply);
        Assert.Contains(
            plan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("different owner set", StringComparison.Ordinal));
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(
            layeredBytes,
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.ItemPath)));
    }

    [Fact]
    public void ShiftedMultiShopInventoryCountFailsClosedAndPreservesSource()
    {
        const ulong multiShopHash = 0x5678;
        var baseShopData = new SwShShopDataFile(
            [],
            [new SwShMultiShopRecord(
                multiShopHash,
                [new SwShShopInventory([1, 2]), new SwShShopInventory([3, 1128, 4])])]);
        var shiftedShopData = new SwShShopDataFile(
            [],
            [new SwShMultiShopRecord(
                multiShopHash,
                [
                    new SwShShopInventory([999]),
                    new SwShShopInventory([1, 2]),
                    new SwShShopInventory([3, 1128, 4]),
                ])]);
        var mappingException = Assert.Throws<SwShRoyalCandyShopMappingException>(() =>
            SwShRoyalCandyShopPatchMapper.Analyze(shiftedShopData, baseShopData));
        Assert.Contains("different inventory count", mappingException.Message, StringComparison.Ordinal);

        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            baseShopData.Write());
        var shiftedBytes = shiftedShopData.Write();
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ShopDataPath, shiftedBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);

        Assert.False(plan.CanApply);
        Assert.Contains(
            plan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("different inventory count", StringComparison.Ordinal));
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(
            shiftedBytes,
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath)));
    }

    [Fact]
    public void AddedDuplicateRoyalCandyShopHashFailsClosedAndPreservesSource()
    {
        const ulong shopHash = 0x1234;
        var baseShopData = new SwShShopDataFile(
            [new SwShSingleShopRecord(shopHash, new SwShShopInventory([50, 1128]))],
            []);
        var duplicateShopData = new SwShShopDataFile(
            [
                new SwShSingleShopRecord(shopHash, new SwShShopInventory([50, 1128])),
                new SwShSingleShopRecord(shopHash, new SwShShopInventory([777])),
            ],
            []);
        var mappingException = Assert.Throws<SwShRoyalCandyShopMappingException>(() =>
            SwShRoyalCandyShopPatchMapper.Analyze(duplicateShopData, baseShopData));
        Assert.Contains("expected 1 physical occurrence", mappingException.Message, StringComparison.Ordinal);

        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            baseShopData.Write());
        var duplicateBytes = duplicateShopData.Write();
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ShopDataPath, duplicateBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);

        Assert.False(plan.CanApply);
        Assert.Contains(
            plan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("expected 1 physical occurrence", StringComparison.Ordinal));
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(
            duplicateBytes,
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath)));
    }

    [Fact]
    public void InstallAndUninstallPreservePreexistingInPlaceItem1128Edit()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var baseItemPath = Path.Combine(
            temp.BaseRomFsPath,
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseTable = SwShItemTable.Parse(File.ReadAllBytes(baseItemPath));
        var userEditedBytes = baseTable.WriteEdits(
            [new SwShItemTableEdit(1128, SwShItemTableField.BuyPrice, 777)]);
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ItemPath, userEditedBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);

        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var itemPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemPath);
        var installedTable = SwShItemTable.Parse(File.ReadAllBytes(itemPath));
        Assert.Equal(1u, installedTable.Records[1128].BuyPrice);
        Assert.NotEqual(
            SwShItemTable.Parse(userEditedBytes).Records[1128].RawRowIndex,
            installedTable.Records[1128].RawRowIndex);

        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(itemPath));
        Assert.Equal(userEditedBytes, File.ReadAllBytes(itemPath));
        Assert.Equal(777u, SwShItemTable.Parse(File.ReadAllBytes(itemPath)).Records[1128].BuyPrice);
    }

    [Fact]
    public void UninstallBlocksAtomicallyWhenActiveRoyalCandyItemRowIsMutated()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var itemPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ItemPath);
        var corruptedItemBytes = File.ReadAllBytes(itemPath);
        var activeRowIndex = SwShItemTable.Parse(corruptedItemBytes).Records[1128].RawRowIndex;
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(corruptedItemBytes.AsSpan(0x40));
        corruptedItemBytes[rowsStart + (activeRowIndex * 0x30) + 0x20] ^= 0x80;
        File.WriteAllBytes(itemPath, corruptedItemBytes);
        var outputsBefore = SnapshotOutputTree(temp.OutputRootPath);

        var stage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        Assert.Equal(
            "blocked",
            stage.Workflow.Workflows.Single(workflow => workflow.WorkflowId == "royal-candy-uninstall").Status);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(stage.Session.PendingEdits);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(temp.OutputRootPath, outputsBefore);
    }

    [Fact]
    public void UninstallBlocksAtomicallyWhenRoyalCandyShopMappingBecomesAmbiguous()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var shopPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        var installedShopData = SwShShopDataFile.Parse(File.ReadAllBytes(shopPath));
        var ambiguousShopBytes = new SwShShopDataFile(
            [.. installedShopData.SingleShops, installedShopData.SingleShops[0]],
            installedShopData.MultiShops).Write();
        Assert.Equal(2, SwShShopDataFile.Parse(ambiguousShopBytes).SingleShops.Count);
        File.WriteAllBytes(shopPath, ambiguousShopBytes);
        var outputsBefore = SnapshotOutputTree(temp.OutputRootPath);

        var stage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        Assert.Equal(
            "blocked",
            stage.Workflow.Workflows.Single(workflow => workflow.WorkflowId == "royal-candy-uninstall").Status);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(stage.Session.PendingEdits);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(temp.OutputRootPath, outputsBefore);
    }

    [Fact]
    public void UninstallReviewsAndRestoresCanonicalAndLegacyShopPathsTogether()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var canonicalBasePath = Path.Combine(
            temp.BaseRomFsPath,
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var baseShopBytes = File.ReadAllBytes(canonicalBasePath);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.LegacyShopDataPath["romfs/".Length..],
            baseShopBytes);

        var canonicalPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        var canonicalData = SwShShopDataFile.Parse(File.ReadAllBytes(canonicalPath));
        File.WriteAllBytes(
            canonicalPath,
            canonicalData.WriteEdits(
            [
                new SwShShopInventoryEdit(
                    SwShShopKind.Single,
                    0x1234,
                    InventoryIndex: 0,
                    Slot: 0,
                    ItemId: 0,
                    Action: SwShShopInventoryEditAction.Set,
                    Items: [777, 50],
                    ShopIndex: 0),
            ]));
        temp.WriteOutputFile(
            SwShRoyalCandyWorkflowService.LegacyShopDataPath,
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([888, 50]))],
                []).Write());

        var stage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(temp.Paths, stage.Session);

        Assert.True(plan.CanApply);
        Assert.Contains(
            plan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Contains(
            plan.Writes,
            write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.LegacyShopDataPath);
        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            [777, 50, 1128],
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(canonicalPath)).SingleShops).Inventory.Items);
        var legacyPath = OutputPath(temp, SwShRoyalCandyWorkflowService.LegacyShopDataPath);
        Assert.Equal(
            [888, 50, 1128],
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(legacyPath)).SingleShops).Inventory.Items);
    }

    [Fact]
    public void InstallAndUninstallPreserveShiftedShopInsertBeforeRoyalCandy()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 1128, 51]))],
                []).Write());
        temp.WriteOutputFile(
            SwShRoyalCandyWorkflowService.ShopDataPath,
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([777, 50, 1128, 51]))],
                []).Write());

        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);

        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var shopPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        Assert.Equal(
            [777, 50, 50, 51],
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items);
        Assert.Equal(
            4,
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items.Count);

        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(shopPath));
        Assert.Equal(
            [777, 50, 1128, 51],
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items);
    }

    [Fact]
    public void FreshInstallRejectsPreexistingMissingShopOccurrenceWithoutChangingSource()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 1128, 51]))],
                []).Write());
        var preexistingBytes = new SwShShopDataFile(
            [new SwShSingleShopRecord(0x1234, new SwShShopInventory([777, 50, 51]))],
            []).Write();
        temp.WriteOutputFile(SwShRoyalCandyWorkflowService.ShopDataPath, preexistingBytes);

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.False(plan.CanApply);
        Assert.Contains(
            plan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("already replaced or missing before this workflow was installed", StringComparison.Ordinal));

        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            preexistingBytes,
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath)));
    }

    [Fact]
    public void UninstallPreservesParseableOpaqueShopBytesAddedAfterInstall()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRoyalCandyBaseInputs(temp);
        var service = new SwShRoyalCandyEditSessionService();
        var installStage = service.StageWorkflow(temp.Paths, "royal-candy-unlimited", levelCaps: null, session: null);
        var installPlan = service.CreateChangePlan(temp.Paths, installStage.Session);
        var install = service.ApplyChangePlan(temp.Paths, installStage.Session, installPlan);
        Assert.DoesNotContain(install.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var shopPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        var installedBytes = File.ReadAllBytes(shopPath);
        byte[] opaqueMarker = [0xDE, 0xAD, 0xBE, 0xEF];
        var userEditedBytes = installedBytes.Concat(opaqueMarker).ToArray();
        Assert.Single(SwShShopDataFile.Parse(userEditedBytes).SingleShops);
        File.WriteAllBytes(shopPath, userEditedBytes);

        var uninstallStage = service.StageWorkflow(temp.Paths, "royal-candy-uninstall", levelCaps: null, session: null);
        var uninstallPlan = service.CreateChangePlan(temp.Paths, uninstallStage.Session);
        var uninstall = service.ApplyChangePlan(temp.Paths, uninstallStage.Session, uninstallPlan);

        Assert.DoesNotContain(uninstall.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(File.Exists(shopPath));
        var restoredBytes = File.ReadAllBytes(shopPath);
        Assert.Equal(opaqueMarker, restoredBytes.AsSpan(installedBytes.Length, opaqueMarker.Length).ToArray());
        Assert.Equal(
            [50, 1128],
            Assert.Single(SwShShopDataFile.Parse(restoredBytes).SingleShops).Inventory.Items);
    }

    [Fact]
    public void ShopMapperKeepsDuplicateMultiShopAndInventoryIdentity()
    {
        const ulong duplicateHash = 0xAABBCCDDEEFF0011;
        var baseData = new SwShShopDataFile(
            [],
            [
                new SwShMultiShopRecord(
                    duplicateHash,
                    [new SwShShopInventory([1, 1128, 2]), new SwShShopInventory([3, 4])]),
                new SwShMultiShopRecord(
                    duplicateHash,
                    [new SwShShopInventory([5, 6]), new SwShShopInventory([7, 1128, 8])]),
            ]);
        var targetData = new SwShShopDataFile(
            [],
            [
                new SwShMultiShopRecord(
                    duplicateHash,
                    [new SwShShopInventory([99, 1, 1128, 2]), new SwShShopInventory([3, 4])]),
                new SwShMultiShopRecord(
                    duplicateHash,
                    [new SwShShopInventory([5, 6]), new SwShShopInventory([7, 1128, 8, 100])]),
            ]);

        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(targetData, baseData);

        Assert.Equal(2, mapping.BaseOccurrences);
        Assert.Equal(2, mapping.OriginalOccurrences);
        Assert.Equal(0, mapping.OwnedReplacementOccurrences);
        Assert.Equal(0, mapping.LegacyMissingOccurrences);
        Assert.Collection(
            mapping.InstallEdits.OrderBy(edit => edit.ShopIndex),
            edit =>
            {
                Assert.Equal(SwShShopKind.Multi, edit.Kind);
                Assert.Equal(0, edit.ShopIndex);
                Assert.Equal(0, edit.InventoryIndex);
                Assert.Equal([99, 1, 50, 2], edit.Items);
            },
            edit =>
            {
                Assert.Equal(SwShShopKind.Multi, edit.Kind);
                Assert.Equal(1, edit.ShopIndex);
                Assert.Equal(1, edit.InventoryIndex);
                Assert.Equal([7, 50, 8, 100], edit.Items);
            });

        var installedData = SwShShopDataFile.Parse(targetData.Write().AsSpan()).WriteEdits(mapping.InstallEdits);
        var restoreMapping = SwShRoyalCandyShopPatchMapper.Analyze(SwShShopDataFile.Parse(installedData), baseData);
        Assert.Equal(2, restoreMapping.OwnedReplacementOccurrences);
        var restoredData = SwShShopDataFile.Parse(
            SwShShopDataFile.Parse(installedData).WriteEdits(restoreMapping.UninstallEdits));
        Assert.Equal([99, 1, 1128, 2], restoredData.MultiShops[0].Inventories[0].Items);
        Assert.Equal([7, 1128, 8, 100], restoredData.MultiShops[1].Inventories[1].Items);
    }

    private static void AssertInstalledAcquisitionOutputs(
        TemporarySwShProject temp,
        IReadOnlyList<int> expectedShopItems)
    {
        var shopPath = OutputPath(temp, SwShRoyalCandyWorkflowService.ShopDataPath);
        var shopItems =
            Assert.Single(SwShShopDataFile.Parse(File.ReadAllBytes(shopPath)).SingleShops).Inventory.Items;
        Assert.Equal(expectedShopItems.Count, shopItems.Count);
        Assert.Equal(expectedShopItems, shopItems);

        var raidPack = SwShGfPackFile.Parse(
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.NestDataPath)));
        Assert.Equal(RaidUnrelatedMemberData, raidPack.GetFileByName("unrelated.bin"));
        AssertRaidRewardItems(
            raidPack,
            expectedDropItems: [50u, 777u],
            expectedBonusItems: [50u, 50u, 50u]);

        var placementPack = SwShGfPackFile.Parse(
            File.ReadAllBytes(OutputPath(temp, SwShRoyalCandyWorkflowService.PlacementPath)));
        var basePlacementPack = SwShGfPackFile.Parse(
            File.ReadAllBytes(Path.Combine(
                temp.BaseRomFsPath,
                SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..]
                    .Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(
            basePlacementPack.GetFileByName("ObjectNameHashTable.tbl"),
            placementPack.GetFileByName("ObjectNameHashTable.tbl"));
        AssertPlacementItems(
            temp,
            placementPack,
            expectedFieldItemHash: RareCandyItemHash,
            expectedHiddenItemHashes: [RareCandyItemHash, RareCandyItemHash, UnrelatedItemHash]);
    }

    private static void AssertValidAcquisitionOwnershipManifest(TemporarySwShProject temp)
    {
        var manifestPath = OutputPath(
            temp,
            SwShRoyalCandyWorkflowService.AcquisitionOwnershipManifestPath);
        Assert.True(File.Exists(manifestPath));
        var inputs = SwShRoyalCandyAcquisitionOwnershipService.ReadAuthoritativeInputs(temp.Paths);
        _ = SwShRoyalCandyAcquisitionOwnershipManifest.ParseAndValidate(
            File.ReadAllBytes(manifestPath),
            inputs.ShopRelativePath,
            inputs.BaseShopBytes,
            inputs.BaseNestBytes,
            inputs.BasePlacementBytes,
            inputs.BaseItemHashBytes);
    }

    private static void AssertRaidRewardItems(
        SwShGfPackFile pack,
        IReadOnlyList<uint> expectedDropItems,
        IReadOnlyList<uint> expectedBonusItems)
    {
        var dropItems = SwShNestHoleRewardArchive.Parse(
                pack.GetFileByName("nest_hole_drop_rewards.bin"))
            .Tables
            .SelectMany(table => table.Rewards)
            .Select(reward => reward.ItemId)
            .ToArray();
        var bonusItems = SwShNestHoleRewardArchive.Parse(
                pack.GetFileByName("nest_hole_bonus_rewards.bin"))
            .Tables
            .SelectMany(table => table.Rewards)
            .Select(reward => reward.ItemId)
            .ToArray();

        Assert.Equal(expectedDropItems, dropItems);
        Assert.Equal(expectedBonusItems, bonusItems);
    }

    private static void AssertPlacementItems(
        TemporarySwShProject temp,
        SwShGfPackFile pack,
        ulong expectedFieldItemHash,
        IReadOnlyList<ulong> expectedHiddenItemHashes)
    {
        var itemHashPath = Path.Combine(
            temp.BaseRomFsPath,
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var itemIdsByHash = SwShItemHashTable.Parse(File.ReadAllBytes(itemHashPath)).ToItemIdByHash();
        var archive = SwShPlacementZoneArchive.Parse(
            pack.GetFileByName(SwShPlacementTestFixtures.AreaMember),
            itemIdsByHash);
        var zone = Assert.Single(archive.Zones);
        var fieldItem = Assert.Single(zone.FieldItems);
        Assert.Equal(expectedFieldItemHash, Assert.Single(fieldItem.ItemHashes));
        var hiddenItem = Assert.Single(zone.HiddenItems);
        Assert.Equal(
            expectedHiddenItemHashes,
            hiddenItem.Chances.Select(chance => chance.ItemHash).ToArray());
    }

    private static byte[] CreateRoyalCandyRaidRewardPack()
    {
        var dropArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                0xAABBCCDD00112233,
                [
                    new SwShNestHoleReward(100, 50, [1, 2, 3, 4, 5]),
                    new SwShNestHoleReward(101, 777, [5, 4, 3, 2, 1]),
                ]),
        ]);
        var bonusArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                0x1020304050607080,
                [
                    new SwShNestHoleReward(200, 1128, [1, 1, 1, 1, 1]),
                    new SwShNestHoleReward(201, 50, [2, 2, 2, 2, 2]),
                ]),
            new SwShNestHoleRewardTable(
                0x1020304050607081,
                [
                    new SwShNestHoleReward(300, 1128, [3, 3, 3, 3, 3]),
                ]),
        ]);

        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", dropArchive.Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", bonusArchive.Write()),
            new SwShGfPackNamedFile("unrelated.bin", RaidUnrelatedMemberData),
        ]).Write();
    }

    private static byte[] CreateRoyalCandyPlacementPack()
    {
        return SwShPlacementTestFixtures.CreatePlacementPack(
            fieldItemHash: RoyalCandyItemHash,
            hiddenItemChances:
            [
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 0,
                    ItemHash: RoyalCandyItemHash,
                    ItemId: 1128,
                    Chance: 50,
                    Quantity: 1,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 1,
                    ItemHash: RareCandyItemHash,
                    ItemId: 50,
                    Chance: 30,
                    Quantity: 2,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 2,
                    ItemHash: UnrelatedItemHash,
                    ItemId: 777,
                    Chance: 20,
                    Quantity: 3,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
            ]);
    }

    private static byte[] CreateLegacyNormalizedItemHashBase()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(3);
        writer.Write(0x1111111111111111UL);
        writer.Write(50);
        writer.Write(unchecked((int)0xA1A2A3A4));
        writer.Write(0x9999999999999999UL);
        writer.Write(0);
        writer.Write(unchecked((int)0xB1B2B3B4));
        writer.Write(0x2222222222222222UL);
        writer.Write(1128);
        writer.Write(unchecked((int)0xC1C2C3C4));
        writer.Write([0xDE, 0xAD, 0xBE, 0xEF]);
        return stream.ToArray();
    }

    private static void WriteRoyalCandyBaseInputs(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemPath["romfs/".Length..],
            CreateCompactRoyalCandyItemTable());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ItemHashPath["romfs/".Length..],
            new SwShItemHashTable(
            [
                new SwShItemHashEntry(50, RareCandyItemHash),
                new SwShItemHashEntry(777, UnrelatedItemHash),
                new SwShItemHashEntry(1128, RoyalCandyItemHash),
            ]).Write());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1234, new SwShShopInventory([50, 1128]))],
                []).Write());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.NestDataPath["romfs/".Length..],
            CreateRoyalCandyRaidRewardPack());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.PlacementPath["romfs/".Length..],
            CreateRoyalCandyPlacementPack());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.BagEventScriptPath["romfs/".Length..],
            CreateRoyalCandyBagEventScript());
        WriteLanguageTextSet(temp, "English");
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(0x0100ABF008968000));
        InstallEmptyBagHook(temp);
    }

    private static SwShRoyalCandyStoryLevelCap[] CreateStoryCaps(string gameFlavor)
    {
        return SwShRoyalCandyWorkflowService.CreateLevelCaps(gameFlavor)
            .Select(cap => new SwShRoyalCandyStoryLevelCap(
                cap.LevelCap,
                ulong.Parse(cap.ProgressHash[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                cap.Label,
                cap.ProgressKind == "workAtLeast"
                    ? SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                    : SwShRoyalCandyStoryLevelCapProgressKind.Flag,
                cap.WorkMinimum ?? 0))
            .ToArray();
    }

    private static void WriteLanguageTextSet(TemporarySwShProject temp, string language)
    {
        var text = CreateTextTable(1128, (50, "Rare Candy"), (1128, "Exp. Candy XL"));
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/iteminfo.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname_acc_classified.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname_acc.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname_classified.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname_plural_classified.dat", text);
        temp.WriteBaseRomFsFile($"bin/message/{language}/common/itemname_plural.dat", text);
    }

    private static void InstallEmptyBagHook(TemporarySwShProject temp)
    {
        var service = new SwShBagHookEditSessionService();
        var stage = service.StageInstall(temp.Paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(temp.Paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string OutputPath(TemporarySwShProject temp, string relativePath)
    {
        return Path.Combine(
            temp.OutputRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotOutputTree(string outputRootPath)
    {
        return Directory.EnumerateFiles(outputRootPath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(outputRootPath, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertOutputTreeMatches(
        string outputRootPath,
        IReadOnlyDictionary<string, byte[]> expected)
    {
        var actualPaths = Directory.EnumerateFiles(outputRootPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputRootPath, path).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expected.Keys.Order(StringComparer.OrdinalIgnoreCase), actualPaths);
        Assert.All(
            expected,
            output => Assert.Equal(
                output.Value,
                File.ReadAllBytes(Path.Combine(
                    outputRootPath,
                    output.Key.Replace('/', Path.DirectorySeparatorChar)))));
    }

    private static byte[] CreateCompactRoyalCandyItemTable()
    {
        var records = Enumerable.Range(0, 1129)
            .Select(itemId => new ItemFixtureRecord(
                itemId,
                itemId == 50 || itemId == 1128 ? 50 : 0,
                0,
                0,
                0,
                SwShItemPouch.Medicine))
            .ToArray();

        return SwShItemTestFixtures.CreateItemTable(records);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine(
                entries.FirstOrDefault(entry => entry.index == index).value ?? string.Empty,
                Flags: 0))
            .ToArray();

        return SwShGameTextFile.Write(lines);
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateCompatibleNso(ProjectGame game = ProjectGame.Sword)
    {
        return CreateNso(CreateCompatibleText(game), [0x10], [0x20], game);
    }

    private static byte[] CreateCompatibleText(ProjectGame game)
    {
        var text = new byte[0x01421100];
        WriteInstruction(text, 0x00747988, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074798C, EncodeConditionalBranch(0x0074798C, 0x00747A80, Arm64Condition.NE));
        WriteInstruction(text, 0x00747D44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747D48, EncodeConditionalBranch(0x00747D48, 0x007477E8, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BA24, EncodeCmpImmediate(26, 50));
        WriteInstruction(text, 0x0074BA28, EncodeConditionalBranch(0x0074BA28, 0x0074BAD4, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BDA8, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BDAC, EncodeConditionalBranch(0x0074BDAC, 0x0074B788, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFE4, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFE8, EncodeConditionalBranch(0x0074DFE8, 0x0074DE78, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFF8, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074DFFC, EncodeConditionalBranch(0x0074DFFC, 0x0074E16C, Arm64Condition.NE));
        WriteInstruction(text, 0x0075CEFC, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0075CF00, EncodeConditionalBranch(0x0075CF00, 0x0075CC18, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB204, EncodeCmpImmediate(20, 50));
        WriteInstruction(text, 0x007BB208, EncodeConditionalBranch(0x007BB208, 0x007BB26C, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB3C0, EncodeCmpImmediate(19, 50));
        WriteInstruction(text, 0x007BB3C4, EncodeConditionalBranch(0x007BB3C4, 0x007BB3EC, Arm64Condition.NE));
        WriteInstruction(text, 0x007BC1F8, EncodeCmpImmediate(8, 50));
        WriteInstruction(text, 0x007BC1FC, EncodeConditionalBranch(0x007BC1FC, 0x007BC2B4, Arm64Condition.NE));
        WriteInstruction(text, 0x00747DE0, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747DE4, EncodeConditionalBranch(0x00747DE4, 0x00747D4C, Arm64Condition.EQ));
        WriteInstruction(text, 0x0074BE44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BE48, EncodeConditionalBranch(0x0074BE48, 0x0074BDB0, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075CCE8, EncodeCmpImmediate(27, 50));
        WriteInstruction(text, 0x0075CCEC, EncodeConditionalBranch(0x0075CCEC, 0x0075D064, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075D08C, EncodeCmpImmediate(10, 50));
        WriteInstruction(text, 0x0075D090, EncodeConditionalBranch(0x0075D090, 0x0075D05C, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BBFD4, EncodeCmpImmediate(23, 50));
        WriteInstruction(text, 0x007BBFD8, EncodeConditionalBranch(0x007BBFD8, 0x007BC054, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007BAF38, 0x6B36231F);
        WriteInstruction(text, 0x007BAF3C, 0x1A963316);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        WriteInstruction(text, 0x007DDA90, EncodeConditionalBranch(0x007DDA90, 0x007DDAF8, Arm64Condition.HI));
        WriteInstruction(text, game == ProjectGame.Shield ? 0x01420F20 : 0x01420EF0, 0xF81D0FF5);
        WriteInstruction(text, game == ProjectGame.Shield ? 0x014210C0 : 0x01421090, 0xA9BE4FF4);
        return text;
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        var imm19 = delta >> 2;
        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static byte[] CreateRoyalCandyBagEventScript()
    {
        const ushort pawnMagic64 = 0xF1E1;
        const short pawnFlagCompact = 0x0004;
        const short defSize = 12;
        const int cellSize = 8;
        const int nativeCount = 77;
        const int natives = 0x38;
        const int libraries = natives + nativeCount * defSize;
        const int cod = libraries;
        const int codeCellCount = 5022;
        const uint duplicatedNativeHash = 0x0473BE4E;

        var prefix = new byte[cod];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 70 * defSize + 8), duplicatedNativeHash);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 76 * defSize + 8), duplicatedNativeHash);

        var cells = new ulong[codeCellCount];
        cells[3686] = 135;
        cells[3687] = 70;
        cells[3688] = 8;
        cells[4991] = 46;
        cells[4992] = 89;
        cells[4993] = 48;
        cells[5020] = 49;
        cells[5021] = unchecked((ulong)((4991 - 5020) * cellSize));

        var compactCode = CompactAmxCells(cells);
        var data = new byte[cod + compactCode.Length];
        Array.Copy(prefix, data, prefix.Length);
        Array.Copy(compactCode, 0, data, cod, compactCode.Length);

        var dat = cod + codeCellCount * cellSize;
        WriteAmxHeaderFields(
            data,
            size: data.Length,
            magic: pawnMagic64,
            flags: pawnFlagCompact,
            defSize: defSize,
            cod: cod,
            dat: dat,
            hea: dat,
            stp: dat,
            publics: natives,
            natives: natives,
            libraries: libraries,
            nameTable: libraries);
        return data;
    }

    private static byte[] CompactAmxCells(IEnumerable<ulong> cells)
    {
        var compact = new List<byte>();
        foreach (var cell in cells)
        {
            var value = unchecked((long)cell);
            var chunks = new List<byte>();
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private static void WriteAmxHeaderFields(
        byte[] data,
        int size,
        ushort magic,
        short flags,
        short defSize,
        int cod,
        int dat,
        int hea,
        int stp,
        int publics,
        int natives,
        int libraries,
        int nameTable)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), magic);
        data[0x06] = 11;
        data[0x07] = 11;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), defSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), nameTable);
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, ProjectGame game)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        Convert.FromHexString(game == ProjectGame.Shield
                ? "A16802625E7826BF83B6F9708E475B912A9AB7DF"
                : "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471")
            .CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        HI = 8,
    }
}
