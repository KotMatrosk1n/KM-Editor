// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.RuntimeSettings;
using KM.Formats.Executable;

namespace KM.SwSh.RuntimeSettings;

public enum SwShGameplayRuntimeEdition
{
    Sword,
    Shield,
}

public enum SwShGameplayRuntimeProfileGateId
{
    CandidateTitleId,
    CandidateUpdateVersion,
    CandidateFullBuildId,
    CandidateNsoEnvelope,
    CandidateTargetWindows,
    CandidateRawMainIdentity,
    FixedRuntimeSlotPolicy,
    BundleIdentityContract,
    SettingsEnvelopeContract,
    MenuArtifactEnvelopeContract,
    AuthorizedCleanBaseRecord,
    SegmentDomainsAndIdentities,
    RetailNpdmIdentity,
    MinimalNpdmFieldDiff,
    StockModuleInventory,
    RuntimeSlotObservation,
    OwnershipLedgerComposition,
    ComposedMainIdentity,
    RuntimeArtifactIdentity,
    ProfileArtifactIdentity,
    MenuArtifactIdentity,
    MessageArtifactIdentities,
    OutputArtifactIdentities,
    BundleHandshakeRuntimeCanary,
    CurrentProcessHandleAbi,
    MemoryAliasAndCacheAbi,
    SettingsFilesystemImportAbis,
    SettingsFilesystemMountOwnershipCanary,
    SettingsFilesystemDirectoryCanary,
    SettingsJournalRuntimeCanary,
    MenuNativeRegistrationAbi,
    MenuVmLifecycleCanary,
    MenuParserAndInvocationCanary,
    MenuSafeEntryLifecycleCanary,
    MenuInteractionAndLocalizationCanary,
    HookPublicationLifecycleCanary,
    ExperienceShareHookAbi,
    ExperienceShareRuntimeCanary,
    ExperienceRateHookAbi,
    ExperienceRateRuntimeCanary,
    LevelCapGrowthHelperAbi,
    LevelCapMutationHook,
    CandyConsumptionCanary,
    ExperienceSourceCensus,
    EmulatorLifecycleCanary,
    HardwareLifecycleCanary,
}

public enum SwShGameplayRuntimeProfileGateStatus
{
    Verified,
    MissingProof,
    Rejected,
}

public enum SwShGameplayRuntimeTargetClass
{
    NativeMenuRegistration,
    CandidateHook,
    InstrumentationOnly,
}

public enum SwShGameplayRuntimeCapabilityId
{
    PackageMaterialization,
    SettingsPersistence,
    NativeSettingsPage,
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public sealed record SwShGameplayRuntimeExpectedProfile(
    SwShGameplayRuntimeEdition Edition,
    ulong TitleId,
    GameplayBundleVersion UpdateVersion,
    string FullBuildId);

public sealed record SwShGameplayRuntimeProfileGate(
    SwShGameplayRuntimeProfileGateId Id,
    SwShGameplayRuntimeProfileGateStatus Status,
    string ReasonCode);

public sealed record SwShGameplayRuntimeTargetWindowResult(
    string TargetId,
    int TextOffset,
    int ByteLength,
    SwShGameplayRuntimeTargetClass TargetClass,
    bool Matches,
    bool PublicationAuthorized);

public sealed record SwShGameplayRuntimeCapabilityAssessment(
    SwShGameplayRuntimeCapabilityId Capability,
    bool Available,
    IReadOnlyList<SwShGameplayRuntimeProfileGateId> BlockingGates);

public sealed record SwShGameplayRuntimeProfileAssessment(
    SwShGameplayRuntimeExpectedProfile ExpectedProfile,
    bool StaticEvidenceMatches,
    bool ProfileComplete,
    bool CanMaterialize,
    IReadOnlyList<SwShGameplayRuntimeProfileGate> Gates,
    IReadOnlyList<SwShGameplayRuntimeTargetWindowResult> TargetWindows,
    IReadOnlyList<SwShGameplayRuntimeCapabilityAssessment> Capabilities,
    IReadOnlyList<GameplayDeferredFeatureAssessment> DeferredFeatures);

/// <summary>
/// Validates the exact edition-specific executable facts currently frozen for the
/// Sword and Shield gameplay settings runtime. Raw executable authority and every
/// runtime gate remain separate, so this class cannot emit a package or authorize a hook.
/// </summary>
public static class SwShGameplayRuntimeProfileValidator
{
    public const string RuntimeComponentRelativePath = "exefs/subsdk9";
    public const string DeterministicMenuEnvelopeSha256 =
        "BD7F103B480793AAC31D2F2D07628CF684B1148578A73308710DBE1398198BC7";
    public const int MaximumRawMainBytes = 128 * 1024 * 1024;
    public const int MaximumDecompressedSegmentBytes = 128 * 1024 * 1024;
    public const int MaximumTotalDecompressedBytes = 256 * 1024 * 1024;

    private static readonly GameplayBundleVersion ExpectedUpdateVersion = new(1, 3, 2);
    private static readonly SwShGameplayRuntimeExpectedProfile SwordProfile = new(
        SwShGameplayRuntimeEdition.Sword,
        0x0100ABF008968000,
        ExpectedUpdateVersion,
        "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000");
    private static readonly SwShGameplayRuntimeExpectedProfile ShieldProfile = new(
        SwShGameplayRuntimeEdition.Shield,
        0x01008DB008C2C000,
        ExpectedUpdateVersion,
        "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000");

    public static SwShGameplayRuntimeExpectedProfile GetExpectedProfile(
        SwShGameplayRuntimeEdition edition)
    {
        return edition switch
        {
            SwShGameplayRuntimeEdition.Sword => SwordProfile,
            SwShGameplayRuntimeEdition.Shield => ShieldProfile,
            _ => throw new ArgumentOutOfRangeException(nameof(edition)),
        };
    }

    public static SwShGameplayRuntimeProfileAssessment Assess(
        SwShGameplayRuntimeEdition edition,
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
        var nsoEnvelopeMatches = TryParseBoundedNso(rawMainBytes, out var nso)
            && string.Equals(
                Convert.ToHexString(nso!.BuildId),
                expected.FullBuildId,
                StringComparison.Ordinal);
        var targetResults = CreateTargetWindows(edition)
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
            targetWindowsMatch,
            bundleIdentityContractMatches,
            settingsEnvelopeContractMatches);
        var staticEvidenceMatches = RequiredStaticCandidateGates.All(id =>
            gates.Single(gate => gate.Id == id).Status
                == SwShGameplayRuntimeProfileGateStatus.Verified);
        var profileComplete = gates.All(
            gate => gate.Status == SwShGameplayRuntimeProfileGateStatus.Verified);
        return new SwShGameplayRuntimeProfileAssessment(
            expected,
            staticEvidenceMatches,
            profileComplete,
            profileComplete,
            gates,
            targetResults,
            CreateCapabilityAssessments(gates),
            GameplayDeferredFeatureCatalog.ForFamily(GameplaySettingsFamily.SwordShield));
    }

    private static readonly SwShGameplayRuntimeProfileGateId[] RequiredStaticCandidateGates =
    [
        SwShGameplayRuntimeProfileGateId.CandidateTitleId,
        SwShGameplayRuntimeProfileGateId.CandidateUpdateVersion,
        SwShGameplayRuntimeProfileGateId.CandidateFullBuildId,
        SwShGameplayRuntimeProfileGateId.CandidateNsoEnvelope,
        SwShGameplayRuntimeProfileGateId.CandidateTargetWindows,
    ];

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
            return ValidateSegmentHashes(nso);
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

    private static bool ValidateSegmentHashes(NsoFile nso)
    {
        return ValidateSegmentHash(nso.Text, nso.Flags.HasFlag(NsoFlags.CheckHashText))
            && ValidateSegmentHash(nso.Ro, nso.Flags.HasFlag(NsoFlags.CheckHashRo))
            && ValidateSegmentHash(nso.Data, nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static bool ValidateSegmentHash(NsoSegment segment, bool required)
    {
        return !required || SHA256.HashData(segment.DecompressedData).AsSpan()
            .SequenceEqual(segment.Hash);
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

    private static SwShGameplayRuntimeTargetWindowResult ToResult(
        TargetWindow target,
        byte[]? textBytes)
    {
        var matches = textBytes is not null
            && target.TextOffset <= textBytes.Length
            && target.ExpectedBytes.Length <= textBytes.Length - target.TextOffset
            && textBytes.AsSpan(target.TextOffset, target.ExpectedBytes.Length)
                .SequenceEqual(target.ExpectedBytes);
        return new SwShGameplayRuntimeTargetWindowResult(
            target.TargetId,
            target.TextOffset,
            target.ExpectedBytes.Length,
            target.TargetClass,
            matches,
            PublicationAuthorized: false);
    }

    private static SwShGameplayRuntimeProfileGate[] CreateGateInventory(
        bool titleMatches,
        bool updateMatches,
        bool buildIdMatches,
        bool nsoEnvelopeMatches,
        bool targetWindowsMatch,
        bool bundleIdentityContractMatches,
        bool settingsEnvelopeContractMatches)
    {
        return
        [
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.CandidateTitleId,
                titleMatches,
                "title-id-mismatch"),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.CandidateUpdateVersion,
                updateMatches,
                "update-version-mismatch"),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.CandidateFullBuildId,
                buildIdMatches,
                "full-build-id-mismatch"),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.CandidateNsoEnvelope,
                nsoEnvelopeMatches,
                "nso-envelope-invalid-or-out-of-bounds"),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.CandidateTargetWindows,
                targetWindowsMatch,
                nsoEnvelopeMatches ? "target-window-mismatch" : "nso-envelope-not-recognized"),
            MissingGate(
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                "raw-main-identity-not-independently-frozen"),
            VerifiedGate(SwShGameplayRuntimeProfileGateId.FixedRuntimeSlotPolicy),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.BundleIdentityContract,
                bundleIdentityContractMatches,
                "bundle-identity-contract-mismatch"),
            CandidateGate(
                SwShGameplayRuntimeProfileGateId.SettingsEnvelopeContract,
                settingsEnvelopeContractMatches,
                "settings-envelope-contract-mismatch"),
            VerifiedGate(SwShGameplayRuntimeProfileGateId.MenuArtifactEnvelopeContract),
            MissingGate(SwShGameplayRuntimeProfileGateId.AuthorizedCleanBaseRecord, "authorized-clean-base-record-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.SegmentDomainsAndIdentities, "segment-domains-and-identities-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.RetailNpdmIdentity, "retail-npdm-identity-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MinimalNpdmFieldDiff, "minimal-npdm-field-diff-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.StockModuleInventory, "stock-module-inventory-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.RuntimeSlotObservation, "runtime-slot-observation-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.OwnershipLedgerComposition, "ownership-ledger-composition-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ComposedMainIdentity, "composed-main-identity-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.RuntimeArtifactIdentity, "runtime-artifact-identity-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ProfileArtifactIdentity, "profile-artifact-identity-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuArtifactIdentity, "menu-artifact-identity-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MessageArtifactIdentities, "message-artifact-identities-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.OutputArtifactIdentities, "output-artifact-identities-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.BundleHandshakeRuntimeCanary, "bundle-handshake-runtime-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.CurrentProcessHandleAbi, "current-process-handle-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MemoryAliasAndCacheAbi, "memory-alias-and-cache-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.SettingsFilesystemImportAbis, "settings-filesystem-import-abis-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.SettingsFilesystemMountOwnershipCanary, "settings-filesystem-mount-ownership-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.SettingsFilesystemDirectoryCanary, "settings-filesystem-directory-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary, "settings-journal-runtime-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuNativeRegistrationAbi, "menu-native-registration-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuVmLifecycleCanary, "menu-vm-lifecycle-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuParserAndInvocationCanary, "menu-parser-and-invocation-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuSafeEntryLifecycleCanary, "menu-safe-entry-lifecycle-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.MenuInteractionAndLocalizationCanary, "menu-interaction-and-localization-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary, "hook-publication-lifecycle-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ExperienceShareHookAbi, "experience-share-hook-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary, "experience-share-runtime-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ExperienceRateHookAbi, "experience-rate-hook-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary, "experience-rate-runtime-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.LevelCapGrowthHelperAbi, "level-cap-growth-helper-abi-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.LevelCapMutationHook, "level-cap-mutation-hook-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.CandyConsumptionCanary, "candy-consumption-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.ExperienceSourceCensus, "experience-source-census-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.EmulatorLifecycleCanary, "emulator-lifecycle-canary-unavailable"),
            MissingGate(SwShGameplayRuntimeProfileGateId.HardwareLifecycleCanary, "hardware-lifecycle-canary-unavailable"),
        ];
    }

    private static SwShGameplayRuntimeCapabilityAssessment[] CreateCapabilityAssessments(
        IReadOnlyList<SwShGameplayRuntimeProfileGate> gates)
    {
        var unavailable = gates
            .Where(gate => gate.Status != SwShGameplayRuntimeProfileGateStatus.Verified)
            .Select(gate => gate.Id)
            .ToArray();
        return
        [
            BlockedCapability(SwShGameplayRuntimeCapabilityId.PackageMaterialization, unavailable),
            BlockedCapability(
                SwShGameplayRuntimeCapabilityId.SettingsPersistence,
                gates,
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SwShGameplayRuntimeProfileGateId.RuntimeArtifactIdentity,
                SwShGameplayRuntimeProfileGateId.ProfileArtifactIdentity,
                SwShGameplayRuntimeProfileGateId.OutputArtifactIdentities,
                SwShGameplayRuntimeProfileGateId.SettingsFilesystemImportAbis,
                SwShGameplayRuntimeProfileGateId.SettingsFilesystemMountOwnershipCanary,
                SwShGameplayRuntimeProfileGateId.SettingsFilesystemDirectoryCanary,
                SwShGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary),
            BlockedCapability(
                SwShGameplayRuntimeCapabilityId.NativeSettingsPage,
                gates,
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SwShGameplayRuntimeProfileGateId.MenuArtifactIdentity,
                SwShGameplayRuntimeProfileGateId.MessageArtifactIdentities,
                SwShGameplayRuntimeProfileGateId.MenuNativeRegistrationAbi,
                SwShGameplayRuntimeProfileGateId.MenuVmLifecycleCanary,
                SwShGameplayRuntimeProfileGateId.MenuParserAndInvocationCanary,
                SwShGameplayRuntimeProfileGateId.MenuSafeEntryLifecycleCanary,
                SwShGameplayRuntimeProfileGateId.MenuInteractionAndLocalizationCanary),
            BlockedCapability(
                SwShGameplayRuntimeCapabilityId.ExperienceShare,
                gates,
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SwShGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SwShGameplayRuntimeProfileGateId.ExperienceShareHookAbi,
                SwShGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary),
            BlockedCapability(
                SwShGameplayRuntimeCapabilityId.ExperienceRate,
                gates,
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SwShGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SwShGameplayRuntimeProfileGateId.ExperienceRateHookAbi,
                SwShGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary,
                SwShGameplayRuntimeProfileGateId.ExperienceSourceCensus),
            BlockedCapability(
                SwShGameplayRuntimeCapabilityId.LevelCap,
                gates,
                SwShGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                SwShGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                SwShGameplayRuntimeProfileGateId.LevelCapGrowthHelperAbi,
                SwShGameplayRuntimeProfileGateId.LevelCapMutationHook,
                SwShGameplayRuntimeProfileGateId.CandyConsumptionCanary,
                SwShGameplayRuntimeProfileGateId.ExperienceSourceCensus),
        ];
    }

    private static SwShGameplayRuntimeCapabilityAssessment BlockedCapability(
        SwShGameplayRuntimeCapabilityId capability,
        IReadOnlyList<SwShGameplayRuntimeProfileGateId> blockingGates)
    {
        return new SwShGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            blockingGates);
    }

    private static SwShGameplayRuntimeCapabilityAssessment BlockedCapability(
        SwShGameplayRuntimeCapabilityId capability,
        IReadOnlyList<SwShGameplayRuntimeProfileGate> gates,
        params SwShGameplayRuntimeProfileGateId[] relevantGates)
    {
        var byId = gates.ToDictionary(gate => gate.Id);
        return new SwShGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            relevantGates
                .Where(id => byId[id].Status != SwShGameplayRuntimeProfileGateStatus.Verified)
                .ToArray());
    }

    private static SwShGameplayRuntimeProfileGate CandidateGate(
        SwShGameplayRuntimeProfileGateId id,
        bool verified,
        string rejectionReason)
    {
        return new SwShGameplayRuntimeProfileGate(
            id,
            verified
                ? SwShGameplayRuntimeProfileGateStatus.Verified
                : SwShGameplayRuntimeProfileGateStatus.Rejected,
            verified ? "verified" : rejectionReason);
    }

    private static SwShGameplayRuntimeProfileGate VerifiedGate(
        SwShGameplayRuntimeProfileGateId id)
    {
        return new SwShGameplayRuntimeProfileGate(
            id,
            SwShGameplayRuntimeProfileGateStatus.Verified,
            "verified");
    }

    private static SwShGameplayRuntimeProfileGate MissingGate(
        SwShGameplayRuntimeProfileGateId id,
        string reason)
    {
        return new SwShGameplayRuntimeProfileGate(
            id,
            SwShGameplayRuntimeProfileGateStatus.MissingProof,
            reason);
    }

    private static TargetWindow[] CreateTargetWindows(SwShGameplayRuntimeEdition edition)
    {
        var registrationOffset = edition switch
        {
            SwShGameplayRuntimeEdition.Sword => 0x01464FC0,
            SwShGameplayRuntimeEdition.Shield => 0x01464FF0,
            _ => throw new ArgumentOutOfRangeException(nameof(edition)),
        };
        var targets = new[]
        {
            Target("native-menu-registration", registrationOffset, "F30F1EF8FD7B01A9FD430091F30300AA", SwShGameplayRuntimeTargetClass.NativeMenuRegistration),
            Target("native-menu-array-wrapper", 0x0066CBA0, "008001910200801272030014", SwShGameplayRuntimeTargetClass.NativeMenuRegistration),
            Target("native-menu-low-level-registrar", 0x0066D970, "F85FBCA9F65701A9F44F02A9FD7B03A9", SwShGameplayRuntimeTargetClass.NativeMenuRegistration),
            Target("experience-share-entry", 0x007FB2C0, "E0030032C0035FD6", SwShGameplayRuntimeTargetClass.CandidateHook),
            Target("experience-rate-calculator", 0x008A5A00, "EA0F1CFCE9A3006DF50F00F9F44F02A9", SwShGameplayRuntimeTargetClass.CandidateHook),
            Target("experience-additive-transition", 0x007E4EA0, "F85FBCA9F65701A9F44F02A9FD7B03A9", SwShGameplayRuntimeTargetClass.InstrumentationOnly),
        };
        if (targets.Any(target =>
                target.TextOffset < 0
                || target.ExpectedBytes.Length == 0
                || target.ExpectedBytes.Length % sizeof(uint) != 0)
            || targets.Select(target => target.TargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets.Select(target => target.TextOffset).Distinct().Count() != targets.Length)
        {
            throw new InvalidOperationException(
                "The Sword and Shield gameplay runtime target inventory is malformed or ambiguous.");
        }

        return targets;
    }

    private static TargetWindow Target(
        string targetId,
        int textOffset,
        string expectedHex,
        SwShGameplayRuntimeTargetClass targetClass)
    {
        if (string.IsNullOrWhiteSpace(targetId)
            || targetId.Length > 96
            || targetId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-'))
        {
            throw new InvalidOperationException(
                "A Sword and Shield gameplay runtime target ID is invalid.");
        }

        return new TargetWindow(
            targetId,
            textOffset,
            Convert.FromHexString(expectedHex),
            targetClass);
    }

    private sealed record TargetWindow(
        string TargetId,
        int TextOffset,
        byte[] ExpectedBytes,
        SwShGameplayRuntimeTargetClass TargetClass);
}
