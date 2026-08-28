// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.ZA;

namespace KM.ZA.RuntimeSettings;

/// <summary>
/// Derives the Z-A 2.0.2 stock Game Settings message layer from the selected
/// project's own Base RomFS. The retail UI archive is identity-checked but is
/// not redistributed or rewritten because the runtime reuses its row template.
/// </summary>
public static class ZaNativeGameplayMenuRomFsMaterializer
{
    public const string GameSettingsUiPath =
        "romfs/ui/data/option/option_gamesetting_00.arc";

    private const int MaximumUiBytes = 16 * 1024 * 1024;
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private const int MaximumTrinityIndexBytes = 64 * 1024 * 1024;
    private const long MaximumTrinityPackBytes = 512L * 1024L * 1024L;
    private const string GameSettingsUiSha256 =
        "64CA9FCCF4BF884910E4842F8D53B8E97209C9836FC4D17968359D2005BA9687";
    private const string CommonTableSha256 =
        "33CD1795B7DFABE1877EB2C3F256B722889244C5EE1423F9C8644FFBFAC825EB";

    private static readonly Locale[] Locales =
    [
        new(
            "English",
            "18930CF35358FD7AFEF07BD9FE97359C159608C669B4315B1D54E716A70D9EC5",
            "Experience Share",
            "Choose whether eligible Pokémon that did not battle receive EXP.",
            "Experience Rate",
            "Adjust battle EXP from 0% through 500% in 10% steps.",
            "Level Cap",
            "Prevent battle EXP from raising Pokémon beyond Off or the selected level from 1 to 100.",
            "Enabled",
            "Disabled",
            "Off"),
        new(
            "Spanish",
            "1555379CBA8B53BA0EB5C7F686B59A176A5CCEBB1A7AAB26C83FD5071E81E753",
            "Reparto de experiencia",
            "Decide si los Pokémon aptos que no combatan reciben experiencia.",
            "Porcentaje de experiencia",
            "Ajusta la experiencia de combate del 0% al 500% en pasos del 10%.",
            "Límite de nivel",
            "Impide que la experiencia de combate supere el nivel elegido del 1 al 100, o desactiva el límite.",
            "Activado",
            "Desactivado",
            "Sin límite"),
        new(
            "French",
            "F329264B0EC2536A9F4616BBBF5FBA5C7453596F36FD2C4E1DBD6DE6580C2EBD",
            "Partage d'EXP",
            "Permet aux Pokémon éligibles hors combat de recevoir de l'EXP.",
            "Taux d'EXP",
            "Règle l'EXP de combat de 0% à 500% par paliers de 10%.",
            "Plafond de niveau",
            "Empêche l'EXP de combat de dépasser le niveau choisi de 1 à 100, ou désactive le plafond.",
            "Activé",
            "Désactivé",
            "Aucun"),
        new(
            "German",
            "675FBA46BB574494A3C84CE21E863ACCC728D666C30C2598D703B493066B1E4D",
            "EP-Teiler",
            "Legt fest, ob berechtigte Pokémon ohne Kampfeinsatz EP erhalten.",
            "EP-Rate",
            "Legt Kampf-EP in 10%-Schritten auf 0% bis 500% fest.",
            "Level-Begrenzung",
            "Begrenzt Kampf-EP auf Level 1 bis 100 oder hebt die Begrenzung auf.",
            "Ein",
            "Aus",
            "Aus"),
        new(
            "Italian",
            "21EA3BD54DB32796B688CAFBB6F8E6BB2F172FA45A83BCC78B0327862DADE680",
            "Condivisione esperienza",
            "Decide se i Pokémon idonei che non lottano ricevono PE.",
            "Tasso esperienza",
            "Imposta i PE lotta dallo 0% al 500% in incrementi del 10%.",
            "Limite livello",
            "Impedisce ai PE lotta di superare il livello scelto da 1 a 100, oppure disattiva il limite.",
            "Attivato",
            "Disattivato",
            "Nessuno"),
        new(
            "JPN",
            "61622137640382DC118457D8FDC087BBD1D033B58DE62F2DB1C43F838EE7F43A",
            "経験値シェア",
            "バトルに参加していない対象のポケモンが 経験値を受け取るか設定します。",
            "経験値倍率",
            "バトルで得る経験値を 0%から500%まで 10%ずつ設定します。",
            "レベル上限",
            "バトルで得る経験値の上限を 1から100に設定するか 上限をなしにします。",
            "オン",
            "オフ",
            "なし"),
        new(
            "JPN_KANJI",
            "61622137640382DC118457D8FDC087BBD1D033B58DE62F2DB1C43F838EE7F43A",
            "経験値シェア",
            "バトルに参加していない対象のポケモンが 経験値を受け取るか設定します。",
            "経験値倍率",
            "バトルで得る経験値を 0%から500%まで 10%ずつ設定します。",
            "レベル上限",
            "バトルで得る経験値の上限を 1から100に設定するか 上限をなしにします。",
            "オン",
            "オフ",
            "なし"),
        new(
            "Korean",
            "52D6CFCD2CDA988ACA2D3FBB81EE8A80E6C350ECC789F3DE454E82BCEB9A1461",
            "경험치 공유",
            "배틀에 참가하지 않은 대상 포켓몬도 경험치를 받을지 설정합니다.",
            "경험치 배율",
            "배틀 경험치를 0%부터 500%까지 10% 단위로 설정합니다.",
            "레벨 상한",
            "배틀 경험치의 상한을 1부터 100으로 설정하거나 상한을 끕니다.",
            "켜기",
            "끄기",
            "없음"),
        new(
            "LATAM",
            "E3C57C41263D70362BDC8F54FDEE7E2AECCF8AE5DE8358CC1BCC2CD52532EECE",
            "Reparto de experiencia",
            "Decide si los Pokémon aptos que no combatan reciben experiencia.",
            "Porcentaje de experiencia",
            "Ajusta la experiencia de combate del 0% al 500% en pasos del 10%.",
            "Límite de nivel",
            "Impide que la experiencia de combate supere el nivel elegido del 1 al 100, o desactiva el límite.",
            "Activado",
            "Desactivado",
            "Sin límite"),
        new(
            "Simp_Chinese",
            "3648216CE167553984CD4E56D004E1100CDA7A45DBA4D13134CB615A9E7C463D",
            "经验值共享",
            "设置未参加对战的符合条件的宝可梦是否获得经验值。",
            "经验值倍率",
            "将对战经验值以10%为单位设为0%至500%。",
            "等级上限",
            "将对战经验值的等级上限设为1至100，或关闭上限。",
            "开启",
            "关闭",
            "无"),
        new(
            "Trad_Chinese",
            "6E00F4C5E3D7A437E9E027AE623C60A495348707DD31602A72FDBBFAFA87DDCF",
            "經驗值共享",
            "設定未參加對戰的符合條件的寶可夢是否獲得經驗值。",
            "經驗值倍率",
            "將對戰經驗值以10%為單位設為0%至500%。",
            "等級上限",
            "將對戰經驗值的等級上限設為1至100，或關閉上限。",
            "開啟",
            "關閉",
            "無"),
    ];

    private static readonly IReadOnlyList<string> RequiredLanguages =
        Array.AsReadOnly(Locales.Select(locale => locale.Language).ToArray());
    private static readonly MessageDefinition[] Messages = CreateMessages();

    /// <summary>
    /// Builds canonical <c>romfs/...</c> outputs from a loose or packed Base
    /// RomFS. Packed sources use the compression runtime already configured by
    /// the application environment.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Build(
        string baseRomFsRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        return BuildCore(
            baseRomFsRoot,
            compressionSupportFolderPath: null,
            cancellationToken);
    }

    /// <summary>
    /// Project overload which retains the configured Z-A compression support
    /// folder when the Base RomFS is a packed Trinity source.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Build(
        ProjectPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            throw new InvalidDataException(
                "Stock Z-A Game Settings require a Pokémon Legends Z-A project.");
        }
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidDataException(
                "Stock Z-A Game Settings require the selected project's Base RomFS.");
        }
        return BuildCore(
            paths.BaseRomFsPath,
            paths.PokemonLegendsZASupportFolderPath,
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, byte[]> BuildCore(
        string baseRomFsRoot,
        string? compressionSupportFolderPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new BaseRomFsSource(
            baseRomFsRoot,
            compressionSupportFolderPath,
            cancellationToken);

        var stockUi = source.Read(GameSettingsUiPath, MaximumUiBytes);
        EnsureHash(GameSettingsUiPath, stockUi, GameSettingsUiSha256);

        var originals = new List<CoordinatedGameTextSource>(Locales.Length);
        foreach (var locale in Locales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataPath = DataPath(locale.Language);
            var tablePath = TablePath(locale.Language);
            var data = source.Read(dataPath, MaximumMessageBytes);
            var table = source.Read(tablePath, MaximumMessageBytes);
            EnsureHash(dataPath, data, locale.DataSha256);
            EnsureHash(tablePath, table, CommonTableSha256);
            originals.Add(new CoordinatedGameTextSource(locale.Language, data, table));
        }

        IReadOnlyList<CoordinatedGameTextSource> current = originals;
        foreach (var message in Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = CoordinatedGameTextAuthoring.Insert(
                    current,
                    RequiredLanguages,
                    message.Key,
                    message.TextByLanguage,
                    GameTextNullLineEncoding.PayloadCountTwo)
                .Select(output => new CoordinatedGameTextSource(
                    output.Language,
                    output.Data,
                    output.Keys))
                .ToArray();
        }

        VerifyExactMessageInverse(originals, current, cancellationToken);
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var virtualPaths = new List<string>(Locales.Length * 2);
        foreach (var transformed in current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataPath = DataPath(transformed.Language);
            var tablePath = TablePath(transformed.Language);
            result.Add(dataPath, transformed.Data);
            result.Add(tablePath, transformed.Keys);
            virtualPaths.Add(dataPath["romfs/".Length..]);
            virtualPaths.Add(tablePath["romfs/".Length..]);
        }

        var descriptor = ZaTrinityDescriptorPatcher
            .CreateLayeredDescriptorFromVirtualPaths(
                baseRomFsRoot,
                virtualPaths,
                Array.Empty<string>());
        var repeatedDescriptor = ZaTrinityDescriptorPatcher
            .CreateLayeredDescriptorFromVirtualPaths(
                baseRomFsRoot,
                virtualPaths,
                Array.Empty<string>());
        if (!descriptor.AsSpan().SequenceEqual(repeatedDescriptor))
        {
            throw new InvalidDataException(
                "The Z-A layered Trinity descriptor transform is not deterministic.");
        }
        var baseDescriptor = ZaTrinityDescriptorPatcher.ReadBaseDescriptor(baseRomFsRoot);
        var emptyLayerDescriptor = ZaTrinityDescriptorPatcher
            .CreateLayeredDescriptorFromVirtualPaths(
                baseRomFsRoot,
                Array.Empty<string>(),
                Array.Empty<string>());
        if (!baseDescriptor.AsSpan().SequenceEqual(emptyLayerDescriptor)
            || baseDescriptor.AsSpan().SequenceEqual(descriptor))
        {
            throw new InvalidDataException(
                "The Z-A layered Trinity descriptor did not preserve its exact empty inverse.");
        }
        result.Add("romfs/arc/data.trpfd", descriptor);

        if (result.Count != 1 + (Locales.Length * 2)
            || result.Keys.Any(path =>
                !path.StartsWith("romfs/", StringComparison.Ordinal)
                || path.Contains('\\')
                || path.Contains("//", StringComparison.Ordinal)
                || path.Split('/').Any(segment => segment is "." or "..")))
        {
            throw new InvalidDataException(
                "The stock Z-A Game Settings artifact inventory is not canonical.");
        }
        return result;
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
            restored = CoordinatedGameTextAuthoring.Delete(
                    restored,
                    RequiredLanguages,
                    Messages[index].Key,
                    GameTextNullLineEncoding.PayloadCountTwo)
                .Select(output => new CoordinatedGameTextSource(
                    output.Language,
                    output.Data,
                    output.Keys))
                .ToArray();
        }

        if (restored.Count != originals.Count)
        {
            throw new InvalidDataException(
                "The stock Z-A Game Settings message transform did not invert exactly.");
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
                    "The stock Z-A Game Settings message transform did not restore its exact Base RomFS bytes.");
            }
        }
    }

    private static MessageDefinition[] CreateMessages()
    {
        var messages = new List<MessageDefinition>(160);
        AddMessage(messages, "km_exp_share", locale => locale.ShareName);
        AddMessage(messages, "km_exp_share_desc", locale => locale.ShareDescription);
        AddMessage(messages, "km_exp_rate", locale => locale.RateName);
        AddMessage(messages, "km_exp_rate_desc", locale => locale.RateDescription);
        AddMessage(messages, "km_level_cap", locale => locale.CapName);
        AddMessage(messages, "km_level_cap_desc", locale => locale.CapDescription);
        AddMessage(messages, "km_enabled", locale => locale.Enabled);
        AddMessage(messages, "km_disabled", locale => locale.Disabled);

        for (var index = 0; index <= 50; index++)
        {
            var selected = index;
            AddMessage(
                messages,
                "km_exp_rate_value_" + selected.ToString(CultureInfo.InvariantCulture),
                locale => locale.RateName + ": "
                    + checked(selected * 10).ToString(CultureInfo.InvariantCulture)
                    + "%");
        }
        AddMessage(
            messages,
            "km_level_cap_value_off",
            locale => locale.CapName + ": " + locale.Off);
        for (var level = 1; level <= 100; level++)
        {
            var selected = level;
            AddMessage(
                messages,
                "km_level_cap_value_" + selected.ToString(CultureInfo.InvariantCulture),
                locale => locale.CapName + ": "
                    + selected.ToString(CultureInfo.InvariantCulture));
        }

        if (messages.Count != 160
            || messages.Select(message => message.Key)
                .Distinct(StringComparer.Ordinal).Count() != messages.Count)
        {
            throw new InvalidOperationException(
                "The stock Z-A Game Settings message inventory is invalid.");
        }
        return messages.ToArray();
    }

    private static void AddMessage(
        ICollection<MessageDefinition> messages,
        string key,
        Func<Locale, string> valueSelector)
    {
        messages.Add(new MessageDefinition(
            key,
            Locales.ToDictionary(
                locale => locale.Language,
                valueSelector,
                StringComparer.Ordinal)));
    }

    private static string DataPath(string language) =>
        $"romfs/ik_message/dat/{language}/common/option.dat";

    private static string TablePath(string language) =>
        $"romfs/ik_message/dat/{language}/common/option.tbl";

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
                $"Base source '{relativePath}' is not the exact supported Z-A 2.0.2 file.");
        }
    }

    private sealed class BaseRomFsSource : IDisposable
    {
        private readonly string baseRomFsRoot;
        private readonly string? compressionSupportFolderPath;
        private readonly CancellationToken cancellationToken;
        private ZaTrinityArchive? archive;

        internal BaseRomFsSource(
            string baseRomFsRoot,
            string? compressionSupportFolderPath,
            CancellationToken cancellationToken)
        {
            this.baseRomFsRoot = baseRomFsRoot;
            this.compressionSupportFolderPath = compressionSupportFolderPath;
            this.cancellationToken = cancellationToken;
        }

        internal byte[] Read(string normalizedPath, int maximumBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var virtualPath = normalizedPath["romfs/".Length..];
            foreach (var candidate in LooseCandidates(baseRomFsRoot, virtualPath))
            {
                if (File.Exists(candidate))
                {
                    return ReadBounded(candidate, maximumBytes, cancellationToken);
                }
            }

            archive ??= ZaTrinityArchive.Open(
                baseRomFsRoot,
                compressionSupportFolderPath,
                maximumIndexBytes: MaximumTrinityIndexBytes,
                maximumPackBytes: MaximumTrinityPackBytes);
            var result = archive.ReadFile(virtualPath, maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public void Dispose() => archive?.Dispose();

        private static IEnumerable<string> LooseCandidates(
            string root,
            string virtualPath)
        {
            var platformPath = virtualPath.Replace('/', Path.DirectorySeparatorChar);
            yield return Path.Combine(root, platformPath);
            yield return Path.Combine(root, "romfs", platformPath);
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
                    "A Base Z-A Game Settings source exceeds its bounded size.");
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
                        "A Base Z-A Game Settings source was truncated while it was read.");
                }
                offset += count;
            }
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "A Base Z-A Game Settings source changed while it was read.");
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
        string ShareName,
        string ShareDescription,
        string RateName,
        string RateDescription,
        string CapName,
        string CapDescription,
        string Enabled,
        string Disabled,
        string Off);
}
