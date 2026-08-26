// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.ExeFs;
using KM.ZA.TypeChart;
using System.Security;

namespace KM.ZA.GameModules;

public sealed record ZaTypeEffectivenessStateSource(
    string RelativePath,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaTypeEffectivenessStateType(
    int TypeIndex,
    string Label,
    string ShortLabel);

public sealed record ZaTypeEffectivenessStateCell(
    int AttackTypeIndex,
    int DefenseTypeIndex,
    int CurrentValue,
    int BaseValue);

public sealed record ZaTypeEffectivenessState(
    string BuildId,
    string ChartOffsetHex,
    ZaTypeEffectivenessStateSource BaseSource,
    ZaTypeEffectivenessStateSource EffectiveSource,
    IReadOnlyList<ZaTypeEffectivenessStateType> Types,
    IReadOnlyList<ZaTypeEffectivenessStateCell> Cells,
    int DifferenceCount);

public sealed class ZaTypeEffectivenessStateService
{
    private readonly Func<string, byte[]> readExecutableBytes;

    public ZaTypeEffectivenessStateService(Func<string, byte[]> readExecutableBytes)
    {
        this.readExecutableBytes = readExecutableBytes
            ?? throw new ArgumentNullException(nameof(readExecutableBytes));
    }

    public ZaTypeEffectivenessState Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        try
        {
            return LoadCore(project);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or ArgumentException
                or NotSupportedException
                or OverflowException)
        {
            throw new InvalidDataException(
                "Type Effectiveness State could not read or verify its bounded executable sources.",
                exception);
        }
    }

    private ZaTypeEffectivenessState LoadCore(OpenedProject project)
    {
        if (project.Paths.SelectedGame is not ProjectGame.ZA
            || !project.Health.CanOpenReadOnlyWorkflows)
        {
            throw new InvalidDataException(
                "Type Effectiveness State requires a readable Z-A project with an explicit game binding.");
        }

        var baseMain = ZaExeFsMainFileResolver.ResolveBase(project)
            ?? throw new InvalidDataException(
                "Type Effectiveness State requires a readable base exefs/main.");
        var effectiveMain = ZaExeFsMainFileResolver.ResolveEffective(project)
            ?? throw new InvalidDataException(
                "Type Effectiveness State requires a readable effective exefs/main.");
        ValidateSourceMetadata(baseMain, isBaseSource: true);
        ValidateSourceMetadata(effectiveMain, isBaseSource: false);

        var baseAnalysis = Analyze(baseMain, project.Paths.SelectedGame);
        var effectiveAnalysis = Analyze(effectiveMain, project.Paths.SelectedGame);
        if (!string.Equals(
                baseAnalysis.BuildId,
                effectiveAnalysis.BuildId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                baseAnalysis.ChartOffsetHex,
                effectiveAnalysis.ChartOffsetHex,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Type Effectiveness State found different verified builds or chart offsets in the base and effective executable sources.");
        }

        ZaTypeChartMainPatcher.ValidateValues(baseAnalysis.EffectivenessValues);
        ZaTypeChartMainPatcher.ValidateValues(effectiveAnalysis.EffectivenessValues);
        var types = ZaTypeChartWorkflowService.Types
            .OrderBy(type => type.TypeIndex)
            .Select(type => new ZaTypeEffectivenessStateType(
                type.TypeIndex,
                type.Label,
                type.ShortLabel))
            .ToArray();
        if (types.Length != ZaTypeChartMainPatcher.TypeCount
            || !types.Select(type => type.TypeIndex)
                .SequenceEqual(Enumerable.Range(0, ZaTypeChartMainPatcher.TypeCount))
            || types.Any(type =>
                string.IsNullOrWhiteSpace(type.Label)
                || string.IsNullOrWhiteSpace(type.ShortLabel)
                || type.Label.Length > 64
                || type.ShortLabel.Length > 16))
        {
            throw new InvalidDataException(
                "Type Effectiveness State type definitions are incomplete or out of order.");
        }

        var current = ZaTypeChartWorkflowService.ToDisplayOrder(
            effectiveAnalysis.EffectivenessValues);
        var baseline = ZaTypeChartWorkflowService.ToDisplayOrder(
            baseAnalysis.EffectivenessValues);
        var cells = Enumerable.Range(0, ZaTypeChartMainPatcher.ChartLength)
            .Select(index => new ZaTypeEffectivenessStateCell(
                index / ZaTypeChartMainPatcher.TypeCount,
                index % ZaTypeChartMainPatcher.TypeCount,
                current[index],
                baseline[index]))
            .ToArray();

        return new ZaTypeEffectivenessState(
            effectiveAnalysis.BuildId,
            effectiveAnalysis.ChartOffsetHex,
            ToSource(baseMain),
            ToSource(effectiveMain),
            types,
            cells,
            cells.Count(cell => cell.CurrentValue != cell.BaseValue));
    }

    private ZaTypeChartMainAnalysis Analyze(
        ZaExeFsMainFile source,
        ProjectGame? selectedGame)
    {
        var analysis = ZaTypeChartMainPatcher.Analyze(
            readExecutableBytes(source.AbsolutePath),
            selectedGame);
        if (analysis.Kind is not ZaTypeChartMainKind.Vanilla
            and not ZaTypeChartMainKind.Modified
            || analysis.ChartOffset is null
            || analysis.ChartOffset.Value != ZaTypeChartMainPatcher.RoChartOffset
            || analysis.BuildId is not { Length: 40 }
            || analysis.BuildId.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException(
                "Type Effectiveness State could not prove the recognized build and exact 18x18 table boundary.");
        }

        return analysis;
    }

    private static ZaTypeEffectivenessStateSource ToSource(ZaExeFsMainFile source)
    {
        return new ZaTypeEffectivenessStateSource(
            source.Reference.RelativePath,
            source.Reference.Layer,
            source.FileState);
    }

    private static void ValidateSourceMetadata(
        ZaExeFsMainFile source,
        bool isBaseSource)
    {
        var isExpectedLayer = isBaseSource
            ? source.Reference.Layer == ProjectFileLayer.Base
            : source.Reference.Layer is ProjectFileLayer.Base or ProjectFileLayer.Layered;
        var isExpectedState = source.Reference.Layer switch
        {
            ProjectFileLayer.Base => source.FileState is
                ProjectFileGraphEntryState.BaseOnly
                or ProjectFileGraphEntryState.LayeredOverride,
            ProjectFileLayer.Layered =>
                source.FileState == ProjectFileGraphEntryState.LayeredOverride,
            _ => false,
        };
        if (!isExpectedLayer
            || !isExpectedState
            || !string.Equals(
                source.Reference.RelativePath,
                ZaExeFsReservedRegionLedger.ExeFsMainPath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Type Effectiveness State received inconsistent executable source metadata.");
        }
    }
}
