// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;

namespace KM.Tools.ModMerging;

internal static class MergePatchText
{
    internal static (string Build, byte[] Bytes) Read(byte[] bytes)
    {
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Executable patch text exceeds the source limit.");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var writes = new SortedDictionary<uint, byte>();
        string? build = null;
        var enabled = false; var reverse = false; long shift = 0; var total = 0;
        foreach (var raw in text.Split('\n'))
        {
            var quoted = false; var escaped = false; var end = raw.Length;
            for (var index = 0; index < raw.Length; index++)
            {
                var character = raw[index];
                if (escaped) { escaped = false; continue; }
                if (quoted && character == '\\') { escaped = true; continue; }
                if (character == '"') quoted = !quoted;
                if (!quoted && character == '/') { end = index; break; }
            }
            var line = raw[..end].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var tag = tokens[0].ToLowerInvariant();
            if (tag == "@stop") break;
            if (tag.StartsWith("@nsobid-", StringComparison.Ordinal)) { SetBuild(tokens[0][8..]); continue; }
            if (tag is "@title" or "@program" or "@url" or "@nsobid")
            {
                if (tag == "@nsobid") { if (tokens.Length != 2) throw new InvalidDataException("Patch build identifier is missing."); SetBuild(tokens[1]); }
                continue;
            }
            if (tag is "@enabled" or "@disabled")
            {
                if (tokens.Length > 1 && (tokens.Length != 2 || tokens[1] != "bin")) throw new InvalidDataException("Only binary patch sections can be combined.");
                enabled = tag == "@enabled"; continue;
            }
            if (tag == "@flag")
            {
                if (tokens.Length < 2) throw new InvalidDataException("Patch flag is missing.");
                switch (tokens[1].ToLowerInvariant())
                {
                    case "offset_shift" when tokens.Length == 3:
                        var sign = tokens[2].StartsWith('-') ? -1 : 1;
                        var number = tokens[2].TrimStart('-', '+');
                        shift = sign * (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? long.Parse(number[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                            : long.Parse(number, CultureInfo.InvariantCulture));
                        if (shift is < -uint.MaxValue or > uint.MaxValue) throw new InvalidDataException("Patch shift is out of range.");
                        break;
                    case "nsobid" when tokens.Length == 3: SetBuild(tokens[2]); enabled = false; break;
                    case "le" when tokens.Length == 2: reverse = false; break;
                    case "be" when tokens.Length == 2: reverse = true; break;
                    case "debug_info" or "print_values" when tokens.Length == 2: break;
                    default: throw new InvalidDataException("Unsupported executable patch flag.");
                }
                continue;
            }
            if (line.StartsWith('@') || line.StartsWith('[')) throw new InvalidDataException("Unsupported executable patch directive.");
            if (!enabled) continue;
            if (build is null || tokens.Length < 2 || !uint.TryParse(tokens[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rawAddress))
                throw new InvalidDataException("Invalid patch address or missing build identifier.");
            var value = line[tokens[0].Length..].Trim();
            byte[] data;
            if (value.StartsWith('"'))
            {
                if (value.Length < 2 || value[^1] != '"') throw new InvalidDataException("Unterminated patch string.");
                var decoded = new StringBuilder();
                for (var index = 1; index < value.Length - 1; index++)
                {
                    var character = value[index];
                    if (character == '\\')
                    {
                        if (++index >= value.Length - 1) throw new InvalidDataException("Invalid patch string escape.");
                        character = value[index] switch { 'a' => '\a', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', var other => other };
                    }
                    else if (character == '"') throw new InvalidDataException("Unexpected patch string delimiter.");
                    decoded.Append(character);
                }
                data = Encoding.UTF8.GetBytes(decoded.Append('\0').ToString());
            }
            else
            {
                using var payload = new MemoryStream();
                foreach (var token in tokens.Skip(1))
                {
                    var part = Convert.FromHexString(token); if (reverse) Array.Reverse(part); payload.Write(part);
                }
                data = payload.ToArray();
            }
            var address = (long)rawAddress + shift;
            if (data.Length == 0 || address < 0 || address + data.Length > 0x1_0000_0000L || (total += data.Length) > 1_000_000)
                throw new InvalidDataException("Patch writes exceed the address or size limit.");
            for (var index = 0; index < data.Length; index++) writes[(uint)(address + index)] = data[index];
        }
        if (build is null) throw new InvalidDataException("Executable patch has no build identifier.");
        return (build, MergeExecutableEdits.WritePatch(writes));

        void SetBuild(string value)
        {
            if (value.Length is < 16 or > 64 || value.Length % 2 != 0 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid patch build identifier.");
            value = value.ToUpperInvariant();
            if (build is not null && build != value) throw new InvalidDataException("Separate patches targeting different builds before merging.");
            build = value;
        }
    }
}
