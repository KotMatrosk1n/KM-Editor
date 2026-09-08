// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.SwSh;
namespace KM.SwSh.RuntimeSettings;

/// <summary>Authors the gameplay rows in the retail Options message bank.</summary>
public static class SwShNativeGameplayMenuRomFsMaterializer
{
    private const int MaximumSourceBytes = 64 * 1024 * 1024;
    private const string MessagePath = "common/option";
    private static readonly string[] Languages =
    ["English", "French", "German", "Italian", "JPN", "JPN_KANJI",
     "Korean", "Simp_Chinese", "Spanish", "Trad_Chinese"];
    private const string KeysHash = "86EADA2B27947F541F466475BA719AD6FAD8BF31B8FDD5ECD711530CDA0875B4";
    private static readonly string[] DataHashes =
    [
        "EE56F45D4EB3B4586DBC0F9857AE62BAE77758605397EDB38387E6FBB00155E1",
        "55EC63FC72682F978B63E2D49F7D48CCD64A86CA83CE4AC74983B03E08318BA1",
        "C416921AB061E33D52A7A9CFD293DF10425794E6D45CC56A0AE3B45CD115C7F2",
        "A7DEB396D402EF4BC1AD66E1821A493052FDD06FACE0A73F170D604C2D593CCA",
        "5870EFFCB12EDC4D619933BB407DBB930EDB387A01EDE9C7BE021FB5BBAB0FC0",
        "210E6FA8E61CFE613383A5C0B7E286F0949C265790128732C53B27131B4E597B",
        "8FA313377AF08B77084E3ABCA9F781B4AD935AA80D9B743F7BB7925F8EA5F0E6",
        "A109878D625190C7FBE69FB10B00578CABDC95746A6D3B49645D4BA7A6A4F363",
        "0D4BEC0295B80611B755540BABD490BE569BA8BF9B2E73B784C3FAF7D26219DE",
        "7D3AD809369CA3379BE1C0FADD465F03536AAA44C806B4FC202E5274EF0C66CA",
    ];
    private static readonly MessageDefinition[] Messages =
    [
        new("km_exp_share", "Experience Share", "Multi Exp.", "EP Teiler", "Condividi ESP", "けいけんちきょうゆう", "経験値共有", "경험치 공유", "经验分享", "Repartir EXP", "經驗分享"),
        new("km_exp_rate", "Experience Rate", "Taux d’EXP", "EP Rate", "Tasso ESP", "けいけんちりつ", "経験値率", "경험치 비율", "经验倍率", "Tasa de EXP", "經驗倍率"),
        new("km_level_cap", "EXP Level Cap", "Limite de niveau EXP", "EP Levelgrenze", "Limite livello ESP", "けいけんちレベルじょうげん", "経験値レベル上限", "경험치 레벨 제한", "经验等级上限", "Límite de nivel EXP", "經驗等級上限"),
        new("km_off", "Off", "Non", "Aus", "No", "オフ", "オフ", "끄기", "关", "No", "關"),
        new("km_on", "On", "Oui", "An", "Sì", "オン", "オン", "켜기", "开", "Sí", "開"),
        new("km_exp_share_help", "Share battle and catch EXP with the party.", "Partager l’EXP des combats et captures avec l’équipe.", "Kampf und Fang EP mit dem Team teilen.", "Condividi ESP di lotte e catture con la squadra.", "せんとうと ほかくの けいけんちを きょうゆう。", "戦闘と捕獲の経験値を共有。", "배틀과 포획 경험치를 파티와 공유합니다.", "与队伍分享对战和捕获经验。", "Comparte EXP de combates y capturas con el equipo.", "與隊伍分享對戰和捕獲經驗。"),
        new("km_exp_rate_help", "Battle and catch EXP: 0% to 500%, in steps of 10%.", "EXP des combats et captures : 0 à 500 %, par pas de 10 %.", "Kampf und Fang EP: 0 bis 500 %, in 10 % Schritten.", "ESP di lotte e catture: da 0% a 500%, incrementi del 10%.", "せんとうと ほかくの けいけんち。0〜500%、10%ずつ。", "戦闘と捕獲の経験値。0〜500%、10%刻み。", "배틀과 포획 경험치: 0~500%, 10% 단위.", "对战和捕获经验：0%至500%，每次10%。", "EXP de combates y capturas: del 0% al 500%, en pasos del 10%.", "對戰和捕獲經驗：0%至500%，每次10%。"),
        new("km_level_cap_help", "Stop battle and catch EXP at this level. Off removes the cap.", "Arrêter l’EXP des combats et captures à ce niveau. Non désactive la limite.", "Kampf und Fang EP bei diesem Level stoppen. Aus hebt die Grenze auf.", "Ferma ESP di lotte e catture a questo livello. No rimuove il limite.", "このレベルで せんとうと ほかくの けいけんちを とめる。オフで かいじょ。", "このレベルで戦闘と捕獲の経験値を止める。オフで解除。", "이 레벨에서 배틀과 포획 경험치를 중지합니다. 끄기는 제한을 해제합니다.", "达到此等级后停止获得对战和捕获经验。关表示无限制。", "Detén la EXP de combates y capturas en este nivel. No elimina el límite.", "達到此等級後停止獲得對戰和捕獲經驗。關表示無限制。"),
        new("km_save_failed", "Could not save gameplay settings. Check SD storage and try again.", "Impossible d’enregistrer. Vérifiez le stockage SD et réessayez.", "Speichern fehlgeschlagen. SD Speicher prüfen und erneut versuchen.", "Salvataggio non riuscito. Controlla la memoria SD e riprova.", "ほぞんできません。SDを かくにんして もういちど。", "保存できません。SDを確認して再試行してください。", "저장하지 못했습니다. SD 저장소를 확인하고 다시 시도하세요.", "无法保存。请检查SD存储后重试。", "No se pudo guardar. Comprueba el almacenamiento SD y vuelve a intentarlo.", "無法儲存。請檢查SD儲存後重試。"),
    ];

    public static IReadOnlyDictionary<string, byte[]> Build(
        ProjectGame game, string baseRomFsRoot, CancellationToken cancellationToken = default)
    {
        if (game is not ProjectGame.Sword and not ProjectGame.Shield)
            throw new InvalidOperationException("The Options gameplay page requires Sword or Shield.");
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        var root = Path.GetFullPath(baseRomFsRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var sources = Languages.Select((language, index) => new CoordinatedGameTextSource(
            language,
            ReadExactSource(root, $"bin/message/{language}/{MessagePath}.dat", DataHashes[index]),
            ReadExactSource(root, $"bin/message/{language}/{MessagePath}.tbl", KeysHash))).ToArray();
        foreach (var message in Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sources = CoordinatedGameTextAuthoring.Insert(sources, Languages, message.Key,
                message.TextByLanguage, GameTextNullLineEncoding.LegacyCountOne)
                .Select(result => new CoordinatedGameTextSource(result.Language, result.Data, result.Keys))
                .ToArray();
        }
        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            outputs[$"romfs/bin/message/{source.Language}/{MessagePath}.dat"] = source.Data;
            outputs[$"romfs/bin/message/{source.Language}/{MessagePath}.tbl"] = source.Keys;
        }
        return outputs;
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
