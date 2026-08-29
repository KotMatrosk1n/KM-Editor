// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.ZA.ExeFs;

namespace KM.ZA.RuntimeSettings;

public enum ZaStaticGameplaySettingsFeature
{
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public enum ZaStaticGameplaySettingsMainKind
{
    Vanilla,
    Configured,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

public sealed record ZaStaticGameplaySettingsFeatureAssessment(
    ZaStaticGameplaySettingsFeature Feature,
    bool Available,
    string EffectScope,
    string? UnavailableReason);

public sealed record ZaStaticGameplaySettingsRequest(
    bool ExperienceShareEnabled,
    uint ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap);

public sealed record ZaStaticGameplaySettingsMainAnalysis(
    ZaStaticGameplaySettingsMainKind Kind,
    string Message,
    ProjectGame? DetectedGame,
    string BuildId,
    bool? ExperienceShareEnabled,
    uint? ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap,
    IReadOnlyList<ZaStaticGameplaySettingsFeatureAssessment> Features);

public sealed record ZaRuntimeManagedGameplayMainLayout(
    int ShareCallAOffset,
    uint ShareCallAVanillaInstruction,
    uint ShareCallAHookInstruction,
    int ShareCallBOffset,
    uint ShareCallBVanillaInstruction,
    uint ShareCallBHookInstruction,
    int AwardSiteAOffset,
    int AwardSiteBOffset,
    int AppendOffset,
    int AppendLength);

/// <summary>
/// Applies exact-build, reversible Pokemon Legends Z-A 2.0.2 static gameplay
/// patches. EXP Share, rate, and the bounded level cap affect only the two
/// verified battle-award paths. The level cap does not intercept candies or
/// other experience sources.
/// </summary>
public static class ZaStaticGameplaySettingsMainPatcher
{
    public const int MinimumExperienceRateBasisPoints = 0;
    public const int MaximumExperienceRateBasisPoints = 50_000;
    public const int ExperienceRateStepBasisPoints = 1_000;
    public const int VanillaExperienceRateBasisPoints = 10_000;
    public const byte VanillaLevelCap = 100;

    private const int MaximumRawMainBytes = 64 * 1024 * 1024;
    private const int MaximumDecompressedSegmentBytes = 64 * 1024 * 1024;
    private const int MaximumTotalDecompressedBytes = 96 * 1024 * 1024;
    private const uint ExpectedNsoVersion = 0;
    private const uint ExpectedNsoFlags = 0x3F;

    private const int BaseTextLength = 0x03163F70;
    private const int RoMemoryOffset = 0x03164000;
    private const int RoLength = 0x00A5E000;
    private const int DataMemoryOffset = 0x03BC2000;
    private const int DataLength = 0x003BC430;
    private const int AppendOffset = BaseTextLength;
    private const int AppendLength = 0x90;
    private const int AppendedTextLength = AppendOffset + AppendLength;
    private const int ShareStubOffset = AppendOffset;
    private const int AwardCommonOffset = AppendOffset + 0x18;

    private const int ShareCallAOffset = 0x009A3FF4;
    private const int ShareCallBOffset = 0x00D735C0;
    private const uint VanillaShareCallA = 0x9400029F;
    private const uint VanillaShareCallB = 0x97F0C52C;
    private const int ShareWindowAOffset = 0x009A3FEC;
    private const int ShareWindowALength = 0x20;
    private const int ShareWindowBOffset = 0x00D735B4;
    private const int ShareWindowBLength = 0x24;

    private const int RateSiteAOffset = 0x009A4174;
    private const int RateSiteBOffset = 0x00D73744;
    private const uint VanillaRateLoad = 0xB9401AA8;
    private const uint VanillaRateAdd = 0x0B170108;
    private const uint VanillaRateStore = 0xB9001AA8;
    private const int RateWindowLength = 0x20;
    private const int RateLivenessAOffset = 0x009A4148;
    private const int RateLivenessALength = 0x80;
    private const int RateLivenessBOffset = 0x00D73718;
    private const int RateLivenessBLength = 0x7C;
    private const int RateLivenessPatchDelta = 0x2C;

    private const int SpeciesGetterOffset = 0x00E49940;
    private const int SpeciesGetterWindowLength = 0x01FC;
    private const int FormGetterOffset = 0x00E4D190;
    private const int FormGetterWindowLength = 0x0208;
    private const int CurrentTotalExpGetterOffset = 0x00E49F60;
    private const int CurrentTotalExpGetterWindowLength = 0x0208;
    private const int MinimumTotalExpGetterOffset = 0x00CECB8C;
    private const int MinimumTotalExpGetterWindowLength = 0x0070;

    private const int AwardBuilderOffset = 0x009A4A70;
    private const int AwardBuilderWindowLength = 0x40;

    private const uint NopInstruction = 0xD503201F;
    private const uint RetInstruction = 0xD65F03C0;
    private const uint SaveShareStateInstruction = 0xA9BF0BFE; // STP X30, X2, [SP, #-16]!
    private const uint RestoreShareStateInstruction = 0xA8C10BFE; // LDP X30, X2, [SP], #16

    private const string ExpectedBuildId =
        "B1F12FD919EAE86AB8A978317677E64BCE443D1F000000000000000000000000";
    private const string CleanTextSha256 =
        "94386B941694D1F619EA3680BA4ACBEEAFB65109CE9010FD6EC9A29D92BBFA8E";
    private const string ShareWindowASha256 =
        "3ACDC04B60C940D4A4E18498D26CEEC16766E2DA9EF90BB9A38C1FC545C6AE78";
    private const string ShareWindowBSha256 =
        "2F3575228BB671A3591978B3C83B6C2ADDCD26A942D25B93029C8635C079B51D";
    private const string RateWindowASha256 =
        "CB69FF771EFE70C4E260BD27E729CF50FA5924A5729CD156EBC079C44E3C3BFC";
    private const string RateWindowBSha256 =
        "C280CD8796580480B5AD957C71D95E1B4861FECFE14148747E24A80E0EFD663C";
    private const string RateLivenessASha256 =
        "C316305F61F9C0DE9A65257B9B73925F644EE0CA62DEB9DEDE40D93626F19232";
    private const string RateLivenessBSha256 =
        "D3C42B9BFC98A5C7A6B8CC24896872F8F4766731E0AABBB2C5B786864AD76116";
    private const string AwardBuilderSha256 =
        "BBFCB608EAD996F0229AF7EDC159F0028ED3E52D240F2604C06BEBDA038C88C1";
    private const string SpeciesGetterSha256 =
        "763E6877FB16038E569968A5F0CA30F97F19FF3B3292689C60782A482B16FA92";
    private const string FormGetterSha256 =
        "644CDE5BB5A9798BAAA6BD398C288041DD4E5DFF7915441EB2095B86BBD09785";
    private const string CurrentTotalExpGetterSha256 =
        "263506D8616674E4B0C1BE626C7528A56E0BF21F769836F3E9B3DC81AAE03145";
    private const string MinimumTotalExpGetterSha256 =
        "259C69FC9CE3966F1CFE5D2F10BCDD064FC02CF9FCA57E2B2B07B46F0B01F754";

    private static readonly ZaStaticGameplaySettingsFeatureAssessment[] FeatureInventory =
    [
        new(
            ZaStaticGameplaySettingsFeature.ExperienceShare,
            Available: true,
            "The two verified battle-award builder paths and their retail participant predicates.",
            UnavailableReason: null),
        new(
            ZaStaticGameplaySettingsFeature.ExperienceRate,
            Available: true,
            "The final award accumulation on both verified battle-award paths.",
            UnavailableReason: null),
        new(
            ZaStaticGameplaySettingsFeature.LevelCap,
            Available: true,
            "The final per-recipient EXP contribution on both verified battle-award paths. Candies and other award sources are outside this beta editor's scope.",
            UnavailableReason: null),
    ];

    public static IReadOnlyList<ZaStaticGameplaySettingsFeatureAssessment> Features =>
        FeatureInventory;

    public static ZaStaticGameplaySettingsMainAnalysis Analyze(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);

        try
        {
            var buildId = ReadBoundedBuildId(baseMainBytes, "base");
            if (!string.Equals(buildId, ExpectedBuildId, StringComparison.Ordinal))
            {
                return Failure(
                    ZaStaticGameplaySettingsMainKind.UnsupportedBuild,
                    "Beta Gameplay Settings supports Pokemon Legends Z-A 2.0.2 exefs/main only.",
                    detectedGame: null,
                    buildId);
            }

            if (expectedGame is not null && expectedGame != ProjectGame.ZA)
            {
                return Failure(
                    ZaStaticGameplaySettingsMainKind.GameMismatch,
                    "The selected project is not Pokemon Legends Z-A.",
                    ProjectGame.ZA,
                    buildId);
            }

            var baseNso = ParseBoundedNso(baseMainBytes, "base", allowAppendedText: false);
            var currentNso = ParseBoundedNso(currentMainBytes, "current", allowAppendedText: true);
            ValidateRequiredSegmentHashes(baseNso);
            ValidateRequiredSegmentHashes(currentNso);
            ValidateCleanBase(baseNso);
            EnsureSameExecutableEnvelope(baseNso, currentNso);
            var state = AnalyzeState(currentNso.Text.DecompressedData);
            var kind = state.ShareEnabled
                    && state.RateBasisPoints == VanillaExperienceRateBasisPoints
                    && !state.LevelCapEnabled
                ? ZaStaticGameplaySettingsMainKind.Vanilla
                : ZaStaticGameplaySettingsMainKind.Configured;
            return new ZaStaticGameplaySettingsMainAnalysis(
                kind,
                kind == ZaStaticGameplaySettingsMainKind.Vanilla
                    ? "The verified battle gameplay settings are vanilla."
                    : "The verified battle gameplay settings contain a recognized KM static patch.",
                ProjectGame.ZA,
                buildId,
                state.ShareEnabled,
                state.RateBasisPoints,
                state.LevelCapEnabled,
                state.LevelCap,
                FeatureInventory);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            return Failure(
                ZaStaticGameplaySettingsMainKind.Conflict,
                exception.Message,
                detectedGame: null,
                buildId: "unknown");
        }
    }

    public static byte[] Apply(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        ZaStaticGameplaySettingsRequest request,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var initial = Analyze(baseMainBytes, currentMainBytes, expectedGame);
        if (initial.Kind is ZaStaticGameplaySettingsMainKind.UnsupportedBuild
            or ZaStaticGameplaySettingsMainKind.GameMismatch
            or ZaStaticGameplaySettingsMainKind.Conflict)
        {
            throw new InvalidDataException(initial.Message);
        }

        var baseNso = ParseBoundedNso(baseMainBytes, "base", allowAppendedText: false);
        var currentNso = ParseBoundedNso(currentMainBytes, "current", allowAppendedText: true);
        var baseText = baseNso.Text.DecompressedData;
        var currentText = currentNso.Text.DecompressedData;
        var text = currentText.AsSpan(0, BaseTextLength).ToArray();

        RestoreInstruction(text, baseText, ShareCallAOffset);
        RestoreInstruction(text, baseText, ShareCallBOffset);
        RestoreRateSite(text, baseText, RateSiteAOffset);
        RestoreRateSite(text, baseText, RateSiteBOffset);

        if (!request.ExperienceShareEnabled)
        {
            WriteInstruction(
                text,
                ShareCallAOffset,
                EncodeBranchLink(ShareCallAOffset, ShareStubOffset));
            WriteInstruction(
                text,
                ShareCallBOffset,
                EncodeBranchLink(ShareCallBOffset, ShareStubOffset));
        }

        var hasAwardPolicy = request.ExperienceRateBasisPoints
                != VanillaExperienceRateBasisPoints
            || request.LevelCapEnabled;
        if (hasAwardPolicy)
        {
            WriteAwardSite(text, RateSiteAOffset, request.ExperienceRateBasisPoints);
            WriteAwardSite(text, RateSiteBOffset, request.ExperienceRateBasisPoints);
        }

        var hasPatch = !request.ExperienceShareEnabled
            || hasAwardPolicy;
        byte[] outputText;
        if (hasPatch)
        {
            outputText = new byte[AppendedTextLength];
            text.CopyTo(outputText, 0);
            BuildAppend(
                !request.ExperienceShareEnabled,
                request.ExperienceRateBasisPoints,
                request.LevelCapEnabled,
                request.LevelCap)
                .CopyTo(outputText, AppendOffset);
        }
        else
        {
            outputText = text;
        }

        var output = currentNso.Write(textDecompressedData: outputText);
        ValidateOutput(
            baseMainBytes,
            currentNso,
            output,
            request,
            expectedGame);
        return output;
    }

    /// <summary>
    /// Builds the inert executable scaffold used by KM's native settings rows.
    /// All retail call sites remain untouched, while the exact owned executable
    /// append is reserved for the runtime's preimage-guarded policy templates.
    /// A loader failure therefore leaves retail gameplay behavior intact.
    /// </summary>
    public static (byte[] Main, ZaRuntimeManagedGameplayMainLayout Layout)
        BuildRuntimeManaged(
            byte[] baseMainBytes,
            ProjectGame? expectedGame = ProjectGame.ZA)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        return BuildRuntimeManaged(
            baseMainBytes,
            baseMainBytes,
            expectedGame);
    }

    /// <summary>
    /// Composes the inert native-settings scaffold onto a reviewed executable.
    /// The exact Base executable authorizes the 2.0.2 build and dependency
    /// preimages. Recognized legacy static settings are restored in memory,
    /// while every unrelated current executable byte is preserved.
    /// </summary>
    public static (byte[] Main, ZaRuntimeManagedGameplayMainLayout Layout)
        BuildRuntimeManaged(
            byte[] baseMainBytes,
            byte[] currentMainBytes,
            ProjectGame? expectedGame = ProjectGame.ZA)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        var cleanBase = Analyze(baseMainBytes, baseMainBytes, expectedGame);
        if (cleanBase.Kind != ZaStaticGameplaySettingsMainKind.Vanilla
            || cleanBase.ExperienceShareEnabled != true
            || cleanBase.ExperienceRateBasisPoints != VanillaExperienceRateBasisPoints
            || cleanBase.LevelCapEnabled)
        {
            throw new InvalidDataException(
                "The native settings menu requires the exact clean Pokemon Legends Z-A 2.0.2 base exefs/main.");
        }

        var current = Analyze(baseMainBytes, currentMainBytes, expectedGame);
        // Use the static patcher's reviewed inverse only for a fully recognized
        // legacy output. Every other non-vanilla state remains fail-closed.
        var compositionMainBytes = current.Kind switch
        {
            ZaStaticGameplaySettingsMainKind.Vanilla => currentMainBytes,
            ZaStaticGameplaySettingsMainKind.Configured => RestoreFromBase(
                baseMainBytes,
                currentMainBytes,
                expectedGame),
            _ => throw new InvalidDataException(current.Message),
        };
        var normalizedCurrent = Analyze(
            baseMainBytes,
            compositionMainBytes,
            expectedGame);
        if (normalizedCurrent.Kind != ZaStaticGameplaySettingsMainKind.Vanilla
            || normalizedCurrent.ExperienceShareEnabled != true
            || normalizedCurrent.ExperienceRateBasisPoints != VanillaExperienceRateBasisPoints
            || normalizedCurrent.LevelCapEnabled)
        {
            throw new InvalidDataException(
                "The native settings menu could not safely normalize the recognized legacy Pokemon Legends Z-A static gameplay settings before composition.");
        }

        var baseNso = ParseBoundedNso(baseMainBytes, "base", allowAppendedText: false);
        var nso = ParseBoundedNso(compositionMainBytes, "current", allowAppendedText: false);
        ValidateRequiredSegmentHashes(baseNso);
        ValidateRequiredSegmentHashes(nso);
        EnsureSameExecutableEnvelope(baseNso, nso);
        ValidateRuntimeOwnedRange(ShareCallAOffset, sizeof(uint));
        ValidateRuntimeOwnedRange(ShareCallBOffset, sizeof(uint));
        ValidateRuntimeOwnedRange(RateSiteAOffset, 3 * sizeof(uint));
        ValidateRuntimeOwnedRange(RateSiteBOffset, 3 * sizeof(uint));
        ValidateRuntimeOwnedRange(AppendOffset, AppendLength);
        var text = new byte[AppendedTextLength];
        nso.Text.DecompressedData.CopyTo(text, 0);
        // This complete, unreachable template gives the runtime a single exact
        // preimage on first activation. Retail hooks remain disabled until every
        // requested policy byte has been prepared and can be changed together.
        BuildAppend(
                shareDisabled: true,
                rateBasisPoints: VanillaExperienceRateBasisPoints,
                levelCapEnabled: true,
                levelCap: 99)
            .CopyTo(text, AppendOffset);

        var output = nso.Write(textDecompressedData: text);
        var verified = ParseBoundedNso(output, "runtime-managed", allowAppendedText: true);
        ValidateRequiredSegmentHashes(verified);
        EnsureSameExecutableEnvelope(nso, verified);
        if (!verified.Ro.DecompressedData.AsSpan().SequenceEqual(nso.Ro.DecompressedData)
            || !verified.Data.DecompressedData.AsSpan().SequenceEqual(nso.Data.DecompressedData)
            || !verified.Text.DecompressedData.AsSpan().SequenceEqual(text)
            || !verified.Text.DecompressedData.AsSpan(0, BaseTextLength)
                .SequenceEqual(nso.Text.DecompressedData))
        {
            throw new InvalidDataException(
                "The native Pokemon Legends Z-A settings scaffold failed executable readback.");
        }

        return (
            output,
            new ZaRuntimeManagedGameplayMainLayout(
                ShareCallAOffset,
                VanillaShareCallA,
                EncodeBranchLink(ShareCallAOffset, ShareStubOffset),
                ShareCallBOffset,
                VanillaShareCallB,
                EncodeBranchLink(ShareCallBOffset, ShareStubOffset),
                RateSiteAOffset,
                RateSiteBOffset,
                AppendOffset,
                AppendLength));
    }

    private static void ValidateRuntimeOwnedRange(
        int offset,
        int length)
    {
        var nativeRegions = ZaExeFsReservedRegionLedger
            .MainTextRegionsForOwner(
                ZaExeFsReservedRegionLedger.OwnerNativeGameplayMenu);
        var hasExactReservation = nativeRegions.Any(region =>
            region.StartOffset == offset
            && region.Length == length);
        var overlapsOtherOwner = ZaExeFsReservedRegionLedger.Regions.Any(region =>
            string.Equals(
                region.RelativePath,
                ZaExeFsReservedRegionLedger.ExeFsMainPath,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
            && !string.Equals(
                region.Owner,
                ZaExeFsReservedRegionLedger.OwnerNativeGameplayMenu,
                StringComparison.Ordinal)
            && ZaExeFsReservedRegionLedger.Overlaps(region, offset, length));
        if (!hasExactReservation || overlapsOtherOwner)
        {
            throw new InvalidDataException(
                "The native Pokemon Legends Z-A settings scaffold does not have an exclusive executable ownership reservation.");
        }
    }

    public static byte[] RestoreFromBase(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);

        var current = Analyze(baseMainBytes, currentMainBytes, expectedGame);
        if (current.Kind is not (ZaStaticGameplaySettingsMainKind.Vanilla
            or ZaStaticGameplaySettingsMainKind.Configured)
            || current.ExperienceShareEnabled is null
            || current.ExperienceRateBasisPoints is null)
        {
            throw new InvalidDataException(current.Message);
        }

        var cleanBase = Analyze(baseMainBytes, baseMainBytes, expectedGame);
        if (cleanBase.Kind != ZaStaticGameplaySettingsMainKind.Vanilla)
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings restore requires the exact clean Pokemon Legends Z-A 2.0.2 base exefs/main.");
        }

        var exactOwnedOutput = Apply(
            baseMainBytes,
            baseMainBytes,
            new ZaStaticGameplaySettingsRequest(
                current.ExperienceShareEnabled.Value,
                current.ExperienceRateBasisPoints.Value,
                current.LevelCapEnabled,
                current.LevelCap),
            expectedGame);
        if (ExecutableSegmentsEqual(currentMainBytes, exactOwnedOutput))
        {
            return baseMainBytes.ToArray();
        }

        return Apply(
            baseMainBytes,
            currentMainBytes,
            new ZaStaticGameplaySettingsRequest(
                ExperienceShareEnabled: true,
                VanillaExperienceRateBasisPoints,
                LevelCapEnabled: false,
                VanillaLevelCap),
            expectedGame);
    }

    private static InstalledState AnalyzeState(ReadOnlySpan<byte> currentText)
    {
        if (currentText.Length is not BaseTextLength and not AppendedTextLength)
        {
            throw new InvalidDataException(
                "The current Z-A .text length is neither the clean 2.0.2 length nor the recognized KM appended length.");
        }

        ValidateCurrentTargetWindows(currentText);
        var shareA = AnalyzeShareCall(
            currentText,
            ShareCallAOffset,
            VanillaShareCallA);
        var shareB = AnalyzeShareCall(
            currentText,
            ShareCallBOffset,
            VanillaShareCallB);
        if (shareA != shareB)
        {
            throw new InvalidDataException(
                "The two EXP Share battle paths are only partially patched.");
        }

        var awardA = AnalyzeAwardSite(
            currentText,
            RateSiteAOffset);
        var awardB = AnalyzeAwardSite(
            currentText,
            RateSiteBOffset);
        if (awardA != awardB)
        {
            throw new InvalidDataException(
                "The two battle EXP policy paths are only partially patched or disagree.");
        }

        var rate = awardA.RateBasisPoints;
        var levelCapEnabled = false;
        var levelCap = VanillaLevelCap;
        if (shareA || awardA.Hooked)
        {
            if (currentText.Length != AppendedTextLength)
            {
                throw new InvalidDataException(
                    "A gameplay hook is present without its complete appended program.");
            }

            if (awardA.Hooked)
            {
                var matches = new List<(bool Enabled, byte Cap)>();
                if (rate != VanillaExperienceRateBasisPoints
                    && AppendedProgramMatches(
                        currentText,
                        shareA,
                        rate,
                        levelCapEnabled: false,
                        VanillaLevelCap))
                {
                    matches.Add((false, VanillaLevelCap));
                }

                for (byte candidate = 1; candidate <= VanillaLevelCap; candidate++)
                {
                    if (AppendedProgramMatches(
                            currentText,
                            shareA,
                            rate,
                            levelCapEnabled: true,
                            candidate))
                    {
                        matches.Add((true, candidate));
                    }
                }

                if (matches.Count != 1)
                {
                    throw new InvalidDataException(
                        "The appended battle EXP policy is changed or is not canonical.");
                }

                (levelCapEnabled, levelCap) = matches[0];
                if (levelCapEnabled && levelCap < VanillaLevelCap)
                {
                    ValidateLevelCapHelperWindows(currentText);
                }
            }
            else if (!AppendedProgramMatches(
                         currentText,
                         shareA,
                         VanillaExperienceRateBasisPoints,
                         levelCapEnabled: false,
                         VanillaLevelCap))
            {
                throw new InvalidDataException(
                    "The appended EXP Share program is changed or is not canonical.");
            }
        }
        else if (currentText.Length != BaseTextLength)
        {
            throw new InvalidDataException(
                "An appended gameplay program exists without any recognized hook.");
        }

        return new InstalledState(
            ShareEnabled: !shareA,
            rate,
            levelCapEnabled,
            levelCap);
    }

    private static bool AnalyzeShareCall(
        ReadOnlySpan<byte> currentText,
        int callOffset,
        uint vanillaInstruction)
    {
        var instruction = ReadInstruction(currentText, callOffset);
        if (instruction == vanillaInstruction)
        {
            return false;
        }

        if (instruction != EncodeBranchLink(callOffset, ShareStubOffset))
        {
            throw new InvalidDataException("An EXP Share call contains an unrecognized instruction.");
        }

        return true;
    }

    private static AwardSiteState AnalyzeAwardSite(
        ReadOnlySpan<byte> currentText,
        int siteOffset)
    {
        var first = ReadInstruction(currentText, siteOffset);
        var second = ReadInstruction(currentText, siteOffset + 4);
        var third = ReadInstruction(currentText, siteOffset + 8);
        if (first == VanillaRateLoad
            && second == VanillaRateAdd
            && third == VanillaRateStore)
        {
            return new AwardSiteState(Hooked: false, VanillaExperienceRateBasisPoints);
        }

        uint rate;
        if (first == EncodeMoveRegister32(16, 23))
        {
            rate = VanillaExperienceRateBasisPoints;
        }
        else if (TryDecodeMovzImmediate32(first, register: 16, out var encodedRate)
                 && IsValidRate((uint)encodedRate)
                 && encodedRate != VanillaExperienceRateBasisPoints)
        {
            rate = (uint)encodedRate;
        }
        else
        {
            throw new InvalidDataException(
                "A battle EXP policy site contains an unrecognized rate setup.");
        }

        var expectedSecond = rate is 0 or VanillaExperienceRateBasisPoints
            ? NopInstruction
            : EncodeUmull(16, 23, 16);
        if (second != expectedSecond
            || third != EncodeBranchLink(siteOffset + 8, AwardCommonOffset))
        {
            throw new InvalidDataException(
                "A battle EXP policy site contains an incomplete or changed hook.");
        }

        return new AwardSiteState(Hooked: true, rate);
    }

    private static bool AppendedProgramMatches(
        ReadOnlySpan<byte> text,
        bool shareDisabled,
        uint rateBasisPoints,
        bool levelCapEnabled,
        byte levelCap)
    {
        return text.Slice(AppendOffset, AppendLength).SequenceEqual(
            BuildAppend(shareDisabled, rateBasisPoints, levelCapEnabled, levelCap));
    }

    private static byte[] BuildAppend(
        bool shareDisabled,
        uint rateBasisPoints,
        bool levelCapEnabled,
        byte levelCap)
    {
        var append = new byte[AppendLength];
        for (var offset = 0; offset < append.Length; offset += sizeof(uint))
        {
            WriteInstruction(append, offset, NopInstruction);
        }

        if (shareDisabled)
        {
            WriteShareStub(append);
        }

        if (rateBasisPoints != VanillaExperienceRateBasisPoints || levelCapEnabled)
        {
            WriteAwardCommon(append, rateBasisPoints, levelCapEnabled, levelCap);
        }

        return append;
    }

    private static void WriteShareStub(Span<byte> append)
    {
        var offset = ShareStubOffset - AppendOffset;
        WriteInstruction(append, offset + 0x00, SaveShareStateInstruction);
        WriteInstruction(
            append,
            offset + 0x04,
            EncodeBranchLink(ShareStubOffset + 0x04, AwardBuilderOffset));
        WriteInstruction(append, offset + 0x08, RestoreShareStateInstruction);
        WriteInstruction(append, offset + 0x0C, EncodeCmpImmediate32(2, 0));
        WriteInstruction(
            append,
            offset + 0x10,
            EncodeConditionalSelect32(0, 31, 0, Arm64Condition.EQ));
        WriteInstruction(append, offset + 0x14, RetInstruction);
    }

    private static void WriteAwardSite(Span<byte> text, int siteOffset, uint rateBasisPoints)
    {
        WriteInstruction(
            text,
            siteOffset,
            rateBasisPoints == VanillaExperienceRateBasisPoints
                ? EncodeMoveRegister32(16, 23)
                : EncodeMovzImmediate32(16, (int)rateBasisPoints));
        WriteInstruction(
            text,
            siteOffset + 4,
            rateBasisPoints is 0 or VanillaExperienceRateBasisPoints
                ? NopInstruction
                : EncodeUmull(16, 23, 16));
        WriteInstruction(
            text,
            siteOffset + 8,
            EncodeBranchLink(siteOffset + 8, AwardCommonOffset));
    }

    private static void WriteAwardCommon(
        Span<byte> append,
        uint rateBasisPoints,
        bool levelCapEnabled,
        byte levelCap)
    {
        var offset = AwardCommonOffset - AppendOffset;
        if (levelCapEnabled && levelCap < VanillaLevelCap)
        {
            WriteInstruction(append, offset, EncodeStorePairPreIndex64(30, 22, 31, -32));
            offset += sizeof(uint);
            WriteRateScaleTail(append, ref offset, rateBasisPoints);
            WriteInstruction(append, offset, EncodeMoveRegister64(22, 16));
            offset += sizeof(uint);

            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate64(0, 19, 0x70));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeBranchLink(AppendOffset + offset, SpeciesGetterOffset));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeStoreUnsignedImmediate32(0, 31, 0x10));
            offset += sizeof(uint);

            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate64(0, 19, 0x70));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeBranchLink(AppendOffset + offset, FormGetterOffset));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeMoveRegister32(1, 0));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate32(0, 31, 0x10));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeMovzImmediate32(2, levelCap + 1));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeBranchLink(AppendOffset + offset, MinimumTotalExpGetterOffset));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeSubImmediate32(8, 0, 1));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeStoreUnsignedImmediate32(8, 31, 0x10));
            offset += sizeof(uint);

            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate64(0, 19, 0x70));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeBranchLink(AppendOffset + offset, CurrentTotalExpGetterOffset));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate32(8, 31, 0x10));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeSubsRegister32(8, 8, 0));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeConditionalSelect32(8, 8, 31, Arm64Condition.HI));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeLoadUnsignedImmediate32(9, 21, 0x18));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeAddRegister64(22, 22, 9));
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeCmpRegister64(22, 8));
            offset += sizeof(uint);
            WriteInstruction(
                append,
                offset,
                EncodeConditionalSelect32(8, 22, 8, Arm64Condition.LS));
            offset += sizeof(uint);
            WriteInstruction(append, offset, VanillaRateStore);
            offset += sizeof(uint);
            WriteInstruction(append, offset, EncodeLoadPairPostIndex64(30, 22, 31, 32));
            offset += sizeof(uint);
            WriteInstruction(append, offset, RetInstruction);
            offset += sizeof(uint);
        }
        else
        {
            WriteRateScaleTail(append, ref offset, rateBasisPoints);
            WriteInstruction(
                append,
                offset,
                levelCapEnabled
                    ? EncodeMoveRegister32(16, 16)
                    : NopInstruction);
            offset += sizeof(uint);

            if (rateBasisPoints is 0 or VanillaExperienceRateBasisPoints)
            {
                WriteInstruction(append, offset, VanillaRateLoad);
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeAddRegister32(8, 8, 16));
                offset += sizeof(uint);
                WriteInstruction(append, offset, VanillaRateStore);
                offset += sizeof(uint);
            }
            else
            {
                WriteInstruction(append, offset, EncodeMovzImmediate32(8, 0xFFFF));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeMovkImmediate32(8, 0xFFFF, 16));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeCmpRegister64(16, 8));
                offset += sizeof(uint);
                WriteInstruction(
                    append,
                    offset,
                    EncodeConditionalSelect64(16, 16, 8, Arm64Condition.LS));
                offset += sizeof(uint);
                WriteInstruction(append, offset, VanillaRateLoad);
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeAddRegister64(16, 16, 8));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeMovzImmediate32(0, 0xFFFF));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeMovkImmediate32(0, 0xFFFF, 16));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeCmpRegister64(16, 0));
                offset += sizeof(uint);
                WriteInstruction(
                    append,
                    offset,
                    EncodeConditionalSelect64(16, 16, 0, Arm64Condition.LS));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeStoreUnsignedImmediate32(16, 21, 0x18));
                offset += sizeof(uint);
                WriteInstruction(append, offset, EncodeMoveRegister32(8, 16));
                offset += sizeof(uint);
            }

            WriteInstruction(append, offset, RetInstruction);
            offset += sizeof(uint);
        }

        if (offset > AppendLength)
        {
            throw new InvalidDataException("The bounded battle EXP program exceeds its executable gap.");
        }
    }

    private static void WriteRateScaleTail(
        Span<byte> append,
        ref int offset,
        uint rateBasisPoints)
    {
        if (rateBasisPoints is 0 or VanillaExperienceRateBasisPoints)
        {
            for (var index = 0; index < 5; index++)
            {
                WriteInstruction(append, offset, NopInstruction);
                offset += sizeof(uint);
            }

            return;
        }

        WriteInstruction(append, offset, EncodeMovzImmediate32(8, VanillaExperienceRateBasisPoints));
        offset += sizeof(uint);
        WriteInstruction(append, offset, EncodeUdiv64(16, 16, 8));
        offset += sizeof(uint);
        WriteInstruction(append, offset, EncodeCmpImmediate32(23, 0));
        offset += sizeof(uint);
        WriteInstruction(
            append,
            offset,
            EncodeConditionalCompareImmediate64(16, 0, nzcv: 0, Arm64Condition.NE));
        offset += sizeof(uint);
        WriteInstruction(
            append,
            offset,
            EncodeConditionalIncrement64(16, 16, 31, Arm64Condition.NE));
        offset += sizeof(uint);
    }

    private static void ValidateCleanBase(NsoFile nso)
    {
        if (nso.Text.Header.MemoryOffset != 0
            || nso.Text.DecompressedData.Length != BaseTextLength
            || nso.Ro.Header.MemoryOffset != RoMemoryOffset
            || !string.Equals(
                Convert.ToHexString(SHA256.HashData(nso.Text.DecompressedData)),
                CleanTextSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The base exefs/main is not the exact clean Pokemon Legends Z-A 2.0.2 executable.");
        }
    }

    private static void ValidateCurrentTargetWindows(ReadOnlySpan<byte> text)
    {
        ValidateNormalizedWindow(
            text,
            ShareWindowAOffset,
            ShareWindowALength,
            ShareCallAOffset,
            VanillaShareCallA,
            ShareWindowASha256,
            "EXP Share path A");
        ValidateNormalizedWindow(
            text,
            ShareWindowBOffset,
            ShareWindowBLength,
            ShareCallBOffset,
            VanillaShareCallB,
            ShareWindowBSha256,
            "EXP Share path B");
        ValidateNormalizedRateWindow(
            text,
            RateSiteAOffset,
            RateWindowLength,
            RateSiteAOffset,
            RateWindowASha256,
            "EXP rate path A");
        ValidateNormalizedRateWindow(
            text,
            RateSiteBOffset,
            RateWindowLength,
            RateSiteBOffset,
            RateWindowBSha256,
            "EXP rate path B");
        ValidateNormalizedRateWindow(
            text,
            RateLivenessAOffset,
            RateLivenessALength,
            RateLivenessAOffset + RateLivenessPatchDelta,
            RateLivenessASha256,
            "EXP rate path A liveness window");
        ValidateNormalizedRateWindow(
            text,
            RateLivenessBOffset,
            RateLivenessBLength,
            RateLivenessBOffset + RateLivenessPatchDelta,
            RateLivenessBSha256,
            "EXP rate path B liveness window");
        ValidateHash(
            text,
            AwardBuilderOffset,
            AwardBuilderWindowLength,
            AwardBuilderSha256,
            "retail battle-award builder");
    }

    private static void ValidateLevelCapHelperWindows(ReadOnlySpan<byte> text)
    {
        ValidateHash(
            text,
            SpeciesGetterOffset,
            SpeciesGetterWindowLength,
            SpeciesGetterSha256,
            "battle level-cap species accessor");
        ValidateHash(
            text,
            FormGetterOffset,
            FormGetterWindowLength,
            FormGetterSha256,
            "battle level-cap form accessor");
        ValidateHash(
            text,
            CurrentTotalExpGetterOffset,
            CurrentTotalExpGetterWindowLength,
            CurrentTotalExpGetterSha256,
            "battle level-cap current EXP accessor");
        ValidateHash(
            text,
            MinimumTotalExpGetterOffset,
            MinimumTotalExpGetterWindowLength,
            MinimumTotalExpGetterSha256,
            "battle level-cap minimum EXP calculator");
    }

    private static void ValidateNormalizedWindow(
        ReadOnlySpan<byte> text,
        int offset,
        int length,
        int normalizedOffset,
        uint normalizedInstruction,
        string expectedSha256,
        string label)
    {
        EnsureWindow(text, offset, length);
        var normalized = text.Slice(offset, length).ToArray();
        WriteInstruction(normalized, normalizedOffset - offset, normalizedInstruction);
        var actual = Convert.ToHexString(SHA256.HashData(normalized));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} does not match its exact 2.0.2 context.");
        }
    }

    private static void ValidateNormalizedRateWindow(
        ReadOnlySpan<byte> text,
        int offset,
        int length,
        int normalizedOffset,
        string expectedSha256,
        string label)
    {
        EnsureWindow(text, offset, length);
        var normalized = text.Slice(offset, length).ToArray();
        WriteInstruction(normalized, normalizedOffset - offset, VanillaRateLoad);
        WriteInstruction(normalized, normalizedOffset - offset + 4, VanillaRateAdd);
        WriteInstruction(normalized, normalizedOffset - offset + 8, VanillaRateStore);
        var actual = Convert.ToHexString(SHA256.HashData(normalized));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {label} does not match its exact 2.0.2 context.");
        }
    }

    private static void ValidateHash(
        ReadOnlySpan<byte> text,
        int offset,
        int length,
        string expectedSha256,
        string label)
    {
        EnsureWindow(text, offset, length);
        var actual = Convert.ToHexString(SHA256.HashData(text.Slice(offset, length)));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The {label} does not match its exact 2.0.2 context.");
        }
    }

    private static void ValidateOutput(
        byte[] baseMainBytes,
        NsoFile beforeNso,
        byte[] output,
        ZaStaticGameplaySettingsRequest request,
        ProjectGame? expectedGame)
    {
        var afterNso = ParseBoundedNso(output, "generated", allowAppendedText: true);
        ValidateRequiredSegmentHashes(afterNso);
        EnsureSameExecutableEnvelope(beforeNso, afterNso);
        EnsurePreservedSegment(beforeNso.Ro, afterNso.Ro, ".ro");
        EnsurePreservedSegment(beforeNso.Data, afterNso.Data, ".data");

        var before = beforeNso.Text.DecompressedData.AsSpan(0, BaseTextLength);
        var after = afterNso.Text.DecompressedData.AsSpan(0, BaseTextLength);
        var ranges = new[]
            {
                new OwnedRange(ShareCallAOffset, sizeof(uint)),
                new OwnedRange(ShareCallBOffset, sizeof(uint)),
                new OwnedRange(RateSiteAOffset, 3 * sizeof(uint)),
                new OwnedRange(RateSiteBOffset, 3 * sizeof(uint)),
            }
            .OrderBy(range => range.Offset)
            .ToArray();
        var cursor = 0;
        foreach (var range in ranges)
        {
            if (range.Offset < cursor || range.Offset > BaseTextLength - range.Length)
            {
                throw new InvalidDataException("Gameplay patch ownership ranges overlap or are invalid.");
            }

            if (!before.Slice(cursor, range.Offset - cursor)
                .SequenceEqual(after.Slice(cursor, range.Offset - cursor)))
            {
                throw new InvalidDataException(
                    "Beta Gameplay Settings changed bytes outside its owned base .text ranges.");
            }

            cursor = range.Offset + range.Length;
        }

        if (!before[cursor..].SequenceEqual(after[cursor..]))
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings changed bytes outside its owned base .text ranges.");
        }

        var analysis = Analyze(baseMainBytes, output, expectedGame);
        if (analysis.Kind is ZaStaticGameplaySettingsMainKind.UnsupportedBuild
            or ZaStaticGameplaySettingsMainKind.GameMismatch
            or ZaStaticGameplaySettingsMainKind.Conflict
            || analysis.ExperienceShareEnabled != request.ExperienceShareEnabled
            || analysis.ExperienceRateBasisPoints != request.ExperienceRateBasisPoints
            || analysis.LevelCapEnabled != request.LevelCapEnabled
            || analysis.LevelCap != request.LevelCap)
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings output did not round-trip to the requested supported values.");
        }
    }

    private static void EnsurePreservedSegment(NsoSegment before, NsoSegment after, string label)
    {
        if (!before.DecompressedData.AsSpan().SequenceEqual(after.DecompressedData))
        {
            throw new InvalidDataException($"Beta Gameplay Settings changed the {label} segment.");
        }
    }

    private static void ValidateRequest(ZaStaticGameplaySettingsRequest request)
    {
        if (!IsValidRate(request.ExperienceRateBasisPoints))
        {
            throw new InvalidDataException(
                "The battle EXP rate must be 0 through 500 percent in 10 percent steps.");
        }

        if (request.LevelCapEnabled
            && request.LevelCap is < 1 or > VanillaLevelCap)
        {
            throw new InvalidDataException(
                "The Z-A beta battle level cap must be 1 through 100.");
        }

        if (!request.LevelCapEnabled && request.LevelCap != VanillaLevelCap)
        {
            throw new InvalidDataException(
                "A disabled Z-A level cap must use the canonical vanilla value 100.");
        }
    }

    private static bool IsValidRate(uint rate)
    {
        return rate <= MaximumExperienceRateBasisPoints
            && rate % ExperienceRateStepBasisPoints == 0;
    }

    private static void EnsureSameExecutableEnvelope(NsoFile expected, NsoFile actual)
    {
        if (!NormalizeExecutableEnvelope(expected, normalizeTextLength: true)
                .AsSpan()
                .SequenceEqual(NormalizeExecutableEnvelope(actual, normalizeTextLength: true)))
        {
            throw new InvalidDataException(
                "The base and current exefs/main files do not share the exact executable envelope.");
        }
    }

    private static bool ExecutableSegmentsEqual(byte[] leftBytes, byte[] rightBytes)
    {
        var left = NsoFile.Parse(leftBytes);
        var right = NsoFile.Parse(rightBytes);
        return NormalizeExecutableEnvelope(left, normalizeTextLength: false)
                .AsSpan()
                .SequenceEqual(NormalizeExecutableEnvelope(right, normalizeTextLength: false))
            && left.Text.DecompressedData.SequenceEqual(right.Text.DecompressedData)
            && left.Ro.DecompressedData.SequenceEqual(right.Ro.DecompressedData)
            && left.Data.DecompressedData.SequenceEqual(right.Data.DecompressedData);
    }

    private static byte[] NormalizeExecutableEnvelope(
        NsoFile nso,
        bool normalizeTextLength)
    {
        if (nso.RawHeader.Length != NsoFile.HeaderSize)
        {
            throw new InvalidDataException("The exefs/main NSO header is incomplete.");
        }

        var envelope = nso.RawHeader.ToArray();
        foreach (var offset in new[] { 0x10, 0x20, 0x30 })
        {
            envelope.AsSpan(offset, sizeof(int)).Clear();
        }

        if (normalizeTextLength)
        {
            envelope.AsSpan(0x18, sizeof(int)).Clear();
        }

        envelope.AsSpan(0x60, 3 * sizeof(int)).Clear();
        envelope.AsSpan(0xA0, 3 * 0x20).Clear();
        return envelope;
    }

    private static void ValidateRequiredSegmentHashes(NsoFile nso)
    {
        ValidateSegmentHash(nso.Text, nso.Flags.HasFlag(NsoFlags.CheckHashText), ".text");
        ValidateSegmentHash(nso.Ro, nso.Flags.HasFlag(NsoFlags.CheckHashRo), ".ro");
        ValidateSegmentHash(nso.Data, nso.Flags.HasFlag(NsoFlags.CheckHashData), ".data");
    }

    private static string ReadBoundedBuildId(byte[] mainBytes, string label)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        var bytes = mainBytes.AsSpan();
        if (bytes.Length is < NsoFile.HeaderSize or > MaximumRawMainBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != NsoFile.Magic)
        {
            throw new InvalidDataException(
                $"The {label} exefs/main NSO envelope is invalid or outside the bounded size limit.");
        }

        return Convert.ToHexString(bytes.Slice(0x40, 0x20));
    }

    private static NsoFile ParseBoundedNso(
        byte[] mainBytes,
        string label,
        bool allowAppendedText)
    {
        var buildId = ReadBoundedBuildId(mainBytes, label);
        var bytes = mainBytes.AsSpan();
        if (!string.Equals(buildId, ExpectedBuildId, StringComparison.Ordinal)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x04, sizeof(uint)))
                != ExpectedNsoVersion
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x0C, sizeof(uint)))
                != ExpectedNsoFlags)
        {
            throw new InvalidDataException(
                $"The {label} exefs/main does not match the exact supported executable envelope.");
        }

        var expectedSegments = new[]
        {
            (MemoryOffset: 0, DecompressedSize: BaseTextLength),
            (MemoryOffset: RoMemoryOffset, DecompressedSize: RoLength),
            (MemoryOffset: DataMemoryOffset, DecompressedSize: DataLength),
        };
        long totalDecompressed = 0;
        var priorFileEnd = NsoFile.HeaderSize;
        for (var index = 0; index < 3; index++)
        {
            var headerOffset = 0x10 + index * 0x10;
            var compressedSizeOffset = 0x60 + index * sizeof(uint);
            var fileOffset = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(headerOffset, sizeof(int)));
            var decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(headerOffset + 0x08, sizeof(int)));
            var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(compressedSizeOffset, sizeof(int)));
            var memoryOffset = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(headerOffset + 0x04, sizeof(int)));
            var expectedDecompressedSize = expectedSegments[index].DecompressedSize;
            var sizeMatches = decompressedSize == expectedDecompressedSize
                || (index == 0
                    && allowAppendedText
                    && decompressedSize == AppendedTextLength);
            if (fileOffset < NsoFile.HeaderSize
                || fileOffset < priorFileEnd
                || memoryOffset != expectedSegments[index].MemoryOffset
                || !sizeMatches
                || decompressedSize < 0
                || decompressedSize > MaximumDecompressedSegmentBytes
                || compressedSize < 0
                || fileOffset > bytes.Length
                || compressedSize > bytes.Length - fileOffset)
            {
                throw new InvalidDataException(
                    $"The {label} exefs/main NSO segment envelope is invalid or outside the bounded size limit.");
            }

            priorFileEnd = checked(fileOffset + compressedSize);
            totalDecompressed = checked(totalDecompressed + decompressedSize);
            if (totalDecompressed > MaximumTotalDecompressedBytes)
            {
                throw new InvalidDataException(
                    $"The {label} exefs/main NSO decompressed size exceeds the bounded total limit.");
            }
        }

        return NsoFile.Parse(mainBytes);
    }

    private static void ValidateSegmentHash(NsoSegment segment, bool required, string label)
    {
        if (required && !SHA256.HashData(segment.DecompressedData).AsSpan().SequenceEqual(segment.Hash))
        {
            throw new InvalidDataException($"The exefs/main {label} segment hash is invalid.");
        }
    }

    private static ZaStaticGameplaySettingsMainAnalysis Failure(
        ZaStaticGameplaySettingsMainKind kind,
        string message,
        ProjectGame? detectedGame,
        string buildId)
    {
        return new ZaStaticGameplaySettingsMainAnalysis(
            kind,
            message,
            detectedGame,
            buildId,
            ExperienceShareEnabled: null,
            ExperienceRateBasisPoints: null,
            LevelCapEnabled: false,
            VanillaLevelCap,
            FeatureInventory);
    }

    private static void RestoreInstruction(Span<byte> current, ReadOnlySpan<byte> baseText, int offset)
    {
        baseText.Slice(offset, sizeof(uint)).CopyTo(current.Slice(offset, sizeof(uint)));
    }

    private static void RestoreRateSite(
        Span<byte> current,
        ReadOnlySpan<byte> baseText,
        int offset)
    {
        baseText.Slice(offset, 3 * sizeof(uint))
            .CopyTo(current.Slice(offset, 3 * sizeof(uint)));
    }

    private static void EnsureWindow(ReadOnlySpan<byte> text, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > text.Length - length)
        {
            throw new InvalidDataException("The exefs/main .text segment is truncated.");
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        EnsureWindow(text, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
    }

    private static void WriteInstruction(Span<byte> text, int offset, uint instruction)
    {
        EnsureWindow(text, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(text.Slice(offset, sizeof(uint)), instruction);
    }

    private static uint EncodeBranchLink(int source, int target) =>
        EncodeBranchCore(0x94000000, source, target);

    private static uint EncodeBranchCore(uint opcode, int source, int target)
    {
        var delta = target - source;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("A gameplay branch target is not 4-byte aligned.");
        }

        var immediate = delta >> 2;
        if (immediate < -(1 << 25) || immediate >= 1 << 25)
        {
            throw new InvalidDataException("A gameplay branch target is outside ARM64 range.");
        }

        return opcode | (uint)(immediate & 0x03FFFFFF);
    }

    private static uint EncodeAddRegister64(int destination, int left, int right)
    {
        return 0x8B000000u
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeAddRegister32(int destination, int left, int right)
    {
        return 0x0B000000u
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeMoveRegister32(int destination, int source)
    {
        return 0x2A0003E0u
            | (uint)((source & 0x1F) << 16)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeMoveRegister64(int destination, int source)
    {
        return 0xAA0003E0u
            | (uint)((source & 0x1F) << 16)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeSubImmediate32(int destination, int source, int immediate)
    {
        if (immediate is < 0 or > 0xFFF)
        {
            throw new InvalidDataException("An appended gameplay SUB immediate is out of range.");
        }

        return 0x51000000u
            | (uint)(immediate << 10)
            | (uint)((source & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeSubsRegister32(int destination, int left, int right)
    {
        return 0x6B000000u
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeStorePairPreIndex64(
        int first,
        int second,
        int baseRegister,
        int byteOffset)
    {
        return EncodePair64(
            0xA9800000u,
            first,
            second,
            baseRegister,
            byteOffset);
    }

    private static uint EncodeLoadPairPostIndex64(
        int first,
        int second,
        int baseRegister,
        int byteOffset)
    {
        return EncodePair64(
            0xA8C00000u,
            first,
            second,
            baseRegister,
            byteOffset);
    }

    private static uint EncodePair64(
        uint opcode,
        int first,
        int second,
        int baseRegister,
        int byteOffset)
    {
        if ((byteOffset & 7) != 0 || byteOffset is < -512 or > 504)
        {
            throw new InvalidDataException("An appended gameplay pair offset is out of range.");
        }

        var immediate = (byteOffset / 8) & 0x7F;
        return opcode
            | (uint)(immediate << 15)
            | (uint)((second & 0x1F) << 10)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(first & 0x1F);
    }

    private static uint EncodeLoadUnsignedImmediate64(
        int destination,
        int baseRegister,
        int byteOffset)
    {
        if ((byteOffset & 7) != 0 || byteOffset is < 0 or > 0x7FF8)
        {
            throw new InvalidDataException("An appended gameplay 64-bit load offset is out of range.");
        }

        return 0xF9400000u
            | (uint)((byteOffset / 8) << 10)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeLoadUnsignedImmediate32(
        int destination,
        int baseRegister,
        int byteOffset)
    {
        return EncodeUnsignedImmediate32(
            0xB9400000u,
            destination,
            baseRegister,
            byteOffset);
    }

    private static uint EncodeStoreUnsignedImmediate32(
        int source,
        int baseRegister,
        int byteOffset)
    {
        return EncodeUnsignedImmediate32(
            0xB9000000u,
            source,
            baseRegister,
            byteOffset);
    }

    private static uint EncodeUnsignedImmediate32(
        uint opcode,
        int register,
        int baseRegister,
        int byteOffset)
    {
        if ((byteOffset & 3) != 0 || byteOffset is < 0 or > 0x3FFC)
        {
            throw new InvalidDataException("An appended gameplay 32-bit memory offset is out of range.");
        }

        return opcode
            | (uint)((byteOffset / 4) << 10)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("An appended gameplay MOVZ immediate is out of range.");
        }

        return 0x52800000u | (uint)(immediate << 5) | (uint)(register & 0x1F);
    }

    private static uint EncodeMovkImmediate32(int register, int immediate, int shift)
    {
        if (immediate is < 0 or > 0xFFFF || shift is not (0 or 16))
        {
            throw new InvalidDataException("An appended gameplay MOVK immediate is out of range.");
        }

        return 0x72800000u
            | (uint)((shift / 16) << 21)
            | (uint)(immediate << 5)
            | (uint)(register & 0x1F);
    }

    private static bool TryDecodeMovzImmediate32(uint instruction, int register, out int immediate)
    {
        immediate = 0;
        if ((instruction & 0xFFE0001F) != (0x52800000u | (uint)(register & 0x1F)))
        {
            return false;
        }

        immediate = (int)((instruction >> 5) & 0xFFFF);
        return true;
    }

    private static uint EncodeUmull(int destination64, int left32, int right32)
    {
        return 0x9BA07C00u
            | (uint)((right32 & 0x1F) << 16)
            | (uint)((left32 & 0x1F) << 5)
            | (uint)(destination64 & 0x1F);
    }

    private static uint EncodeUdiv64(int destination, int left, int right)
    {
        return 0x9AC00800u
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeCmpImmediate32(int register, int immediate)
    {
        return 0x7100001Fu
            | (uint)(immediate << 10)
            | (uint)((register & 0x1F) << 5);
    }

    private static uint EncodeCmpRegister64(int left, int right)
    {
        return 0xEB00001Fu
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5);
    }

    private static uint EncodeConditionalCompareImmediate64(
        int register,
        int immediate,
        int nzcv,
        Arm64Condition condition)
    {
        if (immediate is < 0 or > 0x1F || nzcv is < 0 or > 0xF)
        {
            throw new InvalidDataException("An appended gameplay CCMP immediate is out of range.");
        }

        return 0xFA400800u
            | (uint)(immediate << 16)
            | (uint)(((int)condition & 0xF) << 12)
            | (uint)((register & 0x1F) << 5)
            | (uint)nzcv;
    }

    private static uint EncodeConditionalSelect32(
        int destination,
        int trueRegister,
        int falseRegister,
        Arm64Condition condition)
    {
        return 0x1A800000u
            | (uint)((falseRegister & 0x1F) << 16)
            | (uint)(((int)condition & 0xF) << 12)
            | (uint)((trueRegister & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeConditionalSelect64(
        int destination,
        int trueRegister,
        int falseRegister,
        Arm64Condition condition)
    {
        return 0x9A800000u
            | (uint)((falseRegister & 0x1F) << 16)
            | (uint)(((int)condition & 0xF) << 12)
            | (uint)((trueRegister & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeConditionalIncrement64(
        int destination,
        int trueRegister,
        int falseRegister,
        Arm64Condition condition)
    {
        return 0x9A800400u
            | (uint)((falseRegister & 0x1F) << 16)
            | (uint)(((int)condition & 0xF) << 12)
            | (uint)((trueRegister & 0x1F) << 5)
            | (uint)(destination & 0x1F);
    }

    private sealed record InstalledState(
        bool ShareEnabled,
        uint RateBasisPoints,
        bool LevelCapEnabled,
        byte LevelCap);

    private readonly record struct AwardSiteState(
        bool Hooked,
        uint RateBasisPoints);

    private readonly record struct OwnedRange(int Offset, int Length);

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        HI = 8,
        LS = 9,
    }
}
