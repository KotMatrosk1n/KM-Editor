// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsBattleModelAnimationPatcher
{
    public static byte[] ConvertAnimationToHalfSpeed(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            var patched = source.ToArray();
            var frameRate = SwShFpsFlatBufferAnimation.GetFrameRate(patched);
            if (frameRate <= 1)
            {
                throw new InvalidDataException("60FPS Patch could not find a usable battle model animation FrameRate field.");
            }

            SwShFpsFlatBufferAnimation.SetFrameRate(patched, Math.Max(1u, frameRate / 2));
            return patched;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid battle model GF animation.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid battle model GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid battle model GF animation FlatBuffer.", exception);
        }
    }
}
