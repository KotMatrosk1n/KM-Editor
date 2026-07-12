// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Workflows;
using System.Globalization;

namespace KM.SwSh.Text;

public sealed class SwShTextWorkflowService
{
    public const string MessageRootPath = "romfs/bin/message";
    public const string PreferredLanguage = "English";
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;

    private static readonly IReadOnlyList<SwShTextEditableField> EditableFields =
    [
        new SwShTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    public SwShWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Health.CanOpenReadOnlyWorkflows)
        {
            return CreateSummary(
                SwShWorkflowAvailability.Disabled,
                CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    "Text and Dialogue Map requires valid base RomFS and base ExeFS paths before it can load.",
                    expected: "Readable project paths"));
        }

        return CreateSummary(project.Health.CanOpenEditableWorkflows
            ? SwShWorkflowAvailability.Available
            : SwShWorkflowAvailability.ReadOnly);
    }

    public SwShTextWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);

        if (summary.Availability == SwShWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SwShTextEntryRecord>(),
                Array.Empty<SwShDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var textSources = ResolveMessageSources(project, diagnostics);
        if (textSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map did not find any Sword/Shield message tables.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/**/*.dat"));
            return CreateWorkflow(
                summary,
                Array.Empty<SwShTextEntryRecord>(),
                Array.Empty<SwShDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var entries = new List<SwShTextEntryRecord>();
        var dialogueReferences = new List<SwShDialogueReferenceRecord>();
        var parsedSourceFileCount = 0;

        foreach (var source in textSources)
        {
            try
            {
                var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(source.AbsolutePath));
                var provenance = CreateProvenance(source.Entry);
                var context = GetLanguageRelativePath(source.Entry.RelativePath, source.Language);
                parsedSourceFileCount++;

                for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
                {
                    var line = textFile.Lines[lineIndex];
                    var textId = entries.Count;
                    var label = CreateTextLabel(context, lineIndex);
                    var entry = new SwShTextEntryRecord(
                        textId,
                        CreateTextKey(source.Entry.RelativePath, lineIndex),
                        label,
                        source.Language,
                        source.Entry.RelativePath,
                        lineIndex,
                        line.Text,
                        CanEdit: true,
                        EditBlockedReason: null,
                        provenance);

                    entries.Add(entry);
                    dialogueReferences.Add(new SwShDialogueReferenceRecord(
                        CreateDialogueId(context, lineIndex),
                        label,
                        textId,
                        context,
                        CreatePreview(line.Text),
                        provenance));
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message table '{source.Entry.RelativePath}' could not be decoded: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Sword/Shield encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table '{source.Entry.RelativePath}' could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable Sword/Shield message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table '{source.Entry.RelativePath}' could not be read: {exception.Message}",
                    file: source.Entry.RelativePath,
                    expected: "Readable Sword/Shield message table"));
            }
        }

        return CreateWorkflow(summary, entries, dialogueReferences, diagnostics, parsedSourceFileCount);
    }

    internal static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, entry);
        return sourcePath is null || !File.Exists(sourcePath)
            ? null
            : new WorkflowFileSource(entry, sourcePath, GetLanguage(entry.RelativePath) ?? "unknown");
    }

    internal static string? ResolveOutputPath(ProjectPaths paths, string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath) || Path.IsPathRooted(targetRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, normalizedRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);

        return PathContainment.IsWithinRoot(pathFromOutputRoot)
            ? targetPath
            : null;
    }

    internal static string CreateTextKey(string sourceFile, int lineIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceFile}#{lineIndex}");
    }

    internal static bool TryParseTextKey(string? textKey, out string sourceFile, out int lineIndex)
    {
        sourceFile = string.Empty;
        lineIndex = -1;

        if (string.IsNullOrWhiteSpace(textKey))
        {
            return false;
        }

        var separatorIndex = textKey.LastIndexOf('#');
        if (separatorIndex <= 0 || separatorIndex == textKey.Length - 1)
        {
            return false;
        }

        sourceFile = textKey[..separatorIndex];
        return int.TryParse(
            textKey[(separatorIndex + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out lineIndex)
            && lineIndex >= 0;
    }

    private static SwShTextWorkflow CreateWorkflow(
        SwShWorkflowSummary summary,
        IReadOnlyList<SwShTextEntryRecord> entries,
        IReadOnlyList<SwShDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        int sourceFileCount)
    {
        return new SwShTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            new SwShTextWorkflowStats(
                entries.Count,
                dialogueReferences.Count,
                sourceFileCount),
            diagnostics);
    }

    private static IReadOnlyList<WorkflowFileSource> ResolveMessageSources(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var messageEntries = project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase)
                && entry.RelativePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (messageEntries.Length == 0)
        {
            return Array.Empty<WorkflowFileSource>();
        }

        var preferredLanguage = SwShGameTextLanguage.Resolve(project.Paths);
        var language = messageEntries.Any(entry =>
            string.Equals(GetLanguage(entry.RelativePath), preferredLanguage, StringComparison.OrdinalIgnoreCase))
            ? preferredLanguage
            : messageEntries.Any(entry =>
                string.Equals(GetLanguage(entry.RelativePath), PreferredLanguage, StringComparison.OrdinalIgnoreCase))
                ? PreferredLanguage
                : messageEntries
                .Select(entry => GetLanguage(entry.RelativePath))
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(language))
        {
            return Array.Empty<WorkflowFileSource>();
        }

        if (!string.Equals(language, PreferredLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(preferredLanguage, PreferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"English message tables were not found; loaded '{language}' message tables instead.",
                expected: $"{MessageRootPath}/{PreferredLanguage}/**/*.dat"));
        }

        return messageEntries
            .Where(entry => string.Equals(GetLanguage(entry.RelativePath), language, StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var sourcePath = ResolveSourcePath(project.Paths, entry);
                return sourcePath is null || !File.Exists(sourcePath)
                    ? null
                    : new WorkflowFileSource(entry, sourcePath, language);
            })
            .Where(source => source is not null)
            .Select(source => source!)
            .ToArray();
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

    private static SwShTextProvenance CreateProvenance(ProjectFileGraphEntry entry)
    {
        var sourceLayer = entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;

        return new SwShTextProvenance(entry.RelativePath, sourceLayer, entry.State);
    }

    private static string? GetLanguage(string relativePath)
    {
        if (!relativePath.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var languageStart = MessageRootPath.Length + 1;
        var nextSeparator = relativePath.IndexOf('/', languageStart);

        return nextSeparator < 0
            ? null
            : relativePath[languageStart..nextSeparator];
    }

    private static string GetLanguageRelativePath(string relativePath, string language)
    {
        var prefix = $"{MessageRootPath}/{language}/";
        return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? relativePath[prefix.Length..]
            : relativePath;
    }

    private static string CreateTextLabel(string context, int lineIndex)
    {
        return $"{Path.GetFileNameWithoutExtension(context)} #{lineIndex}";
    }

    private static string CreateDialogueId(string context, int lineIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.ChangeExtension(context, null)?.Replace('\\', '/')}:{lineIndex}");
    }

    private static string CreatePreview(string value)
    {
        const int maxPreviewLength = 96;
        return value.Length <= maxPreviewLength ? value : $"{value[..maxPreviewLength]}...";
    }

    private static SwShWorkflowSummary CreateSummary(
        SwShWorkflowAvailability availability,
        params ValidationDiagnostic[] diagnostics)
    {
        return new SwShWorkflowSummary(
            SwShWorkflowIds.Text,
            "Text and Dialogue Map",
            "Text entries, dialogue references, and source provenance.",
            availability,
            diagnostics);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? expected = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: "workflow.text",
            Expected: expected);
    }

    internal sealed record WorkflowFileSource(
        ProjectFileGraphEntry Entry,
        string AbsolutePath,
        string Language);
}
