// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.ExeFs;

public sealed class SwShExeFsPatchWorkflowService
{
    public const string ExeFsMainPath = "exefs/main";

    private const int RareCandyItemId = 50;
    private const int RoyalCandyItemId = 1128;
    private const int RareCandyUiHookCodeCaveSearchStart = 0x007BC338;
    private const string MainPatchId = "exefs-main-compatibility";

    private readonly SwShParsedDataCache parsedDataCache;

    public SwShExeFsPatchWorkflowService(SwShParsedDataCache? parsedDataCache = null)
    {
        this.parsedDataCache = parsedDataCache ?? new SwShParsedDataCache();
    }

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "ExeFS Patch Manager requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShExeFsPatchWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShExeFsPatchRecord>(),
                Array.Empty<SwShExeFsSegmentRecord>(),
                Array.Empty<SwShExeFsPatchCheckRecord>(),
                sourceFileCount: 0,
                diagnostics);
        }

        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "ExeFS main is not available for this project.",
                expected: ExeFsMainPath));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShExeFsPatchRecord>(),
                Array.Empty<SwShExeFsSegmentRecord>(),
                Array.Empty<SwShExeFsPatchCheckRecord>(),
                sourceFileCount: 0,
                diagnostics);
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "ExeFS main could not be resolved from the project graph.",
                file: graphEntry.RelativePath,
                expected: "Readable Sword/Shield exefs/main NSO"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShExeFsPatchRecord>(),
                Array.Empty<SwShExeFsSegmentRecord>(),
                Array.Empty<SwShExeFsPatchCheckRecord>(),
                sourceFileCount: 0,
                diagnostics);
        }

        var provenance = CreateProvenance(graphEntry);
        try
        {
            var analysis = parsedDataCache.GetOrAdd(
                sourcePath,
                path => CreateCompatibilityAnalysis(path, provenance)).Value;

            if (analysis.Checks.Any(check => check.Status == "Fail"))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "ExeFS main has failing compatibility checks.",
                    file: graphEntry.RelativePath,
                    expected: "Known Sword/Shield 1.3.2-style patch anchors"));
            }

            return CreateWorkflow(
                summary,
                analysis.Patches,
                analysis.Segments,
                analysis.Checks,
                sourceFileCount: 1,
                diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS main could not be decoded as NSO: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Sword/Shield exefs/main NSO"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"ExeFS main could not be read: {exception.Message}",
                file: graphEntry.RelativePath,
                expected: "Readable Sword/Shield exefs/main NSO"));
        }

        return CreateWorkflow(
            summary,
            Array.Empty<SwShExeFsPatchRecord>(),
            Array.Empty<SwShExeFsSegmentRecord>(),
            Array.Empty<SwShExeFsPatchCheckRecord>(),
            sourceFileCount: 0,
            diagnostics);
    }

    private static ExeFsCompatibilityAnalysis CreateCompatibilityAnalysis(
        string sourcePath,
        SwShExeFsPatchProvenance provenance)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var nso = SwShNsoFile.Parse(bytes);
        var segments = CreateSegments(nso, provenance);
        var checks = CreateChecks(nso, provenance);
        var patches = new[] { CreateMainPatchRecord(bytes, nso, checks, provenance) };
        return new ExeFsCompatibilityAnalysis(patches, segments, checks);
    }

    private static SwShExeFsPatchWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShExeFsPatchRecord> patches,
        IReadOnlyList<SwShExeFsSegmentRecord> segments,
        IReadOnlyList<SwShExeFsPatchCheckRecord> checks,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShExeFsPatchWorkflow(
            summary,
            patches,
            segments,
            checks,
            new SwShExeFsPatchWorkflowStats(
                patches.Count,
                checks.Count,
                checks.Count(check => check.Status == "Pass"),
                checks.Count(check => check.Status == "Warning"),
                checks.Count(check => check.Status == "Fail"),
                sourceFileCount),
            diagnostics);
    }

    private static SwShExeFsPatchRecord CreateMainPatchRecord(
        byte[] bytes,
        SwShNsoFile nso,
        IReadOnlyList<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance)
    {
        var status = checks.Any(check => check.Status == "Fail")
            ? "blocked"
            : checks.Any(check => check.Status == "Warning")
                ? "warning"
                : "available";

        return new SwShExeFsPatchRecord(
            MainPatchId,
            "ExeFS main compatibility",
            ExeFsMainPath,
            "NSO signature scan",
            status,
            "Validates Sword/Shield ExeFS main structure, segment hashes, code-cave availability, and known patch anchors.",
            [
                $"Build ID: {ToHex(nso.BuildId)}",
                string.Create(CultureInfo.InvariantCulture, $"File size: 0x{bytes.Length:X} bytes"),
                $"Flags: {nso.Flags}",
                string.Create(CultureInfo.InvariantCulture, $"Checks: {checks.Count} total, {checks.Count(check => check.Status == "Fail")} failing, {checks.Count(check => check.Status == "Warning")} warnings")
            ],
            provenance);
    }

    private static IReadOnlyList<SwShExeFsSegmentRecord> CreateSegments(
        SwShNsoFile nso,
        SwShExeFsPatchProvenance provenance)
    {
        return nso.Segments
            .Select(segment =>
            {
                var actualHash = SwShNsoFile.ComputeHash(segment.DecompressedData);
                var hashStatus = actualHash.SequenceEqual(segment.Hash) ? "Pass" : "Warning";
                return new SwShExeFsSegmentRecord(
                    segment.Name.TrimStart('.'),
                    segment.Name,
                    FormatFileOffset(segment.Header.FileOffset),
                    FormatMemoryOffset(segment.Header.MemoryOffset),
                    FormatHexSize(segment.Header.DecompressedSize),
                    FormatHexSize(segment.CompressedSize),
                    ToHex(actualHash),
                    hashStatus,
                    provenance);
            })
            .ToArray();
    }

    private static IReadOnlyList<SwShExeFsPatchCheckRecord> CreateChecks(
        SwShNsoFile nso,
        SwShExeFsPatchProvenance provenance)
    {
        var checks = new List<SwShExeFsPatchCheckRecord>();
        var text = nso.Text.DecompressedData;
        var largestZeroRun = FindLargestZeroRun(text);

        AddCheck(checks, provenance, "nso-magic", "Pass", "main", string.Empty, "NSO magic", "NSO0", "NSO0", "Valid NSO header.");
        AddHashCheck(checks, provenance, "text-hash", ".text", "Segment hash", nso.Text.Hash, SwShNsoFile.ComputeHash(nso.Text.DecompressedData));
        AddHashCheck(checks, provenance, "ro-hash", ".ro", "Segment hash", nso.Ro.Hash, SwShNsoFile.ComputeHash(nso.Ro.DecompressedData));
        AddHashCheck(checks, provenance, "data-hash", ".data", "Segment hash", nso.Data.Hash, SwShNsoFile.ComputeHash(nso.Data.DecompressedData));
        AddZeroRunCheck(checks, provenance, text, "patch-code-cave", "Patch code cave", 0x0C, RareCandyUiHookCodeCaveSearchStart);
        AddCheck(
            checks,
            provenance,
            "largest-zero-run",
            largestZeroRun.Length >= 0x0C ? "Info" : "Warning",
            ".text",
            largestZeroRun.Offset >= 0 ? FormatTextOffset(largestZeroRun.Offset) : string.Empty,
            "Largest zero run",
            "at least 0xC bytes",
            FormatHexSize(largestZeroRun.Length),
            largestZeroRun.Offset >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"Largest continuous zero-filled region starts at text+0x{largestZeroRun.Offset:X}.")
                : "No zero-filled region found.");

        AddInstructionChecks(checks, provenance, text, "Rare Candy UI route",
        [
            new("ui-check-a", "UI check A", 0x00747988, EncodeCmpImmediate(28, RareCandyItemId), "CMP w28, #50"),
            new("ui-check-b", "UI check B", 0x00747D44, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("ui-check-c", "UI check C", 0x0074BA24, EncodeCmpImmediate(26, RareCandyItemId), "CMP w26, #50"),
            new("ui-check-d", "UI check D", 0x0074BDA8, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("ui-check-e", "UI check E", 0x0074DFE4, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("ui-check-f", "UI check F", 0x0074DFF8, EncodeCmpImmediate(28, RareCandyItemId), "CMP w28, #50"),
            new("ui-check-g", "UI check G", 0x0075CEFC, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("ui-check-h", "UI check H", 0x007BB204, EncodeCmpImmediate(20, RareCandyItemId), "CMP w20, #50"),
            new("ui-check-i", "UI check I", 0x007BB3C0, EncodeCmpImmediate(19, RareCandyItemId), "CMP w19, #50"),
            new("ui-check-j", "UI check J", 0x007BC1F8, EncodeCmpImmediate(8, RareCandyItemId), "CMP w8, #50"),
        ]);

        AddInstructionChecks(checks, provenance, text, "Rare Candy equal branch",
        [
            new("equal-branch-a", "Equal branch A", 0x00747DE0, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("equal-branch-b", "Equal branch B", 0x0074BE44, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("equal-branch-c", "Equal branch C", 0x0075CCE8, EncodeCmpImmediate(27, RareCandyItemId), "CMP w27, #50"),
            new("equal-branch-d", "Equal branch D", 0x0075D08C, EncodeCmpImmediate(10, RareCandyItemId), "CMP w10, #50"),
            new("equal-branch-e", "Equal branch E", 0x007BBFD4, EncodeCmpImmediate(23, RareCandyItemId), "CMP w23, #50"),
        ]);

        AddInstructionChecks(checks, provenance, text, "Royal Candy support",
        [
            new("exp-candy-upper-bound-a", "Exp Candy upper bound A", 0x007BC1BC, EncodeCmpImmediate(9, 4), "CMP w9, #4"),
            new("exp-candy-upper-bound-b", "Exp Candy upper bound B", 0x007BC1C4, EncodeCmpImmediate(9, 4), "CMP w9, #4"),
            new("consume-quantity-move", "Consume quantity move", 0x007B1F20, 0x2A0003E2, "MOV w2, w0"),
            new("allowed-consumable-upper-bound", "Allowed consumable upper bound", 0x007DDA8C, EncodeCmpImmediate(8, 0x32), "CMP w8, #0x32"),
        ]);

        var candidateImmediateHits = CountAlignedInstruction(text, EncodeCmpImmediate(8, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(9, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(19, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(20, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(23, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(27, RoyalCandyItemId))
            + CountAlignedInstruction(text, EncodeCmpImmediate(28, RoyalCandyItemId));
        AddCheck(
            checks,
            provenance,
            "royal-candy-immediate-scan",
            candidateImmediateHits == 0 ? "Info" : "Warning",
            ".text",
            string.Empty,
            "Royal Candy immediate scan",
            "0 patched CMP immediates in vanilla main",
            candidateImmediateHits.ToString(CultureInfo.InvariantCulture),
            candidateImmediateHits == 0
                ? "No obvious item-id 1128 CMP immediates were found in the known route registers."
                : "Potential already-patched or experimental main; review before applying new patches.");

        return checks;
    }

    private static void AddInstructionChecks(
        ICollection<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance,
        byte[] text,
        string area,
        IEnumerable<InstructionCheck> instructionChecks)
    {
        foreach (var check in instructionChecks)
        {
            AddInstructionCheck(checks, provenance, text, area, check);
        }
    }

    private static void AddInstructionCheck(
        ICollection<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance,
        byte[] text,
        string area,
        InstructionCheck check)
    {
        if (check.Offset < 0 || check.Offset + 4 > text.Length)
        {
            AddCheck(
                checks,
                provenance,
                check.CheckId,
                "Fail",
                area,
                FormatTextOffset(check.Offset),
                check.Name,
                FormatInstruction(check.Expected),
                "outside .text",
                "Expected instruction offset is outside the decompressed .text segment.");
            return;
        }

        var actual = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(check.Offset, 4));
        AddCheck(
            checks,
            provenance,
            check.CheckId,
            actual == check.Expected ? "Pass" : "Fail",
            area,
            FormatTextOffset(check.Offset),
            check.Name,
            $"{check.Description} / {FormatInstruction(check.Expected)}",
            FormatInstruction(actual),
            actual == check.Expected
                ? "Signature matches the known vanilla anchor."
                : "Signature mismatch. This main may be a different build or already patched.");
    }

    private static void AddHashCheck(
        ICollection<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance,
        string checkId,
        string area,
        string name,
        byte[] expected,
        byte[] actual)
    {
        var matches = expected.SequenceEqual(actual);
        AddCheck(
            checks,
            provenance,
            checkId,
            matches ? "Pass" : "Warning",
            area,
            string.Empty,
            name,
            ToHex(expected),
            ToHex(actual),
            matches ? "Segment hash matches the NSO header." : "Segment hash differs from the NSO header.");
    }

    private static void AddZeroRunCheck(
        ICollection<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance,
        byte[] text,
        string checkId,
        string name,
        int requiredBytes,
        int startOffset)
    {
        var offset = FindZeroRun(text, requiredBytes, startOffset);
        AddCheck(
            checks,
            provenance,
            checkId,
            offset >= 0 ? "Pass" : "Fail",
            ".text",
            offset >= 0 ? FormatTextOffset(offset) : string.Empty,
            name,
            string.Create(CultureInfo.InvariantCulture, $"{requiredBytes} zero bytes after text+0x{startOffset:X}"),
            offset >= 0 ? FormatTextOffset(offset) : "missing",
            offset >= 0 ? "A code cave is available for small stubs." : "No aligned zero run was found for this stub size.");
    }

    private static void AddCheck(
        ICollection<SwShExeFsPatchCheckRecord> checks,
        SwShExeFsPatchProvenance provenance,
        string checkId,
        string status,
        string area,
        string offset,
        string name,
        string expected,
        string actual,
        string notes)
    {
        checks.Add(new SwShExeFsPatchCheckRecord(
            string.Create(CultureInfo.InvariantCulture, $"{MainPatchId}:{checkId}"),
            MainPatchId,
            status,
            area,
            offset,
            name,
            expected,
            actual,
            notes,
            provenance));
    }

    private static int CountAlignedInstruction(byte[] text, uint instruction)
    {
        var count = 0;
        for (var offset = 0; offset <= text.Length - 4; offset += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, 4)) == instruction)
            {
                count++;
            }
        }

        return count;
    }

    private static int FindZeroRun(byte[] data, int requiredBytes, int startOffset)
    {
        var runStart = -1;
        for (var offset = Math.Max(0, startOffset); offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var alignedStart = (runStart + 3) & ~3;
                if (offset - alignedStart + 1 >= requiredBytes)
                {
                    return alignedStart;
                }

                continue;
            }

            runStart = -1;
        }

        return -1;
    }

    private static ZeroRun FindLargestZeroRun(byte[] data)
    {
        var best = new ZeroRun(-1, 0);
        var runStart = -1;
        for (var offset = 0; offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var length = offset - runStart + 1;
                if (length > best.Length)
                {
                    best = new ZeroRun(runStart, length);
                }

                continue;
            }

            runStart = -1;
        }

        return best;
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseExeFsPath, entry.RelativePath["exefs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShExeFsPatchProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShExeFsPatchProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static string FormatInstruction(uint instruction)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{instruction:X8}");
    }

    private static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"text+0x{offset:X}");
    }

    private static string FormatFileOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"file+0x{offset:X}");
    }

    private static string FormatMemoryOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{offset:X}");
    }

    private static string FormatHexSize(int size)
    {
        return string.Create(CultureInfo.InvariantCulture, $"0x{size:X}");
    }

    private static string ToHex(byte[] data)
    {
        return Convert.ToHexString(data);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.ExeFsPatches,
            "ExeFS Patch Manager",
            "ExeFS main validation, patch anchors, segment hashes, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.exefsPatches",
            Expected: expected);
    }

    private sealed record InstructionCheck(
        string CheckId,
        string Name,
        int Offset,
        uint Expected,
        string Description);

    private sealed record ZeroRun(int Offset, int Length);

    private sealed record ExeFsCompatibilityAnalysis(
        IReadOnlyList<SwShExeFsPatchRecord> Patches,
        IReadOnlyList<SwShExeFsSegmentRecord> Segments,
        IReadOnlyList<SwShExeFsPatchCheckRecord> Checks);
}
