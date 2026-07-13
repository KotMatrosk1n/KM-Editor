// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

internal static class ZaPokemonDataIvEncoding
{
    internal const int GameDefaultRandomMode = 127;
    internal const int FixedOrGuaranteedMode = 128;

    private const int ScriptDefaultMode = -1;
    private const int AlphaDefaultMode = 255;
    private const int LegacyRandomMode = 0;
    private const int LegacyGuaranteedMode = 1;
    private const int LegacyFixedMode = 2;

    internal static int? ReadFlawlessIvCount(ZaPokemonDataEntry entry)
    {
        var hasRandomIvValues = HasOnlyRandomIvs(entry.TalentValue);
        if (entry.TalentScale == LegacyGuaranteedMode
            || (hasRandomIvValues
                && entry.TalentVNum > 0
                && entry.TalentScale is FixedOrGuaranteedMode or ScriptDefaultMode))
        {
            return entry.TalentVNum;
        }

        return entry.TalentScale is LegacyRandomMode or GameDefaultRandomMode or AlphaDefaultMode
            || (hasRandomIvValues
                && entry.TalentScale is FixedOrGuaranteedMode or ScriptDefaultMode or LegacyFixedMode)
            ? 0
            : null;
    }

    internal static ZaPokemonDataStatsRecord CreateRandomIvStats()
    {
        return new ZaPokemonDataStatsRecord(-1, -1, -1, -1, -1, -1);
    }

    internal static void SetPreset(ZaPokemonDataEntry entry, int flawlessIvCount)
    {
        entry.TalentScale = flawlessIvCount <= 0 ? GameDefaultRandomMode : FixedOrGuaranteedMode;
        entry.TalentVNum = Math.Max(flawlessIvCount, 0);
        entry.TalentValue = CreateRandomIvStats();
    }

    internal static void SetFixedIvs(
        ZaPokemonDataEntry entry,
        Func<ZaPokemonDataStatsRecord, ZaPokemonDataStatsRecord> update)
    {
        entry.TalentScale = FixedOrGuaranteedMode;
        entry.TalentVNum = 0;
        entry.TalentValue = update(entry.TalentValue ?? CreateRandomIvStats());
    }

    private static bool HasOnlyRandomIvs(ZaPokemonDataStatsRecord? ivs)
    {
        return ivs is null
            || (ivs.HP == -1
                && ivs.Attack == -1
                && ivs.Defense == -1
                && ivs.SpecialAttack == -1
                && ivs.SpecialDefense == -1
                && ivs.Speed == -1);
    }
}
