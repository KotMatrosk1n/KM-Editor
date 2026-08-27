// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace KM.SwSh.FpsPatch;

public sealed class SwShFpsPatchService
{
    private const string Domain = "tool.60fpsPatch";
    private const string ProjectUnavailableDiagnosticCode = "KM-SWSH-FPS-PROJECT-UNAVAILABLE";
    private const string OutputRootUnavailableDiagnosticCode = "KM-SWSH-FPS-OUTPUT-ROOT-UNAVAILABLE";
    private const string ManifestInvalidDiagnosticCode = "KM-SWSH-FPS-MANIFEST-INVALID";
    private const string MainInputUnavailableDiagnosticCode = "KM-SWSH-FPS-MAIN-INPUT-UNAVAILABLE";
    private const string MainRestoreBlockedDiagnosticCode = "KM-SWSH-FPS-MAIN-RESTORE-BLOCKED";
    private const string ComponentInputUnavailableDiagnosticCode = "KM-SWSH-FPS-COMPONENT-INPUT-UNAVAILABLE";
    private const string ComponentInputReadFailedDiagnosticCode = "KM-SWSH-FPS-COMPONENT-INPUT-READ-FAILED";
    private const string ComponentInputInvalidDiagnosticCode = "KM-SWSH-FPS-COMPONENT-INPUT-INVALID";
    private const string RestorePreflightBlockedDiagnosticCode = "KM-SWSH-FPS-RESTORE-PREFLIGHT-BLOCKED";
    private const string OwnedOutputChangedDiagnosticCode = "KM-SWSH-FPS-OWNED-OUTPUT-CHANGED";
    private const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;
    private const string ManifestRelativePath = ".km-editor/60fps-patch-manifest.json";
    private const string SequenceRootInsideRomFs = "bin/battle/waza/sequence";
    private const string SequenceRootRelativePath = "romfs/bin/battle/waza/sequence";
    private const string BattleCameraRootInsideRomFs = "bin/battle/waza/camera";
    private const string BattleCameraRootRelativePath = "romfs/bin/battle/waza/camera";
    private const string BattleUiRootInsideRomFs = "bin/appli/battle/bin";
    private const string BattleUiRootRelativePath = "romfs/bin/appli/battle/bin";
    private const string DemoSequenceRootInsideRomFs = "bin/demo/sequence";
    private const string DemoSequenceRootRelativePath = "romfs/bin/demo/sequence";
    private const string BattleModelAnimationRootRelativePath = "romfs/bin/battle/waza/model/anm";
    private const string TrainerBallthrowCameraRootInsideRomFs = "bin/battle/waza/camera/ballthrow";
    private const string BattleModelAnimationRootInsideRomFs = "bin/battle/waza/model/anm";
    private const string LegacyTrainerBattleArchiveRootInsideRomFs = "bin/archive/chara/data/tr/anm";
    private const string LegacyCharaTrainerRootInsideRomFs = "bin/chara/data/tr";
    private const string OpeningDemoBseqRelativePath = "romfs/bin/demo/sequence/d010.bseq";
    private const string ExcludedTitleDemoBseqRelativePath = "romfs/bin/demo/sequence/sd9010_title.bseq";
    private const int ExpectedManagedBseqFileCount = 1010;
    private const int MaximumReportedRomFsPaths = 25;

    private static readonly EnumerationOptions RecursiveEnumeration = CreateEnumerationOptions(recursive: true);
    private static readonly EnumerationOptions TopDirectoryEnumeration = CreateEnumerationOptions(recursive: false);

    private static readonly string[] ManagedBseqPrefixes = ["eg", "es", "et", "ew"];
    private static readonly string[] RomFsCategoryOrder =
    [
        "battleSequences",
        "battleCameras",
        "battleInterface",
        "battleModels",
        "openingAndDemos",
        "recoveryAnimation",
        "other",
    ];
    private static readonly IReadOnlySet<string> AllAnimationTimingComponentIds =
        SwShFpsPatchAnimationTimingComponents.All.ToHashSet(StringComparer.Ordinal);
    private static readonly string[] ExcludedBattleCameraDirectories =
    [
        "ballthrow",
        "eg_ball",
        "eg_hokaku",
        "eg_land",
        "hokaku",
    ];
    private static readonly string[] ManagedBattleUiArchiveFileNamePrefixes =
    [
        "battle_ballselect_00",
        "battle_commandSelect_00",
        "battle_commandSelect_01",
        "battle_info_00",
        "battle_kansen_00",
        "battle_opponent_info_00",
        "battle_result_boss_00",
        "battle_skillSelect_00",
        "battle_target_select_00",
        "battle_top_00",
    ];
    private static readonly string[] ExcludedDemoSequenceBseqRelativePaths =
    [
        ExcludedTitleDemoBseqRelativePath,
        "romfs/bin/demo/sequence/sd9110_evolution.bseq",
        "romfs/bin/demo/sequence/sd9111_evolution_after.bseq",
    ];

    private static readonly ManagedBseqTimingOverride[] RequiredManagedBseqFiles =
    [
        new("romfs/bin/battle/waza/sequence/d230.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee004.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee004_g.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee005.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee005_g.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee006.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee006_g.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee101.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee102.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee103.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee104.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee105.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee106.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee107.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee108.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee109.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee110.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee111.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee112.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee113.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee311.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee312.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee315.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee316.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee326.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee327.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee328.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee330.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee331.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee332.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee333.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee340.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee341.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee343.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee344.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee347.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee349.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee350.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee351.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee354.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee400.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee401.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee402.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee403.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee404.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee405.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee406.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee407.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee408.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee409.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee411.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee412.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee420.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee502.bseq", SwShFpsBseqPatcher.MoveEffectTimelineScale),
        new("romfs/bin/battle/waza/sequence/ee630.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
    ];

    private static readonly ManagedBseqTimingOverride[] OptionalManagedBseqScaleOverrides =
    [
        new("romfs/bin/battle/waza/sequence/eg_ball01.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/eg_ball01_crw.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/eg_ball02.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/eg_ball02_crw.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/eg_ball03.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
        new("romfs/bin/battle/waza/sequence/eg_ball03_crw.bseq", SwShFpsBseqPatcher.DynamaxBallTimelineScale),
    ];

    private static readonly string[] RequiredManagedBattleModelAnimationFiles =
    [
        "romfs/bin/battle/waza/model/anm/ee006_kinomi.gfbanm",
        "romfs/bin/battle/waza/model/anm/ew752_kinomi.gfbanm",
    ];

    private static readonly IReadOnlyDictionary<string, double> ManagedBseqTimelineScales = RequiredManagedBseqFiles
        .Concat(OptionalManagedBseqScaleOverrides)
        .ToDictionary(file => file.RelativePath, file => file.Scale, StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShFpsPatchService(ProjectWorkspaceService? projectWorkspaceService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
    }

    public static bool IsManagedRomFsPath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return IsSpecialManagedRomFsPath(normalized)
            || IsManagedMoveEffectBseqPath(normalized)
            || IsManagedBattleCameraPath(normalized)
            || IsManagedBattleUiArchivePath(normalized)
            || IsManagedBattleModelAnimationPath(normalized)
            || IsManagedDemoSequenceBseqPath(normalized)
            || string.Equals(
                normalized,
                SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath,
                StringComparison.OrdinalIgnoreCase);
    }

    public bool IsGeneratedRomFsOutput(ProjectPaths paths, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var normalized = NormalizeRelativePath(relativePath);
        if (!IsManagedRomFsPath(relativePath) || string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return false;
        }

        var sourcePath = ResolveBaseRomFsPath(paths.BaseRomFsPath, normalized);
        var outputPath = ResolveOutputPath(paths.OutputRootPath, normalized);
        if (sourcePath is null || outputPath is null || !File.Exists(sourcePath) || !File.Exists(outputPath))
        {
            return false;
        }

        try
        {
            var generated = ConvertManagedRomFsFile(normalized, File.ReadAllBytes(sourcePath));
            var output = File.ReadAllBytes(outputPath);
            if (output.SequenceEqual(generated))
            {
                return true;
            }

            var manifestHashes = ReadManifestOwnedFileHashes(paths);
            return MatchesManifestOwnedOutput(normalized, output, manifestHashes);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public SwShFpsPatchStatus Load(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);

        var manifest = ReadManifestSnapshot(paths, diagnostics);
        var mainStatus = AnalyzeMain(paths, diagnostics);
        var globalApplyBlocked = !manifest.IsValid
            || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || mainStatus.Kind is SwShFpsPatchMainKind.UnsupportedBuild
                or SwShFpsPatchMainKind.GameMismatch
                or SwShFpsPatchMainKind.Conflict;
        var restorePreflight = PreflightFullRestore(paths, project, manifest);
        var globalRestoreBlocked = restorePreflight.Diagnostics.Any(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var romFsStatus = AnalyzeRomFsOutputs(
            paths,
            manifest.EnabledAnimationTimingComponentIds,
            manifest.OwnedFileHashes,
            diagnostics);

        return CreateStatus(
            mainStatus,
            romFsStatus,
            globalApplyBlocked,
            globalRestoreBlocked,
            restorePreflight.HasRemovableKmState,
            restorePreflight.Diagnostics,
            diagnostics);
    }

    public SwShFpsPatchApplyResult Apply(
        ProjectPaths paths,
        IReadOnlyCollection<string>? enabledAnimationTimingComponentIds = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();
        var enabledComponents = ResolveRequestedAnimationTimingComponents(
            enabledAnimationTimingComponentIds,
            "enabledAnimationTimingComponentIds",
            diagnostics);
        var manifest = ReadManifestSnapshot(paths, diagnostics);
        if (!manifest.IsValid)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch cannot apply while its ownership manifest is invalid.",
                file: ManifestRelativePath,
                expected: "Valid version 1 or 2 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        if (!SwShOutputTransactionWriter.TryCapturePreimage(
                paths,
                ExeFsMainPath,
                out var reviewedMainPreimage,
                out var captureFailure))
        {
            diagnostics.Add(CreateOutputTransactionDiagnostic(
                "60FPS Patch could not review exefs/main before apply",
                captureFailure));
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var preparedMain = PrepareMainApply(paths, diagnostics);
        var preparedRomFsApply = PrepareRomFsApply(
            paths,
            enabledComponents,
            manifest.OwnedFileHashes,
            diagnostics);
        var disabledComponents = AllAnimationTimingComponentIds
            .Except(enabledComponents, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var preparedRomFsDeletes = PrepareRomFsRestore(
            paths,
            disabledComponents,
            manifest,
            diagnostics);
        var preparedLegacyDeletes = PrepareLegacyCleanupDeletes(paths, diagnostics);
        var postTransactionOwnedHashes = BuildPostTransactionOwnedFileHashes(
            paths,
            manifest.OwnedFileHashes,
            preparedRomFsApply.OwnedFileHashes,
            preparedRomFsDeletes,
            enabledComponents,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var manifestMutation = PrepareManifestMutation(
            paths,
            CreateManifest(
                exeFsMainPatched: true,
                postTransactionOwnedHashes,
                enabledComponents),
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var mutations = new List<SwShOutputFileMutation>();
        var committedFiles = new List<ProjectFileReference>();
        if (preparedMain is not null)
        {
            mutations.Add(SwShOutputFileMutation.WriteComposed(
                ExeFsMainPath,
                preparedMain,
                reviewedMainPreimage!));
            committedFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        foreach (var preparedFile in preparedRomFsApply.Files)
        {
            mutations.Add(SwShOutputFileMutation.Write(
                preparedFile.RelativePath,
                preparedFile.Contents,
                preparedFile.ReviewedPreimage));
            committedFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, preparedFile.RelativePath));
        }

        foreach (var preparedDelete in preparedRomFsDeletes.Concat(preparedLegacyDeletes))
        {
            mutations.Add(SwShOutputFileMutation.DeleteLegacyAdoption(
                preparedDelete.RelativePath,
                preparedDelete.ReviewedPreimage));
            committedFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, preparedDelete.RelativePath));
        }

        if (manifestMutation is not null)
        {
            mutations.Add(manifestMutation);
        }

        if (!SwShOutputTransactionWriter.TryApplyFpsPatchBatch(
                paths,
                mutations,
                "tool.sword-shield.60fps-install",
                out var transactionResult,
                out var failure))
        {
            diagnostics.Add(CreateOutputTransactionDiagnostic(
                "60FPS Patch atomic output transaction failed",
                failure));
            return CreateApplyResult(
                paths,
                writtenFiles,
                diagnostics,
                recoveryRequired: transactionResult?.Outcome == OutputApplyOutcome.RecoveryRequired
                    || failure?.RecoveryRequired == true);
        }

        writtenFiles.AddRange(committedFiles);
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            writtenFiles.Count == 0
                ? "60FPS Patch was already installed."
                : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch installed {writtenFiles.Count:N0} output file(s).")));

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    public SwShFpsPatchApplyResult Restore(
        ProjectPaths paths,
        IReadOnlyCollection<string>? animationTimingComponentIds = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();
        var fullRestore = animationTimingComponentIds is null;
        var restoredComponents = ResolveRequestedAnimationTimingComponents(
            animationTimingComponentIds ?? SwShFpsPatchAnimationTimingComponents.All,
            "animationTimingComponentIds",
            diagnostics);

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires a configured Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory") with
            {
                Code = OutputRootUnavailableDiagnosticCode,
            });
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var previousManifest = ReadManifestSnapshot(paths, diagnostics);
        if (!previousManifest.IsValid)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch cannot restore while its ownership manifest is invalid.",
                file: ManifestRelativePath,
                expected: "Valid version 1 or 2 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var preparedRomFsDeletes = PrepareRomFsRestore(
            paths,
            restoredComponents,
            previousManifest,
            diagnostics);
        var preparedMainRestore = fullRestore
            ? PrepareMainRestore(paths, diagnostics)
            : null;
        var preparedLegacyDeletes = fullRestore
            ? PrepareLegacyCleanupDeletes(paths, diagnostics)
            : [];
        var remainingComponents = fullRestore
            ? new HashSet<string>(StringComparer.Ordinal)
            : previousManifest.EnabledAnimationTimingComponentIds
                .Except(restoredComponents, StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
        var postTransactionOwnedHashes = BuildPostTransactionOwnedFileHashes(
            paths,
            previousManifest.OwnedFileHashes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            preparedRomFsDeletes,
            remainingComponents,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var mainPatchedAfterRestore = !fullRestore
            && (previousManifest.ExeFsMainPatched || HasInstalledMainOutput(paths));
        var nextManifest = mainPatchedAfterRestore || postTransactionOwnedHashes.Count > 0
            ? CreateManifest(
                mainPatchedAfterRestore,
                postTransactionOwnedHashes,
                remainingComponents)
            : null;
        var manifestMutation = PrepareManifestMutation(
            paths,
            nextManifest,
            diagnostics);
        if (fullRestore)
        {
            var preparedPaths = preparedRomFsDeletes
                .Concat(preparedLegacyDeletes)
                .Select(prepared => prepared.RelativePath)
                .ToList();
            if (preparedMainRestore is not null)
            {
                preparedPaths.Add(preparedMainRestore.RelativePath);
            }

            if (manifestMutation is not null)
            {
                preparedPaths.Add(manifestMutation.RelativePath);
            }

            ValidatePreparedFullRestoreMutationSet(preparedPaths, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        var mutations = new List<SwShOutputFileMutation>();
        var committedFiles = new List<ProjectFileReference>();
        if (preparedMainRestore is not null)
        {
            mutations.Add(preparedMainRestore);
            committedFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
        }

        foreach (var preparedDelete in preparedRomFsDeletes.Concat(preparedLegacyDeletes))
        {
            mutations.Add(SwShOutputFileMutation.DeleteLegacyAdoption(
                preparedDelete.RelativePath,
                preparedDelete.ReviewedPreimage));
            committedFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, preparedDelete.RelativePath));
        }

        if (manifestMutation is not null)
        {
            mutations.Add(manifestMutation);
        }

        if (!SwShOutputTransactionWriter.TryApplyFpsPatchBatch(
                paths,
                mutations,
                "tool.sword-shield.60fps-restore",
                out var transactionResult,
                out var failure))
        {
            diagnostics.Add(CreateOutputTransactionDiagnostic(
                "60FPS Patch atomic restore transaction failed",
                failure));
            return CreateApplyResult(
                paths,
                writtenFiles,
                diagnostics,
                recoveryRequired: transactionResult?.Outcome == OutputApplyOutcome.RecoveryRequired
                    || failure?.RecoveryRequired == true);
        }

        writtenFiles.AddRange(committedFiles);
        diagnostics.Add(CreateDiagnostic(
            DiagnosticSeverity.Info,
            writtenFiles.Count == 0
                ? "60FPS Patch restore found no matching owned output files to remove."
                : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch restored {writtenFiles.Count:N0} owned output file(s).")));

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    private FullRestorePreflight PreflightFullRestore(
        ProjectPaths paths,
        OpenedProject project,
        FpsPatchManifestSnapshot manifest)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch restore requires a configured Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory") with
            {
                Code = OutputRootUnavailableDiagnosticCode,
            });
        }

        if (!manifest.IsValid)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch cannot restore while its ownership manifest is invalid.",
                file: ManifestRelativePath,
                expected: "Valid version 1 or 2 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new FullRestorePreflight(diagnostics, HasRemovableKmState: false);
        }

        var restoredComponents = AllAnimationTimingComponentIds.ToHashSet(StringComparer.Ordinal);
        var preparedRomFsDeletes = PrepareRomFsRestore(
            paths,
            restoredComponents,
            manifest,
            diagnostics);
        var preparedMainRestore = PrepareMainRestore(paths, diagnostics);
        var preparedLegacyDeletes = PrepareLegacyCleanupDeletes(paths, diagnostics);
        _ = BuildPostTransactionOwnedFileHashes(
            paths,
            manifest.OwnedFileHashes,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            preparedRomFsDeletes,
            new HashSet<string>(StringComparer.Ordinal),
            diagnostics);
        var manifestMutation = PrepareManifestMutation(paths, manifest: null, diagnostics);

        var preparedPaths = preparedRomFsDeletes
            .Concat(preparedLegacyDeletes)
            .Select(prepared => prepared.RelativePath)
            .ToList();
        if (preparedMainRestore is not null)
        {
            preparedPaths.Add(preparedMainRestore.RelativePath);
        }

        if (manifestMutation is not null)
        {
            preparedPaths.Add(manifestMutation.RelativePath);
        }

        ValidatePreparedFullRestoreMutationSet(preparedPaths, diagnostics);
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return new FullRestorePreflight(
            diagnostics,
            HasRemovableKmState: !hasErrors && preparedPaths.Count > 0);
    }

    private static void ValidatePreparedFullRestoreMutationSet(
        IReadOnlyCollection<string> preparedPaths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var duplicatePath = preparedPaths
            .GroupBy(NormalizeRelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicatePath is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch full restore prepared the same output target more than once.",
                file: duplicatePath,
                expected: "One prepared mutation per output target") with
            {
                Code = RestorePreflightBlockedDiagnosticCode,
            });
        }

        if (preparedPaths.Count > SwShOutputTransactionWriter.MaximumFpsPatchFilesPerTransaction)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"60FPS Patch full restore requires {preparedPaths.Count:N0} output mutations, exceeding the verified {SwShOutputTransactionWriter.MaximumFpsPatchFilesPerTransaction:N0}-mutation limit."),
                expected: $"At most {SwShOutputTransactionWriter.MaximumFpsPatchFilesPerTransaction:N0} prepared output mutations") with
            {
                Code = RestorePreflightBlockedDiagnosticCode,
            });
        }
    }

    private byte[]? PrepareMainApply(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base ExeFS and Output Root before it can install.",
                expected: "Readable Base ExeFS and writable Output Root") with
            {
                Code = string.IsNullOrWhiteSpace(paths.BaseExeFsPath)
                    ? MainInputUnavailableDiagnosticCode
                    : OutputRootUnavailableDiagnosticCode,
            });
            return null;
        }

        var basePath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find base exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
            return null;
        }

        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : basePath;

        try
        {
            var current = File.ReadAllBytes(sourcePath);
            var patched = SwShFpsMainPatcher.Apply(current, paths.SelectedGame);
            return patched.SequenceEqual(current) ? null : patched;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with vanilla or KM 60FPS bytes") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
        }

        return null;
    }

    private PreparedRomFsApply PrepareRomFsApply(
        ProjectPaths paths,
        IReadOnlySet<string> enabledAnimationTimingComponentIds,
        IReadOnlyDictionary<string, string> manifestHashes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedFiles = new List<PreparedRomFsFile>();
        var ownedFileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base RomFS and Output Root before it can install.",
                expected: "Readable Base RomFS and writable Output Root") with
            {
                Code = string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
                    ? ComponentInputUnavailableDiagnosticCode
                    : OutputRootUnavailableDiagnosticCode,
            });
            return new PreparedRomFsApply(preparedFiles, ownedFileHashes);
        }

        foreach (var sourceFile in EnumerateManagedRomFsFiles(
                     paths.BaseRomFsPath,
                     diagnostics,
                     enabledAnimationTimingComponentIds))
        {
            PrepareManagedRomFsFile(
                paths,
                sourceFile,
                preparedFiles,
                ownedFileHashes,
                diagnostics,
                manifestHashes);
        }

        return new PreparedRomFsApply(preparedFiles, ownedFileHashes);
    }

    private static void PrepareManagedRomFsFile(
        ProjectPaths paths,
        string relativePath,
        ICollection<PreparedRomFsFile> preparedFiles,
        IDictionary<string, string> ownedFileHashes,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, string> manifestHashes)
    {
        var sourcePath = ResolveBaseRomFsPath(paths.BaseRomFsPath!, relativePath);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find a required Base RomFS file.",
                file: relativePath,
                expected: "Complete Sword/Shield Base RomFS") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return;
        }

        PrepareManagedRomFsFile(
            paths,
            new ManagedRomFsFile(sourcePath, NormalizeRelativePath(relativePath)),
            preparedFiles,
            ownedFileHashes,
            diagnostics,
            manifestHashes);
    }

    private static void PrepareManagedRomFsFile(
        ProjectPaths paths,
        ManagedRomFsFile sourceFile,
        ICollection<PreparedRomFsFile> preparedFiles,
        IDictionary<string, string> ownedFileHashes,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, string> manifestHashes)
    {
        try
        {
            var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
            var generated = ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes);
            var generatedHash = ComputeSha256(generated);
            var targetPath = ResolveOutputPath(paths.OutputRootPath!, sourceFile.RelativePath);
            if (targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "60FPS Patch target must stay inside Output Root.",
                    file: sourceFile.RelativePath,
                    expected: "Output-root-contained RomFS target"));
                return;
            }

            if (File.Exists(targetPath))
            {
                var existing = File.ReadAllBytes(targetPath);
                if (existing.SequenceEqual(generated))
                {
                    ownedFileHashes[sourceFile.RelativePath] = generatedHash;
                    return;
                }

                if (!existing.SequenceEqual(sourceBytes)
                    && !MatchesManifestOwnedOutput(sourceFile.RelativePath, existing, manifestHashes))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Error,
                        "60FPS Patch found an existing non-60FPS ROMFS file and will not overwrite it.",
                        file: sourceFile.RelativePath,
                        expected: "No existing modded ROMFS file, or one already generated by 60FPS Patch"));
                    return;
                }

                preparedFiles.Add(new PreparedRomFsFile(
                    sourceFile.RelativePath,
                    generated,
                    ToOutputFileState(existing)));
                ownedFileHashes[sourceFile.RelativePath] = generatedHash;
                return;
            }

            preparedFiles.Add(new PreparedRomFsFile(
                sourceFile.RelativePath,
                generated,
                OutputFileState.Missing));
            ownedFileHashes[sourceFile.RelativePath] = generatedHash;
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read or stage a ROMFS file: {exception.Message}",
                file: sourceFile.RelativePath,
                expected: "Readable Base RomFS source and writable Output Root target") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read or stage a ROMFS file: {exception.Message}",
                file: sourceFile.RelativePath,
                expected: "Readable Base RomFS source and writable Output Root target") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: sourceFile.RelativePath,
                expected: "Valid Sword/Shield ROMFS file for 60FPS Patch conversion") with
            {
                Code = ComponentInputInvalidDiagnosticCode,
            });
        }
    }

    private SwShOutputFileMutation? PrepareMainRestore(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory") with
            {
                Code = OutputRootUnavailableDiagnosticCode,
            });
            return null;
        }

        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        if (outputMainPath is null || !File.Exists(outputMainPath))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch needs Base ExeFS to restore an installed exefs/main.",
                field: "baseExeFsPath",
                expected: "Readable Base ExeFS folder") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
            return null;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(baseMainPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find Base ExeFS main needed to restore the installed output.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 Base ExeFS main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
            return null;
        }

        try
        {
            var current = File.ReadAllBytes(outputMainPath);
            var baseBytes = File.ReadAllBytes(baseMainPath);
            var restored = SwShFpsMainPatcher.RestoreFromBase(current, baseBytes, paths.SelectedGame);
            if (restored.SequenceEqual(current))
            {
                return null;
            }

            var reviewedPreimage = ToOutputFileState(current);
            return restored.SequenceEqual(baseBytes)
                ? SwShOutputFileMutation.DeleteComposed(
                    ExeFsMainPath,
                    reviewedPreimage,
                    restored)
                : SwShOutputFileMutation.WriteComposed(
                    ExeFsMainPath,
                    restored,
                    reviewedPreimage);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main") with
            {
                Code = MainRestoreBlockedDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main") with
            {
                Code = MainRestoreBlockedDiagnosticCode,
            });
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Output exefs/main containing KM-owned 60FPS bytes") with
            {
                Code = MainRestoreBlockedDiagnosticCode,
            });
        }

        return null;
    }

    private static IReadOnlyList<PreparedRomFsDelete> PrepareRomFsRestore(
        ProjectPaths paths,
        IReadOnlySet<string> animationTimingComponentIds,
        FpsPatchManifestSnapshot manifest,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedDeletes = new List<PreparedRomFsDelete>();
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Static animation timing restore requires Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory") with
            {
                Code = OutputRootUnavailableDiagnosticCode,
            });
            return preparedDeletes;
        }

        var candidates = new Dictionary<string, ManagedRomFsFile?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            foreach (var componentId in animationTimingComponentIds)
            {
                var ignoredInputDiagnostics = new List<ValidationDiagnostic>();
                foreach (var sourceFile in EnumerateManagedRomFsFiles(
                             paths.BaseRomFsPath,
                             ignoredInputDiagnostics,
                             new HashSet<string>(StringComparer.Ordinal) { componentId }))
                {
                    candidates[sourceFile.RelativePath] = sourceFile;
                }
            }
        }

        foreach (var relativePath in manifest.OwnedFileHashes.Keys)
        {
            if (TryGetAnimationTimingComponentId(relativePath, out var componentId)
                && animationTimingComponentIds.Contains(componentId))
            {
                candidates.TryAdd(relativePath, null);
            }
        }

        foreach (var (relativePath, sourceFile) in candidates)
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, relativePath);
            if (targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Static animation timing restore could not contain a managed output path.",
                    file: relativePath,
                    expected: "Output-root-contained managed ROMFS path") with
                {
                    Code = RestorePreflightBlockedDiagnosticCode,
                });
                continue;
            }

            if (!File.Exists(targetPath))
            {
                continue;
            }

            byte[] outputBytes;
            try
            {
                outputBytes = File.ReadAllBytes(targetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Static animation timing restore could not preflight an output: {exception.Message}",
                    file: relativePath,
                    expected: "Readable managed ROMFS output") with
                {
                    Code = RestorePreflightBlockedDiagnosticCode,
                });
                continue;
            }

            if (MatchesManifestOwnedOutput(relativePath, outputBytes, manifest.OwnedFileHashes))
            {
                preparedDeletes.Add(new PreparedRomFsDelete(relativePath, ToOutputFileState(outputBytes)));
                continue;
            }

            if (sourceFile is not null)
            {
                try
                {
                    var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                    var generated = ConvertManagedRomFsFile(relativePath, sourceBytes);
                    if (outputBytes.SequenceEqual(generated))
                    {
                        preparedDeletes.Add(new PreparedRomFsDelete(relativePath, ToOutputFileState(outputBytes)));
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // Exact manifest ownership above remains sufficient when Base RomFS is unavailable or invalid.
                }
            }

            if (manifest.OwnedFileHashes.ContainsKey(relativePath))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    "A recorded KM-owned animation timing output changed and was preserved.",
                    file: relativePath,
                    expected: "Exact manifest-recorded KM-owned ROMFS file") with
                {
                    Code = OwnedOutputChangedDiagnosticCode,
                });
            }
        }

        return preparedDeletes
            .DistinctBy(prepared => prepared.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(prepared => prepared.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PreparedRomFsDelete> PrepareLegacyCleanupDeletes(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedDeletes = new List<PreparedRomFsDelete>();
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return preparedDeletes;
        }

        foreach (var sourceFile in EnumerateLegacyTrainerThrowFiles(paths.BaseRomFsPath, diagnostics))
        {
            PrepareLegacyGeneratedDelete(
                paths,
                sourceFile,
                sourceBytes => SwShFpsLegacyTrainerThrowCleanupPatcher.ConvertLegacyOutput(
                    sourceFile.RelativePath,
                    sourceBytes),
                "legacy trainer throw",
                preparedDeletes,
                diagnostics);
        }

        foreach (var sourceFile in EnumerateLegacyTrainerBallThrowTimingFiles(paths.BaseRomFsPath, diagnostics))
        {
            PrepareLegacyGeneratedDelete(
                paths,
                sourceFile,
                SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed,
                "legacy trainer ball throw",
                preparedDeletes,
                diagnostics);
        }

        foreach (var relativePath in ExcludedDemoSequenceBseqRelativePaths)
        {
            var sourcePath = ResolveBaseRomFsPath(paths.BaseRomFsPath, relativePath);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                continue;
            }

            PrepareLegacyGeneratedDelete(
                paths,
                new ManagedRomFsFile(sourcePath, relativePath),
                sourceBytes => ConvertBseq(sourceBytes, SwShFpsBseqPatcher.OpeningDemoTimelineScale),
                "legacy excluded demo",
                preparedDeletes,
                diagnostics);
        }

        return preparedDeletes
            .DistinctBy(prepared => prepared.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(prepared => prepared.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void PrepareLegacyGeneratedDelete(
        ProjectPaths paths,
        ManagedRomFsFile sourceFile,
        Func<byte[], byte[]> convert,
        string description,
        ICollection<PreparedRomFsDelete> preparedDeletes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var targetPath = ResolveOutputPath(paths.OutputRootPath!, sourceFile.RelativePath);
        if (targetPath is null || !File.Exists(targetPath))
        {
            return;
        }

        try
        {
            var generated = convert(File.ReadAllBytes(sourceFile.SourcePath));
            var output = File.ReadAllBytes(targetPath);
            if (output.SequenceEqual(generated))
            {
                preparedDeletes.Add(new PreparedRomFsDelete(
                    sourceFile.RelativePath,
                    ToOutputFileState(output)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not preflight a {description} output: {exception.Message}",
                file: sourceFile.RelativePath,
                expected: $"Readable {description} source and output") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
        catch (InvalidDataException)
        {
            // If the historical conversion cannot be reproduced exactly, preserve the output.
        }
    }

    private MainStatus AnalyzeMain(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base ExeFS.",
                field: "baseExeFsPath",
                expected: "Readable Base ExeFS folder") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
            return MainStatus.Empty;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        var outputMainPath = string.IsNullOrWhiteSpace(paths.OutputRootPath)
            ? null
            : ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        var sourcePath = outputMainPath is not null && File.Exists(outputMainPath)
            ? outputMainPath
            : baseMainPath;

        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not inspect exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable base or output exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
            return MainStatus.Empty;
        }

        try
        {
            var analysis = SwShFpsMainPatcher.Analyze(File.ReadAllBytes(sourcePath), paths.SelectedGame);
            if (analysis.Kind is SwShFpsPatchMainKind.UnsupportedBuild or SwShFpsPatchMainKind.GameMismatch or SwShFpsPatchMainKind.Conflict)
            {
                diagnostics.Add(CreateDiagnostic(
                    analysis.Kind == SwShFpsPatchMainKind.UnsupportedBuild ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    analysis.Message,
                    file: ExeFsMainPath,
                    expected: "Supported Sword/Shield 1.3.2 exefs/main") with
                {
                    Code = MainInputUnavailableDiagnosticCode,
                });
            }

            return new MainStatus(
                analysis.Kind,
                analysis.BuildId == "unknown" ? null : analysis.BuildId,
                analysis.DetectedGame,
                analysis.PatchedSiteCount,
                analysis.SiteCount);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main") with
            {
                Code = MainInputUnavailableDiagnosticCode,
            });
        }

        return MainStatus.Empty;
    }

    private RomFsStatus AnalyzeRomFsOutputs(
        ProjectPaths paths,
        IReadOnlySet<string> enabledAnimationTimingComponentIds,
        IReadOnlyDictionary<string, string> manifestHashes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var inputDiagnostics = SwShFpsPatchAnimationTimingComponents.All.ToDictionary(
            componentId => componentId,
            _ => new List<ValidationDiagnostic>(),
            StringComparer.Ordinal);
        var sourceFiles = new List<ManagedRomFsFile>();
        foreach (var componentId in SwShFpsPatchAnimationTimingComponents.All)
        {
            var componentDiagnostics = inputDiagnostics[componentId];
            if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
            {
                componentDiagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "This static animation timing component requires Base RomFS.",
                    field: "baseRomFsPath",
                    expected: "Readable Base RomFS folder") with
                {
                    Code = ComponentInputUnavailableDiagnosticCode,
                });
            }
            else
            {
                sourceFiles.AddRange(EnumerateManagedRomFsFiles(
                    paths.BaseRomFsPath,
                    componentDiagnostics,
                    new HashSet<string>(StringComparer.Ordinal) { componentId }));
            }

            if (enabledAnimationTimingComponentIds.Contains(componentId))
            {
                foreach (var diagnostic in componentDiagnostics)
                {
                    diagnostics.Add(diagnostic);
                }
            }
        }

        var distinctSourceFiles = sourceFiles
            .DistinctBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preparedSources = new Dictionary<string, PreparedRomFsSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceFile in distinctSourceFiles)
        {
            var componentId = GetRomFsCategory(sourceFile.RelativePath);
            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                preparedSources[sourceFile.RelativePath] = new PreparedRomFsSource(
                    sourceBytes,
                    ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                var diagnostic = CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"This static animation timing component could not inspect a required Base RomFS file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Readable valid Sword/Shield Base RomFS input") with
                {
                    Code = exception is InvalidDataException
                        ? ComponentInputInvalidDiagnosticCode
                        : ComponentInputReadFailedDiagnosticCode,
                };
                inputDiagnostics[componentId].Add(diagnostic);
                if (enabledAnimationTimingComponentIds.Contains(componentId))
                {
                    diagnostics.Add(diagnostic);
                }
            }
        }

        var inspectedFiles = new List<InspectedRomFsFile>(distinctSourceFiles.Length);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            inspectedFiles.AddRange(distinctSourceFiles.Select(sourceFile => new InspectedRomFsFile(
                sourceFile.RelativePath,
                GetRomFsCategory(sourceFile.RelativePath),
                ManagedRomFsFileState.NotInstalled)));
            return CreateRomFsStatus(
                inspectedFiles,
                enabledAnimationTimingComponentIds,
                inputDiagnostics);
        }

        var reportedConflictDiagnosticCount = 0;
        var omittedConflictDiagnosticCount = 0;

        void AddConflictDiagnostic(string message, string file, string expected)
        {
            if (reportedConflictDiagnosticCount < MaximumReportedRomFsPaths)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    message,
                    file: file,
                    expected: expected));
                reportedConflictDiagnosticCount++;
            }
            else
            {
                omittedConflictDiagnosticCount++;
            }
        }

        foreach (var sourceFile in distinctSourceFiles)
        {
            var componentId = GetRomFsCategory(sourceFile.RelativePath);
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    componentId,
                    ManagedRomFsFileState.Conflict));
                if (enabledAnimationTimingComponentIds.Contains(componentId))
                {
                    AddConflictDiagnostic(
                        "60FPS Patch could not resolve this managed ROMFS path inside Output Root.",
                        sourceFile.RelativePath,
                        "Output-root-contained managed ROMFS path");
                }

                continue;
            }

            if (!File.Exists(targetPath))
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    componentId,
                    ManagedRomFsFileState.NotInstalled));
                continue;
            }

            byte[] outputBytes;
            try
            {
                outputBytes = File.ReadAllBytes(targetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    componentId,
                    ManagedRomFsFileState.Conflict));
                if (enabledAnimationTimingComponentIds.Contains(componentId))
                {
                    AddConflictDiagnostic(
                        $"60FPS Patch could not inspect this managed output: {exception.Message}",
                        sourceFile.RelativePath,
                        "Readable Output Root file");
                }

                continue;
            }

            if (!preparedSources.TryGetValue(sourceFile.RelativePath, out var preparedSource))
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    componentId,
                    MatchesManifestOwnedOutput(sourceFile.RelativePath, outputBytes, manifestHashes)
                        ? ManagedRomFsFileState.StaleOwned
                        : ManagedRomFsFileState.Conflict));
                continue;
            }

            var state = outputBytes.SequenceEqual(preparedSource.GeneratedBytes)
                ? ManagedRomFsFileState.Patched
                : outputBytes.SequenceEqual(preparedSource.SourceBytes)
                    ? ManagedRomFsFileState.NotInstalled
                    : MatchesManifestOwnedOutput(sourceFile.RelativePath, outputBytes, manifestHashes)
                        ? ManagedRomFsFileState.StaleOwned
                        : ManagedRomFsFileState.Conflict;
            inspectedFiles.Add(new InspectedRomFsFile(sourceFile.RelativePath, componentId, state));
            if (state == ManagedRomFsFileState.Conflict
                && enabledAnimationTimingComponentIds.Contains(componentId))
            {
                AddConflictDiagnostic(
                    "60FPS Patch will not overwrite this ROMFS file because it differs from Base RomFS, current KM output, and recorded KM-owned output.",
                    sourceFile.RelativePath,
                    "Vanilla, current KM-generated, or manifest-recorded KM-owned ROMFS file");
            }
        }

        var visitedPaths = inspectedFiles
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, manifestHash) in manifestHashes)
        {
            if (visitedPaths.Contains(relativePath)
                || !TryGetAnimationTimingComponentId(relativePath, out var componentId))
            {
                continue;
            }

            var targetPath = ResolveOutputPath(paths.OutputRootPath, relativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var outputBytes = File.ReadAllBytes(targetPath);
                var state = string.Equals(ComputeSha256(outputBytes), manifestHash, StringComparison.OrdinalIgnoreCase)
                    ? ManagedRomFsFileState.StaleOwned
                    : ManagedRomFsFileState.Conflict;
                inspectedFiles.Add(new InspectedRomFsFile(relativePath, componentId, state));
                if (state == ManagedRomFsFileState.Conflict
                    && enabledAnimationTimingComponentIds.Contains(componentId))
                {
                    AddConflictDiagnostic(
                        "A recorded KM-owned animation timing output changed and will not be overwritten.",
                        relativePath,
                        "Exact manifest-recorded KM-owned ROMFS file");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    relativePath,
                    componentId,
                    ManagedRomFsFileState.Conflict));
                if (enabledAnimationTimingComponentIds.Contains(componentId))
                {
                    AddConflictDiagnostic(
                        $"60FPS Patch could not inspect a recorded owned output: {exception.Message}",
                        relativePath,
                        "Readable manifest-recorded KM-owned ROMFS file");
                }
            }
        }

        if (omittedConflictDiagnosticCount > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"60FPS Patch omitted {omittedConflictDiagnosticCount:N0} additional managed ROMFS conflict diagnostic(s)."),
                expected: "Resolve every reported managed ROMFS conflict before installing"));
        }

        return CreateRomFsStatus(
            inspectedFiles,
            enabledAnimationTimingComponentIds,
            inputDiagnostics);
    }

    private static RomFsStatus CreateRomFsStatus(
        IReadOnlyList<InspectedRomFsFile> files,
        IReadOnlySet<string> enabledAnimationTimingComponentIds,
        IReadOnlyDictionary<string, List<ValidationDiagnostic>> inputDiagnostics)
    {
        var enabledFiles = files
            .Where(file => enabledAnimationTimingComponentIds.Contains(file.Category))
            .ToArray();
        var staleOwnedFiles = enabledFiles
            .Where(file => file.State == ManagedRomFsFileState.StaleOwned)
            .Select(file => file.RelativePath)
            .Take(MaximumReportedRomFsPaths)
            .ToArray();
        var conflictingFiles = enabledFiles
            .Where(file => file.State == ManagedRomFsFileState.Conflict)
            .Select(file => file.RelativePath)
            .Take(MaximumReportedRomFsPaths)
            .ToArray();
        var categories = RomFsCategoryOrder
            .Select(category =>
            {
                var categoryFiles = files.Where(file => file.Category == category).ToArray();
                return new SwShFpsPatchRomFsCategoryStatus(
                    category,
                    categoryFiles.Length,
                    categoryFiles.Count(file => file.State == ManagedRomFsFileState.Patched),
                    categoryFiles.Count(file => file.State == ManagedRomFsFileState.StaleOwned),
                    categoryFiles.Count(file => file.State == ManagedRomFsFileState.Conflict));
            })
            .Where(category => category.ManagedFileCount > 0)
            .ToArray();
        var animationTimingComponents = SwShFpsPatchAnimationTimingComponents.All
            .Select(componentId =>
            {
                var category = categories.FirstOrDefault(category => category.Category == componentId);
                var componentDiagnostics = inputDiagnostics[componentId];
                return new SwShFpsPatchAnimationTimingComponentStatus(
                    componentId,
                    enabledAnimationTimingComponentIds.Contains(componentId),
                    componentDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        ? "blocked"
                        : "ready",
                    componentDiagnostics.ToArray(),
                    category?.ManagedFileCount ?? 0,
                    category?.PatchedFileCount ?? 0,
                    category?.StaleOwnedFileCount ?? 0,
                    category?.ConflictingFileCount ?? 0);
            })
            .ToArray();

        return new RomFsStatus(
            enabledFiles.Length,
            enabledFiles.Count(file => file.State == ManagedRomFsFileState.Patched),
            enabledFiles.Count(file => file.State == ManagedRomFsFileState.StaleOwned),
            enabledFiles.Count(file => file.State == ManagedRomFsFileState.Conflict),
            staleOwnedFiles,
            conflictingFiles,
            categories,
            animationTimingComponents);
    }

    private static string GetRomFsCategory(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.StartsWith(SequenceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "battleSequences";
        }

        if (normalized.StartsWith(BattleCameraRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "battleCameras";
        }

        if (normalized.StartsWith(BattleUiRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "battleInterface";
        }

        if (normalized.StartsWith(BattleModelAnimationRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "battleModels";
        }

        if (normalized.StartsWith(DemoSequenceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return "openingAndDemos";
        }

        if (string.Equals(normalized, SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return "recoveryAnimation";
        }

        return "other";
    }

    private static bool TryGetAnimationTimingComponentId(
        string relativePath,
        out string componentId)
    {
        componentId = string.Empty;
        if (!IsManagedRomFsPath(relativePath))
        {
            return false;
        }

        var category = GetRomFsCategory(relativePath);
        if (!AllAnimationTimingComponentIds.Contains(category))
        {
            return false;
        }

        componentId = category;
        return true;
    }

    private SwShFpsPatchStatus CreateStatus(
        MainStatus mainStatus,
        RomFsStatus romFsStatus,
        bool globalApplyBlocked,
        bool globalRestoreBlocked,
        bool hasRemovableKmState,
        IReadOnlyList<ValidationDiagnostic> restoreDiagnostics,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var hasErrors = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        string status;
        string message;

        var ownedRomFsFileCount = romFsStatus.PatchedFileCount + romFsStatus.StaleOwnedFileCount;
        if (hasErrors)
        {
            status = "blocked";
            message = romFsStatus.ConflictingFileCount > 0
                ? "60FPS Patch found ROMFS output it does not own and will not overwrite."
                : "60FPS Patch has diagnostics that need attention.";
        }
        else if (mainStatus.Kind == SwShFpsPatchMainKind.UnsupportedBuild)
        {
            status = "unsupported";
            message = "60FPS Patch is not available for this exefs/main build.";
        }
        else if (mainStatus.PatchedSiteCount == mainStatus.SiteCount
            && mainStatus.SiteCount > 0
            && romFsStatus.PatchedFileCount == romFsStatus.ManagedFileCount
            && romFsStatus.ConflictingFileCount == 0)
        {
            status = "installed";
            message = "60FPS Patch is installed.";
        }
        else if (mainStatus.PatchedSiteCount == mainStatus.SiteCount
            && mainStatus.SiteCount > 0
            && romFsStatus.ManagedFileCount > 0
            && ownedRomFsFileCount == romFsStatus.ManagedFileCount
            && romFsStatus.StaleOwnedFileCount > 0
            && romFsStatus.ConflictingFileCount == 0)
        {
            status = "updateAvailable";
            message = "60FPS Patch has earlier KM-owned ROMFS output that can be refreshed safely.";
        }
        else if (mainStatus.PatchedSiteCount == 0 && ownedRomFsFileCount == 0)
        {
            status = romFsStatus.ConflictingFileCount == 0 ? "notInstalled" : "blocked";
            message = romFsStatus.ConflictingFileCount == 0
                ? "60FPS Patch is not installed."
                : "60FPS Patch found ROMFS files owned by another mod.";
        }
        else
        {
            status = romFsStatus.ConflictingFileCount == 0 ? "partial" : "blocked";
            message = romFsStatus.StaleOwnedFileCount > 0
                ? "60FPS Patch is partially installed and includes earlier KM-owned ROMFS output."
                : romFsStatus.ConflictingFileCount == 0
                    ? "60FPS Patch is partially installed."
                    : "60FPS Patch is partially installed and has ROMFS conflicts.";
        }

        return new SwShFpsPatchStatus(
            status,
            message,
            globalApplyBlocked,
            globalRestoreBlocked,
            hasRemovableKmState,
            restoreDiagnostics,
            mainStatus.BuildId,
            mainStatus.DetectedGame,
            mainStatus.PatchedSiteCount,
            mainStatus.SiteCount,
            romFsStatus.PatchedFileCount,
            romFsStatus.ManagedFileCount,
            romFsStatus.StaleOwnedFileCount,
            romFsStatus.ConflictingFileCount,
            romFsStatus.StaleOwnedFiles,
            romFsStatus.ConflictingFiles,
            romFsStatus.Categories,
            romFsStatus.AnimationTimingComponents,
            diagnostics);
    }

    private SwShFpsPatchApplyResult CreateApplyResult(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        bool recoveryRequired = false)
    {
        var statusDiagnostics = diagnostics.ToList();
        var status = Load(paths);
        if (statusDiagnostics.Count > 0)
        {
            status = status with { Diagnostics = statusDiagnostics };
        }

        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var applyResult = new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, Array.Empty<PlannedFileWrite>()),
            diagnostics);

        return new SwShFpsPatchApplyResult(status, applyResult, recoveryRequired);
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedRomFsFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlySet<string>? animationTimingComponentIds = null)
    {
        bool Includes(string componentId) => animationTimingComponentIds is null
            || animationTimingComponentIds.Contains(componentId);

        var files = new List<ManagedRomFsFile>();
        if (Includes(SwShFpsPatchAnimationTimingComponents.BattleSequences))
        {
            var errorCountBeforeBseqScan = diagnostics.Count(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var managedBseqFiles = EnumerateManagedBseqFiles(baseRomFsPath, diagnostics);
            var errorCountAfterBseqScan = diagnostics.Count(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (managedBseqFiles.Count != ExpectedManagedBseqFileCount
                && errorCountAfterBseqScan == errorCountBeforeBseqScan)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"60FPS Patch expected {ExpectedManagedBseqFileCount:N0} managed move-effect BSEQ files, but found {managedBseqFiles.Count:N0}."),
                    file: SequenceRootRelativePath,
                    expected: "Complete Sword/Shield Base RomFS move-effect sequence folder") with
                {
                    Code = ComponentInputUnavailableDiagnosticCode,
                });
            }

            files.AddRange(managedBseqFiles);
            foreach (var sourceFile in RequiredManagedBseqFiles)
            {
                AddRequiredManagedRomFsFile(baseRomFsPath, sourceFile.RelativePath, files, diagnostics);
            }
        }

        if (Includes(SwShFpsPatchAnimationTimingComponents.BattleCameras))
        {
            files.AddRange(EnumerateManagedBattleCameraFiles(baseRomFsPath, diagnostics));
        }

        if (Includes(SwShFpsPatchAnimationTimingComponents.BattleInterface))
        {
            files.AddRange(EnumerateManagedBattleUiArchives(baseRomFsPath, diagnostics));
        }

        if (Includes(SwShFpsPatchAnimationTimingComponents.BattleModels))
        {
            foreach (var relativePath in RequiredManagedBattleModelAnimationFiles)
            {
                AddRequiredManagedRomFsFile(baseRomFsPath, relativePath, files, diagnostics);
            }
        }

        if (Includes(SwShFpsPatchAnimationTimingComponents.OpeningAndDemos))
        {
            files.AddRange(EnumerateManagedDemoBseqFiles(baseRomFsPath, diagnostics));
            AddRequiredManagedRomFsFile(
                baseRomFsPath,
                SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath,
                files,
                diagnostics);
        }

        if (Includes(SwShFpsPatchAnimationTimingComponents.RecoveryAnimation))
        {
            AddRequiredManagedRomFsFile(
                baseRomFsPath,
                SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath,
                files,
                diagnostics);
        }

        var includedComponentIds = animationTimingComponentIds ?? AllAnimationTimingComponentIds;
        foreach (var componentId in includedComponentIds)
        {
            if (files.Any(file => string.Equals(
                    GetRomFsCategory(file.RelativePath),
                    componentId,
                    StringComparison.Ordinal)))
            {
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "This static animation timing component found no usable managed Base RomFS files.",
                file: GetAnimationTimingComponentSourcePath(componentId),
                expected: "At least one verified managed Base RomFS file for this component") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedBseqFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sequenceRoot = Path.Combine(baseRomFsPath, SequenceRootInsideRomFs);
        if (!Directory.Exists(sequenceRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find the move-effect sequence folder.",
                file: SequenceRootRelativePath,
                expected: "Sword/Shield Base RomFS move-effect sequence folder") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(sequenceRoot, "*.bseq", SearchOption.TopDirectoryOnly)
                .Where(path => IsManagedBseqFileName(Path.GetFileName(path)))
                .Select(path => new ManagedRomFsFile(
                    Path.GetFullPath(path),
                    $"{SequenceRootRelativePath}/{Path.GetFileName(path).Replace('\\', '/')}"))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan BSEQ files: {exception.Message}",
                file: SequenceRootRelativePath,
                expected: "Readable move-effect sequence folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan BSEQ files: {exception.Message}",
                file: SequenceRootRelativePath,
                expected: "Readable move-effect sequence folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
    }

    private static bool IsManagedBseqFileName(string fileName)
    {
        return fileName.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase)
            && ManagedBseqPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedBattleCameraFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var cameraRoot = Path.Combine(baseRomFsPath, BattleCameraRootInsideRomFs);
        if (!Directory.Exists(cameraRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find the battle camera folder.",
                file: BattleCameraRootRelativePath,
                expected: "Sword/Shield Base RomFS battle camera folder") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(cameraRoot, "*.gfbcama", RecursiveEnumeration)
                .Select(path =>
                {
                    var relativeInsideCameraRoot = Path.GetRelativePath(cameraRoot, path).Replace('\\', '/');
                    return new ManagedRomFsFile(
                        Path.GetFullPath(path),
                        $"{BattleCameraRootRelativePath}/{relativeInsideCameraRoot}");
                })
                .Where(file => IsManagedBattleCameraPath(file.RelativePath))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle camera files: {exception.Message}",
                file: BattleCameraRootRelativePath,
                expected: "Readable battle camera folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle camera files: {exception.Message}",
                file: BattleCameraRootRelativePath,
                expected: "Readable battle camera folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedBattleUiArchives(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var uiRoot = Path.Combine(baseRomFsPath, BattleUiRootInsideRomFs);
        if (!Directory.Exists(uiRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find the battle UI folder.",
                file: BattleUiRootRelativePath,
                expected: "Sword/Shield Base RomFS battle UI folder") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(uiRoot, "*.arc", SearchOption.TopDirectoryOnly)
                .Select(path => new ManagedRomFsFile(
                    Path.GetFullPath(path),
                    $"{BattleUiRootRelativePath}/{Path.GetFileName(path).Replace('\\', '/')}"))
                .Where(file => IsManagedBattleUiArchivePath(file.RelativePath))
                .Where(file => SwShFpsUiKeySelectPatcher.ContainsKeySelectAnimation(File.ReadAllBytes(file.SourcePath)))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle UI files: {exception.Message}",
                file: BattleUiRootRelativePath,
                expected: "Readable battle UI folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle UI files: {exception.Message}",
                file: BattleUiRootRelativePath,
                expected: "Readable battle UI folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedDemoBseqFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sequenceRoot = Path.Combine(baseRomFsPath, DemoSequenceRootInsideRomFs);
        if (!Directory.Exists(sequenceRoot))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find the demo sequence folder.",
                file: DemoSequenceRootRelativePath,
                expected: "Sword/Shield Base RomFS demo sequence folder") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(sequenceRoot, "*.bseq", SearchOption.TopDirectoryOnly)
                .Where(path => !IsExcludedDemoSequenceBseqFileName(Path.GetFileName(path)))
                .Select(path => new ManagedRomFsFile(
                    Path.GetFullPath(path),
                    $"{DemoSequenceRootRelativePath}/{Path.GetFileName(path).Replace('\\', '/')}"))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan demo BSEQ files: {exception.Message}",
                file: DemoSequenceRootRelativePath,
                expected: "Readable demo sequence folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan demo BSEQ files: {exception.Message}",
                file: DemoSequenceRootRelativePath,
                expected: "Readable demo sequence folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
            return [];
        }
    }

    private static void AddRequiredManagedRomFsFile(
        string baseRomFsPath,
        string relativePath,
        ICollection<ManagedRomFsFile> files,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var sourcePath = ResolveBaseRomFsPath(baseRomFsPath, relativePath);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find a required Base RomFS file.",
                file: relativePath,
                expected: "Complete Sword/Shield Base RomFS") with
            {
                Code = ComponentInputUnavailableDiagnosticCode,
            });
            return;
        }

        files.Add(new ManagedRomFsFile(sourcePath, NormalizeRelativePath(relativePath)));
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateLegacyTrainerBallThrowTimingFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var files = new List<ManagedRomFsFile>();
        AddLegacyTrainerBallThrowTimingFiles(
            baseRomFsPath,
            TrainerBallthrowCameraRootInsideRomFs,
            SwShFpsTrainerThrowPatcher.TrainerBallthrowCameraRootRelativePath,
            "*.gfbcama",
            SearchOption.TopDirectoryOnly,
            files,
            diagnostics);
        AddLegacyTrainerBallThrowTimingFiles(
            baseRomFsPath,
            BattleModelAnimationRootInsideRomFs,
            SwShFpsTrainerThrowPatcher.BattleModelAnimationRootRelativePath,
            "*.gfbanm",
            SearchOption.TopDirectoryOnly,
            files,
            diagnostics);

        return files
            .DistinctBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateLegacyTrainerThrowFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var files = new List<ManagedRomFsFile>();
        AddLegacyTrainerThrowFiles(
            baseRomFsPath,
            LegacyCharaTrainerRootInsideRomFs,
            SwShFpsTrainerThrowPatcher.LegacyCharaTrainerRootRelativePath,
            "*.gfbanm",
            SearchOption.AllDirectories,
            files,
            diagnostics);
        AddLegacyTrainerThrowFiles(
            baseRomFsPath,
            LegacyTrainerBattleArchiveRootInsideRomFs,
            SwShFpsTrainerThrowPatcher.LegacyTrainerBattleArchiveRootRelativePath,
            "*_battle*.gfpak",
            SearchOption.TopDirectoryOnly,
            files,
            diagnostics);

        return files
            .DistinctBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddLegacyTrainerThrowFiles(
        string baseRomFsPath,
        string rootInsideRomFs,
        string rootRelativePath,
        string pattern,
        SearchOption searchOption,
        ICollection<ManagedRomFsFile> files,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = Path.Combine(baseRomFsPath, rootInsideRomFs);
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            var enumeration = searchOption == SearchOption.AllDirectories
                ? RecursiveEnumeration
                : TopDirectoryEnumeration;
            foreach (var path in Directory.EnumerateFiles(root, pattern, enumeration))
            {
                var relativePath = $"{rootRelativePath}/{Path.GetRelativePath(root, path).Replace('\\', '/')}";
                var normalized = NormalizeRelativePath(relativePath);
                if (!SwShFpsTrainerThrowPatcher.IsLegacyTrainerCharacterAnimationPath(normalized)
                    && !SwShFpsTrainerThrowPatcher.IsLegacyTrainerBattleArchivePath(normalized))
                {
                    continue;
                }

                files.Add(new ManagedRomFsFile(Path.GetFullPath(path), normalized));
            }
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable legacy trainer animation folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable legacy trainer animation folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
    }

    private static void AddLegacyTrainerBallThrowTimingFiles(
        string baseRomFsPath,
        string rootInsideRomFs,
        string rootRelativePath,
        string pattern,
        SearchOption searchOption,
        ICollection<ManagedRomFsFile> files,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = Path.Combine(baseRomFsPath, rootInsideRomFs);
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            var enumeration = searchOption == SearchOption.AllDirectories
                ? RecursiveEnumeration
                : TopDirectoryEnumeration;
            foreach (var path in Directory.EnumerateFiles(root, pattern, enumeration))
            {
                var relativePath = $"{rootRelativePath}/{Path.GetRelativePath(root, path).Replace('\\', '/')}";
                var normalized = NormalizeRelativePath(relativePath);
                if (!SwShFpsTrainerThrowPatcher.IsLegacyBallThrowTimingPath(normalized))
                {
                    continue;
                }

                files.Add(new ManagedRomFsFile(Path.GetFullPath(path), normalized));
            }
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer ball throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable trainer animation folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer ball throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable trainer animation folder") with
            {
                Code = ComponentInputReadFailedDiagnosticCode,
            });
        }
    }

    private static byte[] ConvertManagedRomFsFile(string relativePath, byte[] sourceBytes)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (ManagedBseqTimelineScales.TryGetValue(normalized, out var scale))
        {
            return ConvertBseq(sourceBytes, scale);
        }

        if (IsManagedMoveEffectBseqPath(normalized))
        {
            return ConvertBseq(sourceBytes, SwShFpsBseqPatcher.MoveEffectTimelineScale);
        }

        if (IsManagedBattleCameraPath(normalized))
        {
            return SwShFpsBattleCameraPatcher.ConvertAnimationToHalfSpeed(sourceBytes);
        }

        if (IsManagedBattleUiArchivePath(normalized))
        {
            return SwShFpsUiKeySelectPatcher.ConvertArchive(sourceBytes);
        }

        if (IsManagedBattleModelAnimationPath(normalized))
        {
            return SwShFpsBattleModelAnimationPatcher.ConvertAnimationToHalfSpeed(sourceBytes);
        }

        if (string.Equals(normalized, OpeningDemoBseqRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return SwShFpsBseqPatcher.ConvertOpeningDemoD010(sourceBytes, out _);
        }

        if (IsManagedDemoSequenceBseqPath(normalized))
        {
            return ConvertBseq(sourceBytes, SwShFpsBseqPatcher.OpeningDemoTimelineScale);
        }

        if (string.Equals(
            normalized,
            SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return SwShFpsDemoAudiencePatcher.ConvertArchive(sourceBytes);
        }

        if (string.Equals(
            normalized,
            SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return SwShFpsPokemonCenterRecoveryPatcher.ConvertArchive(sourceBytes);
        }

        throw new InvalidDataException("60FPS Patch does not manage this ROMFS path.");
    }

    private static byte[] ConvertBseq(byte[] sourceBytes, double scale)
    {
        return SwShFpsBseqPatcher.Convert(
            sourceBytes,
            scale,
            out _);
    }

    private static bool IsSpecialManagedRomFsPath(string normalizedRelativePath)
    {
        return string.Equals(normalizedRelativePath, OpeningDemoBseqRelativePath, StringComparison.OrdinalIgnoreCase)
            || ManagedBseqTimelineScales.ContainsKey(normalizedRelativePath)
            || IsManagedBattleModelAnimationPath(normalizedRelativePath)
            || string.Equals(
                normalizedRelativePath,
                SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManagedMoveEffectBseqPath(string normalizedRelativePath)
    {
        if (!normalizedRelativePath.StartsWith(SequenceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || !normalizedRelativePath.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsManagedBseqFileName(Path.GetFileName(normalizedRelativePath));
    }

    private static bool IsManagedBattleCameraPath(string normalizedRelativePath)
    {
        if (!normalizedRelativePath.StartsWith(BattleCameraRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || !normalizedRelativePath.EndsWith(".gfbcama", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativeInsideCameraRoot = normalizedRelativePath[(BattleCameraRootRelativePath.Length + 1)..];
        var firstSeparatorIndex = relativeInsideCameraRoot.IndexOf('/');
        var cameraDirectory = firstSeparatorIndex < 0
            ? string.Empty
            : relativeInsideCameraRoot[..firstSeparatorIndex];
        return !ExcludedBattleCameraDirectories.Contains(cameraDirectory, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsManagedBattleUiArchivePath(string normalizedRelativePath)
    {
        if (!normalizedRelativePath.StartsWith(BattleUiRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            || !normalizedRelativePath.EndsWith(".arc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalizedRelativePath);
        return ManagedBattleUiArchiveFileNamePrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsManagedBattleModelAnimationPath(string normalizedRelativePath)
    {
        return normalizedRelativePath.StartsWith(BattleModelAnimationRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            && normalizedRelativePath.EndsWith(".gfbanm", StringComparison.OrdinalIgnoreCase)
            && RequiredManagedBattleModelAnimationFiles.Contains(normalizedRelativePath, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsManagedDemoSequenceBseqPath(string normalizedRelativePath)
    {
        return normalizedRelativePath.StartsWith(DemoSequenceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase)
            && !ExcludedDemoSequenceBseqRelativePaths.Contains(normalizedRelativePath, StringComparer.OrdinalIgnoreCase)
            && normalizedRelativePath.EndsWith(".bseq", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedDemoSequenceBseqFileName(string fileName)
    {
        return ExcludedDemoSequenceBseqRelativePaths.Any(
            relativePath => string.Equals(fileName, Path.GetFileName(relativePath), StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateEditableProject(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires valid base paths and a valid Output Root.",
                expected: "Editable project paths") with
            {
                Code = ProjectUnavailableDiagnosticCode,
            });
        }
    }

    private static ValidationDiagnostic CreateOutputTransactionDiagnostic(
        string message,
        SwShOutputTransactionFailure? failure)
    {
        return CreateDiagnostic(
            DiagnosticSeverity.Error,
            $"{message}: {failure?.Message ?? "Unknown output transaction error."}",
            file: string.IsNullOrWhiteSpace(failure?.RelativePath) ? null : failure.RelativePath,
            expected: "Coordinator-owned writable Output Root file") with
        {
            Code = failure?.Code,
        };
    }

    private static OutputFileState ToOutputFileState(ReadOnlySpan<byte> bytes)
    {
        return OutputFileState.Existing(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.Length);
    }

    private static string? ResolveOutputPath(string outputRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(outputRoot);
        var target = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, target);
        return PathContainment.IsOutsideRoot(relative)
            ? null
            : target;
    }

    private static string? ResolveBaseRomFsPath(string baseRomFsRoot, string relativePath)
    {
        if (!relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(
            baseRomFsRoot,
            relativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string ComputeSha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string GetAnimationTimingComponentSourcePath(string componentId)
    {
        return componentId switch
        {
            SwShFpsPatchAnimationTimingComponents.BattleSequences => SequenceRootRelativePath,
            SwShFpsPatchAnimationTimingComponents.BattleCameras => BattleCameraRootRelativePath,
            SwShFpsPatchAnimationTimingComponents.BattleInterface => BattleUiRootRelativePath,
            SwShFpsPatchAnimationTimingComponents.BattleModels => BattleModelAnimationRootRelativePath,
            SwShFpsPatchAnimationTimingComponents.OpeningAndDemos => DemoSequenceRootRelativePath,
            SwShFpsPatchAnimationTimingComponents.RecoveryAnimation =>
                SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath,
            _ => "romfs",
        };
    }

    private static IReadOnlySet<string> ResolveRequestedAnimationTimingComponents(
        IReadOnlyCollection<string>? componentIds,
        string field,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (componentIds is null)
        {
            return AllAnimationTimingComponentIds.ToHashSet(StringComparer.Ordinal);
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var componentId in componentIds)
        {
            if (string.IsNullOrWhiteSpace(componentId)
                || !AllAnimationTimingComponentIds.Contains(componentId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "60FPS Patch received an unknown static animation timing component.",
                    field: field,
                    expected: string.Join(", ", SwShFpsPatchAnimationTimingComponents.All)));
                continue;
            }

            if (!resolved.Add(componentId))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"60FPS Patch received duplicate component id '{componentId}'.",
                    field: field,
                    expected: "Each static animation timing component listed at most once"));
            }
        }

        return resolved;
    }

    private static IReadOnlyDictionary<string, string> ReadManifestOwnedFileHashes(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        return ReadManifestSnapshot(paths, diagnostics).OwnedFileHashes;
    }

    private static FpsPatchManifestSnapshot ReadManifestSnapshot(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return FpsPatchManifestSnapshot.Missing;
        }

        var manifestPath = ResolveOutputPath(paths.OutputRootPath, ManifestRelativePath);
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return FpsPatchManifestSnapshot.Missing;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<FpsPatchManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions);
            if (manifest is null
                || manifest.Version is not (1 or 2)
                || manifest.RomFsFiles is null)
            {
                throw new InvalidDataException("60FPS Patch manifest has an unsupported layout.");
            }

            IReadOnlySet<string> enabledComponents;
            if (manifest.Version == 1)
            {
                enabledComponents = AllAnimationTimingComponentIds.ToHashSet(StringComparer.Ordinal);
            }
            else
            {
                if (manifest.EnabledAnimationTimingComponentIds is null)
                {
                    throw new InvalidDataException("60FPS Patch manifest is missing its animation timing selection.");
                }

                var resolved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var componentId in manifest.EnabledAnimationTimingComponentIds)
                {
                    if (string.IsNullOrWhiteSpace(componentId)
                        || !AllAnimationTimingComponentIds.Contains(componentId)
                        || !resolved.Add(componentId))
                    {
                        throw new InvalidDataException("60FPS Patch manifest contains an invalid animation timing selection.");
                    }
                }

                enabledComponents = resolved;
            }

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.RomFsFiles)
            {
                var relativePath = NormalizeRelativePath(file.RelativePath ?? string.Empty);
                var hash = file.Sha256?.Trim().ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relativePath)
                    || Path.IsPathRooted(relativePath)
                    || relativePath.Split('/').Contains("..", StringComparer.Ordinal)
                    || hash.Length != 64
                    || !hash.All(Uri.IsHexDigit))
                {
                    continue;
                }

                hashes[relativePath] = hash;
            }

            return new FpsPatchManifestSnapshot(
                hashes,
                enabledComponents,
                manifest.ExeFsMainPatched,
                IsValid: true);
        }
        catch (IOException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be read: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Readable 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be read: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Readable 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }
        catch (JsonException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be parsed: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Valid 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }
        catch (InvalidDataException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                exception.Message,
                file: ManifestRelativePath,
                expected: "Version 1 or 2 60FPS Patch ownership manifest") with
            {
                Code = ManifestInvalidDiagnosticCode,
            });
        }

        return FpsPatchManifestSnapshot.Invalid;
    }

    private static bool MatchesManifestOwnedOutput(
        string relativePath,
        byte[] output,
        IReadOnlyDictionary<string, string> manifestHashes)
    {
        return manifestHashes.TryGetValue(NormalizeRelativePath(relativePath), out var expectedHash)
            && string.Equals(ComputeSha256(output), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildPostTransactionOwnedFileHashes(
        ProjectPaths paths,
        IReadOnlyDictionary<string, string> previousHashes,
        IReadOnlyDictionary<string, string> selectedComponentHashes,
        IReadOnlyList<PreparedRomFsDelete> preparedDeletes,
        IReadOnlySet<string> retainedAnimationTimingComponentIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var ownedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, previousHash) in previousHashes)
        {
            if (!TryGetAnimationTimingComponentId(relativePath, out var componentId)
                || !retainedAnimationTimingComponentIds.Contains(componentId))
            {
                continue;
            }

            var targetPath = ResolveOutputPath(paths.OutputRootPath!, relativePath);
            if (targetPath is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "60FPS Patch could not contain a recorded ownership path inside Output Root.",
                    file: relativePath,
                    expected: "Output-root-contained manifest-recorded ROMFS path"));
                continue;
            }

            if (!File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var currentHash = ComputeSha256(File.ReadAllBytes(targetPath));
                ownedHashes[relativePath] = previousHash;
                if (!string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "A recorded KM-owned animation timing output changed and remains recorded but was preserved.",
                        file: relativePath,
                        expected: "Exact manifest-recorded KM-owned ROMFS file"));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"60FPS Patch could not preflight recorded ROMFS ownership: {exception.Message}",
                    file: relativePath,
                    expected: "Readable manifest-recorded KM-owned ROMFS output"));
            }
        }

        foreach (var (relativePath, hash) in selectedComponentHashes)
        {
            ownedHashes[relativePath] = hash;
        }

        foreach (var preparedDelete in preparedDeletes)
        {
            ownedHashes.Remove(preparedDelete.RelativePath);
        }

        return ownedHashes;
    }

    private static FpsPatchManifest CreateManifest(
        bool exeFsMainPatched,
        IReadOnlyDictionary<string, string> ownedFileHashes,
        IReadOnlySet<string> enabledAnimationTimingComponentIds)
    {
        return new FpsPatchManifest(
            Version: 2,
            CreatedAt: DateTimeOffset.UtcNow,
            ExeFsMainPatched: exeFsMainPatched,
            RomFsFiles: ownedFileHashes
                .Select(file => new FpsPatchManifestFile(file.Key, file.Value))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            EnabledAnimationTimingComponentIds: SwShFpsPatchAnimationTimingComponents.All
                .Where(enabledAnimationTimingComponentIds.Contains)
                .ToArray());
    }

    private static SwShOutputFileMutation? PrepareManifestMutation(
        ProjectPaths paths,
        FpsPatchManifest? manifest,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShOutputTransactionWriter.TryCapturePreimage(
                paths,
                ManifestRelativePath,
                out var reviewedPreimage,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch manifest could not be preflighted: {captureFailure?.Message ?? "Unknown output transaction error."}",
                file: ManifestRelativePath,
                expected: "Exact reviewed 60FPS Patch manifest preimage") with
            {
                Code = captureFailure?.Code ?? RestorePreflightBlockedDiagnosticCode,
            });
            return null;
        }

        if (manifest is null)
        {
            return reviewedPreimage is { Exists: true }
                ? SwShOutputFileMutation.DeleteLegacyAdoption(
                    ManifestRelativePath,
                    reviewedPreimage)
                : null;
        }

        return SwShOutputFileMutation.Write(
            ManifestRelativePath,
            JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions),
            reviewedPreimage!);
    }

    private static bool HasInstalledMainOutput(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return false;
        }

        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        if (outputMainPath is null || !File.Exists(outputMainPath))
        {
            return false;
        }

        try
        {
            return SwShFpsMainPatcher.Analyze(File.ReadAllBytes(outputMainPath), paths.SelectedGame).PatchedSiteCount > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static EnumerationOptions CreateEnumerationOptions(bool recursive)
    {
        return new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            RecurseSubdirectories = recursive,
            ReturnSpecialDirectories = false,
        };
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: Domain,
            Field: field,
            Expected: expected);
    }

    private sealed record ManagedRomFsFile(
        string SourcePath,
        string RelativePath);

    private sealed record ManagedBseqTimingOverride(
        string RelativePath,
        double Scale);

    private sealed record PreparedRomFsFile(
        string RelativePath,
        byte[] Contents,
        OutputFileState ReviewedPreimage);

    private sealed record PreparedRomFsApply(
        IReadOnlyList<PreparedRomFsFile> Files,
        IReadOnlyDictionary<string, string> OwnedFileHashes);

    private sealed record PreparedRomFsDelete(
        string RelativePath,
        OutputFileState ReviewedPreimage);

    private sealed record PreparedRomFsSource(
        byte[] SourceBytes,
        byte[] GeneratedBytes);

    private enum ManagedRomFsFileState
    {
        NotInstalled,
        Patched,
        StaleOwned,
        Conflict,
    }

    private sealed record InspectedRomFsFile(
        string RelativePath,
        string Category,
        ManagedRomFsFileState State);

    private sealed record MainStatus(
        SwShFpsPatchMainKind Kind,
        string? BuildId,
        ProjectGame? DetectedGame,
        int PatchedSiteCount,
        int SiteCount)
    {
        public static MainStatus Empty { get; } = new(SwShFpsPatchMainKind.Conflict, null, null, 0, 0);
    }

    private sealed record RomFsStatus(
        int ManagedFileCount,
        int PatchedFileCount,
        int StaleOwnedFileCount,
        int ConflictingFileCount,
        IReadOnlyList<string> StaleOwnedFiles,
        IReadOnlyList<string> ConflictingFiles,
        IReadOnlyList<SwShFpsPatchRomFsCategoryStatus> Categories,
        IReadOnlyList<SwShFpsPatchAnimationTimingComponentStatus> AnimationTimingComponents)
    {
        public static RomFsStatus CreateEmpty(IReadOnlySet<string> enabledAnimationTimingComponentIds)
        {
            return new RomFsStatus(
                0,
                0,
                0,
                0,
                [],
                [],
                [],
                SwShFpsPatchAnimationTimingComponents.All
                    .Select(componentId => new SwShFpsPatchAnimationTimingComponentStatus(
                        componentId,
                        enabledAnimationTimingComponentIds.Contains(componentId),
                        "blocked",
                        [],
                        0,
                        0,
                        0,
                        0))
                    .ToArray());
        }
    }

    private sealed record FpsPatchManifest(
        int Version,
        DateTimeOffset CreatedAt,
        bool ExeFsMainPatched,
        IReadOnlyList<FpsPatchManifestFile> RomFsFiles,
        IReadOnlyList<string>? EnabledAnimationTimingComponentIds = null);

    private sealed record FpsPatchManifestFile(
        string RelativePath,
        string Sha256);

    private sealed record FullRestorePreflight(
        IReadOnlyList<ValidationDiagnostic> Diagnostics,
        bool HasRemovableKmState);

    private sealed record FpsPatchManifestSnapshot(
        IReadOnlyDictionary<string, string> OwnedFileHashes,
        IReadOnlySet<string> EnabledAnimationTimingComponentIds,
        bool ExeFsMainPatched,
        bool IsValid)
    {
        public static FpsPatchManifestSnapshot Missing { get; } = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AllAnimationTimingComponentIds.ToHashSet(StringComparer.Ordinal),
            ExeFsMainPatched: false,
            IsValid: true);

        public static FpsPatchManifestSnapshot Invalid { get; } = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            ExeFsMainPatched: false,
            IsValid: false);
    }
}
