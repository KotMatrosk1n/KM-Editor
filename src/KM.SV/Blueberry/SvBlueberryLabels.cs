// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV.Blueberry;
using KM.Formats.SwSh;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Blueberry;

internal static class SvBlueberryLabels
{
    internal static IReadOnlyDictionary<string, string> Load(OpenedProject project, SvWorkflowFileSource files,
        SvBlueberryKind kind, IReadOnlyList<SvBlueberryRow> rows, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (kind == SvBlueberryKind.Snacksworth)
        {
            var names = SvTextLabelLookup.LoadPokemonNames(project, files, diagnostics, project.Paths);
            return rows.ToDictionary(row => row.Id, row => names.Pokemon(row.Species));
        }
        var keys = rows.ToDictionary(row => row.Id, row => row.LabelKey);
        if (kind == SvBlueberryKind.BbqRewards)
            return rows.ToDictionary(row => row.Id, row => $"Quest {row.Id}");
        var language = SvGameTextLanguage.Resolve(project.Paths);
        var text = ReadMessages(project, files, language, "clubroom_pc");
        return keys.ToDictionary(pair => pair.Key, pair => text.GetValueOrDefault(pair.Value, pair.Value));
    }

    private static IReadOnlyDictionary<string, string> ReadMessages(OpenedProject project, SvWorkflowFileSource files, string language, string name)
    {
        var path = $"message/dat/{language}/common/{name}";
        var data = SwShGameTextFile.Parse(files.Read(project, path + ".dat").Bytes);
        var keys = SwShAhtbFile.Parse(files.Read(project, path + ".tbl").Bytes);
        if (keys.Entries.Count < data.Lines.Count) throw new InvalidDataException("Message keys do not match their values.");
        return data.Lines.Select((line, index) => (key: keys.Entries[index].Name, line.Text)).ToDictionary(pair => pair.key, pair => pair.Text);
    }
}
