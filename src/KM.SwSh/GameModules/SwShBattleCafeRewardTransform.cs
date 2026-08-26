// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.SwSh.Scripts;

namespace KM.SwSh.GameModules;

public sealed record SwShBattleCafeRewardEdit(
    int RowIndex,
    int ExpectedItemId,
    int ExpectedDwightPercent,
    int ExpectedBernardPercent,
    int ExpectedRichardPercent,
    int ItemId,
    int DwightPercent,
    int BernardPercent,
    int RichardPercent);

public sealed record SwShBattleCafeRewardTransformResult(
    byte[] Bytes,
    string SourceSha256,
    string OutputSha256,
    int ChangedRowCount);

public sealed record SwShBattleCafeRewardApplyPlan(
    SwShBattleCafeRewardTransformResult Transform,
    OutputApplyPlan ApplyPlan);

public static class SwShBattleCafeRewardTransform
{
    public const string OutputMode = "sword-shield-battle-cafe";

    public static SwShBattleCafeRewardTransformResult Apply(
        ReadOnlySpan<byte> source,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyList<SwShBattleCafeRewardEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(itemNames);
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count > SwShBattleCafeRewardSourceReader.TableRowCount
            || edits.Any(edit => edit is null)
            || edits.Select(edit => edit.RowIndex).Distinct().Count() != edits.Count)
        {
            throw new ArgumentException(
                "Battle Cafe reward edits must use distinct bounded physical row identities.",
                nameof(edits));
        }

        var sourceBytes = source.ToArray();
        var current = SwShBattleCafeRewardSourceReader.Parse(sourceBytes, itemNames).Rewards;
        ValidateTable(current, itemNames);
        var desired = current.ToArray();
        foreach (var edit in edits)
        {
            if (edit.RowIndex is < 1 or > SwShBattleCafeRewardSourceReader.TableRowCount)
            {
                throw new InvalidDataException("A Battle Cafe reward edit targets an unknown physical row.");
            }

            var index = edit.RowIndex - 1;
            var row = current[index];
            if (row.RowIndex != edit.RowIndex
                || row.ItemId != edit.ExpectedItemId
                || row.DwightPercent != edit.ExpectedDwightPercent
                || row.BernardPercent != edit.ExpectedBernardPercent
                || row.RichardPercent != edit.ExpectedRichardPercent)
            {
                throw new InvalidDataException(
                    "A Battle Cafe reward row no longer matches its reviewed values.");
            }

            if (!itemNames.TryGetValue(edit.ItemId, out var itemName)
                || string.IsNullOrWhiteSpace(itemName))
            {
                throw new InvalidDataException(
                    "A Battle Cafe reward edit references an item outside the loaded item catalog.");
            }

            desired[index] = new SwShBattleCafeRewardEntry(
                edit.RowIndex,
                edit.ItemId,
                itemName,
                edit.DwightPercent,
                edit.BernardPercent,
                edit.RichardPercent);
        }

        ValidateTable(desired, itemNames);
        var patches = new List<SwShAmxDataCellPatch>(
            edits.Count * SwShBattleCafeRewardSourceReader.TableColumnCount);
        foreach (var edit in edits.OrderBy(edit => edit.RowIndex))
        {
            var currentRow = current[edit.RowIndex - 1];
            var desiredRow = desired[edit.RowIndex - 1];
            var firstCell = checked(
                SwShBattleCafeRewardSourceReader.TableFirstRowCell
                + (edit.RowIndex - 1) * SwShBattleCafeRewardSourceReader.TableColumnCount);
            AddPatchIfChanged(patches, firstCell, currentRow.ItemId, desiredRow.ItemId);
            AddPatchIfChanged(
                patches,
                firstCell + 1,
                currentRow.DwightPercent,
                desiredRow.DwightPercent);
            AddPatchIfChanged(
                patches,
                firstCell + 2,
                currentRow.BernardPercent,
                desiredRow.BernardPercent);
            AddPatchIfChanged(
                patches,
                firstCell + 3,
                currentRow.RichardPercent,
                desiredRow.RichardPercent);
        }

        var output = patches.Count == 0
            ? sourceBytes
            : SwShAmxCellPatcher.ApplyDataCellPatches(sourceBytes, patches);
        var reparsed = SwShBattleCafeRewardSourceReader.Parse(output, itemNames).Rewards;
        if (!reparsed.Select(ToNumericState).SequenceEqual(desired.Select(ToNumericState)))
        {
            throw new InvalidDataException(
                "The rebuilt Battle Cafe reward table did not retain the reviewed result.");
        }

        return new SwShBattleCafeRewardTransformResult(
            output,
            Convert.ToHexString(SHA256.HashData(sourceBytes)),
            Convert.ToHexString(SHA256.HashData(output)),
            desired.Zip(current).Count(pair => ToNumericState(pair.First) != ToNumericState(pair.Second)));
    }

    public static SwShBattleCafeRewardApplyPlan CreateApplyPlan(
        ReadOnlySpan<byte> source,
        IReadOnlyDictionary<int, string> itemNames,
        IReadOnlyList<SwShBattleCafeRewardEdit> edits,
        ProjectId projectId,
        OutputFileState reviewedTargetState)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargetState);
        var sourceState = ComputeState(source);
        if (reviewedTargetState.Exists && reviewedTargetState != sourceState)
        {
            throw new InvalidDataException(
                "The layered Battle Cafe target no longer matches the reviewed source bytes.");
        }

        var transform = Apply(source, itemNames, edits);
        if (transform.ChangedRowCount == 0)
        {
            throw new InvalidOperationException(
                "A Battle Cafe output plan requires at least one effective reviewed row change.");
        }

        var path = new RelativeOutputPath(SwShBattleCafeRewardSourceReader.SourceRelativePath);
        var claim = new OwnedTarget(
            GameFamily.SwordShield,
            new OwnedTargetAddress(path),
            new OwnershipOwnerId("sword-shield-battle-cafe"),
            new PreservationRuleDescriptor(
                "verified-amx-reward-table",
                schemaVersion: 1,
                preservesUnownedData: true,
                requiresPreimage: true));
        var mutation = OutputMutation.Write(
            path,
            transform.Bytes,
            reviewedTargetState,
            [claim]);
        var applyPlan = new OutputApplyPlan(
            projectId,
            GameFamily.SwordShield,
            OutputMode,
            OutputReviewFingerprint.FromMutations([mutation]),
            [new OutputApplyOrigin(
                OutputApplyOriginKind.Workflow,
                "sword-shield-battle-cafe-reward-edit")],
            [mutation]);
        return new SwShBattleCafeRewardApplyPlan(transform, applyPlan);
    }

    private static void ValidateTable(
        IReadOnlyList<SwShBattleCafeRewardEntry> rewards,
        IReadOnlyDictionary<int, string> itemNames)
    {
        if (rewards.Count != SwShBattleCafeRewardSourceReader.TableRowCount
            || !rewards.Select(reward => reward.RowIndex)
                .SequenceEqual(Enumerable.Range(1, SwShBattleCafeRewardSourceReader.TableRowCount))
            || rewards.Select(reward => reward.ItemId).Distinct().Count() != rewards.Count
            || rewards.Any(reward =>
                reward.ItemId is < 1 or > ushort.MaxValue
                || !itemNames.TryGetValue(reward.ItemId, out var itemName)
                || string.IsNullOrWhiteSpace(itemName)
                || reward.DwightPercent is < 0 or > 100
                || reward.BernardPercent is < 0 or > 100
                || reward.RichardPercent is < 0 or > 100)
            || rewards.Sum(reward => reward.DwightPercent) != 100
            || rewards.Sum(reward => reward.BernardPercent) != 100
            || rewards.Sum(reward => reward.RichardPercent) != 100)
        {
            throw new InvalidDataException(
                "The Battle Cafe reward table is outside its verified shape or probability totals.");
        }
    }

    private static void AddPatchIfChanged(
        ICollection<SwShAmxDataCellPatch> patches,
        int cell,
        int current,
        int desired)
    {
        if (current != desired)
        {
            patches.Add(new SwShAmxDataCellPatch(cell, desired));
        }
    }

    private static OutputFileState ComputeState(ReadOnlySpan<byte> bytes)
    {
        return OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.Length);
    }

    private static BattleCafeNumericState ToNumericState(SwShBattleCafeRewardEntry reward)
    {
        return new BattleCafeNumericState(
            reward.RowIndex,
            reward.ItemId,
            reward.DwightPercent,
            reward.BernardPercent,
            reward.RichardPercent);
    }

    private readonly record struct BattleCafeNumericState(
        int RowIndex,
        int ItemId,
        int DwightPercent,
        int BernardPercent,
        int RichardPercent);
}
