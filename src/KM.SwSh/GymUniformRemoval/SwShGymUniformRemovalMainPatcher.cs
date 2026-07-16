// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.SwSh.GymUniformRemoval;

internal enum SwShGymUniformRemovalInstallKind
{
    NotInstalled,
    InstalledV1,
    InstalledCompatible,
    UnsupportedBuild,
    GameMismatch,
    ForeignPatch,
    Conflict,
}

internal sealed record SwShGymUniformRemovalAnalysis(
    SwShGymUniformRemovalInstallKind Kind,
    string Message,
    string BuildId,
    string PatchOffsetHex,
    string MainHandlerState,
    ProjectGame? DetectedGame);

internal enum SwShGymUniformRemovalIpsArtifactKind
{
    NotPresent,
    Current,
    Legacy,
    Foreign,
    Invalid,
}

internal sealed record SwShGymUniformRemovalIpsAnalysis(
    SwShGymUniformRemovalIpsArtifactKind Kind,
    string Message,
    string BuildId,
    string PatchOffsetHex,
    string ArtifactState,
    ProjectGame DetectedGame);

internal static class SwShGymUniformRemovalMainPatcher
{
    public const int SwordPatchOffset = 0x01472600;
    public const int ShieldPatchOffset = 0x01472630;
    public const int PatchLength = sizeof(uint) * 2;
    public const string SwordIpsFileName = SwordBuildId + ".ips";
    public const string ShieldIpsFileName = ShieldBuildId + ".ips";

    internal const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    internal const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private const uint VanillaUniformHandlerInstruction1 = 0xD0008CE8;
    private const uint VanillaUniformHandlerInstruction2 = 0xB9400833;
    private const uint KmReturnTrueInstruction = 0x320003E0;
    private const uint CompatibleReturnTrueInstruction = 0x52800020;
    private const uint RetInstruction = 0xD65F03C0;
    private static readonly byte[] Ips32Magic = Encoding.ASCII.GetBytes("IPS32");
    private static readonly byte[] Ips32Eof = Encoding.ASCII.GetBytes("EEOF");
    private static readonly byte[] LegacyIpsEof = Encoding.ASCII.GetBytes("EOF");

    private static readonly PatchDefinition[] Definitions =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, SwordPatchOffset),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldPatchOffset),
    ];

    public static SwShGymUniformRemovalAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var buildId = "unknown";
        PatchDefinition? detectedDefinition = null;
        try
        {
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            buildId = FormatBuildId(nso.BuildId);
            var definition = FindDefinition(nso.BuildId);
            if (definition is null)
            {
                return new SwShGymUniformRemovalAnalysis(
                    SwShGymUniformRemovalInstallKind.UnsupportedBuild,
                    "Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    "unknown",
                    "unsupported",
                    DetectedGame: null);
            }

            detectedDefinition = definition;
            var mismatch = CreateGameMismatchAnalysis(definition, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, definition);

            var first = ReadInstruction(text, definition.PatchOffset);
            var second = ReadInstruction(text, definition.PatchOffset + sizeof(uint));
            var offsetLabel = FormatTextOffset(definition.PatchOffset);

            if (first == KmReturnTrueInstruction && second == RetInstruction)
            {
                return new SwShGymUniformRemovalAnalysis(
                    SwShGymUniformRemovalInstallKind.InstalledV1,
                    "Gym Uniform Removal is installed. Gym challenge uniform-change calls return success without changing the outfit.",
                    buildId,
                    offsetLabel,
                    "KM IPS-compatible return-true stub",
                    definition.Game);
            }

            if (first == CompatibleReturnTrueInstruction && second == RetInstruction)
            {
                return new SwShGymUniformRemovalAnalysis(
                    SwShGymUniformRemovalInstallKind.InstalledCompatible,
                    "A compatible Gym Uniform Removal patch is installed. Reinstalling refreshes it to KM's IPS-compatible return-true stub.",
                    buildId,
                    offsetLabel,
                    "compatible return-true stub",
                    definition.Game);
            }

            if (first == VanillaUniformHandlerInstruction1 && second == VanillaUniformHandlerInstruction2)
            {
                return new SwShGymUniformRemovalAnalysis(
                    SwShGymUniformRemovalInstallKind.NotInstalled,
                    "Gym Uniform Removal is not installed. Installing skips the uniform-change handler while leaving gym scripts running normally.",
                    buildId,
                    offsetLabel,
                    "vanilla handler",
                    definition.Game);
            }

            return new SwShGymUniformRemovalAnalysis(
                LooksLikeBranchOrReturn(first) || LooksLikeBranchOrReturn(second)
                    ? SwShGymUniformRemovalInstallKind.ForeignPatch
                    : SwShGymUniformRemovalInstallKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Gym Uniform Removal expected the vanilla handler at {offsetLabel}, but found 0x{first:X8} 0x{second:X8}."),
                buildId,
                offsetLabel,
                "unknown bytes",
                definition.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShGymUniformRemovalAnalysis(
                SwShGymUniformRemovalInstallKind.Conflict,
                exception.Message,
                buildId,
                detectedDefinition is null ? "unknown" : FormatTextOffset(detectedDefinition.PatchOffset),
                "unreadable",
                detectedDefinition?.Game);
        }
    }

    public static byte[] Apply(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
            or SwShGymUniformRemovalInstallKind.GameMismatch
            or SwShGymUniformRemovalInstallKind.ForeignPatch
            or SwShGymUniformRemovalInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var definition = FindDefinition(nso.BuildId)
            ?? throw new InvalidDataException("Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, definition);

        WriteInstruction(text, definition.PatchOffset, KmReturnTrueInstruction);
        WriteInstruction(text, definition.PatchOffset + sizeof(uint), RetInstruction);

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, definition, expectedGame);
        return output;
    }

    public static byte[] CreateIpsPatch(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
            or SwShGymUniformRemovalInstallKind.GameMismatch
            or SwShGymUniformRemovalInstallKind.ForeignPatch
            or SwShGymUniformRemovalInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        var definition = FindDefinition(nso.BuildId)
            ?? throw new InvalidDataException("Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files.");
        return CreateKmIpsPatch(definition);
    }

    public static SwShGymUniformRemovalAnalysis AnalyzeIpsPatch(
        byte[] ipsBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(ipsBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        try
        {
            var artifact = AnalyzeIpsArtifact(ipsBytes, baseMainBytes, expectedGame);
            return new SwShGymUniformRemovalAnalysis(
                artifact.Kind switch
                {
                    SwShGymUniformRemovalIpsArtifactKind.Current => SwShGymUniformRemovalInstallKind.InstalledV1,
                    SwShGymUniformRemovalIpsArtifactKind.Legacy => SwShGymUniformRemovalInstallKind.InstalledCompatible,
                    SwShGymUniformRemovalIpsArtifactKind.Foreign => SwShGymUniformRemovalInstallKind.ForeignPatch,
                    _ => SwShGymUniformRemovalInstallKind.Conflict,
                },
                artifact.Message,
                artifact.BuildId,
                artifact.PatchOffsetHex,
                MainHandlerState: "vanilla",
                DetectedGame: artifact.DetectedGame);
        }
        catch (InvalidDataException)
        {
            return Analyze(baseMainBytes, expectedGame);
        }
    }

    public static SwShGymUniformRemovalIpsAnalysis AnalyzeIpsArtifact(
        byte[] ipsBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(ipsBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind != SwShGymUniformRemovalInstallKind.NotInstalled
            || baseAnalysis.DetectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                baseAnalysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
                    or SwShGymUniformRemovalInstallKind.GameMismatch
                    or SwShGymUniformRemovalInstallKind.ForeignPatch
                    or SwShGymUniformRemovalInstallKind.Conflict
                    ? baseAnalysis.Message
                    : "Gym Uniform Removal IPS inspection requires a selected-game vanilla base exefs/main.");
        }

        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(baseNso);
        var definition = FindDefinition(baseNso.BuildId)
            ?? throw new InvalidDataException("Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files.");

        if (ipsBytes.SequenceEqual(CreateKmIpsPatch(definition)))
        {
            return new SwShGymUniformRemovalIpsAnalysis(
                SwShGymUniformRemovalIpsArtifactKind.Current,
                "Gym Uniform Removal IPS is installed. Eden/Yuzu will patch the uniform-change handler at load time.",
                baseAnalysis.BuildId,
                baseAnalysis.PatchOffsetHex,
                "current",
                definition.Game);
        }

        if (ipsBytes.SequenceEqual(CreateSingleRecordIpsPatch(definition, LegacyIpsEof))
            || ipsBytes.SequenceEqual(CreateSplitRecordIpsPatch(definition, Ips32Eof))
            || ipsBytes.SequenceEqual(CreateSplitRecordIpsPatch(definition, LegacyIpsEof)))
        {
            return new SwShGymUniformRemovalIpsAnalysis(
                SwShGymUniformRemovalIpsArtifactKind.Legacy,
                "A recognized legacy Gym Uniform Removal IPS is installed. Reinstalling refreshes it to the IPS32 EEOF format Eden accepts.",
                baseAnalysis.BuildId,
                baseAnalysis.PatchOffsetHex,
                "legacy",
                definition.Game);
        }

        var looksLikeIps = ipsBytes.Length >= Ips32Magic.Length + LegacyIpsEof.Length
            && ipsBytes.AsSpan(0, Ips32Magic.Length).SequenceEqual(Ips32Magic);
        return new SwShGymUniformRemovalIpsAnalysis(
            looksLikeIps
                ? SwShGymUniformRemovalIpsArtifactKind.Foreign
                : SwShGymUniformRemovalIpsArtifactKind.Invalid,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Gym Uniform Removal found an existing IPS file at {IpsRelativePath(definition.Game)}, but it is not a KM-owned patch."),
            baseAnalysis.BuildId,
            baseAnalysis.PatchOffsetHex,
            looksLikeIps ? "foreign" : "invalid",
            definition.Game);
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind == SwShGymUniformRemovalInstallKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind is not (SwShGymUniformRemovalInstallKind.InstalledV1
            or SwShGymUniformRemovalInstallKind.InstalledCompatible))
        {
            throw new InvalidDataException("Gym Uniform Removal restore requires an installed Gym Uniform Removal stub.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(baseNso, currentNso, "Gym Uniform Removal restore");
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        var definition = FindDefinition(baseNso.BuildId)
            ?? throw new InvalidDataException("Gym Uniform Removal restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        var baseMismatch = CreateGameMismatchAnalysis(definition, expectedGame, baseBuildId);
        if (baseMismatch is not null)
        {
            throw new InvalidDataException(baseMismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Gym Uniform Removal restore requires current and base main NSO files with matching .text sizes.");
        }

        EnsurePatchRange(currentText, definition);
        EnsurePatchRange(baseText, definition);
        EnsureVanillaBase(baseText, definition);

        baseText.AsSpan(definition.PatchOffset, PatchLength).CopyTo(currentText.AsSpan(definition.PatchOffset, PatchLength));
        var output = currentNso.Write(textDecompressedData: currentText);
        ValidateOutput(
            currentMainBytes,
            output,
            definition,
            expectedGame,
            SwShGymUniformRemovalInstallKind.NotInstalled,
            "Gym Uniform Removal restore");
        return output;
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
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "Gym Uniform Removal verification");
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        var kind = Analyze(mainBytes).Kind;
        return kind is SwShGymUniformRemovalInstallKind.InstalledV1
            or SwShGymUniformRemovalInstallKind.InstalledCompatible;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerGymUniformRemoval);
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions(ProjectGame game)
    {
        if (game is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            return [];
        }

        var inactiveGameToken = game == ProjectGame.Sword ? "-shield-" : "-sword-";
        return ReservedMainTextRegions()
            .Where(region => !region.FeatureId.Contains(inactiveGameToken, StringComparison.Ordinal))
            .ToArray();
    }

    public static string IpsRelativePath(ProjectGame game)
    {
        return "exefs/" + IpsFileName(game);
    }

    public static string IpsFileName(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Shield => ShieldIpsFileName,
            ProjectGame.Sword => SwordIpsFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unsupported Gym Uniform Removal game."),
        };
    }

    public static string? TryGetIpsRelativePath(string buildId)
    {
        var definition = FindDefinitionByPrefix(buildId);
        return definition is null ? null : IpsRelativePath(definition.Game);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        PatchDefinition definition,
        ProjectGame? expectedGame,
        SwShGymUniformRemovalInstallKind expectedKind = SwShGymUniformRemovalInstallKind.InstalledV1,
        string operation = "Gym Uniform Removal apply")
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        ValidateRequiredSegmentHashes(before);
        ValidateRequiredSegmentHashes(after);
        EnsureSameBuildAndLayout(before, after, operation);
        VerifyPreservedSegment(before.Ro, after.Ro, ".ro", operation);
        VerifyPreservedSegment(before.Data, after.Data, ".data", operation);

        var beforeText = before.Text.DecompressedData;
        var afterText = after.Text.DecompressedData;
        if (beforeText.Length != afterText.Length)
        {
            throw new InvalidDataException("Gym Uniform Removal patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < beforeText.Length; offset++)
        {
            if (beforeText[offset] != afterText[offset]
                && (offset < definition.PatchOffset || offset >= definition.PatchOffset + PatchLength))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{operation} unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        if (Analyze(output, expectedGame).Kind != expectedKind)
        {
            throw new InvalidDataException($"{operation} verification failed after writing exefs/main.");
        }
    }

    private static void EnsureSameBuildAndLayout(NsoFile left, NsoFile right, string operation)
    {
        if (left.Version != right.Version
            || left.Flags != right.Flags
            || !left.BuildId.SequenceEqual(right.BuildId)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(left.RawHeader, right.RawHeader))
        {
            throw new InvalidDataException(
                $"{operation} requires matching NSO version, flags, stable header metadata, and full 32-byte build identity.");
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
                $"Gym Uniform Removal rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        var first = ReadInstruction(text, definition.PatchOffset);
        var second = ReadInstruction(text, definition.PatchOffset + sizeof(uint));
        if (first != VanillaUniformHandlerInstruction1 || second != VanillaUniformHandlerInstruction2)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Gym Uniform Removal restore expected the vanilla base handler at {FormatTextOffset(definition.PatchOffset)}, but found 0x{first:X8} 0x{second:X8}."));
        }
    }

    private static PatchDefinition? FindDefinition(ReadOnlySpan<byte> buildId)
    {
        foreach (var definition in Definitions)
        {
            if (IsCanonicalBuildId(buildId, definition.BuildId))
            {
                return definition;
            }
        }

        return null;
    }

    private static PatchDefinition? FindDefinitionByPrefix(string buildId)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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

    private static bool IsZero(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static SwShGymUniformRemovalAnalysis? CreateGameMismatchAnalysis(
        PatchDefinition definition,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || definition.Game == expectedGame.Value)
        {
            return null;
        }

        return new SwShGymUniformRemovalAnalysis(
            SwShGymUniformRemovalInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {definition.GameName}. Gym Uniform Removal will not patch this file because Sword and Shield use different patch sites."),
            buildId,
            FormatTextOffset(definition.PatchOffset),
            "game mismatch",
            definition.Game);
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

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        if (definition.PatchOffset < 0 || definition.PatchOffset + PatchLength > text.Length)
        {
            throw new InvalidDataException(
                $"{definition.GameName} Gym Uniform Removal patch range is outside the decompressed .text segment.");
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static bool LooksLikeBranchOrReturn(uint instruction)
    {
        return (instruction & 0x7C000000) == 0x14000000
            || instruction == RetInstruction;
    }

    private static byte[] CreateKmIpsPatch(PatchDefinition definition)
    {
        return CreateSingleRecordIpsPatch(definition, Ips32Eof);
    }

    private static byte[] CreateSingleRecordIpsPatch(PatchDefinition definition, ReadOnlySpan<byte> terminator)
    {
        using var output = new MemoryStream();
        output.Write(Ips32Magic);
        Span<byte> payload = stackalloc byte[PatchLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, KmReturnTrueInstruction);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[sizeof(uint)..], RetInstruction);
        WriteIpsRecord(output, definition.PatchOffset, payload);
        output.Write(terminator);
        return output.ToArray();
    }

    private static byte[] CreateSplitRecordIpsPatch(PatchDefinition definition, ReadOnlySpan<byte> terminator)
    {
        using var output = new MemoryStream();
        output.Write(Ips32Magic);
        WriteIpsRecord(output, definition.PatchOffset, KmReturnTrueInstruction);
        WriteIpsRecord(output, definition.PatchOffset + sizeof(uint), RetInstruction);
        output.Write(terminator);
        return output.ToArray();
    }

    private static void WriteIpsRecord(Stream output, int offset, uint instruction)
    {
        Span<byte> payload = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, instruction);
        WriteIpsRecord(output, offset, payload);
    }

    private static void WriteIpsRecord(Stream output, int offset, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[sizeof(uint) + sizeof(ushort)];
        BinaryPrimitives.WriteUInt32BigEndian(header, unchecked((uint)offset));
        BinaryPrimitives.WriteUInt16BigEndian(header[sizeof(uint)..], checked((ushort)payload.Length));
        output.Write(header);
        output.Write(payload);
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

    private sealed record PatchDefinition(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int PatchOffset);
}
