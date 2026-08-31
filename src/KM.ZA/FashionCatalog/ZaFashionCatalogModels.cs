// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.ZA.Workflows;

namespace KM.ZA.FashionCatalog;

public enum ZaFashionCatalogFile
{
    DressUpItems,
    DressUpGroups,
    HairAndMakeup,
    DressUpLineups,
    HairAndMakeupLineups,
}

public sealed record ZaFashionCatalogSourceSet(
    byte[] DressUpItems,
    byte[] DressUpGroups,
    byte[] HairAndMakeup,
    byte[] FashionShops,
    byte[] DressUpLineups,
    byte[] HairAndMakeupLineups);

public sealed record ZaFashionCatalogRowBinding(
    string SourceRevision,
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision);

public sealed record ZaFashionCatalogFieldEdit(
    ZaFashionCatalogFile CatalogFile,
    ZaFashionCatalogRowBinding Binding,
    string Field,
    string? Value,
    bool Clear = false);

public static class ZaFashionCatalogFields
{
    public const string ItemId = "itemId";
    public const string ModelPart = "modelPart";
    public const string CatalogGroupCode = "catalogGroupCode";
    public const string ModelVariant = "modelVariant";
    public const string CategoryCode = "categoryCode";
    public const string ColorVariantCode = "colorVariantCode";
    public const string PrimaryColorLabel = "primaryColorLabel";
    public const string SecondaryColorLabel = "secondaryColorLabel";
    public const string Price = "price";
    public const string UiIndex = "uiIndex";
    public const string FootwearSubtype = "footwearSubtype";
    public const string DisplayOrder = "displayOrder";
    public const string DisplayLabel = "displayLabel";
    public const string ModelKey = "modelKey";
    public const string CatalogTypeCode = "catalogTypeCode";
    public const string ColorValue = "colorValue";
    public const string LabelKey = "labelKey";
    public const string GroupCode = "groupCode";
    public const string VariantCode = "variantCode";
}

public static class ZaFashionCatalogDiagnosticCodes
{
    public const string Safety = "KM-ZA-FASHION-CATALOG-SAFETY";
    public const string EditSafety = "KM-ZA-FASHION-CATALOG-EDIT-SAFETY";
    public const string ReviewedState = "KM-ZA-FASHION-CATALOG-REVIEWED-STATE";
}

public sealed record ZaOptionalCatalogText(bool IsSpecified, string? Value)
{
    public static ZaOptionalCatalogText Set(string value) => new(true, value);

    public static ZaOptionalCatalogText Clear() => new(true, null);
}

public sealed record ZaDressUpItemPatch(
    uint? ItemId = null,
    string? ModelPart = null,
    uint? CatalogGroupCode = null,
    string? ModelVariant = null,
    uint? CategoryCode = null,
    uint? ColorVariantCode = null,
    string? PrimaryColorLabel = null,
    string? SecondaryColorLabel = null,
    uint? Price = null,
    string? FootwearSubtype = null);

public sealed record ZaDressUpGroupPatch(
    string? ModelPart = null,
    uint? DisplayOrder = null,
    string? DisplayLabel = null);

public sealed record ZaHairAndMakeupPatch(
    uint? ItemId = null,
    string? ModelKey = null,
    uint? CatalogTypeCode = null,
    ZaOptionalCatalogText? ColorValue = null,
    ZaOptionalCatalogText? LabelKey = null,
    uint? DisplayOrder = null,
    int? GroupCode = null,
    int? VariantCode = null);

public sealed record ZaFashionLineupEntryPatch(uint ItemId);

public sealed record ZaDressUpItemRecord(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    uint ItemId,
    string ModelPart,
    uint CatalogGroupCode,
    string ModelVariant,
    uint CategoryCode,
    uint ColorVariantCode,
    string PrimaryColorLabel,
    string SecondaryColorLabel,
    uint Price,
    uint UiIndex,
    string? FootwearSubtype);

public sealed record ZaDressUpGroupRecord(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    string ModelPart,
    uint DisplayOrder,
    string DisplayLabel);

public sealed record ZaHairAndMakeupRecord(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    uint ItemId,
    string ModelKey,
    uint CatalogTypeCode,
    string? ColorValue,
    string? LabelKey,
    uint DisplayOrder,
    int GroupCode,
    int VariantCode);

public sealed record ZaFashionLineupEntryRecord(
    int PhysicalIndex,
    string PhysicalRowId,
    string RowRevision,
    int LineupPhysicalIndex,
    int EntryPhysicalIndex,
    string LineupId,
    IReadOnlyList<string> ShopIds,
    uint ItemId);

public sealed record ZaFashionCatalogTextLabel(
    string Key,
    string Label);

public sealed record ZaFashionCatalogSnapshot(
    string SourceRevision,
    string DressUpItemsRevision,
    string DressUpGroupsRevision,
    string HairAndMakeupRevision,
    string FashionShopsRevision,
    string DressUpLineupsRevision,
    string HairAndMakeupLineupsRevision,
    IReadOnlyList<ZaDressUpItemRecord> DressUpItems,
    IReadOnlyList<ZaDressUpGroupRecord> DressUpGroups,
    IReadOnlyList<ZaHairAndMakeupRecord> HairAndMakeup,
    IReadOnlyList<ZaFashionLineupEntryRecord> DressUpLineups,
    IReadOnlyList<ZaFashionLineupEntryRecord> HairAndMakeupLineups);

public sealed record ZaFashionCatalogEditResult(
    ZaFashionCatalogFile ChangedFile,
    ZaFashionCatalogSourceSet Sources,
    ZaFashionCatalogSnapshot Snapshot);

public sealed record ZaFashionCatalogWorkflowStats(
    int DressUpItemCount,
    int DressUpGroupCount,
    int HairAndMakeupCount,
    int DressUpLineupEntryCount,
    int HairAndMakeupLineupEntryCount);

public sealed record ZaFashionCatalogWorkflow(
    ZaWorkflowSummary Summary,
    ZaFashionCatalogSnapshot Snapshot,
    IReadOnlyList<ZaFashionCatalogTextLabel> TextLabels,
    ZaFashionCatalogWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    bool CanStage);

public sealed record ZaFashionCatalogStageResult(
    ZaFashionCatalogWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
