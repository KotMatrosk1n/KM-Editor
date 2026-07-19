// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.AngeFight;

public sealed record ZaAngeFightProvenance(
    string RelativePath,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState State);

public sealed record ZaAngeFightSourceRecord(
    string Id,
    string Label,
    string RelativePath,
    string Status,
    string EffectiveSha256,
    string VanillaSha256,
    ZaAngeFightProvenance Provenance);

public sealed record ZaAngeFightFlowerRecord(
    string FlowerId,
    string Label,
    int Hp,
    int VanillaHp);

public sealed record ZaAngeFightAttackRecord(
    string MoveId,
    string Label,
    string Usage,
    int BulletId,
    int AttackId,
    int DamageToPokemon,
    int DamageToPlayer,
    int VanillaDamageToPokemon,
    int VanillaDamageToPlayer,
    double HitIntervalSeconds,
    bool SharedByMultipleActions,
    bool CanRepeatHit);

public sealed record ZaAngeFightWorkflowStats(
    int SourceFileCount,
    int FlowerCount,
    int AttackCount,
    int EditableValueCount);

public sealed record ZaAngeFightWorkflow(
    ZaWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    bool CanUninstall,
    string UninstallMessage,
    IReadOnlyList<ZaAngeFightSourceRecord> Sources,
    IReadOnlyList<ZaAngeFightFlowerRecord> Flowers,
    IReadOnlyList<ZaAngeFightAttackRecord> Attacks,
    ZaAngeFightWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaAngeFightAttackSelection(
    int AttackId,
    int DamageToPokemon,
    int DamageToPlayer);

public sealed record ZaAngeFightSettings(
    int BlueFlowerHp,
    int RedFlowerHp,
    IReadOnlyList<ZaAngeFightAttackSelection> Attacks);

public sealed record ZaAngeFightEditResult(
    ZaAngeFightWorkflow Workflow,
    EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal sealed record ZaAngeFightAttackDefinition(
    string MoveId,
    string Label,
    string Usage,
    int BulletId,
    int AttackId,
    double HitIntervalSeconds,
    bool SharedByMultipleActions,
    bool CanRepeatHit);

internal static class ZaAngeFightCatalog
{
    public static IReadOnlyList<ZaAngeFightAttackDefinition> Attacks { get; } =
    [
        new(
            "standard-projectile",
            "Standard Projectile",
            "Blue + Red shot patterns; Blue spin shot",
            2004,
            2146,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "shockwave",
            "Shockwave",
            "Blue quake; Red quake and quake rush",
            2005,
            2147,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "blue-shine",
            "Blue Shine",
            "Blue shine",
            2006,
            2148,
            10.0,
            SharedByMultipleActions: false,
            CanRepeatHit: false),
        new(
            "beam",
            "Beam",
            "Blue + Red beam",
            2007,
            2149,
            0.6,
            SharedByMultipleActions: true,
            CanRepeatHit: true),
        new(
            "red-shockwave",
            "Red Shockwave",
            "Red quake and quake rush",
            2008,
            2150,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "explosion-impact",
            "Explosion Impact",
            "Blue + Red explosion impact",
            2011,
            2153,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "projectile-variant",
            "Projectile Variant",
            "Blue + Red alternate shot",
            2012,
            2154,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "large-projectile",
            "Large Projectile",
            "Blue + Red large shot",
            2013,
            2155,
            1.0,
            SharedByMultipleActions: true,
            CanRepeatHit: false),
        new(
            "fog-1",
            "Fog 1",
            "Blue fog 1",
            2014,
            2156,
            0.6,
            SharedByMultipleActions: false,
            CanRepeatHit: true),
        new(
            "fog-2",
            "Fog 2",
            "Blue fog 2",
            2015,
            2157,
            0.6,
            SharedByMultipleActions: false,
            CanRepeatHit: true),
    ];
}
