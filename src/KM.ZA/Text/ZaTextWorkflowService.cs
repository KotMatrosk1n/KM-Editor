// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Text;

public sealed class ZaTextWorkflowService
{
    public const string MessageRootPath = ZaMessagePathResolver.MessageRootPath;
    public const string WorkflowLabel = "Text and Dialogue Map";
    public const string WorkflowDescription = "Text entries, dialogue references, and source provenance.";
    public const string TextValueField = "value";
    public const int MaximumTextLength = 4096;
    public const int DefaultQueryLimit = 500;
    public const int MaximumQueryLimit = 1000;

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    private static readonly IReadOnlyList<ZaTextEditableField> EditableFields =
    [
        new ZaTextEditableField(TextValueField, "Text value", "multilineText", 0, MaximumTextLength),
    ];

    private readonly ZaWorkflowFileSource fileSource;

    internal ZaTextWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Text,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaTextWorkflow Load(OpenedProject project, ZaTextWorkflowQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var summary = CreateSummary(project);
        var diagnostics = new List<ValidationDiagnostic>(summary.Diagnostics);
        var normalizedQuery = NormalizeQuery(query);
        if (summary.Availability == ZaWorkflowAvailability.Disabled)
        {
            return CreateWorkflow(
                summary,
                Array.Empty<ZaTextEntryRecord>(),
                Array.Empty<ZaDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var textSources = ResolveMessageSources(project, diagnostics);
        if (textSources.Count == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Warning,
                "Text and Dialogue Map did not find any Pokemon Legends Z-A message tables.",
                expected: $"{MessageRootPath}/{ZaGameTextLanguage.English}/**/*.dat"));
            return CreateWorkflow(
                summary,
                Array.Empty<ZaTextEntryRecord>(),
                Array.Empty<ZaDialogueReferenceRecord>(),
                diagnostics,
                sourceFileCount: 0);
        }

        var entries = new List<ZaTextEntryRecord>();
        var dialogueReferences = new List<ZaDialogueReferenceRecord>();
        var parsedSourceFileCount = 0;
        var scannedTextEntryCount = 0;
        var matchedTextEntryCount = 0;

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
                    var textId = scannedTextEntryCount++;
                    var label = CreateTextLabel(context, lineIndex);
                    if (normalizedQuery is not null)
                    {
                        if (!MatchesQuery(
                            normalizedQuery.SearchText,
                            textId,
                            source.Language,
                            sourceFile.RelativePath,
                            context,
                            label,
                            lineIndex,
                            line.Text))
                        {
                            continue;
                        }

                        if (matchedTextEntryCount++ < normalizedQuery.Offset)
                        {
                            continue;
                        }

                        if (entries.Count >= normalizedQuery.Limit)
                        {
                            break;
                        }
                    }

                    AddTextRecord(
                        entries,
                        dialogueReferences,
                        textId,
                        source,
                        sourceFile,
                        context,
                        lineIndex,
                        line.Text,
                        label,
                        provenance);

                    if (normalizedQuery is not null && entries.Count >= normalizedQuery.Limit)
                    {
                        break;
                    }
                }

                if (normalizedQuery is not null && entries.Count >= normalizedQuery.Limit)
                {
                    break;
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Message table 'romfs/{source.VirtualPath}' could not be decoded: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Pokemon Legends Z-A encrypted text table"));
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Message table 'romfs/{source.VirtualPath}' could not be read: {exception.Message}",
                    file: $"romfs/{source.VirtualPath}",
                    expected: "Readable Pokemon Legends Z-A message table"));
            }
        }

        return CreateWorkflow(summary, entries, dialogueReferences, diagnostics, parsedSourceFileCount);
    }

    private static ZaTextWorkflowQuery? NormalizeQuery(ZaTextWorkflowQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        var searchText = string.IsNullOrWhiteSpace(query.SearchText)
            ? null
            : query.SearchText.Trim();
        return new ZaTextWorkflowQuery(
            searchText,
            Math.Max(0, query.Offset),
            Math.Clamp(query.Limit <= 0 ? DefaultQueryLimit : query.Limit, 1, MaximumQueryLimit));
    }

    private static bool MatchesQuery(
        string? searchText,
        int textId,
        string language,
        string sourceFile,
        string context,
        string label,
        int lineIndex,
        string value)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return sourceFile.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || textId.ToString(CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || language.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || context.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || label.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || value.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || lineIndex.ToString(CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTextRecord(
        ICollection<ZaTextEntryRecord> entries,
        ICollection<ZaDialogueReferenceRecord> dialogueReferences,
        int textId,
        TextFileSource source,
        ZaWorkflowFile sourceFile,
        string context,
        int lineIndex,
        string value,
        string label,
        ZaTextProvenance provenance)
    {
        var entry = new ZaTextEntryRecord(
            textId,
            CreateTextKey(sourceFile.RelativePath, lineIndex),
            label,
            source.Language,
            sourceFile.RelativePath,
            lineIndex,
            value,
            CanEdit: true,
            EditBlockedReason: null,
            provenance);

        entries.Add(entry);
        dialogueReferences.Add(new ZaDialogueReferenceRecord(
            CreateDialogueId(context, lineIndex),
            label,
            textId,
            context,
            CreatePreview(value),
            provenance));
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

    internal static string CreatePreview(string value)
    {
        const int maxPreviewLength = 72;
        var singleLine = value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        return singleLine.Length <= maxPreviewLength ? singleLine : $"{singleLine[..maxPreviewLength]}...";
    }

    private ZaTextWorkflow CreateWorkflow(
        ZaWorkflowSummary summary,
        IReadOnlyList<ZaTextEntryRecord> entries,
        IReadOnlyList<ZaDialogueReferenceRecord> dialogueReferences,
        IReadOnlyList<ValidationDiagnostic> diagnostics,
        int sourceFileCount)
    {
        return new ZaTextWorkflow(
            summary,
            entries,
            dialogueReferences,
            EditableFields,
            new ZaTextWorkflowStats(
                entries.Count,
                dialogueReferences.Count,
                sourceFileCount),
            diagnostics);
    }

    private IReadOnlyList<TextFileSource> ResolveMessageSources(
        OpenedProject project,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var preferredLanguage = ZaGameTextLanguage.Resolve(project.Paths);
        var preferredSources = ResolveMessageSources(project, preferredLanguage);
        if (preferredSources.Count > 0 || string.Equals(preferredLanguage, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase))
        {
            return preferredSources;
        }

        var englishSources = ResolveMessageSources(project, ZaGameTextLanguage.English);
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
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new TextFileSource(path, language))
            .ToArray();
    }

    private IReadOnlyList<string> CreateMessageVirtualPathCandidates(OpenedProject project, string language)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packName in fileSource.ListBasePackNames(project))
        {
            var virtualPath = ZaMessagePathResolver.TryCreateMessageDatPathFromPackName(packName, language);
            if (!string.IsNullOrWhiteSpace(virtualPath)
                && virtualPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
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

        foreach (var filePath in Directory.EnumerateFiles(messageRoot, "*.dat", RecursiveEnumeration))
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

    private static ZaTextProvenance CreateProvenance(ZaWorkflowFile file)
    {
        return new ZaTextProvenance(file.RelativePath, file.SourceLayer, file.FileState);
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
            Domain: ZaEditSessionSupport.TextDomain,
            Field: field,
            Expected: expected);
    }

    private sealed record TextFileSource(string VirtualPath, string Language);
}


