// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.GameModules;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

public sealed record ZaPokemonAlphaSize(
    string Field, string ResourcePath, IReadOnlyList<int> Genders,
    float Scale, float MinimumScale, float VanillaScale,
    IReadOnlyList<int> SharedPersonalIds)
{
    internal double OrdinaryMidpoint { get; init; }
    public float OrdinaryScale => (float)OrdinaryMidpoint;
    internal IReadOnlyList<ProjectFileReference> EditSources { get; init; } = [];
}

internal static class ZaPokemonAlphaSizeService
{
    internal const string FieldPrefix = "alphaSize:";
    internal const string RecordPrefix = "alpha-size:";
    internal const string SourceUnavailable = "KM-ZA-POKEMON-SIZE-SOURCE-UNAVAILABLE";
    internal const string SelectionInvalid = "KM-ZA-POKEMON-SIZE-SELECTION-INVALID";
    internal const string BindingInvalid = "KM-ZA-POKEMON-SIZE-BINDING-INVALID";
    internal const string SessionConflict = "KM-ZA-POKEMON-SIZE-SESSION-CONFLICT";
    internal const string PlanStale = "KM-ZA-POKEMON-SIZE-PLAN-STALE";
    internal const string ApplyFailed = "KM-ZA-POKEMON-SIZE-APPLY-FAILED";

    internal static ZaPokemonRecord[] Project(OpenedProject project, ZaWorkflowFileSource files,
        ZaPokemonRecord[] pokemon, ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            using var reads = ZaWorkflowFileSource.BeginFreshReadScope(project.Paths);
            var catalogSource = files.Read(project, ZaDataPaths.PokemonResourceCatalog);
            var catalog = ZaPokemonResourceCatalogParser.Read(catalogSource.Bytes);
            var entries = catalog.Entries.Where(entry => entry.Species is > 0 and < 9999).ToArray();
            var bindings = pokemon.ToDictionary(row => row.PersonalId, row =>
            {
                var exact = entries.Where(entry => entry.Species == row.SpeciesId && entry.Form == row.Form).ToArray();
                return exact.Length > 0 ? exact : entries.Where(entry => entry.Species == row.SpeciesId && entry.Form == 0).ToArray();
            });
            var owners = bindings.SelectMany(pair => pair.Value.Select(entry => (Path: PathFor(entry), Id: pair.Key)))
                .GroupBy(pair => pair.Path).ToDictionary(group => group.Key, group => group.Select(pair => pair.Id).Distinct().ToArray());
            var values = new Dictionary<string, ZaPokemonAlphaSize>();
            var failed = new HashSet<string>();
            return pokemon.Select(row =>
            {
                var sizes = new List<ZaPokemonAlphaSize>();
                foreach (var group in bindings[row.PersonalId].GroupBy(PathFor))
                {
                    var path = group.Key;
                    if (failed.Contains(path)) continue;
                    if (!values.TryGetValue(path, out var value))
                    {
                        try
                        {
                            var ordinaryPath = path.Replace("_oybn.trpokecfg", ".trpokecfg", StringComparison.Ordinal);
                            var source = files.Read(project, path);
                            var ordinarySource = files.Read(project, ordinaryPath);
                            var vanillaSource = files.ReadBase(project, path);
                            var active = new ZaPokemonSizeDocument(source.Bytes);
                            var ordinary = new ZaPokemonSizeDocument(ordinarySource.Bytes);
                            var vanilla = new ZaPokemonSizeDocument(vanillaSource.Bytes);
                            if (vanilla.Minimum != vanilla.Maximum)
                                throw new InvalidDataException("The base alpha configuration does not use a fixed size.");
                            value = new ZaPokemonAlphaSize(FieldPrefix + path, path, [],
                                active.Maximum, active.Minimum, vanilla.Maximum, owners[path])
                            {
                                OrdinaryMidpoint = ordinary.Midpoint,
                                EditSources = [new(source.SourceLayer, source.RelativePath),
                                    new(ordinarySource.SourceLayer, ordinarySource.RelativePath),
                                    new(vanillaSource.SourceLayer, vanillaSource.RelativePath),
                                    new(catalogSource.SourceLayer, catalogSource.RelativePath)],
                            };
                            values[path] = value;
                        }
                        catch (Exception exception) when (IsSourceFailure(exception))
                        {
                            failed.Add(path);
                            diagnostics.Add(ZaWorkflowSupport.Warning("Alpha size configuration could not be verified.", $"romfs/{path}") with
                            { Code = SourceUnavailable, Domain = ZaEditSessionSupport.PokemonDomain, Field = FieldPrefix + path });
                            continue;
                        }
                    }
                    sizes.Add(value with { Genders = group.Select(entry => (int)entry.Gender).Distinct().ToArray() });
                }
                return row with { AlphaSizes = sizes };
            }).ToArray();
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(ZaWorkflowSupport.Warning("Alpha size catalog could not be verified.",
                $"romfs/{ZaDataPaths.PokemonResourceCatalog}") with
            { Code = SourceUnavailable, Domain = ZaEditSessionSupport.PokemonDomain, Field = "alphaSize" });
            return pokemon;
        }
    }

    private static bool IsSourceFailure(Exception exception) => exception is IOException or ArgumentException
        or InvalidOperationException or OverflowException or UnauthorizedAccessException;

    private static string PathFor(ZaPokemonResourceEntry entry)
    {
        var path = entry.ConfigurationPath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".trpokecfg", StringComparison.Ordinal)
            || path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(part => part is ".." or "." or "")
            || path.Contains(':'))
            throw new InvalidDataException("Pokemon size catalog has an invalid configuration path.");
        return "ik_pokemon/data/" + path[..^10] + "_oybn.trpokecfg";
    }

    internal static bool IsEdit(PendingEdit edit) => edit.Domain == ZaEditSessionSupport.PokemonDomain
        && (edit.Field?.StartsWith(FieldPrefix, StringComparison.Ordinal) == true
            || edit.RecordId?.StartsWith(RecordPrefix, StringComparison.Ordinal) == true);

    internal static bool TryScale(string? value, out float scale)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale)
            && ZaPokemonSizeDocument.Valid(scale);
    }

    internal static PendingEdit? Create(ZaPokemonRecord row, string field, string value,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var size = row.AlphaSizes?.FirstOrDefault(size => size.Field == field);
        if (size is null || !TryScale(value, out var scale))
        {
            diagnostics.Add(Error(size is null ? BindingInvalid : SelectionInvalid, field));
            return null;
        }
        return new PendingEdit(ZaEditSessionSupport.PokemonDomain, "Set Pokemon alpha size.",
            size.EditSources, RecordPrefix + size.ResourcePath, field, scale.ToString("R", CultureInfo.InvariantCulture));
    }

    internal static void Validate(ZaPokemonWorkflow workflow, PendingEdit edit, ICollection<ValidationDiagnostic> diagnostics)
    {
        var size = Find(workflow, edit);
        if (size is null || edit.RecordId != RecordPrefix + size.ResourcePath
            || !size.EditSources.Select(source => source.RelativePath).ToHashSet(StringComparer.Ordinal)
                .SetEquals(edit.Sources.Select(source => source.RelativePath)))
            diagnostics.Add(Error(BindingInvalid, edit.Field));
        else if (!float.TryParse(edit.NewValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            || !ZaPokemonSizeDocument.Valid(scale))
            diagnostics.Add(Error(SelectionInvalid, edit.Field));
    }

    internal static ZaPokemonAlphaSize? Find(ZaPokemonWorkflow workflow, PendingEdit edit) => workflow.Pokemon
        .SelectMany(row => row.AlphaSizes ?? []).FirstOrDefault(size => size.Field == edit.Field);

    internal static ZaPokemonWorkflow Overlay(ZaPokemonWorkflow workflow, PendingEdit edit)
    {
        if (!float.TryParse(edit.NewValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            || !ZaPokemonSizeDocument.Valid(scale)) return workflow;
        return workflow with { Pokemon = workflow.Pokemon.Select(row => row with
        {
            AlphaSizes = row.AlphaSizes?.Select(size => size.Field != edit.Field ? size : size with
            { Scale = scale, MinimumScale = scale }).ToArray(),
        }).ToArray() };
    }

    internal static ValidationDiagnostic Error(string code, string? field) => ZaEditSessionSupport.CreateDiagnostic(
        DiagnosticSeverity.Error, code switch
        {
            SelectionInvalid => "Alpha scale must be a positive finite number.",
            SessionConflict => "Apply Form or Pokédex placement changes before editing alpha size.",
            SourceUnavailable => "Alpha size configuration could not be verified.",
            ApplyFailed => "Alpha size output could not be written. Check Diagnostics and review again.",
            PlanStale => "Alpha size sources changed after Review. Review the changes again.",
            _ => "Alpha size sources or form bindings changed. Reload Pokemon Data and review again.",
        },
        ZaEditSessionSupport.PokemonDomain, field: field, code: code);
}
