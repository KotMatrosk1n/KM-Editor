// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
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
    UnsupportedBuild,
    GameMismatch,
    ForeignPatch,
    Conflict,
}

internal sealed record SwShIvScreenAnalysis(
    SwShIvScreenInstallKind Kind,
    string Message,
    string BuildId,
    string PatchOffsetHex,
    ProjectGame? DetectedGame);

internal static class SwShIvScreenMainPatcher
{
    public const int LegacySecondaryStatsHookSiteOffset = 0x0137F634;
    public const int LegacyNormalStatsGraphHookSiteOffset = 0x0138F268;
    public const int ExeFsHookSiteOffset = 0x0138FBE8;
    public const int VanillaSecondaryStatsSetupOffset = 0x013872D0;
    public const int SelectedPokemonResolverOffset = 0x01385A70;
    public const int RawIvGetterOffset = 0x00779070;
    public const int HyperTrainingIvWrapperOffset = 0x007790D0;
    public const int ShieldExeFsHookSiteOffset = ExeFsHookSiteOffset + ShieldOffsetDelta;

    private const int CalculatedStatGetterOffset = 0x00778E20;
    private const int NormalStatsGraphRendererOffset = 0x0138FB60;
    private const int ValueWrapperSlotOffset = 0x0138F324;
    private const int LegacyHpIvWrapperSlotOffset = 0x01390204;
    private const int RenderWrapperSlotOffset = 0x01390204;
    private const int XYellowStateRequestBranchOffset = 0x0139FB60;
    private const int SummaryMultiChartRefreshOffset = 0x0138A1A0;
    private const int SummaryTextHpCurrentGetterOffset = 0x0077AFD0;
    private const int SummaryTextHpMaxGetterOffset = 0x0077AC70;
    private const int SummaryTextStatGetterOffset = 0x0077AC30;
    private const int SummaryGraphAlternateStatGetterOffset = 0x00779F50;
    private const int HpCurrentTextWrapperSlotOffset = 0x013912F4;
    private const int HpCurrentTextRawSlotOffset = 0x01391304;
    private const int HpMaxTextWrapperSlotOffset = 0x013916F4;
    private const int HpMaxTextRawSlotOffset = 0x01391704;
    private const int SummaryStatWrapperSlotOffset = 0x01391734;
    private const int SummaryStatRawSlotOffset = 0x01391744;
    private const int SummaryGraphAlternateWrapperSlotOffset = 0x01392854;
    private const int SummaryGraphAlternateRawSlotOffset = 0x01392864;
    private const int ValuePaneVisibilityLoadOffset = 0x0138B1FC;
    private const int ValuePaneVisibilityMaskOffset = 0x0138B200;
    private const int XToggleRefreshReturnOffset = 0x0138B550;
    private const int ShieldOffsetDelta = 0x30;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private const uint VanillaLegacySecondaryStatsHookInstruction = 0x94001F27; // bl 0x013872D0
    private const uint VanillaLegacyNormalStatsGraphHookInstruction = 0x9400023E; // bl 0x0138FB60
    private const uint RawIvGetterEntry = 0x7100143F;
    private const uint CalculatedStatGetterEntry = 0xA9BF7BFD;
    private const uint NormalStatsGraphRendererEntry = 0xD10243FF;
    private const uint VanillaValuePaneVisibilityLoadInstruction = 0x39592408;
    private const uint VanillaValuePaneVisibilityInvertInstruction = 0x52000108;
    private const uint VanillaXYellowStateRequestInstruction = 0x340000A8;
    private const uint XYellowStateBypassInstruction = 0x14000005;
    private const uint MovW8W0Instruction = 0x2A0003E8;
    private const uint MovW8WzrInstruction = 0x2A1F03E8;
    private const uint MovW0WzrInstruction = 0x2A1F03E0;
    private const uint MovW1WzrInstruction = 0x2A1F03E1;
    private const uint MovW8OneInstruction = 0x52800028;
    private const uint ForceVisibleMovInstruction = MovW8OneInstruction;
    private const uint XModePaneVisibilityInstruction = 0x39592688; // ldrb w8, [x20,#0x649]
    private const uint XTogglePaneVisibilityInstruction = 0x39592668; // ldrb w8, [x19,#0x649]
    private const uint NopInstruction = 0xD503201F;
    private const int SlotLength = 0x0C;

    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("SWSH_IV_DISPLAY_V1");

    private static readonly (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartStatSourceCallSites =
    [
        (0x0138AA50, 0x97CFBD40, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138AA60, 0x97CFC074, SummaryStatWrapperSlotOffset),
        (0x0138AA90, 0x97CFBD30, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138AAA0, 0x97CFC064, SummaryStatWrapperSlotOffset),
        (0x0138AAD0, 0x97CFBD20, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138AAE0, 0x97CFC054, SummaryStatWrapperSlotOffset),
        (0x0138AB10, 0x97CFBD10, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138AB20, 0x97CFC044, SummaryStatWrapperSlotOffset),
        (0x0138AB50, 0x97CFBD00, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138AB60, 0x97CFC034, SummaryStatWrapperSlotOffset),
        (0x0138AB90, 0x97CFBCF0, SummaryGraphAlternateWrapperSlotOffset),
        (0x0138ABA0, 0x97CFC024, SummaryStatWrapperSlotOffset),
    ];

    private static readonly (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartTextHpValueCallSites =
    [
        (0x0138A2B4, 0x97CFC347, HpCurrentTextWrapperSlotOffset),
        (0x0138A3CC, 0x97CFC229, HpMaxTextWrapperSlotOffset),
    ];

    private static readonly (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartTextStatValueCallSites =
    [
        (0x0138A47C, 0x97CFC1ED, SummaryStatWrapperSlotOffset),
        (0x0138A518, 0x97CFC1C6, SummaryStatWrapperSlotOffset),
        (0x0138A5B4, 0x97CFC19F, SummaryStatWrapperSlotOffset),
        (0x0138A650, 0x97CFC178, SummaryStatWrapperSlotOffset),
        (0x0138A6F0, 0x97CFC150, SummaryStatWrapperSlotOffset),
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

    private static readonly (int Offset, uint VanillaInstruction)[] YellowGraphValueCallSites =
    [
        (0x0138AE28, 0x97CFBE2E),
        (0x0138AE3C, 0x97CFBE29),
        (0x0138AE50, 0x97CFBE24),
        (0x0138AE64, 0x97CFBE1F),
        (0x0138AE78, 0x97CFBE1A),
        (0x0138AE8C, 0x97CFBE15),
    ];

    private static readonly (int Offset, uint[] VanillaInstructions, uint[] PatchedInstructions, uint[] LegacyPatchedInstructions)[] YellowValuePaneVisibilitySlots =
    [
        (0x0138AEAC,
            [0x2A1F03E8, 0x7103F27F, 0x54000063, 0x39592688, 0x52000108],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
        (0x0138AEE0,
            [0x7103EF1F, 0x54000089, 0x39592688, 0x52000108, 0x14000002, 0x2A1F03E8],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
        (0x0138AF18,
            [0x7103F2FF, 0x54000083, 0x39592688, 0x52000108, 0x14000002, 0x2A1F03E8],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
        (0x0138AF54,
            [0x7103F39F, 0x54000083, 0x39592688, 0x52000108, 0x14000002, 0x2A1F03E8],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
        (0x0138AF8C,
            [0x7103F37F, 0x54000083, 0x39592688, 0x52000108, 0x14000002, 0x2A1F03E8],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
        (0x0138AFC4,
            [0x7103F33F, 0x54000083, 0x39592688, 0x52000108, 0x14000002, 0x2A1F03E8],
            [MovW8WzrInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction],
            [XModePaneVisibilityInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction, NopInstruction]),
    ];

    private static readonly (int Offset, uint VanillaInstruction)[] NumericTextPaneRefreshSites =
    [
        (0x0138B230, XTogglePaneVisibilityInstruction),
        (0x0138B264, XTogglePaneVisibilityInstruction),
        (0x0138B298, XTogglePaneVisibilityInstruction),
        (0x0138B2CC, XTogglePaneVisibilityInstruction),
        (0x0138B300, XTogglePaneVisibilityInstruction),
        (0x0138B334, XTogglePaneVisibilityInstruction),
        (0x0138B368, XTogglePaneVisibilityInstruction),
        (0x0138B39C, XTogglePaneVisibilityInstruction),
    ];

    private static readonly (int Offset, uint[] VanillaInstructions) XToggleRefreshSlot =
        (0x0138B3AC, [0xF942EE60, 0x97FFE9B0, 0x2A1F03E1]);

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
        0x013912F4,
        0x01391304,
        0x013916F4,
        0x01391704,
        0x01391734,
        0x01391744,
        0x01392854,
        0x01392864,
    ];

    private static readonly int[] MarkerFragmentOffsets =
    [
        0x013975B4,
        0x01397934,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, 0),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldOffsetDelta),
    ];

    public static SwShIvScreenAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.UnsupportedBuild,
                    "IV Screen supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    "unknown",
                    DetectedGame: null);
            }

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsureRequiredRanges(text, layout);

            var hasMarker = HasMarker(text, layout);
            if (hasMarker)
            {
                if (IsInstalledV1(text, layout))
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.InstalledV1,
                        "IV Screen is installed. Reinstalling refreshes the existing raw-IV summary hooks and marker instead of adding a second hook.",
                        buildId,
                        FormatTextOffset(layout.ExeFsHookSiteOffset),
                        layout.Game);
                }

                if (IsLegacyInstall(text, layout))
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.InstalledLegacyV1,
                        "IV Screen is installed with an older hook layout. Reinstalling migrates it to the IV-owned summary value-source hooks.",
                        buildId,
                        FormatTextOffset(layout.ExeFsHookSiteOffset),
                        layout.Game);
                }

                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.Conflict,
                    "IV Screen marker is present, but the owned Pokemon Summary hook sites do not match a supported IV Screen layout.",
                    buildId,
                    FormatTextOffset(layout.ExeFsHookSiteOffset),
                    layout.Game);
            }

            if (AllVanilla(text, layout))
            {
                var occupiedSlot = FindOccupiedOwnedSlot(text, layout);
                if (occupiedSlot is not null)
                {
                    return new SwShIvScreenAnalysis(
                        SwShIvScreenInstallKind.Conflict,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"IV Screen reserved slot main.text+0x{occupiedSlot.Value:X} is not empty."),
                        buildId,
                        FormatTextOffset(layout.ExeFsHookSiteOffset),
                        layout.Game);
                }

                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.NotInstalled,
                    "IV Screen is not installed. Installing adds independent Pokemon Summary stats and X-mode raw-IV value hooks.",
                    buildId,
                    FormatTextOffset(layout.ExeFsHookSiteOffset),
                    layout.Game);
            }

            return new SwShIvScreenAnalysis(
                AnyBranchLikeAtOwnedHookSite(text, layout) ? SwShIvScreenInstallKind.ForeignPatch : SwShIvScreenInstallKind.Conflict,
                "Pokemon Summary IV Screen hook sites are already modified and do not contain the IV Screen marker.",
                buildId,
                FormatTextOffset(layout.ExeFsHookSiteOffset),
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShIvScreenAnalysis(
                SwShIvScreenInstallKind.Conflict,
                exception.Message,
                "unknown",
                "unknown",
                DetectedGame: null);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShIvScreenInstallKind.UnsupportedBuild
            or SwShIvScreenInstallKind.GameMismatch
            or SwShIvScreenInstallKind.ForeignPatch
            or SwShIvScreenInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("IV Screen supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsureRequiredRanges(text, layout);
        EnsureAnchors(text, layout);
        EnsureSlotsAvailableOrOwned(text, layout);

        ClearOwnedSlots(text, layout);
        WriteXModeValueWrappers(text, layout);
        WriteMarker(text, layout);

        WriteInstruction(text, layout.LegacySecondaryStatsHookSiteOffset, VanillaLegacySecondaryStatsHookInstruction);
        WriteInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset, VanillaLegacyNormalStatsGraphHookInstruction);
        RestoreUnsafeSummaryWrapperHooks(text, layout);

        foreach (var callSite in layout.NormalGraphValueCallSites)
        {
            WriteInstruction(text, callSite.Offset, callSite.VanillaInstruction);
        }

        foreach (var callSite in layout.MultiChartStatSourceCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, callSite.WrapperOffset));
        }

        foreach (var callSite in layout.MultiChartTextHpValueCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, callSite.WrapperOffset));
        }

        foreach (var callSite in layout.MultiChartTextStatValueCallSites)
        {
            WriteInstruction(text, callSite.Offset, EncodeBranchLink(callSite.Offset, callSite.WrapperOffset));
        }

        foreach (var site in layout.YellowRawValueAddSites)
        {
            WriteInstruction(text, site.Offset, MovW8WzrInstruction);
        }

        foreach (var callSite in layout.YellowGraphValueCallSites)
        {
            WriteInstruction(text, callSite.Offset, MovW0WzrInstruction);
        }

        WriteYellowValuePaneVisibility(text, layout);
        WriteNumericTextPaneRefresh(text, layout);
        WriteXToggleRefresh(text, layout);

        return nso.Write(textDecompressedData: text);
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = SwShNsoFile.Parse(currentMainBytes);
        var baseNso = SwShNsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("IV Screen restore requires current and base main NSO files with the same build ID.");
        }

        var layout = FindLayout(baseBuildId)
            ?? throw new InvalidDataException("IV Screen restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, baseBuildId);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("IV Screen restore requires current and base main NSO files with matching .text sizes.");
        }

        if (!AllVanilla(baseText, layout))
        {
            throw new InvalidDataException($"{layout.GameName} IV Screen restore expected a vanilla base main NSO.");
        }

        foreach (var region in ReservedMainTextRegions(layout.Game))
        {
            var offset = region.StartOffset!.Value;
            var length = region.Length!.Value;
            EnsureTextRange(currentText, offset, length, region.Label);
            EnsureTextRange(baseText, offset, length, $"Base {region.Label}");
            baseText.AsSpan(offset, length).CopyTo(currentText.AsSpan(offset, length));
        }

        return currentNso.Write(textDecompressedData: currentText);
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions(ProjectGame? game = null)
    {
        if (game is null)
        {
            return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerIvScreen);
        }

        var layout = Layouts.FirstOrDefault(candidate => candidate.Game == game.Value);
        return layout is null
            ? []
            : CreateReservedRegions(layout);
    }

    private static bool IsInstalledV1(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
            && ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset) == VanillaLegacyNormalStatsGraphHookInstruction
            && AllInstructionsVanilla(text, layout.NormalGraphValueCallSites)
            && AllBranchLinksToTargets(text, layout.MultiChartStatSourceCallSites)
            && AllBranchLinksToTargets(text, layout.MultiChartTextHpValueCallSites)
            && AllBranchLinksToTargets(text, layout.MultiChartTextStatValueCallSites)
            && AllInstructionsEqual(text, layout.YellowRawValueAddSites, MovW8WzrInstruction)
            && AllInstructionsEqual(text, layout.YellowGraphValueCallSites, MovW0WzrInstruction)
            && IsYellowValuePaneVisibilityWritten(text, layout)
            && AreNumericTextPaneRefreshesWritten(text, layout)
            && IsXToggleRefreshWritten(text, layout)
            && UnsafeSummaryWrapperHooksAreVanilla(text, layout)
            && AreXModeValueWrappersWritten(text, layout);
    }

    private static bool IsLegacyInstall(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return IsBranchLinkTo(ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset), layout.LegacySecondaryStatsHookSiteOffset, layout.ValueWrapperSlotOffset)
            || IsBranchLinkTo(ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset), layout.LegacyNormalStatsGraphHookSiteOffset, layout.ValueWrapperSlotOffset)
            || AnyBranchLinkTo(text, layout.NormalGraphValueCallSites, layout.ValueWrapperSlotOffset)
            || AnyBranchLinkTo(text, layout.MultiChartStatSourceCallSites, RawIvGetterOffset)
            || AnyBranchLinkToTargets(text, layout.MultiChartStatSourceCallSites)
            || AnyBranchLinkTo(text, layout.MultiChartTextHpValueCallSites, layout.LegacyHpIvWrapperSlotOffset)
            || AnyBranchLinkToTargets(text, layout.MultiChartTextHpValueCallSites)
            || AnyBranchLinkTo(text, layout.MultiChartTextStatValueCallSites, RawIvGetterOffset)
            || AnyBranchLinkToTargets(text, layout.MultiChartTextStatValueCallSites)
            || AnyInstructionEqual(text, layout.YellowRawValueAddSites, MovW8W0Instruction)
            || AnyInstructionEqual(text, layout.YellowRawValueAddSites, MovW8WzrInstruction)
            || AnyInstructionEqual(text, layout.YellowGraphValueCallSites, MovW0WzrInstruction)
            || AnyBranchLinkTo(text, layout.YellowGraphValueCallSites, RawIvGetterOffset)
            || AnyYellowValuePaneVisibilityWritten(text, layout)
            || AnyNumericTextPaneRefreshWritten(text, layout)
            || IsXToggleRefreshWritten(text, layout)
            || AnyBranchLinkTo(text, layout.RendererWrapperCallSites, layout.RenderWrapperSlotOffset)
            || ReadInstruction(text, layout.ValuePaneVisibilityLoadOffset) == ForceVisibleMovInstruction
            || ReadInstruction(text, layout.ValuePaneVisibilityMaskOffset) == NopInstruction
            || ReadInstruction(text, layout.XYellowStateRequestBranchOffset) == XYellowStateBypassInstruction;
    }

    private static bool AllVanilla(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
            && ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset) == VanillaLegacyNormalStatsGraphHookInstruction
            && AllInstructionsVanilla(text, layout.NormalGraphValueCallSites)
            && AllInstructionsVanilla(text, layout.MultiChartStatSourceCallSites)
            && AllInstructionsVanilla(text, layout.MultiChartTextHpValueCallSites)
            && AllInstructionsVanilla(text, layout.MultiChartTextStatValueCallSites)
            && AllInstructionsVanilla(text, layout.YellowRawValueAddSites)
            && AllInstructionsVanilla(text, layout.YellowGraphValueCallSites)
            && YellowValuePaneVisibilityIsVanilla(text, layout)
            && NumericTextPaneRefreshesAreVanilla(text, layout)
            && XToggleRefreshIsVanilla(text, layout)
            && UnsafeSummaryWrapperHooksAreVanilla(text, layout);
    }

    private static bool AnyBranchLikeAtOwnedHookSite(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return AnyBranchLike(text, layout.RendererWrapperCallSites)
            || AnyBranchLike(text, layout.NormalGraphValueCallSites)
            || AnyBranchLike(text, layout.MultiChartStatSourceCallSites)
            || AnyBranchLike(text, layout.MultiChartTextHpValueCallSites)
            || AnyBranchLike(text, layout.MultiChartTextStatValueCallSites)
            || AnyBranchLike(text, layout.YellowGraphValueCallSites)
            || IsBranchOrBranchLink(ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset))
            || IsBranchOrBranchLink(ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset));
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

    private static bool AllBranchLinksToTargets(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites)
    {
        foreach (var site in sites)
        {
            if (!IsBranchLinkTo(ReadInstruction(text, site.Offset), site.Offset, site.WrapperOffset))
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

    private static bool AnyInstructionEqual(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction)[] sites,
        uint instruction)
    {
        foreach (var site in sites)
        {
            if (ReadInstruction(text, site.Offset) == instruction)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyBranchLinkTo(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites,
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

    private static bool AnyBranchLinkToTargets(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites)
    {
        foreach (var site in sites)
        {
            if (IsBranchLinkTo(ReadInstruction(text, site.Offset), site.Offset, site.WrapperOffset))
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

    private static bool AllInstructionsVanilla(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites)
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

    private static void RestoreUnsafeSummaryWrapperHooks(byte[] text, PatchLayout layout)
    {
        foreach (var callSite in layout.RendererWrapperCallSites)
        {
            WriteInstruction(text, callSite.Offset, callSite.VanillaInstruction);
        }

        WriteInstruction(text, layout.ValuePaneVisibilityLoadOffset, VanillaValuePaneVisibilityLoadInstruction);
        WriteInstruction(text, layout.ValuePaneVisibilityMaskOffset, VanillaValuePaneVisibilityInvertInstruction);
        WriteInstruction(text, layout.XYellowStateRequestBranchOffset, VanillaXYellowStateRequestInstruction);
    }

    private static bool UnsafeSummaryWrapperHooksAreVanilla(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return ReadInstruction(text, layout.ValuePaneVisibilityLoadOffset) == VanillaValuePaneVisibilityLoadInstruction
            && ReadInstruction(text, layout.ValuePaneVisibilityMaskOffset) == VanillaValuePaneVisibilityInvertInstruction
            && AllInstructionsVanilla(text, layout.RendererWrapperCallSites)
            && ReadInstruction(text, layout.XYellowStateRequestBranchOffset) == VanillaXYellowStateRequestInstruction;
    }

    private static void WriteYellowValuePaneVisibility(byte[] text, PatchLayout layout)
    {
        foreach (var slot in layout.YellowValuePaneVisibilitySlots)
        {
            WriteInstructions(text, slot.Offset, slot.PatchedInstructions);
        }
    }

    private static bool IsYellowValuePaneVisibilityWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var slot in layout.YellowValuePaneVisibilitySlots)
        {
            if (!InstructionsEqual(text, slot.Offset, slot.PatchedInstructions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyYellowValuePaneVisibilityWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var slot in layout.YellowValuePaneVisibilitySlots)
        {
            if (InstructionsEqual(text, slot.Offset, slot.PatchedInstructions)
                || InstructionsEqual(text, slot.Offset, slot.LegacyPatchedInstructions)
                || InstructionsEqual(text, slot.Offset, PreviousXorYellowValuePaneVisibilityInstructions(slot.LegacyPatchedInstructions.Length))
                || InstructionsEqual(text, slot.Offset, PreviousForcedVisibleYellowValuePaneVisibilityInstructions(slot.LegacyPatchedInstructions.Length)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyBranchLike(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites)
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

    private static bool YellowValuePaneVisibilityIsVanilla(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var slot in layout.YellowValuePaneVisibilitySlots)
        {
            if (!InstructionsEqual(text, slot.Offset, slot.VanillaInstructions))
            {
                return false;
            }
        }

        return true;
    }

    private static uint[] PreviousXorYellowValuePaneVisibilityInstructions(int length)
    {
        var instructions = new uint[length];
        if (instructions.Length > 0)
        {
            instructions[0] = XModePaneVisibilityInstruction;
        }

        if (instructions.Length > 1)
        {
            instructions[1] = VanillaValuePaneVisibilityInvertInstruction;
        }

        for (var index = 2; index < instructions.Length; index++)
        {
            instructions[index] = NopInstruction;
        }

        return instructions;
    }

    private static uint[] PreviousForcedVisibleYellowValuePaneVisibilityInstructions(int length)
    {
        var instructions = new uint[length];
        if (instructions.Length > 0)
        {
            instructions[0] = MovW8OneInstruction;
        }

        for (var index = 1; index < instructions.Length; index++)
        {
            instructions[index] = NopInstruction;
        }

        return instructions;
    }

    private static void WriteNumericTextPaneRefresh(byte[] text, PatchLayout layout)
    {
        foreach (var site in layout.NumericTextPaneRefreshSites)
        {
            WriteInstruction(text, site.Offset, MovW8OneInstruction);
        }
    }

    private static bool AreNumericTextPaneRefreshesWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return AllInstructionsEqual(text, layout.NumericTextPaneRefreshSites, MovW8OneInstruction);
    }

    private static bool AnyNumericTextPaneRefreshWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return AnyInstructionEqual(text, layout.NumericTextPaneRefreshSites, MovW8OneInstruction)
            || AnyInstructionEqual(text, layout.NumericTextPaneRefreshSites, MovW8WzrInstruction);
    }

    private static bool NumericTextPaneRefreshesAreVanilla(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return AllInstructionsVanilla(text, layout.NumericTextPaneRefreshSites);
    }

    private static void WriteXToggleRefresh(byte[] text, PatchLayout layout)
    {
        WriteInstructions(text, layout.XToggleRefreshSlot.Offset, XToggleRefreshPatchedInstructions(layout));
    }

    private static bool IsXToggleRefreshWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return InstructionsEqual(text, layout.XToggleRefreshSlot.Offset, XToggleRefreshPatchedInstructions(layout));
    }

    private static bool XToggleRefreshIsVanilla(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return InstructionsEqual(text, layout.XToggleRefreshSlot.Offset, layout.XToggleRefreshSlot.VanillaInstructions);
    }

    private static uint[] XToggleRefreshPatchedInstructions(PatchLayout layout)
    {
        return
        [
            0xAA1303E0, // mov x0, x19
            EncodeBranchLink(layout.XToggleRefreshSlot.Offset + sizeof(uint), layout.SummaryMultiChartRefreshOffset),
            EncodeBranch(layout.XToggleRefreshSlot.Offset + (2 * sizeof(uint)), layout.XToggleRefreshReturnOffset),
        ];
    }

    private static void WriteXModeValueWrappers(byte[] text, PatchLayout layout)
    {
        WriteXModeValueWrapper(
            text,
            layout.HpCurrentTextWrapperSlotOffset,
            layout.HpCurrentTextRawSlotOffset,
            SummaryTextHpCurrentGetterOffset,
            setHpStatIndex: true);
        WriteXModeValueWrapper(
            text,
            layout.HpMaxTextWrapperSlotOffset,
            layout.HpMaxTextRawSlotOffset,
            SummaryTextHpMaxGetterOffset,
            setHpStatIndex: true);
        WriteXModeValueWrapper(
            text,
            layout.SummaryStatWrapperSlotOffset,
            layout.SummaryStatRawSlotOffset,
            SummaryTextStatGetterOffset,
            setHpStatIndex: false);
        WriteXModeValueWrapper(
            text,
            layout.SummaryGraphAlternateWrapperSlotOffset,
            layout.SummaryGraphAlternateRawSlotOffset,
            SummaryGraphAlternateStatGetterOffset,
            setHpStatIndex: false);
    }

    private static bool AreXModeValueWrappersWritten(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return IsXModeValueWrapperWritten(
                text,
                layout.HpCurrentTextWrapperSlotOffset,
                layout.HpCurrentTextRawSlotOffset,
                SummaryTextHpCurrentGetterOffset,
                setHpStatIndex: true)
            && IsXModeValueWrapperWritten(
                text,
                layout.HpMaxTextWrapperSlotOffset,
                layout.HpMaxTextRawSlotOffset,
                SummaryTextHpMaxGetterOffset,
                setHpStatIndex: true)
            && IsXModeValueWrapperWritten(
                text,
                layout.SummaryStatWrapperSlotOffset,
                layout.SummaryStatRawSlotOffset,
                SummaryTextStatGetterOffset,
                setHpStatIndex: false)
            && IsXModeValueWrapperWritten(
                text,
                layout.SummaryGraphAlternateWrapperSlotOffset,
                layout.SummaryGraphAlternateRawSlotOffset,
                SummaryGraphAlternateStatGetterOffset,
                setHpStatIndex: false);
    }

    private static void WriteXModeValueWrapper(
        byte[] text,
        int wrapperOffset,
        int rawSlotOffset,
        int vanillaTargetOffset,
        bool setHpStatIndex)
    {
        WriteInstructions(
            text,
            wrapperOffset,
            [
                XModePaneVisibilityInstruction,
                EncodeCompareBranchZero32(wrapperOffset + sizeof(uint), rawSlotOffset, 8),
                EncodeBranch(wrapperOffset + (2 * sizeof(uint)), vanillaTargetOffset),
            ]);

        var rawBranchOffset = setHpStatIndex
            ? rawSlotOffset + sizeof(uint)
            : rawSlotOffset;
        WriteInstructions(
            text,
            rawSlotOffset,
            setHpStatIndex
                ? [MovW1WzrInstruction, EncodeBranch(rawBranchOffset, RawIvGetterOffset), NopInstruction]
                : [EncodeBranch(rawBranchOffset, RawIvGetterOffset), NopInstruction, NopInstruction]);
    }

    private static bool IsXModeValueWrapperWritten(
        ReadOnlySpan<byte> text,
        int wrapperOffset,
        int rawSlotOffset,
        int vanillaTargetOffset,
        bool setHpStatIndex)
    {
        if (!InstructionsEqual(
                text,
                wrapperOffset,
                [
                    XModePaneVisibilityInstruction,
                    EncodeCompareBranchZero32(wrapperOffset + sizeof(uint), rawSlotOffset, 8),
                    EncodeBranch(wrapperOffset + (2 * sizeof(uint)), vanillaTargetOffset),
                ]))
        {
            return false;
        }

        var rawBranchOffset = setHpStatIndex
            ? rawSlotOffset + sizeof(uint)
            : rawSlotOffset;
        return InstructionsEqual(
            text,
            rawSlotOffset,
            setHpStatIndex
                ? [MovW1WzrInstruction, EncodeBranch(rawBranchOffset, RawIvGetterOffset), NopInstruction]
                : [EncodeBranch(rawBranchOffset, RawIvGetterOffset), NopInstruction, NopInstruction]);
    }

    private static bool InstructionsEqual(ReadOnlySpan<byte> text, int offset, IReadOnlyList<uint> instructions)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            if (ReadInstruction(text, offset + (index * sizeof(uint))) != instructions[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteMarker(byte[] text, PatchLayout layout)
    {
        foreach (var markerOffset in layout.MarkerFragmentOffsets)
        {
            text.AsSpan(markerOffset, SlotLength).Clear();
        }

        var markerIndex = 0;
        foreach (var markerOffset in layout.MarkerFragmentOffsets)
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

    private static bool HasMarker(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        Span<byte> markerBytes = stackalloc byte[Marker.Length];
        var markerIndex = 0;
        foreach (var markerOffset in layout.MarkerFragmentOffsets)
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

    private static int? FindOccupiedOwnedSlot(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var slotOffset in layout.PayloadSlotOffsets.Concat(layout.MarkerFragmentOffsets))
        {
            EnsureTextRange(text, slotOffset, SlotLength, $"IV Screen slot main.text+0x{slotOffset:X}");
            if (!IsZero(text.Slice(slotOffset, SlotLength)))
            {
                return slotOffset;
            }
        }

        return null;
    }

    private static void EnsureSlotsAvailableOrOwned(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (HasMarker(text, layout))
        {
            return;
        }

        var occupiedSlot = FindOccupiedOwnedSlot(text, layout);
        if (occupiedSlot is not null)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen reserved slot main.text+0x{occupiedSlot.Value:X} is not empty."));
        }
    }

    private static void ClearOwnedSlots(byte[] text, PatchLayout layout)
    {
        foreach (var slotOffset in layout.PayloadSlotOffsets.Concat(layout.MarkerFragmentOffsets))
        {
            EnsureTextRange(text, slotOffset, SlotLength, $"IV Screen slot main.text+0x{slotOffset:X}");
            text.AsSpan(slotOffset, SlotLength).Clear();
        }
    }

    private static void EnsureRequiredRanges(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        foreach (var offset in layout.PayloadSlotOffsets
                     .Concat(layout.MarkerFragmentOffsets)
                     .Concat(layout.NormalGraphValueCallSites.Select(site => site.Offset))
                     .Concat(layout.MultiChartStatSourceCallSites.Select(site => site.Offset))
                     .Concat(layout.MultiChartTextHpValueCallSites.Select(site => site.Offset))
                     .Concat(layout.MultiChartTextStatValueCallSites.Select(site => site.Offset))
                     .Concat(layout.YellowRawValueAddSites.Select(site => site.Offset))
                     .Concat(layout.YellowGraphValueCallSites.Select(site => site.Offset))
                     .Concat(layout.YellowValuePaneVisibilitySlots.Select(slot => slot.Offset))
                     .Concat(layout.NumericTextPaneRefreshSites.Select(site => site.Offset))
                     .Concat(layout.RendererWrapperCallSites.Select(site => site.Offset))
                     .Append(layout.LegacySecondaryStatsHookSiteOffset)
                     .Append(layout.LegacyNormalStatsGraphHookSiteOffset)
                     .Append(layout.XToggleRefreshSlot.Offset)
                     .Append(layout.XYellowStateRequestBranchOffset)
                     .Append(RawIvGetterOffset)
                     .Append(CalculatedStatGetterOffset)
                     .Append(layout.NormalStatsGraphRendererOffset)
                     .Append(layout.SummaryMultiChartRefreshOffset)
                     .Append(layout.ValuePaneVisibilityLoadOffset)
                     .Append(layout.ValuePaneVisibilityMaskOffset))
        {
            EnsureTextRange(text, offset, sizeof(uint), $"IV Screen range main.text+0x{offset:X}");
        }
    }

    private static void EnsureAnchors(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureAnchor(text, RawIvGetterOffset, RawIvGetterEntry, "raw IV getter");
        EnsureAnchor(text, CalculatedStatGetterOffset, CalculatedStatGetterEntry, "calculated stat getter");
        EnsureAnchor(text, layout.NormalStatsGraphRendererOffset, NormalStatsGraphRendererEntry, "normal stats graph renderer");

        if (ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset) != VanillaLegacySecondaryStatsHookInstruction
            && !IsBranchLinkTo(ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset), layout.LegacySecondaryStatsHookSiteOffset, layout.ValueWrapperSlotOffset))
        {
            throw new InvalidDataException("IV Screen install requires a vanilla or IV Screen-owned legacy secondary setup hook.");
        }

        if (ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset) != VanillaLegacyNormalStatsGraphHookInstruction
            && !IsBranchLinkTo(ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset), layout.LegacyNormalStatsGraphHookSiteOffset, layout.ValueWrapperSlotOffset))
        {
            throw new InvalidDataException("IV Screen install requires a vanilla or IV Screen-owned legacy normal graph hook.");
        }

        foreach (var site in layout.NormalGraphValueCallSites)
        {
            EnsureVanillaOrOwnedCall(text, site.Offset, site.VanillaInstruction, "normal graph value source", layout.ValueWrapperSlotOffset);
        }

        foreach (var site in layout.MultiChartStatSourceCallSites)
        {
            EnsureVanillaOrOwnedCall(
                text,
                site.Offset,
                site.VanillaInstruction,
                "summary multi-chart stat source",
                site.WrapperOffset,
                RawIvGetterOffset);
        }

        foreach (var site in layout.MultiChartTextHpValueCallSites)
        {
            EnsureVanillaOrOwnedCall(
                text,
                site.Offset,
                site.VanillaInstruction,
                "summary multi-chart HP text value source",
                site.WrapperOffset,
                layout.LegacyHpIvWrapperSlotOffset);
        }

        foreach (var site in layout.MultiChartTextStatValueCallSites)
        {
            EnsureVanillaOrOwnedCall(
                text,
                site.Offset,
                site.VanillaInstruction,
                "summary multi-chart text value source",
                site.WrapperOffset,
                RawIvGetterOffset);
        }

        foreach (var site in layout.YellowGraphValueCallSites)
        {
            var actual = ReadInstruction(text, site.Offset);
            if (actual != site.VanillaInstruction
                && actual != MovW0WzrInstruction
                && !IsBranchLinkTo(actual, site.Offset, RawIvGetterOffset))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"IV Screen expected vanilla or owned summary X-mode graph value source at main.text+0x{site.Offset:X}, but found 0x{actual:X8}."));
            }
        }

        foreach (var site in layout.RendererWrapperCallSites)
        {
            EnsureVanillaOrOwnedCall(text, site.Offset, site.VanillaInstruction, "summary multi-chart wrapper", layout.RenderWrapperSlotOffset);
        }

        foreach (var site in layout.YellowRawValueAddSites)
        {
            var actual = ReadInstruction(text, site.Offset);
            if (actual != site.VanillaInstruction
                && actual != MovW8W0Instruction
                && actual != MovW8WzrInstruction)
            {
                throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen expected vanilla or owned yellow raw-value instruction at main.text+0x{site.Offset:X}, but found 0x{actual:X8}."));
            }
        }

        foreach (var slot in layout.YellowValuePaneVisibilitySlots)
        {
            if (!InstructionsEqual(text, slot.Offset, slot.VanillaInstructions)
                && !InstructionsEqual(text, slot.Offset, slot.PatchedInstructions)
                && !InstructionsEqual(text, slot.Offset, slot.LegacyPatchedInstructions)
                && !InstructionsEqual(text, slot.Offset, PreviousXorYellowValuePaneVisibilityInstructions(slot.LegacyPatchedInstructions.Length))
                && !InstructionsEqual(text, slot.Offset, PreviousForcedVisibleYellowValuePaneVisibilityInstructions(slot.LegacyPatchedInstructions.Length)))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"IV Screen expected vanilla or owned X-mode value-pane visibility instructions at main.text+0x{slot.Offset:X}."));
            }
        }

        foreach (var site in layout.NumericTextPaneRefreshSites)
        {
            var actual = ReadInstruction(text, site.Offset);
            if (actual != site.VanillaInstruction
                && actual != MovW8OneInstruction
                && actual != MovW8WzrInstruction)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"IV Screen expected vanilla or owned numeric text-pane refresh instruction at main.text+0x{site.Offset:X}, but found 0x{actual:X8}."));
            }
        }

        if (!XToggleRefreshIsVanilla(text, layout) && !IsXToggleRefreshWritten(text, layout))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"IV Screen expected vanilla or owned X-toggle refresh instructions at main.text+0x{layout.XToggleRefreshSlot.Offset:X}."));
        }
    }

    private static void EnsureVanillaOrOwnedCall(
        ReadOnlySpan<byte> text,
        int offset,
        uint vanillaInstruction,
        string label,
        params int[] ownedTargetOffsets)
    {
        var actual = ReadInstruction(text, offset);
        if (actual == vanillaInstruction)
        {
            return;
        }

        foreach (var ownedTargetOffset in ownedTargetOffsets)
        {
            if (IsBranchLinkTo(actual, offset, ownedTargetOffset))
            {
                return;
            }
        }

        throw new InvalidDataException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"IV Screen expected vanilla or owned {label} at main.text+0x{offset:X}, but found 0x{actual:X8}."));
    }

    private static SwShIvScreenAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SwShIvScreenAnalysis(
            SwShIvScreenInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. IV Screen will not patch this file because Sword and Shield use different Pokemon Summary hook sites."),
            buildId,
            FormatTextOffset(layout.ExeFsHookSiteOffset),
            layout.Game);
    }

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
    }

    private static string FormatGame(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "Pokemon Sword",
            ProjectGame.Shield => "Pokemon Shield",
            _ => game.ToString(),
        };
    }

    private static IReadOnlyList<SwShExeFsReservedRegion> CreateReservedRegions(PatchLayout layout)
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerIvScreen)
            .Select(region => new SwShExeFsReservedRegion(
                region.Owner,
                region.FeatureId,
                region.RelativePath,
                region.Area,
                region.StartOffset is null ? null : region.StartOffset.Value + layout.Shift,
                region.Length,
                region.Label,
                region.Rule))
            .ToArray();
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

    private static void WriteInstructions(byte[] text, int offset, IReadOnlyList<uint> instructions)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            WriteInstruction(text, offset + (index * sizeof(uint)), instructions[index]);
        }
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

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        return EncodeBranchLike(0x94000000, sourceOffset, targetOffset);
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        return EncodeBranchLike(0x14000000, sourceOffset, targetOffset);
    }

    private static uint EncodeCompareBranchNonZero32(int sourceOffset, int targetOffset, int register)
    {
        return EncodeCompareBranch32(0x35000000, sourceOffset, targetOffset, register);
    }

    private static uint EncodeCompareBranchZero32(int sourceOffset, int targetOffset, int register)
    {
        return EncodeCompareBranch32(0x34000000, sourceOffset, targetOffset, register);
    }

    private static uint EncodeCompareBranch32(uint opcode, int sourceOffset, int targetOffset, int register)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Compare-branch target must be 4-byte aligned.");
        }

        var imm19 = delta >> 2;
        if (imm19 < -(1 << 18) || imm19 >= (1 << 18))
        {
            throw new InvalidDataException("Compare-branch target is outside ARM64 range.");
        }

        return opcode
            | (uint)((imm19 & 0x7FFFF) << 5)
            | (uint)(register & 0x1F);
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

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int Shift)
    {
        public int LegacySecondaryStatsHookSiteOffset => ShiftOffset(SwShIvScreenMainPatcher.LegacySecondaryStatsHookSiteOffset);
        public int LegacyNormalStatsGraphHookSiteOffset => ShiftOffset(SwShIvScreenMainPatcher.LegacyNormalStatsGraphHookSiteOffset);
        public int ExeFsHookSiteOffset => ShiftOffset(SwShIvScreenMainPatcher.ExeFsHookSiteOffset);
        public int VanillaSecondaryStatsSetupOffset => ShiftOffset(SwShIvScreenMainPatcher.VanillaSecondaryStatsSetupOffset);
        public int SelectedPokemonResolverOffset => ShiftOffset(SwShIvScreenMainPatcher.SelectedPokemonResolverOffset);
        public int NormalStatsGraphRendererOffset => ShiftOffset(SwShIvScreenMainPatcher.NormalStatsGraphRendererOffset);
        public int ValueWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.ValueWrapperSlotOffset);
        public int LegacyHpIvWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.LegacyHpIvWrapperSlotOffset);
        public int RenderWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.RenderWrapperSlotOffset);
        public int XYellowStateRequestBranchOffset => ShiftOffset(SwShIvScreenMainPatcher.XYellowStateRequestBranchOffset);
        public int SummaryMultiChartRefreshOffset => ShiftOffset(SwShIvScreenMainPatcher.SummaryMultiChartRefreshOffset);
        public int HpCurrentTextWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.HpCurrentTextWrapperSlotOffset);
        public int HpCurrentTextRawSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.HpCurrentTextRawSlotOffset);
        public int HpMaxTextWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.HpMaxTextWrapperSlotOffset);
        public int HpMaxTextRawSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.HpMaxTextRawSlotOffset);
        public int SummaryStatWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.SummaryStatWrapperSlotOffset);
        public int SummaryStatRawSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.SummaryStatRawSlotOffset);
        public int SummaryGraphAlternateWrapperSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.SummaryGraphAlternateWrapperSlotOffset);
        public int SummaryGraphAlternateRawSlotOffset => ShiftOffset(SwShIvScreenMainPatcher.SummaryGraphAlternateRawSlotOffset);
        public int ValuePaneVisibilityLoadOffset => ShiftOffset(SwShIvScreenMainPatcher.ValuePaneVisibilityLoadOffset);
        public int ValuePaneVisibilityMaskOffset => ShiftOffset(SwShIvScreenMainPatcher.ValuePaneVisibilityMaskOffset);
        public int XToggleRefreshReturnOffset => ShiftOffset(SwShIvScreenMainPatcher.XToggleRefreshReturnOffset);

        public (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartStatSourceCallSites =>
            ShiftBranchLinkSitesToOriginalTargets(SwShIvScreenMainPatcher.MultiChartStatSourceCallSites);

        public (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartTextHpValueCallSites =>
            ShiftBranchLinkSitesToOriginalTargets(SwShIvScreenMainPatcher.MultiChartTextHpValueCallSites);

        public (int Offset, uint VanillaInstruction, int WrapperOffset)[] MultiChartTextStatValueCallSites =>
            ShiftBranchLinkSitesToOriginalTargets(SwShIvScreenMainPatcher.MultiChartTextStatValueCallSites);

        public (int Offset, uint VanillaInstruction)[] YellowRawValueAddSites =>
            ShiftInstructionSites(SwShIvScreenMainPatcher.YellowRawValueAddSites);

        public (int Offset, uint VanillaInstruction)[] YellowGraphValueCallSites =>
            ShiftBranchLinkSitesToOriginalTargets(SwShIvScreenMainPatcher.YellowGraphValueCallSites);

        public (int Offset, uint[] VanillaInstructions, uint[] PatchedInstructions, uint[] LegacyPatchedInstructions)[] YellowValuePaneVisibilitySlots =>
            SwShIvScreenMainPatcher.YellowValuePaneVisibilitySlots
                .Select(slot => (ShiftOffset(slot.Offset), slot.VanillaInstructions, slot.PatchedInstructions, slot.LegacyPatchedInstructions))
                .ToArray();

        public (int Offset, uint VanillaInstruction)[] NumericTextPaneRefreshSites =>
            ShiftInstructionSites(SwShIvScreenMainPatcher.NumericTextPaneRefreshSites);

        public (int Offset, uint[] VanillaInstructions) XToggleRefreshSlot =>
            (ShiftOffset(SwShIvScreenMainPatcher.XToggleRefreshSlot.Offset), SwShIvScreenMainPatcher.XToggleRefreshSlot.VanillaInstructions);

        public (int Offset, uint VanillaInstruction)[] RendererWrapperCallSites =>
            ShiftBranchLinkSitesToShiftedTargets(SwShIvScreenMainPatcher.RendererWrapperCallSites);

        public (int Offset, uint VanillaInstruction)[] NormalGraphValueCallSites =>
            ShiftBranchLinkSitesToOriginalTargets(SwShIvScreenMainPatcher.NormalGraphValueCallSites);

        public int[] PayloadSlotOffsets =>
            SwShIvScreenMainPatcher.PayloadSlotOffsets.Select(ShiftOffset).ToArray();

        public int[] MarkerFragmentOffsets =>
            SwShIvScreenMainPatcher.MarkerFragmentOffsets.Select(ShiftOffset).ToArray();

        public int ShiftOffset(int offset)
        {
            return offset + Shift;
        }

        private (int Offset, uint VanillaInstruction)[] ShiftInstructionSites((int Offset, uint VanillaInstruction)[] sites)
        {
            return sites
                .Select(site => (ShiftOffset(site.Offset), site.VanillaInstruction))
                .ToArray();
        }

        private (int Offset, uint VanillaInstruction)[] ShiftBranchLinkSitesToOriginalTargets((int Offset, uint VanillaInstruction)[] sites)
        {
            return sites
                .Select(site =>
                {
                    var shiftedOffset = ShiftOffset(site.Offset);
                    var targetOffset = DecodeBranchTarget(site.VanillaInstruction, site.Offset);
                    return (shiftedOffset, EncodeBranchLink(shiftedOffset, targetOffset));
                })
                .ToArray();
        }

        private (int Offset, uint VanillaInstruction, int WrapperOffset)[] ShiftBranchLinkSitesToOriginalTargets(
            (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites)
        {
            return sites
                .Select(site =>
                {
                    var shiftedOffset = ShiftOffset(site.Offset);
                    var targetOffset = DecodeBranchTarget(site.VanillaInstruction, site.Offset);
                    return (shiftedOffset, EncodeBranchLink(shiftedOffset, targetOffset), ShiftOffset(site.WrapperOffset));
                })
                .ToArray();
        }

        private (int Offset, uint VanillaInstruction)[] ShiftBranchLinkSitesToShiftedTargets((int Offset, uint VanillaInstruction)[] sites)
        {
            return sites
                .Select(site =>
                {
                    var shiftedOffset = ShiftOffset(site.Offset);
                    var shiftedTarget = ShiftOffset(DecodeBranchTarget(site.VanillaInstruction, site.Offset));
                    return (shiftedOffset, EncodeBranchLink(shiftedOffset, shiftedTarget));
                })
                .ToArray();
        }
    }
}
