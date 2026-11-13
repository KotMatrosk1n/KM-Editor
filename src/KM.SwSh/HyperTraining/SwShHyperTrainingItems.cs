// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.NpcItemGift;

namespace KM.SwSh.HyperTraining;

public sealed record SwShHyperTrainingItems(int BottleCapItemId, int GoldBottleCapItemId);
public sealed record SwShHyperTrainingItemOption(int ItemId, string Name);
internal sealed record SwShHyperTrainingSelection(int MinimumLevel, SwShHyperTrainingItems Items, bool PokemonCenterEnabled = false)
{
    internal string Payload => PokemonCenterEnabled
        ? string.Create(CultureInfo.InvariantCulture, $"{MinimumLevel},{Items.BottleCapItemId},{Items.GoldBottleCapItemId},1")
        : Items == SwShHyperTrainingItemsPatcher.Defaults
        ? MinimumLevel.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{MinimumLevel},{Items.BottleCapItemId},{Items.GoldBottleCapItemId}");
    internal static SwShHyperTrainingSelection Parse(string? value)
    {
        var parts = value?.Split(',') ?? [];
        if (parts.Length is not (1 or 3 or 4) || parts.Any(part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new InvalidDataException("Hyper Training settings are invalid.");
        var level = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var items = parts.Length == 1 ? SwShHyperTrainingItemsPatcher.Defaults
            : new SwShHyperTrainingItems(int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
        var selection = new SwShHyperTrainingSelection(level, items, parts.Length == 4 && parts[3] == "1");
        SwShHyperTrainingItemsPatcher.Validate(items);
        if (level is < 1 or > 100 || selection.Payload != value) throw new InvalidDataException("Hyper Training settings are not canonical.");
        return selection;
    }
}

internal static class SwShHyperTrainingItemWorkflow
{
    internal static SwShHyperTrainingWorkflow Enrich(OpenedProject project, SwShHyperTrainingWorkflow workflow)
    {
        if (workflow.InstallStatus == "blocked" || workflow.DetectedGame is null) return workflow;
        try
        {
            byte[] Source(string path) => File.ReadAllBytes(SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path)?.AbsolutePath
                ?? throw new InvalidDataException("Hyper Training item source is unavailable."));
            byte[] Base(string path) => File.ReadAllBytes(SwShHyperTrainingWorkflowService.ResolveBaseSourcePath(project.Paths, path)
                ?? throw new InvalidDataException("Hyper Training item Base source is unavailable."));
            if (SwShHyperTrainingItemsPatcher.ReadScript(Base(SwShHyperTrainingWorkflowService.ScriptPath)) != SwShHyperTrainingItemsPatcher.Defaults
                || SwShHyperTrainingItemsPatcher.ReadMain(Base(SwShHyperTrainingWorkflowService.ExeFsMainPath), project.Paths.SelectedGame) != SwShHyperTrainingItemsPatcher.Defaults)
                throw new InvalidDataException("Hyper Training requires original Base payment items.");
            var script = SwShHyperTrainingItemsPatcher.ReadScript(Source(SwShHyperTrainingWorkflowService.ScriptPath));
            var runtime = SwShHyperTrainingItemsPatcher.ReadMain(Source(SwShHyperTrainingWorkflowService.ExeFsMainPath), project.Paths.SelectedGame);
            var options = new SwShItemsWorkflowService().LoadForLookup(project).Items
                .Where(item => SwShNpcItemGiftWorkflowService.IsSelectableNpcGiftItem(item)
                    && item.Metadata.Pouch is >= 0 and <= 7 && item.Metadata.Pouch != (int)SwShItemPouch.TMs)
                .Select(item => new SwShHyperTrainingItemOption(item.ItemId, item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.ItemId).ToArray();
            var customItems = script != SwShHyperTrainingItemsPatcher.Defaults || runtime != script
                || SwShHyperTrainingItemDialogue.HasGeneratedDialogue(project);
            var sources = workflow.Sources.ToList();
            if (customItems)
            {
                foreach (var path in SwShHyperTrainingItemDialogue.Targets(project))
                {
                    if (sources.Any(source => source.RelativePath == path)) continue;
                    var entry = SwShHyperTrainingWorkflowService.ResolveWorkflowFile(project, path)!.Entry;
                    sources.Add(new(path, path, path, "available", new(path,
                        entry.LayeredFile is null ? ProjectFileLayer.Base : ProjectFileLayer.Layered, entry.State)));
                }
            }
            return workflow with
            {
                Items = script, RuntimeItems = runtime, ItemOptions = options,
                InstallStatus = customItems && workflow.InstallStatus != "readOnly" ? "installed" : workflow.InstallStatus,
                Sources = sources,
                Stats = customItems ? new(sources.Count(source => source.Status == "available"),
                    2 + SwShHyperTrainingItemDialogue.Targets(project).Count()) : workflow.Stats,
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return workflow with { InstallStatus = "blocked", Diagnostics = [.. workflow.Diagnostics, Diagnostic(exception.Message)] };
        }
    }

    internal static void ValidateSelection(SwShHyperTrainingItems items, SwShHyperTrainingWorkflow workflow)
    {
        SwShHyperTrainingItemsPatcher.Validate(items);
        if (items != SwShHyperTrainingItemsPatcher.Defaults
            && (!workflow.ItemOptions.Any(option => option.ItemId == items.BottleCapItemId)
                || !workflow.ItemOptions.Any(option => option.ItemId == items.GoldBottleCapItemId)))
            throw new InvalidDataException("Choose available bag items for Hyper Training. Key items and machines cannot be consumed.");
    }

    internal static ValidationDiagnostic Diagnostic(string message) => new(DiagnosticSeverity.Error, message,
        Domain: "workflow.hyperTraining", Field: "items") { Code = "KM-SWSH-HYPER-TRAINING-ITEMS-INVALID" };
}
