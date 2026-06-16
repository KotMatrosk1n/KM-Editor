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

    public SwShDynamaxAdventureSeedPlanningService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public SwShDynamaxAdventureSeedPlanResult Predict(
        ProjectPaths paths,
        ulong seed,
        int npcCount,
        IReadOnlyList<int>? requiredRows = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new List<ValidationDiagnostic>();
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
        catch (InvalidDataException exception)
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
        catch (InvalidDataException exception)
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
            return new DynamaxAdventurePlanningContext(
                SwShDynamaxAdventureArchive.Parse(File.ReadAllBytes(adventureSource.AbsolutePath)),
                SwShPersonalTable.Parse(File.ReadAllBytes(personalSource.AbsolutePath)).Records);
        }
        catch (InvalidDataException exception)
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
