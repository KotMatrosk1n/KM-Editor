// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.Models;
using KM.ZA.Workflows;

namespace KM.ZA.Models;

public sealed class ZaModelTextureEditSessionService
{
    public EditSession Stage(ProjectPaths paths, EditSession? session, ModelTextureIntent intent)
    {
        var project = Open(paths);
        intent.Validate();
        if (intent.Game != paths.SelectedGame.ToString()) throw new InvalidDataException("Texture edit belongs to another game.");
        var resource = new ZaModelPreviewService().Textures(project, intent.Model).SingleOrDefault(t => t.Id == intent.Texture)
            ?? throw new InvalidDataException("Select a color texture belonging to this model.");
        var encoded = ModelTextureEncodingCache.Encode(resource.Bytes, intent);
        var current = session ?? EditSession.Start();
        var remaining = current.PendingEdits.Where(e => e.Domain != ModelTextureIntent.Domain || e.RecordId != intent.Texture).ToList();
        if (encoded.ChangedPixels > 0)
        {
            var bound = intent with { EncodedHash = ModelTextureIntent.Hash(encoded.Bytes) };
            remaining.Add(new(ModelTextureIntent.Domain, $"Recolor {Path.GetFileName(intent.Texture)}", [new(ProjectFileLayer.Base, "romfs/" + intent.Texture)],
                intent.Texture, "recolor", bound.Serialize(), ModelTextureIntent.Domain));
        }
        if (remaining.Count(e => e.Domain == ModelTextureIntent.Domain) > 32) throw new InvalidDataException("Too many staged texture edits.");
        return current with { PendingEdits = remaining };
    }

    public ZaEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        try { _ = Outputs(paths, session); return new(session, true, []); }
        catch (Exception ex) when (Expected(ex)) { return new(session, false, [Diagnostic("Texture edits could not be validated. Reload the model and stage them again.")]); }
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session, ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        try
        {
            var outputs = Outputs(paths, session);
            if (outputs.Count == 0) return new(session.Id, [], [Diagnostic("Stage a texture color change before reviewing.")]);
            var writes = outputs.Keys.Order(StringComparer.Ordinal).Select(path => ZaWorkflowFileSource.CreatePlannedWrite(paths, path,
                [new(ProjectFileLayer.Base, "romfs/" + path)], outputMode)).ToList();
            if (outputMode == ZaOutputMode.Standalone) writes.Add(ZaWorkflowFileSource.CreateDescriptorPlannedWrite(paths));
            return ZaChangePlanSourceGuard.Capture(paths, session, () => new(session.Id,
                writes.Select(write => new PlannedFileWrite(write.TargetRelativePath, write.Sources, write.ReplacesExistingOutput, "Apply verified texture colors.")).ToArray(), []), outputMode);
        }
        catch (Exception ex) when (Expected(ex)) { return new(session.Id, [], [Diagnostic("Texture output could not be prepared. Reload and review the edits again.")]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed, ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        using var outputLock = ZaWorkflowFileSource.AcquireOutputLock(paths);
        var current = CreateChangePlan(paths, session, outputMode);
        var diagnostics = current.Diagnostics.ToList(); var written = new List<ProjectFileReference>();
        OutputApplyResult? outputTransaction = null;
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Diagnostic("Texture review is stale. Review the changes again before applying."));
        if (diagnostics.Count == 0)
        {
            try
            {
                var outputs = Outputs(paths, session);
                var context = new ZaOutputApplyContext(OutputReviewFingerprint.FromChangePlan(current),
                    new OwnershipOwnerId("workflow.za.output"), [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, ModelTextureIntent.Domain)]);
                outputTransaction = ZaWorkflowFileSource.WriteBatch(paths, outputs.Select(pair => new ZaWorkflowFileWrite(pair.Key, pair.Value, context)).ToArray(),
                    outputMode, applyContext: context, revalidateReviewedState: () => ChangePlanReview.Matches(reviewed, CreateChangePlan(paths, session, outputMode)));
                foreach (var (path, bytes) in outputs)
                {
                    _ = new ModelTextureDocument(bytes);
                    written.Add(ZaEditSessionSupport.GeneratedReference(path, outputMode));
                }
                if (outputMode == ZaOutputMode.Standalone) written.Add(ZaEditSessionSupport.GeneratedReference(ZaWorkflowFileSource.DescriptorVirtualPath, outputMode));
            }
            catch (Exception ex) when (Expected(ex)) { diagnostics.Add(Diagnostic("Texture output failed. Check output recovery before retrying.")); }
        }
        return new ApplyResult(id, now, written, new(id, now, current.Writes), diagnostics, outputTransaction).Complete(current);
    }

    private static Dictionary<string, byte[]> Outputs(ProjectPaths paths, EditSession session)
    {
        var project = Open(paths);
        if (session.PendingEdits.Count is < 1 or > 32) throw new InvalidDataException("Texture edit count is invalid.");
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var resources = new Dictionary<string, ModelTextureResource[]>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != ModelTextureIntent.Domain || edit.Owner != ModelTextureIntent.Domain || edit.Field != "recolor") throw new InvalidDataException("Texture edit is unsupported.");
            var intent = ModelTextureIntent.Parse(edit.NewValue);
            if (intent.Game != paths.SelectedGame.ToString() || edit.RecordId != intent.Texture || intent.EncodedHash == "") throw new InvalidDataException("Texture edit binding is invalid.");
            if (!resources.TryGetValue(intent.Model, out var models)) resources.Add(intent.Model, models = new ZaModelPreviewService().Textures(project, intent.Model));
            var resource = models.SingleOrDefault(t => t.Id == intent.Texture) ?? throw new InvalidDataException("Texture is no longer associated with the model.");
            var result = ModelTextureEncodingCache.Encode(resource.Bytes, intent);
            if (result.ChangedPixels == 0 || !outputs.TryAdd(intent.Texture, result.Bytes)) throw new InvalidDataException("Texture edits conflict or have no changes.");
            bytes += result.Bytes.Length;
            if (bytes > 64L * 1024 * 1024) throw new InvalidDataException("Texture output exceeds the editing budget.");
        }
        return outputs;
    }
    private static OpenedProject Open(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.ZA)) throw new InvalidDataException("Select a Legends Z-A project.");
        var project = new ProjectWorkspaceService().ValidateAndOpen(paths);
        if (!project.Health.CanOpenEditableWorkflows) throw new InvalidDataException("Project paths must support editing.");
        return project;
    }
    private static bool Expected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.Text.Json.JsonException;
    private static ValidationDiagnostic Diagnostic(string message) => new(DiagnosticSeverity.Error, message, Domain: ModelTextureIntent.Domain) { Code = "KM-MODEL-TEXTURE-EDIT-INVALID" };
}
