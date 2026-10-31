// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.SwSh.Gifts;
using System.Text.Json.Serialization;

namespace KM.SwSh.CustomGifts;

public sealed record SwShCustomGiftPokemon(
    int SpeciesId, int Form, int Level, bool IsEgg, int HeldItemId, int BallItemId,
    int Ability, int Nature, int Gender, int ShinyLock, int DynamaxLevel, bool CanGigantamax,
    int SpecialMoveId, SwShGiftPokemonIvsRecord Ivs, int FlawlessIvCount)
{
    public int GenderRatio { get; init; } = 127;
    public int AbilityMask { get; init; } = 3;
}

public sealed record SwShCodeGift(int ClaimId, string Name, string Code, bool OncePerSave,
    SwShCustomGiftPokemon Pokemon);
public sealed record SwShRoyalGiftPool(int Badges, IReadOnlyList<SwShCustomGiftPokemon> Pokemon)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<decimal>? Chances { get; init; }
}
public sealed record SwShCustomGiftCatalog(int Version, string Mode, int NextClaimId,
    IReadOnlyList<SwShCodeGift> CodeGifts, IReadOnlyList<SwShRoyalGiftPool> RoyalPools)
{
    public static SwShCustomGiftCatalog Empty => new(1, "none", 1800, [],
        Enumerable.Range(0, 9).Select(count => new SwShRoyalGiftPool(count, []) { Chances = [] }).ToArray());
}

public sealed record SwShCustomGiftWorkflow(bool CanEdit, string InstalledMode, bool NeedsRepair, SwShCustomGiftCatalog Catalog,
    IReadOnlyList<SwShGiftPokemonEditableField> EditableFields, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record SwShCustomGiftEditResult(SwShCustomGiftWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
