// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Behavior;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;

namespace KM.SwSh.Workflows;

internal static class SwShWorkflowDependencyValidator
{
    private const string MessageRootPath = "romfs/bin/message";
    private const string EnglishCommonMessagePath = "romfs/bin/message/English/common";

    public static SwShWorkflowSummary Apply(OpenedProject project, SwShWorkflowSummary summary)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(summary);

        if (!project.Health.CanOpenReadOnlyWorkflows || summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return summary;
        }

        var dependencies = GetDependencies(summary.Id);
        if (dependencies.Count == 0)
        {
            return summary;
        }

        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        foreach (var dependency in dependencies)
        {
            ValidateDependency(project, summary.Id, dependency, diagnostics);
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? summary with
            {
                Availability = SwShWorkflowAvailability.Disabled,
                Diagnostics = diagnostics,
            }
            : summary with { Diagnostics = diagnostics };
    }

    private static IReadOnlyList<WorkflowDependency> GetDependencies(string workflowId)
    {
        return workflowId switch
        {
            SwShWorkflowIds.Items =>
            [
                File("item data", SwShItemsWorkflowService.ItemDataPath),
                File("item names", SwShItemsWorkflowService.EnglishItemNamePath),
                File("move names for TM/TR labels", SwShItemsWorkflowService.EnglishMoveNamePath),
            ],
            SwShWorkflowIds.Pokemon =>
            [
                File("personal data", SwShPokemonWorkflowService.PersonalDataPath),
                File("learnset data", SwShPokemonWorkflowService.LearnsetDataPath),
                Prefix("evolution data", SwShPokemonWorkflowService.EvolutionDataDirectory),
                File("Pokemon display names", SwShPokemonWorkflowService.EnglishPokemonNamePath),
                File("species names", SwShPokemonWorkflowService.EnglishSpeciesNamePath),
                File("item names", SwShPokemonWorkflowService.EnglishItemNamePath),
                File("ability names", SwShPokemonWorkflowService.EnglishAbilityNamePath),
                File("move names", SwShPokemonWorkflowService.EnglishMoveNamePath),
            ],
            SwShWorkflowIds.Moves =>
            [
                Prefix("move data", SwShMovesWorkflowService.MoveDataDirectory),
                File("move names", SwShMovesWorkflowService.EnglishMoveNamePath),
                File("move descriptions", SwShMovesWorkflowService.EnglishMoveDescriptionPath),
                File("type names", SwShMovesWorkflowService.EnglishTypeNamePath),
            ],
            SwShWorkflowIds.Text =>
            [
                Prefix("message data", SwShTextWorkflowService.MessageRootPath),
            ],
            SwShWorkflowIds.Trainers =>
            [
                Prefix("trainer data", SwShTrainersWorkflowService.TrainerDataRootPath),
                Prefix("trainer teams", SwShTrainersWorkflowService.TrainerPokeRootPath),
                Prefix("trainer classes", SwShTrainersWorkflowService.TrainerClassRootPath),
                Prefix("trainer lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.GiftPokemon =>
            [
                File("gift Pokemon data", SwShGiftPokemonWorkflowService.GiftPokemonDataPath),
                Prefix("gift lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.TradePokemon =>
            [
                AnyFile(
                    "trade Pokemon data",
                    SwShTradePokemonWorkflowService.TradePokemonDataPath,
                    SwShTradePokemonWorkflowService.LegacyTradePokemonDataPath),
                Prefix("trade lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.StaticEncounters =>
            [
                AnyFile(
                    "static encounter data",
                    SwShStaticEncountersWorkflowService.StaticEncounterDataPath,
                    SwShStaticEncountersWorkflowService.LegacyStaticEncounterDataPath),
                Prefix("static encounter lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.RentalPokemon =>
            [
                File("rental Pokemon data", SwShRentalPokemonWorkflowService.RentalPokemonDataPath),
                Prefix("rental lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.DynamaxAdventures =>
            [
                File("Dynamax Adventures Pokemon data", SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath),
                Prefix("Dynamax Adventures lookup text", EnglishCommonMessagePath),
            ],
            SwShWorkflowIds.Shops =>
            [
                AnyFile(
                    "shop data",
                    SwShShopsWorkflowService.ShopDataPath,
                    SwShShopsWorkflowService.LegacyShopDataPath),
                File("item data for inventory names and prices", SwShItemsWorkflowService.ItemDataPath),
                File("item names", SwShItemsWorkflowService.EnglishItemNamePath),
            ],
            SwShWorkflowIds.Encounters =>
            [
                File("wild encounter data", SwShEncountersWorkflowService.WildDataPath),
                File("species names", $"{EnglishCommonMessagePath}/monsname.dat"),
            ],
            SwShWorkflowIds.RaidBattles =>
            [
                File("raid battle data", SwShRaidRewardsWorkflowService.NestDataPath),
                File("species names", $"{EnglishCommonMessagePath}/monsname.dat"),
            ],
            SwShWorkflowIds.RaidRewards =>
            [
                File("raid reward data", SwShRaidRewardsWorkflowService.NestDataPath),
                File("item names", SwShRaidRewardsWorkflowService.EnglishItemNamePath),
            ],
            SwShWorkflowIds.RaidBonusRewards =>
            [
                File("raid bonus reward data", SwShRaidRewardsWorkflowService.NestDataPath),
                File("item names", SwShRaidRewardsWorkflowService.EnglishItemNamePath),
            ],
            SwShWorkflowIds.Placement =>
            [
                File("placement data", SwShPlacementWorkflowService.PlacementDataPath),
                File("item hash table", SwShPlacementWorkflowService.ItemHashPath),
                File("item names", SwShPlacementWorkflowService.EnglishItemNamePath),
            ],
            SwShWorkflowIds.Behavior =>
            [
                File("behavior data", SwShBehaviorWorkflowService.BehaviorDataPath),
                File("species names", SwShBehaviorWorkflowService.EnglishSpeciesNamePath),
            ],
            SwShWorkflowIds.FlagworkSave =>
            [
                Prefix("flagwork data", SwShFlagworkSaveWorkflowService.FlagworkRootPath),
            ],
            SwShWorkflowIds.ExeFsPatches =>
            [
                File("ExeFS main", SwShExeFsPatchWorkflowService.ExeFsMainPath),
            ],
            SwShWorkflowIds.RoyalCandy =>
            [
                File("Royal Candy item data", SwShRoyalCandyWorkflowService.ItemPath),
                File("Royal Candy item hash table", SwShRoyalCandyWorkflowService.ItemHashPath),
                AnyFile(
                    "Royal Candy shop data",
                    SwShRoyalCandyWorkflowService.ShopDataPath,
                    SwShRoyalCandyWorkflowService.LegacyShopDataPath),
                File("Royal Candy raid reward data", SwShRoyalCandyWorkflowService.NestDataPath),
                File("Royal Candy placement data", SwShRoyalCandyWorkflowService.PlacementPath),
                File("Royal Candy bag event script", SwShRoyalCandyWorkflowService.BagEventScriptPath),
                File("Royal Candy ExeFS main", SwShRoyalCandyWorkflowService.ExeFsMainPath),
            ],
            SwShWorkflowIds.SpreadsheetImport =>
            [
                Prefix("spreadsheet-import lookup text", MessageRootPath),
            ],
            _ => [],
        };
    }

    private static void ValidateDependency(
        OpenedProject project,
        string workflowId,
        WorkflowDependency dependency,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var candidates = dependency.Candidates
            .Select(candidate => ValidateCandidate(project, candidate))
            .ToArray();

        if (candidates.Any(candidate => candidate.IsValid))
        {
            return;
        }

        var expected = string.Join(" or ", dependency.Candidates.Select(candidate => candidate.RelativePath));
        var layeredOnly = candidates.FirstOrDefault(candidate => candidate.HasLayeredOnly);
        if (layeredOnly is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                workflowId,
                DiagnosticSeverity.Error,
                $"{dependency.Label} is misaligned: LayeredFS output contains '{layeredOnly.Candidate.RelativePath}', but the base dump is missing the matching dependency.",
                layeredOnly.Candidate.RelativePath,
                expected));
            return;
        }

        var emptyOrUnreadable = candidates.FirstOrDefault(candidate => candidate.HasEmptyOrUnreadableFile);
        if (emptyOrUnreadable is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                workflowId,
                DiagnosticSeverity.Error,
                $"{dependency.Label} dependency '{emptyOrUnreadable.Candidate.RelativePath}' is empty or unreadable.",
                emptyOrUnreadable.Candidate.RelativePath,
                "Readable non-empty Sword/Shield dependency"));
            return;
        }

        diagnostics.Add(CreateDiagnostic(
            workflowId,
            DiagnosticSeverity.Error,
            $"{dependency.Label} dependency is missing.",
            dependency.Candidates[0].RelativePath,
            expected));
    }

    private static CandidateValidation ValidateCandidate(OpenedProject project, DependencyCandidate candidate)
    {
        var entries = candidate.Kind == DependencyKind.ExactFile
            ? project.FileGraph.Entries
                .Where(entry => string.Equals(entry.RelativePath, candidate.RelativePath, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : project.FileGraph.Entries
                .Where(entry => entry.RelativePath.StartsWith(EnsurePrefix(candidate.RelativePath), StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (entries.Length == 0)
        {
            return new CandidateValidation(candidate, IsValid: false);
        }

        var hasLayeredOnlyEntry = entries.Any(entry => entry.BaseFile is null && entry.LayeredFile is not null);

        foreach (var entry in entries)
        {
            if (entry.BaseFile is null)
            {
                continue;
            }

            var sourcePath = ResolveSourcePath(project.Paths, entry);
            if (sourcePath is null || !System.IO.File.Exists(sourcePath))
            {
                continue;
            }

            try
            {
                if (new FileInfo(sourcePath).Length > 0)
                {
                    return new CandidateValidation(candidate, IsValid: true);
                }
            }
            catch (IOException)
            {
                return new CandidateValidation(candidate, IsValid: false, HasEmptyOrUnreadableFile: true);
            }
            catch (UnauthorizedAccessException)
            {
                return new CandidateValidation(candidate, IsValid: false, HasEmptyOrUnreadableFile: true);
            }
        }

        return new CandidateValidation(
            candidate,
            IsValid: false,
            HasLayeredOnly: hasLayeredOnlyEntry,
            HasEmptyOrUnreadableFile: entries.Any(entry => entry.BaseFile is not null));
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        var relativePath = entry.RelativePath;
        var sourceRoot = entry.LayeredFile is not null
            ? paths.OutputRootPath
            : relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                ? paths.BaseExeFsPath
                : paths.BaseRomFsPath;
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return null;
        }

        var sourceRelativePath = entry.LayeredFile is not null
            ? relativePath
            : relativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                ? relativePath["romfs/".Length..]
                : relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase)
                    ? relativePath["exefs/".Length..]
                    : relativePath;

        return Path.GetFullPath(Path.Combine(
            sourceRoot,
            sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string EnsurePrefix(string relativePath)
    {
        return relativePath.EndsWith("/", StringComparison.Ordinal)
            ? relativePath
            : relativePath + "/";
    }

    private static WorkflowDependency File(string label, string relativePath)
    {
        return new WorkflowDependency(label, [new DependencyCandidate(DependencyKind.ExactFile, relativePath)]);
    }

    private static WorkflowDependency AnyFile(string label, params string[] relativePaths)
    {
        return new WorkflowDependency(
            label,
            relativePaths
                .Select(relativePath => new DependencyCandidate(DependencyKind.ExactFile, relativePath))
                .ToArray());
    }

    private static WorkflowDependency Prefix(string label, string relativePath)
    {
        return new WorkflowDependency(label, [new DependencyCandidate(DependencyKind.Prefix, relativePath)]);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        string workflowId,
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: $"workflow.{workflowId}.dependencies",
            Expected: expected);
    }

    private enum DependencyKind
    {
        ExactFile,
        Prefix,
    }

    private sealed record WorkflowDependency(
        string Label,
        IReadOnlyList<DependencyCandidate> Candidates);

    private sealed record DependencyCandidate(
        DependencyKind Kind,
        string RelativePath);

    private sealed record CandidateValidation(
        DependencyCandidate Candidate,
        bool IsValid,
        bool HasLayeredOnly = false,
        bool HasEmptyOrUnreadableFile = false);
}
