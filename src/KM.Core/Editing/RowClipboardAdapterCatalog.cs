// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Editing;

/// <summary>
/// Stable logical-row schemas shared by the desktop and the authoritative paste service.
/// These schemas deliberately own semantic values only. Record identities, physical offsets,
/// unknown bytes, pointers, and presentation text remain target-owned.
/// </summary>
public static class RowClipboardAdapterCatalog
{
    public const string LegendsArceusProfileId = "1.1.1";
    public static readonly RowClipboardAdapterSchema PlaLearnset = new(
        new("pla.pokemon.learnset", "pla.pokemon.learnset-row", 1), [ProjectGame.PLA], [LegendsArceusProfileId], [RowClipboardPasteMode.Replace, RowClipboardPasteMode.Append],
        dependencyKinds: [], fieldPolicies: [Unsigned("level"), Unsigned("mastery"), Unsigned("move")], maximumRows: 128);
    public static readonly RowClipboardAdapterSchema PlaTrainerParty = new(
        new("pla.trainers.party", "pla.trainer.party-member", 1), [ProjectGame.PLA], [LegendsArceusProfileId], [RowClipboardPasteMode.Replace],
        dependencyKinds: [], fieldPolicies: [Signed("species"), Signed("form"), Signed("battleLevel"), Signed("gender"), Signed("effortHp"), Signed("effortAttack"), Signed("effortDefense"), Signed("effortSpecialAttack"), Signed("effortSpecialDefense"), Signed("effortSpeed"), Signed("move1"), Signed("mastered1"), Signed("move2"), Signed("mastered2"), Signed("move3"), Signed("mastered3"), Signed("move4"), Signed("mastered4")], maximumRows: 1);
    public static readonly RowClipboardAdapterSchema PlaEncounterSlot = new(
        new("pla.encounters.slots", "pla.encounter.slot", 1), [ProjectGame.PLA], [LegendsArceusProfileId], [RowClipboardPasteMode.Replace],
        dependencyKinds: [], fieldPolicies: [Signed("species"), Signed("form"), Signed("wildMin"), Signed("wildMax"), Signed("wildWeight"), Signed("perfectIvs"), Signed("effortHp"), Signed("effortAttack"), Signed("effortDefense"), Signed("effortSpecialAttack"), Signed("effortSpecialDefense"), Signed("effortSpeed"), new RowClipboardFieldPolicy("dawn", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("day", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("dusk", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("night", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("sunny", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("cloudy", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("rain", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("snow", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("drought", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("fog", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("rainstorm", [RowClipboardValueKind.Decimal]), new RowClipboardFieldPolicy("snowstorm", [RowClipboardValueKind.Decimal])], maximumRows: 1);

    public const string PokemonLearnsetEditorId = "pokemon.learnset";
    public const string PokemonLearnsetRowKind = "pokemon.learnset-row";
    public const string EncounterSlotEditorId = "encounters.slots";
    public const string EncounterSlotRowKind = "encounter.slot";
    public const string TrainerPartyEditorId = "trainers.party";
    public const string TrainerPartyRowKind = "trainer.party-member";

    public const string SwordShieldProfileId = "1.3.2";
    public const string ScarletVioletProfileId = "4.0.0";
    public const string LegendsZaProfileId = "2.0.2";

    private static readonly RowClipboardEditorSchema PokemonLearnsetEditor =
        new(PokemonLearnsetEditorId, PokemonLearnsetRowKind, 1);
    private static readonly RowClipboardEditorSchema EncounterSlotEditor =
        new(EncounterSlotEditorId, EncounterSlotRowKind, 1);
    private static readonly RowClipboardEditorSchema TrainerPartyEditor =
        new(TrainerPartyEditorId, TrainerPartyRowKind, 1);

    public static readonly RowClipboardAdapterSchema PokemonLearnset =
        CreatePokemonLearnset([ProjectGame.Sword, ProjectGame.Shield], SwordShieldProfileId);
    public static readonly RowClipboardAdapterSchema PokemonLearnsetScarletViolet =
        CreatePokemonLearnset([ProjectGame.Scarlet, ProjectGame.Violet], ScarletVioletProfileId);
    public static readonly RowClipboardAdapterSchema PokemonLearnsetZa =
        CreatePokemonLearnset([ProjectGame.ZA], LegendsZaProfileId);

    public static readonly RowClipboardAdapterSchema EncounterSlot =
        CreateEncounterSlot(
            [ProjectGame.Sword, ProjectGame.Shield],
            SwordShieldProfileId,
            BasicEncounterFields());
    public static readonly RowClipboardAdapterSchema EncounterSlotScarletViolet =
        CreateEncounterSlot(
            [ProjectGame.Scarlet, ProjectGame.Violet],
            ScarletVioletProfileId,
            BasicEncounterFields().Concat([Signed("heldItemId"), Signed("heldItemChancePercent")]));
    public static readonly RowClipboardAdapterSchema EncounterSlotZa =
        CreateEncounterSlot(
            [ProjectGame.ZA],
            LegendsZaProfileId,
            ZaEncounterFields());

    public static readonly RowClipboardAdapterSchema TrainerParty =
        CreateTrainerParty(
            [ProjectGame.Sword, ProjectGame.Shield],
            SwordShieldProfileId,
            CommonTrainerFields().Concat(
            [
                Signed("dynamaxLevel"),
                Boolean("canGigantamax"),
                Boolean("canDynamax"),
            ]));
    public static readonly RowClipboardAdapterSchema TrainerPartyScarletViolet =
        CreateTrainerParty(
            [ProjectGame.Scarlet, ProjectGame.Violet],
            ScarletVioletProfileId,
            CommonTrainerFields().Append(Signed("teraType")));
    public static readonly RowClipboardAdapterSchema TrainerPartyZa =
        CreateTrainerParty(
            [ProjectGame.ZA],
            LegendsZaProfileId,
            CommonTrainerFields());

    public static RowClipboardAdapterSchema Resolve(
        RowClipboardEditorSchema editor,
        RowClipboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(scope);
        var adapter = (editor.EditorId, scope.Game) switch
        {
            ("pla.pokemon.learnset", ProjectGame.PLA) => PlaLearnset,
            ("pla.trainers.party", ProjectGame.PLA) => PlaTrainerParty,
            ("pla.encounters.slots", ProjectGame.PLA) => PlaEncounterSlot,

            (PokemonLearnsetEditorId, ProjectGame.Sword or ProjectGame.Shield) => PokemonLearnset,
            (PokemonLearnsetEditorId, ProjectGame.Scarlet or ProjectGame.Violet) => PokemonLearnsetScarletViolet,
            (PokemonLearnsetEditorId, ProjectGame.ZA) => PokemonLearnsetZa,
            (EncounterSlotEditorId, ProjectGame.Sword or ProjectGame.Shield) => EncounterSlot,
            (EncounterSlotEditorId, ProjectGame.Scarlet or ProjectGame.Violet) => EncounterSlotScarletViolet,
            (EncounterSlotEditorId, ProjectGame.ZA) => EncounterSlotZa,
            (TrainerPartyEditorId, ProjectGame.Sword or ProjectGame.Shield) => TrainerParty,
            (TrainerPartyEditorId, ProjectGame.Scarlet or ProjectGame.Violet) => TrainerPartyScarletViolet,
            (TrainerPartyEditorId, ProjectGame.ZA) => TrainerPartyZa,
            _ => throw new ArgumentException("The logical-row editor schema is not registered for this game.", nameof(editor)),
        };
        if (adapter.Editor != editor || !SupportsExactScope(adapter, scope))
        {
            throw new ArgumentException("The logical-row editor schema is not registered for this exact profile.", nameof(editor));
        }

        return adapter;
    }

    public static string ProfileId(ProjectGame game) => game switch
    {
        ProjectGame.Sword or ProjectGame.Shield => SwordShieldProfileId,
        ProjectGame.Scarlet or ProjectGame.Violet => ScarletVioletProfileId,
        ProjectGame.ZA => LegendsZaProfileId,
        ProjectGame.PLA => LegendsArceusProfileId,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public static bool SupportsExactScope(
        RowClipboardAdapterSchema adapter,
        RowClipboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(scope);
        return adapter.Supports(scope)
            && string.Equals(scope.ProfileId, ProfileId(scope.Game), StringComparison.Ordinal);
    }

    private static RowClipboardFieldPolicy Signed(string field) =>
        new(field, [RowClipboardValueKind.SignedInteger]);

    private static RowClipboardFieldPolicy Unsigned(string field) =>
        new(field, [RowClipboardValueKind.UnsignedInteger]);

    private static RowClipboardFieldPolicy Boolean(string field) =>
        new(field, [RowClipboardValueKind.Boolean]);

    private static RowClipboardAdapterSchema CreatePokemonLearnset(
        IEnumerable<ProjectGame> games,
        string profileId) =>
        new(
            PokemonLearnsetEditor,
            games,
            [profileId],
            [RowClipboardPasteMode.Replace, RowClipboardPasteMode.Append],
            dependencyKinds: [],
            fieldPolicies: [Unsigned("level"), Unsigned("moveId")]);

    private static RowClipboardAdapterSchema CreateEncounterSlot(
        IEnumerable<ProjectGame> games,
        string profileId,
        IEnumerable<RowClipboardFieldPolicy> fields) =>
        new(
            EncounterSlotEditor,
            games,
            [profileId],
            [RowClipboardPasteMode.Replace],
            dependencyKinds: [],
            fieldPolicies: fields);

    private static RowClipboardAdapterSchema CreateTrainerParty(
        IEnumerable<ProjectGame> games,
        string profileId,
        IEnumerable<RowClipboardFieldPolicy> fields) =>
        new(
            TrainerPartyEditor,
            games,
            [profileId],
            [RowClipboardPasteMode.Replace],
            dependencyKinds: [],
            fieldPolicies: fields,
            maximumRows: 6);

    private static IEnumerable<RowClipboardFieldPolicy> BasicEncounterFields() =>
    [
        Signed("form"),
        Signed("levelMax"),
        Signed("levelMin"),
        Signed("probability"),
        Signed("speciesId"),
    ];

    private static IEnumerable<RowClipboardFieldPolicy> ZaEncounterFields() =>
    [
        Signed("ability"), Signed("alphaChancePercent"), Signed("alphaLevelBonus"),
        Signed("appearanceMaxCount"), Signed("appearanceMinCount"), Signed("flawlessIvCount"),
        Signed("form"), Signed("gender"), Signed("heldItemId"),
        Signed("ivAttack"), Signed("ivDefense"), Signed("ivHp"), Signed("ivSpecialAttack"), Signed("ivSpecialDefense"), Signed("ivSpeed"),
        Signed("levelMax"), Signed("levelMin"), Signed("move1Id"), Signed("move2Id"), Signed("move3Id"), Signed("move4Id"),
        Signed("nature"), Signed("shinyLock"), Signed("slotMaxCount"), Signed("speciesId"),
        Signed("strengthenAttack"), Signed("strengthenDefense"), Signed("strengthenHp"), Signed("strengthenSpecialAttack"), Signed("strengthenSpecialDefense"), Signed("strengthenSpeed"),
        Signed("weight"),
    ];

    private static IEnumerable<RowClipboardFieldPolicy> CommonTrainerFields() =>
    [
        Signed("ability"), Signed("evAttack"), Signed("evDefense"), Signed("evHp"), Signed("evSpecialAttack"), Signed("evSpecialDefense"), Signed("evSpeed"),
        Signed("form"), Signed("gender"), Signed("heldItemId"),
        Signed("ivAttack"), Signed("ivDefense"), Signed("ivHp"), Signed("ivSpecialAttack"), Signed("ivSpecialDefense"), Signed("ivSpeed"),
        Signed("level"), Signed("move1Id"), Signed("move2Id"), Signed("move3Id"), Signed("move4Id"),
        Signed("nature"), Boolean("shiny"), Signed("speciesId"),
    ];
}
