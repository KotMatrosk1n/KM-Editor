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

        var mainStatus = AnalyzeMain(paths, diagnostics);
        var romFsStatus = AnalyzeRomFsOutputs(paths, diagnostics);

        return CreateStatus(mainStatus, romFsStatus, diagnostics);
    }

    public SwShFpsPatchApplyResult Apply(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();

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
        var preparedRomFsFiles = PrepareRomFsApply(paths, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        if (preparedMain is not null)
        {
            WriteOutputFile(
                paths,
                ExeFsMainPath,
                preparedMain,
                reviewedMainPreimage!,
                diagnostics,
                writtenFiles);
        }

        foreach (var preparedFile in preparedRomFsFiles)
        {
            WriteOutputFile(
                paths,
                preparedFile.RelativePath,
                preparedFile.Contents,
                preparedFile.ReviewedPreimage,
                diagnostics,
                writtenFiles);
        }

        RemoveLegacyTrainerThrowOutputs(paths, diagnostics, writtenFiles);
        RemoveLegacyExcludedDemoSequenceOutputs(paths, diagnostics, writtenFiles);

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            RefreshManifestSnapshot(paths, diagnostics);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "60FPS Patch was already installed."
                    : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch installed {writtenFiles.Count:N0} output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    public SwShFpsPatchApplyResult Restore(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);
        var diagnostics = new List<ValidationDiagnostic>();
        ValidateEditableProject(project, diagnostics);
        var writtenFiles = new List<ProjectFileReference>();

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires a configured Output Root.",
                field: "outputRootPath",
                expected: "Writable LayeredFS output directory"));
            return CreateApplyResult(paths, writtenFiles, diagnostics);
        }

        RestoreMain(paths, diagnostics, writtenFiles);
        RestoreRomFsFiles(paths, diagnostics, writtenFiles);
        RemoveLegacyTrainerThrowOutputs(paths, diagnostics, writtenFiles);
        RemoveLegacyExcludedDemoSequenceOutputs(paths, diagnostics, writtenFiles);
        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            RefreshManifestSnapshot(paths, diagnostics);
        }

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                writtenFiles.Count == 0
                    ? "60FPS Patch uninstall found no owned output files to remove."
                    : string.Create(CultureInfo.InvariantCulture, $"60FPS Patch uninstalled {writtenFiles.Count:N0} owned output file(s).")));
        }

        return CreateApplyResult(paths, writtenFiles, diagnostics);
    }

    private byte[]? PrepareMainApply(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base ExeFS and Output Root before it can install.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return null;
        }

        var basePath = Path.Combine(paths.BaseExeFsPath, "main");
        if (!File.Exists(basePath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch could not find base exefs/main.",
                file: ExeFsMainPath,
                expected: "Readable Sword/Shield 1.3.2 exefs/main"));
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
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Supported Sword/Shield 1.3.2 exefs/main with vanilla or KM 60FPS bytes"));
        }

        return null;
    }

    private IReadOnlyList<PreparedRomFsFile> PrepareRomFsApply(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preparedFiles = new List<PreparedRomFsFile>();
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base RomFS and Output Root before it can install.",
                expected: "Readable Base RomFS and writable Output Root"));
            return preparedFiles;
        }

        var manifestHashes = ReadManifestOwnedFileHashes(paths, diagnostics);
        var moveEffectFiles = EnumerateManagedBseqFiles(paths.BaseRomFsPath, diagnostics);
        if (moveEffectFiles.Count != ExpectedManagedBseqFileCount)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"60FPS Patch expected {ExpectedManagedBseqFileCount:N0} managed move-effect BSEQ files, but found {moveEffectFiles.Count:N0}."),
                file: SequenceRootRelativePath,
                expected: "Complete Sword/Shield Base RomFS move-effect sequence folder"));
            return preparedFiles;
        }

        foreach (var sourceFile in moveEffectFiles)
        {
            PrepareManagedRomFsFile(paths, sourceFile, preparedFiles, diagnostics, manifestHashes);
        }

        foreach (var sourceFile in EnumerateManagedBattleCameraFiles(paths.BaseRomFsPath, diagnostics))
        {
            PrepareManagedRomFsFile(paths, sourceFile, preparedFiles, diagnostics, manifestHashes);
        }

        foreach (var sourceFile in EnumerateManagedBattleUiArchives(paths.BaseRomFsPath, diagnostics))
        {
            PrepareManagedRomFsFile(paths, sourceFile, preparedFiles, diagnostics, manifestHashes);
        }

        foreach (var sourceFile in EnumerateManagedDemoBseqFiles(paths.BaseRomFsPath, diagnostics))
        {
            PrepareManagedRomFsFile(paths, sourceFile, preparedFiles, diagnostics, manifestHashes);
        }

        foreach (var sourceFile in RequiredManagedBseqFiles)
        {
            PrepareManagedRomFsFile(paths, sourceFile.RelativePath, preparedFiles, diagnostics, manifestHashes);
        }

        foreach (var relativePath in RequiredManagedBattleModelAnimationFiles)
        {
            PrepareManagedRomFsFile(paths, relativePath, preparedFiles, diagnostics, manifestHashes);
        }

        PrepareManagedRomFsFile(paths, SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath, preparedFiles, diagnostics, manifestHashes);
        PrepareManagedRomFsFile(paths, SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath, preparedFiles, diagnostics, manifestHashes);

        return preparedFiles;
    }

    private static void PrepareManagedRomFsFile(
        ProjectPaths paths,
        string relativePath,
        ICollection<PreparedRomFsFile> preparedFiles,
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
                expected: "Complete Sword/Shield Base RomFS"));
            return;
        }

        PrepareManagedRomFsFile(
            paths,
            new ManagedRomFsFile(sourcePath, NormalizeRelativePath(relativePath)),
            preparedFiles,
            diagnostics,
            manifestHashes);
    }

    private static void PrepareManagedRomFsFile(
        ProjectPaths paths,
        ManagedRomFsFile sourceFile,
        ICollection<PreparedRomFsFile> preparedFiles,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, string> manifestHashes)
    {
        try
        {
            var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
            var generated = ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes);
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
                return;
            }

            preparedFiles.Add(new PreparedRomFsFile(
                sourceFile.RelativePath,
                generated,
                OutputFileState.Missing));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read or stage a ROMFS file: {exception.Message}",
                file: sourceFile.RelativePath,
                expected: "Readable Base RomFS source and writable Output Root target"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not read or stage a ROMFS file: {exception.Message}",
                file: sourceFile.RelativePath,
                expected: "Readable Base RomFS source and writable Output Root target"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: sourceFile.RelativePath,
                expected: "Valid Sword/Shield ROMFS file for 60FPS Patch conversion"));
        }
    }

    private void RestoreMain(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires Base ExeFS and Output Root.",
                expected: "Readable Base ExeFS and writable Output Root"));
            return;
        }

        var baseMainPath = Path.Combine(paths.BaseExeFsPath, "main");
        var outputMainPath = ResolveOutputPath(paths.OutputRootPath, ExeFsMainPath);
        if (outputMainPath is null || !File.Exists(outputMainPath))
        {
            return;
        }

        try
        {
            var current = File.ReadAllBytes(outputMainPath);
            var baseBytes = File.ReadAllBytes(baseMainPath);
            var restored = SwShFpsMainPatcher.RestoreFromBase(current, baseBytes, paths.SelectedGame);
            if (restored.SequenceEqual(current))
            {
                return;
            }

            var reviewedPreimage = ToOutputFileState(current);
            var mutation = restored.SequenceEqual(baseBytes)
                ? SwShOutputFileMutation.DeleteComposed(
                    ExeFsMainPath,
                    reviewedPreimage,
                    restored)
                : SwShOutputFileMutation.WriteComposed(
                    ExeFsMainPath,
                    restored,
                    reviewedPreimage);
            if (TryApplyOutputMutation(
                    paths,
                    mutation,
                    "tool.sword-shield.60fps-uninstall-main",
                    diagnostics))
            {
                writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, ExeFsMainPath));
            }
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not restore exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable base and output exefs/main"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                file: ExeFsMainPath,
                expected: "Output exefs/main containing KM-owned 60FPS bytes"));
        }
    }

    private void RestoreRomFsFiles(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch uninstall requires Base RomFS and Output Root.",
                expected: "Readable Base RomFS and writable Output Root"));
            return;
        }

        var manifestHashes = ReadManifestOwnedFileHashes(paths, diagnostics);
        foreach (var sourceFile in EnumerateManagedRomFsFiles(paths.BaseRomFsPath, diagnostics))
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (!outputBytes.SequenceEqual(generated)
                    && !MatchesManifestOwnedOutput(sourceFile.RelativePath, outputBytes, manifestHashes))
                {
                    diagnostics.Add(CreateDiagnostic(
                        DiagnosticSeverity.Warning,
                        "60FPS Patch left this ROMFS file in place because it no longer matches KM-owned 60FPS output.",
                        file: sourceFile.RelativePath,
                        expected: "Unmodified 60FPS Patch generated file"));
                    continue;
                }

                if (TryApplyOutputMutation(
                        paths,
                        SwShOutputFileMutation.DeleteLegacyAdoption(
                            sourceFile.RelativePath,
                            ToOutputFileState(outputBytes)),
                        "tool.sword-shield.60fps-uninstall-romfs",
                        diagnostics))
                {
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, sourceFile.RelativePath));
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a generated ROMFS file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable generated 60FPS Patch file"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a generated ROMFS file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable generated 60FPS Patch file"));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    exception.Message,
                    file: sourceFile.RelativePath,
                    expected: "Valid SwSh BSEQ sequence file"));
            }
        }
    }

    private static void RemoveLegacyTrainerThrowOutputs(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        foreach (var sourceFile in EnumerateLegacyTrainerThrowFiles(paths.BaseRomFsPath, diagnostics))
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = SwShFpsLegacyTrainerThrowCleanupPatcher.ConvertLegacyOutput(
                    sourceFile.RelativePath,
                    sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (!outputBytes.SequenceEqual(generated))
                {
                    continue;
                }

                if (TryApplyOutputMutation(
                        paths,
                        SwShOutputFileMutation.DeleteLegacyAdoption(
                            sourceFile.RelativePath,
                            ToOutputFileState(outputBytes)),
                        "tool.sword-shield.60fps-cleanup-legacy-trainer",
                        diagnostics))
                {
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, sourceFile.RelativePath));
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy trainer throw output: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable legacy 60FPS Patch trainer animation output"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy trainer throw output: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable legacy 60FPS Patch trainer animation output"));
            }
            catch (InvalidDataException)
            {
                // If the legacy conversion cannot be reproduced, leave the file in place.
            }
        }

        foreach (var sourceFile in EnumerateLegacyTrainerBallThrowTimingFiles(paths.BaseRomFsPath, diagnostics))
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = SwShFpsTrainerThrowPatcher.ConvertAnimationToHalfSpeed(sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (!outputBytes.SequenceEqual(generated))
                {
                    continue;
                }

                if (TryApplyOutputMutation(
                        paths,
                        SwShOutputFileMutation.DeleteLegacyAdoption(
                            sourceFile.RelativePath,
                            ToOutputFileState(outputBytes)),
                        "tool.sword-shield.60fps-cleanup-legacy-ball-throw",
                        diagnostics))
                {
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, sourceFile.RelativePath));
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy ball throw output: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable legacy 60FPS Patch ball throw output"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy ball throw output: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Deletable legacy 60FPS Patch ball throw output"));
            }
            catch (InvalidDataException)
            {
                // If the legacy conversion cannot be reproduced, leave the file in place.
            }
        }
    }

    private static void RemoveLegacyExcludedDemoSequenceOutputs(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        foreach (var relativePath in ExcludedDemoSequenceBseqRelativePaths)
        {
            var sourcePath = ResolveBaseRomFsPath(paths.BaseRomFsPath, relativePath);
            var targetPath = ResolveOutputPath(paths.OutputRootPath, relativePath);
            if (sourcePath is null || targetPath is null || !File.Exists(sourcePath) || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourcePath);
                var generated = ConvertBseq(sourceBytes, SwShFpsBseqPatcher.OpeningDemoTimelineScale);
                var outputBytes = File.ReadAllBytes(targetPath);
                if (!outputBytes.SequenceEqual(generated))
                {
                    continue;
                }

                if (TryApplyOutputMutation(
                        paths,
                        SwShOutputFileMutation.DeleteLegacyAdoption(
                            relativePath,
                            ToOutputFileState(outputBytes)),
                        "tool.sword-shield.60fps-cleanup-legacy-demo",
                        diagnostics))
                {
                    writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, relativePath));
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy excluded demo output: {exception.Message}",
                    file: relativePath,
                    expected: "Deletable legacy 60FPS Patch demo output"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not remove a legacy excluded demo output: {exception.Message}",
                    file: relativePath,
                    expected: "Deletable legacy 60FPS Patch demo output"));
            }
            catch (InvalidDataException)
            {
                // If the legacy conversion cannot be reproduced, leave the file in place.
            }
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
                expected: "Readable Base ExeFS folder"));
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
                expected: "Readable base or output exefs/main"));
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
                    expected: "Supported Sword/Shield 1.3.2 exefs/main"));
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
                expected: "Readable exefs/main"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not inspect exefs/main: {exception.Message}",
                file: ExeFsMainPath,
                expected: "Readable exefs/main"));
        }

        return MainStatus.Empty;
    }

    private RomFsStatus AnalyzeRomFsOutputs(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "60FPS Patch requires Base RomFS.",
                field: "baseRomFsPath",
                expected: "Readable Base RomFS folder"));
            return RomFsStatus.Empty;
        }

        var sourceFiles = EnumerateManagedRomFsFiles(paths.BaseRomFsPath, diagnostics);
        var manifestHashes = ReadManifestOwnedFileHashes(paths, diagnostics);
        var inspectedFiles = new List<InspectedRomFsFile>(sourceFiles.Count);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            inspectedFiles.AddRange(sourceFiles.Select(sourceFile => new InspectedRomFsFile(
                sourceFile.RelativePath,
                GetRomFsCategory(sourceFile.RelativePath),
                ManagedRomFsFileState.NotInstalled)));
            return CreateRomFsStatus(inspectedFiles);
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
                return;
            }

            omittedConflictDiagnosticCount++;
        }

        foreach (var sourceFile in sourceFiles)
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    ManagedRomFsFileState.Conflict));
                AddConflictDiagnostic(
                    "60FPS Patch could not resolve this managed ROMFS path inside Output Root.",
                    sourceFile.RelativePath,
                    "Output-root-contained managed ROMFS path");
                continue;
            }

            if (!File.Exists(targetPath))
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    ManagedRomFsFileState.NotInstalled));
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes);
                var outputBytes = File.ReadAllBytes(targetPath);
                var state = outputBytes.SequenceEqual(generated)
                    ? ManagedRomFsFileState.Patched
                    : outputBytes.SequenceEqual(sourceBytes)
                        ? ManagedRomFsFileState.NotInstalled
                        : MatchesManifestOwnedOutput(sourceFile.RelativePath, outputBytes, manifestHashes)
                            ? ManagedRomFsFileState.StaleOwned
                            : ManagedRomFsFileState.Conflict;
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    state));
                if (state == ManagedRomFsFileState.Conflict)
                {
                    AddConflictDiagnostic(
                        "60FPS Patch will not overwrite this ROMFS file because it differs from Base RomFS, current KM output, and recorded KM-owned output.",
                        sourceFile.RelativePath,
                        "Vanilla, current KM-generated, or manifest-recorded KM-owned ROMFS file");
                }
            }
            catch (IOException exception)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    ManagedRomFsFileState.Conflict));
                AddConflictDiagnostic(
                    $"60FPS Patch could not inspect this managed ROMFS file: {exception.Message}",
                    sourceFile.RelativePath,
                    "Readable Base RomFS and Output Root files");
            }
            catch (UnauthorizedAccessException exception)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    ManagedRomFsFileState.Conflict));
                AddConflictDiagnostic(
                    $"60FPS Patch could not inspect this managed ROMFS file: {exception.Message}",
                    sourceFile.RelativePath,
                    "Readable Base RomFS and Output Root files");
            }
            catch (InvalidDataException exception)
            {
                inspectedFiles.Add(new InspectedRomFsFile(
                    sourceFile.RelativePath,
                    GetRomFsCategory(sourceFile.RelativePath),
                    ManagedRomFsFileState.Conflict));
                AddConflictDiagnostic(
                    exception.Message,
                    sourceFile.RelativePath,
                    "Valid managed Sword/Shield ROMFS file");
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

        return CreateRomFsStatus(inspectedFiles);
    }

    private static RomFsStatus CreateRomFsStatus(IReadOnlyList<InspectedRomFsFile> files)
    {
        var staleOwnedFiles = files
            .Where(file => file.State == ManagedRomFsFileState.StaleOwned)
            .Select(file => file.RelativePath)
            .Take(MaximumReportedRomFsPaths)
            .ToArray();
        var conflictingFiles = files
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

        return new RomFsStatus(
            files.Count,
            files.Count(file => file.State == ManagedRomFsFileState.Patched),
            files.Count(file => file.State == ManagedRomFsFileState.StaleOwned),
            files.Count(file => file.State == ManagedRomFsFileState.Conflict),
            staleOwnedFiles,
            conflictingFiles,
            categories);
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

    private SwShFpsPatchStatus CreateStatus(
        MainStatus mainStatus,
        RomFsStatus romFsStatus,
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
            && romFsStatus.ManagedFileCount > 0
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
            diagnostics);
    }

    private SwShFpsPatchApplyResult CreateApplyResult(
        ProjectPaths paths,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
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

        return new SwShFpsPatchApplyResult(status, applyResult);
    }

    private static IReadOnlyList<ManagedRomFsFile> EnumerateManagedRomFsFiles(
        string baseRomFsPath,
        ICollection<ValidationDiagnostic> diagnostics)
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
                expected: "Complete Sword/Shield Base RomFS move-effect sequence folder"));
        }

        var files = managedBseqFiles.ToList();
        files.AddRange(EnumerateManagedBattleCameraFiles(baseRomFsPath, diagnostics));
        files.AddRange(EnumerateManagedBattleUiArchives(baseRomFsPath, diagnostics));
        files.AddRange(EnumerateManagedDemoBseqFiles(baseRomFsPath, diagnostics));
        foreach (var sourceFile in RequiredManagedBseqFiles)
        {
            AddRequiredManagedRomFsFile(baseRomFsPath, sourceFile.RelativePath, files, diagnostics);
        }

        foreach (var relativePath in RequiredManagedBattleModelAnimationFiles)
        {
            AddRequiredManagedRomFsFile(baseRomFsPath, relativePath, files, diagnostics);
        }

        AddRequiredManagedRomFsFile(
            baseRomFsPath,
            SwShFpsDemoAudiencePatcher.AudienceArchiveRelativePath,
            files,
            diagnostics);
        AddRequiredManagedRomFsFile(
            baseRomFsPath,
            SwShFpsPokemonCenterRecoveryPatcher.RecoveryArchiveRelativePath,
            files,
            diagnostics);
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
                expected: "Sword/Shield Base RomFS move-effect sequence folder"));
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
                expected: "Readable move-effect sequence folder"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan BSEQ files: {exception.Message}",
                file: SequenceRootRelativePath,
                expected: "Readable move-effect sequence folder"));
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
                expected: "Sword/Shield Base RomFS battle camera folder"));
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
                expected: "Readable battle camera folder"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle camera files: {exception.Message}",
                file: BattleCameraRootRelativePath,
                expected: "Readable battle camera folder"));
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
                expected: "Sword/Shield Base RomFS battle UI folder"));
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
                expected: "Readable battle UI folder"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan battle UI files: {exception.Message}",
                file: BattleUiRootRelativePath,
                expected: "Readable battle UI folder"));
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
                expected: "Sword/Shield Base RomFS demo sequence folder"));
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
                expected: "Readable demo sequence folder"));
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"60FPS Patch could not scan demo BSEQ files: {exception.Message}",
                file: DemoSequenceRootRelativePath,
                expected: "Readable demo sequence folder"));
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
                expected: "Complete Sword/Shield Base RomFS"));
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
                expected: "Readable legacy trainer animation folder"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable legacy trainer animation folder"));
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
                expected: "Readable trainer animation folder"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch could not scan legacy trainer ball throw files: {exception.Message}",
                file: rootRelativePath,
                expected: "Readable trainer animation folder"));
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
                expected: "Editable project paths"));
        }
    }

    private static void WriteOutputFile(
        ProjectPaths paths,
        string relativePath,
        byte[] contents,
        OutputFileState reviewedPreimage,
        ICollection<ValidationDiagnostic> diagnostics,
        ICollection<ProjectFileReference> writtenFiles)
    {
        var mutation = string.Equals(relativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
            ? SwShOutputFileMutation.WriteComposed(relativePath, contents, reviewedPreimage)
            : SwShOutputFileMutation.Write(relativePath, contents, reviewedPreimage);
        if (TryApplyOutputMutation(
                paths,
                mutation,
                "tool.sword-shield.60fps-install",
                diagnostics))
        {
            writtenFiles.Add(new ProjectFileReference(ProjectFileLayer.Layered, relativePath));
            return;
        }
    }

    private static bool TryApplyOutputMutation(
        ProjectPaths paths,
        SwShOutputFileMutation mutation,
        string operationId,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (SwShOutputTransactionWriter.TryApply(
                paths,
                [mutation],
                operationId,
                out _,
                out var failure))
        {
            return true;
        }

        diagnostics.Add(CreateOutputTransactionDiagnostic(
            "60FPS Patch output transaction failed",
            failure));
        return false;
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

    private static IReadOnlyDictionary<string, string> ReadManifestOwnedFileHashes(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic>? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var manifestPath = ResolveOutputPath(paths.OutputRootPath, ManifestRelativePath);
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<FpsPatchManifest>(
                File.ReadAllText(manifestPath),
                ManifestJsonOptions);
            if (manifest is null || manifest.Version != 1 || manifest.RomFsFiles is null)
            {
                throw new InvalidDataException("60FPS Patch manifest has an unsupported layout.");
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

            return hashes;
        }
        catch (IOException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be read: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Readable 60FPS Patch ownership manifest"));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be read: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Readable 60FPS Patch ownership manifest"));
        }
        catch (JsonException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be parsed: {exception.Message}",
                file: ManifestRelativePath,
                expected: "Valid 60FPS Patch ownership manifest"));
        }
        catch (InvalidDataException exception)
        {
            diagnostics?.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                exception.Message,
                file: ManifestRelativePath,
                expected: "Version 1 60FPS Patch ownership manifest"));
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesManifestOwnedOutput(
        string relativePath,
        byte[] output,
        IReadOnlyDictionary<string, string> manifestHashes)
    {
        return manifestHashes.TryGetValue(NormalizeRelativePath(relativePath), out var expectedHash)
            && string.Equals(ComputeSha256(output), expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshManifestSnapshot(
        ProjectPaths paths,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath) || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        var previousHashes = ReadManifestOwnedFileHashes(paths, diagnostics);
        var enumerationDiagnostics = new List<ValidationDiagnostic>();
        var sourceFiles = EnumerateManagedRomFsFiles(paths.BaseRomFsPath, enumerationDiagnostics);
        foreach (var diagnostic in enumerationDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (enumerationDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        var ownedFiles = new List<FpsPatchManifestFile>();
        foreach (var sourceFile in sourceFiles)
        {
            var targetPath = ResolveOutputPath(paths.OutputRootPath, sourceFile.RelativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var sourceBytes = File.ReadAllBytes(sourceFile.SourcePath);
                var generated = ConvertManagedRomFsFile(sourceFile.RelativePath, sourceBytes);
                var output = File.ReadAllBytes(targetPath);
                if (output.SequenceEqual(generated)
                    || MatchesManifestOwnedOutput(sourceFile.RelativePath, output, previousHashes))
                {
                    ownedFiles.Add(new FpsPatchManifestFile(
                        sourceFile.RelativePath,
                        ComputeSha256(output)));
                }
            }
            catch (IOException exception)
            {
                PreservePreviousManifestEntry(sourceFile.RelativePath, previousHashes, ownedFiles);
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not refresh ownership for a managed ROMFS file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Readable managed ROMFS output"));
            }
            catch (UnauthorizedAccessException exception)
            {
                PreservePreviousManifestEntry(sourceFile.RelativePath, previousHashes, ownedFiles);
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not refresh ownership for a managed ROMFS file: {exception.Message}",
                    file: sourceFile.RelativePath,
                    expected: "Readable managed ROMFS output"));
            }
            catch (InvalidDataException exception)
            {
                PreservePreviousManifestEntry(sourceFile.RelativePath, previousHashes, ownedFiles);
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    exception.Message,
                    file: sourceFile.RelativePath,
                    expected: "Valid managed Sword/Shield ROMFS output"));
            }
        }

        PreserveUnvisitedManifestEntries(
            paths,
            sourceFiles
                .Select(sourceFile => sourceFile.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            previousHashes,
            ownedFiles,
            diagnostics);

        var mainPatched = HasInstalledMainOutput(paths);
        if (!mainPatched && ownedFiles.Count == 0)
        {
            DeleteManifest(paths, diagnostics);
            return;
        }

        WriteManifest(
            paths,
            new FpsPatchManifest(
                Version: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                ExeFsMainPatched: mainPatched,
                RomFsFiles: ownedFiles
                    .DistinctBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray()),
            diagnostics);
    }

    private static void PreserveUnvisitedManifestEntries(
        ProjectPaths paths,
        IReadOnlySet<string> visitedPaths,
        IReadOnlyDictionary<string, string> previousHashes,
        ICollection<FpsPatchManifestFile> ownedFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var (relativePath, previousHash) in previousHashes)
        {
            if (visitedPaths.Contains(relativePath) || !IsManagedRomFsPath(relativePath))
            {
                continue;
            }

            var targetPath = ResolveOutputPath(paths.OutputRootPath!, relativePath);
            if (targetPath is null || !File.Exists(targetPath))
            {
                continue;
            }

            try
            {
                var output = File.ReadAllBytes(targetPath);
                if (string.Equals(ComputeSha256(output), previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    ownedFiles.Add(new FpsPatchManifestFile(relativePath, previousHash));
                }
            }
            catch (IOException exception)
            {
                ownedFiles.Add(new FpsPatchManifestFile(relativePath, previousHash));
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not verify previously recorded ROMFS ownership: {exception.Message}",
                    file: relativePath,
                    expected: "Readable previously recorded KM-owned ROMFS output"));
            }
            catch (UnauthorizedAccessException exception)
            {
                ownedFiles.Add(new FpsPatchManifestFile(relativePath, previousHash));
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"60FPS Patch could not verify previously recorded ROMFS ownership: {exception.Message}",
                    file: relativePath,
                    expected: "Readable previously recorded KM-owned ROMFS output"));
            }
        }
    }

    private static void PreservePreviousManifestEntry(
        string relativePath,
        IReadOnlyDictionary<string, string> previousHashes,
        ICollection<FpsPatchManifestFile> ownedFiles)
    {
        if (previousHashes.TryGetValue(NormalizeRelativePath(relativePath), out var previousHash))
        {
            ownedFiles.Add(new FpsPatchManifestFile(relativePath, previousHash));
        }
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

    private static void WriteManifest(
        ProjectPaths paths,
        FpsPatchManifest manifest,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        if (!SwShOutputTransactionWriter.TryApply(
                paths,
                [SwShOutputFileMutation.Write(
                    ManifestRelativePath,
                    JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions))],
                "tool.sword-shield.60fps-manifest-write",
                out _,
                out var failure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be written: {failure?.Message ?? "Unknown output transaction error."}",
                file: ManifestRelativePath,
                expected: "Coordinator-owned writable 60FPS Patch manifest") with
            {
                Code = failure?.Code,
            });
        }
    }

    private static void DeleteManifest(ProjectPaths paths, ICollection<ValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return;
        }

        if (!SwShOutputTransactionWriter.TryCapturePreimage(
                paths,
                ManifestRelativePath,
                out var reviewedPreimage,
                out var captureFailure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be reviewed for deletion: {captureFailure?.Message ?? "Unknown output transaction error."}",
                file: ManifestRelativePath,
                expected: "Exact reviewed 60FPS Patch manifest preimage") with
            {
                Code = captureFailure?.Code,
            });
            return;
        }

        if (reviewedPreimage is null || !reviewedPreimage.Exists)
        {
            return;
        }

        if (!SwShOutputTransactionWriter.TryApply(
                paths,
                [SwShOutputFileMutation.DeleteLegacyAdoption(
                    ManifestRelativePath,
                    reviewedPreimage)],
                "tool.sword-shield.60fps-manifest-delete",
                out _,
                out var failure))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"60FPS Patch manifest could not be deleted: {failure?.Message ?? "Unknown output transaction error."}",
                file: ManifestRelativePath,
                expected: "Coordinator-owned deletable 60FPS Patch manifest") with
            {
                Code = failure?.Code,
            });
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
        IReadOnlyList<SwShFpsPatchRomFsCategoryStatus> Categories)
    {
        public static RomFsStatus Empty { get; } = new(0, 0, 0, 0, [], [], []);
    }

    private sealed record FpsPatchManifest(
        int Version,
        DateTimeOffset CreatedAt,
        bool ExeFsMainPatched,
        IReadOnlyList<FpsPatchManifestFile> RomFsFiles);

    private sealed record FpsPatchManifestFile(
        string RelativePath,
        string Sha256);
}
