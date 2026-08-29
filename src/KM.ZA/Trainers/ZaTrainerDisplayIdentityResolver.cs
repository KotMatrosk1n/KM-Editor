// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Formats.ZA.Generated.GameData;
using KM.ZA.Data;

namespace KM.ZA.Trainers;

internal static class ZaTrainerDisplayIdentityResolver
{
    public static ZaTrainerDisplayIdentity Resolve(
        int trainerId,
        ZaTrainerRow trainer,
        ZaTextLabelLookup labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var (classId, className) = labels.TrainerTypeByHash(
            trainer.TrainerType,
            trainer.TrainerType2);
        var isHyperspaceTrainer = ZaTrainerNameCatalog.IsHyperspaceTrainer(trainer.TrainerId);
        var trainerName = isHyperspaceTrainer
            ? ZaLabels.FormatTrainerIdForLookup(trainer.TrainerId!, className)
            : labels.TrainerNameFromText(trainer.TrainerId, trainerId)
                ?? labels.TrainerNameFromKeys(
                    ZaTrainerNameCatalog.ResolveTrainerNameKeys(trainer.TrainerId))
                ?? ZaLabels.FormatTrainerIdForLookup(
                    trainer.TrainerId
                        ?? $"Trainer {trainerId.ToString(CultureInfo.InvariantCulture)}",
                    className);
        trainerName = ZaTextLabelLookup.NormalizeTrainerName(trainerName, className);
        if (isHyperspaceTrainer
            && labels.HyperspaceTrainerClassFromText(trainer.TrainerId) is { } trainerArchetype)
        {
            className = trainerArchetype;
        }

        return new ZaTrainerDisplayIdentity(
            trainerName,
            classId,
            className,
            isHyperspaceTrainer);
    }
}

internal sealed record ZaTrainerDisplayIdentity(
    string Name,
    int ClassId,
    string ClassName,
    bool IsHyperspaceTrainer);
