// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SwSh.GameModules;

public static class SwShBattleCafeRewardSourceReader
{
    public const string SourceRelativePath = "romfs/bin/script/amx/sub_event_011.amx";

    private const int MaximumExpandedBytes = 4 * 1024 * 1024;
    private const ushort Amx64Magic = 0xF1E1;
    private const short CompactFlag = 0x0004;
    private const int CellSize = 8;
    private const int ExpectedCodeCellCount = 5_541;
    private const int ExpectedDataCellCount = 4_443;
    private const int TableVectorCell = 4_327;
    internal const int TableRowCount = 23;
    internal const int TableFirstRowCell = 4_351;
    internal const int TableColumnCount = 4;
    private const int OwnerSelectorFirstCodeCell = 5_239;
    private const int OwnerSelectorCodeCellCount = 75;
    private const int RewardConsumerFirstCodeCell = 5_385;
    private const int RewardConsumerCodeCellCount = 120;
    private const string OwnerSelectorSha256 =
        "92AE354BD0F7C13E5E3E3F597DC2F8DCFF9F478A4A820B943933865D2841F8E0";
    private const string RewardConsumerSha256 =
        "9F7598206DCDA382FD9DF91B0CC5A40D5164CDC096F35E05110C297B10004FBF";

    public static SwShBattleCafeRewardSource Load(
        OpenedProject project,
        Func<string, byte[]> readAllBytes,
        IReadOnlyDictionary<int, string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(readAllBytes);
        ArgumentNullException.ThrowIfNull(itemNames);

        var entry = project.FileGraph.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, SourceRelativePath, StringComparison.OrdinalIgnoreCase));
        var sourcePath = entry is null ? null : ResolveSourcePath(project.Paths, entry);
        if (sourcePath is null)
        {
            return Unavailable("battle-cafe-source-unavailable");
        }

        try
        {
            return Parse(readAllBytes(sourcePath), itemNames);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return Unavailable("battle-cafe-source-shape-unverified");
        }
    }

    public static SwShBattleCafeRewardSource Parse(
        ReadOnlySpan<byte> source,
        IReadOnlyDictionary<int, string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(itemNames);
        var decoded = DecodeAmx(source);
        if (decoded.Code.Length != ExpectedCodeCellCount * CellSize
            || decoded.Data.Length != ExpectedDataCellCount * CellSize)
        {
            throw new InvalidDataException("Battle Cafe source has an unsupported AMX code or data shape.");
        }

        ValidateCodeSlice(
            decoded.Code,
            OwnerSelectorFirstCodeCell,
            OwnerSelectorCodeCellCount,
            OwnerSelectorSha256);
        ValidateCodeSlice(
            decoded.Code,
            RewardConsumerFirstCodeCell,
            RewardConsumerCodeCellCount,
            RewardConsumerSha256);

        if (ReadDataCell(decoded.Data, TableVectorCell) != TableRowCount)
        {
            throw new InvalidDataException("Battle Cafe source has an unsupported reward row count.");
        }

        for (var rowIndex = 0; rowIndex < TableRowCount; rowIndex++)
        {
            var expectedRelativeOffset = checked(184L + rowIndex * 24L);
            if (ReadDataCell(decoded.Data, TableVectorCell + 1 + rowIndex) != expectedRelativeOffset)
            {
                throw new InvalidDataException("Battle Cafe source has an unsupported row indirection table.");
            }
        }

        var rewards = new List<SwShBattleCafeRewardEntry>(TableRowCount);
        var itemIds = new HashSet<int>();
        var percentageTotals = new int[3];
        for (var rowIndex = 0; rowIndex < TableRowCount; rowIndex++)
        {
            var firstCell = checked(TableFirstRowCell + rowIndex * TableColumnCount);
            var itemId = ReadBoundedInt32(decoded.Data, firstCell, 1, ushort.MaxValue, "item ID");
            if (!itemIds.Add(itemId))
            {
                throw new InvalidDataException("Battle Cafe source contains duplicate reward item identity.");
            }

            if (!itemNames.TryGetValue(itemId, out var itemName) || string.IsNullOrWhiteSpace(itemName))
            {
                throw new InvalidDataException("Battle Cafe source references an item outside the loaded item catalog.");
            }

            var dwight = ReadBoundedInt32(decoded.Data, firstCell + 1, 0, 100, "reward percentage");
            var bernard = ReadBoundedInt32(decoded.Data, firstCell + 2, 0, 100, "reward percentage");
            var richard = ReadBoundedInt32(decoded.Data, firstCell + 3, 0, 100, "reward percentage");
            percentageTotals[0] = checked(percentageTotals[0] + dwight);
            percentageTotals[1] = checked(percentageTotals[1] + bernard);
            percentageTotals[2] = checked(percentageTotals[2] + richard);
            rewards.Add(new SwShBattleCafeRewardEntry(
                rowIndex + 1,
                itemId,
                itemName,
                dwight,
                bernard,
                richard));
        }

        if (percentageTotals.Any(total => total != 100))
        {
            throw new InvalidDataException("Battle Cafe source reward percentages do not total 100 for every owner branch.");
        }

        return new SwShBattleCafeRewardSource(rewards);
    }

    private static DecodedAmx DecodeAmx(ReadOnlySpan<byte> source)
    {
        if (source.Length < 0x38)
        {
            throw new InvalidDataException("Battle Cafe AMX source is too small.");
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(source);
        var magic = BinaryPrimitives.ReadUInt16LittleEndian(source[0x04..]);
        var flags = BinaryPrimitives.ReadInt16LittleEndian(source[0x08..]);
        var codeOffset = BinaryPrimitives.ReadInt32LittleEndian(source[0x0C..]);
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(source[0x10..]);
        var heapOffset = BinaryPrimitives.ReadInt32LittleEndian(source[0x14..]);
        if (size != source.Length
            || magic != Amx64Magic
            || codeOffset < 0x38
            || codeOffset > dataOffset
            || dataOffset > heapOffset
            || heapOffset > MaximumExpandedBytes
            || (dataOffset - codeOffset) % CellSize != 0
            || (heapOffset - dataOffset) % CellSize != 0)
        {
            throw new InvalidDataException("Battle Cafe AMX header is unsupported.");
        }

        byte[] expanded;
        if ((flags & CompactFlag) != 0)
        {
            expanded = ExpandCompact(source, size, codeOffset, heapOffset);
        }
        else
        {
            if (source.Length < heapOffset)
            {
                throw new InvalidDataException("Battle Cafe AMX memory is truncated.");
            }

            expanded = source[..heapOffset].ToArray();
        }

        return new DecodedAmx(
            expanded.AsSpan(codeOffset, dataOffset - codeOffset).ToArray(),
            expanded.AsSpan(dataOffset, heapOffset - dataOffset).ToArray());
    }

    private static byte[] ExpandCompact(
        ReadOnlySpan<byte> source,
        int size,
        int codeOffset,
        int heapOffset)
    {
        var expanded = new byte[heapOffset];
        source[..codeOffset].CopyTo(expanded);
        var sourceRemaining = size - codeOffset;
        var destinationRemaining = heapOffset - codeOffset;
        while (sourceRemaining > 0)
        {
            ulong cell = 0;
            var shift = 0;
            byte signSource = 0;
            do
            {
                sourceRemaining--;
                if (sourceRemaining < 0)
                {
                    throw new InvalidDataException("Battle Cafe compact AMX cell is truncated.");
                }

                signSource = source[codeOffset + sourceRemaining];
                cell |= (ulong)(signSource & 0x7F) << shift;
                shift += 7;
                if (shift > 70)
                {
                    throw new InvalidDataException("Battle Cafe compact AMX cell exceeds its bounded width.");
                }
            }
            while (sourceRemaining > 0 && (source[codeOffset + sourceRemaining - 1] & 0x80) != 0);

            if ((signSource & 0x40) != 0)
            {
                while (shift < CellSize * 8)
                {
                    cell |= 0xFFUL << shift;
                    shift += 8;
                }
            }

            destinationRemaining -= CellSize;
            if (destinationRemaining < 0)
            {
                throw new InvalidDataException("Battle Cafe compact AMX expands beyond its declared memory.");
            }

            BinaryPrimitives.WriteUInt64LittleEndian(
                expanded.AsSpan(codeOffset + destinationRemaining, CellSize),
                cell);
        }

        if (destinationRemaining != 0)
        {
            throw new InvalidDataException("Battle Cafe compact AMX does not fill its declared memory.");
        }

        return expanded;
    }

    private static void ValidateCodeSlice(
        byte[] code,
        int firstCell,
        int cellCount,
        string expectedSha256)
    {
        var firstByte = checked(firstCell * CellSize);
        var byteCount = checked(cellCount * CellSize);
        if (firstByte > code.Length - byteCount)
        {
            throw new InvalidDataException("Battle Cafe AMX consumer slice is missing.");
        }

        var actual = Convert.ToHexString(SHA256.HashData(code.AsSpan(firstByte, byteCount)));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Battle Cafe AMX consumer shape is not the verified selection route.");
        }
    }

    private static long ReadDataCell(byte[] data, int cell)
    {
        var byteOffset = checked(cell * CellSize);
        if (byteOffset > data.Length - CellSize)
        {
            throw new InvalidDataException("Battle Cafe AMX data cell is outside the declared data section.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(byteOffset, CellSize));
    }

    private static int ReadBoundedInt32(
        byte[] data,
        int cell,
        int minimum,
        int maximum,
        string field)
    {
        var value = ReadDataCell(data, cell);
        if (value < minimum || value > maximum)
        {
            throw new InvalidDataException($"Battle Cafe {field} is outside its verified range.");
        }

        return checked((int)value);
    }

    private static SwShBattleCafeRewardSource Unavailable(string reasonCode)
    {
        return new SwShBattleCafeRewardSource([], reasonCode);
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null)
        {
            if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
            {
                return null;
            }

            return Path.Combine(
                paths.OutputRootPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        if (entry.BaseFile is not null
            && !string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                paths.BaseRomFsPath,
                entry.RelativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        return null;
    }

    private sealed record DecodedAmx(byte[] Code, byte[] Data);
}

public static class SwShTrainerTypeEventAssignmentSourceReader
{
    public const string SourceRootRelativePath = "romfs/bin/trainer/trainer_type";

    private const int ExpectedAssignmentCount = 254;
    private const int FileSize = 0x118;
    private const int EventOffset = 0x18;
    private const int EventCapacity = 0x80;

    public static SwShTrainerTypeEventAssignmentSource Load(
        OpenedProject project,
        Func<string, byte[]> readAllBytes)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(readAllBytes);

        try
        {
            var candidates = project.FileGraph.Entries
                .Where(entry => entry.RelativePath.StartsWith(
                    SourceRootRelativePath + "/",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length != ExpectedAssignmentCount)
            {
                return Unavailable("trainer-type-event-source-incomplete");
            }

            var assignments = new List<SwShTrainerTypeEventAssignment>(ExpectedAssignmentCount);
            var identities = new HashSet<int>();
            foreach (var entry in candidates)
            {
                if (!TryReadCanonicalTrainerTypeId(entry.RelativePath, out var trainerTypeId)
                    || !identities.Add(trainerTypeId))
                {
                    return Unavailable("trainer-type-event-identity-ambiguous");
                }

                var sourcePath = ResolveSourcePath(project.Paths, entry);
                if (sourcePath is null)
                {
                    return Unavailable("trainer-type-event-source-unavailable");
                }

                assignments.Add(Parse(
                    trainerTypeId,
                    entry.LayeredFile is not null,
                    readAllBytes(sourcePath)));
            }

            if (!identities.SetEquals(Enumerable.Range(0, ExpectedAssignmentCount)))
            {
                return Unavailable("trainer-type-event-source-incomplete");
            }

            return new SwShTrainerTypeEventAssignmentSource(
                assignments.OrderBy(assignment => assignment.TrainerTypeId).ToArray());
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return Unavailable("trainer-type-event-source-shape-unverified");
        }
    }

    public static SwShTrainerTypeEventAssignment Parse(
        int trainerTypeId,
        bool isLayered,
        ReadOnlySpan<byte> source)
    {
        if (trainerTypeId is < 0 or >= ExpectedAssignmentCount || source.Length != FileSize)
        {
            throw new InvalidDataException("Trainer type event assignment has an unsupported physical row shape.");
        }

        var eventBytes = source.Slice(EventOffset, EventCapacity);
        var terminator = eventBytes.IndexOf((byte)0);
        if (terminator <= 0 || terminator >= EventCapacity)
        {
            throw new InvalidDataException("Trainer type event assignment is not null terminated within its fixed field.");
        }

        var valueBytes = eventBytes[..terminator];
        foreach (var value in valueBytes)
        {
            if (value is < 0x20 or > 0x7E)
            {
                throw new InvalidDataException("Trainer type event assignment contains unsupported text bytes.");
            }
        }

        var eventName = Encoding.ASCII.GetString(valueBytes);
        if (!eventName.StartsWith("Play_bgm_", StringComparison.Ordinal)
            || eventName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidDataException("Trainer type event assignment is outside the verified audio event family.");
        }

        return new SwShTrainerTypeEventAssignment(trainerTypeId, eventName, isLayered);
    }

    private static bool TryReadCanonicalTrainerTypeId(string relativePath, out int trainerTypeId)
    {
        trainerTypeId = -1;
        var normalized = relativePath.Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        const string prefix = "trainer_type_";
        const string suffix = ".bin";
        if (fileName.Length != prefix.Length + 3 + suffix.Length
            || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(
                fileName.AsSpan(prefix.Length, 3),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out trainerTypeId))
        {
            return false;
        }

        return trainerTypeId is >= 0 and < ExpectedAssignmentCount
            && string.Equals(
                normalized,
                $"{SourceRootRelativePath}/trainer_type_{trainerTypeId:000}.bin",
                StringComparison.OrdinalIgnoreCase);
    }

    private static SwShTrainerTypeEventAssignmentSource Unavailable(string reasonCode)
    {
        return new SwShTrainerTypeEventAssignmentSource([], reasonCode);
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null)
        {
            if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
            {
                return null;
            }

            return Path.Combine(
                paths.OutputRootPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        if (entry.BaseFile is not null
            && !string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                paths.BaseRomFsPath,
                entry.RelativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        return null;
    }
}
