// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.Formats.Pokemon;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class SwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal (IReadOnlyList<SwShRestorationContribution> Contributions, int[] ExcludedMoves) Gameplay(SwShPokemonIdentity identity)
        {
            var species = identity.Species; var profile = Profile(identity);
            var personalId = SwShRestorationForms.PersonalRow(identity);
            var source = new ModelBuffer(Donor("avalon/data/personal_array.bin"));
            var candidates = source.Tables(source.Root, 0, 4096).Where(t => source.Field(t, 0) is var at && at != 0
                && source.U16(at + 4) == species && source.U16(at + 2) == identity.Form).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException("Source Pokemon gameplay identity is missing or ambiguous.");
            var table = candidates[0]; var detail = source.Field(table, 0);
            int NationalSpecies(int developmentSpecies, int form)
            {
                var matches = source.Tables(source.Root, 0, 4096).Where(t => source.Field(t, 0) is var at && at != 0
                    && source.U16(at) == developmentSpecies && source.U16(at + 2) == form)
                    .Select(t => (int)source.U16(source.Field(t, 0) + 4)).Distinct().ToArray();
                return matches.Length == 1 && SwShRestorationSpecies.IsSpecies(matches[0]) ? matches[0]
                    : throw new InvalidDataException("The source development species has no unambiguous National identity.");
            }
            if (profile.SourceSpecies is { } sourceId && source.U16(detail) != sourceId)
                throw new InvalidDataException("The source development species identity changed.");
            int Scalar(int field, bool wide = false) => source.Field(table, field) is var at && at != 0 ? wide ? source.U16(at) : source.U8(at) : 0;
            int[] Moves(int field)
            {
                var (start, count) = source.Vector(table, field, 2, 1024);
                return Enumerable.Range(0, count).Select(i => (int)source.U16(start + i * 2)).ToArray();
            }
            var nativePersonal = Native(SwShRestorationComposer.PersonalPath[6..]);
            if (species > SwShEffectivePokemonCatalog.MaximumSpecies) SwShRestorationSpecies.ValidateReservation(nativePersonal, nativePersonal);
            var personalTable = SwShPersonalTable.Parse(nativePersonal, 4096);
            if (identity.Form != 0) SwShRestorationFormSlots.Validate(nativePersonal, nativePersonal);
            if (personalId < personalTable.Records.Count && personalTable.Records[personalId].IsPresentInGame)
                throw new InvalidDataException("Restoration target is already native or has no personal slot.");
            var before = SwShRestorationSpecies.NativeRecord(nativePersonal, personalId, SwShPersonalTable.RecordSize);
            var row = before?.ToArray() ?? new byte[176];
            var stats = source.Field(table, 22); var yields = source.Field(table, 21); int[] order = [0, 1, 2, 5, 3, 4];
            for (var i = 0; i < order.Length; i++) row[i] = source.U8(stats + order[i]);
            row[6] = checked((byte)Scalar(5)); row[7] = checked((byte)Scalar(6)); row[8] = checked((byte)Scalar(11)); row[9] = checked((byte)Scalar(19));
            var ev = 0;
            for (var i = 0; i < order.Length; i++) { var value = source.U8(yields + order[i]); if (value > 3) throw new InvalidDataException("Source EV yield is unsupported."); ev |= value << (i * 2); }
            var gender = source.Field(table, 12); var hatch = source.Field(table, 15);
            if (gender == 0 || source.U8(gender) != profile.GenderKind || source.U8(gender + 1) != profile.FemalePercent
                || hatch == 0 || (source.U16(hatch) == 0 && source.U16(hatch + 2) == 0 ? 0 : NationalSpecies(source.U16(hatch), source.U16(hatch + 2))) != profile.HatchedSpecies
                || source.U16(hatch + 2) != profile.HatchedForm)
                throw new InvalidDataException("Source gender or hatch identity needs another conversion profile.");
            Put16(row, 10, ev); row.AsSpan(12, 6).Clear(); row[18] = profile.GenderThreshold; row[19] = checked((byte)Scalar(16)); row[20] = checked((byte)Scalar(17));
            row[21] = checked((byte)Scalar(10)); row[22] = checked((byte)Scalar(13)); row[23] = checked((byte)Scalar(14));
            var abilities = Enumerable.Range(0, 3).Select(i => Scalar(7 + i, true)).ToArray();
            var fallbackAbility = abilities.FirstOrDefault(ability => ability is >= 1 and <= 267);
            if (fallbackAbility == 0) throw new InvalidDataException("This Pokemon has no ability supported by Sword and Shield in its source data.");
            for (var i = 0; i < abilities.Length; i++)
                Put16(row, 24 + i * 2, abilities[i] is >= 1 and <= 267 ? abilities[i] : fallbackAbility);
            Put16(row, 30, 0); row[32] = 1; row[33] = (byte)((source.U8(detail + 6) & 63) | 64);
            Put16(row, 34, profile.Experience); Put16(row, 36, source.U16(detail + 8)); Put16(row, 38, source.U16(detail + 10));
            var excluded = new HashSet<int>();
            bool MoveAvailable(int move)
            {
                var path = $"bin/pml/waza/waza{move:0000}.wazabin";
                if (move is < 1 or > 826 || !System.IO.File.Exists(Path.Combine(target.BaseRomFsPath!, path))
                    || !SwShMoveDataFile.Parse(Native(path)).Record.CanUseMove)
                { excluded.Add(move); return false; }
                return true;
            }
            var machineMoves = Moves(24).Where(MoveAvailable).ToHashSet();
            void Compatibility(int offset, int length, IEnumerable<int> moves)
            {
                row.AsSpan(offset, length).Clear(); var index = 0;
                foreach (var move in moves) { if (machineMoves.Contains(move)) row[offset + index / 8] |= (byte)(1 << (index % 8)); index++; }
                if (index > length * 8) throw new InvalidDataException("Compatibility field exceeds native storage.");
            }
            Compatibility(40, 16, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId));
            Compatibility(56, 4, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TypeTutorCompatibilityGroupId));
            Compatibility(60, 16, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.TechnicalRecordCompatibilityGroupId));
            Compatibility(168, 4, SwShPokemonWorkflowService.GetDefaultCompatibilityMoveIds(SwShPokemonWorkflowService.ArmorTutorCompatibilityGroupId));
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(76), checked((uint)SwShRestorationSpecies.IconSpecies(species))); Put16(row, 86, profile.HatchedSpecies); row.AsSpan(88, 8).Clear(); row.AsSpan(172, 4).Clear();
            if (identity.Form != 0 || profile.HatchedForm != 0) { Put16(row, 90, 1); Put16(row, 94, profile.HatchedForm); }
            _ = SwShPersonalTable.Parse(row);
            var result = new List<SwShRestorationContribution>
            { new(SwShRestorationComposer.PersonalPath, SwShRestorationWriter.Personal, personalId.ToString(CultureInfo.InvariantCulture), before, row) };
            var learned = Moves(26).Where(MoveAvailable).Select(m => (Move: m, Level: 1)).ToList();
            var (learnStart, learnCount) = source.Vector(table, 27, 4, 1024);
            for (var i = 0; i < learnCount; i++)
            {
                var move = source.U16(learnStart + i * 4); var level = Math.Max(0, (int)unchecked((sbyte)source.U8(learnStart + i * 4 + 2)));
                if (MoveAvailable(move) && !learned.Contains((move, level))) learned.Add((move, level));
            }
            if (learned.Count >= 65) throw new InvalidDataException("Converted learnset exceeds native storage.");
            var learnset = Enumerable.Repeat((byte)255, 260).ToArray();
            for (var i = 0; i < learned.Count; i++) { Put16(learnset, i * 4, learned[i].Move); Put16(learnset, i * 4 + 2, learned[i].Level); }
            var learnNative = Native(SwShRestorationComposer.LearnsetPath[6..]);
            if (learnNative.Length / 260 != personalTable.Records.Count || learnNative.Length % 260 != 0) throw new InvalidDataException("Target learnset and personal identities disagree.");
            if (species == SwShRestorationSpecies.Annihilape && learnNative.AsSpan(personalId * 260, 260).ContainsAnyExcept((byte)255))
                throw new InvalidDataException("The reserved new species learnset row is occupied in Base RomFS.");
            result.Add(new(SwShRestorationComposer.LearnsetPath, SwShRestorationWriter.Learnset, personalId.ToString(CultureInfo.InvariantCulture), SwShRestorationSpecies.NativeRecord(learnNative, personalId, 260), learnset));
            var (evolutionStart, evolutionCount) = source.Vector(table, 23, 16, 9); var evolution = new byte[72];
            for (var i = 0; i < evolutionCount; i++)
            {
                var at = evolutionStart + i * 16; var form = source.U16(at + 14);
                var destination = NationalSpecies(source.U16(at + 12), form);
                if (species == 203 && destination == 981 && form == 0 && source.U16(at + 2) == 21 && source.U16(at + 4) == 888) continue;
                if (!TryProfile(new(destination, form), out var evolved) || evolved.HatchedSpecies != profile.HatchedSpecies || evolved.HatchedForm != profile.HatchedForm) throw new InvalidDataException("Evolution needs an undeclared restoration dependency.");
                var original = new SwShEvolutionRecord(i, source.U16(at + 2), source.U16(at + 4), destination, form, source.U16(at));
                var converted = SwShRestorationEvolutionPolicy.Convert(identity, original);
                var method = converted.Method; var argument = converted.Argument; var level = converted.Level;
                if (method is < 1 or > 49 or 35 || method == 21 && !MoveAvailable(argument))
                    throw new InvalidDataException("Evolution condition needs an explicit supported replacement.");
                if (method == 22) argument = NationalSpecies(argument, 0);
                if (method is 6 or 8 or 17 or 18 or 19 or 20 or 42)
                {
                    if (converted != original)
                    {
                        if (!SwShItemTable.Parse(Native(SwShItemTable.ItemDataRelativePath[6..]), 4096).Records.Any(r => r.ItemId == argument))
                            throw new InvalidDataException("Replacement evolution item is unavailable in the target.");
                    }
                    else
                    {
                    var mapping = EvolutionItemConversionTable.Read(Donor("world/data/battle/plib_item_conversion/plib_item_conversion_array.bin"))
                        .Where(r => r.ParameterId == argument && r.ItemId > 0).ToArray();
                    if (mapping.Length != 1 || !SwShItemTable.Parse(Native(SwShItemTable.ItemDataRelativePath[6..]), 4096).Records.Any(r => r.ItemId == mapping[0].ItemId))
                        throw new InvalidDataException("Evolution item has no supported native identity.");
                    argument = mapping[0].ItemId;
                    }
                }
                Put16(evolution, i * 8, method); Put16(evolution, i * 8 + 2, argument); Put16(evolution, i * 8 + 4, destination);
                evolution[i * 8 + 6] = checked((byte)form); evolution[i * 8 + 7] = checked((byte)level);
            }
            result.Add(File($"bin/pml/evolution/evo_{personalId:000}.bin", evolution));
            // Native parents keep every native route. Only the two explicitly
            // requested regional branches add a new route to their table.
            if (SwShRestorationEvolutionPolicy.NativeParent(identity) is { } parentSpecies)
            {
                var parentTables = source.Tables(source.Root, 0, 4096).Where(t => source.Field(t, 0) is var at && at != 0
                    && source.U16(at + 4) == parentSpecies && source.U16(at + 2) == 0).ToArray();
                if (parentTables.Length != 1) throw new InvalidDataException("Regional evolution parent is ambiguous.");
                var parentPath = $"bin/pml/evolution/evo_{parentSpecies:000}.bin";
                var nativeRows = SwShEvolutionSet.Parse(Native(parentPath)).Evolutions.ToList();
                if (nativeRows.Any(r => r.Species == species && r.Form == identity.Form))
                    throw new InvalidDataException("Regional evolution destination is already native.");
                var freeSlot = Enumerable.Range(0, SwShEvolutionSet.MaxEvolutionCount)
                    .FirstOrDefault(slot => nativeRows.All(r => r.Slot != slot), -1);
                if (freeSlot < 0) throw new InvalidDataException("The native parent's evolution table is full.");
                var route = SwShRestorationEvolutionPolicy.Convert(new(parentSpecies, 0),
                    new(freeSlot, 0, 0, species, identity.Form, 0));
                if (route.Method != 8 || route.Argument != SwShRestorationEvolutionPolicy.DuskStone
                    || !SwShItemTable.Parse(Native(SwShItemTable.ItemDataRelativePath[6..]), 4096).Records.Any(r => r.ItemId == route.Argument))
                    throw new InvalidDataException("Regional evolution replacement has no native item.");
                nativeRows.Add(route);
                result.Add(File(parentPath, SwShEvolutionSet.Write(nativeRows)));
                nativeParentEvolutionPaths.Add("romfs/" + parentPath);
            }
            var eggMoves = Moves(25).Where(MoveAvailable).ToArray(); var eggs = new byte[4 + eggMoves.Length * 2]; Put16(eggs, 0, identity.Form == 0 && species <= 898
                ? BinaryPrimitives.ReadUInt16LittleEndian(Native(SwShRestorationForms.EggPath(species)[6..]))
                : SwShRestorationForms.EggRow(identity)); Put16(eggs, 2, eggMoves.Length);
            for (var i = 0; i < eggMoves.Length; i++) Put16(eggs, 4 + i * 2, eggMoves[i]);
            result.Add(File($"bin/pml/tamagowaza/tamagowaza_{SwShRestorationForms.EggRow(identity):0000}.bin", eggs));
            foreach (var iconGender in SwShRestorationComposer.IconGenders(species))
            {
                var icon = new byte[8]; Put16(icon, 0, SwShRestorationSpecies.IconSpecies(species)); Put16(icon, 2, identity.Form); Put16(icon, 4, iconGender);
                result.Add(new(SwShRestorationComposer.IconsPath, SwShRestorationWriter.Icons, SwShRestorationComposer.IconKey(species, identity.Form, iconGender), null, icon));
            }
            var camp = SwShGfPackFile.Parse(Native(SwShRestorationComposer.CampPath[6..]));
            foreach (var group in new[] { "common", "motion", "face", "ball", "turning" })
            {
                if (group == "common" && species <= SwShEffectivePokemonCatalog.MaximumSpecies && personalTable.Records[species].IsPresentInGame) continue;
                string Name(int id, int form = 0) => group == "common" ? $"poke_{id:0000}_XX_X.bin" : $"poke_{group}_{id:0000}_{form:00}_0.bin";
                var folder = $"bin/pokemon_data/pokecamp/{group}/";
                // The native legendary templates have no ball table. Their new
                // forms retain their other metadata and use the standard ball row.
                var campTemplate = species is 483 or 484 && identity.Form == 1 && group == "ball" ? 1 : profile.NativeTemplate;
                var data = camp.ReadFileByHash(SwShGfPackFile.HashMemberPath(folder + Name(campTemplate)), 1024 * 1024);
                if (group == "common")
                {
                    var metadata = new ModelBuffer(data); var field = metadata.Field(metadata.Root, 2);
                    if (metadata.U32(field) != profile.NativeTemplate) throw new InvalidDataException("Native camp donor identity is invalid.");
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field), checked((uint)species));
                }
                result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp, folder + Name(species, identity.Form), null, data));
                if (group != "common") foreach (var variant in SwShRestorationDefinitions.Get(definitionId).AdditionalModelVariants.Where(v => v.Identity == identity))
                    result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp,
                        folder + $"poke_{group}_{species:0000}_{identity.Form:00}_{variant.Gender}.bin", null, data));
            }
            return (result, excluded.Order().ToArray());
        }
        private static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)value));
    }
}
