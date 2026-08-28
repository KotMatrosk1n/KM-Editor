// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SwSh;

namespace KM.SV.RuntimeSettings;

/// <summary>
/// Derives the S/V 4.0.0 stock Options-menu script and message tables from the
/// selected project's own base RomFS. No retail bytes are embedded or read from
/// a private extraction.
/// </summary>
internal static class SvGameplayOptionsRomFsMaterializer
{
    public const string MainScriptPath =
        "romfs/script/lua/bin/release/main/main.blua";

    private const int MaximumMainScriptBytes = 16 * 1024 * 1024;
    private const int MaximumMessageSourceBytes = 1024 * 1024;
    private const int MaximumTrinityIndexBytes = 64 * 1024 * 1024;
    private const long MaximumTrinityPackBytes = 128L * 1024L * 1024L;
    private const string CommonTableSha256 =
        "8E2FE8C9B578138F1BAA6B099E51715A5491B0C417B056FC3E564BBEC7BD9D5F";

    private static readonly Locale[] Locales =
    [
        new(
            "English",
            "9D898387467CA715E9ADA7B99062E58F652AFA56FB57E5352C3F67409777ECC3",
            "Level Cap",
            "Prevent battle EXP from raising Pokémon above the selected level.",
            "Experience Rate",
            "Set battle EXP from 0% through 500% of the normal amount.",
            "Experience Share",
            "Choose whether eligible Pokémon that did not battle receive EXP.",
            "Off",
            "On",
            "Lv."),
        new(
            "Spanish",
            "2826E5BE74AC9B6DD081E241EE8286D01942A52383DC2F4EBCA94F6E71D3C82C",
            "Límite de nivel",
            "Impide que la experiencia de combate supere el nivel elegido.",
            "Porcentaje de experiencia",
            "Ajusta la experiencia de combate del 0% al 500% del valor normal.",
            "Reparto de experiencia",
            "Decide si los Pokémon aptos que no combatan reciben experiencia.",
            "Desactivado",
            "Activado",
            "Nv."),
        new(
            "French",
            "5DB3FC950FD11908F92A5A3B2C4F425F00905F16691D5357ED2FFE1180D76022",
            "Plafond de niveau",
            "Empêche l'EXP de combat de dépasser le niveau choisi.",
            "Taux d'EXP",
            "Règle l'EXP de combat de 0% à 500% du montant normal.",
            "Partage d'EXP",
            "Permet aux Pokémon éligibles hors combat de recevoir de l'EXP.",
            "Désactivé",
            "Activé",
            "N."),
        new(
            "German",
            "0793BB71D9D11359A499A2E5F37C8393880010E91AA0D68C775C4FC5EED3922E",
            "Level-Begrenzung",
            "Verhindert, dass Kampf-EP Pokémon über den gewählten Level bringen.",
            "EP-Rate",
            "Legt Kampf-EP auf 0% bis 500% des normalen Werts fest.",
            "EP-Teiler",
            "Legt fest, ob berechtigte Pokémon ohne Kampfeinsatz EP erhalten.",
            "Aus",
            "Ein",
            "Lv."),
        new(
            "Italian",
            "F401865C07DD223D1F83C2C9A64D916B639A4E015E8984058B9FFBB7CE9D9AD9",
            "Limite livello",
            "Impedisce ai PE lotta di superare il livello scelto.",
            "Tasso esperienza",
            "Imposta i PE lotta dallo 0% al 500% del valore normale.",
            "Condivisione esperienza",
            "Decide se i Pokémon idonei che non lottano ricevono PE.",
            "Disattivato",
            "Attivato",
            "Lv."),
        new(
            "JPN",
            "470B9F3DD648A120513FBAEB437B50CB67263705456A99A452E879447D2FFBE9",
            "レベル上限",
            "バトルで得る経験値で 選んだレベルをこえないようにします。",
            "経験値倍率",
            "バトルで得る経験値を 0%から500%に設定します。",
            "経験値シェア",
            "バトルに参加していない対象のポケモンが 経験値を受け取るか設定します。",
            "オフ",
            "オン",
            "Lv."),
        new(
            "JPN_KANJI",
            "470B9F3DD648A120513FBAEB437B50CB67263705456A99A452E879447D2FFBE9",
            "レベル上限",
            "バトルで得る経験値で 選んだレベルをこえないようにします。",
            "経験値倍率",
            "バトルで得る経験値を 0%から500%に設定します。",
            "経験値シェア",
            "バトルに参加していない対象のポケモンが 経験値を受け取るか設定します。",
            "オフ",
            "オン",
            "Lv."),
        new(
            "Korean",
            "91313FA21072F5EEB4906D7C9F3DAAD8015B08E5E6C3E609B57B7C0BB5A8E18B",
            "레벨 상한",
            "배틀 경험치로 선택한 레벨을 넘지 않도록 합니다.",
            "경험치 배율",
            "배틀 경험치를 기본의 0%부터 500%까지 설정합니다.",
            "경험치 공유",
            "배틀에 참가하지 않은 대상 포켓몬도 경험치를 받을지 설정합니다.",
            "끄기",
            "켜기",
            "Lv."),
        new(
            "Simp_Chinese",
            "CE187E9196A73958AD938CA236EDC2D850298393033ABB6302C1E455DEBF26BE",
            "等级上限",
            "防止对战经验值使宝可梦超过所选等级。",
            "经验值倍率",
            "将对战经验值设为通常的0%至500%。",
            "经验值共享",
            "设置未参加对战的符合条件的宝可梦是否获得经验值。",
            "关闭",
            "开启",
            "等级"),
        new(
            "Trad_Chinese",
            "17397B7A716526998A824FA7DFBB953D6B93FD0C9AD2CFBE2BD0FDFBB0B33268",
            "等級上限",
            "防止對戰經驗值使寶可夢超過所選等級。",
            "經驗值倍率",
            "將對戰經驗值設為通常的0%至500%。",
            "經驗值共享",
            "設定未參加對戰的符合條件的寶可夢是否獲得經驗值。",
            "關閉",
            "開啟",
            "等級"),
    ];

    private static readonly IReadOnlyList<string> RequiredLanguages =
        Array.AsReadOnly(Locales.Select(locale => locale.Language).ToArray());
    private static readonly MessageDefinition[] Messages = CreateMessages();

    /// <summary>
    /// Produces deterministic, normalized <c>romfs/...</c> components from the
    /// exact S/V 4.0.0 files in <see cref="ProjectPaths.BaseRomFsPath"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Build(
        ProjectPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.SelectedGame is not ProjectGame.Scarlet and not ProjectGame.Violet)
        {
            throw new InvalidDataException(
                "Stock S/V gameplay Options require a Scarlet or Violet project.");
        }
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Stock S/V gameplay Options require the selected project's base RomFS.");
        }

        using var source = new BaseRomFsSource(paths, cancellationToken);
        var baseMain = source.Read(MainScriptPath, MaximumMainScriptBytes);
        EnsureHash(
            MainScriptPath,
            baseMain,
            SvGameplayOptionsLuaTransformer.VanillaSourceSha256);
        var transformedMain =
            SvGameplayOptionsLuaTransformer.TransformVanillaSource(baseMain);

        var originalSources = new List<CoordinatedGameTextSource>(Locales.Length);
        foreach (var locale in Locales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataPath = DataPath(locale.Language);
            var tablePath = TablePath(locale.Language);
            var data = source.Read(dataPath, MaximumMessageSourceBytes);
            var table = source.Read(tablePath, MaximumMessageSourceBytes);
            EnsureHash(dataPath, data, locale.DataSha256);
            EnsureHash(tablePath, table, CommonTableSha256);
            originalSources.Add(new CoordinatedGameTextSource(
                locale.Language,
                data,
                table));
        }

        IReadOnlyList<CoordinatedGameTextSource> current = originalSources;
        foreach (var message in Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputs = CoordinatedGameTextAuthoring.Insert(
                current,
                RequiredLanguages,
                message.Key,
                message.TextByLanguage,
                GameTextNullLineEncoding.PayloadCountTwo);
            current = outputs
                .Select(output => new CoordinatedGameTextSource(
                    output.Language,
                    output.Data,
                    output.Keys))
                .ToArray();
        }

        VerifyExactMessageInverse(originalSources, current, cancellationToken);
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [MainScriptPath] = transformedMain,
        };
        foreach (var messageSource in current)
        {
            result.Add(DataPath(messageSource.Language), messageSource.Data);
            result.Add(TablePath(messageSource.Language), messageSource.Keys);
        }

        if (result.Count != 1 + (Locales.Length * 2)
            || result.Keys.Any(path =>
                !path.StartsWith("romfs/", StringComparison.Ordinal)
                || path.Contains('\\')
                || path.Contains("//", StringComparison.Ordinal)
                || path.Split('/').Any(segment => segment is "." or "..")))
        {
            throw new InvalidDataException(
                "The stock S/V gameplay Options artifact inventory is not canonical.");
        }

        return result;
    }

    /// <summary>
    /// Convenience overload for a loose, already decompressed base RomFS. A
    /// packed Trinity source should use the <see cref="ProjectPaths"/> overload
    /// so its configured compression support directory is retained.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Build(
        string baseRomFsRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        return Build(
            new ProjectPaths(
                BaseRomFsPath: baseRomFsRoot,
                BaseExeFsPath: null,
                OutputRootPath: null,
                SaveFilePath: null,
                ScarletVioletSupportFolderPath: null,
                SelectedGame: ProjectGame.Scarlet),
            cancellationToken);
    }

    private static void VerifyExactMessageInverse(
        IReadOnlyList<CoordinatedGameTextSource> originals,
        IReadOnlyList<CoordinatedGameTextSource> transformed,
        CancellationToken cancellationToken)
    {
        var restored = transformed;
        for (var index = Messages.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputs = CoordinatedGameTextAuthoring.Delete(
                restored,
                RequiredLanguages,
                Messages[index].Key,
                GameTextNullLineEncoding.PayloadCountTwo);
            restored = outputs
                .Select(output => new CoordinatedGameTextSource(
                    output.Language,
                    output.Data,
                    output.Keys))
                .ToArray();
        }

        if (restored.Count != originals.Count)
        {
            throw new InvalidDataException(
                "The stock S/V gameplay Options message transform did not invert exactly.");
        }
        for (var index = 0; index < originals.Count; index++)
        {
            var original = originals[index];
            var candidate = restored[index];
            if (!string.Equals(original.Language, candidate.Language, StringComparison.Ordinal)
                || !original.Data.AsSpan().SequenceEqual(candidate.Data)
                || !original.Keys.AsSpan().SequenceEqual(candidate.Keys))
            {
                throw new InvalidDataException(
                    "The stock S/V gameplay Options message transform did not restore its exact base bytes.");
            }
        }
    }

    private static MessageDefinition[] CreateMessages()
    {
        var result = new List<MessageDefinition>(160);
        AddMessage(result, "km_ui_gameplay_level_cap_name", locale => locale.CapName);
        AddMessage(result, "km_ui_gameplay_level_cap_info", locale => locale.CapInfo);
        AddMessage(result, "km_ui_gameplay_level_cap_000", locale => locale.Off);
        for (var level = 1; level <= 100; level++)
        {
            var selectedLevel = level;
            AddMessage(
                result,
                "km_ui_gameplay_level_cap_" + FormatIndex(selectedLevel),
                locale => locale.LevelPrefix
                    + selectedLevel.ToString(CultureInfo.InvariantCulture));
        }

        AddMessage(result, "km_ui_gameplay_experience_rate_name", locale => locale.RateName);
        AddMessage(result, "km_ui_gameplay_experience_rate_info", locale => locale.RateInfo);
        for (var index = 0; index <= 50; index++)
        {
            var percentage = checked(index * 10);
            AddMessage(
                result,
                "km_ui_gameplay_experience_rate_" + FormatIndex(index),
                _ => percentage.ToString(CultureInfo.InvariantCulture) + "%");
        }

        AddMessage(result, "km_ui_gameplay_experience_share_name", locale => locale.ShareName);
        AddMessage(result, "km_ui_gameplay_experience_share_info", locale => locale.ShareInfo);
        AddMessage(result, "km_ui_gameplay_experience_share_000", locale => locale.On);
        AddMessage(result, "km_ui_gameplay_experience_share_001", locale => locale.Off);
        if (result.Count != 160
            || result.Select(message => message.Key).Distinct(StringComparer.Ordinal).Count()
                != result.Count)
        {
            throw new InvalidOperationException(
                "The stock S/V gameplay Options message inventory is invalid.");
        }

        return result.ToArray();
    }

    private static void AddMessage(
        ICollection<MessageDefinition> messages,
        string key,
        Func<Locale, string> valueSelector)
    {
        var values = Locales.ToDictionary(
            locale => locale.Language,
            valueSelector,
            StringComparer.Ordinal);
        messages.Add(new MessageDefinition(key, values));
    }

    private static string FormatIndex(int value) =>
        value.ToString("D3", CultureInfo.InvariantCulture);

    private static string DataPath(string language) =>
        $"romfs/message/dat/{language}/common/option.dat";

    private static string TablePath(string language) =>
        $"romfs/message/dat/{language}/common/option.tbl";

    private static void EnsureHash(
        string relativePath,
        ReadOnlySpan<byte> bytes,
        string expectedSha256)
    {
        var expected = Convert.FromHexString(expectedSha256);
        Span<byte> actual = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, actual);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidDataException(
                $"Base source '{relativePath}' is not the exact supported S/V 4.0.0 file.");
        }
    }

    private sealed class BaseRomFsSource : IDisposable
    {
        private readonly ProjectPaths paths;
        private readonly CancellationToken cancellationToken;
        private SvTrinityArchive? archive;

        internal BaseRomFsSource(
            ProjectPaths paths,
            CancellationToken cancellationToken)
        {
            this.paths = paths;
            this.cancellationToken = cancellationToken;
        }

        internal byte[] Read(string normalizedPath, int maximumBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var virtualPath = normalizedPath["romfs/".Length..];
            foreach (var candidate in LooseCandidates(paths.BaseRomFsPath!, virtualPath))
            {
                if (File.Exists(candidate))
                {
                    return ReadBounded(candidate, maximumBytes, cancellationToken);
                }
            }

            archive ??= SvTrinityArchive.Open(
                paths.BaseRomFsPath!,
                paths.ScarletVioletSupportFolderPath,
                maximumIndexBytes: MaximumTrinityIndexBytes,
                maximumPackBytes: MaximumTrinityPackBytes);
            var result = archive.ReadFile(virtualPath, maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public void Dispose()
        {
            archive?.Dispose();
        }

        private static IEnumerable<string> LooseCandidates(
            string baseRomFsPath,
            string virtualPath)
        {
            var platformPath = virtualPath.Replace('/', Path.DirectorySeparatorChar);
            yield return Path.Combine(baseRomFsPath, platformPath);
            yield return Path.Combine(baseRomFsPath, "romfs", platformPath);
        }

        private static byte[] ReadBounded(
            string path,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length < 0 || stream.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "A base S/V gameplay Options source exceeds its bounded size.");
            }

            var bytes = new byte[checked((int)stream.Length)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = stream.Read(
                    bytes,
                    offset,
                    Math.Min(128 * 1024, bytes.Length - offset));
                if (count == 0)
                {
                    throw new EndOfStreamException(
                        "A base S/V gameplay Options source was truncated while it was read.");
                }
                offset += count;
            }
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "A base S/V gameplay Options source changed while it was read.");
            }
            return bytes;
        }
    }

    private sealed record MessageDefinition(
        string Key,
        IReadOnlyDictionary<string, string> TextByLanguage);

    private sealed record Locale(
        string Language,
        string DataSha256,
        string CapName,
        string CapInfo,
        string RateName,
        string RateInfo,
        string ShareName,
        string ShareInfo,
        string Off,
        string On,
        string LevelPrefix);
}
