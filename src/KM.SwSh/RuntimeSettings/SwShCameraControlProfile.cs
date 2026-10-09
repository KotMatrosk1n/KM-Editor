// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using KM.SwSh.FpsPatch;

namespace KM.SwSh.RuntimeSettings;

internal static class SwShCameraControlProfile
{
    private static readonly (int Offset, int Length)[] dependencies =
    [
        (0x00CF8F00, 0x210), (0x00CFA230, 0x70),
        (0x00CFD500, 0x1680), (0x00D3B5F0, 0x2A80),
        (0x00D8F2E0, 0xF00), (0x00F194A0, 0x490),
        (0x00D93390, 0x568),
    ];

    public static void ValidateComposition(byte[] baseBytes, byte[] currentBytes, ProjectGame game)
    {
        var baseline = NsoFile.Parse(baseBytes).Text.DecompressedData;
        var current = NsoFile.Parse(currentBytes).Text.DecompressedData;
        var delta = game == ProjectGame.Shield ? 0x30 : 0;
        bool Matches(byte[] candidate) => dependencies.All(region =>
            candidate.AsSpan(region.Offset + delta, region.Length)
                .SequenceEqual(baseline.AsSpan(region.Offset + delta, region.Length)));
        if (Matches(current)) return;

        // Only the proven FPS transformations may differ in the camera's
        // dependencies. Normalize a verification copy; preserve the delivered
        // executable and its native return-blend timing hooks.
        try
        {
            var proof = SwShFpsMainPatcher.RestoreFromBase(currentBytes, baseBytes, game);
            if (Matches(NsoFile.Parse(proof).Text.DecompressedData)) return;
        }
        catch (InvalidDataException)
        {
            // Report this operation's specific ownership conflict below.
        }
        throw new InvalidDataException(
            "Camera Control requires the original camera and field task code, or the compatible KM 60FPS Patch. Another executable edit overlaps its runtime dependencies.");
    }

    public static IEnumerable<SwShExeFsReservedRegion> CreateReservations()
    {
        foreach (var (name, delta) in new[] { ("sword", 0), ("shield", 0x30) })
        foreach (var (label, offset) in new[]
        {
            ("initialize", 0x00CF8F00), ("update", 0x00CFA230),
            ("push-task", 0x00F19670), ("queue-task", 0x00F195A0),
        })
            yield return new(SwShExeFsReservedRegionLedger.OwnerNativeGameplayMenu,
                $"camera-control-{name}-{label}", SwShExeFsReservedRegionLedger.ExeFsMainPath,
                "main.text", offset + delta, 16, "Camera Control runtime hook", "do-not-overwrite");
    }
}
