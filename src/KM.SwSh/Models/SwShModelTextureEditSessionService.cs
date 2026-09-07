// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.Models;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Items;

namespace KM.SwSh.Models;

public sealed class SwShModelTextureEditSessionService
{
    public EditSession RestoreVanilla(ProjectPaths paths, EditSession? session, string model)
    {
        var project = Open(paths);
        var service = new SwShModelPreviewService();
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
                [new(ProjectFileLayer.Base, "romfs/" + Target(paths, original))], asset.Id, "recolor", intent.Serialize(), ModelTextureIntent.Domain));
        }
        if (edits.Count(e => e.Domain == ModelTextureIntent.Domain) > 2048) throw new InvalidDataException("Too many model asset edits.");
        return current with { PendingEdits = edits };
    }

    public EditSession Stage(ProjectPaths paths, EditSession? session, ModelTextureIntent intent)
    {
        var project = Open(paths); intent.Validate();
        if (intent.Game != paths.SelectedGame.ToString()) throw new InvalidDataException("Model asset edit belongs to another game.");
        if (intent.Kind == "restore") throw new InvalidDataException("Use Restore Vanilla for complete model restoration.");
        var service = new SwShModelPreviewService();
        var resource = (intent.Kind == "texture" ? service.Textures(project, intent.Model) : service.Assets(project, intent.Model)).SingleOrDefault(t => t.Id == intent.Texture)
            ?? throw new InvalidDataException("Select an asset belonging to this model.");
        var encoded = ModelTextureEncodingCache.Encode(resource.Bytes, intent);
        var current = session ?? EditSession.Start();
        var remaining = current.PendingEdits.Where(e => e.Domain != ModelTextureIntent.Domain || e.RecordId != intent.Texture).ToList();
        if (encoded.ChangedPixels > 0)
        {
            var target = Target(paths, resource);
            var bound = intent with { EncodedHash = ModelTextureIntent.Hash(encoded.Bytes) };
            remaining.Add(new(ModelTextureIntent.Domain, $"{(intent.Kind == "material" ? "Edit materials in" : "Recolor")} {Path.GetFileName(intent.Texture)}", [new(ProjectFileLayer.Base, "romfs/" + target)],
                intent.Texture, "recolor", bound.Serialize(), ModelTextureIntent.Domain));
        }
        if (remaining.Count(e => e.Domain == ModelTextureIntent.Domain) > 2048) throw new InvalidDataException("Too many staged model edits.");
        return current with { PendingEdits = remaining };
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        try { _ = Outputs(paths, session); return new(session, true, []); }
        catch (Exception ex) when (Expected(ex)) { return new(session, false, [Diagnostic("Model asset edits could not be validated. Reload the model and stage them again.")]); }
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        try
        {
            var outputs = Outputs(paths, session);
            if (outputs.Count == 0) return new(session.Id, [], [Diagnostic("Stage a model change before reviewing.")]);
            var writes = outputs.Select(pair => new PlannedFileWrite(pair.Key, [new(ProjectFileLayer.Base, pair.Key)],
                File.Exists(Path.Combine(paths.OutputRootPath!, pair.Key)), "Apply verified model changes.",
                ModelTextureIntent.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", session.PendingEdits.Select(e => e.NewValue)))))).ToArray();
            return SwShChangePlanSourceGuard.CaptureBounded(paths, new(session.Id, writes, []) { EffectivePendingEdits = session.PendingEdits },
                64L * 1024 * 1024, 128L * 1024 * 1024);
        }
        catch (Exception ex) when (Expected(ex)) { return new(session.Id, [], [Diagnostic("Model asset output could not be prepared. Reload and review the edits again.")]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed)
    {
        var current = CreateChangePlan(paths, session);
        var diagnostics = current.Diagnostics.ToList(); var written = new List<ProjectFileReference>();
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        ApplyResult Result() => new(id, now, written, new(id, now, current.Writes), diagnostics);
        if (!ChangePlanReview.Matches(reviewed, current)) diagnostics.Add(Diagnostic("Model review is stale. Review the changes again before applying."));
        diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewed));
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result();
        if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, current, out var scope, out var scopeDiagnostics))
        {
            diagnostics.AddRange(scopeDiagnostics); return Result();
        }
        using var verified = scope!;
        try
        {
            var outputs = Outputs(verified.ApplyPaths, session);
            foreach (var (path, bytes) in outputs)
            {
                var target = SwShOutputRollbackScope.ResolvePhysicalContainedPath(verified.ApplyPaths.OutputRootPath, path)
                    ?? throw new InvalidDataException("Model asset output target is invalid.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Model asset output verification failed.");
                written.Add(new(ProjectFileLayer.Generated, path));
            }
        }
        catch (Exception ex) when (Expected(ex)) { diagnostics.Add(Diagnostic("Model asset output failed. Check output recovery before retrying.")); }
        return verified.Commit(Result()).Complete(current);
    }

    private static Dictionary<string, byte[]> Outputs(ProjectPaths paths, EditSession session)
    {
        var project = Open(paths);
        if (session.PendingEdits.Count is < 1 or > 2048) throw new InvalidDataException("Model edit count is invalid.");
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var packs = new Dictionary<string, SwShGfPackFile>(StringComparer.Ordinal);
        var resources = new Dictionary<string, ModelTextureResource[]>(StringComparer.Ordinal);
        var assetModels = session.PendingEdits.Select(e => ModelTextureIntent.Parse(e.NewValue)).Where(i => i.Kind != "texture").Select(i => i.Model).ToHashSet(StringComparer.Ordinal);
        var vanilla = new Dictionary<string, ModelTextureResource[]>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var source = new SwShModelSource(project);
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != ModelTextureIntent.Domain || edit.Owner != ModelTextureIntent.Domain || edit.Field != "recolor") throw new InvalidDataException("Model asset edit is unsupported.");
            var intent = ModelTextureIntent.Parse(edit.NewValue);
            if (intent.Game != paths.SelectedGame.ToString() || edit.RecordId != intent.Texture || intent.EncodedHash == "" || !seen.Add(intent.Texture))
                throw new InvalidDataException("Model asset edit binding is invalid or duplicated.");
            if (!resources.TryGetValue(intent.Model, out var models)) resources.Add(intent.Model, models = assetModels.Contains(intent.Model) ? new SwShModelPreviewService().Assets(project, intent.Model) : new SwShModelPreviewService().Textures(project, intent.Model));
            var resource = models.SingleOrDefault(t => t.Id == intent.Texture) ?? throw new InvalidDataException("Asset is no longer associated with the model.");
            byte[]? baseBytes = null;
            if (intent.Kind == "restore")
            {
                if (!vanilla.TryGetValue(intent.Model, out var bases)) vanilla.Add(intent.Model, bases = new SwShModelPreviewService().Assets(project, intent.Model, true));
                baseBytes = bases.Single(a => a.Id == intent.Texture).Bytes;
            }
            var result = ModelTextureEncodingCache.Encode(resource.Bytes, intent, baseBytes);
            if (result.ChangedPixels == 0) throw new InvalidDataException("Model asset edit has no changes.");
            var target = Target(paths, resource);
            if (target == resource.Id) outputs.Add("romfs/" + target, result.Bytes);
            else
            {
                if (!packs.TryGetValue(target, out var pack))
                {
                    var bytes = source.Read(target);
                    _ = SwShGfPackFile.ParseBoundedReadOnly(bytes, 4096, 128L * 1024 * 1024);
                    packs.Add(target, pack = SwShGfPackFile.Parse(bytes));
                }
                pack.SetFileByName(Path.GetFileName(resource.Id), result.Bytes);
            }
        }
        foreach (var (path, pack) in packs)
        {
            var bytes = pack.Write();
            var readback = SwShGfPackFile.ParseBoundedReadOnly(bytes, 4096, 128L * 1024 * 1024);
            foreach (var edit in session.PendingEdits)
            {
                var intent = ModelTextureIntent.Parse(edit.NewValue);
                var resource = resources[intent.Model].Single(t => t.Id == intent.Texture);
                if (Target(paths, resource) == path && ModelTextureIntent.Hash(readback.GetFileByName(Path.GetFileName(resource.Id))) != intent.EncodedHash)
                    throw new InvalidDataException("Packed model asset verification failed.");
            }
            outputs.Add("romfs/" + path, bytes);
        }
        source.Verify();
        if (outputs.Values.Sum(b => (long)b.Length) > 128L * 1024 * 1024) throw new InvalidDataException("Model asset output exceeds the editing budget.");
        return outputs;
    }
    private static string Target(ProjectPaths paths, ModelTextureResource resource)
    {
        if (File.Exists(Path.Combine(paths.OutputRootPath!, "romfs", resource.Id)) || File.Exists(Path.Combine(paths.BaseRomFsPath!, resource.Id))) return resource.Id;
        return resource.Archive ?? throw new InvalidDataException("Model asset archive is missing.");
    }
    private static OpenedProject Open(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)) throw new InvalidDataException("Select a Sword or Shield project.");
        var project = new ProjectWorkspaceService().ValidateAndOpen(paths);
        if (!project.Health.CanOpenEditableWorkflows) throw new InvalidDataException("Project paths must support editing.");
        return project;
    }
    private static bool Expected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.Text.Json.JsonException;
    private static ValidationDiagnostic Diagnostic(string message) => new(DiagnosticSeverity.Error, message, Domain: ModelTextureIntent.Domain) { Code = "KM-MODEL-ASSET-EDIT-INVALID" };
}
