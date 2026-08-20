// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KM.Api.Projects;
using KM.Api.Workspace;
using KM.Core.Projects;
using KM.Core.Workspace;
using KM.Tools.Bridge;

namespace KM.Tools.Application;

/// <summary>
/// Owns bounded private application and project personalization documents.
/// </summary>
public sealed class WorkspacePersonalStateApplicationService
{
    private const int MaximumIdentifierLength = 128;
    private const int MaximumStableIdLength = 1_024;
    private const int MaximumDisplayNameLength = 256;
    private const int MaximumPathLength = 32_767;
    private const int MaximumSubcontextEntries = 32;
    private const int MaximumSubcontextStringLength = 4_096;
    private const int MaximumSavedViewAggregatePayloadBytes = 512 * 1024;
    private const int MaximumLocalePackEntryCount = 8_192;
    private const int MaximumLocalePackKeyLength = 1_024;
    private const int MaximumLocalePackValueLength = 8_192;
    private const int MaximumJsonNodes = 8_192;
    private const int MaximumJsonDepth = 32;
    private const int MaximumLocalePackIdLength = 64;
    private const int MaximumLocalePackDisplayNameLength = 64;
    private const string ProjectIdPrefix = "km1_";
    private static readonly JsonSerializerOptions SizeSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly WorkspaceDocumentDefinition<WorkspaceApplicationStateDocumentDto>
        ApplicationDocumentDefinition = new(
            new WorkspaceDocumentId("application-state"),
            "workspace-application-state",
            WorkspacePersonalStateContract.SchemaVersion);
    private static readonly WorkspaceDocumentDefinition<WorkspaceProjectPersonalStateDocumentDto>
        ProjectDocumentDefinition = new(
            new WorkspaceDocumentId("personal-state"),
            "workspace-project-personal-state",
            WorkspacePersonalStateContract.SchemaVersion);
    private static readonly WorkspaceDocumentId AuthoringOperationLeaseId =
        new("change-sets-operation");
    private static readonly HashSet<string> InspectorTabs =
        ["compare", "references", "impact", "history", "notes", "provenance"];
    private static readonly HashSet<string> BuiltInLanguages =
        ["en", "es", "fr", "de", "ru", "uk", "zh"];
    private static readonly HashSet<string> AllGameSections =
    [
        "health", "workbench", "workflows", "items", "pokemon", "moves", "text",
        "trainers", "giftPokemon", "tradePokemon", "shops", "encounters", "placement",
        "typeChart", "spreadsheetImport", "modMerger", "gameDump", "changes", "settings",
    ];
    private static readonly HashSet<string> SwordShieldSections =
    [
        "staticEncounters", "rentalPokemon", "dynamaxAdventures", "raidBattles",
        "raidRewards", "raidBonusRewards", "behavior", "flagworkSave", "bagHook",
        "royalCandy", "startingItems", "npcItemGift", "catchCap", "ivScreen",
        "hyperTraining", "shinyRate", "fairyGymBoosts", "fashionUnlock",
        "gymUniformRemoval", "exefsPatches", "fpsPatch", "profanityFilter", "randomizer",
    ];
    private static readonly HashSet<string> ScarletVioletSections =
    [
        "staticEncounters", "teraRaids", "fashionUnlock", "hyperspaceBypass",
    ];
    private static readonly HashSet<string> ZaSections = ["dexLayout", "angeFight"];

    private readonly VersionedWorkspaceDocumentStore store;

    public WorkspacePersonalStateApplicationService(
        VersionedWorkspaceDocumentStore? store = null)
    {
        this.store = store ?? new VersionedWorkspaceDocumentStore(
            GetDefaultAppDataRoot(),
            serializerOptions: SizeSerializerOptions);
    }

    public async Task<ReadWorkspaceApplicationStateResponse> ReadApplicationAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await store.ReadAsync(
                WorkspaceDocumentScope.Application,
                ApplicationDocumentDefinition,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return new ReadWorkspaceApplicationStateResponse(false, null, null);
        }

        ValidateApplicationDocument(result.Document);
        return new ReadWorkspaceApplicationStateResponse(true, result.Document, result.ETag);
    }

    public async Task<WriteWorkspaceApplicationStateResponse> WriteApplicationAsync(
        WorkspaceApplicationStateDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (document is null)
        {
            throw Invalid("Application workspace state is required.");
        }

        ValidateApplicationDocument(document);
        ValidateExpectedETag(expectedETag);
        var result = await store.WriteConditionalAsync(
                WorkspaceDocumentScope.Application,
                ApplicationDocumentDefinition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new WriteWorkspaceApplicationStateResponse(result.WrittenAtUtc, result.ETag);
    }

    public async Task<ReadWorkspaceProjectStateResponse> ReadProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var scope = GetProjectScope(projectId);
        var result = await store.ReadAsync(scope, ProjectDocumentDefinition, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return new ReadWorkspaceProjectStateResponse(false, null, null);
        }

        ValidateProjectDocument(result.Document);
        return new ReadWorkspaceProjectStateResponse(true, result.Document, result.ETag);
    }

    public async Task<WriteWorkspaceProjectStateResponse> WriteProjectAsync(
        string projectId,
        WorkspaceProjectPersonalStateDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var identity = GetProjectIdentity(projectId);
        using var authoringLease = await store.AcquireProjectOperationLeaseAsync(
                identity,
                AuthoringOperationLeaseId,
                cancellationToken)
            .ConfigureAwait(false);
        return await WriteProjectCoreAsync(projectId, document, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<WriteWorkspaceProjectStateResponse> WriteProjectForRelocationAsync(
        string projectId,
        WorkspaceProjectPersonalStateDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return await WriteProjectCoreAsync(projectId, document, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WriteWorkspaceProjectStateResponse> WriteProjectCoreAsync(
        string projectId,
        WorkspaceProjectPersonalStateDocumentDto document,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        if (document is null)
        {
            throw Invalid("Project personal state is required.");
        }

        var scope = GetProjectScope(projectId);
        ValidateProjectDocument(document);
        ValidateExpectedETag(expectedETag);
        var result = await store.WriteConditionalAsync(
                scope,
                ProjectDocumentDefinition,
                document,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new WriteWorkspaceProjectStateResponse(result.WrittenAtUtc, result.ETag);
    }

    public async Task<DeleteWorkspaceProjectStateResponse> DeleteProjectAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        var identity = GetProjectIdentity(projectId);
        using var authoringLease = await store.AcquireProjectOperationLeaseAsync(
                identity,
                AuthoringOperationLeaseId,
                cancellationToken)
            .ConfigureAwait(false);
        return await DeleteProjectCoreAsync(projectId, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<DeleteWorkspaceProjectStateResponse> DeleteProjectForRelocationAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        return await DeleteProjectCoreAsync(projectId, expectedETag, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DeleteWorkspaceProjectStateResponse> DeleteProjectCoreAsync(
        string projectId,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        var scope = GetProjectScope(projectId);
        ValidateExpectedETag(expectedETag);
        var result = await store.DeleteConditionalAsync(
                scope,
                ProjectDocumentDefinition.DocumentId,
                expectedETag,
                cancellationToken)
            .ConfigureAwait(false);
        return new DeleteWorkspaceProjectStateResponse(result.Deleted);
    }

    public WorkspaceProjectPersonalStateDocumentDto PrepareForRelocation(
        WorkspaceProjectPersonalStateDocumentDto document,
        string? candidateOutputRootPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateProjectDocument(document);
        var matchingProfileIds = string.IsNullOrWhiteSpace(candidateOutputRootPath)
            ? []
            : document.OutputProfiles
                .Where(profile => PathsEqual(profile.OutputRootPath, candidateOutputRootPath))
                .Select(profile => profile.ProfileId)
                .Take(2)
                .ToArray();
        var relocated = document with
        {
            ActiveOutputProfileId = matchingProfileIds.Length == 1
                ? matchingProfileIds[0]
                : null,
        };
        ValidateProjectDocument(relocated);
        return relocated;
    }

    private static void ValidateApplicationDocument(WorkspaceApplicationStateDocumentDto document)
    {
        if (document.SchemaVersion != WorkspacePersonalStateContract.SchemaVersion)
        {
            throw Invalid("Application workspace state has an unsupported schema version.");
        }

        RequireTimestamp(document.UpdatedAtUtc, "application workspace state");
        RequireList(document.RecentProjects, "recent project profiles");
        RequireList(document.ShortcutOverrides, "shortcut overrides");
        RequireList(document.LocalePacks, "locale packs");
        RequireList(document.GameDumpDestinations, "game dump destinations");
        if (document.RecentProjects.Count > WorkspacePersonalStateContract.MaximumRecentProjectCount
            || document.ShortcutOverrides.Count > WorkspacePersonalStateContract.MaximumShortcutOverrideCount
            || document.LocalePacks.Count > WorkspacePersonalStateContract.MaximumLocalePackCount
            || document.GameDumpDestinations.Count
                > WorkspacePersonalStateContract.MaximumGameDumpDestinationCount)
        {
            throw Invalid("Application workspace state exceeds a supported entry limit.");
        }

        var projectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in document.RecentProjects)
        {
            if (profile is null || profile.Paths is null)
            {
                throw Invalid("A recent project profile is invalid.");
            }

            ValidateProjectId(profile.ProjectId);
            ValidateOptionalDisplayText(profile.Name, "recent project name", MaximumDisplayNameLength);
            RequireTimestamp(profile.LastOpenedAtUtc, "recent project profile");
            if (!Enum.IsDefined(profile.Game) || profile.Paths.SelectedGame != profile.Game)
            {
                throw Invalid("A recent project profile has an invalid game scope.");
            }

            ValidateRecentProjectPaths(profile.Paths, profile.Game);
            var computedProjectId = ProjectIdentity.FromPaths(ProjectBridgeMapper.ToCore(profile.Paths)).Value;
            if (!string.Equals(profile.ProjectId, computedProjectId, StringComparison.Ordinal))
            {
                throw Invalid("A recent project profile does not match its stable project identity.");
            }

            if (!projectIds.Add(profile.ProjectId))
            {
                throw Invalid("Application workspace state contains duplicate recent projects.");
            }
        }

        var commands = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shortcut in document.ShortcutOverrides)
        {
            if (shortcut is null)
            {
                throw Invalid("A shortcut override is invalid.");
            }

            ValidateContractKey(shortcut.CommandId, "shortcut command id");
            ValidateDisplayText(shortcut.Shortcut, "shortcut", MaximumIdentifierLength);
            RequireTimestamp(shortcut.UpdatedAtUtc, "shortcut override");
            if (!commands.Add(shortcut.CommandId))
            {
                throw Invalid("Application workspace state contains duplicate shortcut overrides.");
            }
        }

        var destinationGames = new HashSet<ProjectGameDto>();
        foreach (var destination in document.GameDumpDestinations)
        {
            if (destination is null || !Enum.IsDefined(destination.Game))
            {
                throw Invalid("A game dump destination has an invalid game scope.");
            }

            ValidateFullyQualifiedPath(
                destination.DestinationPath,
                "Game Dump destination path",
                required: true);
            RequireTimestamp(destination.UpdatedAtUtc, "game dump destination");
            if (!destinationGames.Add(destination.Game))
            {
                throw Invalid("Application workspace state contains duplicate Game Dump destinations.");
            }
        }

        ValidateLocalePacks(document.LocalePacks);
        ValidateSerializedSize(
            document,
            WorkspacePersonalStateContract.ApplicationMaximumSerializedDocumentBytes,
            "Application workspace state");
    }

    private static void ValidateProjectDocument(WorkspaceProjectPersonalStateDocumentDto document)
    {
        if (document.SchemaVersion != WorkspacePersonalStateContract.SchemaVersion
            || !Enum.IsDefined(document.Game))
        {
            throw Invalid("Project personal state has an invalid schema or game scope.");
        }

        RequireTimestamp(document.UpdatedAtUtc, "project personal state");
        RequireList(document.Bookmarks, "bookmarks");
        RequireList(document.Notes, "notes");
        RequireList(document.SavedViews, "saved views");
        RequireList(document.RecentTargets, "recent targets");
        RequireList(document.OutputProfiles, "output profiles");
        if (document.Bookmarks.Count > WorkspacePersonalStateContract.MaximumBookmarkCount
            || document.Notes.Count > WorkspacePersonalStateContract.MaximumNoteCount
            || document.SavedViews.Count > WorkspacePersonalStateContract.MaximumSavedViewCount
            || document.RecentTargets.Count > WorkspacePersonalStateContract.MaximumRecentTargetCount
            || document.OutputProfiles.Count > WorkspacePersonalStateContract.MaximumOutputProfileCount)
        {
            throw Invalid("Project personal state exceeds a supported entry limit.");
        }

        ValidateBookmarks(document.Bookmarks, document.Game);
        ValidateNotes(document.Notes, document.Game);
        ValidateSavedViews(document.SavedViews, document.Game);
        ValidateRecentTargets(document.RecentTargets, document.Game);
        ValidateOutputProfiles(
            document.OutputProfiles,
            document.ActiveOutputProfileId);
        ValidateSerializedSize(
            document,
            WorkspacePersonalStateContract.ProjectMaximumSerializedDocumentBytes,
            "Project personal state");
    }

    private static void ValidateBookmarks(
        IReadOnlyList<WorkspaceBookmarkDto> bookmarks,
        ProjectGameDto game)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bookmark in bookmarks)
        {
            if (bookmark is null || bookmark.Location is null)
            {
                throw Invalid("A bookmark is invalid.");
            }

            ValidateStableId(bookmark.BookmarkId, "bookmark id");
            if (bookmark.Kind is not ("pin" or "bookmark"))
            {
                throw Invalid("A bookmark has an invalid kind.");
            }

            ValidateOptionalDisplayText(bookmark.Label, "bookmark label", MaximumDisplayNameLength);
            RequireTimestamp(bookmark.CreatedAtUtc, "bookmark");
            RequireTimestamp(bookmark.UpdatedAtUtc, "bookmark");
            if (bookmark.UpdatedAtUtc < bookmark.CreatedAtUtc)
            {
                throw Invalid("A bookmark cannot be updated before it was created.");
            }

            ValidateLocation(bookmark.Location, game);
            if (!ids.Add(bookmark.BookmarkId))
            {
                throw Invalid("Project personal state contains duplicate bookmark ids.");
            }

            if (!targets.Add(CreateBookmarkTargetKey(bookmark)))
            {
                throw Invalid("Project personal state contains duplicate bookmark targets.");
            }
        }
    }

    private static void ValidateNotes(
        IReadOnlyList<WorkspaceProjectNoteDto> notes,
        ProjectGameDto game)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var locations = new HashSet<string>(StringComparer.Ordinal);
        long aggregateBytes = 0;
        foreach (var note in notes)
        {
            if (note is null || note.Location is null || note.Body is null)
            {
                throw Invalid("A project note is invalid.");
            }

            ValidateStableId(note.NoteId, "note id");
            if (ContainsDisallowedTextControl(note.Body))
            {
                throw Invalid("A project note contains an unsupported control character.");
            }

            var noteBytes = Encoding.UTF8.GetByteCount(note.Body);
            if (noteBytes > WorkspacePersonalStateContract.MaximumNoteBytes)
            {
                throw Invalid("A project note exceeds the supported size limit.");
            }

            aggregateBytes = checked(aggregateBytes + noteBytes);
            if (aggregateBytes > WorkspacePersonalStateContract.MaximumAggregateNoteBytes)
            {
                throw Invalid("Project notes exceed the supported aggregate size limit.");
            }

            RequireTimestamp(note.UpdatedAtUtc, "project note");
            ValidateLocation(note.Location, game);
            if (!ids.Add(note.NoteId))
            {
                throw Invalid("Project personal state contains duplicate note ids.");
            }

            if (!locations.Add(CreateLocationKey(note.Location)))
            {
                throw Invalid("Project personal state contains duplicate note locations.");
            }
        }
    }

    private static void ValidateSavedViews(
        IReadOnlyList<WorkspaceSavedViewDto> savedViews,
        ProjectGameDto game)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long aggregatePayloadBytes = 0;
        foreach (var savedView in savedViews)
        {
            if (savedView is null || savedView.Location is null)
            {
                throw Invalid("A saved view is invalid.");
            }

            ValidateStableId(savedView.ViewId, "saved view id");
            ValidateDisplayText(savedView.Name, "saved view name", MaximumDisplayNameLength);
            ValidateContractKey(savedView.AdapterId, "saved view adapter id");
            if (savedView.AdapterSchemaVersion <= 0)
            {
                throw Invalid("A saved view adapter schema version must be positive.");
            }

            ValidateJsonValue(savedView.Payload, "saved view payload");
            var payloadBytes = Encoding.UTF8.GetByteCount(savedView.Payload.GetRawText());
            if (payloadBytes > WorkspacePersonalStateContract.MaximumSavedViewPayloadBytes)
            {
                throw Invalid("A saved view payload exceeds the supported size limit.");
            }

            aggregatePayloadBytes = checked(aggregatePayloadBytes + payloadBytes);
            if (aggregatePayloadBytes > MaximumSavedViewAggregatePayloadBytes)
            {
                throw Invalid("Saved view payloads exceed the supported aggregate size limit.");
            }

            RequireTimestamp(savedView.UpdatedAtUtc, "saved view");
            ValidateLocation(savedView.Location, game);
            if (!ids.Add(savedView.ViewId))
            {
                throw Invalid("Project personal state contains duplicate saved view ids.");
            }
        }
    }

    private static void ValidateRecentTargets(
        IReadOnlyList<WorkspaceRecentTargetDto> recentTargets,
        ProjectGameDto game)
    {
        var locations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in recentTargets)
        {
            if (target is null || target.Location is null)
            {
                throw Invalid("A recent target is invalid.");
            }

            RequireTimestamp(target.VisitedAtUtc, "recent target");
            ValidateLocation(target.Location, game);
            if (!locations.Add(CreateLocationKey(target.Location)))
            {
                throw Invalid("Project personal state contains duplicate recent targets.");
            }
        }
    }

    private static void ValidateOutputProfiles(
        IReadOnlyList<WorkspaceOutputProfileDto> profiles,
        string? activeProfileId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                throw Invalid("An output profile is invalid.");
            }

            ValidateStableId(profile.ProfileId, "output profile id");
            ValidateDisplayText(profile.Name, "output profile name", MaximumDisplayNameLength);
            ValidateFullyQualifiedPath(profile.OutputRootPath, "output profile root", required: true);
            if (profile.OutputMode is { } outputMode && !Enum.IsDefined(outputMode))
            {
                throw Invalid("An output profile has an invalid output mode.");
            }

            RequireTimestamp(profile.UpdatedAtUtc, "output profile");
            if (!ids.Add(profile.ProfileId))
            {
                throw Invalid("Project personal state contains duplicate output profile ids.");
            }
        }

        if (activeProfileId is not null)
        {
            ValidateStableId(activeProfileId, "active output profile id");
            if (!ids.Contains(activeProfileId))
            {
                throw Invalid("The active output profile does not exist.");
            }
        }
    }

    private static void ValidateLocation(
        WorkspaceScopedLocationDto location,
        ProjectGameDto expectedGame)
    {
        if (location.Version != 1
            || !Enum.IsDefined(location.Game)
            || location.Game != expectedGame
            || !IsSectionAvailable(location.Section, expectedGame))
        {
            throw Invalid("A saved workspace location has an invalid version, section, or game scope.");
        }

        ValidateOptionalStableId(location.ChangeSetId, "location change-set id");
        if (location.InspectorTab is not null && !InspectorTabs.Contains(location.InspectorTab))
        {
            throw Invalid("A saved workspace location has an invalid inspector tab.");
        }

        if (location.Entity is { } entity)
        {
            var expectedFamily = GetGameFamily(expectedGame);
            if (!string.Equals(entity.GameFamily, expectedFamily, StringComparison.Ordinal))
            {
                throw Invalid("A saved workspace location entity has an invalid game family.");
            }

            ValidateSemanticContractKey(entity.Domain, "semantic entity domain");
            if (entity.RecordKind is null || entity.RecordKind.SchemaVersion <= 0)
            {
                throw Invalid("A semantic entity record kind is invalid.");
            }

            ValidateSemanticContractKey(entity.RecordKind.Key, "semantic record kind");
            ValidateStableId(entity.RecordId, "semantic record id", 4_096);
            ValidateOptionalStableId(entity.SubrecordId, "semantic subrecord id", 4_096);
        }

        if (location.Subcontext is null)
        {
            return;
        }

        if (location.Subcontext.Count > MaximumSubcontextEntries)
        {
            throw Invalid("A saved workspace location has too many subcontext entries.");
        }

        foreach (var (key, value) in location.Subcontext)
        {
            ValidateContractKey(key, "location subcontext key");
            if (value.ValueKind is not (
                    JsonValueKind.String
                    or JsonValueKind.Number
                    or JsonValueKind.True
                    or JsonValueKind.False
                    or JsonValueKind.Null))
            {
                throw Invalid("A saved workspace location subcontext value must be a JSON primitive.");
            }

            if (value.ValueKind == JsonValueKind.String
                && value.GetString() is { } text
                && (text.Length > MaximumSubcontextStringLength
                    || ContainsDisallowedTextControl(text)))
            {
                throw Invalid("A saved workspace location subcontext string is too long.");
            }

            if (value.ValueKind == JsonValueKind.Number
                && (!value.TryGetDouble(out var number) || !double.IsFinite(number)))
            {
                throw Invalid("A saved workspace location subcontext number must be finite.");
            }
        }
    }

    private static void ValidateLocalePacks(IReadOnlyList<WorkspaceLocalePackDto> localePacks)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var localeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long aggregateBytes = 0;
        foreach (var pack in localePacks)
        {
            if (pack is null || pack.Keys is null || pack.Literals is null)
            {
                throw Invalid("A locale pack is invalid.");
            }

            if (pack.SchemaVersion != 1)
            {
                throw Invalid("A locale pack has an unsupported schema version.");
            }

            ValidateLocalePackId(pack.Id);
            if (!IsLanguageTag(pack.LocaleTag))
            {
                throw Invalid("A locale pack language tag is invalid.");
            }

            ValidateDisplayText(
                pack.DisplayName,
                "locale pack display name",
                MaximumLocalePackDisplayNameLength);
            if (!IsNormalizedFormC(pack.DisplayName)
                || ContainsDisallowedUnicodeFormat(pack.DisplayName))
            {
                throw Invalid("A locale pack display name is invalid.");
            }

            if (pack.Direction != "ltr" || !BuiltInLanguages.Contains(pack.GameTextLanguage))
            {
                throw Invalid("A locale pack has invalid presentation metadata.");
            }
            if (pack.Keys.Count + pack.Literals.Count > MaximumLocalePackEntryCount)
            {
                throw Invalid("A locale pack has too many translated entries.");
            }

            ValidateLocaleDictionary(pack.Keys);
            ValidateLocaleDictionary(pack.Literals);
            var packBytes = JsonSerializer.SerializeToUtf8Bytes(
                pack,
                SizeSerializerOptions).Length;
            if (packBytes > WorkspacePersonalStateContract.MaximumLocalePackBytes)
            {
                throw Invalid("A locale pack exceeds the supported size limit.");
            }

            aggregateBytes = checked(aggregateBytes + packBytes);
            if (aggregateBytes > WorkspacePersonalStateContract.MaximumLocalePackAggregateBytes)
            {
                throw Invalid("Locale packs exceed the supported aggregate size limit.");
            }

            if (!ids.Add(pack.Id))
            {
                throw Invalid("Application workspace state contains duplicate locale packs.");
            }

            if (!localeTags.Add(pack.LocaleTag))
            {
                throw Invalid("Application workspace state contains duplicate locale tags.");
            }
        }
    }

    private static void ValidateLocaleDictionary(IReadOnlyDictionary<string, string> entries)
    {
        foreach (var (key, value) in entries)
        {
            if (string.IsNullOrEmpty(key)
                || key.Length > MaximumLocalePackKeyLength
                || key.Any(char.IsControl)
                || ContainsDisallowedUnicodeFormat(key)
                || !IsNormalizedFormC(key)
                || value is null
                || value.Length > MaximumLocalePackValueLength
                || value.Any(char.IsControl)
                || ContainsDisallowedUnicodeFormat(value)
                || !IsNormalizedFormC(value))
            {
                throw Invalid("A locale pack contains an invalid translated entry.");
            }
        }
    }

    private static void ValidateRecentProjectPaths(ProjectPathsDto paths, ProjectGameDto game)
    {
        ValidateFullyQualifiedPath(paths.BaseRomFsPath, "Base RomFS path", required: true);
        ValidateFullyQualifiedPath(paths.BaseExeFsPath, "Base ExeFS path", required: true);
        ValidateFullyQualifiedPath(paths.OutputRootPath, "Output Root path", required: false);
        ValidateFullyQualifiedPath(paths.SaveFilePath, "save file path", required: false);
        ValidateFullyQualifiedPath(
            paths.ScarletVioletSupportFolderPath,
            "support folder path",
            required: false);
        ValidateFullyQualifiedPath(
            paths.PokemonLegendsZASupportFolderPath,
            "support folder path",
            required: false);
        ValidateOptionalDisplayText(paths.GameTextLanguage, "game text language", 64);

        if (game is not (ProjectGameDto.Scarlet or ProjectGameDto.Violet)
            && !string.IsNullOrWhiteSpace(paths.ScarletVioletSupportFolderPath))
        {
            throw Invalid("A recent project profile contains a support path for another game family.");
        }

        if (game != ProjectGameDto.ZA
            && !string.IsNullOrWhiteSpace(paths.PokemonLegendsZASupportFolderPath))
        {
            throw Invalid("A recent project profile contains a support path for another game family.");
        }
    }

    private static void ValidateJsonValue(JsonElement root, string label)
    {
        if (root.ValueKind == JsonValueKind.Undefined)
        {
            throw Invalid($"The {label} is missing.");
        }

        var nodes = 0;
        var pending = new Stack<(JsonElement Element, int Depth)>();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            nodes++;
            if (nodes > MaximumJsonNodes || depth > MaximumJsonDepth)
            {
                throw Invalid($"The {label} exceeds a supported JSON complexity limit.");
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Length == 0
                            || property.Name.Length > MaximumIdentifierLength
                            || property.Name.Any(char.IsControl))
                        {
                            throw Invalid($"The {label} contains an invalid object key.");
                        }

                        pending.Push((property.Value, depth + 1));
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        pending.Push((item, depth + 1));
                    }

                    break;
                case JsonValueKind.Number:
                    if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                    {
                        throw Invalid($"The {label} contains a non-finite number.");
                    }

                    break;
                case JsonValueKind.String:
                    if (element.GetString() is { } text
                        && (text.Length > MaximumSubcontextStringLength
                            || ContainsDisallowedTextControl(text)))
                    {
                        throw Invalid($"The {label} contains an oversized string.");
                    }

                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    break;
                default:
                    throw Invalid($"The {label} contains an unsupported JSON value.");
            }
        }
    }

    private static string CreateLocationKey(WorkspaceScopedLocationDto location)
    {
        var builder = new StringBuilder();
        AppendKeyPart(builder, location.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendKeyPart(builder, location.Game.ToString());
        AppendKeyPart(builder, location.Section);
        AppendKeyPart(builder, location.ChangeSetId);
        AppendKeyPart(builder, location.InspectorTab);
        if (location.Entity is { } entity)
        {
            AppendKeyPart(builder, entity.GameFamily);
            AppendKeyPart(builder, entity.Domain);
            AppendKeyPart(builder, entity.RecordKind.Key);
            AppendKeyPart(builder, entity.RecordKind.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendKeyPart(builder, entity.RecordId);
            AppendKeyPart(builder, entity.SubrecordId);
        }
        else
        {
            AppendKeyPart(builder, null);
        }

        if (location.Subcontext is not null)
        {
            foreach (var entry in location.Subcontext.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                AppendKeyPart(builder, entry.Key);
                AppendKeyPart(builder, CreatePrimitiveLocationKey(entry.Value));
            }
        }

        return builder.ToString();
    }

    private static string CreateBookmarkTargetKey(WorkspaceBookmarkDto bookmark)
    {
        var builder = new StringBuilder();
        AppendKeyPart(builder, bookmark.Kind);
        AppendKeyPart(builder, bookmark.Kind == "bookmark" ? bookmark.Label : null);
        AppendKeyPart(builder, CreateLocationKey(bookmark.Location));
        return builder.ToString();
    }

    private static string CreatePrimitiveLocationKey(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => "string:" + value.GetString(),
            JsonValueKind.Number => "number:" + CreateCanonicalNumber(value),
            JsonValueKind.True => "boolean:true",
            JsonValueKind.False => "boolean:false",
            JsonValueKind.Null => "null",
            _ => throw Invalid("A saved workspace location subcontext value is invalid."),
        };
    }

    private static string CreateCanonicalNumber(JsonElement value)
    {
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw Invalid("A saved workspace location subcontext number must be finite.");
        }

        return number == 0
            ? "0"
            : number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AppendKeyPart(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:");
            return;
        }

        builder.Append(value.Length).Append(':').Append(value);
    }

    private static bool IsSectionAvailable(string section, ProjectGameDto game)
    {
        if (string.IsNullOrEmpty(section) || section.Length > MaximumIdentifierLength)
        {
            return false;
        }

        if (AllGameSections.Contains(section))
        {
            return true;
        }

        return game switch
        {
            ProjectGameDto.Sword or ProjectGameDto.Shield => SwordShieldSections.Contains(section),
            ProjectGameDto.Scarlet or ProjectGameDto.Violet => ScarletVioletSections.Contains(section),
            ProjectGameDto.ZA => ZaSections.Contains(section),
            _ => false,
        };
    }

    private static string GetGameFamily(ProjectGameDto game)
    {
        return game switch
        {
            ProjectGameDto.Sword or ProjectGameDto.Shield => "swordShield",
            ProjectGameDto.Scarlet or ProjectGameDto.Violet => "scarletViolet",
            ProjectGameDto.ZA => "legendsZA",
            _ => throw Invalid("A workspace game is invalid."),
        };
    }

    private static WorkspaceDocumentScope GetProjectScope(string projectId)
    {
        return WorkspaceDocumentScope.ForProject(GetProjectIdentity(projectId));
    }

    private static WorkspaceProjectIdentity GetProjectIdentity(string projectId)
    {
        ValidateProjectId(projectId);
        return WorkspaceProjectIdentity.FromProjectId(new ProjectId(projectId));
    }

    private static void ValidateProjectId(string projectId)
    {
        if (string.IsNullOrEmpty(projectId)
            || projectId.Length != ProjectIdPrefix.Length + 64
            || !projectId.StartsWith(ProjectIdPrefix, StringComparison.Ordinal)
            || projectId.AsSpan(ProjectIdPrefix.Length).ContainsAnyExcept("0123456789abcdef"))
        {
            throw Invalid("The project id is invalid.");
        }
    }

    private static void ValidateLocalePackId(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumLocalePackIdLength
            || !IsLowerAsciiLetterOrDigit(value[0])
            || !IsLowerAsciiLetterOrDigit(value[^1])
            || value.Any(character =>
                !IsLowerAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw Invalid("The locale pack id is invalid.");
        }
    }

    private static void ValidateSemanticContractKey(string value, string label)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumIdentifierLength
            || !IsLowerAsciiLetterOrDigit(value[0])
            || !IsLowerAsciiLetterOrDigit(value[^1])
            || value.Any(character =>
                !IsLowerAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static bool IsLowerAsciiLetterOrDigit(char value)
    {
        return char.IsAsciiDigit(value) || value is >= 'a' and <= 'z';
    }

    private static void ValidateFullyQualifiedPath(string? path, string label, bool required)
    {
        if (path is null)
        {
            if (required)
            {
                throw Invalid($"The {label} is required.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(path)
            || path != path.Trim()
            || path.Length > MaximumPathLength
            || path.Any(char.IsControl)
            || !Path.IsPathFullyQualified(path))
        {
            throw Invalid($"The {label} is invalid.");
        }

        try
        {
            _ = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            throw Invalid($"The {label} is invalid.", exception);
        }
    }

    private static void ValidateContractKey(string value, string label)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumIdentifierLength
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static void ValidateStableId(
        string value,
        string label,
        int maximumLength = MaximumStableIdLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw Invalid($"The {label} must be a non-empty bounded identifier.");
        }
    }

    private static void ValidateOptionalStableId(
        string? value,
        string label,
        int maximumLength = MaximumStableIdLength)
    {
        if (value is not null)
        {
            ValidateStableId(value, label, maximumLength);
        }
    }

    private static void ValidateDisplayText(string value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw Invalid($"The {label} is invalid.");
        }
    }

    private static void ValidateOptionalDisplayText(
        string? value,
        string label,
        int maximumLength)
    {
        if (value is not null)
        {
            ValidateDisplayText(value, label, maximumLength);
        }
    }

    private static bool ContainsDisallowedTextControl(string value)
    {
        return value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    }

    private static bool ContainsDisallowedUnicodeFormat(string value)
    {
        return value.Any(character => character is
            '\u061c'
            or (>= '\u200b' and <= '\u200f')
            or (>= '\u202a' and <= '\u202e')
            or '\u2060'
            or (>= '\u2066' and <= '\u2069')
            or '\ufeff');
    }

    private static bool IsNormalizedFormC(string value)
    {
        try
        {
            return value.IsNormalized(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsLanguageTag(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length is < 2 or > 64
            || value.Any(char.IsControl))
        {
            return false;
        }

        var segments = value.Split('-');
        return segments.Length is >= 1 and <= 8
            && segments.All(segment =>
                segment.Length is >= 1 and <= 8
                && segment.All(char.IsAsciiLetterOrDigit));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(
                normalizedLeft,
                normalizedRight,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateExpectedETag(string? expectedETag)
    {
        if (expectedETag is not null
            && (expectedETag.Length != 64 || expectedETag.Any(character => !Uri.IsHexDigit(character))))
        {
            throw Invalid("The expected workspace document ETag is invalid.");
        }
    }

    private static void RequireTimestamp(DateTimeOffset value, string label)
    {
        if (value == default)
        {
            throw Invalid($"The {label} requires a valid timestamp.");
        }
    }

    private static void RequireList<T>(IReadOnlyList<T>? value, string label)
    {
        if (value is null)
        {
            throw Invalid($"Workspace state is missing {label}.");
        }
    }

    private static void ValidateSerializedSize<T>(T document, int maximumBytes, string label)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SizeSerializerOptions);
        if (bytes.Length > maximumBytes)
        {
            throw Invalid($"{label} exceeds the supported size limit.");
        }
    }

    private static string GetDefaultAppDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData)
            || !Path.IsPathFullyQualified(localApplicationData))
        {
            throw new InvalidOperationException(
                "A private local application-data location is unavailable.");
        }

        return Path.Combine(localApplicationData, "KM Editor");
    }

    private static WorkspacePersonalStateValidationException Invalid(
        string message,
        Exception? innerException = null)
    {
        return innerException is null
            ? new WorkspacePersonalStateValidationException(message)
            : new WorkspacePersonalStateValidationException(message, innerException);
    }

}

public sealed class WorkspacePersonalStateValidationException : Exception
{
    public WorkspacePersonalStateValidationException(string message)
        : base(message)
    {
    }

    public WorkspacePersonalStateValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
