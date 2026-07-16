// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.SwSh.IvScreen;

internal enum SwShIvScreenInstallKind
{
    NotInstalled,
    NotInstalledDependencyConflict,
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
    string PrimaryValueSourceOffsetHex,
    string XToggleRefreshOffsetHex,
    ProjectGame? DetectedGame);

internal static class SwShIvScreenMainPatcher
{
    public const int LegacySecondaryStatsHookSiteOffset = 0x0137F634;
    public const int LegacyNormalStatsGraphHookSiteOffset = 0x0138F268;
    public const int PrimaryValueSourceOffset = 0x0138A2B4;
    public const int XToggleRefreshOffset = 0x0138B3AC;
    public const int VanillaSecondaryStatsSetupOffset = 0x013872D0;
    public const int SelectedPokemonResolverOffset = 0x01385A70;
    public const int RawIvGetterOffset = 0x00779070;
    public const int HyperTrainingIvWrapperOffset = 0x007790D0;
    public const int ShieldPrimaryValueSourceOffset = PrimaryValueSourceOffset + ShieldOffsetDelta;
    public const int ShieldXToggleRefreshOffset = XToggleRefreshOffset + ShieldOffsetDelta;

    private const int CalculatedStatGetterOffset = 0x00778E20;
    private const int NormalStatsGraphRendererOffset = 0x0138FB60;
    private const int ValueWrapperSlotOffset = 0x0138F324;
    private const int LegacyHpIvWrapperSlotOffset = 0x01390204;
    private const int RenderWrapperSlotOffset = 0x01390204;
    private const int InitialRenderWrapperContinueSlotOffset = 0x01390BE4;
    private const int InitialRenderWrapperReturnSlotOffset = 0x01391114;
    private const int InitialMultiChartRendererOffset = 0x0138A1A0;
    private const int InitialForceValuePaneVisibilityOffset = 0x0138B1E0;
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
    private const uint SummaryTextHpCurrentGetterEntry = 0xF81E0FF3;
    private const uint SummaryTextHpMaxGetterEntry = 0xF81E0FF3;
    private const uint SummaryTextStatGetterEntry = 0x7100143F;
    private const uint SummaryGraphAlternateStatGetterEntry = 0xA9BF7BFD;
    private const uint SummaryMultiChartRefreshEntry = 0xD10503FF;
    private const uint XToggleRefreshReturnEntry = 0xA9457BFD;
    private const uint InitialForceValuePaneVisibilityEntry = 0xD10183FF;
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

    private static readonly (int Offset, uint[] Instructions)[] InitialValueWrapperSlots =
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

    private static readonly int[] InitialPayloadSlotOffsets =
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
        RenderWrapperSlotOffset,
        InitialRenderWrapperContinueSlotOffset,
        InitialRenderWrapperReturnSlotOffset,
    ];

    private static readonly int[] PayloadSlotOffsets =
        SwShExeFsReservedRegionLedger
            .MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerIvScreen)
            .Where(region => region.FeatureId.StartsWith("iv-screen-cave-", StringComparison.Ordinal))
            .Select(region => region.StartOffset!.Value)
            .Order()
            .ToArray();

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
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(nso.BuildId);
            if (layout is null)
            {
                return new SwShIvScreenAnalysis(
                    SwShIvScreenInstallKind.UnsupportedBuild,
                    "IV Screen supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    "unknown",
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

            var hasMarkerPrefix = HasMarkerPrefix(text, layout);
            if (hasMarkerPrefix)
            {
                if (!HasExactMarker(text, layout))
                {
                    return CreateAnalysis(
                        SwShIvScreenInstallKind.Conflict,
                        "IV Screen marker bytes are present, but the complete marker reservation is damaged or contains unowned data.",
                        buildId,
                        layout);
                }

                if (IsInstalledV1(text, layout))
                {
                    var dependencyError = GetDependencyError(text, layout, initialLegacy: false);
                    if (dependencyError is not null)
                    {
                        return CreateAnalysis(
                            SwShIvScreenInstallKind.Conflict,
                            dependencyError,
                            buildId,
                            layout);
                    }

                    return CreateAnalysis(
                        SwShIvScreenInstallKind.InstalledV1,
                        "IV Screen is installed. Reinstalling refreshes the existing raw-IV summary hooks and marker instead of adding a second hook.",
                        buildId,
                        layout);
                }

                if (IsLegacyInstall(text, layout))
                {
                    var dependencyError = GetDependencyError(text, layout, initialLegacy: true);
                    if (dependencyError is not null)
                    {
                        return CreateAnalysis(
                            SwShIvScreenInstallKind.Conflict,
                            dependencyError,
                            buildId,
                            layout);
                    }

                    var migrationDependencyError = GetDependencyError(text, layout, initialLegacy: false);
                    var legacyMessage = migrationDependencyError is null
                        ? "IV Screen is installed with the exact initial Sword hook layout. Reinstall migrates it, while uninstall safely restores only its historical regions."
                        : $"IV Screen is installed with the exact initial Sword hook layout. Uninstall remains available, but migration is unavailable because {migrationDependencyError}";

                    return CreateAnalysis(
                        SwShIvScreenInstallKind.InstalledLegacyV1,
                        legacyMessage,
                        buildId,
                        layout);
                }

                return CreateAnalysis(
                    SwShIvScreenInstallKind.Conflict,
                    "IV Screen marker is present, but the owned Pokemon Summary hook sites do not match a supported IV Screen layout.",
                    buildId,
                    layout);
            }

            if (AllVanilla(text, layout))
            {
                var occupiedSlot = FindOccupiedOwnedSlot(text, layout);
                if (occupiedSlot is not null)
                {
                    return CreateAnalysis(
                        SwShIvScreenInstallKind.Conflict,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"IV Screen reserved slot main.text+0x{occupiedSlot.Value:X} is not empty."),
                        buildId,
                        layout);
                }

                var dependencyError = GetDependencyError(text, layout, initialLegacy: false);
                if (dependencyError is not null)
                {
                    return CreateAnalysis(
                        SwShIvScreenInstallKind.NotInstalledDependencyConflict,
                        $"IV Screen is not installed, but installation is blocked because {dependencyError}",
                        buildId,
                        layout);
                }

                return CreateAnalysis(
                    SwShIvScreenInstallKind.NotInstalled,
                    "IV Screen is not installed. Installing adds independent Pokemon Summary stats and X-mode raw-IV value hooks.",
                    buildId,
                    layout);
            }

            return CreateAnalysis(
                AnyBranchLikeAtOwnedHookSite(text, layout) ? SwShIvScreenInstallKind.ForeignPatch : SwShIvScreenInstallKind.Conflict,
                "Pokemon Summary IV Screen hook sites are already modified and do not contain the IV Screen marker.",
                buildId,
                layout);
        }
        catch (InvalidDataException exception)
        {
            return new SwShIvScreenAnalysis(
                SwShIvScreenInstallKind.Conflict,
                exception.Message,
                "unknown",
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
            or SwShIvScreenInstallKind.NotInstalledDependencyConflict
            or SwShIvScreenInstallKind.GameMismatch
            or SwShIvScreenInstallKind.ForeignPatch
            or SwShIvScreenInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var layout = FindLayout(nso.BuildId)
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

        var output = nso.Write(textDecompressedData: text);
        VerifyApplyOutput(mainBytes, output, layout, expectedGame);
        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(currentNso, baseNso, "IV Screen restore");

        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("IV Screen restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, currentBuildId);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind is not (SwShIvScreenInstallKind.InstalledV1 or SwShIvScreenInstallKind.InstalledLegacyV1))
        {
            throw new InvalidDataException(
                $"IV Screen restore requires an exact current or recognized legacy install: {currentAnalysis.Message}");
        }

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind != SwShIvScreenInstallKind.NotInstalled)
        {
            throw new InvalidDataException(
                $"{layout.GameName} IV Screen restore expected a verified vanilla base main NSO: {baseAnalysis.Message}");
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        var restoredRegions = currentAnalysis.Kind == SwShIvScreenInstallKind.InstalledV1
            ? WrittenRegions(layout)
            : LegacyOwnedRegions(layout);
        foreach (var region in restoredRegions)
        {
            EnsureTextRange(currentText, region.Offset, region.Length, region.Label);
            EnsureTextRange(baseText, region.Offset, region.Length, $"Base {region.Label}");
            baseText.AsSpan(region.Offset, region.Length).CopyTo(currentText.AsSpan(region.Offset, region.Length));
        }

        var output = currentNso.Write(textDecompressedData: currentText);
        VerifyRestoreOutput(
            currentMainBytes,
            baseMainBytes,
            output,
            restoredRegions,
            expectedGame);
        return output;
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

    internal static void EnsureCompatibleExecutableIdentity(
        byte[] baseMainBytes,
        byte[] effectiveMainBytes)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(effectiveMainBytes);

        var baseNso = NsoFile.Parse(baseMainBytes);
        var effectiveNso = NsoFile.Parse(effectiveMainBytes);
        ValidateRequiredSegmentHashes(baseNso);
        ValidateRequiredSegmentHashes(effectiveNso);
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "IV Screen apply");
    }

    internal static string? GetApplyPreflightError(
        byte[] mainBytes,
        ProjectGame? expectedGame)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var analysis = Analyze(mainBytes, expectedGame);
            if (analysis.Kind is not (SwShIvScreenInstallKind.NotInstalled
                or SwShIvScreenInstallKind.InstalledV1
                or SwShIvScreenInstallKind.InstalledLegacyV1))
            {
                return analysis.Message;
            }

            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            var layout = FindLayout(nso.BuildId)
                ?? throw new InvalidDataException("IV Screen supports Sword and Shield 1.3.2 exefs/main files.");
            var text = nso.Text.DecompressedData;
            EnsureRequiredRanges(text, layout);
            EnsureAnchors(text, layout);
            EnsureSlotsAvailableOrOwned(text, layout);
            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
        }
    }

    private static bool IsInstalledV1(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return HasExactMarker(text, layout)
            && ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
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
            && AreXModeValueWrappersWritten(text, layout)
            && InactivePayloadSlotsAreZero(text, layout);
    }

    private static bool IsLegacyInstall(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        return layout.Game == ProjectGame.Sword
            && HasExactMarker(text, layout)
            && ReadInstruction(text, layout.LegacySecondaryStatsHookSiteOffset) == VanillaLegacySecondaryStatsHookInstruction
            && ReadInstruction(text, layout.LegacyNormalStatsGraphHookSiteOffset) == VanillaLegacyNormalStatsGraphHookInstruction
            && AllBranchLinksTo(text, layout.NormalGraphValueCallSites, layout.ValueWrapperSlotOffset)
            && AllBranchLinksTo(text, layout.MultiChartStatSourceCallSites, RawIvGetterOffset)
            && AllInstructionsVanilla(text, layout.MultiChartTextHpValueCallSites)
            && AllInstructionsVanilla(text, layout.MultiChartTextStatValueCallSites)
            && AllInstructionsEqual(text, layout.YellowRawValueAddSites, MovW8W0Instruction)
            && AllInstructionsVanilla(text, layout.YellowGraphValueCallSites)
            && YellowValuePaneVisibilityIsVanilla(text, layout)
            && NumericTextPaneRefreshesAreVanilla(text, layout)
            && XToggleRefreshIsVanilla(text, layout)
            && ReadInstruction(text, layout.ValuePaneVisibilityLoadOffset) == ForceVisibleMovInstruction
            && ReadInstruction(text, layout.ValuePaneVisibilityMaskOffset) == NopInstruction
            && AllBranchLinksTo(text, layout.RendererWrapperCallSites, layout.RenderWrapperSlotOffset)
            && ReadInstruction(text, layout.XYellowStateRequestBranchOffset) == XYellowStateBypassInstruction
            && IsInitialValueWrapperWritten(text)
            && IsInitialRenderWrapperWritten(text)
            && InitialInactivePayloadSlotsAreZero(text, layout);
    }

    private static bool IsInitialValueWrapperWritten(ReadOnlySpan<byte> text)
    {
        foreach (var slot in InitialValueWrapperSlots)
        {
            if (!InstructionsEqual(text, slot.Offset, slot.Instructions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInitialRenderWrapperWritten(ReadOnlySpan<byte> text)
    {
        return ReadInstruction(text, RenderWrapperSlotOffset + 0x00) == 0xA9BF7BF3
            && ReadInstruction(text, RenderWrapperSlotOffset + 0x04) == 0xAA0003F3
            && IsBranchLinkTo(
                ReadInstruction(text, RenderWrapperSlotOffset + 0x08),
                RenderWrapperSlotOffset + 0x08,
                InitialMultiChartRendererOffset)
            && ReadInstruction(text, InitialRenderWrapperContinueSlotOffset + 0x00) == 0xAA1303E0
            && IsBranchLinkTo(
                ReadInstruction(text, InitialRenderWrapperContinueSlotOffset + 0x04),
                InitialRenderWrapperContinueSlotOffset + 0x04,
                InitialForceValuePaneVisibilityOffset)
            && IsBranchTo(
                ReadInstruction(text, InitialRenderWrapperContinueSlotOffset + 0x08),
                InitialRenderWrapperContinueSlotOffset + 0x08,
                InitialRenderWrapperReturnSlotOffset)
            && ReadInstruction(text, InitialRenderWrapperReturnSlotOffset + 0x00) == 0xA8C17BF3
            && ReadInstruction(text, InitialRenderWrapperReturnSlotOffset + 0x04) == 0xD65F03C0
            && ReadInstruction(text, InitialRenderWrapperReturnSlotOffset + 0x08) == NopInstruction;
    }

    private static bool InitialInactivePayloadSlotsAreZero(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        var activeOffsets = InitialPayloadSlotOffsets.ToHashSet();
        foreach (var slotOffset in layout.PayloadSlotOffsets)
        {
            if (!activeOffsets.Contains(slotOffset)
                && !IsZero(text.Slice(slotOffset, SlotLength)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InactivePayloadSlotsAreZero(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        var activeOffsets = new HashSet<int>
        {
            layout.HpCurrentTextWrapperSlotOffset,
            layout.HpCurrentTextRawSlotOffset,
            layout.HpMaxTextWrapperSlotOffset,
            layout.HpMaxTextRawSlotOffset,
            layout.SummaryStatWrapperSlotOffset,
            layout.SummaryStatRawSlotOffset,
            layout.SummaryGraphAlternateWrapperSlotOffset,
            layout.SummaryGraphAlternateRawSlotOffset,
        };

        foreach (var slotOffset in layout.PayloadSlotOffsets)
        {
            if (!activeOffsets.Contains(slotOffset)
                && !IsZero(text.Slice(slotOffset, SlotLength)))
            {
                return false;
            }
        }

        return true;
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

    private static bool HasMarkerPrefix(ReadOnlySpan<byte> text, PatchLayout layout)
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

    private static bool HasExactMarker(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        if (!HasMarkerPrefix(text, layout))
        {
            return false;
        }

        var markerIndex = 0;
        foreach (var markerOffset in layout.MarkerFragmentOffsets)
        {
            var fragmentLength = Math.Min(SlotLength, Marker.Length - markerIndex);
            if (fragmentLength < SlotLength
                && !IsZero(text.Slice(markerOffset + fragmentLength, SlotLength - fragmentLength)))
            {
                return false;
            }

            markerIndex += Math.Max(fragmentLength, 0);
        }

        return true;
    }

    private static bool AllBranchLinksTo(
        ReadOnlySpan<byte> text,
        (int Offset, uint VanillaInstruction, int WrapperOffset)[] sites,
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
        if (HasExactMarker(text, layout))
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
                     .Append(layout.XToggleRefreshReturnOffset)
                     .Append(layout.XYellowStateRequestBranchOffset)
                     .Append(RawIvGetterOffset)
                     .Append(CalculatedStatGetterOffset)
                     .Append(layout.NormalStatsGraphRendererOffset)
                     .Append(SummaryTextHpCurrentGetterOffset)
                     .Append(SummaryTextHpMaxGetterOffset)
                     .Append(SummaryTextStatGetterOffset)
                     .Append(SummaryGraphAlternateStatGetterOffset)
                     .Append(layout.SummaryMultiChartRefreshOffset)
                     .Append(InitialForceValuePaneVisibilityOffset)
                     .Append(layout.ValuePaneVisibilityLoadOffset)
                     .Append(layout.ValuePaneVisibilityMaskOffset))
        {
            EnsureTextRange(text, offset, sizeof(uint), $"IV Screen range main.text+0x{offset:X}");
        }
    }

    private static void EnsureAnchors(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureActiveDependencyAnchors(text, layout);

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

        var valuePaneLoad = ReadInstruction(text, layout.ValuePaneVisibilityLoadOffset);
        var valuePaneMask = ReadInstruction(text, layout.ValuePaneVisibilityMaskOffset);
        var xYellowRequest = ReadInstruction(text, layout.XYellowStateRequestBranchOffset);
        if (valuePaneLoad is not (VanillaValuePaneVisibilityLoadInstruction or ForceVisibleMovInstruction)
            || valuePaneMask is not (VanillaValuePaneVisibilityInvertInstruction or NopInstruction)
            || xYellowRequest is not (VanillaXYellowStateRequestInstruction or XYellowStateBypassInstruction))
        {
            throw new InvalidDataException("IV Screen expected vanilla or recognized legacy Pokemon Summary visibility controls.");
        }
    }

    private static void EnsureActiveDependencyAnchors(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureAnchor(text, RawIvGetterOffset, RawIvGetterEntry, "raw IV getter");
        EnsureAnchor(text, CalculatedStatGetterOffset, CalculatedStatGetterEntry, "calculated stat getter");
        EnsureAnchor(text, layout.NormalStatsGraphRendererOffset, NormalStatsGraphRendererEntry, "normal stats graph renderer");
        EnsureAnchor(text, SummaryTextHpCurrentGetterOffset, SummaryTextHpCurrentGetterEntry, "summary current-HP getter");
        EnsureAnchor(text, SummaryTextHpMaxGetterOffset, SummaryTextHpMaxGetterEntry, "summary max-HP getter");
        EnsureAnchor(text, SummaryTextStatGetterOffset, SummaryTextStatGetterEntry, "summary stat getter");
        EnsureAnchor(text, SummaryGraphAlternateStatGetterOffset, SummaryGraphAlternateStatGetterEntry, "summary alternate-stat getter");
        EnsureAnchor(text, layout.SummaryMultiChartRefreshOffset, SummaryMultiChartRefreshEntry, "summary multi-chart refresh entry");
        EnsureAnchor(text, layout.XToggleRefreshReturnOffset, XToggleRefreshReturnEntry, "X-toggle refresh return");
    }

    private static void EnsureInitialLegacyDependencyAnchors(ReadOnlySpan<byte> text)
    {
        EnsureAnchor(text, CalculatedStatGetterOffset, CalculatedStatGetterEntry, "calculated stat getter");
        EnsureAnchor(text, RawIvGetterOffset, RawIvGetterEntry, "raw IV getter");
        EnsureAnchor(text, InitialMultiChartRendererOffset, SummaryMultiChartRefreshEntry, "initial multi-chart renderer entry");
        EnsureAnchor(
            text,
            InitialForceValuePaneVisibilityOffset,
            InitialForceValuePaneVisibilityEntry,
            "initial value-pane visibility helper");
        EnsureAnchor(text, NormalStatsGraphRendererOffset, NormalStatsGraphRendererEntry, "normal stats graph renderer");
    }

    private static string? GetDependencyError(
        ReadOnlySpan<byte> text,
        PatchLayout layout,
        bool initialLegacy)
    {
        try
        {
            if (initialLegacy)
            {
                EnsureInitialLegacyDependencyAnchors(text);
            }
            else
            {
                EnsureActiveDependencyAnchors(text, layout);
            }

            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
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

    private static void VerifyApplyOutput(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        PatchLayout layout,
        ProjectGame? expectedGame)
    {
        VerifyOutputPreservation(
            sourceMainBytes,
            outputMainBytes,
            WrittenRegions(layout),
            "IV Screen apply");

        var analysis = Analyze(outputMainBytes, expectedGame);
        if (analysis.Kind != SwShIvScreenInstallKind.InstalledV1
            || analysis.DetectedGame != layout.Game)
        {
            throw new InvalidDataException(
                $"IV Screen apply verification did not find the exact current install graph: {analysis.Message}");
        }
    }

    private static void VerifyRestoreOutput(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        byte[] outputMainBytes,
        IReadOnlyList<PatchRegion> restoredRegions,
        ProjectGame? expectedGame)
    {
        VerifyOutputPreservation(
            currentMainBytes,
            outputMainBytes,
            restoredRegions,
            "IV Screen restore");

        var analysis = Analyze(outputMainBytes, expectedGame);
        if (analysis.Kind is not (SwShIvScreenInstallKind.NotInstalled
            or SwShIvScreenInstallKind.NotInstalledDependencyConflict))
        {
            throw new InvalidDataException(
                $"IV Screen restore verification did not return the exact vanilla install surface: {analysis.Message}");
        }

        var baseText = NsoFile.Parse(baseMainBytes).Text.DecompressedData;
        var outputText = NsoFile.Parse(outputMainBytes).Text.DecompressedData;
        foreach (var region in restoredRegions)
        {
            if (!baseText.AsSpan(region.Offset, region.Length)
                .SequenceEqual(outputText.AsSpan(region.Offset, region.Length)))
            {
                throw new InvalidDataException(
                    $"IV Screen restore verification found a mismatched {region.Label}.");
            }
        }
    }

    private static void VerifyOutputPreservation(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        IReadOnlyList<PatchRegion> writtenRegions,
        string operation)
    {
        var source = NsoFile.Parse(sourceMainBytes);
        var output = NsoFile.Parse(outputMainBytes);
        ValidateRequiredSegmentHashes(source);
        ValidateRequiredSegmentHashes(output);
        EnsureSameBuildAndLayout(source, output, operation);
        VerifyPreservedSegment(source.Ro, output.Ro, ".ro", operation);
        VerifyPreservedSegment(source.Data, output.Data, ".data", operation);
        VerifyTextOutsideRegions(
            source.Text.DecompressedData,
            output.Text.DecompressedData,
            writtenRegions,
            operation);
    }

    private static void EnsureSameBuildAndLayout(NsoFile left, NsoFile right, string operation)
    {
        if (left.Version != right.Version
            || left.Flags != right.Flags
            || !left.BuildId.SequenceEqual(right.BuildId))
        {
            throw new InvalidDataException(
                $"{operation} requires matching NSO version, flags, and full 32-byte build identity.");
        }

        if (!SwShExeFsMainComparison.StableHeaderBytesMatch(left.RawHeader, right.RawHeader))
        {
            throw new InvalidDataException($"{operation} requires matching stable NSO header metadata.");
        }

        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            if (leftSegment.Header.MemoryOffset != rightSegment.Header.MemoryOffset
                || leftSegment.Header.DecompressedSize != rightSegment.Header.DecompressedSize
                || leftSegment.DecompressedData.Length != rightSegment.DecompressedData.Length)
            {
                throw new InvalidDataException(
                    $"{operation} requires matching {leftSegment.Name} memory offsets and decompressed sizes.");
            }
        }
    }

    private static void VerifyPreservedSegment(
        NsoSegment source,
        NsoSegment output,
        string segmentName,
        string operation)
    {
        if (source.Header.MemoryOffset != output.Header.MemoryOffset
            || source.Header.DecompressedSize != output.Header.DecompressedSize
            || source.CompressedSize != output.CompressedSize
            || !source.Hash.SequenceEqual(output.Hash)
            || !source.CompressedData.SequenceEqual(output.CompressedData)
            || !source.DecompressedData.SequenceEqual(output.DecompressedData))
        {
            throw new InvalidDataException($"{operation} verification found a changed {segmentName} segment.");
        }
    }

    private static void VerifyTextOutsideRegions(
        ReadOnlySpan<byte> sourceText,
        ReadOnlySpan<byte> outputText,
        IReadOnlyList<PatchRegion> writtenRegions,
        string operation)
    {
        if (sourceText.Length != outputText.Length)
        {
            throw new InvalidDataException($"{operation} verification found a changed .text size.");
        }

        var cursor = 0;
        foreach (var region in writtenRegions.OrderBy(region => region.Offset))
        {
            EnsureTextRange(sourceText, region.Offset, region.Length, region.Label);
            if (region.Offset < cursor)
            {
                throw new InvalidDataException($"{operation} has overlapping written-region definitions.");
            }

            if (!sourceText.Slice(cursor, region.Offset - cursor)
                .SequenceEqual(outputText.Slice(cursor, region.Offset - cursor)))
            {
                throw new InvalidDataException(
                    $"{operation} verification found a change outside IV Screen written ranges before {region.Label}.");
            }

            cursor = region.Offset + region.Length;
        }

        if (!sourceText[cursor..].SequenceEqual(outputText[cursor..]))
        {
            throw new InvalidDataException(
                $"{operation} verification found a change outside IV Screen written ranges after the final region.");
        }
    }

    private static void ValidateRequiredSegmentHashes(NsoFile nso)
    {
        ValidateRequiredSegmentHash(nso.Text, nso.Flags.HasFlag(NsoFlags.CheckHashText));
        ValidateRequiredSegmentHash(nso.Ro, nso.Flags.HasFlag(NsoFlags.CheckHashRo));
        ValidateRequiredSegmentHash(nso.Data, nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static void ValidateRequiredSegmentHash(NsoSegment segment, bool required)
    {
        if (required && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
        {
            throw new InvalidDataException(
                $"IV Screen patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static IReadOnlyList<PatchRegion> WrittenRegions(PatchLayout layout)
    {
        var regions = new List<PatchRegion>();
        regions.AddRange(layout.PayloadSlotOffsets.Select(offset =>
            new PatchRegion(offset, SlotLength, $"IV Screen payload slot main.text+0x{offset:X}")));
        regions.AddRange(layout.MarkerFragmentOffsets.Select(offset =>
            new PatchRegion(offset, SlotLength, $"IV Screen marker slot main.text+0x{offset:X}")));
        regions.Add(new PatchRegion(layout.LegacySecondaryStatsHookSiteOffset, sizeof(uint), "IV Screen legacy secondary hook site"));
        regions.Add(new PatchRegion(layout.LegacyNormalStatsGraphHookSiteOffset, sizeof(uint), "IV Screen legacy normal graph hook site"));
        AddInstructionSites(regions, layout.NormalGraphValueCallSites.Select(site => site.Offset), "normal graph value source");
        AddInstructionSites(regions, layout.MultiChartStatSourceCallSites.Select(site => site.Offset), "multi-chart stat source");
        AddInstructionSites(regions, layout.MultiChartTextHpValueCallSites.Select(site => site.Offset), "multi-chart HP text source");
        AddInstructionSites(regions, layout.MultiChartTextStatValueCallSites.Select(site => site.Offset), "multi-chart stat text source");
        AddInstructionSites(regions, layout.YellowRawValueAddSites.Select(site => site.Offset), "X-mode raw value");
        AddInstructionSites(regions, layout.YellowGraphValueCallSites.Select(site => site.Offset), "X-mode graph value");
        regions.AddRange(layout.YellowValuePaneVisibilitySlots.Select(slot =>
            new PatchRegion(slot.Offset, slot.PatchedInstructions.Length * sizeof(uint), "X-mode pane visibility")));
        AddInstructionSites(regions, layout.NumericTextPaneRefreshSites.Select(site => site.Offset), "numeric pane refresh");
        regions.Add(new PatchRegion(layout.XToggleRefreshSlot.Offset, 3 * sizeof(uint), "X-toggle refresh"));
        AddInstructionSites(regions, layout.RendererWrapperCallSites.Select(site => site.Offset), "legacy renderer wrapper call");
        regions.Add(new PatchRegion(layout.ValuePaneVisibilityLoadOffset, sizeof(uint), "legacy pane visibility load"));
        regions.Add(new PatchRegion(layout.ValuePaneVisibilityMaskOffset, sizeof(uint), "legacy pane visibility mask"));
        regions.Add(new PatchRegion(layout.XYellowStateRequestBranchOffset, sizeof(uint), "legacy X-mode yellow bypass"));
        return NormalizeRegions(regions);
    }

    private static IReadOnlyList<PatchRegion> LegacyOwnedRegions(PatchLayout layout)
    {
        var regions = new List<PatchRegion>();
        regions.AddRange(InitialPayloadSlotOffsets.Select(offset =>
            new PatchRegion(offset, SlotLength, $"Initial IV Screen payload slot main.text+0x{offset:X}")));
        regions.AddRange(layout.MarkerFragmentOffsets.Select(offset =>
            new PatchRegion(offset, SlotLength, $"Initial IV Screen marker slot main.text+0x{offset:X}")));
        regions.Add(new PatchRegion(layout.LegacySecondaryStatsHookSiteOffset, sizeof(uint), "Initial IV Screen secondary hook site"));
        regions.Add(new PatchRegion(layout.LegacyNormalStatsGraphHookSiteOffset, sizeof(uint), "Initial IV Screen normal graph hook site"));
        AddInstructionSites(regions, layout.NormalGraphValueCallSites.Select(site => site.Offset), "initial normal graph source");
        AddInstructionSites(regions, layout.MultiChartStatSourceCallSites.Select(site => site.Offset), "initial multi-chart stat source");
        AddInstructionSites(regions, layout.YellowRawValueAddSites.Select(site => site.Offset), "initial X-mode raw value");
        AddInstructionSites(regions, layout.RendererWrapperCallSites.Select(site => site.Offset), "initial renderer wrapper call");
        regions.Add(new PatchRegion(layout.ValuePaneVisibilityLoadOffset, sizeof(uint), "initial pane visibility load"));
        regions.Add(new PatchRegion(layout.ValuePaneVisibilityMaskOffset, sizeof(uint), "initial pane visibility mask"));
        regions.Add(new PatchRegion(layout.XYellowStateRequestBranchOffset, sizeof(uint), "initial X-mode yellow bypass"));
        return NormalizeRegions(regions);
    }

    private static void AddInstructionSites(
        ICollection<PatchRegion> regions,
        IEnumerable<int> offsets,
        string label)
    {
        foreach (var offset in offsets)
        {
            regions.Add(new PatchRegion(offset, sizeof(uint), $"IV Screen {label} main.text+0x{offset:X}"));
        }
    }

    private static IReadOnlyList<PatchRegion> NormalizeRegions(IEnumerable<PatchRegion> regions)
    {
        var ordered = regions.OrderBy(region => region.Offset).ToArray();
        if (ordered.Length == 0)
        {
            return Array.Empty<PatchRegion>();
        }

        var normalized = new List<PatchRegion>(ordered.Length);
        var current = ordered[0];
        foreach (var next in ordered.Skip(1))
        {
            var currentEnd = current.Offset + current.Length;
            if (next.Offset <= currentEnd)
            {
                current = current with
                {
                    Length = Math.Max(currentEnd, next.Offset + next.Length) - current.Offset,
                    Label = "IV Screen merged owned region",
                };
                continue;
            }

            normalized.Add(current);
            current = next;
        }

        normalized.Add(current);
        return normalized;
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
            FormatTextOffset(layout.PrimaryValueSourceOffset),
            FormatTextOffset(layout.XToggleRefreshSlot.Offset),
            layout.Game);
    }

    private static SwShIvScreenAnalysis CreateAnalysis(
        SwShIvScreenInstallKind kind,
        string message,
        string buildId,
        PatchLayout layout)
    {
        return new SwShIvScreenAnalysis(
            kind,
            message,
            buildId,
            FormatTextOffset(layout.PrimaryValueSourceOffset),
            FormatTextOffset(layout.XToggleRefreshSlot.Offset),
            layout.Game);
    }

    private static PatchLayout? FindLayout(ReadOnlySpan<byte> buildId)
    {
        foreach (var layout in Layouts)
        {
            if (IsCanonicalBuildId(buildId, layout.BuildId))
            {
                return layout;
            }
        }

        return null;
    }

    private static bool IsCanonicalBuildId(ReadOnlySpan<byte> buildId, string expectedPrefixHex)
    {
        const int nsoBuildIdLength = 0x20;
        const int knownBuildIdLength = 0x14;
        if (buildId.Length != nsoBuildIdLength)
        {
            return false;
        }

        var expectedPrefix = Convert.FromHexString(expectedPrefixHex);
        return expectedPrefix.Length == knownBuildIdLength
            && buildId[..knownBuildIdLength].SequenceEqual(expectedPrefix)
            && IsZero(buildId[knownBuildIdLength..]);
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

    private static IReadOnlyList<SwShExeFsReservedRegion> CreateReservedRegions(PatchLayout layout) =>
        SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerIvScreen,
            layout.Game);

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

    private static bool IsBranchTo(uint instruction, int sourceOffset, int targetOffset)
    {
        return (instruction & 0xFC000000) == 0x14000000
            && DecodeBranchTarget(instruction, sourceOffset) == targetOffset;
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

    private sealed record PatchRegion(int Offset, int Length, string Label);

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int Shift)
    {
        public int LegacySecondaryStatsHookSiteOffset => ShiftOffset(SwShIvScreenMainPatcher.LegacySecondaryStatsHookSiteOffset);
        public int LegacyNormalStatsGraphHookSiteOffset => ShiftOffset(SwShIvScreenMainPatcher.LegacyNormalStatsGraphHookSiteOffset);
        public int PrimaryValueSourceOffset => ShiftOffset(SwShIvScreenMainPatcher.PrimaryValueSourceOffset);
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
