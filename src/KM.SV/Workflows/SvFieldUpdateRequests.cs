// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SV.Workflows;

public sealed record SvItemFieldUpdate(int ItemId, string Field, string Value);

public sealed record SvMoveFieldUpdate(int MoveId, string Field, string Value);

public sealed record SvPokemonFieldUpdate(int PersonalId, string Field, string Value);

public sealed record SvTrainerFieldUpdate(int TrainerId, int? Slot, string Field, string Value);

public sealed record SvEncounterSlotFieldUpdate(string TableId, int Slot, string Field, string Value);

public sealed record SvGiftPokemonFieldUpdate(int GiftIndex, string Field, string Value);

public sealed record SvTradePokemonFieldUpdate(int TradeIndex, string Field, string Value);

public sealed record SvPlacementObjectFieldUpdate(string ObjectId, string Field, string Value);

public sealed record SvTeraRaidFieldUpdate(string RecordId, string Field, string Value);
