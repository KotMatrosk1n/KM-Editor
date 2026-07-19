// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.ZA.AngeFight;

internal sealed record ZaAngeFlowerValues(
    int BlueHp,
    int RedHp);

internal sealed record ZaAngeAttackDamageValues(
    int AttackId,
    int DamageToPokemon,
    int DamageToPlayer);

/// <summary>
/// Reads and patches the two Eternal Flower HP fields without rebuilding the
/// surrounding field-gimmick FlatBuffer.
/// </summary>
internal sealed class ZaAngeFlowerDataDocument
{
    private const string BlueFlowerId = "TOWERFLOWER_BLUE";
    private const string RedFlowerId = "TOWERFLOWER_RED";

    private readonly byte[] originalBytes;
    private readonly int blueHpOffset;
    private readonly int redHpOffset;

    private ZaAngeFlowerDataDocument(
        byte[] originalBytes,
        int blueHpOffset,
        int redHpOffset,
        ZaAngeFlowerValues values)
    {
        this.originalBytes = originalBytes;
        this.blueHpOffset = blueHpOffset;
        this.redHpOffset = redHpOffset;
        Values = values;
    }

    public ZaAngeFlowerValues Values { get; }

    public static ZaAngeFlowerDataDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var reader = new ZaAngeFlatBufferReader(bytes);
        var root = reader.ReadRootTable("field-gimmick root", maximumFieldCount: 1);
        var rows = reader.ReadTableVector(
            root,
            fieldIndex: 0,
            "field-gimmick rows");

        LocatedFlower? blue = null;
        LocatedFlower? red = null;
        foreach (var row in rows)
        {
            reader.ValidateTable(row, "field-gimmick row", maximumFieldCount: 14);
            var idField = reader.GetFieldOffset(
                row,
                fieldIndex: 0,
                "field-gimmick GimmickId");
            if (idField is null)
            {
                continue;
            }

            var id = reader.ReadStringReference(idField.Value, "field-gimmick GimmickId");
            if (!string.Equals(id, BlueFlowerId, StringComparison.Ordinal)
                && !string.Equals(id, RedFlowerId, StringComparison.Ordinal))
            {
                continue;
            }

            var hpOffset = reader.GetRequiredScalarFieldOffset(
                row,
                fieldIndex: 5,
                scalarSize: sizeof(int),
                $"{id} Hp");
            var hp = reader.ReadInt32(hpOffset, $"{id} Hp");
            if (hp < 1)
            {
                throw new InvalidDataException(
                    $"{id} Hp must be a positive signed 32-bit value, but the file contains {hp}.");
            }

            var located = new LocatedFlower(hp, hpOffset);
            if (string.Equals(id, BlueFlowerId, StringComparison.Ordinal))
            {
                if (blue is not null)
                {
                    throw new InvalidDataException(
                        $"Field-gimmick data contains more than one {BlueFlowerId} row.");
                }

                blue = located;
            }
            else
            {
                if (red is not null)
                {
                    throw new InvalidDataException(
                        $"Field-gimmick data contains more than one {RedFlowerId} row.");
                }

                red = located;
            }
        }

        if (blue is null || red is null)
        {
            var missing = blue is null ? BlueFlowerId : RedFlowerId;
            throw new InvalidDataException(
                $"Field-gimmick data does not contain exactly one {missing} row.");
        }

        return new ZaAngeFlowerDataDocument(
            bytes.ToArray(),
            blue.HpOffset,
            red.HpOffset,
            new ZaAngeFlowerValues(blue.Hp, red.Hp));
    }

    public byte[] Write(ZaAngeFlowerValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateHp(values.BlueHp, "Blue Flower HP");
        ValidateHp(values.RedHp, "Red Flower HP");

        if (values == Values)
        {
            return originalBytes.ToArray();
        }

        var output = originalBytes.ToArray();
        var changedOffsets = new HashSet<int>();
        if (values.BlueHp != Values.BlueHp)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(blueHpOffset, sizeof(int)),
                values.BlueHp);
            changedOffsets.Add(blueHpOffset);
        }

        if (values.RedHp != Values.RedHp)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                output.AsSpan(redHpOffset, sizeof(int)),
                values.RedHp);
            changedOffsets.Add(redHpOffset);
        }

        VerifyOnlyScalarFieldsChanged(originalBytes, output, changedOffsets);
        var reparsed = Parse(output);
        if (reparsed.Values != values)
        {
            throw new InvalidDataException(
                "Ange Flower HP verification failed after patching the field-gimmick FlatBuffer.");
        }

        return output;
    }

    public bool HasOnlyOwnedDifferencesFrom(ZaAngeFlowerDataDocument vanilla)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        if (originalBytes.Length != vanilla.originalBytes.Length
            || blueHpOffset != vanilla.blueHpOffset
            || redHpOffset != vanilla.redHpOffset)
        {
            return false;
        }

        for (var offset = 0; offset < originalBytes.Length; offset++)
        {
            if (originalBytes[offset] == vanilla.originalBytes[offset])
            {
                continue;
            }

            if (!IsWithinScalar(offset, blueHpOffset)
                && !IsWithinScalar(offset, redHpOffset))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateHp(int value, string label)
    {
        if (value < 1)
        {
            throw new InvalidDataException(
                $"{label} must be between 1 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void VerifyOnlyScalarFieldsChanged(
        byte[] original,
        byte[] output,
        IReadOnlySet<int> changedOffsets)
    {
        if (original.Length != output.Length)
        {
            throw new InvalidDataException(
                "Ange Flower HP patch unexpectedly changed the field-gimmick file length.");
        }

        for (var offset = 0; offset < original.Length; offset++)
        {
            if (original[offset] == output[offset])
            {
                continue;
            }

            if (!changedOffsets.Any(start => offset >= start && offset < start + sizeof(int)))
            {
                throw new InvalidDataException(
                    $"Ange Flower HP patch unexpectedly changed byte 0x{offset:X}.");
            }
        }
    }

    private static bool IsWithinScalar(int offset, int scalarOffset)
    {
        return offset >= scalarOffset && offset < scalarOffset + sizeof(int);
    }

    private sealed record LocatedFlower(
        int Hp,
        int HpOffset);
}

/// <summary>
/// Reads the ten timeline-referenced, non-Ember direct-damage attack rows.
/// Variable-length changes are appended as new FlatBuffer strings and only the
/// owning TargetTagList uoffset is repointed, preserving every unrelated byte.
/// </summary>
internal sealed class ZaAngeAttackDataDocument
{
    private const string DamagePrefix = "SimpleDamage#DefaultProperty=";
    private const string PlayerDamageSeparator = "#PlayerDamage=";

    private static readonly int[] RequiredAttackIds =
    [
        2146,
        2147,
        2148,
        2149,
        2150,
        2153,
        2154,
        2155,
        2156,
        2157,
    ];

    private readonly byte[] originalBytes;
    private readonly IReadOnlyDictionary<int, LocatedAttack> locatedByAttackId;

    private ZaAngeAttackDataDocument(
        byte[] originalBytes,
        IReadOnlyDictionary<int, LocatedAttack> locatedByAttackId)
    {
        this.originalBytes = originalBytes;
        this.locatedByAttackId = locatedByAttackId;
        Values = RequiredAttackIds
            .Select(attackId => locatedByAttackId[attackId].Values)
            .ToArray();
    }

    public IReadOnlyList<ZaAngeAttackDamageValues> Values { get; }

    public static IReadOnlyList<int> AttackIds => RequiredAttackIds;

    public static ZaAngeAttackDataDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var reader = new ZaAngeFlatBufferReader(bytes);
        var root = reader.ReadRootTable("attack parameter array root", maximumFieldCount: 1);
        var groups = reader.ReadTableVector(
            root,
            fieldIndex: 0,
            "attack parameter groups");
        var required = RequiredAttackIds.ToHashSet();
        var located = new Dictionary<int, LocatedAttack>();

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
                var attackIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 0,
                    "AttackId");
                var attackId = attackIdOffset is null
                    ? 0
                    : reader.ReadInt32(attackIdOffset.Value, "AttackId");
                if (!required.Contains(attackId))
                {
                    continue;
                }

                if (located.ContainsKey(attackId))
                {
                    throw new InvalidDataException(
                        $"Attack parameter data contains more than one AttackId {attackId} row.");
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
                            out var damageToPokemon,
                            out var damageToPlayer)
                            ? new ParsedDamage(damageToPokemon, damageToPlayer)
                            : null,
                    })
                    .Where(candidate => candidate.Parsed is not null)
                    .ToArray();
                if (damageTags.Length != 1)
                {
                    throw new InvalidDataException(
                        $"AttackId {attackId} must contain exactly one canonical SimpleDamage target tag, "
                        + $"but {damageTags.Length} were found.");
                }

                var damage = damageTags[0];
                located.Add(
                    attackId,
                    new LocatedAttack(
                        new ZaAngeAttackDamageValues(
                            attackId,
                            damage.Parsed!.DamageToPokemon,
                            damage.Parsed.DamageToPlayer),
                        damage.Entry.ReferenceOffset));
            }
        }

        var missing = RequiredAttackIds.Where(attackId => !located.ContainsKey(attackId)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "Attack parameter data is missing required Ange attack row(s): "
                + string.Join(", ", missing.Select(value => value.ToString(CultureInfo.InvariantCulture)))
                + ".");
        }

        return new ZaAngeAttackDataDocument(bytes.ToArray(), located);
    }

    public byte[] Write(IReadOnlyList<ZaAngeAttackDamageValues> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var requested = ValidateAndIndex(values);
        var replacements = RequiredAttackIds
            .Select(attackId => new
            {
                AttackId = attackId,
                Current = locatedByAttackId[attackId],
                Requested = requested[attackId],
            })
            .Where(change => change.Current.Values != change.Requested)
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
                    replacement.Requested.DamageToPokemon,
                    replacement.Requested.DamageToPlayer));
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
                    $"AttackId {replacement.AttackId} replacement string does not follow its FlatBuffer reference.");
            }

            var relativeBytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(relativeBytes, checked((uint)relativeOffset));
            for (var index = 0; index < relativeBytes.Length; index++)
            {
                output[referenceOffset + index] = relativeBytes[index];
            }

            changedReferences.Add(referenceOffset);
        }

        var result = output.ToArray();
        VerifyOnlyReferencesAndAppendedStringsChanged(
            originalBytes,
            result,
            changedReferences);

        var reparsed = Parse(result);
        var actual = reparsed.Values.ToDictionary(value => value.AttackId);
        foreach (var attackId in RequiredAttackIds)
        {
            if (actual[attackId] != requested[attackId])
            {
                throw new InvalidDataException(
                    $"AttackId {attackId} verification failed after patching Ange direct damage.");
            }
        }

        return result;
    }

    public bool HasOnlyOwnedDifferencesFrom(ZaAngeAttackDataDocument vanilla)
    {
        ArgumentNullException.ThrowIfNull(vanilla);
        if (originalBytes.Length < vanilla.originalBytes.Length)
        {
            return false;
        }

        var ownedReferenceOffsets = new HashSet<int>();
        foreach (var attackId in RequiredAttackIds)
        {
            var effectiveOffset = locatedByAttackId[attackId].TargetTagReferenceOffset;
            var vanillaOffset = vanilla.locatedByAttackId[attackId].TargetTagReferenceOffset;
            if (effectiveOffset != vanillaOffset)
            {
                return false;
            }

            ownedReferenceOffsets.Add(effectiveOffset);
        }

        for (var offset = 0; offset < vanilla.originalBytes.Length; offset++)
        {
            if (originalBytes[offset] == vanilla.originalBytes[offset])
            {
                continue;
            }

            if (!ownedReferenceOffsets.Any(start =>
                    offset >= start && offset < start + sizeof(uint)))
            {
                return false;
            }
        }

        return HasOnlyCanonicalAppendedDamageStrings(
            originalBytes,
            vanilla.originalBytes.Length);
    }

    private static IReadOnlyDictionary<int, ZaAngeAttackDamageValues> ValidateAndIndex(
        IReadOnlyList<ZaAngeAttackDamageValues> values)
    {
        if (values.Count != RequiredAttackIds.Length)
        {
            throw new InvalidDataException(
                $"Ange damage selection must contain exactly {RequiredAttackIds.Length} attack rows.");
        }

        var indexed = new Dictionary<int, ZaAngeAttackDamageValues>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!RequiredAttackIds.Contains(value.AttackId))
            {
                throw new InvalidDataException(
                    $"AttackId {value.AttackId} is not an editable Ange direct-damage row.");
            }

            if (!indexed.TryAdd(value.AttackId, value))
            {
                throw new InvalidDataException(
                    $"Ange damage selection contains duplicate AttackId {value.AttackId}.");
            }

            ValidateDamage(value.DamageToPokemon, value.AttackId, "damage to Pokemon");
            ValidateDamage(value.DamageToPlayer, value.AttackId, "damage to player");
        }

        var missing = RequiredAttackIds.Where(attackId => !indexed.ContainsKey(attackId)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "Ange damage selection is missing AttackId "
                + string.Join(", ", missing.Select(value => value.ToString(CultureInfo.InvariantCulture)))
                + ".");
        }

        return indexed;
    }

    private static void ValidateDamage(int value, int attackId, string field)
    {
        if (value < 0)
        {
            throw new InvalidDataException(
                $"AttackId {attackId} {field} must be between 0 and "
                + $"{int.MaxValue.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static string CreateDamageTag(int damageToPokemon, int damageToPlayer)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{DamagePrefix}{damageToPokemon}{PlayerDamageSeparator}{damageToPlayer}");
    }

    private static bool TryParseDamageTag(
        string value,
        out int damageToPokemon,
        out int damageToPlayer)
    {
        damageToPokemon = 0;
        damageToPlayer = 0;
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

        var pokemonText = value.AsSpan(
            DamagePrefix.Length,
            separatorIndex - DamagePrefix.Length);
        var playerText = value.AsSpan(separatorIndex + PlayerDamageSeparator.Length);
        return pokemonText.Length > 0
            && playerText.Length > 0
            && int.TryParse(
                pokemonText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out damageToPokemon)
            && int.TryParse(
                playerText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out damageToPlayer)
            && damageToPokemon >= 0
            && damageToPlayer >= 0;
    }

    private static void VerifyOnlyReferencesAndAppendedStringsChanged(
        byte[] original,
        byte[] output,
        IReadOnlySet<int> changedReferences)
    {
        if (output.Length <= original.Length)
        {
            throw new InvalidDataException(
                "Ange direct-damage patch did not append its replacement FlatBuffer strings.");
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
                    $"Ange direct-damage patch unexpectedly changed byte 0x{offset:X}.");
            }
        }
    }

    private static bool HasOnlyCanonicalAppendedDamageStrings(
        byte[] bytes,
        int originalLength)
    {
        var offset = originalLength;
        while (offset < bytes.Length)
        {
            while ((offset & 3) != 0 && offset < bytes.Length)
            {
                if (bytes[offset] != 0)
                {
                    return false;
                }

                offset++;
            }

            if (offset == bytes.Length
                || offset > bytes.Length - sizeof(int))
            {
                return false;
            }

            var byteLength = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset, sizeof(int)));
            if (byteLength <= 0)
            {
                return false;
            }

            var payloadOffset = checked(offset + sizeof(int));
            var terminatorPosition = (long)payloadOffset + byteLength;
            if (terminatorPosition >= bytes.Length
                || bytes[checked((int)terminatorPosition)] != 0)
            {
                return false;
            }

            string value;
            try
            {
                value = ZaAngeFlatBufferReader.StrictUtf8.GetString(
                    bytes,
                    payloadOffset,
                    byteLength);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (!TryParseDamageTag(value, out _, out _))
            {
                return false;
            }

            offset = checked((int)terminatorPosition + 1);
        }

        return true;
    }

    private sealed record LocatedAttack(
        ZaAngeAttackDamageValues Values,
        int TargetTagReferenceOffset);

    private sealed record ParsedDamage(
        int DamageToPokemon,
        int DamageToPlayer);
}

internal readonly record struct ZaAngeFlatBufferStringEntry(
    int ReferenceOffset,
    string Value);

internal static class ZaAngeBulletMappingDocument
{
    private static readonly IReadOnlyDictionary<int, int> ExpectedAttackByBulletId =
        new Dictionary<int, int>
        {
            [2004] = 2146,
            [2005] = 2147,
            [2006] = 2148,
            [2007] = 2149,
            [2008] = 2150,
            [2011] = 2153,
            [2012] = 2154,
            [2013] = 2155,
            [2014] = 2156,
            [2015] = 2157,
        };

    public static void Validate(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var reader = new ZaAngeFlatBufferReader(bytes);
        var root = reader.ReadRootTable("bullet parameter array root", maximumFieldCount: 1);
        var groups = reader.ReadTableVector(
            root,
            fieldIndex: 0,
            "bullet parameter groups");
        var actualByBulletId = new Dictionary<int, int>();

        foreach (var group in groups)
        {
            reader.ValidateTable(group, "bullet parameter group", maximumFieldCount: 1);
            var rows = reader.ReadTableVector(
                group,
                fieldIndex: 0,
                "bullet parameter rows");
            foreach (var row in rows)
            {
                reader.ValidateTable(row, "bullet parameter row", maximumFieldCount: 43);
                var bulletIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 0,
                    "BulletId");
                var bulletId = bulletIdOffset is null
                    ? 0
                    : reader.ReadInt32(bulletIdOffset.Value, "BulletId");
                if (!ExpectedAttackByBulletId.ContainsKey(bulletId))
                {
                    continue;
                }

                if (actualByBulletId.ContainsKey(bulletId))
                {
                    throw new InvalidDataException(
                        $"Bullet parameter data contains more than one BulletId {bulletId} row.");
                }

                var attackIdOffset = reader.GetFieldOffset(
                    row,
                    fieldIndex: 1,
                    $"BulletId {bulletId} AttackId");
                var attackId = attackIdOffset is null
                    ? 0
                    : reader.ReadInt32(
                        attackIdOffset.Value,
                        $"BulletId {bulletId} AttackId");
                actualByBulletId.Add(bulletId, attackId);
            }
        }

        foreach (var expected in ExpectedAttackByBulletId)
        {
            if (!actualByBulletId.TryGetValue(expected.Key, out var actualAttackId))
            {
                throw new InvalidDataException(
                    $"Bullet parameter data does not contain required Ange BulletId {expected.Key}.");
            }

            if (actualAttackId != expected.Value)
            {
                throw new InvalidDataException(
                    $"Ange BulletId {expected.Key} maps to AttackId {actualAttackId}, "
                    + $"but this editor requires AttackId {expected.Value}.");
            }
        }
    }
}

/// <summary>
/// Minimal checked FlatBuffer reader for the exact tables owned by Ange Fight.
/// It rejects unsupported schema growth, backward offsets, invalid vtables, and
/// malformed UTF-8 instead of trusting research-time physical offsets.
/// </summary>
internal sealed class ZaAngeFlatBufferReader
{
    public static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly byte[] bytes;

    public ZaAngeFlatBufferReader(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        this.bytes = bytes;
        if (bytes.Length < sizeof(uint) * 2)
        {
            throw new InvalidDataException(
                "Ange FlatBuffer is too small to contain a root table.");
        }
    }

    public int ReadRootTable(string label, int maximumFieldCount)
    {
        var relativeOffset = ReadUInt32(0, $"{label} offset");
        var tableOffset = ResolveForwardOffset(0, relativeOffset, label);
        ValidateTable(tableOffset, label, maximumFieldCount);
        return tableOffset;
    }

    public void ValidateTable(int tableOffset, string label, int maximumFieldCount)
    {
        _ = ReadTableInfo(tableOffset, label, maximumFieldCount);
    }

    public int? GetFieldOffset(int tableOffset, int fieldIndex, string label)
    {
        if (fieldIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldIndex));
        }

        var table = ReadTableInfo(tableOffset, label, maximumFieldCount: null);
        if (fieldIndex >= table.FieldCount)
        {
            return null;
        }

        var fieldRelativeOffset = ReadUInt16(
            checked(table.VtableOffset + sizeof(ushort) * (2 + fieldIndex)),
            $"{label} field {fieldIndex} offset");
        if (fieldRelativeOffset == 0)
        {
            return null;
        }

        if (fieldRelativeOffset < sizeof(int)
            || fieldRelativeOffset >= table.ObjectLength)
        {
            throw new InvalidDataException(
                $"{label} field {fieldIndex} lies outside its FlatBuffer table object.");
        }

        return checked(tableOffset + fieldRelativeOffset);
    }

    public int GetRequiredScalarFieldOffset(
        int tableOffset,
        int fieldIndex,
        int scalarSize,
        string label)
    {
        var fieldOffset = GetFieldOffset(tableOffset, fieldIndex, label)
            ?? throw new InvalidDataException(
                $"{label} is omitted from the FlatBuffer and cannot be patched safely.");
        var table = ReadTableInfo(tableOffset, label, maximumFieldCount: null);
        if (scalarSize <= 0
            || fieldOffset > checked(tableOffset + table.ObjectLength - scalarSize))
        {
            throw new InvalidDataException(
                $"{label} does not fit inside its FlatBuffer table object.");
        }

        return fieldOffset;
    }

    public int ReadInt32(int offset, string label)
    {
        EnsureRange(offset, sizeof(int), label);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    }

    public IReadOnlyList<int> ReadTableVector(
        int tableOffset,
        int fieldIndex,
        string label)
    {
        var entries = ReadOffsetVector(tableOffset, fieldIndex, label);
        var tables = new int[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            tables[index] = ResolveForwardOffset(
                entries[index],
                ReadUInt32(entries[index], $"{label} entry {index}"),
                $"{label} entry {index}");
        }

        return tables;
    }

    public IReadOnlyList<ZaAngeFlatBufferStringEntry> ReadStringVectorEntries(
        int tableOffset,
        int fieldIndex,
        string label)
    {
        var fieldOffset = GetFieldOffset(tableOffset, fieldIndex, label);
        if (fieldOffset is null)
        {
            return [];
        }

        var entries = ReadOffsetVector(fieldOffset.Value, label);
        var strings = new ZaAngeFlatBufferStringEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            strings[index] = new ZaAngeFlatBufferStringEntry(
                entries[index],
                ReadStringReference(entries[index], $"{label} entry {index}"));
        }

        return strings;
    }

    public string ReadStringReference(int referenceOffset, string label)
    {
        var stringOffset = ResolveForwardOffset(
            referenceOffset,
            ReadUInt32(referenceOffset, $"{label} offset"),
            label);
        var length = ReadUInt32(stringOffset, $"{label} length");
        if (length > int.MaxValue)
        {
            throw new InvalidDataException($"{label} is too large.");
        }

        var byteLength = checked((int)length);
        var payloadOffset = checked(stringOffset + sizeof(uint));
        EnsureRange(payloadOffset, checked(byteLength + 1), label);
        if (bytes[payloadOffset + byteLength] != 0)
        {
            throw new InvalidDataException($"{label} is not null-terminated.");
        }

        try
        {
            return StrictUtf8.GetString(bytes, payloadOffset, byteLength);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{label} contains invalid UTF-8.", exception);
        }
    }

    private IReadOnlyList<int> ReadOffsetVector(
        int tableOffset,
        int fieldIndex,
        string label)
    {
        var fieldOffset = GetFieldOffset(tableOffset, fieldIndex, label)
            ?? throw new InvalidDataException($"{label} is missing.");
        return ReadOffsetVector(fieldOffset, label);
    }

    private IReadOnlyList<int> ReadOffsetVector(int fieldOffset, string label)
    {
        var vectorOffset = ResolveForwardOffset(
            fieldOffset,
            ReadUInt32(fieldOffset, $"{label} offset"),
            label);
        var count = ReadUInt32(vectorOffset, $"{label} length");
        if (count > int.MaxValue)
        {
            throw new InvalidDataException($"{label} contains too many entries.");
        }

        var entryCount = checked((int)count);
        var entriesOffset = checked(vectorOffset + sizeof(uint));
        var byteLength = checked(entryCount * sizeof(uint));
        EnsureRange(entriesOffset, byteLength, label);
        var entries = new int[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            entries[index] = checked(entriesOffset + (index * sizeof(uint)));
        }

        return entries;
    }

    private TableInfo ReadTableInfo(
        int tableOffset,
        string label,
        int? maximumFieldCount)
    {
        EnsureRange(tableOffset, sizeof(int), label);
        if ((tableOffset & 3) != 0)
        {
            throw new InvalidDataException($"{label} is not 4-byte aligned.");
        }

        var vtableDistance = ReadInt32(tableOffset, $"{label} vtable distance");
        if (vtableDistance == 0)
        {
            throw new InvalidDataException($"{label} has an invalid vtable distance.");
        }

        var vtablePosition = checked((long)tableOffset - vtableDistance);
        if (vtablePosition < 0 || vtablePosition > int.MaxValue)
        {
            throw new InvalidDataException($"{label} has an invalid vtable distance.");
        }

        var vtableOffset = checked((int)vtablePosition);
        if ((vtableOffset & 1) != 0)
        {
            throw new InvalidDataException($"{label} vtable is not 2-byte aligned.");
        }

        var vtableLength = ReadUInt16(vtableOffset, $"{label} vtable length");
        var objectLength = ReadUInt16(
            checked(vtableOffset + sizeof(ushort)),
            $"{label} object length");
        if (vtableLength < sizeof(ushort) * 2 || (vtableLength & 1) != 0)
        {
            throw new InvalidDataException($"{label} has an invalid vtable length.");
        }

        if (objectLength < sizeof(int))
        {
            throw new InvalidDataException($"{label} has an invalid object length.");
        }

        EnsureRange(vtableOffset, vtableLength, $"{label} vtable");
        EnsureRange(tableOffset, objectLength, $"{label} object");
        var fieldCount = (vtableLength - (sizeof(ushort) * 2)) / sizeof(ushort);
        if (maximumFieldCount is not null && fieldCount > maximumFieldCount.Value)
        {
            throw new InvalidDataException(
                $"{label} has {fieldCount} fields; this editor supports at most "
                + $"{maximumFieldCount.Value} for the verified schema.");
        }

        return new TableInfo(vtableOffset, objectLength, fieldCount);
    }

    private int ResolveForwardOffset(int referenceOffset, uint relativeOffset, string label)
    {
        if (relativeOffset == 0)
        {
            throw new InvalidDataException($"{label} has a null FlatBuffer offset.");
        }

        var target = checked((long)referenceOffset + relativeOffset);
        if (target <= referenceOffset || target > int.MaxValue)
        {
            throw new InvalidDataException($"{label} has an invalid forward FlatBuffer offset.");
        }

        var targetOffset = checked((int)target);
        EnsureRange(targetOffset, 1, label);
        if ((targetOffset & 3) != 0)
        {
            throw new InvalidDataException($"{label} target is not 4-byte aligned.");
        }

        return targetOffset;
    }

    private ushort ReadUInt16(int offset, string label)
    {
        EnsureRange(offset, sizeof(ushort), label);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    }

    private uint ReadUInt32(int offset, string label)
    {
        EnsureRange(offset, sizeof(uint), label);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
    }

    private void EnsureRange(int offset, int length, string label)
    {
        if (offset < 0
            || length < 0
            || offset > bytes.Length - length)
        {
            throw new InvalidDataException($"{label} points outside the FlatBuffer.");
        }
    }

    private sealed record TableInfo(
        int VtableOffset,
        int ObjectLength,
        int FieldCount);
}
