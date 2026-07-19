// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.AngeFight;

internal sealed class ZaAngeFightWorkflowService
{
    public const string WorkflowLabel = "Ange Fight";
    public const string WorkflowDescription =
        "Advanced editor for Eternal Flower HP and Ange's non-Ember direct-damage attacks.";

    private static readonly SourceDefinition[] SourceDefinitions =
    [
        new("flowers", "Eternal Flower data", ZaDataPaths.FieldWazagimmickPublic),
        new("attacks", "Direct-damage attack data", ZaDataPaths.AiAttackParamArray),
        new("bullets", "Bullet-to-attack mapping", ZaDataPaths.AiBulletParamArray),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaAngeFightWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.AngeFight,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaAngeFightWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var sources = new List<ZaAngeFightSourceRecord>();
        var flowers = Array.Empty<ZaAngeFightFlowerRecord>();
        var attacks = Array.Empty<ZaAngeFightAttackRecord>();
        var modified = false;

        try
        {
            var effectiveById = new Dictionary<string, ZaWorkflowFile>(StringComparer.Ordinal);
            var vanillaById = new Dictionary<string, ZaWorkflowFile>(StringComparer.Ordinal);
            foreach (var definition in SourceDefinitions)
            {
                var effective = fileSource.Read(project, definition.VirtualPath);
                var vanilla = fileSource.ReadBase(project, definition.VirtualPath);
                effectiveById.Add(definition.Id, effective);
                vanillaById.Add(definition.Id, vanilla);
                sources.Add(CreateSourceRecord(definition, effective, vanilla));
            }

            var effectiveFlowerDocument = ZaAngeFlowerDataDocument.Parse(
                effectiveById["flowers"].Bytes);
            var vanillaFlowerDocument = ZaAngeFlowerDataDocument.Parse(
                vanillaById["flowers"].Bytes);
            var effectiveAttackDocument = ZaAngeAttackDataDocument.Parse(
                effectiveById["attacks"].Bytes);
            var vanillaAttackDocument = ZaAngeAttackDataDocument.Parse(
                vanillaById["attacks"].Bytes);
            ZaAngeBulletMappingDocument.Validate(effectiveById["bullets"].Bytes);
            ZaAngeBulletMappingDocument.Validate(vanillaById["bullets"].Bytes);

            flowers =
            [
                new ZaAngeFightFlowerRecord(
                    "blue",
                    "Blue Flower",
                    effectiveFlowerDocument.Values.BlueHp,
                    vanillaFlowerDocument.Values.BlueHp),
                new ZaAngeFightFlowerRecord(
                    "red",
                    "Red Flower",
                    effectiveFlowerDocument.Values.RedHp,
                    vanillaFlowerDocument.Values.RedHp),
            ];

            var effectiveAttacks = effectiveAttackDocument.Values.ToDictionary(
                value => value.AttackId);
            var vanillaAttacks = vanillaAttackDocument.Values.ToDictionary(
                value => value.AttackId);
            attacks = ZaAngeFightCatalog.Attacks
                .Select(definition =>
                {
                    var effective = effectiveAttacks[definition.AttackId];
                    var vanilla = vanillaAttacks[definition.AttackId];
                    return new ZaAngeFightAttackRecord(
                        definition.MoveId,
                        definition.Label,
                        definition.Usage,
                        definition.BulletId,
                        definition.AttackId,
                        effective.DamageToPokemon,
                        effective.DamageToPlayer,
                        vanilla.DamageToPokemon,
                        vanilla.DamageToPlayer,
                        definition.HitIntervalSeconds,
                        definition.SharedByMultipleActions,
                        definition.CanRepeatHit);
                })
                .ToArray();

            modified = flowers.Any(flower => flower.Hp != flower.VanillaHp)
                || attacks.Any(attack =>
                    attack.DamageToPokemon != attack.VanillaDamageToPokemon
                    || attack.DamageToPlayer != attack.VanillaDamageToPlayer);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or ArgumentException
                or OverflowException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Ange Fight could not be loaded safely: {exception.Message}",
                expected: "Verified Eternal Flower, attack, and bullet parameter FlatBuffers"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.AngeFight,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);
        var isBlocked = diagnostics.Any(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        var isEditable = summary.Availability == ZaWorkflowAvailability.Available;
        var installStatus = isBlocked
            ? "blocked"
            : modified
                ? "modified"
                : isEditable
                    ? "vanilla"
                    : "readOnly";
        var installMessage = installStatus switch
        {
            "blocked" => "Ange Fight is blocked because its exact data mapping could not be verified.",
            "modified" => "Ange Fight has non-vanilla HP or direct-damage values in the effective project layer.",
            "readOnly" => "Ange Fight values can be inspected, but project paths are not editable.",
            _ => "Ange Fight is using the verified vanilla HP and direct-damage values.",
        };
        var canUninstall = modified && isEditable && !isBlocked;
        var uninstallMessage = canUninstall
            ? "Uninstall restores only Ange Fight-owned values from the verified vanilla members."
            : modified
                ? "Fix the project paths or mapping diagnostics before staging uninstall."
                : "Ange Fight already matches the verified vanilla values.";

        return new ZaAngeFightWorkflow(
            summary,
            installStatus,
            installMessage,
            canUninstall,
            uninstallMessage,
            sources,
            flowers,
            attacks,
            new ZaAngeFightWorkflowStats(
                sources.Count,
                flowers.Length,
                attacks.Length,
                flowers.Length + (attacks.Length * 2)),
            diagnostics);
    }

    internal IReadOnlyList<ZaAngeFightPlanSource> GetPlanSources(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SourceDefinitions
            .Select(definition =>
            {
                var effective = fileSource.Read(project, definition.VirtualPath);
                var vanilla = fileSource.ReadBase(project, definition.VirtualPath);
                return new ZaAngeFightPlanSource(
                    definition.Id,
                    definition.VirtualPath,
                    effective,
                    vanilla);
            })
            .ToArray();
    }

    internal static IReadOnlyList<string> WritableVirtualPaths { get; } =
    [
        ZaDataPaths.FieldWazagimmickPublic,
        ZaDataPaths.AiAttackParamArray,
    ];

    private static ZaAngeFightSourceRecord CreateSourceRecord(
        SourceDefinition definition,
        ZaWorkflowFile effective,
        ZaWorkflowFile vanilla)
    {
        return new ZaAngeFightSourceRecord(
            definition.Id,
            definition.Label,
            effective.RelativePath,
            effective.SourceLayer == ProjectFileLayer.Layered ? "layered" : "base",
            Hash(effective.Bytes),
            Hash(vanilla.Bytes),
            new ZaAngeFightProvenance(
                effective.RelativePath,
                effective.SourceLayer,
                effective.FileState));
    }

    internal static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: ZaAngeFightEditSessionService.AngeFightEditDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record SourceDefinition(
        string Id,
        string Label,
        string VirtualPath);
}

internal sealed record ZaAngeFightPlanSource(
    string Id,
    string VirtualPath,
    ZaWorkflowFile Effective,
    ZaWorkflowFile Vanilla);
