// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Trainers;

internal sealed partial class SvTrainersEditSessionService
{
    private const string PartnerSourceCode = "KM-SV-TRAINER-PARTNER-SOURCE-INVALID";
    private const string PartnerIdentityCode = "KM-SV-TRAINER-PARTNER-IDENTITY-UNSUPPORTED";
    private const string PartnerTeraCode = "KM-SV-TRAINER-PARTNER-TERA-UNSUPPORTED";

    private static string? GetPartnerScene(string? label) => label switch
    {
        "pepper_nusi_01" => "common/RockClashEvent_/RockClashEvent",
        "pepper_nusi_02" => "hagane/HaganeRockClashEvent_/HaganeRockClashEvent",
        "pepper_nusi_03" => "hiko/HikoRockClashEvent_/HikoRockClashEvent",
        "pepper_nusi_04" => "jimen/JimenRockClashEvent_/JimenRockClashEvent",
        "pepper_nusi_05" => "dragon/DragonRockClashEvent_/DragonRockClashEvent",
        _ => null,
    };

    private static ValidationDiagnostic PartnerTeraDiagnostic() => new(
        DiagnosticSeverity.Error,
        "Terastallization is unavailable for Arven's Titan partner teams because it can stop the battle from progressing. Turn off Can Terastallize for this team.",
        File: $"romfs/{SvDataPaths.TrainerDataArray}",
        Domain: SvEditSessionSupport.TrainersDomain,
        Field: ChangeGemField,
        Expected: "Can Terastallize disabled") { Code = PartnerTeraCode };

    private static void ValidatePartnerTerastallization(
        SvTrainersWorkflow workflow,
        IEnumerable<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var trainerIds = edits.Where(edit => edit.Domain == SvEditSessionSupport.TrainersDomain)
            .Select(edit => TryParseTeamRecordId(edit.RecordId, out var trainerId, out _)
                ? trainerId : int.TryParse(edit.RecordId, NumberStyles.None, CultureInfo.InvariantCulture, out trainerId)
                    ? trainerId : -1).ToHashSet();
        if (workflow.Trainers.Any(trainer => trainerIds.Contains(trainer.TrainerId)
            && trainer.CanTerastallize && GetPartnerScene(trainer.Location) is not null))
            diagnostics.Add(PartnerTeraDiagnostic());
    }

    private IReadOnlyList<PartnerSceneWrite> PreparePartnerScenes(
        ProjectPaths paths,
        EditSession session,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var edits = session.PendingEdits.Where(edit =>
                edit.Domain == SvEditSessionSupport.TrainersDomain
                && edit.Field is SvTrainersWorkflowService.SpeciesIdField or SvTrainersWorkflowService.FormField
                && TryParseTeamRecordId(edit.RecordId, out _, out var slot) && slot == 0)
            .ToArray();
        if (edits.Length == 0)
        {
            return [];
        }

        var writes = new List<PartnerSceneWrite>();
        string? currentPath = SvDataPaths.TrainerDataArray;
        try
        {
            var project = projectWorkspaceService.Open(paths);
            var trainerSource = fileSource.Read(project, SvDataPaths.TrainerDataArray);
            var rows = ReadRows(trainerSource.Bytes);
            foreach (var group in edits.GroupBy(edit => edit.RecordId, StringComparer.Ordinal))
            {
                TryParseTeamRecordId(group.Key, out var trainerId, out _);
                var row = rows.ElementAtOrDefault(trainerId);
                // Titan battles reuse these separately created scene actors. Other multi-battle
                // partners create their Pokemon from trainer data and need no scene dependency.
                var scene = row is null ? null : GetPartnerScene(row.Trid);
                if (scene is null || row is null)
                {
                    continue;
                }

                if (rows.Count(candidate => candidate.Trid == row.Trid) != 1)
                {
                    throw new InvalidDataException("The Titan partner trainer identity is ambiguous.");
                }

                var lead = row.Pokemon.FirstOrDefault();
                var species = (int)(lead?.DevId ?? 0);
                var form = (int)(lead?.FormId ?? 0);
                foreach (var edit in group)
                {
                    var value = int.Parse(edit.NewValue!, CultureInfo.InvariantCulture);
                    if (edit.Field == SvTrainersWorkflowService.SpeciesIdField)
                    {
                        species = value;
                    }
                    else
                    {
                        form = value;
                    }
                }

                if (species <= 0 || species > ushort.MaxValue || form != 0 || lead?.FormId is not (null or 0))
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        DiagnosticSeverity.Error,
                        "Titan partner scene updates require an occupied lead Pokemon with form 0. Alternate forms are not supported yet.",
                        Domain: SvEditSessionSupport.TrainersDomain,
                        Field: SvTrainersWorkflowService.SpeciesIdField,
                        Expected: "Occupied lead Pokemon with form 0") { Code = PartnerIdentityCode });
                    continue;
                }

                foreach (var edition in new[] { 0, 1 })
                {
                    AddScene($"world/scene/parts/field/field_contents/nushi/{scene}_{edition}.trscn", "frend_partner");
                    if (row.Trid == "pepper_nusi_05")
                    {
                        AddScene($"world/scene/parts/event/event_scenario/main_scenario/nushi_dragon_020_/nushi_dragon_020_pre_start_{edition}.trsog", "sushi_battle_frend_partner");
                    }
                }

                void AddScene(string path, string actor)
                {
                    currentPath = path;
                    var source = fileSource.Read(project, path);
                    var bytes = SvTrainerPartnerSceneWriter.WriteSpecies(source.Bytes, actor, (ushort)species);
                    writes.Add(new PartnerSceneWrite(path, bytes,
                    [
                        new ProjectFileReference(source.SourceLayer, source.RelativePath),
                        new ProjectFileReference(trainerSource.SourceLayer, trainerSource.RelativePath),
                    ]));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or InvalidOperationException or ArgumentException or OverflowException)
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                "The Titan partner scene could not be synchronized. Check that the scene contains the expected partner actor and readable Pokemon components.",
                File: currentPath is null ? null : $"romfs/{currentPath}",
                Domain: SvEditSessionSupport.TrainersDomain,
                Expected: "Readable Titan partner scene with one matching actor") { Code = PartnerSourceCode });
        }

        return writes;
    }

    private sealed record PartnerSceneWrite(string VirtualPath, byte[] Bytes, IReadOnlyList<ProjectFileReference> Sources);
}
