// SPDX-License-Identifier: GPL-3.0-only

using KM.ZA.Data;

namespace KM.ZA.Encounters;

internal static class ZaEncounterPlayerPartnerCatalog
{
    public const int EditSlot = -1;
    public const int PokemonDataSourceIndex = 772;
    public const string PokemonDataId = "vsmega_init_rukario";
    public const string BossSpawnerId = "btl_spn_boss_0359_01";
    public const string BossContextKey = "story";
    public const string RecordId = "player-partner:vsmega_init_rukario:772";
    public const string VanillaRestoreMarker =
        "verified-base:world/ik_data/field/pokemon/pokemon_data/pokemon_data/pokemon_data_array.bin";

    public static bool IsTargetTable(ZaEncounterTableRecord table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return string.Equals(
                table.RawSpawnerId,
                BossSpawnerId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                table.BossBattleContextKey,
                BossContextKey,
                StringComparison.Ordinal);
    }

    public static bool IsRecordId(string? recordId)
    {
        return string.Equals(recordId, RecordId, StringComparison.Ordinal);
    }

    public static bool TryResolveExactRow(
        ZaPokemonDataDocument document,
        out ZaPokemonDataEntry row,
        out string blockedReason)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sourceMatches = document.Entries
            .Where(candidate => candidate.SourceIndex == PokemonDataSourceIndex)
            .Take(2)
            .ToArray();
        var idMatches = document.Entries
            .Where(candidate => string.Equals(
                candidate.Id,
                PokemonDataId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (sourceMatches.Length == 1
            && idMatches.Length == 1
            && ReferenceEquals(sourceMatches[0], idMatches[0]))
        {
            row = sourceMatches[0];
            blockedReason = string.Empty;
            return true;
        }

        row = null!;
        blockedReason = idMatches.Length switch
        {
            0 => $"PokemonData row '{PokemonDataId}' is missing.",
            > 1 => $"PokemonData row '{PokemonDataId}' is duplicated.",
            _ when idMatches[0].SourceIndex != PokemonDataSourceIndex =>
                $"PokemonData row '{PokemonDataId}' moved from verified source index {PokemonDataSourceIndex} to {idMatches[0].SourceIndex}.",
            _ => $"PokemonData source index {PokemonDataSourceIndex} no longer has the verified '{PokemonDataId}' identity.",
        };
        return false;
    }
}
