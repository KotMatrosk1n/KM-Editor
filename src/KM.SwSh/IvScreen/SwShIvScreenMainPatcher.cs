// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.SwSh.IvScreen;

internal enum SwShIvScreenInstallKind
{
    NotInstalled,
    InstalledV1,
    InstalledLegacyV1,
    ForeignPatch,
    Conflict,
}

internal sealed record SwShIvScreenAnalysis(
    SwShIvScreenInstallKind Kind,
    string Message);

internal static class SwShIvScreenMainPatcher
{
    public const int LegacySecondaryStatsHookSiteOffset = 0x0137F634;
    public const int LegacyNormalStatsGraphHookSiteOffset = 0x0138F268;
    public const int ExeFsHookSiteOffset = 0x01392EA8;
    public const int VanillaSecondaryStatsSetupOffset = 0x013872D0;
    public const int SelectedPokemonResolverOffset = 0x01385A70;
    public const int RawIvGetterOffset = 0x00779070;
    public const int HyperTrainingIvWrapperOffset = 0x007790D0;

    private const int CalculatedStatGetterOffset = 0x00778E20;
    private const int NormalStatsGraphRendererOffset = 0x0138FB60;
    private const int MultiChartRendererOffset = 0x0138A1A0;
    private const int ForceValuePaneVisibilityOffset = 0x0138B1E0;
    private const int ValueWrapperSlotOffset = 0x0138F324;
    private const int RenderWrapperSlotOffset = 0x01390204;
    private const int RenderWrapperContinueSlotOffset = 0x01390BE4;
    private const int RenderWrapperReturnSlotOffset = 0x01391114;
    private const int XYellowStateRequestBranchOffset = 0x0139FB60;

    private const uint VanillaLegacySecondaryStatsHookInstruction = 0x94001F27; // bl 0x013872D0
    private const uint VanillaLegacyNormalStatsGraphHookInstruction = 0x9400023E; // bl 0x0138FB60
    private const uint VanillaSecondaryStatsSetupEntry = 0xD103C3FF;
    private const uint SelectedPokemonResolverEntry = 0xD10143FF;
    private const uint RawIvGetterEntry = 0x7100143F;
    private const uint HyperTrainingIvWrapperEntry = 0xA9BE4FF4;
    private const uint NormalStatsGraphRendererEntry = 0xD10243FF;
    private const uint MultiChartRendererEntry = 0xD10503FF;
    private const uint ForceValuePaneVisibilityEntry = 0xD10183FF;
    private const uint VanillaXYellowStateRequestInstruction = 0x340000A8;
    private const uint XYellowStateBypassInstruction = 0x14000005;
    private const uint MovW8W0Instruction = 0x2A0003E8;
    private const uint ForceVisibleMovInstruction = 0x52800028;
    private const uint NopInstruction = 0xD503201F;
    private const int SlotLength = 0x0C;

    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("SWSH_IV_DISPLAY_V1");

    private static readonly (int Offset, uint VanillaInstruction)[] MultiChartStatSourceCallSites =
    [
        (0x0138AA50, 0x97CFBD40),
        (0x0138AA60, 0x97CFC074),
        (0x0138AA90, 0x97CFBD30),
        (0x0138AAA0, 0x97CFC064),
        (0x0138AAD0, 0x97CFBD20),
        (0x0138AAE0, 0x97CFC054),
        (0x0138AB10, 0x97CFBD10),
        (0x0138AB20, 0x97CFC044),
        (0x0138AB50, 0x97CFBD00),
        (0x0138AB60, 0x97CFC034),
        (0x0138AB90, 0x97CFBCF0),
        (0x0138ABA0, 0x97CFC024),
    ];

    private static readonly (int Offset, uint VanillaInstruction)[] YellowRawValueAddSites =
    [
        (0x0138AC88, 0x0B130008),
        (0x0138ACAC, 0x0B130008),
        (0x0138ACD0, 0x0B130008),
        (0x0138ACF8, 0x0B170008),
        (0x0138AD1C, 0x0B130008),
        (0x0138AD40, 0x0B130008),
    ];

    private static readonly (int Offset, uint VanillaInstruction)[] RendererWrapperCallSites =
    [
        (0x01392EA8, 0x97FFDCBE),
        (0x01393310, 0x97FFDBA4),
        (0x0139EF4C, 0x97FFAC95),
    ];

    private static readonly (int Offset, uint VanillaInstruction)[] NormalGraphValueCallSites =
    [
        (0x0138FBE8, 0x97CFA48E),
        (0x0138FC38, 0x97CFA47A),
        (0x0138FC74, 0x97CFA46B),
        (0x0138FC9C, 0x97CFA461),
        (0x0138FD2C, 0x97CFA43D),
        (0x0138FD5C, 0x97CFA431),
        (0x0138FD84, 0x97CFA427),
        (0x0138FEA0, 0x97CFA3E0),
    ];

    private static readonly (int Offset, uint[] Instructions)[] ValueWrapperSlots =
    [
        (0x0138F324, [0x7100003F, 0x54007400, 0x140000F6]),
        (0x0138F704, [0x7100043F, 0x54005500, 0x14000016]),
        (0x0138F764, [0x7100083F, 0x54005200, 0x14000086]),
        (0x0138F984, [0x71000C3F, 0x54003D80, 0x14000072]),
        (0x0138FB54, [0x7100103F, 0x54003280, 0x14000126]),
        (0x0138FFF4, [0x7100143F, 0x54000D80, 0x14000016]),
        (0x01390054, [0x7100183F, 0x54000780, 0x14000002]),
        (0x01390064, [0x71001C3F, 0x54000680, 0x14000032]),
        (0x01390134, [0x17CFA33B, 0x52800001, 0x14000002]),
        (0x01390144, [0x17CFA3CB, 0x52800061, 0x14000016]),
        (0x013901A4, [0x17CFA3B3, 0x17CFA3B2, NopInstruction]),
    ];

    private static readonly int[] PayloadSlotOffsets =
    [
        0x0138F324,
        0x0138F704,
        0x0138F764,
        0x0138F984,
        0x0138FB54,
        0x0138FFF4,
        0x01390054,
        0x01390064,
        0x01390134,
        0x01390144,
        0x013901A4,
        0x01390204,
        0x01390BE4,
        0x01391114,
    ];

    private static readonly int[] MarkerFragmentOffsets =
    [
        0x013975B4,
        0x01397934,
    ];

    public static SwShIvScreenAnalysis Analyze(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var text = nso.Text.DecompressedData;
            EnsureRequiredRanges(text);

            var hasMarker = HasMarker(text);
            if (hasMarker)
            {
                if (IsInstalledV1(text))
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.InstalledV1,
                        "IV Screen is installed. Reinstalling refreshes the existing raw-IV summary hooks and marker instead of adding a second hook.");
                }

                if (IsLegacyInstall(text))
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.InstalledLegacyV1,
                        "IV Screen is installed with an older hook layout. Reinstalling migrates it to the IV-owned summary value-source hooks.");
                }

                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.Conflict,
                    "IV Screen marker is present, but the owned Pokemon Summary hook sites do not match a supported IV Screen layout.");
            }

            if (AllVanilla(text))
            {
                var occupiedSlot = FindOccupiedOwnedSlot(text);
                if (occupiedSlot is not null)
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.Conflict,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"IV Screen reserved slot main.text+0x{occupiedSlot.Value:X} is not empty."));
                }

                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.NotInstalled,
                    "IV Screen is not installed. Installing adds independent Pokemon Summary raw-IV value hooks and bypasses the yellow X-training overlay.");
            }

            return new SwShIvScreenAnalysis(
                AnyBranchLikeAtOwnedHookSite(text) ? SwShIvScreenInstallKind.ForeignPatch : SwShIvScreenInstallKind.Conflict,
                "Pokemon Summary IV Screen hook sites are already modified and do not contain the IV Screen marker.");
        }
        catch (InvalidDataException exception)
        {
            return new SwShIvScreenAnalysis(
                SwShIvScreenInstallKind.Conflict,
                exception.Message);
        }
    }

    public static byte[] Apply(byte[] mainBytes)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes);
        if (analysis.Kind is SwShIvScreenInstallKind.ForeignPatch or SwShIvScreenInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.ToArray();
        EnsureRequiredRanges(text);
        EnsureAnchors(text);
        EnsureSlotsAvailableOrOwned(text);

        ClearOwnedSlots(text);
        WriteValueWrapper(text);
        WriteRenderWrapper(text);
        WriteMarker(text);

        WriteInstruction(text, LegacySecondaryStatsHookSiteOffset, VanillaLegacySecondaryStatsHookInstruction);
        WriteInstruction(text, LegacyNormalStatsGraphHookSiteOffset, VanillaLegacyNormalStatsGraphHookInstruction);

        foreach (var callSite in NormalGraphValueCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, ValueWrapperSlotOffset));
        }

        foreach (var callSite in MultiChartStatSourceCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, RawIvGetterOffset));
        }

        foreach (var site in YellowRawValueAddSites)
        {
            WriteInstruction(text, site.Offset, MovW8W0Instruction);
        }

        WriteInstruction(text, 0x0138B1FC, ForceVisibleMovInstruction);
        WriteInstruction(text, 0x0138B200, NopInstruction);

        foreach (var callSite in RendererWrapperCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, RenderWrapperSlotOffset));
        }

        WriteInstruction(text, XYellowStateRequestBranchOffset, XYellowStateBypassInstruction);

        return nso.Write(textDecompressedData: text);
    }

    public static byte[] RestoreFromBase(byte[] currentMainBytes, byte[] baseMainBytes)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("IV Screen restore requires current and base main NSO files with matching .text sizes.");
        }

        foreach (var region in SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerIvScreen))
        {
            var offset = region.StartOffset!.Value;
            var length = region.Length!.Value;
            EnsureTextRange(currentText, offset, length, region.Label);
            EnsureTextRange(baseText, offset, length, $"Base {region.Label}");
            baseText.AsSpan(offset, length).CopyTo(currentText.AsSpan(offset, length));
        }

        return currentNso.Write(textDecompressedData: currentText);
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerIvScreen);
    }

    private static bool IsInstalledV1(ReadOnlySpan<byte> text)
    {
        return ReadInstruction(text, LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
            && ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset) == VanillaLegacyNormalStatsGraphHookInstruction
            && AllBranchLinksTo(text, NormalGraphValueCallSites, ValueWrapperSlotOffset)
            && AllBranchLinksTo(text, MultiChartStatSourceCallSites, RawIvGetterOffset)
            && AllInstructionsEqual(text, YellowRawValueAddSites, MovW8W0Instruction)
            && ReadInstruction(text, 0x0138B1FC) == ForceVisibleMovInstruction
            && ReadInstruction(text, 0x0138B200) == NopInstruction
            && AllBranchLinksTo(text, RendererWrapperCallSites, RenderWrapperSlotOffset)
            && ReadInstruction(text, XYellowStateRequestBranchOffset) == XYellowStateBypassInstruction
            && IsValueWrapperWritten(text)
            && IsRenderWrapperWritten(text);
    }

    private static bool IsLegacyInstall(ReadOnlySpan<byte> text)
    {
        return IsBranchLinkTo(ReadInstruction(text, LegacySecondaryStatsHookSiteOffset), LegacySecondaryStatsHookSiteOffset, ValueWrapperSlotOffset)
            || IsBranchLinkTo(ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset), LegacyNormalStatsGraphHookSiteOffset, ValueWrapperSlotOffset)
            || AnyBranchLinkTo(text, NormalGraphValueCallSites, ValueWrapperSlotOffset)
            || AnyBranchLinkTo(text, RendererWrapperCallSites, RenderWrapperSlotOffset);
    }

    private static bool AllVanilla(ReadOnlySpan<byte> text)
    {
        return ReadInstruction(text, LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
            && ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset) == VanillaLegacyNormalStatsGraphHookInstruction
            && AllInstructionsVanilla(text, NormalGraphValueCallSites)
            && AllInstructionsVanilla(text, MultiChartStatSourceCallSites)
            && AllInstructionsVanilla(text, YellowRawValueAddSites)
            && AllInstructionsVanilla(text, RendererWrapperCallSites)
            && ReadInstruction(text, 0x0138B1FC) == 0x39592408
            && ReadInstruction(text, 0x0138B200) == 0x52000108
            && ReadInstruction(text, XYellowStateRequestBranchOffset) == VanillaXYellowStateRequestInstruction;
    }

    private static bool AnyBranchLikeAtOwnedHookSite(ReadOnlySpan<byte> text)
    {
        return AnyBranchLike(text, RendererWrapperCallSites)
            || AnyBranchLike(text, NormalGraphValueCallSites)
            || AnyBranchLike(text, MultiChartStatSourceCallSites)
            || IsBranchOrBranchLink(ReadInstruction(text, LegacySecondaryStatsHookSiteOffset))
            || IsBranchOrBranchLink(ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset));
    }

    private static bool AllBranchLinksTo(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites,
        int targetOffset)
    {
        foreach (var site in sites)
        {
            if (!IsBranchLinkTo(ReadInstruction(text, site.Offset), site.Offset, targetOffset))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyBranchLinkTo(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites,
        int targetOffset)
    {
        foreach (var site in sites)
        {
            if (IsBranchLinkTo(ReadInstruction(text, site.Offset), site.Offset, targetOffset))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AllInstructionsEqual(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites,
        uint instruction)
    {
        foreach (var site in sites)
        {
            if (ReadInstruction(text, site.Offset) != instruction)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllInstructionsVanilla(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites)
    {
        foreach (var site in sites)
        {
            if (ReadInstruction(text, site.Offset) != site.VanillaInstruction)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyBranchLike(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites)
    {
        foreach (var site in sites)
        {
            if (IsBranchOrBranchLink(ReadInstruction(text, site.Offset)))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteValueWrapper(byte[] text)
    {
        foreach (var slot in ValueWrapperSlots)
        {
            for (var index = 0; index < slot.Instructions.Length; index++)
            {
                WriteInstruction(text, slot.Offset + (index * sizeof(uint)), slot.Instructions[index]);
            }
        }
    }

    private static bool IsValueWrapperWritten(ReadOnlySpan<byte> text)
    {
        foreach (var slot in ValueWrapperSlots)
        {
            for (var index = 0; index < slot.Instructions.Length; index++)
            {
                if (ReadInstruction(text, slot.Offset + (index * sizeof(uint))) != slot.Instructions[index])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void WriteRenderWrapper(byte[] text)
    {
        WriteInstruction(text, RenderWrapperSlotOffset + 0x00, 0xA9BF7BF3); // stp x19, x30, [sp,#-0x10]!
        WriteInstruction(text, RenderWrapperSlotOffset + 0x04, 0xAA0003F3); // mov x19, x0
        WriteInstruction(text, RenderWrapperSlotOffset + 0x08, EncodeBranchLink(RenderWrapperSlotOffset + 0x08, MultiChartRendererOffset));
        WriteInstruction(text, RenderWrapperContinueSlotOffset + 0x00, 0xAA1303E0); // mov x0, x19
        WriteInstruction(text, RenderWrapperContinueSlotOffset + 0x04, EncodeBranchLink(RenderWrapperContinueSlotOffset + 0x04, ForceValuePaneVisibilityOffset));
        WriteInstruction(text, RenderWrapperContinueSlotOffset + 0x08, EncodeBranch(RenderWrapperContinueSlotOffset + 0x08, RenderWrapperReturnSlotOffset));
        WriteInstruction(text, RenderWrapperReturnSlotOffset + 0x00, 0xA8C17BF3); // ldp x19, x30, [sp],#0x10
        WriteInstruction(text, RenderWrapperReturnSlotOffset + 0x04, 0xD65F03C0); // ret
        WriteInstruction(text, RenderWrapperReturnSlotOffset + 0x08, NopInstruction);
    }

    private static bool IsRenderWrapperWritten(ReadOnlySpan<byte> text)
    {
        return ReadInstruction(text, RenderWrapperSlotOffset + 0x00) == 0xA9BF7BF3
            && ReadInstruction(text, RenderWrapperSlotOffset + 0x04) == 0xAA0003F3
            && IsBranchLinkTo(ReadInstruction(text, RenderWrapperSlotOffset + 0x08), RenderWrapperSlotOffset + 0x08, MultiChartRendererOffset)
            && ReadInstruction(text, RenderWrapperContinueSlotOffset + 0x00) == 0xAA1303E0
            && IsBranchLinkTo(ReadInstruction(text, RenderWrapperContinueSlotOffset + 0x04), RenderWrapperContinueSlotOffset + 0x04, ForceValuePaneVisibilityOffset)
            && IsBranchTo(ReadInstruction(text, RenderWrapperContinueSlotOffset + 0x08), RenderWrapperContinueSlotOffset + 0x08, RenderWrapperReturnSlotOffset)
            && ReadInstruction(text, RenderWrapperReturnSlotOffset + 0x00) == 0xA8C17BF3
            && ReadInstruction(text, RenderWrapperReturnSlotOffset + 0x04) == 0xD65F03C0
            && ReadInstruction(text, RenderWrapperReturnSlotOffset + 0x08) == NopInstruction;
    }

    private static void WriteMarker(byte[] text)
    {
        foreach (var markerOffset in MarkerFragmentOffsets)
        {
            text.AsSpan(markerOffset, SlotLength).Clear();
        }

        var markerIndex = 0;
        foreach (var markerOffset in MarkerFragmentOffsets)
        {
            var fragmentLength = Math.Min(SlotLength, Marker.Length - markerIndex);
            if (fragmentLength <= 0)
            {
                break;
            }

            Marker.AsSpan(markerIndex, fragmentLength).CopyTo(text.AsSpan(markerOffset, fragmentLength));
            markerIndex += fragmentLength;
        }
    }

    private static bool HasMarker(ReadOnlySpan<byte> text)
    {
        Span<byte> markerBytes = stackalloc byte[Marker.Length];
        var markerIndex = 0;
        foreach (var markerOffset in MarkerFragmentOffsets)
        {
            EnsureTextRange(text, markerOffset, SlotLength, $"IV Screen marker main.text+0x{markerOffset:X}");
            var fragmentLength = Math.Min(SlotLength, Marker.Length - markerIndex);
            if (fragmentLength <= 0)
            {
                break;
            }

            text.Slice(markerOffset, fragmentLength).CopyTo(markerBytes[markerIndex..(markerIndex + fragmentLength)]);
            markerIndex += fragmentLength;
        }

        return markerBytes.SequenceEqual(Marker);
    }

    private static int? FindOccupiedOwnedSlot(ReadOnlySpan<byte> text)
    {
        foreach (var slotOffset in PayloadSlotOffsets.Concat(MarkerFragmentOffsets))
        {
            EnsureTextRange(text, slotOffset, SlotLength, $"IV Screen slot main.text+0x{slotOffset:X}");
            if (!IsZero(text.Slice(slotOffset, SlotLength)))
            {
                return slotOffset;
            }
        }

        return null;
    }

    private static void EnsureSlotsAvailableOrOwned(ReadOnlySpan<byte> text)
    {
        if (HasMarker(text))
        {
            return;
        }

        var occupiedSlot = FindOccupiedOwnedSlot(text);
        if (occupiedSlot is not null)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen reserved slot main.text+0x{occupiedSlot.Value:X} is not empty."));
        }
    }

    private static void ClearOwnedSlots(byte[] text)
    {
        foreach (var slotOffset in PayloadSlotOffsets.Concat(MarkerFragmentOffsets))
        {
            EnsureTextRange(text, slotOffset, SlotLength, $"IV Screen slot main.text+0x{slotOffset:X}");
            text.AsSpan(slotOffset, SlotLength).Clear();
        }
    }

    private static void EnsureRequiredRanges(ReadOnlySpan<byte> text)
    {
        foreach (var offset in PayloadSlotOffsets
                     .Concat(MarkerFragmentOffsets)
                     .Concat(NormalGraphValueCallSites.Select(site => site.Offset))
                     .Concat(MultiChartStatSourceCallSites.Select(site => site.Offset))
                     .Concat(YellowRawValueAddSites.Select(site => site.Offset))
                     .Concat(RendererWrapperCallSites.Select(site => site.Offset))
                     .Append(LegacySecondaryStatsHookSiteOffset)
                     .Append(LegacyNormalStatsGraphHookSiteOffset)
                     .Append(XYellowStateRequestBranchOffset)
                     .Append(VanillaSecondaryStatsSetupOffset)
                     .Append(SelectedPokemonResolverOffset)
                     .Append(RawIvGetterOffset)
                     .Append(HyperTrainingIvWrapperOffset)
                     .Append(NormalStatsGraphRendererOffset)
                     .Append(MultiChartRendererOffset)
                     .Append(ForceValuePaneVisibilityOffset)
                     .Append(0x0138B1FC)
                     .Append(0x0138B200))
        {
            EnsureTextRange(text, offset, sizeof(uint), $"IV Screen range main.text+0x{offset:X}");
        }
    }

    private static void EnsureAnchors(ReadOnlySpan<byte> text)
    {
        EnsureAnchor(text, VanillaSecondaryStatsSetupOffset, VanillaSecondaryStatsSetupEntry, "secondary-stats setup");
        EnsureAnchor(text, SelectedPokemonResolverOffset, SelectedPokemonResolverEntry, "selected Pokemon resolver");
        EnsureAnchor(text, RawIvGetterOffset, RawIvGetterEntry, "raw IV getter");
        EnsureAnchor(text, HyperTrainingIvWrapperOffset, HyperTrainingIvWrapperEntry, "Hyper Training-adjusted IV wrapper guard");
        EnsureAnchor(text, NormalStatsGraphRendererOffset, NormalStatsGraphRendererEntry, "normal stats graph renderer");
        EnsureAnchor(text, MultiChartRendererOffset, MultiChartRendererEntry, "summary multi-chart renderer");
        EnsureAnchor(text, ForceValuePaneVisibilityOffset, ForceValuePaneVisibilityEntry, "summary value-pane visibility helper");

        if (ReadInstruction(text, LegacySecondaryStatsHookSiteOffset) != VanillaLegacySecondaryStatsHookInstruction
            && !IsBranchLinkTo(ReadInstruction(text, LegacySecondaryStatsHookSiteOffset), LegacySecondaryStatsHookSiteOffset, ValueWrapperSlotOffset))
        {
            throw new InvalidDataException("IV Screen install requires a vanilla or IV Screen-owned legacy secondary setup hook.");
        }

        if (ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset) != VanillaLegacyNormalStatsGraphHookInstruction
            && !IsBranchLinkTo(ReadInstruction(text, LegacyNormalStatsGraphHookSiteOffset), LegacyNormalStatsGraphHookSiteOffset, ValueWrapperSlotOffset))
        {
            throw new InvalidDataException("IV Screen install requires a vanilla or IV Screen-owned legacy normal graph hook.");
        }

        foreach (var site in NormalGraphValueCallSites)
        {
            EnsureVanillaOrOwnedCall(text, site.Offset, site.VanillaInstruction, ValueWrapperSlotOffset, "normal graph value source");
        }

        foreach (var site in MultiChartStatSourceCallSites)
        {
            EnsureVanillaOrOwnedCall(text, site.Offset, site.VanillaInstruction, RawIvGetterOffset, "summary multi-chart stat source");
        }

        foreach (var site in RendererWrapperCallSites)
        {
            EnsureVanillaOrOwnedCall(text, site.Offset, site.VanillaInstruction, RenderWrapperSlotOffset, "summary multi-chart wrapper");
        }

        foreach (var site in YellowRawValueAddSites)
        {
            var actual = ReadInstruction(text, site.Offset);
            if (actual != site.VanillaInstruction && actual != MovW8W0Instruction)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"IV Screen expected vanilla or owned yellow raw-value instruction at main.text+0x{site.Offset:X}, but found 0x{actual:X8}."));
            }
        }
    }

    private static void EnsureVanillaOrOwnedCall(
        ReadOnlySpan<byte> text,
        int offset,
        uint vanillaInstruction,
        int ownedTargetOffset,
        string label)
    {
        var actual = ReadInstruction(text, offset);
        if (actual != vanillaInstruction && !IsBranchLinkTo(actual, offset, ownedTargetOffset))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen expected vanilla or owned {label} at main.text+0x{offset:X}, but found 0x{actual:X8}."));
        }
    }

    private static void EnsureAnchor(ReadOnlySpan<byte> text, int offset, uint expectedInstruction, string label)
    {
        var actualInstruction = ReadInstruction(text, offset);
        if (actualInstruction != expectedInstruction)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen expected vanilla {label} at main.text+0x{offset:X}, but found 0x{actualInstruction:X8}."));
        }
    }

    private static void EnsureTextRange(ReadOnlySpan<byte> text, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Instruction main.text+0x{offset:X}");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Patch instruction main.text+0x{offset:X}");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBranchOrBranchLink(uint instruction)
    {
        return (instruction & 0x7C000000) == 0x14000000;
    }

    private static bool IsBranchTo(uint instruction, int sourceOffset, int targetOffset)
    {
        if ((instruction & 0xFC000000) != 0x14000000)
        {
            return false;
        }

        return DecodeBranchTarget(instruction, sourceOffset) == targetOffset;
    }

    private static bool IsBranchLinkTo(uint instruction, int sourceOffset, int targetOffset)
    {
        if ((instruction & 0xFC000000) != 0x94000000)
        {
            return false;
        }

        return DecodeBranchTarget(instruction, sourceOffset) == targetOffset;
    }

    private static int DecodeBranchTarget(uint instruction, int sourceOffset)
    {
        var imm26 = (int)(instruction & 0x03FFFFFF);
        if ((imm26 & 0x02000000) != 0)
        {
            imm26 |= unchecked((int)0xFC000000);
        }

        return sourceOffset + (imm26 << 2);
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        return EncodeBranchLike(0x14000000, sourceOffset, targetOffset);
    }

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        return EncodeBranchLike(0x94000000, sourceOffset, targetOffset);
    }

    private static uint EncodeBranchLike(uint opcode, int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Branch target must be 4-byte aligned.");
        }

        var imm26 = delta >> 2;
        if (imm26 < -(1 << 25) || imm26 >= (1 << 25))
        {
            throw new InvalidDataException("Branch target is outside ARM64 range.");
        }

        return opcode | (uint)(imm26 & 0x03FFFFFF);
    }
}
