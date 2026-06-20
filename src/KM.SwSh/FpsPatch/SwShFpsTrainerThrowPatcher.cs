// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsTrainerThrowAnimationInfo(
    uint KeyFrames,
    uint FrameRate);

internal static class SwShFpsTrainerThrowPatcher
{
    internal const string TrainerBallthrowCameraRootRelativePath = "romfs/bin/battle/waza/camera/ballthrow";
    internal const string BattleModelAnimationRootRelativePath = "romfs/bin/battle/waza/model/anm";
    internal const string LegacyTrainerBattleArchiveRootRelativePath = "romfs/bin/archive/chara/data/tr/anm";
    internal const string LegacyCharaTrainerRootRelativePath = "romfs/bin/chara/data/tr";

    public static bool IsLegacyBallThrowTimingPath(string normalizedRelativePath)
    {
        var fileName = Path.GetFileName(normalizedRelativePath);
        if (string.IsNullOrWhiteSpace(fileName)
            || (!fileName.EndsWith(".gfbanm", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".gfbcama", StringComparison.OrdinalIgnoreCase))
            || !fileName.Contains("ballthrow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedRelativePath.StartsWith(TrainerBallthrowCameraRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.StartsWith("pc", StringComparison.OrdinalIgnoreCase);
        }

        if (normalizedRelativePath.StartsWith(BattleModelAnimationRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.Contains("_tr", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    internal static bool IsLegacyTrainerCharacterAnimationPath(string normalizedRelativePath)
    {
        var fileName = Path.GetFileName(normalizedRelativePath);
        return normalizedRelativePath.StartsWith(LegacyCharaTrainerRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".gfbanm", StringComparison.OrdinalIgnoreCase)
            && fileName.Contains("ballthrow", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsLegacyTrainerBattleArchivePath(string normalizedRelativePath)
    {
        var fileName = Path.GetFileName(normalizedRelativePath);
        return normalizedRelativePath.StartsWith(LegacyTrainerBattleArchiveRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            && fileName.Contains("_battle", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".gfpak", StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] ConvertAnimationToHalfSpeed(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            var patched = source.ToArray();
            var frameRate = SwShFpsFlatBufferAnimation.GetFrameRate(patched);
            if (frameRate <= 1)
            {
                throw new InvalidDataException("60FPS Patch could not find a usable trainer ball throw FrameRate field.");
            }

            SwShFpsFlatBufferAnimation.SetFrameRate(patched, Math.Max(1u, frameRate / 2));
            return patched;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid trainer ball throw GF animation.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid trainer ball throw GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid trainer ball throw GF animation FlatBuffer.", exception);
        }
    }

    internal static SwShFpsTrainerThrowAnimationInfo InspectAnimation(byte[] source)
    {
        return new SwShFpsTrainerThrowAnimationInfo(
            SwShFpsFlatBufferAnimation.GetKeyFrames(source),
            SwShFpsFlatBufferAnimation.GetFrameRate(source));
    }
}
