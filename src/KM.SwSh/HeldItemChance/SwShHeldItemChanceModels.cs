// SPDX-License-Identifier: GPL-3.0-only
namespace KM.SwSh.HeldItemChance;

public sealed record SwShHeldItemChanceOverride(int Species, int Form, IReadOnlyList<int> Rates);
public sealed record SwShHeldItemChancePokemon(int PersonalId, int Species, int Form, string Name,
    string FormLabel, string Type1, string Type2, IReadOnlyList<int> Items, IReadOnlyList<int> Rates, bool CustomRates);
public sealed record SwShHeldItemChanceUpdate(int PersonalId, IReadOnlyList<int> Items, IReadOnlyList<int>? Rates);
public sealed record SwShHeldItemOption(int Value, string Label);
