// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.Gifts;

public sealed partial class SwShGiftPokemonEditSessionService
{
    internal const string RestoreVanillaField = "restoreVanilla";

    public SwShGiftPokemonEditResult RestoreVanilla(ProjectPaths paths, EditSession? session, int giftIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        projectWorkspaceService.ClearMemoryCache();
        var project = projectWorkspaceService.Open(paths);
        var workflow = giftPokemonWorkflowService.Load(project);
        var original = RebindDeletedOutputEdits(project, workflow, session ?? StartSession());
        var diagnostics = new List<ValidationDiagnostic>();
        var gift = ResolveGift(workflow, giftIndex, diagnostics, RestoreVanillaField);
        if (CanEditGiftPokemon(project, workflow, diagnostics) && gift is not null)
        {
            if (gift.Vanilla is null)
                diagnostics.Add(CreateDiagnostic(DiagnosticSeverity.Error,
                    "Gift restoration requires one matching Base RomFS record.", field: RestoreVanillaField));
            else
            {
                var restore = new PendingEdit(SwShGiftPokemonWorkflowService.GiftPokemonEditDomain,
                    $"Restore {gift.Label} to vanilla.", CreateExpectedSources(project, gift, RestoreVanillaField),
                    RecordId: SwShGiftPokemonWorkflowService.CreateGiftRecordId(giftIndex, gift.SourceIdentity),
                    Field: RestoreVanillaField, NewValue: "vanilla");
                var candidate = original with { PendingEdits = original.PendingEdits.Where(edit =>
                    !IsGiftEdit(edit) || !SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var index)
                    || index != giftIndex).Append(restore).ToArray() };
                ValidateLoadedSession(project, workflow, candidate, diagnostics, addSuccessDiagnostic: false);
                if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
                    return new(OverlayPendingEdits(workflow, candidate.PendingEdits), candidate, diagnostics);
            }
        }
        return new(OverlayPendingEdits(workflow, original.PendingEdits), original, diagnostics);
    }

    private static byte[] WriteGiftEdits(OpenedProject project, IReadOnlyList<PendingEdit> edits,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var source = SwShGiftPokemonWorkflowService.ResolveGiftPokemonDataSource(project)
            ?? throw new InvalidDataException("Gift source is unavailable.");
        var bytes = File.ReadAllBytes(source.AbsolutePath);
        var archive = SwShGiftPokemonArchive.Parse(bytes);
        var ordinary = edits.Where(edit => edit.Field != RestoreVanillaField)
            .Select(edit => ToGiftEdit(archive, edit, diagnostics)).OfType<SwShGiftPokemonEdit>().ToArray();
        var restores = edits.Where(edit => edit.Field == RestoreVanillaField).Select(edit =>
        {
            if (edit.NewValue != "vanilla" || !SwShGiftPokemonWorkflowService.TryParseGiftRecordId(edit.RecordId, out var index, out var identity)
                || archive.Gifts.SingleOrDefault(gift => gift.Index == index) is not { } row
                || (identity is not null && identity != SwShGiftPokemonWorkflowService.CreateSourceIdentity(row)))
                throw new InvalidDataException("Gift restoration no longer matches the staged source record.");
            return index;
        }).ToArray();
        if (restores.Length > 0)
        {
            if (source.GraphEntry.BaseFile is null) throw new InvalidDataException("Base RomFS gift table is unavailable.");
            var vanilla = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(Path.Combine(project.Paths.BaseRomFsPath!,
                SwShGiftPokemonWorkflowService.GiftPokemonDataPath[6..])));
            bytes = archive.RestoreRecords(vanilla, restores);
        }
        return SwShGiftPokemonArchive.Parse(bytes).WriteEdits(ordinary);
    }
}
