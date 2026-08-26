// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Api.Editing;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.SV.Encounters;
using KM.SV.Pokemon;
using KM.SV.Trainers;
using KM.SV.Workflows;
using KM.SwSh.Encounters;
using KM.SwSh.Pokemon;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using KM.ZA.Encounters;
using KM.ZA.Pokemon;
using KM.ZA.Trainers;
using KM.ZA.Workflows;

namespace KM.Tools.Application;

/// <summary>
/// Concrete logical-row adapters. Every mutation is computed from a fresh effective workflow
/// and returned as an immutable edit session; failed batches return the original session.
/// </summary>
public sealed class RowClipboardWorkflowMutationProvider
{
    private const string Domain = "rowClipboard";
    private readonly SvWorkflowService sv;
    private readonly SwShWorkflowService swsh;
    private readonly ZaWorkflowService za;

    public RowClipboardWorkflowMutationProvider(
        SwShWorkflowService swsh,
        SvWorkflowService sv,
        ZaWorkflowService za)
    {
        this.swsh = swsh ?? throw new ArgumentNullException(nameof(swsh));
        this.sv = sv ?? throw new ArgumentNullException(nameof(sv));
        this.za = za ?? throw new ArgumentNullException(nameof(za));
    }

    public string CaptureSourceFingerprint(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => swsh.CaptureSemanticExploreSourceFingerprint(paths),
            ProjectGame.Scarlet or ProjectGame.Violet => sv.CaptureSemanticExploreSourceFingerprint(paths),
            ProjectGame.ZA => za.CaptureSemanticExploreSourceFingerprint(paths),
            _ => throw new InvalidOperationException("A supported game is required."),
        };
    }

    public RowClipboardMutationResult Mutate(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(target);

        if (envelope.Editor == RowClipboardAdapterCatalog.PokemonLearnset.Editor)
        {
            return MutateLearnset(paths, session, envelope, mode, target);
        }

        if (envelope.Editor == RowClipboardAdapterCatalog.EncounterSlot.Editor)
        {
            return MutateEncounters(paths, session, envelope, mode, target);
        }

        if (envelope.Editor == RowClipboardAdapterCatalog.TrainerParty.Editor)
        {
            return MutateTrainerParty(paths, session, envelope, mode, target);
        }

        return Failure(session, RowClipboardDiagnosticCodes.UnsupportedAdapter, "This logical-row clipboard schema is not supported.");
    }

    private RowClipboardMutationResult MutateLearnset(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target)
    {
        if (target.PersonalId is not { } personalId
            || (mode == RowClipboardPasteMode.Replace && target.Slot is null)
            || (mode == RowClipboardPasteMode.Append && target.Slot is not null)
            || mode is not (RowClipboardPasteMode.Replace or RowClipboardPasteMode.Append))
        {
            return Failure(session, RowClipboardDiagnosticCodes.TargetInvalid, "The Pokemon learnset paste target or mode is invalid.");
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var rows = new List<(int MoveId, int Level)>();
        foreach (var sourceRow in envelope.Rows)
        {
            if (!TryRequireExactFields(sourceRow, ["level", "moveId"], diagnostics)
                || !TryReadNonNegativeInt(sourceRow, "moveId", out var moveId)
                || !TryReadNonNegativeInt(sourceRow, "level", out var level))
            {
                AddOnce(diagnostics, RowClipboardDiagnosticCodes.BatchRejected, "A Pokemon learnset row contains an invalid or incomplete typed value.");
                continue;
            }

            rows.Add((moveId, level));
        }

        if (HasErrors(diagnostics))
        {
            return new RowClipboardMutationResult(session, diagnostics, []);
        }

        return paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => MutateSwShLearnset(paths, session, envelope, target, rows),
            ProjectGame.Scarlet or ProjectGame.Violet => MutateSvLearnset(paths, session, envelope, target, rows),
            ProjectGame.ZA => MutateZaLearnset(paths, session, envelope, target, rows),
            _ => Failure(session, RowClipboardDiagnosticCodes.ScopeMismatch, "A supported game is required."),
        };
    }

    private RowClipboardMutationResult MutateSwShLearnset(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteTarget target,
        IReadOnlyList<(int MoveId, int Level)> rows)
    {
        var initial = swsh.ReadPokemonEffectiveFreshBounded(paths, session);
        var pokemon = initial.Workflow.Pokemon.FirstOrDefault(value => value.PersonalId == target.PersonalId);
        if (pokemon is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                pokemon is null,
                "The Pokemon learnset paste target does not exist.");
        }

        var start = target.Slot ?? pokemon.Learnset.Count;
        var preview = LearnsetPreviewRows(pokemon.Learnset, start, rows, static value => (value.MoveId, value.Level));
        var current = session;
        SwShPokemonEditResult result = initial;
        for (var index = 0; index < rows.Count; index++)
        {
            result = swsh.UpdatePokemonLearnsetFreshBounded(
                paths,
                current,
                pokemon.PersonalId,
                "upsert",
                start + index,
                rows[index].MoveId,
                rows[index].Level);
            if (HasErrors(result.Diagnostics))
            {
                return new RowClipboardMutationResult(session, result.Diagnostics, preview);
            }

            current = result.Session;
        }

        return new RowClipboardMutationResult(
            current,
            result.Diagnostics,
            LearnsetPreviewRows(
                pokemon.Learnset,
                result.Workflow.Pokemon.First(value => value.PersonalId == pokemon.PersonalId).Learnset,
                start,
                rows.Count,
                static value => (value.MoveId, value.Level)));
    }

    private RowClipboardMutationResult MutateSvLearnset(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteTarget target,
        IReadOnlyList<(int MoveId, int Level)> rows)
    {
        var initial = sv.ReadPokemonEffectiveFreshBounded(paths, session);
        var pokemon = initial.Workflow.Pokemon.FirstOrDefault(value => value.PersonalId == target.PersonalId);
        if (pokemon is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                pokemon is null,
                "The Pokemon learnset paste target does not exist.");
        }

        var start = target.Slot ?? pokemon.Learnset.Count;
        var preview = LearnsetPreviewRows(pokemon.Learnset, start, rows, static value => (value.MoveId, value.Level));
        var current = session;
        SvPokemonEditResult result = initial;
        for (var index = 0; index < rows.Count; index++)
        {
            result = sv.UpdatePokemonLearnsetFreshBounded(
                paths,
                current,
                pokemon.PersonalId,
                "upsert",
                start + index,
                rows[index].MoveId,
                rows[index].Level);
            if (HasErrors(result.Diagnostics))
            {
                return new RowClipboardMutationResult(session, result.Diagnostics, preview);
            }

            current = result.Session;
        }

        return new RowClipboardMutationResult(
            current,
            result.Diagnostics,
            LearnsetPreviewRows(
                pokemon.Learnset,
                result.Workflow.Pokemon.First(value => value.PersonalId == pokemon.PersonalId).Learnset,
                start,
                rows.Count,
                static value => (value.MoveId, value.Level)));
    }

    private RowClipboardMutationResult MutateZaLearnset(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteTarget target,
        IReadOnlyList<(int MoveId, int Level)> rows)
    {
        var initial = za.ReadPokemonEffectiveFreshBounded(paths, session);
        var pokemon = initial.Workflow.Pokemon.FirstOrDefault(value => value.PersonalId == target.PersonalId);
        if (pokemon is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                pokemon is null,
                "The Pokemon learnset paste target does not exist.");
        }

        var start = target.Slot ?? pokemon.Learnset.Count;
        var preview = LearnsetPreviewRows(pokemon.Learnset, start, rows, static value => (value.MoveId, value.Level));
        var current = session;
        ZaPokemonEditResult result = initial;
        for (var index = 0; index < rows.Count; index++)
        {
            result = za.UpdatePokemonLearnsetFreshBounded(
                paths,
                current,
                pokemon.PersonalId,
                "upsert",
                start + index,
                rows[index].MoveId,
                rows[index].Level);
            if (HasErrors(result.Diagnostics))
            {
                return new RowClipboardMutationResult(session, result.Diagnostics, preview);
            }

            current = result.Session;
        }

        return new RowClipboardMutationResult(
            current,
            result.Diagnostics,
            LearnsetPreviewRows(
                pokemon.Learnset,
                result.Workflow.Pokemon.First(value => value.PersonalId == pokemon.PersonalId).Learnset,
                start,
                rows.Count,
                static value => (value.MoveId, value.Level)));
    }

    private RowClipboardMutationResult MutateTrainerParty(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target)
    {
        if (mode != RowClipboardPasteMode.Replace
            || target.TrainerId is not { } trainerId
            || target.Slot is not { } start
            || start + envelope.Rows.Length > 6)
        {
            return Failure(session, RowClipboardDiagnosticCodes.TargetInvalid, "The Trainer party paste target is invalid.");
        }

        return paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => MutateSwShTrainerParty(paths, session, envelope, trainerId, start),
            ProjectGame.Scarlet or ProjectGame.Violet => MutateSvTrainerParty(paths, session, envelope, trainerId, start),
            ProjectGame.ZA => MutateZaTrainerParty(paths, session, envelope, trainerId, start),
            _ => Failure(session, RowClipboardDiagnosticCodes.ScopeMismatch, "A supported game is required."),
        };
    }

    private RowClipboardMutationResult MutateSwShTrainerParty(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        int trainerId,
        int start)
    {
        var initial = swsh.ReadTrainersEffectiveFreshBounded(paths, session);
        var trainer = initial.Workflow.Trainers.FirstOrDefault(value => value.TrainerId == trainerId);
        if (trainer is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                trainer is null,
                "The Trainer party paste target does not exist.");
        }

        var before = BuildTrainerPreviewRows(
            envelope,
            trainerId,
            start,
            slot => trainer.Team.FirstOrDefault(value => value.Slot == slot) is { } member
                ? TrainerValues(member)
                : []);
        if (before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(session, before.Diagnostics, before.Rows);
        }

        var updates = BuildSwShTrainerUpdates(envelope, trainerId, start);
        var result = swsh.UpdateTrainerFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updatedTrainer = result.Workflow.Trainers.First(value => value.TrainerId == trainerId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => TrainerValues(updatedTrainer.Team.First(value => value.Slot == slot))));
    }

    private RowClipboardMutationResult MutateSvTrainerParty(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        int trainerId,
        int start)
    {
        var initial = sv.UpdateTrainerFieldsFreshBounded(paths, session, []);
        var trainer = initial.Workflow.Trainers.FirstOrDefault(value => value.TrainerId == trainerId);
        if (trainer is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                trainer is null,
                "The Trainer party paste target does not exist.");
        }

        var before = BuildTrainerPreviewRows(
            envelope,
            trainerId,
            start,
            slot => trainer.Team.FirstOrDefault(value => value.Slot == slot) is { } member
                ? TrainerValues(member)
                : []);
        if (before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(session, before.Diagnostics, before.Rows);
        }

        var updates = BuildSvTrainerUpdates(envelope, trainerId, start);
        var result = sv.UpdateTrainerFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updatedTrainer = result.Workflow.Trainers.First(value => value.TrainerId == trainerId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => TrainerValues(updatedTrainer.Team.First(value => value.Slot == slot))));
    }

    private RowClipboardMutationResult MutateZaTrainerParty(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        int trainerId,
        int start)
    {
        var initial = za.UpdateTrainerFieldsFreshBounded(paths, session, []);
        var trainer = initial.Workflow.Trainers.FirstOrDefault(value => value.TrainerId == trainerId);
        if (trainer is null || HasErrors(initial.Diagnostics))
        {
            return MissingTargetOrFailure(
                session,
                initial.Diagnostics,
                trainer is null,
                "The Trainer party paste target does not exist.");
        }

        var before = BuildTrainerPreviewRows(
            envelope,
            trainerId,
            start,
            slot => trainer.Team.FirstOrDefault(value => value.Slot == slot) is { } member
                ? TrainerValues(member)
                : []);
        if (before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(session, before.Diagnostics, before.Rows);
        }

        var updates = BuildZaTrainerUpdates(envelope, trainerId, start);
        var result = za.UpdateTrainerFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updatedTrainer = result.Workflow.Trainers.First(value => value.TrainerId == trainerId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => TrainerValues(updatedTrainer.Team.First(value => value.Slot == slot))));
    }

    private RowClipboardMutationResult MutateEncounters(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardPasteTarget target)
    {
        if (mode != RowClipboardPasteMode.Replace
            || target.TableId is not { } tableId
            || target.Slot is not { } start)
        {
            return Failure(session, RowClipboardDiagnosticCodes.TargetInvalid, "The encounter paste target is invalid.");
        }

        return paths.SelectedGame switch
        {
            ProjectGame.Sword or ProjectGame.Shield => MutateSwShEncounters(paths, session, envelope, tableId, start),
            ProjectGame.Scarlet or ProjectGame.Violet => MutateSvEncounters(paths, session, envelope, tableId, start),
            ProjectGame.ZA => MutateZaEncounters(paths, session, envelope, tableId, start),
            _ => Failure(session, RowClipboardDiagnosticCodes.ScopeMismatch, "A supported game is required."),
        };
    }

    private RowClipboardMutationResult MutateSwShEncounters(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        string tableId,
        int start)
    {
        var initial = swsh.UpdateEncounterSlotFieldsFreshBounded(paths, session, []);
        var table = initial.Workflow.Tables.FirstOrDefault(value => value.TableId == tableId);
        var before = BuildEncounterPreviewRows(
            envelope,
            tableId,
            start,
            slot => table?.Slots.FirstOrDefault(value => value.Slot == slot) is { } row ? EncounterValues(row) : []);
        if (table is null || HasErrors(initial.Diagnostics) || before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(
                session,
                WithMissingTargetDiagnostic(
                    initial.Diagnostics.Concat(before.Diagnostics),
                    table is null,
                    "The encounter paste target does not exist."),
                before.Rows);
        }

        var updates = BuildSwShEncounterUpdates(envelope, tableId, start);
        var result = swsh.UpdateEncounterSlotFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updated = result.Workflow.Tables.First(value => value.TableId == tableId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => EncounterValues(updated.Slots.First(value => value.Slot == slot))));
    }

    private RowClipboardMutationResult MutateSvEncounters(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        string tableId,
        int start)
    {
        var initial = sv.UpdateEncounterSlotFieldsFreshBounded(paths, session, []);
        var table = initial.Workflow.Tables.FirstOrDefault(value => value.TableId == tableId);
        var before = BuildEncounterPreviewRows(
            envelope,
            tableId,
            start,
            slot => table?.Slots.FirstOrDefault(value => value.Slot == slot) is { } row ? EncounterValues(row) : []);
        if (table is null || HasErrors(initial.Diagnostics) || before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(
                session,
                WithMissingTargetDiagnostic(
                    initial.Diagnostics.Concat(before.Diagnostics),
                    table is null,
                    "The encounter paste target does not exist."),
                before.Rows);
        }

        var updates = BuildSvEncounterUpdates(envelope, tableId, start);
        var result = sv.UpdateEncounterSlotFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updated = result.Workflow.Tables.First(value => value.TableId == tableId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => EncounterValues(updated.Slots.First(value => value.Slot == slot))));
    }

    private RowClipboardMutationResult MutateZaEncounters(
        ProjectPaths paths,
        EditSession session,
        RowClipboardEnvelopeV1 envelope,
        string tableId,
        int start)
    {
        var initial = za.UpdateEncounterSlotFieldsFreshBounded(paths, session, []);
        var table = initial.Workflow.Tables.FirstOrDefault(value => value.TableId == tableId);
        var before = BuildEncounterPreviewRows(
            envelope,
            tableId,
            start,
            slot => table?.Slots.FirstOrDefault(value => value.Slot == slot) is { } row ? EncounterValues(row) : []);
        if (table is null || HasErrors(initial.Diagnostics) || before.Diagnostics.Count > 0)
        {
            return new RowClipboardMutationResult(
                session,
                WithMissingTargetDiagnostic(
                    initial.Diagnostics.Concat(before.Diagnostics),
                    table is null,
                    "The encounter paste target does not exist."),
                before.Rows);
        }

        var updates = BuildZaEncounterUpdates(envelope, tableId, start);
        var result = za.UpdateEncounterSlotFieldsFreshBounded(paths, session, updates);
        if (HasErrors(result.Diagnostics))
        {
            return new RowClipboardMutationResult(session, result.Diagnostics, before.Rows);
        }

        var updated = result.Workflow.Tables.First(value => value.TableId == tableId);
        return new RowClipboardMutationResult(
            result.Session,
            result.Diagnostics,
            MergeAfter(before.Rows, slot => EncounterValues(updated.Slots.First(value => value.Slot == slot))));
    }

    private static PreviewBuildResult BuildTrainerPreviewRows(
        RowClipboardEnvelopeV1 envelope,
        int trainerId,
        int start,
        Func<int, IReadOnlyList<RowClipboardOwnedValue>> readBefore)
    {
        var rows = new List<RowClipboardMutationPreviewRow>();
        var diagnostics = new List<ValidationDiagnostic>();
        for (var index = 0; index < envelope.Rows.Length; index++)
        {
            var slot = start + index;
            var before = readBefore(slot);
            if (before.Count == 0
                || !TryRequireExactFields(envelope.Rows[index], before.Select(value => value.FieldKey), diagnostics))
            {
                AddOnce(diagnostics, RowClipboardDiagnosticCodes.BatchRejected, "A complete Trainer party member does not match the target's editable fields.");
            }

            rows.Add(new RowClipboardMutationPreviewRow(
                TrainerTargetIdentity(trainerId, slot),
                before,
                envelope.Rows[index].Values));
        }

        return new PreviewBuildResult(rows, diagnostics);
    }

    private static PreviewBuildResult BuildEncounterPreviewRows(
        RowClipboardEnvelopeV1 envelope,
        string tableId,
        int start,
        Func<int, IReadOnlyList<RowClipboardOwnedValue>> readBefore)
    {
        var rows = new List<RowClipboardMutationPreviewRow>();
        var diagnostics = new List<ValidationDiagnostic>();
        for (var index = 0; index < envelope.Rows.Length; index++)
        {
            var slot = start + index;
            var before = readBefore(slot);
            if (before.Count == 0
                || !TryRequireExactFields(envelope.Rows[index], before.Select(value => value.FieldKey), diagnostics))
            {
                AddOnce(diagnostics, RowClipboardDiagnosticCodes.BatchRejected, "An encounter row does not match the target's editable fields.");
            }

            rows.Add(new RowClipboardMutationPreviewRow(
                EncounterTargetIdentity(tableId, slot),
                before,
                envelope.Rows[index].Values));
        }

        return new PreviewBuildResult(rows, diagnostics);
    }

    private static IReadOnlyList<RowClipboardMutationPreviewRow> MergeAfter(
        IReadOnlyList<RowClipboardMutationPreviewRow> before,
        Func<int, IReadOnlyList<RowClipboardOwnedValue>> readAfter) =>
        before.Select(row => row with { After = readAfter(ParseSlot(row.TargetIdentity.Key)) }).ToArray();

    private static IReadOnlyList<RowClipboardMutationPreviewRow> LearnsetPreviewRows<T>(
        IReadOnlyList<T> source,
        int start,
        IReadOnlyList<(int MoveId, int Level)> pasted,
        Func<T, (int MoveId, int Level)> read)
    {
        var rows = new List<RowClipboardMutationPreviewRow>();
        for (var index = 0; index < pasted.Count; index++)
        {
            var slot = start + index;
            var before = slot < source.Count ? LearnsetValues(read(source[slot])) : [];
            rows.Add(new RowClipboardMutationPreviewRow(
                LearnsetTargetIdentity(slot),
                before,
                LearnsetValues(pasted[index])));
        }

        return rows;
    }

    private static IReadOnlyList<RowClipboardMutationPreviewRow> LearnsetPreviewRows<T>(
        IReadOnlyList<T> beforeSource,
        IReadOnlyList<T> afterSource,
        int start,
        int count,
        Func<T, (int MoveId, int Level)> read)
    {
        var rows = new List<RowClipboardMutationPreviewRow>();
        for (var index = 0; index < count; index++)
        {
            var slot = start + index;
            rows.Add(new RowClipboardMutationPreviewRow(
                LearnsetTargetIdentity(slot),
                slot < beforeSource.Count ? LearnsetValues(read(beforeSource[slot])) : [],
                slot < afterSource.Count ? LearnsetValues(read(afterSource[slot])) : []));
        }

        return rows;
    }

    private static IReadOnlyList<SwShTrainerFieldUpdate> BuildSwShTrainerUpdates(RowClipboardEnvelopeV1 envelope, int trainerId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new SwShTrainerFieldUpdate(trainerId, slot, field, value));

    private static IReadOnlyList<SvTrainerFieldUpdate> BuildSvTrainerUpdates(RowClipboardEnvelopeV1 envelope, int trainerId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new SvTrainerFieldUpdate(trainerId, slot, field, value));

    private static IReadOnlyList<ZaTrainerFieldUpdate> BuildZaTrainerUpdates(RowClipboardEnvelopeV1 envelope, int trainerId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new ZaTrainerFieldUpdate(trainerId, slot, field, value));

    private static IReadOnlyList<SwShEncounterSlotFieldUpdate> BuildSwShEncounterUpdates(RowClipboardEnvelopeV1 envelope, string tableId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new SwShEncounterSlotFieldUpdate(tableId, slot, field, value));

    private static IReadOnlyList<SvEncounterSlotFieldUpdate> BuildSvEncounterUpdates(RowClipboardEnvelopeV1 envelope, string tableId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new SvEncounterSlotFieldUpdate(tableId, slot, field, value));

    private static IReadOnlyList<ZaEncounterSlotFieldUpdate> BuildZaEncounterUpdates(RowClipboardEnvelopeV1 envelope, string tableId, int start) =>
        BuildFieldUpdates(envelope, start, (slot, field, value) => new ZaEncounterSlotFieldUpdate(tableId, slot, field, value));

    private static IReadOnlyList<T> BuildFieldUpdates<T>(
        RowClipboardEnvelopeV1 envelope,
        int start,
        Func<int, string, string, T> create)
    {
        var updates = new List<T>();
        for (var index = 0; index < envelope.Rows.Length; index++)
        {
            foreach (var value in envelope.Rows[index].Values
                         .OrderBy(value => FieldOrder(value.FieldKey)))
            {
                updates.Add(create(start + index, value.FieldKey, ScalarText(value.Value)));
            }
        }

        return updates;
    }

    private static int FieldOrder(string field) => field switch
    {
        "speciesId" => 0,
        "form" => 1,
        _ => 2,
    };

    private static string ScalarText(RowClipboardValue value) => value switch
    {
        RowClipboardBooleanValue boolean => boolean.Value ? "1" : "0",
        RowClipboardSignedIntegerValue signed => signed.CanonicalValue,
        RowClipboardUnsignedIntegerValue unsigned => unsigned.CanonicalValue,
        _ => throw new ArgumentException("This logical-row field is not a scalar editable value.", nameof(value)),
    };

    private static IReadOnlyList<RowClipboardOwnedValue> TrainerValues(SvTrainerPokemonRecord value) =>
        CommonTrainerValues(value.SpeciesId, value.Form, value.Level, value.HeldItemId, value.MoveIds, value.Gender, value.Ability, value.Nature, value.Evs.HP, value.Evs.Attack, value.Evs.Defense, value.Evs.SpecialAttack, value.Evs.SpecialDefense, value.Evs.Speed, value.Ivs.HP, value.Ivs.Attack, value.Ivs.Defense, value.Ivs.SpecialAttack, value.Ivs.SpecialDefense, value.Ivs.Speed, value.Shiny)
            .Concat(value.TeraType is { } tera ? [Signed("teraType", tera)] : [])
            .OrderBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RowClipboardOwnedValue> TrainerValues(ZaTrainerPokemonRecord value) =>
        CommonTrainerValues(value.SpeciesId, value.Form, value.Level, value.HeldItemId, value.MoveIds, value.Gender, value.Ability, value.Nature, value.Evs.HP, value.Evs.Attack, value.Evs.Defense, value.Evs.SpecialAttack, value.Evs.SpecialDefense, value.Evs.Speed, value.Ivs.HP, value.Ivs.Attack, value.Ivs.Defense, value.Ivs.SpecialAttack, value.Ivs.SpecialDefense, value.Ivs.Speed, value.Shiny);

    private static IReadOnlyList<RowClipboardOwnedValue> TrainerValues(SwShTrainerPokemonRecord value) =>
        CommonTrainerValues(value.SpeciesId, value.Form, value.Level, value.HeldItemId, value.MoveIds, value.Gender, value.Ability, value.Nature, value.Evs.HP, value.Evs.Attack, value.Evs.Defense, value.Evs.SpecialAttack, value.Evs.SpecialDefense, value.Evs.Speed, value.Ivs.HP, value.Ivs.Attack, value.Ivs.Defense, value.Ivs.SpecialAttack, value.Ivs.SpecialDefense, value.Ivs.Speed, value.Shiny)
            .Concat([
                Signed("dynamaxLevel", value.DynamaxLevel),
                Boolean("canGigantamax", value.CanGigantamax),
                Boolean("canDynamax", value.CanDynamax),
            ])
            .OrderBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RowClipboardOwnedValue> CommonTrainerValues(
        int speciesId, int form, int level, int heldItemId, IReadOnlyList<int> moves,
        int gender, int ability, int nature,
        int evHp, int evAttack, int evDefense, int evSpecialAttack, int evSpecialDefense, int evSpeed,
        int ivHp, int ivAttack, int ivDefense, int ivSpecialAttack, int ivSpecialDefense, int ivSpeed,
        bool shiny)
    {
        var moveIds = moves.Concat([0, 0, 0, 0]).Take(4).ToArray();
        return new RowClipboardOwnedValue[]
        {
            Signed("ability", ability), Signed("evAttack", evAttack), Signed("evDefense", evDefense),
            Signed("evHp", evHp), Signed("evSpecialAttack", evSpecialAttack), Signed("evSpecialDefense", evSpecialDefense), Signed("evSpeed", evSpeed),
            Signed("form", form), Signed("gender", gender), Signed("heldItemId", heldItemId),
            Signed("ivAttack", ivAttack), Signed("ivDefense", ivDefense), Signed("ivHp", ivHp),
            Signed("ivSpecialAttack", ivSpecialAttack), Signed("ivSpecialDefense", ivSpecialDefense), Signed("ivSpeed", ivSpeed),
            Signed("level", level), Signed("move1Id", moveIds[0]), Signed("move2Id", moveIds[1]), Signed("move3Id", moveIds[2]), Signed("move4Id", moveIds[3]),
            Signed("nature", nature), Boolean("shiny", shiny), Signed("speciesId", speciesId),
        }.OrderBy(value => value.FieldKey, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<RowClipboardOwnedValue> EncounterValues(SvEncounterSlotRecord value) =>
        BasicEncounterValues(value.SpeciesId, value.Form, value.LevelMin, value.LevelMax, value.Weight);

    private static IReadOnlyList<RowClipboardOwnedValue> EncounterValues(SwShEncounterSlotRecord value) =>
        BasicEncounterValues(value.SpeciesId, value.Form, value.LevelMin, value.LevelMax, value.Weight);

    private static IReadOnlyList<RowClipboardOwnedValue> BasicEncounterValues(int speciesId, int form, int levelMin, int levelMax, int probability) =>
        new[] { Signed("speciesId", speciesId), Signed("form", form), Signed("levelMin", levelMin), Signed("levelMax", levelMax), Signed("probability", probability) }
            .OrderBy(value => value.FieldKey, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<RowClipboardOwnedValue> EncounterValues(ZaEncounterSlotRecord value)
    {
        var result = new List<RowClipboardOwnedValue>
        {
            Signed("speciesId", value.SpeciesId), Signed("form", value.Form), Signed("levelMin", value.LevelMin), Signed("levelMax", value.LevelMax),
        };
        Add(result, "weight", value.CanEditWeight ? value.Weight : null);
        Add(result, "slotMaxCount", value.CanEditSlotMaxCount ? value.SlotMaxCount : null);
        Add(result, "appearanceMinCount", value.CanEditAppearanceCounts ? value.AppearanceMinCount : null);
        Add(result, "appearanceMaxCount", value.CanEditAppearanceCounts ? value.AppearanceMaxCount : null);
        Add(result, "alphaChancePercent", value.HasAlphaChance ? value.AlphaChancePercent : null);
        Add(result, "alphaLevelBonus", value.HasAlphaChance ? value.AlphaLevelBonus : null);
        Add(result, "heldItemId", value.HeldItemId);
        Add(result, "ability", value.Ability);
        Add(result, "nature", value.Nature);
        Add(result, "gender", value.Gender);
        Add(result, "shinyLock", value.ShinyMode);
        if (value.MoveIds is { } moves)
        {
            var padded = moves.Concat([0, 0, 0, 0]).Take(4).ToArray();
            Add(result, "move1Id", padded[0]); Add(result, "move2Id", padded[1]); Add(result, "move3Id", padded[2]); Add(result, "move4Id", padded[3]);
        }
        Add(result, "flawlessIvCount", value.FlawlessIvCount);
        Add(result, "ivHp", value.IvHp); Add(result, "ivAttack", value.IvAttack); Add(result, "ivDefense", value.IvDefense);
        Add(result, "ivSpecialAttack", value.IvSpecialAttack); Add(result, "ivSpecialDefense", value.IvSpecialDefense); Add(result, "ivSpeed", value.IvSpeed);
        if (value.CanEditStrengthenValues)
        {
            Add(result, "strengthenHp", value.StrengthenHp); Add(result, "strengthenAttack", value.StrengthenAttack); Add(result, "strengthenDefense", value.StrengthenDefense);
            Add(result, "strengthenSpecialAttack", value.StrengthenSpecialAttack); Add(result, "strengthenSpecialDefense", value.StrengthenSpecialDefense); Add(result, "strengthenSpeed", value.StrengthenSpeed);
        }
        return result.OrderBy(item => item.FieldKey, StringComparer.Ordinal).ToArray();
    }

    private static void Add(List<RowClipboardOwnedValue> values, string field, int? value)
    {
        if (value is { } present)
        {
            values.Add(Signed(field, present));
        }
    }

    private static IReadOnlyList<RowClipboardOwnedValue> LearnsetValues((int MoveId, int Level) value) =>
        [Unsigned("level", value.Level), Unsigned("moveId", value.MoveId)];

    private static RowClipboardOwnedValue Signed(string field, int value) =>
        new(field, new RowClipboardSignedIntegerValue(value.ToString(CultureInfo.InvariantCulture)));

    private static RowClipboardOwnedValue Unsigned(string field, int value) =>
        new(field, new RowClipboardUnsignedIntegerValue(value.ToString(CultureInfo.InvariantCulture)));

    private static RowClipboardOwnedValue Boolean(string field, bool value) =>
        new(field, new RowClipboardBooleanValue(value));

    private static bool TryReadNonNegativeInt(RowClipboardLogicalRow row, string field, out int value)
    {
        var owned = row.Values.FirstOrDefault(candidate => candidate.FieldKey == field);
        if (owned?.Value is RowClipboardUnsignedIntegerValue unsigned
            && unsigned.Value <= int.MaxValue)
        {
            value = (int)unsigned.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryRequireExactFields(
        RowClipboardLogicalRow row,
        IEnumerable<string> expected,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var actual = row.Values.Select(value => value.FieldKey).Order(StringComparer.Ordinal).ToArray();
        var normalizedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (actual.SequenceEqual(normalizedExpected, StringComparer.Ordinal))
        {
            return true;
        }

        AddOnce(diagnostics, RowClipboardDiagnosticCodes.BatchRejected, "The logical row is incomplete or contains fields that are not owned by this target.");
        return false;
    }

    private static int ParseSlot(string key)
    {
        var separator = key.LastIndexOfAny([':', '#']);
        return separator >= 0
            && int.TryParse(key[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                ? slot
                : throw new InvalidOperationException("A logical-row target slot is invalid.");
    }

    private static RowClipboardLogicalIdentity TrainerTargetIdentity(int trainerId, int slot) =>
        new(RowClipboardAdapterCatalog.TrainerPartyRowKind, $"trainer:{trainerId.ToString(CultureInfo.InvariantCulture)}:slot:{slot.ToString(CultureInfo.InvariantCulture)}");

    private static RowClipboardLogicalIdentity EncounterTargetIdentity(string tableId, int slot) =>
        new(RowClipboardAdapterCatalog.EncounterSlotRowKind, $"{tableId}#{slot.ToString(CultureInfo.InvariantCulture)}");

    private static RowClipboardLogicalIdentity LearnsetTargetIdentity(int slot) =>
        new(RowClipboardAdapterCatalog.PokemonLearnsetRowKind, $"slot:{slot.ToString(CultureInfo.InvariantCulture)}");

    private static RowClipboardMutationResult Failure(EditSession session, string code, string message) =>
        new(session, [Error(code, message)], []);

    private static RowClipboardMutationResult MissingTargetOrFailure(
        EditSession session,
        IEnumerable<ValidationDiagnostic> diagnostics,
        bool missing,
        string message) =>
        new(session, WithMissingTargetDiagnostic(diagnostics, missing, message), []);

    private static IReadOnlyList<ValidationDiagnostic> WithMissingTargetDiagnostic(
        IEnumerable<ValidationDiagnostic> diagnostics,
        bool missing,
        string message)
    {
        var result = diagnostics.ToList();
        if (missing)
        {
            AddOnce(result, RowClipboardDiagnosticCodes.TargetInvalid, message);
        }

        return result;
    }

    private static ValidationDiagnostic Error(string code, string message) =>
        new(DiagnosticSeverity.Error, message, Domain: Domain) { Code = code };

    private static void AddOnce(ICollection<ValidationDiagnostic> diagnostics, string code, string message)
    {
        if (!diagnostics.Any(value => string.Equals(value.Code, code, StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(code, message));
        }
    }

    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) =>
        diagnostics.Any(value => value.Severity == DiagnosticSeverity.Error);

    private sealed record PreviewBuildResult(
        IReadOnlyList<RowClipboardMutationPreviewRow> Rows,
        IReadOnlyList<ValidationDiagnostic> Diagnostics);
}
