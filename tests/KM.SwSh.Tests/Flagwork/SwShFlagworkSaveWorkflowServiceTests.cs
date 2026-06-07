// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Flagwork;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Flagwork;

public sealed class SwShFlagworkSaveWorkflowServiceTests
{
    [Fact]
    public void LoadReadsFlagAndSaveInspectorRecordsFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/flagwork.save.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "flags": [
                {
                  "flagId": "story.badge_1",
                  "name": "Badge 1 Obtained",
                  "category": "Story",
                  "valueKind": "boolean",
                  "defaultValue": "false",
                  "description": "First gym badge story flag."
                }
              ],
              "saveBlocks": [
                {
                  "blockId": "player.profile",
                  "name": "Player Profile",
                  "offset": 128,
                  "length": 64,
                  "description": "Player profile save block."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var flag = Assert.Single(workflow.Flags);
        Assert.Equal("story.badge_1", flag.FlagId);
        Assert.Equal("Badge 1 Obtained", flag.Name);
        Assert.Equal("Story", flag.Category);
        Assert.Equal("boolean", flag.ValueKind);
        Assert.Equal("false", flag.DefaultValue);
        Assert.Equal(ProjectFileLayer.Base, flag.Provenance.SourceLayer);
        var saveBlock = Assert.Single(workflow.SaveBlocks);
        Assert.Equal("player.profile", saveBlock.BlockId);
        Assert.Equal("Player Profile", saveBlock.Name);
        Assert.Equal(128, saveBlock.Offset);
        Assert.Equal(64, saveBlock.Length);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, saveBlock.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalFlagCount);
        Assert.Equal(1, workflow.Stats.TotalSaveBlockCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/flags.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Empty(workflow.Flags);
        Assert.Empty(workflow.SaveBlocks);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.flagworkSave");
    }

    [Fact]
    public void LoadWarnsWhenFlagIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/flagwork.save.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "flags": [
                {
                  "flagId": "story.badge_1",
                  "name": "Badge 1 Obtained",
                  "category": "Story",
                  "valueKind": "boolean",
                  "defaultValue": "false",
                  "description": "First gym badge story flag."
                },
                {
                  "flagId": "story.badge_1",
                  "name": "Duplicate Badge 1",
                  "category": "Story",
                  "valueKind": "boolean",
                  "defaultValue": "false",
                  "description": "Duplicate flag fixture."
                }
              ],
              "saveBlocks": []
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShFlagworkSaveWorkflowService().Load(project);

        Assert.Equal(2, workflow.Flags.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.flagworkSave");
    }
}
