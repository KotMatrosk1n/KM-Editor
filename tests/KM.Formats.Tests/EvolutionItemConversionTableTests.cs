// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.Pokemon;
using Xunit;

namespace KM.Formats.Tests;

public sealed class EvolutionItemConversionTableTests
{
    [Fact]
    public void WriteAndReadPreserveItemParameterMappingsAndFreeSlots()
    {
        EvolutionItemConversion[] rows =
        [
            new(83, 4),
            new(0, 11),
            new(248, 12),
            new(80, 1),
        ];

        var bytes = EvolutionItemConversionTable.Write(rows);
        var parsed = EvolutionItemConversionTable.Read(bytes);

        Assert.Equal(rows, parsed);
    }

    [Fact]
    public void ReadRejectsTruncatedOrInvalidTables()
    {
        byte[][] invalidTables =
        [
            [],
            [4, 0, 0, 0],
            [255, 255, 255, 127, 0, 0, 0, 0],
        ];

        foreach (var bytes in invalidTables)
        {
            Assert.Throws<InvalidDataException>(() => EvolutionItemConversionTable.Read(bytes));
        }
    }
}
