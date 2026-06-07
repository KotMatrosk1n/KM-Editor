// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.ExeFs;

public sealed class SwShExeFsPatchWorkflowServiceTests
{
    [Fact]
    public void LoadReadsExeFsPatchesFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile(
            "kmeditor/exefs.patches.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "patches": [
                {
                  "patchId": "sample_patch",
                  "name": "Sample ExeFS Patch",
                  "targetFile": "exefs/main",
                  "patchKind": "IPS",
                  "status": "available",
                  "description": "Enable a safe ExeFS patch fixture."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var patch = Assert.Single(workflow.Patches);
        Assert.Equal("sample_patch", patch.PatchId);
        Assert.Equal("Sample ExeFS Patch", patch.Name);
        Assert.Equal("exefs/main", patch.TargetFile);
        Assert.Equal("IPS", patch.PatchKind);
        Assert.Equal("available", patch.Status);
        Assert.Equal(ProjectFileLayer.Base, patch.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, patch.Provenance.FileState);
        Assert.Equal("exefs/kmeditor/exefs.patches.readmodel.json", patch.Provenance.SourceFile);
        Assert.Equal(1, workflow.Stats.TotalPatchCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Empty(workflow.Patches);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.exefsPatches");
    }

    [Fact]
    public void LoadWarnsWhenPatchIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile(
            "kmeditor/exefs.patches.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "patches": [
                {
                  "patchId": "sample_patch",
                  "name": "Sample ExeFS Patch",
                  "targetFile": "exefs/main",
                  "patchKind": "IPS",
                  "status": "available",
                  "description": "Enable a safe ExeFS patch fixture."
                },
                {
                  "patchId": "sample_patch",
                  "name": "Duplicate ExeFS Patch",
                  "targetFile": "exefs/main",
                  "patchKind": "IPS",
                  "status": "available",
                  "description": "Duplicate fixture."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShExeFsPatchWorkflowService().Load(project);

        Assert.Equal(2, workflow.Patches.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.exefsPatches");
    }
}
