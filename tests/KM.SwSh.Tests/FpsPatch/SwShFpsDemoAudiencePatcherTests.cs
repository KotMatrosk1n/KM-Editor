// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.FpsPatch;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace KM.SwSh.Tests.FpsPatch;

public sealed class SwShFpsDemoAudiencePatcherTests
{
    private const ulong AudienceClipHash = 0xE46DE99F0E990642;
    private const ulong AudienceSubClipHash = 0xCD523A1B23139151;
    private const ulong AudienceSub02ClipHash = 0xC6A053DEED7A7E03;

    [Fact]
    public void ConvertArchiveExpandsOpeningAudienceFlipbookKeysTo60Fps()
    {
        var values = Enumerable.Range(0, 33)
            .Select(index => (float)(Math.Floor(index / 2.0d) * 0.0625d))
            .ToArray();
        var source = CreateGfPak(
            (AudienceClipHash, CreateGfbanmClip(includeTranslateV0: true, values)),
            (AudienceSubClipHash, CreateGfbanmClip(includeTranslateV0: true, values.Select(value => value + 2.0f).ToArray())),
            (AudienceSub02ClipHash, CreateGfbanmClip(includeTranslateV0: false, values)));

        var patched = SwShFpsDemoAudiencePatcher.ConvertArchive(source);

        var clips = SwShFpsDemoAudiencePatcher.InspectArchive(patched)
            .OrderBy(clip => clip.RelativeHash)
            .ToArray();
        Assert.Equal(2, clips.Length);
        foreach (var clip in clips)
        {
            Assert.Equal(65u, clip.KeyFrames);
            Assert.Equal(60u, clip.FrameRate);
            var vectorValues = Assert.Single(clip.TranslateV0Values);
            var originalValues = clip.RelativeHash == AudienceSubClipHash
                ? CreateFlipbookValues(2.0f)
                : CreateFlipbookValues();
            Assert.Equal(ExpandFlipbookValues(originalValues), vectorValues);
        }
    }

    [Fact]
    public void ServiceRecognizesGeneratedOpeningRomFsOutputs()
    {
        using var temp = TemporarySwShProject.Create();
        var bseq = CreateOpeningDemoBseq();
        temp.WriteBaseRomFsFile("bin/demo/sequence/d010.bseq", bseq);
        temp.WriteOutputFile(
            "romfs/bin/demo/sequence/d010.bseq",
            SwShFpsBseqPatcher.ConvertOpeningDemoD010(bseq, out _));
        var genericDemoBseq = SwShFpsRomFsTestFixtures.CreateMoveBseq(frameCount: 10, startFrame: 2, endFrame: 4);
        temp.WriteBaseRomFsFile("bin/demo/sequence/d030.bseq", genericDemoBseq);
        temp.WriteOutputFile(
            "romfs/bin/demo/sequence/d030.bseq",
            SwShFpsBseqPatcher.Convert(genericDemoBseq, SwShFpsBseqPatcher.OpeningDemoTimelineScale, out _));

        var archive = CreateGfPak(
            (AudienceClipHash, CreateGfbanmClip(includeTranslateV0: true, CreateFlipbookValues())),
            (AudienceSubClipHash, CreateGfbanmClip(includeTranslateV0: true, CreateFlipbookValues(2.0f))),
            (AudienceSub02ClipHash, CreateGfbanmClip(includeTranslateV0: false, CreateFlipbookValues())));
        temp.WriteBaseRomFsFile("bin/archive/demo/share/anime/a_pl0110.gfpak", archive);
        temp.WriteOutputFile(
            "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak",
            SwShFpsDemoAudiencePatcher.ConvertArchive(archive));

        var service = new SwShFpsPatchService();

        Assert.True(service.IsGeneratedRomFsOutput(temp.Paths, "romfs/bin/demo/sequence/d010.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(temp.Paths, "romfs/bin/demo/sequence/d030.bseq"));
        Assert.True(service.IsGeneratedRomFsOutput(temp.Paths, "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/demo/sequence/d010.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/demo/sequence/d030.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/battle/waza/sequence/d230.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/demo/sequence/d230.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/battle/waza/sequence/ee411.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/battle/waza/sequence/eg_ball01.bseq"));
        Assert.True(SwShFpsPatchService.IsManagedRomFsPath("romfs/bin/archive/demo/share/anime/a_pl0110.gfpak"));
    }

    private static float[] CreateFlipbookValues(float offset = 0.0f)
    {
        return Enumerable.Range(0, 33)
            .Select(index => offset + (float)(Math.Floor(index / 2.0d) * 0.0625d))
            .ToArray();
    }

    private static float[] ExpandFlipbookValues(IReadOnlyList<float> values)
    {
        var expanded = new float[(values.Count - 1) * 2 + 1];
        var outputIndex = 0;
        for (var index = 0; index < values.Count - 1; index++)
        {
            expanded[outputIndex++] = values[index];
            expanded[outputIndex++] = values[index];
        }

        expanded[outputIndex] = values[^1];
        return expanded;
    }

    private static byte[] CreateOpeningDemoBseq()
    {
        const ulong commandHash = 0x1122334455667788;
        const int commandCount = 22;
        var data = new byte[0x24 + commandCount * 20 + 20];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0);
        WriteU32(data, 0x0C, 5342);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, commandHash);
        WriteU32(data, 0x20, 0);
        for (var index = 0; index < commandCount; index++)
        {
            var offset = 0x24 + index * 20;
            WriteU32(data, offset, 0);
            WriteU32(data, offset + 4, index == 21 ? 173u : 1u);
            WriteU64(data, offset + 12, commandHash);
        }

        WriteU32(data, 0x24 + commandCount * 20, 0xFFFFFFFF);
        return data;
    }

    private static byte[] CreateGfbanmClip(bool includeTranslateV0, float[] values)
    {
        var builder = new FlatBufferFixtureBuilder();
        var root = builder.AddTable(maxFieldIndex: 2, objectSize: 12, new Dictionary<int, ushort>
        {
            [0] = 4,
            [2] = 8,
        });
        builder.WriteRoot(root.TableOffset);

        var info = builder.AddTable(maxFieldIndex: 2, objectSize: 12, new Dictionary<int, ushort>
        {
            [1] = 4,
            [2] = 8,
        });
        builder.PatchOffset(root.FieldLocations[0], info.TableOffset);
        builder.WriteU32(info.TableOffset + 4, 33);
        builder.WriteU32(info.TableOffset + 8, 30);

        var materialTable = builder.AddTable(maxFieldIndex: 0, objectSize: 8, new Dictionary<int, ushort>
        {
            [0] = 4,
        });
        builder.PatchOffset(root.FieldLocations[2], materialTable.TableOffset);

        var materialVector = builder.AddTableVector(1);
        builder.PatchOffset(materialTable.FieldLocations[0], materialVector.VectorOffset);

        var materialItemFields = includeTranslateV0
            ? new Dictionary<int, ushort> { [0] = 4, [2] = 8, [3] = 12 }
            : new Dictionary<int, ushort> { [0] = 4 };
        var materialItem = builder.AddTable(
            maxFieldIndex: includeTranslateV0 ? 3 : 0,
            objectSize: includeTranslateV0 ? 16 : 8,
            materialItemFields);
        builder.PatchOffset(materialVector.ElementLocations[0], materialItem.TableOffset);
        builder.PatchOffset(materialItem.FieldLocations[0], builder.AddString("mat"));

        if (!includeTranslateV0)
        {
            return builder.ToArray();
        }

        var paramVectorA = builder.AddTableVector(1);
        builder.PatchOffset(materialItem.FieldLocations[2], paramVectorA.VectorOffset);
        var paramA = AddTranslateParam(builder);
        builder.PatchOffset(paramVectorA.ElementLocations[0], paramA.ParamTable.TableOffset);

        var paramVectorB = builder.AddTableVector(1);
        builder.PatchOffset(materialItem.FieldLocations[3], paramVectorB.VectorOffset);
        var paramB = AddTranslateParam(builder);
        builder.PatchOffset(paramVectorB.ElementLocations[0], paramB.ParamTable.TableOffset);

        var valueVector = builder.AddFloatVector(values);
        builder.PatchOffset(paramA.ValueTable.FieldLocations[0], valueVector);
        builder.PatchOffset(paramB.ValueTable.FieldLocations[0], valueVector);
        return builder.ToArray();
    }

    private static (FlatTable ParamTable, FlatTable ValueTable) AddTranslateParam(FlatBufferFixtureBuilder builder)
    {
        var paramTable = builder.AddTable(maxFieldIndex: 2, objectSize: 12, new Dictionary<int, ushort>
        {
            [0] = 4,
            [2] = 8,
        });
        builder.PatchOffset(paramTable.FieldLocations[0], builder.AddString("TranslateV0"));

        var valueTable = builder.AddTable(maxFieldIndex: 0, objectSize: 8, new Dictionary<int, ushort>
        {
            [0] = 4,
        });
        builder.PatchOffset(paramTable.FieldLocations[2], valueTable.TableOffset);
        return (paramTable, valueTable);
    }

    private static byte[] CreateGfPak(params (ulong RelativeHash, byte[] Data)[] files)
    {
        const ulong folderHash = 0x123456789ABCDEF0;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GFLXPACK"));
        writer.Write(0x1000u);
        writer.Write(0u);
        writer.Write((uint)files.Length);
        writer.Write(1u);
        var fileHeaderOffsetPointer = stream.Position;
        writer.Write(0ul);
        var hashOffsetPointer = stream.Position;
        writer.Write(0ul);
        var folderOffsetPointer = stream.Position;
        writer.Write(0ul);

        var hashOffset = stream.Position;
        foreach (var file in files)
        {
            writer.Write(file.RelativeHash ^ 0x0102030405060708ul);
        }

        PatchU64(stream, hashOffsetPointer, (ulong)hashOffset);

        var folderOffset = stream.Position;
        PatchU64(stream, folderOffsetPointer, (ulong)folderOffset);
        writer.Write(folderHash);
        writer.Write((uint)files.Length);
        writer.Write(0xCCu);
        for (var index = 0; index < files.Length; index++)
        {
            writer.Write(files[index].RelativeHash);
            writer.Write((uint)index);
            writer.Write(0xCCu);
        }

        var fileHeaderOffset = stream.Position;
        PatchU64(stream, fileHeaderOffsetPointer, (ulong)fileHeaderOffset);
        var nextDataOffset = fileHeaderOffset + files.Length * 0x18L;
        foreach (var file in files)
        {
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)file.Data.Length);
            writer.Write((uint)file.Data.Length);
            writer.Write(0xCCu);
            writer.Write((ulong)nextDataOffset);
            nextDataOffset += file.Data.Length;
        }

        foreach (var file in files)
        {
            writer.Write(file.Data);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void PatchU64(Stream stream, long offset, ulong value)
    {
        var oldPosition = stream.Position;
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
        stream.Position = oldPosition;
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }

    private sealed record FlatTable(int TableOffset, IReadOnlyDictionary<int, int> FieldLocations);

    private sealed record FlatVector(int VectorOffset, IReadOnlyList<int> ElementLocations);

    private sealed class FlatBufferFixtureBuilder
    {
        private readonly List<byte> data = [0, 0, 0, 0];

        public void WriteRoot(int tableOffset)
        {
            WriteU32(0, (uint)tableOffset);
        }

        public FlatTable AddTable(int maxFieldIndex, int objectSize, IReadOnlyDictionary<int, ushort> fieldOffsets)
        {
            Align(2);
            var vtableOffset = data.Count;
            AppendU16((ushort)(4 + (maxFieldIndex + 1) * 2));
            AppendU16((ushort)objectSize);
            for (var index = 0; index <= maxFieldIndex; index++)
            {
                AppendU16(fieldOffsets.TryGetValue(index, out var offset) ? offset : (ushort)0);
            }

            Align(4);
            var tableOffset = data.Count;
            for (var index = 0; index < objectSize; index++)
            {
                data.Add(0);
            }

            WriteI32(tableOffset, tableOffset - vtableOffset);
            var locations = fieldOffsets.ToDictionary(
                pair => pair.Key,
                pair => tableOffset + pair.Value);
            return new FlatTable(tableOffset, locations);
        }

        public FlatVector AddTableVector(int count)
        {
            Align(4);
            var vectorOffset = data.Count;
            AppendU32((uint)count);
            var elementLocations = new List<int>(count);
            for (var index = 0; index < count; index++)
            {
                elementLocations.Add(data.Count);
                AppendU32(0);
            }

            return new FlatVector(vectorOffset, elementLocations);
        }

        public int AddFloatVector(float[] values)
        {
            Align(4);
            var vectorOffset = data.Count;
            AppendU32((uint)values.Length);
            Span<byte> scratch = stackalloc byte[sizeof(float)];
            foreach (var value in values)
            {
                BinaryPrimitives.WriteSingleLittleEndian(scratch, value);
                data.AddRange(scratch.ToArray());
            }

            return vectorOffset;
        }

        public int AddString(string value)
        {
            Align(4);
            var stringOffset = data.Count;
            var bytes = Encoding.ASCII.GetBytes(value);
            AppendU32((uint)bytes.Length);
            data.AddRange(bytes);
            data.Add(0);
            return stringOffset;
        }

        public void PatchOffset(int fieldLocation, int targetOffset)
        {
            WriteU32(fieldLocation, checked((uint)(targetOffset - fieldLocation)));
        }

        public void WriteU32(int offset, uint value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            for (var index = 0; index < scratch.Length; index++)
            {
                data[offset + index] = scratch[index];
            }
        }

        public byte[] ToArray()
        {
            return data.ToArray();
        }

        private void WriteI32(int offset, int value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(scratch, value);
            for (var index = 0; index < scratch.Length; index++)
            {
                data[offset + index] = scratch[index];
            }
        }

        private void AppendU16(ushort value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, value);
            data.AddRange(scratch.ToArray());
        }

        private void AppendU32(uint value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            data.AddRange(scratch.ToArray());
        }

        private void Align(int alignment)
        {
            while (data.Count % alignment != 0)
            {
                data.Add(0);
            }
        }
    }
}
