// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.SwSh.Editing;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Editing;

public sealed class SwShChangePlanSourceGuardTests
{
    private const string RelativePath = "romfs/data/example.bin";

    [Fact]
    public void CaptureTracksCurrentLayerAndValidateRejectsChangedBytes()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var basePlan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Base);

        var reviewedPlan = SwShChangePlanSourceGuard.Capture(project.Paths, basePlan);

        var reviewedWrite = Assert.Single(reviewedPlan.Writes);
        Assert.Equal(ProjectFileLayer.Base, Assert.Single(reviewedWrite.Sources).Layer);
        Assert.False(string.IsNullOrWhiteSpace(reviewedWrite.SourceFingerprint));
        Assert.Empty(SwShChangePlanSourceGuard.Validate(project.Paths, reviewedPlan));

        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 4]);

        Assert.Contains(
            SwShChangePlanSourceGuard.Validate(project.Paths, reviewedPlan),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CapturePromotesAPlanToTheCurrentLayeredSource()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var sessionId = EditSessionId.New();
        var reviewedPlan = SwShChangePlanSourceGuard.Capture(
            project.Paths,
            CreatePlan(sessionId, ProjectFileLayer.Base));

        project.WriteOutputFile(RelativePath, [3, 2, 1]);
        var currentPlan = SwShChangePlanSourceGuard.Capture(
            project.Paths,
            CreatePlan(sessionId, ProjectFileLayer.Base));

        Assert.Equal(
            ProjectFileLayer.Layered,
            Assert.Single(Assert.Single(currentPlan.Writes).Sources).Layer);
        Assert.False(ChangePlanReview.Matches(reviewedPlan, currentPlan));
    }

    [Fact]
    public void ValidateRejectsPlanReplayedAgainstDifferentOutputRoot()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var reviewedPlan = SwShChangePlanSourceGuard.Capture(
            project.Paths,
            CreatePlan(EditSessionId.New(), ProjectFileLayer.Base));
        var otherOutputRoot = Directory.CreateDirectory(
            Path.Combine(project.RootPath, "other-output")).FullName;

        var diagnostics = SwShChangePlanSourceGuard.Validate(
            project.Paths with { OutputRootPath = otherOutputRoot },
            reviewedPlan);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaptureAddsAuthoritativeTargetWhenPlanSourcesAreEmpty()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var plan = new ChangePlan(
            EditSessionId.New(),
            [
                new PlannedFileWrite(
                    RelativePath,
                    Array.Empty<ProjectFileReference>(),
                    ReplacesExistingOutput: false,
                    "Apply example edit"),
            ],
            Array.Empty<ValidationDiagnostic>());

        var reviewedPlan = SwShChangePlanSourceGuard.Capture(project.Paths, plan);

        var source = Assert.Single(Assert.Single(reviewedPlan.Writes).Sources);
        Assert.Equal(ProjectFileLayer.Base, source.Layer);
        Assert.Equal(RelativePath, source.RelativePath);
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 4]);
        Assert.Contains(
            SwShChangePlanSourceGuard.Validate(project.Paths, reviewedPlan),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CaptureRejectsMissingBaseSource()
    {
        using var project = TemporarySwShProject.Create();

        var reviewedPlan = SwShChangePlanSourceGuard.Capture(
            project.Paths,
            CreatePlan(EditSessionId.New(), ProjectFileLayer.Base));

        Assert.False(reviewedPlan.CanApply);
        Assert.Contains(
            reviewedPlan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(reviewedPlan.Writes).SourceFingerprint));
    }

    [Fact]
    public void CaptureRejectsMissingLayeredSource()
    {
        using var project = TemporarySwShProject.Create();

        var reviewedPlan = SwShChangePlanSourceGuard.Capture(
            project.Paths,
            CreatePlan(EditSessionId.New(), ProjectFileLayer.Layered));

        Assert.False(reviewedPlan.CanApply);
        Assert.Contains(
            reviewedPlan.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Layered", StringComparison.OrdinalIgnoreCase)
                && diagnostic.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaptureAllowsAbsentGeneratedTarget()
    {
        using var project = TemporarySwShProject.Create();
        var plan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Generated);

        var reviewedPlan = SwShChangePlanSourceGuard.Capture(project.Paths, plan);

        Assert.True(reviewedPlan.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(reviewedPlan.Writes).SourceFingerprint));
        Assert.Empty(SwShChangePlanSourceGuard.Validate(project.Paths, reviewedPlan));
    }

    [Fact]
    public void CaptureRejectsBaseSourceLinkedOutsideConfiguredRoot()
    {
        using var project = TemporarySwShProject.Create();
        var externalPath = Path.Combine(project.RootPath, "external-source.bin");
        File.WriteAllBytes(externalPath, [1, 2, 3]);
        var linkPath = Path.Combine(project.BaseRomFsPath, "data", "example.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        if (!TryCreateFileSymbolicLink(linkPath, externalPath))
        {
            return;
        }

        try
        {
            var reviewedPlan = SwShChangePlanSourceGuard.Capture(
                project.Paths,
                CreatePlan(EditSessionId.New(), ProjectFileLayer.Base));

            Assert.False(reviewedPlan.CanApply);
            Assert.Contains(
                reviewedPlan.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Message.Contains("safe file", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(linkPath);
        }
    }

    [Fact]
    public void CaptureRejectsBaseSourceLinkedElsewhereInsideConfiguredRoot()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/actual.bin", [1, 2, 3]);
        var actualPath = Path.Combine(project.BaseRomFsPath, "data", "actual.bin");
        var linkPath = Path.Combine(project.BaseRomFsPath, "data", "example.bin");
        if (!TryCreateFileSymbolicLink(linkPath, actualPath))
        {
            return;
        }

        try
        {
            var reviewedPlan = SwShChangePlanSourceGuard.Capture(
                project.Paths,
                CreatePlan(EditSessionId.New(), ProjectFileLayer.Base));

            Assert.False(reviewedPlan.CanApply);
        }
        finally
        {
            File.Delete(linkPath);
        }
    }

    [Fact]
    public void AcquireApplyScopeFingerprintsHeldBaseBytesAndKeepsThemStable()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var sourcePath = Path.Combine(project.BaseRomFsPath, "data", "example.bin");
        var plan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Base);
        var reviewedPlan = SwShChangePlanSourceGuard.Capture(project.Paths, plan);

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out var diagnostics));
        Assert.Empty(diagnostics);
        using (var scope = verifiedScope!)
        {
            Assert.True(ChangePlanReview.Matches(reviewedPlan, scope.CurrentPlan));
            Assert.Throws<IOException>(() => File.WriteAllBytes(sourcePath, [9, 9, 9]));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(sourcePath));
        }

        File.WriteAllBytes(sourcePath, [9, 9, 9]);
        Assert.Equal([9, 9, 9], File.ReadAllBytes(sourcePath));
    }

    [Fact]
    public void AcquireApplyScopeCopiesLayeredBytesAndDisposeLeavesOutputUnchanged()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        project.WriteOutputFile(RelativePath, [4, 5, 6]);
        var plan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Base);

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using (var scope = verifiedScope!)
        {
            var stagedPath = GetOutputPath(scope.ApplyPaths.OutputRootPath!, RelativePath);
            Assert.Equal([4, 5, 6], File.ReadAllBytes(stagedPath));
            File.WriteAllBytes(stagedPath, [7, 8, 9]);
        }

        Assert.Equal([4, 5, 6], File.ReadAllBytes(GetOutputPath(project.OutputRootPath, RelativePath)));
    }

    [Fact]
    public void AcquireApplyScopeCopiesSharedOutputSourceOnceAcrossLayerIdentities()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteOutputFile(RelativePath, [4, 5, 6]);
        var plan = new ChangePlan(
            EditSessionId.New(),
            [
                new PlannedFileWrite(
                    RelativePath,
                    [
                        new ProjectFileReference(ProjectFileLayer.Layered, RelativePath),
                        new ProjectFileReference(ProjectFileLayer.Generated, RelativePath),
                    ],
                    ReplacesExistingOutput: true,
                    "Apply test edit"),
            ],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;

        Assert.Equal(
            [4, 5, 6],
            File.ReadAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, RelativePath)));
    }

    [Fact]
    public void PrepareSnapshotPlanSanitizesPrivateSnapshotPaths()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1, 2, 3]);
        var plan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Base);

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        var privateOutputRoot = scope.ApplyPaths.OutputRootPath!;
        var snapshotPlan = plan with
        {
            Diagnostics =
            [
                new ValidationDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Injected failure at {privateOutputRoot}.",
                    File: privateOutputRoot,
                    Expected: privateOutputRoot),
            ],
        };

        Assert.False(scope.TryPrepareSnapshotPlan(snapshotPlan, out var preparedPlan));

        Assert.All(
            preparedPlan.Diagnostics,
            diagnostic =>
            {
                Assert.DoesNotContain(privateOutputRoot, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(privateOutputRoot, diagnostic.File ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(privateOutputRoot, diagnostic.Expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            });
        Assert.Contains(
            preparedPlan.Diagnostics,
            diagnostic => diagnostic.Message.Contains("<verified apply snapshot>", StringComparison.Ordinal));
    }

    [Fact]
    public void CommitPromotesOnlyReportedPlannedWritesAndDeletions()
    {
        using var project = TemporarySwShProject.Create();
        const string createdPath = "romfs/data/created.bin";
        project.WriteBaseRomFsFile("data/example.bin", [1]);
        project.WriteOutputFile(RelativePath, [2]);
        var sessionId = EditSessionId.New();
        var plan = new ChangePlan(
            sessionId,
            [
                CreateWrite(RelativePath, ProjectFileLayer.Layered),
                CreateWrite(createdPath, ProjectFileLayer.Generated),
            ],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.Delete(GetOutputPath(scope.ApplyPaths.OutputRootPath!, RelativePath));
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, createdPath, createParent: true), [7, 8]);
        var result = scope.Commit(CreateApplyResult(
            scope.CurrentPlan,
            RelativePath,
            createdPath));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, RelativePath)));
        Assert.Equal([7, 8], File.ReadAllBytes(GetOutputPath(project.OutputRootPath, createdPath)));
    }

    [Fact]
    public void CommitWithApplyErrorLeavesRealOutputUntouched()
    {
        using var project = TemporarySwShProject.Create();
        const string createdPath = "romfs/data/created.bin";
        var plan = new ChangePlan(
            EditSessionId.New(),
            [CreateWrite(createdPath, ProjectFileLayer.Generated)],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, createdPath, createParent: true), [7]);
        var failedResult = CreateApplyResult(scope.CurrentPlan, createdPath) with
        {
            Diagnostics =
            [
                new ValidationDiagnostic(DiagnosticSeverity.Error, "Injected apply failure."),
            ],
        };

        var result = scope.Commit(failedResult);

        Assert.Empty(result.WrittenFiles);
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, createdPath)));
    }

    [Fact]
    public void CommitAllowsReportedPlannedNoOpAndReturnsNoWrittenFile()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteOutputFile(RelativePath, [4, 5, 6]);
        var plan = new ChangePlan(
            EditSessionId.New(),
            [CreateWrite(RelativePath, ProjectFileLayer.Layered)],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;

        var result = scope.Commit(CreateApplyResult(scope.CurrentPlan, RelativePath));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal([4, 5, 6], File.ReadAllBytes(GetOutputPath(project.OutputRootPath, RelativePath)));
    }

    [Fact]
    public void CommitRejectsUndeclaredSnapshotMutation()
    {
        using var project = TemporarySwShProject.Create();
        const string plannedPath = "romfs/data/planned.bin";
        const string undeclaredPath = "romfs/data/undeclared.bin";
        var plan = new ChangePlan(
            EditSessionId.New(),
            [CreateWrite(plannedPath, ProjectFileLayer.Generated)],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, undeclaredPath, createParent: true), [7]);

        var result = scope.Commit(CreateApplyResult(scope.CurrentPlan, plannedPath));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("reviewed change plan", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, plannedPath)));
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, undeclaredPath)));
    }

    [Fact]
    public void LateSecondPromotionFailureRollsBackFirstTarget()
    {
        using var project = TemporarySwShProject.Create();
        const string firstPath = "romfs/data/a.bin";
        const string secondPath = "romfs/data/b.bin";
        var plan = new ChangePlan(
            EditSessionId.New(),
            [
                CreateWrite(firstPath, ProjectFileLayer.Generated),
                CreateWrite(secondPath, ProjectFileLayer.Generated),
            ],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, firstPath, createParent: true), [1]);
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, secondPath, createParent: true), [2]);

        var result = scope.Commit(
            CreateApplyResult(scope.CurrentPlan, firstPath, secondPath),
            (index, _) =>
            {
                if (index == 1)
                {
                    throw new IOException("Injected late promotion failure.");
                }
            });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.WrittenFiles);
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, firstPath)));
        Assert.False(File.Exists(GetOutputPath(project.OutputRootPath, secondPath)));
        Assert.False(Directory.Exists(Path.Combine(project.OutputRootPath, "romfs")));
    }

    [Fact]
    public void LateMissingTargetCollisionIsPreserved()
    {
        using var project = TemporarySwShProject.Create();
        const string targetPath = "romfs/data/collision.bin";
        var plan = new ChangePlan(
            EditSessionId.New(),
            [CreateWrite(targetPath, ProjectFileLayer.Generated)],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, targetPath, createParent: true), [1]);
        var realTargetPath = GetOutputPath(project.OutputRootPath, targetPath, createParent: true);

        var result = scope.Commit(
            CreateApplyResult(scope.CurrentPlan, targetPath),
            (_, _) => File.WriteAllBytes(realTargetPath, [9]));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal([9], File.ReadAllBytes(realTargetPath));
    }

    [Fact]
    public void LateExistingTargetChangeIsPreserved()
    {
        using var project = TemporarySwShProject.Create();
        const string targetPath = "romfs/data/existing.bin";
        project.WriteOutputFile(targetPath, [1]);
        var plan = new ChangePlan(
            EditSessionId.New(),
            [CreateWrite(targetPath, ProjectFileLayer.Layered)],
            Array.Empty<ValidationDiagnostic>());

        Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
            project.Paths,
            plan,
            out var verifiedScope,
            out _));
        using var scope = verifiedScope!;
        File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, targetPath), [2]);
        var realTargetPath = GetOutputPath(project.OutputRootPath, targetPath);

        var result = scope.Commit(
            CreateApplyResult(scope.CurrentPlan, targetPath),
            (_, _) => File.WriteAllBytes(realTargetPath, [9]));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.WrittenFiles);
        Assert.Equal([9], File.ReadAllBytes(realTargetPath));
    }

    [Fact]
    public void ApplyScopeStabilizesConfiguredOutputRootLink()
    {
        using var project = TemporarySwShProject.Create();
        project.WriteBaseRomFsFile("data/example.bin", [1]);
        var firstOutputRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "linked-output-a")).FullName;
        var secondOutputRoot = Directory.CreateDirectory(Path.Combine(project.RootPath, "linked-output-b")).FullName;
        var outputLink = Path.Combine(project.RootPath, "output-link");
        if (!TryCreateDirectorySymbolicLink(outputLink, firstOutputRoot))
        {
            return;
        }

        try
        {
            var paths = project.Paths with { OutputRootPath = outputLink };
            var plan = CreatePlan(EditSessionId.New(), ProjectFileLayer.Base);
            var reviewedPlan = SwShChangePlanSourceGuard.Capture(paths, plan);
            Assert.True(SwShChangePlanSourceGuard.TryAcquireApplyScope(
                paths,
                plan,
                out var verifiedScope,
                out _));
            using var scope = verifiedScope!;
            Assert.True(ChangePlanReview.Matches(reviewedPlan, scope.CurrentPlan));
            File.WriteAllBytes(GetOutputPath(scope.ApplyPaths.OutputRootPath!, RelativePath, createParent: true), [7]);

            Directory.Delete(outputLink);
            Assert.True(TryCreateDirectorySymbolicLink(outputLink, secondOutputRoot));
            var result = scope.Commit(CreateApplyResult(scope.CurrentPlan, RelativePath));

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Equal([7], File.ReadAllBytes(GetOutputPath(firstOutputRoot, RelativePath)));
            Assert.False(File.Exists(GetOutputPath(secondOutputRoot, RelativePath)));
        }
        finally
        {
            if (Directory.Exists(outputLink))
            {
                Directory.Delete(outputLink);
            }
        }
    }

    private static ChangePlan CreatePlan(EditSessionId sessionId, ProjectFileLayer layer)
    {
        return new ChangePlan(
            sessionId,
            [
                new PlannedFileWrite(
                    RelativePath,
                    [new ProjectFileReference(layer, RelativePath)],
                    ReplacesExistingOutput: false,
                    "Apply example edit"),
            ],
            Array.Empty<ValidationDiagnostic>());
    }

    private static PlannedFileWrite CreateWrite(string relativePath, ProjectFileLayer layer)
    {
        return new PlannedFileWrite(
            relativePath,
            [new ProjectFileReference(layer, relativePath)],
            ReplacesExistingOutput: layer == ProjectFileLayer.Layered,
            "Apply test edit");
    }

    private static ApplyResult CreateApplyResult(ChangePlan plan, params string[] writtenPaths)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenPaths
                .Select(path => new ProjectFileReference(ProjectFileLayer.Generated, path))
                .ToArray(),
            new WriteManifest(applyId, appliedAt, plan.Writes),
            Array.Empty<ValidationDiagnostic>());
    }

    private static string GetOutputPath(string outputRootPath, string relativePath, bool createParent = false)
    {
        var path = Path.Combine(outputRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (createParent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        return path;
    }

    private static bool TryCreateFileSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
