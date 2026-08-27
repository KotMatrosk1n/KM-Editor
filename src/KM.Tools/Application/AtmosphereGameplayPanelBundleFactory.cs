// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Projects;
using KM.Core.RuntimeSettings;
using KM.Core.Semantics;

namespace KM.Tools.Application;

/// <summary>
/// Builds KM-owned, exact-build gameplay panels for compatible in-game cheat managers.
/// The generated title layer contains only independently-authored Atmosphere VM writes;
/// it does not include an external manager or runtime binary.
/// </summary>
public static class AtmosphereGameplayPanelBundleFactory
{
    public const string DeliveryId = "atmosphere-gameplay-panel-v1";

    private static readonly GameplayBundleVersion PackageVersion = new(2, 5, 0);
    private static readonly GameplaySettingPresence SettingsPresence =
        GameplaySettingPresence.ExperienceShare
        | GameplaySettingPresence.ExperienceRate
        | GameplaySettingPresence.LevelCap;
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly GameplayPanelPatchProfile ScarletVioletProfile = new(
        [
            new(0x01141BA0, 0x72001C1F),
            new(0x0178143C, 0x9472FA27),
            new(0x01781E30, 0x1472F798),
            new(0x0343FC90, 0xB9400028),
            new(0x0343FC94, 0x340001E8),
            new(0x0343FC98, 0x5284E209),
            new(0x0343FC9C, 0x34000169),
            new(0x0343FCA0, 0x9BA97D08),
            new(0x0343FCA4, 0x5284E209),
            new(0x0343FCA8, 0x9AC90908),
            new(0x0343FCAC, 0xB5000068),
            new(0x0343FCB0, 0x52800028),
            new(0x0343FCB4, 0x14000006),
            new(0x0343FCB8, 0xD360FD0A),
            new(0x0343FCBC, 0xB400008A),
            new(0x0343FCC0, 0x12800008),
            new(0x0343FCC4, 0x14000002),
            new(0x0343FCC8, 0x2A1F03E8),
            new(0x0343FCCC, 0xB9000028),
            new(0x0343FCD0, 0xA9427BFD),
            new(0x0343FCD4, 0x178D0858),
            new(0x0343FCD8, 0xD101C3FF),
            new(0x0343FCDC, 0xA9007BFD),
            new(0x0343FCE0, 0xA90153F3),
            new(0x0343FCE4, 0xA9025BF5),
            new(0x0343FCE8, 0xA90363F7),
            new(0x0343FCEC, 0xA9046BF9),
            new(0x0343FCF0, 0xA90573FB),
            new(0x0343FCF4, 0xF9400A95),
            new(0x0343FCF8, 0xAA0003F3),
            new(0x0343FCFC, 0xAA0103F4),
            new(0x0343FD00, 0x52800C96),
            new(0x0343FD04, 0xAA1303E0),
            new(0x0343FD08, 0xAA1403E1),
            new(0x0343FD0C, 0x978D05E0),
            new(0x0343FD10, 0x710192DF),
            new(0x0343FD14, 0x540004E0),
            new(0x0343FD18, 0xB40004D5),
            new(0x0343FD1C, 0x3940C2B7),
            new(0x0343FD20, 0x71001AFF),
            new(0x0343FD24, 0x528000C8),
            new(0x0343FD28, 0x1A8892F7),
            new(0x0343FD2C, 0xAA1F03F8),
            new(0x0343FD30, 0x6B17031F),
            new(0x0343FD34, 0x540003E2),
            new(0x0343FD38, 0xF8787AB9),
            new(0x0343FD3C, 0xB4000379),
            new(0x0343FD40, 0x900085E8),
            new(0x0343FD44, 0x9132C108),
            new(0x0343FD48, 0xF9400329),
            new(0x0343FD4C, 0xEB08013F),
            new(0x0343FD50, 0x540002C1),
            new(0x0343FD54, 0x794E233A),
            new(0x0343FD58, 0x794E333B),
            new(0x0343FD5C, 0xAA1903E0),
            new(0x0343FD60, 0x97E3EFBC),
            new(0x0343FD64, 0x2A0003FC),
            new(0x0343FD68, 0x2A1A03E0),
            new(0x0343FD6C, 0x2A1B03E1),
            new(0x0343FD70, 0x110006C2),
            new(0x0343FD74, 0x975D1898),
            new(0x0343FD78, 0x5100041A),
            new(0x0343FD7C, 0x6B1C035A),
            new(0x0343FD80, 0x1A9F235A),
            new(0x0343FD84, 0x8B181279),
            new(0x0343FD88, 0x8B181289),
            new(0x0343FD8C, 0xB9400328),
            new(0x0343FD90, 0xB940012A),
            new(0x0343FD94, 0x4B0A0108),
            new(0x0343FD98, 0x8B2A4108),
            new(0x0343FD9C, 0xEB1A011F),
            new(0x0343FDA0, 0x9A9A9108),
            new(0x0343FDA4, 0xB9000328),
            new(0x0343FDA8, 0x91000718),
            new(0x0343FDAC, 0x17FFFFE1),
            new(0x0343FDB0, 0xA94573FB),
            new(0x0343FDB4, 0xA9446BF9),
            new(0x0343FDB8, 0xA94363F7),
            new(0x0343FDBC, 0xA9425BF5),
            new(0x0343FDC0, 0xA94153F3),
            new(0x0343FDC4, 0xA9407BFD),
            new(0x0343FDC8, 0x9101C3FF),
            new(0x0343FDCC, 0xD65F03C0),
            new(0x0343FDD0, 0xD503201F),
            new(0x0343FDD4, 0xD503201F),
        ],
        [new(0x01141BA0, 0x72001FFF)],
        [new(0x0343FC98, 0x52800009)],
        [new(0x0343FD00, 0x52800016)]);

    private static readonly GameplayPanelPatchProfile SwordShieldProfile = new(
        [
            new(0x007FB2C0, 0x320003E0),
            new(0x0083A3E0, 0x14019EF9),
            new(0x008A0294, 0xB94003E8),
            new(0x008A0298, 0x2A0803EA),
            new(0x008A029C, 0x1400034E),
            new(0x008A0FD4, 0x5284E209),
            new(0x008A0FD8, 0x9BA97D08),
            new(0x008A0FDC, 0x140000CE),
            new(0x008A1314, 0x5284E209),
            new(0x008A1318, 0x9AC90908),
            new(0x008A131C, 0x14000086),
            new(0x008A1534, 0x529FFFE9),
            new(0x008A1538, 0x72BFFFE9),
            new(0x008A153C, 0x14000002),
            new(0x008A1544, 0xEB09011F),
            new(0x008A1548, 0x9A899108),
            new(0x008A154C, 0x1400017A),
            new(0x008A1B34, 0x7100011F),
            new(0x008A1B38, 0x52800029),
            new(0x008A1B3C, 0x1400005A),
            new(0x008A1CA4, 0x1A891108),
            new(0x008A1CA8, 0x7100015F),
            new(0x008A1CAC, 0x1400003E),
            new(0x008A1DA4, 0x1A8803E8),
            new(0x008A1DA8, 0xD65F03C0),
            new(0x008A1DAC, 0xD503201F),
            new(0x008A1FC4, 0xA9BD7BFD),
            new(0x008A1FC8, 0xA90153F3),
            new(0x008A1FCC, 0x140000AA),
            new(0x008A2274, 0xA9025BF5),
            new(0x008A2278, 0xAA0003F3),
            new(0x008A227C, 0x1400005A),
            new(0x008A23E4, 0x2A0103F4),
            new(0x008A23E8, 0x2A0203F5),
            new(0x008A23EC, 0x1400005A),
            new(0x008A2554, 0x52800C88),
            new(0x008A2558, 0xD503201F),
            new(0x008A255C, 0x140000DE),
            new(0x008A28D4, 0x7101911F),
            new(0x008A28D8, 0x540079E0),
            new(0x008A28DC, 0x1400007A),
            new(0x008A2AC4, 0xF9401260),
            new(0x008A2AC8, 0xF9400800),
            new(0x008A2ACC, 0x14000086),
            new(0x008A2CE4, 0xF9400400),
            new(0x008A2CE8, 0x8B340C08),
            new(0x008A2CEC, 0x1400006E),
            new(0x008A2EA4, 0xF9409116),
            new(0x008A2EA8, 0x7940E2C0),
            new(0x008A2EAC, 0x14000002),
            new(0x008A2EB4, 0x394CF6C1),
            new(0x008A2EB8, 0x52800C82),
            new(0x008A2EBC, 0x140000F6),
            new(0x008A3294, 0x97FD2483),
            new(0x008A3298, 0x51000400),
            new(0x008A329C, 0x1400005A),
            new(0x008A3404, 0xB9406EC8),
            new(0x008A3408, 0x6B080009),
            new(0x008A340C, 0x14000022),
            new(0x008A3494, 0x1A8993E9),
            new(0x008A3498, 0x6B0902BF),
            new(0x008A349C, 0x140000B2),
            new(0x008A3764, 0x1A8992B5),
            new(0x008A3768, 0xD503201F),
            new(0x008A376C, 0x1400002A),
            new(0x008A3814, 0xAA1303E0),
            new(0x008A3818, 0x2A1403E1),
            new(0x008A381C, 0x14000026),
            new(0x008A38B4, 0x2A1503E2),
            new(0x008A38B8, 0xA9425BF5),
            new(0x008A38BC, 0x1400004E),
            new(0x008A39F4, 0xA94153F3),
            new(0x008A39F8, 0xA8C37BFD),
            new(0x008A39FC, 0x140001B2),
            new(0x008A40C4, 0xF81D0FF5),
            new(0x008A40C8, 0x17FE58C7),
            new(0x008A40CC, 0xD503201F),
            new(0x008A564C, 0x97FFEB12),
        ],
        [new(0x007FB2C0, 0x2A1F03E0)],
        [new(0x008A0FD4, 0x52800009)],
        [new(0x008A2554, 0x52800008), new(0x008A2EB8, 0x52800002)]);

    private static readonly GameplayPanelPatchProfile LegendsZaProfile = new(
        [
            new(0x009A3FF4, 0x9400029F),
            new(0x009A4174, 0x5284E210),
            new(0x009A4178, 0x9BB07EF0),
            new(0x009A417C, 0x949EFF83),
            new(0x00D735C0, 0x97F0C52C),
            new(0x00D73744, 0x5284E210),
            new(0x00D73748, 0x9BB07EF0),
            new(0x00D7374C, 0x948FC20F),
            new(0x03163F70, 0xD503201F),
            new(0x03163F74, 0xD503201F),
            new(0x03163F78, 0xD503201F),
            new(0x03163F7C, 0xD503201F),
            new(0x03163F80, 0xD503201F),
            new(0x03163F84, 0xD503201F),
            new(0x03163F88, 0xA9BE5BFE),
            new(0x03163F8C, 0x5284E208),
            new(0x03163F90, 0x9AC80A10),
            new(0x03163F94, 0x710002FF),
            new(0x03163F98, 0xFA401A00),
            new(0x03163F9C, 0x9A9F1610),
            new(0x03163FA0, 0xAA1003F6),
            new(0x03163FA4, 0xF9403A60),
            new(0x03163FA8, 0x97739666),
            new(0x03163FAC, 0xB90013E0),
            new(0x03163FB0, 0xF9403A60),
            new(0x03163FB4, 0x9773A477),
            new(0x03163FB8, 0x2A0003E1),
            new(0x03163FBC, 0xB94013E0),
            new(0x03163FC0, 0x52800C82),
            new(0x03163FC4, 0x976E22F2),
            new(0x03163FC8, 0x51000408),
            new(0x03163FCC, 0xB90013E8),
            new(0x03163FD0, 0xF9403A60),
            new(0x03163FD4, 0x977397E3),
            new(0x03163FD8, 0xB94013E8),
            new(0x03163FDC, 0x6B000108),
            new(0x03163FE0, 0x1A9F8108),
            new(0x03163FE4, 0xB9401AA9),
            new(0x03163FE8, 0x8B0902D6),
            new(0x03163FEC, 0xEB0802DF),
            new(0x03163FF0, 0x1A8892C8),
            new(0x03163FF4, 0xB9001AA8),
            new(0x03163FF8, 0xA8C25BFE),
            new(0x03163FFC, 0xD65F03C0),
        ],
        [
            new(0x009A3FF4, 0x949EFFDF),
            new(0x00D735C0, 0x948FC26C),
            new(0x03163F70, 0xA9BF0BFE),
            new(0x03163F74, 0x976102BF),
            new(0x03163F78, 0xA8C10BFE),
            new(0x03163F7C, 0x7100005F),
            new(0x03163F80, 0x1A8003E0),
            new(0x03163F84, 0xD65F03C0),
        ],
        [new(0x009A4174, 0x52800010), new(0x00D73744, 0x52800010)],
        [new(0x03163FC0, 0x52800002)]);

    public static (InGameSettingsBundleCatalog Catalog, GameplaySettingsBundleAuthority Authority)
        CreateProductionCatalog()
    {
        var entries = new[]
        {
            CreateEntry(
                ProjectGame.Sword,
                new GameplayBundleVersion(1, 3, 2),
                "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000",
                SwordShieldProfile),
            CreateEntry(
                ProjectGame.Shield,
                new GameplayBundleVersion(1, 3, 2),
                "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000",
                SwordShieldProfile),
            CreateEntry(
                ProjectGame.Scarlet,
                new GameplayBundleVersion(4, 0, 0),
                "421C5411B487EB4D049DD065FEC9547773E8E598000000000000000000000000",
                ScarletVioletProfile),
            CreateEntry(
                ProjectGame.Violet,
                new GameplayBundleVersion(4, 0, 0),
                "709BFD66115298640155FCC4979DBA151C7CC79A000000000000000000000000",
                ScarletVioletProfile),
            CreateEntry(
                ProjectGame.ZA,
                new GameplayBundleVersion(2, 0, 2),
                "B1F12FD919EAE86AB8A978317677E64BCE443D1F000000000000000000000000",
                LegendsZaProfile),
        };
        return (
            InGameSettingsBundleCatalog.Create(entries),
            GameplaySettingsBundleAuthority.AllowOnly(entries.Select(entry => entry.AuthorityKey)));
    }

    public static byte[] BuildCheatFile(GameplayPanelPatchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var builder = new StringBuilder();
        AppendSection(builder, "KM Gameplay Panel Support", profile.MasterWrites, master: true);
        AppendSection(builder, "KM Experience Share Off", profile.ExperienceShareDisabledWrites);
        for (var basisPoints = 0u; basisPoints <= 50_000; basisPoints += 1_000)
        {
            AppendSection(
                builder,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"KM Experience Rate {basisPoints / 100}%"),
                profile.ExperienceRateWrites.Select(write =>
                    write.WithImmediate(basisPoints)));
        }

        AppendSection(
            builder,
            "KM Supported EXP Level Cap Off",
            profile.LevelCapWrites.Select(write => write.WithImmediate(100)));
        foreach (var level in Enumerable.Range(1, 10)
                     .Concat(Enumerable.Range(3, 17).Select(index => index * 5)))
        {
            AppendSection(
                builder,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"KM Supported EXP Level Cap {level}"),
                profile.LevelCapWrites.Select(write => write.WithImmediate((uint)level)));
        }

        var bytes = Utf8.GetBytes(builder.ToString());
        ValidateCheatFile(bytes);
        return bytes;
    }

    public static byte[] BuildCheatFile(ProjectGame game)
    {
        return BuildCheatFile(game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => SwordShieldProfile,
            ProjectGame.Scarlet or ProjectGame.Violet => ScarletVioletProfile,
            ProjectGame.ZA => LegendsZaProfile,
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        });
    }

    public static byte[] BuildToggleFile()
    {
        return AtmosphereCheatToggleDocument.Create(
            EnumerateToggleNames().Select(name => new KeyValuePair<string, bool>(name, false)));
    }

    private static InGameSettingsBundleCatalogEntry CreateEntry(
        ProjectGame game,
        GameplayBundleVersion updateVersion,
        string buildId,
        GameplayPanelPatchProfile profile)
    {
        var gameInfo = ProjectGameMetadata.Get(game);
        var family = game switch
        {
            ProjectGame.Sword or ProjectGame.Shield => GameFamily.SwordShield,
            ProjectGame.Scarlet or ProjectGame.Violet => GameFamily.ScarletViolet,
            ProjectGame.ZA => GameFamily.LegendsZA,
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
        EnsureExactBuildBetaControlsAuthorized(
            GameplayBundleDeploymentPlanner.ToSettingsFamily(family));
        var cheatBytes = BuildCheatFile(profile);
        var toggleBytes = BuildToggleFile();
        var cheatSha256 = GameplayBundleIdentity.ComputeSha256(cheatBytes);
        var toggleSha256 = GameplayBundleIdentity.ComputeSha256(toggleBytes);
        var cheatPath = string.Create(
            CultureInfo.InvariantCulture,
            $"atmosphere/contents/{gameInfo.TitleId:X16}/cheats/{buildId[..16]}.txt");
        var togglePath = string.Create(
            CultureInfo.InvariantCulture,
            $"atmosphere/contents/{gameInfo.TitleId:X16}/cheats/toggles.txt");
        var sourceRevision = Convert.ToHexString(
            SHA1.HashData(Utf8.GetBytes(DeliveryId)));
        var identity = GameplayBundleIdentity.SerializeIdentityPreimage(
            gameInfo.TitleId,
            updateVersion,
            buildId,
            PackageVersion,
            sourceRevision,
            cheatSha256,
            [
                new GameplayBundleSemanticComponent(cheatPath, cheatSha256),
                new GameplayBundleSemanticComponent(togglePath, toggleSha256),
            ]);
        var bundleId = GameplayBundleIdentity.CreateBundleId(identity);
        var manifest = new GameplayBundleManifest(
            gameInfo.TitleId,
            updateVersion,
            buildId,
            GameplayBundleIdentity.BundleAbi,
            bundleId,
            GameplayBundleIdentity.SettingsSchema,
            PackageVersion,
            [
                new GameplayBundleOutputComponent(
                    cheatPath,
                    checked((ulong)cheatBytes.LongLength),
                    cheatSha256),
                new GameplayBundleOutputComponent(
                    togglePath,
                    checked((ulong)toggleBytes.LongLength),
                    toggleSha256),
            ]);
        var journal = GameplaySettingsJournal.CreateBootstrap(
            GameplayBundleDeploymentPlanner.ToSettingsFamily(family),
            gameInfo.TitleId,
            new GameplaySettingsWriterVersion(
                checked((ushort)PackageVersion.Major),
                checked((ushort)PackageVersion.Minor),
                checked((ushort)PackageVersion.Patch)),
            SettingsPresence);
        var archive = GameplayBundleArchive.Build(
            manifest,
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [cheatPath] = cheatBytes,
                [togglePath] = toggleBytes,
            },
            GameplayBundleDeploymentPlanner.ToSettingsFamily(family),
            journal);
        return new InGameSettingsBundleCatalogEntry(
            family,
            archive.Bytes,
            isCurrent: true);
    }

    private static void EnsureExactBuildBetaControlsAuthorized(
        GameplaySettingsFamily family)
    {
        var expected = new[]
        {
            GameplayRuntimeControlId.ExperienceShare,
            GameplayRuntimeControlId.ExperienceRate,
            GameplayRuntimeControlId.LevelCap,
        };
        if (expected.Any(id => !GameplayRuntimeControlCatalog
                .Get(id)
                .CanExposeExactBuildBetaControl(family)))
        {
            throw new InvalidOperationException(
                "The exact-build gameplay panel is not authorized by the beta control catalog.");
        }
    }

    private static void AppendSection(
        StringBuilder builder,
        string name,
        IEnumerable<GameplayPanelMemoryWrite> writes,
        bool master = false)
    {
        builder.Append(master ? '{' : '[')
            .Append(name)
            .Append(master ? '}' : ']')
            .Append('\n');
        foreach (var write in writes)
        {
            builder.Append("04000000 ")
                .Append(write.Offset.ToString("X8", CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(write.Value.ToString("X8", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        builder.Append('\n');
    }

    private static IEnumerable<string> EnumerateToggleNames()
    {
        yield return "KM Experience Share Off";
        for (var basisPoints = 0u; basisPoints <= 50_000; basisPoints += 1_000)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"KM Experience Rate {basisPoints / 100}%");
        }

        yield return "KM Supported EXP Level Cap Off";
        foreach (var level in Enumerable.Range(1, 10)
                     .Concat(Enumerable.Range(3, 17).Select(index => index * 5)))
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"KM Supported EXP Level Cap {level}");
        }
    }

    private static void ValidateCheatFile(ReadOnlySpan<byte> bytes)
    {
        var text = Utf8.GetString(bytes);
        if (text.Length == 0
            || text[^1] != '\n'
            || text.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidDataException("The gameplay panel text envelope is not canonical.");
        }

        var sectionCount = 0;
        var masterCount = 0;
        var sectionWordCount = 0;
        var totalWordCount = 0;
        var toggleNames = new List<string>();
        string? masterName = null;
        foreach (var rawLine in text.Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                continue;
            }

            if ((rawLine[0] == '{' && rawLine[^1] == '}')
                || (rawLine[0] == '[' && rawLine[^1] == ']'))
            {
                if (sectionWordCount > 0x100)
                {
                    throw new InvalidDataException(
                        "A gameplay panel section exceeds the runtime opcode limit.");
                }

                sectionCount++;
                masterCount += rawLine[0] == '{' ? 1 : 0;
                if (rawLine[0] == '[')
                {
                    toggleNames.Add(rawLine[1..^1]);
                }
                else
                {
                    masterName = rawLine[1..^1];
                }

                sectionWordCount = 0;
                continue;
            }

            var words = rawLine.Split(' ', StringSplitOptions.None);
            if (sectionCount == 0
                || words.Length != 3
                || words[0] != "04000000"
                || words[1].Length != 8
                || words[2].Length != 8
                || !uint.TryParse(
                    words[1],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var offset)
                || !uint.TryParse(
                    words[2],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out _)
                || (offset & 3) != 0)
            {
                throw new InvalidDataException(
                    "A gameplay panel opcode line is malformed or unaligned.");
            }

            sectionWordCount += 3;
            totalWordCount += 3;
        }

        if (sectionCount is < 1 or > 0x80
            || masterCount != 1
            || !string.Equals(masterName, "KM Gameplay Panel Support", StringComparison.Ordinal)
            || sectionWordCount > 0x100
            || totalWordCount > 0x400
            || !toggleNames.SequenceEqual(EnumerateToggleNames(), StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The gameplay panel section inventory exceeds the runtime limits.");
        }
    }
}

public sealed record GameplayPanelPatchProfile(
    IReadOnlyList<GameplayPanelMemoryWrite> MasterWrites,
    IReadOnlyList<GameplayPanelMemoryWrite> ExperienceShareDisabledWrites,
    IReadOnlyList<GameplayPanelMemoryWrite> ExperienceRateWrites,
    IReadOnlyList<GameplayPanelMemoryWrite> LevelCapWrites)
{
    public void Validate()
    {
        ValidateWrites(MasterWrites, nameof(MasterWrites), maximumCount: 0x100);
        ValidateWrites(ExperienceShareDisabledWrites, nameof(ExperienceShareDisabledWrites));
        ValidateWrites(ExperienceRateWrites, nameof(ExperienceRateWrites));
        ValidateWrites(LevelCapWrites, nameof(LevelCapWrites));
        if (ExperienceRateWrites.Any(write => !write.IsMovzImmediate32)
            || LevelCapWrites.Any(write => !write.IsMovzImmediate32))
        {
            throw new InvalidDataException(
                "A gameplay panel value binding is not a 32-bit MOVZ immediate instruction.");
        }

        var master = MasterWrites.ToDictionary(write => write.Offset);
        if (ExperienceShareDisabledWrites.Any(write =>
                !master.TryGetValue(write.Offset, out var defaultWrite)
                || defaultWrite.Value == write.Value)
            || ExperienceRateWrites.Any(write =>
                !master.TryGetValue(write.Offset, out var defaultWrite)
                || defaultWrite.Immediate != 10_000)
            || LevelCapWrites.Any(write =>
                !master.TryGetValue(write.Offset, out var defaultWrite)
                || defaultWrite.Immediate != 100))
        {
            throw new InvalidDataException(
                "The gameplay panel master section does not restore every control to retail behavior.");
        }
    }

    private static void ValidateWrites(
        IReadOnlyList<GameplayPanelMemoryWrite> writes,
        string parameterName,
        int maximumCount = 0x100)
    {
        ArgumentNullException.ThrowIfNull(writes);
        if (writes.Count is < 1 || writes.Count > maximumCount
            || writes.Any(write => (write.Offset & 3) != 0)
            || writes.Select(write => write.Offset).Distinct().Count() != writes.Count)
        {
            throw new ArgumentException(
                "A gameplay panel write inventory is empty, unaligned, duplicated, or out of bounds.",
                parameterName);
        }
    }
}

public readonly record struct GameplayPanelMemoryWrite(uint Offset, uint Value)
{
    private const uint MovzOpcodeMask = 0xFFE00000;
    private const uint MovzPreservedBitsMask = 0xFFE0001F;
    private const uint MovzImmediate32 = 0x52800000;

    public bool IsMovzImmediate32 => (Value & MovzOpcodeMask) == MovzImmediate32;

    public uint? Immediate => IsMovzImmediate32 ? (Value >> 5) & 0xFFFF : null;

    public GameplayPanelMemoryWrite WithImmediate(uint value)
    {
        if (!IsMovzImmediate32 || value > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return this with { Value = (Value & MovzPreservedBitsMask) | (value << 5) };
    }
}
