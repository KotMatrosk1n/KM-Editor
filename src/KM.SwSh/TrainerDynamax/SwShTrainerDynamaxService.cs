// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Trainers;

namespace KM.SwSh.TrainerDynamax;

public sealed record SwShTrainerDynamaxStatus(bool CanEdit, SwShTrainerDynamaxSettings Settings,
    bool Partial, string? BuildId, string SourceLayer, IReadOnlyList<ValidationDiagnostic> Diagnostics, IReadOnlyList<SwShTrainerDynamaxTrainer>? Trainers = null);
public sealed record SwShTrainerDynamaxReview(string? ReviewToken, SwShTrainerDynamaxSettings Settings,
    string OutputAction, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShTrainerDynamaxApplyResult(SwShTrainerDynamaxStatus Status, ApplyResult ApplyResult);

public sealed class SwShTrainerDynamaxService(ProjectWorkspaceService? workspace = null)
{
    private const string MainPath = "exefs/main";
    public const string InvalidCode = "KM-SWSH-TRAINER-DYNAMAX-INVALID";
    public const string StaleCode = "KM-SWSH-TRAINER-DYNAMAX-REVIEW-STALE";
    private readonly ProjectWorkspaceService workspace = workspace ?? new();

    public SwShTrainerDynamaxStatus Load(ProjectPaths paths)
    {
        try
        {
            var snapshot = Read(paths);
            var state = SwShTrainerDynamaxMainPatcher.Inspect(snapshot.Source, paths.SelectedGame);
            var roster = new SwShTrainersWorkflowService().Load(workspace.Open(paths));
            var trainers = roster.Trainers.Where(row => row.TrainerId is >= 1 and <= 436)
                .Select(row => new SwShTrainerDynamaxTrainer(row.TrainerId, row.Name, SwShTrainerDynamaxVanilla.Player(row.TrainerId), SwShTrainerDynamaxVanilla.Opponent(row.TrainerId))).ToArray();
            return new(true, new((state.DisabledSides & 1) != 0, (state.DisabledSides & 2) != 0, state.Trainers, (state.DisabledSides & 4) != 0, (state.DisabledSides & 8) != 0),
                state.Partial, state.BuildId, snapshot.Preimage.Exists ? "layered" : "base", roster.Diagnostics, trainers);
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            return new(false, new(false, false), false, null, "unknown", [Failure(exception)]);
        }
    }

    public SwShTrainerDynamaxReview Review(ProjectPaths paths, SwShTrainerDynamaxSettings settings)
    {
        try
        {
            var prepared = Prepare(paths, settings);
            return new(prepared.Token, settings, prepared.Action, []);
        }
        catch (Exception exception) when (IsInputFailure(exception))
        {
            return new(null, settings, "none", [Failure(exception)]);
        }
    }

    public SwShTrainerDynamaxApplyResult Apply(ProjectPaths paths, SwShTrainerDynamaxSettings settings, string reviewToken)
    {
        List<ValidationDiagnostic> diagnostics = [];
        List<ProjectFileReference> written = [];
        OutputApplyResult? transaction = null;
        try
        {
            var prepared = Prepare(paths, settings);
            if (!string.Equals(prepared.Token, reviewToken, StringComparison.Ordinal))
            {
                diagnostics.Add(new ValidationDiagnostic(DiagnosticSeverity.Error,
                    "Trainer Dynamax sources or settings changed. Review again before applying.",
                    Domain: "tool.trainerDynamax") { Code = StaleCode });
            }
            else if (prepared.Action != "none")
            {
                var mutation = prepared.Action == "delete"
                    ? SwShOutputFileMutation.DeleteComposed(MainPath, prepared.Snapshot.Preimage, prepared.Bytes)
                    : SwShOutputFileMutation.WriteComposed(MainPath, prepared.Bytes, prepared.Snapshot.Preimage);
                if (SwShOutputTransactionWriter.TryApply(paths, [mutation], "tool.sword-shield.trainer-dynamax",
                    out transaction, out var failure))
                    written.Add(new(ProjectFileLayer.Layered, MainPath));
                else
                    diagnostics.Add(new ValidationDiagnostic(DiagnosticSeverity.Error, failure?.Message ?? "Trainer Dynamax could not apply output.",
                        File: MainPath, Domain: "tool.trainerDynamax") { Code = failure?.Code ?? InvalidCode });
            }
        }
        catch (Exception exception) when (IsInputFailure(exception)) { diagnostics.Add(Failure(exception)); }
        var id = Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow;
        return new(Load(paths), new(id, at, written, new WriteManifest(id, at, []), diagnostics, transaction));
    }

    private Prepared Prepare(ProjectPaths paths, SwShTrainerDynamaxSettings settings)
    {
        var snapshot = Read(paths);
        settings = settings.Canonical();
        var bytes = SwShTrainerDynamaxMainPatcher.ApplySettings(snapshot.Vanilla, snapshot.Source, paths.SelectedGame, settings);
        var unchanged = SwShTrainerDynamaxMainPatcher.Equivalent(bytes, snapshot.Source);
        var action = unchanged ? "none" : settings.Mask == 0 && SwShTrainerDynamaxMainPatcher.Equivalent(bytes, snapshot.Vanilla)
            ? "delete" : snapshot.Preimage.Exists ? "write" : "create";
        // Review binds both current file existence and contents, the vanilla source,
        // destination configuration and the exact settings the user reviewed.
        var token = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Paths = paths, Settings = settings, Vanilla = Hash(snapshot.Vanilla),
            Source = Hash(snapshot.Source), snapshot.Preimage, Action = action,
        })));
        return new(snapshot, bytes, action, token);
    }

    private Snapshot Read(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
            throw new InvalidDataException("Trainer Dynamax is available for Sword and Shield only.");
        var project = workspace.Open(paths);
        if (!project.Health.CanOpenEditableWorkflows || string.IsNullOrWhiteSpace(paths.BaseExeFsPath)
            || string.IsNullOrWhiteSpace(paths.OutputRootPath))
            throw new InvalidDataException("Trainer Dynamax requires valid vanilla sources and a configured Output Root.");
        if (!SwShOutputTransactionWriter.TryCapturePreimage(paths, MainPath, out var preimage, out _))
            throw new InvalidDataException("Trainer Dynamax could not read its output target. Check exefs/main and refresh.");
        var vanilla = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath, "main"));
        var vanillaState = SwShTrainerDynamaxMainPatcher.Inspect(vanilla, paths.SelectedGame);
        if (vanillaState.Installed)
            throw new InvalidDataException("Trainer Dynamax requires vanilla Base ExeFS.");
        var source = preimage!.Exists
            ? File.ReadAllBytes(SwShExeFsPatchWorkflowService.ResolveOutputPath(paths, MainPath)!) : vanilla;
        if (preimage.Exists && (source.LongLength != preimage.LengthBytes || !string.Equals(Hash(source), preimage.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Trainer Dynamax output changed while being read. Refresh and review again.");
        _ = SwShTrainerDynamaxMainPatcher.Inspect(source, paths.SelectedGame);
        return new(vanilla, source, preimage);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsInputFailure(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException;
    private static ValidationDiagnostic Failure(Exception exception) => new(DiagnosticSeverity.Error,
        exception is InvalidDataException ? exception.Message : "Trainer Dynamax could not read valid source and output files. Check the paths and refresh.",
        File: MainPath, Domain: "tool.trainerDynamax") { Code = InvalidCode };
    private sealed record Snapshot(byte[] Vanilla, byte[] Source, OutputFileState Preimage);
    private sealed record Prepared(Snapshot Snapshot, byte[] Bytes, string Action, string Token);
}
