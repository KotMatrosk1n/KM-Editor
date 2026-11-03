// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.SV.Data;
using KM.SV.EvolutionItems;
using KM.SV.Workflows;

namespace KM.SV.Pokemon;

internal sealed partial class SvPokemonWorkflowService
{
    private SvPokemonRecord[] LoadMaterialDrops(OpenedProject project, SvPokemonRecord[] pokemon,
        SvTextLabelLookup labels, ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.MaterialDropArray);
            var table = new SvMaterialDropTable(source.Bytes);
            var provenance = new SvPokemonProvenance(source.RelativePath, source.SourceLayer, source.FileState);
            var drops = table.Rows.ToDictionary(row => row.SpeciesId, row =>
                new SvPokemonMaterialDrop(row.ItemId, labels.Item(row.ItemId), row.Rate, row.Count, provenance));
            return pokemon.Select(row => row with { MaterialDrop = drops.GetValueOrDefault(row.SpeciesId) }).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            // An unavailable optional table must not block ordinary personal edits.
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Pokemon material drops could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.MaterialDropArray}", MaterialDropCountField,
                "Readable species material drop table"));
            return pokemon;
        }
    }
}

internal sealed partial class SvPokemonEditSessionService
{
    private static bool IsMaterialDropEdit(PendingEdit edit) =>
        IsMaterialDropField(edit.Field);

    private static bool IsMaterialDropField(string? field) => field is
        SvPokemonWorkflowService.MaterialDropItemField or SvPokemonWorkflowService.MaterialDropRateField
        or SvPokemonWorkflowService.MaterialDropCountField;

    private static int MaterialDropValue(SvPokemonMaterialDrop drop, string? field) => field switch
    {
        SvPokemonWorkflowService.MaterialDropItemField => drop.ItemId,
        SvPokemonWorkflowService.MaterialDropRateField => drop.Rate,
        _ => drop.Count,
    };

    private static PendingEdit? CreateMaterialDropEdit(SvPokemonWorkflow workflow, SvPokemonRecord pokemon,
        string field, string value, ICollection<ValidationDiagnostic> diagnostics)
    {
        var drop = pokemon.MaterialDrop;
        if (drop is null)
        {
            diagnostics.Add(SvEditSessionSupport.CreateDiagnostic(DiagnosticSeverity.Error,
                "This Pokemon has no editable material drop row.", SvEditSessionSupport.PokemonDomain,
                file: $"romfs/{SvDataPaths.MaterialDropArray}", field: field,
                expected: "Existing species material drop row"));
            return null;
        }
        var definition = workflow.EditableFields.Single(candidate => candidate.Field == field);
        var parsed = SvEditSessionSupport.TryParseInt(value, definition.MinimumValue, definition.MaximumValue,
            field, SvEditSessionSupport.PokemonDomain, diagnostics);
        if (parsed is null) return null;
        // The source is keyed by species, so all forms stage against one stable target.
        var target = workflow.Pokemon.First(row => row.SpeciesId == pokemon.SpeciesId);
        return new PendingEdit(SvEditSessionSupport.PokemonDomain,
            $"Set {pokemon.Name} {definition.Label.ToLowerInvariant()} to {parsed.Value}.",
            [new ProjectFileReference(drop.Provenance.SourceLayer, drop.Provenance.SourceFile),
             new ProjectFileReference(pokemon.Provenance.SourceLayer, pokemon.Provenance.SourceFile)],
            target.PersonalId.ToString(CultureInfo.InvariantCulture), field,
            parsed.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static SvPokemonWorkflow OverlayMaterialDrop(SvPokemonWorkflow workflow, int personalId, PendingEdit edit)
    {
        var target = workflow.Pokemon.FirstOrDefault(row => row.PersonalId == personalId);
        if (target?.MaterialDrop is null || !int.TryParse(edit.NewValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return workflow;
        var definition = workflow.EditableFields.FirstOrDefault(field => field.Field == edit.Field);
        if (definition is null || value < definition.MinimumValue || value > definition.MaximumValue) return workflow;
        var itemLabel = workflow.EditableFields.First(field => field.Field == SvPokemonWorkflowService.MaterialDropItemField)
            .Options.FirstOrDefault(option => option.Value == value)?.Label ?? SvLabels.Item(value);
        var itemPrefix = value.ToString(CultureInfo.InvariantCulture) + " ";
        if (itemLabel.StartsWith(itemPrefix, StringComparison.Ordinal)) itemLabel = itemLabel[itemPrefix.Length..];
        return workflow with
        {
            Pokemon = workflow.Pokemon.Select(row => row.SpeciesId == target.SpeciesId && row.MaterialDrop is { } drop
                ? row with { MaterialDrop = edit.Field switch
                {
                    SvPokemonWorkflowService.MaterialDropItemField => drop with { ItemId = value, Item = itemLabel },
                    SvPokemonWorkflowService.MaterialDropRateField => drop with { Rate = value },
                    _ => drop with { Count = value },
                } } : row).ToArray(),
        };
    }

    private static ChangePlan CreatePokemonDataChangePlan(ProjectPaths paths, EditSession session,
        IReadOnlyList<ValidationDiagnostic> diagnostics, SvOutputMode outputMode)
    {
        var groups = session.PendingEdits.GroupBy(edit => IsMaterialDropEdit(edit)
            ? SvDataPaths.MaterialDropArray : SvDataPaths.PersonalArray).ToArray();
        if (groups.Length == 0)
            return SvEditSessionSupport.CreateSingleFileChangePlan(paths, session, SvEditSessionSupport.PokemonDomain,
                SvDataPaths.PersonalArray, "Pokemon Data", diagnostics, outputMode);
        var plans = groups.Select(group => SvEditSessionSupport.CreateSingleFileChangePlan(paths,
            session with { PendingEdits = group.ToArray() }, SvEditSessionSupport.PokemonDomain,
            group.Key, "Pokemon Data", diagnostics, outputMode)).ToArray();
        var writes = plans.SelectMany(plan => plan.Writes).DistinctBy(write => write.TargetRelativePath).ToArray();
        return new ChangePlan(session.Id, writes, plans.SelectMany(plan => plan.Diagnostics)
            .Where(diagnostic => !diagnostic.Message.StartsWith("Change plan preview contains ", StringComparison.Ordinal))
            .Distinct().Append(SvEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Info, $"Change plan preview contains {writes.Length} target files.",
            SvEditSessionSupport.PokemonDomain)).ToArray());
    }

    private byte[] PrepareMaterialDropBytes(OpenedProject project, IReadOnlyList<PendingEdit> edits)
    {
        var workflow = pokemonWorkflowService.Load(project);
        var table = new SvMaterialDropTable(fileSource.Read(project, SvDataPaths.MaterialDropArray).Bytes);
        foreach (var edit in edits)
        {
            var target = workflow.Pokemon.Single(row => row.PersonalId.ToString(CultureInfo.InvariantCulture) == edit.RecordId);
            var value = int.Parse(edit.NewValue!, CultureInfo.InvariantCulture);
            table.SetValues(target.SpeciesId,
                itemId: edit.Field == SvPokemonWorkflowService.MaterialDropItemField ? value : null,
                rate: edit.Field == SvPokemonWorkflowService.MaterialDropRateField ? value : null,
                count: edit.Field == SvPokemonWorkflowService.MaterialDropCountField ? value : null);
        }
        return table.ToArray();
    }

    private void WritePersonalPokemonData(ProjectPaths paths, OpenedProject project, EditSession session,
        SvOutputMode outputMode, ICollection<ProjectFileReference> writtenFiles, ICollection<ValidationDiagnostic> diagnostics)
    {
        var rows = ReadRows(fileSource.Read(project, SvDataPaths.PersonalArray).Bytes);
        var conversionState = SvEvolutionItemConversionState.Load(project, fileSource);
        PrepareEvolutionItemConversions(rows, session.PendingEdits, conversionState);
        var baseRows = NeedsBaseRows(session.PendingEdits)
            ? ReadRows(fileSource.ReadBase(project, SvDataPaths.PersonalArray).Bytes) : rows;
        var removals = new List<(PendingEdit Edit, string GroupId, int Slot)>();
        foreach (var edit in session.PendingEdits)
        {
            if (IsIndexedCompatibilityRemoval(edit, out var groupId, out var slot)) removals.Add((edit, groupId, slot));
            else ApplyEdit(rows, baseRows, edit, conversionState, diagnostics);
        }
        foreach (var group in removals.GroupBy(candidate => (candidate.Edit.RecordId, candidate.GroupId)))
            foreach (var removal in group.OrderByDescending(candidate => candidate.Slot))
                ApplyEdit(rows, baseRows, removal.Edit, conversionState, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return;
        var personalBytes = WriteRows(rows);
        if (conversionState.Modified)
        {
            SvWorkflowFileSource.Write(paths, SvDataPaths.EvolutionItemConversionArray, conversionState.Write(), outputMode);
            writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.EvolutionItemConversionArray, outputMode));
        }
        SvWorkflowFileSource.Write(paths, SvDataPaths.PersonalArray, personalBytes, outputMode);
        writtenFiles.Add(SvEditSessionSupport.GeneratedReference(SvDataPaths.PersonalArray, outputMode));
    }
}
