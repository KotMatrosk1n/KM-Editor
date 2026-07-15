// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Behavior;
using KM.SwSh.Tests.Items;
using System.Globalization;
using Xunit;

namespace KM.SwSh.Tests.Behavior;

public sealed class SwShBehaviorEditSessionServiceTests
{
    [Fact]
    public void UpdateEntryFieldAcceptsLegacyIndexAndStoresSignedCanonicalRecordId()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShBehaviorEditSessionService();

        var result = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.BehaviorField,
            value: "WaterDash");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.behavior", edit.Domain);
        Assert.Equal(SwShSymbolBehaviorArchive.BehaviorField, edit.Field);
        Assert.Matches("^behavior:0:[0-9A-F]{64}$", edit.RecordId);
        Assert.Equal("WaterDash", edit.NewValue);
        Assert.Equal("WaterDash", result.Workflow.Entries.Single(entry => entry.Index == 0).Behavior);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateEntryFieldsStagesKnownFieldsAtomically()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryFields(
            temp.Paths,
            session: null,
            [
                new(entryId, SwShSymbolBehaviorArchive.BehaviorField, "WaterDash"),
                new(entryId, SwShSymbolBehaviorArchive.ModelPartField, "head"),
                new(entryId, SwShSymbolBehaviorArchive.GrassShakeRadiusField, "6.25"),
            ]);

        Assert.Equal(3, result.Session.PendingEdits.Count);
        Assert.All(result.Session.PendingEdits, edit => Assert.Equal(entryId, edit.RecordId));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var entry = result.Workflow.Entries.Single(candidate => candidate.Index == 0);
        Assert.Equal("WaterDash", entry.Behavior);
        Assert.Equal("head", entry.ModelPart);
        Assert.Equal(6.25, entry.GrassShakeRadius);
    }

    [Fact]
    public void UpdateEntryFieldsRollsBackIncomingSessionAndWorkflowWhenOneFieldIsInvalid()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var firstEntryId = LoadEntryId(temp, index: 0);
        var secondEntryId = LoadEntryId(temp, index: 1);
        var existing = service.UpdateEntryField(
            temp.Paths,
            session: null,
            secondEntryId,
            SwShSymbolBehaviorArchive.BehaviorField,
            "Common");
        var originalEdit = Assert.Single(existing.Session.PendingEdits);

        var result = service.UpdateEntryFields(
            temp.Paths,
            existing.Session,
            [
                new(firstEntryId, SwShSymbolBehaviorArchive.ModelPartField, "head"),
                new(firstEntryId, SwShSymbolBehaviorArchive.BehaviorField, "NotAProfile"),
            ]);

        Assert.Equal(originalEdit, Assert.Single(result.Session.PendingEdits));
        Assert.Equal("body", result.Workflow.Entries.Single(entry => entry.Index == 0).ModelPart);
        Assert.Equal("Common", result.Workflow.Entries.Single(entry => entry.Index == 1).Behavior);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateEntryFieldsReturnsDiagnosticAndPreservesMalformedIncomingSession()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);
        var validSpeciesUpdate = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId,
            SwShSymbolBehaviorArchive.SpeciesIdField,
            "133");
        var validEdit = Assert.Single(validSpeciesUpdate.Session.PendingEdits);
        var malformedEdit = validEdit with { NewValue = "not-an-int" };
        var malformedSession = validSpeciesUpdate.Session with
        {
            PendingEdits = [malformedEdit],
        };

        var result = service.UpdateEntryFields(
            temp.Paths,
            malformedSession,
            [new(entryId, SwShSymbolBehaviorArchive.BehaviorField, "WaterDash")]);

        Assert.Equal(malformedSession.Id, result.Session.Id);
        Assert.Equal(malformedEdit, Assert.Single(result.Session.PendingEdits));
        var entry = result.Workflow.Entries.Single(candidate => candidate.Index == 0);
        Assert.Equal(25, entry.SpeciesId);
        Assert.Equal("Common", entry.Behavior);
        Assert.Equal(
            "not-an-int",
            entry.Fields.Single(field => field.Field == SwShSymbolBehaviorArchive.SpeciesIdField).Value);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == SwShSymbolBehaviorArchive.SpeciesIdField);
    }

    [Fact]
    public void UpdateEntryFieldsRejectsDuplicateCanonicalAndLegacyAliases()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryFields(
            temp.Paths,
            session: null,
            [
                new(entryId, SwShSymbolBehaviorArchive.BehaviorField, "WaterDash"),
                new("0", SwShSymbolBehaviorArchive.BehaviorField, "Common"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal("Common", result.Workflow.Entries.Single(entry => entry.Index == 0).Behavior);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("more than once", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsLegacyAndDuplicatePendingAliases()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var update = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var canonicalEdit = Assert.Single(update.Session.PendingEdits);
        var legacySession = update.Session with
        {
            PendingEdits = [canonicalEdit with { RecordId = "0" }],
        };
        var duplicateSession = update.Session with
        {
            PendingEdits = [canonicalEdit, canonicalEdit],
        };

        var legacyValidation = service.Validate(temp.Paths, legacySession);
        var duplicateValidation = service.Validate(temp.Paths, duplicateSession);

        Assert.False(legacyValidation.IsValid);
        Assert.Contains(legacyValidation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == "entryId");
        Assert.False(duplicateValidation.IsValid);
        Assert.Contains(duplicateValidation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("more than one", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateEntryFieldRejectsMismatchedSignedIdentity()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();

        var result = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: $"behavior:0:{new string('0', 64)}",
            field: SwShSymbolBehaviorArchive.BehaviorField,
            value: "WaterDash");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == "entryId");
    }

    [Fact]
    public void UpdateEntryFieldRemovesSourceEquivalentAndRevertedEdits()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var noOp = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId,
            SwShSymbolBehaviorArchive.BehaviorField,
            "Common");
        Assert.Empty(noOp.Session.PendingEdits);

        var changed = service.UpdateEntryField(
            temp.Paths,
            noOp.Session,
            entryId,
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        Assert.Single(changed.Session.PendingEdits);

        var reverted = service.UpdateEntryField(
            temp.Paths,
            changed.Session,
            entryId,
            SwShSymbolBehaviorArchive.BehaviorField,
            "Common");
        Assert.Empty(reverted.Session.PendingEdits);
        Assert.Equal("Common", reverted.Workflow.Entries.Single(entry => entry.Index == 0).Behavior);
    }

    [Fact]
    public void UpdateEntryFieldsValidatesTheFinalSpeciesAndFormTogether()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryFields(
            temp.Paths,
            session: null,
            [
                new(entryId, SwShSymbolBehaviorArchive.FormField, "1"),
                new(entryId, SwShSymbolBehaviorArchive.SpeciesIdField, "133"),
            ]);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var entry = result.Workflow.Entries.Single(candidate => candidate.Index == 0);
        Assert.Equal(133, entry.SpeciesId);
        Assert.Equal("Eevee", entry.SpeciesName);
        Assert.Equal(1, entry.Form);
        Assert.Equal(["0", "1"], entry.FormOptions.Select(option => option.Value));
    }

    [Theory]
    [InlineData(SwShSymbolBehaviorArchive.SpeciesIdField, "26")]
    [InlineData(SwShSymbolBehaviorArchive.FormField, "1")]
    public void UpdateEntryFieldRejectsUnavailableSpeciesOrImpossibleForm(string field, string value)
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryField(temp.Paths, null, entryId, field, value);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == field);
    }

    [Theory]
    [InlineData(SwShSymbolBehaviorArchive.BehaviorField, "NotAProfile")]
    [InlineData(SwShSymbolBehaviorArchive.ModelPartField, "tail")]
    [InlineData(SwShSymbolBehaviorArchive.BehaviorField, "Common\0WaterDash")]
    [InlineData(SwShSymbolBehaviorArchive.ModelPartField, "body\n")]
    public void UpdateEntryFieldRejectsUnknownOrUnsafeStringValues(string field, string value)
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryField(temp.Paths, null, entryId, field, value);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && (diagnostic.Field == field || diagnostic.Field == "value"));
    }

    [Theory]
    [InlineData(SwShSymbolBehaviorArchive.HitboxRadiusField)]
    [InlineData(SwShSymbolBehaviorArchive.GrassShakeRadiusField)]
    public void UpdateEntryFieldRejectsNegativeRadius(string field)
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);

        var result = service.UpdateEntryField(temp.Paths, null, entryId, field, "-0.01");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && (diagnostic.Field == field || diagnostic.Field == "value"));
    }

    [Fact]
    public void UpdateEntryFieldCanonicalizesFloat32Value()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);
        const string input = "0.10000000149011612";
        var expected = float.Parse(input, CultureInfo.InvariantCulture)
            .ToString("R", CultureInfo.InvariantCulture);

        var result = service.UpdateEntryField(
            temp.Paths,
            null,
            entryId,
            SwShSymbolBehaviorArchive.HitboxRadiusField,
            input);

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(expected, edit.NewValue);
        Assert.Equal(
            (double)float.Parse(input, CultureInfo.InvariantCulture),
            result.Workflow.Entries.Single(entry => entry.Index == 0).HitboxRadius);
    }

    [Fact]
    public void DirectValidationPlanningAndApplyRejectEmptySessionOwnership()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var session = EditSession.Start();

        var validation = service.Validate(temp.Paths, session);
        var plan = service.CreateChangePlan(temp.Paths, session);
        var apply = service.ApplyChangePlan(temp.Paths, session, ChangePlan.Empty(session.Id));

        Assert.False(validation.IsValid);
        Assert.Empty(plan.Writes);
        Assert.Empty(apply.WrittenFiles);
        Assert.All(
            [validation.Diagnostics, plan.Diagnostics, apply.Diagnostics],
            diagnostics => Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Domain == "workflow.behavior"));
    }

    [Fact]
    public void DirectValidationPlanningAndApplyRejectMixedSessionOwnership()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var update = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var foreignEdit = new PendingEdit(
            "workflow.items",
            "Foreign item edit",
            Array.Empty<ProjectFileReference>(),
            RecordId: "1",
            Field: "buyPrice",
            NewValue: "500");
        var mixedSession = update.Session with
        {
            PendingEdits = update.Session.PendingEdits.Append(foreignEdit).ToArray(),
        };

        var validation = service.Validate(temp.Paths, mixedSession);
        var plan = service.CreateChangePlan(temp.Paths, mixedSession);
        var apply = service.ApplyChangePlan(temp.Paths, mixedSession, reviewedPlan);

        Assert.False(validation.IsValid);
        Assert.Empty(plan.Writes);
        Assert.Empty(apply.WrittenFiles);
        Assert.All(
            [validation.Diagnostics, plan.Diagnostics, apply.Diagnostics],
            diagnostics => Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                    && diagnostic.Domain == "workflow.behavior"));
    }

    [Fact]
    public void ApplyChangePlanRejectsSingleChangedValueWithForgedOldSummary()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);
        var first = service.UpdateEntryField(
            temp.Paths,
            null,
            entryId,
            SwShSymbolBehaviorArchive.HitboxRadiusField,
            "2.5");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var reviewedEdit = Assert.Single(first.Session.PendingEdits);
        var forgedSession = first.Session with
        {
            PendingEdits =
            [
                reviewedEdit with
                {
                    NewValue = "3.5",
                    Summary = reviewedEdit.Summary,
                },
            ],
        };
        var currentPlan = service.CreateChangePlan(temp.Paths, forgedSession);

        Assert.Equal(
            Assert.Single(reviewedPlan.Writes).SourceFingerprint,
            Assert.Single(currentPlan.Writes).SourceFingerprint);
        Assert.NotEqual(reviewedPlan.Writes[0].Reason, currentPlan.Writes[0].Reason);

        var apply = service.ApplyChangePlan(temp.Paths, forgedSession, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(GetOutputBehaviorPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyChangePlanRejectsChangedValueInSameEntryMultiEditSession()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var entryId = LoadEntryId(temp, index: 0);
        var staged = service.UpdateEntryFields(
            temp.Paths,
            session: null,
            [
                new(entryId, SwShSymbolBehaviorArchive.HitboxRadiusField, "2.5"),
                new(entryId, SwShSymbolBehaviorArchive.GrassShakeRadiusField, "6.25"),
            ]);
        var reviewedPlan = service.CreateChangePlan(temp.Paths, staged.Session);
        Assert.Equal(2, staged.Session.PendingEdits.Count);
        Assert.Single(staged.Session.PendingEdits.Select(edit => edit.RecordId).Distinct(StringComparer.Ordinal));
        var forgedEdits = staged.Session.PendingEdits
            .Select(edit => edit.Field == SwShSymbolBehaviorArchive.HitboxRadiusField
                ? edit with { NewValue = "3.5", Summary = edit.Summary }
                : edit)
            .ToArray();
        var forgedSession = staged.Session with { PendingEdits = forgedEdits };
        var currentPlan = service.CreateChangePlan(temp.Paths, forgedSession);
        var reviewedWrite = Assert.Single(reviewedPlan.Writes);
        var currentWrite = Assert.Single(currentPlan.Writes);

        Assert.Equal(reviewedWrite.SourceFingerprint, currentWrite.SourceFingerprint);
        Assert.Equal(
            reviewedWrite.Sources.Select(source => (source.Layer, source.RelativePath)),
            currentWrite.Sources.Select(source => (source.Layer, source.RelativePath)));
        Assert.NotEqual(reviewedWrite.Reason, currentWrite.Reason);

        var apply = service.ApplyChangePlan(temp.Paths, forgedSession, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(GetOutputBehaviorPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyChangePlanRejectsSourceDrift()
    {
        using var temp = CreateEditableProject();
        var service = new SwShBehaviorEditSessionService();
        var update = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(reviewedPlan.Writes).SourceFingerprint));
        var changedSource = SwShBehaviorTestFixtures.CreateBehaviorArchive().WriteEdits(
        [
            new SwShSymbolBehaviorEdit(
                1,
                SwShSymbolBehaviorArchive.GrassShakeRadiusField,
                "8.5"),
        ]);
        File.WriteAllBytes(GetBaseBehaviorPath(temp), changedSource);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.False(File.Exists(GetOutputBehaviorPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SequentialLayeredAppliesPreserveTheFirstEdit()
    {
        using var temp = CreateEditableProject();
        var workspace = new ProjectWorkspaceService();
        var service = new SwShBehaviorEditSessionService(workspace);
        var first = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var firstApply = service.ApplyChangePlan(
            temp.Paths,
            first.Session,
            service.CreateChangePlan(temp.Paths, first.Session));
        Assert.Single(firstApply.WrittenFiles);

        workspace.ClearMemoryCache();
        var layeredEntryId = new SwShBehaviorWorkflowService()
            .Load(workspace.Open(temp.Paths))
            .Entries
            .Single(entry => entry.Index == 0)
            .EntryId;
        var second = service.UpdateEntryField(
            temp.Paths,
            null,
            layeredEntryId,
            SwShSymbolBehaviorArchive.ModelPartField,
            "head");
        var secondApply = service.ApplyChangePlan(
            temp.Paths,
            second.Session,
            service.CreateChangePlan(temp.Paths, second.Session));

        Assert.Single(secondApply.WrittenFiles);
        var output = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(GetOutputBehaviorPath(temp)));
        Assert.Equal("WaterDash", output.Entries[0].Behavior);
        Assert.Equal("head", output.Entries[0].ModelPart);
        Assert.Equal("WaterDash", output.Entries[1].Behavior);
    }

    [Fact]
    public void FailedTemporaryWritePreservesExistingOutput()
    {
        using var temp = CreateEditableProject();
        var originalOutput = SwShBehaviorTestFixtures.CreateBehaviorArchive().Write();
        temp.WriteOutputFile(SwShBehaviorWorkflowService.BehaviorDataPath, originalOutput);
        var service = new SwShBehaviorEditSessionService(
            (_, _) => throw new IOException("Injected Behavior write failure."));
        var update = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(originalOutput, File.ReadAllBytes(GetOutputBehaviorPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("Injected Behavior write failure", StringComparison.Ordinal));
    }

    [Fact]
    public void TemporaryOutputVerificationFailurePreservesExistingOutput()
    {
        using var temp = CreateEditableProject();
        var originalOutput = SwShBehaviorTestFixtures.CreateBehaviorArchive().Write();
        temp.WriteOutputFile(SwShBehaviorWorkflowService.BehaviorDataPath, originalOutput);
        var service = new SwShBehaviorEditSessionService(
            (path, _) => File.WriteAllBytes(path, [0]));
        var update = service.UpdateEntryField(
            temp.Paths,
            null,
            LoadEntryId(temp, index: 0),
            SwShSymbolBehaviorArchive.BehaviorField,
            "WaterDash");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(originalOutput, File.ReadAllBytes(GetOutputBehaviorPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("verification failed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(GetOutputBehaviorPath(temp))!),
            path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredBehaviorData()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var source = SwShBehaviorTestFixtures.CreateBehaviorArchive();
        var service = new SwShBehaviorEditSessionService();
        var behaviorUpdate = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.BehaviorField,
            value: "WaterDash");
        var radiusUpdate = service.UpdateEntryField(
            temp.Paths,
            behaviorUpdate.Session,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.HitboxRadiusField,
            value: "9.5");

        var validation = service.Validate(temp.Paths, radiusUpdate.Session);
        var plan = service.CreateChangePlan(temp.Paths, radiusUpdate.Session);
        var apply = service.ApplyChangePlan(temp.Paths, radiusUpdate.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShBehaviorWorkflowService.BehaviorDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShBehaviorWorkflowService.BehaviorDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "field",
            "param",
            "symbol_encount_mons_param",
            "symbol_encount_mons_param.bin");
        var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal("WaterDash", archive.Entries[0].Behavior);
        Assert.Equal(9.5f, archive.Entries[0].HitboxRadius);
        Assert.Equal("WaterDash", archive.Entries[1].Behavior);
        Assert.All(
            archive.Entries[1].Fields,
            field => Assert.Equal(
                source.Entries[1].Fields.Single(candidate => candidate.Field == field.Field).Value,
                field.Value));
        Assert.All(
            archive.Entries[0].Fields.Where(field => field.Field is not (
                SwShSymbolBehaviorArchive.BehaviorField
                or SwShSymbolBehaviorArchive.HitboxRadiusField)),
            field => Assert.Equal(
                source.Entries[0].Fields.Single(candidate => candidate.Field == field.Field).Value,
                field.Value));
    }

    [Fact]
    public void UpdateEntryFieldRejectsReadOnlyUnknownFields()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShBehaviorEditSessionService();

        var result = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.Hash1Field,
            value: "123");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.behavior"
                && diagnostic.Expected == "Editable Behavior field");
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }

    private static string LoadEntryId(TemporarySwShProject temp, int index)
    {
        var workspace = new ProjectWorkspaceService();
        return new SwShBehaviorWorkflowService()
            .Load(workspace.Open(temp.Paths))
            .Entries
            .Single(entry => entry.Index == index)
            .EntryId;
    }

    private static string GetBaseBehaviorPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.BaseRomFsPath,
            SwShBehaviorWorkflowService.BehaviorDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetOutputBehaviorPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            SwShBehaviorWorkflowService.BehaviorDataPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
