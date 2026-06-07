// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Items;
using KM.Api.Editing;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.SwSh.Items;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;

namespace KM.Tools.Bridge;

public static class SwShBridgeMapper
{
    public static ListWorkflowsResponse ToDto(SwShWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadItemsWorkflowResponse ToDto(SwShItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static LoadTextWorkflowResponse ToDto(SwShTextWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTextWorkflowResponse(ToTextWorkflowDto(workflow));
    }

    public static LoadTrainersWorkflowResponse ToDto(SwShTrainersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTrainersWorkflowResponse(ToTrainersWorkflowDto(workflow));
    }

    public static UpdateItemFieldResponse ToDto(SwShItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(SwShEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(SwShItemsWorkflow workflow)
    {
        return new ItemsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Items.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ItemsWorkflowStatsDto(
                workflow.Stats.TotalItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TextWorkflowDto ToTextWorkflowDto(SwShTextWorkflow workflow)
    {
        return new TextWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Entries.Select(ToDto).ToArray(),
            workflow.DialogueReferences.Select(ToDto).ToArray(),
            new TextWorkflowStatsDto(
                workflow.Stats.TotalTextEntryCount,
                workflow.Stats.DialogueReferenceCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TrainersWorkflowDto ToTrainersWorkflowDto(SwShTrainersWorkflow workflow)
    {
        return new TrainersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trainers.Select(ToDto).ToArray(),
            new TrainersWorkflowStatsDto(
                workflow.Stats.TotalTrainerCount,
                workflow.Stats.TotalPokemonCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static WorkflowSummaryDto ToDto(SwShWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemRecordDto ToDto(SwShItemRecord item)
    {
        return new ItemRecordDto(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            new ItemProvenanceDto(
                item.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(item.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(item.Provenance.FileState)));
    }

    private static ItemEditableFieldDto ToDto(SwShItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue);
    }

    private static TextEntryRecordDto ToDto(SwShTextEntryRecord entry)
    {
        return new TextEntryRecordDto(
            entry.TextId,
            entry.Label,
            entry.Language,
            entry.Value,
            ToDto(entry.Provenance));
    }

    private static DialogueReferenceRecordDto ToDto(SwShDialogueReferenceRecord reference)
    {
        return new DialogueReferenceRecordDto(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            ToDto(reference.Provenance));
    }

    private static TextProvenanceDto ToDto(SwShTextProvenance provenance)
    {
        return new TextProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TrainerRecordDto ToDto(SwShTrainerRecord trainer)
    {
        return new TrainerRecordDto(
            trainer.TrainerId,
            trainer.Name,
            trainer.TrainerClass,
            trainer.Location,
            trainer.BattleType,
            trainer.Team.Select(ToDto).ToArray(),
            ToDto(trainer.Provenance));
    }

    private static TrainerPokemonRecordDto ToDto(SwShTrainerPokemonRecord pokemon)
    {
        return new TrainerPokemonRecordDto(
            pokemon.Slot,
            pokemon.Species,
            pokemon.Level,
            pokemon.HeldItem,
            pokemon.Moves);
    }

    private static TrainerProvenanceDto ToDto(SwShTrainerProvenance provenance)
    {
        return new TrainerProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static WorkflowAvailabilityDto ToDto(SwShWorkflowAvailability availability)
    {
        return availability switch
        {
            SwShWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            SwShWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            SwShWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
