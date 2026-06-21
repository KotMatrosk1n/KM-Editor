// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsBattleCameraAnimationInfo(
    uint KeyFrames,
    uint FrameRate);

internal static class SwShFpsBattleCameraPatcher
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
                throw new InvalidDataException("60FPS Patch could not find a usable battle camera FrameRate field.");
            }

            SwShFpsFlatBufferAnimation.SetFrameRate(patched, Math.Max(1u, frameRate / 2));
            return patched;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid battle camera GF animation.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid battle camera GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid battle camera GF animation FlatBuffer.", exception);
        }
    }

    internal static SwShFpsBattleCameraAnimationInfo InspectAnimation(byte[] source)
    {
        return new SwShFpsBattleCameraAnimationInfo(
            SwShFpsFlatBufferAnimation.GetKeyFrames(source),
            SwShFpsFlatBufferAnimation.GetFrameRate(source));
    }
}
