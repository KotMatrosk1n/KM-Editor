// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Gifts;

internal sealed class SvGiftPokemonEditSessionService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SvWorkflowFileSource fileSource;
    private readonly SvGiftPokemonWorkflowService giftPokemonWorkflowService;

    public SvGiftPokemonEditSessionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SvWorkflowFileSource? fileSource = null,
        SvGiftPokemonWorkflowService? giftPokemonWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SvGiftPokemonWorkflowService(this.fileSource);
    }

    public SvGiftPokemonEditResult UpdateField(
        ProjectPaths paths,
        EditSession? session,
        int giftIndex,
        string field,
        string value)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var currentSession = session ?? EditSession.Start();
        var project = projectWorkspaceService.Open(paths);
        var loadedWorkflow = giftPokemonWorkflowService.Load(project);
        var workflow = OverlayPendingEdits(project, loadedWorkflow, currentSession.PendingEdits);
        var diagnostics = new List<ValidationDiagnostic>();

        if (!SvEditSessionSupport.CanEdit(
                project,
                workflow.Summary,
                workflow.Diagnostics,
                SvEditSessionSupport.GiftPokemonDomain,
                diagnostics))
        {
            return new SvGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var gift = workflow.Gifts.FirstOrDefault(candidate => candidate.GiftIndex == giftIndex);
        if (gift is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon index {giftIndex} is not present in the loaded workflow.",
                SvEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return new SvGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var pendingEdit = CreatePendingEdit(workflow, gift, field, value, diagnostics);
        if (pendingEdit is null)
        {
            return new SvGiftPokemonEditResult(workflow, currentSession, diagnostics);
        }

        var updatedSession = SvEditSessionSupport.ReplacePendingEdit(currentSession, pendingEdit);
        return new SvGiftPokemonEditResult(
            OverlayPendingEdits(project, loadedWorkflow, updatedSession.PendingEdits, diagnostics),
            updatedSession,
            diagnostics);
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();

        SvEditSessionSupport.CanEdit(
            project,
            workflow.Summary,
            workflow.Diagnostics,
            SvEditSessionSupport.GiftPokemonDomain,
            diagnostics);

        foreach (var edit in session.PendingEdits)
        {
            ValidatePendingEdit(workflow, edit, diagnostics);
        }

        if (session.PendingEdits.Count > 0 && diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                "Pending Gift Pokemon change is valid.",
                SvEditSessionSupport.GiftPokemonDomain));
        }

        return new SvEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    public ChangePlan CreateChangePlan(
        ProjectPaths paths,
        EditSession session,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);

        var validation = Validate(paths, session);
        return SvEditSessionSupport.CreateSingleFileChangePlan(
            paths,
            session,
            SvEditSessionSupport.GiftPokemonDomain,
            SvDataPaths.EventAddPokemonArray,
            "Gift Pokemon",
            validation.Diagnostics,
            outputMode);
    }

    public ApplyResult ApplyChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(reviewedPlan);

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateChangePlan(paths, session, outputMode);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!SvEditSessionSupport.ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                SvEditSessionSupport.GiftPokemonDomain,
                expected: "Current reviewed Gift Pokemon change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        try
        {
            var project = projectWorkspaceService.Open(paths);
            var source = fileSource.Read(project, SvDataPaths.EventAddPokemonArray);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in session.PendingEdits)
            {
                ApplyEdit(rows, edit, moveResolver, diagnostics);
            }

            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
            }

            SvWorkflowFileSource.Write(paths, SvDataPaths.EventAddPokemonArray, WriteRows(rows), outputMode);
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.EventAddPokemonArray, outputMode));
            if (outputMode == SvOutputMode.Standalone)
            {
                writtenFiles.Add(SvEditSessionSupport.GeneratedDescriptorReference());
            }

            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Info,
                outputMode == SvOutputMode.Standalone
                    ? "Applied Gift Pokemon change plan as standalone Scarlet/Violet output and patched the Trinity descriptor."
                    : "Applied Gift Pokemon change plan for Trinity Mod Manager. Run this output folder through Trinity Mod Manager before installing.",
                SvEditSessionSupport.GiftPokemonDomain));
        }
        catch (Exception exception)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon output could not be written: {exception.Message}",
                SvEditSessionSupport.GiftPokemonDomain,
                file: $"romfs/{SvDataPaths.EventAddPokemonArray}",
                expected: "Readable source and writable output root"));
        }

        return SvEditSessionSupport.CreateApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
    }

    private static PendingEdit? CreatePendingEdit(
        SvGiftPokemonWorkflow workflow,
        SvGiftPokemonEntry gift,
        string field,
        string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var normalizedField = field.Trim();
        var editableField = SvGiftPokemonWorkflowService.GetEditableField(workflow, normalizedField);
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
            SvEditSessionSupport.GiftPokemonDomain,
            diagnostics);
        if (parsedValue is null)
        {
            return null;
        }

        return SvEditSessionSupport.CreatePendingEdit(
            SvEditSessionSupport.GiftPokemonDomain,
            $"Set {gift.Label} {editableField.Label.ToLowerInvariant()} to {parsedValue.Value}.",
            new ProjectFileReference(gift.Provenance.SourceLayer, gift.Provenance.SourceFile),
            SvGiftPokemonWorkflowService.CreateGiftRecordId(gift.GiftIndex),
            normalizedField,
            parsedValue.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidatePendingEdit(
        SvGiftPokemonWorkflow workflow,
        PendingEdit edit,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Pending edit domain '{edit.Domain}' is not supported by Scarlet/Violet Gift Pokemon.",
                SvEditSessionSupport.GiftPokemonDomain,
                expected: SvEditSessionSupport.GiftPokemonDomain));
            return;
        }

        if (!SvGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || workflow.Gifts.All(candidate => candidate.GiftIndex != giftIndex))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit targets a record that is not loaded.",
                SvEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing gift Pokemon record"));
            return;
        }

        var editableField = SvGiftPokemonWorkflowService.GetEditableField(workflow, edit.Field);
        if (editableField is null)
        {
            diagnostics.Add(CreateUnsupportedFieldDiagnostic(edit.Field ?? string.Empty));
            return;
        }

        _ = SvEditSessionSupport.TryParseInt(
            edit.NewValue,
            editableField.MinimumValue,
            editableField.MaximumValue,
            edit.Field,
            SvEditSessionSupport.GiftPokemonDomain,
            diagnostics);
    }

    private SvGiftPokemonWorkflow OverlayPendingEdits(
        OpenedProject project,
        SvGiftPokemonWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        var pendingEdits = edits
            .Where(edit =>
                string.Equals(edit.Domain, SvEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
                && SvGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out _)
                && int.TryParse(
                    edit.NewValue,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out _))
            .ToArray();

        if (pendingEdits.Length == 0)
        {
            return workflow;
        }

        try
        {
            var overlayDiagnostics = new List<ValidationDiagnostic>();
            var source = fileSource.Read(project, SvDataPaths.EventAddPokemonArray);
            var labels = SvTextLabelLookup.Load(project, fileSource, overlayDiagnostics);
            var abilityResolver = SvGiftPokemonWorkflowService.SvGiftAbilityResolver.Load(
                project,
                fileSource,
                labels,
                overlayDiagnostics);
            var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, overlayDiagnostics);
            var rows = ReadRows(source.Bytes);
            foreach (var edit in pendingEdits)
            {
                ApplyEdit(rows, edit, moveResolver, overlayDiagnostics);
            }

            if (overlayDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                if (diagnostics is not null)
                {
                    foreach (var diagnostic in overlayDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                return workflow;
            }

            var overlaySource = source with { Bytes = WriteRows(rows) };
            var giftsByIndex = SvGiftPokemonWorkflowService
                .LoadRecords(overlaySource, labels, abilityResolver, moveResolver)
                .ToDictionary(gift => gift.GiftIndex);

            return workflow with
            {
                Gifts = workflow.Gifts
                    .Select(gift => giftsByIndex.TryGetValue(gift.GiftIndex, out var updatedGift) ? updatedGift : gift)
                    .ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            diagnostics?.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon pending changes could not be previewed: {exception.Message}",
                SvEditSessionSupport.GiftPokemonDomain,
                file: $"romfs/{SvDataPaths.EventAddPokemonArray}",
                expected: "Readable S/V gift Pokemon source"));
            return workflow;
        }
    }

    private static void ApplyEdit(
        IReadOnlyList<EventGiftRow> rows,
        PendingEdit edit,
        SvDefaultMoveResolver moveResolver,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!string.Equals(edit.Domain, SvEditSessionSupport.GiftPokemonDomain, StringComparison.Ordinal)
            || !SvGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var giftIndex)
            || !int.TryParse(edit.NewValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Pending Gift Pokemon edit is not valid for apply.",
                SvEditSessionSupport.GiftPokemonDomain,
                expected: "Valid Gift Pokemon edit"));
            return;
        }

        var row = rows.ElementAtOrDefault(giftIndex);
        if (row?.PokeData is not { } pokeData)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Gift Pokemon index {giftIndex} is not present in the source event gift array.",
                SvEditSessionSupport.GiftPokemonDomain,
                field: "giftIndex",
                expected: "Existing source gift Pokemon row"));
            return;
        }

        ApplyField(pokeData, edit.Field, value, moveResolver);
    }

    private static void ApplyField(
        PokeDataFullRow row,
        string? field,
        int value,
        SvDefaultMoveResolver moveResolver)
    {
        switch (field)
        {
            case SvGiftPokemonWorkflowService.SpeciesField:
                row.DevId = (global::pml.common.DevID)checked((ushort)value);
                break;
            case SvGiftPokemonWorkflowService.FormField:
                row.FormId = checked((short)value);
                break;
            case SvGiftPokemonWorkflowService.LevelField:
                row.Level = value;
                break;
            case SvGiftPokemonWorkflowService.HeldItemIdField:
                row.Item = (global::ItemID)value;
                break;
            case SvGiftPokemonWorkflowService.BallItemIdField:
                row.BallId = (global::BallType)value;
                break;
            case SvGiftPokemonWorkflowService.AbilityField:
                row.Tokusei = (global::TokuseiType)value;
                break;
            case SvGiftPokemonWorkflowService.NatureField:
                row.Seikaku = (global::SeikakuType)value;
                break;
            case SvGiftPokemonWorkflowService.GenderField:
                row.Sex = (global::SexType)value;
                break;
            case SvGiftPokemonWorkflowService.ShinyLockField:
                row.RareType = (global::RareType)value;
                break;
            case SvGiftPokemonWorkflowService.TeraTypeField:
                row.GemType = (global::GemType)value;
                break;
            case SvGiftPokemonWorkflowService.Move1IdField:
                row.SetMove(0, value, moveResolver);
                break;
            case SvGiftPokemonWorkflowService.Move2IdField:
                row.SetMove(1, value, moveResolver);
                break;
            case SvGiftPokemonWorkflowService.Move3IdField:
                row.SetMove(2, value, moveResolver);
                break;
            case SvGiftPokemonWorkflowService.Move4IdField:
                row.SetMove(3, value, moveResolver);
                break;
            case SvGiftPokemonWorkflowService.FlawlessIvCountField:
                row.SetIvPreset(value);
                break;
            case SvGiftPokemonWorkflowService.IvHpField:
                row.SetIv(value, ivs => ivs with { Hp = value });
                break;
            case SvGiftPokemonWorkflowService.IvAttackField:
                row.SetIv(value, ivs => ivs with { Atk = value });
                break;
            case SvGiftPokemonWorkflowService.IvDefenseField:
                row.SetIv(value, ivs => ivs with { Def = value });
                break;
            case SvGiftPokemonWorkflowService.IvSpecialAttackField:
                row.SetIv(value, ivs => ivs with { SpAtk = value });
                break;
            case SvGiftPokemonWorkflowService.IvSpecialDefenseField:
                row.SetIv(value, ivs => ivs with { SpDef = value });
                break;
            case SvGiftPokemonWorkflowService.IvSpeedField:
                row.SetIv(value, ivs => ivs with { Agi = value });
                break;
            case SvGiftPokemonWorkflowService.ScaleModeField:
                row.ScaleType = (global::SizeType)value;
                break;
            case SvGiftPokemonWorkflowService.ScaleValueField:
                row.ScaleValue = checked((short)value);
                break;
        }
    }

    private static IReadOnlyList<EventGiftRow> ReadRows(byte[] bytes)
    {
        var table = global::EventAddPokemonArray.GetRootAsEventAddPokemonArray(new ByteBuffer(bytes));
        var rows = new List<EventGiftRow>();
        for (var index = 0; index < table.ValuesLength; index++)
        {
            var row = table.Values(index);
            rows.Add(row is { } value ? EventGiftRow.From(value) : new EventGiftRow());
        }

        return rows;
    }

    private static byte[] WriteRows(IReadOnlyList<EventGiftRow> rows)
    {
        var builder = new FlatBufferBuilder(1024);
        var offsets = rows.Select(row => row.Write(builder)).ToArray();
        var vector = global::EventAddPokemonArray.CreateValuesVector(builder, offsets);
        var root = global::EventAddPokemonArray.CreateEventAddPokemonArray(builder, vector);
        global::EventAddPokemonArray.FinishEventAddPokemonArrayBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static ValidationDiagnostic CreateUnsupportedFieldDiagnostic(string field)
    {
        return SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"Gift Pokemon field '{field}' is not supported by Scarlet/Violet Gift Pokemon yet.",
            SvEditSessionSupport.GiftPokemonDomain,
            field: "field",
            expected: "Supported S/V gift Pokemon field");
    }

    private sealed class EventGiftRow
    {
        public string? Label { get; init; }
        public bool PokedexRegistration { get; init; }
        public PokeDataFullRow? PokeData { get; init; }

        public static EventGiftRow From(global::EventAddPokemon row)
        {
            return new EventGiftRow
            {
                Label = row.Label,
                PokedexRegistration = row.PokedexRegistration,
                PokeData = row.PokeData is { } pokeData ? PokeDataFullRow.From(pokeData) : null,
            };
        }

        public Offset<global::EventAddPokemon> Write(FlatBufferBuilder builder)
        {
            var labelOffset = string.IsNullOrEmpty(Label) ? default : builder.CreateString(Label);
            var pokeDataOffset = PokeData?.Write(builder) ?? default;
            return global::EventAddPokemon.CreateEventAddPokemon(
                builder,
                labelOffset,
                pokeDataOffset,
                PokedexRegistration);
        }
    }

    private sealed class PokeDataFullRow
    {
        public global::pml.common.DevID DevId { get; set; }
        public short FormId { get; set; }
        public global::ItemID Item { get; set; }
        public int Level { get; set; }
        public global::SexType Sex { get; set; }
        public global::SeikakuType Seikaku { get; set; }
        public global::SeikakuType SeikakuHosei { get; init; }
        public global::TokuseiType Tokusei { get; set; }
        public global::RareType RareType { get; set; }
        public int RareTryCount { get; init; }
        public global::TalentType TalentType { get; set; }
        public ParamSetRow? TalentValue { get; set; }
        public sbyte TalentVnum { get; set; }
        public ParamSetRow? EffortValue { get; init; }
        public int Friendship { get; init; }
        public global::SizeType HeightType { get; init; }
        public short HeigntValue { get; init; }
        public global::SizeType WeightType { get; init; }
        public short WaightValue { get; init; }
        public global::SizeType ScaleType { get; set; }
        public short ScaleValue { get; set; }
        public bool SetPersonalRand { get; init; }
        public ulong PersonalRand { get; init; }
        public bool SetRandSeed { get; init; }
        public ulong RandSeed { get; init; }
        public global::WazaType WazaType { get; set; }
        public WazaSetRow?[] Waza { get; } = new WazaSetRow?[4];
        public bool UseNickName { get; init; }
        public ulong NicknameLabel { get; init; }
        public ulong ParentNameLabel { get; init; }
        public global::SexType ParentSex { get; init; }
        public global::LangType ParentLangId { get; init; }
        public int ParentMemoryCode { get; init; }
        public int ParentMemoryData { get; init; }
        public int ParentMemoryFeel { get; init; }
        public int ParentMemoryLevel { get; init; }
        public global::LangType LangId { get; init; }
        public global::BallType BallId { get; set; }
        public global::RibbonType SetRibbon { get; init; }
        public bool EventFlg { get; init; }
        public global::GemType GemType { get; set; }
        public sbyte WazaConfirmLevel { get; init; }
        public global::PokeMemoType Pokememo { get; init; }
        public int PokememoPlace { get; init; }
        public long TrainerId { get; init; }

        public static PokeDataFullRow From(global::PokeDataFull row)
        {
            var result = new PokeDataFullRow
            {
                DevId = row.DevId,
                FormId = row.FormId,
                Item = row.Item,
                Level = row.Level,
                Sex = row.Sex,
                Seikaku = row.Seikaku,
                SeikakuHosei = row.SeikakuHosei,
                Tokusei = row.Tokusei,
                RareType = row.RareType,
                RareTryCount = row.RareTryCount,
                TalentType = row.TalentType,
                TalentValue = row.TalentValue is { } talentValue ? ParamSetRow.From(talentValue) : null,
                TalentVnum = row.TalentVnum,
                EffortValue = row.EffortValue is { } effortValue ? ParamSetRow.From(effortValue) : null,
                Friendship = row.Friendship,
                HeightType = row.HeightType,
                HeigntValue = row.HeigntValue,
                WeightType = row.WeightType,
                WaightValue = row.WaightValue,
                ScaleType = row.ScaleType,
                ScaleValue = row.ScaleValue,
                SetPersonalRand = row.SetPersonalRand,
                PersonalRand = row.PersonalRand,
                SetRandSeed = row.SetRandSeed,
                RandSeed = row.RandSeed,
                WazaType = row.WazaType,
                UseNickName = row.UseNickName,
                NicknameLabel = row.NicknameLabel,
                ParentNameLabel = row.ParentNameLabel,
                ParentSex = row.ParentSex,
                ParentLangId = row.ParentLangId,
                ParentMemoryCode = row.ParentMemoryCode,
                ParentMemoryData = row.ParentMemoryData,
                ParentMemoryFeel = row.ParentMemoryFeel,
                ParentMemoryLevel = row.ParentMemoryLevel,
                LangId = row.LangId,
                BallId = row.BallId,
                SetRibbon = row.SetRibbon,
                EventFlg = row.EventFlg,
                GemType = row.GemType,
                WazaConfirmLevel = row.WazaConfirmLevel,
                Pokememo = row.Pokememo,
                PokememoPlace = row.PokememoPlace,
                TrainerId = row.TrainerId,
            };

            result.Waza[0] = row.Waza1 is { } waza1 ? WazaSetRow.From(waza1) : null;
            result.Waza[1] = row.Waza2 is { } waza2 ? WazaSetRow.From(waza2) : null;
            result.Waza[2] = row.Waza3 is { } waza3 ? WazaSetRow.From(waza3) : null;
            result.Waza[3] = row.Waza4 is { } waza4 ? WazaSetRow.From(waza4) : null;
            return result;
        }

        public void SetMove(int index, int moveId, SvDefaultMoveResolver moveResolver)
        {
            if (WazaType == global::WazaType.DEFAULT)
            {
                var currentMoves = Waza
                    .Select(waza => waza is null ? 0 : (int)waza.WazaId)
                    .ToArray();
                var defaultMoves = currentMoves.Any(move => move != 0)
                    ? currentMoves
                    : moveResolver.Resolve((int)DevId, FormId, Level);

                for (var defaultIndex = 0; defaultIndex < Waza.Length; defaultIndex++)
                {
                    var defaultMove = defaultMoves.ElementAtOrDefault(defaultIndex);
                    Waza[defaultIndex] = defaultMove == 0
                        ? null
                        : new WazaSetRow((global::pml.common.WazaID)checked((ushort)defaultMove), 0);
                }

                WazaType = global::WazaType.MANUAL;
            }

            Waza[index] = moveId == 0
                ? null
                : (Waza[index] ?? new WazaSetRow((global::pml.common.WazaID)0, 0)) with
                {
                    WazaId = (global::pml.common.WazaID)checked((ushort)moveId),
                };
        }

        public void SetIvPreset(int value)
        {
            if (value <= 0)
            {
                TalentType = global::TalentType.RANDOM;
                TalentVnum = 0;
                TalentValue = null;
                return;
            }

            TalentType = global::TalentType.V_NUM;
            TalentVnum = checked((sbyte)value);
            TalentValue = null;
        }

        public void SetIv(int value, Func<ParamSetRow, ParamSetRow> update)
        {
            TalentType = global::TalentType.VALUE;
            TalentVnum = 0;
            TalentValue = update(TalentValue ?? ParamSetRow.Zero);
        }

        public Offset<global::PokeDataFull> Write(FlatBufferBuilder builder)
        {
            var wazaOffsets = Waza
                .Select(waza => waza?.Write(builder) ?? default)
                .ToArray();
            var talentOffset = TalentValue?.Write(builder) ?? default;
            var effortOffset = EffortValue?.Write(builder) ?? default;

            return global::PokeDataFull.CreatePokeDataFull(
                builder,
                DevId,
                FormId,
                Item,
                Level,
                Sex,
                Seikaku,
                SeikakuHosei,
                Tokusei,
                RareType,
                RareTryCount,
                TalentType,
                talentOffset,
                TalentVnum,
                effortOffset,
                Friendship,
                HeightType,
                HeigntValue,
                WeightType,
                WaightValue,
                ScaleType,
                ScaleValue,
                SetPersonalRand,
                PersonalRand,
                SetRandSeed,
                RandSeed,
                WazaType,
                wazaOffsets[0],
                wazaOffsets[1],
                wazaOffsets[2],
                wazaOffsets[3],
                UseNickName,
                NicknameLabel,
                ParentNameLabel,
                ParentSex,
                ParentLangId,
                ParentMemoryCode,
                ParentMemoryData,
                ParentMemoryFeel,
                ParentMemoryLevel,
                LangId,
                BallId,
                SetRibbon,
                EventFlg,
                GemType,
                WazaConfirmLevel,
                Pokememo,
                PokememoPlace,
                TrainerId);
        }
    }

    private sealed record WazaSetRow(global::pml.common.WazaID WazaId, sbyte PointUp)
    {
        public static WazaSetRow From(global::WazaSet row) => new(row.WazaId, row.PointUp);

        public Offset<global::WazaSet> Write(FlatBufferBuilder builder) =>
            global::WazaSet.CreateWazaSet(builder, WazaId, PointUp);
    }

    private sealed record ParamSetRow(int Hp, int Atk, int Def, int SpAtk, int SpDef, int Agi)
    {
        public static readonly ParamSetRow Zero = new(0, 0, 0, 0, 0, 0);

        public static ParamSetRow From(global::ParamSet row) =>
            new(row.Hp, row.Atk, row.Def, row.SpAtk, row.SpDef, row.Agi);

        public Offset<global::ParamSet> Write(FlatBufferBuilder builder) =>
            global::ParamSet.CreateParamSet(builder, Hp, Atk, Def, SpAtk, SpDef, Agi);
    }
}
