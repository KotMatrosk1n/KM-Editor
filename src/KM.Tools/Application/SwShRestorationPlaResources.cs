// SPDX-License-Identifier: GPL-3.0-only
using KM.Formats.Models;

namespace KM.Tools.Application;

/// <summary>Resolves the selected PLA identity's declared motion and attachment resources.</summary>
internal sealed record SwShRestorationPlaResources(string BaseAnimation, string EffectLocators, string MotionStem, string Configuration)
{
    internal const string CatalogPath = "bin/pokemon/table/poke_resource_table.trpmcatalog";

    internal static SwShRestorationPlaResources Read(byte[] catalog, int species, int form, int gender, string expectedModel)
    {
        var parts = expectedModel.Split('/');
        if (parts.Length != 6 || parts[0] != "bin" || parts[1] != "pokemon" || parts[4] != "mdl"
            || !ValidStem(parts[3]) || parts[2] != parts[3][..6] || parts[5] != parts[3] + ".trmdl")
            throw new InvalidDataException("PLA restoration model path is unsupported.");
        var folder = string.Join('/', parts.Take(4)) + "/";
        var data = new ModelBuffer(catalog);
        var version = data.Table(data.Root, 0);
        if (version == 0 || data.Value(version, 0) != 5 || data.Value(version, 1) != 4)
            throw new InvalidDataException("PLA restoration catalog version is unsupported.");
        var selected = new List<int>();
        foreach (var row in data.Tables(data.Root, 1, 4096))
        {
            var identity = data.Table(row, 0);
            if (identity == 0) throw new InvalidDataException("PLA restoration catalog identity is missing.");
            int Number(int field, bool wide = true) => data.Field(identity, field) is var at && at != 0
                ? wide ? data.U16(at) : data.U8(at) : 0;
            if (Number(0) == species && Number(1) == form && Number(2, false) == gender) selected.Add(row);
        }
        if (selected.Count != 1 || data.Text(selected[0], 1) != expectedModel)
            throw new InvalidDataException("PLA restoration catalog model identity is missing or ambiguous.");

        string Declared(int field, string name, string directory, string suffix)
        {
            var entries = data.Tables(selected[0], field, 64).Where(item => data.Text(item, 0) == name).ToArray();
            var path = entries.Length == 1 ? data.Text(entries[0], 1) : null;
            var prefix = folder + directory + "/";
            if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidDataException("PLA restoration catalog resource is missing or ambiguous.");
            var resourceStem = path[prefix.Length..^suffix.Length];
            if (!ValidStem(resourceStem) || resourceStem[..6] != parts[2])
                throw new InvalidDataException("PLA restoration catalog resource requires another source adapter.");
            return path;
        }
        // Boss models deliberately use ordinary animation/locator stems inside
        // their own archive folder. The catalog owns those paths, not the model name.
        var motion = Declared(5, "base", "anm", "_base.tracn");
        var effect = Declared(6, "eff", "locators", "_eff.trskl");
        var motionStem = Path.GetFileName(motion)[..^"_base.tracn".Length];
        if (Path.GetFileName(effect) != motionStem + "_eff.trskl")
            throw new InvalidDataException("PLA restoration motion and attachment stems require another source adapter.");
        var configuration = data.Text(selected[0], 3);
        if (configuration != folder + motionStem + ".trpokecfg")
            throw new InvalidDataException("PLA restoration catalog configuration requires another source adapter.");
        return new(motion, effect, motionStem, configuration);
    }

    internal string MotionName(string path, string targetStem)
    {
        var folder = BaseAnimation[..(BaseAnimation.LastIndexOf('/') + 1)];
        if (!path.StartsWith(folder + MotionStem + "_", StringComparison.Ordinal) || !path.EndsWith(".tranm", StringComparison.Ordinal))
            throw new InvalidDataException("PLA restoration motion does not match its declared source.");
        var suffix = path[(folder.Length + MotionStem.Length)..^".tranm".Length];
        if (suffix.Length < 6 || suffix.AsSpan(1, 5).ContainsAnyExceptInRange('0', '9')
            || suffix.Length > 6 && suffix[6] != '_' || suffix.Contains('/') || suffix.Contains('\\'))
            throw new InvalidDataException("PLA restoration motion identity is unsupported.");
        return targetStem + suffix;
    }

    private static bool ValidStem(string stem) => stem.Length == 12 && stem.StartsWith("pm", StringComparison.Ordinal)
        && stem[6] == '_' && stem[9] == '_' && stem.AsSpan(2, 4).ContainsAnyExceptInRange('0', '9') == false
        && stem.AsSpan(7, 2).ContainsAnyExceptInRange('0', '9') == false && stem.AsSpan(10, 2).ContainsAnyExceptInRange('0', '9') == false;
}
