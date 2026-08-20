// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.ZA.AngeFight;

namespace KM.ZA.Moves;

internal sealed record ZaMovePlayerDamageValues(
    int AttackId,
    int RuntimeMoveId,
    int DefaultDamage,
    int PlayerDamage,
    float HitInterval);

/// <summary>
/// Reads the canonical player-damage tags owned by boss move attack rows.
/// Variable-length edits append replacement FlatBuffer strings and repoint
/// only the owning TargetTagList references.
/// </summary>
internal sealed class ZaMovePlayerDamageDataDocument
{
    private const string DamagePrefix = "SimpleDamage#DefaultProperty=";
    private const string PlayerDamageSeparator = "#PlayerDamage=";
    private const int BossRuntimeMoveIdMinimum = 2000;
    private const int BossRuntimeMoveIdMaximum = 2999;

    private readonly byte[] originalBytes;
    private readonly IReadOnlyDictionary<int, LocatedDamage> locatedByAttackId;

    private ZaMovePlayerDamageDataDocument(
        byte[] originalBytes,
        IReadOnlyDictionary<int, LocatedDamage> locatedByAttackId)
    {
        this.originalBytes = originalBytes;
        this.locatedByAttackId = locatedByAttackId;
        Values = locatedByAttackId.Values
            .Select(value => value.Values)
            .OrderBy(value => value.RuntimeMoveId)
            .ThenBy(value => value.AttackId)
            .ToArray();
    }

    public const string FieldPrefix = "playerDamage.";

    public const int MinimumPlayerDamage = 0;

    public const int MaximumPlayerDamage = 999;

    public IReadOnlyList<ZaMovePlayerDamageValues> Values { get; }

    public static ZaMovePlayerDamageDataDocument Parse(
        byte[] bytes,
        int? maximumVectorEntries = null,
        int? maximumAggregateVectorEntries = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var reader = new ZaAngeFlatBufferReader(
            bytes,
            maximumVectorEntries,
            maximumAggregateVectorEntries);
        var root = reader.ReadRootTable("attack parameter array root", maximumFieldCount: 1);
        var groups = reader.ReadTableVector(
            root,
            fieldIndex: 0,
            "attack parameter groups");
        var seenBossAttackIds = new HashSet<int>();
        var located = new Dictionary<int, LocatedDamage>();

        foreach (var group in groups)
        {
            reader.ValidateTable(group, "attack parameter group", maximumFieldCount: 1);
            var rows = reader.ReadTableVector(
                group,
                fieldIndex: 0,
                "attack parameter rows");
            foreach (var row in rows)
            {
                reader.ValidateTable(row, "attack parameter row", maximumFieldCount: 15);
                var runtimeMoveIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 10,
                    "WazaId");
                var runtimeMoveId = runtimeMoveIdOffset is null
                    ? 0
                    : reader.ReadInt32(runtimeMoveIdOffset.Value, "WazaId");
                if (!IsBossRuntimeMove(runtimeMoveId))
                {
                    continue;
                }

                var attackIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 0,
                    $"WazaId {runtimeMoveId} AttackId");
                var attackId = attackIdOffset is null
                    ? 0
                    : reader.ReadInt32(
                        attackIdOffset.Value,
                        $"WazaId {runtimeMoveId} AttackId");
                if (attackId <= 0)
                {
                    throw new InvalidDataException(
                        $"Boss runtime move {runtimeMoveId} contains an invalid AttackId {attackId}.");
                }

                if (!seenBossAttackIds.Add(attackId))
                {
                    throw new InvalidDataException(
                        $"Attack parameter data contains more than one boss move AttackId {attackId} row.");
                }

                var targetTags = reader.ReadStringVectorEntries(
                    row,
                    fieldIndex: 6,
                    $"AttackId {attackId} TargetTagList");
                var damageTags = targetTags
                    .Select(entry => new
                    {
                        Entry = entry,
                        Parsed = TryParseDamageTag(
                            entry.Value,
                            out var defaultDamage,
                            out var playerDamage)
                            ? new ParsedDamage(defaultDamage, playerDamage)
                            : null,
                    })
                    .Where(candidate => candidate.Parsed is not null)
                    .ToArray();
                if (damageTags.Length == 0)
                {
                    continue;
                }

                if (damageTags.Length > 1)
                {
                    throw new InvalidDataException(
                        $"Boss move AttackId {attackId} must contain at most one canonical "
                        + $"SimpleDamage target tag, but {damageTags.Length} were found.");
                }

                var hitIntervalOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 8,
                    $"AttackId {attackId} HitInterval");
                var hitInterval = hitIntervalOffset is null
                    ? 0.0f
                    : reader.ReadSingle(
                        hitIntervalOffset.Value,
                        $"AttackId {attackId} HitInterval");
                if (!float.IsFinite(hitInterval))
                {
                    throw new InvalidDataException(
                        $"Boss move AttackId {attackId} contains a nonfinite HitInterval.");
                }

                var damage = damageTags[0];
                ValidatePlayerDamage(damage.Parsed!.PlayerDamage, attackId);
                located.Add(
                    attackId,
                    new LocatedDamage(
                        new ZaMovePlayerDamageValues(
                            attackId,
                            runtimeMoveId,
                            damage.Parsed.DefaultDamage,
                            damage.Parsed.PlayerDamage,
                            hitInterval),
                        damage.Entry.ReferenceOffset));
            }
        }

        return new ZaMovePlayerDamageDataDocument(bytes.ToArray(), located);
    }

    public static string Field(int attackId)
    {
        if (attackId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackId));
        }

        return $"{FieldPrefix}{attackId.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseField(string? field, out int attackId)
    {
        attackId = 0;
        return !string.IsNullOrWhiteSpace(field)
            && field.StartsWith(FieldPrefix, StringComparison.Ordinal)
            && int.TryParse(
                field.AsSpan(FieldPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out attackId)
            && attackId > 0;
    }

    public static bool IsBossRuntimeMove(int runtimeMoveId) =>
        runtimeMoveId is >= BossRuntimeMoveIdMinimum and <= BossRuntimeMoveIdMaximum;

    public static bool IsForBaseMove(int runtimeMoveId, int baseMoveId) =>
        IsBossRuntimeMove(runtimeMoveId)
        && baseMoveId is >= 0 and < 1000
        && runtimeMoveId % 1000 == baseMoveId;

    public static bool IsForBaseMove(ZaMovePlayerDamageValues value, int baseMoveId)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsForBaseMove(value.RuntimeMoveId, baseMoveId);
    }

    public IReadOnlyList<ZaMovePlayerDamageValues> GetValuesForRuntimeMove(int runtimeMoveId)
    {
        ValidateRuntimeMoveId(runtimeMoveId);
        return Values
            .Where(value => value.RuntimeMoveId == runtimeMoveId)
            .OrderBy(value => value.AttackId)
            .ToArray();
    }

    public string GetCanonicalFingerprint(int runtimeMoveId) =>
        CreateFingerprint(GetValuesForRuntimeMove(runtimeMoveId), includePlayerDamage: true);

    public string GetCanonicalShapeFingerprint(int runtimeMoveId) =>
        CreateFingerprint(GetValuesForRuntimeMove(runtimeMoveId), includePlayerDamage: false);

    public bool HasSameCanonicalShape(
        ZaMovePlayerDamageDataDocument other,
        int runtimeMoveId)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(
            GetCanonicalShapeFingerprint(runtimeMoveId),
            other.GetCanonicalShapeFingerprint(runtimeMoveId),
            StringComparison.Ordinal);
    }

    public byte[] Write(IReadOnlyList<ZaMovePlayerDamageValues> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var requested = ValidateAndIndex(values);
        var replacements = Values
            .Select(current => new
            {
                Current = locatedByAttackId[current.AttackId],
                Requested = requested[current.AttackId],
            })
            .Where(change => change.Current.Values.PlayerDamage != change.Requested.PlayerDamage)
            .ToArray();
        if (replacements.Length == 0)
        {
            return originalBytes.ToArray();
        }

        var output = new List<byte>(checked(originalBytes.Length + (replacements.Length * 80)));
        output.AddRange(originalBytes);
        var changedReferences = new HashSet<int>();

        foreach (var replacement in replacements)
        {
            while ((output.Count & 3) != 0)
            {
                output.Add(0);
            }

            var encoded = ZaAngeFlatBufferReader.StrictUtf8.GetBytes(
                CreateDamageTag(
                    replacement.Current.Values.DefaultDamage,
                    replacement.Requested.PlayerDamage));
            var stringOffset = output.Count;
            var lengthBytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, encoded.Length);
            output.AddRange(lengthBytes);
            output.AddRange(encoded);
            output.Add(0);

            var referenceOffset = replacement.Current.TargetTagReferenceOffset;
            var relativeOffset = checked(stringOffset - referenceOffset);
            if (relativeOffset <= 0)
            {
                throw new InvalidDataException(
                    $"AttackId {replacement.Current.Values.AttackId} replacement string does not "
                    + "follow its FlatBuffer reference.");
            }

            var relativeBytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(
                relativeBytes,
                checked((uint)relativeOffset));
            for (var index = 0; index < relativeBytes.Length; index++)
            {
                output[referenceOffset + index] = relativeBytes[index];
            }

            if (!changedReferences.Add(referenceOffset))
            {
                throw new InvalidDataException(
                    "Boss player-damage rows unexpectedly share a TargetTagList reference.");
            }
        }

        var result = output.ToArray();
        VerifyOnlyReferencesAndAppendedStringsChanged(
            originalBytes,
            result,
            changedReferences);

        var reparsed = Parse(result);
        if (reparsed.Values.Count != requested.Count)
        {
            throw new InvalidDataException(
                "Boss player-damage verification found a different number of canonical rows after patching.");
        }

        var actual = reparsed.Values.ToDictionary(value => value.AttackId);
        foreach (var expected in requested.Values)
        {
            if (!actual.TryGetValue(expected.AttackId, out var actualValue)
                || actualValue != expected)
            {
                throw new InvalidDataException(
                    $"AttackId {expected.AttackId} verification failed after patching boss player damage.");
            }
        }

        return result;
    }

    private IReadOnlyDictionary<int, ZaMovePlayerDamageValues> ValidateAndIndex(
        IReadOnlyList<ZaMovePlayerDamageValues> values)
    {
        if (values.Count != Values.Count)
        {
            throw new InvalidDataException(
                $"Boss player-damage selection must contain exactly {Values.Count} canonical attack rows.");
        }

        var requested = new Dictionary<int, ZaMovePlayerDamageValues>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!locatedByAttackId.TryGetValue(value.AttackId, out var current))
            {
                throw new InvalidDataException(
                    $"AttackId {value.AttackId} is not an editable boss player-damage row.");
            }

            if (!requested.TryAdd(value.AttackId, value))
            {
                throw new InvalidDataException(
                    $"Boss player-damage selection contains duplicate AttackId {value.AttackId}.");
            }

            if (value.RuntimeMoveId != current.Values.RuntimeMoveId
                || value.DefaultDamage != current.Values.DefaultDamage
                || value.HitInterval != current.Values.HitInterval)
            {
                throw new InvalidDataException(
                    $"AttackId {value.AttackId} attempted to change non-owned boss attack metadata.");
            }

            ValidatePlayerDamage(value.PlayerDamage, value.AttackId);
        }

        return requested;
    }

    private static void ValidatePlayerDamage(int value, int attackId)
    {
        if (value is < MinimumPlayerDamage or > MaximumPlayerDamage)
        {
            throw new InvalidDataException(
                $"AttackId {attackId} damage to player must be between "
                + $"{MinimumPlayerDamage} and {MaximumPlayerDamage}.");
        }
    }

    private static void ValidateRuntimeMoveId(int runtimeMoveId)
    {
        if (!IsBossRuntimeMove(runtimeMoveId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeMoveId),
                runtimeMoveId,
                $"Boss runtime move ID must be between {BossRuntimeMoveIdMinimum} and "
                + $"{BossRuntimeMoveIdMaximum}.");
        }
    }

    private static string CreateFingerprint(
        IEnumerable<ZaMovePlayerDamageValues> values,
        bool includePlayerDamage)
    {
        var canonical = string.Join(
            "\n",
            values
                .OrderBy(value => value.AttackId)
                .Select(value => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{value.AttackId}:{value.RuntimeMoveId}:{value.DefaultDamage}:"
                    + $"{(includePlayerDamage ? value.PlayerDamage : 0)}:"
                    + $"{BitConverter.SingleToInt32Bits(value.HitInterval):X8}")));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string CreateDamageTag(int defaultDamage, int playerDamage) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{DamagePrefix}{defaultDamage}{PlayerDamageSeparator}{playerDamage}");

    private static bool TryParseDamageTag(
        string value,
        out int defaultDamage,
        out int playerDamage)
    {
        defaultDamage = 0;
        playerDamage = 0;
        if (!value.StartsWith(DamagePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(
            PlayerDamageSeparator,
            DamagePrefix.Length,
            StringComparison.Ordinal);
        if (separatorIndex < 0
            || value.IndexOf(
                PlayerDamageSeparator,
                separatorIndex + PlayerDamageSeparator.Length,
                StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var defaultDamageText = value.AsSpan(
            DamagePrefix.Length,
            separatorIndex - DamagePrefix.Length);
        var playerDamageText = value.AsSpan(separatorIndex + PlayerDamageSeparator.Length);
        return defaultDamageText.Length > 0
            && playerDamageText.Length > 0
            && int.TryParse(
                defaultDamageText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out defaultDamage)
            && int.TryParse(
                playerDamageText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out playerDamage)
            && defaultDamage >= 0
            && playerDamage >= 0;
    }

    private static void VerifyOnlyReferencesAndAppendedStringsChanged(
        byte[] original,
        byte[] output,
        IReadOnlySet<int> changedReferences)
    {
        if (output.Length <= original.Length)
        {
            throw new InvalidDataException(
                "Boss player-damage patch did not append its replacement FlatBuffer strings.");
        }

        for (var offset = 0; offset < original.Length; offset++)
        {
            if (original[offset] == output[offset])
            {
                continue;
            }

            if (!changedReferences.Any(start => offset >= start && offset < start + sizeof(uint)))
            {
                throw new InvalidDataException(
                    $"Boss player-damage patch unexpectedly changed byte 0x{offset:X}.");
            }
        }
    }

    private sealed record LocatedDamage(
        ZaMovePlayerDamageValues Values,
        int TargetTagReferenceOffset);

    private sealed record ParsedDamage(
        int DefaultDamage,
        int PlayerDamage);
}
