// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.ExeFs;

public sealed class SwShExeFsPatchWorkflowService
{
    public const string ExeFsMainPath = "exefs/main";
    public const string MainPatchId = "exefs-main-compatibility";

    private const int RareCandyItemId = 50;
    private const int RoyalCandyItemId = 1128;
    private const int RareCandyUiHookCodeCaveSearchStart = 0x007BC338;

    private readonly SwShParsedDataCache parsedDataCache;

    public SwShExeFsPatchWorkflowService(SwShParsedDataCache? parsedDataCache = null)
    {
        this.parsedDataCache = parsedDataCache ?? new SwShParsedDataCache();
    }

    public void ClearMemoryCache()
    {
        parsedDataCache.Clear();
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
            var facts = parsedDataCache.GetOrAdd(
                sourcePath,
                CreateCompatibilityFacts).Value;
            var baseSource = CreateBaseExecutableSourceValidation(
                project.Paths,
                graphEntry,
                facts);
            var checks = CreateCheckRecords(
                facts,
                project.Paths.SelectedGame,
                baseSource,
                provenance);
            var segments = facts.Segments
                .Select(segment => segment.ToRecord(provenance))
                .ToArray();
            var patches = new[] { CreateMainPatchRecord(facts, checks, provenance) };

            if (!baseSource.IsReady && graphEntry.LayeredFile is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    baseSource.Message,
                    file: graphEntry.RelativePath,
                    expected: "Matching safe vanilla base exefs/main"));
            }

            if (checks.Any(check => check.Status == "Fail"))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "ExeFS main has failing compatibility checks.",
                    file: graphEntry.RelativePath,
                    expected: "Known Sword/Shield 1.3.2-style patch anchors"));
            }

            return CreateWorkflow(
                summary,
                patches,
                segments,
                checks,
                sourceFileCount: graphEntry.BaseFile is not null && graphEntry.LayeredFile is not null ? 2 : 1,
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
        catch (UnauthorizedAccessException exception)
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

    private static ExeFsCompatibilityFacts CreateCompatibilityFacts(string sourcePath)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var nso = NsoFile.Parse(bytes);
        var detectedGame = SwShExeFsRoyalCandyMainPatcher.DetectSupportedGame(nso.BuildId);
        return new ExeFsCompatibilityFacts(
            bytes.Length,
            nso.Version,
            nso.Flags,
            nso.BuildId.ToArray(),
            nso.RawHeader.ToArray(),
            nso.Segments
                .Select(segment => new ExeFsSegmentLayoutFact(
                    segment.Name,
                    segment.Header.MemoryOffset,
                    segment.Header.DecompressedSize))
                .ToArray(),
            detectedGame,
            CreateSegmentFacts(nso),
            CreateAnchorCheckFacts(nso),
            CreatePatchPreflight(bytes, detectedGame));
    }

    private BaseExecutableSourceValidation CreateBaseExecutableSourceValidation(
        ProjectPaths paths,
        ProjectFileGraphEntry graphEntry,
        ExeFsCompatibilityFacts sourceFacts)
    {
        if (graphEntry.LayeredFile is null)
        {
            return new BaseExecutableSourceValidation(
                IsReady: graphEntry.BaseFile is not null,
                Actual: FormatFileState(graphEntry.State),
                graphEntry.BaseFile is not null
                    ? "The effective executable source is the base exefs/main."
                    : "A base exefs/main is not available.");
        }

        if (graphEntry.BaseFile is null)
        {
            return new BaseExecutableSourceValidation(
                IsReady: false,
                Actual: FormatFileState(graphEntry.State),
                "Layered exefs/main can be inspected, but staging requires a matching safe vanilla base exefs/main.");
        }

        var basePath = ResolveBaseSourcePath(paths, graphEntry);
        if (basePath is null || !File.Exists(basePath))
        {
            return new BaseExecutableSourceValidation(
                IsReady: false,
                Actual: FormatFileState(graphEntry.State),
                "Layered exefs/main does not have a readable matching base exefs/main.");
        }

        try
        {
            var baseFacts = parsedDataCache.GetOrAdd(
                basePath,
                CreateCompatibilityFacts).Value;
            if (!ExecutableIdentityAndLayoutMatch(sourceFacts, baseFacts, out var mismatch))
            {
                return new BaseExecutableSourceValidation(
                    IsReady: false,
                    Actual: FormatFileState(graphEntry.State),
                    $"Layered exefs/main does not match the base executable identity and layout: {mismatch}");
            }

            if (!baseFacts.Preflight.IsReady)
            {
                return new BaseExecutableSourceValidation(
                    IsReady: false,
                    Actual: FormatFileState(graphEntry.State),
                    $"Base exefs/main is not a safe vanilla executable: {baseFacts.Preflight.Message}");
            }

            return new BaseExecutableSourceValidation(
                IsReady: true,
                Actual: FormatFileState(graphEntry.State),
                "The layered executable matches a supported, safe vanilla base identity and layout.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new BaseExecutableSourceValidation(
                IsReady: false,
                Actual: FormatFileState(graphEntry.State),
                $"Base exefs/main could not be validated as a matching safe vanilla executable: {exception.Message}");
        }
    }

    private static bool ExecutableIdentityAndLayoutMatch(
        ExeFsCompatibilityFacts source,
        ExeFsCompatibilityFacts baseFacts,
        out string mismatch)
    {
        if (source.Version != baseFacts.Version)
        {
            mismatch = "NSO versions differ.";
            return false;
        }

        if (source.Flags != baseFacts.Flags)
        {
            mismatch = "NSO flags differ.";
            return false;
        }

        if (!source.BuildId.SequenceEqual(baseFacts.BuildId))
        {
            mismatch = "build IDs differ.";
            return false;
        }

        if (!SwShExeFsMainComparison.StableHeaderBytesMatch(source.RawHeader, baseFacts.RawHeader))
        {
            mismatch = "stable NSO header metadata differs.";
            return false;
        }

        if (source.SegmentLayouts.Count != baseFacts.SegmentLayouts.Count
            || !source.SegmentLayouts
                .Zip(baseFacts.SegmentLayouts)
                .All(pair => pair.First.Name == pair.Second.Name
                    && pair.First.MemoryOffset == pair.Second.MemoryOffset
                    && pair.First.DecompressedSize == pair.Second.DecompressedSize))
        {
            mismatch = "segment memory offsets or decompressed sizes differ.";
            return false;
        }

        mismatch = string.Empty;
        return true;
    }

    private static ExeFsPatchPreflight CreatePatchPreflight(
        byte[] sourceBytes,
        ProjectGame? detectedGame)
    {
        if (detectedGame is null)
        {
            return new ExeFsPatchPreflight(
                IsReady: false,
                "The executable build ID is not one of the supported Sword/Shield 1.3.2 builds.");
        }

        try
        {
            var installation = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                sourceBytes,
                detectedGame.Value);
            if (installation.Kind != SwShRoyalCandyExeFsSignatureKind.NotInstalled)
            {
                return new ExeFsPatchPreflight(
                    IsReady: false,
                    installation.Message);
            }

            var output = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(
                sourceBytes,
                detectedGame.Value);
            SwShExeFsRoyalCandyMainPatcher.VerifyBasePatchOutput(
                sourceBytes,
                output,
                detectedGame.Value);
            return new ExeFsPatchPreflight(
                IsReady: true,
                "All required instructions, game-specific helpers, code caves, and output preservation checks passed.");
        }
        catch (InvalidDataException exception)
        {
            return new ExeFsPatchPreflight(IsReady: false, exception.Message);
        }
    }

    private static IReadOnlyList<SwShExeFsPatchCheckRecord> CreateCheckRecords(
        ExeFsCompatibilityFacts facts,
        ProjectGame? selectedGame,
        BaseExecutableSourceValidation baseSource,
        SwShExeFsPatchProvenance provenance)
    {
        var checks = new List<SwShExeFsPatchCheckRecord>();
        var buildId = ToHex(facts.BuildId);
        checks.Add(CreateCheckRecord(
            "supported-build",
            facts.DetectedGame is null ? "Fail" : "Pass",
            "main",
            string.Empty,
            "Supported game build",
            "Sword or Shield 1.3.2 build ID",
            facts.DetectedGame is null ? buildId : FormatGame(facts.DetectedGame),
            facts.DetectedGame is null
                ? "This build is not supported for executable patching."
                : "Build ID maps to one verified game layout.",
            provenance));

        var selectedGameMatches = facts.DetectedGame is not null
            && (selectedGame is null || selectedGame == facts.DetectedGame);
        checks.Add(CreateCheckRecord(
            "selected-game",
            selectedGameMatches ? "Pass" : "Fail",
            "main",
            string.Empty,
            "Selected game route",
            selectedGame is null ? "Game inferred from a supported build ID" : FormatGame(selectedGame),
            FormatGame(facts.DetectedGame),
            selectedGameMatches
                ? selectedGame is null
                    ? "The game route was inferred from the recognized build ID."
                    : "The selected game matches the executable build ID."
                : "The selected game does not match the executable build ID.",
            provenance));

        checks.Add(CreateCheckRecord(
            "base-source",
            baseSource.IsReady ? "Pass" : "Fail",
            "main",
            string.Empty,
            "Base executable source",
            "Matching safe vanilla base exefs/main for any layered override",
            baseSource.Actual,
            baseSource.Message,
            provenance));

        checks.AddRange(facts.AnchorChecks.Select(check => check.ToRecord(provenance)));
        checks.Add(CreateCheckRecord(
            "exact-patch-preflight",
            facts.Preflight.IsReady ? "Pass" : "Fail",
            ".text",
            string.Empty,
            "Exact Royal Candy patch preflight",
            "All owned anchors, helper routes, code caves, and preserved output semantics",
            facts.Preflight.IsReady ? "Ready" : "Blocked",
            facts.Preflight.Message,
            provenance));
        return checks;
    }

    private static SwShExeFsPatchCheckRecord CreateCheckRecord(
        string checkId,
        string status,
        string area,
        string offset,
        string name,
        string expected,
        string actual,
        string notes,
        SwShExeFsPatchProvenance provenance)
    {
        return new SwShExeFsPatchCheckRecord(
            string.Create(CultureInfo.InvariantCulture, $"{MainPatchId}:{checkId}"),
            MainPatchId,
            status,
            area,
            offset,
            name,
            expected,
            actual,
            notes,
            provenance);
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
        ExeFsCompatibilityFacts facts,
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
            "Royal Candy executable patch",
            ExeFsMainPath,
            "Executable patch",
            status,
            "Installs only the Royal Candy executable portion after exact build and anchor verification. The Royal Candy editor owns the complete data, script, and shop install lifecycle.",
            [
                $"Build ID: {ToHex(facts.BuildId)}",
                $"Detected game: {FormatGame(facts.DetectedGame)}",
                string.Create(CultureInfo.InvariantCulture, $"File size: 0x{facts.FileSize:X} bytes"),
                $"Flags: {facts.Flags}",
                string.Create(CultureInfo.InvariantCulture, $"Checks: {checks.Count} total, {checks.Count(check => check.Status == "Fail")} failing, {checks.Count(check => check.Status == "Warning")} warnings")
            ],
            provenance);
    }

    private static IReadOnlyList<ExeFsSegmentFact> CreateSegmentFacts(NsoFile nso)
    {
        return nso.Segments
            .Select(segment =>
            {
                var actualHash = NsoFile.ComputeHash(segment.DecompressedData);
                var hashMatches = actualHash.SequenceEqual(segment.Hash);
                var hashRequired = IsSegmentHashRequired(nso.Flags, segment.Name);
                var hashStatus = hashMatches ? "Pass" : hashRequired ? "Fail" : "Warning";
                return new ExeFsSegmentFact(
                    segment.Name.TrimStart('.'),
                    segment.Name,
                    FormatFileOffset(segment.Header.FileOffset),
                    FormatMemoryOffset(segment.Header.MemoryOffset),
                    FormatHexSize(segment.Header.DecompressedSize),
                    FormatHexSize(segment.CompressedSize),
                    ToHex(actualHash),
                    hashStatus);
            })
            .ToArray();
    }

    private static IReadOnlyList<ExeFsPatchCheckFact> CreateAnchorCheckFacts(NsoFile nso)
    {
        var checks = new List<ExeFsPatchCheckFact>();
        var text = nso.Text.DecompressedData;
        var largestZeroRun = FindLargestZeroRun(text);

        AddCheck(checks, "nso-magic", "Pass", "main", string.Empty, "NSO magic", "NSO0", "NSO0", "Valid NSO header.");
        AddHashCheck(
            checks,
            "text-hash",
            ".text",
            "Segment hash",
            nso.Text.Hash,
            NsoFile.ComputeHash(nso.Text.DecompressedData),
            nso.Flags.HasFlag(NsoFlags.CheckHashText));
        AddHashCheck(
            checks,
            "ro-hash",
            ".ro",
            "Segment hash",
            nso.Ro.Hash,
            NsoFile.ComputeHash(nso.Ro.DecompressedData),
            nso.Flags.HasFlag(NsoFlags.CheckHashRo));
        AddHashCheck(
            checks,
            "data-hash",
            ".data",
            "Segment hash",
            nso.Data.Hash,
            NsoFile.ComputeHash(nso.Data.DecompressedData),
            nso.Flags.HasFlag(NsoFlags.CheckHashData));
        AddZeroRunCheck(checks, text, "patch-code-cave", "Patch code cave", 0x0C, RareCandyUiHookCodeCaveSearchStart);
        AddCheck(
            checks,
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

        AddInstructionChecks(checks, text, "Rare Candy UI route",
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

        AddInstructionChecks(checks, text, "Rare Candy equal branch",
        [
            new("equal-branch-a", "Equal branch A", 0x00747DE0, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("equal-branch-b", "Equal branch B", 0x0074BE44, EncodeCmpImmediate(9, RareCandyItemId), "CMP w9, #50"),
            new("equal-branch-c", "Equal branch C", 0x0075CCE8, EncodeCmpImmediate(27, RareCandyItemId), "CMP w27, #50"),
            new("equal-branch-d", "Equal branch D", 0x0075D08C, EncodeCmpImmediate(10, RareCandyItemId), "CMP w10, #50"),
            new("equal-branch-e", "Equal branch E", 0x007BBFD4, EncodeCmpImmediate(23, RareCandyItemId), "CMP w23, #50"),
        ]);

        AddInstructionChecks(checks, text, "Royal Candy support",
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
        ICollection<ExeFsPatchCheckFact> checks,
        byte[] text,
        string area,
        IEnumerable<InstructionCheck> instructionChecks)
    {
        foreach (var check in instructionChecks)
        {
            AddInstructionCheck(checks, text, area, check);
        }
    }

    private static void AddInstructionCheck(
        ICollection<ExeFsPatchCheckFact> checks,
        byte[] text,
        string area,
        InstructionCheck check)
    {
        if (check.Offset < 0 || check.Offset + 4 > text.Length)
        {
            AddCheck(
                checks,
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
        ICollection<ExeFsPatchCheckFact> checks,
        string checkId,
        string area,
        string name,
        byte[] expected,
        byte[] actual,
        bool required)
    {
        var matches = expected.SequenceEqual(actual);
        AddCheck(
            checks,
            checkId,
            matches ? "Pass" : required ? "Fail" : "Warning",
            area,
            string.Empty,
            name,
            ToHex(expected),
            ToHex(actual),
            matches
                ? required
                    ? "Segment hash matches the NSO header and the corresponding NSO hash-check flag is enabled."
                    : "Segment hash matches the NSO header."
                : required
                    ? "Segment hash differs from the NSO header while the corresponding NSO hash-check flag is enabled."
                    : "Segment hash differs from the NSO header, but the corresponding NSO hash-check flag is disabled.");
    }

    private static bool IsSegmentHashRequired(NsoFlags flags, string segmentName)
    {
        return segmentName switch
        {
            ".text" => flags.HasFlag(NsoFlags.CheckHashText),
            ".ro" => flags.HasFlag(NsoFlags.CheckHashRo),
            ".data" => flags.HasFlag(NsoFlags.CheckHashData),
            _ => false,
        };
    }

    private static void AddZeroRunCheck(
        ICollection<ExeFsPatchCheckFact> checks,
        byte[] text,
        string checkId,
        string name,
        int requiredBytes,
        int startOffset)
    {
        var offset = FindZeroRun(text, requiredBytes, startOffset);
        AddCheck(
            checks,
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
        ICollection<ExeFsPatchCheckFact> checks,
        string checkId,
        string status,
        string area,
        string offset,
        string name,
        string expected,
        string actual,
        string notes)
    {
        checks.Add(new ExeFsPatchCheckFact(
            string.Create(CultureInfo.InvariantCulture, $"{MainPatchId}:{checkId}"),
            status,
            area,
            offset,
            name,
            expected,
            actual,
            notes));
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

    private static string? ResolveBaseSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.BaseFile is null
            || !entry.BaseFile.RelativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CombineGraphPath(
            paths.BaseExeFsPath,
            entry.BaseFile.RelativePath["exefs/".Length..]);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(normalizedRoot, comparison) ? targetPath : null;
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

    private static string FormatGame(ProjectGame? game)
    {
        return game switch
        {
            ProjectGame.Sword => "Sword",
            ProjectGame.Shield => "Shield",
            null => "Unsupported",
            _ => game.Value.ToString(),
        };
    }

    private static string FormatFileState(ProjectFileGraphEntryState state)
    {
        return state switch
        {
            ProjectFileGraphEntryState.BaseOnly => "Base only",
            ProjectFileGraphEntryState.LayeredOverride => "Layered override",
            ProjectFileGraphEntryState.LayeredOnly => "Layered only",
            _ => state.ToString(),
        };
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.ExeFsPatches,
            "ExeFS Patch Manager",
            "Royal Candy executable patch readiness, exact build checks, segment hashes, and source provenance.",
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

    private sealed record ExeFsCompatibilityFacts(
        int FileSize,
        uint Version,
        NsoFlags Flags,
        byte[] BuildId,
        byte[] RawHeader,
        IReadOnlyList<ExeFsSegmentLayoutFact> SegmentLayouts,
        ProjectGame? DetectedGame,
        IReadOnlyList<ExeFsSegmentFact> Segments,
        IReadOnlyList<ExeFsPatchCheckFact> AnchorChecks,
        ExeFsPatchPreflight Preflight);

    private sealed record ExeFsSegmentLayoutFact(
        string Name,
        int MemoryOffset,
        int DecompressedSize);

    private sealed record ExeFsSegmentFact(
        string SegmentId,
        string Name,
        string FileOffset,
        string MemoryOffset,
        string DecompressedSize,
        string CompressedSize,
        string Sha256,
        string HashStatus)
    {
        public SwShExeFsSegmentRecord ToRecord(SwShExeFsPatchProvenance provenance)
        {
            return new SwShExeFsSegmentRecord(
                SegmentId,
                Name,
                FileOffset,
                MemoryOffset,
                DecompressedSize,
                CompressedSize,
                Sha256,
                HashStatus,
                provenance);
        }
    }

    private sealed record ExeFsPatchCheckFact(
        string CheckId,
        string Status,
        string Area,
        string Offset,
        string Name,
        string Expected,
        string Actual,
        string Notes)
    {
        public SwShExeFsPatchCheckRecord ToRecord(SwShExeFsPatchProvenance provenance)
        {
            return new SwShExeFsPatchCheckRecord(
                CheckId,
                MainPatchId,
                Status,
                Area,
                Offset,
                Name,
                Expected,
                Actual,
                Notes,
                provenance);
        }
    }

    private sealed record ExeFsPatchPreflight(bool IsReady, string Message);

    private sealed record BaseExecutableSourceValidation(
        bool IsReady,
        string Actual,
        string Message);
}
