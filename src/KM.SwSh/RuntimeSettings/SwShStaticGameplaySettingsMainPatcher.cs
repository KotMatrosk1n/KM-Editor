// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;

namespace KM.SwSh.RuntimeSettings;

public enum SwShStaticGameplaySettingsFeature
{
    ExperienceShare,
    ExperienceRate,
    LevelCap,
}

public enum SwShStaticGameplaySettingsMainKind
{
    Vanilla,
    Configured,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

public sealed record SwShStaticGameplaySettingsFeatureAssessment(
    SwShStaticGameplaySettingsFeature Feature,
    bool Available,
    string EffectScope,
    string? UnavailableReason);

public sealed record SwShStaticGameplaySettingsRequest(
    bool ExperienceShareEnabled,
    uint ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap);

public sealed record SwShStaticGameplaySettingsMainAnalysis(
    SwShStaticGameplaySettingsMainKind Kind,
    string Message,
    ProjectGame? DetectedGame,
    string BuildId,
    bool? ExperienceShareEnabled,
    uint? ExperienceRateBasisPoints,
    bool LevelCapEnabled,
    byte LevelCap,
    IReadOnlyList<SwShStaticGameplaySettingsFeatureAssessment> Features);

/// <summary>
/// Applies exact-build, reversible Sword/Shield 1.3.2 static gameplay patches.
/// EXP Share, EXP rate, and the bounded level cap affect the verified battle/catch
/// award path only. The cap clamps the amount before both the local award and its
/// replicated command; candy, Camp, Poke Jobs, and scripted awards are unchanged.
/// </summary>
public static class SwShStaticGameplaySettingsMainPatcher
{
    public const int MinimumExperienceRateBasisPoints = 0;
    public const int MaximumExperienceRateBasisPoints = 50_000;
    public const int ExperienceRateStepBasisPoints = 1_000;
    public const int VanillaExperienceRateBasisPoints = 10_000;
    public const byte MinimumLevelCap = 1;
    public const byte VanillaLevelCap = 100;

    private const int MaximumRawMainBytes = 64 * 1024 * 1024;
    private const int MaximumDecompressedSegmentBytes = 64 * 1024 * 1024;
    private const int MaximumTotalDecompressedBytes = 96 * 1024 * 1024;
    private const uint ExpectedNsoVersion = 0;
    private const uint ExpectedNsoFlags = 0x3F;
    private const int RoMemoryOffset = 0x01901000;
    private const int RoLength = 0x00BD9168;
    private const int DataMemoryOffset = 0x024DB000;
    private const int DataLength = 0x0015AF38;

    private const int ShareFunctionWindowOffset = 0x007FB2B0;
    private const int ShareFunctionWindowLength = 0x30;
    private const int ShareInstructionOffset = 0x007FB2C0;
    private const int SharePatchLength = 0x04;
    private const int ShareCallerWindowOffset = 0x008A54D0;
    private const int ShareCallerWindowLength = 0x1B0;
    private const int RateCallOffset = 0x008A5648;
    private const int RatePatchOffset = 0x008A564C;
    private const int RateCalculatorOffset = 0x008A5A00;
    private const int RateCalculatorLength = 0x1D4;
    private const int LevelCapAwardWindowOffset = 0x0083A3E0;
    private const int LevelCapAwardWindowLength = 0x44;
    private const int LevelCapHookOffset = 0x0083A3E0;
    private const int LevelCapContinuationOffset = 0x0083A3E4;
    private const int LevelCapGrowthThresholdOffset = 0x007EC4A0;
    private const int LevelCapGrowthThresholdWindowLength = 0x24;
    private const int CodeChunkLength = 0x0C;
    private const int GeneralRateChunkCount = 8;
    private const int ZeroRateChunkCount = 1;
    private const int LevelCapProgramChunkCount = 17;
    private const int CodeCaveSearchStart = 0x008A0000;

    private const uint VanillaShareInstruction = 0x320003E0; // ORR W0, WZR, #1
    private const uint DisabledShareInstruction = 0x2A1F03E0; // MOV W0, WZR
    private const uint RetInstruction = 0xD65F03C0;
    private const uint NopInstruction = 0xD503201F;
    private const uint VanillaRateCallInstruction = 0x940000EE;
    private const uint VanillaRateLoadInstruction = 0xB94003E8; // LDR W8, [SP]
    private const uint VanillaLevelCapAwardInstruction = 0xF81D0FF5; // STR X21, [SP, #-0x30]!

    private const string SwordBuildId =
        "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000";
    private const string ShieldBuildId =
        "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000";
    private const string SwordTextSha256 =
        "82A40B8D3C334DBA1B5D9D1F2F99D5BA6A04D0B1953A069C39C3E9097264A37E";
    private const string ShieldTextSha256 =
        "88F4650C736FB61E1D0C624BFDFF1718C87A286F2686C7C3CBB9378301F8EB28";
    private const string ShareFunctionSha256 =
        "555A8B51F118B6DE76D98936AAAE2B970F7BB66D293A30CBB9AE4267CDEA5018";
    private const string SwordShareCallerSha256 =
        "1C51978F42C833B736062521977385142E021A4415F61864A9EF1BCE750BA5C4";
    private const string ShieldShareCallerSha256 =
        "45D51B2DE32239283AF20D69E3D48BA1B4C46889C751884C29DA90CA7351ACBE";
    private const string SwordCalculatorSha256 =
        "D05FB8B0B21828E444CB3FB968708A4A2E36901960DD830EBB1893B71C02D925";
    private const string ShieldCalculatorSha256 =
        "D7B5FA00A9A6138A0D353E5D019A6978F986E0497F7D23C00568A6D728186756";
    private const string LevelCapAwardWindowSha256 =
        "31D668DCC8DB722CA8CEC57E28A15B7EF4EC1FEC706677CF63F8DB4FB6DB858C";
    private const string LevelCapGrowthThresholdSha256 =
        "168FD413C0F4D61F83FCE70FF1BEA54F56374091B2400BEEC1281610F9C9F0CE";

    private static readonly SwShStaticGameplaySettingsFeatureAssessment[] FeatureInventory =
    [
        new(
            SwShStaticGameplaySettingsFeature.ExperienceShare,
            Available: true,
            "Battle and catch recipients using the sole verified retail Share decision caller.",
            UnavailableReason: null),
        new(
            SwShStaticGameplaySettingsFeature.ExperienceRate,
            Available: true,
            "Final battle and catch EXP awards after the retail calculator and its modifiers.",
            UnavailableReason: null),
        new(
            SwShStaticGameplaySettingsFeature.LevelCap,
            Available: true,
            "Battle and catch EXP only. The award is clamped before local application and replication so it cannot cross the selected level; candy, Camp, Poke Jobs, and scripted awards are unchanged.",
            UnavailableReason: null),
    ];

    public static IReadOnlyList<SwShStaticGameplaySettingsFeatureAssessment> Features =>
        FeatureInventory;

    public static SwShStaticGameplaySettingsMainAnalysis Analyze(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);

        try
        {
            EnsureSupportedExpectedGame(expectedGame);
            var buildId = ReadBoundedBuildId(baseMainBytes, "base");
            var layout = FindLayout(baseMainBytes.AsSpan(0x40, 0x20));
            if (layout is null)
            {
                return Failure(
                    SwShStaticGameplaySettingsMainKind.UnsupportedBuild,
                    "Beta Gameplay Settings supports Sword and Shield 1.3.2 exefs/main files only.",
                    detectedGame: null,
                    buildId);
            }

            if (expectedGame is not null && layout.Game != expectedGame.Value)
            {
                return Failure(
                    SwShStaticGameplaySettingsMainKind.GameMismatch,
                    $"Selected {FormatGame(expectedGame.Value)}, but the base exefs/main is {layout.Label}.",
                    layout.Game,
                    buildId);
            }

            var baseNso = ParseBoundedNso(baseMainBytes, "base", layout);
            var currentNso = ParseBoundedNso(currentMainBytes, "current", layout);
            ValidateRequiredSegmentHashes(baseNso);
            ValidateRequiredSegmentHashes(currentNso);
            EnsureSameExecutableEnvelope(baseNso, currentNso);
            var baseText = baseNso.Text.DecompressedData;
            var currentText = currentNso.Text.DecompressedData;
            ValidateBasePreimages(baseText, layout);
            EnsureWindow(currentText, ShareFunctionWindowOffset, ShareFunctionWindowLength);
            EnsureWindow(currentText, ShareCallerWindowOffset, ShareCallerWindowLength);
            EnsureWindow(currentText, RateCalculatorOffset, RateCalculatorLength);

            var shareInstruction = ReadInstruction(currentText, ShareInstructionOffset);
            var shareEnabled = shareInstruction switch
            {
                VanillaShareInstruction => true,
                DisabledShareInstruction => false,
                _ => throw new InvalidDataException(
                    "The EXP Share decision function contains an unrecognized instruction."),
            };
            if (ReadInstruction(currentText, ShareInstructionOffset + sizeof(uint)) != RetInstruction)
            {
                throw new InvalidDataException(
                    "The EXP Share decision function no longer has its exact return instruction.");
            }

            ValidateNormalizedWindow(
                currentText,
                ShareFunctionWindowOffset,
                ShareFunctionWindowLength,
                ShareInstructionOffset,
                VanillaShareInstruction,
                ShareFunctionSha256,
                "EXP Share function");

            var rate = AnalyzeRatePatch(baseText, currentText, layout.Game);
            ValidateNormalizedWindow(
                currentText,
                ShareCallerWindowOffset,
                ShareCallerWindowLength,
                RatePatchOffset,
                VanillaRateLoadInstruction,
                layout.ShareCallerSha256,
                "battle/catch award caller");
            ValidateHash(
                currentText,
                RateCalculatorOffset,
                RateCalculatorLength,
                layout.CalculatorSha256,
                "battle/catch EXP calculator");
            var levelCap = AnalyzeLevelCapPatch(baseText, currentText, layout.Game);
            ValidateNormalizedWindow(
                currentText,
                LevelCapAwardWindowOffset,
                LevelCapAwardWindowLength,
                LevelCapHookOffset,
                VanillaLevelCapAwardInstruction,
                LevelCapAwardWindowSha256,
                "battle/catch award wrapper");

            var kind = shareEnabled
                    && rate.BasisPoints == VanillaExperienceRateBasisPoints
                    && !levelCap.Enabled
                ? SwShStaticGameplaySettingsMainKind.Vanilla
                : SwShStaticGameplaySettingsMainKind.Configured;
            return new SwShStaticGameplaySettingsMainAnalysis(
                kind,
                kind == SwShStaticGameplaySettingsMainKind.Vanilla
                    ? "The verified battle/catch gameplay settings are vanilla."
                    : "The verified battle/catch gameplay settings contain a recognized KM static patch.",
                layout.Game,
                buildId,
                shareEnabled,
                rate.BasisPoints,
                levelCap.Enabled,
                levelCap.Cap,
                FeatureInventory);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            return Failure(
                SwShStaticGameplaySettingsMainKind.Conflict,
                exception.Message,
                detectedGame: null,
                buildId: "unknown");
        }
    }

    public static byte[] Apply(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        SwShStaticGameplaySettingsRequest request,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var initial = Analyze(baseMainBytes, currentMainBytes, expectedGame);
        if (initial.Kind is SwShStaticGameplaySettingsMainKind.UnsupportedBuild
            or SwShStaticGameplaySettingsMainKind.GameMismatch
            or SwShStaticGameplaySettingsMainKind.Conflict)
        {
            throw new InvalidDataException(initial.Message);
        }

        _ = ReadBoundedBuildId(baseMainBytes, "base");
        var layout = FindLayout(baseMainBytes.AsSpan(0x40, 0x20))
            ?? throw new InvalidDataException("The base exefs/main build is unsupported.");
        var baseNso = ParseBoundedNso(baseMainBytes, "base", layout);
        var currentNso = ParseBoundedNso(currentMainBytes, "current", layout);
        ValidateRequiredSegmentHashes(baseNso);
        ValidateRequiredSegmentHashes(currentNso);
        var baseText = baseNso.Text.DecompressedData;
        var beforeText = currentNso.Text.DecompressedData;
        var text = beforeText.ToArray();
        var previousRate = AnalyzeRatePatch(baseText, text, layout.Game);
        var previousLevelCap = AnalyzeLevelCapPatch(baseText, text, layout.Game);
        if (request.LevelCapEnabled)
        {
            ValidateHash(
                text,
                LevelCapGrowthThresholdOffset,
                LevelCapGrowthThresholdWindowLength,
                LevelCapGrowthThresholdSha256,
                "battle/catch level-cap growth-threshold helper");
        }

        baseText.AsSpan(ShareInstructionOffset, SharePatchLength)
            .CopyTo(text.AsSpan(ShareInstructionOffset, SharePatchLength));
        baseText.AsSpan(RatePatchOffset, sizeof(uint))
            .CopyTo(text.AsSpan(RatePatchOffset, sizeof(uint)));
        baseText.AsSpan(LevelCapHookOffset, sizeof(uint))
            .CopyTo(text.AsSpan(LevelCapHookOffset, sizeof(uint)));
        foreach (var caveOffset in previousRate.CaveOffsets)
        {
            baseText.AsSpan(caveOffset, CodeChunkLength)
                .CopyTo(text.AsSpan(caveOffset, CodeChunkLength));
        }
        foreach (var caveOffset in previousLevelCap.CaveOffsets)
        {
            baseText.AsSpan(caveOffset, CodeChunkLength)
                .CopyTo(text.AsSpan(caveOffset, CodeChunkLength));
        }

        if (!request.ExperienceShareEnabled)
        {
            WriteInstruction(text, ShareInstructionOffset, DisabledShareInstruction);
        }

        var newRateCaves = Array.Empty<int>();
        if (request.ExperienceRateBasisPoints != VanillaExperienceRateBasisPoints)
        {
            var requiredChunks = request.ExperienceRateBasisPoints == 0
                ? ZeroRateChunkCount
                : GeneralRateChunkCount;
            newRateCaves = AllocateCodeCaves(
                baseText,
                text,
                layout.Game,
                requiredChunks);
            WriteRateProgram(text, newRateCaves, request.ExperienceRateBasisPoints);
            WriteInstruction(
                text,
                RatePatchOffset,
                EncodeBranchLink(RatePatchOffset, newRateCaves[0]));
        }

        var newLevelCapCaves = Array.Empty<int>();
        if (request.LevelCapEnabled)
        {
            newLevelCapCaves = AllocateLevelCapCodeCaves(
                baseText,
                text,
                layout.Game);
            WriteLevelCapProgram(text, newLevelCapCaves, request.LevelCap);
            WriteInstruction(
                text,
                LevelCapHookOffset,
                EncodeBranch(LevelCapHookOffset, newLevelCapCaves[0]));
        }

        var output = currentNso.Write(textDecompressedData: text);
        ValidateOutput(
            baseNso,
            currentNso,
            output,
            request,
            expectedGame,
            previousRate.CaveOffsets
                .Concat(previousLevelCap.CaveOffsets)
                .Concat(newRateCaves)
                .Concat(newLevelCapCaves)
                .ToHashSet());
        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] baseMainBytes,
        byte[] currentMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(currentMainBytes);

        var current = Analyze(baseMainBytes, currentMainBytes, expectedGame);
        if (current.Kind is not (SwShStaticGameplaySettingsMainKind.Vanilla
            or SwShStaticGameplaySettingsMainKind.Configured)
            || current.ExperienceShareEnabled is null
            || current.ExperienceRateBasisPoints is null)
        {
            throw new InvalidDataException(current.Message);
        }

        var cleanBase = Analyze(baseMainBytes, baseMainBytes, expectedGame);
        if (cleanBase.Kind != SwShStaticGameplaySettingsMainKind.Vanilla)
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings restore requires the exact clean selected-game 1.3.2 base exefs/main.");
        }

        var exactOwnedOutput = Apply(
            baseMainBytes,
            baseMainBytes,
            new SwShStaticGameplaySettingsRequest(
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
            new SwShStaticGameplaySettingsRequest(
                ExperienceShareEnabled: true,
                VanillaExperienceRateBasisPoints,
                LevelCapEnabled: false,
                VanillaLevelCap),
            expectedGame);
    }

    private static RatePatchAnalysis AnalyzeRatePatch(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        ProjectGame game)
    {
        if (ReadInstruction(currentText, RateCallOffset) != VanillaRateCallInstruction)
        {
            throw new InvalidDataException(
                "The battle/catch EXP calculator call no longer has its exact retail instruction.");
        }

        var instruction = ReadInstruction(currentText, RatePatchOffset);
        if (instruction == VanillaRateLoadInstruction)
        {
            return new RatePatchAnalysis(VanillaExperienceRateBasisPoints, []);
        }

        if (!TryDecodeBranchLink(instruction, RatePatchOffset, out var firstCave))
        {
            throw new InvalidDataException(
                "The battle/catch EXP result site contains an unrecognized patch.");
        }

        if (IsExactZeroRateProgram(baseText, currentText, firstCave, game))
        {
            return new RatePatchAnalysis(0, [firstCave]);
        }

        var caves = FollowGeneralRateProgram(baseText, currentText, firstCave, game);
        var rateInstruction = ReadInstruction(currentText, caves[1]);
        if (!TryDecodeMovzImmediate32(rateInstruction, register: 9, out var basisPoints)
            || !IsValidRate((uint)basisPoints)
            || basisPoints == 0)
        {
            throw new InvalidDataException(
                "The installed battle/catch EXP rate constant is invalid.");
        }

        ValidateGeneralRateProgram(currentText, caves, (uint)basisPoints);
        if (basisPoints == VanillaExperienceRateBasisPoints)
        {
            throw new InvalidDataException(
                "The installed battle/catch EXP rate hook encodes the vanilla 100% rate through a noncanonical patch.");
        }

        return new RatePatchAnalysis((uint)basisPoints, caves);
    }

    private static LevelCapPatchAnalysis AnalyzeLevelCapPatch(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        ProjectGame game)
    {
        var instruction = ReadInstruction(currentText, LevelCapHookOffset);
        if (instruction == VanillaLevelCapAwardInstruction)
        {
            return new LevelCapPatchAnalysis(
                Enabled: false,
                VanillaLevelCap,
                CaveOffsets: []);
        }

        if (!TryDecodeBranch(instruction, LevelCapHookOffset, out var firstCave))
        {
            throw new InvalidDataException(
                "The battle/catch level-cap award wrapper contains an unrecognized patch.");
        }

        var caves = FollowLevelCapProgram(baseText, currentText, firstCave, game);
        if (!TryDecodeMovzImmediate32(
                ReadInstruction(currentText, caves[3]),
                register: 8,
                out var cap)
            || cap is < MinimumLevelCap or > VanillaLevelCap)
        {
            throw new InvalidDataException(
                "The installed battle/catch level-cap constant is invalid.");
        }

        ValidateLevelCapProgram(currentText, caves, (byte)cap);
        ValidateHash(
            currentText,
            LevelCapGrowthThresholdOffset,
            LevelCapGrowthThresholdWindowLength,
            LevelCapGrowthThresholdSha256,
            "battle/catch level-cap growth-threshold helper");
        return new LevelCapPatchAnalysis(
            Enabled: true,
            (byte)cap,
            caves);
    }

    private static int[] FollowLevelCapProgram(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        int firstCave,
        ProjectGame game)
    {
        var caves = new int[LevelCapProgramChunkCount];
        caves[0] = firstCave;
        for (var index = 0; index <= 3; index++)
        {
            ValidateOwnedCave(baseText, currentText, caves[index], game);
            if (!TryDecodeBranch(
                    ReadInstruction(currentText, caves[index] + 8),
                    caves[index] + 8,
                    out caves[index + 1]))
            {
                throw new InvalidDataException(
                    "The installed level-cap cave chain contains an invalid branch.");
            }
        }

        ValidateOwnedCave(baseText, currentText, caves[4], game);
        if (!TryDecodeBranch(
                ReadInstruction(currentText, caves[4] + 8),
                caves[4] + 8,
                out caves[5]))
        {
            throw new InvalidDataException(
                "The installed level-cap decision chunk contains an invalid continuation branch.");
        }

        for (var index = 5; index < caves.Length - 1; index++)
        {
            ValidateOwnedCave(baseText, currentText, caves[index], game);
            if (!TryDecodeBranch(
                    ReadInstruction(currentText, caves[index] + 8),
                    caves[index] + 8,
                    out caves[index + 1]))
            {
                throw new InvalidDataException(
                    "The installed level-cap cave chain contains an invalid branch.");
            }
        }

        ValidateOwnedCave(baseText, currentText, caves[^1], game);
        if (caves.Distinct().Count() != caves.Length)
        {
            throw new InvalidDataException(
                "The installed level-cap cave chain contains a cycle.");
        }

        return caves;
    }

    private static void ValidateLevelCapProgram(
        ReadOnlySpan<byte> text,
        IReadOnlyList<int> caves,
        byte cap)
    {
        ExpectLevelCapLinkedChunk(text, caves, 0,
            0xA9BD7BFD, // STP X29, X30, [SP, #-0x30]!
            0xA90153F3); // STP X19, X20, [SP, #0x10]
        ExpectLevelCapLinkedChunk(text, caves, 1,
            0xA9025BF5, // STP X21, X22, [SP, #0x20]
            EncodeMovRegister64(19, 0));
        ExpectLevelCapLinkedChunk(text, caves, 2,
            EncodeMovRegister32(20, 1),
            EncodeMovRegister32(21, 2));
        ExpectLevelCapLinkedChunk(text, caves, 3,
            EncodeMovzImmediate32(8, cap),
            NopInstruction);

        if (ReadInstruction(text, caves[4]) != EncodeCmpImmediate32(8, VanillaLevelCap)
            || ReadInstruction(text, caves[4] + 4)
                != EncodeConditionalBranch(
                    caves[4] + 4,
                    caves[13],
                    Arm64Condition.EQ)
            || ReadInstruction(text, caves[4] + 8)
                != EncodeBranch(caves[4] + 8, caves[5]))
        {
            throw new InvalidDataException(
                "The installed level-cap decision chunk does not match the KM instruction sequence.");
        }

        ExpectLevelCapLinkedChunk(text, caves, 5,
            0xF9401260, // LDR X0, [X19, #0x20]
            0xF9400800); // LDR X0, [X0, #0x10]
        ExpectLevelCapLinkedChunk(text, caves, 6,
            0xF9400400, // LDR X0, [X0, #8]
            0x8B340C08); // ADD X8, X0, W20, UXTB #3
        ExpectLevelCapLinkedChunk(text, caves, 7,
            0xF9409116, // LDR X22, [X8, #0x120]
            0x7940E2C0); // LDRH W0, [X22, #0x70]
        ExpectLevelCapLinkedChunk(text, caves, 8,
            0x394CF6C1, // LDRB W1, [X22, #0x33D]
            EncodeMovzImmediate32(2, cap + 1));
        ExpectLevelCapLinkedChunk(text, caves, 9,
            EncodeBranchLink(caves[9], LevelCapGrowthThresholdOffset),
            0x51000400); // SUB W0, W0, #1
        ExpectLevelCapLinkedChunk(text, caves, 10,
            0xB9406EC8, // LDR W8, [X22, #0x6C]
            0x6B080009); // SUBS W9, W0, W8
        ExpectLevelCapLinkedChunk(text, caves, 11,
            EncodeConditionalSelect32(9, 31, 9, Arm64Condition.LS),
            0x6B0902BF); // CMP W21, W9
        ExpectLevelCapLinkedChunk(text, caves, 12,
            EncodeConditionalSelect32(21, 21, 9, Arm64Condition.LS),
            NopInstruction);
        ExpectLevelCapLinkedChunk(text, caves, 13,
            EncodeMovRegister64(0, 19),
            EncodeMovRegister32(1, 20));
        ExpectLevelCapLinkedChunk(text, caves, 14,
            EncodeMovRegister32(2, 21),
            0xA9425BF5); // LDP X21, X22, [SP, #0x20]
        ExpectLevelCapLinkedChunk(text, caves, 15,
            0xA94153F3, // LDP X19, X20, [SP, #0x10]
            0xA8C37BFD); // LDP X29, X30, [SP], #0x30

        if (ReadInstruction(text, caves[16]) != VanillaLevelCapAwardInstruction
            || ReadInstruction(text, caves[16] + 4)
                != EncodeBranch(caves[16] + 4, LevelCapContinuationOffset)
            || ReadInstruction(text, caves[16] + 8) != NopInstruction)
        {
            throw new InvalidDataException(
                "The installed level-cap return chunk does not match the KM instruction sequence.");
        }
    }

    private static void ExpectLevelCapLinkedChunk(
        ReadOnlySpan<byte> text,
        IReadOnlyList<int> caves,
        int index,
        uint first,
        uint second)
    {
        if (ReadInstruction(text, caves[index]) != first
            || ReadInstruction(text, caves[index] + 4) != second
            || ReadInstruction(text, caves[index] + 8)
                != EncodeBranch(caves[index] + 8, caves[index + 1]))
        {
            throw new InvalidDataException(
                "The installed level-cap cave program does not match the KM instruction sequence.");
        }
    }

    private static bool IsExactZeroRateProgram(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        int caveOffset,
        ProjectGame game)
    {
        ValidateOwnedCave(baseText, currentText, caveOffset, game);
        return ReadInstruction(currentText, caveOffset) == VanillaRateLoadInstruction
            && ReadInstruction(currentText, caveOffset + 4) == EncodeMovRegister32(8, 31)
            && ReadInstruction(currentText, caveOffset + 8) == RetInstruction;
    }

    private static int[] FollowGeneralRateProgram(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        int firstCave,
        ProjectGame game)
    {
        var caves = new int[GeneralRateChunkCount];
        caves[0] = firstCave;
        for (var index = 0; index < caves.Length; index++)
        {
            ValidateOwnedCave(baseText, currentText, caves[index], game);
            if (caves.Take(index).Contains(caves[index]))
            {
                throw new InvalidDataException("The installed EXP rate cave chain contains a cycle.");
            }

            if (index == caves.Length - 1)
            {
                continue;
            }

            var branch = ReadInstruction(currentText, caves[index] + 8);
            if (!TryDecodeBranch(branch, caves[index] + 8, out caves[index + 1]))
            {
                throw new InvalidDataException(
                    "The installed EXP rate cave chain contains an invalid branch.");
            }
        }

        return caves;
    }

    private static void ValidateGeneralRateProgram(
        ReadOnlySpan<byte> text,
        IReadOnlyList<int> caves,
        uint basisPoints)
    {
        ExpectChunk(text, caves, 0,
            VanillaRateLoadInstruction,
            EncodeMovRegister32(10, 8));
        ExpectChunk(text, caves, 1,
            EncodeMovzImmediate32(9, (int)basisPoints),
            EncodeUmull(8, 8, 9));
        ExpectChunk(text, caves, 2,
            EncodeMovzImmediate32(9, VanillaExperienceRateBasisPoints),
            EncodeUdiv64(8, 8, 9));
        ExpectChunk(text, caves, 3,
            EncodeMovzImmediate32(9, 0xFFFF),
            EncodeMovkImmediate32(9, 0xFFFF, 16));
        ExpectChunk(text, caves, 4,
            EncodeCmpRegister64(8, 9),
            EncodeConditionalSelect64(8, 8, 9, Arm64Condition.LS));
        ExpectChunk(text, caves, 5,
            EncodeCmpImmediate32(8, 0),
            EncodeMovzImmediate32(9, 1));
        ExpectChunk(text, caves, 6,
            EncodeConditionalSelect32(8, 8, 9, Arm64Condition.NE),
            EncodeCmpImmediate32(10, 0));
        ExpectFinalChunk(text, caves[7],
            EncodeConditionalSelect32(8, 31, 8, Arm64Condition.EQ),
            RetInstruction,
            NopInstruction);
    }

    private static void ExpectChunk(
        ReadOnlySpan<byte> text,
        IReadOnlyList<int> caves,
        int index,
        uint first,
        uint second)
    {
        if (ReadInstruction(text, caves[index]) != first
            || ReadInstruction(text, caves[index] + 4) != second
            || ReadInstruction(text, caves[index] + 8)
                != EncodeBranch(caves[index] + 8, caves[index + 1]))
        {
            throw new InvalidDataException(
                "The installed EXP rate cave program does not match the KM instruction sequence.");
        }
    }

    private static void ExpectFinalChunk(
        ReadOnlySpan<byte> text,
        int offset,
        uint first,
        uint second,
        uint third)
    {
        if (ReadInstruction(text, offset) != first
            || ReadInstruction(text, offset + 4) != second
            || ReadInstruction(text, offset + 8) != third)
        {
            throw new InvalidDataException(
                "The installed EXP rate cave program has an invalid return chunk.");
        }
    }

    private static void WriteRateProgram(
        byte[] text,
        IReadOnlyList<int> caves,
        uint basisPoints)
    {
        if (basisPoints == 0)
        {
            WriteChunk(
                text,
                caves[0],
                VanillaRateLoadInstruction,
                EncodeMovRegister32(8, 31),
                RetInstruction);
            return;
        }

        WriteLinkedChunk(text, caves, 0,
            VanillaRateLoadInstruction,
            EncodeMovRegister32(10, 8));
        WriteLinkedChunk(text, caves, 1,
            EncodeMovzImmediate32(9, (int)basisPoints),
            EncodeUmull(8, 8, 9));
        WriteLinkedChunk(text, caves, 2,
            EncodeMovzImmediate32(9, VanillaExperienceRateBasisPoints),
            EncodeUdiv64(8, 8, 9));
        WriteLinkedChunk(text, caves, 3,
            EncodeMovzImmediate32(9, 0xFFFF),
            EncodeMovkImmediate32(9, 0xFFFF, 16));
        WriteLinkedChunk(text, caves, 4,
            EncodeCmpRegister64(8, 9),
            EncodeConditionalSelect64(8, 8, 9, Arm64Condition.LS));
        WriteLinkedChunk(text, caves, 5,
            EncodeCmpImmediate32(8, 0),
            EncodeMovzImmediate32(9, 1));
        WriteLinkedChunk(text, caves, 6,
            EncodeConditionalSelect32(8, 8, 9, Arm64Condition.NE),
            EncodeCmpImmediate32(10, 0));
        WriteChunk(text, caves[7],
            EncodeConditionalSelect32(8, 31, 8, Arm64Condition.EQ),
            RetInstruction,
            NopInstruction);
    }

    private static void WriteLevelCapProgram(
        byte[] text,
        IReadOnlyList<int> caves,
        byte cap)
    {
        if (caves.Count != LevelCapProgramChunkCount
            || cap is < MinimumLevelCap or > VanillaLevelCap)
        {
            throw new InvalidDataException(
                "The battle/catch level-cap program request is invalid.");
        }

        WriteLinkedChunk(text, caves, 0,
            0xA9BD7BFD, // STP X29, X30, [SP, #-0x30]!
            0xA90153F3); // STP X19, X20, [SP, #0x10]
        WriteLinkedChunk(text, caves, 1,
            0xA9025BF5, // STP X21, X22, [SP, #0x20]
            EncodeMovRegister64(19, 0));
        WriteLinkedChunk(text, caves, 2,
            EncodeMovRegister32(20, 1),
            EncodeMovRegister32(21, 2));
        WriteLinkedChunk(text, caves, 3,
            EncodeMovzImmediate32(8, cap),
            NopInstruction);
        WriteChunk(text, caves[4],
            EncodeCmpImmediate32(8, VanillaLevelCap),
            EncodeConditionalBranch(caves[4] + 4, caves[13], Arm64Condition.EQ),
            EncodeBranch(caves[4] + 8, caves[5]));
        WriteLinkedChunk(text, caves, 5,
            0xF9401260, // LDR X0, [X19, #0x20]
            0xF9400800); // LDR X0, [X0, #0x10]
        WriteLinkedChunk(text, caves, 6,
            0xF9400400, // LDR X0, [X0, #8]
            0x8B340C08); // ADD X8, X0, W20, UXTB #3
        WriteLinkedChunk(text, caves, 7,
            0xF9409116, // LDR X22, [X8, #0x120]
            0x7940E2C0); // LDRH W0, [X22, #0x70]
        WriteLinkedChunk(text, caves, 8,
            0x394CF6C1, // LDRB W1, [X22, #0x33D]
            EncodeMovzImmediate32(2, cap + 1));
        WriteLinkedChunk(text, caves, 9,
            EncodeBranchLink(caves[9], LevelCapGrowthThresholdOffset),
            0x51000400); // SUB W0, W0, #1
        WriteLinkedChunk(text, caves, 10,
            0xB9406EC8, // LDR W8, [X22, #0x6C]
            0x6B080009); // SUBS W9, W0, W8
        WriteLinkedChunk(text, caves, 11,
            EncodeConditionalSelect32(9, 31, 9, Arm64Condition.LS),
            0x6B0902BF); // CMP W21, W9
        WriteLinkedChunk(text, caves, 12,
            EncodeConditionalSelect32(21, 21, 9, Arm64Condition.LS),
            NopInstruction);
        WriteLinkedChunk(text, caves, 13,
            EncodeMovRegister64(0, 19),
            EncodeMovRegister32(1, 20));
        WriteLinkedChunk(text, caves, 14,
            EncodeMovRegister32(2, 21),
            0xA9425BF5); // LDP X21, X22, [SP, #0x20]
        WriteLinkedChunk(text, caves, 15,
            0xA94153F3, // LDP X19, X20, [SP, #0x10]
            0xA8C37BFD); // LDP X29, X30, [SP], #0x30
        WriteChunk(text, caves[16],
            VanillaLevelCapAwardInstruction,
            EncodeBranch(caves[16] + 4, LevelCapContinuationOffset),
            NopInstruction);
    }

    private static void WriteLinkedChunk(
        byte[] text,
        IReadOnlyList<int> caves,
        int index,
        uint first,
        uint second)
    {
        WriteChunk(
            text,
            caves[index],
            first,
            second,
            EncodeBranch(caves[index] + 8, caves[index + 1]));
    }

    private static void WriteChunk(
        byte[] text,
        int offset,
        uint first,
        uint second,
        uint third)
    {
        WriteInstruction(text, offset, first);
        WriteInstruction(text, offset + 4, second);
        WriteInstruction(text, offset + 8, third);
    }

    private static int[] AllocateCodeCaves(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        ProjectGame game,
        int count)
    {
        var caves = new List<int>(count);
        FindCodeCaves(baseText, currentText, game, CodeCaveSearchStart, baseText.Length, caves, count);
        if (caves.Count < count)
        {
            FindCodeCaves(baseText, currentText, game, 0, CodeCaveSearchStart, caves, count);
        }

        if (caves.Count != count)
        {
            throw new InvalidDataException(
                $"Could not find {count.ToString(CultureInfo.InvariantCulture)} unclaimed 12-byte code caves for Beta Gameplay Settings.");
        }

        return caves.ToArray();
    }

    private static int[] AllocateLevelCapCodeCaves(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        ProjectGame game)
    {
        const int candidateLimitPerSearchArea = 512;
        var candidates = new List<int>(candidateLimitPerSearchArea);
        FindCodeCaves(
            baseText,
            currentText,
            game,
            CodeCaveSearchStart,
            baseText.Length,
            candidates,
            candidateLimitPerSearchArea);
        if (TrySelectLevelCapCodeCaves(candidates, out var selected))
        {
            return selected;
        }

        candidates.Clear();
        FindCodeCaves(
            baseText,
            currentText,
            game,
            0,
            CodeCaveSearchStart,
            candidates,
            candidateLimitPerSearchArea);
        if (TrySelectLevelCapCodeCaves(candidates, out selected))
        {
            return selected;
        }

        throw new InvalidDataException(
            "Could not find a bounded set of unclaimed 12-byte code caves for the battle/catch level-cap patch.");
    }

    private static bool TrySelectLevelCapCodeCaves(
        IReadOnlyList<int> candidates,
        out int[] selected)
    {
        selected = [];
        for (var start = 0; start <= candidates.Count - LevelCapProgramChunkCount; start++)
        {
            var decisionBranchOffset = candidates[start + 4] + sizeof(uint);
            var restoreOffset = candidates[start + 13];
            var delta = restoreOffset - decisionBranchOffset;
            var immediate = delta >> 2;
            if ((delta & 3) != 0
                || immediate < -(1 << 18)
                || immediate >= 1 << 18)
            {
                continue;
            }

            selected = candidates
                .Skip(start)
                .Take(LevelCapProgramChunkCount)
                .ToArray();
            return true;
        }

        return false;
    }

    private static void FindCodeCaves(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        ProjectGame game,
        int startOffset,
        int endOffset,
        ICollection<int> caves,
        int requiredCount)
    {
        var reservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game);
        for (var offset = Math.Max(0, startOffset); offset + CodeChunkLength <= endOffset; offset += 4)
        {
            if (caves.Count == requiredCount)
            {
                return;
            }

            if (baseText.Slice(offset, CodeChunkLength).IndexOfAnyExcept((byte)0) >= 0
                || currentText.Slice(offset, CodeChunkLength).IndexOfAnyExcept((byte)0) >= 0
                || reservations.Any(region =>
                    SwShExeFsReservedRegionLedger.Overlaps(region, offset, CodeChunkLength)))
            {
                continue;
            }

            caves.Add(offset);
            offset += CodeChunkLength - 4;
        }
    }

    private static void ValidateOwnedCave(
        ReadOnlySpan<byte> baseText,
        ReadOnlySpan<byte> currentText,
        int caveOffset,
        ProjectGame game)
    {
        EnsureWindow(baseText, caveOffset, CodeChunkLength);
        EnsureWindow(currentText, caveOffset, CodeChunkLength);
        if ((caveOffset & 3) != 0
            || baseText.Slice(caveOffset, CodeChunkLength).IndexOfAnyExcept((byte)0) >= 0
            || SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(game)
                .Any(region => SwShExeFsReservedRegionLedger.Overlaps(
                    region,
                    caveOffset,
                    CodeChunkLength)))
        {
            throw new InvalidDataException(
                "The installed gameplay-settings branch does not target a KM-owned base-zero code cave.");
        }
    }

    private static void ValidateBasePreimages(ReadOnlySpan<byte> baseText, PatchLayout layout)
    {
        ValidateHash(
            baseText,
            0,
            baseText.Length,
            layout.TextSha256,
            "canonical base .text segment");
        ValidateHash(
            baseText,
            ShareFunctionWindowOffset,
            ShareFunctionWindowLength,
            ShareFunctionSha256,
            "base EXP Share function");
        ValidateHash(
            baseText,
            ShareCallerWindowOffset,
            ShareCallerWindowLength,
            layout.ShareCallerSha256,
            "base battle/catch award caller");
        ValidateHash(
            baseText,
            RateCalculatorOffset,
            RateCalculatorLength,
            layout.CalculatorSha256,
            "base battle/catch EXP calculator");
        ValidateHash(
            baseText,
            LevelCapAwardWindowOffset,
            LevelCapAwardWindowLength,
            LevelCapAwardWindowSha256,
            "base battle/catch award wrapper");
        ValidateHash(
            baseText,
            LevelCapGrowthThresholdOffset,
            LevelCapGrowthThresholdWindowLength,
            LevelCapGrowthThresholdSha256,
            "base battle/catch level-cap growth-threshold helper");
        if (ReadInstruction(baseText, ShareInstructionOffset) != VanillaShareInstruction
            || ReadInstruction(baseText, ShareInstructionOffset + 4) != RetInstruction
            || ReadInstruction(baseText, RateCallOffset) != VanillaRateCallInstruction
            || ReadInstruction(baseText, RatePatchOffset) != VanillaRateLoadInstruction
            || ReadInstruction(baseText, LevelCapHookOffset)
                != VanillaLevelCapAwardInstruction)
        {
            throw new InvalidDataException(
                "The base executable does not contain the exact retail gameplay preimages.");
        }
    }

    private static void ValidateNormalizedWindow(
        ReadOnlySpan<byte> text,
        int offset,
        int length,
        int normalizedInstructionOffset,
        uint normalizedInstruction,
        string expectedSha256,
        string label)
    {
        EnsureWindow(text, offset, length);
        var normalized = text.Slice(offset, length).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            normalized.AsSpan(normalizedInstructionOffset - offset),
            normalizedInstruction);
        var actual = Convert.ToHexString(SHA256.HashData(normalized));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The {label} context does not match its exact 1.3.2 preimage.");
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
            throw new InvalidDataException($"The {label} does not match its exact 1.3.2 preimage.");
        }
    }

    private static void ValidateOutput(
        NsoFile baseNso,
        NsoFile beforeNso,
        byte[] output,
        SwShStaticGameplaySettingsRequest request,
        ProjectGame? expectedGame,
        IReadOnlySet<int> caveOffsets)
    {
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("The base exefs/main build is unsupported.");
        var afterNso = ParseBoundedNso(output, "generated", layout);
        ValidateRequiredSegmentHashes(afterNso);
        EnsureSameExecutableEnvelope(beforeNso, afterNso);
        EnsurePreservedSegment(beforeNso.Ro, afterNso.Ro, ".ro");
        EnsurePreservedSegment(beforeNso.Data, afterNso.Data, ".data");

        var before = beforeNso.Text.DecompressedData;
        var after = afterNso.Text.DecompressedData;
        if (before.Length != after.Length)
        {
            throw new InvalidDataException("Beta Gameplay Settings changed the .text segment length.");
        }

        var ownedRanges = caveOffsets
            .Select(cave => new OwnedRange(cave, CodeChunkLength))
            .Append(new OwnedRange(ShareInstructionOffset, SharePatchLength))
            .Append(new OwnedRange(RatePatchOffset, sizeof(uint)))
            .Append(new OwnedRange(LevelCapHookOffset, sizeof(uint)))
            .OrderBy(range => range.Offset)
            .ToArray();
        var cursor = 0;
        foreach (var range in ownedRanges)
        {
            if (range.Offset < cursor
                || range.Offset > before.Length - range.Length)
            {
                throw new InvalidDataException(
                    "Beta Gameplay Settings produced an invalid or overlapping owned range.");
            }

            if (!before.AsSpan(cursor, range.Offset - cursor)
                .SequenceEqual(after.AsSpan(cursor, range.Offset - cursor)))
            {
                throw new InvalidDataException(
                    "Beta Gameplay Settings changed bytes outside its owned .text ranges.");
            }

            cursor = range.Offset + range.Length;
        }

        if (!before.AsSpan(cursor).SequenceEqual(after.AsSpan(cursor)))
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings changed bytes outside its owned .text ranges.");
        }

        var analysis = Analyze(baseNso.Write(), output, expectedGame);
        if (analysis.Kind is SwShStaticGameplaySettingsMainKind.UnsupportedBuild
            or SwShStaticGameplaySettingsMainKind.GameMismatch
            or SwShStaticGameplaySettingsMainKind.Conflict
            || analysis.ExperienceShareEnabled != request.ExperienceShareEnabled
            || analysis.ExperienceRateBasisPoints != request.ExperienceRateBasisPoints
            || analysis.LevelCapEnabled != request.LevelCapEnabled
            || analysis.LevelCap != request.LevelCap)
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings output did not round-trip to the requested supported values.");
        }
    }

    private static void EnsurePreservedSegment(
        NsoSegment before,
        NsoSegment after,
        string label)
    {
        if (!before.DecompressedData.AsSpan().SequenceEqual(after.DecompressedData))
        {
            throw new InvalidDataException($"Beta Gameplay Settings changed the {label} segment.");
        }
    }

    private static void ValidateRequest(SwShStaticGameplaySettingsRequest request)
    {
        if (!IsValidRate(request.ExperienceRateBasisPoints))
        {
            throw new InvalidDataException(
                "The battle/catch EXP rate must be 0 through 500 percent in 10 percent steps.");
        }

        if (request.LevelCapEnabled
            && request.LevelCap is < MinimumLevelCap or > VanillaLevelCap)
        {
            throw new InvalidDataException(
                "The Sword/Shield battle/catch level cap must be 1 through 100.");
        }

        if (!request.LevelCapEnabled && request.LevelCap != VanillaLevelCap)
        {
            throw new InvalidDataException(
                "A disabled Sword/Shield level cap must use the canonical vanilla value 100.");
        }
    }

    private static bool IsValidRate(uint basisPoints)
    {
        return basisPoints <= MaximumExperienceRateBasisPoints
            && basisPoints % ExperienceRateStepBasisPoints == 0;
    }

    private static void EnsureSameExecutableEnvelope(NsoFile expected, NsoFile actual)
    {
        var expectedEnvelope = NormalizeExecutableEnvelope(expected);
        var actualEnvelope = NormalizeExecutableEnvelope(actual);
        if (!expectedEnvelope.AsSpan().SequenceEqual(actualEnvelope))
        {
            throw new InvalidDataException(
                "The base and current exefs/main files do not share the exact executable segment envelope.");
        }
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
        // Every other header byte is part of the executable's exact segment envelope.
        foreach (var offset in new[] { 0x10, 0x20, 0x30 })
        {
            envelope.AsSpan(offset, sizeof(int)).Clear();
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
        PatchLayout layout)
    {
        _ = ReadBoundedBuildId(mainBytes, label);
        var bytes = mainBytes.AsSpan();
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x04, sizeof(uint)))
                != ExpectedNsoVersion
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0x0C, sizeof(uint)))
                != ExpectedNsoFlags
            || !bytes.Slice(0x40, 0x20).SequenceEqual(Convert.FromHexString(layout.BuildId)))
        {
            throw new InvalidDataException(
                $"The {label} exefs/main does not match the exact supported executable envelope.");
        }

        var expectedSegments = new[]
        {
            (MemoryOffset: 0, DecompressedSize: layout.TextLength),
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
            if (fileOffset < NsoFile.HeaderSize
                || fileOffset < priorFileEnd
                || memoryOffset != expectedSegments[index].MemoryOffset
                || decompressedSize != expectedSegments[index].DecompressedSize
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

    private static PatchLayout? FindLayout(ReadOnlySpan<byte> buildId)
    {
        var value = Convert.ToHexString(buildId);
        return value switch
        {
            SwordBuildId => new PatchLayout(
                ProjectGame.Sword,
                "Pokemon Sword 1.3.2",
                SwordBuildId,
                0x01900F30,
                SwordTextSha256,
                SwordShareCallerSha256,
                SwordCalculatorSha256),
            ShieldBuildId => new PatchLayout(
                ProjectGame.Shield,
                "Pokemon Shield 1.3.2",
                ShieldBuildId,
                0x01900FC0,
                ShieldTextSha256,
                ShieldShareCallerSha256,
                ShieldCalculatorSha256),
            _ => null,
        };
    }

    private static SwShStaticGameplaySettingsMainAnalysis Failure(
        SwShStaticGameplaySettingsMainKind kind,
        string message,
        ProjectGame? detectedGame,
        string buildId)
    {
        return new SwShStaticGameplaySettingsMainAnalysis(
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

    private static void EnsureSupportedExpectedGame(ProjectGame? game)
    {
        if (game is not null and not ProjectGame.Sword and not ProjectGame.Shield)
        {
            throw new InvalidDataException(
                "Beta Gameplay Settings requires a Pokemon Sword or Pokemon Shield project.");
        }
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

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        return EncodeBranchCore(0x14000000, sourceOffset, targetOffset);
    }

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        return EncodeBranchCore(0x94000000, sourceOffset, targetOffset);
    }

    private static uint EncodeBranchCore(uint opcode, int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("A gameplay-settings branch target is not 4-byte aligned.");
        }

        var immediate = delta >> 2;
        if (immediate < -(1 << 25) || immediate >= 1 << 25)
        {
            throw new InvalidDataException("A gameplay-settings branch target is outside ARM64 range.");
        }

        return opcode | (uint)(immediate & 0x03FFFFFF);
    }

    private static uint EncodeConditionalBranch(
        int sourceOffset,
        int targetOffset,
        Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException(
                "A gameplay-settings conditional branch target is not 4-byte aligned.");
        }

        var immediate = delta >> 2;
        if (immediate < -(1 << 18) || immediate >= 1 << 18)
        {
            throw new InvalidDataException(
                "A gameplay-settings conditional branch target is outside ARM64 range.");
        }

        return 0x54000000u
            | (uint)((immediate & 0x7FFFF) << 5)
            | (uint)((int)condition & 0xF);
    }

    private static bool TryDecodeBranch(uint instruction, int sourceOffset, out int targetOffset)
    {
        return TryDecodeBranchCore(instruction, 0x14000000, sourceOffset, out targetOffset);
    }

    private static bool TryDecodeBranchLink(uint instruction, int sourceOffset, out int targetOffset)
    {
        return TryDecodeBranchCore(instruction, 0x94000000, sourceOffset, out targetOffset);
    }

    private static bool TryDecodeBranchCore(
        uint instruction,
        uint opcode,
        int sourceOffset,
        out int targetOffset)
    {
        targetOffset = 0;
        if ((instruction & 0xFC000000) != opcode)
        {
            return false;
        }

        var immediate = SignExtend((int)(instruction & 0x03FFFFFF), 26) << 2;
        targetOffset = sourceOffset + immediate;
        return true;
    }

    private static uint EncodeMovRegister32(int destination, int source)
    {
        return 0x2A0003E0u
            | (uint)((source & 0x1F) << 16)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeMovRegister64(int destination, int source)
    {
        return 0xAA0003E0u
            | (uint)((source & 0x1F) << 16)
            | (uint)(destination & 0x1F);
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("An EXP rate MOVZ immediate is out of range.");
        }

        return 0x52800000u
            | (uint)(immediate << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovkImmediate32(int register, int immediate, int shift)
    {
        if (immediate is < 0 or > 0xFFFF || shift is not (0 or 16))
        {
            throw new InvalidDataException("An EXP rate MOVK immediate is out of range.");
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

    private static uint EncodeCmpRegister64(int left, int right)
    {
        return 0xEB00001Fu
            | (uint)((right & 0x1F) << 16)
            | (uint)((left & 0x1F) << 5);
    }

    private static uint EncodeCmpImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFF)
        {
            throw new InvalidDataException("An EXP rate CMP immediate is out of range.");
        }

        return 0x7100001Fu
            | (uint)(immediate << 10)
            | (uint)((register & 0x1F) << 5);
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

    private static int SignExtend(int value, int bitCount)
    {
        var shift = 32 - bitCount;
        return value << shift >> shift;
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string Label,
        string BuildId,
        int TextLength,
        string TextSha256,
        string ShareCallerSha256,
        string CalculatorSha256);

    private sealed record RatePatchAnalysis(
        uint BasisPoints,
        IReadOnlyList<int> CaveOffsets);

    private sealed record LevelCapPatchAnalysis(
        bool Enabled,
        byte Cap,
        IReadOnlyList<int> CaveOffsets);

    private readonly record struct OwnedRange(int Offset, int Length);

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        LS = 9,
    }
}
