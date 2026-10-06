// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.FairyGymBoosts;
using KM.SwSh.MarnieBoosts;

namespace KM.SwSh.FpsPatch;

/// <summary>Separates verified quiz outcomes from timing ownership in shared battle sequences.</summary>
internal static class SwShFpsBattleBoostComposition
{
    private static readonly string[] FairyPaths =
    [
        SwShFairyGymBoostsWorkflowService.AnnetteSequencePath,
        SwShFairyGymBoostsWorkflowService.TeresaSequencePath,
        SwShFairyGymBoostsWorkflowService.TheodoraSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalNicknameSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalColorSequencePath,
        SwShFairyGymBoostsWorkflowService.OpalAgeSequencePath,
    ];

    public static bool ContainsPath(string path) =>
        FairyPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
        || SwShMarnieBoostsPatcher.Paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    public static bool TryRestoreOutcomes(string path, byte[] vanilla, byte[] source, out byte[] normalized)
    {
        normalized = source;
        var fairyPath = FairyPaths.FirstOrDefault(candidate => candidate.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (fairyPath is null)
            return SwShMarnieBoostsPatcher.TryRestoreOutcomes(path, vanilla, source, out normalized);
        try
        {
            SwShFairyGymBoostsBseqPatcher.ValidateVanillaBase(vanilla,
                SwShFairyGymBoostsWorkflowService.GetVanillaSlots(fairyPath));
            SwShFairyGymBoostsBseqPatcher.ValidateEffective(source);
            SwShFairyGymBoostsBseqPatcher.EnsureCompatible(vanilla, source);
            normalized = source.ToArray();
            vanilla.AsSpan(SwShFairyGymBoostsBseqPatcher.PayloadOffset, SwShFairyGymBoostsBseqPatcher.OwnedByteCount)
                .CopyTo(normalized.AsSpan(SwShFairyGymBoostsBseqPatcher.PayloadOffset));
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    public static byte[] PreserveOutcomes(string path, byte[] desired, byte[] current)
    {
        if (!FairyPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            return SwShMarnieBoostsPatcher.PreserveOutcomes(desired, current);
        var output = desired.ToArray();
        current.AsSpan(SwShFairyGymBoostsBseqPatcher.PayloadOffset, SwShFairyGymBoostsBseqPatcher.OwnedByteCount)
            .CopyTo(output.AsSpan(SwShFairyGymBoostsBseqPatcher.PayloadOffset));
        return output;
    }
}
