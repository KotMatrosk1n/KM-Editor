// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.GameDump;
using KM.ZA.Encounters;
using KM.ZA.Moves;
using KM.ZA.ScriptedBosses;
using static KM.Core.GameDump.DumpSchemaBuilder;

namespace KM.ZA.GameDump;

public sealed partial class ZaEditableDumpProvider
{
    private static void BuildMoves(ZaMovesWorkflow data, List<DumpRecord> records)
    {
        var definitions = Definitions(data.EditableFields);
        foreach (var move in data.Moves)
        {
            var record = new DumpRecord(Value(move.MoveId), move.Name, "moves", Value(move.MoveId));
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (ZaRuntimeMoveData.TryParseTimingField(definition.Field, out _, out var occurrence, out var member))
                {
                    foreach (var timing in move.TimingRows.Where(t => occurrence is null || t.Occurrence == occurrence))
                    {
                        var target = occurrence is null ? ZaRuntimeMoveData.TimingSharedField(timing.TimingMoveId, member)
                            : ZaRuntimeMoveData.TimingField(timing.TimingMoveId, timing.Occurrence, member);
                        Add(definition with { Field = target });
                    }
                }
                else Add(definition);
            }
            records.Add(record);

            void Add(DumpFieldDefinition definition)
            {
                var value = ZaMovesEditSessionService.GetEditableValue(move, definition.Field);
                if (value is not null && added.Add(definition.Field)) record.Fields.Add(Field(definition, value));
            }
        }
    }

    private static void BuildBosses(ZaEncountersWorkflow data, List<DumpRecord> records)
    {
        var targets = new Dictionary<int, (string Table, int Slot)>();
        foreach (var table in data.Tables)
            foreach (var slot in table.Slots)
                if (ZaEncounterBossActionRestoreResolver.TryResolve(data, table, slot, out var actions, out _))
                    foreach (var action in actions.Where(a => a.CanEdit && a.SelectorActionId is not null))
                        targets.TryAdd(action.SelectorActionId!.Value, (table.TableId, slot.Slot));

        foreach (var profile in data.ScriptedBosses)
        {
            var record = new DumpRecord(profile.Key, profile.Name, "scriptedBosses", profile.Key);
            record.Fields.Add(new("scope", "scope", profile.Scope, "text", Editable: false));
            record.Children["phases"] = profile.PhaseModel.Phases.Select(phase =>
            {
                var child = new DumpRecord(phase.Key, phase.StageName, "scriptedBosses", phase.Key);
                foreach (var (key, value) in new[] { ("stage", phase.Stage), ("hp_phase", phase.HpPhase), ("species", phase.SpeciesId),
                    ("form", phase.Form), ("minimum_hp_percent", phase.MinimumHpPercent), ("maximum_hp_percent", phase.MaximumHpPercent) })
                    child.Fields.Add(new(key, key, Value(value), Editable: false));
                return child;
            }).ToList();
            record.Children["actions"] = profile.Actions.Select(action =>
            {
                var canEdit = action.CanEdit && action.SelectorActionId is { } id && targets.ContainsKey(id);
                var target = action.SelectorActionId is { } selector ? targets.GetValueOrDefault(selector) : default;
                var child = new DumpRecord(action.Key, action.Name, "scriptedBosses", target.Table ?? profile.Key, target.Slot);
                if (action.MoveId is { } move)
                {
                    var choices = data.ScriptedBossMoveOptions.Where(option => option.Variant == action.Variant && action.SelectorActionId is { } actionId
                        && ZaScriptedBossActionCatalog.ResolveMoveCompatibility(option, actionId).State is "base-verified" or "gameplay-tested" or "experimental")
                        .Select(option => new DumpChoice(Value(option.MoveId), option.Name)).ToArray();
                    child.Fields.Add(new("move", action.SelectorActionId is { } actionId ? ZaScriptedBossActionCatalog.CreateEditField(actionId) : "move",
                        Value(move), Minimum: 0, Maximum: ushort.MaxValue, Choices: choices, Editable: canEdit));
                }
                child.Fields.Add(new("kind", "kind", action.Kind, "text", Editable: false));
                child.Fields.Add(new("runtime_state", "runtimeState", action.RuntimeState, "text", Editable: false));
                if (action.LockReason is { } reason) child.Fields.Add(new("lock_reason", "lockReason", reason, "text", Editable: false));
                return child;
            }).ToList();
            records.Add(record);
        }
    }
}
