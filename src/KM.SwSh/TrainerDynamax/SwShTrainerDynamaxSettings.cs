// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.TrainerDynamax;

// Each side follows global settings (0), normal game rules (1), or is disabled (2).
public sealed record SwShTrainerDynamaxOverride(int TrainerId, int Player, int Opponent);
public sealed record SwShTrainerDynamaxTrainer(int TrainerId, string Name);
public sealed record SwShTrainerDynamaxSettings(bool DisablePlayer, bool DisableOpponents,
    IReadOnlyList<SwShTrainerDynamaxOverride>? Trainers = null)
{
    internal int Mask => (DisablePlayer ? 1 : 0) | (DisableOpponents ? 2 : 0);
    internal SwShTrainerDynamaxSettings Canonical()
    {
        var rows = Trainers ?? [];
        if (rows.Count > 436 || rows.Any(row => row is null || row.TrainerId is < 1 or > 436
            || row.Player is < 0 or > 2 || row.Opponent is < 0 or > 2)
            || rows.Select(row => row.TrainerId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Trainer Dynamax requires unique trainer IDs and valid permission choices.");
        return this with { Trainers = rows.Where(row => row.Player != 0 || row.Opponent != 0).OrderBy(row => row.TrainerId).ToArray() };
    }
}
