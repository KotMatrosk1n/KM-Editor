// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsPokemonCenterRecoveryPatcher
{
    public const string RecoveryArchiveRelativePath = "romfs/bin/archive/field/model/unit_obj_pc_recovery01.gfpak";

    private const uint RecoveryFrameRate = 24;

    private static readonly string[] RecoveryAnimationNames =
    [
        "unit_obj_pc_recovery01_main01_ballput.gfbanm",
        "unit_obj_pc_recovery01_main01_recovery.gfbanm",
        "unit_obj_pc_recovery01_ballflash01_recovery.gfbanm",
    ];

    public static byte[] ConvertArchive(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var archive = SwShGfPackFile.Parse(source);
        foreach (var fileName in RecoveryAnimationNames)
        {
            if (!archive.TryGetFileByName(fileName, out var animation))
            {
                throw new InvalidDataException($"60FPS Patch could not find Pokemon Center recovery animation '{fileName}'.");
            }

            archive.SetFileByName(fileName, ConvertAnimationToRecoverySpeed(animation));
        }

        return archive.Write();
    }

    internal static byte[] ConvertAnimationToRecoverySpeed(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            var patched = source.ToArray();
            if (SwShFpsFlatBufferAnimation.GetFrameRate(patched) <= 1)
            {
                throw new InvalidDataException("60FPS Patch could not find a usable Pokemon Center recovery FrameRate field.");
            }

            SwShFpsFlatBufferAnimation.SetFrameRate(patched, RecoveryFrameRate);
            return patched;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid Pokemon Center recovery GF animation.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid Pokemon Center recovery GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid Pokemon Center recovery GF animation FlatBuffer.", exception);
        }
    }
}
