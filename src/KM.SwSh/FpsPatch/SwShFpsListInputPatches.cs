// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SwSh.FpsPatch;

internal static class SwShFpsListInputPatches
{
    // The list had a second held movement path independent of shared input repeat.
    // Finish each requested scroll once, then dispatch queued presses and repeats
    // through the original bounds, cursor and wrap logic. A fresh press is consumed
    // when queued so the input adapter cannot enqueue it again as a repeat.
    //
    // The former held direction at list +0x840 holds up to sixteen nonzero nibbles.
    // Values 1..4 are up, down, left, right; 5..8 retain repeat mode. The oldest
    // event is the highest occupied nibble. Overflow does not overwrite earlier
    // taps. The constructor initializes this storage; inactive, blocked and new
    // list states clear it. Scroll start preserves pending events.
    //
    // Helpers occupy unreachable portions of the replaced scroll continuation.
    // No allocation, global input state, segment growth or object growth is needed.
    // Only the verified ordinary list state uses the queue. Other state types retain
    // their native dispatch. All spans use edition specific expected bytes.

    private static readonly SwShFpsMainPatcher.MainPatch[] Sword =
    [
        new(0x15C1DF8,
            Convert.FromHexString(
                "1F0500718116005475121C91A80240B91F0D0071800B0054280C003568425C39E80B003558000014680647B91F050071" +
                "C00000541F0D007121010054684A48B90805001105000014684A48B91F05007161000054E8031F32684A08B9604E43F9" +
                "0A1D009480130037684A48B91F090071E1020054684E43F900D541BD60AA43F90200381E011440B934150094F403002A" +
                "605A40F9080040F9082940F900013FD668AA43F99F02006B820C0054694E43F9"),
            Convert.FromHexString(
                "09B843F9290140F9691500B42A0140F94A2D40F9AB2D02105F010BEBC11400542A6940B96A0000351F2004F9A2000014" +
                "0C2044F96C0000B55F0D0071C01300542A2C40290EA843F9CF2940B9CA0000342F010035CF2140B9FF090071CB120054" +
                "050000148F000034CF2540B9FF0900712B1200548DFD7CD38D0100B57F0100712D008052ADD58D1A8A0000346D008052" +
                "5F010071ADD58D1A4E040051AD090E0BAC110CAA0C2004F920008052C0035FD6"),
            "Queued list input"),
        new(0x15C1EC0,
            Convert.FromHexString(
                "62000014684248B9690A47B975121C911F01096BA1040054684648B9690E47B91F01096B21040054684E43F900D541BD" +
                "01A541BD0020211E8503005401A141BD0020211E2D03005468E647BD60AA43F974022191E10314AA5A1400940000221E" +
                "69EA47BD60AA43F9E10314AA0809201E541400940000221E6AEE47BD60AA43F9E10314AA2909201E4E1400940000221E" +
                "604E43F94209201E61F247B9"),
            Convert.FromHexString(
                "F47BBDA9E80B00F9F40300AAA92702101F0109EB01040054896A40B9C90300343F0D0071610A00546A2244F92A0A00B4" +
                "4B11C0DA8C0780D26B0D7E928B010BCB4C25CB9AED0180D2AD21CB9A4A012D8A6A2204F98C050051827D025342040011" +
                "8C0500128A0100124A791F534A050051EB031F2A9F011F7249118B1A4A018B1AE92B0429E00313AAE183009158000094" +
                "380000147F2204F936000014"),
            "List input dispatch"),
        new(0x15C2030,
            Convert.FromHexString("090D40B9091100B968AA43F9000D40BD609A43F9"),
            Convert.FromHexString("E00314AAE80B40F900013FD6F47BC3A8C0035FD6"),
            "List input dispatch return"),
        new(0x15C2048,
            Convert.FromHexString("8A12009474AA43F97F4A08B9E00314AA82064229BD140094E103002AE00314AAE2140094E00700F9E1230091"),
            Convert.FromHexString("09805E39E900003409084239A9000034090C42396900003409E047B9490000341F2004F9FFC300D181F8FF17"),
            "Discard pending input when the list is inactive"),
        new(0x15C20AC,
            Convert.FromHexString("68AA43F9081140B9"),
            Convert.FromHexString("F50F1DF841FAFF17"),
            "List input entry trampoline"),
        new(0x15C09B0,
            Convert.FromHexString("F50F1DF8"),
            Convert.FromHexString("12050014"),
            "List input queue hook"),
        new(0x15C0270,
            Convert.FromHexString("FFC300D1"),
            Convert.FromHexString("76070014"),
            "List queue lifecycle hook"),
        new(0x15C02CC,
            Convert.FromHexString("00013FD6"),
            Convert.FromHexString("FD060094"),
            "List update queue hook"),
        new(0x15C1D38,
            Convert.FromHexString("682204F9"),
            Convert.FromHexString("1F2003D5"),
            "Keep pending list input on scroll start"),
        new(0x15C1DF4,
            Convert.FromHexString("68010034"),
            Convert.FromHexString("68000014"),
            "Finish one scroll per accepted input"),
    ];

    private static readonly SwShFpsMainPatcher.MainPatch[] Shield =
    [
        new(0x15C1E88,
            Convert.FromHexString(
                "1F0500718116005475121C91A80240B91F0D0071800B0054280C003568425C39E80B003558000014680647B91F050071" +
                "C00000541F0D007121010054684A48B90805001105000014684A48B91F05007161000054E8031F32684A08B9604E43F9" +
                "0A1D009480130037684A48B91F090071E1020054684E43F900D541BD60AA43F90200381E011440B934150094F403002A" +
                "605A40F9080040F9082940F900013FD668AA43F99F02006B820C0054694E43F9"),
            Convert.FromHexString(
                "09B843F9290140F9691500B42A0140F94A2D40F9AB2D02105F010BEBC11400542A6940B96A0000351F2004F9A2000014" +
                "0C2044F96C0000B55F0D0071C01300542A2C40290EA843F9CF2940B9CA0000342F010035CF2140B9FF090071CB120054" +
                "050000148F000034CF2540B9FF0900712B1200548DFD7CD38D0100B57F0100712D008052ADD58D1A8A0000346D008052" +
                "5F010071ADD58D1A4E040051AD090E0BAC110CAA0C2004F920008052C0035FD6"),
            "Queued list input"),
        new(0x15C1F50,
            Convert.FromHexString(
                "62000014684248B9690A47B975121C911F01096BA1040054684648B9690E47B91F01096B21040054684E43F900D541BD" +
                "01A541BD0020211E8503005401A141BD0020211E2D03005468E647BD60AA43F974022191E10314AA5A1400940000221E" +
                "69EA47BD60AA43F9E10314AA0809201E541400940000221E6AEE47BD60AA43F9E10314AA2909201E4E1400940000221E" +
                "604E43F94209201E61F247B9"),
            Convert.FromHexString(
                "F47BBDA9E80B00F9F40300AAA92702101F0109EB01040054896A40B9C90300343F0D0071610A00546A2244F92A0A00B4" +
                "4B11C0DA8C0780D26B0D7E928B010BCB4C25CB9AED0180D2AD21CB9A4A012D8A6A2204F98C050051827D025342040011" +
                "8C0500128A0100124A791F534A050051EB031F2A9F011F7249118B1A4A018B1AE92B0429E00313AAE183009158000094" +
                "380000147F2204F936000014"),
            "List input dispatch"),
        new(0x15C20C0,
            Convert.FromHexString("090D40B9091100B968AA43F9000D40BD609A43F9"),
            Convert.FromHexString("E00314AAE80B40F900013FD6F47BC3A8C0035FD6"),
            "List input dispatch return"),
        new(0x15C20D8,
            Convert.FromHexString("8A12009474AA43F97F4A08B9E00314AA82064229BD140094E103002AE00314AAE2140094E00700F9E1230091"),
            Convert.FromHexString("09805E39E900003409084239A9000034090C42396900003409E047B9490000341F2004F9FFC300D181F8FF17"),
            "Discard pending input when the list is inactive"),
        new(0x15C213C,
            Convert.FromHexString("68AA43F9081140B9"),
            Convert.FromHexString("F50F1DF841FAFF17"),
            "List input entry trampoline"),
        new(0x15C0A40,
            Convert.FromHexString("F50F1DF8"),
            Convert.FromHexString("12050014"),
            "List input queue hook"),
        new(0x15C0300,
            Convert.FromHexString("FFC300D1"),
            Convert.FromHexString("76070014"),
            "List queue lifecycle hook"),
        new(0x15C035C,
            Convert.FromHexString("00013FD6"),
            Convert.FromHexString("FD060094"),
            "List update queue hook"),
        new(0x15C1DC8,
            Convert.FromHexString("682204F9"),
            Convert.FromHexString("1F2003D5"),
            "Keep pending list input on scroll start"),
        new(0x15C1E84,
            Convert.FromHexString("68010034"),
            Convert.FromHexString("68000014"),
            "Finish one scroll per accepted input"),
    ];

    public static IReadOnlyList<SwShFpsMainPatcher.MainPatch> ForGame(ProjectGame game) =>
        game == ProjectGame.Sword ? Sword : Shield;
}
