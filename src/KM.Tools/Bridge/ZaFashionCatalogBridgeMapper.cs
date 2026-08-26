// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.FashionCatalog;
using KM.Api.Workflows;
using KM.ZA.FashionCatalog;
using KM.ZA.Workflows;

namespace KM.Tools.Bridge;

public static class ZaFashionCatalogBridgeMapper
{
    public static LoadFashionCatalogWorkflowResponse ToDto(ZaFashionCatalogWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return new LoadFashionCatalogWorkflowResponse(ToWorkflowDto(workflow));
    }

    public static StageFashionCatalogFieldEditResponse ToDto(
        ZaFashionCatalogStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new StageFashionCatalogFieldEditResponse(
            ToWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ZaFashionCatalogFieldEdit ToCore(
        FashionCatalogFileDto catalogFile,
        FashionCatalogRowBindingDto binding,
        string field,
        string? value,
        bool clear)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new ZaFashionCatalogFieldEdit(
            catalogFile switch
            {
                FashionCatalogFileDto.DressUpItems => ZaFashionCatalogFile.DressUpItems,
                FashionCatalogFileDto.DressUpGroups => ZaFashionCatalogFile.DressUpGroups,
                FashionCatalogFileDto.HairAndMakeup => ZaFashionCatalogFile.HairAndMakeup,
                FashionCatalogFileDto.DressUpLineups => ZaFashionCatalogFile.DressUpLineups,
                FashionCatalogFileDto.HairAndMakeupLineups => ZaFashionCatalogFile.HairAndMakeupLineups,
                _ => throw new ArgumentOutOfRangeException(nameof(catalogFile), catalogFile, null),
            },
            new ZaFashionCatalogRowBinding(
                binding.SourceRevision,
                binding.PhysicalIndex,
                binding.PhysicalRowId,
                binding.RowRevision),
            field,
            value,
            clear);
    }

    private static FashionCatalogWorkflowDto ToWorkflowDto(ZaFashionCatalogWorkflow workflow)
    {
        var snapshot = workflow.Snapshot;
        return new FashionCatalogWorkflowDto(
            ToDto(workflow.Summary),
            snapshot.SourceRevision,
            snapshot.DressUpItemsRevision,
            snapshot.DressUpGroupsRevision,
            snapshot.HairAndMakeupRevision,
            snapshot.FashionShopsRevision,
            snapshot.DressUpLineupsRevision,
            snapshot.HairAndMakeupLineupsRevision,
            snapshot.DressUpItems.Select(row => new DressUpItemRecordDto(
                row.PhysicalIndex,
                row.PhysicalRowId,
                row.RowRevision,
                row.ItemId,
                row.ModelPart,
                row.CatalogGroupCode,
                row.ModelVariant,
                row.CategoryCode,
                row.ColorVariantCode,
                row.PrimaryColorLabel,
                row.SecondaryColorLabel,
                row.DisplayOrder,
                row.VariantOrder)).ToArray(),
            snapshot.DressUpGroups.Select(row => new DressUpGroupRecordDto(
                row.PhysicalIndex,
                row.PhysicalRowId,
                row.RowRevision,
                row.ModelPart,
                row.DisplayOrder,
                row.DisplayLabel)).ToArray(),
            snapshot.HairAndMakeup.Select(row => new HairAndMakeupRecordDto(
                row.PhysicalIndex,
                row.PhysicalRowId,
                row.RowRevision,
                row.ItemId,
                row.ModelKey,
                row.CatalogTypeCode,
                row.ColorValue,
                row.LabelKey,
                row.DisplayOrder,
                row.GroupCode,
                row.VariantCode)).ToArray(),
            snapshot.DressUpLineups.Select(ToDto).ToArray(),
            snapshot.HairAndMakeupLineups.Select(ToDto).ToArray(),
            new FashionCatalogStatsDto(
                workflow.Stats.DressUpItemCount,
                workflow.Stats.DressUpGroupCount,
                workflow.Stats.HairAndMakeupCount,
                workflow.Stats.DressUpLineupEntryCount,
                workflow.Stats.HairAndMakeupLineupEntryCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
            workflow.CanStage);
    }

    private static FashionLineupEntryRecordDto ToDto(ZaFashionLineupEntryRecord row) =>
        new(
            row.PhysicalIndex,
            row.PhysicalRowId,
            row.RowRevision,
            row.LineupPhysicalIndex,
            row.EntryPhysicalIndex,
            row.LineupId,
            row.ShopIds,
            row.ItemId);

    private static WorkflowSummaryDto ToDto(ZaWorkflowSummary summary) =>
        new(
            summary.Id,
            summary.Label,
            summary.Description,
            summary.Availability switch
            {
                ZaWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
                ZaWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
                ZaWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(summary),
                    summary.Availability,
                    null),
            },
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
}
