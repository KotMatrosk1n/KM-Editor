// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.TrainerDynamax;

// Legacy inherit (0) and vanilla (1) retain their meaning. Explicit choices are disabled (2) and enabled (3).
public sealed record SwShTrainerDynamaxOverride(int TrainerId, int Player, int Opponent);
public sealed record SwShTrainerDynamaxTrainer(int TrainerId, string Name, bool? VanillaPlayer = null, bool? VanillaOpponent = null);
public sealed record SwShTrainerDynamaxSettings(bool DisablePlayer, bool DisableOpponents,
    IReadOnlyList<SwShTrainerDynamaxOverride>? Trainers = null, bool EnablePlayer = false, bool EnableOpponents = false)
{
    internal int Mask => (DisablePlayer ? 1 : 0) | (DisableOpponents ? 2 : 0) | (EnablePlayer ? 4 : 0) | (EnableOpponents ? 8 : 0);
    internal SwShTrainerDynamaxSettings Canonical()
    {
        var rows = Trainers ?? [];
        if (DisablePlayer && EnablePlayer || DisableOpponents && EnableOpponents || rows.Count > 436 || rows.Any(row => row is null || row.TrainerId is < 1 or > 436
            || row.Player is < 0 or > 3 || row.Opponent is < 0 or > 3)
            || rows.Select(row => row.TrainerId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Trainer Dynamax requires unique trainer IDs and valid permission choices.");
        return this with { Trainers = rows.Where(row => row.Player != 0 || row.Opponent != 0).OrderBy(row => row.TrainerId).ToArray() };
    }
}
