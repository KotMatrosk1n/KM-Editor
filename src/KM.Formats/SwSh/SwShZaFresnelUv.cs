// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Formats.SwSh;

/// <summary>Source ZA gem coordinates with explicitly selected UV sets.</summary>
internal static class SwShZaFresnelUv
{
    // Inputs: N64..66, T67..69, handedness70, UV0 72/73, UV1 76/77,
    // world position80..82; REQUIRED camera C3[76..78] in matching units.
    // REQUIRED owned scalar62..65=UVScaleOffset,66..69=UVScaleOffset1,
    // 70/71=ParallaxUVIntensity XY. No transform or selector defaults.
    // Outputs83..92: base, normal0, normal1, AO, secondary color UV pairs.
    // Scratch93..111. Allocate112; native predicates/condition flags intact.
    // Sampling, attribute binding and source selector resolution are separate.
    internal static (ulong Instruction, ulong Control)[] Create(int layerUv, int aoUv)
    {
        ulong[] words = (layerUv, aoUv) switch
        {
            (-1, -1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276263UL, // Source fragment0 instruction 35.
                0x5080000000576364UL, // Source fragment0 instruction 37.
                0x5C68100005D74563UL, // Source fragment0 instruction 39.
                0x5C68100005D74365UL, // Source fragment0 instruction 40.
                0x5C68100005E74067UL, // Source fragment0 instruction 41.
                0x5C68100005D74468UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E74169UL, // Source fragment0 instruction 44.
                0x5C6810000677635EUL, // Source fragment0 instruction 45.
                0x5C6810000647606AUL, // Source fragment0 instruction 46.
                0x5C68100005D76860UL, // Source fragment0 instruction 47.
                0x5C68100006475F6BUL, // Source fragment0 instruction 48.
                0x5C6810000697655FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7656CUL, // Source fragment0 instruction 50.
                0x5C6810000647625EUL, // Source fragment0 instruction 51.
                0x5C68100006A76962UL, // Source fragment0 instruction 52.
                0x59A2300006976364UL, // Source fragment0 instruction 53.
                0x59A22F8006776860UL, // Source fragment0 instruction 54.
                0x5C68100004676C5FUL, // Source fragment0 instruction 55.
                0x5C68100006A76869UL, // Source fragment0 instruction 56.
                0x59A0310006B76768UL, // Source fragment0 instruction 57.
                0x5C68100004676462UL, // Source fragment0 instruction 58.
                0x5C68100004676064UL, // Source fragment0 instruction 59.
                0x5C68100006A75F60UL, // Source fragment0 instruction 60.
                0x59A0348006B7655FUL, // Source fragment0 instruction 61.
                0x59A0340005E75D65UL, // Source fragment0 instruction 62.
                0x508000000047655DUL, // Source fragment0 instruction 63.
                0x59A0300006B76265UL, // Source fragment0 instruction 64.
                0x59A02F8005E76360UL, // Source fragment0 instruction 65.
                0x59A0328005E7645FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x4C68101404275F62UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078006170055UL, // Explicit owned uniform, constant or output.
                0x5C98078006670056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078006170059UL, // Explicit owned uniform, constant or output.
                0x5C9807800667005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800627005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (-1, 0) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074962UL, // Source fragment0 instruction 82.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078006170055UL, // Explicit owned uniform, constant or output.
                0x5C98078006670056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004870059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (-1, 1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074D62UL, // Source fragment0 instruction 82.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078006170055UL, // Explicit owned uniform, constant or output.
                0x5C98078006670056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (0, -1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074962UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004870055UL, // Explicit owned uniform, constant or output.
                0x5C98078006270056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078006170059UL, // Explicit owned uniform, constant or output.
                0x5C9807800667005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (0, 0) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074962UL, // Source fragment0 instruction 82.
                0x3859103F80074964UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004870055UL, // Explicit owned uniform, constant or output.
                0x5C98078006470056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004870059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (0, 1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074D62UL, // Source fragment0 instruction 82.
                0x3859103F80074964UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004870055UL, // Explicit owned uniform, constant or output.
                0x5C98078006470056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (1, -1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074D62UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70055UL, // Explicit owned uniform, constant or output.
                0x5C98078006270056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078006170059UL, // Explicit owned uniform, constant or output.
                0x5C9807800667005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (1, 0) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074962UL, // Source fragment0 instruction 82.
                0x3859103F80074D64UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70055UL, // Explicit owned uniform, constant or output.
                0x5C98078006470056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004870059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            (1, 1) =>
            [
                0x5C6810000437435DUL, // Source fragment0 instruction 18.
                0x5C6810000407405EUL, // Source fragment0 instruction 19.
                0x4C59100C04C7505FUL, // Source fragment0 instruction 20.
                0x4C59100C04D75160UL, // Source fragment0 instruction 21.
                0x59A02E8004474461UL, // Source fragment0 instruction 22.
                0x59A02F000417415DUL, // Source fragment0 instruction 23.
                0x5C68100005F75F5EUL, // Source fragment0 instruction 24.
                0x4C59100C04E75262UL, // Source fragment0 instruction 25.
                0x4C58301404074863UL, // Source fragment0 instruction 26.
                0x4C58301404174964UL, // Source fragment0 instruction 27.
                0x59A0308004574565UL, // Source fragment0 instruction 28.
                0x59A02E8004274261UL, // Source fragment0 instruction 29.
                0x508000000057655DUL, // Source fragment0 instruction 30.
                0x59A02F0006076065UL, // Source fragment0 instruction 31.
                0x508000000057615EUL, // Source fragment0 instruction 32.
                0x4C68101403E76361UL, // Source fragment0 instruction 33.
                0x0103F8000007F063UL, // Explicit owned uniform, constant or output.
                0x49A1319403F76466UL, // Source fragment0 instruction 34.
                0x59A0328006276264UL, // Source fragment0 instruction 35.
                0x5080000000576465UL, // Source fragment0 instruction 37.
                0x5C68100005D74564UL, // Source fragment0 instruction 39.
                0x5C68100005D74367UL, // Source fragment0 instruction 40.
                0x5C68100005E74068UL, // Source fragment0 instruction 41.
                0x5C68100005D74469UL, // Source fragment0 instruction 42.
                0x5C68100005E7425DUL, // Source fragment0 instruction 43.
                0x5C68100005E7416AUL, // Source fragment0 instruction 44.
                0x5C6810000687645EUL, // Source fragment0 instruction 45.
                0x5C6810000657606BUL, // Source fragment0 instruction 46.
                0x5C68100005D76960UL, // Source fragment0 instruction 47.
                0x5C68100006575F6CUL, // Source fragment0 instruction 48.
                0x5C68100006A7675FUL, // Source fragment0 instruction 49.
                0x59A22F0005D7676DUL, // Source fragment0 instruction 50.
                0x5C6810000657625EUL, // Source fragment0 instruction 51.
                0x5C68100006B76A62UL, // Source fragment0 instruction 52.
                0x59A2300006A76465UL, // Source fragment0 instruction 53.
                0x59A22F8006876960UL, // Source fragment0 instruction 54.
                0x5C68100004676D5FUL, // Source fragment0 instruction 55.
                0x5C68100006B7696AUL, // Source fragment0 instruction 56.
                0x59A0310006C76869UL, // Source fragment0 instruction 57.
                0x5C68100004676562UL, // Source fragment0 instruction 58.
                0x5C68100004676065UL, // Source fragment0 instruction 59.
                0x5C68100006B75F60UL, // Source fragment0 instruction 60.
                0x59A0350006C7675FUL, // Source fragment0 instruction 61.
                0x59A0348005E75D67UL, // Source fragment0 instruction 62.
                0x508000000047675DUL, // Source fragment0 instruction 63.
                0x59A0300006C76267UL, // Source fragment0 instruction 64.
                0x59A02F8005E76460UL, // Source fragment0 instruction 65.
                0x59A0338005E7655FUL, // Source fragment0 instruction 66.
                0x5C68120005D7605EUL, // Source fragment0 instruction 67.
                0x5C68100005D75F60UL, // Source fragment0 instruction 68.
                0x49A0309404675E5DUL, // Source fragment0 instruction 69.
                0x3968103F0007605EUL, // Source fragment0 instruction 70.
                0x4C58301404475D5FUL, // Source fragment0 instruction 72.
                0x49A0331404775E60UL, // Source fragment0 instruction 73.
                0x4C5830140457605EUL, // Source fragment0 instruction 75.
                0x3859103F80074D62UL, // Source fragment0 instruction 82.
                0x3859103F80074D64UL, // Source fragment0 instruction 89.
                0x4C68101404275F63UL, // Source fragment0 instruction 91.
                0x4C68101404375E5FUL, // Source fragment0 instruction 96.
                0x5C98078005D70053UL, // Explicit owned uniform, constant or output.
                0x5C98078006070054UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70055UL, // Explicit owned uniform, constant or output.
                0x5C98078006470056UL, // Explicit owned uniform, constant or output.
                0x5C98078006170057UL, // Explicit owned uniform, constant or output.
                0x5C98078006670058UL, // Explicit owned uniform, constant or output.
                0x5C98078004C70059UL, // Explicit owned uniform, constant or output.
                0x5C9807800627005AUL, // Explicit owned uniform, constant or output.
                0x5C9807800637005BUL, // Explicit owned uniform, constant or output.
                0x5C98078005F7005CUL, // Explicit owned uniform, constant or output.
            ],
            _ => throw new InvalidDataException("ZA gem UV selectors require another source adapter.")
        };
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }

    // Call after Create: masked variants6/8 require this explicit selector.
    // Their source role is MaskedEmissionMap (enum14/TEX36).
    // Outputs mask UV93/94; no additional scratch. Negative selects transformed base UV
    // already in87/88, zero selects raw UV0, one selects raw UV1.
    internal static (ulong Instruction, ulong Control)[] EmissionMask(int maskUv)
    {
        ulong[] words = maskUv switch
        {
            -1 => [0x5c9807800577005dUL, 0x5c9807800587005eUL],
            0 => [0x5c9807800487005dUL, 0x3859103f8007495eUL],
            1 => [0x5c98078004c7005dUL, 0x3859103f80074d5eUL],
            _ => throw new InvalidDataException("ZA gem emission UV selector requires another source adapter.")
        };
        return words.Select(w => (w, SwShShaderScheduling.Serial(w))).ToArray();
    }
}
