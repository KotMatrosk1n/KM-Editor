// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.ZA.Data;
using KM.ZA.Workflows;

namespace KM.ZA.Behavior;

public sealed record ZaBehaviorResource(string EntryId, int SpeciesId, string SpeciesName, int Form,
    int Gender, string SourceFile, string SourceLayer, string Profile, bool IsInitialized,
    IReadOnlyList<string> Tags, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> VanillaTags, IReadOnlyDictionary<string, string> VanillaFields, string VanillaProfile);
public sealed record ZaBehaviorField(string Field, string Label, double Minimum, double Maximum,
    double StockMinimum, double StockMaximum);
public sealed record ZaBehaviorProfile(string Value, string Label);
public sealed record ZaBehaviorWorkflow(ZaWorkflowSummary Summary, IReadOnlyList<ZaBehaviorResource> Resources,
    IReadOnlyList<ZaBehaviorField> Fields, IReadOnlyList<ZaBehaviorProfile> Profiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
public sealed record ZaBehaviorUpdate(string EntryId, string Field, string Value);
public sealed record ZaBehaviorEditResult(ZaBehaviorWorkflow Workflow, EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

internal static class ZaBehaviorSettings
{
    internal const string Domain = "workflow.behavior";
    internal const string CatalogPath = "param_chr/catalog/bin/sources/flatbuffers/character/pokemon/wild_single.data.bin";
    internal const string Prefix = "param_chr/data/character/pokemon/wild_single/";
    internal static readonly ZaBehaviorField[] Fields =
    [
        new("viewingHorizontalAngle", "Horizontal viewing angle", 0, float.MaxValue, 120, 120),
        new("viewingVerticalAngle", "Vertical viewing angle", 0, float.MaxValue, 60, 60),
        new("viewingRange", "Sight range", 0, float.MaxValue, 6, 15),
        new("hearingRange", "Hearing range", 0, float.MaxValue, 6, 20),
        new("homerange", "Home range", 0, float.MaxValue, 5, 30),
    ];
    internal static readonly Dictionary<string, string[]> Profiles = new(StringComparer.Ordinal)
    {
        ["aggressive"] = ["Warlike"],
        ["gentle1"] = ["Gentle", "Gentle1"],
        ["gentle2"] = ["Gentle", "Gentle2"],
        ["gentle4"] = ["Gentle", "Gentle4"],
        ["timid"] = ["Cowardice"],
        ["sluggish"] = ["Torpor", "Torpor1"],
    };
    internal static readonly ZaBehaviorProfile[] Options =
    [new("aggressive", "Aggressive"), new("gentle1", "Gentle (variant 1)"),
     new("gentle2", "Gentle (variant 2)"), new("gentle4", "Gentle (variant 4)"),
     new("timid", "Timid"), new("sluggish", "Sluggish")];
    internal static bool IsTemperament(string tag) => tag is "Warlike" or "Gentle" or "Cowardice" or "Torpor"
        || Enumerable.Range(1, 10).Any(i => tag == $"Gentle{i}" || tag == $"Torpor{i}");
    internal static string Profile(IEnumerable<string> tags)
    {
        var owned = tags.Where(IsTemperament).ToHashSet(StringComparer.Ordinal);
        return Profiles.FirstOrDefault(p => owned.SetEquals(p.Value)).Key ?? "custom";
    }
    internal static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    internal static ValidationDiagnostic Error(string suffix, string message, string? field = null, string? file = null) =>
        new(DiagnosticSeverity.Error, message, file, Domain, field) { Code = "KM-ZA-BEHAVIOR-" + suffix };
    internal static bool SourceFailure(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or FormatException or OverflowException or InvalidOperationException;
}

internal sealed class ZaBehaviorWorkflowService(ZaWorkflowFileSource files)
{
    internal ZaWorkflowSummary CreateSummary(OpenedProject project) => ZaWorkflowSupport.CreateSummary(project,
        "behavior", "Behavior", "Edit wild temperament, perception and home range.");

    internal ZaBehaviorState LoadState(OpenedProject project)
    {
        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var sources = new Dictionary<string, ZaWorkflowFile>(StringComparer.Ordinal);
        var resources = new List<ZaBehaviorResource>();
        ZaWorkflowFile? catalog = null;
        if (project.Paths.SelectedGame != ProjectGame.ZA || summary.Availability == ZaWorkflowAvailability.Disabled)
            return new(new(summary, resources, ZaBehaviorSettings.Fields, ZaBehaviorSettings.Options, diagnostics), sources, catalog);
        using var reads = ZaWorkflowFileSource.BeginFreshReadScope(project.Paths);
        try
        {
            catalog = files.Read(project, ZaBehaviorSettings.CatalogPath);
            var identities = ZaBehaviorDocument.Catalog(catalog.Bytes);
            var labels = ZaTextLabelLookup.Load(project, files, diagnostics, project.Paths);
            foreach (var identity in identities)
            {
                var path = ZaBehaviorSettings.Prefix + identity + ".bin";
                try
                {
                    var source = files.Read(project, path);
                    var doc = new ZaBehaviorDocument(source.Bytes);
                    sources.Add(identity, source);
                    var species = int.Parse(identity.AsSpan(0, 4), CultureInfo.InvariantCulture);
                    var vanilla = source.SourceLayer == KM.Core.Files.ProjectFileLayer.Base ? doc
                        : new ZaBehaviorDocument(files.ReadBase(project, source.VirtualPath).Bytes);
                    resources.Add(Project(identity, labels.Pokemon(species), source, doc, vanilla));
                }
                catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
                {
                    diagnostics.Add(ZaBehaviorSettings.Error("SOURCE-UNAVAILABLE", "A Behavior resource could not be read.", file: "romfs/" + path)
                        with { Severity = DiagnosticSeverity.Warning });
                }
            }
        }
        catch (Exception e) when (ZaBehaviorSettings.SourceFailure(e))
        { diagnostics.Add(ZaBehaviorSettings.Error("SOURCE-UNAVAILABLE", "Behavior data could not be loaded.")); }
        if (resources.Count == 0 && diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
            diagnostics.Add(ZaBehaviorSettings.Error("SOURCE-UNAVAILABLE", "No readable Behavior resources are available."));
        return new(new(summary, resources, ZaBehaviorSettings.Fields, ZaBehaviorSettings.Options, diagnostics), sources, catalog);
    }

    internal static ZaBehaviorResource Project(string id, string name, ZaWorkflowFile source, ZaBehaviorDocument doc, ZaBehaviorDocument vanilla) =>
        new(id, int.Parse(id.AsSpan(0, 4), CultureInfo.InvariantCulture), name,
            int.Parse(id.AsSpan(5, 2), CultureInfo.InvariantCulture), int.Parse(id.AsSpan(8, 2), CultureInfo.InvariantCulture),
            source.RelativePath, source.SourceLayer.ToString(), ZaBehaviorSettings.Profile(doc.Tags),
            doc.Tags.Contains("State_usually") && doc.Tags.Contains("WildTeam"), doc.Tags,
            ToFields(doc), vanilla.Tags, ToFields(vanilla), ZaBehaviorSettings.Profile(vanilla.Tags));

    private static IReadOnlyDictionary<string, string> ToFields(ZaBehaviorDocument doc) =>
        ZaBehaviorSettings.Fields.Select((f, i) => (f.Field, Value: ZaBehaviorSettings.Format(doc.Values[i])))
            .ToDictionary(p => p.Field, p => p.Value);
}

internal sealed record ZaBehaviorState(ZaBehaviorWorkflow Workflow,
    Dictionary<string, ZaWorkflowFile> Sources, ZaWorkflowFile? Catalog);
