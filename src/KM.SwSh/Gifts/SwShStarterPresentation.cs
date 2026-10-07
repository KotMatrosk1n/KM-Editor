// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.SwSh.Gifts;

internal sealed partial class SwShStarterPresentation(OpenedProject project)
{
    internal const string SourceInvalidCode = "KM-SWSH-GIFT-PRESENTATION-SOURCE-INVALID";
    internal const string TextPreservedCode = "KM-SWSH-GIFT-PRESENTATION-TEXT-PRESERVED";
    internal const string PlacementPath = "romfs/bin/archive/field/resident/placement.gfpak";
    internal static readonly ulong[] GiftHashes = [15057383039541199142UL, 16578209965379977056UL, 2926511046552020493UL];
    private static readonly ulong[][] Actors = [
        [0x94380622C10DE827, 0x304E963B9DA9CFF9, 0x63377A543E598274],
        [0x605DF2FBCB281B7F, 0x87AF6314F6CEA0E1, 0x2265B730A6B00C8C]];
    private readonly Dictionary<string, ProjectFileGraphEntry> graph = project.FileGraph.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, byte[]> Outputs { get; } = new(StringComparer.Ordinal);
    internal HashSet<ProjectFileReference> Sources { get; } = [];
    internal List<ValidationDiagnostic> Diagnostics { get; } = [];

    internal static bool IsStarter(SwShGiftPokemonRecord gift) => GiftHashes.Contains(gift.Hash1);

    internal void Build(byte[] finalGiftTable, IReadOnlySet<int>? slots = null)
    {
        var gifts = SwShGiftPokemonArchive.Parse(finalGiftTable).Gifts;
        var values = GiftHashes.Select(hash =>
        {
            var records = gifts.Where(g => g.Hash1 == hash).ToArray();
            if (records.Length != 1 || records[0].IsEgg != 0) throw new InvalidDataException("Each starter requires one non-egg gift record.");
            return new SwShStarterIdentity(records[0].Species, records[0].Form);
        }).ToArray();
        var currentGifts = SwShGiftPokemonArchive.Parse(Read("romfs/bin/script_event_data/add_poke.bin")).Gifts;
        var currentValues = GiftHashes.Select(hash => currentGifts.Single(g => g.Hash1 == hash))
            .Select(g => new SwShStarterIdentity(g.Species, g.Form)).ToArray();
        var previous = new SwShStarterIdentity[3][];
        for (var script = 0; script < 3; script++)
        {
            var path = $"romfs/bin/script/amx/{SwShStarterPresentationScript.Names[script]}.amx";
            var source = Read(path);
            previous[script] = SwShStarterPresentationScript.Read(source, script);
            var mapped = previous[script].ToArray();
            for (var slot = 0; slot < 3; slot++) if (slots is null || slots.Contains(slot)) mapped[slot] = values[slot];
            Outputs[path] = SwShStarterPresentationScript.Write(source, script, mapped);
        }
        VerifyModels(values.Where((_, slot) => slots is null || slots.Contains(slot)).ToArray());
        var pack = SwShGfPackFile.Parse(Read(PlacementPath));
        var members = new[] { "a_0101.bin", "a_t0101_i0101.bin" };
        for (var area = 0; area < 2; area++)
        {
            var member = pack.GetFileByName(members[area]);
            for (var slot = 0; slot < 3; slot++)
                if (slots is null || slots.Contains(slot))
                    member = SwShPlacementSpeciesWriter.Write(member, Actors[area][slot], (uint)values[slot].Species, (uint)values[slot].Form);
            pack.SetFileByName(members[area], member);
        }
        Outputs[PlacementPath] = pack.Write();
        foreach (var language in SwShGameTextLanguage.SupportedMessageLanguages)
        {
            var selection = SwShGameTextLanguage.ScriptMessagePath(language, "main_event_0080.dat");
            var mum = SwShGameTextLanguage.ScriptMessagePath(language, "main_event_0180.dat");
            if (!graph.ContainsKey(selection) && !graph.ContainsKey(mum)) continue;
            var names = SwShGameTextFile.Parse(Read(SwShGameTextLanguage.CommonMessagePath(language, "monsname.dat"))).Lines;
            // Other surviving scripts and the effective gift table identify our generated wording
            // even when a user deleted only one of the generated scene files.
            WriteText(selection, language, names, values, [.. previous, currentValues], false, slots);
            WriteText(mum, language, names, values, [.. previous, currentValues], true, slots);
        }
    }

    private void VerifyModels(SwShStarterIdentity[] values)
    {
        var catalog = new ModelBuffer(Read("romfs/bin/pokemon/table/poke_resource_table.gfbpmcatalog"));
        var rows = catalog.Tables(catalog.Root, 1, 8192);
        foreach (var value in values.Distinct())
        {
            var row = rows.FirstOrDefault(row =>
            {
                var id = catalog.Table(row, 0);
                int Number(int field) => catalog.Field(id, field) is var at && at != 0 ? catalog.U16(at) : 0;
                return id != 0 && Number(0) == value.Species && Number(1) == value.Form;
            });
            if (row == 0) throw new InvalidDataException("A selected starter form has no field model in the current model catalog.");
            var archive = catalog.Text(row, 3) ?? throw new InvalidDataException("Starter model archive is missing.");
            var pack = SwShGfPackFile.Parse(Read("romfs/" + archive));
            var model = catalog.Text(row, 1) ?? throw new InvalidDataException("Starter model resource is missing.");
            if (!HasResource(model, pack)) throw new InvalidDataException("The selected starter model resource is unavailable.");
            var reaction = false;
            var transitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in catalog.Tables(row, 4, 32))
            {
                var path = catalog.Text(table, 1);
                if (path is null) continue;
                var bytes = Resource(path, pack);
                var text = System.Text.Encoding.Latin1.GetString(bytes);
                foreach (var transition in new[] { "to_kw32_happyA01", "to_kw32_happyB01" })
                    if (text.Contains(transition, StringComparison.Ordinal)) transitions.Add(transition);
                var config = new ModelBuffer(bytes);
                var group = config.Table(config.Root, 6);
                if (group == 0) continue;
                foreach (var clip in config.Tables(group, 0, 2048))
                    if (config.Text(clip, 0) == "kw32_happyB01" && config.Text(clip, 1) is { } file && HasResource(file, pack)) reaction = true;
            }
            if (!reaction || transitions.Count != 2) throw new InvalidDataException("The selected starter form has no supported field reaction animation.");
        }
    }

    private bool HasResource(string path, SwShGfPackFile pack) => graph.ContainsKey("romfs/" + path)
        ? Read("romfs/" + path).Length > 0 : pack.ContainsFileName(Path.GetFileName(path));
    private byte[] Resource(string path, SwShGfPackFile pack) => graph.ContainsKey("romfs/" + path)
        ? Read("romfs/" + path) : pack.GetFileByName(Path.GetFileName(path));

    private void WriteText(string path, string language, IReadOnlyList<SwShGameTextLine> names,
        SwShStarterIdentity[] values, SwShStarterIdentity[][] previous, bool mum, IReadOnlySet<int>? slots = null)
    {
        if (!graph.ContainsKey(path)) return;
        var source = SwShGameTextFile.Parse(Read(path));
        var vanilla = SwShGameTextFile.Parse(Read(path, true));
        if (source.Lines.Count != vanilla.Lines.Count || source.Lines.Count < (mum ? 11 : 15))
            throw new InvalidDataException("Starter dialogue has an unsupported line layout.");
        var lines = source.Lines.ToArray();
        for (var slot = 0; slot < 3; slot++)
        {
            if (slots is not null && !slots.Contains(slot)) continue;
            var original = SwShStarterPresentationScript.Original[slot];
            var positions = mum ? new[] { slot == 0 ? 2 : slot - 1, slot == 0 ? 10 : slot + 7 }
                : new[] { slot == 0 ? 5 : slot + 2, slot == 0 ? 14 : slot + 11 };
            foreach (var index in positions)
            {
                string Name(int species) => species > 0 && species < names.Count ? names[species].Text
                    : throw new InvalidDataException("Starter species has no localized name.");
                string Generated(SwShStarterIdentity identity) => identity == original ? vanilla.Lines[index].Text
                    : SwShStarterPresentationText.Create(language, Name(identity.Species), mum, !mum && index >= 12, vanilla.Lines[index].Text);
                var wanted = Generated(values[slot]);
                var current = lines[index].Text;
                if (current == wanted) continue;
                if (current != vanilla.Lines[index].Text && !previous.Any(mapping => current == Generated(mapping[slot])))
                {
                    Diagnostics.Add(new(DiagnosticSeverity.Warning, "Custom starter dialogue was preserved. Review this line in Text if it names the previous Pokemon.", path,
                        SwShGiftPokemonWorkflowService.GiftPokemonEditDomain, $"line:{index}") { Code = TextPreservedCode });
                    continue;
                }
                lines[index] = lines[index] with { Text = wanted };
            }
        }
        Outputs[path] = source.WritePreserving(lines);
    }

    private byte[] Read(string path, bool vanilla = false)
    {
        if (!vanilla && Outputs.TryGetValue(path, out var output)) return output;
        if (!graph.TryGetValue(path, out var entry)) throw new InvalidDataException($"Starter presentation source is missing: {path}.");
        var reference = vanilla ? entry.BaseFile : entry.LayeredFile ?? entry.BaseFile;
        if (reference is null) throw new InvalidDataException($"Starter presentation base source is missing: {path}.");
        Sources.Add(reference);
        var root = reference.Layer == ProjectFileLayer.Base ? project.Paths.BaseRomFsPath : project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(root) || !path.StartsWith("romfs/", StringComparison.Ordinal)) throw new InvalidDataException("Starter source root is unavailable.");
        return File.ReadAllBytes(Path.Combine(root, reference.Layer == ProjectFileLayer.Base ? path[6..] : path));
    }
}
