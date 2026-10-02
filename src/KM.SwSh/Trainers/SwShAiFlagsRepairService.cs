// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;

namespace KM.SwSh.Trainers;

public sealed record SwShAiFlagsRepairRow(int TrainerId, string Name, string SourceFile, int CurrentFlags,
    int? ProposedFlags, string Fingerprint, bool Candidate, bool PreviouslyFixed, bool ChangedSinceFix,
    DateTimeOffset? FixedAtUtc, int? BaseFlags);
public sealed record SwShAiFlagsRepairSelection(int TrainerId, string Fingerprint, bool AcknowledgePreviousFix);
public sealed record SwShAiFlagsRepairWorkflow(bool CanEdit, IReadOnlyList<SwShAiFlagsRepairRow> Trainers,
    ProjectGame? DetectedGame, bool CustomAiScripts, string ContextFingerprint,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShAiFlagsRepairResult(SwShAiFlagsRepairWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class SwShAiFlagsRepairService(ProjectWorkspaceService? workspace = null)
{
    public const string InvalidCode = "KM-SWSH-AI-FLAGS-REPAIR-INVALID";
    private readonly ProjectWorkspaceService workspace = workspace ?? new ProjectWorkspaceService();
    public static bool IsCandidate(int flags) => (flags & 0x1040) == 0x40;
    public static int RepairFlags(int flags) => (flags & ~0x40) | 0x1000;
    private static readonly string[] AiScripts = ["btl_ai_honoo_gym_rival.amx", "btl_ai_pokechange.amx"];

    public SwShAiFlagsRepairWorkflow Load(ProjectPaths paths)
    {
        workspace.ClearMemoryCache();
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
            return new(false, [], paths.SelectedGame, false, "", [Error("Fix AI Flags requires Sword or Shield.")]);
        var project = workspace.Open(paths);
        var roster = new SwShTrainersWorkflowService().Load(project);
        var diagnostics = roster.Diagnostics.ToList();
        IReadOnlyList<OutputRepairStamp> stamps = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(paths.OutputRootPath) && new OutputWorkspaceStorage(paths).HasMaterial)
                stamps = OutputTransactionCoordinator.ForProject(paths).GetRepairStampsAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutputCoordinatorException)
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning,
                "Previous AI flag repair records could not be read. Review current flags before staging a repair.",
                Domain: "workflow.trainers", Field: "aiFlags") { Code = InvalidCode });
        }
        var byPath = stamps.Where(stamp => stamp.Kind == PendingEditOwners.SwShAiFlagsRepair)
            .GroupBy(stamp => stamp.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(stamp => stamp.AppliedAtUtc).First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<SwShAiFlagsRepairRow>();
        foreach (var trainer in roster.Trainers)
        {
            var source = SwShTrainersWorkflowService.ResolveWorkflowFile(project, trainer.Provenance.SourceFile);
            if (source is null) continue;
            var bytes = File.ReadAllBytes(source.AbsolutePath);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var flags = (int)(SwShTrainerDataFile.Parse(bytes).Record.AiFlags & 0x1fff);
            // The base comparison is context only. A user's base may itself be a modified dump.
            int? baseFlags = null;
            if (!string.IsNullOrWhiteSpace(paths.BaseRomFsPath) && source.Entry.BaseFile is not null)
            {
                var basePath = Path.Combine(paths.BaseRomFsPath, trainer.Provenance.SourceFile[6..].Replace('/', Path.DirectorySeparatorChar));
                baseFlags = (int)(SwShTrainerDataFile.Parse(File.ReadAllBytes(basePath)).Record.AiFlags & 0x1fff);
            }
            byPath.TryGetValue(trainer.Provenance.SourceFile, out var stamp);
            rows.Add(new(trainer.TrainerId, trainer.Name, trainer.Provenance.SourceFile, flags,
                IsCandidate(flags) ? RepairFlags(flags) : null, hash, IsCandidate(flags), stamp is not null,
                stamp is not null && !string.Equals(stamp.Sha256, hash, StringComparison.OrdinalIgnoreCase),
                stamp?.AppliedAtUtc, baseFlags));
        }
        var context = ReadAiContext(paths);
        return new(project.Health.CanOpenEditableWorkflows && roster.Summary.Availability == SwShWorkflowAvailability.Available
            && !diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), rows, paths.SelectedGame,
            context.Custom, context.Fingerprint, diagnostics);
    }

    public SwShAiFlagsRepairResult Stage(ProjectPaths paths, IReadOnlyList<SwShAiFlagsRepairSelection> selections,
        string contextFingerprint, bool acknowledgeCustomScripts, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var workflow = Load(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        if (!workflow.CanEdit || selections.Count is < 1 or > 512
            || selections.Select(row => row.TrainerId).Distinct().Count() != selections.Count)
            diagnostics.Add(Error("Select between 1 and 512 distinct trainer candidates in an editable Sword or Shield project."));
        if (workflow.ContextFingerprint != contextFingerprint || workflow.CustomAiScripts && !acknowledgeCustomScripts)
            diagnostics.Add(Error("The AI scripts changed or need review. Refresh Fix AI Flags and acknowledge custom AI scripts."));
        var updates = new List<SwShTrainerFieldUpdate>();
        foreach (var selection in selections)
        {
            var row = workflow.Trainers.FirstOrDefault(row => row.TrainerId == selection.TrainerId);
            if (row is null || !row.Candidate || row.Fingerprint != selection.Fingerprint
                || row.PreviouslyFixed && !selection.AcknowledgePreviousFix)
            {
                diagnostics.Add(Error("A selected trainer changed, was previously fixed, or is no longer a candidate. Refresh and review it again."));
                continue;
            }
            if (current.PendingEdits.Any(edit => edit.Domain == "workflow.trainers" && edit.Field == "aiFlags"
                && edit.RecordId == row.TrainerId.ToString(CultureInfo.InvariantCulture)))
            {
                diagnostics.Add(Error("A selected trainer already has a staged AI flag edit. Apply or remove that edit before repairing this trainer."));
                continue;
            }
            updates.Add(new(row.TrainerId, null, "aiFlags", row.ProposedFlags!.Value.ToString(CultureInfo.InvariantCulture)));
        }
        if (diagnostics.Count > 0) return new(workflow, current, diagnostics);
        var result = new SwShTrainersEditSessionService(workspace).UpdateFields(paths, current, updates);
        if (result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            return new(workflow, current, result.Diagnostics);
        var ids = selections.Select(row => row.TrainerId.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var marked = result.Session with
        {
            PendingEdits = result.Session.PendingEdits.Select(edit => edit.Domain == "workflow.trainers"
                && edit.Field == "aiFlags" && ids.Contains(edit.RecordId ?? "")
                ? edit with { Owner = PendingEditOwners.SwShAiFlagsRepair } : edit).ToArray(),
        };
        return new(workflow, marked, result.Diagnostics);
    }

    private static (bool Custom, string Fingerprint) ReadAiContext(ProjectPaths paths)
    {
        var custom = false;
        var tokens = new List<string>();
        foreach (var name in AiScripts)
        {
            var relative = Path.Combine("bin", "battle", "ai_script", name);
            var basePath = string.IsNullOrWhiteSpace(paths.BaseRomFsPath) ? null : Path.Combine(paths.BaseRomFsPath, relative);
            var outputPath = string.IsNullOrWhiteSpace(paths.OutputRootPath) ? null : Path.Combine(paths.OutputRootPath, "romfs", relative);
            static string Hash(string? path)
            {
                if (path is null || !File.Exists(path)) return "missing";
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            var baseHash = Hash(basePath);
            var outputHash = Hash(outputPath);
            custom |= outputHash != "missing" && outputHash != baseHash;
            tokens.Add(name + ":" + baseHash + ":" + outputHash);
        }
        return (custom, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", tokens)))));
    }

    private static ValidationDiagnostic Error(string message) => new(DiagnosticSeverity.Error, message,
        Domain: "workflow.trainers", Field: "aiFlags") { Code = InvalidCode };
}
