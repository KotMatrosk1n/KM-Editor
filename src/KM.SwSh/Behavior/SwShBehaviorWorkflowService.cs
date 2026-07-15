// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KM.SwSh.Behavior;

public sealed class SwShBehaviorWorkflowService
{
    public const string BehaviorEditDomain = "workflow.behavior";
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

    private static readonly IReadOnlyDictionary<string, BehaviorFieldMapping> FieldMappings =
        new Dictionary<string, BehaviorFieldMapping>(StringComparer.Ordinal)
        {
            [SwShSymbolBehaviorArchive.Field00] = new(
                "Likely Scale X Multiplier",
                "Model / Offset",
                "Most vanilla entries use 1.0, matching a model scale multiplier slot. Likely, but read-only until the loader mapping is confirmed."),
            [SwShSymbolBehaviorArchive.Field01] = new(
                "Likely Scale Y Multiplier",
                "Model / Offset",
                "Most vanilla entries use 1.0, matching a second model scale multiplier slot. Likely, but read-only until the loader mapping is confirmed."),
            [SwShSymbolBehaviorArchive.Field03] = new(
                "Likely Model Scale",
                "Model / Offset",
                "Varies with species visual size across the vanilla table. Likely model scale tuning, but read-only until the loader mapping is confirmed."),
            [SwShSymbolBehaviorArchive.Field07] = new(
                "Likely Rare Position Offset",
                "Model / Offset",
                "Only a handful of vanilla entries use this signed offset-like value. Exact target is not confirmed, so it stays read-only."),
            [SwShSymbolBehaviorArchive.Field08] = CreateUnusedMapping("08"),
            [SwShSymbolBehaviorArchive.Field09] = new(
                "Likely Height Offset",
                "Model / Offset",
                "Common signed offset-like value used by many entries. Likely vertical placement or collision offset tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field11] = new(
                "Likely Watch/Hearing Flag",
                "Watch / Reaction",
                "Sparse byte flag. Native AI exposes watch and hearing boolean getters, but this exact FlatBuffer slot is not proven, so it stays read-only."),
            [SwShSymbolBehaviorArchive.Field12] = CreateUnusedMapping("12"),
            [SwShSymbolBehaviorArchive.Field14] = CreateUnusedMapping("14"),
            [SwShSymbolBehaviorArchive.Field15] = CreateUnusedMapping("15"),
            [SwShSymbolBehaviorArchive.Field16] = new(
                "Likely Scale Tuning A",
                "Model / Offset",
                "Multiplier-shaped value that is 1.0 for almost every vanilla entry. Likely scale or offset tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field17] = new(
                "Likely Scale Tuning B",
                "Model / Offset",
                "Multiplier-shaped value that is 1.0 for almost every vanilla entry. Likely paired scale or offset tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field18] = new(
                "Likely Movement Frame Count",
                "Movement Timing",
                "Frame-count-shaped integer range in vanilla data. Likely movement or animation timing, but read-only until the runtime loader mapping is proven."),
            [SwShSymbolBehaviorArchive.Field19] = new(
                "Likely Offset Tuning A",
                "Model / Offset",
                "Sparse signed offset-like value. It appears to tune placement or collision for special body shapes, but the exact target is not confirmed."),
            [SwShSymbolBehaviorArchive.Field20] = new(
                "Likely Offset Tuning B",
                "Model / Offset",
                "Sparse signed offset-like value with large overrides on a few entries. It stays read-only until the exact runtime use is confirmed."),
            [SwShSymbolBehaviorArchive.Field21] = new(
                "Likely Water Height Offset",
                "Model / Offset",
                "Water and floating entries carry signed height offsets here, and Lua uses GetOffsetWaterParam for water collision and event offsets. Likely, but read-only until the loader mapping is confirmed."),
            [SwShSymbolBehaviorArchive.Field23] = new(
                "Likely Movement Tuning A",
                "Movement Timing",
                "Default 8.0 on most entries. Likely movement or wait timing tuning, but read-only until the exact native getter mapping is confirmed."),
            [SwShSymbolBehaviorArchive.Field24] = new(
                "Likely Movement Tuning B",
                "Movement Timing",
                "Default 5.0 on most entries. Likely paired movement or wait timing tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field25] = new(
                "Likely Motion Mode",
                "Movement Timing",
                "Small enum-like value from 0 to 5 in vanilla data. Likely selects a motion or wait behavior variant, but the exact mode table is not confirmed."),
            [SwShSymbolBehaviorArchive.Field26] = new(
                "Likely Event Collision Radius",
                "Watch / Collision",
                "Sparse radius-like value separate from the main hitbox radius. Likely event or encounter collision tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field28] = CreateUnusedMapping("28"),
            [SwShSymbolBehaviorArchive.Field29] = new(
                "Likely Motion Variant",
                "Movement Timing",
                "Small enum-like value from 0 to 3 in vanilla data. Likely selects a motion variant, but the exact native use is not confirmed."),
            [SwShSymbolBehaviorArchive.Field30] = CreateUnusedMapping("30"),
            [SwShSymbolBehaviorArchive.Field32] = new(
                "Likely Animation Frame Baseline",
                "Movement Timing",
                "Two common frame-like values, 48 and 52, dominate the vanilla table. Likely animation timing baseline, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field33] = CreateUnusedMapping("33"),
            [SwShSymbolBehaviorArchive.Field34] = CreateUnusedMapping("34"),
            [SwShSymbolBehaviorArchive.Field35] = CreateUnusedMapping("35"),
            [SwShSymbolBehaviorArchive.Field36] = CreateUnusedMapping("36"),
            [SwShSymbolBehaviorArchive.Field37] = new(
                "Likely Turn Speed",
                "Movement Timing",
                "Small speed-like values match the native AI's turn-speed getter pattern. Likely turn speed, but read-only until the FlatBuffer-to-runtime copy is proven."),
            [SwShSymbolBehaviorArchive.Field38] = new(
                "Likely Move Frame Minimum",
                "Movement Timing",
                "Constant 25.0 in vanilla data. Likely a movement frame minimum or wait default, but the exact getter slot is not confirmed."),
            [SwShSymbolBehaviorArchive.Field39] = new(
                "Likely Watch Angle",
                "Watch / Reaction",
                "Angle-shaped values such as 60, 270, and 300 appear here. Likely watch-out angle tuning, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field40] = new(
                "Likely Wait Frequency",
                "Movement Timing",
                "Mostly 45.0 with high overrides on a few entries. Likely wait or movement timing tuning, but the exact use is not confirmed."),
            [SwShSymbolBehaviorArchive.Field41] = new(
                "Likely Wait Frame",
                "Movement Timing",
                "Constant 45.0 in vanilla data. Likely a wait-frame default, but read-only until confirmed."),
            [SwShSymbolBehaviorArchive.Field42] = CreateUnusedMapping("42"),
            [SwShSymbolBehaviorArchive.Field43] = CreateUnusedMapping("43"),
            [SwShSymbolBehaviorArchive.Field44] = new(
                "Likely Watch Distance",
                "Watch / Reaction",
                "Almost every vanilla entry uses 800.0, with a 2000.0 long-range outlier. Native Lua reads watch-out distance, making this a strong candidate but still read-only."),
            [SwShSymbolBehaviorArchive.Field45] = new(
                "Likely Watch Radius",
                "Watch / Reaction",
                "Almost every vanilla entry uses 7.5, with a small species-specific override. Likely watch-out radius, but read-only until the loader mapping is confirmed."),
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
        var personalRecords = LoadPersonalRecords(project, diagnostics, out var personalSourceCount);
        var presentSpeciesIds = SwShSpeciesAvailability.CreatePresentSpeciesIds(personalRecords);

        try
        {
            var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(behaviorSource.AbsolutePath));
            var provenance = CreateProvenance(behaviorSource.GraphEntry);
            var behaviorOptions = CreateBehaviorOptions(archive.Entries);
            var modelPartOptions = CreateModelPartOptions(archive.Entries);
            var speciesOptions = CreateSpeciesOptions(speciesNames, presentSpeciesIds);
            var fields = CreateFields(speciesOptions, behaviorOptions, modelPartOptions);
            var entries = archive.Entries
                .Select(entry => CreateEntryRecord(entry, speciesNames, personalRecords, provenance))
                .OrderBy(entry => entry.SpeciesName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Form)
                .ThenBy(entry => entry.Index)
                .ToArray();

            return CreateWorkflow(
                summary,
                entries,
                fields,
                sourceFileCount: 1 + speciesSourceCount + personalSourceCount,
                diagnostics) with
            {
                PersonalRecords = personalRecords,
            };
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
                sourceFileCount: 1 + speciesSourceCount + personalSourceCount,
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
                sourceFileCount: 1 + speciesSourceCount + personalSourceCount,
                diagnostics);
        }
        catch (UnauthorizedAccessException exception)
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
                sourceFileCount: 1 + speciesSourceCount + personalSourceCount,
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

    internal static string CreateEntryId(SwShSymbolBehaviorEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"behavior:{entry.Index}:{CreateEntrySourceIdentity(entry)}");
    }

    internal static bool TryParseEntryId(
        string? entryId,
        out int entryIndex,
        out string sourceIdentity,
        out bool isLegacy)
    {
        entryIndex = -1;
        sourceIdentity = string.Empty;
        isLegacy = false;

        if (int.TryParse(entryId, NumberStyles.None, CultureInfo.InvariantCulture, out entryIndex)
            && entryIndex >= 0)
        {
            isLegacy = true;
            return true;
        }

        var parts = entryId?.Split(':') ?? [];
        if (parts.Length != 3
            || !string.Equals(parts[0], "behavior", StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out entryIndex)
            || entryIndex < 0)
        {
            entryIndex = -1;
            return false;
        }

        sourceIdentity = parts[2];
        return sourceIdentity.Length == 64 && sourceIdentity.All(Uri.IsHexDigit);
    }

    internal static string CreateEntrySourceIdentity(SwShSymbolBehaviorEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendIdentityString(hash, "swsh-symbol-behavior-entry-v1");
        AppendIdentityInt32(hash, entry.Index);
        AppendIdentityInt32(hash, entry.Fields.Count);
        foreach (var field in entry.Fields.OrderBy(field => field.FieldIndex))
        {
            AppendIdentityString(hash, field.Field);
            AppendIdentityInt32(hash, field.FieldIndex);
            AppendIdentityInt32(hash, (int)field.FieldType);
            switch (field.Value)
            {
                case float single:
                    AppendIdentityInt32(hash, BitConverter.SingleToInt32Bits(single));
                    break;
                case int integer:
                    AppendIdentityInt32(hash, integer);
                    break;
                case byte byteValue:
                    hash.AppendData([byteValue]);
                    break;
                case ulong unsigned:
                    AppendIdentityUInt64(hash, unsigned);
                    break;
                case string text:
                    AppendIdentityString(hash, text);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Symbol behavior field '{field.Field}' has unsupported identity value type '{field.Value?.GetType().Name ?? "null"}'.");
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static SwShBehaviorEntryRecord CreateEntryRecord(
        SwShSymbolBehaviorEntry entry,
        IReadOnlyList<string> speciesNames,
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        SwShBehaviorProvenance provenance)
    {
        if (!float.IsFinite(entry.HitboxRadius) || !float.IsFinite(entry.GrassShakeRadius))
        {
            throw new InvalidDataException(
                $"Symbol behavior entry {entry.Index} contains a non-finite editable radius.");
        }

        var speciesName = GetIndexedName(entry.SpeciesId, speciesNames, "Species");
        var form = entry.Form == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"-{entry.Form}");
        var behaviorLabel = GetBehaviorLabel(entry.Behavior);
        var mode = string.IsNullOrWhiteSpace(entry.Behavior) ? "No Behavior" : behaviorLabel;

        return new SwShBehaviorEntryRecord(
            CreateEntryId(entry),
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
            provenance)
        {
            FormOptions = CreateFormOptions(personalRecords, entry.SpeciesId, entry.Form),
        };
    }

    internal static IReadOnlyList<SwShBehaviorField> CreateFields(
        IReadOnlyList<SwShBehaviorFieldOption> speciesOptions,
        IReadOnlyList<SwShBehaviorFieldOption> behaviorOptions,
        IReadOnlyList<SwShBehaviorFieldOption> modelPartOptions)
    {
        return SwShSymbolBehaviorArchive.FieldSpecs
            .Select(spec => CreateField(spec, speciesOptions, behaviorOptions, modelPartOptions))
            .ToArray();
    }

    internal static IReadOnlyList<SwShBehaviorFieldOption> CreateFormOptions(
        IReadOnlyList<SwShPersonalRecord> personalRecords,
        int speciesId,
        int currentForm)
    {
        var values = new SortedSet<int> { 0 };
        if ((uint)speciesId < (uint)personalRecords.Count)
        {
            var formCount = Math.Max(1, personalRecords[speciesId].FormCount);
            for (var form = 1; form < formCount && form <= MaximumFormValue; form++)
            {
                values.Add(form);
            }
        }

        if (currentForm >= MinimumFormValue && currentForm <= MaximumFormValue)
        {
            values.Add(currentForm);
        }

        return values
            .Select(form => new SwShBehaviorFieldOption(
                form.ToString(CultureInfo.InvariantCulture),
                SwShSpeciesFormLabels.FormatSpeciesFormOptionLabel(speciesId, form)))
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
        if (FieldMappings.TryGetValue(field, out var mapping))
        {
            return mapping.Label;
        }

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
        IReadOnlyList<SwShBehaviorFieldOption> behaviorOptions,
        IReadOnlyList<SwShBehaviorFieldOption> modelPartOptions)
    {
        var field = spec.Field;
        var isReadOnly = !IsEditableField(field) || spec.IsUnusedDefault;
        var options = field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField => speciesOptions,
            SwShSymbolBehaviorArchive.BehaviorField => behaviorOptions,
            SwShSymbolBehaviorArchive.ModelPartField => modelPartOptions,
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
        if (FieldMappings.TryGetValue(spec.Field, out var mapping))
        {
            return mapping.Group;
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
        if (FieldMappings.TryGetValue(field, out var mapping))
        {
            return mapping.Description;
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
            _ when spec.IsUnusedDefault => CreateUnusedMapping(field[5..]).Description,
            _ when field.StartsWith("field", StringComparison.Ordinal) => "Raw symbol AI tuning value. Disabled until its role is confirmed.",
            _ => string.Empty,
        };
    }

    private static double GetMinimumValue(SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.Field is SwShSymbolBehaviorArchive.HitboxRadiusField
            or SwShSymbolBehaviorArchive.GrassShakeRadiusField)
        {
            return 0;
        }

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

    private static BehaviorFieldMapping CreateUnusedMapping(string fieldNumber)
    {
        return new BehaviorFieldMapping(
            $"Unused Default {fieldNumber}",
            "Unused Defaults",
            "Base schema and vanilla data mark this as an unused default value. It stays read-only.");
    }

    private sealed record BehaviorFieldMapping(
        string Label,
        string Group,
        string Description);

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

    private static IReadOnlyList<SwShBehaviorFieldOption> CreateModelPartOptions(
        IReadOnlyList<SwShSymbolBehaviorEntry> entries)
    {
        return entries
            .Select(entry => entry.ModelPart)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(value => new SwShBehaviorFieldOption(value, value))
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
        var source = ResolveCommonTextSource(project, "monsname.dat");
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Species name table could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield monsname.dat"));
            return [];
        }
    }

    private static IReadOnlyList<SwShPersonalRecord> LoadPersonalRecords(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics,
        out int sourceFileCount)
    {
        var source = SwShPokemonWorkflowService.ResolvePersonalDataSource(project);
        if (source is null)
        {
            sourceFileCount = 0;
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Behavior personal data is not available; species and form changes are disabled until it can be loaded.",
                expected: SwShPersonalTable.PersonalDataRelativePath));
            return [];
        }

        sourceFileCount = 1;
        try
        {
            return SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records;
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Behavior personal data could not be decoded: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Sword/Shield personal data table"));
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"Behavior personal data could not be read: {exception.Message}",
                file: source.GraphEntry.RelativePath,
                expected: "Readable Sword/Shield personal data table"));
            return [];
        }
    }

    private static WorkflowFileSource? ResolveCommonTextSource(
        OpenedProject project,
        string fileName)
    {
        var language = SwShGameTextLanguage.Resolve(project.Paths);
        var preferred = ResolveWorkflowFile(project, SwShGameTextLanguage.CommonMessagePath(language, fileName));
        if (preferred is not null)
        {
            return preferred;
        }

        if (!string.Equals(language, SwShGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            var english = ResolveWorkflowFile(
                project,
                SwShGameTextLanguage.CommonMessagePath(SwShGameTextLanguage.English, fileName));
            if (english is not null)
            {
                return english;
            }
        }

        return project.FileGraph.Entries
            .Where(entry =>
                entry.RelativePath.StartsWith("romfs/bin/message/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith($"/common/{fileName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ResolveWorkflowFile(project, entry.RelativePath))
            .FirstOrDefault(source => source is not null);
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

    private static void AppendIdentityString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendIdentityInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendIdentityInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendIdentityUInt64(IncrementalHash hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
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
