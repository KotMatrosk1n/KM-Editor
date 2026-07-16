// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventureSaveSeedServiceTests
{
    [Fact]
    public void SetSeedReturnsDiagnosticWhenSaveFilePathIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        var service = new SwShDynamaxAdventureSaveSeedService();

        var result = service.SetSeed(
            temp.Paths with { SelectedGame = ProjectGame.Sword },
            seed: 0x1234);

        Assert.False(result.WasChanged);
        Assert.False(result.ChecksumsValid);
        Assert.Null(result.SaveFilePath);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("save file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetSeedReturnsDiagnosticWhenSaveFileDoesNotExist()
    {
        using var temp = TemporarySwShProject.Create();
        var missingSavePath = Path.Combine(temp.RootPath, "missing-main");
        var service = new SwShDynamaxAdventureSaveSeedService();

        var result = service.SetSeed(
            temp.Paths with { SaveFilePath = missingSavePath, SelectedGame = ProjectGame.Sword },
            seed: 0x1234);

        Assert.False(result.WasChanged);
        Assert.False(result.ChecksumsValid);
        Assert.Equal(missingSavePath, result.SaveFilePath);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("could not be found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifiedReplacementRestoresOriginalWhenPostReplaceHookThrows()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService((_, _) =>
            throw new InvalidDataException("Injected post-replacement failure."));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true);

        Assert.False(result.Succeeded);
        Assert.True(result.RollbackVerified);
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
        Assert.Equal(original, File.ReadAllBytes(savePath));
    }

    [Fact]
    public void VerifiedReplacementRestoresRacingSourceAndRejectsStaleSnapshot()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);
        var racingSource = new byte[] { 5, 6, 7, 8 };
        var service = new SwShDynamaxAdventureSaveSeedService();

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true,
            beforeReplacement: () => File.WriteAllBytes(savePath, racingSource));

        Assert.False(result.Succeeded);
        Assert.True(result.SourceChanged);
        Assert.True(result.RollbackVerified);
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(racingSource, File.ReadAllBytes(result.RecoveryFilePath));
        Assert.Equal(racingSource, File.ReadAllBytes(savePath));
    }

    [Fact]
    public void VerifiedReplacementDoesNotAcceptSameSemanticExternalBytes()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var external = new byte[] { 9, 8, 7, 0 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService((targetPath, _) =>
            File.WriteAllBytes(targetPath, external));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(external, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void PostReplaceExceptionDoesNotClobberNewerExternalTarget()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var external = new byte[] { 4, 3, 2, 1 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService((targetPath, _) =>
        {
            File.WriteAllBytes(targetPath, external);
            throw new InvalidDataException("Injected post-replacement failure.");
        });

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(external, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void CombinedPreAndPostReplaceRacesPreserveNewestTargetAndBackup()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);
        var racingSource = new byte[] { 5, 6, 7, 8 };
        var newestTarget = new byte[] { 4, 3, 2, 1 };
        var service = new SwShDynamaxAdventureSaveSeedService((targetPath, _) =>
            File.WriteAllBytes(targetPath, newestTarget));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true,
            beforeReplacement: () => File.WriteAllBytes(savePath, racingSource));

        Assert.False(result.Succeeded);
        Assert.True(result.SourceChanged);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(newestTarget, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(racingSource, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void RollbackSwapDoesNotClobberWriterLandingAfterPrecheck()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var external = new byte[] { 4, 3, 2, 1 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            beforeRollbackReplacement: targetPath => File.WriteAllBytes(targetPath, external));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => false);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(external, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void RollbackDoesNotClobberNewestWriterWhenBothSwapWindowsRace()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var firstExternal = new byte[] { 4, 3, 2, 1 };
        var newestExternal = new byte[] { 8, 7, 6, 5 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            beforeRollbackReplacement: targetPath => File.WriteAllBytes(targetPath, firstExternal),
            afterRollbackSwap: targetPath => File.WriteAllBytes(targetPath, newestExternal));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => false);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(newestExternal, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void ExternalTargetRestoreRetainsWriterLandingInSecondSwapWindow()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var firstExternal = new byte[] { 4, 3, 2, 1 };
        var newestExternal = new byte[] { 8, 7, 6, 5 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            beforeRollbackReplacement: targetPath => File.WriteAllBytes(targetPath, firstExternal),
            beforeExternalTargetRestoreReplacement: targetPath =>
                File.WriteAllBytes(targetPath, newestExternal));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => false);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(firstExternal, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(newestExternal, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void ExternalTargetRestoreExceptionReportsWriterDisplacedBySecondSwap()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var firstExternal = new byte[] { 4, 3, 2, 1 };
        var newestExternal = new byte[] { 8, 7, 6, 5 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            beforeRollbackReplacement: targetPath => File.WriteAllBytes(targetPath, firstExternal),
            beforeExternalTargetRestoreReplacement: targetPath =>
                File.WriteAllBytes(targetPath, newestExternal),
            afterExternalTargetRestoreSwap: _ =>
                throw new InvalidDataException("Injected post-external-restore failure."));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => false);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(firstExternal, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(newestExternal, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void VerifiedRollbackRetainsPriorSaveWhenWriterLandsBeforeCleanup()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var external = new byte[] { 4, 3, 2, 1 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            afterRollbackVerificationBeforeCleanup: targetPath =>
                File.WriteAllBytes(targetPath, external));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => false);
        var message = SwShDynamaxAdventureSaveSeedService.CreateReplacementFailureMessage(result);

        Assert.False(result.Succeeded);
        Assert.False(result.RollbackVerified);
        Assert.False(result.TargetDiverged);
        Assert.Equal(external, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(original, File.ReadAllBytes(result.RecoveryFilePath));
        Assert.Contains("attempted recovery", message, StringComparison.Ordinal);
        Assert.Contains(result.RecoveryFilePath, message, StringComparison.Ordinal);
        Assert.DoesNotContain("KM restored", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PostReplaceExceptionReportsRacingSourceFromReplacementBackup()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);
        var racingSource = new byte[] { 5, 6, 7, 8 };
        var service = new SwShDynamaxAdventureSaveSeedService((_, _) =>
            throw new InvalidDataException("Injected post-replacement failure."));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true,
            beforeReplacement: () => File.WriteAllBytes(savePath, racingSource));
        var message = SwShDynamaxAdventureSaveSeedService.CreateReplacementFailureMessage(result);

        Assert.False(result.Succeeded);
        Assert.True(result.SourceChanged);
        Assert.True(result.RollbackVerified);
        Assert.Equal(racingSource, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(racingSource, File.ReadAllBytes(result.RecoveryFilePath));
        Assert.Contains("save changed during replacement", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified the version", message, StringComparison.Ordinal);
        Assert.DoesNotContain("KM restored", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RollbackExceptionAfterSwapIsReportedUnsafeWithDisplacedTarget()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        var original = new byte[] { 1, 2, 3, 4 };
        var replacement = new byte[] { 9, 8, 7, 6 };
        File.WriteAllBytes(savePath, original);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => { },
            afterRollbackSwap: _ => throw new InvalidDataException("Injected rollback swap failure."));

        var result = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacement,
            verifyReplacement: () => false);

        Assert.False(result.Succeeded);
        Assert.True(result.TargetDiverged);
        Assert.False(result.RollbackVerified);
        Assert.Equal(original, File.ReadAllBytes(savePath));
        Assert.NotNull(result.RecoveryFilePath);
        Assert.Equal(replacement, File.ReadAllBytes(result.RecoveryFilePath));
    }

    [Fact]
    public void ReplacementFailureMessageDoesNotClaimRestoreWhenRecoveryCannotBeVerified()
    {
        using var temp = TemporarySwShProject.Create();
        var savePath = Path.Combine(temp.RootPath, "main");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);
        var service = new SwShDynamaxAdventureSaveSeedService(
            afterReplacement: (_, _) => throw new InvalidDataException("Injected replacement failure."),
            afterRollbackSwap: targetPath => File.WriteAllBytes(targetPath, [4, 3, 2, 1]));
        var replacement = service.ReplaceWithVerifiedRollbackForTests(
            savePath,
            replacementBytes: [9, 8, 7, 6],
            verifyReplacement: () => true);

        var message = SwShDynamaxAdventureSaveSeedService.CreateReplacementFailureMessage(replacement);

        Assert.False(replacement.Succeeded);
        Assert.False(replacement.TargetDiverged);
        Assert.False(replacement.RollbackVerified);
        Assert.NotNull(replacement.RecoveryFilePath);
        Assert.Contains("attempted recovery", message, StringComparison.Ordinal);
        Assert.Contains(replacement.RecoveryFilePath, message, StringComparison.Ordinal);
        Assert.DoesNotContain("KM restored", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetDivergedFailureMessageReportsWhetherRecoveryWasRetained()
    {
        const string recoveryPath = "recovery/main.km-editor.recovery";
        var withRecovery = new SwShDynamaxAdventureSaveSeedService.ReplacementResult(
            Succeeded: false,
            SourceChanged: false,
            TargetDiverged: true,
            RollbackVerified: false,
            BackupFilePath: null,
            RecoveryFilePath: recoveryPath,
            Exception: null);

        var retainedMessage = SwShDynamaxAdventureSaveSeedService.CreateReplacementFailureMessage(withRecovery);
        var unavailableMessage = SwShDynamaxAdventureSaveSeedService.CreateReplacementFailureMessage(
            withRecovery with { RecoveryFilePath = null });

        Assert.Contains("could not verify which version is currently live", retainedMessage, StringComparison.Ordinal);
        Assert.Contains(recoveryPath, retainedMessage, StringComparison.Ordinal);
        Assert.Contains("could not verify which version is currently live", unavailableMessage, StringComparison.Ordinal);
        Assert.Contains("no verified recovery copy could be retained", unavailableMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("A recovery copy was retained at", unavailableMessage, StringComparison.Ordinal);
    }
}
