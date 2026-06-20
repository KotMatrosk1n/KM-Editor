// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Text;

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsLegacyTrainerThrowCleanupPatcher
{
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

    public static byte[] ConvertLegacyOutput(string normalizedRelativePath, byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (SwShFpsTrainerThrowPatcher.IsLegacyTrainerCharacterAnimationPath(normalizedRelativePath))
        {
            return SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(source);
        }

        if (SwShFpsTrainerThrowPatcher.IsLegacyTrainerBattleArchivePath(normalizedRelativePath))
        {
            return ConvertTrainerBattleArchive(source, normalizedRelativePath);
        }

        throw new InvalidDataException("60FPS Patch does not recognize this legacy trainer throw output.");
    }

    private static byte[] ConvertTrainerBattleArchive(byte[] source, string archiveRelativePath)
    {
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
                $"60FPS Patch could not find legacy trainer ball throw clips in {Path.GetFileName(archiveRelativePath)}.");
        }

        foreach (var fileName in targetClipNames)
        {
            archive.SetFileByName(fileName, SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(archive.GetFileByName(fileName)));
        }

        return archive.Write();
    }

    private static IReadOnlyList<string> ExtractBallthrowClipNames(byte[] source)
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
