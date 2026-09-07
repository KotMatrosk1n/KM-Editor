// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.Models;
using KM.SV.Workflows;

namespace KM.SV.Models;

public sealed class SvModelTextureEditSessionService
{
    public EditSession RestoreVanilla(ProjectPaths paths, EditSession? session, string model)
    {
        var project = Open(paths);
        var service = new SvModelPreviewService();
        var vanilla = service.Assets(project, model, true);
        var currentAssets = service.Assets(project, model).ToDictionary(a => a.Id, StringComparer.Ordinal);
        var ids = vanilla.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var current = session ?? EditSession.Start();
        var edits = current.PendingEdits.Where(e => e.Domain != ModelTextureIntent.Domain || e.RecordId is null || !ids.Contains(e.RecordId)).ToList();
        foreach (var asset in vanilla)
        {
            var original = currentAssets[asset.Id];
            if (original.Bytes.AsSpan().SequenceEqual(asset.Bytes)) continue;
            var intent = new ModelTextureIntent(paths.SelectedGame.ToString()!, model, asset.Id,
                ModelTextureIntent.Hash(original.Bytes), [], ModelTextureIntent.Hash(asset.Bytes), "restore");
            edits.Add(new(ModelTextureIntent.Domain, $"Restore {Path.GetFileName(asset.Id)}",
                [new(ProjectFileLayer.Base, "romfs/" + asset.Id)], asset.Id, "recolor", intent.Serialize(), ModelTextureIntent.Domain));
        }
        if (edits.Count(e => e.Domain == ModelTextureIntent.Domain) > 2048) throw new InvalidDataException("Too many model asset edits.");
        return current with { PendingEdits = edits };
    }

    public EditSession Stage(ProjectPaths paths, EditSession? session, ModelTextureIntent intent)
    {
        var project = Open(paths);
        intent.Validate();
        if (intent.Game != paths.SelectedGame.ToString()) throw new InvalidDataException("Model asset edit belongs to another game.");
        if (intent.Kind == "restore") throw new InvalidDataException("Use Restore Vanilla for complete model restoration.");
        var service = new SvModelPreviewService();
        var resource = (intent.Kind == "texture" ? service.Textures(project, intent.Model) : service.Assets(project, intent.Model)).SingleOrDefault(t => t.Id == intent.Texture)
            ?? throw new InvalidDataException("Select an asset belonging to this model.");
        var encoded = ModelTextureEncodingCache.Encode(resource.Bytes, intent);
        var current = session ?? EditSession.Start();
        var remaining = current.PendingEdits.Where(e => e.Domain != ModelTextureIntent.Domain || e.RecordId != intent.Texture).ToList();
        if (encoded.ChangedPixels > 0)
        {
            var bound = intent with { EncodedHash = ModelTextureIntent.Hash(encoded.Bytes) };
            remaining.Add(new(ModelTextureIntent.Domain, $"{(intent.Kind == "material" ? "Edit materials in" : "Recolor")} {Path.GetFileName(intent.Texture)}", [new(ProjectFileLayer.Base, "romfs/" + intent.Texture)],
                intent.Texture, "recolor", bound.Serialize(), ModelTextureIntent.Domain));
        }
        if (remaining.Count(e => e.Domain == ModelTextureIntent.Domain) > 2048) throw new InvalidDataException("Too many staged model edits.");
        return current with { PendingEdits = remaining };
    }

    public SvEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        try { _ = Outputs(paths, session); return new(session, true, []); }
        catch (Exception ex) when (Expected(ex)) { return new(session, false, [Diagnostic("Model asset edits could not be validated. Reload the model and stage them again.")]); }
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        try
        {
            var outputs = Outputs(paths, session);
            if (outputs.Count == 0) return new(session.Id, [], [Diagnostic("Stage a model change before reviewing.")]);
            var writes = outputs.Keys.Order(StringComparer.Ordinal).Select(path => SvWorkflowFileSource.CreatePlannedWrite(paths, path,
                [new(ProjectFileLayer.Base, "romfs/" + path)], outputMode)).ToList();
            if (outputMode == SvOutputMode.Standalone) writes.Add(SvWorkflowFileSource.CreateDescriptorPlannedWrite(paths));
            return SvChangePlanSourceGuard.Capture(paths, session, new(session.Id,
                writes.Select(write => new PlannedFileWrite(write.TargetRelativePath, write.Sources, write.ReplacesExistingOutput, "Apply verified model changes.")).ToArray(), []), outputMode);
        }
        catch (Exception ex) when (Expected(ex)) { return new(session.Id, [], [Diagnostic("Model asset output could not be prepared. Reload and review the edits again.")]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        using var outputLock = SvWorkflowFileSource.AcquireOutputLock(paths);
        var current = CreateChangePlan(paths, session, outputMode);
        var diagnostics = current.Diagnostics.ToList(); var written = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Diagnostic("Model review is stale. Review the changes again before applying."));
        if (diagnostics.Count == 0)
        {
            try
            {
                var outputs = Outputs(paths, session);
                var context = new SvOutputApplyContext(OutputReviewFingerprint.FromChangePlan(current),
                    new OwnershipOwnerId("workflow.sv.model-textures"), [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, ModelTextureIntent.Domain)]);
                outputTransaction = SvWorkflowFileSource.WriteBatch(paths, outputs.Select(pair => new SvWorkflowFileWrite(pair.Key, pair.Value, context)).ToArray(),
                    outputMode, context, () => ChangePlanReview.Matches(reviewed, CreateChangePlan(paths, session, outputMode)));
                foreach (var (path, bytes) in outputs)
                {
                    var readback = SvWorkflowFileSource.ReadOutputBytesForVerification(paths, path, outputMode);
                    if (!readback.AsSpan().SequenceEqual(bytes)) throw new IOException("Model asset output did not match its verified encoding.");
                    // The exact replacement bytes were verified before output.
                    written.Add(SvEditSessionSupport.GeneratedReference(path, outputMode));
                }
                if (outputMode == SvOutputMode.Standalone) written.Add(SvEditSessionSupport.GeneratedReference(SvWorkflowFileSource.DescriptorVirtualPath, outputMode));
            }
            catch (Exception ex) when (Expected(ex)) { diagnostics.Add(Diagnostic("Model asset output failed. Check output recovery before retrying.")); }
        }
        return new ApplyResult(id, now, written, new(id, now, current.Writes), diagnostics, outputTransaction).Complete(current);
    }

    private static Dictionary<string, byte[]> Outputs(ProjectPaths paths, EditSession session)
    {
        var project = Open(paths);
        if (session.PendingEdits.Count is < 1 or > 2048) throw new InvalidDataException("Model edit count is invalid.");
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var resources = new Dictionary<string, ModelTextureResource[]>(StringComparer.Ordinal);
        var assetModels = session.PendingEdits.Select(e => ModelTextureIntent.Parse(e.NewValue)).Where(i => i.Kind != "texture").Select(i => i.Model).ToHashSet(StringComparer.Ordinal);
        var vanilla = new Dictionary<string, ModelTextureResource[]>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != ModelTextureIntent.Domain || edit.Owner != ModelTextureIntent.Domain || edit.Field != "recolor") throw new InvalidDataException("Model asset edit is unsupported.");
            var intent = ModelTextureIntent.Parse(edit.NewValue);
            if (intent.Game != paths.SelectedGame.ToString() || edit.RecordId != intent.Texture || intent.EncodedHash == "") throw new InvalidDataException("Model asset edit binding is invalid.");
            if (!resources.TryGetValue(intent.Model, out var models)) resources.Add(intent.Model, models = assetModels.Contains(intent.Model) ? new SvModelPreviewService().Assets(project, intent.Model) : new SvModelPreviewService().Textures(project, intent.Model));
            var resource = models.SingleOrDefault(t => t.Id == intent.Texture) ?? throw new InvalidDataException("Asset is no longer associated with the model.");
            byte[]? baseBytes = null;
            if (intent.Kind == "restore")
            {
                if (!vanilla.TryGetValue(intent.Model, out var bases)) vanilla.Add(intent.Model, bases = new SvModelPreviewService().Assets(project, intent.Model, true));
                baseBytes = bases.Single(a => a.Id == intent.Texture).Bytes;
            }
            var result = ModelTextureEncodingCache.Encode(resource.Bytes, intent, baseBytes);
            if (result.ChangedPixels == 0 || !outputs.TryAdd(intent.Texture, result.Bytes)) throw new InvalidDataException("Model asset edits conflict or have no changes.");
            bytes += result.Bytes.Length;
            if (bytes > 64L * 1024 * 1024) throw new InvalidDataException("Model asset output exceeds the editing budget.");
        }
        return outputs;
    }
    private static OpenedProject Open(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet)) throw new InvalidDataException("Select a Scarlet or Violet project.");
        var project = new ProjectWorkspaceService().ValidateAndOpen(paths);
        if (!project.Health.CanOpenEditableWorkflows) throw new InvalidDataException("Project paths must support editing.");
        return project;
    }
    private static bool Expected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.Text.Json.JsonException;
    private static ValidationDiagnostic Diagnostic(string message) => new(DiagnosticSeverity.Error, message, Domain: ModelTextureIntent.Domain) { Code = "KM-MODEL-ASSET-EDIT-INVALID" };
}
