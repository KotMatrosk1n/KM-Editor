// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KM.Core.GameDump;

public static class DumpYamlWriter
{
    private const int TargetFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static GameDumpCategory WithYaml(GameDumpCategory category) => category with
    {
        Formats = [GameDumpFormat.Yaml, .. category.Formats],
        DefaultFormat = GameDumpFormat.Yaml,
    };

    public static GameDumpWriteCategoryResult WriteCategory(IEditableDumpProvider provider, string folder,
        GameDumpSelection selection, IReadOnlyList<string?> languages)
    {
        var diagnostics = new List<KM.Core.Diagnostics.ValidationDiagnostic>();
        var files = new List<GameDumpWrittenFile>();
        var languagesWritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = new List<GameDumpLanguageExportMetadata>();
        var rowCount = 0;
        foreach (var language in languages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var document = provider.Load(selection.CategoryId, language);
            diagnostics.AddRange(document.Diagnostics);
            if (diagnostics.Any(d => d.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error))
                return new([], diagnostics, rowCount, new(metadata));
            var requested = document.RequestedLanguage ?? document.Language;
            var fallback = !string.Equals(requested, document.Language, StringComparison.OrdinalIgnoreCase);
            metadata.Add(new(requested, document.Language, fallback,
                fallback ? $"{requested} was unavailable; {document.Language} was exported." : null,
                document.SourceFileCount, document.Records.Count));
            if (!languagesWritten.Add(document.Language)) continue;
            files.AddRange(Write(folder, document));
            rowCount += document.Records.Count;
        }
        return new(files, diagnostics, rowCount, selection.CategoryId == "text" ? new(metadata) : null);
    }

    public static IReadOnlyList<GameDumpWrittenFile> Write(string folder, EditableDumpDocument document)
    {
        foreach (var component in new[] { document.Category, document.Language })
            if (!System.Text.RegularExpressions.Regex.IsMatch(component, "^[A-Za-z0-9_-]+$"))
                throw new InvalidOperationException("The dump category or language has an invalid file name component.");
        var files = new List<GameDumpWrittenFile>();
        var part = 1;
        var text = new StringBuilder();
        var size = 0;
        foreach (var record in document.Records)
        {
            var block = new StringBuilder();
            WriteRecord(block, record, 1);
            var bytes = Encoding.UTF8.GetByteCount(block.ToString());
            if (size > 0 && size + bytes > TargetFileBytes) Flush();
            text.Append(block);
            size += bytes;
        }
        if (size > 0 || files.Count == 0) Flush();
        return files;

        void Flush()
        {
            var file = $"{document.Category}-{document.Language}-{part:D4}.yaml";
            var relative = Path.Combine(document.Category, file);
            var destination = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var writer = new StreamWriter(destination, false, new UTF8Encoding(false));
            writer.NewLine = "\n";
            writer.WriteLine("# Edit values, then use Dump Importer to review the changes.");
            writer.WriteLine("# Keep record IDs and source fingerprints. Omitted fields stay unchanged.");
            writer.WriteLine("# Fields under read_only are reference information and cannot be changed.");
            writer.WriteLine("format: km-editor-dump");
            writer.WriteLine("version: 1");
            writer.WriteLine($"game: {Quote(document.Game)}");
            writer.WriteLine($"category: {Quote(document.Category)}");
            writer.WriteLine($"language: {Quote(document.Language)}");
            writer.WriteLine($"document_id: {Quote($"{document.Game}/{document.Category}/{document.Language}/{part}")}");
            writer.WriteLine(size == 0 ? "records: []" : "records:");
            writer.Write(text);
            writer.Flush();
            files.Add(new(document.Category, relative, new FileInfo(destination).Length));
            text.Clear();
            size = 0;
            part++;
        }
    }

    private static void WriteRecord(StringBuilder output, DumpRecord record, int depth)
    {
        var indent = new string(' ', depth * 2);
        output.Append(indent).Append("- id: ").AppendLine(Quote(record.Id));
        output.Append(indent).Append("  name: ").AppendLine(Quote(record.Name));
        output.Append(indent).Append("  source: ").AppendLine(Quote(record.Fingerprint));
        foreach (var editable in new[] { true, false })
        {
            var fields = record.Fields.Where(field => field.Editable == editable).ToArray();
            if (fields.Length == 0) continue;
            output.Append(indent).Append("  ").Append(editable ? "values" : "read_only").AppendLine(":");
            foreach (var field in fields)
            {
                output.Append(indent).Append("    ").Append(System.Text.RegularExpressions.Regex.IsMatch(field.Key, "^[a-z][a-z0-9_.]*$")
                    ? field.Key : Quote(field.Key)).Append(": ");
                var value = field.DisplayValue;
                if (field.Kind == "boolean" && field.Value is "0" or "1")
                    output.AppendLine(field.Value == "1" ? "true" : "false");
                else if (field.Kind != "text" && value == field.Value && double.TryParse(value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                    output.AppendLine(value);
                else if (field.Kind == "text" && value.Contains('\n') && !value.Contains('\r')
                    && !value.EndsWith("\n\n", StringComparison.Ordinal)
                    && value.All(c => !char.IsControl(c) || c is '\n' or '\t')
                    && !value.Contains('\u0085') && !value.Contains('\u2028') && !value.Contains('\u2029'))
                {
                    var lines = value.Split('\n');
                    var trailingNewline = value.EndsWith('\n');
                    output.AppendLine(trailingNewline ? "|2" : "|2-");
                    foreach (var line in lines.Take(lines.Length - (trailingNewline ? 1 : 0)))
                        output.Append(indent).Append("      ").AppendLine(line);
                }
                else
                    output.AppendLine(Quote(value));
            }
        }
        foreach (var child in record.Children)
        {
            output.Append(indent).Append("  ").Append(child.Key).AppendLine(child.Value.Count == 0 ? ": []" : ":");
            foreach (var nested in child.Value) WriteRecord(output, nested, depth + 2);
        }
        output.AppendLine();
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value, StringOptions);
}
