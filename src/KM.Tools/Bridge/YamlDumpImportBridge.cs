// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.SpreadsheetImport;
using KM.Api.Workflows;
using KM.Core.Editing;
using KM.Core.GameDump;

namespace KM.Tools.Bridge;

internal static class YamlDumpImportBridge
{
    public const string ProfileId = "game-dump-yaml";

    public static bool IsYaml(string path) => Path.GetExtension(path).Equals(".yaml", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".yml", StringComparison.OrdinalIgnoreCase);

    public static LoadSpreadsheetImportWorkflowResponse WithYaml(LoadSpreadsheetImportWorkflowResponse response) =>
        new(WithYaml(response.Workflow));

    public static SpreadsheetImportWorkflowDto WithYaml(SpreadsheetImportWorkflowDto workflow)
    {
        if (workflow.Profiles.Any(profile => profile.ProfileId == ProfileId)) return workflow;
        var status = workflow.Summary.Availability == WorkflowAvailabilityDto.Available ? "available" : "readOnly";
        var profile = new SpreadsheetImportProfileRecordDto(ProfileId, "Editable YAML Game Dump", "yaml/yml", "auto", status,
            "Imports editable Game Dump categories. The file identifies its game, category and language. Invalid files leave pending edits unchanged.", [],
            new("backend:yaml-game-dump", ProjectFileLayerDto.Generated, ProjectFileGraphEntryStateDto.BaseOnly));
        return workflow with
        {
            Profiles = [profile, .. workflow.Profiles],
            Stats = workflow.Stats with { TotalProfileCount = workflow.Stats.TotalProfileCount + 1 },
        };
    }

    public static PreviewSpreadsheetImportResponse Preview(IEditableDumpProvider provider, string game, string path,
        EditSession? session, SpreadsheetImportWorkflowDto workflow)
    {
        var result = new DumpYamlImporter(provider).Preview(path, game, session);
        var rows = result.Rows.Select(row => new SpreadsheetImportRowPreviewRecordDto(row.Location.Line, row.RecordId,
            row.Status, row.Name, row.Updates.Select(update => new SpreadsheetImportCellPreviewRecordDto(update.Field.Key,
                update.Location.Field, update.Value, row.Status, $"Line {update.Location.Line}")).ToArray(),
            row.Issues.Select(issue => ProjectBridgeMapper.ToDto(issue.ToDiagnostic())).ToArray())).ToList();
        rows.AddRange(result.Issues.Select(issue => new SpreadsheetImportRowPreviewRecordDto(issue.Location.Line, "", "rejected",
            issue.Location.Field, [], [ProjectBridgeMapper.ToDto(issue.ToDiagnostic())])));
        rows.AddRange(result.Diagnostics.Where(diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error)
            .Select(diagnostic => new SpreadsheetImportRowPreviewRecordDto(1, "", "rejected", diagnostic.Field ?? "category", [],
                [ProjectBridgeMapper.ToDto(diagnostic)])));
        var diagnostics = result.Diagnostics.Select(ProjectBridgeMapper.ToDto)
            .Concat(result.Issues.Select(issue => ProjectBridgeMapper.ToDto(issue.ToDiagnostic())))
            .Concat(result.Rows.SelectMany(row => row.Issues).Select(issue => ProjectBridgeMapper.ToDto(issue.ToDiagnostic()))).ToArray();
        // Unchanged records can number in the hundreds of thousands. Counts include them; the UI only needs changed or rejected rows.
        var visible = rows.Where(row => row.Status != "skipped" || row.Diagnostics.Count > 0).ToArray();
        return new(WithYaml(workflow), EditSessionBridgeMapper.ToDto(result.Session),
            new(ProfileId, path, rows.Count, rows.Count(row => row.Status == "accepted"), rows.Count(row => row.Status == "rejected"),
                rows.Count(row => row.Status == "skipped"), visible), diagnostics);
    }
}
