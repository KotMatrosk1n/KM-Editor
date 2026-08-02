// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.ZA.Generated.BattleMoves;
using System.Globalization;
using System.Security.Cryptography;

namespace KM.ZA.Moves;

internal static class ZaRuntimeMoveData
{
    public const string BattlePrefix = "battle.";
    public const string TimingPrefix = "timing.";

    private static readonly string[] KnownSpawnLocators =
    [
        string.Empty,
        "eff_attack01",
        "eff_attack02",
        "eff_attack03",
        "eff_center01",
        "eff_directionattack01",
        "eff_face01",
        "eff_front01",
        "eff_frontunder01",
        "eff_headcenter01",
        "eff_overhead01",
        "eff_rangeattack01",
        "eff_rangeattack02",
        "feeler_a_01",
        "origin",
    ];

    public static IReadOnlyList<string> SpawnLocators => KnownSpawnLocators;

    public static ZaBattleMoveParameterArrayT ReadBattle(byte[] bytes) =>
        ZaBattleMoveParameterArrayT.DeserializeFromBinary(bytes);

    public static ZaMoveTimingParameterArrayT ReadTiming(byte[] bytes) =>
        ZaMoveTimingParameterArrayT.DeserializeFromBinary(bytes);

    public static IEnumerable<ZaBattleMoveParameterT> BattleRows(ZaBattleMoveParameterArrayT table) =>
        table.Values?.Where(group => group?.Root is not null).SelectMany(group => group.Root) ?? [];

    public static IEnumerable<ZaMoveTimingParameterT> TimingRows(ZaMoveTimingParameterArrayT table) =>
        table.Values?.Where(group => group?.Root is not null).SelectMany(group => group.Root) ?? [];

    public static string CreateBattleRowsFingerprint(IEnumerable<ZaBattleMoveParameterT> rows)
    {
        var table = new ZaBattleMoveParameterArrayT
        {
            Values =
            [
                new ZaBattleMoveParameterGroupT
                {
                    Root = rows.Select(Clone).ToList(),
                },
            ],
        };
        return Convert.ToHexString(SHA256.HashData(table.SerializeToBinary())).ToLowerInvariant();
    }

    public static string CreateTimingRowsFingerprint(IEnumerable<ZaMoveTimingParameterT> rows)
    {
        var table = new ZaMoveTimingParameterArrayT
        {
            Values =
            [
                new ZaMoveTimingParameterGroupT
                {
                    Root = rows.Select(Clone).ToList(),
                },
            ],
        };
        return Convert.ToHexString(SHA256.HashData(table.SerializeToBinary())).ToLowerInvariant();
    }

    public static ZaBattleMoveParameterT Clone(ZaBattleMoveParameterT row)
    {
        return new ZaBattleMoveParameterT
        {
            MoveId = row.MoveId,
            VariantType = row.VariantType,
            Type = row.Type,
            Category = row.Category,
            DamageType = row.DamageType,
            Power = row.Power,
            CriticalRank = row.CriticalRank,
            HpRecoverRatio = row.HpRecoverRatio,
            ShrinkPercent = row.ShrinkPercent,
            ConditionId = row.ConditionId,
            ConditionPercent = row.ConditionPercent,
            ConditionCount = row.ConditionCount,
            ConditionTurnMin = row.ConditionTurnMin,
            ConditionTurnMax = row.ConditionTurnMax,
            Stat1 = row.Stat1,
            Stat1Stage = row.Stat1Stage,
            Stat1Percent = row.Stat1Percent,
            Stat2 = row.Stat2,
            Stat2Stage = row.Stat2Stage,
            Stat2Percent = row.Stat2Percent,
            Stat3 = row.Stat3,
            Stat3Stage = row.Stat3Stage,
            Stat3Percent = row.Stat3Percent,
            DamageRecoverRatio = row.DamageRecoverRatio,
            DamageDrainRatio = row.DamageDrainRatio,
            IsGuard = row.IsGuard,
            IsAvoidedByFloating = row.IsAvoidedByFloating,
            MakesContact = row.MakesContact,
            IsSlicing = row.IsSlicing,
            IsWind = row.IsWind,
            BypassesSubstitute = row.BypassesSubstitute,
            ThawsUser = row.ThawsUser,
            RestoresHp = row.RestoresHp,
            AllowedWhileHealBlocked = row.AllowedWhileHealBlocked,
            CallableByMetronome = row.CallableByMetronome,
            AppliesCondition = row.AppliesCondition,
            BlockedByProtect = row.BlockedByProtect,
            CannotKnockOut = row.CannotKnockOut,
            ValueEffectRatio = row.ValueEffectRatio,
        };
    }

    public static ZaMoveTimingParameterT Clone(ZaMoveTimingParameterT row)
    {
        return new ZaMoveTimingParameterT
        {
            MoveId = row.MoveId,
            ChargeFrame = row.ChargeFrame,
            AttackLoopFrame = row.AttackLoopFrame,
            SpawnOrigin = row.SpawnOrigin,
            SpawnLocator = row.SpawnLocator,
            SpawnOffsetX = row.SpawnOffsetX,
            SpawnOffsetY = row.SpawnOffsetY,
            SpawnOffsetZ = row.SpawnOffsetZ,
            ShotDirection = row.ShotDirection,
            TargetCorrectionType = row.TargetCorrectionType,
            ImpactMotionSpeed = row.ImpactMotionSpeed,
            MovementType = row.MovementType,
            RangeMin = row.RangeMin,
            RangeMax = row.RangeMax,
            HeightTolerance = row.HeightTolerance,
            EffectiveRange = row.EffectiveRange,
            ProjectileCountMin = row.ProjectileCountMin,
            ProjectileCountMax = row.ProjectileCountMax,
            HitPercent = row.HitPercent,
            Cooldown = row.Cooldown,
            EffectTime = row.EffectTime,
            EffectValue = row.EffectValue,
            MegaPowerBonus = row.MegaPowerBonus,
            PlayedMotionSpeed = row.PlayedMotionSpeed,
            OverwriteProjectile1 = row.OverwriteProjectile1,
            ReplacementProjectile1 = row.ReplacementProjectile1,
            OverwriteProjectile2 = row.OverwriteProjectile2,
            ReplacementProjectile2 = row.ReplacementProjectile2,
            OverwriteProjectile3 = row.OverwriteProjectile3,
            ReplacementProjectile3 = row.ReplacementProjectile3,
            OverwriteProjectile4 = row.OverwriteProjectile4,
            ReplacementProjectile4 = row.ReplacementProjectile4,
            OverwriteProjectile5 = row.OverwriteProjectile5,
            ReplacementProjectile5 = row.ReplacementProjectile5,
            ProjectileCorrectionScale = row.ProjectileCorrectionScale,
        };
    }

    public static ZaMoveTimingParameterT ToTableRow(ZaMoveTimingRecord row)
    {
        return new ZaMoveTimingParameterT
        {
            MoveId = row.TimingMoveId,
            ChargeFrame = row.ChargeFrames,
            AttackLoopFrame = row.AttackLoopFrames,
            SpawnOrigin = row.SpawnOrigin,
            SpawnLocator = row.SpawnLocator,
            SpawnOffsetX = checked((float)row.SpawnOffsetX),
            SpawnOffsetY = checked((float)row.SpawnOffsetY),
            SpawnOffsetZ = checked((float)row.SpawnOffsetZ),
            ShotDirection = row.ShotDirection,
            TargetCorrectionType = row.TargetCorrectionType,
            ImpactMotionSpeed = checked((float)row.ImpactMotionSpeed),
            MovementType = row.MovementType,
            RangeMin = checked((float)row.RangeMin),
            RangeMax = checked((float)row.RangeMax),
            HeightTolerance = checked((float)row.HeightTolerance),
            EffectiveRange = checked((float)row.EffectiveRange),
            ProjectileCountMin = row.ProjectileCountMin,
            ProjectileCountMax = row.ProjectileCountMax,
            HitPercent = row.HitPercent,
            Cooldown = checked((float)row.Cooldown),
            EffectTime = checked((float)row.EffectTime),
            EffectValue = row.EffectValue,
            MegaPowerBonus = checked((float)row.MegaPowerBonus),
            PlayedMotionSpeed = checked((float)row.PlayedMotionSpeed),
            OverwriteProjectile1 = row.OverwriteProjectile1,
            ReplacementProjectile1 = row.ReplacementProjectile1,
            OverwriteProjectile2 = row.OverwriteProjectile2,
            ReplacementProjectile2 = row.ReplacementProjectile2,
            OverwriteProjectile3 = row.OverwriteProjectile3,
            ReplacementProjectile3 = row.ReplacementProjectile3,
            OverwriteProjectile4 = row.OverwriteProjectile4,
            ReplacementProjectile4 = row.ReplacementProjectile4,
            OverwriteProjectile5 = row.OverwriteProjectile5,
            ReplacementProjectile5 = row.ReplacementProjectile5,
            ProjectileCorrectionScale = checked((float)row.ProjectileCorrectionScale),
        };
    }

    public static string BattleField(int variant, string field) =>
        $"{BattlePrefix}{variant.ToString(CultureInfo.InvariantCulture)}.{field}";

    public static string TimingSharedField(int timingMoveId, string field) =>
        $"{TimingPrefix}{timingMoveId.ToString(CultureInfo.InvariantCulture)}.{field}";

    public static string TimingField(int timingMoveId, int occurrence, string field) =>
        $"{TimingPrefix}{timingMoveId.ToString(CultureInfo.InvariantCulture)}.{occurrence.ToString(CultureInfo.InvariantCulture)}.{field}";

    // Retained as the occurrence-only workflow template shape. Concrete edits
    // always include the exact encoded timing move ID through the overload above.
    public static string TimingField(int occurrence, string field) =>
        $"{TimingPrefix}{occurrence.ToString(CultureInfo.InvariantCulture)}.{field}";

    public static bool IsTimingForMove(int timingMoveId, int moveId) =>
        timingMoveId >= 0
        && moveId >= 0
        && GetTimingBaseMoveId(timingMoveId) == moveId;

    public static int GetTimingBaseMoveId(int timingMoveId) =>
        timingMoveId >= 0
            ? timingMoveId % 1000
            : throw new ArgumentOutOfRangeException(nameof(timingMoveId));

    public static int GetTimingVariant(int timingMoveId) => timingMoveId switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(timingMoveId)),
        < 1000 => 0,
        < 2000 => 1,
        _ => 2,
    };

    public static bool TryParseBattleField(string? field, out int variant, out string member)
    {
        variant = 0;
        member = string.Empty;
        if (string.IsNullOrWhiteSpace(field) || !field.StartsWith(BattlePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = field.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out variant)
            || variant is < byte.MinValue or > byte.MaxValue
            || string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        member = parts[2];
        return true;
    }

    public static bool TryParseTimingField(
        string? field,
        out int? timingMoveId,
        out int? occurrence,
        out string member)
    {
        timingMoveId = null;
        occurrence = null;
        member = string.Empty;
        if (string.IsNullOrWhiteSpace(field) || !field.StartsWith(TimingPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = field[TimingPrefix.Length..].Split('.', StringSplitOptions.None);
        if (parts.Length == 1)
        {
            member = parts[0];
            return member.Length > 0;
        }

        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedIdentity)
            && parsedIdentity >= 0
            && !string.IsNullOrWhiteSpace(parts[1]))
        {
            member = parts[1];
            if (member is "hitPercent" or "cooldown")
            {
                timingMoveId = parsedIdentity;
            }
            else
            {
                // Legacy/template shape: timing.<occurrence>.<member>.
                occurrence = parsedIdentity;
            }

            return true;
        }

        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTimingMoveId)
            && parsedTimingMoveId >= 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedOccurrence)
            && parsedOccurrence >= 0
            && !string.IsNullOrWhiteSpace(parts[2]))
        {
            timingMoveId = parsedTimingMoveId;
            occurrence = parsedOccurrence;
            member = parts[2];
            return true;
        }

        return false;
    }

    public static ZaMoveRuntimeVariantRecord ToRecord(ZaBattleMoveParameterT row)
    {
        return new ZaMoveRuntimeVariantRecord(
            row.VariantType,
            row.Type,
            ZaMovesWorkflowService.FormatType(row.Type),
            row.Category,
            row.DamageType,
            ZaMovesWorkflowService.FormatCategory(row.DamageType),
            row.Power,
            row.CriticalRank,
            row.HpRecoverRatio,
            row.ShrinkPercent,
            row.ConditionId,
            row.ConditionPercent,
            row.ConditionCount,
            row.ConditionTurnMin,
            row.ConditionTurnMax,
            [
                new(1, row.Stat1, ZaMovesWorkflowService.FormatStat(row.Stat1), row.Stat1Stage, row.Stat1Percent),
                new(2, row.Stat2, ZaMovesWorkflowService.FormatStat(row.Stat2), row.Stat2Stage, row.Stat2Percent),
                new(3, row.Stat3, ZaMovesWorkflowService.FormatStat(row.Stat3), row.Stat3Stage, row.Stat3Percent),
            ],
            row.DamageRecoverRatio,
            row.DamageDrainRatio,
            row.IsGuard,
            row.IsAvoidedByFloating,
            row.MakesContact,
            row.IsSlicing,
            row.IsWind,
            row.BypassesSubstitute,
            row.ThawsUser,
            row.RestoresHp,
            row.AllowedWhileHealBlocked,
            row.CallableByMetronome,
            row.AppliesCondition,
            row.BlockedByProtect,
            row.CannotKnockOut,
            row.ValueEffectRatio);
    }

    public static ZaMoveTimingRecord ToRecord(
        ZaMoveTimingParameterT row,
        int occurrence = 0,
        IReadOnlyList<string>? spawnLocators = null)
    {
        var spawnLocator = row.SpawnLocator ?? string.Empty;
        var locatorOptions = spawnLocators ?? KnownSpawnLocators;
        return new ZaMoveTimingRecord(
            row.MoveId,
            GetTimingVariant(row.MoveId),
            occurrence,
            row.ChargeFrame,
            row.AttackLoopFrame,
            row.SpawnOrigin,
            spawnLocator,
            GetSpawnLocatorOption(spawnLocator, locatorOptions),
            ToRoundTripDouble(row.SpawnOffsetX, nameof(row.SpawnOffsetX)),
            ToRoundTripDouble(row.SpawnOffsetY, nameof(row.SpawnOffsetY)),
            ToRoundTripDouble(row.SpawnOffsetZ, nameof(row.SpawnOffsetZ)),
            row.ShotDirection,
            row.TargetCorrectionType,
            ToRoundTripDouble(row.ImpactMotionSpeed, nameof(row.ImpactMotionSpeed)),
            row.MovementType,
            ToRoundTripDouble(row.RangeMin, nameof(row.RangeMin)),
            ToRoundTripDouble(row.RangeMax, nameof(row.RangeMax)),
            ToRoundTripDouble(row.HeightTolerance, nameof(row.HeightTolerance)),
            ToRoundTripDouble(row.EffectiveRange, nameof(row.EffectiveRange)),
            row.ProjectileCountMin,
            row.ProjectileCountMax,
            row.HitPercent,
            ToRoundTripDouble(row.Cooldown, nameof(row.Cooldown)),
            ToRoundTripDouble(row.EffectTime, nameof(row.EffectTime)),
            row.EffectValue,
            ToRoundTripDouble(row.MegaPowerBonus, nameof(row.MegaPowerBonus)),
            ToRoundTripDouble(row.PlayedMotionSpeed, nameof(row.PlayedMotionSpeed)),
            row.OverwriteProjectile1,
            row.ReplacementProjectile1,
            row.OverwriteProjectile2,
            row.ReplacementProjectile2,
            row.OverwriteProjectile3,
            row.ReplacementProjectile3,
            row.OverwriteProjectile4,
            row.ReplacementProjectile4,
            row.OverwriteProjectile5,
            row.ReplacementProjectile5,
            ToRoundTripDouble(row.ProjectileCorrectionScale, nameof(row.ProjectileCorrectionScale)));
    }

    private static double ToRoundTripDouble(float value, string member)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"ZA move timing field '{member}' contains the nonfinite value '{value}'.");
        }

        return double.Parse(
            value.ToString("R", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    public static string? GetValue(ZaMoveRuntimeVariantRecord row, string member)
    {
        object? value = member switch
        {
            "effectCategory" => row.EffectCategory,
            "type" => row.Type,
            "damageType" => row.DamageType,
            "power" => row.Power,
            "criticalRank" => row.CriticalRank,
            "hpRecoverRatio" => row.HpRecoverRatio,
            "shrinkPercent" => row.ShrinkPercent,
            "conditionId" => row.ConditionId,
            "conditionPercent" => row.ConditionPercent,
            "conditionCount" => row.ConditionCount,
            "conditionTurnMin" => row.ConditionTurnMin,
            "conditionTurnMax" => row.ConditionTurnMax,
            "stat1" => Stat(row, 1)?.Stat,
            "stat1Stage" => Stat(row, 1)?.Stage,
            "stat1Percent" => Stat(row, 1)?.Percent,
            "stat2" => Stat(row, 2)?.Stat,
            "stat2Stage" => Stat(row, 2)?.Stage,
            "stat2Percent" => Stat(row, 2)?.Percent,
            "stat3" => Stat(row, 3)?.Stat,
            "stat3Stage" => Stat(row, 3)?.Stage,
            "stat3Percent" => Stat(row, 3)?.Percent,
            "damageRecoverRatio" => row.DamageRecoverRatio,
            "damageDrainRatio" => row.DamageDrainRatio,
            "isGuard" => row.IsGuard ? 1 : 0,
            "isAvoidedByFloating" => row.IsAvoidedByFloating ? 1 : 0,
            "makesContact" => row.MakesContact ? 1 : 0,
            "isSlicing" => row.IsSlicing ? 1 : 0,
            "isWind" => row.IsWind ? 1 : 0,
            "bypassesSubstitute" => row.BypassesSubstitute ? 1 : 0,
            "thawsUser" => row.ThawsUser ? 1 : 0,
            "restoresHp" => row.RestoresHp ? 1 : 0,
            "allowedWhileHealBlocked" => row.AllowedWhileHealBlocked ? 1 : 0,
            "callableByMetronome" => row.CallableByMetronome ? 1 : 0,
            "appliesCondition" => row.AppliesCondition ? 1 : 0,
            "blockedByProtect" => row.BlockedByProtect ? 1 : 0,
            "cannotKnockOut" => row.CannotKnockOut ? 1 : 0,
            "valueEffectRatio" => row.ValueEffectRatio,
            _ => null,
        };
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static string? GetValue(ZaMoveTimingRecord row, string member)
    {
        object? value = member switch
        {
            "chargeFrames" => row.ChargeFrames,
            "attackLoopFrames" => row.AttackLoopFrames,
            "spawnOrigin" => row.SpawnOrigin,
            "spawnLocator" => row.SpawnLocatorOption,
            "spawnOffsetX" => row.SpawnOffsetX,
            "spawnOffsetY" => row.SpawnOffsetY,
            "spawnOffsetZ" => row.SpawnOffsetZ,
            "shotDirection" => row.ShotDirection,
            "targetCorrectionType" => row.TargetCorrectionType,
            "impactMotionSpeed" => row.ImpactMotionSpeed,
            "movementType" => row.MovementType,
            "rangeMin" => row.RangeMin,
            "rangeMax" => row.RangeMax,
            "heightTolerance" => row.HeightTolerance,
            "effectiveRange" => row.EffectiveRange,
            "projectileCountMin" => row.ProjectileCountMin,
            "projectileCountMax" => row.ProjectileCountMax,
            "hitPercent" => row.HitPercent,
            "cooldown" => row.Cooldown,
            "effectTime" => row.EffectTime,
            "effectValue" => row.EffectValue,
            "megaPowerBonus" => row.MegaPowerBonus,
            "playedMotionSpeed" => row.PlayedMotionSpeed,
            "overwriteProjectile1" => row.OverwriteProjectile1,
            "replacementProjectile1" => row.ReplacementProjectile1,
            "overwriteProjectile2" => row.OverwriteProjectile2,
            "replacementProjectile2" => row.ReplacementProjectile2,
            "overwriteProjectile3" => row.OverwriteProjectile3,
            "replacementProjectile3" => row.ReplacementProjectile3,
            "overwriteProjectile4" => row.OverwriteProjectile4,
            "replacementProjectile4" => row.ReplacementProjectile4,
            "overwriteProjectile5" => row.OverwriteProjectile5,
            "replacementProjectile5" => row.ReplacementProjectile5,
            "projectileCorrectionScale" => row.ProjectileCorrectionScale,
            _ => null,
        };
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static bool Apply(ZaBattleMoveParameterT row, string member, int value)
    {
        switch (member)
        {
            case "effectCategory": row.Category = checked((byte)value); break;
            case "type": row.Type = checked((byte)value); break;
            case "damageType": row.DamageType = checked((byte)value); break;
            case "power": row.Power = checked((byte)value); break;
            case "criticalRank": row.CriticalRank = checked((byte)value); break;
            case "hpRecoverRatio": row.HpRecoverRatio = checked((sbyte)value); break;
            case "shrinkPercent": row.ShrinkPercent = checked((byte)value); break;
            case "conditionId": row.ConditionId = checked((ushort)value); break;
            case "conditionPercent": row.ConditionPercent = checked((byte)value); break;
            case "conditionCount": row.ConditionCount = checked((byte)value); break;
            case "conditionTurnMin": row.ConditionTurnMin = checked((byte)value); break;
            case "conditionTurnMax": row.ConditionTurnMax = checked((byte)value); break;
            case "stat1": row.Stat1 = checked((byte)value); break;
            case "stat1Stage": row.Stat1Stage = checked((sbyte)value); break;
            case "stat1Percent": row.Stat1Percent = checked((byte)value); break;
            case "stat2": row.Stat2 = checked((byte)value); break;
            case "stat2Stage": row.Stat2Stage = checked((sbyte)value); break;
            case "stat2Percent": row.Stat2Percent = checked((byte)value); break;
            case "stat3": row.Stat3 = checked((byte)value); break;
            case "stat3Stage": row.Stat3Stage = checked((sbyte)value); break;
            case "stat3Percent": row.Stat3Percent = checked((byte)value); break;
            case "damageRecoverRatio": row.DamageRecoverRatio = checked((sbyte)value); break;
            case "damageDrainRatio": row.DamageDrainRatio = checked((sbyte)value); break;
            case "isGuard": row.IsGuard = value != 0; break;
            case "isAvoidedByFloating": row.IsAvoidedByFloating = value != 0; break;
            case "makesContact": row.MakesContact = value != 0; break;
            case "isSlicing": row.IsSlicing = value != 0; break;
            case "isWind": row.IsWind = value != 0; break;
            case "bypassesSubstitute": row.BypassesSubstitute = value != 0; break;
            case "thawsUser": row.ThawsUser = value != 0; break;
            case "restoresHp": row.RestoresHp = value != 0; break;
            case "allowedWhileHealBlocked": row.AllowedWhileHealBlocked = value != 0; break;
            case "callableByMetronome": row.CallableByMetronome = value != 0; break;
            case "appliesCondition": row.AppliesCondition = value != 0; break;
            case "blockedByProtect": row.BlockedByProtect = value != 0; break;
            case "cannotKnockOut": row.CannotKnockOut = value != 0; break;
            case "valueEffectRatio": row.ValueEffectRatio = checked((sbyte)value); break;
            default: return false;
        }

        return true;
    }

    public static bool Apply(
        ZaMoveTimingParameterT row,
        string member,
        string value,
        IReadOnlyList<string>? spawnLocators = null)
    {
        switch (member)
        {
            case "chargeFrames" when TryParseInt(value, out var chargeFrames):
                row.ChargeFrame = chargeFrames;
                return true;
            case "attackLoopFrames" when TryParseInt(value, out var attackLoopFrames):
                row.AttackLoopFrame = attackLoopFrames;
                return true;
            case "spawnOrigin" when TryParseInt(value, out var spawnOrigin):
                row.SpawnOrigin = spawnOrigin;
                return true;
            case "spawnLocator" when TryParseInt(value, out var spawnLocatorOption)
                                     && TryGetSpawnLocator(
                                         spawnLocatorOption,
                                         spawnLocators ?? KnownSpawnLocators,
                                         out var spawnLocator):
                row.SpawnLocator = spawnLocator;
                return true;
            case "spawnOffsetX" when TryParseFloat(value, out var spawnOffsetX):
                row.SpawnOffsetX = spawnOffsetX;
                return true;
            case "spawnOffsetY" when TryParseFloat(value, out var spawnOffsetY):
                row.SpawnOffsetY = spawnOffsetY;
                return true;
            case "spawnOffsetZ" when TryParseFloat(value, out var spawnOffsetZ):
                row.SpawnOffsetZ = spawnOffsetZ;
                return true;
            case "shotDirection" when TryParseInt(value, out var shotDirection):
                row.ShotDirection = shotDirection;
                return true;
            case "targetCorrectionType" when TryParseInt(value, out var targetCorrectionType):
                row.TargetCorrectionType = targetCorrectionType;
                return true;
            case "impactMotionSpeed" when TryParseFloat(value, out var impactMotionSpeed):
                row.ImpactMotionSpeed = impactMotionSpeed;
                return true;
            case "movementType" when TryParseInt(value, out var movementType):
                row.MovementType = movementType;
                return true;
            case "rangeMin" when TryParseFloat(value, out var rangeMin):
                row.RangeMin = rangeMin;
                return true;
            case "rangeMax" when TryParseFloat(value, out var rangeMax):
                row.RangeMax = rangeMax;
                return true;
            case "heightTolerance" when TryParseFloat(value, out var heightTolerance):
                row.HeightTolerance = heightTolerance;
                return true;
            case "effectiveRange" when TryParseFloat(value, out var effectiveRange):
                row.EffectiveRange = effectiveRange;
                return true;
            case "projectileCountMin" when TryParseInt(value, out var projectileCountMin):
                row.ProjectileCountMin = projectileCountMin;
                return true;
            case "projectileCountMax" when TryParseInt(value, out var projectileCountMax):
                row.ProjectileCountMax = projectileCountMax;
                return true;
            case "hitPercent" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hitPercent):
                row.HitPercent = hitPercent;
                return true;
            case "cooldown" when TryParseFloat(value, out var cooldown):
                row.Cooldown = cooldown;
                return true;
            case "effectTime" when TryParseFloat(value, out var effectTime):
                row.EffectTime = effectTime;
                return true;
            case "effectValue" when TryParseInt(value, out var effectValue):
                row.EffectValue = effectValue;
                return true;
            case "megaPowerBonus" when TryParseFloat(value, out var megaPowerBonus):
                row.MegaPowerBonus = megaPowerBonus;
                return true;
            case "playedMotionSpeed" when TryParseFloat(value, out var playedMotionSpeed):
                row.PlayedMotionSpeed = playedMotionSpeed;
                return true;
            case "overwriteProjectile1" when TryParseInt(value, out var overwriteProjectile1):
                row.OverwriteProjectile1 = overwriteProjectile1;
                return true;
            case "replacementProjectile1" when TryParseInt(value, out var replacementProjectile1):
                row.ReplacementProjectile1 = replacementProjectile1;
                return true;
            case "overwriteProjectile2" when TryParseInt(value, out var overwriteProjectile2):
                row.OverwriteProjectile2 = overwriteProjectile2;
                return true;
            case "replacementProjectile2" when TryParseInt(value, out var replacementProjectile2):
                row.ReplacementProjectile2 = replacementProjectile2;
                return true;
            case "overwriteProjectile3" when TryParseInt(value, out var overwriteProjectile3):
                row.OverwriteProjectile3 = overwriteProjectile3;
                return true;
            case "replacementProjectile3" when TryParseInt(value, out var replacementProjectile3):
                row.ReplacementProjectile3 = replacementProjectile3;
                return true;
            case "overwriteProjectile4" when TryParseInt(value, out var overwriteProjectile4):
                row.OverwriteProjectile4 = overwriteProjectile4;
                return true;
            case "replacementProjectile4" when TryParseInt(value, out var replacementProjectile4):
                row.ReplacementProjectile4 = replacementProjectile4;
                return true;
            case "overwriteProjectile5" when TryParseInt(value, out var overwriteProjectile5):
                row.OverwriteProjectile5 = overwriteProjectile5;
                return true;
            case "replacementProjectile5" when TryParseInt(value, out var replacementProjectile5):
                row.ReplacementProjectile5 = replacementProjectile5;
                return true;
            case "projectileCorrectionScale" when TryParseFloat(value, out var projectileCorrectionScale):
                row.ProjectileCorrectionScale = projectileCorrectionScale;
                return true;
            default:
                return false;
        }
    }

    public static bool IsProjectileMember(string member) =>
        member.StartsWith("overwriteProjectile", StringComparison.Ordinal)
        || member.StartsWith("replacementProjectile", StringComparison.Ordinal);

    private static int GetSpawnLocatorOption(string locator, IReadOnlyList<string> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index], locator, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetSpawnLocator(
        int option,
        IReadOnlyList<string> options,
        out string locator)
    {
        if ((uint)option < (uint)options.Count)
        {
            locator = options[option];
            return true;
        }

        locator = string.Empty;
        return false;
    }

    private static bool TryParseInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseFloat(string value, out float parsed) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
        && float.IsFinite(parsed);

    private static ZaMoveStatChangeRecord? Stat(ZaMoveRuntimeVariantRecord row, int slot) =>
        row.StatChanges.FirstOrDefault(candidate => candidate.Slot == slot);
}
