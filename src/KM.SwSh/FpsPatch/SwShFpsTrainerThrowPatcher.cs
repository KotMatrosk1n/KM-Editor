// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Text;

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsTrainerThrowAnimationInfo(
    uint KeyFrames,
    uint FrameRate);

internal static class SwShFpsTrainerThrowPatcher
{
    internal const string TrainerBattleArchiveRootRelativePath = "romfs/bin/archive/chara/data/tr/anm";
    internal const string TrainerBallthrowCameraRootRelativePath = "romfs/bin/battle/waza/camera/ballthrow";
    internal const string BattleModelAnimationRootRelativePath = "romfs/bin/battle/waza/model/anm";
    internal const string CharaTrainerRootRelativePath = "romfs/bin/chara/data/tr";

    private const int MaxCandidateBattleAnimationId = 1500;
    private const int MaxExtractedFileNameLength = 256;

    private static readonly byte[] GfbanmSuffix = Encoding.ASCII.GetBytes(".gfbanm");
    private static readonly string[] BallthrowArchiveSuffixes =
    [
        "_ballthrow01.gfbanm",
        "_ballthrow01_start.gfbanm",
        "_ballthrow01_loop.gfbanm",
        "_ballthrow01_end.gfbanm",
        "_ballthrow02.gfbanm",
        "_ballthrow02_start.gfbanm",
        "_ballthrow02_loop.gfbanm",
        "_ballthrow02_end.gfbanm",
        "_g_ballthrow01.gfbanm",
        "_g_ballthrow01_start.gfbanm",
        "_g_ballthrow01_loop.gfbanm",
        "_g_ballthrow01_end.gfbanm",
        "_g_ballthrow02.gfbanm",
        "_g_ballthrow02_start.gfbanm",
        "_g_ballthrow02_loop.gfbanm",
        "_g_ballthrow02_end.gfbanm",
    ];

    public static bool IsManagedRomFsPath(string normalizedRelativePath)
    {
        return IsTrainerThrowAnimationPath(normalizedRelativePath)
            || IsTrainerBattleArchivePath(normalizedRelativePath);
    }

    public static bool IsTrainerThrowAnimationPath(string normalizedRelativePath)
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

        return normalizedRelativePath.StartsWith(CharaTrainerRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".gfbanm", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTrainerBattleArchivePath(string normalizedRelativePath)
    {
        var fileName = Path.GetFileName(normalizedRelativePath);
        return normalizedRelativePath.StartsWith(TrainerBattleArchiveRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
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
                throw new InvalidDataException("60FPS Patch could not find a usable trainer throw FrameRate field.");
            }

            SwShFpsFlatBufferAnimation.SetFrameRate(patched, Math.Max(1u, frameRate / 2));
            return patched;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid trainer throw GF animation.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid trainer throw GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid trainer throw GF animation FlatBuffer.", exception);
        }
    }

    public static byte[] ConvertTrainerBattleArchive(byte[] source, string archiveRelativePath)
    {
        ArgumentNullException.ThrowIfNull(source);

        var archive = SwShGfPackFile.Parse(source);
        var targetClipNames = ExtractBallthrowClipNames(source)
            .Concat(GenerateBallthrowCandidateNames(archiveRelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(archive.ContainsFileName)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targetClipNames.Length == 0)
        {
            throw new InvalidDataException(
                $"60FPS Patch could not find trainer ball throw clips in {Path.GetFileName(archiveRelativePath)}.");
        }

        foreach (var fileName in targetClipNames)
        {
            archive.SetFileByName(fileName, ConvertAnimationToHalfSpeed(archive.GetFileByName(fileName)));
        }

        return archive.Write();
    }

    internal static SwShFpsTrainerThrowAnimationInfo InspectAnimation(byte[] source)
    {
        return new SwShFpsTrainerThrowAnimationInfo(
            SwShFpsFlatBufferAnimation.GetKeyFrames(source),
            SwShFpsFlatBufferAnimation.GetFrameRate(source));
    }

    internal static IReadOnlyList<string> ExtractBallthrowClipNames(byte[] source)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 0; offset <= source.Length - GfbanmSuffix.Length; offset++)
        {
            if (!MatchesAsciiIgnoreCase(source, offset, GfbanmSuffix))
            {
                continue;
            }

            var endOffset = offset + GfbanmSuffix.Length;
            var startOffset = offset - 1;
            while (startOffset >= 0
                && endOffset - startOffset <= MaxExtractedFileNameLength
                && IsFileNameByte(source[startOffset]))
            {
                startOffset--;
            }

            if (endOffset - startOffset - 1 > MaxExtractedFileNameLength)
            {
                continue;
            }

            var candidate = Encoding.ASCII.GetString(source, startOffset + 1, endOffset - startOffset - 1)
                .Replace('\\', '/');
            var slashIndex = candidate.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                candidate = candidate[(slashIndex + 1)..];
            }

            if (candidate.Contains("ballthrow", StringComparison.OrdinalIgnoreCase)
                && candidate.EndsWith(".gfbanm", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(candidate);
            }
        }

        return result.OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> GenerateBallthrowCandidateNames(string archiveRelativePath)
    {
        var prefix = GetTrainerPrefix(archiveRelativePath);
        if (prefix is null)
        {
            yield break;
        }

        for (var animationId = 0; animationId <= MaxCandidateBattleAnimationId; animationId++)
        {
            var animationPrefix = $"{prefix}_ba{animationId:D4}";
            foreach (var suffix in BallthrowArchiveSuffixes)
            {
                yield return animationPrefix + suffix;
            }
        }
    }

    private static string? GetTrainerPrefix(string archiveRelativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(archiveRelativePath);
        var parts = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].StartsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{parts[0]}_{parts[1]}";
    }

    private static bool MatchesAsciiIgnoreCase(byte[] source, int offset, byte[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            var value = source[offset + index];
            if (value >= (byte)'A' && value <= (byte)'Z')
            {
                value = (byte)(value + 32);
            }

            if (value != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFileNameByte(byte value)
    {
        return value is >= (byte)'a' and <= (byte)'z'
            or >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_'
            or (byte)'-'
            or (byte)'.'
            or (byte)'/'
            or (byte)'\\';
    }
}
