// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Text;

public sealed class SvTextWorkflowService
{
    public const string MessageRootPath = "message/dat";
    public const string WorkflowLabel = "Text and Dialogue Map";
    public const string WorkflowDescription = "Text entries, dialogue references, and source provenance.";
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;

    private static readonly IReadOnlyList<SvTextEditableField> EditableFields =
    [
        new SvTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    private readonly SvWorkflowFileSource fileSource;

    internal SvTextWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Text,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvTextWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        if (summary.Availability == SvWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<SvTextEntryRecord>(),
                Array.Empty<SvDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var textSources = ResolveMessageSources(project, diagnostics);
        if (textSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map did not find any S/V message tables.",
                expected: $"{MessageRootPath}/{SvGameTextLanguage.English}/**/*.dat"));
            return CreateWorkflow(
                summary,
                Array.Empty<SvTextEntryRecord>(),
                Array.Empty<SvDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var entries = new List<SvTextEntryRecord>();
        var dialogueReferences = new List<SvDialogueReferenceRecord>();
        var parsedSourceFileCount = 0;

        foreach (var source in textSources)
        {
            try
            {
                var sourceFile = fileSource.Read(project, source.VirtualPath);
                var textFile = SwShGameTextFile.Parse(sourceFile.Bytes);
                var provenance = CreateProvenance(sourceFile);
                var context = GetLanguageRelativePath(sourceFile.VirtualPath, source.Language);
                parsedSourceFileCount++;

                for (var lineIndex = 0; lineIndex < textFile.Lines.Count; lineIndex++)
                {
                    var line = textFile.Lines[lineIndex];
                    var textId = entries.Count;
                    var canEdit = IsSafelyEditable(line.Text);
                    var label = CreateTextLabel(context, lineIndex);
                    var entry = new SvTextEntryRecord(
                        textId,
                        CreateTextKey(sourceFile.RelativePath, lineIndex),
                        label,
                        source.Language,
                        sourceFile.RelativePath,
                        lineIndex,
                        line.Text,
                        canEdit,
                        canEdit ? null : "Variable placeholders are read-only in this text editing slice.",
                        provenance);

                    entries.Add(entry);
                    dialogueReferences.Add(new SvDialogueReferenceRecord(
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
                    $"Message table 'romfs/{source.VirtualPath}' could not be decoded: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "S/V encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable S/V message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable S/V message table"));
            }
        }

        return CreateWorkflow(summary, entries, dialogueReferences, diagnostics, parsedSourceFileCount);
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

    internal static bool TryGetVirtualPathFromTextKey(string? textKey, out string virtualPath, out int lineIndex)
    {
        virtualPath = string.Empty;
        if (!TryParseTextKey(textKey, out var sourceFile, out lineIndex))
        {
            return false;
        }

        var normalizedSource = sourceFile.Replace('\\', '/').TrimStart('/');
        if (normalizedSource.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSource = normalizedSource["romfs/".Length..];
        }

        if (!normalizedSource.StartsWith($"{MessageRootPath}/", StringComparison.OrdinalIgnoreCase)
            || !normalizedSource.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        virtualPath = normalizedSource;
        return true;
    }

    internal static bool IsSafelyEditable(string value)
    {
        return !value.Contains("[VAR", StringComparison.Ordinal);
    }

    internal static string CreatePreview(string value)
    {
        const int maxPreviewLength = 72;
        var singleLine = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length <= maxPreviewLength ? singleLine : $"{singleLine[..maxPreviewLength]}...";
    }

    private SvTextWorkflow CreateWorkflow(
        SvWorkflowSummary summary,
        IReadOnlyList<SvTextEntryRecord> entries,
        IReadOnlyList<SvDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        int sourceFileCount)
    {
        return new SvTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            new SvTextWorkflowStats(
                entries.Count,
                dialogueReferences.Count,
                sourceFileCount),
            diagnostics);
    }

    private IReadOnlyList<TextFileSource> ResolveMessageSources(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preferredLanguage = SvGameTextLanguage.Resolve(project.Paths);
        var preferredSources = ResolveMessageSources(project, preferredLanguage);
        if (preferredSources.Count > 0 || string.Equals(preferredLanguage, SvGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            return preferredSources;
        }

        var englishSources = ResolveMessageSources(project, SvGameTextLanguage.English);
        if (englishSources.Count > 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{preferredLanguage}' message tables were not found; loaded English message tables instead.",
                expected: $"{MessageRootPath}/{preferredLanguage}/**/*.dat"));
        }

        return englishSources;
    }

    private IReadOnlyList<TextFileSource> ResolveMessageSources(OpenedProject project, string language)
    {
        return CreateMessageVirtualPathCandidates(project, language)
            .Where(path => fileSource.Exists(project, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new TextFileSource(path, language))
            .ToArray();
    }

    private IReadOnlyList<string> CreateMessageVirtualPathCandidates(OpenedProject project, string language)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packName in fileSource.ListBasePackNames(project))
        {
            var virtualPath = TryCreateMessageDatPathFromPackName(packName, language);
            if (!string.IsNullOrWhiteSpace(virtualPath))
            {
                candidates.Add(virtualPath);
            }
        }

        AddLooseMessageCandidates(candidates, project.Paths.BaseRomFsPath, language, hasRomFsPrefix: false);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            AddLooseMessageCandidates(candidates, project.Paths.OutputRootPath, language, hasRomFsPrefix: false);
            AddLooseMessageCandidates(candidates, Path.Combine(project.Paths.OutputRootPath, "romfs"), language, hasRomFsPrefix: false);
        }

        return candidates.ToArray();
    }

    private static void AddLooseMessageCandidates(
        ISet<string> candidates,
        string? romFsRootPath,
        string language,
        bool hasRomFsPrefix)
    {
        if (string.IsNullOrWhiteSpace(romFsRootPath))
        {
            return;
        }

        var messageRoot = Path.Combine(romFsRootPath, MessageRootPath.Replace('/', Path.DirectorySeparatorChar), language);
        if (!Directory.Exists(messageRoot))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(messageRoot, "*.dat", SearchOption.AllDirectories))
        {
            var relativeToRoot = Path.GetRelativePath(romFsRootPath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var normalized = hasRomFsPrefix && relativeToRoot.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
                ? relativeToRoot["romfs/".Length..]
                : relativeToRoot;

            if (normalized.StartsWith($"{MessageRootPath}/{language}/", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }
    }

    private static string? TryCreateMessageDatPathFromPackName(string packName, string language)
    {
        const string packSuffix = ".trpak";

        if (string.IsNullOrWhiteSpace(packName))
        {
            return null;
        }

        var normalized = packName.Replace('\\', '/').TrimStart('/');
        var prefix = $"arc/messagedat{language}";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(packSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tail = normalized[prefix.Length..^packSuffix.Length];
        var folder = string.Empty;
        var fileName = string.Empty;
        if (tail.StartsWith("common", StringComparison.OrdinalIgnoreCase))
        {
            folder = "common";
            fileName = tail["common".Length..];
        }
        else if (tail.StartsWith("script", StringComparison.OrdinalIgnoreCase))
        {
            folder = "script";
            fileName = tail["script".Length..];
        }

        fileName = fileName.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : $"{MessageRootPath}/{language}/{folder}/{fileName}.dat";
    }

    private static SvTextProvenance CreateProvenance(SvWorkflowFile file)
    {
        return new SvTextProvenance(file.RelativePath, file.SourceLayer, file.FileState);
    }

    private static string GetLanguageRelativePath(string virtualPath, string language)
    {
        var prefix = $"{MessageRootPath}/{language}/";
        return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? virtualPath[prefix.Length..]
            : virtualPath;
    }

    private static string CreateTextLabel(string context, int lineIndex)
    {
        return $"{Path.GetFileNameWithoutExtension(context)} #{lineIndex}";
    }

    private static string CreateDialogueId(string context, int lineIndex)
    {
        return $"{Path.ChangeExtension(context, null)?.Replace('\\', '/') ?? context}:{lineIndex}";
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? file = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: file,
            Domain: SvEditSessionSupport.TextDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextFileSource(string VirtualPath, string Language);
}
