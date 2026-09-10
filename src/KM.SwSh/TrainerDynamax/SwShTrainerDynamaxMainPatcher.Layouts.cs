// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.SwSh.TrainerDynamax;

internal static partial class SwShTrainerDynamaxMainPatcher
{
    // Exact Sword/Shield 1.3.2 ARM64 layouts. Each helper span holds two logical
    // instructions followed by a branch to the next verified padding span.
    // Entry hooks: permission 0x8A3A00, companion 0x8A3C80, execution 0x8A3550.
    // Wrappers save X0-X2/LR, classify the actor, then either return a denial or
    // restore arguments and replay SUB SP, SP, #0x60 before returning to entry+4.
    // Policy accepts context types 1/2 and formats 0/1. The local client is at
    // engine+0x174; opponent sides differ in client bit zero. Same-side allies
    // bypass denial. Mode bits zero/one disable player/opponents respectively.
    // Execution maps battler ranges 0..5/6..11/12..17/18..23 to clients 0/2/1/3.
    // Other contexts, formats and invalid actor IDs retain the original path.
    private static readonly Layout[] Layouts =
    [
        new(ProjectGame.Sword, "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000", 0x8B7C48,
        [
            new(0x8B0374, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a956030014")),
            new(0x8B10D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e90300aaea03022a86010014")),
            new(0x8B16F4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a4150094ef230035ae000014")),
            new(0x8B19B4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a806000014")),
            new(0x8B19D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d10bc8ff1766000014")),
            new(0x8B1B74, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a80e040014")),
            new(0x8B2BB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd6a2020014")),
            new(0x8B3644, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a9ba010014")),
            new(0x8B3D34, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e90300aaea03022a1a000014")),
            new(0x8B3DA4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("f80b0094ef4f00350a020014")),
            new(0x8B45D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a85e000014")),
            new(0x8B4754, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d14bbdff1712000014")),
            new(0x8B47A4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a82e010014")),
            new(0x8B4C64, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd612000014")),
            new(0x8B4CB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a996020014")),
            new(0x8B5714, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("090440f94a00403902000014")),
            new(0x8B5724, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f610071624f005402000014")),
            new(0x8B5734, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f3100716201005402000014")),
            new(0x8B5744, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f190071ea379f1a02000014")),
            new(0x8B5754, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("4a791f531b01001402000014")),
            new(0x8B5764, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f490071ea379f1a6e000014")),
            new(0x8B5924, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("4a791f534a050011a6000014")),
            new(0x8B5BC4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("700400946f6c003552010014")),
            new(0x8B6114, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a8ae010014")),
            new(0x8B67D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d15fb3ff175e000014")),
            new(0x8B6954, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a8d6000014")),
            new(0x8B6CB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd632000014")),
            new(0x8B6D84, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ef031f2a09b900b416000014")),
            new(0x8B6DE4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2b4540f90bb600b40e010014")),
            new(0x8B7224, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("6c0140b98c0500512a010014")),
            new(0x8B76D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("9f050071886e005406000014")),
            new(0x8B76F4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2d5141b9bf05007136000014")),
            new(0x8B77D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a86600542ed1453902000014")),
            new(0x8B77E4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f0d00710866005406000014")),
            new(0x8B7804, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("df0d00710865005406000014")),
            new(0x8B7824, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2d2100355f050071f2000014")),
            new(0x8B7BF4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a8450054df05007112000014")),
            new(0x8B7C44, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("28430054710080524a000014")),
            new(0x8B7D74, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f010e6b603900541a000014")),
            new(0x8B7DE4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("50010e4a103600369a010014")),
            new(0x8B8454, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2f060153c0035fd612000014")),
            new(0x8B84A4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2f020012c0035fd600000000")),
            new(0x8A3A00, Convert.FromHexString("ff8301d1"), Convert.FromHexString("5d320014")),
            new(0x8A3C80, Convert.FromHexString("ff8301d1"), Convert.FromHexString("713e0014")),
            new(0x8A3550, Convert.FromHexString("ff8301d1"), Convert.FromHexString("d9450014")),
        ]),
        new(ProjectGame.Shield, "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000", 0x8B7C48,
        [
            new(0x8B0374, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a956030014")),
            new(0x8B10D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e90300aaea03022a86010014")),
            new(0x8B16F4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a4150094ef230035ae000014")),
            new(0x8B19B4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a806000014")),
            new(0x8B19D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d10bc8ff1766000014")),
            new(0x8B1B74, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a80e040014")),
            new(0x8B2BB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd6a2020014")),
            new(0x8B3644, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a9ba010014")),
            new(0x8B3D34, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e90300aaea03022a1a000014")),
            new(0x8B3DA4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("f80b0094ef4f00350a020014")),
            new(0x8B45D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a85e000014")),
            new(0x8B4754, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d14bbdff1712000014")),
            new(0x8B47A4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a82e010014")),
            new(0x8B4C64, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd612000014")),
            new(0x8B4CB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e07bbea9e10b01a996020014")),
            new(0x8B5714, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("090440f94a00403902000014")),
            new(0x8B5724, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f610071624f005402000014")),
            new(0x8B5734, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f3100716201005402000014")),
            new(0x8B5744, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f190071ea379f1a02000014")),
            new(0x8B5754, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("4a791f531b01001402000014")),
            new(0x8B5764, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f490071ea379f1a6e000014")),
            new(0x8B5924, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("4a791f534a050011a6000014")),
            new(0x8B5BC4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("700400946f6c003552010014")),
            new(0x8B6114, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a8ae010014")),
            new(0x8B67D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ff8301d15fb3ff175e000014")),
            new(0x8B6954, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e10b41a9e07bc2a8d6000014")),
            new(0x8B6CB4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("e0031f2ac0035fd632000014")),
            new(0x8B6D84, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("ef031f2a09b900b416000014")),
            new(0x8B6DE4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2b4540f90bb600b40e010014")),
            new(0x8B7224, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("6c0140b98c0500512a010014")),
            new(0x8B76D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("9f050071886e005406000014")),
            new(0x8B76F4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2d5141b9bf05007136000014")),
            new(0x8B77D4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a86600542ed1453902000014")),
            new(0x8B77E4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f0d00710866005406000014")),
            new(0x8B7804, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("df0d00710865005406000014")),
            new(0x8B7824, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2d2100355f050071f2000014")),
            new(0x8B7BF4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("a8450054df05007112000014")),
            new(0x8B7C44, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("28430054710080524a000014")),
            new(0x8B7D74, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("5f010e6b603900541a000014")),
            new(0x8B7DE4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("50010e4a103600369a010014")),
            new(0x8B8454, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2f060153c0035fd612000014")),
            new(0x8B84A4, Convert.FromHexString("000000000000000000000000"), Convert.FromHexString("2f020012c0035fd600000000")),
            new(0x8A3A00, Convert.FromHexString("ff8301d1"), Convert.FromHexString("5d320014")),
            new(0x8A3C80, Convert.FromHexString("ff8301d1"), Convert.FromHexString("713e0014")),
            new(0x8A3550, Convert.FromHexString("ff8301d1"), Convert.FromHexString("d9450014")),
        ]),
    ];
}
