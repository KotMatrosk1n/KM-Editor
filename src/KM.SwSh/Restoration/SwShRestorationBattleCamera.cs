// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.Json;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public static class SwShRestorationBattleCamera
{
    public static SwShBattleCameraAdjustment? For(SwShPokemonIdentity identity) => identity switch
    {
        { Species: 990, Form: 0 } => new(new(990, 0, 0, 0), -1, -1),
        { Species: 983, Form: 0 } => new(new(983, 0, 0, 0), -1, -1),
        { Species: 985, Form: 0 } => new(new(985, 0, 0, 0), -1, -1),
        { Species: 993, Form: 0 } => new(new(993, 0, 0, 0), -1, -1),
        { Species: 1005, Form: 0 } => new(new(1005, 0, 0, 0), -1, -1),
        { Species: 1007, Form: 0 } => new(new(1007, 0, 0, 0), -1, -1),
        _ => null,
    };

    public static string Key(SwShBattleCameraIdentity identity) => string.Create(CultureInfo.InvariantCulture,
        $"{identity.Species}:{identity.Form}:{identity.Sex}:{identity.Giant}");

    public static SwShRestorationContribution Create(SwShBattleCameraAdjustment adjustment, byte[] native)
    {
        SwShBattleCameraTable.Validate(adjustment);
        if (new SwShBattleCameraTable(native).Read(adjustment.Identity) is not null)
            throw new InvalidDataException("Battle camera adjustment already exists in the source.");
        return new(SwShRestorationComposer.BattleCameraPath, SwShRestorationWriter.BattleCamera,
            Key(adjustment.Identity), null, JsonSerializer.SerializeToUtf8Bytes(adjustment));
    }

    internal static SwShBattleCameraAdjustment Decode(byte[] bytes, string key)
    {
        if (bytes.Length > 4096) throw new InvalidDataException("Battle camera contribution exceeds its limit.");
        var adjustment = JsonSerializer.Deserialize<SwShBattleCameraAdjustment>(bytes)
            ?? throw new InvalidDataException("Battle camera contribution is missing.");
        SwShBattleCameraTable.Validate(adjustment);
        if (Key(adjustment.Identity) != key || !SwShRestorationDefinitions.All.Any(d =>
            d.Identities.Contains(new(adjustment.Identity.Species, adjustment.Identity.Form))))
            throw new InvalidDataException("Battle camera contribution identity is invalid.");
        return adjustment;
    }
}
