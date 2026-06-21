// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using System.Text;

namespace KM.SwSh.Tests.FpsPatch;

internal static class SwShFpsRomFsTestFixtures
{
    public const ulong AudienceClipHash = 0xE46DE99F0E990642;
    public const ulong AudienceSubClipHash = 0xCD523A1B23139151;
    public const ulong AudienceSub02ClipHash = 0xC6A053DEED7A7E03;
    public const string TrainerThrowCameraRelativePath = "romfs/bin/battle/waza/camera/ballthrow/tr0002_00_ba_ballthrow01_cam.gfbcama";
    public const string PlayerThrowCameraRelativePath = "romfs/bin/battle/waza/camera/ballthrow/pc0001_00_ba_ballthrow01_cam.gfbcama";
    public const string TrainerThrowBattleModelRelativePath = "romfs/bin/battle/waza/model/anm/ob0304_00_tr0002_00_ba0120_g_ballthrow01_end.gfbanm";
    public const string PlayerThrowBattleModelRelativePath = "romfs/bin/battle/waza/model/anm/ob0304_00_pc0002_00_ba0120_g_ballthrow01_end.gfbanm";
    public const string TrainerThrowLooseRelativePath = "romfs/bin/chara/data/tr/tr0165_00_wife/anm/tr0165_00_ba0120_g_ballthrow01_loop.gfbanm";
    public const string TrainerThrowArchiveRelativePath = "romfs/bin/archive/chara/data/tr/anm/tr0002_00_friend_tr0002_00_battle01.gfpak";
    public const string TitleDemoRelativePath = "romfs/bin/demo/sequence/sd9010_title.bseq";
    public const string TrainerThrowArchiveClipName = "tr0002_00_ba0122_g_ballthrow01_end.gfbanm";
    public const string TrainerNonThrowArchiveClipName = "tr0002_00_ba0414_speak03_end.gfbanm";
    private static readonly string[] RequiredSpecialBallSequenceNames =
    [
        "d230.bseq",
        "ee101.bseq",
        "ee102.bseq",
        "ee103.bseq",
        "ee104.bseq",
        "ee105.bseq",
        "ee106.bseq",
        "ee107.bseq",
        "ee108.bseq",
        "ee109.bseq",
        "ee110.bseq",
        "ee111.bseq",
        "ee112.bseq",
        "ee113.bseq",
        "ee311.bseq",
        "ee312.bseq",
        "ee315.bseq",
        "ee316.bseq",
        "ee326.bseq",
        "ee327.bseq",
        "ee328.bseq",
        "ee330.bseq",
        "ee331.bseq",
        "ee332.bseq",
        "ee333.bseq",
        "ee340.bseq",
        "ee341.bseq",
        "ee343.bseq",
        "ee344.bseq",
        "ee347.bseq",
        "ee349.bseq",
        "ee350.bseq",
        "ee351.bseq",
        "ee354.bseq",
        "ee400.bseq",
        "ee401.bseq",
        "ee402.bseq",
        "ee403.bseq",
        "ee404.bseq",
        "ee405.bseq",
        "ee406.bseq",
        "ee407.bseq",
        "ee408.bseq",
        "ee409.bseq",
        "ee411.bseq",
        "ee412.bseq",
        "ee420.bseq",
        "ee502.bseq",
        "ee630.bseq",
    ];

    public static void WriteCompleteManagedBaseRomFs(TemporarySwShProject temp)
    {
        var moveBseq = CreateMoveBseq(frameCount: 10, startFrame: 2, endFrame: 4);
        WriteMoveEffectFiles(temp, "eg", 180, moveBseq);
        temp.WriteBaseRomFsFile("bin/battle/waza/sequence/eg_ball01.bseq", moveBseq);
        WriteMoveEffectFiles(temp, "es", 37, moveBseq);
        WriteMoveEffectFiles(temp, "et", 5, moveBseq);
        WriteMoveEffectFiles(temp, "ew", 787, moveBseq);

        temp.WriteBaseRomFsFile("bin/demo/sequence/d010.bseq", CreateOpeningDemoBseq());
        temp.WriteBaseRomFsFile("bin/demo/sequence/d030.bseq", moveBseq);
        temp.WriteBaseRomFsFile("bin/demo/sequence/r2d020.bseq", moveBseq);
        temp.WriteBaseRomFsFile(TitleDemoRelativePath["romfs/".Length..], moveBseq);
        foreach (var sequenceName in RequiredSpecialBallSequenceNames)
        {
            temp.WriteBaseRomFsFile($"bin/battle/waza/sequence/{sequenceName}", moveBseq);
        }

        temp.WriteBaseRomFsFile("bin/archive/demo/share/anime/a_pl0110.gfpak", CreateAudienceArchive());
        temp.WriteBaseRomFsFile("bin/archive/field/model/unit_obj_pc_recovery01.gfpak", CreatePokemonCenterRecoveryArchive());
        temp.WriteBaseRomFsFile(
            TrainerThrowCameraRelativePath["romfs/".Length..],
            CreateGfAnimationClip(keyFrames: 143, frameRate: 60));
        temp.WriteBaseRomFsFile(
            PlayerThrowCameraRelativePath["romfs/".Length..],
            CreateGfAnimationClip(keyFrames: 101, frameRate: 60));
        temp.WriteBaseRomFsFile(
            TrainerThrowBattleModelRelativePath["romfs/".Length..],
            CreateGfAnimationClip(keyFrames: 196, frameRate: 60));
        temp.WriteBaseRomFsFile(
            PlayerThrowBattleModelRelativePath["romfs/".Length..],
            CreateGfAnimationClip(keyFrames: 371, frameRate: 60));
        temp.WriteBaseRomFsFile(
            TrainerThrowLooseRelativePath["romfs/".Length..],
            CreateGfAnimationClip(keyFrames: 120, frameRate: 60));
        temp.WriteBaseRomFsFile(
            TrainerThrowArchiveRelativePath["romfs/".Length..],
            CreateTrainerThrowArchive());
    }

    public static byte[] CreateMoveBseq(uint frameCount, uint startFrame, uint endFrame)
    {
        const ulong commandHash = 0x1122334455667788;
        var data = new byte[0x50];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0);
        WriteU32(data, 0x0C, frameCount);
        WriteU32(data, 0x10, 0);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, commandHash);
        WriteU32(data, 0x20, 0);
        WriteU32(data, 0x24, startFrame);
        WriteU32(data, 0x28, endFrame);
        WriteU64(data, 0x30, commandHash);
        WriteU32(data, 0x38, 0xFFFFFFFF);
        return data;
    }

    public static byte[] CreateOpeningDemoBseq()
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

    public static byte[] CreateAudienceArchive()
    {
        return CreateGfPak(
            (AudienceClipHash, CreateGfbanmClip(includeTranslateV0: true, CreateFlipbookValues())),
            (AudienceSubClipHash, CreateGfbanmClip(includeTranslateV0: true, CreateFlipbookValues(2.0f))),
            (AudienceSub02ClipHash, CreateGfbanmClip(includeTranslateV0: false, CreateFlipbookValues())));
    }

    public static byte[] CreateGfAnimationClip(uint keyFrames, uint frameRate)
    {
        return CreateGfbanmClip(
            includeTranslateV0: false,
            [],
            keyFrames,
            frameRate);
    }

    public static byte[] CreateTrainerThrowArchive()
    {
        return SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    TrainerThrowArchiveClipName,
                    CreateGfAnimationClip(keyFrames: 148, frameRate: 60)),
                new SwShGfPackNamedFile(
                    TrainerNonThrowArchiveClipName,
                    CreateGfAnimationClip(keyFrames: 80, frameRate: 60)),
            ])
            .Write();
    }

    public static byte[] CreatePokemonCenterRecoveryArchive()
    {
        return SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    "unit_obj_pc_recovery01_main01_ballput.gfbanm",
                    CreateGfAnimationClip(keyFrames: 108, frameRate: 30)),
                new SwShGfPackNamedFile(
                    "unit_obj_pc_recovery01_main01_recovery.gfbanm",
                    CreateGfAnimationClip(keyFrames: 83, frameRate: 30)),
                new SwShGfPackNamedFile(
                    "unit_obj_pc_recovery01_ballflash01_recovery.gfbanm",
                    CreateGfAnimationClip(keyFrames: 83, frameRate: 30)),
                new SwShGfPackNamedFile(
                    "unit_obj_pc_recovery01_text01_open.gfbanm",
                    CreateGfAnimationClip(keyFrames: 6, frameRate: 30)),
            ])
            .Write();
    }

    public static float[] CreateFlipbookValues(float offset = 0.0f)
    {
        return Enumerable.Range(0, 33)
            .Select(index => offset + (float)(Math.Floor(index / 2.0d) * 0.0625d))
            .ToArray();
    }

    public static float[] ExpandFlipbookValues(IReadOnlyList<float> values)
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

    private static void WriteMoveEffectFiles(
        TemporarySwShProject temp,
        string prefix,
        int count,
        byte[] contents)
    {
        for (var index = 0; index < count; index++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/battle/waza/sequence/{prefix}{index:D3}.bseq",
                contents);
        }
    }

    private static byte[] CreateGfbanmClip(
        bool includeTranslateV0,
        float[] values,
        uint keyFrames = 33,
        uint frameRate = 30)
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
        builder.WriteU32(info.TableOffset + 4, keyFrames);
        builder.WriteU32(info.TableOffset + 8, frameRate);

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
