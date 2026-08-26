// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Core.RuntimeSettings;
using KM.Formats.Executable;

namespace KM.ZA.RuntimeSettings;

public enum ZaGameplayRuntimeProfileGateId
{
    CandidateTitleId,
    CandidateUpdateVersion,
    CandidateFullBuildId,
    CandidateRawMainIdentity,
    CandidateNsoEnvelope,
    CandidateTargetWindows,
    FixedRuntimeSlotPolicy,
    BundleIdentityContract,
    SettingsEnvelopeContract,
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
    OutputArtifactIdentities,
    BundleHandshakeRuntimeCanary,
    CurrentProcessHandleAbi,
    MemoryAliasAndCacheAbi,
    SettingsFilesystemAbi,
    SettingsJournalRuntimeCanary,
    NativePageLifecycleAbi,
    NativeInputRenderLifecycleCanary,
    HookPublicationLifecycleCanary,
    ExperienceShareCallbackAbi,
    ExperienceShareRuntimeCanary,
    ExperienceRateCallbackAbi,
    ExperienceRateRuntimeCanary,
    LevelCapHelperAbis,
    LevelCapSourceCensus,
    CandyLifecycleCanary,
    EmulatorLifecycleCanary,
    HardwareLifecycleCanary,
}

public enum ZaGameplayRuntimeProfileGateStatus
{
    Verified,
    MissingProof,
    Rejected,
}

public enum ZaGameplayRuntimeTargetClass
{
    NativePageTemplate,
    CandidateHook,
    InstrumentationOnly,
    SemanticChain,
    ExcludedSemanticWindow,
}

public enum ZaGameplayRuntimeCapabilityId
{
    PackageMaterialization,
    SettingsPersistence,
    NativeSettingsPage,
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public sealed record ZaGameplayRuntimeProfileGate(
    ZaGameplayRuntimeProfileGateId Id,
    ZaGameplayRuntimeProfileGateStatus Status,
    string ReasonCode);

public sealed record ZaGameplayRuntimeTargetWindowResult(
    string TargetId,
    int TextOffset,
    int ByteLength,
    ZaGameplayRuntimeTargetClass TargetClass,
    bool Matches,
    bool PublicationAuthorized);

public sealed record ZaGameplayRuntimeCapabilityAssessment(
    ZaGameplayRuntimeCapabilityId Capability,
    bool Available,
    IReadOnlyList<ZaGameplayRuntimeProfileGateId> BlockingGates);

public sealed record ZaGameplayRuntimeProfileAssessment(
    ulong ExpectedTitleId,
    GameplayBundleVersion ExpectedUpdateVersion,
    string ExpectedFullBuildId,
    bool RecognizedCandidate,
    bool ProfileComplete,
    bool CanMaterialize,
    IReadOnlyList<ZaGameplayRuntimeProfileGate> Gates,
    IReadOnlyList<ZaGameplayRuntimeTargetWindowResult> TargetWindows,
    IReadOnlyList<ZaGameplayRuntimeCapabilityAssessment> Capabilities,
    IReadOnlyList<GameplayDeferredFeatureAssessment> DeferredFeatures);

/// <summary>
/// Validates the exact executable facts that are currently frozen for the Z-A gameplay
/// settings runtime. This class deliberately has no bundle writer. A recognized executable
/// is necessary but cannot authorize a package while any release gate lacks proof.
/// </summary>
public static class ZaGameplayRuntimeProfileValidator
{
    public const ulong TitleId = 0x0100F43008C44000;
    public const string FullBuildId =
        "B1F12FD919EAE86AB8A978317677E64BCE443D1F000000000000000000000000";
    public const string RawMainSha256 =
        "2308530D73B7FEA60BB845AD57D9B1F10D3FF213F89C01F73627DC8224F43EF9";
    public const string RuntimeComponentRelativePath = "exefs/subsdk9";
    public const int ExpectedRawMainLength = 33_970_422;

    public static GameplayBundleVersion UpdateVersion { get; } = new(2, 0, 2);

    private static readonly TargetWindow[] ExpectedTargetWindows = CreateTargetWindows();

    public static ZaGameplayRuntimeProfileAssessment Assess(
        ulong titleId,
        GameplayBundleVersion updateVersion,
        ReadOnlyMemory<byte> rawMainBytes)
    {
        var hasExactLength = rawMainBytes.Length == ExpectedRawMainLength;
        var rawIdentityMatches = hasExactLength
            && string.Equals(
                Convert.ToHexString(SHA256.HashData(rawMainBytes.Span)),
                RawMainSha256,
                StringComparison.Ordinal);
        var embeddedBuildId = rawMainBytes.Length >= NsoFile.HeaderSize
            ? TryReadBuildId(rawMainBytes.Span)
            : null;
        var buildIdMatches = string.Equals(
            embeddedBuildId,
            FullBuildId,
            StringComparison.Ordinal);
        var titleMatches = titleId == TitleId;
        var updateMatches = updateVersion == UpdateVersion;

        NsoFile? nso = null;
        var nsoEnvelopeMatches = false;
        if (rawIdentityMatches && buildIdMatches)
        {
            try
            {
                nso = NsoFile.Parse(rawMainBytes.ToArray());
                nsoEnvelopeMatches = string.Equals(
                    Convert.ToHexString(nso.BuildId),
                    FullBuildId,
                    StringComparison.Ordinal);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or ArgumentException
                    or OverflowException)
            {
                nso = null;
            }
        }

        var targetResults = ExpectedTargetWindows
            .Select(target => ToResult(target, nso?.Text.DecompressedData))
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
            hasExactLength,
            rawIdentityMatches,
            nsoEnvelopeMatches,
            targetWindowsMatch,
            bundleIdentityContractMatches,
            settingsEnvelopeContractMatches);
        var recognizedCandidate = gates
            .Take(6)
            .All(gate => gate.Status == ZaGameplayRuntimeProfileGateStatus.Verified);

        var profileComplete = gates.All(
            gate => gate.Status == ZaGameplayRuntimeProfileGateStatus.Verified);
        var capabilities = CreateCapabilityAssessments(gates);
        return new ZaGameplayRuntimeProfileAssessment(
            TitleId,
            UpdateVersion,
            FullBuildId,
            recognizedCandidate,
            profileComplete,
            profileComplete,
            gates,
            targetResults,
            capabilities,
            GameplayDeferredFeatureCatalog.ForFamily(GameplaySettingsFamily.LegendsZA));
    }

    private static ZaGameplayRuntimeTargetWindowResult ToResult(
        TargetWindow target,
        byte[]? textBytes)
    {
        var matches = textBytes is not null
            && target.TextOffset <= textBytes.Length
            && target.ExpectedBytes.Length <= textBytes.Length - target.TextOffset
            && textBytes.AsSpan(target.TextOffset, target.ExpectedBytes.Length)
                .SequenceEqual(target.ExpectedBytes);
        return new ZaGameplayRuntimeTargetWindowResult(
            target.TargetId,
            target.TextOffset,
            target.ExpectedBytes.Length,
            target.TargetClass,
            matches,
            PublicationAuthorized: false);
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

    private static ZaGameplayRuntimeProfileGate[] CreateGateInventory(
        bool titleMatches,
        bool updateMatches,
        bool buildIdMatches,
        bool hasExactLength,
        bool rawIdentityMatches,
        bool nsoEnvelopeMatches,
        bool targetWindowsMatch,
        bool bundleIdentityContractMatches,
        bool settingsEnvelopeContractMatches)
    {
        return
        [
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateTitleId,
                titleMatches,
                "title-id-mismatch"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateUpdateVersion,
                updateMatches,
                "update-version-mismatch"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateFullBuildId,
                buildIdMatches,
                hasExactLength ? "full-build-id-mismatch" : "raw-main-length-mismatch"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateRawMainIdentity,
                rawIdentityMatches,
                hasExactLength ? "raw-main-identity-mismatch" : "raw-main-length-mismatch"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateNsoEnvelope,
                nsoEnvelopeMatches,
                rawIdentityMatches ? "raw-main-format-invalid" : "raw-main-not-recognized"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.CandidateTargetWindows,
                targetWindowsMatch,
                nsoEnvelopeMatches ? "target-window-mismatch" : "raw-main-not-recognized"),
            VerifiedGate(ZaGameplayRuntimeProfileGateId.FixedRuntimeSlotPolicy),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.BundleIdentityContract,
                bundleIdentityContractMatches,
                "bundle-identity-contract-mismatch"),
            CandidateGate(
                ZaGameplayRuntimeProfileGateId.SettingsEnvelopeContract,
                settingsEnvelopeContractMatches,
                "settings-envelope-contract-mismatch"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.AuthorizedCleanBaseRecord,
                "authorized-clean-base-record-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.SegmentDomainsAndIdentities,
                "segment-domains-and-identities-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.RetailNpdmIdentity,
                "retail-npdm-identity-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.MinimalNpdmFieldDiff,
                "minimal-npdm-field-diff-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.StockModuleInventory,
                "stock-module-inventory-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.RuntimeSlotObservation,
                "runtime-slot-observation-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.OwnershipLedgerComposition,
                "ownership-ledger-composition-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ComposedMainIdentity,
                "composed-main-identity-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.RuntimeArtifactIdentity,
                "runtime-artifact-identity-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ProfileArtifactIdentity,
                "profile-artifact-identity-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.OutputArtifactIdentities,
                "output-artifact-identities-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.BundleHandshakeRuntimeCanary,
                "bundle-handshake-runtime-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.CurrentProcessHandleAbi,
                "current-process-handle-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.MemoryAliasAndCacheAbi,
                "memory-alias-and-cache-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.SettingsFilesystemAbi,
                "settings-filesystem-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary,
                "settings-journal-runtime-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.NativePageLifecycleAbi,
                "native-page-lifecycle-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.NativeInputRenderLifecycleCanary,
                "native-input-render-lifecycle-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary,
                "hook-publication-lifecycle-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ExperienceShareCallbackAbi,
                "experience-share-callback-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary,
                "experience-share-runtime-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ExperienceRateCallbackAbi,
                "experience-rate-callback-abi-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary,
                "experience-rate-runtime-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.LevelCapHelperAbis,
                "level-cap-helper-abis-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.LevelCapSourceCensus,
                "level-cap-source-census-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.CandyLifecycleCanary,
                "candy-lifecycle-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.EmulatorLifecycleCanary,
                "emulator-lifecycle-canary-unavailable"),
            MissingGate(
                ZaGameplayRuntimeProfileGateId.HardwareLifecycleCanary,
                "hardware-lifecycle-canary-unavailable"),
        ];
    }

    private static ZaGameplayRuntimeCapabilityAssessment[] CreateCapabilityAssessments(
        IReadOnlyList<ZaGameplayRuntimeProfileGate> gates)
    {
        var unavailable = gates
            .Where(gate => gate.Status != ZaGameplayRuntimeProfileGateStatus.Verified)
            .Select(gate => gate.Id)
            .ToArray();
        return
        [
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.PackageMaterialization,
                unavailable),
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.SettingsPersistence,
                gates,
                ZaGameplayRuntimeProfileGateId.AuthorizedCleanBaseRecord,
                ZaGameplayRuntimeProfileGateId.RuntimeArtifactIdentity,
                ZaGameplayRuntimeProfileGateId.ProfileArtifactIdentity,
                ZaGameplayRuntimeProfileGateId.OutputArtifactIdentities,
                ZaGameplayRuntimeProfileGateId.BundleHandshakeRuntimeCanary,
                ZaGameplayRuntimeProfileGateId.SettingsFilesystemAbi,
                ZaGameplayRuntimeProfileGateId.SettingsJournalRuntimeCanary),
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.NativeSettingsPage,
                gates,
                ZaGameplayRuntimeProfileGateId.NativePageLifecycleAbi,
                ZaGameplayRuntimeProfileGateId.NativeInputRenderLifecycleCanary,
                ZaGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary),
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.ExperienceShare,
                gates,
                ZaGameplayRuntimeProfileGateId.ExperienceShareCallbackAbi,
                ZaGameplayRuntimeProfileGateId.ExperienceShareRuntimeCanary,
                ZaGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary),
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.ExperienceRate,
                gates,
                ZaGameplayRuntimeProfileGateId.ExperienceRateCallbackAbi,
                ZaGameplayRuntimeProfileGateId.ExperienceRateRuntimeCanary,
                ZaGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary),
            BlockedCapability(
                ZaGameplayRuntimeCapabilityId.LevelCap,
                gates,
                ZaGameplayRuntimeProfileGateId.LevelCapHelperAbis,
                ZaGameplayRuntimeProfileGateId.LevelCapSourceCensus,
                ZaGameplayRuntimeProfileGateId.CandyLifecycleCanary,
                ZaGameplayRuntimeProfileGateId.HookPublicationLifecycleCanary),
        ];
    }

    private static ZaGameplayRuntimeCapabilityAssessment BlockedCapability(
        ZaGameplayRuntimeCapabilityId capability,
        IReadOnlyList<ZaGameplayRuntimeProfileGateId> blockingGates)
    {
        return new ZaGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            blockingGates);
    }

    private static ZaGameplayRuntimeCapabilityAssessment BlockedCapability(
        ZaGameplayRuntimeCapabilityId capability,
        IReadOnlyList<ZaGameplayRuntimeProfileGate> gates,
        params ZaGameplayRuntimeProfileGateId[] relevantGates)
    {
        var byId = gates.ToDictionary(gate => gate.Id);
        var blocking = relevantGates
            .Where(id => byId[id].Status != ZaGameplayRuntimeProfileGateStatus.Verified)
            .ToArray();
        return new ZaGameplayRuntimeCapabilityAssessment(
            capability,
            Available: false,
            blocking);
    }

    private static ZaGameplayRuntimeProfileGate CandidateGate(
        ZaGameplayRuntimeProfileGateId id,
        bool verified,
        string rejectionReason)
    {
        return new ZaGameplayRuntimeProfileGate(
            id,
            verified
                ? ZaGameplayRuntimeProfileGateStatus.Verified
                : ZaGameplayRuntimeProfileGateStatus.Rejected,
            verified ? "verified" : rejectionReason);
    }

    private static ZaGameplayRuntimeProfileGate VerifiedGate(
        ZaGameplayRuntimeProfileGateId id)
    {
        return new ZaGameplayRuntimeProfileGate(
            id,
            ZaGameplayRuntimeProfileGateStatus.Verified,
            "verified");
    }

    private static ZaGameplayRuntimeProfileGate MissingGate(
        ZaGameplayRuntimeProfileGateId id,
        string reason)
    {
        return new ZaGameplayRuntimeProfileGate(
            id,
            ZaGameplayRuntimeProfileGateStatus.MissingProof,
            reason);
    }

    private static TargetWindow[] CreateTargetWindows()
    {
        var targets = new[]
        {
            Target("native-page-registry-primary", 0x007BA9E4, "FF8302D1FD7B08A9F34B00F9FD030291", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-registry-secondary", 0x007BACEC, "FF8302D1FD7B08A9F34B00F9FD030291", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-options-constructor", 0x00B7F770, "FF8301D1FD7B03A9F52300F9F44F05A9", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-registry", 0x007610CC, "FF0306D1FD7B16A9FC4F17A9FD830591", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-builder", 0x0076131C, "FD7BBEA9F30B00F9FD03009100E4006F", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-finalizer", 0x00761574, "091840B9E80300AAC90100351F7D00A9", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-allocator-thunk", 0x02C998C0, "FD7BBEA9F44F01A9FD030091200040F9", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-allocator", 0x02C998FC, "FD7BBDA9F50B00F9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-constructor", 0x02CA0E94, "FD7BBEA9F30B00F9FD030091F30300AA", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-initialize", 0x02CA0EE4, "FFC301D1FD7B05A9F44F06A9FD430191", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-update", 0x02CA10B8, "FFC301D1FD7B03A9F85F04A9F65705A9", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-sequence", 0x02CA1690, "FD7BBEA9F30B00F9FD030091F30300AA", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-activate", 0x02C9F860, "FFC300D1FD7B02A9FD83009100A00491", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("native-page-template-deactivate", 0x02C9F8C4, "FFC300D1FD7B02A9FD83009100A00491", ZaGameplayRuntimeTargetClass.NativePageTemplate),
            Target("battle-award-builder", 0x009A4A70, "FF4302D1E82300FDF92F00F9FDFB04A9", ZaGameplayRuntimeTargetClass.CandidateHook),
            Target("battle-award-calculator", 0x009A4E74, "1F0000B9AB9999528B99B9721F100039", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("battle-award-calculator-call", 0x009A4D14, "E0830091E1030091FF770039E80300BDFF2300B9FF93003952000094", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("experience-share-call-a", 0x009A3FEC, "7F0208EBE2179F1A9F020094", ZaGameplayRuntimeTargetClass.CandidateHook),
            Target("experience-share-call-b", 0x00D735B4, "084143393F03086BE2179F1A2CC5F097", ZaGameplayRuntimeTargetClass.CandidateHook),
            Target("experience-rate-accumulator-a", 0x009A4174, "A81A40B90801170BA81A00B9", ZaGameplayRuntimeTargetClass.CandidateHook),
            Target("experience-rate-accumulator-b", 0x00D73744, "A81A40B90801170BA81A00B9", ZaGameplayRuntimeTargetClass.CandidateHook),
            Target("retail-rate-scalar-a", 0x009A4084, "080140F9015541BD2008201E1700391E", ZaGameplayRuntimeTargetClass.ExcludedSemanticWindow),
            Target("retail-rate-scalar-b", 0x00D73650, "080140F9015941BD2008201E0800391E1F05007117859F1A", ZaGameplayRuntimeTargetClass.ExcludedSemanticWindow),
            Target("additive-experience-core", 0x00864A40, "FD7BBDA9F50B00F9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("additive-experience-tail", 0x00864A8C, "603A40F9349517940100140BE00313AAF50B40F9F44F42A9FD7BC3A8A7570F14", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("additive-experience-wrapper-a", 0x008649D0, "FD7BBDA9F50B00F9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("additive-experience-call-a", 0x008649F4, "602A40F9E103152A11000094602A40F9", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("additive-experience-wrapper-b", 0x00DF58D4, "FD7BBDA9F65701A9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("additive-experience-call-b", 0x00DF5908, "602A40F9E103162A4CBCE997", ZaGameplayRuntimeTargetClass.InstrumentationOnly),
            Target("candy-maximum-quantity", 0x02B3049C, "FD7BBDA9F50B00F9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-apply-coroutine", 0x02B30600, "FF8303D1FD7B0AA9F75B00F9F6570CA9", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-total-award", 0x02B30C4C, "FD7BBDA9F50B00F9F44F02A9FD030091", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-usability", 0x02B3141C, "FD7BBEA9F30B00F9FD030091A8630091", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-level-limit-exception", 0x02B314C4, "FD7BBEA9F44F01A9FD030091F40300AA", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-award-wrapper", 0x028EEB8C, "FF0303D1FD7B09A9F55300F9F44F0BA9", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-inventory-decrement", 0x02D5B4B0, "FF0301D1FD7B02A9F44F03A9FD830091", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-award-call", 0x02B30768, "A00301D114F2669781724439821A41B905F9F69701000014", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-additive-call", 0x028EEBE4, "A0835FF8E103132A080040F9088141F900013FD6", ZaGameplayRuntimeTargetClass.SemanticChain),
            Target("candy-decrement-call", 0x02B307A8, "80C20391A8CFFF97822241B9817244393EAB0894", ZaGameplayRuntimeTargetClass.SemanticChain),
        };

        if (targets.Any(target =>
                target.TextOffset < 0
                || target.ExpectedBytes.Length == 0
                || target.ExpectedBytes.Length % sizeof(uint) != 0)
            || targets.Select(target => target.TargetId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets.Select(target => target.TextOffset).Distinct().Count() != targets.Length)
        {
            throw new InvalidOperationException(
                "The Z-A gameplay runtime target inventory is malformed or ambiguous.");
        }

        return targets;
    }

    private static TargetWindow Target(
        string targetId,
        int textOffset,
        string expectedHex,
        ZaGameplayRuntimeTargetClass targetClass)
    {
        if (string.IsNullOrWhiteSpace(targetId)
            || targetId.Length > 96
            || targetId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-'))
        {
            throw new InvalidOperationException(
                "A Z-A gameplay runtime target ID is invalid.");
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
        ZaGameplayRuntimeTargetClass TargetClass);
}
