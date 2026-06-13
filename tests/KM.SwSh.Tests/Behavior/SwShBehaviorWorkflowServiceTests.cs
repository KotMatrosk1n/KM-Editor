// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Behavior;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Behavior;

public sealed class SwShBehaviorWorkflowServiceTests
{
    [Fact]
    public void LoadReadsBehaviorEntriesFromSymbolBehaviorFile()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShBehaviorWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Stats.TotalEntryCount);
        Assert.Equal(2, workflow.Stats.TotalBehaviorCount);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
        Assert.Contains(
            workflow.Fields,
            field => field.Field == SwShSymbolBehaviorArchive.BehaviorField && !field.IsReadOnly);
        Assert.Contains(
            workflow.Fields,
            field => field.Field == SwShSymbolBehaviorArchive.Hash1Field && field.IsReadOnly);

        var pikachu = workflow.Entries.Single(entry => entry.SpeciesId == 25);
        Assert.Equal("Pikachu", pikachu.SpeciesName);
        Assert.Equal("Common - standard wild movement behavior", pikachu.BehaviorLabel);
        Assert.Equal("body", pikachu.ModelPart);
        Assert.Equal(1.5, pikachu.HitboxRadius);
        Assert.Equal("0x0102030405060708", pikachu.Hash1);
        Assert.Equal(ProjectFileLayer.Base, pikachu.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, pikachu.Provenance.FileState);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenBehaviorFileIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", []);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShBehaviorWorkflowService().Load(project);

        Assert.Empty(workflow.Entries);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.behavior"
                && diagnostic.Expected == SwShBehaviorWorkflowService.BehaviorDataPath);
    }

    [Fact]
    public void LoadLabelsRawBehaviorFieldsWithMappingStatus()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShBehaviorWorkflowService().Load(project);

        Assert.DoesNotContain(
            workflow.Fields,
            field => field.Label.StartsWith("Behavior Parameter", StringComparison.Ordinal));

        var waterOffset = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.Field21);
        Assert.Equal("Likely Water Height Offset", waterOffset.Label);
        Assert.Equal("Model / Offset", waterOffset.Group);
        Assert.True(waterOffset.IsReadOnly);
        Assert.Contains("GetOffsetWaterParam", waterOffset.Description);

        var watchDistance = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.Field44);
        Assert.Equal("Likely Watch Distance", watchDistance.Label);
        Assert.Equal("Watch / Reaction", watchDistance.Group);
        Assert.True(watchDistance.IsReadOnly);
        Assert.Contains("watch-out distance", watchDistance.Description);

        var unusedDefault = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.Field08);
        Assert.Equal("Unused Default 08", unusedDefault.Label);
        Assert.Equal("Unused Defaults", unusedDefault.Group);
        Assert.True(unusedDefault.IsReadOnly);
    }
}
