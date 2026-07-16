// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using System.Globalization;

namespace KM.SwSh.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSeedPlanningService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly bool allowSyntheticTestTables;

    public SwShDynamaxAdventureSeedPlanningService(ProjectWorkspaceService? projectWorkspaceService = null)
        : this(projectWorkspaceService, allowSyntheticTestTables: false)
    {
    }

    private SwShDynamaxAdventureSeedPlanningService(
        ProjectWorkspaceService? projectWorkspaceService,
        bool allowSyntheticTestTables)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.allowSyntheticTestTables = allowSyntheticTestTables;
    }

    internal static SwShDynamaxAdventureSeedPlanningService CreateForSyntheticTests(
        ProjectWorkspaceService? projectWorkspaceService = null)
    {
        return new SwShDynamaxAdventureSeedPlanningService(
            projectWorkspaceService,
            allowSyntheticTestTables: true);
    }

    public SwShDynamaxAdventureSeedPlanResult Predict(
        ProjectPaths paths,
        ulong seed,
        int npcCount,
        IReadOnlyList<int>? requiredRows = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
        if (npcCount is < 0 or > 3)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed prediction requires an NPC count from 0 through 3.",
                field: "npcCount",
                expected: "0-3"));
        }
        if (requiredRows is { Count: > SwShDynamaxAdventureSeedPlanner.MaximumRequiredRows })
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed prediction accepts at most {SwShDynamaxAdventureSeedPlanner.MaximumRequiredRows.ToString(CultureInfo.InvariantCulture)} required rows.",
                field: "requiredRows",
                expected: "A bounded list of Adventure row indexes"));
        }
        if (diagnostics.Count > 0)
        {
            return new SwShDynamaxAdventureSeedPlanResult(seed, npcCount, [], [], [], diagnostics);
        }

        var context = LoadPlanningContext(paths, diagnostics);
        if (context is null)
        {
            return new SwShDynamaxAdventureSeedPlanResult(
                seed,
                npcCount,
                [],
                [],
                [],
                diagnostics);
        }

        try
        {
            var prediction = SwShDynamaxAdventureSeedPlanner.Predict(
                seed,
                npcCount,
                context.Archive.Entries,
                context.PersonalRecords);
            AddMissingRequiredRowDiagnostic(requiredRows ?? [], context.Archive.Entries.Count, diagnostics, DiagnosticSeverity.Warning);
            AddRequiredBossRowDiagnostic(requiredRows ?? [], context.Archive.Entries.Count, diagnostics, DiagnosticSeverity.Warning);
            var rowPositions = SwShDynamaxAdventureSeedPlanner.GetRowPositions(
                prediction,
                requiredRows ?? []);

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Predicted Dynamax Adventures seed 0x{seed:X16} against the active Adventure table."));

            return new SwShDynamaxAdventureSeedPlanResult(
                seed,
                npcCount,
                prediction.Rentals.Select(ToPublicTemplate).ToArray(),
                prediction.Encounters.Select(ToPublicTemplate).ToArray(),
                rowPositions.Select(ToPublicRowPosition).ToArray(),
                diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed prediction failed: {exception.Message}",
                expected: "Adventure rows and personal data"));
        }

        return new SwShDynamaxAdventureSeedPlanResult(seed, npcCount, [], [], [], diagnostics);
    }

    public SwShDynamaxAdventureSeedSearchPlanResult SearchRows(
        ProjectPaths paths,
        IReadOnlyList<int> requiredRows,
        int npcCount,
        ulong startSeed,
        ulong limit,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(requiredRows);

        var diagnostics = new List<ValidationDiagnostic>();
        if (requiredRows.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed search requires at least one required row.",
                field: "requiredRows",
                expected: "One or more Adventure row indexes"));
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        if (requiredRows.Count > SwShDynamaxAdventureSeedPlanner.MaximumRequiredRows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed search accepts at most {SwShDynamaxAdventureSeedPlanner.MaximumRequiredRows.ToString(CultureInfo.InvariantCulture)} required rows.",
                field: "requiredRows",
                expected: "A bounded list of Adventure row indexes"));
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        if (npcCount is < 0 or > 3)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed search requires an NPC count from 0 through 3.",
                field: "npcCount",
                expected: "0-3"));
        }
        if (limit > SwShDynamaxAdventureSeedPlanner.MaximumSearchLimit)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed search limit cannot exceed {SwShDynamaxAdventureSeedPlanner.MaximumSearchLimit.ToString("N0", CultureInfo.InvariantCulture)}.",
                field: "limit",
                expected: $"0-{SwShDynamaxAdventureSeedPlanner.MaximumSearchLimit.ToString(CultureInfo.InvariantCulture)}"));
        }
        if (maxResults is < 1 or > SwShDynamaxAdventureSeedPlanner.MaximumSearchResults)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed search results must be between 1 and {SwShDynamaxAdventureSeedPlanner.MaximumSearchResults.ToString(CultureInfo.InvariantCulture)}.",
                field: "maxResults",
                expected: $"1-{SwShDynamaxAdventureSeedPlanner.MaximumSearchResults.ToString(CultureInfo.InvariantCulture)}"));
        }
        if (diagnostics.Count > 0)
        {
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        var context = LoadPlanningContext(paths, diagnostics);
        if (context is null)
        {
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        var missingRows = requiredRows
            .Distinct()
            .Where(row => row < 0 || row >= context.Archive.Entries.Count)
            .ToArray();
        if (missingRows.Length > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed search includes row(s) outside the loaded table: {string.Join(", ", missingRows)}.",
                field: "requiredRows",
                expected: $"Rows 0 through {(context.Archive.Entries.Count - 1).ToString(CultureInfo.InvariantCulture)}"));
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        AddRequiredBossRowDiagnostic(requiredRows, context.Archive.Entries.Count, diagnostics, DiagnosticSeverity.Error);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                [],
                diagnostics);
        }

        try
        {
            var results = SwShDynamaxAdventureSeedPlanner.SearchRows(
                    context.Archive.Entries,
                    context.PersonalRecords,
                    requiredRows,
                    npcCount,
                    startSeed,
                    limit,
                    maxResults)
                .Select(result => new SwShDynamaxAdventureSeedSearchPlanMatch(
                    result.Seed,
                    result.Positions.Select(ToPublicRowPosition).ToArray()))
                .ToArray();

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Searched {limit.ToString("N0", CultureInfo.InvariantCulture)} Dynamax Adventures seed value(s) against the active Adventure table."));

            return new SwShDynamaxAdventureSeedSearchPlanResult(
                npcCount,
                startSeed,
                limit,
                maxResults,
                results,
                diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed search failed: {exception.Message}",
                expected: "Adventure rows and personal data"));
        }

        return new SwShDynamaxAdventureSeedSearchPlanResult(
            npcCount,
            startSeed,
            limit,
            maxResults,
            [],
            diagnostics);
    }

    private DynamaxAdventurePlanningContext? LoadPlanningContext(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShDynamaxAdventuresWorkflowService.IsSupportedGame(paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed planning requires Pokemon Sword or Pokemon Shield to be selected explicitly.",
                expected: "Selected Pokemon Sword or Pokemon Shield project"));
            return null;
        }

        var project = projectWorkspaceService.Open(paths);
        var adventureSource = SwShDynamaxAdventuresWorkflowService.ResolveDynamaxAdventureDataSource(project);
        if (adventureSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed planning could not resolve the Adventure table.",
                file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                expected: "Readable Sword/Shield Dynamax Adventures table"));
            return null;
        }

        var personalSource = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (personalSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dynamax Adventures seed planning could not resolve personal data.",
                file: SwShPersonalTable.PersonalDataRelativePath,
                expected: "Readable Sword/Shield personal_total.bin"));
            return null;
        }

        try
        {
            var baseAdventurePath = CombineGraphPath(
                paths.BaseRomFsPath,
                SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..]);
            if (baseAdventurePath is null || !File.Exists(baseAdventurePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures seed planning could not resolve the verified base Adventure table.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Verified canonical base Adventure table"));
                return null;
            }

            var baseBytes = SwShDynamaxAdventuresWorkflowService.ReadBoundedDynamaxAdventureTable(baseAdventurePath);
            var baseArchive = SwShDynamaxAdventureArchive.Parse(baseBytes);
            if (!allowSyntheticTestTables
                && !SwShDynamaxAdventuresWorkflowService.IsCanonicalBaseDynamaxAdventureTable(baseBytes, baseArchive))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures seed planning rejected the base Adventure table identity.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Verified canonical base Adventure table"));
                return null;
            }

            var effectiveBytes = SwShDynamaxAdventuresWorkflowService.ReadBoundedDynamaxAdventureTable(adventureSource.AbsolutePath);
            var effectiveArchive = SwShDynamaxAdventureArchive.Parse(effectiveBytes);
            if (!SwShDynamaxAdventuresWorkflowService.TryValidateRecordContractDomain(
                effectiveArchive.Entries,
                out var invalidEntry,
                out var invalidField,
                out var expected))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dynamax Adventures seed planning rejected row {invalidEntry.ToString(CultureInfo.InvariantCulture)} because {invalidField} is outside the supported domain.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    field: invalidField,
                    expected: expected));
                return null;
            }

            if (!SwShDynamaxAdventuresWorkflowService.IsDynamaxAdventureTableLayoutCompatible(
                baseArchive,
                baseBytes,
                effectiveArchive,
                effectiveBytes))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Dynamax Adventures seed planning rejected an effective table that is not a supported layout-preserving projection of the base table.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    expected: "Canonical layout with supported in-place row edits"));
                return null;
            }

            var personalRecords = SwShPersonalTable.Parse(
                File.ReadAllBytes(personalSource.AbsolutePath)).Records;
            if (!SwShDynamaxAdventuresWorkflowService.TryValidatePersonalRecordResolution(
                effectiveArchive.Entries,
                personalRecords,
                out invalidEntry,
                out expected))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dynamax Adventures seed planning rejected row {invalidEntry.ToString(CultureInfo.InvariantCulture)} because its form does not exist for that species in Sword/Shield personal data.",
                    file: SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
                    field: SwShDynamaxAdventuresWorkflowService.FormField,
                    expected: expected));
                return null;
            }

            return new DynamaxAdventurePlanningContext(
                effectiveArchive,
                personalRecords);
        }
        catch (Exception exception) when (exception is
            InvalidDataException
            or ArgumentOutOfRangeException
            or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed planning could not decode required data: {exception.Message}",
                expected: "Sword/Shield Dynamax Adventures and personal tables"));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed planning could not read required data: {exception.Message}",
                expected: "Readable project files"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dynamax Adventures seed planning could not read required data: {exception.Message}",
                expected: "Readable project files"));
        }

        return null;
    }

    private static void AddMissingRequiredRowDiagnostic(
        IReadOnlyList<int> requiredRows,
        int rowCount,
        ICollection<ValidationDiagnostic> diagnostics,
        DiagnosticSeverity severity)
    {
        var missingRows = requiredRows
            .Distinct()
            .Where(row => row < 0 || row >= rowCount)
            .Order()
            .ToArray();
        if (missingRows.Length == 0)
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            severity,
            $"Dynamax Adventures seed planning includes row(s) outside the loaded table: {string.Join(", ", missingRows)}.",
            field: "requiredRows",
            expected: $"Rows 0 through {(rowCount - 1).ToString(CultureInfo.InvariantCulture)}"));
    }

    private static void AddRequiredBossRowDiagnostic(
        IReadOnlyList<int> requiredRows,
        int rowCount,
        ICollection<ValidationDiagnostic> diagnostics,
        DiagnosticSeverity severity)
    {
        var bossRows = requiredRows
            .Distinct()
            .Where(row => row >= SwShDynamaxAdventureSeedPlanner.DefaultBossStartRow && row < rowCount)
            .Order()
            .ToArray();
        if (bossRows.Length == 0)
        {
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            severity,
            $"Dynamax Adventures seed planning cannot select boss row(s) {string.Join(", ", bossRows)}. The rental choice seed selects rental and normal route rows; final bosses use target species and session state.",
            field: "requiredRows",
            expected: "Normal route row indexes below 226"));
    }

    private static SwShDynamaxAdventureSeedPlanTemplate ToPublicTemplate(
        SwShDynamaxAdventureSeedTemplate template)
    {
        return new SwShDynamaxAdventureSeedPlanTemplate(
            template.Row,
            template.Species,
            template.Form,
            template.IsBoss);
    }

    private static SwShDynamaxAdventureSeedPlanRowPosition ToPublicRowPosition(
        SwShDynamaxAdventureSeedRowPosition position)
    {
        return new SwShDynamaxAdventureSeedPlanRowPosition(
            position.Row,
            position.Kind == SwShDynamaxAdventureSeedSlotKind.Rental
                ? SwShDynamaxAdventureSeedPlanSlotKind.Rental
                : SwShDynamaxAdventureSeedPlanSlotKind.Encounter,
            position.Slot);
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
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

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: SwShDynamaxAdventuresWorkflowService.DynamaxAdventuresEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record DynamaxAdventurePlanningContext(
        SwShDynamaxAdventureArchive Archive,
        IReadOnlyList<SwShPersonalRecord> PersonalRecords);

    private sealed record WorkflowFileSource(
        ProjectFileGraphEntry GraphEntry,
        string AbsolutePath);
}

public sealed record SwShDynamaxAdventureSeedPlanResult(
    ulong Seed,
    int NpcCount,
    IReadOnlyList<SwShDynamaxAdventureSeedPlanTemplate> Rentals,
    IReadOnlyList<SwShDynamaxAdventureSeedPlanTemplate> Encounters,
    IReadOnlyList<SwShDynamaxAdventureSeedPlanRowPosition> RequiredRowPositions,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShDynamaxAdventureSeedSearchPlanResult(
    int NpcCount,
    ulong StartSeed,
    ulong Limit,
    int MaxResults,
    IReadOnlyList<SwShDynamaxAdventureSeedSearchPlanMatch> Results,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShDynamaxAdventureSeedSearchPlanMatch(
    ulong Seed,
    IReadOnlyList<SwShDynamaxAdventureSeedPlanRowPosition> Positions);

public sealed record SwShDynamaxAdventureSeedPlanTemplate(
    int Row,
    int Species,
    int Form,
    bool IsBoss);

public enum SwShDynamaxAdventureSeedPlanSlotKind
{
    Rental,
    Encounter,
}

public sealed record SwShDynamaxAdventureSeedPlanRowPosition(
    int Row,
    SwShDynamaxAdventureSeedPlanSlotKind Kind,
    int Slot);
