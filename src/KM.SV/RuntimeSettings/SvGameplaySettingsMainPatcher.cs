// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using KM.Core.RuntimeSettings;
using KM.Formats.Executable;
using KM.SV.ExeFs;

namespace KM.SV.RuntimeSettings;

public enum SvGameplaySettingsStaticField
{
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public enum SvGameplaySettingsMainKind
{
    Vanilla,
    Modified,
    UnsupportedBuild,
    EditionMismatch,
    Conflict,
}

public sealed record SvGameplaySettingsStaticCapability(
    SvGameplaySettingsStaticField Field,
    bool Available,
    string ReasonCode,
    string Scope);

public sealed record SvGameplaySettingsOwnedTextRegion(
    string FeatureId,
    int StartOffset,
    int Length,
    string Purpose);

public sealed record SvGameplaySettingsMainAnalysis(
    SvGameplaySettingsMainKind Kind,
    string Message,
    SvGameplayRuntimeEdition? DetectedEdition,
    string BuildId,
    GameplaySettingsValues Values,
    bool CanonicalTextIdentityMatches,
    IReadOnlyList<SvGameplaySettingsStaticCapability> Capabilities,
    IReadOnlyList<SvGameplaySettingsOwnedTextRegion> OwnedTextRegions);

public sealed record SvRuntimeManagedGameplayMainLayout(
    int ShareDecisionOffset,
    uint ShareHookInstruction,
    int RateHookOffset,
    uint RateHookInstruction,
    int LevelCapHookOffset,
    uint LevelCapEnabledInstruction,
    int RuntimeSnapshotOffset);

/// <summary>
/// Applies the beta Scarlet/Violet 4.0.0 gameplay controls that have complete,
/// static executable contracts. The patcher never emits a settings sidecar:
/// every available value is encoded into guarded game code in exefs/main.
/// </summary>
public static class SvGameplaySettingsMainPatcher
{
    public const int OriginalTextLength = 0x0343FC90;
    public const int ShareDecisionOffset = 0x01141BA0;
    public const int RateEpilogueOffset = 0x01781E30;
    public const int CapMergeCallOffset = 0x0178143C;
    public const int RateStubOffset = OriginalTextLength;
    public const int RateStubLength = 0x48;
    public const int CapStubOffset = RateStubOffset + RateStubLength;
    public const int CapStubLength = 0x100;
    public const int RuntimeRateStubOffset = OriginalTextLength;
    public const int RuntimeRateStubLength = 0x60;
    public const int RuntimeCapStubOffset = RuntimeRateStubOffset + RuntimeRateStubLength;
    public const int RuntimeCapStubLength = 0x120;
    public const int RuntimeShareStubOffset = RuntimeCapStubOffset + RuntimeCapStubLength;
    public const int RuntimeShareStubLength = 0x30;
    public const int RuntimeSnapshotOffset = 0x047D3000;
    public const int RuntimeBssReservationLength = 0x1000;
    public const uint MaximumExperienceRateBasisPoints = 50_000;
    public const uint ExperienceRateStepBasisPoints = 1_000;

    private const int ShareFunctionOffset = 0x01141B64;
    private const int ShareFunctionLength = 0x110;
    private const int ShareCallerOffset = 0x01141A54;
    private const int RateFunctionOffset = 0x01781CE8;
    private const int RateFunctionLength = 0x26C;
    private const int RateCallerOffset = 0x01781B80;
    private const int CapMergeCallerFunctionOffset = 0x017813A4;
    private const int CapMergeCallerFunctionLength = 0xE8;
    private const int AwardMergeFunctionOffset = 0x0178148C;
    private const int AwardMergeFunctionLength = 0x3B0;
    private const int CurrentExperienceGetterOffset = 0x02D3BC50;
    private const int RecipientGetterLength = 0x08;
    private const int RecipientIdentityUseOffset = 0x01781AE8;
    private const int RecipientIdentityUseLength = 0x34;
    private const int RecipientIdentityInitializationOffset = 0x0147FD38;
    private const int RecipientIdentityInitializationLength = 0x24;
    private const int RecipientFormRefreshOffset = 0x0187184C;
    private const int RecipientFormRefreshLength = 0x108;
    private const int RecipientAlternateFormRefreshOffset = 0x0287A330;
    private const int RecipientAlternateFormRefreshLength = 0x180;
    private const int NormalPartyRecipientBuildOffset = 0x00FA1584;
    private const int NormalPartyRecipientBuildLength = 0x474;
    private const int NormalPartyRecipientFactoryOffset = 0x00FA19F8;
    private const int NormalPartyRecipientFactoryLength = 0x170;
    private const int NormalPartyRecipientAppendOffset = 0x00FA1F30;
    private const int NormalPartyRecipientAppendLength = 0x278;
    private const int NormalPartyRecipientVtableRelocationRoOffset = 0x0064AFB0;
    private const int NormalPartyRecipientVtableRelocationLength = 0x18;
    private const int MinimumExperienceFunctionOffset = 0x00B85FD4;
    private const int MinimumExperienceFunctionLength = 0x10C;
    private const int GrowthGroupFunctionOffset = 0x00B860E0;
    private const int GrowthGroupFunctionLength = 0x1CC;
    private const int OriginalTailFingerprintLength = 0x100;
    private const uint ExpectedNsoVersion = 0;
    private const uint ExpectedNsoFlags = 0x3F;
    private const int ExpectedRoMemoryOffset = 0x03440000;
    private const int ExpectedRoLength = 0x00F420A0;
    private const int ExpectedDataMemoryOffset = 0x04383000;
    private const int ExpectedDataLength = 0x00368868;
    private const int ExpectedTextHeaderAux = 0x100;
    private const int ExpectedRoHeaderAux = 0x1;
    private const int ExpectedDataHeaderAux = 0x000E7798;

    private const uint ShareDecisionVanilla = 0x72001C1F; // tst w0, #0xff
    private const uint ShareDecisionDisabled = 0x72001FFF; // tst wzr, #0xff
    private const uint ShareCallerVanilla = 0x94000044;
    private const uint RateCallerVanilla = 0x9400005A;
    private const uint CapMergeCallerVanilla = 0x94000014;
    private const uint RateEpilogueVanilla = 0xA9427BFD; // ldp x29, x30, [sp, #0x20]
    private const uint MoveRateImmediateBase = 0x52800009; // movz w9, #imm16
    private const uint MoveCapImmediateBase = 0x52800016; // movz w22, #imm16

    private const string ScarletBuildId =
        "421C5411B487EB4D049DD065FEC9547773E8E598000000000000000000000000";
    private const string VioletBuildId =
        "709BFD66115298640155FCC4979DBA151C7CC79A000000000000000000000000";
    private const string ScarletTextSha256 =
        "F48571CECF394151DA2276AC88F31BEBC74E1B77BB5D413D8BC6FEB768EA0C84";
    private const string VioletTextSha256 =
        "7ED23874DC1765429CC43C7E7B13768B04C7F51FA932337DE788D18F8E693F45";
    private const string ShareFunctionSha256 =
        "E9E456DBD5373D2BB48E8992D6085FE6B9BBFBFF152EEE6243DB2EAF64C92484";
    private const string RateFunctionSha256 =
        "1893C43E6E4AE6FD358DEB26BB585A91F6EAE54247B8CF4CC915054514C41D1E";
    private const string OriginalTailSha256 =
        "6AD29D15332799D7B61EFF35D22F449C1151AA73FC43A944D28C7B6C4F8AC86F";
    private const string CapMergeCallerFunctionSha256 =
        "B70FBE3D36675705C5C995DC7AA8469508F2A8691E13C3DCAFABAA347575B119";
    private const string AwardMergeFunctionSha256 =
        "9D987FD8D7E24FDF9DC12A42264E5050BB0A29A7B57528B37CFDC0CF982B8E23";
    private const string CurrentExperienceGetterSha256 =
        "1D508A88EAA79472130512CA701FA0B73BBA28F7BAB8307AAE4939584F8E7BF8";
    private const string RecipientIdentityUseSha256 =
        "CEF0FC75290F7D8EA2BA13605AE9944D3E5EB41A792C7D51C315E67868456610";
    private const string RecipientIdentityInitializationSha256 =
        "FEA230B7DF1117D42DEC391F5908994C06DE6C09EECE85B0CF854E471BF990BA";
    private const string RecipientFormRefreshSha256 =
        "02E6BF8415B1B3C81865CD3259CCDD234C13B2D9CD8D440E8367336C1518F9AC";
    private const string RecipientAlternateFormRefreshSha256 =
        "C7DE4333D7F6B71B22D92281C94E762B8EFCCF4CC594170C22EF4794058BF180";
    private const string NormalPartyRecipientBuildSha256 =
        "332D37BF0D4B4B76CD30C0C226CEAECB2CD22BBD008F17E56EE86F50095835CF";
    private const string NormalPartyRecipientFactorySha256 =
        "A399E6318071276FB4B39CDE74B5B7691FF9F0D3F5C25C35A22E5B47AB4FAFB4";
    private const string NormalPartyRecipientAppendSha256 =
        "0F5C0D612FD747C97386F221C4E26858E842B6E7C219A2C39E2085FAE83F280B";
    private const string NormalPartyRecipientVtableRelocationSha256 =
        "D86E3F427A10BD6683AAC99D5E139A47BFC80D689819250FC40AC910310D3A0F";
    private const string MinimumExperienceFunctionSha256 =
        "76B58530877130A21B8F58442500615D2583E30AFC32EAED346E709CE4BB6CEB";
    private const string GrowthGroupFunctionSha256 =
        "DC35B9410E4C6AF0B73880135EBD540F91FEC950FBEA5CD7A27B4D4E18328E06";

    private static readonly Profile[] Profiles =
    [
        new(
            SvGameplayRuntimeEdition.Scarlet,
            ScarletBuildId,
            Convert.FromHexString(ScarletTextSha256)),
        new(
            SvGameplayRuntimeEdition.Violet,
            VioletBuildId,
            Convert.FromHexString(VioletTextSha256)),
    ];

    private static readonly byte[] ExpectedShareFunctionHash =
        Convert.FromHexString(ShareFunctionSha256);
    private static readonly byte[] ExpectedRateFunctionHash =
        Convert.FromHexString(RateFunctionSha256);
    private static readonly byte[] ExpectedOriginalTailHash =
        Convert.FromHexString(OriginalTailSha256);
    private static readonly byte[] ExpectedCapMergeCallerFunctionHash =
        Convert.FromHexString(CapMergeCallerFunctionSha256);
    private static readonly byte[] ExpectedAwardMergeFunctionHash =
        Convert.FromHexString(AwardMergeFunctionSha256);
    private static readonly byte[] ExpectedCurrentExperienceGetterHash =
        Convert.FromHexString(CurrentExperienceGetterSha256);
    private static readonly byte[] ExpectedRecipientIdentityUseHash =
        Convert.FromHexString(RecipientIdentityUseSha256);
    private static readonly byte[] ExpectedRecipientIdentityInitializationHash =
        Convert.FromHexString(RecipientIdentityInitializationSha256);
    private static readonly byte[] ExpectedRecipientFormRefreshHash =
        Convert.FromHexString(RecipientFormRefreshSha256);
    private static readonly byte[] ExpectedRecipientAlternateFormRefreshHash =
        Convert.FromHexString(RecipientAlternateFormRefreshSha256);
    private static readonly byte[] ExpectedNormalPartyRecipientBuildHash =
        Convert.FromHexString(NormalPartyRecipientBuildSha256);
    private static readonly byte[] ExpectedNormalPartyRecipientFactoryHash =
        Convert.FromHexString(NormalPartyRecipientFactorySha256);
    private static readonly byte[] ExpectedNormalPartyRecipientAppendHash =
        Convert.FromHexString(NormalPartyRecipientAppendSha256);
    private static readonly byte[] ExpectedNormalPartyRecipientVtableRelocationHash =
        Convert.FromHexString(NormalPartyRecipientVtableRelocationSha256);
    private static readonly byte[] ExpectedMinimumExperienceFunctionHash =
        Convert.FromHexString(MinimumExperienceFunctionSha256);
    private static readonly byte[] ExpectedGrowthGroupFunctionHash =
        Convert.FromHexString(GrowthGroupFunctionSha256);

    private static readonly SvGameplaySettingsStaticCapability[] Capabilities =
    [
        new(
            SvGameplaySettingsStaticField.ExperienceShare,
            Available: true,
            "available-static-main-patch",
            "Controls eligible nonparticipants routed through the verified additional-party battle EXP path."),
        new(
            SvGameplaySettingsStaticField.ExperienceRate,
            Available: true,
            "available-static-main-patch",
            "Scales awards returned by the verified normal battle EXP calculator from 0 through 500 percent."),
        new(
            SvGameplaySettingsStaticField.LevelCap,
            Available: true,
            "available-bounded-normal-battle-main-patch",
            "Caps cumulative EXP granted to the local player's standard party by the verified normal-battle award pipeline. Other battle-party variants, candy, items, and nonbattle EXP sources retain retail behavior."),
    ];
    private static readonly IReadOnlyList<SvGameplaySettingsStaticCapability> CapabilitiesView =
        Array.AsReadOnly(Capabilities);

    public static IReadOnlyList<SvGameplaySettingsStaticCapability> GetCapabilities(
        SvGameplayRuntimeEdition edition)
    {
        _ = GetProfile(edition);
        return CapabilitiesView;
    }

    public static IReadOnlyList<SvGameplaySettingsOwnedTextRegion> GetOwnedTextRegions(
        uint experienceRateBasisPoints)
    {
        return GetOwnedTextRegions(
            new GameplaySettingsValues(
                ExperienceShareEnabled: true,
                experienceRateBasisPoints,
                LevelCapEnabled: false,
                LevelCap: GameplaySettingsValues.Vanilla.LevelCap));
    }

    public static IReadOnlyList<SvGameplaySettingsOwnedTextRegion> GetOwnedTextRegions(
        GameplaySettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateRequestedValues(values);
        var regions = new List<SvGameplaySettingsOwnedTextRegion>
        {
            new(
                "sv-beta-experience-share",
                ShareDecisionOffset,
                sizeof(uint),
                "Additional-party EXP Share decision"),
        };
        if (values.ExperienceRateBasisPoints != GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints)
        {
            regions.Add(new(
                "sv-beta-experience-rate-hook",
                RateEpilogueOffset,
                sizeof(uint),
                "Normal battle EXP calculator epilogue branch"));
            regions.Add(new(
                "sv-beta-experience-rate-stub",
                RateStubOffset,
                RateStubLength,
                "Owned normal battle EXP scaling stub"));
        }
        else if (values.LevelCapEnabled)
        {
            regions.Add(new(
                "sv-beta-level-cap-padding",
                RateStubOffset,
                RateStubLength,
                "Canonical alignment padding before the owned level-cap stub"));
        }

        if (values.LevelCapEnabled)
        {
            regions.Add(new(
                "sv-beta-level-cap-hook",
                CapMergeCallOffset,
                sizeof(uint),
                "Normal-battle cumulative award merge call"));
            regions.Add(new(
                "sv-beta-level-cap-stub",
                CapStubOffset,
                CapStubLength,
                "Owned per-recipient cumulative normal-battle EXP cap stub"));
        }

        return regions;
    }

    public static SvGameplaySettingsMainAnalysis Analyze(
        byte[] mainBytes,
        SvGameplayRuntimeEdition expectedEdition)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        _ = GetProfile(expectedEdition);

        if (mainBytes.Length is < NsoFile.HeaderSize
            or > SvGameplayRuntimeProfileValidator.MaximumRawMainBytes)
        {
            return Conflict(
                "Gameplay Settings expected a bounded exefs/main NSO file.");
        }

        try
        {
            if (!TryParseBoundedNso(mainBytes, out var nso))
            {
                return Conflict(
                    "Gameplay Settings rejected an invalid or out-of-bounds exefs/main NSO envelope.");
            }

            if (!DeclaredSegmentHashesMatch(nso))
            {
                return Conflict(
                    "Gameplay Settings rejected exefs/main because a declared NSO segment hash does not match its decompressed data.");
            }

            var buildId = Convert.ToHexString(nso.BuildId);
            var profile = FindProfile(buildId);
            if (profile is null)
            {
                return new SvGameplaySettingsMainAnalysis(
                    SvGameplaySettingsMainKind.UnsupportedBuild,
                    "Gameplay Settings supports exact Scarlet and Violet 4.0.0 exefs/main builds only.",
                    DetectedEdition: null,
                    buildId,
                    GameplaySettingsValues.Vanilla,
                    CanonicalTextIdentityMatches: false,
                    CapabilitiesView,
                    []);
            }

            if (profile.Edition != expectedEdition)
            {
                return new SvGameplaySettingsMainAnalysis(
                    SvGameplaySettingsMainKind.EditionMismatch,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Gameplay Settings expected {FormatEdition(expectedEdition)}, but exefs/main is {FormatEdition(profile.Edition)} 4.0.0."),
                    profile.Edition,
                    buildId,
                    GameplaySettingsValues.Vanilla,
                    CanonicalTextIdentityMatches: false,
                    CapabilitiesView,
                    []);
            }

            var text = nso.Text.DecompressedData;
            EnsureStaticLayout(nso, text);
            EnsureInstruction(text, ShareCallerOffset, ShareCallerVanilla, "EXP Share caller");
            EnsureInstruction(text, RateCallerOffset, RateCallerVanilla, "EXP rate caller");

            var shareInstruction = ReadInstruction(text, ShareDecisionOffset);
            var shareEnabled = shareInstruction switch
            {
                ShareDecisionVanilla => true,
                ShareDecisionDisabled => false,
                _ => throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Gameplay Settings found unrecognized EXP Share bytes at {FormatTextOffset(ShareDecisionOffset)}.")),
            };

            EnsureNormalizedFunctionFingerprint(
                text,
                ShareFunctionOffset,
                ShareFunctionLength,
                ShareDecisionOffset,
                ShareDecisionVanilla,
                ExpectedShareFunctionHash,
                "EXP Share recipient function");

            var patchedSettings = ReadPatchedSettings(text);
            EnsureNormalizedFunctionFingerprint(
                text,
                RateFunctionOffset,
                RateFunctionLength,
                RateEpilogueOffset,
                RateEpilogueVanilla,
                ExpectedRateFunctionHash,
                "normal EXP calculator");
            EnsureNormalizedFunctionFingerprint(
                text,
                CapMergeCallerFunctionOffset,
                CapMergeCallerFunctionLength,
                CapMergeCallOffset,
                CapMergeCallerVanilla,
                ExpectedCapMergeCallerFunctionHash,
                "normal-battle cumulative award caller");
            EnsureExactFunctionFingerprint(
                text,
                AwardMergeFunctionOffset,
                AwardMergeFunctionLength,
                ExpectedAwardMergeFunctionHash,
                "normal-battle award merge function");
            EnsureExactFunctionFingerprint(
                text,
                CurrentExperienceGetterOffset,
                RecipientGetterLength,
                ExpectedCurrentExperienceGetterHash,
                "battle recipient current EXP getter");
            EnsureExactFunctionFingerprint(
                text,
                RecipientIdentityUseOffset,
                RecipientIdentityUseLength,
                ExpectedRecipientIdentityUseHash,
                "normal EXP recipient identity path");
            EnsureExactFunctionFingerprint(
                text,
                RecipientIdentityInitializationOffset,
                RecipientIdentityInitializationLength,
                ExpectedRecipientIdentityInitializationHash,
                "battle recipient identity initialization path");
            EnsureExactFunctionFingerprint(
                text,
                RecipientFormRefreshOffset,
                RecipientFormRefreshLength,
                ExpectedRecipientFormRefreshHash,
                "battle recipient effective-form refresh path");
            EnsureExactFunctionFingerprint(
                text,
                RecipientAlternateFormRefreshOffset,
                RecipientAlternateFormRefreshLength,
                ExpectedRecipientAlternateFormRefreshHash,
                "alternate battle recipient effective-form refresh path");
            EnsureExactFunctionFingerprint(
                text,
                NormalPartyRecipientBuildOffset,
                NormalPartyRecipientBuildLength,
                ExpectedNormalPartyRecipientBuildHash,
                "normal-party recipient build path");
            EnsureExactFunctionFingerprint(
                text,
                NormalPartyRecipientFactoryOffset,
                NormalPartyRecipientFactoryLength,
                ExpectedNormalPartyRecipientFactoryHash,
                "normal-party recipient factory");
            EnsureExactFunctionFingerprint(
                text,
                NormalPartyRecipientAppendOffset,
                NormalPartyRecipientAppendLength,
                ExpectedNormalPartyRecipientAppendHash,
                "normal-party recipient append path");
            EnsureExactSegmentFingerprint(
                nso.Ro.DecompressedData,
                NormalPartyRecipientVtableRelocationRoOffset,
                NormalPartyRecipientVtableRelocationLength,
                ExpectedNormalPartyRecipientVtableRelocationHash,
                "normal-party recipient vtable relocation");
            EnsureExactFunctionFingerprint(
                text,
                MinimumExperienceFunctionOffset,
                MinimumExperienceFunctionLength,
                ExpectedMinimumExperienceFunctionHash,
                "retail minimum cumulative EXP function");
            EnsureExactFunctionFingerprint(
                text,
                GrowthGroupFunctionOffset,
                GrowthGroupFunctionLength,
                ExpectedGrowthGroupFunctionHash,
                "retail species growth-group function");

            var values = new GameplaySettingsValues(
                shareEnabled,
                patchedSettings.ExperienceRateBasisPoints,
                patchedSettings.LevelCapEnabled,
                patchedSettings.LevelCap);
            var canonicalTextMatches = CanonicalTextMatches(text, profile, values);
            var kind = values == GameplaySettingsValues.Vanilla
                ? SvGameplaySettingsMainKind.Vanilla
                : SvGameplaySettingsMainKind.Modified;
            var message = kind == SvGameplaySettingsMainKind.Vanilla
                ? "Gameplay Settings matches the vanilla EXP Share, normal battle EXP rate, and level-cap code."
                : "Gameplay Settings contains recognized KM beta EXP Share, normal battle EXP rate, or bounded level-cap code.";
            return new SvGameplaySettingsMainAnalysis(
                kind,
                message,
                profile.Edition,
                buildId,
                values,
                canonicalTextMatches,
                CapabilitiesView,
                GetOwnedTextRegions(values));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            return Conflict(exception.Message);
        }
    }

    public static byte[] Apply(
        byte[] mainBytes,
        SvGameplayRuntimeEdition expectedEdition,
        GameplaySettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(values);
        ValidateRequestedValues(values);

        var analysis = Analyze(mainBytes, expectedEdition);
        EnsureEditable(analysis);
        if (analysis.Values == values)
        {
            return mainBytes.ToArray();
        }

        var nso = NsoFile.Parse(mainBytes);
        var text = nso.Text.DecompressedData.AsSpan(0, OriginalTextLength).ToArray();
        WriteInstruction(
            text,
            ShareDecisionOffset,
            values.ExperienceShareEnabled
                ? ShareDecisionVanilla
                : ShareDecisionDisabled);
        WriteInstruction(text, RateEpilogueOffset, RateEpilogueVanilla);
        WriteInstruction(text, CapMergeCallOffset, CapMergeCallerVanilla);

        if (values.ExperienceRateBasisPoints != GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints)
        {
            var stub = CreateRateStub(values.ExperienceRateBasisPoints);
            Array.Resize(ref text, checked(OriginalTextLength + stub.Length));
            WriteInstruction(
                text,
                RateEpilogueOffset,
                EncodeUnconditionalBranch(RateEpilogueOffset, RateStubOffset));
            stub.CopyTo(text.AsSpan(RateStubOffset));
        }

        if (values.LevelCapEnabled)
        {
            Array.Resize(
                ref text,
                checked(OriginalTextLength + RateStubLength + CapStubLength));
            var capStub = CreateCapStub(values.LevelCap);
            capStub.CopyTo(text.AsSpan(CapStubOffset));
            WriteInstruction(
                text,
                CapMergeCallOffset,
                EncodeBranchLink(CapMergeCallOffset, CapStubOffset));
        }

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, expectedEdition, values);
        return output;
    }

    /// <summary>
    /// Derives the exact-build executable scaffold used by KM's native settings
    /// menu. The installed image behaves like retail before the guest runtime
    /// reads its journal: EXP Share is enabled, the rate stub is an identity
    /// transform, and the level-cap stub is present but not called.
    /// </summary>
    public static (byte[] Main, SvRuntimeManagedGameplayMainLayout Layout)
        BuildRuntimeManaged(
            byte[] baseMainBytes,
            SvGameplayRuntimeEdition expectedEdition)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        return BuildRuntimeManaged(
            baseMainBytes,
            baseMainBytes,
            expectedEdition);
    }

    /// <summary>
    /// Composes the exact native-settings scaffold onto a reviewed executable.
    /// The clean Base executable authorizes the build and retail preimages;
    /// unrelated current text, rodata, and data bytes are retained verbatim.
    /// </summary>
    public static (byte[] Main, SvRuntimeManagedGameplayMainLayout Layout)
        BuildRuntimeManaged(
            byte[] baseMainBytes,
            byte[] currentMainBytes,
            SvGameplayRuntimeEdition expectedEdition)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        var cleanBase = Analyze(baseMainBytes, expectedEdition);
        if (cleanBase.Kind != SvGameplaySettingsMainKind.Vanilla
            || !cleanBase.CanonicalTextIdentityMatches
            || cleanBase.Values != GameplaySettingsValues.Vanilla)
        {
            throw new InvalidDataException(
                "The native settings menu requires the exact clean selected-game 4.0.0 base exefs/main.");
        }

        var current = Analyze(currentMainBytes, expectedEdition);
        if (current.Kind != SvGameplaySettingsMainKind.Vanilla
            || current.Values != GameplaySettingsValues.Vanilla)
        {
            throw new InvalidDataException(
                "The native settings menu requires the reviewed current Scarlet/Violet executable to retain vanilla static gameplay settings and every verified runtime dependency.");
        }

        var baseNso = NsoFile.Parse(baseMainBytes);
        var nso = NsoFile.Parse(currentMainBytes);
        if (!baseNso.BuildId.AsSpan().SequenceEqual(nso.BuildId)
            || !NormalizeExecutableEnvelope(baseNso)
                .AsSpan()
                .SequenceEqual(NormalizeExecutableEnvelope(nso))
            || nso.Text.DecompressedData.Length != OriginalTextLength
            || BinaryPrimitives.ReadInt32LittleEndian(
                nso.RawHeader.AsSpan(0x3C, sizeof(int))) != ExpectedDataHeaderAux)
        {
            throw new InvalidDataException(
                "The reviewed current Scarlet/Violet executable does not have the exact composable 4.0.0 segment and BSS layout.");
        }

        ValidateRuntimeOwnedRange("main.header", 0x3C, sizeof(int));
        ValidateRuntimeOwnedRange("main.text", ShareDecisionOffset, sizeof(uint));
        ValidateRuntimeOwnedRange("main.text", RateEpilogueOffset, sizeof(uint));
        ValidateRuntimeOwnedRange("main.text", CapMergeCallOffset, sizeof(uint));
        ValidateRuntimeOwnedRange(
            "main.text",
            RuntimeRateStubOffset,
            RuntimeRateStubLength + RuntimeCapStubLength + RuntimeShareStubLength);
        var originalText = nso.Text.DecompressedData;
        var text = new byte[checked(
            OriginalTextLength
                + RuntimeRateStubLength
                + RuntimeCapStubLength
                + RuntimeShareStubLength)];
        originalText.AsSpan(0, OriginalTextLength).CopyTo(text);
        var rateHook = EncodeUnconditionalBranch(RateEpilogueOffset, RuntimeRateStubOffset);
        var capHook = EncodeBranchLink(CapMergeCallOffset, RuntimeCapStubOffset);
        var shareHook = EncodeUnconditionalBranch(ShareDecisionOffset, RuntimeShareStubOffset);
        WriteInstruction(text, ShareDecisionOffset, shareHook);
        WriteInstruction(text, RateEpilogueOffset, rateHook);
        WriteInstruction(text, CapMergeCallOffset, capHook);
        CreateRuntimeRateStub().CopyTo(text.AsSpan(RuntimeRateStubOffset));
        CreateRuntimeCapStub().CopyTo(text.AsSpan(RuntimeCapStubOffset));
        CreateRuntimeShareStub().CopyTo(text.AsSpan(RuntimeShareStubOffset));

        var runtimeHeader = nso.RawHeader.ToArray();
        var originalBssLength = BinaryPrimitives.ReadInt32LittleEndian(
            runtimeHeader.AsSpan(0x3C, sizeof(int)));
        if (originalBssLength != ExpectedDataHeaderAux
            || RuntimeSnapshotOffset
                != checked(ExpectedDataMemoryOffset + ExpectedDataLength + originalBssLength))
        {
            throw new InvalidDataException(
                "The native Scarlet/Violet settings snapshot reservation does not match the exact executable layout.");
        }
        BinaryPrimitives.WriteInt32LittleEndian(
            runtimeHeader.AsSpan(0x3C, sizeof(int)),
            checked(originalBssLength + RuntimeBssReservationLength));
        var runtimeNso = nso with { RawHeader = runtimeHeader };
        var output = runtimeNso.Write(textDecompressedData: text);
        var verified = NsoFile.Parse(output);
        ValidateRuntimeEnvelopeComposition(nso, verified);
        if (!DeclaredSegmentHashesMatch(verified)
            || !verified.BuildId.AsSpan().SequenceEqual(nso.BuildId)
            || !verified.Ro.DecompressedData.AsSpan().SequenceEqual(nso.Ro.DecompressedData)
            || !verified.Data.DecompressedData.AsSpan().SequenceEqual(nso.Data.DecompressedData)
            || verified.Text.DecompressedData.Length != text.Length
            || !verified.Text.DecompressedData.AsSpan().SequenceEqual(text)
            || BinaryPrimitives.ReadInt32LittleEndian(
                verified.RawHeader.AsSpan(0x3C, sizeof(int)))
                != checked(ExpectedDataHeaderAux + RuntimeBssReservationLength))
        {
            throw new InvalidDataException(
                "The native Scarlet/Violet settings scaffold failed executable readback.");
        }

        var normalized = text.AsSpan(0, OriginalTextLength).ToArray();
        WriteInstruction(normalized, ShareDecisionOffset, ShareDecisionVanilla);
        WriteInstruction(normalized, RateEpilogueOffset, RateEpilogueVanilla);
        WriteInstruction(normalized, CapMergeCallOffset, CapMergeCallerVanilla);
        if (!normalized.AsSpan().SequenceEqual(originalText.AsSpan(0, OriginalTextLength)))
        {
            throw new InvalidDataException(
                "The native Scarlet/Violet settings scaffold changed bytes outside its owned runtime hook.");
        }

        return (
            output,
            new SvRuntimeManagedGameplayMainLayout(
                ShareDecisionOffset,
                shareHook,
                RateEpilogueOffset,
                rateHook,
                CapMergeCallOffset,
                capHook,
                RuntimeSnapshotOffset));
    }

    private static void ValidateRuntimeOwnedRange(
        string area,
        int offset,
        int length)
    {
        var nativeRegions = SvExeFsReservedRegionLedger.Regions
            .Where(region => string.Equals(
                    region.RelativePath,
                    SvExeFsReservedRegionLedger.ExeFsMainPath,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, area, StringComparison.Ordinal)
                && string.Equals(
                    region.Owner,
                    SvExeFsReservedRegionLedger.OwnerNativeGameplayMenu,
                    StringComparison.Ordinal))
            .ToArray();
        var hasExactReservation = nativeRegions.Any(region =>
            region.StartOffset == offset
            && region.Length == length);
        var overlapsOtherOwner = SvExeFsReservedRegionLedger.Regions.Any(region =>
            string.Equals(
                region.RelativePath,
                SvExeFsReservedRegionLedger.ExeFsMainPath,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(region.Area, area, StringComparison.Ordinal)
            && !string.Equals(
                region.Owner,
                SvExeFsReservedRegionLedger.OwnerNativeGameplayMenu,
                StringComparison.Ordinal)
            && SvExeFsReservedRegionLedger.Overlaps(region, offset, length));
        if (!hasExactReservation || overlapsOtherOwner)
        {
            throw new InvalidDataException(
                "The native Scarlet/Violet settings scaffold does not have an exclusive executable ownership reservation.");
        }
    }

    private static void ValidateRuntimeEnvelopeComposition(
        NsoFile before,
        NsoFile after)
    {
        var beforeEnvelope = NormalizeExecutableEnvelope(before);
        var afterEnvelope = NormalizeExecutableEnvelope(after);
        foreach (var offset in new[] { 0x18, 0x3C })
        {
            beforeEnvelope.AsSpan(offset, sizeof(int)).Clear();
            afterEnvelope.AsSpan(offset, sizeof(int)).Clear();
        }

        if (!beforeEnvelope.AsSpan().SequenceEqual(afterEnvelope))
        {
            throw new InvalidDataException(
                "The native Scarlet/Violet settings scaffold changed the executable envelope outside its text-length and BSS reservations.");
        }
    }

    public static byte[] RestoreVanilla(
        byte[] currentMainBytes,
        SvGameplayRuntimeEdition expectedEdition)
    {
        return Apply(
            currentMainBytes,
            expectedEdition,
            GameplaySettingsValues.Vanilla);
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        SvGameplayRuntimeEdition expectedEdition)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var current = Analyze(currentMainBytes, expectedEdition);
        EnsureEditable(current);
        var cleanBase = Analyze(baseMainBytes, expectedEdition);
        if (cleanBase.Kind != SvGameplaySettingsMainKind.Vanilla
            || !cleanBase.CanonicalTextIdentityMatches)
        {
            throw new InvalidDataException(
                "Gameplay Settings restore requires the exact clean selected-edition 4.0.0 base exefs/main.");
        }

        if (!string.Equals(current.BuildId, cleanBase.BuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Gameplay Settings restore requires current and base exefs/main files with the same full build ID.");
        }

        var exactOwnedOutput = Apply(
            baseMainBytes,
            expectedEdition,
            current.Values);
        if (ExecutableSegmentsEqual(currentMainBytes, exactOwnedOutput))
        {
            return baseMainBytes.ToArray();
        }

        return RestoreVanilla(currentMainBytes, expectedEdition);
    }

    private static bool ExecutableSegmentsEqual(byte[] leftBytes, byte[] rightBytes)
    {
        var left = NsoFile.Parse(leftBytes);
        var right = NsoFile.Parse(rightBytes);
        return NormalizeExecutableEnvelope(left)
                .AsSpan()
                .SequenceEqual(NormalizeExecutableEnvelope(right))
            && left.Text.DecompressedData.SequenceEqual(right.Text.DecompressedData)
            && left.Ro.DecompressedData.SequenceEqual(right.Ro.DecompressedData)
            && left.Data.DecompressedData.SequenceEqual(right.Data.DecompressedData);
    }

    private static byte[] NormalizeExecutableEnvelope(NsoFile nso)
    {
        if (nso.RawHeader.Length != NsoFile.HeaderSize)
        {
            throw new InvalidDataException("The exefs/main NSO header is incomplete.");
        }

        var envelope = nso.RawHeader.ToArray();
        // File offsets, compressed sizes, and segment hashes are serialization-derived.
        // Every other header byte must remain identical before an exact base replacement.
        foreach (var offset in new[] { 0x10, 0x20, 0x30 })
        {
            envelope.AsSpan(offset, sizeof(int)).Clear();
        }

        envelope.AsSpan(0x60, 3 * sizeof(int)).Clear();
        envelope.AsSpan(0xA0, 3 * 0x20).Clear();
        return envelope;
    }

    private static PatchedGameplayCodeSettings ReadPatchedSettings(ReadOnlySpan<byte> text)
    {
        var rateHook = ReadInstruction(text, RateEpilogueOffset);
        var expectedRateHook = EncodeUnconditionalBranch(RateEpilogueOffset, RateStubOffset);
        var rateInstalled = rateHook == expectedRateHook;
        if (!rateInstalled && rateHook != RateEpilogueVanilla)
        {
            throw new InvalidDataException(
                $"Gameplay Settings found unrecognized EXP rate bytes at {FormatTextOffset(RateEpilogueOffset)}.");
        }

        var capHook = ReadInstruction(text, CapMergeCallOffset);
        var expectedCapHook = EncodeBranchLink(CapMergeCallOffset, CapStubOffset);
        var capInstalled = capHook == expectedCapHook;
        if (!capInstalled && capHook != CapMergeCallerVanilla)
        {
            throw new InvalidDataException(
                $"Gameplay Settings found unrecognized level-cap bytes at {FormatTextOffset(CapMergeCallOffset)}.");
        }

        var rateOnlyLength = checked(OriginalTextLength + RateStubLength);
        var capLength = checked(rateOnlyLength + CapStubLength);
        if (text.Length == OriginalTextLength)
        {
            if (rateInstalled || capInstalled)
            {
                throw new InvalidDataException(
                    "Gameplay Settings found a beta gameplay branch without its complete owned executable payload.");
            }
        }
        else if (text.Length == rateOnlyLength)
        {
            if (!rateInstalled || capInstalled)
            {
                throw new InvalidDataException(
                    "Gameplay Settings found an inconsistent EXP-rate payload or level-cap branch.");
            }
        }
        else if (text.Length == capLength)
        {
            if (!capInstalled)
            {
                throw new InvalidDataException(
                    "Gameplay Settings found an appended level-cap payload without its exact owned branch.");
            }
        }
        else
        {
            throw new InvalidDataException(
                "Gameplay Settings found an unsupported decompressed .text size or a foreign appended executable payload.");
        }

        var rate = GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints;
        if (rateInstalled)
        {
            var encodedRate = ReadInstruction(text, RateStubOffset + 0x08);
            if ((encodedRate & 0xFFE0001F) != MoveRateImmediateBase)
            {
                throw new InvalidDataException(
                    "Gameplay Settings found an invalid rate immediate in the owned EXP scaling stub.");
            }

            rate = (encodedRate >> 5) & 0xFFFF;
            ValidateRate(rate);
            var expectedStub = CreateRateStub(rate);
            if (!text.Slice(RateStubOffset, expectedStub.Length).SequenceEqual(expectedStub))
            {
                throw new InvalidDataException(
                    "Gameplay Settings found modified or incomplete bytes in the owned EXP scaling stub.");
            }
        }
        else if (capInstalled
            && !IsZeroFilled(text.Slice(RateStubOffset, RateStubLength)))
        {
            throw new InvalidDataException(
                "Gameplay Settings found foreign bytes in the owned level-cap alignment padding.");
        }

        if (!capInstalled)
        {
            return new PatchedGameplayCodeSettings(
                rate,
                LevelCapEnabled: false,
                LevelCap: GameplaySettingsValues.Vanilla.LevelCap);
        }

        var encodedCap = ReadInstruction(text, CapStubOffset + 0x28);
        if ((encodedCap & 0xFFE0001F) != MoveCapImmediateBase)
        {
            throw new InvalidDataException(
                "Gameplay Settings found an invalid level immediate in the owned level-cap stub.");
        }

        var cap = checked((byte)((encodedCap >> 5) & 0xFFFF));
        ValidateCap(cap);
        var expectedCapStub = CreateCapStub(cap);
        if (!text.Slice(CapStubOffset, expectedCapStub.Length).SequenceEqual(expectedCapStub))
        {
            throw new InvalidDataException(
                "Gameplay Settings found modified or incomplete bytes in the owned level-cap stub.");
        }

        return new PatchedGameplayCodeSettings(rate, LevelCapEnabled: true, cap);
    }

    private static byte[] CreateRateStub(uint rate)
    {
        ValidateRate(rate);
        if (rate == GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints)
        {
            throw new InvalidDataException(
                "The vanilla 100 percent EXP rate does not use an appended scaling stub.");
        }

        uint[] instructions =
        [
            0xB9400028, // ldr w8, [x1]
            0x340001E8, // cbz w8, replay
            MoveRateImmediateBase | rate << 5, // movz w9, #rate
            0x34000169, // cbz w9, zero
            0x9BA97D08, // umull x8, w8, w9
            0x5284E209, // movz w9, #10000
            0x9AC90908, // udiv x8, x8, x9
            0xB5000068, // cbnz x8, check-high-word
            0x52800028, // movz w8, #1
            0x14000006, // b store
            0xD360FD0A, // lsr x10, x8, #32
            0xB400008A, // cbz x10, store
            0x12800008, // movn w8, #0
            0x14000002, // b store
            0x2A1F03E8, // mov w8, wzr
            0xB9000028, // str w8, [x1]
            RateEpilogueVanilla,
            EncodeUnconditionalBranch(RateStubOffset + 0x44, RateEpilogueOffset + sizeof(uint)),
        ];

        var bytes = new byte[checked(instructions.Length * sizeof(uint))];
        for (var index = 0; index < instructions.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                instructions[index]);
        }

        if (bytes.Length != RateStubLength)
        {
            throw new InvalidOperationException("The Scarlet/Violet EXP rate stub length changed unexpectedly.");
        }

        return bytes;
    }

    private static byte[] CreateCapStub(int cap)
    {
        ValidateCap(cap);

        uint[] instructions =
        [
            0xD101C3FF, // sub sp, sp, #0x70
            0xA9007BFD, // stp x29, x30, [sp]
            0xA90153F3, // stp x19, x20, [sp, #0x10]
            0xA9025BF5, // stp x21, x22, [sp, #0x20]
            0xA90363F7, // stp x23, x24, [sp, #0x30]
            0xA9046BF9, // stp x25, x26, [sp, #0x40]
            0xA90573FB, // stp x27, x28, [sp, #0x50]
            0xF9400A95, // ldr x21, [x20, #0x10]
            0xAA0003F3, // mov x19, x0
            0xAA0103F4, // mov x20, x1
            MoveCapImmediateBase | checked((uint)cap) << 5, // movz w22, #cap
            0xAA1303E0, // mov x0, x19
            0xAA1403E1, // mov x1, x20
            EncodeBranchLink(CapStubOffset + 0x34, AwardMergeFunctionOffset),
            0x710192DF, // cmp w22, #100
            0x540004E0, // b.eq done
            0xB40004D5, // cbz x21, done
            0x3940C2B7, // ldrb w23, [x21, #0x30]
            0x71001AFF, // cmp w23, #6
            0x528000C8, // mov w8, #6
            0x1A8892F7, // csel w23, w23, w8, ls
            0xAA1F03F8, // mov x24, xzr
            0x6B17031F, // loop: cmp w24, w23
            0x540003E2, // b.hs done
            0xF8787AB9, // ldr x25, [x21, x24, lsl #3]
            0xB4000379, // cbz x25, next
            0x900085E8, // adrp x8, exact normal-party recipient vtable page
            0x9132C108, // add x8, x8, #0xcb0
            0xF9400329, // ldr x9, [x25]
            0xEB08013F, // cmp x9, x8
            0x540002C1, // b.ne next
            0x794E233A, // ldrh w26, [x25, #0x710]
            0x794E333B, // ldrh w27, [x25, #0x718]
            0xAA1903E0, // mov x0, x25
            EncodeBranchLink(CapStubOffset + 0x88, CurrentExperienceGetterOffset),
            0x2A0003FC, // mov w28, w0
            0x2A1A03E0, // mov w0, w26
            0x2A1B03E1, // mov w1, w27
            0x110006C2, // add w2, w22, #1
            EncodeBranchLink(CapStubOffset + 0x9C, MinimumExperienceFunctionOffset),
            0x5100041A, // sub w26, w0, #1
            0x6B1C035A, // subs w26, w26, w28
            0x1A9F235A, // csel w26, w26, wzr, hs
            0x8B181279, // add x25, x19, x24, lsl #4
            0x8B181289, // add x9, x20, x24, lsl #4
            0xB9400328, // ldr w8, [x25]
            0xB940012A, // ldr w10, [x9]
            0x4B0A0108, // sub w8, w8, w10 (reconstruct prior modulo 2^32)
            0x8B2A4108, // add x8, x8, w10, uxtw (exact 64-bit aggregate)
            0xEB1A011F, // cmp x8, x26
            0x9A9A9108, // csel x8, x8, x26, ls
            0xB9000328, // str w8, [x25]
            0x91000718, // next: add x24, x24, #1
            0x17FFFFE1, // b loop
            0xA94573FB, // done: ldp x27, x28, [sp, #0x50]
            0xA9446BF9, // ldp x25, x26, [sp, #0x40]
            0xA94363F7, // ldp x23, x24, [sp, #0x30]
            0xA9425BF5, // ldp x21, x22, [sp, #0x20]
            0xA94153F3, // ldp x19, x20, [sp, #0x10]
            0xA9407BFD, // ldp x29, x30, [sp]
            0x9101C3FF, // add sp, sp, #0x70
            0xD65F03C0, // ret
            0xD503201F, // canonical padding
            0xD503201F, // canonical padding
        ];

        var bytes = new byte[checked(instructions.Length * sizeof(uint))];
        for (var index = 0; index < instructions.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                instructions[index]);
        }

        if (bytes.Length != CapStubLength)
        {
            throw new InvalidOperationException("The Scarlet/Violet level-cap stub length changed unexpectedly.");
        }

        return bytes;
    }

    private static byte[] CreateRuntimeRateStub()
    {
        var instructions = Enumerable.Repeat(0xD503201Fu, RuntimeRateStubLength / sizeof(uint))
            .ToArray();
        instructions[0] = 0xB9400028; // ldr w8, [x1]
        instructions[1] = EncodeCompareAndBranchZero(
            RuntimeRateStubOffset + 0x04,
            RuntimeRateStubOffset + 0x58,
            register: 8,
            is64Bit: false);
        instructions[2] = EncodeAdrp(
            9,
            RuntimeRateStubOffset + 0x08,
            RuntimeSnapshotOffset);
        instructions[3] = 0xC8DFFD29; // ldar x9, [x9]
        instructions[4] = EncodeCompareAndBranchZero(
            RuntimeRateStubOffset + 0x10,
            RuntimeRateStubOffset + 0x1C,
            register: 9,
            is64Bit: true,
            nonZero: true);
        instructions[5] = MoveRateImmediateBase
            | GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints << 5;
        instructions[6] = EncodeUnconditionalBranch(
            RuntimeRateStubOffset + 0x18, RuntimeRateStubOffset + 0x20);
        instructions[7] = EncodeUnsignedBitfieldExtract64(9, 9, 9, 32);
        instructions[8] = EncodeCompareAndBranchZero(
            RuntimeRateStubOffset + 0x20,
            RuntimeRateStubOffset + 0x50,
            register: 9,
            is64Bit: false);
        instructions[9] = 0x9BA97D08; // umull x8, w8, w9
        instructions[10] = 0x5284E209; // movz w9, #10000
        instructions[11] = 0x9AC90908; // udiv x8, x8, x9
        instructions[12] = EncodeCompareAndBranchZero(
            RuntimeRateStubOffset + 0x30,
            RuntimeRateStubOffset + 0x3C,
            register: 8,
            is64Bit: true,
            nonZero: true);
        instructions[13] = 0x52800028; // movz w8, #1
        instructions[14] = EncodeUnconditionalBranch(
            RuntimeRateStubOffset + 0x38, RuntimeRateStubOffset + 0x54);
        instructions[15] = 0xD360FD0A; // lsr x10, x8, #32
        instructions[16] = EncodeCompareAndBranchZero(
            RuntimeRateStubOffset + 0x40,
            RuntimeRateStubOffset + 0x54,
            register: 10,
            is64Bit: true);
        instructions[17] = 0x12800008; // movn w8, #0
        instructions[18] = EncodeUnconditionalBranch(
            RuntimeRateStubOffset + 0x48, RuntimeRateStubOffset + 0x54);
        instructions[20] = 0x2A1F03E8; // mov w8, wzr
        instructions[21] = 0xB9000028; // str w8, [x1]
        instructions[22] = RateEpilogueVanilla;
        instructions[23] = EncodeUnconditionalBranch(
            RuntimeRateStubOffset + 0x5C, RateEpilogueOffset + sizeof(uint));
        return SerializeInstructions(
            instructions,
            RuntimeRateStubLength,
            "runtime EXP rate");
    }

    private static byte[] CreateRuntimeCapStub()
    {
        var instructions = Enumerable.Repeat(0xD503201Fu, RuntimeCapStubLength / sizeof(uint))
            .ToArray();
        instructions[0] = 0xD101C3FF;
        instructions[1] = 0xA9007BFD;
        instructions[2] = 0xA90153F3;
        instructions[3] = 0xA9025BF5;
        instructions[4] = 0xA90363F7;
        instructions[5] = 0xA9046BF9;
        instructions[6] = 0xA90573FB;
        instructions[7] = 0xF9400A95;
        instructions[8] = 0xAA0003F3;
        instructions[9] = 0xAA0103F4;
        instructions[10] = EncodeAdrp(
            22,
            RuntimeCapStubOffset + 0x28,
            RuntimeSnapshotOffset);
        instructions[11] = 0xC8DFFED6; // ldar x22, [x22]
        instructions[12] = EncodeCompareAndBranchZero(
            RuntimeCapStubOffset + 0x30,
            RuntimeCapStubOffset + 0x40,
            register: 22,
            is64Bit: true);
        instructions[13] = EncodeTestBitBranchZero(
            RuntimeCapStubOffset + 0x34,
            RuntimeCapStubOffset + 0x40,
            register: 22,
            bit: 1);
        instructions[14] = EncodeUnsignedBitfieldExtract64(22, 22, 2, 7);
        instructions[15] = EncodeUnconditionalBranch(
            RuntimeCapStubOffset + 0x3C, RuntimeCapStubOffset + 0x44);
        instructions[16] = MoveCapImmediateBase
            | (uint)GameplaySettingsValues.Vanilla.LevelCap << 5;
        instructions[17] = 0xAA1303E0;
        instructions[18] = 0xAA1403E1;
        instructions[19] = EncodeBranchLink(
            RuntimeCapStubOffset + 0x4C, AwardMergeFunctionOffset);
        instructions[20] = 0x710192DF;
        instructions[21] = EncodeConditionalBranch(
            RuntimeCapStubOffset + 0x54,
            RuntimeCapStubOffset + 0xF0,
            condition: 0);
        instructions[22] = EncodeCompareAndBranchZero(
            RuntimeCapStubOffset + 0x58,
            RuntimeCapStubOffset + 0xF0,
            register: 21,
            is64Bit: true);
        instructions[23] = 0x3940C2B7;
        instructions[24] = 0x71001AFF;
        instructions[25] = 0x528000C8;
        instructions[26] = 0x1A8892F7;
        instructions[27] = 0xAA1F03F8;
        instructions[28] = 0x6B17031F;
        instructions[29] = EncodeConditionalBranch(
            RuntimeCapStubOffset + 0x74,
            RuntimeCapStubOffset + 0xF0,
            condition: 2);
        instructions[30] = 0xF8787AB9;
        instructions[31] = EncodeCompareAndBranchZero(
            RuntimeCapStubOffset + 0x7C,
            RuntimeCapStubOffset + 0xE8,
            register: 25,
            is64Bit: true);
        instructions[32] = 0x900085E8;
        instructions[33] = 0x9132C108;
        instructions[34] = 0xF9400329;
        instructions[35] = 0xEB08013F;
        instructions[36] = EncodeConditionalBranch(
            RuntimeCapStubOffset + 0x90,
            RuntimeCapStubOffset + 0xE8,
            condition: 1);
        instructions[37] = 0x794E233A;
        instructions[38] = 0x794E333B;
        instructions[39] = 0xAA1903E0;
        instructions[40] = EncodeBranchLink(
            RuntimeCapStubOffset + 0xA0, CurrentExperienceGetterOffset);
        instructions[41] = 0x2A0003FC;
        instructions[42] = 0x2A1A03E0;
        instructions[43] = 0x2A1B03E1;
        instructions[44] = 0x110006C2;
        instructions[45] = EncodeBranchLink(
            RuntimeCapStubOffset + 0xB4, MinimumExperienceFunctionOffset);
        instructions[46] = 0x5100041A;
        instructions[47] = 0x6B1C035A;
        instructions[48] = 0x1A9F235A;
        instructions[49] = 0x8B181279;
        instructions[50] = 0x8B181289;
        instructions[51] = 0xB9400328;
        instructions[52] = 0xB940012A;
        instructions[53] = 0x4B0A0108;
        instructions[54] = 0x8B2A4108;
        instructions[55] = 0xEB1A011F;
        instructions[56] = 0x9A9A9108;
        instructions[57] = 0xB9000328;
        instructions[58] = 0x91000718;
        instructions[59] = EncodeUnconditionalBranch(
            RuntimeCapStubOffset + 0xEC, RuntimeCapStubOffset + 0x70);
        instructions[60] = 0xA94573FB;
        instructions[61] = 0xA9446BF9;
        instructions[62] = 0xA94363F7;
        instructions[63] = 0xA9425BF5;
        instructions[64] = 0xA94153F3;
        instructions[65] = 0xA9407BFD;
        instructions[66] = 0x9101C3FF;
        instructions[67] = 0xD65F03C0;
        return SerializeInstructions(
            instructions,
            RuntimeCapStubLength,
            "runtime level cap");
    }

    private static byte[] CreateRuntimeShareStub()
    {
        var instructions = Enumerable.Repeat(0xD503201Fu, RuntimeShareStubLength / sizeof(uint))
            .ToArray();
        instructions[0] = 0xA9BF47F0; // stp x16, x17, [sp, #-16]!
        instructions[1] = EncodeAdrp(
            16,
            RuntimeShareStubOffset + 0x04,
            RuntimeSnapshotOffset);
        instructions[2] = 0xC8DFFE10; // ldar x16, [x16]
        instructions[3] = EncodeCompareAndBranchZero(
            RuntimeShareStubOffset + 0x0C,
            RuntimeShareStubOffset + 0x14,
            register: 16,
            is64Bit: true);
        instructions[4] = EncodeTestBitBranchZero(
            RuntimeShareStubOffset + 0x10,
            RuntimeShareStubOffset + 0x1C,
            register: 16,
            bit: 0);
        instructions[5] = ShareDecisionVanilla;
        instructions[6] = EncodeUnconditionalBranch(
            RuntimeShareStubOffset + 0x18,
            RuntimeShareStubOffset + 0x20);
        instructions[7] = ShareDecisionDisabled;
        instructions[8] = 0xA8C147F0; // ldp x16, x17, [sp], #16
        instructions[9] = EncodeUnconditionalBranch(
            RuntimeShareStubOffset + 0x24, ShareDecisionOffset + sizeof(uint));
        return SerializeInstructions(
            instructions,
            RuntimeShareStubLength,
            "runtime EXP Share");
    }

    private static byte[] SerializeInstructions(
        IReadOnlyList<uint> instructions,
        int expectedLength,
        string label)
    {
        var bytes = new byte[checked(instructions.Count * sizeof(uint))];
        for (var index = 0; index < instructions.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                instructions[index]);
        }
        if (bytes.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"The Scarlet/Violet {label} stub length changed unexpectedly.");
        }
        return bytes;
    }

    private static void ValidateRequestedValues(GameplaySettingsValues values)
    {
        ValidateRate(values.ExperienceRateBasisPoints);
        ValidateCap(values.LevelCap);

        if (!values.LevelCapEnabled
            && values.LevelCap != GameplaySettingsValues.Vanilla.LevelCap)
        {
            throw new InvalidDataException(
                "A disabled Scarlet/Violet level cap must retain the vanilla level 100 value.");
        }
    }

    private static void ValidateCap(int cap)
    {
        if (cap is < 1 or > 100)
        {
            throw new InvalidDataException("The Scarlet/Violet level cap must be from 1 through 100.");
        }
    }

    private static void ValidateRate(uint rate)
    {
        if (rate > MaximumExperienceRateBasisPoints
            || rate % ExperienceRateStepBasisPoints != 0)
        {
            throw new InvalidDataException(
                "The Scarlet/Violet EXP rate must be from 0 through 500 percent in 10 percent steps.");
        }
    }

    private static void EnsureEditable(SvGameplaySettingsMainAnalysis analysis)
    {
        if (analysis.Kind is SvGameplaySettingsMainKind.UnsupportedBuild
            or SvGameplaySettingsMainKind.EditionMismatch
            or SvGameplaySettingsMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }
    }

    private static void EnsureStaticLayout(NsoFile nso, ReadOnlySpan<byte> text)
    {
        if (nso.Text.Header.MemoryOffset != 0
            || nso.Ro.Header.MemoryOffset != ExpectedRoMemoryOffset
            || text.Length is not OriginalTextLength
                and not (OriginalTextLength + RateStubLength)
                and not (OriginalTextLength + RateStubLength + CapStubLength))
        {
            throw new InvalidDataException(
                "Gameplay Settings rejected the exefs/main segment layout for the selected 4.0.0 profile.");
        }

        EnsureRange(text, OriginalTextLength - OriginalTailFingerprintLength, OriginalTailFingerprintLength, "original .text tail");
        var tailHash = SHA256.HashData(
            text.Slice(
                OriginalTextLength - OriginalTailFingerprintLength,
                OriginalTailFingerprintLength));
        if (!tailHash.AsSpan().SequenceEqual(ExpectedOriginalTailHash))
        {
            throw new InvalidDataException(
                "Gameplay Settings rejected the executable tail reserved for its owned EXP scaling stub.");
        }
    }

    private static void EnsureNormalizedFunctionFingerprint(
        ReadOnlySpan<byte> text,
        int functionOffset,
        int functionLength,
        int ownedInstructionOffset,
        uint vanillaInstruction,
        ReadOnlySpan<byte> expectedHash,
        string label)
    {
        EnsureRange(text, functionOffset, functionLength, label);
        if (ownedInstructionOffset < functionOffset
            || ownedInstructionOffset + sizeof(uint) > functionOffset + functionLength)
        {
            throw new InvalidOperationException($"The {label} owned instruction is outside its function fingerprint.");
        }

        var normalized = text.Slice(functionOffset, functionLength).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            normalized.AsSpan(ownedInstructionOffset - functionOffset, sizeof(uint)),
            vanillaInstruction);
        if (!SHA256.HashData(normalized).AsSpan().SequenceEqual(expectedHash))
        {
            throw new InvalidDataException(
                $"Gameplay Settings rejected the exact {label} function fingerprint.");
        }
    }

    private static void EnsureExactFunctionFingerprint(
        ReadOnlySpan<byte> text,
        int functionOffset,
        int functionLength,
        ReadOnlySpan<byte> expectedHash,
        string label)
    {
        EnsureRange(text, functionOffset, functionLength, label);
        if (!SHA256.HashData(text.Slice(functionOffset, functionLength))
            .AsSpan()
            .SequenceEqual(expectedHash))
        {
            throw new InvalidDataException(
                $"Gameplay Settings rejected the exact {label} fingerprint.");
        }
    }

    private static void EnsureExactSegmentFingerprint(
        ReadOnlySpan<byte> segment,
        int offset,
        int length,
        ReadOnlySpan<byte> expectedHash,
        string label)
    {
        if (offset < 0 || length < 0 || offset > segment.Length - length)
        {
            throw new InvalidDataException(
                $"Gameplay Settings {label} is outside its executable segment.");
        }

        if (!SHA256.HashData(segment.Slice(offset, length))
            .AsSpan()
            .SequenceEqual(expectedHash))
        {
            throw new InvalidDataException(
                $"Gameplay Settings rejected the exact {label} fingerprint.");
        }
    }

    private static bool CanonicalTextMatches(
        ReadOnlySpan<byte> text,
        Profile profile,
        GameplaySettingsValues values)
    {
        var normalized = text[..OriginalTextLength].ToArray();
        if (!values.ExperienceShareEnabled)
        {
            WriteInstruction(normalized, ShareDecisionOffset, ShareDecisionVanilla);
        }

        if (values.ExperienceRateBasisPoints
            != GameplaySettingsValues.Vanilla.ExperienceRateBasisPoints)
        {
            WriteInstruction(normalized, RateEpilogueOffset, RateEpilogueVanilla);
        }

        if (values.LevelCapEnabled)
        {
            WriteInstruction(normalized, CapMergeCallOffset, CapMergeCallerVanilla);
        }

        return SHA256.HashData(normalized).AsSpan().SequenceEqual(profile.TextSha256);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        SvGameplayRuntimeEdition expectedEdition,
        GameplaySettingsValues expectedValues)
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Gameplay Settings changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData)
            || !before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException(
                "Gameplay Settings unexpectedly changed a non-text NSO segment.");
        }

        ValidateOwnedTextDifferences(before.Text.DecompressedData, after.Text.DecompressedData);
        var analysis = Analyze(output, expectedEdition);
        if (analysis.Kind is not (SvGameplaySettingsMainKind.Vanilla
            or SvGameplaySettingsMainKind.Modified)
            || analysis.Values != expectedValues)
        {
            throw new InvalidDataException(
                "Gameplay Settings verification failed after writing exefs/main.");
        }
    }

    private static void ValidateOwnedTextDifferences(
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after)
    {
        var commonLength = Math.Min(before.Length, after.Length);
        for (var offset = 0; offset < commonLength; offset++)
        {
            if (before[offset] == after[offset])
            {
                continue;
            }

            var owned = offset is >= ShareDecisionOffset
                    and < (ShareDecisionOffset + sizeof(uint))
                || offset is >= RateEpilogueOffset
                    and < (RateEpilogueOffset + sizeof(uint))
                || offset is >= CapMergeCallOffset
                    and < (CapMergeCallOffset + sizeof(uint))
                || offset >= OriginalTextLength;
            if (!owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Gameplay Settings unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (before.Length != after.Length
            && (before.Length is not OriginalTextLength
                    and not (OriginalTextLength + RateStubLength)
                    and not (OriginalTextLength + RateStubLength + CapStubLength)
                || after.Length is not OriginalTextLength
                    and not (OriginalTextLength + RateStubLength)
                    and not (OriginalTextLength + RateStubLength + CapStubLength)))
        {
            throw new InvalidDataException(
                "Gameplay Settings changed the decompressed .text size outside its exact owned stub range.");
        }
    }

    private static bool DeclaredSegmentHashesMatch(NsoFile nso)
    {
        return DeclaredSegmentHashMatches(
                nso.Text,
                nso.Flags.HasFlag(NsoFlags.CheckHashText))
            && DeclaredSegmentHashMatches(
                nso.Ro,
                nso.Flags.HasFlag(NsoFlags.CheckHashRo))
            && DeclaredSegmentHashMatches(
                nso.Data,
                nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static bool TryParseBoundedNso(byte[] mainBytes, out NsoFile nso)
    {
        nso = null!;
        var bytes = mainBytes.AsSpan();
        if (bytes.Length < NsoFile.HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != NsoFile.Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x04, sizeof(uint)))
                != ExpectedNsoVersion
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x0C, sizeof(uint)))
                != ExpectedNsoFlags)
        {
            return false;
        }

        var expectedSegments = new[]
        {
            (MemoryOffset: 0, DecompressedSize: OriginalTextLength, HeaderAux: ExpectedTextHeaderAux),
            (MemoryOffset: ExpectedRoMemoryOffset, DecompressedSize: ExpectedRoLength, HeaderAux: ExpectedRoHeaderAux),
            (MemoryOffset: ExpectedDataMemoryOffset, DecompressedSize: ExpectedDataLength, HeaderAux: ExpectedDataHeaderAux),
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
            var headerAux = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(headerOffset + 0x0C, sizeof(int)));
            var expected = expectedSegments[index];
            var decompressedSizeMatches = decompressedSize == expected.DecompressedSize
                || index == 0
                    && (decompressedSize == OriginalTextLength + RateStubLength
                        || decompressedSize
                            == OriginalTextLength + RateStubLength + CapStubLength);
            if (fileOffset < NsoFile.HeaderSize
                || fileOffset < priorFileEnd
                || memoryOffset != expected.MemoryOffset
                || !decompressedSizeMatches
                || headerAux != expected.HeaderAux
                || decompressedSize < 0
                || decompressedSize
                    > SvGameplayRuntimeProfileValidator.MaximumDecompressedSegmentBytes
                || compressedSize < 0
                || fileOffset > bytes.Length
                || compressedSize > bytes.Length - fileOffset)
            {
                return false;
            }

            priorFileEnd = checked(fileOffset + compressedSize);
            totalDecompressed = checked(totalDecompressed + decompressedSize);
            if (totalDecompressed
                > SvGameplayRuntimeProfileValidator.MaximumTotalDecompressedBytes)
            {
                return false;
            }
        }

        nso = NsoFile.Parse(mainBytes);
        return true;
    }

    private static bool DeclaredSegmentHashMatches(NsoSegment segment, bool required)
    {
        return !required
            || SHA256.HashData(segment.DecompressedData).AsSpan().SequenceEqual(segment.Hash);
    }

    private static uint EncodeCompareAndBranchZero(
        int sourceOffset,
        int targetOffset,
        int register,
        bool is64Bit,
        bool nonZero = false)
    {
        ValidateRegister(register);
        var immediate = GetSignedBranchImmediate(
            sourceOffset,
            targetOffset,
            immediateBits: 19,
            "CBZ/CBNZ");
        var opcode = is64Bit ? 0xB4000000u : 0x34000000u;
        if (nonZero)
        {
            opcode |= 0x01000000u;
        }

        return opcode | ((uint)immediate & 0x7FFFFu) << 5 | (uint)register;
    }

    private static uint EncodeConditionalBranch(
        int sourceOffset,
        int targetOffset,
        int condition)
    {
        if (condition is < 0 or > 0xF)
        {
            throw new ArgumentOutOfRangeException(nameof(condition));
        }

        var immediate = GetSignedBranchImmediate(
            sourceOffset,
            targetOffset,
            immediateBits: 19,
            "conditional branch");
        return 0x54000000u
            | ((uint)immediate & 0x7FFFFu) << 5
            | (uint)condition;
    }

    private static uint EncodeTestBitBranchZero(
        int sourceOffset,
        int targetOffset,
        int register,
        int bit)
    {
        ValidateRegister(register);
        if (bit is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(bit));
        }

        var immediate = GetSignedBranchImmediate(
            sourceOffset,
            targetOffset,
            immediateBits: 14,
            "TBZ");
        return 0x36000000u
            | ((uint)bit & 0x20u) << 26
            | ((uint)bit & 0x1Fu) << 19
            | ((uint)immediate & 0x3FFFu) << 5
            | (uint)register;
    }

    private static uint EncodeAdrp(
        int register,
        int sourceOffset,
        int targetOffset)
    {
        ValidateRegister(register);
        var sourcePage = (long)sourceOffset & ~0xFFFL;
        var targetPage = (long)targetOffset & ~0xFFFL;
        var immediate = (targetPage - sourcePage) / 0x1000;
        const long minimum = -(1L << 20);
        const long maximum = (1L << 20) - 1;
        if (immediate < minimum || immediate > maximum)
        {
            throw new InvalidOperationException(
                "An AArch64 ADRP target is outside the signed 21-bit page range.");
        }

        var encoded = (uint)immediate & 0x1FFFFFu;
        return 0x90000000u
            | (encoded & 0x3u) << 29
            | (encoded >> 2) << 5
            | (uint)register;
    }

    private static uint EncodeUnsignedBitfieldExtract64(
        int destinationRegister,
        int sourceRegister,
        int leastSignificantBit,
        int width)
    {
        ValidateRegister(destinationRegister);
        ValidateRegister(sourceRegister);
        if (leastSignificantBit is < 0 or > 63
            || width is < 1 or > 64
            || leastSignificantBit > 64 - width)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var mostSignificantBit = leastSignificantBit + width - 1;
        return 0xD3400000u
            | (uint)leastSignificantBit << 16
            | (uint)mostSignificantBit << 10
            | (uint)sourceRegister << 5
            | (uint)destinationRegister;
    }

    private static long GetSignedBranchImmediate(
        int sourceOffset,
        int targetOffset,
        int immediateBits,
        string instruction)
    {
        var delta = (long)targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidOperationException(
                $"An AArch64 {instruction} target is not instruction aligned.");
        }

        var immediate = delta / sizeof(uint);
        var minimum = -(1L << (immediateBits - 1));
        var maximum = (1L << (immediateBits - 1)) - 1;
        if (immediate < minimum || immediate > maximum)
        {
            throw new InvalidOperationException(
                $"An AArch64 {instruction} target is outside its signed immediate range.");
        }

        return immediate;
    }

    private static void ValidateRegister(int register)
    {
        if (register is < 0 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    private static uint EncodeUnconditionalBranch(int sourceOffset, int targetOffset)
    {
        var delta = (long)targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidOperationException("An AArch64 branch target is not instruction aligned.");
        }

        var immediate = delta / sizeof(uint);
        const long minimum = -(1L << 25);
        const long maximum = (1L << 25) - 1;
        if (immediate is < minimum or > maximum)
        {
            throw new InvalidOperationException("An AArch64 branch target is outside the signed B range.");
        }

        return 0x14000000u | ((uint)immediate & 0x03FFFFFFu);
    }

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        return EncodeUnconditionalBranch(sourceOffset, targetOffset) | 0x80000000u;
    }

    private static bool IsZeroFilled(ReadOnlySpan<byte> bytes)
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

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        EnsureRange(text, offset, sizeof(uint), $"instruction {FormatTextOffset(offset)}");
        return BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        EnsureRange(text, offset, sizeof(uint), $"patch instruction {FormatTextOffset(offset)}");
        BinaryPrimitives.WriteUInt32LittleEndian(
            text.AsSpan(offset, sizeof(uint)),
            instruction);
    }

    private static void EnsureInstruction(
        ReadOnlySpan<byte> text,
        int offset,
        uint expected,
        string label)
    {
        var actual = ReadInstruction(text, offset);
        if (actual != expected)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Gameplay Settings expected the exact {label} at {FormatTextOffset(offset)}, but found 0x{actual:X8}."));
        }
    }

    private static void EnsureRange(
        ReadOnlySpan<byte> bytes,
        int offset,
        int length,
        string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException($"Gameplay Settings {label} is outside the decompressed .text segment.");
        }
    }

    private static Profile GetProfile(SvGameplayRuntimeEdition edition)
    {
        return Profiles.SingleOrDefault(profile => profile.Edition == edition)
            ?? throw new ArgumentOutOfRangeException(nameof(edition));
    }

    private static Profile? FindProfile(string buildId)
    {
        return Profiles.SingleOrDefault(profile =>
            string.Equals(profile.FullBuildId, buildId, StringComparison.Ordinal));
    }

    private static SvGameplaySettingsMainAnalysis Conflict(string message)
    {
        return new SvGameplaySettingsMainAnalysis(
            SvGameplaySettingsMainKind.Conflict,
            message,
            DetectedEdition: null,
            BuildId: "unknown",
            GameplaySettingsValues.Vanilla,
            CanonicalTextIdentityMatches: false,
            CapabilitiesView,
            []);
    }

    private static string FormatTextOffset(int offset)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"main.text+0x{offset:X8}");
    }

    private static string FormatEdition(SvGameplayRuntimeEdition edition)
    {
        return edition switch
        {
            SvGameplayRuntimeEdition.Scarlet => "Pokemon Scarlet",
            SvGameplayRuntimeEdition.Violet => "Pokemon Violet",
            _ => edition.ToString(),
        };
    }

    private sealed record Profile(
        SvGameplayRuntimeEdition Edition,
        string FullBuildId,
        byte[] TextSha256);

    private sealed record PatchedGameplayCodeSettings(
        uint ExperienceRateBasisPoints,
        bool LevelCapEnabled,
        byte LevelCap);
}
