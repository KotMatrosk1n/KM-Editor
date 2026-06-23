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
    string StubKind,
    ProjectGame? DetectedGame);

internal static class SwShGymUniformRemovalMainPatcher
{
    public const int SwordPatchOffset = 0x01472600;
    public const int ShieldPatchOffset = 0x01472630;
    public const int PatchLength = sizeof(uint) * 2;
    public const string SwordIpsFileName = SwordBuildId + ".ips";
    public const string ShieldIpsFileName = ShieldBuildId + ".ips";

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

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

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var definition = FindDefinition(buildId);
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
                "unknown",
                "unknown",
                "unreadable",
                DetectedGame: null);
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
        var definition = FindDefinition(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, definition);

        WriteInstruction(text, definition.PatchOffset, KmReturnTrueInstruction);
        WriteInstruction(text, definition.PatchOffset + sizeof(uint), RetInstruction);

        return nso.Write(textDecompressedData: text);
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

        var definition = FindDefinition(analysis.BuildId)
            ?? throw new InvalidDataException("Gym Uniform Removal supports Sword and Shield 1.3.2 exefs/main files.");
        return CreateKmIpsPatch(definition);
    }

    public static SwShGymUniformRemovalAnalysis AnalyzeIpsPatch(
        byte[] ipsBytes,
        byte[] mainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(ipsBytes);
        ArgumentNullException.ThrowIfNull(mainBytes);

        var mainAnalysis = Analyze(mainBytes, expectedGame);
        if (mainAnalysis.Kind is SwShGymUniformRemovalInstallKind.UnsupportedBuild
            or SwShGymUniformRemovalInstallKind.GameMismatch
            or SwShGymUniformRemovalInstallKind.ForeignPatch
            or SwShGymUniformRemovalInstallKind.Conflict)
        {
            return mainAnalysis;
        }

        var definition = FindDefinition(mainAnalysis.BuildId);
        if (definition is null)
        {
            return mainAnalysis;
        }

        if (ipsBytes.SequenceEqual(CreateKmIpsPatch(definition)))
        {
            return new SwShGymUniformRemovalAnalysis(
                SwShGymUniformRemovalInstallKind.InstalledV1,
                "Gym Uniform Removal IPS is installed. Eden/Yuzu will patch the uniform-change handler at load time.",
                mainAnalysis.BuildId,
                mainAnalysis.PatchOffsetHex,
                "KM single-record IPS patch",
                definition.Game);
        }

        if (ipsBytes.SequenceEqual(CreateSingleRecordIpsPatch(definition, LegacyIpsEof))
            || ipsBytes.SequenceEqual(CreateSplitRecordIpsPatch(definition, Ips32Eof))
            || ipsBytes.SequenceEqual(CreateSplitRecordIpsPatch(definition, LegacyIpsEof)))
        {
            return new SwShGymUniformRemovalAnalysis(
                SwShGymUniformRemovalInstallKind.InstalledCompatible,
                "A stale Gym Uniform Removal IPS is installed. Reinstalling refreshes it to the IPS32 EEOF format Eden accepts.",
                mainAnalysis.BuildId,
                mainAnalysis.PatchOffsetHex,
                "stale Gym Uniform Removal IPS patch",
                definition.Game);
        }

        var looksLikeIps = ipsBytes.Length >= Ips32Magic.Length + LegacyIpsEof.Length
            && ipsBytes.AsSpan(0, Ips32Magic.Length).SequenceEqual(Ips32Magic);
        return new SwShGymUniformRemovalAnalysis(
            looksLikeIps ? SwShGymUniformRemovalInstallKind.ForeignPatch : SwShGymUniformRemovalInstallKind.Conflict,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Gym Uniform Removal found an existing IPS file at {IpsRelativePath(definition.Game)}, but it is not a KM-owned patch."),
            mainAnalysis.BuildId,
            mainAnalysis.PatchOffsetHex,
            looksLikeIps ? "foreign IPS patch" : "invalid IPS patch",
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
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Gym Uniform Removal restore requires current and base main NSO files with the same build ID.");
        }

        var definition = FindDefinition(baseBuildId)
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
        return currentNso.Write(textDecompressedData: currentText);
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
        var definition = FindDefinition(buildId);
        return definition is null ? null : IpsRelativePath(definition.Game);
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

    private static PatchDefinition? FindDefinition(string buildId)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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
