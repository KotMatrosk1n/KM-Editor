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
    private const string LegacyOutputRecoveredCode = "KM-SWSH-BEHAVIOR-LEGACY-OUTPUT-RECOVERED";

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
        SwShSymbolBehaviorArchive.Field03,
        SwShSymbolBehaviorArchive.Field07,
        SwShSymbolBehaviorArchive.Field08,
        SwShSymbolBehaviorArchive.Field09,
        SwShSymbolBehaviorArchive.Field19,
        SwShSymbolBehaviorArchive.Field20,
        SwShSymbolBehaviorArchive.Field21,
        SwShSymbolBehaviorArchive.Field25,
        SwShSymbolBehaviorArchive.Field26,
        SwShSymbolBehaviorArchive.Field33,
        SwShSymbolBehaviorArchive.Field34,
        SwShSymbolBehaviorArchive.Field35,
        SwShSymbolBehaviorArchive.Field36,
        SwShSymbolBehaviorArchive.Field37,
        SwShSymbolBehaviorArchive.Field38,
        SwShSymbolBehaviorArchive.Field39,
        SwShSymbolBehaviorArchive.Field40,
        SwShSymbolBehaviorArchive.Field41,
        SwShSymbolBehaviorArchive.Field44,
    };

    private static readonly IReadOnlyList<SwShBehaviorFieldOption> SurfFollowOptions =
    [
        new("0", "Wait"),
        new("1", "Normal follow (1)"),
        new("2", "Normal follow (2)"),
        new("3", "Follow without appeal or roar"),
        new("4", "Follow without appeal"),
        new("5", "Run only"),
    ];

    private static readonly IReadOnlyDictionary<string, BehaviorFieldMapping> FieldMappings =
        new Dictionary<string, BehaviorFieldMapping>(StringComparer.Ordinal)
        {
            [SwShSymbolBehaviorArchive.Field00] = new(
                "Raw Field 00", "Raw Fields",
                "The game copies this float into the actor. Its gameplay effect is unconfirmed."),
            [SwShSymbolBehaviorArchive.Field01] = new(
                "Raw Field 01", "Raw Fields",
                "The game copies this float into the actor. Its gameplay effect is unconfirmed."),
            [SwShSymbolBehaviorArchive.ModelPartField] = new(
                "Effect Attachment Node", "Effects",
                "Model rig node used to position attached effects. If the node is absent, the game uses the attachment transform."),
            [SwShSymbolBehaviorArchive.Field03] = new(
                "Effect Scale", "Effects",
                "Uniform scale multiplier for attached effects. The encounter pop effect clamps this multiplier to 1.0 through 1.6."),
            [SwShSymbolBehaviorArchive.Hash1Field] = new(
                "Effect 1 ID", "Effects",
                "Hashed identifier for the first effect. Field AI can start and stop this effect."),
            [SwShSymbolBehaviorArchive.Hash2Field] = new(
                "Effect 2 ID", "Effects",
                "Hashed identifier for the second effect. The shipped field AI does not start or stop this effect."),
            [SwShSymbolBehaviorArchive.HitboxRadiusField] = new(
                "Base Encounter Radius", "Encounter Collider",
                "A positive value sets the native encounter collider radius. Zero uses the actor default. Field AI can change the resulting radius."),
            [SwShSymbolBehaviorArchive.Field07] = new(
                "Encounter Center X", "Encounter Collider",
                "X offset of the encounter collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.Field08] = new(
                "Encounter Center Y", "Encounter Collider",
                "Y offset of the encounter collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.Field09] = new(
                "Encounter Center Z", "Encounter Collider",
                "Z offset of the encounter collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.FormField] = new(
                "Form", "Identity",
                "Exact form in the configuration key. The game uses the first row matching species and form and has no fallback to form 0."),
            [SwShSymbolBehaviorArchive.Field11] = new(
                "Has Hearing", "Field AI Getters",
                "The game exposes this flag to Lua. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field12] = new(
                "Has Whistle Hearing", "Field AI Getters",
                "The game exposes this flag to Lua. The shipped field AI checks the player's whistle directly."),
            [SwShSymbolBehaviorArchive.SpeciesIdField] = new(
                "Species", "Identity",
                "Species in the configuration key. The first row matching species and form supplies the configuration for a pokemon chosen by the spawn."),
            [SwShSymbolBehaviorArchive.Field14] = new(
                "Motion 1", "Field AI Getters",
                "Integer returned by GetMotion1. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field15] = new(
                "Motion 2", "Field AI Getters",
                "Integer returned by GetMotion2. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field16] = new(
                "Raw Field 16", "Raw Fields",
                "The game copies this float into the actor. Its gameplay effect is unconfirmed."),
            [SwShSymbolBehaviorArchive.Field17] = new(
                "Raw Field 17", "Raw Fields",
                "The game copies this float into the actor. Its gameplay effect is unconfirmed."),
            [SwShSymbolBehaviorArchive.Field18] = new(
                "Move Type", "Field AI Getters",
                "Text returned by GetMoveType. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field19] = new(
                "Ground Height Offset", "Position Offsets",
                "Offset added to the actor's world Y position when ground behavior enables it."),
            [SwShSymbolBehaviorArchive.Field20] = new(
                "Sky Height Offset", "Position Offsets",
                "Offset added to spawn Y when sky behavior enables it."),
            [SwShSymbolBehaviorArchive.Field21] = new(
                "Water Height Offset", "Position Offsets",
                "Offset added to spawn Y when water behavior enables it. Water AI also uses its negative for the collider center offsets."),
            [SwShSymbolBehaviorArchive.InternalSpeciesNameField] = new(
                "Internal Species Name", "Identity",
                "Internal name stored in the archive. The game looks up rows by numeric species and form."),
            [SwShSymbolBehaviorArchive.Field23] = new(
                "Random Move Frame Maximum", "Field AI Getters",
                "Value returned as an integer by GetRandomMoveFrameMax. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field24] = new(
                "Random Move Frame Minimum", "Field AI Getters",
                "Value returned as an integer by GetRandomMoveFrameMin. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field25] = new(
                "Surf Follow Mode", "Follower",
                "Controls how the follower profile reacts while the player surfs."),
            [SwShSymbolBehaviorArchive.Field26] = new(
                "Follower Distance Adjustment", "Follower",
                "A signed amount added to the follower's follow distance and encounter radius."),
            [SwShSymbolBehaviorArchive.GrassShakeRadiusField] = new(
                "Follower Model Scale (%)", "Follower",
                "Uniform model scale when the follower profile applies it. 145 means 145% of base scale. Zero uses base scale."),
            [SwShSymbolBehaviorArchive.Field28] = new(
                "Reaction Parameter 3", "Field AI Getters",
                "Value returned by GetReactionParam3. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field29] = new(
                "Reaction State", "Field AI Getters",
                "Integer returned by GetReactionState. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field30] = new(
                "Reaction Time", "Field AI Getters",
                "Value returned by GetReactionTime. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.BehaviorField] = new(
                "Default AI Profile", "Behavior",
                "Case-sensitive field AI profile for this row. A spawn placement can override it."),
            [SwShSymbolBehaviorArchive.Field32] = new(
                "Special Leave", "Field AI Getters",
                "Text returned by GetSpecialLeave. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field33] = new(
                "Touch Radius", "Touch Collider",
                "A positive value sets the separate native touch collider radius. Zero uses the actor default. Field AI can change the resulting radius."),
            [SwShSymbolBehaviorArchive.Field34] = new(
                "Touch Center X", "Touch Collider",
                "X offset of the touch collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.Field35] = new(
                "Touch Center Y", "Touch Collider",
                "Y offset of the touch collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.Field36] = new(
                "Touch Center Z", "Touch Collider",
                "Z offset of the touch collider center. Water and sky profiles can replace the center."),
            [SwShSymbolBehaviorArchive.Field37] = new(
                "Turn Limit (Degrees)", "Behavior",
                "Maximum turn angle per steering update. A limit of 180 allows an immediate turn in the normal steering path."),
            [SwShSymbolBehaviorArchive.Field38] = new(
                "Random Action Chance (%)", "Behavior",
                "Whole percent chance when the active AI profile rolls for a random action."),
            [SwShSymbolBehaviorArchive.Field39] = new(
                "Random Action Wait Maximum", "Behavior",
                "Exclusive upper bound in AI update ticks. It must be greater than the minimum."),
            [SwShSymbolBehaviorArchive.Field40] = new(
                "Random Action Wait Minimum", "Behavior",
                "Inclusive lower bound in AI update ticks. It must be less than the maximum."),
            [SwShSymbolBehaviorArchive.Field41] = new(
                "Sight Half-Angle (Degrees)", "Behavior",
                "Horizontal sight extends this angle to either side of the actor's facing direction. Zero disables this sight check."),
            [SwShSymbolBehaviorArchive.Field42] = new(
                "Bicycle Watchout Distance", "Field AI Getters",
                "Value returned by GetWatchOutBicycleDistance. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field43] = new(
                "Bicycle Watchout Radius", "Field AI Getters",
                "Value returned by GetWatchOutBicycleRadius. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field44] = new(
                "Sight Distance", "Behavior",
                "Three-dimensional distance used by the active AI profile to check for the player, along with other reaction conditions."),
            [SwShSymbolBehaviorArchive.Field45] = new(
                "Watchout Radius", "Field AI Getters",
                "Value returned by GetWatchOutRadius. The shipped field AI does not read it."),
            [SwShSymbolBehaviorArchive.Field46] = new(
                "Watchout Y Flag", "Field AI Getters",
                "Flag returned by IsWatchOutY. The shipped field AI does not read it."),
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

    public SwShBehaviorWorkflow Load(OpenedProject project) => LoadCore(project, null);

    internal SwShBehaviorWorkflow LoadForEditing(OpenedProject project, IReadOnlySet<int> indices) => LoadCore(project, indices);

    private SwShBehaviorWorkflow LoadCore(OpenedProject project, IReadOnlySet<int>? indices)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project) with { IsPartial = indices is not null };
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
            var archive = ReadBehaviorArchive(project, behaviorSource);
            if (archive.RecoveredLegacyRowCount > 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Recovered {archive.RecoveredLegacyRowCount} rows from an older Behavior output. Apply a Behavior edit to write the repaired archive.",
                    file: behaviorSource.GraphEntry.RelativePath) with { Code = LegacyOutputRecoveredCode });
            }

            var provenance = CreateProvenance(behaviorSource.GraphEntry);
            var behaviorOptions = CreateBehaviorOptions(archive.Entries);
            var modelPartOptions = CreateModelPartOptions(archive.Entries);
            var speciesOptions = CreateSpeciesOptions(speciesNames, presentSpeciesIds);
            var fields = CreateFields(speciesOptions, behaviorOptions, modelPartOptions);
            var entries = archive.Entries
                .Where(entry => indices is null || indices.Contains(entry.Index))
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
                RequiresLegacyRecovery = archive.RecoveredLegacyRowCount > 0,
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

    internal static SwShSymbolBehaviorArchive ReadBehaviorArchive(OpenedProject project, WorkflowFileSource source)
    {
        var data = File.ReadAllBytes(source.AbsolutePath);
        try
        {
            return SwShSymbolBehaviorArchive.Parse(data);
        }
        catch (InvalidDataException exception) when (source.GraphEntry.LayeredFile is not null
            && source.GraphEntry.BaseFile is not null)
        {
            var basePath = CombineGraphPath(project.Paths.BaseRomFsPath, BehaviorDataPath["romfs/".Length..]);
            if (basePath is null)
            {
                throw;
            }

            try
            {
                return SwShSymbolBehaviorArchive.RecoverLegacyOutput(data, File.ReadAllBytes(basePath));
            }
            catch (Exception recoveryException) when (recoveryException is InvalidDataException
                or OverflowException or DecoderFallbackException)
            {
                throw new InvalidDataException(
                    $"The output archive could not be read ({exception.Message}) or recovered with the selected base RomFS ({recoveryException.Message}).",
                    recoveryException);
            }
        }
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
        var fields = entry.GetLegacyIdentityFields().OrderBy(field => field.FieldIndex).ToArray();
        AppendIdentityInt32(hash, fields.Length);
        foreach (var field in fields)
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
        var values = new SortedSet<int>(SwShSpeciesAvailability.PresentForms(personalRecords, speciesId, MaximumFormValue));

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
        return FieldMappings.TryGetValue(field, out var mapping) ? mapping.Label : field;
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
        var mapping = FieldMappings[field];
        var options = field switch
        {
            SwShSymbolBehaviorArchive.SpeciesIdField => speciesOptions,
            SwShSymbolBehaviorArchive.BehaviorField => behaviorOptions,
            SwShSymbolBehaviorArchive.ModelPartField => modelPartOptions,
            SwShSymbolBehaviorArchive.Field25 => SurfFollowOptions,
            _ => Array.Empty<SwShBehaviorFieldOption>(),
        };
        var valueKind = spec.FieldType switch
        {
            _ when field is SwShSymbolBehaviorArchive.Field25 or SwShSymbolBehaviorArchive.Field38
                or SwShSymbolBehaviorArchive.Field39 or SwShSymbolBehaviorArchive.Field40 => "integer",
            SwShSymbolBehaviorFieldType.Single => "number",
            SwShSymbolBehaviorFieldType.Int32 => "integer",
            SwShSymbolBehaviorFieldType.Byte => "integer",
            SwShSymbolBehaviorFieldType.UInt64 => "hash",
            SwShSymbolBehaviorFieldType.String => "string",
            _ => "string",
        };

        return new SwShBehaviorField(
            field,
            mapping.Label,
            mapping.Group,
            valueKind,
            GetMinimumValue(spec),
            GetMaximumValue(spec),
            !IsEditableField(field),
            mapping.Description,
            options);
    }

    private static double GetMinimumValue(SwShSymbolBehaviorFieldSpec spec)
    {
        if (spec.Field is SwShSymbolBehaviorArchive.HitboxRadiusField
            or SwShSymbolBehaviorArchive.GrassShakeRadiusField
            or SwShSymbolBehaviorArchive.FormField
            or SwShSymbolBehaviorArchive.SpeciesIdField
            or SwShSymbolBehaviorArchive.Field03
            or SwShSymbolBehaviorArchive.Field25
            or SwShSymbolBehaviorArchive.Field33
            or SwShSymbolBehaviorArchive.Field37
            or SwShSymbolBehaviorArchive.Field38
            or SwShSymbolBehaviorArchive.Field39
            or SwShSymbolBehaviorArchive.Field40
            or SwShSymbolBehaviorArchive.Field41
            or SwShSymbolBehaviorArchive.Field44)
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
        return spec.Field switch
        {
            SwShSymbolBehaviorArchive.FormField => MaximumFormValue,
            SwShSymbolBehaviorArchive.SpeciesIdField => MaximumSpeciesIdValue,
            SwShSymbolBehaviorArchive.Field25 => 5,
            SwShSymbolBehaviorArchive.Field37 or SwShSymbolBehaviorArchive.Field41 => 180,
            SwShSymbolBehaviorArchive.Field38 => 100,
            _ => spec.FieldType switch
            {
                SwShSymbolBehaviorFieldType.Single => MaximumNumberValue,
                SwShSymbolBehaviorFieldType.Int32 => int.MaxValue,
                SwShSymbolBehaviorFieldType.Byte => MaximumByteValue,
                SwShSymbolBehaviorFieldType.UInt64 => ulong.MaxValue,
                _ => MaximumStringLength,
            },
        };
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
