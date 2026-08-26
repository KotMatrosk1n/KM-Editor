// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Pokemon;
using KM.Core.Projects;
using KM.Formats.SV.Habitat;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;

namespace KM.SV.HabitatCoordinates;

public sealed class SvHabitatCoordinatesWorkflowService
{
    public const string WorkflowId = "habitatCoordinates";
    private const int MaximumSpeciesNameLength = 160;

    private readonly SvWorkflowFileSource fileSource;
    private readonly SvWorkflowFileSource labelFileSource;

    public SvHabitatCoordinatesWorkflowService()
        : this(new SvWorkflowFileSource(
            bypassReusableBaseCache: true,
            maximumReadBytes: SvHabitatDistributionDocument.MaximumSourceBytes),
            new SvWorkflowFileSource())
    {
    }

    internal SvHabitatCoordinatesWorkflowService(SvWorkflowFileSource fileSource)
        : this(fileSource, new SvWorkflowFileSource())
    {
    }

    internal SvHabitatCoordinatesWorkflowService(
        SvWorkflowFileSource fileSource,
        SvWorkflowFileSource labelFileSource)
    {
        this.fileSource = fileSource;
        this.labelFileSource = labelFileSource;
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SvWorkflowSupport.CreateSummary(
            project,
            WorkflowId,
            "Habitat Coordinates",
            "Edit existing Pokedex distribution cells with coordinates observed in each exact region source.");
    }

    public SvHabitatCoordinatesWorkflow Load(
        OpenedProject project,
        SvHabitatCoordinatesQuery? query = null,
        EditSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        SvHabitatCoordinatesQuery normalizedQuery;
        try
        {
            normalizedQuery = SvHabitatCoordinatesProfiles.NormalizeQuery(query);
        }
        catch (ArgumentException exception)
        {
            normalizedQuery = SvHabitatCoordinatesProfiles.NormalizeQuery(null);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                exception.Message,
                field: "query",
                expected: "Bounded habitat region search and page range",
                code: SvHabitatCoordinatesDiagnosticCodes.QueryInvalid));
        }

        if (!SvWorkflowFileSource.IsScarletViolet(project.Paths.SelectedGame))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Habitat Coordinates requires a Pokemon Scarlet or Pokemon Violet project.",
                expected: "Scarlet or Violet project",
                code: SvHabitatCoordinatesDiagnosticCodes.ProjectUnsupported));
            return CreateBlockedWorkflow(summary, normalizedQuery, diagnostics);
        }

        var build = SvHabitatCoordinatesProfiles.InspectBuild(project.Paths);
        if (!build.IsSupported)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                build.Message,
                file: "exefs/main",
                expected: "Exact selected-edition Scarlet/Violet 4.0.0 build ID",
                code: SvHabitatCoordinatesDiagnosticCodes.BuildUnsupported));
        }

        var loadedRegions = new Dictionary<string, SvHabitatLoadedRegion>(StringComparer.Ordinal);
        var states = new List<SvHabitatRegionState>(SvHabitatCoordinatesProfiles.Regions.Count);
        foreach (var profile in SvHabitatCoordinatesProfiles.Regions)
        {
            try
            {
                var loaded = LoadRegion(project, profile);
                loadedRegions[profile.Region] = loaded;
                states.Add(ToRegionState(
                    loaded,
                    build.IsSupported && summary.Availability == SvWorkflowAvailability.Available));
            }
            catch (Exception exception) when (IsSourceFailure(exception))
            {
                var code = IsUnavailableSource(exception)
                    ? SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnavailable
                    : SvHabitatCoordinatesDiagnosticCodes.RegionSourceUnsupported;
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"{profile.Label} habitat data could not be loaded safely: {exception.Message}",
                    file: $"romfs/{profile.SourceFile}",
                    expected: "Exact supported Scarlet/Violet 4.0.0 distribution source or canonical KM output",
                    code: code));
                states.Add(BlockedRegion(profile));
            }
        }

        var labels = SvTextLabelLookup.None();
        try
        {
            labels = SvTextLabelLookup.LoadPokemonNames(
                project,
                labelFileSource,
                diagnostics,
                project.Paths);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Pokemon names could not be loaded from the selected game language. Built-in names will be used: {exception.Message}"));
        }

        var page = CreatePage(
            normalizedQuery,
            loadedRegions.GetValueOrDefault(normalizedQuery.Region),
            session,
            labels);
        return new SvHabitatCoordinatesWorkflow(
            summary,
            SvHabitatCoordinatesProfiles.SupportedBuildLabel,
            build.DetectedBuildId,
            states,
            page,
            new SvHabitatCoordinatesStats(
                states.Count,
                states.Count(state => state.CanStage),
                states.Sum(state => state.RowCount),
                states.Sum(state => state.SemanticIdentityCount)),
            diagnostics);
    }

    internal SvHabitatLoadedRegion LoadRegion(OpenedProject project, string region)
    {
        return LoadRegion(project, SvHabitatCoordinatesProfiles.ResolveRegion(region));
    }

    internal SvHabitatLoadedRegion LoadRegion(
        OpenedProject project,
        SvHabitatRegionProfile profile)
    {
        var baseFile = fileSource.ReadBase(project, profile.SourceFile);
        if (baseFile.Bytes.Length > SvHabitatDistributionDocument.MaximumSourceBytes)
        {
            throw new InvalidDataException("The habitat base source exceeds its bounded byte limit.");
        }

        var baseSha256 = Convert.ToHexString(SHA256.HashData(baseFile.Bytes));
        if (!string.Equals(baseSha256, profile.ExactBaseSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The habitat base source does not match the exact supported input.");
        }

        var currentFile = fileSource.Read(project, profile.SourceFile);
        if (currentFile.Bytes.Length > SvHabitatDistributionDocument.MaximumSourceBytes)
        {
            throw new InvalidDataException("The current habitat source exceeds its bounded byte limit.");
        }

        SvHabitatDistributionDocument.ValidateSupportedCurrent(
            baseFile.Bytes,
            currentFile.Bytes,
            profile.SourceFile);
        return new SvHabitatLoadedRegion(
            profile,
            SvHabitatDistributionDocument.Parse(baseFile.Bytes, profile.SourceFile),
            SvHabitatDistributionDocument.Parse(currentFile.Bytes, profile.SourceFile),
            SvWorkflowFileSource.CreateReference(currentFile),
            currentFile.FileState);
    }

    private static SvHabitatCoordinatePage CreatePage(
        SvHabitatCoordinatesQuery query,
        SvHabitatLoadedRegion? loaded,
        EditSession? session,
        SvTextLabelLookup labels)
    {
        if (loaded is null)
        {
            return new SvHabitatCoordinatePage(
                query.Region,
                query.Search,
                Offset: 0,
                query.Limit,
                TotalMatches: 0,
                Records: []);
        }

        var staged = (session?.PendingEdits ?? [])
            .Select(edit => SvHabitatPendingEditCodec.TryDecode(edit, out var mutation) ? mutation : null)
            .Where(mutation => mutation is not null
                && string.Equals(mutation.Region, query.Region, StringComparison.Ordinal))
            .GroupBy(
                mutation => (
                    mutation!.Mutation.Locator.OuterGroupOccurrence,
                    mutation.Mutation.Locator.RowOccurrence))
            .ToDictionary(
                group => group.Key,
                group => new SvHabitatCoordinateChoice(
                    group.Last()!.Mutation.DesiredCoordinate.X,
                    group.Last()!.Mutation.DesiredCoordinate.Y));
        var labelCache = new Dictionary<
            (int DevNo, int FormNo),
            (string SpeciesName, string? FormName)>();
        var matching = loaded.CurrentDocument.Groups
            .SelectMany(group => group.Rows)
            .Select(row =>
            {
                var labelKey = (row.Identity.DevNo, row.Identity.FormNo);
                if (!labelCache.TryGetValue(labelKey, out var display))
                {
                    var speciesName = ResolveSpeciesName(row.Identity.DevNo, labels);
                    display = (
                        speciesName,
                        ResolveFormName(row.Identity, speciesName));
                    labelCache.Add(labelKey, display);
                }

                return (
                    Row: row,
                    display.SpeciesName,
                    display.FormName);
            })
            .Where(candidate => MatchesSearch(
                candidate.Row,
                candidate.SpeciesName,
                candidate.FormName,
                query.Search))
            .ToArray();
        var effectiveOffset = Math.Min(query.Offset, matching.Length);
        var records = matching
            .Skip(effectiveOffset)
            .Take(query.Limit)
            .Select(candidate =>
            {
                var row = candidate.Row;
                staged.TryGetValue(
                    (row.Locator.OuterGroupOccurrence, row.Locator.RowOccurrence),
                    out var stagedCoordinate);
                return new SvHabitatCoordinateRecord(
                    new SvHabitatRowBinding(
                        row.Locator.SourceFile,
                        loaded.CurrentDocument.SourceRevision,
                        row.Locator.OuterGroupOccurrence,
                        row.Locator.RowOccurrence,
                        row.Locator.RowPreimageSha256,
                        row.Identity.DevNo,
                        row.Identity.FormNo,
                        row.Identity.VersionA,
                        row.Identity.VersionB,
                        row.Coordinate.X,
                        row.Coordinate.Y),
                    candidate.SpeciesName,
                    candidate.FormName,
                    row.Coordinate.X,
                    row.Coordinate.Y,
                    stagedCoordinate is not null,
                    stagedCoordinate);
            })
            .ToArray();
        return new SvHabitatCoordinatePage(
            query.Region,
            query.Search,
            effectiveOffset,
            query.Limit,
            matching.Length,
            records);
    }

    private static string ResolveSpeciesName(int speciesId, SvTextLabelLookup labels)
    {
        if (speciesId == 0)
        {
            return "Empty";
        }

        var candidate = labels.Pokemon(speciesId).Trim();
        return !string.IsNullOrWhiteSpace(candidate)
            && candidate.Length <= MaximumSpeciesNameLength
            && !candidate.Any(char.IsControl)
                ? candidate
                : SvLabels.Pokemon(speciesId);
    }

    private static string? ResolveFormName(
        SvHabitatSemanticIdentity identity,
        string speciesName)
    {
        return identity.FormNo == 0
            ? PokemonFormLabels.ResolveBaseFormLabel(
                identity.DevNo,
                speciesName,
                PokemonFormLabelFamily.ScarletViolet)
            : PokemonFormLabels.ResolveFormLabel(
                identity.DevNo,
                speciesName,
                identity.FormNo,
                PokemonFormLabelFamily.ScarletViolet);
    }

    private static bool MatchesSearch(
        SvHabitatDistributionRow row,
        string speciesName,
        string? formName,
        string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        var identity = row.Identity;
        var coordinate = row.Coordinate;
        var genericForm = identity.FormNo == 0 ? "standard base" : string.Empty;
        var searchable = string.Create(
            CultureInfo.InvariantCulture,
            $"{speciesName} {formName ?? string.Empty} {genericForm} "
            + $"form {identity.FormNo} "
            + $"pokemon species pokedex dex number no #{identity.DevNo} "
            + $"{identity.DevNo} {identity.FormNo} {identity.DevNo}/{identity.FormNo} "
            + $"grid cell x {coordinate.X} y {coordinate.Y} "
            + $"{coordinate.X},{coordinate.Y} {coordinate.X}, {coordinate.Y} "
            + $"{(identity.VersionA ? "scarlet version-a" : string.Empty)} "
            + $"{(identity.VersionB ? "violet version-b" : string.Empty)}");
        return search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static SvHabitatRegionState ToRegionState(
        SvHabitatLoadedRegion loaded,
        bool canStage)
    {
        var document = loaded.CurrentDocument;
        return new SvHabitatRegionState(
            loaded.Profile.Region,
            loaded.Profile.Label,
            loaded.Profile.SourceFile,
            loaded.CurrentSource.Layer,
            loaded.FileState,
            document.SourceRevision,
            canStage,
            document.Groups.Count,
            document.RowCount,
            document.Groups
                .SelectMany(group => group.Rows)
                .Select(row => row.Identity)
                .Distinct()
                .Count(),
            loaded.BaseDocument.ObservedCoordinates
                .Select(coordinate => new SvHabitatCoordinateChoice(coordinate.X, coordinate.Y))
                .ToArray());
    }

    private static SvHabitatRegionState BlockedRegion(SvHabitatRegionProfile profile) =>
        new(
            profile.Region,
            profile.Label,
            profile.SourceFile,
            SourceLayer: null,
            FileState: null,
            SourceRevision: string.Empty,
            CanStage: false,
            OuterGroupCount: 0,
            RowCount: 0,
            SemanticIdentityCount: 0,
            CoordinateChoices: []);

    private static SvHabitatCoordinatesWorkflow CreateBlockedWorkflow(
        SvWorkflowSummary summary,
        SvHabitatCoordinatesQuery query,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var regions = SvHabitatCoordinatesProfiles.Regions.Select(BlockedRegion).ToArray();
        return new SvHabitatCoordinatesWorkflow(
            summary,
            SvHabitatCoordinatesProfiles.SupportedBuildLabel,
            "unknown",
            regions,
            new SvHabitatCoordinatePage(
                query.Region,
                query.Search,
                Offset: 0,
                query.Limit,
                0,
                []),
            new SvHabitatCoordinatesStats(regions.Length, 0, 0, 0),
            diagnostics);
    }

    internal static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string? code = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            file,
            Domain: "sv.habitatCoordinates",
            Field: field,
            Expected: expected)
        {
            Code = code,
        };
    }

    private static bool IsSourceFailure(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException
            or SecurityException
            or OverflowException;
    }

    internal static bool IsUnavailableSource(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception? candidate = exception;
        for (var depth = 0; candidate is not null && depth < 8; depth++)
        {
            if (candidate is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }

            candidate = candidate.InnerException;
        }

        return false;
    }
}
