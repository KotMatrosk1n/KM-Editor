// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.SwSh;

namespace KM.SwSh.RuntimeSettings;

/// <summary>
/// Builds the Sword/Shield native gameplay settings page exclusively from the
/// selected project's exact retail RomFS. The transformation is deliberately
/// limited to the identical Sword/Shield 1.3.2 Pokemon Center script and its
/// coordinated message tables.
/// </summary>
public static class SwShNativeGameplayMenuRomFsMaterializer
{
    private const string PokemonCenterAmx = "bin/script/amx/pokemoncenter.amx";
    private const string ScriptRecordTable = "bin/script/param/script_id/script_id_record.bin";
    private const string PokemonCenterMessage = "script/pokemoncenter";
    private const string RetailAmxSha256 = "12972D2EF01C86790BC9F2127BEE9046DDE583CAF70C88FAA16B2A05F7B5CF70";
    private const string PatchedAmxSha256 = "5A32FEE9E93072858648B6074CF9E15A606344BE53B412118AB921E3C6F4902C";
    private const string RetailScriptRecordSha256 = "FB3B64876282FED9A2036CFB2AE5017C36678D10A04B36521744370440823CFF";
    private const int MaximumSourceBytes = 64 * 1024 * 1024;

    private static readonly string[] Languages =
    [
        "English", "French", "German", "Italian", "JPN", "JPN_KANJI",
        "Korean", "Simp_Chinese", "Spanish", "Trad_Chinese",
    ];

    private static readonly IReadOnlyDictionary<string, string> RetailMessageDataHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["English"] = "587295DF8D266B447316DFAD959E23E526643A9708FFCC73D928441211911120",
            ["French"] = "71F8B1379713B5D256AF6B0E37B3C239D4884E3574396520DEBC7DA183D37876",
            ["German"] = "3641D38D15CE5FB50AC4EF34BE367A1C461D9D48B2DCC5CE9B5B8E6E7884BC00",
            ["Italian"] = "0AF32659A9B3F62EA4352950D28DA911A0DD2DA964E19EE7A524124C92A1E47E",
            ["JPN"] = "A8CC79782E411B6F72EC860C32A15D6DC77CC9B53293BA9E84BBF73C1C091488",
            ["JPN_KANJI"] = "037AD970FA3D5709E9B2FC525350A232AF4283D9B2395EC097888DC051A12340",
            ["Korean"] = "323ADB58BA47CEF6CB15B99497469A8187B302C51D15FCE2ECF33BC1BB47B5A4",
            ["Simp_Chinese"] = "A78318D093EAA4A37A4CFCD8460E22D5121E7D48132C9CF54FBA329B9E7901B4",
            ["Spanish"] = "716F87E0E67EAB76828A1C98F40CD72807F5B212B2D33999F0A669D1EAD10ADE",
            ["Trad_Chinese"] = "97A6F7992133E484737C40A6D1CD5F2A1F27F7EA3AFEAE587DA50A4F72753E42",
        };

    private const string RetailMessageKeysSha256 =
        "A7D3161F7A733EDB206411D0F6EC1BCABDD20478173F2D0D4AF06B9258A10883";

    private static readonly ulong[] PokemonCenterScriptIds =
    [
        0x38ABF2481E194867UL, 0x59A886B867B9C018UL, 0xDB41278104D79CBAUL,
        0xF845AC7F6F685C58UL, 0xF195E4765058E4E4UL, 0x2F8C2AE4C3E0D915UL,
        0xFCCD872DEA802D55UL, 0xC63112E6AE612047UL, 0x0BBC05E808CEF5C3UL,
        0xBA7578B2D64CC142UL, 0x8C4A8899C985EF72UL,
    ];

    private static readonly MessageDefinition[] Messages = BuildMessages();

    public static IReadOnlyDictionary<string, byte[]> Build(
        ProjectGame game,
        string baseRomFsRoot,
        CancellationToken cancellationToken = default)
    {
        if (game is not ProjectGame.Sword and not ProjectGame.Shield)
        {
            throw new InvalidOperationException(
                "The Sword/Shield native gameplay page requires Sword or Shield.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(baseRomFsRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "The selected Sword/Shield base RomFS folder does not exist.");
        }

        var amxSource = ReadExactSource(root, PokemonCenterAmx, RetailAmxSha256);
        cancellationToken.ThrowIfCancellationRequested();
        var recordSource = ReadExactSource(root, ScriptRecordTable, RetailScriptRecordSha256);
        VerifyPokemonCenterScriptRecords(recordSource);

        var messageSources = Languages.ToDictionary(
            language => language,
            language => new MessagePair(
                ReadExactSource(
                    root,
                    $"bin/message/{language}/{PokemonCenterMessage}.dat",
                    RetailMessageDataHashes[language]),
                ReadExactSource(
                    root,
                    $"bin/message/{language}/{PokemonCenterMessage}.tbl",
                    RetailMessageKeysSha256)),
            StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();

        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["romfs/" + PokemonCenterAmx] = PatchPokemonCenterAmx(amxSource),
        };
        foreach (var message in Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coordinated = Languages
                .Select(language => new CoordinatedGameTextSource(
                    language,
                    messageSources[language].Data,
                    messageSources[language].Keys))
                .ToArray();
            var inserted = CoordinatedGameTextAuthoring.Insert(
                coordinated,
                Languages,
                message.Key,
                message.TextByLanguage,
                GameTextNullLineEncoding.LegacyCountOne);
            foreach (var result in inserted)
            {
                messageSources[result.Language] = new MessagePair(result.Data, result.Keys);
            }
        }

        foreach (var language in Languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outputs[$"romfs/bin/message/{language}/{PokemonCenterMessage}.dat"] =
                messageSources[language].Data;
            outputs[$"romfs/bin/message/{language}/{PokemonCenterMessage}.tbl"] =
                messageSources[language].Keys;
        }

        return outputs;
    }

    private static byte[] PatchPokemonCenterAmx(byte[] source)
    {
        VerifySnapshotTransforms();
        var document = SwShAmxDocument.Parse(source);
        var unmodifiedRoundTrip = document.CreateAssembler().Assemble();
        VerifySemanticRoundTrip(document, SwShAmxDocument.Parse(unmodifiedRoundTrip));

        var assembler = document.CreateAssembler();
        var listMenu = RequireInstruction(assembler, 1102, "proc");
        var requestList = RequireInstruction(assembler, 1131, "proc");
        var closeMessage = RequireInstruction(assembler, 1096, "proc");
        var leaveSequence = RequireInstruction(assembler, 7676, "push.p.c", 0);
        var leaveRowId = RequireInstruction(assembler, 7679, "push.p.c", 4);
        var stockSwitch = RequireInstruction(assembler, 7906, "casetbl");
        var stockCleanup = RequireInstruction(assembler, 7917, "push.c");
        VerifyStockSwitch(stockSwitch, stockCleanup);

        var kmRow = BuildMenuRow(4, "km_gameplay_settings", listMenu);
        assembler.InsertBefore(leaveSequence, kmRow);
        assembler.ReplaceLiteralOperand(leaveRowId, 0, 4, 5);

        var readNative = assembler.GetOrAddNative("KmSettingsRead_");
        var writeNative = assembler.GetOrAddNative("KmSettingsWrite_");
        var pageEntry = SwShAmxInstruction.CreateLiteral(134);
        var pageRefresh = SwShAmxInstruction.CreateLiteral(134);
        var shareOffRow = SwShAmxInstruction.CreateLiteral(134);
        var shareRowsDone = SwShAmxInstruction.CreateLiteral(134);
        var rateRows = Enumerable.Range(0, 51)
            .Select(_ => SwShAmxInstruction.CreateLiteral(134))
            .ToArray();
        var rateRowsDone = SwShAmxInstruction.CreateLiteral(134);
        var rateTable = SwShAmxInstruction.CreateSwitchTable(
            130,
            SwShAmxOperand.CodeTarget(rateRows[10]),
            rateRows.Select((row, index) => new SwShAmxSwitchCase(
                index * 1000L,
                SwShAmxOperand.CodeTarget(row))).ToArray());
        var capOffRow = SwShAmxInstruction.CreateLiteral(134);
        var capRows = Enumerable.Range(1, 100)
            .Select(_ => SwShAmxInstruction.CreateLiteral(134))
            .ToArray();
        var capRowsDone = SwShAmxInstruction.CreateLiteral(134);
        var capTable = SwShAmxInstruction.CreateSwitchTable(
            130,
            SwShAmxOperand.CodeTarget(capOffRow),
            capRows.Select((row, index) => new SwShAmxSwitchCase(
                index + 1L,
                SwShAmxOperand.CodeTarget(row))).ToArray());
        var shareAction = SwShAmxInstruction.CreateLiteral(134);
        var rateAction = SwShAmxInstruction.CreateLiteral(134);
        var rateZero = SwShAmxInstruction.CreateLiteral(134);
        var rateReady = SwShAmxInstruction.CreateLiteral(134);
        var capAction = SwShAmxInstruction.CreateLiteral(134);
        var capEnable = SwShAmxInstruction.CreateLiteral(134);
        var capDisable = SwShAmxInstruction.CreateLiteral(134);
        var capReady = SwShAmxInstruction.CreateLiteral(134);
        var pageBack = SwShAmxInstruction.CreateLiteral(134);
        var pageTable = SwShAmxInstruction.CreateSwitchTable(
            130,
            SwShAmxOperand.CodeTarget(pageBack),
            new SwShAmxSwitchCase(0, SwShAmxOperand.CodeTarget(shareAction)),
            new SwShAmxSwitchCase(1, SwShAmxOperand.CodeTarget(rateAction)),
            new SwShAmxSwitchCase(2, SwShAmxOperand.CodeTarget(capAction)));

        var page = new List<SwShAmxInstruction>
        {
            pageEntry,
            I(44, -16),
            pageRefresh,
            I(135, readNative, 0),
            I(17, -24),
            I(3, -24), I(12, 1), I(81),
            SwShAmxInstruction.CreateBranch(53, shareOffRow),
        };
        page.AddRange(BuildMenuRow(0, "km_experience_share_on", listMenu));
        page.Add(SwShAmxInstruction.CreateBranch(51, shareRowsDone));
        page.Add(shareOffRow);
        page.AddRange(BuildMenuRow(0, "km_experience_share_off", listMenu));
        page.Add(shareRowsDone);

        page.AddRange(
        [
            I(3, -24), I(70, 9), I(12, uint.MaxValue), I(81),
            SwShAmxInstruction.CreateBranch(129, rateTable),
        ]);
        for (var index = 0; index < rateRows.Length; index++)
        {
            page.Add(rateRows[index]);
            page.AddRange(BuildMenuRow(1, ExperienceRateMessageKey(index * 10), listMenu));
            page.Add(SwShAmxInstruction.CreateBranch(51, rateRowsDone));
        }
        page.Add(rateTable);
        page.Add(rateRowsDone);

        page.AddRange(
        [
            I(3, -24), I(12, 2), I(81),
            SwShAmxInstruction.CreateBranch(53, capOffRow),
            I(3, -24), I(70, 2), I(12, 127), I(81),
            SwShAmxInstruction.CreateBranch(129, capTable),
            capOffRow,
        ]);
        page.AddRange(BuildMenuRow(2, "km_level_cap_off", listMenu));
        page.Add(SwShAmxInstruction.CreateBranch(51, capRowsDone));
        for (var index = 0; index < capRows.Length; index++)
        {
            page.Add(capRows[index]);
            page.AddRange(BuildMenuRow(2, LevelCapMessageKey(index + 1), listMenu));
            page.Add(SwShAmxInstruction.CreateBranch(51, capRowsDone));
        }
        page.Add(capTable);
        page.Add(capRowsDone);

        page.AddRange(BuildMenuRow(3, "km_settings_back", listMenu));
        page.AddRange(
        [
            I(39, 0), I(39, 1), I(39, 0), I(39, 1), I(39, 32),
            SwShAmxInstruction.CreateBranch(49, requestList),
            I(17, -16),
            I(39, 0), SwShAmxInstruction.CreateBranch(49, closeMessage),
            I(3, -16), SwShAmxInstruction.CreateBranch(129, pageTable),

            shareAction,
            I(3, -24), I(12, 1), I(83), I(17, -24),
            I(41, -24), I(135, writeNative, 8),
            SwShAmxInstruction.CreateBranch(51, pageRefresh),

            rateAction,
            I(3, -24), I(70, 9), I(12, uint.MaxValue), I(81), I(87, 1000),
            I(12, 50000), SwShAmxInstruction.CreateBranch(63, rateZero),
            I(17, -32), SwShAmxInstruction.CreateBranch(51, rateReady),
            rateZero,
            I(89), I(17, -32),
            rateReady,
            I(3, -24), I(12, unchecked((long)0xFFFFFE00000001FFUL)), I(81), I(36),
            I(3, -32), I(68, 9), I(43), I(82), I(17, -24),
            I(41, -24), I(135, writeNative, 8),
            SwShAmxInstruction.CreateBranch(51, pageRefresh),

            capAction,
            I(3, -24), I(12, 2), I(81), SwShAmxInstruction.CreateBranch(53, capEnable),
            I(3, -24), I(70, 2), I(12, 127), I(81), I(12, 100),
            SwShAmxInstruction.CreateBranch(64, capDisable),
            I(3, -24), I(87, 4), I(17, -24),
            SwShAmxInstruction.CreateBranch(51, capReady),
            capDisable,
            I(3, -24), I(12, -511), I(81), I(87, 400), I(17, -24),
            SwShAmxInstruction.CreateBranch(51, capReady),
            capEnable,
            I(3, -24), I(12, -511), I(81), I(87, 6), I(17, -24),
            capReady,
            I(41, -24), I(135, writeNative, 8),
            SwShAmxInstruction.CreateBranch(51, pageRefresh),

            pageBack,
            I(44, 16), SwShAmxInstruction.CreateBranch(51, stockCleanup),
            pageTable,
        ]);

        assembler.InsertBefore(stockCleanup, page.ToArray());
        assembler.AddSwitchCase(stockSwitch, 4, pageEntry);
        var output = assembler.Assemble();
        VerifyPatchedAmx(document, output, readNative, writeNative);
        return output;
    }

    private static SwShAmxInstruction[] BuildMenuRow(
        int rowId,
        string messageKey,
        SwShAmxInstruction listMenu)
    {
        var messageHash = unchecked((long)SwShGfPackFile.HashFnv1a64(messageKey));
        return
        [
            I(39, 0), I(39, messageHash), I(39, rowId), I(39, 24),
            SwShAmxInstruction.CreateBranch(49, listMenu),
        ];
    }

    private static MessageDefinition[] BuildMessages()
    {
        var messages = new List<MessageDefinition>
        {
            new("km_gameplay_settings",
                "KM Gameplay Settings", "Paramètres de jeu KM", "KM Spieleinstellungen",
                "Impostazioni di gioco KM", "KM ゲームせってい", "KM ゲーム設定",
                "KM 게임 설정", "KM 游戏设置", "Ajustes de juego KM", "KM 遊戲設定"),
            new("km_experience_share_off",
                "Experience Share: Off", "Multi Exp. : Non", "EP-Teiler: Aus",
                "Condividi ESP: No", "けいけんちきょうゆう: オフ", "経験値共有: オフ",
                "경험치 공유: 끄기", "经验分享: 关", "Repartir EXP: No", "經驗分享: 關"),
            new("km_experience_share_on",
                "Experience Share: On", "Multi Exp. : Oui", "EP-Teiler: An",
                "Condividi ESP: Sì", "けいけんちきょうゆう: オン", "経験値共有: オン",
                "경험치 공유: 켜기", "经验分享: 开", "Repartir EXP: Sí", "經驗分享: 開"),
        };

        for (var percent = 0; percent <= 500; percent += 10)
        {
            var value = percent.ToString(CultureInfo.InvariantCulture);
            messages.Add(new MessageDefinition(
                ExperienceRateMessageKey(percent),
                $"Experience Rate: {value}%", $"Taux d'EXP : {value} %", $"EP-Rate: {value}%",
                $"Tasso ESP: {value}%", $"けいけんちりつ: {value}%", $"経験値率: {value}%",
                $"경험치 비율: {value}%", $"经验倍率: {value}%", $"Tasa de EXP: {value}%", $"經驗倍率: {value}%"));
        }

        messages.Add(new MessageDefinition(
            "km_level_cap_off",
            "Level Cap: Off", "Limite de niveau : Non", "Level-Limit: Aus",
            "Limite livello: No", "レベルじょうげん: オフ", "レベル上限: オフ",
            "레벨 제한: 끄기", "等级上限: 关", "Límite de nivel: No", "等級上限: 關"));
        for (var level = 1; level <= 100; level++)
        {
            var value = level.ToString(CultureInfo.InvariantCulture);
            messages.Add(new MessageDefinition(
                LevelCapMessageKey(level),
                $"Level Cap: {value}", $"Limite de niveau : {value}", $"Level-Limit: {value}",
                $"Limite livello: {value}", $"レベルじょうげん: {value}", $"レベル上限: {value}",
                $"레벨 제한: {value}", $"等级上限: {value}", $"Límite de nivel: {value}", $"等級上限: {value}"));
        }

        messages.Add(new MessageDefinition(
            "km_settings_back",
            "Back", "Retour", "Zurück", "Indietro", "もどる", "戻る",
            "뒤로", "返回", "Atrás", "返回"));
        return messages.ToArray();
    }

    private static string ExperienceRateMessageKey(int percent) =>
        "km_experience_rate_" + percent.ToString("D3", CultureInfo.InvariantCulture);

    private static string LevelCapMessageKey(int level) =>
        "km_level_cap_" + level.ToString("D3", CultureInfo.InvariantCulture);

    private static void VerifySnapshotTransforms()
    {
        const ulong envelope = (7UL << 41) | (1UL << 44);
        const ulong rateMask = ~((ulong)uint.MaxValue << 9);
        const ulong capMask = ~0x1FEUL;
        for (var share = 0; share <= 1; share++)
        {
            for (var rate = 0; rate <= 50_000; rate += 1_000)
            {
                for (var cap = 0; cap <= 100; cap++)
                {
                    var enabled = cap != 0;
                    var encodedCap = (uint)(enabled ? cap : 100);
                    var packed = envelope
                        | (ulong)(uint)share
                        | (enabled ? 2UL : 0UL)
                        | (ulong)encodedCap << 2
                        | (ulong)(uint)rate << 9;

                    var shareNext = packed ^ 1UL;
                    if ((shareNext & ~1UL) != (packed & ~1UL)
                        || (shareNext & 1UL) == (packed & 1UL))
                    {
                        throw new InvalidOperationException(
                            "The Sword/Shield Experience Share snapshot transform is not reversible.");
                    }

                    var nextRate = rate == 50_000 ? 0 : rate + 1_000;
                    var rateNext = (packed & rateMask) | (ulong)nextRate << 9;
                    if (((rateNext >> 9) & uint.MaxValue) != (ulong)nextRate
                        || (rateNext & rateMask) != (packed & rateMask))
                    {
                        throw new InvalidOperationException(
                            "The Sword/Shield Experience Rate snapshot transform is not exact.");
                    }

                    var capNext = enabled
                        ? cap == 100
                            ? (packed & capMask) | (100UL << 2)
                            : packed + 4
                        : (packed & capMask) | 6UL;
                    var expectedEnabled = !enabled || cap < 100;
                    var expectedCap = !enabled ? 1 : cap == 100 ? 100 : cap + 1;
                    if (((capNext & 2UL) != 0) != expectedEnabled
                        || ((capNext >> 2) & 0x7FUL) != (ulong)expectedCap
                        || (capNext & capMask) != (packed & capMask))
                    {
                        throw new InvalidOperationException(
                            "The Sword/Shield Level Cap snapshot transform is not exact.");
                    }
                }
            }
        }
    }

    private static SwShAmxInstruction I(int opcode, params long[] operands) =>
        SwShAmxInstruction.CreateLiteral(opcode, operands);

    private static SwShAmxInstruction RequireInstruction(
        SwShAmxAssembler assembler,
        int cell,
        string mnemonic,
        long? operand = null)
    {
        var instruction = assembler.GetInstructionAtOriginalCell(cell);
        if (!string.Equals(instruction.Mnemonic, mnemonic, StringComparison.Ordinal)
            || operand is { } expected
            && (instruction.Operands.Count != 1
                || instruction.Operands[0].Kind != SwShAmxOperandKind.Literal
                || instruction.Operands[0].LiteralValue != expected))
        {
            throw new InvalidDataException(
                $"The retail Pokemon Center AMX preimage at cell {cell} is not the supported 1.3.2 instruction.");
        }
        return instruction;
    }

    private static void VerifyStockSwitch(
        SwShAmxInstruction table,
        SwShAmxInstruction cleanup)
    {
        var expectedTargets = new Dictionary<long, int>
        {
            [0] = 7698, [1] = 7746, [2] = 7751, [3] = 7880,
        };
        if (!table.IsSwitchTable
            || table.IsIndirectSwitchTable
            || table.DefaultDestination?.Kind != SwShAmxOperandKind.CodeTarget
            || !ReferenceEquals(table.DefaultDestination.Target, cleanup)
            || table.SwitchCases.Count != expectedTargets.Count
            || table.SwitchCases.Any(candidate =>
                !expectedTargets.TryGetValue(candidate.Value, out var target)
                || candidate.Destination.Kind != SwShAmxOperandKind.CodeTarget
                || candidate.Destination.Target.OriginalCell != target))
        {
            throw new InvalidDataException(
                "The retail Pokemon Center menu switch is not the exact supported 1.3.2 control flow.");
        }
    }

    private static void VerifyPatchedAmx(
        SwShAmxDocument source,
        byte[] output,
        int readNative,
        int writeNative)
    {
        var patched = SwShAmxDocument.Parse(output);
        var outputSha256 = Convert.ToHexString(SHA256.HashData(output));
        if (!string.Equals(
                outputSha256,
                PatchedAmxSha256,
                StringComparison.Ordinal)
            || !patched.DataCells.SequenceEqual(source.DataCells)
            || patched.NativeHashes.Count != source.NativeHashes.Count + 2
            || !patched.NativeHashes.Take(source.NativeHashes.Count).SequenceEqual(source.NativeHashes)
            || patched.NativeHashes[readNative] != SwShAmxNativeNameHash.Compute("KmSettingsRead_")
            || patched.NativeHashes[writeNative] != SwShAmxNativeNameHash.Compute("KmSettingsWrite_"))
        {
            throw new InvalidDataException(
                $"The transformed Pokemon Center AMX failed semantic preservation validation ({outputSha256}).");
        }
    }

    private static void VerifySemanticRoundTrip(
        SwShAmxDocument source,
        SwShAmxDocument rebuilt)
    {
        if (!rebuilt.DataCells.SequenceEqual(source.DataCells)
            || !rebuilt.NativeHashes.SequenceEqual(source.NativeHashes)
            || rebuilt.Instructions.Count != source.Instructions.Count
            || rebuilt.EntryPoint?.OriginalCell != source.EntryPoint?.OriginalCell)
        {
            throw new InvalidDataException(
                "The retail Pokemon Center AMX failed its source-to-IR inverse validation.");
        }
        for (var index = 0; index < source.Instructions.Count; index++)
        {
            var left = source.Instructions[index];
            var right = rebuilt.Instructions[index];
            if (left.Opcode != right.Opcode
                || left.OriginalCell != right.OriginalCell
                || left.Operands.Count != right.Operands.Count
                || left.SwitchCases.Count != right.SwitchCases.Count
                || !OperandsEqual(left.DefaultDestination, right.DefaultDestination))
            {
                throw new InvalidDataException(
                    "The retail Pokemon Center AMX failed its instruction inverse validation.");
            }
            for (var operand = 0; operand < left.Operands.Count; operand++)
            {
                if (!OperandsEqual(left.Operands[operand], right.Operands[operand]))
                {
                    throw new InvalidDataException(
                        "The retail Pokemon Center AMX failed its operand inverse validation.");
                }
            }
            for (var @case = 0; @case < left.SwitchCases.Count; @case++)
            {
                if (left.SwitchCases[@case].Value != right.SwitchCases[@case].Value
                    || !OperandsEqual(
                        left.SwitchCases[@case].Destination,
                        right.SwitchCases[@case].Destination))
                {
                    throw new InvalidDataException(
                        "The retail Pokemon Center AMX failed its switch-table inverse validation.");
                }
            }
        }
    }

    private static bool OperandsEqual(SwShAmxOperand? left, SwShAmxOperand? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }
        return left.Kind == right.Kind
            && (left.Kind == SwShAmxOperandKind.Literal
                ? left.LiteralValue == right.LiteralValue
                : left.Target.OriginalCell == right.Target.OriginalCell);
    }

    private static void VerifyPokemonCenterScriptRecords(byte[] source)
    {
        var records = SwShScriptRecordTable.Parse(source).Records
            .Where(record => string.Equals(record.AmxPath, PokemonCenterAmx, StringComparison.Ordinal))
            .ToArray();
        if (records.Length != PokemonCenterScriptIds.Length
            || !records.Select(record => record.ScriptId).SequenceEqual(PokemonCenterScriptIds)
            || records.Any(record => !string.Equals(
                record.TextPath, PokemonCenterMessage + ".dat", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The Sword/Shield script record table does not contain the exact retail Pokemon Center identities.");
        }
    }

    private static byte[] ReadExactSource(string root, string relativePath, string expectedSha256)
    {
        var canonicalRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var sourcePath = Path.GetFullPath(Path.Combine(root, canonicalRelative));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!sourcePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                $"The required Sword/Shield base RomFS file '{relativePath}' is missing.");
        }

        var info = new FileInfo(sourcePath);
        if (info.Length is <= 0 or > MaximumSourceBytes)
        {
            throw new InvalidDataException(
                $"The Sword/Shield base RomFS file '{relativePath}' has an invalid bounded size.");
        }
        var bytes = File.ReadAllBytes(sourcePath);
        if (!string.Equals(
            Convert.ToHexString(SHA256.HashData(bytes)), expectedSha256,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The Sword/Shield base RomFS file '{relativePath}' is not the exact supported retail 1.3.2 source.");
        }
        return bytes;
    }

    private sealed record MessagePair(byte[] Data, byte[] Keys);

    private sealed record MessageDefinition
    {
        public MessageDefinition(string key, params string[] values)
        {
            if (values.Length != Languages.Length)
            {
                throw new ArgumentException("Every gameplay message requires every supported language.", nameof(values));
            }
            Key = key;
            TextByLanguage = Languages
                .Select((language, index) => (language, values[index]))
                .ToDictionary(pair => pair.language, pair => pair.Item2, StringComparer.Ordinal);
        }

        public string Key { get; }
        public IReadOnlyDictionary<string, string> TextByLanguage { get; }
    }
}
