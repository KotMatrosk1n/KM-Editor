// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Gifts;

internal sealed partial class SwShStarterPresentation
{
    internal const string PlacementPreservedCode = "KM-SWSH-GIFT-PRESENTATION-PLACEMENT-PRESERVED";

    internal void Restore(IReadOnlySet<int> slots)
    {
        var originals = SwShStarterPresentationScript.Original;
        var gifts = SwShGiftPokemonArchive.Parse(Read(SwShGiftPokemonWorkflowService.GiftPokemonDataPath)).Gifts;
        var current = GiftHashes.Select(hash => gifts.Single(gift => gift.Hash1 == hash))
            .Select(gift => new SwShStarterIdentity(gift.Species, gift.Form)).ToArray();
        var previous = new SwShStarterIdentity[3][];
        var scripts = SwShStarterPresentationScript.Names.Select(name => $"romfs/bin/script/amx/{name}.amx").ToArray();
        for (var script = 0; script < scripts.Length; script++)
            previous[script] = graph.ContainsKey(scripts[script])
                ? SwShStarterPresentationScript.Read(Read(scripts[script]), script) : originals.ToArray();
        var owned = slots.Where(slot => previous.Any(mapping => mapping[slot] != originals[slot])).ToHashSet();
        var textSources = new List<(string Path, string Language, IReadOnlyList<SwShGameTextLine> Names, bool Mum)>();
        foreach (var language in SwShGameTextLanguage.SupportedMessageLanguages)
            foreach (var mum in new[] { false, true })
            {
                var path = SwShGameTextLanguage.ScriptMessagePath(language, mum ? "main_event_0180.dat" : "main_event_0080.dat");
                if (!graph.ContainsKey(path)) continue;
                var names = SwShGameTextFile.Parse(Read(SwShGameTextLanguage.CommonMessagePath(language, "monsname.dat"))).Lines;
                textSources.Add((path, language, names, mum));
                var lines = SwShGameTextFile.Parse(Read(path)).Lines;
                var vanilla = SwShGameTextFile.Parse(Read(path, true)).Lines;
                if (lines.Count != vanilla.Count || lines.Count < (mum ? 11 : 15))
                    throw new InvalidDataException("Starter dialogue has an unsupported line layout.");
                foreach (var slot in slots)
                {
                    if (current[slot] == originals[slot] || current[slot].Species <= 0 || current[slot].Species >= names.Count) continue;
                    var indexes = mum ? new[] { slot == 0 ? 2 : slot - 1, slot == 0 ? 10 : slot + 7 }
                        : new[] { slot == 0 ? 5 : slot + 2, slot == 0 ? 14 : slot + 11 };
                    if (indexes.Any(index => lines[index].Text != vanilla[index].Text
                        && lines[index].Text == SwShStarterPresentationText.Create(language, names[current[slot].Species].Text,
                            mum, !mum && index >= 12, vanilla[index].Text))) owned.Add(slot);
                }
            }
        // A gift table edited without companion output does not acquire scene ownership on restore.
        if (owned.Count == 0) return;
        for (var script = 0; script < scripts.Length; script++)
        {
            if (!graph.ContainsKey(scripts[script])) continue;
            var mapping = previous[script].ToArray();
            foreach (var slot in owned) mapping[slot] = originals[slot];
            var source = Read(scripts[script]);
            var output = SwShStarterPresentationScript.Write(source, script, mapping);
            if (!source.AsSpan().SequenceEqual(output)) Outputs[scripts[script]] = output;
        }
        if (graph.ContainsKey(PlacementPath))
        {
            var source = Read(PlacementPath);
            var pack = SwShGfPackFile.Parse(source);
            var changed = false;
            var members = new[] { "a_0101.bin", "a_t0101_i0101.bin" };
            for (var area = 0; area < members.Length; area++)
            {
                var member = pack.GetFileByName(members[area]);
                var before = member;
                foreach (var slot in owned)
                {
                    var actor = SwShPlacementZoneArchive.Parse(member).Zones.SelectMany(zone => zone.RawObjects)
                        .Single(row => row.ObjectType == "Critter" && row.Fields.Any(field =>
                            field.Field == "raw.Critter.Field_01.Field_00.Field_00.HashObjectName"
                            && field.Value.Equals($"0x{Actors[area][slot]:X16}", StringComparison.OrdinalIgnoreCase)));
                    int Value(string field) => int.Parse(actor.Fields.Single(f => f.Field == field).Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    var identity = new SwShStarterIdentity(Value("raw.Critter.Species"), Value("raw.Critter.Form"));
                    if (identity == originals[slot]) continue;
                    if (identity != current[slot] && !previous.Any(mapping => mapping[slot] == identity))
                    {
                        Diagnostics.Add(new(DiagnosticSeverity.Warning,
                            "A separately edited starter placement was preserved. Review its species and form in Placement.", PlacementPath,
                            SwShGiftPokemonWorkflowService.GiftPokemonEditDomain) { Code = PlacementPreservedCode });
                        continue;
                    }
                    member = SwShPlacementSpeciesWriter.Write(member, Actors[area][slot], (uint)originals[slot].Species, (uint)originals[slot].Form);
                }
                if (!before.AsSpan().SequenceEqual(member)) { pack.SetFileByName(members[area], member); changed = true; }
            }
            if (changed) Outputs[PlacementPath] = pack.Write();
        }
        foreach (var (path, language, names, mum) in textSources)
        {
            var alreadyPlanned = Outputs.ContainsKey(path);
            var source = Read(path);
            WriteText(path, language, names, originals, [.. previous, current], mum, owned);
            if (!alreadyPlanned && Outputs[path].AsSpan().SequenceEqual(source)) Outputs.Remove(path);
        }
    }
}
