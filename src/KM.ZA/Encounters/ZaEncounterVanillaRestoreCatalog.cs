// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Encounters;

internal sealed record ZaEncounterVanillaFieldValue(
    string Field,
    int Value,
    bool RequiresWrite);

internal sealed record ZaEncounterVanillaRestoreValues(
    IReadOnlyList<ZaEncounterVanillaFieldValue> Fields,
    ProjectFileReference EncounterBaseSource,
    ProjectFileReference SpawnerBaseSource);

/// <summary>
/// Resolves one loaded encounter slot against the exact same source coordinates
/// and stable identities in the immutable base files.
/// </summary>
internal sealed class ZaEncounterVanillaRestoreCatalog
{
    private const string UnavailableReason =
        "Verified vanilla encounter files are unavailable for this project.";
    private const string IdentityReason =
        "This encounter cannot be matched exactly to the verified vanilla files.";
    private const string PopulationReason =
        "This encounter's population data is missing or mixed, so it cannot be restored safely.";
    private const string MaterializationReason =
        "A changed vanilla value cannot be written because its source scalar is not safely materialized.";
    private const string StrengthenShapeReason =
        "This encounter has a non-vanilla StrengthenValue block that cannot be removed by scalar restoration.";
    private const string ValueReason =
        "The verified vanilla values are not valid for the currently loaded game data.";

    private readonly ZaEncounterDataDocument currentEncounterDocument;
    private readonly ZaPokemonSpawnerDataDocument currentSpawnerDocument;
    private readonly ZaEncounterDataDocument baseEncounterDocument;
    private readonly ZaPokemonSpawnerDataDocument baseSpawnerDocument;

    private ZaEncounterVanillaRestoreCatalog(
        ZaWorkflowFile baseEncounterSource,
        ZaWorkflowFile baseSpawnerSource,
        ZaEncounterDataDocument currentEncounterDocument,
        ZaPokemonSpawnerDataDocument currentSpawnerDocument,
        ZaEncounterDataDocument baseEncounterDocument,
        ZaPokemonSpawnerDataDocument baseSpawnerDocument)
    {
        this.currentEncounterDocument = currentEncounterDocument;
        this.currentSpawnerDocument = currentSpawnerDocument;
        this.baseEncounterDocument = baseEncounterDocument;
        this.baseSpawnerDocument = baseSpawnerDocument;
        EncounterBaseSource = new ProjectFileReference(
            ProjectFileLayer.Base,
            baseEncounterSource.RelativePath);
        SpawnerBaseSource = new ProjectFileReference(
            ProjectFileLayer.Base,
            baseSpawnerSource.RelativePath);
    }

    public ProjectFileReference EncounterBaseSource { get; }

    public ProjectFileReference SpawnerBaseSource { get; }

    public static bool TryCreate(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        out ZaEncounterVanillaRestoreCatalog? catalog,
        out string blockedReason)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);

        try
        {
            var currentEncounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            var currentSpawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            var baseEncounterSource = fileSource.ReadBase(project, ZaDataPaths.EncountDataArray);
            var baseSpawnerSource = fileSource.ReadBase(project, ZaDataPaths.PokemonSpawnerDataArray);
            catalog = new ZaEncounterVanillaRestoreCatalog(
                baseEncounterSource,
                baseSpawnerSource,
                ZaEncounterDataDocument.Parse(currentEncounterSource.Bytes),
                ZaPokemonSpawnerDataDocument.Parse(currentSpawnerSource.Bytes),
                ZaEncounterDataDocument.Parse(baseEncounterSource.Bytes),
                ZaPokemonSpawnerDataDocument.Parse(baseSpawnerSource.Bytes));
            blockedReason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            catalog = null;
            blockedReason = UnavailableReason;
            return false;
        }
    }

    public bool TryResolve(
        ZaEncountersWorkflow workflow,
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        out ZaEncounterVanillaRestoreValues? values,
        out string blockedReason)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(slot);

        values = null;
        if (!TryResolveIdentity(
                table,
                slot,
                out var currentRow,
                out var baseRow,
                out var currentSpawnerSlot,
                out var baseSpawnerSlot,
                out var currentAppearance,
                out var baseAppearance))
        {
            blockedReason = IdentityReason;
            return false;
        }

        if (!CurrentWorkflowValuesMatchSource(slot, currentRow, currentSpawnerSlot, currentAppearance))
        {
            blockedReason = IdentityReason;
            return false;
        }

        if (currentRow.StrengthenValue is not null && baseRow.StrengthenValue is null)
        {
            blockedReason = StrengthenShapeReason;
            return false;
        }

        if (!TryReadWholePercent(baseRow.OyabunProbability, out var baseAlphaChance)
            || baseRow.OyabunAdditionalLevel is < 0 or > 100)
        {
            blockedReason = ValueReason;
            return false;
        }

        var fields = CreateSharedFields(
                currentRow,
                baseRow,
                baseAlphaChance)
            .ToList();
        fields.AddRange(
        [
            CreateField(
                ZaEncountersWorkflowService.WeightField,
                baseSpawnerSlot.Weight,
                currentSpawnerSlot.Weight != baseSpawnerSlot.Weight),
            CreateField(
                ZaEncountersWorkflowService.SlotMaxCountField,
                baseSpawnerSlot.MaxCount,
                currentSpawnerSlot.MaxCount != baseSpawnerSlot.MaxCount),
        ]);

        if (currentAppearance is not null && baseAppearance is not null)
        {
            fields.Add(CreateField(
                ZaEncountersWorkflowService.AppearanceMinCountField,
                baseAppearance.Minimum,
                currentAppearance.Minimum != baseAppearance.Minimum));
            fields.Add(CreateField(
                ZaEncountersWorkflowService.AppearanceMaxCountField,
                baseAppearance.Maximum,
                currentAppearance.Maximum != baseAppearance.Maximum));
        }

        if (!ValidateChangedValues(
                workflow,
                slot.SpeciesId,
                slot.Form,
                fields,
                baseRow,
                baseAlphaChance,
                baseAppearance))
        {
            blockedReason = ValueReason;
            return false;
        }

        if (fields.Any(field =>
                field.RequiresWrite
                && field.Field == ZaEncountersWorkflowService.WeightField)
            && !currentSpawnerSlot.CanEditWeight
            || fields.Any(field =>
                field.RequiresWrite
                && field.Field == ZaEncountersWorkflowService.SlotMaxCountField)
            && !currentSpawnerSlot.CanEditMaxCount
            || fields.Any(field =>
                field.RequiresWrite
                && field.Field == ZaEncountersWorkflowService.AppearanceMinCountField)
            && currentAppearance?.CanEditMinimum != true
            || fields.Any(field =>
                field.RequiresWrite
                && field.Field == ZaEncountersWorkflowService.AppearanceMaxCountField)
            && currentAppearance?.CanEditMaximum != true)
        {
            blockedReason = MaterializationReason;
            return false;
        }

        values = new ZaEncounterVanillaRestoreValues(
            fields,
            EncounterBaseSource,
            SpawnerBaseSource);
        blockedReason = string.Empty;
        return true;
    }

    public bool TryValidatePendingRestore(
        ZaEncountersWorkflow workflow,
        PendingEdit edit)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(edit);

        if (!int.TryParse(
                edit.NewValue,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }
        if (edit.Field is null
            || !IsValidVerifiedBaseValue(workflow, edit.Field, value))
        {
            return false;
        }

        if (IsSharedField(edit.Field)
            && ZaEncountersWorkflowService.TryParsePokemonDataRecordId(
                edit.RecordId,
                out var sourceIndex))
        {
            if (!TryResolveSharedRow(
                    sourceIndex,
                    out var currentRow,
                    out var baseRow)
                || !TryReadBaseRowValue(baseRow, edit.Field, out var baseValue)
                || value != baseValue
                || !TryReadWholePercent(baseRow.OyabunProbability, out var baseAlphaChance)
                || baseRow.OyabunAdditionalLevel is < 0 or > 100)
            {
                return false;
            }

            var sharedFields = CreateSharedFields(
                currentRow,
                baseRow,
                baseAlphaChance);
            return ValidateChangedValues(
                workflow,
                currentRow.DevNo,
                currentRow.FormNo,
                sharedFields,
                baseRow,
                baseAlphaChance,
                baseAppearance: null);
        }

        if (IsSpawnerSlotField(edit.Field)
            && ZaEncountersWorkflowService.TryParseSlotRecordId(
                edit.RecordId,
                out var tableId,
                out var slotIndex))
        {
            var table = workflow.Tables.FirstOrDefault(candidate =>
                string.Equals(candidate.TableId, tableId, StringComparison.Ordinal));
            var slot = table?.Slots.FirstOrDefault(candidate => candidate.Slot == slotIndex);
            if (table is null
                || slot is null
                || !TryResolveSpawnerIdentity(
                    table,
                    out var currentSpawner,
                    out var baseSpawner)
                || !TryGetExactSlot(currentSpawner, slot.Slot, out var currentSpawnerSlot)
                || !TryGetExactSlot(baseSpawner, slot.Slot, out var baseSpawnerSlot)
                || !TryResolveAppearanceIdentity(
                    table,
                    out var currentSpawnerAppearance,
                    out var baseSpawnerAppearance)
                || string.IsNullOrWhiteSpace(slot.EncounterDataId)
                || !string.Equals(
                    currentSpawnerSlot.EncountDataId,
                    slot.EncounterDataId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    baseSpawnerSlot.EncountDataId,
                    currentSpawnerSlot.EncountDataId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var spawnerFields = CreateSpawnerFields(
                currentSpawnerSlot,
                baseSpawnerSlot,
                currentSpawnerAppearance,
                baseSpawnerAppearance);
            if (!ValidateChangedFieldBounds(workflow, spawnerFields)
                || !ValidateChangedSpawnerCounts(
                    spawnerFields,
                    baseSpawnerAppearance))
            {
                return false;
            }

            return edit.Field switch
            {
                ZaEncountersWorkflowService.WeightField =>
                    value == baseSpawnerSlot.Weight
                    && (currentSpawnerSlot.Weight == baseSpawnerSlot.Weight
                        || currentSpawnerSlot.CanEditWeight),
                ZaEncountersWorkflowService.SlotMaxCountField =>
                    value == baseSpawnerSlot.MaxCount
                    && (currentSpawnerSlot.MaxCount == baseSpawnerSlot.MaxCount
                        || currentSpawnerSlot.CanEditMaxCount),
                _ => false,
            };
        }

        if (!IsAppearanceField(edit.Field)
            || !ZaEncountersWorkflowService.TryParseAppearanceRecordId(
                edit.RecordId,
                out var appearanceTableId))
        {
            return false;
        }

        var appearanceTable = workflow.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.TableId, appearanceTableId, StringComparison.Ordinal));
        if (appearanceTable is null
            || !TryResolveAppearanceIdentity(
                appearanceTable,
                out var currentAppearance,
                out var baseAppearance)
            || currentAppearance is null
            || baseAppearance is null)
        {
            return false;
        }

        var appearanceFields = CreateAppearanceFields(
            currentAppearance,
            baseAppearance);
        if (!ValidateChangedFieldBounds(workflow, appearanceFields)
            || !ValidateChangedSpawnerCounts(appearanceFields, baseAppearance))
        {
            return false;
        }

        return edit.Field switch
        {
            ZaEncountersWorkflowService.AppearanceMinCountField =>
                value == baseAppearance.Minimum
                && (currentAppearance.Minimum == baseAppearance.Minimum
                    || currentAppearance.CanEditMinimum),
            ZaEncountersWorkflowService.AppearanceMaxCountField =>
                value == baseAppearance.Maximum
                && (currentAppearance.Maximum == baseAppearance.Maximum
                    || currentAppearance.CanEditMaximum),
            _ => false,
        };
    }

    public bool HasRestoreSourceMarker(PendingEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return edit.Sources.Contains(EncounterBaseSource)
            && edit.Sources.Contains(SpawnerBaseSource);
    }

    public bool IsVerifiedBaseSpeciesForm(
        int sourceIndex,
        int speciesId,
        int form)
    {
        return TryResolveSharedRow(sourceIndex, out _, out var baseRow)
            && baseRow.DevNo == speciesId
            && baseRow.FormNo == form;
    }

    private bool TryResolveIdentity(
        ZaEncounterTableRecord table,
        ZaEncounterSlotRecord slot,
        out ZaEncounterDataEntry currentRow,
        out ZaEncounterDataEntry baseRow,
        out ZaPokemonSpawnerEncountDataInfo currentSpawnerSlot,
        out ZaPokemonSpawnerEncountDataInfo baseSpawnerSlot,
        out AppearanceState? currentAppearance,
        out AppearanceState? baseAppearance)
    {
        currentRow = null!;
        baseRow = null!;
        currentSpawnerSlot = null!;
        baseSpawnerSlot = null!;
        currentAppearance = null;
        baseAppearance = null;

        if (slot.PokemonDataSourceIndex < 0
            || string.IsNullOrWhiteSpace(slot.EncounterRecordId)
            || !string.Equals(
                slot.EncounterRecordId,
                ZaEncountersWorkflowService.CreatePokemonDataRecordId(
                    slot.PokemonDataSourceIndex),
                StringComparison.Ordinal)
            || !TryResolveSharedRow(slot.PokemonDataSourceIndex, out currentRow, out baseRow)
            || string.IsNullOrWhiteSpace(slot.EncounterDataId)
            || !RowMatchesSlotReference(currentRow.Id!, slot.EncounterDataId))
        {
            return false;
        }

        if (!TryResolveSpawnerIdentity(
                table,
                out var currentSpawner,
                out var baseSpawner)
            || !TryGetExactSlot(currentSpawner, slot.Slot, out currentSpawnerSlot)
            || !TryGetExactSlot(baseSpawner, slot.Slot, out baseSpawnerSlot)
            || string.IsNullOrWhiteSpace(currentSpawnerSlot.EncountDataId)
            || !string.Equals(
                currentSpawnerSlot.EncountDataId,
                slot.EncounterDataId,
                StringComparison.Ordinal)
            || !string.Equals(
                baseSpawnerSlot.EncountDataId,
                currentSpawnerSlot.EncountDataId,
                StringComparison.Ordinal)
            || !TryResolveAppearanceIdentity(
                table,
                out currentAppearance,
                out baseAppearance))
        {
            return false;
        }

        return true;
    }

    private bool TryResolveSharedRow(
        int sourceIndex,
        out ZaEncounterDataEntry currentRow,
        out ZaEncounterDataEntry baseRow)
    {
        currentRow = null!;
        baseRow = null!;
        var currentMatches = currentEncounterDocument.Entries
            .OfType<ZaEncounterDataEntry>()
            .Where(candidate => candidate.SourceIndex == sourceIndex)
            .Take(2)
            .ToArray();
        var baseMatches = baseEncounterDocument.Entries
            .OfType<ZaEncounterDataEntry>()
            .Where(candidate => candidate.SourceIndex == sourceIndex)
            .Take(2)
            .ToArray();
        if (currentMatches.Length != 1
            || baseMatches.Length != 1
            || string.IsNullOrWhiteSpace(currentMatches[0].Id)
            || !string.Equals(
                currentMatches[0].Id,
                baseMatches[0].Id,
                StringComparison.Ordinal))
        {
            return false;
        }

        currentRow = currentMatches[0];
        baseRow = baseMatches[0];
        return true;
    }

    private bool TryResolveSpawnerIdentity(
        ZaEncounterTableRecord table,
        out ZaPokemonSpawnerDataEntry currentSpawner,
        out ZaPokemonSpawnerDataEntry baseSpawner)
    {
        currentSpawner = null!;
        baseSpawner = null!;
        if (!ZaEncountersWorkflowService.TryParseTableId(
                table.TableId,
                out var groupIndex,
                out var spawnerIndex)
            || string.IsNullOrWhiteSpace(table.RawSpawnerId))
        {
            return false;
        }

        var currentMatches = currentSpawnerDocument.Entries
            .Where(candidate =>
                candidate.GroupIndex == groupIndex
                && candidate.SpawnerIndex == spawnerIndex)
            .Take(2)
            .ToArray();
        var baseMatches = baseSpawnerDocument.Entries
            .Where(candidate =>
                candidate.GroupIndex == groupIndex
                && candidate.SpawnerIndex == spawnerIndex)
            .Take(2)
            .ToArray();
        if (currentMatches.Length != 1
            || baseMatches.Length != 1
            || !string.Equals(
                currentMatches[0].Id,
                table.RawSpawnerId,
                StringComparison.Ordinal)
            || !string.Equals(
                baseMatches[0].Id,
                currentMatches[0].Id,
                StringComparison.Ordinal))
        {
            return false;
        }

        currentSpawner = currentMatches[0];
        baseSpawner = baseMatches[0];
        return true;
    }

    private bool TryResolveAppearanceIdentity(
        ZaEncounterTableRecord table,
        out AppearanceState? currentAppearance,
        out AppearanceState? baseAppearance)
    {
        currentAppearance = null;
        baseAppearance = null;
        if (!TryResolveSpawnerIdentity(table, out var currentSpawner, out var baseSpawner)
            || !TryReadAppearanceState(currentSpawner, out currentAppearance)
            || !TryReadAppearanceState(baseSpawner, out baseAppearance)
            || (currentAppearance is null) != (baseAppearance is null))
        {
            return false;
        }

        if (currentAppearance is null)
        {
            return true;
        }

        return currentAppearance.ObjectNames.SequenceEqual(
            baseAppearance!.ObjectNames,
            StringComparer.Ordinal);
    }

    private static bool CurrentWorkflowValuesMatchSource(
        ZaEncounterSlotRecord slot,
        ZaEncounterDataEntry row,
        ZaPokemonSpawnerEncountDataInfo spawnerSlot,
        AppearanceState? appearance)
    {
        var hasWholeAlphaChance = TryReadWholePercent(
            row.OyabunProbability,
            out var alphaChance);
        var alphaChanceMatches = hasWholeAlphaChance
            ? slot.AlphaChancePercent == alphaChance
            : slot.AlphaChancePercent is null;
        var alphaBonusMatches = row.OyabunAdditionalLevel is >= 0 and <= 100
            ? slot.AlphaLevelBonus == row.OyabunAdditionalLevel
            : slot.AlphaLevelBonus is null;
        var appearanceMatches = appearance is null
            ? slot.AppearanceObjectCount == 0
                && slot.AppearanceMinCount is null
                && slot.AppearanceMaxCount is null
            : slot.AppearanceObjectCount == appearance.ObjectNames.Count
                && slot.AppearanceMinCount == appearance.Minimum
                && slot.AppearanceMaxCount == appearance.Maximum;

        return slot.SpeciesId == row.DevNo
            && slot.Form == row.FormNo
            && slot.LevelMin == row.MinLevel
            && slot.LevelMax == row.MaxLevel
            && slot.HeldItemId == (row.HoldItem ?? 0)
            && slot.Ability == row.Tokusei
            && slot.Nature == row.Seikaku
            && slot.Gender == row.Sex
            && slot.ShinyMode == row.Rare
            && (slot.MoveIds ?? Array.Empty<int>())
                .SequenceEqual(ReadMoves(row))
            && slot.IvHp == ReadIv(row.TalentValue, stats => stats.HP)
            && slot.IvAttack == ReadIv(row.TalentValue, stats => stats.Attack)
            && slot.IvDefense == ReadIv(row.TalentValue, stats => stats.Defense)
            && slot.IvSpecialAttack == ReadIv(row.TalentValue, stats => stats.SpecialAttack)
            && slot.IvSpecialDefense == ReadIv(row.TalentValue, stats => stats.SpecialDefense)
            && slot.IvSpeed == ReadIv(row.TalentValue, stats => stats.Speed)
            && slot.TalentScale == row.TalentScale
            && slot.TalentVCount == row.TalentVNum
            && slot.StrengthenHp == ReadStrengthen(row.StrengthenValue, stats => stats.HP)
            && slot.StrengthenAttack == ReadStrengthen(row.StrengthenValue, stats => stats.Attack)
            && slot.StrengthenDefense == ReadStrengthen(row.StrengthenValue, stats => stats.Defense)
            && slot.StrengthenSpecialAttack == ReadStrengthen(row.StrengthenValue, stats => stats.SpecialAttack)
            && slot.StrengthenSpecialDefense == ReadStrengthen(row.StrengthenValue, stats => stats.SpecialDefense)
            && slot.StrengthenSpeed == ReadStrengthen(row.StrengthenValue, stats => stats.Speed)
            && alphaChanceMatches
            && alphaBonusMatches
            && slot.Weight == spawnerSlot.Weight
            && slot.SlotMaxCount == spawnerSlot.MaxCount
            && appearanceMatches;
    }

    private static IReadOnlyList<ZaEncounterVanillaFieldValue> CreateSharedFields(
        ZaEncounterDataEntry currentRow,
        ZaEncounterDataEntry baseRow,
        int baseAlphaChance)
    {
        var fields = new List<ZaEncounterVanillaFieldValue>
        {
            CreateField(
                ZaEncountersWorkflowService.SpeciesIdField,
                baseRow.DevNo,
                currentRow.DevNo != baseRow.DevNo),
            CreateField(
                ZaEncountersWorkflowService.FormField,
                baseRow.FormNo,
                currentRow.FormNo != baseRow.FormNo),
            CreateField(
                ZaEncountersWorkflowService.LevelMinField,
                baseRow.MinLevel,
                currentRow.MinLevel != baseRow.MinLevel),
            CreateField(
                ZaEncountersWorkflowService.LevelMaxField,
                baseRow.MaxLevel,
                currentRow.MaxLevel != baseRow.MaxLevel),
            CreateField(
                ZaEncountersWorkflowService.AlphaChancePercentField,
                baseAlphaChance,
                currentRow.OyabunProbability != baseAlphaChance),
            CreateField(
                ZaEncountersWorkflowService.AlphaLevelBonusField,
                baseRow.OyabunAdditionalLevel,
                currentRow.OyabunAdditionalLevel != baseRow.OyabunAdditionalLevel),
            CreateField(
                ZaEncountersWorkflowService.HeldItemIdField,
                baseRow.HoldItem ?? 0,
                (currentRow.HoldItem ?? 0) != (baseRow.HoldItem ?? 0)),
            CreateField(
                ZaEncountersWorkflowService.AbilityField,
                baseRow.Tokusei,
                currentRow.Tokusei != baseRow.Tokusei),
            CreateField(
                ZaEncountersWorkflowService.NatureField,
                baseRow.Seikaku,
                currentRow.Seikaku != baseRow.Seikaku),
            CreateField(
                ZaEncountersWorkflowService.GenderField,
                baseRow.Sex,
                currentRow.Sex != baseRow.Sex),
            CreateField(
                ZaEncountersWorkflowService.ShinyModeField,
                baseRow.Rare,
                currentRow.Rare != baseRow.Rare),
            CreateField(
                ZaEncountersWorkflowService.Move1IdField,
                ReadMove(baseRow, 0),
                ReadMove(currentRow, 0) != ReadMove(baseRow, 0)),
            CreateField(
                ZaEncountersWorkflowService.Move2IdField,
                ReadMove(baseRow, 1),
                ReadMove(currentRow, 1) != ReadMove(baseRow, 1)),
            CreateField(
                ZaEncountersWorkflowService.Move3IdField,
                ReadMove(baseRow, 2),
                ReadMove(currentRow, 2) != ReadMove(baseRow, 2)),
            CreateField(
                ZaEncountersWorkflowService.Move4IdField,
                ReadMove(baseRow, 3),
                ReadMove(currentRow, 3) != ReadMove(baseRow, 3)),
            CreateField(
                ZaEncountersWorkflowService.IvHpField,
                ReadIv(baseRow.TalentValue, stats => stats.HP),
                ReadIv(currentRow.TalentValue, stats => stats.HP)
                    != ReadIv(baseRow.TalentValue, stats => stats.HP)),
            CreateField(
                ZaEncountersWorkflowService.IvAttackField,
                ReadIv(baseRow.TalentValue, stats => stats.Attack),
                ReadIv(currentRow.TalentValue, stats => stats.Attack)
                    != ReadIv(baseRow.TalentValue, stats => stats.Attack)),
            CreateField(
                ZaEncountersWorkflowService.IvDefenseField,
                ReadIv(baseRow.TalentValue, stats => stats.Defense),
                ReadIv(currentRow.TalentValue, stats => stats.Defense)
                    != ReadIv(baseRow.TalentValue, stats => stats.Defense)),
            CreateField(
                ZaEncountersWorkflowService.IvSpecialAttackField,
                ReadIv(baseRow.TalentValue, stats => stats.SpecialAttack),
                ReadIv(currentRow.TalentValue, stats => stats.SpecialAttack)
                    != ReadIv(baseRow.TalentValue, stats => stats.SpecialAttack)),
            CreateField(
                ZaEncountersWorkflowService.IvSpecialDefenseField,
                ReadIv(baseRow.TalentValue, stats => stats.SpecialDefense),
                ReadIv(currentRow.TalentValue, stats => stats.SpecialDefense)
                    != ReadIv(baseRow.TalentValue, stats => stats.SpecialDefense)),
            CreateField(
                ZaEncountersWorkflowService.IvSpeedField,
                ReadIv(baseRow.TalentValue, stats => stats.Speed),
                ReadIv(currentRow.TalentValue, stats => stats.Speed)
                    != ReadIv(baseRow.TalentValue, stats => stats.Speed)),
            CreateField(
                ZaEncountersWorkflowService.VanillaTalentScaleField,
                baseRow.TalentScale,
                currentRow.TalentScale != baseRow.TalentScale),
            CreateField(
                ZaEncountersWorkflowService.VanillaTalentVCountField,
                baseRow.TalentVNum,
                currentRow.TalentVNum != baseRow.TalentVNum),
        };

        if (baseRow.StrengthenValue is { } baseStrengthen)
        {
            var currentStrengthen = currentRow.StrengthenValue;
            fields.AddRange(
            [
                CreateField(
                    ZaEncountersWorkflowService.StrengthenHpField,
                    baseStrengthen.HP,
                    currentStrengthen?.HP != baseStrengthen.HP),
                CreateField(
                    ZaEncountersWorkflowService.StrengthenAttackField,
                    baseStrengthen.Attack,
                    currentStrengthen?.Attack != baseStrengthen.Attack),
                CreateField(
                    ZaEncountersWorkflowService.StrengthenDefenseField,
                    baseStrengthen.Defense,
                    currentStrengthen?.Defense != baseStrengthen.Defense),
                CreateField(
                    ZaEncountersWorkflowService.StrengthenSpecialAttackField,
                    baseStrengthen.SpecialAttack,
                    currentStrengthen?.SpecialAttack != baseStrengthen.SpecialAttack),
                CreateField(
                    ZaEncountersWorkflowService.StrengthenSpecialDefenseField,
                    baseStrengthen.SpecialDefense,
                    currentStrengthen?.SpecialDefense != baseStrengthen.SpecialDefense),
                CreateField(
                    ZaEncountersWorkflowService.StrengthenSpeedField,
                    baseStrengthen.Speed,
                    currentStrengthen?.Speed != baseStrengthen.Speed),
            ]);
        }

        return fields;
    }

    private static IReadOnlyList<int> ReadMoves(ZaEncounterDataEntry row)
    {
        return row.WazaList?.Values.Take(4).ToArray() ?? [0, 0, 0, 0];
    }

    private static int ReadMove(ZaEncounterDataEntry row, int index)
    {
        return ReadMoves(row)[index];
    }

    private static int ReadIv(
        ZaPokemonDataStatsRecord? stats,
        Func<ZaPokemonDataStatsRecord, int> select)
    {
        return stats is null ? -1 : select(stats);
    }

    private static int? ReadStrengthen(
        ZaPokemonDataStatsRecord? stats,
        Func<ZaPokemonDataStatsRecord, int> select)
    {
        return stats is null ? null : select(stats);
    }

    private static IReadOnlyList<ZaEncounterVanillaFieldValue> CreateSpawnerFields(
        ZaPokemonSpawnerEncountDataInfo currentSlot,
        ZaPokemonSpawnerEncountDataInfo baseSlot,
        AppearanceState? currentAppearance,
        AppearanceState? baseAppearance)
    {
        var fields = new List<ZaEncounterVanillaFieldValue>
        {
            CreateField(
                ZaEncountersWorkflowService.WeightField,
                baseSlot.Weight,
                currentSlot.Weight != baseSlot.Weight),
            CreateField(
                ZaEncountersWorkflowService.SlotMaxCountField,
                baseSlot.MaxCount,
                currentSlot.MaxCount != baseSlot.MaxCount),
        };
        if (currentAppearance is not null && baseAppearance is not null)
        {
            fields.AddRange(CreateAppearanceFields(
                currentAppearance,
                baseAppearance));
        }

        return fields;
    }

    private static IReadOnlyList<ZaEncounterVanillaFieldValue> CreateAppearanceFields(
        AppearanceState currentAppearance,
        AppearanceState baseAppearance)
    {
        return
        [
            CreateField(
                ZaEncountersWorkflowService.AppearanceMinCountField,
                baseAppearance.Minimum,
                currentAppearance.Minimum != baseAppearance.Minimum),
            CreateField(
                ZaEncountersWorkflowService.AppearanceMaxCountField,
                baseAppearance.Maximum,
                currentAppearance.Maximum != baseAppearance.Maximum),
        ];
    }

    private static bool ValidateChangedValues(
        ZaEncountersWorkflow workflow,
        int sourceSpeciesId,
        int sourceForm,
        IReadOnlyList<ZaEncounterVanillaFieldValue> fields,
        ZaEncounterDataEntry baseRow,
        int baseAlphaChance,
        AppearanceState? baseAppearance)
    {
        if (!ValidateChangedFieldBounds(workflow, fields))
        {
            return false;
        }

        var changesSpeciesPair = fields.Any(field =>
            field.RequiresWrite
            && field.Field is
                ZaEncountersWorkflowService.SpeciesIdField
                or ZaEncountersWorkflowService.FormField);
        if (changesSpeciesPair
            && (!IsValidVerifiedBaseValue(
                    workflow,
                    ZaEncountersWorkflowService.SpeciesIdField,
                    baseRow.DevNo)
                || !IsValidVerifiedBaseValue(
                    workflow,
                    ZaEncountersWorkflowService.FormField,
                    baseRow.FormNo)
                || !ValidateChangedSpeciesFormPair(
                    workflow,
                    sourceSpeciesId,
                    sourceForm,
                    baseRow.DevNo,
                    baseRow.FormNo)))
        {
            return false;
        }

        var changesSharedLevelData = fields.Any(field =>
            field.RequiresWrite
            && field.Field is
                ZaEncountersWorkflowService.LevelMinField
                or ZaEncountersWorkflowService.LevelMaxField
                or ZaEncountersWorkflowService.AlphaChancePercentField
                or ZaEncountersWorkflowService.AlphaLevelBonusField);
        if (changesSharedLevelData
            && (baseRow.MinLevel > baseRow.MaxLevel
                || baseAlphaChance > 0
                && (long)baseRow.MaxLevel + baseRow.OyabunAdditionalLevel > 100))
        {
            return false;
        }

        return ValidateChangedSpawnerCounts(fields, baseAppearance);
    }

    private static bool ValidateChangedFieldBounds(
        ZaEncountersWorkflow workflow,
        IReadOnlyList<ZaEncounterVanillaFieldValue> fields)
    {
        return fields
            .Where(field => field.RequiresWrite)
            .All(field => IsValidVerifiedBaseValue(
                workflow,
                field.Field,
                field.Value));
    }

    private static bool ValidateChangedSpeciesFormPair(
        ZaEncountersWorkflow workflow,
        int sourceSpeciesId,
        int sourceForm,
        int baseSpeciesId,
        int baseForm)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        return ZaSpeciesFormPairValidation.ValidateChangedPair(
            workflow.PokemonAvailability,
            sourceSpeciesId,
            sourceForm,
            baseSpeciesId,
            baseForm,
            ZaEditSessionSupport.EncountersDomain,
            "Verified vanilla encounter",
            diagnostics);
    }

    private static bool ValidateChangedSpawnerCounts(
        IReadOnlyList<ZaEncounterVanillaFieldValue> fields,
        AppearanceState? baseAppearance)
    {
        var changesSpawnerCounts = fields.Any(field =>
            field.RequiresWrite
            && field.Field is
                ZaEncountersWorkflowService.SlotMaxCountField
                or ZaEncountersWorkflowService.AppearanceMinCountField
                or ZaEncountersWorkflowService.AppearanceMaxCountField);
        return !changesSpawnerCounts
            || baseAppearance is null
            || baseAppearance.Minimum <= baseAppearance.Maximum;
    }

    private static bool IsValidVerifiedBaseValue(
        ZaEncountersWorkflow workflow,
        string field,
        int value)
    {
        if (field is ZaEncountersWorkflowService.VanillaTalentScaleField
            or ZaEncountersWorkflowService.VanillaTalentVCountField
            or ZaEncountersWorkflowService.StrengthenHpField
            or ZaEncountersWorkflowService.StrengthenAttackField
            or ZaEncountersWorkflowService.StrengthenDefenseField
            or ZaEncountersWorkflowService.StrengthenSpecialAttackField
            or ZaEncountersWorkflowService.StrengthenSpecialDefenseField
            or ZaEncountersWorkflowService.StrengthenSpeedField)
        {
            return true;
        }

        var editableField = ZaEncountersWorkflowService.GetEditableField(
            workflow,
            field);
        return editableField is not null
            && (editableField.MinimumValue is null
                || value >= editableField.MinimumValue.Value)
            && (editableField.MaximumValue is null
                || value <= editableField.MaximumValue.Value);
    }

    private static bool TryReadAppearanceState(
        ZaPokemonSpawnerDataEntry spawner,
        out AppearanceState? state)
    {
        state = null;
        if (spawner.AppearanceSpawnerObjectInfoList.Count == 0)
        {
            return true;
        }

        var objects = spawner.AppearanceSpawnerObjectInfoList;
        if (objects[0]?.AppearanceInfo is not { } first)
        {
            return false;
        }

        var names = new List<string?>(objects.Count);
        var canEditMinimum = true;
        var canEditMaximum = true;
        foreach (var appearance in objects)
        {
            if (appearance?.AppearanceInfo is not { } info
                || info.MinCount != first.MinCount
                || info.MaxCount != first.MaxCount)
            {
                return false;
            }

            names.Add(appearance.ObjectName);
            canEditMinimum &= info.CanEditMinCount;
            canEditMaximum &= info.CanEditMaxCount;
        }

        state = new AppearanceState(
            first.MinCount,
            first.MaxCount,
            names,
            canEditMinimum,
            canEditMaximum);
        return true;
    }

    private static bool TryGetExactSlot(
        ZaPokemonSpawnerDataEntry spawner,
        int slotIndex,
        out ZaPokemonSpawnerEncountDataInfo slot)
    {
        var matches = spawner.EncountDataInfoList
            .OfType<ZaPokemonSpawnerEncountDataInfo>()
            .Where(candidate => candidate.SlotIndex == slotIndex)
            .Take(2)
            .ToArray();
        slot = matches.Length == 1 ? matches[0] : null!;
        return matches.Length == 1;
    }

    private static bool RowMatchesSlotReference(string rowId, string slotReference)
    {
        return string.Equals(rowId, slotReference, StringComparison.Ordinal)
            || string.Equals(
                rowId,
                ZaEncounterDataIds.NormalizeSpawnerEncounterDataId(slotReference),
                StringComparison.Ordinal);
    }

    private static bool TryReadWholePercent(float value, out int wholePercent)
    {
        if (float.IsFinite(value)
            && value is >= 0 and <= 100
            && value == MathF.Truncate(value))
        {
            wholePercent = checked((int)value);
            return true;
        }

        wholePercent = 0;
        return false;
    }

    private static bool TryReadBaseRowValue(
        ZaEncounterDataEntry row,
        string? field,
        out int value)
    {
        switch (field)
        {
            case ZaEncountersWorkflowService.SpeciesIdField:
                value = row.DevNo;
                return true;
            case ZaEncountersWorkflowService.FormField:
                value = row.FormNo;
                return true;
            case ZaEncountersWorkflowService.LevelMinField:
                value = row.MinLevel;
                return true;
            case ZaEncountersWorkflowService.LevelMaxField:
                value = row.MaxLevel;
                return true;
            case ZaEncountersWorkflowService.AlphaChancePercentField:
                return TryReadWholePercent(row.OyabunProbability, out value);
            case ZaEncountersWorkflowService.AlphaLevelBonusField:
                value = row.OyabunAdditionalLevel;
                return value is >= 0 and <= 100;
            case ZaEncountersWorkflowService.HeldItemIdField:
                value = row.HoldItem ?? 0;
                return true;
            case ZaEncountersWorkflowService.AbilityField:
                value = row.Tokusei;
                return true;
            case ZaEncountersWorkflowService.NatureField:
                value = row.Seikaku;
                return true;
            case ZaEncountersWorkflowService.GenderField:
                value = row.Sex;
                return true;
            case ZaEncountersWorkflowService.ShinyModeField:
                value = row.Rare;
                return true;
            case ZaEncountersWorkflowService.Move1IdField:
                value = ReadMove(row, 0);
                return true;
            case ZaEncountersWorkflowService.Move2IdField:
                value = ReadMove(row, 1);
                return true;
            case ZaEncountersWorkflowService.Move3IdField:
                value = ReadMove(row, 2);
                return true;
            case ZaEncountersWorkflowService.Move4IdField:
                value = ReadMove(row, 3);
                return true;
            case ZaEncountersWorkflowService.IvHpField:
                value = ReadIv(row.TalentValue, stats => stats.HP);
                return true;
            case ZaEncountersWorkflowService.IvAttackField:
                value = ReadIv(row.TalentValue, stats => stats.Attack);
                return true;
            case ZaEncountersWorkflowService.IvDefenseField:
                value = ReadIv(row.TalentValue, stats => stats.Defense);
                return true;
            case ZaEncountersWorkflowService.IvSpecialAttackField:
                value = ReadIv(row.TalentValue, stats => stats.SpecialAttack);
                return true;
            case ZaEncountersWorkflowService.IvSpecialDefenseField:
                value = ReadIv(row.TalentValue, stats => stats.SpecialDefense);
                return true;
            case ZaEncountersWorkflowService.IvSpeedField:
                value = ReadIv(row.TalentValue, stats => stats.Speed);
                return true;
            case ZaEncountersWorkflowService.StrengthenHpField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.HP, out value);
            case ZaEncountersWorkflowService.StrengthenAttackField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.Attack, out value);
            case ZaEncountersWorkflowService.StrengthenDefenseField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.Defense, out value);
            case ZaEncountersWorkflowService.StrengthenSpecialAttackField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.SpecialAttack, out value);
            case ZaEncountersWorkflowService.StrengthenSpecialDefenseField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.SpecialDefense, out value);
            case ZaEncountersWorkflowService.StrengthenSpeedField:
                return TryReadStrengthen(row.StrengthenValue, stats => stats.Speed, out value);
            case ZaEncountersWorkflowService.VanillaTalentScaleField:
                value = row.TalentScale;
                return true;
            case ZaEncountersWorkflowService.VanillaTalentVCountField:
                value = row.TalentVNum;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryReadStrengthen(
        ZaPokemonDataStatsRecord? stats,
        Func<ZaPokemonDataStatsRecord, int> select,
        out int value)
    {
        if (stats is null)
        {
            value = 0;
            return false;
        }

        value = select(stats);
        return true;
    }

    private static ZaEncounterVanillaFieldValue CreateField(
        string field,
        int value,
        bool requiresWrite)
    {
        return new ZaEncounterVanillaFieldValue(field, value, requiresWrite);
    }

    private static bool IsSharedField(string? field)
    {
        return field is
            ZaEncountersWorkflowService.SpeciesIdField
            or ZaEncountersWorkflowService.FormField
            or ZaEncountersWorkflowService.LevelMinField
            or ZaEncountersWorkflowService.LevelMaxField
            or ZaEncountersWorkflowService.AlphaChancePercentField
            or ZaEncountersWorkflowService.AlphaLevelBonusField
            or ZaEncountersWorkflowService.HeldItemIdField
            or ZaEncountersWorkflowService.AbilityField
            or ZaEncountersWorkflowService.NatureField
            or ZaEncountersWorkflowService.GenderField
            or ZaEncountersWorkflowService.ShinyModeField
            or ZaEncountersWorkflowService.Move1IdField
            or ZaEncountersWorkflowService.Move2IdField
            or ZaEncountersWorkflowService.Move3IdField
            or ZaEncountersWorkflowService.Move4IdField
            or ZaEncountersWorkflowService.IvHpField
            or ZaEncountersWorkflowService.IvAttackField
            or ZaEncountersWorkflowService.IvDefenseField
            or ZaEncountersWorkflowService.IvSpecialAttackField
            or ZaEncountersWorkflowService.IvSpecialDefenseField
            or ZaEncountersWorkflowService.IvSpeedField
            or ZaEncountersWorkflowService.StrengthenHpField
            or ZaEncountersWorkflowService.StrengthenAttackField
            or ZaEncountersWorkflowService.StrengthenDefenseField
            or ZaEncountersWorkflowService.StrengthenSpecialAttackField
            or ZaEncountersWorkflowService.StrengthenSpecialDefenseField
            or ZaEncountersWorkflowService.StrengthenSpeedField
            or ZaEncountersWorkflowService.VanillaTalentScaleField
            or ZaEncountersWorkflowService.VanillaTalentVCountField;
    }

    private static bool IsSpawnerSlotField(string? field)
    {
        return field is
            ZaEncountersWorkflowService.WeightField
            or ZaEncountersWorkflowService.SlotMaxCountField;
    }

    private static bool IsAppearanceField(string? field)
    {
        return field is
            ZaEncountersWorkflowService.AppearanceMinCountField
            or ZaEncountersWorkflowService.AppearanceMaxCountField;
    }

    private sealed record AppearanceState(
        int Minimum,
        int Maximum,
        IReadOnlyList<string?> ObjectNames,
        bool CanEditMinimum,
        bool CanEditMaximum);
}
