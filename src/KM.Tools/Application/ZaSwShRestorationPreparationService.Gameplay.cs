// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Restoration;

namespace KM.Tools.Application;

public sealed partial class ZaSwShRestorationPreparationService
{
    private sealed partial class Preparation
    {
        internal (IReadOnlyList<SwShRestorationContribution> Contributions, int[] ExcludedMoves) Gameplay(SwShPokemonIdentity identity)
        {
            if (Profile(identity).PlaAssets) return PlaGameplay(identity);
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
                    : throw new InvalidDataException("The Z-A development species has no unambiguous National identity.");
            }
            if (profile.SourceSpecies is { } sourceId && source.U16(detail) != sourceId)
                throw new InvalidDataException("The Z-A development species identity changed.");
            int Scalar(int field, bool wide = false) => source.Field(table, field) is var at && at != 0 ? wide ? source.U16(at) : source.U8(at) : 0;
            int[] Moves(int field)
            {
                var (start, count) = source.Vector(table, field, 2, 1024);
                return Enumerable.Range(0, count).Select(i => (int)source.U16(start + i * 2)).ToArray();
            }
            var nativePersonal = Native(SwShRestorationComposer.PersonalPath[6..]);
            var personalTable = SwShPersonalTable.Parse(nativePersonal, 4096);
            if (species > SwShEffectivePokemonCatalog.MaximumSpecies) SwShRestorationSpecies.ValidateReservation(nativePersonal, nativePersonal);
            if (identity.Form != 0) SwShRestorationFormSlots.Validate(nativePersonal, nativePersonal);
            if (personalId < personalTable.Records.Count && personalTable.Records[personalId].IsPresentInGame)
                throw new InvalidDataException("Restoration target is already native or has no personal slot.");
            var before = SwShRestorationSpecies.NativeRecord(nativePersonal, personalId, SwShPersonalTable.RecordSize);
            var row = before?.ToArray() ?? new byte[176];
            var stats = source.Field(table, 20); var yields = source.Field(table, 19); int[] order = [0, 1, 2, 5, 3, 4];
            for (var i = 0; i < order.Length; i++) row[i] = source.U8(stats + order[i]);
            row[6] = checked((byte)Scalar(3)); row[7] = checked((byte)Scalar(4)); row[8] = checked((byte)Scalar(9)); row[9] = checked((byte)Scalar(17));
            var ev = 0;
            for (var i = 0; i < order.Length; i++) { var value = source.U8(yields + order[i]); if (value > 3) throw new InvalidDataException("Source EV yield is unsupported."); ev |= value << (i * 2); }
            Put16(row, 10, ev); row.AsSpan(12, 6).Clear(); var gender = source.Field(table, 10);
            var hatch = source.Field(table, 13);
            if (gender == 0 || source.U8(gender) != profile.GenderKind || source.U8(gender + 1) != profile.FemalePercent
                || hatch == 0 || (source.U16(hatch) == 0 && source.U16(hatch + 2) == 0 ? 0
                    : NationalSpecies(source.U16(hatch), source.U16(hatch + 2))) != profile.HatchedSpecies
                || source.U16(hatch + 2) != profile.HatchedForm)
                throw new InvalidDataException("Z-A gender or hatch identity needs another target adapter.");
            row[18] = profile.GenderThreshold; row[19] = checked((byte)Scalar(14)); row[20] = checked((byte)Scalar(15));
            row[21] = checked((byte)Scalar(8)); row[22] = checked((byte)Scalar(11)); row[23] = checked((byte)Scalar(12));
            var abilities = Enumerable.Range(0, 3).Select(i => Scalar(5 + i, true)).ToArray();
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
            var machineMoves = Moves(22).Where(MoveAvailable).ToHashSet();
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
            // Alternate Mega identities are battle forms, not regional forms. The
            // regional bit also controls the editor's base-learnset eligibility.
            // Keep the authored hatch identity (including Original Color) separate.
            if (identity.Form != 0 || profile.HatchedForm != 0)
            {
                row[90] = SwShRestorationForms.IsMega(identity) ? (byte)0 : (byte)1;
                row[94] = checked((byte)profile.HatchedForm);
            }
            _ = SwShPersonalTable.Parse(row);
            var result = new List<SwShRestorationContribution>
            { new(SwShRestorationComposer.PersonalPath, SwShRestorationWriter.Personal, personalId.ToString(CultureInfo.InvariantCulture), before, row) };
            var learned = Moves(24).Where(MoveAvailable).Select(m => (Move: m, Level: 1)).ToList();
            var (learnStart, learnCount) = source.Vector(table, 25, 4, 1024);
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
            result.Add(new(SwShRestorationComposer.LearnsetPath, SwShRestorationWriter.Learnset, personalId.ToString(CultureInfo.InvariantCulture), SwShRestorationSpecies.NativeRecord(learnNative, personalId, 260), learnset));
            var (evolutionStart, evolutionCount) = source.Vector(table, 21, 16, 9); var evolution = new byte[72];
            for (var i = 0; i < evolutionCount; i++)
            {
                var at = evolutionStart + i * 16; var form = source.U16(at + 14);
                var destination = NationalSpecies(source.U16(at + 12), form);
                if (species == 57 && destination == 979 && form == 0 && source.U16(at + 2) == 56 && source.U16(at + 4) == 20)
                {
                    // Primeape uses the native Dusk Stone evolution condition.
                    Put16(evolution, i * 8, 8); Put16(evolution, i * 8 + 2, 108);
                    Put16(evolution, i * 8 + 4, 979);
                    continue;
                }
                if (!TryProfile(new(destination, form), out var evolutionProfile)
                    || (evolutionProfile.EvolutionFamilySpecies ?? evolutionProfile.HatchedSpecies)
                        != (profile.EvolutionFamilySpecies ?? profile.HatchedSpecies)
                    || evolutionProfile.HatchedForm != profile.HatchedForm)
                    throw new InvalidDataException("Evolution needs an undeclared restoration dependency.");
                var stone = (species, destination, source.U16(at + 4)) switch
                {
                    (603, 604, 4) => 83, // Thunder Stone
                    (70, 71, 6) => 85, // Leaf Stone
                    (670, 671, 7) => 107, // Shiny Stone
                    (739, 740, 52) => 849, // Ice Stone
                    _ => 0,
                };
                if (source.U16(at + 2) == 8 && stone != 0 && source.U16(at) == 0)
                {
                    Put16(evolution, i * 8, 8); Put16(evolution, i * 8 + 2, stone);
                    Put16(evolution, i * 8 + 4, destination); evolution[i * 8 + 6] = checked((byte)form);
                    continue;
                }
                if (species == 433 && destination == 358 && source.U16(at + 2) == 3
                    && source.U16(at + 4) == 0 && source.U16(at) == 0)
                {
                    Put16(evolution, i * 8, 3); // Native friendship at night.
                    Put16(evolution, i * 8 + 4, destination); evolution[i * 8 + 6] = checked((byte)form);
                    continue;
                }
                if (species == 603 && destination == 604 && source.U16(at + 2) == 8 && source.U16(at + 4) == 4)
                {
                    // Z-A's Thunder Stone parameter 4 maps to native item 83.
                    Put16(evolution, i * 8, 8); Put16(evolution, i * 8 + 2, 83);
                    Put16(evolution, i * 8 + 4, destination);
                    continue;
                }
                if (species == 739 && destination == 740 && source.U16(at + 2) == 39
                    && source.U16(at + 4) == 0 && source.U16(at) == 0)
                {
                    // SwSh has no source summit location. Prefer the source's
                    // working Ice Stone branch and omit the unusable location
                    // branch. A donor without that item route uses level 30.
                    var hasIceStone = Enumerable.Range(0, evolutionCount).Any(index =>
                    {
                        var route = evolutionStart + index * 16;
                        return source.U16(route) == 0 && source.U16(route + 2) == 8
                            && source.U16(route + 4) == 52 && source.U16(route + 12) == source.U16(at + 12)
                            && source.U16(route + 14) == form;
                    });
                    if (!hasIceStone)
                    {
                        Put16(evolution, i * 8, 4); Put16(evolution, i * 8 + 4, destination);
                        evolution[i * 8 + 6] = checked((byte)form); evolution[i * 8 + 7] = 30;
                    }
                    continue;
                }
                if (source.U16(at + 2) != 4 || source.U16(at + 4) != 0 || source.U16(at) is < 1 or > 100)
                    throw new InvalidDataException("Z-A evolution condition needs another target adapter.");
                Put16(evolution, i * 8, source.U16(at + 2)); Put16(evolution, i * 8 + 2, source.U16(at + 4)); Put16(evolution, i * 8 + 4, destination);
                evolution[i * 8 + 6] = checked((byte)form); evolution[i * 8 + 7] = checked((byte)source.U16(at));
            }
            result.Add(File($"bin/pml/evolution/evo_{personalId:000}.bin", evolution));
            var eggMoves = Moves(23).Where(MoveAvailable).ToArray(); var eggs = new byte[4 + eggMoves.Length * 2]; Put16(eggs, 0, identity.Form == 0 && species <= 898
                ? BinaryPrimitives.ReadUInt16LittleEndian(Native(SwShRestorationForms.EggPath(species)[6..]))
                : SwShRestorationForms.EggRow(identity)); Put16(eggs, 2, eggMoves.Length);
            for (var i = 0; i < eggMoves.Length; i++) Put16(eggs, 4 + i * 2, eggMoves[i]);
            result.Add(File($"bin/pml/tamagowaza/tamagowaza_{SwShRestorationForms.EggRow(identity):0000}.bin", eggs));
            foreach (var iconGender in SwShRestorationComposer.IconGenders(species))
            {
                var icon = new byte[8]; Put16(icon, 0, SwShRestorationSpecies.IconSpecies(species)); Put16(icon, 2, identity.Form); Put16(icon, 4, iconGender);
                result.Add(new(SwShRestorationComposer.IconsPath, SwShRestorationWriter.Icons,
                    SwShRestorationComposer.IconKey(species, identity.Form, iconGender), null, icon));
            }
            var camp = SwShGfPackFile.Parse(Native(SwShRestorationComposer.CampPath[6..]));
            foreach (var group in new[] { "common", "motion", "face", "ball", "turning" })
            {
                // Native species already own their shared camp metadata.
                if (group == "common" && species <= SwShEffectivePokemonCatalog.MaximumSpecies && personalTable.Records[species].IsPresentInGame) continue;
                string Name(int id, int form = 0) => group == "common" ? $"poke_{id:0000}_XX_X.bin" : $"poke_{group}_{id:0000}_{form:00}_0.bin";
                var folder = $"bin/pokemon_data/pokecamp/{group}/";
                var data = camp.ReadFileByHash(SwShGfPackFile.HashMemberPath(folder + Name(profile.NativeTemplate)), 1024 * 1024);
                if (group == "common")
                {
                    var metadata = new ModelBuffer(data); var field = metadata.Field(metadata.Root, 2);
                    if (metadata.U32(field) != profile.NativeTemplate) throw new InvalidDataException("Native camp donor identity is invalid.");
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field), checked((uint)species));
                    if (profile.MealVerticalOffset is { } offset) SetMealVerticalOffset(data, offset);
                }
                result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp, folder + Name(species, identity.Form), null, data));
                if (profile.SeparateFemale && group != "common")
                    result.Add(new(SwShRestorationComposer.CampPath, SwShRestorationWriter.Camp,
                        folder + $"poke_{group}_{species:0000}_{identity.Form:00}_1.bin", null, data));
            }
            return (result, excluded.Order().ToArray());
        }
        private static void Put16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)value));
    }
}
