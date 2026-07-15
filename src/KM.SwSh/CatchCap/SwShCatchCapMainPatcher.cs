// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.CatchCap;

internal enum SwShCatchCapInstallKind
{
    NotInstalled,
    InstalledV1,
    UnsupportedBuild,
    GameMismatch,
    ForeignPatch,
    Conflict,
}

internal sealed record SwShCatchCapAnalysis(
    SwShCatchCapInstallKind Kind,
    string Message,
    IReadOnlyList<byte> Caps,
    string LogicExpression,
    string CapLogicSha256,
    string BuildId,
    string DisplayHookOffsetHex,
    string RuntimeHookOffsetHex,
    ProjectGame? DetectedGame)
{
    public string PatchOffsetHex => DisplayHookOffsetHex;
}

internal static class SwShCatchCapMainPatcher
{
    public const int CapCount = 9;
    public const int MinimumCap = 1;
    public const int MaximumCap = 100;
    public const int FinalBadgeCount = 8;
    public const int FinalBadgeCap = 100;
    public const int ExeFsHookSiteOffset = 0x013AE3AC;
    public const int ExeFsTableOffset = 0x013AE3B0;
    public const int ExeFsReturnOffset = 0x013AE3C8;
    public const int ExeFsRuntimeHookSiteOffset = 0x013AE3DC;
    public const int ExeFsRuntimeReturnOffset = 0x013AE3F4;
    public const int ShieldExeFsHookSiteOffset = 0x013AE3DC;
    public const int ShieldExeFsTableOffset = 0x013AE3E0;
    public const int ShieldExeFsReturnOffset = 0x013AE3F8;
    public const int ShieldExeFsRuntimeHookSiteOffset = 0x013AE40C;
    public const int ShieldExeFsRuntimeReturnOffset = 0x013AE424;

    internal const int ExeFsCaveClampOffset = 0x013AE0B4;
    internal const int ExeFsCaveLoadAddressOffset = 0x013AEBE4;
    internal const int ExeFsCaveLoadValueOffset = 0x013AD734;
    internal const int ExeFsCaveOverflowOffset = 0x013AF464;
    private const int ShieldOffsetDelta = 0x30;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const uint VanillaFormulaStartInstruction = 0x0B000809; // add w9,w0,w0,lsl #2
    private const uint VanillaMaskInstruction = 0x12001C08; // and w8,w0,#0xff
    private const uint VanillaCompareSevenInstruction = 0x71001D1F; // cmp w8,#0x7
    private const uint VanillaLoadHundredInstruction = 0x52800C88; // mov w8,#0x64
    private const uint VanillaAddTwentyInstruction = 0x11005129; // add w9,w9,#0x14
    private const uint VanillaSelectInstruction = 0x1A898100; // csel w0,w8,w9,hi
    private const uint VanillaTailRestoreInstruction = 0xA9417BFD; // ldp x29,x30,[sp,#0x10]
    private const uint VanillaRuntimeRestoreInstruction = 0xA8C17BFD; // ldp x29,x30,[sp],#0x10
    private const uint VanillaFinalRestoreInstruction = 0xA8C24FF4; // ldp x20,x19,[sp],#0x20
    private const uint VanillaRetInstruction = 0xD65F03C0;

    private static readonly byte[] DefaultCaps = [0x14, 0x19, 0x1E, 0x23, 0x28, 0x2D, 0x32, 0x37, 0x64];
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("CCHv1");

    private static readonly PatchDefinition[] Definitions =
    [
        new(
            ProjectGame.Sword,
            "Pokemon Sword 1.3.2",
            SwordBuildId,
            ExeFsHookSiteOffset,
            ExeFsTableOffset,
            ExeFsReturnOffset,
            ExeFsRuntimeHookSiteOffset,
            ExeFsRuntimeReturnOffset,
            ExeFsCaveClampOffset,
            ExeFsCaveLoadAddressOffset,
            ExeFsCaveLoadValueOffset,
            ExeFsCaveOverflowOffset),
        new(
            ProjectGame.Shield,
            "Pokemon Shield 1.3.2",
            ShieldBuildId,
            ShieldExeFsHookSiteOffset,
            ShieldExeFsTableOffset,
            ShieldExeFsReturnOffset,
            ShieldExeFsRuntimeHookSiteOffset,
            ShieldExeFsRuntimeReturnOffset,
            ExeFsCaveClampOffset + ShieldOffsetDelta,
            ExeFsCaveLoadAddressOffset + ShieldOffsetDelta,
            ExeFsCaveLoadValueOffset + ShieldOffsetDelta,
            ExeFsCaveOverflowOffset + ShieldOffsetDelta),
    ];

    public static SwShCatchCapAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            var buildId = FormatBuildId(nso.BuildId);
            var definition = FindDefinition(nso.BuildId);
            if (definition is null)
            {
                return CreateAnalysis(
                    SwShCatchCapInstallKind.UnsupportedBuild,
                    "Catch Cap Editor supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    DefaultCaps,
                    buildId,
                    "unknown",
                    "unknown",
                    DetectedGame: null);
            }

            var mismatch = CreateGameMismatchAnalysis(definition, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRanges(text, definition);

            if (HasMarkerBytes(text, definition))
            {
                var rawCaps = text.AsSpan(definition.TableOffset, CapCount).ToArray();
                var caps = NormalizeCaps(rawCaps);
                if (!HasExactMarkerAndMetadata(text, definition))
                {
                    return CreateAnalysis(
                        SwShCatchCapInstallKind.Conflict,
                        "Catch Cap Editor marker bytes are present, but the marker version or reserved metadata is damaged.",
                        caps,
                        buildId,
                        FormatTextOffset(definition.HookSiteOffset),
                        FormatTextOffset(definition.RuntimeHookSiteOffset),
                        definition.Game);
                }

                if (!HasExactDisplayGraph(text, definition))
                {
                    return CreateAnalysis(
                        SwShCatchCapInstallKind.Conflict,
                        "Catch Cap Editor marker is present, but the display hook branch or cave graph is damaged or redirected.",
                        caps,
                        buildId,
                        FormatTextOffset(definition.HookSiteOffset),
                        FormatTextOffset(definition.RuntimeHookSiteOffset),
                        definition.Game);
                }

                var installState = GetExactInstallState(text, definition);
                if (installState == CatchCapInstallState.None)
                {
                    return CreateAnalysis(
                        SwShCatchCapInstallKind.Conflict,
                        "Catch Cap Editor marker is present, but the runtime catch gate is neither the exact vanilla formula nor the exact KM hook.",
                        caps,
                        buildId,
                        FormatTextOffset(definition.HookSiteOffset),
                        FormatTextOffset(definition.RuntimeHookSiteOffset),
                        definition.Game);
                }

                var message = CreateInstalledMessage(rawCaps, installState == CatchCapInstallState.DisplayAndRuntime);
                return CreateAnalysis(
                    SwShCatchCapInstallKind.InstalledV1,
                    message,
                    caps,
                    buildId,
                    FormatTextOffset(definition.HookSiteOffset),
                    FormatTextOffset(definition.RuntimeHookSiteOffset),
                    definition.Game);
            }

            var hookInstruction = ReadInstruction(text, definition.HookSiteOffset);
            if (IsUnconditionalBranch(hookInstruction))
            {
                return CreateAnalysis(
                    SwShCatchCapInstallKind.ForeignPatch,
                    "Catch-cap formula tail is already branched, but the KM Catch Cap Hook marker is not present.",
                    DefaultCaps,
                    buildId,
                    FormatTextOffset(definition.HookSiteOffset),
                    FormatTextOffset(definition.RuntimeHookSiteOffset),
                    definition.Game);
            }

            if (!HasVanillaInstallSurface(text, definition))
            {
                return CreateAnalysis(
                    SwShCatchCapInstallKind.Conflict,
                    "Catch Cap Editor found non-vanilla bytes in its display formula, runtime formula, protected epilogues, or reserved caves.",
                    DefaultCaps,
                    buildId,
                    FormatTextOffset(definition.HookSiteOffset),
                    FormatTextOffset(definition.RuntimeHookSiteOffset),
                    definition.Game);
            }

            return CreateAnalysis(
                SwShCatchCapInstallKind.NotInstalled,
                "Catch Cap Editor hook is not installed. Staging values installs the hook and writes the selected cap table.",
                DefaultCaps,
                buildId,
                FormatTextOffset(definition.HookSiteOffset),
                FormatTextOffset(definition.RuntimeHookSiteOffset),
                definition.Game);
        }
        catch (InvalidDataException exception)
        {
            return CreateAnalysis(
                SwShCatchCapInstallKind.Conflict,
                exception.Message,
                DefaultCaps,
                "unknown",
                "unknown",
                "unknown",
                DetectedGame: null);
        }
    }

    public static byte[] Apply(byte[] mainBytes, IReadOnlyList<int> caps, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(caps);

        if (caps.Count != CapCount)
        {
            throw new InvalidDataException("Catch Cap Editor requires exactly nine cap values; badge count 8 must be level 100.");
        }

        var capBytes = new byte[caps.Count];
        for (var index = 0; index < caps.Count; index++)
        {
            var cap = caps[index];
            if (cap is < MinimumCap or > MaximumCap)
            {
                throw new InvalidDataException($"Catch cap {cap} must be between {MinimumCap} and {MaximumCap}.");
            }

            if (index == FinalBadgeCount && cap != FinalBadgeCap)
            {
                throw new InvalidDataException(
                    $"Catch cap for badge count {FinalBadgeCount} is fixed at level {FinalBadgeCap}; the game treats eight badges as catch any level.");
            }

            if (index > 0 && cap < caps[index - 1])
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Catch cap for badge count {index} must be level {caps[index - 1]} or higher."));
            }

            capBytes[index] = checked((byte)cap);
        }

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShCatchCapInstallKind.UnsupportedBuild
            or SwShCatchCapInstallKind.GameMismatch
            or SwShCatchCapInstallKind.ForeignPatch
            or SwShCatchCapInstallKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var definition = FindDefinition(nso.BuildId)
            ?? throw new InvalidDataException("Catch Cap Editor supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRanges(text, definition);

        if (analysis.Kind == SwShCatchCapInstallKind.NotInstalled)
        {
            InstallDisplayHook(text, definition);
        }

        // Older KM output only patched the display formula. Keep installed hooks upgradeable by
        // adding the runtime gate when the marker is present but the second formula is still vanilla.
        if (!HasRuntimeHook(text, definition))
        {
            InstallRuntimeHook(text, definition);
        }

        WriteTableAndMarker(text, capBytes, definition);
        var output = nso.Write(textDecompressedData: text);
        VerifyApplyOutput(mainBytes, output, capBytes, definition, expectedGame);
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

        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        var currentDefinition = FindDefinition(currentNso.BuildId)
            ?? throw new InvalidDataException("Catch Cap restore requires a supported Sword or Shield 1.3.2 current main NSO.");
        var definition = FindDefinition(baseNso.BuildId)
            ?? throw new InvalidDataException("Catch Cap restore requires a supported Sword or Shield 1.3.2 base main NSO.");
        if (currentDefinition.Game != definition.Game)
        {
            throw new InvalidDataException("Catch Cap restore requires current and base main NSO files for the same game.");
        }

        EnsureSameBuildAndLayout(currentNso, baseNso, "Catch Cap restore");
        var mismatch = CreateGameMismatchAnalysis(definition, expectedGame, baseBuildId);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        EnsurePatchRanges(currentText, definition);
        EnsurePatchRanges(baseText, definition);
        EnsureVanillaBase(baseText, definition);
        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind != SwShCatchCapInstallKind.InstalledV1)
        {
            throw new InvalidDataException(
                $"Catch Cap restore requires one exact installed KM hook; found {currentAnalysis.Kind}: {currentAnalysis.Message}");
        }

        var installState = GetExactInstallState(currentText, definition);
        if (installState == CatchCapInstallState.None)
        {
            throw new InvalidDataException("Catch Cap restore could not verify the exact installed display and runtime graph.");
        }

        // Restore only bytes actually written by this install shape. The display and runtime
        // epilogues are dependencies that must remain exact, but Catch Cap never owns them.
        var writtenRegions = WrittenRegions(
            definition,
            includeRuntimeGate: installState == CatchCapInstallState.DisplayAndRuntime);
        foreach (var region in writtenRegions)
        {
            EnsureTextRange(currentText, region.Offset, region.Length, region.Label);
            EnsureTextRange(baseText, region.Offset, region.Length, $"Base {region.Label}");
            baseText.AsSpan(region.Offset, region.Length).CopyTo(currentText.AsSpan(region.Offset, region.Length));
        }

        var output = currentNso.Write(textDecompressedData: currentText);
        VerifyRestoreOutput(currentMainBytes, baseMainBytes, output, writtenRegions, expectedGame);
        return output;
    }

    public static string FormatLogicExpression(IReadOnlyList<byte> caps)
    {
        if (caps.Count != CapCount)
        {
            return "Invalid cap table";
        }

        var step = caps[1] - caps[0];
        var isLinear = true;
        for (var index = 1; index < 8; index++)
        {
            if (caps[index] - caps[index - 1] != step)
            {
                isLinear = false;
                break;
            }
        }

        if (isLinear)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"badge_count < 8 ? {caps[0]} + badge_count * {step} : {FinalBadgeCap}");
        }

        return "badge_count < 8 ? cap_table[badge_count] : 100";
    }

    public static string ComputeCapLogicSha256(IReadOnlyList<byte> caps)
    {
        var hex = string.Join(' ', caps.Select(cap => cap.ToString("X2", CultureInfo.InvariantCulture)));
        var canonical = $"SWSH-CATCH-CAP-HOOK|v1|u8|9|{hex}";
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string CreateInstalledMessage(IReadOnlyList<byte> rawCaps, bool hasRuntimeHook)
    {
        var staleFinalBadgeMessage = rawCaps[FinalBadgeCount] == FinalBadgeCap
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $" The installed table has stale Lv.{rawCaps[FinalBadgeCount]} metadata for eight badges; stage and apply to rewrite it to Lv.100.");

        var runtimeMessage = hasRuntimeHook
            ? "Catch Cap Editor hook is installed for display and runtime capture checks."
            : "Catch Cap Editor has a legacy display-only hook installed; stage and apply to add the runtime capture gate hook.";

        return runtimeMessage
            + " Changing values edits badge counts 0-7; eight badges is fixed at Lv.100 by the game."
            + staleFinalBadgeMessage;
    }

    private static void InstallDisplayHook(byte[] text, PatchDefinition definition)
    {
        if (!HasVanillaDisplaySurface(text, definition))
        {
            throw new InvalidDataException(
                $"Catch Cap Hook install requires the exact vanilla display formula, epilogue, and empty cave bytes at {FormatTextOffset(definition.HookSiteOffset)}.");
        }

        // The display formula tail does not have enough contiguous space for the table lookup, so
        // it jumps through reserved cavelets and then rejoins the original epilogue.
        WriteInstruction(text, definition.HookSiteOffset, EncodeBranch(definition.HookSiteOffset, definition.CaveClampOffset));

        WriteInstruction(text, definition.CaveClampOffset, EncodeCmpImmediate(0, 8));
        WriteInstruction(text, definition.CaveClampOffset + 4, EncodeConditionalBranch(definition.CaveClampOffset + 4, definition.CaveLoadAddressOffset, Arm64Condition.LS));
        WriteInstruction(text, definition.CaveClampOffset + 8, EncodeBranch(definition.CaveClampOffset + 8, definition.CaveOverflowOffset));

        WriteInstruction(text, definition.CaveOverflowOffset, EncodeMovzImmediate32(0, 8));
        WriteInstruction(text, definition.CaveOverflowOffset + 4, EncodeBranch(definition.CaveOverflowOffset + 4, definition.CaveLoadAddressOffset));
        WriteInstruction(text, definition.CaveOverflowOffset + 8, EncodeNop());

        WriteInstruction(text, definition.CaveLoadAddressOffset, EncodeAdr(8, definition.CaveLoadAddressOffset, definition.TableOffset));
        WriteInstruction(text, definition.CaveLoadAddressOffset + 4, EncodeBranch(definition.CaveLoadAddressOffset + 4, definition.CaveLoadValueOffset));
        WriteInstruction(text, definition.CaveLoadAddressOffset + 8, EncodeNop());

        WriteInstruction(text, definition.CaveLoadValueOffset, EncodeLdrbRegisterOffsetUxtw(0, 8, 0));
        WriteInstruction(text, definition.CaveLoadValueOffset + 4, VanillaTailRestoreInstruction);
        WriteInstruction(text, definition.CaveLoadValueOffset + 8, EncodeBranch(definition.CaveLoadValueOffset + 8, definition.ReturnOffset));
    }

    private static void InstallRuntimeHook(byte[] text, PatchDefinition definition)
    {
        if (!HasVanillaRuntimeFormula(text, definition))
        {
            throw new InvalidDataException(
                $"Catch Cap Hook install requires the vanilla runtime catch gate at {FormatTextOffset(definition.RuntimeHookSiteOffset)}.");
        }

        // The runtime capture formula has a compact 0x18 byte window before its epilogue. That is
        // just enough room to clamp badge count 8, load the shared table byte, and fall through.
        WriteInstruction(text, definition.RuntimeHookSiteOffset, EncodeCmpImmediate(0, 8));
        WriteInstruction(
            text,
            definition.RuntimeHookSiteOffset + 4,
            EncodeConditionalBranch(definition.RuntimeHookSiteOffset + 4, definition.RuntimeHookSiteOffset + 0x0C, Arm64Condition.LS));
        WriteInstruction(text, definition.RuntimeHookSiteOffset + 8, EncodeMovzImmediate32(0, 8));
        WriteInstruction(text, definition.RuntimeHookSiteOffset + 0x0C, EncodeAdr(8, definition.RuntimeHookSiteOffset + 0x0C, definition.TableOffset));
        WriteInstruction(text, definition.RuntimeHookSiteOffset + 0x10, EncodeLdrbRegisterOffsetUxtw(0, 8, 0));
        WriteInstruction(text, definition.RuntimeHookSiteOffset + 0x14, EncodeNop());
    }

    private static void WriteTableAndMarker(byte[] text, IReadOnlyList<byte> caps, PatchDefinition definition)
    {
        for (var index = 0; index < CapCount; index++)
        {
            text[definition.TableOffset + index] = caps[index];
        }

        Marker.CopyTo(text.AsSpan(definition.TableOffset + CapCount));
        text[definition.TableOffset + CapCount + Marker.Length] = CapCount;
        text[definition.TableOffset + CapCount + Marker.Length + 1] = 1;
        text.AsSpan(definition.TableOffset + 0x10, 8).Clear();
    }

    private static byte[] NormalizeCaps(ReadOnlySpan<byte> caps)
    {
        var normalized = caps.ToArray();
        // Badge count 8 is always "catch anything" in game logic; stale legacy table metadata should
        // not make the UI or diagnostics imply that the final badge cap is editable.
        normalized[FinalBadgeCount] = FinalBadgeCap;
        return normalized;
    }

    private static bool HasMarkerBytes(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return text.Length >= definition.TableOffset + CapCount + Marker.Length
            && text.Slice(definition.TableOffset + CapCount, Marker.Length).SequenceEqual(Marker);
    }

    private static bool HasExactMarkerAndMetadata(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return text.Length >= definition.TableOffset + 0x18
            && HasMarkerBytes(text, definition)
            && text[definition.TableOffset + CapCount + Marker.Length] == CapCount
            && text[definition.TableOffset + CapCount + Marker.Length + 1] == 1
            && IsZeroFilled(text.Slice(definition.TableOffset + 0x10, 0x08));
    }

    private static CatchCapInstallState GetExactInstallState(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        if (!HasExactMarkerAndMetadata(text, definition) || !HasExactDisplayGraph(text, definition))
        {
            return CatchCapInstallState.None;
        }

        if (HasRuntimeHook(text, definition))
        {
            return CatchCapInstallState.DisplayAndRuntime;
        }

        return HasVanillaRuntimeFormula(text, definition)
            ? CatchCapInstallState.LegacyDisplayOnly
            : CatchCapInstallState.None;
    }

    private static bool HasExactDisplayGraph(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return ReadInstruction(text, definition.HookSiteOffset)
                == EncodeBranch(definition.HookSiteOffset, definition.CaveClampOffset)
            && ReadInstruction(text, definition.CaveClampOffset) == EncodeCmpImmediate(0, 8)
            && ReadInstruction(text, definition.CaveClampOffset + 4)
                == EncodeConditionalBranch(
                    definition.CaveClampOffset + 4,
                    definition.CaveLoadAddressOffset,
                    Arm64Condition.LS)
            && ReadInstruction(text, definition.CaveClampOffset + 8)
                == EncodeBranch(definition.CaveClampOffset + 8, definition.CaveOverflowOffset)
            && ReadInstruction(text, definition.CaveOverflowOffset) == EncodeMovzImmediate32(0, 8)
            && ReadInstruction(text, definition.CaveOverflowOffset + 4)
                == EncodeBranch(definition.CaveOverflowOffset + 4, definition.CaveLoadAddressOffset)
            && ReadInstruction(text, definition.CaveOverflowOffset + 8) == EncodeNop()
            && ReadInstruction(text, definition.CaveLoadAddressOffset)
                == EncodeAdr(8, definition.CaveLoadAddressOffset, definition.TableOffset)
            && ReadInstruction(text, definition.CaveLoadAddressOffset + 4)
                == EncodeBranch(definition.CaveLoadAddressOffset + 4, definition.CaveLoadValueOffset)
            && ReadInstruction(text, definition.CaveLoadAddressOffset + 8) == EncodeNop()
            && ReadInstruction(text, definition.CaveLoadValueOffset)
                == EncodeLdrbRegisterOffsetUxtw(0, 8, 0)
            && ReadInstruction(text, definition.CaveLoadValueOffset + 4) == VanillaTailRestoreInstruction
            && ReadInstruction(text, definition.CaveLoadValueOffset + 8)
                == EncodeBranch(definition.CaveLoadValueOffset + 8, definition.ReturnOffset)
            && HasDisplayEpilogue(text, definition);
    }

    private static bool HasVanillaDisplaySurface(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        if (!HasVanillaDisplayFormula(text, definition))
        {
            return false;
        }

        foreach (var cave in definition.CaveOffsets)
        {
            if (!IsZeroFilled(text.Slice(cave, 0x0C)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasVanillaDisplayFormula(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return ReadInstruction(text, definition.HookSiteOffset) == VanillaFormulaStartInstruction
            && ReadInstruction(text, definition.TableOffset) == VanillaTailRestoreInstruction
            && ReadInstruction(text, definition.TableOffset + 4) == VanillaMaskInstruction
            && ReadInstruction(text, definition.TableOffset + 8) == VanillaCompareSevenInstruction
            && ReadInstruction(text, definition.TableOffset + 0x0C) == VanillaLoadHundredInstruction
            && ReadInstruction(text, definition.TableOffset + 0x10) == VanillaAddTwentyInstruction
            && ReadInstruction(text, definition.TableOffset + 0x14) == VanillaSelectInstruction
            && HasDisplayEpilogue(text, definition);
    }

    private static bool HasDisplayEpilogue(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return ReadInstruction(text, definition.ReturnOffset) == VanillaFinalRestoreInstruction
            && ReadInstruction(text, definition.ReturnOffset + 4) == VanillaRetInstruction;
    }

    private static bool HasVanillaInstallSurface(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return HasVanillaDisplaySurface(text, definition)
            && HasVanillaRuntimeFormula(text, definition);
    }

    private static bool HasVanillaRuntimeFormula(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return ReadInstruction(text, definition.RuntimeHookSiteOffset) == VanillaFormulaStartInstruction
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 4) == VanillaMaskInstruction
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 8) == VanillaCompareSevenInstruction
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x0C) == VanillaLoadHundredInstruction
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x10) == VanillaAddTwentyInstruction
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x14) == VanillaSelectInstruction
            && ReadInstruction(text, definition.RuntimeReturnOffset) == VanillaRuntimeRestoreInstruction
            && ReadInstruction(text, definition.RuntimeReturnOffset + 4) == VanillaRetInstruction;
    }

    private static bool HasRuntimeHook(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        return ReadInstruction(text, definition.RuntimeHookSiteOffset) == EncodeCmpImmediate(0, 8)
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 4) == EncodeConditionalBranch(definition.RuntimeHookSiteOffset + 4, definition.RuntimeHookSiteOffset + 0x0C, Arm64Condition.LS)
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 8) == EncodeMovzImmediate32(0, 8)
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x0C) == EncodeAdr(8, definition.RuntimeHookSiteOffset + 0x0C, definition.TableOffset)
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x10) == EncodeLdrbRegisterOffsetUxtw(0, 8, 0)
            && ReadInstruction(text, definition.RuntimeHookSiteOffset + 0x14) == EncodeNop()
            && ReadInstruction(text, definition.RuntimeReturnOffset) == VanillaRuntimeRestoreInstruction
            && ReadInstruction(text, definition.RuntimeReturnOffset + 4) == VanillaRetInstruction;
    }

    private static void EnsurePatchRanges(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        foreach (var region in WrittenRegions(definition, includeRuntimeGate: true)
            .Concat(DependencyRegions(definition)))
        {
            EnsureTextRange(text, region.Offset, region.Length, region.Label);
        }
    }

    private static void EnsureVanillaBase(ReadOnlySpan<byte> text, PatchDefinition definition)
    {
        EnsurePatchRanges(text, definition);
        if (!HasVanillaDisplayFormula(text, definition))
        {
            throw new InvalidDataException(
                $"{definition.GameName} Catch Cap restore expected the exact vanilla display formula and protected epilogue at {FormatTextOffset(definition.HookSiteOffset)}.");
        }

        if (!HasVanillaRuntimeFormula(text, definition))
        {
            throw new InvalidDataException(
                $"{definition.GameName} Catch Cap restore expected the exact vanilla runtime formula and protected epilogue at {FormatTextOffset(definition.RuntimeHookSiteOffset)}.");
        }

        foreach (var cave in definition.CaveOffsets)
        {
            if (!IsZeroFilled(text.Slice(cave, 0x0C)))
            {
                throw new InvalidDataException(
                    $"{definition.GameName} Catch Cap restore expected an empty base cave at main.text+0x{cave:X}.");
            }
        }
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

    private static void VerifyApplyOutput(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        IReadOnlyList<byte> expectedCaps,
        PatchDefinition definition,
        ProjectGame? expectedGame)
    {
        var writtenRegions = WrittenRegions(definition, includeRuntimeGate: true);
        VerifyOutputPreservation(
            sourceMainBytes,
            outputMainBytes,
            writtenRegions,
            "Catch Cap apply");

        var analysis = Analyze(outputMainBytes, expectedGame);
        var outputNso = NsoFile.Parse(outputMainBytes);
        var outputText = outputNso.Text.DecompressedData;
        if (analysis.Kind != SwShCatchCapInstallKind.InstalledV1
            || GetExactInstallState(outputText, definition) != CatchCapInstallState.DisplayAndRuntime
            || !outputText.AsSpan(definition.TableOffset, CapCount).SequenceEqual(expectedCaps.ToArray()))
        {
            throw new InvalidDataException(
                "Catch Cap apply verification did not find the exact display and runtime hook graph with the requested badge table.");
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
            "Catch Cap restore");

        var outputAnalysis = Analyze(outputMainBytes, expectedGame);
        if (outputAnalysis.Kind != SwShCatchCapInstallKind.NotInstalled)
        {
            throw new InvalidDataException(
                $"Catch Cap restore verification did not return the exact vanilla install surface: {outputAnalysis.Message}");
        }

        var baseText = NsoFile.Parse(baseMainBytes).Text.DecompressedData;
        var outputText = NsoFile.Parse(outputMainBytes).Text.DecompressedData;
        foreach (var region in restoredRegions)
        {
            if (!baseText.AsSpan(region.Offset, region.Length)
                .SequenceEqual(outputText.AsSpan(region.Offset, region.Length)))
            {
                throw new InvalidDataException(
                    $"Catch Cap restore verification found a mismatched {region.Label}.");
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
                    $"{operation} verification found a change outside Catch Cap written ranges before {region.Label}.");
            }

            cursor = region.Offset + region.Length;
        }

        if (!sourceText[cursor..].SequenceEqual(outputText[cursor..]))
        {
            throw new InvalidDataException(
                $"{operation} verification found a change outside Catch Cap written ranges after the final region.");
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
                $"Catch Cap patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static SwShCatchCapAnalysis CreateAnalysis(
        SwShCatchCapInstallKind kind,
        string message,
        IReadOnlyList<byte> caps,
        string buildId,
        string displayHookOffsetHex,
        string runtimeHookOffsetHex,
        ProjectGame? DetectedGame)
    {
        return new SwShCatchCapAnalysis(
            kind,
            message,
            caps,
            FormatLogicExpression(caps),
            ComputeCapLogicSha256(caps),
            buildId,
            displayHookOffsetHex,
            runtimeHookOffsetHex,
            DetectedGame);
    }

    private static SwShCatchCapAnalysis? CreateGameMismatchAnalysis(
        PatchDefinition definition,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || definition.Game == expectedGame.Value)
        {
            return null;
        }

        return CreateAnalysis(
            SwShCatchCapInstallKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {definition.GameName}. Catch Cap Editor will not patch this file because Sword and Shield use different hook sites."),
            DefaultCaps,
            buildId,
            FormatTextOffset(definition.HookSiteOffset),
            FormatTextOffset(definition.RuntimeHookSiteOffset),
            definition.Game);
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
            && IsZeroFilled(buildId[knownBuildIdLength..]);
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

    private static IReadOnlyList<PatchRegion> WrittenRegions(
        PatchDefinition definition,
        bool includeRuntimeGate)
    {
        List<PatchRegion> regions =
        [
            new(definition.HookSiteOffset, sizeof(uint), "Catch Cap hook branch site"),
            new(definition.TableOffset, 0x18, "Catch Cap cap table, marker, and reserved metadata"),
            new(definition.CaveClampOffset, 0x0C, "Catch Cap standard cave slot 1"),
            new(definition.CaveLoadAddressOffset, 0x0C, "Catch Cap standard cave slot 2"),
            new(definition.CaveLoadValueOffset, 0x0C, "Catch Cap standard cave slot 3"),
            new(definition.CaveOverflowOffset, 0x0C, "Catch Cap fallback cave slot 1"),
        ];

        if (includeRuntimeGate)
        {
            regions.Add(new(definition.RuntimeHookSiteOffset, 0x18, "Catch Cap runtime capture gate"));
        }

        return regions;
    }

    private static IReadOnlyList<PatchRegion> DependencyRegions(PatchDefinition definition)
    {
        return
        [
            new(definition.ReturnOffset, sizeof(uint) * 2, "Catch Cap protected display epilogue"),
            new(definition.RuntimeReturnOffset, sizeof(uint) * 2, "Catch Cap protected runtime epilogue"),
        ];
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

    private static bool IsUnconditionalBranch(uint instruction)
    {
        return (instruction & 0x7C000000) == 0x14000000;
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Conditional branch target must be 4-byte aligned.");
        }

        var imm19 = delta >> 2;
        if (imm19 < -(1 << 18) || imm19 >= (1 << 18))
        {
            throw new InvalidDataException("Conditional branch target is outside ARM64 range.");
        }

        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
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

        return (uint)(0x14000000 | (imm26 & 0x03FFFFFF));
    }

    private static uint EncodeAdr(int register, int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if (delta < -(1 << 20) || delta >= (1 << 20))
        {
            throw new InvalidDataException("ADR target is outside ARM64 range.");
        }

        var immediate = delta & 0x1FFFFF;
        var immediateLow = immediate & 0x3;
        var immediateHigh = (immediate >> 2) & 0x7FFFF;
        return 0x10000000u
            | (uint)(immediateLow << 29)
            | (uint)(immediateHigh << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("MOVZ immediate must fit in 16 bits.");
        }

        return (uint)(0x52800000 | ((immediate & 0xFFFF) << 5) | (register & 0x1F));
    }

    private static uint EncodeLdrbRegisterOffsetUxtw(int targetRegister, int baseRegister, int offsetRegister)
    {
        return 0x38604800u
            | (uint)((offsetRegister & 0x1F) << 16)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(targetRegister & 0x1F);
    }

    private static uint EncodeNop()
    {
        return 0xD503201F;
    }

    private enum Arm64Condition
    {
        LS = 9,
    }

    private enum CatchCapInstallState
    {
        None,
        LegacyDisplayOnly,
        DisplayAndRuntime,
    }

    private sealed record PatchDefinition(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int HookSiteOffset,
        int TableOffset,
        int ReturnOffset,
        int RuntimeHookSiteOffset,
        int RuntimeReturnOffset,
        int CaveClampOffset,
        int CaveLoadAddressOffset,
        int CaveLoadValueOffset,
        int CaveOverflowOffset)
    {
        public IReadOnlyList<int> CaveOffsets { get; } =
        [
            CaveClampOffset,
            CaveLoadAddressOffset,
            CaveLoadValueOffset,
            CaveOverflowOffset,
        ];
    }

    private sealed record PatchRegion(int Offset, int Length, string Label);
}
