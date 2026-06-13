// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Behavior;

public sealed class SwShBehaviorWorkflowService
{
    public const string BehaviorDataPath = "romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin";
    public const string EnglishSpeciesNamePath = "romfs/bin/message/English/common/monsname.dat";

    public const double MinimumNumberValue = -1_000_000;
    public const double MaximumNumberValue = 1_000_000;
    public const int MinimumFormValue = 0;
    public const int MaximumFormValue = 999;
    public const int MaximumByteValue = byte.MaxValue;
    public const int MaximumSpeciesIdValue = ushort.MaxValue;
    public const int MaximumStringLength = 128;

    private static readonly HashSet<string> EditableFields = new(StringComparer.Ordinal)
    {
        SwShSymbolBehaviorArchive.SpeciesIdField,
        SwShSymbolBehaviorArchive.FormField,
        SwShSymbolBehaviorArchive.BehaviorField,
        SwShSymbolBehaviorArchive.ModelPartField,
        SwShSymbolBehaviorArchive.HitboxRadiusField,
        SwShSymbolBehaviorArchive.GrassShakeRadiusField,
    };

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Behavior requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShBehaviorWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(summary, Array.Empty<SwShBehaviorEntryRecord>(), [], sourceFileCount: 0, diagnostics);
        }

        var behaviorSource = ResolveBehaviorDataSource(project);
        if (behaviorSource is null)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Behavior data is not available for this project.",
                expected: BehaviorDataPath));
            return CreateWorkflow(summary, Array.Empty<SwShBehaviorEntryRecord>(), [], sourceFileCount: 0, diagnostics);
        }

        var speciesNames = LoadSpeciesNames(project, diagnostics, out var speciesSourceCount);
        var presentSpeciesIds = SwShSpeciesAvailability.LoadPresentSpeciesIds(project);

        try
        {
            var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(behaviorSource.AbsolutePath));
            var provenance = CreateProvenance(behaviorSource.GraphEntry);
            var behaviorOptions = CreateBehaviorOptions(archive.Entries);
            var speciesOptions = CreateSpeciesOptions(speciesNames, presentSpeciesIds);
            var fields = CreateFields(speciesOptions, behaviorOptions);
            var entries = archive.Entries
                .Select(entry => CreateEntryRecord(entry, speciesNames, provenance))
                .OrderBy(entry => entry.SpeciesName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Form)
                .ThenBy(entry => entry.Index)
                .ToArray();

            return CreateWorkflow(
                summary,
                entries,
                fields,
                sourceFileCount: 1 + speciesSourceCount + (presentSpeciesIds.Count > 0 ? 1 : 0),
                diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior data source is not supported: {exception.Message}",
                file: behaviorSource.GraphEntry.RelativePath,
                expected: "Sword/Shield symbol encounter behavior data"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShBehaviorEntryRecord>(),
                [],
                sourceFileCount: 1 + speciesSourceCount + (presentSpeciesIds.Count > 0 ? 1 : 0),
                diagnostics);
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Behavior data source could not be read: {exception.Message}",
                file: behaviorSource.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield symbol encounter behavior data"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShBehaviorEntryRecord>(),
                [],
                sourceFileCount: 1 + speciesSourceCount + (presentSpeciesIds.Count > 0 ? 1 : 0),
                diagnostics);
        }
    }

    internal static WorkflowFileSource? ResolveBehaviorDataSource(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ResolveWorkflowFile(project, BehaviorDataPath);
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? targetPath
            : null;
    }

    internal static bool IsEditableField(string field)
    {
        return EditableFields.Contains(field);
    }

    internal static SwShBehaviorEntryRecord CreateEntryRecord(
        SwShSymbolBehaviorEntry entry,
        IReadOnlyList<string> speciesNames,
        SwShBehaviorProvenance provenance)
    {
        var speciesName = GetIndexedName(entry.SpeciesId, speciesNames, "Species");
        var form = entry.Form == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"-{entry.Form}");
        var behaviorLabel = GetBehaviorLabel(entry.Behavior);
        var mode = string.IsNullOrWhiteSpace(entry.Behavior) ? "No Behavior" : behaviorLabel;

        return new SwShBehaviorEntryRecord(
            entry.Index.ToString(CultureInfo.InvariantCulture),
            entry.Index,
            string.Create(CultureInfo.InvariantCulture, $"{entry.Index:000} {speciesName}{form} | {mode}"),
            entry.SpeciesId,
            speciesName,
            entry.Form,
            entry.Behavior,
            behaviorLabel,
            entry.ModelPart,
            entry.HitboxRadius,
            entry.GrassShakeRadius,
            FormatHash(entry.Hash1),
            FormatHash(entry.Hash2),
            entry.InternalSpeciesName,
            entry.Fields
                .Select(field => new SwShBehaviorFieldValue(field.Field, entry.GetStringValue(field.Field)))
                .ToArray(),
            provenance);
    }

    internal static IReadOnlyList<SwShBehaviorField> CreateFields(
        IReadOnlyList<SwShBehaviorFieldOption> speciesOptions,
        IReadOnlyList<SwShBehaviorFieldOption> behaviorOptions)
    {
        return SwShSymbolBehaviorArchive.FieldSpecs
            .Select(spec => CreateField(spec, speciesOptions, behaviorOptions))
            .ToArray();
    }

    internal static string GetBehaviorLabel(string behavior)
    {
        return behavior switch
        {
            "Anawohoru" => "Anawohoru - Diglett burrow / pop-up behavior",
            "Appeal" => "Appeal - notices player / attention behavior",
            "Approach" => "Approach - moves toward or chases player",
            "Common" => "Common - standard wild movement behavior",
            "Escape" => "Escape - flees from the player",
            "Haneru" => "Haneru - Magikarp splash / flop behavior",
            "Hindrance" => "Hindrance - Obstagoon blocking behavior",
            "Homing" => "Homing - charging pursuit behavior",
            "JumpWater" => "JumpWater - water jump / surface leap behavior",
            "Maggyo" => "Maggyo - Stunfisk trap behavior",
            "Massuguma" => "Massuguma - Linoone dash behavior",
            "Warp" => "Warp - teleport away behavior",
            "WaterDash" => "WaterDash - Sharpedo-style water dash",
            "Ziguzaguma" => "Ziguzaguma - Zigzagoon zigzag movement",
            _ => string.IsNullOrWhiteSpace(behavior) ? "No Behavior" : behavior,
        };
    }

    internal static string GetFieldLabel(string field)
    {
        return field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField => "Species",
            SwShSymbolBehaviorArchive.FormField => "Form",
            SwShSymbolBehaviorArchive.BehaviorField => "Behavior",
            SwShSymbolBehaviorArchive.ModelPartField => "Model Anchor",
            SwShSymbolBehaviorArchive.HitboxRadiusField => "Hitbox Radius",
            SwShSymbolBehaviorArchive.GrassShakeRadiusField => "Grass Shake Radius",
            SwShSymbolBehaviorArchive.Hash1Field => "Internal Hash 1",
            SwShSymbolBehaviorArchive.Hash2Field => "Internal Hash 2",
            SwShSymbolBehaviorArchive.InternalSpeciesNameField => "Internal Species Name",
            _ when field.StartsWith("field", StringComparison.Ordinal) && field.Length == 7 => $"Behavior Parameter {field[5..]}",
            _ => field,
        };
    }

    private static SwShBehaviorWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShBehaviorEntryRecord> entries,
        IReadOnlyList<SwShBehaviorField> fields,
        int sourceFileCount,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new SwShBehaviorWorkflow(
            summary,
            entries,
            fields,
            new SwShBehaviorWorkflowStats(
                entries.Count,
                entries.Select(entry => entry.Behavior).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                sourceFileCount),
            diagnostics);
    }

    private static SwShBehaviorField CreateField(
        SwShSymbolBehaviorFieldSpec spec,
        IReadOnlyList<SwShBehaviorFieldOption> speciesOptions,
        IReadOnlyList<SwShBehaviorFieldOption> behaviorOptions)
    {
        var field = spec.Field;
        var isReadOnly = !IsEditableField(field) || spec.IsUnusedDefault;
        var options = field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField => speciesOptions,
            SwShSymbolBehaviorArchive.BehaviorField => behaviorOptions,
            _ => Array.Empty<SwShBehaviorFieldOption>(),
        };
        var valueKind = spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => "number",
            SwShSymbolBehaviorFieldType.Int32 => "integer",
            SwShSymbolBehaviorFieldType.Byte => "integer",
            SwShSymbolBehaviorFieldType.UInt64 => "hash",
            SwShSymbolBehaviorFieldType.String => "string",
            _ => "string",
        };

        return new SwShBehaviorField(
            field,
            GetFieldLabel(field),
            GetFieldGroup(spec),
            valueKind,
            GetMinimumValue(spec),
            GetMaximumValue(spec),
            isReadOnly,
            GetFieldDescription(field, spec),
            options);
    }

    private static string GetFieldGroup(SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.IsUnusedDefault)
        {
            return "Unused Defaults";
        }

        return spec.Field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField
                or SwShSymbolBehaviorArchive.FormField
                or SwShSymbolBehaviorArchive.InternalSpeciesNameField => "Identity",
            SwShSymbolBehaviorArchive.BehaviorField => "Behavior",
            SwShSymbolBehaviorArchive.ModelPartField
                or SwShSymbolBehaviorArchive.HitboxRadiusField
                or SwShSymbolBehaviorArchive.GrassShakeRadiusField => "Collision / Range",
            SwShSymbolBehaviorArchive.Hash1Field
                or SwShSymbolBehaviorArchive.Hash2Field => "Internal References",
            _ when spec.Field.StartsWith("field", StringComparison.Ordinal) => "Behavior Tuning",
            _ => "Raw / Unknown",
        };
    }

    private static string GetFieldDescription(string field, SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.IsUnusedDefault)
        {
            return "Reserved value from the base table. Disabled until its role is confirmed.";
        }

        return field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField => "Pokemon species this behavior entry applies to.",
            SwShSymbolBehaviorArchive.FormField => "Form index for the selected species. Zero is the default form.",
            SwShSymbolBehaviorArchive.BehaviorField => "Primary field AI profile used by symbol encounters.",
            SwShSymbolBehaviorArchive.ModelPartField => "Named model part used as the interaction or collision anchor.",
            SwShSymbolBehaviorArchive.HitboxRadiusField => "Radius used by the symbol encounter's collision or interaction hitbox.",
            SwShSymbolBehaviorArchive.GrassShakeRadiusField => "Radius used by grass-shake behavior. Zero disables that radius for entries that do not use it.",
            SwShSymbolBehaviorArchive.Hash1Field
                or SwShSymbolBehaviorArchive.Hash2Field => "Unresolved internal reference. Disabled until its role is confirmed.",
            _ when field.StartsWith("field", StringComparison.Ordinal) => "Unmapped symbol AI tuning value. Disabled until its role is confirmed.",
            _ => string.Empty,
        };
    }

    private static double GetMinimumValue(SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.Field == SwShSymbolBehaviorArchive.FormField || spec.Field == SwShSymbolBehaviorArchive.SpeciesIdField)
        {
            return 0;
        }

        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => MinimumNumberValue,
            SwShSymbolBehaviorFieldType.Int32 => int.MinValue,
            SwShSymbolBehaviorFieldType.Byte => 0,
            SwShSymbolBehaviorFieldType.UInt64 => 0,
            _ => 0,
        };
    }

    private static double GetMaximumValue(SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.Field == SwShSymbolBehaviorArchive.FormField)
        {
            return MaximumFormValue;
        }

        if (spec.Field == SwShSymbolBehaviorArchive.SpeciesIdField)
        {
            return MaximumSpeciesIdValue;
        }

        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => MaximumNumberValue,
            SwShSymbolBehaviorFieldType.Int32 => int.MaxValue,
            SwShSymbolBehaviorFieldType.Byte => MaximumByteValue,
            SwShSymbolBehaviorFieldType.UInt64 => ulong.MaxValue,
            _ => MaximumStringLength,
        };
    }

    private static IReadOnlyList<SwShBehaviorFieldOption> CreateBehaviorOptions(IReadOnlyList<SwShSymbolBehaviorEntry> entries)
    {
        return entries
            .Select(entry => entry.Behavior)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(value => new SwShBehaviorFieldOption(value, GetBehaviorLabel(value)))
            .ToArray();
    }

    private static IReadOnlyList<SwShBehaviorFieldOption> CreateSpeciesOptions(
        IReadOnlyList<string> speciesNames,
        IReadOnlySet<int> presentSpeciesIds)
    {
        return SwShSpeciesAvailability.CreateSpeciesOptions(
            speciesNames,
            presentSpeciesIds,
            (value, label) => new SwShBehaviorFieldOption(
                value.ToString(CultureInfo.InvariantCulture),
                label));
    }

    private static string[] LoadSpeciesNames(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out int sourceFileCount)
    {
        var source = ResolveWorkflowFile(project, EnglishSpeciesNamePath);
        if (source is null)
        {
            sourceFileCount = 0;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Species names are not available; species IDs will be shown as fallback names.",
                expected: EnglishSpeciesNamePath));
            return [];
        }

        sourceFileCount = 1;
        try
        {
            return SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath))
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield monsname.dat"));
            return [];
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield monsname.dat"));
            return [];
        }
    }

    private static string GetIndexedName(int index, IReadOnlyList<string> names, string fallbackPrefix)
    {
        return (uint)index < (uint)names.Count && !string.IsNullOrWhiteSpace(names[index])
            ? names[index]
            : string.Create(CultureInfo.InvariantCulture, $"{fallbackPrefix} {index}");
    }

    private static string FormatHash(ulong hash)
    {
        const ulong fnvEmptyHash = 14695981039346656837;
        return hash == fnvEmptyHash
            ? string.Create(CultureInfo.InvariantCulture, $"None (0x{hash:X16})")
            : string.Create(CultureInfo.InvariantCulture, $"0x{hash:X16}");
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);

        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry, sourcePath)
            : null;
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SwShBehaviorProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShBehaviorProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Behavior,
            "Behavior",
            "Symbol encounter behavior profiles, model anchors, collision radii, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
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
            Field: field,
            Domain: "workflow.behavior",
            Expected: expected);
    }
}

internal sealed record WorkflowFileSource(
    ProjectFileGraphEntry GraphEntry,
    string AbsolutePath);
