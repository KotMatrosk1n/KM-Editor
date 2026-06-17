// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.Items;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Encounters;

internal sealed class SvEncountersEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvEncountersWorkflowService encountersWorkflowService;

    public SvEncountersEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvEncountersWorkflowService? encountersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.encountersWorkflowService = encountersWorkflowService ?? new SvEncountersWorkflowService(this.fileSource);
    }

    public SwShEncountersEditResult UpdateSlotField(
        ProjectPaths paths,
        EditSession? session,
        string tableId,
        int slot,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = encountersWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.EncountersDomain,
                diagnostics))
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var table = workflow.Tables.FirstOrDefault(candidate => candidate.TableId == tableId);
        var slotRecord = table?.Slots.FirstOrDefault(candidate => candidate.Slot == slot);
        if (table is null || slotRecord is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Encounter edit targets a table or slot that is not loaded.",
                SvEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing encounter table slot"));
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, table, slotRecord, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SwShEncountersEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SwShEncountersEditResult(
            OverlayPendingEdits(loadedWorkflow, updatedSession.PendingEdits),
            updatedSession,
            diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = encountersWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.EncountersDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Wild Encounters change is valid.",
                SvEditSessionSupport.EncountersDomain));
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        return SvEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            SvEditSessionSupport.EncountersDomain,
            SvDataPaths.WildEncounterArray,
            "Wild Encounters",
            validation.Diagnostics);
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.EncountersDomain,
                expected: "Current reviewed Wild Encounters change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.WildEncounterArray);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.WildEncounterArray, WriteRows(rows));
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.WildEncounterArray));
            writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Applied Wild Encounters change plan and patched the Scarlet/Violet Trinity descriptor.",
                SvEditSessionSupport.EncountersDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Wild Encounters output could not be written: {exception.Message}",
                SvEditSessionSupport.EncountersDomain,
                file: $"romfs/{SvDataPaths.WildEncounterArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SwShEncountersWorkflow workflow,
        SwShEncounterTableRecord table,
        SwShEncounterSlotRecord slot,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, normalizedField, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(normalizedField));
            return null;
        }

        var parsedValue = SvEditSessionSupport.TryParseInt(
            value,
            editableField.MinimumValue,
            editableField.MaximumValue,
            normalizedField,
            SvEditSessionSupport.EncountersDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.EncountersDomain,
            CreateSummary(table, slot, normalizedField, parsedValue.Value),
            new ProjectFileReference(table.Provenance.SourceLayer, table.Provenance.SourceFile),
            CreateSlotRecordId(table.TableId, slot.Slot),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SwShEncountersWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.EncountersDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Wild Encounters.",
                SvEditSessionSupport.EncountersDomain,
                expected: SvEditSessionSupport.EncountersDomain));
            return;
        }

        var editableField = workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, edit.Field, StringComparison.Ordinal));
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? "(missing)"));
            return;
        }

        if (!TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || workflow.Tables.FirstOrDefault(table => table.TableId == tableId)?.Slots.All(row => row.Slot != slot) != false)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit targets a slot that is not loaded.",
                SvEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing encounter slot"));
            return;
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.EncountersDomain,
            diagnostics);
    }

    private static SwShEncountersWorkflow OverlayPendingEdits(SwShEncountersWorkflow workflow, IEnumerable<PendingEdit> edits)
    {
        var updatedWorkflow = workflow;
        foreach (var edit in edits)
        {
            updatedWorkflow = OverlayPendingEdit(updatedWorkflow, edit);
        }

        return updatedWorkflow;
    }

    private static SwShEncountersWorkflow OverlayPendingEdit(SwShEncountersWorkflow workflow, PendingEdit edit)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return workflow;
        }

        return workflow with
        {
            Tables = workflow.Tables
                .Select(table => table.TableId == tableId
                    ? table with
                    {
                        Slots = table.Slots
                            .Select(row => row.Slot == slot ? OverlaySlot(row, edit.Field, value) : row)
                            .ToArray(),
                    }
                    : table)
                .ToArray(),
        };
    }

    private static SwShEncounterSlotRecord OverlaySlot(SwShEncounterSlotRecord slot, string? field, int value)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField => slot with
            {
                SpeciesId = value,
                Species = value == 0 ? "Empty" : SvLabels.Pokemon(value),
            },
            SwShEncountersWorkflowService.FormField => slot with { Form = value },
            SwShEncountersWorkflowService.ProbabilityField => slot with { Weight = value },
            SwShEncountersWorkflowService.LevelMinField => slot with { LevelMin = value },
            SwShEncountersWorkflowService.LevelMaxField => slot with { LevelMax = value },
            _ => slot,
        };
    }

    private static void ApplyEdit(
        IReadOnlyList<EncounterRow> rows,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.EncountersDomain, StringComparison.Ordinal)
            || !TryParseSlotRecordId(edit.RecordId, out var tableId, out var slot)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit is not valid for apply.",
                SvEditSessionSupport.EncountersDomain,
                expected: "Valid encounter edit"));
            return;
        }

        var row = rows
            .Where(candidate => string.Equals(candidate.GroupKey, tableId, StringComparison.Ordinal))
            .ElementAtOrDefault(slot);
        if (row is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending encounter edit target is not present in the source array.",
                SvEditSessionSupport.EncountersDomain,
                field: "slot",
                expected: "Existing source encounter row"));
            return;
        }

        switch (edit.Field)
        {
            case SwShEncountersWorkflowService.SpeciesIdField:
                row.Devid = (global::pml.common.DevID)checked((ushort)value);
                break;
            case SwShEncountersWorkflowService.FormField:
                row.Formno = checked((sbyte)value);
                break;
            case SwShEncountersWorkflowService.ProbabilityField:
                row.Lotvalue = checked((short)value);
                break;
            case SwShEncountersWorkflowService.LevelMinField:
                row.Minlevel = checked((short)value);
                break;
            case SwShEncountersWorkflowService.LevelMaxField:
                row.Maxlevel = checked((short)value);
                break;
        }
    }

    private static IReadOnlyList<EncounterRow> ReadRows(byte[] bytes)
    {
        var table = global::EncountPokeDataArray.GetRootAsEncountPokeDataArray(new ByteBuffer(bytes));
        var rows = new List<EncounterRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            if (row is not null)
            {
                rows.Add(EncounterRow.From(row.Value));
            }
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<EncounterRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::EncountPokeDataArray.CreateValuesVector(builder, offsets);
        var root = global::EncountPokeDataArray.CreateEncountPokeDataArray(builder, vector);
        global::EncountPokeDataArray.FinishEncountPokeDataArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static string CreateSlotRecordId(string tableId, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{tableId}#{slot}");
    }

    private static bool TryParseSlotRecordId(string? recordId, out string tableId, out int slot)
    {
        tableId = string.Empty;
        slot = -1;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        tableId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 0;
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Encounter field '{field}' is not supported by Scarlet/Violet Wild Encounters yet.",
            SvEditSessionSupport.EncountersDomain,
            field: "field",
            expected: "speciesId, form, probability, levelMin, or levelMax");
    }

    private static string CreateSummary(SwShEncounterTableRecord table, SwShEncounterSlotRecord slot, string field, int value)
    {
        return field switch
        {
            SwShEncountersWorkflowService.SpeciesIdField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} species ID to {value}.",
            SwShEncountersWorkflowService.FormField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} form to {value}.",
            SwShEncountersWorkflowService.ProbabilityField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} {table.EncounterType} slot {slot.Slot} lot weight to {value}.",
            SwShEncountersWorkflowService.LevelMinField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} slot {slot.Slot} minimum level to {value}.",
            SwShEncountersWorkflowService.LevelMaxField =>
                $"Set {table.GameVersion} {table.Area} {table.Location} slot {slot.Slot} maximum level to {value}.",
            _ => $"Set {table.Location} encounter {field} to {value}.",
        };
    }

    private sealed class EncounterRow
    {
        public global::pml.common.DevID Devid { get; set; }
        public global::SexType Sex { get; init; }
        public sbyte Formno { get; set; }
        public short Minlevel { get; set; }
        public short Maxlevel { get; set; }
        public short Lotvalue { get; set; }
        public global::Biome Biome1 { get; init; }
        public short Lotvalue1 { get; init; }
        public global::Biome Biome2 { get; init; }
        public short Lotvalue2 { get; init; }
        public global::Biome Biome3 { get; init; }
        public short Lotvalue3 { get; init; }
        public global::Biome Biome4 { get; init; }
        public short Lotvalue4 { get; init; }
        public string? Area { get; init; }
        public string? LocationName { get; init; }
        public int Minheight { get; init; }
        public int Maxheight { get; init; }
        public EnableTableRow? Enabletable { get; init; }
        public TimeTableRow? Timetable { get; init; }
        public string? FlagName { get; init; }
        public short Bandrate { get; init; }
        public global::bandtype Bandtype { get; init; }
        public global::pml.common.DevID Bandpoke { get; init; }
        public global::SexType BandSex { get; init; }
        public sbyte BandFormno { get; init; }
        public sbyte OutbreakLotvalue { get; init; }
        public string? PokeVoiceClassification { get; init; }
        public VersionTableRow? Versiontable { get; init; }
        public BringItemRow? BringItem { get; init; }
        public string GroupKey { get; init; } = string.Empty;

        public static EncounterRow From(global::EncountPokeData row)
        {
            return new EncounterRow
            {
                Devid = row.Devid,
                Sex = row.Sex,
                Formno = row.Formno,
                Minlevel = row.Minlevel,
                Maxlevel = row.Maxlevel,
                Lotvalue = row.Lotvalue,
                Biome1 = row.Biome1,
                Lotvalue1 = row.Lotvalue1,
                Biome2 = row.Biome2,
                Lotvalue2 = row.Lotvalue2,
                Biome3 = row.Biome3,
                Lotvalue3 = row.Lotvalue3,
                Biome4 = row.Biome4,
                Lotvalue4 = row.Lotvalue4,
                Area = row.Area,
                LocationName = row.LocationName,
                Minheight = row.Minheight,
                Maxheight = row.Maxheight,
                Enabletable = row.Enabletable is { } enable ? EnableTableRow.From(enable) : null,
                Timetable = row.Timetable is { } time ? TimeTableRow.From(time) : null,
                FlagName = row.FlagName,
                Bandrate = row.Bandrate,
                Bandtype = row.Bandtype,
                Bandpoke = row.Bandpoke,
                BandSex = row.BandSex,
                BandFormno = row.BandFormno,
                OutbreakLotvalue = row.OutbreakLotvalue,
                PokeVoiceClassification = row.PokeVoiceClassification,
                Versiontable = row.Versiontable is { } version ? VersionTableRow.From(version) : null,
                BringItem = row.BringItem is { } bringItem ? BringItemRow.From(bringItem) : null,
                GroupKey = SvEncounterGrouping.CreateGroupKey(row),
            };
        }

        public Offset<global::EncountPokeData> Write(FlatBufferBuilder builder)
        {
            var areaOffset = string.IsNullOrEmpty(Area) ? default : builder.CreateString(Area);
            var locationNameOffset = string.IsNullOrEmpty(LocationName) ? default : builder.CreateString(LocationName);
            var flagNameOffset = string.IsNullOrEmpty(FlagName) ? default : builder.CreateString(FlagName);
            var voiceOffset = string.IsNullOrEmpty(PokeVoiceClassification)
                ? default
                : builder.CreateString(PokeVoiceClassification);
            var timeOffset = Timetable?.Write(builder) ?? default;
            var versionOffset = Versiontable?.Write(builder) ?? default;

            global::EncountPokeData.StartEncountPokeData(builder);
            global::EncountPokeData.AddVersiontable(builder, versionOffset);
            global::EncountPokeData.AddPokeVoiceClassification(builder, voiceOffset);
            global::EncountPokeData.AddOutbreakLotvalue(builder, OutbreakLotvalue);
            global::EncountPokeData.AddBandFormno(builder, BandFormno);
            global::EncountPokeData.AddBandSex(builder, BandSex);
            global::EncountPokeData.AddBandpoke(builder, Bandpoke);
            global::EncountPokeData.AddBandtype(builder, Bandtype);
            global::EncountPokeData.AddBandrate(builder, Bandrate);
            global::EncountPokeData.AddFlagName(builder, flagNameOffset);
            global::EncountPokeData.AddTimetable(builder, timeOffset);
            global::EncountPokeData.AddMaxheight(builder, Maxheight);
            global::EncountPokeData.AddMinheight(builder, Minheight);
            global::EncountPokeData.AddLocationName(builder, locationNameOffset);
            global::EncountPokeData.AddArea(builder, areaOffset);
            global::EncountPokeData.AddLotvalue4(builder, Lotvalue4);
            global::EncountPokeData.AddBiome4(builder, Biome4);
            global::EncountPokeData.AddLotvalue3(builder, Lotvalue3);
            global::EncountPokeData.AddBiome3(builder, Biome3);
            global::EncountPokeData.AddLotvalue2(builder, Lotvalue2);
            global::EncountPokeData.AddBiome2(builder, Biome2);
            global::EncountPokeData.AddLotvalue1(builder, Lotvalue1);
            global::EncountPokeData.AddBiome1(builder, Biome1);
            global::EncountPokeData.AddLotvalue(builder, Lotvalue);
            global::EncountPokeData.AddMaxlevel(builder, Maxlevel);
            global::EncountPokeData.AddMinlevel(builder, Minlevel);
            global::EncountPokeData.AddFormno(builder, Formno);
            global::EncountPokeData.AddSex(builder, Sex);
            global::EncountPokeData.AddDevid(builder, Devid);
            if (Enabletable is not null)
            {
                global::EncountPokeData.AddEnabletable(builder, Enabletable.Write(builder));
            }

            if (BringItem is not null)
            {
                global::EncountPokeData.AddBringItem(builder, BringItem.Write(builder));
            }

            return global::EncountPokeData.EndEncountPokeData(builder);
        }
    }

    private sealed record EnableTableRow(bool Land, bool UpWater, bool Underwater, bool Air1, bool Air2)
    {
        public static EnableTableRow From(global::EnableTable row) =>
            new(row.Land, row.UpWater, row.Underwater, row.Air1, row.Air2);

        public Offset<global::EnableTable> Write(FlatBufferBuilder builder) =>
            global::EnableTable.CreateEnableTable(builder, Land, UpWater, Underwater, Air1, Air2);
    }

    private sealed record TimeTableRow(bool Morning, bool Noon, bool Evening, bool Night)
    {
        public static TimeTableRow From(global::TimeTable row) =>
            new(row.Morning, row.Noon, row.Evening, row.Night);

        public Offset<global::TimeTable> Write(FlatBufferBuilder builder) =>
            global::TimeTable.CreateTimeTable(builder, Morning, Noon, Evening, Night);
    }

    private sealed record VersionTableRow(bool A, bool B)
    {
        public static VersionTableRow From(global::VersionTable row) => new(row.A, row.B);

        public Offset<global::VersionTable> Write(FlatBufferBuilder builder) =>
            global::VersionTable.CreateVersionTable(builder, A, B);
    }

    private sealed record BringItemRow(global::ItemID ItemID, sbyte BringRate)
    {
        public static BringItemRow From(global::BringItem row) => new(row.ItemID, row.BringRate);

        public Offset<global::BringItem> Write(FlatBufferBuilder builder) =>
            global::BringItem.CreateBringItem(builder, ItemID, BringRate);
    }
}
