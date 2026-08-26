// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;
using KM.Core.Projects;
using KM.SV.Data;
using KM.SV.Gifts;
using KM.SV.Raids;
using KM.SV.Trades;
using KM.SV.Workflows;

namespace KM.SV.GameModules;

public sealed class SvEventDataComparisonService
{
    private const int MaximumSourceBytesPerFile = 64 * 1024 * 1024;
    private const int MaximumSourceFiles = 16;
    private const long MaximumSourceBytes = 256L * 1024L * 1024L;
    private const int MaximumEntities = 10_000;
    private const int MaximumDifferences = 39_900;

    private static readonly IReadOnlyList<FieldDefinition<SvGiftPokemonEntry>> GiftFields = CreateGiftFields();

    private static IReadOnlyList<FieldDefinition<SvGiftPokemonEntry>> CreateGiftFields()
    {
        return
        [
        Signed(SvGiftPokemonWorkflowService.SpeciesField, "Species", row => row.SpeciesId),
        Signed(SvGiftPokemonWorkflowService.FormField, "Form", row => row.Form),
        Signed(SvGiftPokemonWorkflowService.LevelField, "Level", row => row.Level),
        Signed(SvGiftPokemonWorkflowService.HeldItemIdField, "Held item", row => row.HeldItemId),
        Signed(SvGiftPokemonWorkflowService.BallItemIdField, "Ball", row => row.BallId),
        Signed(SvGiftPokemonWorkflowService.AbilityField, "Ability mode", row => row.Ability),
        Signed(SvGiftPokemonWorkflowService.NatureField, "Nature", row => row.Nature),
        Signed(SvGiftPokemonWorkflowService.GenderField, "Gender", row => row.Gender),
        Signed(SvGiftPokemonWorkflowService.ShinyLockField, "Shiny mode", row => row.ShinyLock),
        Signed(SvGiftPokemonWorkflowService.TeraTypeField, "Tera type", row => row.TeraType),
        Signed(SvGiftPokemonWorkflowService.Move1IdField, "Move 1", row => row.Moves[0].MoveId),
        Signed(SvGiftPokemonWorkflowService.Move2IdField, "Move 2", row => row.Moves[1].MoveId),
        Signed(SvGiftPokemonWorkflowService.Move3IdField, "Move 3", row => row.Moves[2].MoveId),
        Signed(SvGiftPokemonWorkflowService.Move4IdField, "Move 4", row => row.Moves[3].MoveId),
        NullableSigned(
            SvGiftPokemonWorkflowService.FlawlessIvCountField,
            "Guaranteed perfect IVs",
            row => row.FlawlessIvCount),
        Signed(SvGiftPokemonWorkflowService.IvHpField, "HP IV", row => row.Ivs.HP),
        Signed(SvGiftPokemonWorkflowService.IvAttackField, "Attack IV", row => row.Ivs.Attack),
        Signed(SvGiftPokemonWorkflowService.IvDefenseField, "Defense IV", row => row.Ivs.Defense),
        Signed(SvGiftPokemonWorkflowService.IvSpecialAttackField, "Special Attack IV", row => row.Ivs.SpecialAttack),
        Signed(SvGiftPokemonWorkflowService.IvSpecialDefenseField, "Special Defense IV", row => row.Ivs.SpecialDefense),
        Signed(SvGiftPokemonWorkflowService.IvSpeedField, "Speed IV", row => row.Ivs.Speed),
        Signed(SvGiftPokemonWorkflowService.ScaleModeField, "Scale mode", row => row.ScaleMode),
        Signed(SvGiftPokemonWorkflowService.ScaleValueField, "Scale value", row => row.ScaleValue),
        ];

        static FieldDefinition<SvGiftPokemonEntry> Signed(
            string key,
            string label,
            Func<SvGiftPokemonEntry, long> read) => CreateSigned(key, label, read);

        static FieldDefinition<SvGiftPokemonEntry> NullableSigned(
            string key,
            string label,
            Func<SvGiftPokemonEntry, int?> read) => CreateNullableSigned(key, label, read);
    }

    private static readonly IReadOnlyList<FieldDefinition<SvTradePokemonEntry>> TradeFields = CreateTradeFields();

    private static IReadOnlyList<FieldDefinition<SvTradePokemonEntry>> CreateTradeFields()
    {
        return
        [
        Signed(SvTradePokemonWorkflowService.SpeciesField, "Species", row => row.SpeciesId),
        Signed(SvTradePokemonWorkflowService.FormField, "Form", row => row.Form),
        Signed(SvTradePokemonWorkflowService.LevelField, "Level", row => row.Level),
        Signed(SvTradePokemonWorkflowService.HeldItemIdField, "Held item", row => row.HeldItemId),
        Signed(SvTradePokemonWorkflowService.BallItemIdField, "Ball", row => row.BallId),
        Signed(SvTradePokemonWorkflowService.AbilityField, "Ability mode", row => row.Ability),
        Signed(SvTradePokemonWorkflowService.NatureField, "Nature", row => row.Nature),
        Signed(SvTradePokemonWorkflowService.GenderField, "Gender", row => row.Gender),
        Signed(SvTradePokemonWorkflowService.ShinyLockField, "Shiny mode", row => row.ShinyLock),
        Signed(SvTradePokemonWorkflowService.TeraTypeField, "Tera type", row => row.TeraType),
        Signed(SvTradePokemonWorkflowService.Move1IdField, "Move 1", row => row.Moves[0].MoveId),
        Signed(SvTradePokemonWorkflowService.Move2IdField, "Move 2", row => row.Moves[1].MoveId),
        Signed(SvTradePokemonWorkflowService.Move3IdField, "Move 3", row => row.Moves[2].MoveId),
        Signed(SvTradePokemonWorkflowService.Move4IdField, "Move 4", row => row.Moves[3].MoveId),
        NullableSigned(
            SvTradePokemonWorkflowService.FlawlessIvCountField,
            "Guaranteed perfect IVs",
            row => row.FlawlessIvCount),
        Signed(SvTradePokemonWorkflowService.IvHpField, "HP IV", row => row.Ivs.HP),
        Signed(SvTradePokemonWorkflowService.IvAttackField, "Attack IV", row => row.Ivs.Attack),
        Signed(SvTradePokemonWorkflowService.IvDefenseField, "Defense IV", row => row.Ivs.Defense),
        Signed(SvTradePokemonWorkflowService.IvSpecialAttackField, "Special Attack IV", row => row.Ivs.SpecialAttack),
        Signed(SvTradePokemonWorkflowService.IvSpecialDefenseField, "Special Defense IV", row => row.Ivs.SpecialDefense),
        Signed(SvTradePokemonWorkflowService.IvSpeedField, "Speed IV", row => row.Ivs.Speed),
        Signed(SvTradePokemonWorkflowService.ScaleModeField, "Scale mode", row => row.ScaleMode),
        Signed(SvTradePokemonWorkflowService.ScaleValueField, "Scale value", row => row.ScaleValue),
        Signed(SvTradePokemonWorkflowService.RequiredSpeciesField, "Requested species", row => row.RequiredSpeciesId),
        Signed(SvTradePokemonWorkflowService.RequiredFormField, "Requested form", row => row.RequiredForm),
        Signed(SvTradePokemonWorkflowService.TrainerIdField, "Trainer ID", row => row.TrainerId),
        Signed(SvTradePokemonWorkflowService.OtGenderField, "Original Trainer gender", row => row.OtGender),
        ];

        static FieldDefinition<SvTradePokemonEntry> Signed(
            string key,
            string label,
            Func<SvTradePokemonEntry, long> read) => CreateSigned(key, label, read);

        static FieldDefinition<SvTradePokemonEntry> NullableSigned(
            string key,
            string label,
            Func<SvTradePokemonEntry, int?> read) => CreateNullableSigned(key, label, read);
    }

    private static readonly IReadOnlyList<FieldDefinition<SvTeraRaidEntry>> RaidFields = CreateRaidFields();

    private static IReadOnlyList<FieldDefinition<SvTeraRaidEntry>> CreateRaidFields()
    {
        return
        [
        Signed(SvTeraRaidsWorkflowService.VersionField, "Game", row => row.Version),
        Signed(SvTeraRaidsWorkflowService.DifficultyField, "Difficulty", row => row.Difficulty),
        Signed(SvTeraRaidsWorkflowService.DeliveryGroupIdField, "Delivery group", row => row.DeliveryGroupId),
        Signed(SvTeraRaidsWorkflowService.SpawnRateField, "Spawn weight", row => row.SpawnRate),
        Signed(SvTeraRaidsWorkflowService.CaptureRateField, "Capture rate", row => row.CaptureRate),
        Signed(SvTeraRaidsWorkflowService.CaptureLevelField, "Capture level", row => row.CaptureLevel),
        Signed(SvTeraRaidsWorkflowService.SpeciesField, "Species", row => row.SpeciesId),
        Signed(SvTeraRaidsWorkflowService.FormField, "Form", row => row.Form),
        Signed(SvTeraRaidsWorkflowService.LevelField, "Level", row => row.Level),
        Signed(SvTeraRaidsWorkflowService.HeldItemIdField, "Held item", row => row.HeldItemId),
        Signed(SvTeraRaidsWorkflowService.BallItemIdField, "Ball", row => row.BallItemId),
        Signed(SvTeraRaidsWorkflowService.AbilityField, "Ability mode", row => row.Ability),
        Signed(SvTeraRaidsWorkflowService.NatureField, "Nature", row => row.Nature),
        Signed(SvTeraRaidsWorkflowService.GenderField, "Gender", row => row.Gender),
        Signed(SvTeraRaidsWorkflowService.ShinyLockField, "Shiny mode", row => row.ShinyLock),
        Signed(SvTeraRaidsWorkflowService.TeraTypeField, "Tera type", row => row.TeraType),
        Signed(SvTeraRaidsWorkflowService.MoveModeField, "Move mode", row => row.MoveMode),
        Signed(SvTeraRaidsWorkflowService.Move1IdField, "Move 1", row => row.Moves[0].MoveId),
        Signed(SvTeraRaidsWorkflowService.Move2IdField, "Move 2", row => row.Moves[1].MoveId),
        Signed(SvTeraRaidsWorkflowService.Move3IdField, "Move 3", row => row.Moves[2].MoveId),
        Signed(SvTeraRaidsWorkflowService.Move4IdField, "Move 4", row => row.Moves[3].MoveId),
        NullableSigned(
            SvTeraRaidsWorkflowService.FlawlessIvCountField,
            "Guaranteed perfect IVs",
            row => row.FlawlessIvCount),
        Signed(SvTeraRaidsWorkflowService.IvHpField, "HP IV", row => row.Ivs.HP),
        Signed(SvTeraRaidsWorkflowService.IvAttackField, "Attack IV", row => row.Ivs.Attack),
        Signed(SvTeraRaidsWorkflowService.IvDefenseField, "Defense IV", row => row.Ivs.Defense),
        Signed(SvTeraRaidsWorkflowService.IvSpecialAttackField, "Special Attack IV", row => row.Ivs.SpecialAttack),
        Signed(SvTeraRaidsWorkflowService.IvSpecialDefenseField, "Special Defense IV", row => row.Ivs.SpecialDefense),
        Signed(SvTeraRaidsWorkflowService.IvSpeedField, "Speed IV", row => row.Ivs.Speed),
        Signed(SvTeraRaidsWorkflowService.ScaleModeField, "Scale mode", row => row.ScaleMode),
        Signed(SvTeraRaidsWorkflowService.ScaleValueField, "Scale value", row => row.ScaleValue),
        Signed(SvTeraRaidsWorkflowService.HeightModeField, "Height mode", row => row.HeightMode),
        Signed(SvTeraRaidsWorkflowService.HeightValueField, "Height value", row => row.HeightValue),
        Signed(SvTeraRaidsWorkflowService.WeightModeField, "Weight mode", row => row.WeightMode),
        Signed(SvTeraRaidsWorkflowService.WeightValueField, "Weight value", row => row.WeightValue),
        Signed(SvTeraRaidsWorkflowService.HpMultiplierField, "HP multiplier", row => row.HpMultiplier),
        Signed(SvTeraRaidsWorkflowService.ShieldTriggerHpField, "Shield HP trigger", row => row.ShieldTriggerHp),
        Signed(SvTeraRaidsWorkflowService.ShieldTriggerTimeField, "Shield time trigger", row => row.ShieldTriggerTime),
        Signed(SvTeraRaidsWorkflowService.DoubleActionHpField, "Double action HP", row => row.DoubleActionHp),
        Signed(SvTeraRaidsWorkflowService.DoubleActionTimeField, "Double action time", row => row.DoubleActionTime),
        Signed(SvTeraRaidsWorkflowService.DoubleActionRateField, "Double action rate", row => row.DoubleActionRate),
        Text(SvTeraRaidsWorkflowService.FixedRewardTableField, "Fixed reward table", row => row.FixedRewardTableHash),
        Text(SvTeraRaidsWorkflowService.LotteryRewardTableField, "Lottery reward table", row => row.LotteryRewardTableHash),
        ];

        static FieldDefinition<SvTeraRaidEntry> Signed(
            string key,
            string label,
            Func<SvTeraRaidEntry, long> read) => CreateSigned(key, label, read);

        static FieldDefinition<SvTeraRaidEntry> NullableSigned(
            string key,
            string label,
            Func<SvTeraRaidEntry, int?> read) => CreateNullableSigned(key, label, read);

        static FieldDefinition<SvTeraRaidEntry> Text(
            string key,
            string label,
            Func<SvTeraRaidEntry, string> read) => CreateText(key, label, read);
    }

    private readonly SvCacheManager cacheManager;

    public SvEventDataComparisonService(SvCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new SvCacheManager();
    }

    public SvEventDataComparison LoadFreshBounded(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!SvWorkflowFileSource.IsScarletViolet(paths.SelectedGame))
        {
            throw new InvalidDataException(
                "Event data comparison requires a Scarlet or Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Event data comparison requires a configured base RomFS.");
        }

        lock (SvWorkflowFileSource.OutputWriteSyncRoot)
        {
            var initial = CaptureObservation(paths);
            var final = CaptureObservation(paths);
            if (!ObservationsMatch(initial, final))
            {
                throw new SvEventDataObservationChangedException();
            }

            return initial.Comparison;
        }
    }

    private CaptureResult CaptureObservation(ProjectPaths paths)
    {
        var source = new SvWorkflowFileSource(
            cacheManager,
            bypassReusableBaseCache: true,
            MaximumSourceBytesPerFile,
            MaximumSourceFiles,
            MaximumSourceBytes);
        var workspace = new ProjectWorkspaceService();
        var effectiveProject = workspace.Open(paths, DateTimeOffset.UtcNow);
        var basePaths = paths with { OutputRootPath = null };
        var baseProject = workspace.Open(basePaths, DateTimeOffset.UtcNow);
        using var sourceFingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var entries = new List<SvEventComparisonEntry>();
        var changedFieldCount = 0;
        AddEntries(
            entries,
            SvEventComparisonDomain.GiftPokemon,
            "gift",
            ReadGifts(source, baseProject, sourceFingerprint, "base"),
            ReadGifts(source, effectiveProject, sourceFingerprint, "effective"),
            row => row.GiftIndex,
            GiftFields,
            ref changedFieldCount);
        AddEntries(
            entries,
            SvEventComparisonDomain.TradePokemon,
            "trade",
            ReadTrades(source, baseProject, sourceFingerprint, "base"),
            ReadTrades(source, effectiveProject, sourceFingerprint, "effective"),
            row => row.TradeIndex,
            TradeFields,
            ref changedFieldCount);
        AddEntries(
            entries,
            SvEventComparisonDomain.EventDeliveryRaid,
            "event-raid",
            ReadEventDeliveryRaids(source, baseProject, sourceFingerprint, "base"),
            ReadEventDeliveryRaids(source, effectiveProject, sourceFingerprint, "effective"),
            row => row.EntryIndex,
            RaidFields,
            ref changedFieldCount);

        if (entries.Count > MaximumEntities)
        {
            throw new InvalidDataException(
                "Event data comparison exceeds its bounded entity limit.");
        }

        var ordered = entries
            .OrderBy(entry => entry.Domain)
            .ThenBy(entry => entry.Occurrence)
            .ToArray();
        return new CaptureResult(
            new SvEventDataComparison(
                ordered,
                ordered.Length,
                ordered.Count(entry => entry.Presence != SvEventComparisonPresence.Unchanged),
                changedFieldCount),
            Convert.ToHexStringLower(sourceFingerprint.GetHashAndReset()));
    }

    private static IReadOnlyList<SvGiftPokemonEntry> ReadGifts(
        SvWorkflowFileSource source,
        OpenedProject project,
        IncrementalHash sourceFingerprint,
        string layerRole)
    {
        var file = source.Read(project, SvDataPaths.EventAddPokemonArray);
        AppendSourceFingerprint(sourceFingerprint, layerRole, file);
        var table = global::EventAddPokemonArray.GetRootAsEventAddPokemonArray(
            new ByteBuffer(file.Bytes));
        source.EnsureBoundedTableCount(table.ValuesLength, "The S/V event gift table");
        return SvGiftPokemonWorkflowService.LoadRecords(
                file,
                SvTextLabelLookup.None(),
                SvGiftPokemonWorkflowService.SvGiftAbilityResolver.Empty,
                SvDefaultMoveResolver.Empty)
            .ToArray();
    }

    private static IReadOnlyList<SvTradePokemonEntry> ReadTrades(
        SvWorkflowFileSource source,
        OpenedProject project,
        IncrementalHash sourceFingerprint,
        string layerRole)
    {
        var tradeList = source.Read(project, SvDataPaths.EventTradeListArray);
        var tradePokemon = source.Read(project, SvDataPaths.EventTradePokemonArray);
        AppendSourceFingerprint(sourceFingerprint, layerRole, tradeList);
        AppendSourceFingerprint(sourceFingerprint, layerRole, tradePokemon);
        var listTable = global::EventTradeListArray.GetRootAsEventTradeListArray(
            new ByteBuffer(tradeList.Bytes));
        var pokemonTable = global::EventTradePokemonArray.GetRootAsEventTradePokemonArray(
            new ByteBuffer(tradePokemon.Bytes));
        source.EnsureBoundedTableCount(listTable.ValuesLength, "The S/V event trade request table");
        source.EnsureBoundedTableCount(pokemonTable.ValuesLength, "The S/V event trade Pokemon table");
        return SvTradePokemonWorkflowService.LoadRecords(
                tradeList,
                tradePokemon,
                SvTextLabelLookup.None(),
                SvTradePokemonWorkflowService.SvTradeAbilityResolver.Empty,
                SvDefaultMoveResolver.Empty)
            .ToArray();
    }

    private static IReadOnlyList<SvTeraRaidEntry> ReadEventDeliveryRaids(
        SvWorkflowFileSource source,
        OpenedProject project,
        IncrementalHash sourceFingerprint,
        string layerRole)
    {
        var definition = SvTeraRaidsWorkflowService.EnemySourceDefinitions.Single(candidate =>
            string.Equals(candidate.SourceKey, "delivery", StringComparison.Ordinal));
        var file = source.Read(project, definition.VirtualPath);
        AppendSourceFingerprint(sourceFingerprint, layerRole, file);
        var rows = SvTeraRaidsWorkflowService.ReadRaidRows(
            file.Bytes,
            source,
            includeEditorMetadata: true);
        var sourceRows = new SvTeraRaidsWorkflowService.RaidEnemySourceRows(
            definition,
            file,
            rows);
        return SvTeraRaidsWorkflowService.BuildRaidEntries(
                sourceRows,
                SvTextLabelLookup.None(),
                SvTeraRaidsWorkflowService.SvTeraRaidAbilityResolver.Empty,
                SvDefaultMoveResolver.Empty,
                new Dictionary<string, SvTeraRaidRewardTableRecord>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, SvTeraRaidRewardTableRecord>(StringComparer.OrdinalIgnoreCase),
                selectedGame: null,
                includeEditorMetadata: true)
            .ToArray();
    }

    private static void AddEntries<TRow>(
        ICollection<SvEventComparisonEntry> destination,
        SvEventComparisonDomain domain,
        string identityPrefix,
        IReadOnlyList<TRow> baseRows,
        IReadOnlyList<TRow> effectiveRows,
        Func<TRow, int> occurrence,
        IReadOnlyList<FieldDefinition<TRow>> fields,
        ref int changedFieldCount)
    {
        var baseByOccurrence = IndexRows(baseRows, occurrence, domain, "base");
        var effectiveByOccurrence = IndexRows(effectiveRows, occurrence, domain, "effective");
        foreach (var index in baseByOccurrence.Keys
                     .Union(effectiveByOccurrence.Keys)
                     .Order())
        {
            baseByOccurrence.TryGetValue(index, out var baseRow);
            effectiveByOccurrence.TryGetValue(index, out var effectiveRow);
            var differences = new List<SvEventFieldDifference>();
            SvEventComparisonPresence presence;
            if (baseRow is null)
            {
                presence = SvEventComparisonPresence.Added;
            }
            else if (effectiveRow is null)
            {
                presence = SvEventComparisonPresence.Removed;
            }
            else
            {
                foreach (var field in fields)
                {
                    var baseValue = field.Read(baseRow);
                    var effectiveValue = field.Read(effectiveRow);
                    if (baseValue != effectiveValue)
                    {
                        if (changedFieldCount >= MaximumDifferences)
                        {
                            throw new InvalidDataException(
                                "Event data comparison exceeds its bounded field-difference limit.");
                        }

                        differences.Add(new SvEventFieldDifference(
                            field.Key,
                            field.Label,
                            baseValue,
                            effectiveValue));
                        changedFieldCount = checked(changedFieldCount + 1);
                    }
                }

                presence = differences.Count == 0
                    ? SvEventComparisonPresence.Unchanged
                    : SvEventComparisonPresence.Modified;
            }

            if (destination.Count >= MaximumEntities)
            {
                throw new InvalidDataException(
                    "Event data comparison exceeds its bounded entity limit.");
            }

            destination.Add(new SvEventComparisonEntry(
                string.Create(CultureInfo.InvariantCulture, $"{identityPrefix}:{index}"),
                domain,
                index,
                presence,
                fields.Count,
                differences));
        }
    }

    private static Dictionary<int, TRow> IndexRows<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, int> occurrence,
        SvEventComparisonDomain domain,
        string layer)
    {
        var result = new Dictionary<int, TRow>();
        foreach (var row in rows)
        {
            var index = occurrence(row);
            if (index < 0 || !result.TryAdd(index, row))
            {
                throw new InvalidDataException(
                    $"The {domain} {layer} event rows do not have unique physical occurrences.");
            }
        }

        return result;
    }

    private static bool ObservationsMatch(
        CaptureResult left,
        CaptureResult right)
    {
        if (!string.Equals(left.SourceFingerprint, right.SourceFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        return ComparisonsMatch(left.Comparison, right.Comparison);
    }

    private static bool ComparisonsMatch(
        SvEventDataComparison left,
        SvEventDataComparison right)
    {
        if (left.ComparedEntityCount != right.ComparedEntityCount
            || left.ChangedEntityCount != right.ChangedEntityCount
            || left.ChangedFieldCount != right.ChangedFieldCount
            || left.Entries.Count != right.Entries.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Entries.Count; index++)
        {
            var leftEntry = left.Entries[index];
            var rightEntry = right.Entries[index];
            if (!string.Equals(leftEntry.StableIdentity, rightEntry.StableIdentity, StringComparison.Ordinal)
                || leftEntry.Domain != rightEntry.Domain
                || leftEntry.Occurrence != rightEntry.Occurrence
                || leftEntry.Presence != rightEntry.Presence
                || leftEntry.ComparedFieldCount != rightEntry.ComparedFieldCount
                || !leftEntry.Differences.SequenceEqual(rightEntry.Differences))
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendSourceFingerprint(
        IncrementalHash fingerprint,
        string layerRole,
        SvWorkflowFile source)
    {
        AppendFingerprintText(fingerprint, layerRole);
        AppendFingerprintText(fingerprint, source.VirtualPath);
        AppendFingerprintText(fingerprint, ((int)source.SourceLayer).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(fingerprint, ((int)source.FileState).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintText(fingerprint, source.Bytes.Length.ToString(CultureInfo.InvariantCulture));
        fingerprint.AppendData(source.Bytes);
    }

    private static void AppendFingerprintText(IncrementalHash fingerprint, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        fingerprint.AppendData(length);
        fingerprint.AppendData(bytes);
    }

    private static FieldDefinition<TRow> CreateSigned<TRow>(
        string key,
        string label,
        Func<TRow, long> read)
    {
        return new FieldDefinition<TRow>(key, label, row => Scalar(read(row)));
    }

    private static FieldDefinition<TRow> CreateNullableSigned<TRow>(
        string key,
        string label,
        Func<TRow, int?> read)
    {
        return new FieldDefinition<TRow>(key, label, row =>
            read(row) is { } value ? Scalar(value) : NullScalar());
    }

    private static FieldDefinition<TRow> CreateText<TRow>(
        string key,
        string label,
        Func<TRow, string> read)
    {
        return new FieldDefinition<TRow>(key, label, row =>
            new SvEventComparisonScalar(
                SvEventComparisonScalarKind.Text,
                read(row)));
    }

    private static SvEventComparisonScalar Scalar(long value)
    {
        return new SvEventComparisonScalar(
            SvEventComparisonScalarKind.SignedInteger,
            value.ToString(CultureInfo.InvariantCulture));
    }

    private static SvEventComparisonScalar NullScalar()
    {
        return new SvEventComparisonScalar(SvEventComparisonScalarKind.Null, null);
    }

    private sealed record FieldDefinition<TRow>(
        string Key,
        string Label,
        Func<TRow, SvEventComparisonScalar> Read);

    private sealed record CaptureResult(
        SvEventDataComparison Comparison,
        string SourceFingerprint);
}
