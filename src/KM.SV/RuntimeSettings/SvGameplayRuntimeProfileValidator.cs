// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.RuntimeSettings;
using KM.Formats.Executable;

namespace KM.SV.RuntimeSettings;

public enum SvGameplayRuntimeEdition
{
    Scarlet,
    Violet,
}

public enum SvGameplayRuntimeProfileGateId
{
    CandidateTitleId,
    CandidateUpdateVersion,
    CandidateFullBuildId,
    CandidateNsoEnvelope,
    CandidateTextSegmentIdentity,
    CandidateRoSegmentIdentity,
    CandidateDataSegmentIdentity,
    CandidateTargetWindows,
    CandidateRawMainIdentity,
    FixedRuntimeSlotPolicy,
    BundleIdentityContract,
    SettingsEnvelopeContract,
    AuthorizedCleanBaseRecord,
    SegmentFileAndMemoryDomains,
    RetailNpdmIdentity,
    MinimalNpdmFieldDiff,
    StockModuleInventory,
    RuntimeSlotObservation,
    OwnershipLedgerComposition,
    ComposedMainIdentity,
    RuntimeArtifactIdentity,
    ProfileArtifactIdentity,
    RomFsArtifactIdentities,
    OutputArtifactIdentities,
    MaterializationReceiptRoundTrip,
    DirectSdDeploymentCoordinator,
    RuntimeMutableSettingsOwner,
    BundleHandshakeRuntimeCanary,
    CurrentProcessHandleAbi,
    MemoryAliasAndCacheAbi,
    SettingsFilesystemImportAbis,
    SettingsFilesystemMountOwnershipCanary,
    SettingsFilesystemDirectoryCanary,
    SettingsJournalRuntimeCanary,
    LuaRomFsSourceIdentityInventory,
    LuaTmPrototypeTransform,
    LuaOptionsPrototypeTransforms,
    MessageSourceIdentityInventory,
    LuaNativeRegistrationAbi,
    LuaVmLifecycleCanary,
    LuaCallbackAbi,
    LuaParserAndInvocationCanary,
    LuaSafeEntryLifecycleCanary,
    MenuInteractionAndLocalizationCanary,
    HookPublicationLifecycleCanary,
    ExperienceShareHookAbi,
    ExperienceShareRuntimeCanary,
    ExperienceRateHookAbi,
    ExperienceRateRuntimeCanary,
    LevelCapRecipientGetterAbi,
    LevelCapHelperAbis,
    LevelCapMutationHook,
    LevelCapSourceCensus,
    CandyAwardSourceCensus,
    CandyConsumptionCanary,
    LetsGoAwardSourceCensus,
    SynchroAwardSourceCensus,
    PicnicAwardSourceCensus,
    OtherAwardSourceCensus,
    EditionRuntimeParityCanary,
    EmulatorLifecycleCanary,
    HardwareLifecycleCanary,
}

public enum SvGameplayRuntimeProfileGateStatus
{
    Verified,
    MissingProof,
    Rejected,
}

public enum SvGameplayRuntimeTargetClass
{
    ResearchOnly,
    CandidateHook,
    InstrumentationOnly,
    SemanticChain,
}

public enum SvGameplayRuntimeCapabilityId
{
    PackageMaterialization,
    SettingsPersistence,
    InGameSettingsMenu,
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public sealed record SvGameplayRuntimeExpectedProfile(
    SvGameplayRuntimeEdition Edition,
    ulong TitleId,
    GameplayBundleVersion UpdateVersion,
    string FullBuildId,
    string TextSegmentSha256,
    string RoSegmentSha256,
    string DataSegmentSha256);

public sealed record SvGameplayRuntimeProfileGate(
    SvGameplayRuntimeProfileGateId Id,
    SvGameplayRuntimeProfileGateStatus Status,
    string ReasonCode);

public sealed record SvGameplayRuntimeTargetWindowResult(
    string TargetId,
    int TextOffset,
    int ByteLength,
    int? FunctionByteLength,
    SvGameplayRuntimeTargetClass TargetClass,
    bool WindowMatches,
    bool? FunctionFingerprintMatches,
    bool Matches,
    bool PublicationAuthorized);

public sealed record SvGameplayRuntimeCapabilityAssessment(
    SvGameplayRuntimeCapabilityId Capability,
    bool Available,
    IReadOnlyList<SvGameplayRuntimeProfileGateId> BlockingGates);

public sealed record SvGameplayRuntimeProfileAssessment(
    SvGameplayRuntimeExpectedProfile ExpectedProfile,
    bool StaticEvidenceMatches,
    bool ProfileComplete,
    bool CanMaterialize,
    bool CanExposeControls,
    IReadOnlyList<SvGameplayRuntimeProfileGate> Gates,
    IReadOnlyList<SvGameplayRuntimeTargetWindowResult> TargetWindows,
    IReadOnlyList<SvGameplayRuntimeCapabilityAssessment> Capabilities,
    IReadOnlyList<GameplayDeferredFeatureAssessment> DeferredFeatures);

/// <summary>
/// Validates the exact edition-specific executable facts currently frozen for the
/// Scarlet and Violet 4.0.0 gameplay runtime. This validator does not contain a
/// package writer or runtime payload. Static recognition cannot authorize output or UI.
/// </summary>
public static class SvGameplayRuntimeProfileValidator
{
    public const string RuntimeComponentRelativePath = "exefs/subsdk9";
    public const int MaximumRawMainBytes = 128 * 1024 * 1024;
    public const int MaximumDecompressedSegmentBytes = 128 * 1024 * 1024;
    public const int MaximumTotalDecompressedBytes = 256 * 1024 * 1024;

    private static readonly GameplayBundleVersion ExpectedUpdateVersion = new(4, 0, 0);
    private static readonly SvGameplayRuntimeExpectedProfile ScarletProfile = new(
        SvGameplayRuntimeEdition.Scarlet,
        0x0100A3D008C5C000,
        ExpectedUpdateVersion,
        "421C5411B487EB4D049DD065FEC9547773E8E598000000000000000000000000",
        "F48571CECF394151DA2276AC88F31BEBC74E1B77BB5D413D8BC6FEB768EA0C84",
        "87FCCDBF63746F59C674A32501A5C252DDEE1BAEE6A879A7931A99A54D20D4FC",
        "6A36DC4E651B720494BB59F9FE080451EF04897811DBF5B19D6B66E265BE3083");
    private static readonly SvGameplayRuntimeExpectedProfile VioletProfile = new(
        SvGameplayRuntimeEdition.Violet,
        0x01008F6008C5E000,
        ExpectedUpdateVersion,
        "709BFD66115298640155FCC4979DBA151C7CC79A000000000000000000000000",
        "7ED23874DC1765429CC43C7E7B13768B04C7F51FA932337DE788D18F8E693F45",
        "755FB7FB5E35D52D58DBBD48F4AE127B0ACF8D9E7BB2163E35311717B3552267",
        "6A36DC4E651B720494BB59F9FE080451EF04897811DBF5B19D6B66E265BE3083");

    private static readonly SvGameplayRuntimeProfileGateId[] RequiredStaticCandidateGates =
    [
        SvGameplayRuntimeProfileGateId.CandidateTitleId,
        SvGameplayRuntimeProfileGateId.CandidateUpdateVersion,
        SvGameplayRuntimeProfileGateId.CandidateFullBuildId,
        SvGameplayRuntimeProfileGateId.CandidateNsoEnvelope,
        SvGameplayRuntimeProfileGateId.CandidateTextSegmentIdentity,
        SvGameplayRuntimeProfileGateId.CandidateRoSegmentIdentity,
        SvGameplayRuntimeProfileGateId.CandidateDataSegmentIdentity,
        SvGameplayRuntimeProfileGateId.CandidateTargetWindows,
    ];

    private static readonly TargetWindow[] ExpectedTargetWindows = CreateTargetWindows();

    public static SvGameplayRuntimeExpectedProfile GetExpectedProfile(
        SvGameplayRuntimeEdition edition)
    {
        return edition switch
        {
            SvGameplayRuntimeEdition.Scarlet => ScarletProfile,
            SvGameplayRuntimeEdition.Violet => VioletProfile,
            _ => throw new ArgumentOutOfRangeException(nameof(edition)),
        };
    }

    public static SvGameplayRuntimeProfileAssessment Assess(
        SvGameplayRuntimeEdition edition,
        ulong titleId,
        GameplayBundleVersion updateVersion,
        ReadOnlyMemory<byte> rawMainBytes)
    {
        var expected = GetExpectedProfile(edition);
        var titleMatches = titleId == expected.TitleId;
        var updateMatches = updateVersion == expected.UpdateVersion;
        var embeddedBuildId = TryReadBuildId(rawMainBytes.Span);
        var buildIdMatches = string.Equals(
            embeddedBuildId,
            expected.FullBuildId,
            StringComparison.Ordinal);
        var nsoEnvelopeMatches = TryParseBoundedNso(rawMainBytes, out var nso);
        var textIdentityMatches = SegmentIdentityMatches(
            nsoEnvelopeMatches ? nso!.Text : null,
            expected.TextSegmentSha256);
        var roIdentityMatches = SegmentIdentityMatches(
            nsoEnvelopeMatches ? nso!.Ro : null,
            expected.RoSegmentSha256);
        var dataIdentityMatches = SegmentIdentityMatches(
            nsoEnvelopeMatches ? nso!.Data : null,
            expected.DataSegmentSha256);
        var targetResults = ExpectedTargetWindows
            .Select(target => ToResult(
                target,
                nsoEnvelopeMatches ? nso!.Text.DecompressedData : null))
            .ToArray();
        var targetWindowsMatch = nsoEnvelopeMatches
            && targetResults.All(target => target.Matches);
        var bundleIdentityContractMatches = GameplayBundleIdentity.BundleAbi == 1
            && GameplayBundleIdentity.SettingsSchema == 1;
        var settingsEnvelopeContractMatches = GameplaySettingsJournal.RecordSize == 0x100
            && GameplaySettingsJournal.SlotSize == 0x1000
            && GameplaySettingsJournal.JournalSize == 0x2000
            && GameplaySettingsJournal.SupportedSchema == 1;
        var gates = CreateGateInventory(
            titleMatches,
            updateMatches,
            buildIdMatches,
            nsoEnvelopeMatches,
            textIdentityMatches,
            roIdentityMatches,
            dataIdentityMatches,
            targetWindowsMatch,
            bundleIdentityContractMatches,
            settingsEnvelopeContractMatches);
        var staticEvidenceMatches = RequiredStaticCandidateGates.All(id =>
            gates.Single(gate => gate.Id == id).Status
                == SvGameplayRuntimeProfileGateStatus.Verified);
        var profileComplete = gates.All(
            gate => gate.Status == SvGameplayRuntimeProfileGateStatus.Verified);
        return new SvGameplayRuntimeProfileAssessment(
            expected,
            staticEvidenceMatches,
            profileComplete,
            CanMaterialize: profileComplete,
            CanExposeControls: profileComplete,
            gates,
            targetResults,
            CreateCapabilityAssessments(gates),
            GameplayDeferredFeatureCatalog.ForFamily(GameplaySettingsFamily.ScarletViolet));
    }

    private static bool TryParseBoundedNso(
        ReadOnlyMemory<byte> rawMainBytes,
        out NsoFile? nso)
    {
        nso = null;
        var bytes = rawMainBytes.Span;
        if (bytes.Length is < NsoFile.HeaderSize or > MaximumRawMainBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != NsoFile.Magic)
        {
            return false;
        }

        long totalDecompressed = 0;
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
            if (fileOffset < NsoFile.HeaderSize
                || decompressedSize < 0
                || decompressedSize > MaximumDecompressedSegmentBytes
                || compressedSize < 0
                || fileOffset > bytes.Length
                || compressedSize > bytes.Length - fileOffset)
            {
                return false;
            }

            totalDecompressed = checked(totalDecompressed + decompressedSize);
            if (totalDecompressed > MaximumTotalDecompressedBytes)
            {
                return false;
            }
        }

        try
        {
            nso = NsoFile.Parse(rawMainBytes.ToArray());
            return ValidateDeclaredSegmentHashes(nso);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            nso = null;
            return false;
        }
    }

    private static bool ValidateDeclaredSegmentHashes(NsoFile nso)
    {
        return ValidateDeclaredSegmentHash(
                nso.Text,
                nso.Flags.HasFlag(NsoFlags.CheckHashText))
            && ValidateDeclaredSegmentHash(
                nso.Ro,
                nso.Flags.HasFlag(NsoFlags.CheckHashRo))
            && ValidateDeclaredSegmentHash(
                nso.Data,
                nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static bool ValidateDeclaredSegmentHash(NsoSegment segment, bool required)
    {
        return !required || SHA256.HashData(segment.DecompressedData).AsSpan()
            .SequenceEqual(segment.Hash);
    }

    private static bool SegmentIdentityMatches(NsoSegment? segment, string expectedSha256)
    {
        return segment is not null
            && string.Equals(
                Convert.ToHexString(SHA256.HashData(segment.DecompressedData)),
                expectedSha256,
                StringComparison.Ordinal);
    }

    private static string? TryReadBuildId(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < NsoFile.HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != NsoFile.Magic)
        {
            return null;
        }

        return Convert.ToHexString(bytes.Slice(0x40, 0x20));
    }

    private static SvGameplayRuntimeTargetWindowResult ToResult(
        TargetWindow target,
        byte[]? textBytes)
    {
        var windowMatches = RangeMatches(
            textBytes,
            target.TextOffset,
            target.ExpectedBytes);
        bool? functionFingerprintMatches = null;
        if (target.FunctionFingerprint is not null)
        {
            functionFingerprintMatches = textBytes is not null
                && target.TextOffset <= textBytes.Length
                && target.FunctionFingerprint.ByteLength
                    <= textBytes.Length - target.TextOffset
                && SHA256.HashData(
                        textBytes.AsSpan(
                            target.TextOffset,
                            target.FunctionFingerprint.ByteLength))
                    .AsSpan()
                    .SequenceEqual(target.FunctionFingerprint.Sha256);
        }

        var matches = windowMatches
            && functionFingerprintMatches is not false;
        return new SvGameplayRuntimeTargetWindowResult(
            target.TargetId,
            target.TextOffset,
            target.ExpectedBytes.Length,
            target.FunctionFingerprint?.ByteLength,
            target.TargetClass,
            windowMatches,
            functionFingerprintMatches,
            matches,
            PublicationAuthorized: false);
    }

    private static bool RangeMatches(
        byte[]? bytes,
        int offset,
        ReadOnlySpan<byte> expected)
    {
        return bytes is not null
            && offset >= 0
            && offset <= bytes.Length
            && expected.Length <= bytes.Length - offset
            && bytes.AsSpan(offset, expected.Length).SequenceEqual(expected);
    }

    private static SvGameplayRuntimeProfileGate[] CreateGateInventory(
        bool titleMatches,
        bool updateMatches,
        bool buildIdMatches,
        bool nsoEnvelopeMatches,
        bool textIdentityMatches,
        bool roIdentityMatches,
        bool dataIdentityMatches,
        bool targetWindowsMatch,
        bool bundleIdentityContractMatches,
        bool settingsEnvelopeContractMatches)
    {
        return
        [
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateTitleId, titleMatches, "title-id-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateUpdateVersion, updateMatches, "update-version-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateFullBuildId, buildIdMatches, "full-build-id-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateNsoEnvelope, nsoEnvelopeMatches, "nso-envelope-invalid-or-out-of-bounds"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateTextSegmentIdentity, textIdentityMatches, "text-segment-identity-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateRoSegmentIdentity, roIdentityMatches, "ro-segment-identity-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateDataSegmentIdentity, dataIdentityMatches, "data-segment-identity-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.CandidateTargetWindows, targetWindowsMatch, nsoEnvelopeMatches ? "target-window-or-function-fingerprint-mismatch" : "nso-envelope-not-recognized"),
            MissingGate(SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity, "raw-main-identity-not-independently-frozen"),
            VerifiedGate(SvGameplayRuntimeProfileGateId.FixedRuntimeSlotPolicy),
            CandidateGate(SvGameplayRuntimeProfileGateId.BundleIdentityContract, bundleIdentityContractMatches, "bundle-identity-contract-mismatch"),
            CandidateGate(SvGameplayRuntimeProfileGateId.SettingsEnvelopeContract, settingsEnvelopeContractMatches, "settings-envelope-contract-mismatch"),
            MissingGate(SvGameplayRuntimeProfileGateId.AuthorizedCleanBaseRecord, "authorized-clean-base-record-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SegmentFileAndMemoryDomains, "segment-file-and-memory-domains-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.RetailNpdmIdentity, "retail-npdm-identity-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.MinimalNpdmFieldDiff, "minimal-npdm-field-diff-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.StockModuleInventory, "stock-module-inventory-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.RuntimeSlotObservation, "runtime-slot-observation-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.OwnershipLedgerComposition, "ownership-ledger-composition-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ComposedMainIdentity, "composed-main-identity-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.RuntimeArtifactIdentity, "runtime-artifact-identity-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ProfileArtifactIdentity, "profile-artifact-identity-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.RomFsArtifactIdentities, "romfs-artifact-identities-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.OutputArtifactIdentities, "output-artifact-identities-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.MaterializationReceiptRoundTrip, "materialization-receipt-round-trip-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.DirectSdDeploymentCoordinator, "direct-sd-deployment-coordinator-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.RuntimeMutableSettingsOwner, "runtime-mutable-settings-owner-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.BundleHandshakeRuntimeCanary, "bundle-handshake-runtime-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.CurrentProcessHandleAbi, "current-process-handle-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.MemoryAliasAndCacheAbi, "memory-alias-and-cache-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SettingsFilesystemImportAbis, "settings-filesystem-import-abis-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SettingsFilesystemMountOwnershipCanary, "settings-filesystem-mount-ownership-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SettingsFilesystemDirectoryCanary, "settings-filesystem-directory-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary, "settings-journal-runtime-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaRomFsSourceIdentityInventory, "lua-romfs-source-identity-inventory-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaTmPrototypeTransform, "lua-tm-prototype-transform-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaOptionsPrototypeTransforms, "lua-options-prototype-transforms-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.MessageSourceIdentityInventory, "message-source-identity-inventory-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaNativeRegistrationAbi, "lua-native-registration-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaVmLifecycleCanary, "lua-vm-lifecycle-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaCallbackAbi, "lua-callback-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaParserAndInvocationCanary, "lua-parser-and-invocation-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LuaSafeEntryLifecycleCanary, "lua-safe-entry-lifecycle-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.MenuInteractionAndLocalizationCanary, "menu-interaction-and-localization-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary, "hook-publication-lifecycle-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ExperienceShareHookAbi, "experience-share-hook-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary, "experience-share-runtime-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ExperienceRateHookAbi, "experience-rate-hook-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary, "experience-rate-runtime-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LevelCapRecipientGetterAbi, "level-cap-recipient-getter-abi-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LevelCapHelperAbis, "level-cap-helper-abis-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LevelCapMutationHook, "level-cap-mutation-hook-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LevelCapSourceCensus, "level-cap-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.CandyAwardSourceCensus, "candy-award-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.CandyConsumptionCanary, "candy-consumption-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.LetsGoAwardSourceCensus, "lets-go-award-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.SynchroAwardSourceCensus, "synchro-award-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.PicnicAwardSourceCensus, "picnic-award-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.OtherAwardSourceCensus, "other-award-source-census-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.EditionRuntimeParityCanary, "edition-runtime-parity-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.EmulatorLifecycleCanary, "emulator-lifecycle-canary-unavailable"),
            MissingGate(SvGameplayRuntimeProfileGateId.HardwareLifecycleCanary, "hardware-lifecycle-canary-unavailable"),
        ];
    }

    private static SvGameplayRuntimeCapabilityAssessment[] CreateCapabilityAssessments(
        IReadOnlyList<SvGameplayRuntimeProfileGate> gates)
    {
        var unavailable = gates
            .Where(gate => gate.Status != SvGameplayRuntimeProfileGateStatus.Verified)
            .Select(gate => gate.Id)
            .ToArray();
        return
        [
            BlockedCapability(SvGameplayRuntimeCapabilityId.PackageMaterialization, unavailable),
            BlockedCapability(
                SvGameplayRuntimeCapabilityId.SettingsPersistence,
                gates,
                SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SvGameplayRuntimeProfileGateId.RuntimeArtifactIdentity,
                SvGameplayRuntimeProfileGateId.ProfileArtifactIdentity,
                SvGameplayRuntimeProfileGateId.OutputArtifactIdentities,
                SvGameplayRuntimeProfileGateId.RuntimeMutableSettingsOwner,
                SvGameplayRuntimeProfileGateId.SettingsFilesystemImportAbis,
                SvGameplayRuntimeProfileGateId.SettingsFilesystemMountOwnershipCanary,
                SvGameplayRuntimeProfileGateId.SettingsFilesystemDirectoryCanary,
                SvGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary),
            BlockedCapability(
                SvGameplayRuntimeCapabilityId.InGameSettingsMenu,
                gates,
                SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SvGameplayRuntimeProfileGateId.RomFsArtifactIdentities,
                SvGameplayRuntimeProfileGateId.LuaRomFsSourceIdentityInventory,
                SvGameplayRuntimeProfileGateId.LuaOptionsPrototypeTransforms,
                SvGameplayRuntimeProfileGateId.MessageSourceIdentityInventory,
                SvGameplayRuntimeProfileGateId.LuaNativeRegistrationAbi,
                SvGameplayRuntimeProfileGateId.LuaVmLifecycleCanary,
                SvGameplayRuntimeProfileGateId.LuaCallbackAbi,
                SvGameplayRuntimeProfileGateId.LuaParserAndInvocationCanary,
                SvGameplayRuntimeProfileGateId.LuaSafeEntryLifecycleCanary,
                SvGameplayRuntimeProfileGateId.MenuInteractionAndLocalizationCanary),
            BlockedCapability(
                SvGameplayRuntimeCapabilityId.ExperienceShare,
                gates,
                SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SvGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SvGameplayRuntimeProfileGateId.ExperienceShareHookAbi,
                SvGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary),
            BlockedCapability(
                SvGameplayRuntimeCapabilityId.ExperienceRate,
                gates,
                SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SvGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SvGameplayRuntimeProfileGateId.ExperienceRateHookAbi,
                SvGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary,
                SvGameplayRuntimeProfileGateId.CandyAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.LetsGoAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.SynchroAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.PicnicAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.OtherAwardSourceCensus),
            BlockedCapability(
                SvGameplayRuntimeCapabilityId.LevelCap,
                gates,
                SvGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SvGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SvGameplayRuntimeProfileGateId.LevelCapRecipientGetterAbi,
                SvGameplayRuntimeProfileGateId.LevelCapHelperAbis,
                SvGameplayRuntimeProfileGateId.LevelCapMutationHook,
                SvGameplayRuntimeProfileGateId.LevelCapSourceCensus,
                SvGameplayRuntimeProfileGateId.CandyConsumptionCanary,
                SvGameplayRuntimeProfileGateId.CandyAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.LetsGoAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.SynchroAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.PicnicAwardSourceCensus,
                SvGameplayRuntimeProfileGateId.OtherAwardSourceCensus),
        ];
    }

    private static SvGameplayRuntimeCapabilityAssessment BlockedCapability(
        SvGameplayRuntimeCapabilityId capability,
        IReadOnlyList<SvGameplayRuntimeProfileGateId> blockingGates)
    {
        return new SvGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            blockingGates);
    }

    private static SvGameplayRuntimeCapabilityAssessment BlockedCapability(
        SvGameplayRuntimeCapabilityId capability,
        IReadOnlyList<SvGameplayRuntimeProfileGate> gates,
        params SvGameplayRuntimeProfileGateId[] relevantGates)
    {
        var byId = gates.ToDictionary(gate => gate.Id);
        return new SvGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            relevantGates
                .Where(id => byId[id].Status != SvGameplayRuntimeProfileGateStatus.Verified)
                .ToArray());
    }

    private static SvGameplayRuntimeProfileGate CandidateGate(
        SvGameplayRuntimeProfileGateId id,
        bool verified,
        string rejectionReason)
    {
        return new SvGameplayRuntimeProfileGate(
            id,
            verified
                ? SvGameplayRuntimeProfileGateStatus.Verified
                : SvGameplayRuntimeProfileGateStatus.Rejected,
            verified ? "verified" : rejectionReason);
    }

    private static SvGameplayRuntimeProfileGate VerifiedGate(
        SvGameplayRuntimeProfileGateId id)
    {
        return new SvGameplayRuntimeProfileGate(
            id,
            SvGameplayRuntimeProfileGateStatus.Verified,
            "verified");
    }

    private static SvGameplayRuntimeProfileGate MissingGate(
        SvGameplayRuntimeProfileGateId id,
        string reason)
    {
        return new SvGameplayRuntimeProfileGate(
            id,
            SvGameplayRuntimeProfileGateStatus.MissingProof,
            reason);
    }

    private static TargetWindow[] CreateTargetWindows()
    {
        var targets = new[]
        {
            Target(
                "lua-callback-research-locator",
                0x004C550,
                "FD7BBEA9F30B00F9FD0300910800D0D2",
                SvGameplayRuntimeTargetClass.ResearchOnly,
                0x58,
                "0D358535705053BD9993B7283DAD9E5D7E63DE57C592584624E39B9809E1E789"),
            Target(
                "normal-experience-calculator",
                0x01781CE8,
                "FFC300D1FD7B02A9FD8300913F0000B9",
                SvGameplayRuntimeTargetClass.CandidateHook,
                0x26C,
                "1893C43E6E4AE6FD358DEB26BB585A91F6EAE54247B8CF4CC915054514C41D1E"),
            Target(
                "normal-experience-calculator-call",
                0x01781B80,
                "5A000094",
                SvGameplayRuntimeTargetClass.InstrumentationOnly),
            Target(
                "normal-experience-calculator-wrapper",
                0x017813A4,
                "FF0302D1FD7B06A9FD830191F44F07A9",
                SvGameplayRuntimeTargetClass.SemanticChain),
            Target(
                "normal-experience-wrapper-caller-a",
                0x0113FC20,
                "E1051994",
                SvGameplayRuntimeTargetClass.InstrumentationOnly),
            Target(
                "normal-experience-wrapper-caller-b",
                0x0192DC1C,
                "E24DF997",
                SvGameplayRuntimeTargetClass.InstrumentationOnly),
            Target(
                "share-recipient-function",
                0x01141B64,
                "FD7BBAA9FC6F01A9FD030091FA6702A9",
                SvGameplayRuntimeTargetClass.SemanticChain,
                0x110,
                "E9E456DBD5373D2BB48E8992D6085FE6B9BBFBFF152EEE6243DB2EAF64C92484"),
            Target(
                "share-recipient-caller",
                0x01141A54,
                "44000094",
                SvGameplayRuntimeTargetClass.InstrumentationOnly),
            Target(
                "share-inline-decision",
                0x01141BA0,
                "1F1C0072",
                SvGameplayRuntimeTargetClass.CandidateHook),
        };

        if (targets.Any(target =>
                target.TextOffset < 0
                || target.ExpectedBytes.Length == 0
                || target.ExpectedBytes.Length % sizeof(uint) != 0
                || target.FunctionFingerprint is { ByteLength: <= 0 })
            || targets.Select(target => target.TargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets.Select(target => target.TextOffset).Distinct().Count() != targets.Length)
        {
            throw new InvalidOperationException(
                "The Scarlet and Violet gameplay runtime target inventory is malformed or ambiguous.");
        }

        return targets;
    }

    private static TargetWindow Target(
        string targetId,
        int textOffset,
        string expectedHex,
        SvGameplayRuntimeTargetClass targetClass,
        int? functionByteLength = null,
        string? functionSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(targetId)
            || targetId.Length > 96
            || targetId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-'))
        {
            throw new InvalidOperationException(
                "A Scarlet and Violet gameplay runtime target ID is invalid.");
        }

        if (functionByteLength.HasValue != (functionSha256 is not null))
        {
            throw new InvalidOperationException(
                "A Scarlet and Violet function fingerprint is incomplete.");
        }

        var fingerprint = functionByteLength is not null
            ? new FunctionFingerprint(
                functionByteLength.Value,
                Convert.FromHexString(functionSha256!))
            : null;
        if (fingerprint is not null && fingerprint.Sha256.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidOperationException(
                "A Scarlet and Violet function fingerprint hash is invalid.");
        }

        return new TargetWindow(
            targetId,
            textOffset,
            Convert.FromHexString(expectedHex),
            targetClass,
            fingerprint);
    }

    private sealed record TargetWindow(
        string TargetId,
        int TextOffset,
        byte[] ExpectedBytes,
        SvGameplayRuntimeTargetClass TargetClass,
        FunctionFingerprint? FunctionFingerprint);

    private sealed record FunctionFingerprint(
        int ByteLength,
        byte[] Sha256);
}
