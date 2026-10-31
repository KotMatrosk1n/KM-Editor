// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.ExeFs;
using KM.SwSh.Gifts;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.Formats.SwSh;

namespace KM.SwSh.CustomGifts;

public sealed class SwShCustomGiftService(ProjectWorkspaceService? workspace = null)
{
    public const string Domain = "workflow.customGifts";
    public const string SourceCode = "KM-SWSH-CUSTOM-GIFTS-SOURCE-INVALID";
    public const string CatalogCode = "KM-SWSH-CUSTOM-GIFTS-CATALOG-INVALID";
    public const string RecoveredCode = "KM-SWSH-CUSTOM-GIFTS-CATALOG-RECOVERED";
    public const string SessionCode = "KM-SWSH-CUSTOM-GIFTS-SESSION-INVALID";
    public const string StaleCode = "KM-SWSH-CUSTOM-GIFTS-PLAN-STALE";
    public const string ApplyCode = "KM-SWSH-CUSTOM-GIFTS-APPLY-FAILED";
    private readonly ProjectWorkspaceService workspace = workspace ?? new();

    public SwShCustomGiftWorkflow Load(ProjectPaths paths)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        return Workflow(state, diagnostics);
    }

    public SwShCustomGiftEditResult Stage(ProjectPaths paths, SwShCustomGiftCatalog catalog, EditSession? session)
    {
        var current = session ?? EditSession.Start();
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        if (state is not null && !HasErrors(diagnostics))
        {
            try
            {
                var normalized = Normalize(catalog, state.Metadata, state.PokemonCatalog);
                if (normalized.NextClaimId < state.Catalog.NextClaimId)
                    throw new InvalidDataException("Gift identities cannot be reused. Keep the current next identity when removing or reordering gifts.");
                var retained = current.PendingEdits.Where(edit => edit.Domain != Domain);
                current = current with { PendingEdits = retained.Append(CreateEdit(normalized)).ToArray() };
            }
            catch (Exception exception) when (SourceError(exception))
            { diagnostics.Add(Error(exception is InvalidDataException ? exception.Message : "The custom gift catalog is invalid.", CatalogCode)); }
        }
        return new(Workflow(state, diagnostics), current, diagnostics);
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        _ = Decode(session, state, diagnostics);
        return new(session, !HasErrors(diagnostics), diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var state = Read(paths, diagnostics);
        RequireEditable(state, diagnostics);
        var catalog = Decode(session, state, diagnostics);
        if (state is null || catalog is null || HasErrors(diagnostics)) return new(session.Id, [], diagnostics);
        try
        {
            var dependencies = Sources(state, paths).Concat(CreateEdit(catalog).Sources).ToArray();
            var writes = Outputs(state, catalog, paths.SelectedGame!.Value).Where(output => !Same(output.Before, output.After))
                .Select(output => new PlannedFileWrite(output.Path, dependencies, File.Exists(output.Target),
                    catalog.Mode == "none" ? "Remove the custom gift receiver and retain gift definitions."
                        : $"Install {(catalog.Mode == "royal" ? "Royal Gift" : "Mystery Gift")} as the sole custom gift receiver.")).ToArray();
            return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, diagnostics));
        }
        catch (Exception exception) when (SourceError(exception))
        { return new(session.Id, [], [Error("The custom gift output could not be prepared. Check sources and stage again.")]); }
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            var diagnostics = plan.Diagnostics.ToList();
            if (!ChangePlanReview.Matches(reviewedPlan, plan)) diagnostics.Add(Error("The gift plan changed. Review it again.", StaleCode));
            diagnostics.AddRange(SwShChangePlanSourceGuard.Validate(paths, reviewedPlan));
            if (HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out var failures)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [.. prepared.Diagnostics, Error("Gift sources changed. Review again.", StaleCode)]);
            var state = Read(verified.ApplyPaths, diagnostics);
            var catalog = Decode(session, state, diagnostics);
            if (state is null || catalog is null || HasErrors(diagnostics)) return Result(plan, [], diagnostics);
            var written = new List<ProjectFileReference>();
            foreach (var output in Outputs(state, catalog, paths.SelectedGame!.Value).Where(output => !Same(output.Before, output.After)))
            {
                if (output.After is null) File.Delete(output.Target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output.Target)!);
                    File.WriteAllBytes(output.Target, output.After);
                    if (!File.ReadAllBytes(output.Target).AsSpan().SequenceEqual(output.After)) throw new InvalidDataException();
                }
                written.Add(new(ProjectFileLayer.Generated, output.Path));
            }
            var after = Read(verified.ApplyPaths, diagnostics);
            if (after is null || after.InstalledMode != catalog.Mode || after.NeedsRepair || HasErrors(diagnostics)) throw new InvalidDataException();
            return verified.Commit(Result(prepared, written, diagnostics));
        }
        catch (Exception exception) when (SourceError(exception))
        { return Result(reviewedPlan, [], [Error("Custom gifts could not be applied. Check the output, then review again.", ApplyCode)]); }
        finally { workspace.ClearMemoryCache(); }
    }

    private State? Read(ProjectPaths paths, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)) throw new InvalidDataException();
            var project = workspace.Open(paths);
            if (!project.Health.CanOpenReadOnlyWorkflows) throw new InvalidDataException();
            var main = ResourceAt(paths, "exefs/main", required: true);
            var baseline = SwShCustomGiftModule.VerifyMain(main.Vanilla!, paths.SelectedGame.Value);
            if (!ReferenceEquals(main.Vanilla, main.Source))
            {
                var effective = SwShCustomGiftModule.VerifyMain(main.Source!, paths.SelectedGame.Value);
                SwShExeFsMainComparison.EnsureCompatibleBaseLayout(baseline, effective, "Custom Gifts");
            }
            var module = ResourceAt(paths, SwShCustomGiftModule.RelativePath);
            var metadata = ResourceAt(paths, SwShCustomGiftModule.CatalogPath);
            var installed = module.Source is null ? null : SwShCustomGiftModule.Read(module.Source, paths.SelectedGame.Value);
            var catalog = installed ?? SwShCustomGiftCatalog.Empty;
            var damagedDefinitions = false;
            if (metadata.Source is not null)
            {
                try
                {
                    catalog = SwShCustomGiftCatalogCodec.Deserialize(new UTF8Encoding(false, true).GetString(metadata.Source));
                    SwShCustomGiftCatalogCodec.Validate(catalog, legacy: catalog.RoyalPools is not null
                        && catalog.RoyalPools.All(pool => pool is not null && pool.Chances is null));
                }
                catch (Exception exception) when (installed is not null && catalog.Version == 1
                    && catalog.Mode is "none" or "mystery" or "royal" && SourceError(exception))
                {
                    catalog = installed;
                    damagedDefinitions = true;
                    diagnostics.Add(new(DiagnosticSeverity.Warning, "Gift definitions were recovered from the verified receiver. Stage and review to repair the catalog.",
                        File: SwShCustomGiftModule.CatalogPath, Domain: Domain, Field: "catalog") { Code = RecoveredCode });
                }
            }
            var giftMetadata = new SwShGiftPokemonWorkflowService().Load(project);
            if (giftMetadata.EditableFields.Count == 0) throw new InvalidDataException();
            var pokemonCatalog = SwShEffectivePokemonCatalog.Load(paths);
            if (!pokemonCatalog.Available) throw new InvalidDataException();
            var dependencies = pokemonCatalog.Sources.Select(source => source.RelativePath)
                .Concat(project.FileGraph.Entries.Where(entry => entry.RelativePath.StartsWith(SwShMoveDataFile.MoveDataRelativeDirectory + "/", StringComparison.Ordinal)
                    || entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.Ordinal)
                        && new[] { "/monsname.dat", "/itemname.dat", "/wazaname.dat", "/tokusei.dat" }.Any(name => entry.RelativePath.EndsWith(name, StringComparison.Ordinal))
                    || entry.RelativePath.StartsWith("romfs/bin/km-editor/restoration/", StringComparison.Ordinal))
                    .Select(entry => entry.RelativePath)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            diagnostics.AddRange(giftMetadata.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var repair = damagedDefinitions || catalog.RoyalPools!.Any(pool => pool.Chances is null)
                || installed is not null && !SwShCustomGiftModule.IsCurrent(module.Source!)
                || (installed?.Mode ?? "none") != catalog.Mode
                || installed is not null && SwShCustomGiftCatalogCodec.Serialize(installed) != SwShCustomGiftCatalogCodec.Serialize(catalog)
                || installed is not null && metadata.Source is null;
            return new(main, module, metadata, catalog, installed?.Mode ?? "none", repair, giftMetadata, pokemonCatalog, dependencies,
                project.Health.CanOpenEditableWorkflows && module.Target.Length > 0 && metadata.Target.Length > 0);
        }
        catch (Exception exception) when (SourceError(exception))
        { diagnostics.Add(Error("Custom gifts require readable original 1.3.2 sources and a compatible gift module and catalog.")); return null; }
    }

    private static SwShCustomGiftCatalog Normalize(SwShCustomGiftCatalog catalog, SwShGiftPokemonWorkflow metadata, SwShEffectivePokemonCatalog pokemonCatalog)
    {
        catalog = SwShCustomGiftCatalogOdds.WithChances(catalog);
        SwShCustomGiftCatalogCodec.Validate(catalog);
        SwShCustomGiftPokemon NormalizePokemon(SwShCustomGiftPokemon pokemon, bool active)
        {
            if (!active) return pokemon;
            if (!pokemonCatalog.Entries.Any(entry => entry.Identity.Species == pokemon.SpeciesId && entry.Identity.Form == pokemon.Form
                && entry.Capabilities.HasFlag(SwShPokemonCapability.Assignment)))
                throw new InvalidDataException("Every active gift Pokemon and form must have complete assignment and restoration resources.");
            var record = metadata.AbilityResolver.ResolvePersonalRecord(pokemon.SpeciesId, pokemon.Form)
                ?? throw new InvalidDataException("Every active gift Pokemon must be available in the project, including its form and restoration assets.");
            foreach (var (field, value) in new[] { ("species", pokemon.SpeciesId), ("heldItemId", pokemon.HeldItemId),
                ("ballItemId", pokemon.BallItemId), ("specialMoveId", pokemon.SpecialMoveId) })
            {
                var options = metadata.EditableFields.FirstOrDefault(candidate => candidate.Field == field)?.Options;
                if (options is null || options.Count == 0 || !options.Any(option => option.Value == value))
                    throw new InvalidDataException("Gift species, moves, balls, and held items must be available in the current project.");
            }
            var mask = 1 | (record.Ability2 != 0 ? 2 : 0) | (record.HiddenAbility != 0 ? 4 : 0);
            if (pokemon.Ability != 0 && (mask & 1 << (pokemon.Ability - 1)) == 0
                || pokemon.Gender == 1 && record.GenderRatio is 254 or 255
                || pokemon.Gender == 2 && record.GenderRatio is 0 or 255)
                throw new InvalidDataException("The selected ability slot or gender is unavailable for this Pokemon.");
            return pokemon with { GenderRatio = record.GenderRatio, AbilityMask = mask };
        }
        return catalog with
        {
            CodeGifts = catalog.CodeGifts.Select(gift => gift with { Pokemon = NormalizePokemon(gift.Pokemon, catalog.Mode == "mystery") }).ToArray(),
            RoyalPools = catalog.RoyalPools.Select(pool => pool with { Pokemon = pool.Pokemon.Select(pokemon => NormalizePokemon(pokemon, catalog.Mode == "royal")).ToArray() }).ToArray(),
        };
    }

    private static SwShCustomGiftCatalog? Decode(EditSession session, State? state, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            if (session.PendingEdits.Count != 1 || state is null) throw new InvalidDataException();
            var edit = session.PendingEdits[0];
            var catalog = SwShCustomGiftCatalogCodec.Deserialize(edit.NewValue!);
            var expected = CreateEdit(catalog);
            if (edit.Domain != expected.Domain || edit.RecordId != expected.RecordId || edit.Field != expected.Field
                || edit.NewValue != expected.NewValue || edit.Summary != expected.Summary || !edit.Sources.SequenceEqual(expected.Sources)
                || catalog.NextClaimId < state.Catalog.NextClaimId) throw new InvalidDataException();
            return Normalize(catalog, state.Metadata, state.PokemonCatalog);
        }
        catch (Exception exception) when (SourceError(exception))
        { diagnostics.Add(Error("The staged gift catalog is invalid. Stage it again.", SessionCode)); return null; }
    }

    private static Resource ResourceAt(ProjectPaths paths, string path, bool required = false)
    {
        var original = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path);
        var target = SwShHyperTrainingWorkflowService.ResolveOutputPath(paths, path) ?? "";
        if (Directory.Exists(target)) throw new InvalidDataException();
        byte[]? ReadBounded(string? file)
        {
            if (file is null || !File.Exists(file)) return null;
            if (new FileInfo(file).Length > (path == "exefs/main" ? 64L * 1024 * 1024 : 2L * 1024 * 1024)) throw new InvalidDataException();
            return File.ReadAllBytes(file);
        }
        var vanilla = ReadBounded(original);
        var output = ReadBounded(target);
        if (required && vanilla is null) throw new InvalidDataException();
        return new(path, vanilla, output, output ?? vanilla, target);
    }
    private static IEnumerable<ProjectFileReference> Sources(State state, ProjectPaths paths)
    {
        foreach (var path in new[] { state.Main.Path, "exefs/main.npdm", state.Module.Path, state.Definitions.Path, SwShPokemonWorkflowService.PersonalDataPath,
            SwShGiftPokemonWorkflowService.GiftPokemonDataPath }.Concat(state.Dependencies).Distinct(StringComparer.Ordinal))
        {
            var original = SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(paths, path);
            if (original is not null && File.Exists(original)) yield return new(ProjectFileLayer.Base, path);
            yield return new(ProjectFileLayer.Generated, path);
        }
    }
    private static IEnumerable<Output> Outputs(State state, SwShCustomGiftCatalog catalog, ProjectGame game)
    {
        yield return new(state.Module.Path, state.Module.Target, state.Module.Output,
            catalog.Mode == "none" && state.Module.Vanilla is null ? null : SwShCustomGiftModule.Write(catalog, game));
        yield return new(state.Definitions.Path, state.Definitions.Target, state.Definitions.Output,
            Encoding.UTF8.GetBytes(SwShCustomGiftCatalogCodec.Serialize(catalog)));
    }
    private static PendingEdit CreateEdit(SwShCustomGiftCatalog catalog)
    {
        var json = SwShCustomGiftCatalogCodec.Serialize(catalog);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new(Domain, "Stage custom gift catalog.", [new(ProjectFileLayer.Pending, $"pending/custom-gifts/catalog/{hash}")], "custom-gifts", "catalog", json);
    }
    private static SwShCustomGiftWorkflow Workflow(State? state, IReadOnlyList<ValidationDiagnostic> diagnostics) =>
        new(state?.CanEdit == true && !HasErrors(diagnostics), state?.InstalledMode ?? "none", state?.NeedsRepair == true,
            state?.Catalog ?? SwShCustomGiftCatalog.Empty, state?.Metadata.EditableFields ?? [], diagnostics);
    private static void RequireEditable(State? state, List<ValidationDiagnostic> diagnostics)
    { if (state is not null && !state.CanEdit) diagnostics.Add(Error("Configure an editable Output Root before staging custom gifts.")); }
    private static bool SourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
        or OverflowException or JsonException or KM.Core.Output.OutputCoordinatorException;
    private static bool HasErrors(IEnumerable<ValidationDiagnostic> diagnostics) => diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    private static ValidationDiagnostic Error(string message, string code = SourceCode) =>
        new(DiagnosticSeverity.Error, message, File: SwShCustomGiftModule.RelativePath, Domain: Domain, Field: "catalog") { Code = code };
    private static bool Same(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    { var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow; return new(id, now, written, new WriteManifest(id, now, plan.Writes), diagnostics); }
    private sealed record Resource(string Path, byte[]? Vanilla, byte[]? Output, byte[]? Source, string Target);
    private sealed record Output(string Path, string Target, byte[]? Before, byte[]? After);
    private sealed record State(Resource Main, Resource Module, Resource Definitions, SwShCustomGiftCatalog Catalog,
        string InstalledMode, bool NeedsRepair, SwShGiftPokemonWorkflow Metadata, SwShEffectivePokemonCatalog PokemonCatalog,
        IReadOnlyList<string> Dependencies, bool CanEdit);
}
