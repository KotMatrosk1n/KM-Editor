// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Behavior;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
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
        Assert.Equal(3, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
        Assert.Contains(
            workflow.Fields,
            field => field.Field == SwShSymbolBehaviorArchive.BehaviorField && !field.IsReadOnly);
        Assert.Contains(
            workflow.Fields,
            field => field.Field == SwShSymbolBehaviorArchive.Hash1Field && field.IsReadOnly);

        var pikachu = workflow.Entries.Single(entry => entry.SpeciesId == 25);
        Assert.Matches("^behavior:0:[0-9A-F]{64}$", pikachu.EntryId);
        Assert.Equal("Pikachu", pikachu.SpeciesName);
        Assert.Equal("Common - standard wild movement behavior", pikachu.BehaviorLabel);
        Assert.Equal("body", pikachu.ModelPart);
        Assert.Equal(1.5, pikachu.HitboxRadius);
        Assert.Equal("0x0102030405060708", pikachu.Hash1);
        Assert.Equal(ProjectFileLayer.Base, pikachu.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, pikachu.Provenance.FileState);
        Assert.Collection(
            pikachu.FormOptions,
            option => Assert.Equal("0", option.Value));

        var eevee = workflow.Entries.Single(entry => entry.SpeciesId == 133);
        Assert.Matches("^behavior:1:[0-9A-F]{64}$", eevee.EntryId);
        Assert.Equal(["0", "1"], eevee.FormOptions.Select(option => option.Value));

        var species = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.SpeciesIdField);
        Assert.Equal(["1", "25", "133"], species.Options.Select(option => option.Value));
        var behavior = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.BehaviorField);
        Assert.Equal(["Common", "WaterDash"], behavior.Options.Select(option => option.Value));
        var modelPart = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.ModelPartField);
        Assert.Equal(["body", "head"], modelPart.Options.Select(option => option.Value));
    }

    [Theory]
    [InlineData(SwShSymbolBehaviorArchive.HitboxRadiusField, "NaN")]
    [InlineData(SwShSymbolBehaviorArchive.GrassShakeRadiusField, "Infinity")]
    public void LoadRejectsNonFiniteEditableRadius(string field, string value)
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var source = SwShBehaviorTestFixtures.CreateBehaviorArchive();
        var nonFinite = value == "NaN" ? float.NaN : float.PositiveInfinity;
        var original = field == SwShSymbolBehaviorArchive.HitboxRadiusField
            ? source.Entries[0].HitboxRadius
            : source.Entries[0].GrassShakeRadius;
        var corruptArchive = source.Write();
        PatchUniqueSingle(corruptArchive, original, nonFinite);
        temp.WriteBaseRomFsFile(
            SwShBehaviorWorkflowService.BehaviorDataPath["romfs/".Length..],
            corruptArchive);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShBehaviorWorkflowService().Load(project);

        Assert.Empty(workflow.Entries);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Domain == "workflow.behavior"
            && diagnostic.Message.Contains("non-finite", StringComparison.OrdinalIgnoreCase));
    }

    private static void PatchUniqueSingle(byte[] data, float original, float replacement)
    {
        Span<byte> pattern = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(pattern, BitConverter.SingleToInt32Bits(original));
        var offset = data.AsSpan().IndexOf(pattern);
        Assert.True(offset >= 0, $"Fixture does not contain float {original}.");
        Assert.Equal(-1, data.AsSpan(offset + 1).IndexOf(pattern));
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(replacement));
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

        var rarePositionOffset = workflow.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.Field07);
        Assert.Equal("Likely Rare Position Offset", rarePositionOffset.Label);

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
