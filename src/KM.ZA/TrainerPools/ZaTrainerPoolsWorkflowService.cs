// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.GameData;
using KM.Formats.ZA.Generated.TrainerPools;
using KM.ZA.Data;
using KM.ZA.Trainers;
using KM.ZA.Workflows;

namespace KM.ZA.TrainerPools;

internal sealed class ZaTrainerPoolsWorkflowService
{
    private const string WorkflowLabel = "Trainer Pools";
    private const string WorkflowDescription =
        "Swap exact trainer identities across synchronized Story and Infinity pool mirrors without resizing pools.";
    private static readonly string[] StoryMovements =
    [
        "fm180",
        "fm360",
        "freemove",
        "turn180",
        "turn360",
    ];

    private static readonly string[] InfinitySuffixes =
    [
        "fm180_easy_01",
        "fm360_easy_01",
        "turn180_easy_01",
        "turn360_easy_01",
        "freemove_easy_01",
        "freemove_hard_02",
        "fm360_hard_02",
        "fm180_hard_01",
        "fm360_hard_01",
        "turn180_hard_01",
        "turn360_hard_01",
        "freemove_hard_01",
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaTrainerPoolsWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.TrainerPools,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaTrainerPoolsWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return TryLoadState(project, out var state, out var blockedWorkflow)
            ? state!.Workflow
            : blockedWorkflow!;
    }

    public bool TryLoadState(
        OpenedProject project,
        out ZaTrainerPoolsLoadedState? state,
        out ZaTrainerPoolsWorkflow? blockedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(project);
        state = null;
        blockedWorkflow = null;
        var diagnostics = new List<ValidationDiagnostic>();
        if (project.Paths.SelectedGame is not ProjectGame.ZA)
        {
            diagnostics.Add(Error(
                "Trainer Pools require a Pokemon Legends Z-A project.",
                expected: "Pokemon Legends Z-A project"));
            blockedWorkflow = EmptyWorkflow(diagnostics);
            return false;
        }

        try
        {
            var tableSource = fileSource.Read(project, ZaDataPaths.TrainerPoolTableDataArray);
            var identitySource = fileSource.Read(project, ZaDataPaths.TrainerPoolIdentityDataArray);
            var rosterSource = fileSource.Read(project, ZaDataPaths.TrainerDataArray);
            var spawnerSource = fileSource.Read(project, ZaDataPaths.BattleTrainerSpawnerDataArray);
            var document = ZaTrainerPoolDataDocument.Parse(tableSource.Bytes);
            var displayRecords = new ZaTrainersWorkflowService(fileSource)
                .Load(project)
                .Trainers
                .ToDictionary(record => record.TrainerId);
            var roster = ReadRoster(rosterSource.Bytes, displayRecords, diagnostics);
            var identities = ReadIdentities(identitySource.Bytes, roster, diagnostics);
            var referencedTableIds = ReadReferencedTableIds(spawnerSource.Bytes, diagnostics);
            var pools = BuildPools(document, identities, referencedTableIds, diagnostics);
            ValidateCompleteData(document, identities, referencedTableIds, diagnostics);

            if (!project.Health.CanOpenEditableWorkflows)
            {
                diagnostics.Add(Error(
                    "Trainer Pools require valid base paths, the Z-A support runtime, and a writable output root.",
                    expected: "Editable Pokemon Legends Z-A project paths"));
            }

            var workflow = new ZaTrainerPoolsWorkflow(
                pools,
                new ZaTrainerPoolsWorkflowStats(
                    pools.Count,
                    pools.Sum(pool => pool.PhysicalTableIds.Count),
                    pools.Sum(pool => pool.MemberCount * pool.PhysicalTableIds.Count),
                    pools.Sum(pool => pool.PhysicalTableIds.Count - pool.ReferencedPhysicalTableCount)),
                diagnostics,
                diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error));
            var sources = new[]
            {
                Reference(tableSource),
                Reference(identitySource),
                Reference(rosterSource),
                Reference(spawnerSource),
            };
            state = new ZaTrainerPoolsLoadedState(
                workflow,
                document,
                identities,
                referencedTableIds,
                sources);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            OverflowException)
        {
            diagnostics.Add(Error(
                $"Trainer Pools could not be loaded safely: {exception.Message}",
                file: $"romfs/{ZaDataPaths.TrainerPoolTableDataArray}",
                expected: "Readable supported Trainer Pools and validation sources",
                code: ZaTrainerPoolsDiagnosticCodes.Safety));
            blockedWorkflow = EmptyWorkflow(diagnostics);
            return false;
        }
    }

    public static bool TryApplyFixedCountSwap(
        ZaTrainerPoolsLoadedState state,
        ZaTrainerPoolFixedCountSwap operation,
        ICollection<ValidationDiagnostic> diagnostics,
        out ZaTrainerPoolDataDocument? editedDocument,
        out int changedReferenceCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(diagnostics);
        editedDocument = null;
        changedReferenceCount = 0;
        var sourcePool = state.Workflow.Pools.FirstOrDefault(pool => string.Equals(
            pool.LogicalPoolId,
            operation.SourceLogicalPoolId,
            StringComparison.Ordinal));
        var destinationPool = state.Workflow.Pools.FirstOrDefault(pool => string.Equals(
            pool.LogicalPoolId,
            operation.DestinationLogicalPoolId,
            StringComparison.Ordinal));
        if (sourcePool is null || destinationPool is null)
        {
            diagnostics.Add(Error(
                "The fixed-count swap targets an unavailable or unsupported logical pool.",
                field: "logicalPoolId",
                expected: "A complete supported Story or Infinity logical pool",
                code: ZaTrainerPoolsDiagnosticCodes.SelectionInvalid));
            return false;
        }

        if (sourcePool.Kind != destinationPool.Kind
            || sourcePool.PhysicalTableIds.Count != destinationPool.PhysicalTableIds.Count)
        {
            diagnostics.Add(Error(
                "The selected logical pools have incompatible physical mirror shapes.",
                field: "logicalPoolId",
                expected: "Two Story pools with five mirrors or two Infinity tiers with twelve mirrors",
                code: ZaTrainerPoolsDiagnosticCodes.PoolsIncompatible));
            return false;
        }

        if (string.Equals(
                operation.SourceRawTrainerId,
                operation.DestinationRawTrainerId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "Choose two different raw trainer identities for a fixed-count swap.",
                field: "rawTrainerId",
                expected: "Different exact raw trainer identities",
                code: ZaTrainerPoolsDiagnosticCodes.SelectionInvalid));
            return false;
        }

        if (!sourcePool.Members.Any(member => string.Equals(
                member.RawTrainerId,
                operation.SourceRawTrainerId,
                StringComparison.Ordinal))
            || !destinationPool.Members.Any(member => string.Equals(
                member.RawTrainerId,
                operation.DestinationRawTrainerId,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                "A selected raw trainer identity is not a member of its selected logical pool.",
                field: "rawTrainerId",
                expected: "An exact raw trainer identity present in every required mirror",
                code: ZaTrainerPoolsDiagnosticCodes.SelectionInvalid));
            return false;
        }

        var samePool = string.Equals(
            sourcePool.LogicalPoolId,
            destinationPool.LogicalPoolId,
            StringComparison.Ordinal);
        if (!samePool
            && (destinationPool.Members.Any(member => string.Equals(
                    member.RawTrainerId,
                    operation.SourceRawTrainerId,
                    StringComparison.Ordinal))
                || sourcePool.Members.Any(member => string.Equals(
                    member.RawTrainerId,
                    operation.DestinationRawTrainerId,
                    StringComparison.Ordinal))))
        {
            diagnostics.Add(Error(
                "The swap would duplicate a raw trainer identity inside a destination pool.",
                field: "rawTrainerId",
                expected: "Source and destination identities absent from the opposite logical pool",
                code: ZaTrainerPoolsDiagnosticCodes.SelectionInvalid));
            return false;
        }

        var original = state.Document;
        var edited = original.Clone();
        var tables = edited.Tables.ToDictionary(table => table.Id!, StringComparer.Ordinal);
        if (samePool)
        {
            foreach (var tableId in sourcePool.PhysicalTableIds)
            {
                if (!TrySwapExactlyOnce(
                        tables[tableId],
                        operation.SourceRawTrainerId,
                        operation.DestinationRawTrainerId,
                        diagnostics))
                {
                    return false;
                }

                changedReferenceCount += 2;
            }
        }
        else
        {
            foreach (var tableId in sourcePool.PhysicalTableIds)
            {
                if (!TryReplaceExactlyOnce(
                        tables[tableId],
                        operation.SourceRawTrainerId,
                        operation.DestinationRawTrainerId,
                        diagnostics))
                {
                    return false;
                }

                changedReferenceCount++;
            }

            foreach (var tableId in destinationPool.PhysicalTableIds)
            {
                if (!TryReplaceExactlyOnce(
                        tables[tableId],
                        operation.DestinationRawTrainerId,
                        operation.SourceRawTrainerId,
                        diagnostics))
                {
                    return false;
                }

                changedReferenceCount++;
            }
        }

        if (!string.Equals(
                original.CreateSemanticFingerprint(includeTrainerIds: false),
                edited.CreateSemanticFingerprint(includeTrainerIds: false),
                StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "The Trainer Pools swap changed data outside trainer references.",
                expected: "Only exact raw trainer references change",
                code: ZaTrainerPoolsDiagnosticCodes.VerificationFailed));
            return false;
        }

        if (original.CountTrainerReferenceDifferences(edited) != changedReferenceCount)
        {
            diagnostics.Add(Error(
                "The Trainer Pools swap did not produce the exact expected mirror delta.",
                expected: $"Exactly {changedReferenceCount} changed raw trainer references",
                code: ZaTrainerPoolsDiagnosticCodes.VerificationFailed));
            return false;
        }

        var output = edited.Write();
        var reparsed = ZaTrainerPoolDataDocument.Parse(output);
        if (!string.Equals(
                edited.CreateSemanticFingerprint(),
                reparsed.CreateSemanticFingerprint(),
                StringComparison.Ordinal)
            || !string.Equals(
                edited.CreateSemanticFingerprint(includeTrainerIds: false),
                reparsed.CreateSemanticFingerprint(includeTrainerIds: false),
                StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "The rebuilt Trainer Pools file did not reparse to the complete reviewed semantic state.",
                expected: "Complete semantic rebuild and reparse equality",
                code: ZaTrainerPoolsDiagnosticCodes.VerificationFailed));
            return false;
        }

        editedDocument = reparsed;
        return true;
    }

    public static IReadOnlyList<ValidationDiagnostic> ValidateEditedState(
        ZaTrainerPoolsLoadedState sourceState,
        ZaTrainerPoolDataDocument editedDocument)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        _ = BuildPools(
            editedDocument,
            sourceState.Identities,
            sourceState.ReferencedTableIds,
            diagnostics);
        ValidateCompleteData(
            editedDocument,
            sourceState.Identities,
            sourceState.ReferencedTableIds,
            diagnostics);
        return diagnostics;
    }

    private static bool TryReplaceExactlyOnce(
        ZaTrainerPoolDataTable table,
        string oldValue,
        string newValue,
        ICollection<ValidationDiagnostic> diagnostics,
        bool allowExistingReplacementValue = false)
    {
        var matches = table.Appearances
            .Where(appearance => appearance is not null)
            .Select(appearance => appearance!)
            .Where(appearance => string.Equals(appearance.TrainerId, oldValue, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            diagnostics.Add(Error(
                $"Physical mirror '{table.Id}' does not contain exactly one selected raw trainer identity.",
                file: $"romfs/{ZaDataPaths.TrainerPoolTableDataArray}",
                field: "rawTrainerId",
                expected: "Exactly one source or destination identity per required mirror"));
            return false;
        }

        if (!allowExistingReplacementValue
            && table.Appearances.Any(appearance => appearance is not null && string.Equals(
                appearance.TrainerId,
                newValue,
                StringComparison.Ordinal)))
        {
            diagnostics.Add(Error(
                $"Physical mirror '{table.Id}' already contains the replacement raw trainer identity.",
                field: "rawTrainerId",
                expected: "No duplicate trainer identity inside a physical table"));
            return false;
        }

        matches[0].TrainerId = newValue;
        return true;
    }

    private static bool TrySwapExactlyOnce(
        ZaTrainerPoolDataTable table,
        string firstValue,
        string secondValue,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var first = table.Appearances
            .Where(appearance => appearance is not null)
            .Select(appearance => appearance!)
            .Where(appearance => string.Equals(appearance.TrainerId, firstValue, StringComparison.Ordinal))
            .ToArray();
        var second = table.Appearances
            .Where(appearance => appearance is not null)
            .Select(appearance => appearance!)
            .Where(appearance => string.Equals(appearance.TrainerId, secondValue, StringComparison.Ordinal))
            .ToArray();
        if (first.Length != 1 || second.Length != 1)
        {
            diagnostics.Add(Error(
                $"Physical mirror '{table.Id}' does not contain exactly one of each selected raw trainer identity.",
                file: $"romfs/{ZaDataPaths.TrainerPoolTableDataArray}",
                field: "rawTrainerId",
                expected: "Exactly one source and destination identity per required mirror"));
            return false;
        }

        first[0].TrainerId = secondValue;
        second[0].TrainerId = firstValue;
        return true;
    }

    private static IReadOnlyList<ZaTrainerPoolRecord> BuildPools(
        ZaTrainerPoolDataDocument document,
        IReadOnlyDictionary<string, ZaTrainerPoolIdentityRecord> identities,
        IReadOnlySet<string> referencedTableIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var candidates = new Dictionary<string, LogicalPoolCandidate>(StringComparer.Ordinal);
        foreach (var table in document.Tables.Where(table => !string.IsNullOrWhiteSpace(table.Id)))
        {
            if (!TryClassify(table.Id!, out var logicalId, out var kind, out var suffix, out var requiredSuffixes))
            {
                continue;
            }

            if (!candidates.TryGetValue(logicalId, out var candidate))
            {
                candidate = new LogicalPoolCandidate(logicalId, kind, requiredSuffixes);
                candidates.Add(logicalId, candidate);
            }

            if (!candidate.Tables.TryAdd(suffix, table))
            {
                diagnostics.Add(Error(
                    $"Logical pool '{logicalId}' has duplicate physical mirror suffix '{suffix}'.",
                    field: "tableId",
                    expected: "One physical definition per required mirror"));
            }
        }

        var pools = new List<ZaTrainerPoolRecord>();
        foreach (var candidate in candidates.Values.OrderBy(candidate => candidate.LogicalId, StringComparer.Ordinal))
        {
            var missing = candidate.RequiredSuffixes
                .Where(suffix => !candidate.Tables.ContainsKey(suffix))
                .ToArray();
            if (missing.Length > 0 || candidate.Tables.Count != candidate.RequiredSuffixes.Count)
            {
                diagnostics.Add(Warning(
                    $"Logical pool '{candidate.LogicalId}' is hidden because its physical mirror family is incomplete.",
                    field: "tableId",
                    expected: string.Join(", ", candidate.RequiredSuffixes)));
                continue;
            }

            var physicalTables = candidate.RequiredSuffixes
                .Select(suffix => candidate.Tables[suffix])
                .ToArray();
            if (!MirrorsMatch(candidate, physicalTables, diagnostics))
            {
                continue;
            }

            var representative = physicalTables[0];
            var members = representative.Appearances
                .Where(appearance => appearance is not null)
                .Select(appearance => appearance!)
                .Select(appearance =>
                {
                    var rawId = appearance.TrainerId ?? string.Empty;
                    identities.TryGetValue(rawId, out var identity);
                    return new ZaTrainerPoolMember(
                        rawId,
                        identity?.AppearanceAssetId ?? string.Empty,
                        identity?.RawRosterId ?? string.Empty,
                        identity?.RosterIndex ?? -1,
                        identity?.DisplayName ?? rawId,
                        identity?.StoredRank ?? -1,
                        identity?.TeamSize ?? 0,
                        appearance.Weight);
                })
                .ToArray();
            var physicalIds = physicalTables.Select(table => table.Id!).ToArray();
            pools.Add(new ZaTrainerPoolRecord(
                candidate.LogicalId,
                CreatePoolDisplayLabel(candidate.LogicalId, candidate.Kind, physicalIds.Length),
                candidate.Kind == ZaTrainerPoolKind.Story
                    ? "story-five-mirror"
                    : "infinity-twelve-mirror",
                candidate.Kind,
                physicalIds,
                physicalIds.Count(referencedTableIds.Contains),
                members.Length,
                members.Sum(member => member.Weight),
                members));
        }

        return pools;
    }

    private static string CreatePoolDisplayLabel(
        string logicalPoolId,
        ZaTrainerPoolKind kind,
        int mirrorCount)
    {
        if (kind == ZaTrainerPoolKind.Infinity
            && logicalPoolId.StartsWith("Infinity", StringComparison.Ordinal)
            && int.TryParse(logicalPoolId["Infinity".Length..], out var tier))
        {
            return $"Infinity tier {tier} · {mirrorCount} synchronized mirrors";
        }

        var parts = logicalPoolId.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var rank = parts.FirstOrDefault() is { Length: 5 } rankPart
            && rankPart.EndsWith("rank", StringComparison.Ordinal)
                ? rankPart[..1]
                : "?";
        var difficulty = parts.Skip(1).FirstOrDefault() switch
        {
            "easy" => "Easy",
            "hard" => "Hard",
            _ => "Story",
        };
        return $"Rank {rank} · {difficulty} · {mirrorCount} synchronized mirrors";
    }

    private static bool MirrorsMatch(
        LogicalPoolCandidate candidate,
        IReadOnlyList<ZaTrainerPoolDataTable> tables,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var expected = tables[0].Appearances
            .Select(appearance => (appearance?.TrainerId, appearance?.Weight))
            .ToArray();
        foreach (var table in tables.Skip(1))
        {
            var actual = table.Appearances
                .Select(appearance => (appearance?.TrainerId, appearance?.Weight))
                .ToArray();
            if (!expected.SequenceEqual(actual))
            {
                diagnostics.Add(Error(
                    $"Logical pool '{candidate.LogicalId}' is not synchronized across all physical mirrors.",
                    field: "tableId",
                    expected: "Identical ordered raw trainer identities and weights in every required mirror"));
                return false;
            }
        }

        if (candidate.Kind == ZaTrainerPoolKind.Infinity
            && (expected.Length != 40 || expected.Sum(entry => entry.Weight ?? 0) != 400))
        {
            diagnostics.Add(Error(
                $"Infinity pool '{candidate.LogicalId}' does not have the production-safe 40-row, weight-400 shape.",
                field: "weight",
                expected: "40 rows and total weight 400"));
            return false;
        }

        return true;
    }

    private static void ValidateCompleteData(
        ZaTrainerPoolDataDocument document,
        IReadOnlyDictionary<string, ZaTrainerPoolIdentityRecord> identities,
        IReadOnlySet<string> referencedTableIds,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var tableIds = new HashSet<string>(StringComparer.Ordinal);
        var appearanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in document.Groups)
        {
            if (group is null || !group.HasTables)
            {
                diagnostics.Add(Error(
                    "Trainer Pools contains a null group or omitted table vector.",
                    expected: "Materialized complete database groups"));
                continue;
            }

            foreach (var table in group.Tables)
            {
                if (table is null || !table.HasId || string.IsNullOrWhiteSpace(table.Id) || !table.HasAppearances)
                {
                    diagnostics.Add(Error(
                        "Trainer Pools contains a null or incomplete physical table.",
                        expected: "Materialized table ID and appearance vector"));
                    continue;
                }

                if (!tableIds.Add(table.Id))
                {
                    diagnostics.Add(Error(
                        $"Trainer Pools table ID '{table.Id}' is duplicated.",
                        field: "tableId",
                        expected: "Globally unique table IDs"));
                }

                var trainerIds = new HashSet<string>(StringComparer.Ordinal);
                var totalWeight = 0L;
                foreach (var appearance in table.Appearances)
                {
                    if (appearance is null
                        || !appearance.HasId
                        || string.IsNullOrWhiteSpace(appearance.Id)
                        || !appearance.HasTrainerId
                        || string.IsNullOrWhiteSpace(appearance.TrainerId))
                    {
                        diagnostics.Add(Error(
                            $"Trainer Pools table '{table.Id}' contains a null or incomplete appearance row.",
                            expected: "Materialized appearance ID and raw trainer identity"));
                        continue;
                    }

                    if (!appearanceIds.Add(appearance.Id))
                    {
                        diagnostics.Add(Error(
                            $"Trainer Pools appearance ID '{appearance.Id}' is duplicated.",
                            field: "appearanceId",
                            expected: "Globally unique appearance IDs"));
                    }

                    if (!trainerIds.Add(appearance.TrainerId))
                    {
                        diagnostics.Add(Error(
                            $"Trainer Pools table '{table.Id}' repeats raw trainer identity '{appearance.TrainerId}'.",
                            field: "rawTrainerId",
                            expected: "No repeated raw trainer identity within a physical table"));
                    }

                    if (!appearance.HasWeight || appearance.Weight <= 0)
                    {
                        diagnostics.Add(Error(
                            $"Trainer Pools appearance '{appearance.Id}' has a non-positive or omitted weight.",
                            field: "weight",
                            expected: "Positive materialized weight"));
                    }
                    else
                    {
                        totalWeight = checked(totalWeight + appearance.Weight);
                    }

                    if (!identities.TryGetValue(appearance.TrainerId, out var identity)
                        || identity.RosterIndex < 0)
                    {
                        diagnostics.Add(Error(
                            $"Raw trainer identity '{appearance.TrainerId}' does not resolve through appearance mapping and roster data.",
                            field: "rawTrainerId",
                            expected: "Exact appearance mapping and roster record"));
                    }
                }

                if (table.Appearances.Count == 0 || totalWeight <= 0)
                {
                    diagnostics.Add(Error(
                        $"Trainer Pools table '{table.Id}' has no selectable positive-weight rows.",
                        field: "weight",
                        expected: "At least one row and positive total weight"));
                }
            }
        }

        foreach (var referencedTableId in referencedTableIds)
        {
            if (!tableIds.Contains(referencedTableId))
            {
                diagnostics.Add(Error(
                    $"Battle-trainer spawner references missing pool table '{referencedTableId}'.",
                    file: $"romfs/{ZaDataPaths.BattleTrainerSpawnerDataArray}",
                    field: "tableId",
                    expected: "Existing Trainer Pools table ID"));
            }
        }
    }

    private static IReadOnlyDictionary<string, RosterRecord> ReadRoster(
        byte[] bytes,
        IReadOnlyDictionary<int, ZaTrainerRecord> displayRecords,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = ZaTrainerTable.GetRootAsZaTrainerTable(new ByteBuffer(bytes));
        var result = new Dictionary<string, RosterRecord>(StringComparer.Ordinal);
        for (var index = 0; index < root.ValueLength; index++)
        {
            var row = root.Value(index);
            if (row is null || string.IsNullOrWhiteSpace(row.Value.TrainerId))
            {
                continue;
            }

            var teamSize = new[]
            {
                row.Value.Pokemon1,
                row.Value.Pokemon2,
                row.Value.Pokemon3,
                row.Value.Pokemon4,
                row.Value.Pokemon5,
                row.Value.Pokemon6,
            }.Count(pokemon => pokemon is not null && pokemon.Value.SpeciesId != 0);
            var displayName = displayRecords.TryGetValue(index, out var display)
                ? display.Name
                : row.Value.TrainerId!;
            if (!result.TryAdd(
                    row.Value.TrainerId!,
                    new RosterRecord(index, displayName, row.Value.Rank, teamSize)))
            {
                diagnostics.Add(Error(
                    $"Trainer roster ID '{row.Value.TrainerId}' is duplicated.",
                    file: $"romfs/{ZaDataPaths.TrainerDataArray}",
                    field: "rosterId",
                    expected: "Unique raw roster IDs"));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, ZaTrainerPoolIdentityRecord> ReadIdentities(
        byte[] bytes,
        IReadOnlyDictionary<string, RosterRecord> roster,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = ZaTrainerPoolIdentityDatabaseArray.GetRootAsZaTrainerPoolIdentityDatabaseArray(
            new ByteBuffer(bytes));
        var result = new Dictionary<string, ZaTrainerPoolIdentityRecord>(StringComparer.Ordinal);
        for (var groupIndex = 0; groupIndex < root.GroupsLength; groupIndex++)
        {
            var group = root.Groups(groupIndex);
            if (group is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < group.Value.IdentitiesLength; rowIndex++)
            {
                var row = group.Value.Identities(rowIndex);
                if (row is null
                    || string.IsNullOrWhiteSpace(row.Value.TrainerId)
                    || string.IsNullOrWhiteSpace(row.Value.AssetId)
                    || string.IsNullOrWhiteSpace(row.Value.RosterId))
                {
                    diagnostics.Add(Error(
                        "Trainer appearance mapping contains an incomplete identity row.",
                        file: $"romfs/{ZaDataPaths.TrainerPoolIdentityDataArray}",
                        expected: "Raw trainer ID, appearance asset ID, and roster ID"));
                    continue;
                }

                roster.TryGetValue(row.Value.RosterId, out var rosterRecord);
                var identity = new ZaTrainerPoolIdentityRecord(
                    row.Value.TrainerId,
                    row.Value.AssetId,
                    row.Value.RosterId,
                    rosterRecord?.Index ?? -1,
                    rosterRecord?.DisplayName ?? row.Value.RosterId,
                    rosterRecord?.StoredRank ?? -1,
                    rosterRecord?.TeamSize ?? 0);
                if (result.TryGetValue(identity.RawTrainerId, out var existing))
                {
                    if (existing != identity)
                    {
                        diagnostics.Add(Error(
                            $"Raw trainer identity '{identity.RawTrainerId}' has conflicting appearance mappings.",
                            file: $"romfs/{ZaDataPaths.TrainerPoolIdentityDataArray}",
                            field: "rawTrainerId",
                            expected: "One exact mapping per raw trainer identity"));
                    }

                    continue;
                }

                result.Add(identity.RawTrainerId, identity);
            }
        }

        return result;
    }

    private static IReadOnlySet<string> ReadReferencedTableIds(
        byte[] bytes,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var root = ZaTrainerPoolSpawnerDatabaseArray.GetRootAsZaTrainerPoolSpawnerDatabaseArray(
            new ByteBuffer(bytes));
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var groupIndex = 0; groupIndex < root.GroupsLength; groupIndex++)
        {
            var group = root.Groups(groupIndex);
            if (group is null)
            {
                continue;
            }

            for (var spawnerIndex = 0; spawnerIndex < group.Value.SpawnersLength; spawnerIndex++)
            {
                var spawner = group.Value.Spawners(spawnerIndex);
                if (spawner is null)
                {
                    continue;
                }

                for (var referenceIndex = 0;
                     referenceIndex < spawner.Value.TableReferencesLength;
                     referenceIndex++)
                {
                    var reference = spawner.Value.TableReferences(referenceIndex);
                    if (reference is null || string.IsNullOrWhiteSpace(reference.Value.TableId))
                    {
                        diagnostics.Add(Error(
                            "Battle-trainer spawner contains an incomplete pool-table reference.",
                            file: $"romfs/{ZaDataPaths.BattleTrainerSpawnerDataArray}",
                            field: "tableId",
                            expected: "Materialized pool table ID"));
                        continue;
                    }

                    result.Add(reference.Value.TableId);
                }
            }
        }

        return result;
    }

    private static bool TryClassify(
        string tableId,
        out string logicalId,
        out ZaTrainerPoolKind kind,
        out string suffix,
        out IReadOnlyList<string> requiredSuffixes)
    {
        foreach (var infinitySuffix in InfinitySuffixes)
        {
            var marker = "_" + infinitySuffix;
            if (!tableId.EndsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = tableId[..^marker.Length];
            if (prefix is "Infinity01" or "Infinity02" or "Infinity03" or "Infinity04")
            {
                logicalId = prefix;
                kind = ZaTrainerPoolKind.Infinity;
                suffix = infinitySuffix;
                requiredSuffixes = InfinitySuffixes;
                return true;
            }
        }

        foreach (var difficulty in new[] { "easy", "hard" })
        {
            foreach (var movement in StoryMovements)
            {
                var storySuffix = $"{movement}_{difficulty}_01";
                var marker = "_" + storySuffix;
                if (!tableId.EndsWith(marker, StringComparison.Ordinal))
                {
                    continue;
                }

                var prefix = tableId[..^marker.Length];
                if (prefix.Length == 5
                    && prefix[0] is >= 'A' and <= 'Y'
                    && prefix.AsSpan(1).SequenceEqual("rank"))
                {
                    logicalId = $"{prefix}_{difficulty}_01";
                    kind = ZaTrainerPoolKind.Story;
                    suffix = storySuffix;
                    requiredSuffixes = StoryMovements
                        .Select(value => $"{value}_{difficulty}_01")
                        .ToArray();
                    return true;
                }
            }
        }

        logicalId = string.Empty;
        kind = default;
        suffix = string.Empty;
        requiredSuffixes = Array.Empty<string>();
        return false;
    }

    private static ProjectFileReference Reference(ZaWorkflowFile source)
    {
        return new ProjectFileReference(source.SourceLayer, source.RelativePath);
    }

    private static ZaTrainerPoolsWorkflow EmptyWorkflow(IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ZaTrainerPoolsWorkflow(
            Array.Empty<ZaTrainerPoolRecord>(),
            new ZaTrainerPoolsWorkflowStats(0, 0, 0, 0),
            diagnostics,
            CanStage: false);
    }

    private static ValidationDiagnostic Error(
        string message,
        string? file = null,
        string? field = null,
        string? expected = null,
        string code = ZaTrainerPoolsDiagnosticCodes.Safety)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Error,
            message,
            ZaEditSessionSupport.TrainerPoolsDomain,
            file,
            field,
            expected,
            code: code);
    }

    private static ValidationDiagnostic Warning(
        string message,
        string? field = null,
        string? expected = null)
    {
        return ZaEditSessionSupport.CreateDiagnostic(
            DiagnosticSeverity.Warning,
            message,
            ZaEditSessionSupport.TrainerPoolsDomain,
            field: field,
            expected: expected,
            code: ZaTrainerPoolsDiagnosticCodes.UnsupportedMirrorShape);
    }

    private sealed record RosterRecord(
        int Index,
        string DisplayName,
        int StoredRank,
        int TeamSize);

    private sealed class LogicalPoolCandidate
    {
        public LogicalPoolCandidate(
            string logicalId,
            ZaTrainerPoolKind kind,
            IReadOnlyList<string> requiredSuffixes)
        {
            LogicalId = logicalId;
            Kind = kind;
            RequiredSuffixes = requiredSuffixes;
        }

        public string LogicalId { get; }
        public ZaTrainerPoolKind Kind { get; }
        public IReadOnlyList<string> RequiredSuffixes { get; }
        public Dictionary<string, ZaTrainerPoolDataTable> Tables { get; } = new(StringComparer.Ordinal);
    }
}
