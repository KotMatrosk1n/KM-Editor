// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.NameFilter;

internal enum SwShNameFilterMainKind
{
    NotInstalled,
    Installed,
    InstalledCompatible,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SwShNameFilterMainAnalysis(
    SwShNameFilterMainKind Kind,
    string Message,
    string BuildId,
    string PatchOffsetHex,
    string PatchShape,
    ProjectGame? DetectedGame);

internal static class SwShNameFilterMainPatcher
{
    public const int SwordProfanityCheckCallOffset = 0x00EF1228;
    public const int ShieldProfanityCheckCallOffset = 0x00EF1258;
    public const int PatchLength = sizeof(uint);

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private const uint SwordVanillaProfanityCheckCall = 0x97E37E86;
    private const uint ShieldVanillaProfanityCheckCall = 0x97E37E7A;
    private const uint KmReturnCleanInstruction = 0x2A1F03E0; // mov w0, wzr
    private const uint CompatibleReturnCleanInstruction = 0x52800000; // mov w0, #0

    private static readonly PatchDefinition[] Definitions =
    [
        new(
            ProjectGame.Sword,
            "Pokemon Sword 1.3.2",
            SwordBuildId,
            SwordProfanityCheckCallOffset,
            SwordVanillaProfanityCheckCall),
        new(
            ProjectGame.Shield,
            "Pokemon Shield 1.3.2",
            ShieldBuildId,
            ShieldProfanityCheckCallOffset,
            ShieldVanillaProfanityCheckCall),
    ];

    public static SwShNameFilterMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var definition = FindDefinition(buildId);
            if (definition is null)
            {
                return new SwShNameFilterMainAnalysis(
                    SwShNameFilterMainKind.UnsupportedBuild,
                    "Profanity Filter supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
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
            var instruction = ReadInstruction(text, definition.ProfanityCheckCallOffset);
            var offsetLabel = FormatTextOffset(definition.ProfanityCheckCallOffset);

            if (instruction == definition.VanillaProfanityCheckCall)
            {
                return new SwShNameFilterMainAnalysis(
                    SwShNameFilterMainKind.NotInstalled,
                    "Profanity Filter is not installed. The software keyboard still calls the profanity filter for player and Pokemon names.",
                    buildId,
                    offsetLabel,
                    "vanilla profanity-filter call",
                    definition.Game);
            }

            if (instruction == KmReturnCleanInstruction)
            {
                return new SwShNameFilterMainAnalysis(
                    SwShNameFilterMainKind.Installed,
                    "Profanity Filter is installed. The name-input callback treats the profanity-filter result as clean.",
                    buildId,
                    offsetLabel,
                    "KM clean-result instruction",
                    definition.Game);
            }

            if (instruction == CompatibleReturnCleanInstruction)
            {
                return new SwShNameFilterMainAnalysis(
                    SwShNameFilterMainKind.InstalledCompatible,
                    "A compatible Profanity Filter patch is installed. Reinstalling refreshes it to KM's clean-result instruction.",
                    buildId,
                    offsetLabel,
                    "compatible clean-result instruction",
                    definition.Game);
            }

            return new SwShNameFilterMainAnalysis(
                SwShNameFilterMainKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Profanity Filter expected the vanilla profanity-filter call at {offsetLabel}, but found 0x{instruction:X8}."),
                buildId,
                offsetLabel,
                "unknown bytes",
                definition.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShNameFilterMainAnalysis(
                SwShNameFilterMainKind.Conflict,
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
        if (analysis.Kind is SwShNameFilterMainKind.UnsupportedBuild
            or SwShNameFilterMainKind.GameMismatch
            or SwShNameFilterMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        var definition = FindDefinition(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Profanity Filter supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, definition);
        WriteInstruction(text, definition.ProfanityCheckCallOffset, KmReturnCleanInstruction);

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, expectedGame, definition, expectedKind: SwShNameFilterMainKind.Installed);
        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind == SwShNameFilterMainKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind is not (SwShNameFilterMainKind.Installed
            or SwShNameFilterMainKind.InstalledCompatible))
        {
            throw new InvalidDataException("Profanity Filter restore requires an installed clean-result patch.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Profanity Filter restore requires current and base main NSO files with the same build ID.");
        }

        var definition = FindDefinition(baseBuildId)
            ?? throw new InvalidDataException("Profanity Filter restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        var mismatch = CreateGameMismatchAnalysis(definition, expectedGame, baseBuildId);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Profanity Filter restore requires current and base main NSO files with matching .text sizes.");
        }

        EnsurePatchRange(currentText, definition);
        EnsurePatchRange(baseText, definition);
        EnsureVanillaBase(baseText, definition);
        baseText.AsSpan(definition.ProfanityCheckCallOffset, PatchLength)
            .CopyTo(currentText.AsSpan(definition.ProfanityCheckCallOffset, PatchLength));

        var output = currentNso.Write(textDecompressedData: currentText);
        ValidateOutput(currentMainBytes, output, expectedGame, definition, expectedKind: SwShNameFilterMainKind.NotInstalled);
        return output;
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        var kind = Analyze(mainBytes).Kind;
        return kind is SwShNameFilterMainKind.Installed
            or SwShNameFilterMainKind.InstalledCompatible;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerNameFilterBypass);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        ProjectGame? expectedGame,
        PatchDefinition definition,
        SwShNameFilterMainKind expectedKind)
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Profanity Filter patch changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData))
        {
            throw new InvalidDataException("Profanity Filter patch unexpectedly changed the .ro segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Profanity Filter patch unexpectedly changed the .data segment.");
        }

        var textBefore = before.Text.DecompressedData;
        var textAfter = after.Text.DecompressedData;
        if (textBefore.Length != textAfter.Length)
        {
            throw new InvalidDataException("Profanity Filter patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < textBefore.Length; offset++)
        {
            var changed = textBefore[offset] != textAfter[offset];
            var insidePatch = offset >= definition.ProfanityCheckCallOffset
                && offset < definition.ProfanityCheckCallOffset + PatchLength;
            if (changed && !insidePatch)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Profanity Filter patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

        var analysis = Analyze(output, expectedGame);
        if (analysis.Kind != expectedKind)
        {
            throw new InvalidDataException("Profanity Filter patch verification failed after writing exefs/main.");
        }
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        var instruction = ReadInstruction(text, definition.ProfanityCheckCallOffset);
        if (instruction != definition.VanillaProfanityCheckCall)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Profanity Filter restore expected the vanilla profanity-filter call at {FormatTextOffset(definition.ProfanityCheckCallOffset)}, but found 0x{instruction:X8}."));
        }
    }

    private static SwShNameFilterMainAnalysis? CreateGameMismatchAnalysis(
        PatchDefinition definition,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || definition.Game == expectedGame.Value)
        {
            return null;
        }

        return new SwShNameFilterMainAnalysis(
            SwShNameFilterMainKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {definition.GameName}. Profanity Filter will not patch this file because Sword and Shield use different name-input call sites."),
            buildId,
            FormatTextOffset(definition.ProfanityCheckCallOffset),
            "game mismatch",
            definition.Game);
    }

    private static PatchDefinition? FindDefinition(string buildId)
    {
        return Definitions.FirstOrDefault(definition =>
            string.Equals(definition.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        if (definition.ProfanityCheckCallOffset < 0 || definition.ProfanityCheckCallOffset + PatchLength > text.Length)
        {
            throw new InvalidDataException(
                $"{definition.GameName} Profanity Filter patch range is outside the decompressed .text segment.");
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

    private sealed record PatchDefinition(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int ProfanityCheckCallOffset,
        uint VanillaProfanityCheckCall);
}
