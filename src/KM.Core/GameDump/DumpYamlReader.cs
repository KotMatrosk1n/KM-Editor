// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace KM.Core.GameDump;

public sealed class DumpYamlNode(DumpLocation location)
{
    public DumpLocation Location { get; } = location;
    public string? Scalar { get; init; }
    public bool IsNull { get; init; }
    public Dictionary<string, DumpYamlNode>? Mapping { get; init; }
    public List<DumpYamlNode>? Sequence { get; init; }
}

public sealed class DumpYamlException(DumpIssue issue) : Exception(issue.Message)
{
    public DumpIssue Issue { get; } = issue;
}

public static class DumpYamlReader
{
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    private const int MaximumNodes = 2_000_000;
    private const int MaximumDepth = 40;

    public static DumpYamlNode ReadFile(string path)
    {
        var name = Path.GetFileName(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes)
            throw Error(new(name, 1, 1, "document"), "File is too large. Export a smaller category selection.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return Read(reader, name);
    }

    public static DumpYamlNode Read(TextReader reader, string file)
    {
        var parser = new Parser(reader);
        var count = 0;
        var field = "document";
        try
        {
            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();
            var result = ReadNode(parser, file, field, 0, ref count, ref field);
            parser.Consume<DocumentEnd>();
            if (!parser.TryConsume<StreamEnd>(out _))
                throw Error(Location(file, parser.Current!, "document"), "Use one YAML document per file.");
            return result;
        }
        catch (YamlException exception)
        {
            throw Error(new(file, checked((int)exception.Start.Line), checked((int)exception.Start.Column), field),
                "Invalid YAML syntax. Check indentation, quotes and list markers.");
        }
    }

    private static DumpYamlNode ReadNode(IParser parser, string file, string path, int depth, ref int count, ref string lastField)
    {
        lastField = path;
        var current = parser.Current ?? throw Error(new(file, 1, 1, path), "A value is missing.");
        var location = Location(file, current, path);
        if (++count > MaximumNodes || depth > MaximumDepth)
            throw Error(location, "The document has too many entries or too much nesting.");
        if (current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
            throw Error(location, "YAML anchors and explicit tags are not supported. Use ordinary values.");
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new(location)
            {
                Scalar = scalar.Value,
                IsNull = scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL",
            };
        }
        if (parser.TryConsume<MappingStart>(out _))
        {
            var map = new Dictionary<string, DumpYamlNode>(StringComparer.Ordinal);
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (!parser.TryConsume<Scalar>(out var key))
                    throw Error(Location(file, parser.Current!, path), "A field name must be plain text.");
                var childPath = path == "document" ? key.Value : $"{path}.{key.Value}";
                lastField = childPath;
                if (!key.Anchor.IsEmpty || !key.Tag.IsEmpty || key.Value == "<<")
                    throw Error(Location(file, key, childPath), "Aliases, tags and merged mappings are not supported.");
                if (map.ContainsKey(key.Value))
                    throw Error(Location(file, key, childPath), "This field is listed more than once.", "KM-DUMP-YAML-DUPLICATE");
                map.Add(key.Value, ReadNode(parser, file, childPath, depth + 1, ref count, ref lastField));
            }
            return new(location) { Mapping = map };
        }
        if (parser.TryConsume<SequenceStart>(out _))
        {
            var list = new List<DumpYamlNode>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                list.Add(ReadNode(parser, file, $"{path}[{list.Count + 1}]", depth + 1, ref count, ref lastField));
            return new(location) { Sequence = list };
        }
        throw Error(location, "Use a value, a list or named fields. YAML aliases are not supported.");
    }

    private static DumpLocation Location(string file, ParsingEvent entry, string field) =>
        new(file, checked((int)entry.Start.Line), checked((int)entry.Start.Column), field);

    internal static DumpYamlException Error(DumpLocation location, string message, string code = "KM-DUMP-YAML-SYNTAX") =>
        new(new(code, message, location));
}
