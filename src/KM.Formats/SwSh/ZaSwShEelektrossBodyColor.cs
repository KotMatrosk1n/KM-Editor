// SPDX-License-Identifier: GPL-3.0-only
// Generated only from the hash-bound Eelektross variant0 authored-material SSA.
using KM.Formats.ZA;

namespace KM.Formats.SwSh;

public static class ZaSwShEelektrossBodyColor
{
    public sealed record Parameter(int Buffer, int Word, string Property, int? Component);
    public sealed record Samples(float[] BaseColor, float[] LayerMask, float Layer5Mask, float Metallic, float Roughness, float Ao);
    public sealed record MufuCuts(float FresnelEx2, float NormalizationReciprocal, float C1Word0);
    public sealed record Cut(float[] Emission, float[] Complement, float ComplementWeight, float FresnelFactor,
        float Metallic, float Roughness, float AoOcclusion, float SssMask, float CoverageDenominator, float AoFactor);
    public sealed record Checkpoint(int SourceInstruction, int SourceRegister, int EmittedInstruction, int NativeRegister, string Name);
    public sealed record Kernel(ulong[] Instructions, Checkpoint[] Checkpoints);
    public static IReadOnlyList<Parameter> Inputs { get; } = Array.AsReadOnly(new Parameter[]
    {
        new(7, 12, "MetallicLayer5", null),
        new(7, 28, "RoughnessLayer5", null),
        new(7, 33, "EmissionIntensity", null),
        new(7, 34, "EmissionIntensityLayer1", null),
        new(7, 35, "EmissionIntensityLayer2", null),
        new(7, 36, "EmissionIntensityLayer3", null),
        new(7, 37, "EmissionIntensityLayer4", null),
        new(7, 38, "EmissionIntensityLayer5", null),
        new(7, 120, "FresnelAlphaMin", null),
        new(7, 121, "FresnelAlphaMax", null),
        new(7, 188, "SSSMaskOffset", null),
        new(7, 226, "FresnelAngleBias", null),
        new(7, 270, "FresnelPower", null),
        new(7, 271, "AOIntensity", null),
        new(7, 272, "AOIntensityFresnel", null),
        new(8, 4, "UVScaleOffset", 0),
        new(8, 5, "UVScaleOffset", 1),
        new(8, 6, "UVScaleOffset", 2),
        new(8, 7, "UVScaleOffset", 3),
        new(8, 40, "BaseColorLayer1", 0),
        new(8, 41, "BaseColorLayer1", 1),
        new(8, 42, "BaseColorLayer1", 2),
        new(8, 44, "BaseColorLayer2", 0),
        new(8, 45, "BaseColorLayer2", 1),
        new(8, 46, "BaseColorLayer2", 2),
        new(8, 48, "BaseColorLayer3", 0),
        new(8, 49, "BaseColorLayer3", 1),
        new(8, 50, "BaseColorLayer3", 2),
        new(8, 52, "BaseColorLayer4", 0),
        new(8, 53, "BaseColorLayer4", 1),
        new(8, 54, "BaseColorLayer4", 2),
        new(8, 56, "BaseColorLayer5", 0),
        new(8, 57, "BaseColorLayer5", 1),
        new(8, 58, "BaseColorLayer5", 2),
        new(8, 59, "BaseColorLayer5", 3),
        new(8, 60, "BaseColorLayer6", 0),
        new(8, 61, "BaseColorLayer6", 1),
        new(8, 62, "BaseColorLayer6", 2),
        new(8, 63, "BaseColorLayer6", 3),
        new(8, 64, "BaseColorLayer7", 0),
        new(8, 65, "BaseColorLayer7", 1),
        new(8, 66, "BaseColorLayer7", 2),
        new(8, 67, "BaseColorLayer7", 3),
        new(8, 68, "BaseColorLayer8", 0),
        new(8, 69, "BaseColorLayer8", 1),
        new(8, 70, "BaseColorLayer8", 2),
        new(8, 76, "EmissionColor", 0),
        new(8, 77, "EmissionColor", 1),
        new(8, 78, "EmissionColor", 2),
        new(8, 80, "EmissionColorLayer1", 0),
        new(8, 81, "EmissionColorLayer1", 1),
        new(8, 82, "EmissionColorLayer1", 2),
        new(8, 84, "EmissionColorLayer2", 0),
        new(8, 85, "EmissionColorLayer2", 1),
        new(8, 86, "EmissionColorLayer2", 2),
        new(8, 88, "EmissionColorLayer3", 0),
        new(8, 89, "EmissionColorLayer3", 1),
        new(8, 90, "EmissionColorLayer3", 2),
        new(8, 92, "EmissionColorLayer4", 0),
        new(8, 93, "EmissionColorLayer4", 1),
        new(8, 94, "EmissionColorLayer4", 2),
        new(8, 96, "EmissionColorLayer5", 0),
        new(8, 97, "EmissionColorLayer5", 1),
        new(8, 98, "EmissionColorLayer5", 2),
        new(8, 424, "BaseColorLayer9", 0),
        new(8, 425, "BaseColorLayer9", 1),
        new(8, 426, "BaseColorLayer9", 2),
    });
    public static int Index(int buffer, int word)
    {
        for (var i = 0; i < Inputs.Count; i++) if (Inputs[i].Buffer == buffer && Inputs[i].Word == word) return i;
        throw new InvalidDataException($"Unowned Eelektross material word C{buffer}[{word}].");
    }
    // The immutable previous 19 slots remain C5[52..70]. New source words append at71.
    public static int DefaultNativeScalarWord(int buffer, int word)
    {
        for (var i = 0; i < ZaSwShEelektrossMaterial.Inputs.Count; i++)
            if (ZaSwShEelektrossMaterial.Inputs[i].Buffer == buffer && ZaSwShEelektrossMaterial.Inputs[i].Word == word)
                return ZaSwShEelektrossMaterial.FirstScalar + i;
        var next = 71;
        foreach (var p in Inputs)
        {
            var old = ZaSwShEelektrossMaterial.Inputs.Any(x => x.Buffer == p.Buffer && x.Word == p.Word);
            if (old) continue;
            if (p.Buffer == buffer && p.Word == word) return next;
            next++;
        }
        throw new InvalidDataException("Unowned Eelektross native scalar mapping.");
    }
    // Source dot is the completed original view/normal producer, not an assumed UV or native normal.
    // Caller supplies source LG2/EX2 results; MathF.Pow is intentionally not substituted.
    public static float FresnelLog2Argument(ReadOnlySpan<float> values, float sourceViewDot, float c1Word0)
    {
        RequireValues(values); RequireFinite(sourceViewDot); RequireFinite(c1Word0);
        var clamped = MathF.Max(MathF.Min(sourceViewDot, 1f), c1Word0);
        var biased = clamped + (-values[Index(7, 226)]);
        var nonnegative = MathF.Max(0f, biased);
        return MathF.Abs((-nonnegative) + 1f);
    }
    // Source i121 result: the original RRO.EX2 i124 remains required before MUFU.EX2 i126.
    public static float FresnelExponentBeforeRro(ReadOnlySpan<float> values, float sourceLog2Result)
    {
        RequireValues(values); RequireFinite(sourceLog2Result);
        return sourceLog2Result * values[Index(7, 270)];
    }
    public static Cut Evaluate(ReadOnlySpan<float> values, Samples samples, MufuCuts cuts)
    {
        RequireValues(values);
        if (samples is null || cuts is null || samples.BaseColor is null || samples.BaseColor.Length != 3
            || samples.LayerMask is null || samples.LayerMask.Length != 4) throw new InvalidDataException("Eelektross sample ABI changed.");
        foreach (var value in samples.BaseColor) RequireFinite(value);
        foreach (var value in samples.LayerMask) RequireFinite(value);
        RequireFinite(samples.Layer5Mask); RequireFinite(samples.Metallic); RequireFinite(samples.Roughness); RequireFinite(samples.Ao);
        RequireFinite(cuts.FresnelEx2); RequireFinite(cuts.NormalizationReciprocal); RequireFinite(cuts.C1Word0);
        var owned = values.ToArray();
        float V(int buffer, int word) => owned[Index(buffer, word)];
        var previous = new float[ZaSwShEelektrossMaterial.Inputs.Count];
        for (var i = 0; i < previous.Length; i++) previous[i] = V(ZaSwShEelektrossMaterial.Inputs[i].Buffer, ZaSwShEelektrossMaterial.Inputs[i].Word);
        var prior = ZaSwShEelektrossShader.Evaluate(previous, samples.LayerMask);
        var si0r10 = Bits(0x3f800000U); // source i0 R10
        var si16r4 = samples.LayerMask[0]; // source i16 R4
        var si16r5 = samples.LayerMask[1]; // source i16 R5
        var si16r6 = samples.LayerMask[2]; // source i16 R6
        var si16r7 = samples.LayerMask[3]; // source i16 R7
        var si17r0 = samples.BaseColor[0]; // source i17 R0
        var si17r1 = samples.BaseColor[1]; // source i17 R1
        var si17r2 = samples.BaseColor[2]; // source i17 R2
        var si18r16 = samples.Layer5Mask; // source i18 R16
        var si19r15 = samples.Metallic; // source i19 R15
        var si20r9 = samples.Roughness; // source i20 R9
        var si21r11 = samples.Ao; // source i21 R11
        var si30r30 = V(7, 33); // source i30 R30
        var si98r24 = V(7, 120); // source i98 R24
        var si101r14 = V(7, 34); // source i101 R14
        var si116r17 = (si16r4 + si16r5); // source i116 R17
        var si118r23 = ((-si101r14) + Bits(0x3f800000U)); // source i118 R23
        var si119r17 = (si16r6 + si116r17); // source i119 R17
        var si120r23 = MathF.Max(0f, si118r23); // source i120 R23
        var si122r8 = ((-si98r24) + V(7, 121)); // source i122 R8
        var si123r17 = (si16r7 + si119r17); // source i123 R17
        var si125r21 = V(7, 35); // source i125 R21
        var si126r22 = cuts.FresnelEx2; // source i126 R22
        var si127r25 = Math.Clamp(((-si123r17) + Bits(0x3f800000U)), 0f, 1f); // source i127 R25
        var si131r24 = prior.EmissionWeighted[1]; // source i131 R24
        var si132r8 = MathF.FusedMultiplyAdd(si126r22, si122r8, V(7, 120)); // source i132 R8
        var si133r20 = prior.EmissionWeighted[0]; // source i133 R20
        var si135r22 = V(7, 36); // source i135 R22
        var si136r26 = (si0r10 + (-V(7, 33))); // source i136 R26
        var si137r29 = prior.EmissionWeighted[2]; // source i137 R29
        var si138r33 = MathF.FusedMultiplyAdd(si135r22, V(8, 68), (-si133r20)); // source i138 R33
        var si139r18 = (si30r30 * V(8, 76)); // source i139 R18
        var si140r19 = (si30r30 * V(8, 77)); // source i140 R19
        var si141r37 = MathF.FusedMultiplyAdd(si135r22, V(8, 69), (-si131r24)); // source i141 R37
        var si142r26 = MathF.Max(0f, si136r26); // source i142 R26
        var si143r28 = MathF.FusedMultiplyAdd(si135r22, V(8, 70), (-si137r29)); // source i143 R28
        var si144r40 = MathF.FusedMultiplyAdd(si16r6, si138r33, si133r20); // source i144 R40
        var si145r20 = (si30r30 * V(8, 78)); // source i145 R20
        var si146r33 = (si127r25 * si139r18); // source i146 R33
        var si147r19 = (si127r25 * si140r19); // source i147 R19
        var si148r17 = MathF.FusedMultiplyAdd(si16r6, si141r37, si131r24); // source i148 R17
        var si149r18 = V(7, 37); // source i149 R18
        var si150r29 = MathF.FusedMultiplyAdd(si16r6, si143r28, si137r29); // source i150 R29
        var si151r37 = (si127r25 * si145r20); // source i151 R37
        var si152r34 = MathF.FusedMultiplyAdd(si101r14, V(8, 80), (-si146r33)); // source i152 R34
        var si153r38 = MathF.FusedMultiplyAdd(si101r14, V(8, 81), (-si147r19)); // source i153 R38
        var si154r30 = MathF.FusedMultiplyAdd((-si127r25), si142r26, si120r23); // source i154 R30
        var si155r39 = (si127r25 * si142r26); // source i155 R39
        var si156r20 = (si0r10 + (-V(7, 35))); // source i156 R20
        var si157r14 = MathF.FusedMultiplyAdd(si101r14, V(8, 82), (-si151r37)); // source i157 R14
        var si158r24 = MathF.FusedMultiplyAdd(si16r4, si152r34, si146r33); // source i158 R24
        var si160r28 = MathF.FusedMultiplyAdd(si16r4, si153r38, si147r19); // source i160 R28
        var si161r34 = MathF.FusedMultiplyAdd(si149r18, V(8, 424), (-si144r40)); // source i161 R34
        var si162r19 = MathF.FusedMultiplyAdd((-si16r4), si127r25, si16r4); // source i162 R19
        var si163r32 = MathF.FusedMultiplyAdd(si16r4, si157r14, si151r37); // source i163 R32
        var si167r14 = MathF.FusedMultiplyAdd(si16r7, si161r34, si144r40); // source i167 R14
        var si168r34 = (si127r25 + si162r19); // source i168 R34
        var si169r30 = MathF.FusedMultiplyAdd(si16r4, si154r30, si155r39); // source i169 R30
        var si172r36 = MathF.FusedMultiplyAdd(si149r18, V(8, 426), (-si150r29)); // source i172 R36
        var si174r20 = MathF.Max(0f, si156r20); // source i174 R20
        var si175r33 = MathF.FusedMultiplyAdd((-si16r5), si168r34, si16r5); // source i175 R33
        var si178r19 = MathF.FusedMultiplyAdd(si16r7, si172r36, si150r29); // source i178 R19
        var si179r38 = MathF.FusedMultiplyAdd(si125r21, V(8, 85), (-si160r28)); // source i179 R38
        var si180r36 = (si174r20 + (-si169r30)); // source i180 R36
        var si182r33 = (si168r34 + si175r33); // source i182 R33
        var si183r34 = MathF.FusedMultiplyAdd(si125r21, V(8, 84), (-si158r24)); // source i183 R34
        var si184r29 = (si0r10 + (-V(7, 36))); // source i184 R29
        var si186r37 = MathF.FusedMultiplyAdd(si16r5, si179r38, si160r28); // source i186 R37
        var si187r51 = MathF.FusedMultiplyAdd(si16r5, si180r36, si169r30); // source i187 R51
        var si188r50 = MathF.FusedMultiplyAdd(si125r21, V(8, 86), (-si163r32)); // source i188 R50
        var si190r40 = MathF.FusedMultiplyAdd(si149r18, V(8, 425), (-si148r17)); // source i190 R40
        var si191r36 = MathF.FusedMultiplyAdd(si16r5, si183r34, si158r24); // source i191 R36
        var si192r21 = MathF.Max(0f, si184r29); // source i192 R21
        var si194r24 = MathF.FusedMultiplyAdd((-si16r6), si182r33, si16r6); // source i194 R24
        var si195r48 = prior.ComplementWeighted[1]; // source i195 R48
        var si196r17 = MathF.FusedMultiplyAdd(si16r7, si190r40, si148r17); // source i196 R17
        var si197r29 = MathF.FusedMultiplyAdd(si135r22, V(8, 88), (-si191r36)); // source i197 R29
        var si198r41 = (si192r21 + (-si187r51)); // source i198 R41
        var si199r28 = MathF.FusedMultiplyAdd(si135r22, V(8, 89), (-si186r37)); // source i199 R28
        var si200r40 = prior.ComplementWeighted[0]; // source i200 R40
        var si202r39 = (si17r2 * si127r25); // source i202 R39
        var si203r49 = MathF.FusedMultiplyAdd(si16r5, si188r50, si163r32); // source i203 R49
        var si204r29 = MathF.FusedMultiplyAdd(si16r6, si197r29, si191r36); // source i204 R29
        var si205r30 = MathF.FusedMultiplyAdd(si16r6, si198r41, si187r51); // source i205 R30
        var si206r28 = MathF.FusedMultiplyAdd(si16r6, si199r28, si186r37); // source i206 R28
        var si207r41 = MathF.FusedMultiplyAdd(si192r21, V(8, 68), (-si200r40)); // source i207 R41
        var si208r36 = (si17r0 * si127r25); // source i208 R36
        var si209r37 = (si17r1 * si127r25); // source i209 R37
        var si210r38 = (si142r26 * si202r39); // source i210 R38
        var si212r33 = (si182r33 + si194r24); // source i212 R33
        var si213r25 = MathF.FusedMultiplyAdd(si16r6, si207r41, si200r40); // source i213 R25
        var si214r34 = (si142r26 * si208r36); // source i214 R34
        var si215r37 = (si142r26 * si209r37); // source i215 R37
        var si216r40 = (si17r0 * V(8, 40)); // source i216 R40
        var si217r26 = (si17r1 * V(8, 41)); // source i217 R26
        var si218r36 = (si17r2 * V(8, 42)); // source i218 R36
        var si219r24 = MathF.FusedMultiplyAdd(si135r22, V(8, 90), (-si203r49)); // source i219 R24
        var si220r32 = prior.ComplementWeighted[2]; // source i220 R32
        var si221r47 = MathF.FusedMultiplyAdd(si192r21, V(8, 69), (-si195r48)); // source i221 R47
        var si222r39 = MathF.FusedMultiplyAdd(si120r23, si216r40, (-si214r34)); // source i222 R39
        var si223r46 = MathF.FusedMultiplyAdd(si120r23, si217r26, (-si215r37)); // source i223 R46
        var si224r41 = MathF.FusedMultiplyAdd(si120r23, si218r36, (-si210r38)); // source i224 R41
        var si225r24 = MathF.FusedMultiplyAdd(si16r6, si219r24, si203r49); // source i225 R24
        var si226r49 = MathF.FusedMultiplyAdd(si192r21, V(8, 70), (-si220r32)); // source i226 R49
        var si227r22 = MathF.FusedMultiplyAdd(si16r6, si221r47, si195r48); // source i227 R22
        var si228r39 = MathF.FusedMultiplyAdd(si16r4, si222r39, si214r34); // source i228 R39
        var si229r46 = MathF.FusedMultiplyAdd(si16r4, si223r46, si215r37); // source i229 R46
        var si230r38 = MathF.FusedMultiplyAdd(si16r4, si224r41, si210r38); // source i230 R38
        var si231r34 = (si17r0 * V(8, 44)); // source i231 R34
        var si232r37 = (si17r1 * V(8, 45)); // source i232 R37
        var si233r41 = (si17r2 * V(8, 46)); // source i233 R41
        var si234r26 = MathF.FusedMultiplyAdd(si16r6, si226r49, si220r32); // source i234 R26
        var si235r49 = MathF.FusedMultiplyAdd(si149r18, V(8, 93), (-si206r28)); // source i235 R49
        var si236r47 = MathF.FusedMultiplyAdd((-si16r7), si212r33, si16r7); // source i236 R47
        var si237r4 = MathF.FusedMultiplyAdd(si174r20, si231r34, (-si228r39)); // source i237 R4
        var si238r32 = MathF.FusedMultiplyAdd(si174r20, si232r37, (-si229r46)); // source i238 R32
        var si239r20 = MathF.FusedMultiplyAdd(si174r20, si233r41, (-si230r38)); // source i239 R20
        var si240r40 = (si0r10 + (-V(7, 37))); // source i240 R40
        var si241r34 = MathF.FusedMultiplyAdd(si16r7, si235r49, si206r28); // source i241 R34
        var si242r28 = (si17r0 * V(8, 48)); // source i242 R28
        var si243r39 = MathF.FusedMultiplyAdd(si16r5, si237r4, si228r39); // source i243 R39
        var si244r32 = MathF.FusedMultiplyAdd(si16r5, si238r32, si229r46); // source i244 R32
        var si245r20 = MathF.FusedMultiplyAdd(si16r5, si239r20, si230r38); // source i245 R20
        var si246r38 = (si17r1 * V(8, 49)); // source i246 R38
        var si247r46 = (si17r2 * V(8, 50)); // source i247 R46
        var si248r33 = (si212r33 + si236r47); // source i248 R33
        var si249r28 = MathF.FusedMultiplyAdd(si192r21, si242r28, (-si243r39)); // source i249 R28
        var si250r36 = MathF.FusedMultiplyAdd(si149r18, V(8, 92), (-si204r29)); // source i250 R36
        var si251r23 = MathF.Max(0f, si240r40); // source i251 R23
        var si252r5 = MathF.FusedMultiplyAdd(si192r21, si246r38, (-si244r32)); // source i252 R5
        var si253r21 = MathF.FusedMultiplyAdd(si192r21, si247r46, (-si245r20)); // source i253 R21
        var si254r2 = (si17r2 * V(8, 54)); // source i254 R2
        var si255r4 = MathF.Max(si248r33, cuts.C1Word0); // source i255 R4
        var si256r28 = MathF.FusedMultiplyAdd(si16r6, si249r28, si243r39); // source i256 R28
        var si257r4 = cuts.NormalizationReciprocal; // source i257 R4
        var si258r5 = MathF.FusedMultiplyAdd(si16r6, si252r5, si244r32); // source i258 R5
        var si259r21 = MathF.FusedMultiplyAdd(si16r6, si253r21, si245r20); // source i259 R21
        var si260r6 = (si17r0 * V(8, 52)); // source i260 R6
        var si261r0 = (si17r1 * V(8, 53)); // source i261 R0
        var si262r29 = MathF.FusedMultiplyAdd(si16r7, si250r36, si204r29); // source i262 R29
        var si263r37 = MathF.FusedMultiplyAdd(si149r18, V(8, 94), (-si225r24)); // source i263 R37
        var si264r33 = MathF.FusedMultiplyAdd(si251r23, V(8, 425), (-si227r22)); // source i264 R33
        var si265r2 = MathF.FusedMultiplyAdd(si251r23, si254r2, (-si259r21)); // source i265 R2
        var si266r6 = MathF.FusedMultiplyAdd(si251r23, si260r6, (-si256r28)); // source i266 R6
        var si267r0 = MathF.FusedMultiplyAdd(si251r23, si261r0, (-si258r5)); // source i267 R0
        var si268r18 = MathF.FusedMultiplyAdd(si251r23, V(8, 424), (-si213r25)); // source i268 R18
        var si269r1 = MathF.FusedMultiplyAdd(si251r23, V(8, 426), (-si234r26)); // source i269 R1
        var si270r29 = (si262r29 * si257r4); // source i270 R29
        var si271r2 = MathF.FusedMultiplyAdd(si16r7, si265r2, si259r21); // source i271 R2
        var si272r6 = MathF.FusedMultiplyAdd(si16r7, si266r6, si256r28); // source i272 R6
        var si273r5 = MathF.FusedMultiplyAdd(si16r7, si267r0, si258r5); // source i273 R5
        var si274r34 = (si241r34 * si257r4); // source i274 R34
        var si275r37 = MathF.FusedMultiplyAdd(si16r7, si263r37, si225r24); // source i275 R37
        var si276r22 = MathF.FusedMultiplyAdd(si16r7, si264r33, si227r22); // source i276 R22
        var si277r25 = MathF.FusedMultiplyAdd(si16r7, si268r18, si213r25); // source i277 R25
        var si278r14 = MathF.FusedMultiplyAdd(si167r14, si257r4, (-si270r29)); // source i278 R14
        var si279r5 = (si273r5 * si257r4); // source i279 R5
        var si280r1 = MathF.FusedMultiplyAdd(si16r7, si269r1, si234r26); // source i280 R1
        var si281r6 = (si272r6 * si257r4); // source i281 R6
        var si282r2 = (si271r2 * si257r4); // source i282 R2
        var si283r17 = MathF.FusedMultiplyAdd(si196r17, si257r4, (-si274r34)); // source i283 R17
        var si284r37 = (si275r37 * si257r4); // source i284 R37
        var si285r22 = MathF.FusedMultiplyAdd(si276r22, si257r4, (-si279r5)); // source i285 R22
        var si286r29 = MathF.FusedMultiplyAdd(si132r8, si278r14, si270r29); // source i286 R29
        var si287r25 = MathF.FusedMultiplyAdd(si277r25, si257r4, (-si281r6)); // source i287 R25
        var si288r1 = MathF.FusedMultiplyAdd(si280r1, si257r4, (-si282r2)); // source i288 R1
        var si289r40 = (si251r23 + (-si205r30)); // source i289 R40
        var si290r17 = MathF.FusedMultiplyAdd(si132r8, si283r17, si274r34); // source i290 R17
        var si292r18 = V(7, 38); // source i292 R18
        var si294r10 = (si0r10 + (-V(7, 38))); // source i294 R10
        var si295r19 = MathF.FusedMultiplyAdd(si178r19, si257r4, (-si284r37)); // source i295 R19
        var si296r22 = MathF.FusedMultiplyAdd(si285r22, si132r8, si279r5); // source i296 R22
        var si297r25 = MathF.FusedMultiplyAdd(si287r25, si132r8, si281r6); // source i297 R25
        var si298r5 = MathF.FusedMultiplyAdd(si288r1, si132r8, si282r2); // source i298 R5
        var si299r30 = MathF.FusedMultiplyAdd(si16r7, si289r40, si205r30); // source i299 R30
        var si300r6 = MathF.FusedMultiplyAdd(si292r18, V(8, 96), (-si286r29)); // source i300 R6
        var si301r2 = MathF.FusedMultiplyAdd(si292r18, V(8, 97), (-si290r17)); // source i301 R2
        var si302r19 = MathF.FusedMultiplyAdd(si132r8, si295r19, si284r37); // source i302 R19
        var si304r4 = V(7, 271); // source i304 R4
        var si305r7 = MathF.Max(0f, si294r10); // source i305 R7
        var si307r0 = MathF.FusedMultiplyAdd(si18r16, si300r6, si286r29); // source i307 R0
        var si308r2 = MathF.FusedMultiplyAdd(si18r16, si301r2, si290r17); // source i308 R2
        var si309r1 = MathF.FusedMultiplyAdd(si292r18, V(8, 98), (-si302r19)); // source i309 R1
        var si311r21 = MathF.FusedMultiplyAdd(si305r7, si299r30, (-si299r30)); // source i311 R21
        var si312r6 = MathF.FusedMultiplyAdd(si305r7, V(8, 96), (-si297r25)); // source i312 R6
        var si313r17 = MathF.FusedMultiplyAdd(si305r7, V(8, 97), (-si296r22)); // source i313 R17
        var si314r7 = MathF.FusedMultiplyAdd(si305r7, V(8, 98), (-si298r5)); // source i314 R7
        var si315r1 = MathF.FusedMultiplyAdd(si18r16, si309r1, si302r19); // source i315 R1
        var si317r10 = MathF.FusedMultiplyAdd(si18r16, si311r21, si299r30); // source i317 R10
        var si318r25 = MathF.FusedMultiplyAdd(si18r16, si312r6, si297r25); // source i318 R25
        var si319r17 = MathF.FusedMultiplyAdd(si18r16, si313r17, si296r22); // source i319 R17
        var si320r16 = MathF.FusedMultiplyAdd(si18r16, si314r7, si298r5); // source i320 R16
        var si321r5 = ((-si304r4) + V(7, 272)); // source i321 R5
        var si325r6 = ((-si19r15) + V(7, 12)); // source i325 R6
        var si329r5 = MathF.FusedMultiplyAdd(si132r8, si321r5, V(7, 271)); // source i329 R5
        var si330r7 = ((-si20r9) + V(7, 28)); // source i330 R7
        var si331r6 = MathF.FusedMultiplyAdd(si132r8, si325r6, si19r15); // source i331 R6
        var si333r15 = MathF.FusedMultiplyAdd((-si329r5), si21r11, si329r5); // source i333 R15
        var si334r9 = MathF.FusedMultiplyAdd(si132r8, si330r7, si20r9); // source i334 R9
        var si337r4 = Math.Clamp((si132r8 * V(7, 188)), 0f, 1f); // source i337 R4
        var si339r7 = ((-si333r15) + Bits(0x3f800000U)); // source i339 R7
        return new([si307r0, si308r2, si315r1], [si318r25, si319r17, si320r16], si317r10, si132r8, si331r6, si334r9, si333r15, si337r4, si255r4, si339r7);
    }

    // Raw ordered continuation after CreateLayerColorKernel. No TEX, MUFU, flow, scene or output instructions.
    // Read-only ABI: masks148..151; baseRGB152..154; layer5Mask155; metallic156; roughness157; AO158;
    // EX2 result159; RCP result160; exact source C1[0]161. Scratch60..139; prior outputs140..145.
    // Authored outputs170..183 in Cut order. No native geometry or fragment admission is implied.
    public static Kernel CreateKernel(Func<int, int, int>? nativeScalarWord = null)
    {
        nativeScalarWord ??= DefaultNativeScalarWord;
        var nativeMap = new Dictionary<(int Buffer, int Word), int>();
        var distinct = new HashSet<int>();
        foreach (var p in Inputs)
        {
            var mapped = nativeScalarWord(p.Buffer, p.Word);
            if ((uint)mapped >= 256 || mapped < 52 || !distinct.Add(mapped))
                throw new InvalidDataException("Colliding or out-of-range Eelektross native material scalar map.");
            nativeMap.Add((p.Buffer, p.Word), mapped);
        }
        var prior = ZaSwShEelektrossShader.CreateLayerColorKernel();
        var code = new List<ulong>(prior.Select(w =>
        {
            if ((w >> 48) != 0x4c98 || (w >> 34 & 31) != 5) return w;
            var input = ZaSwShEelektrossMaterial.Inputs[(int)(w >> 20 & 16383) - 52];
            return w & ~(16383UL << 20) | (ulong)nativeMap[(input.Buffer, input.Word)] << 20;
        }));
        var prefix = code.Count;
        void Add(ulong instruction, int buffer = -1, int word = -1)
        {
            if (buffer >= 0)
            {
                var mapped = nativeMap[(buffer, word)];
                if ((uint)mapped >= 256) throw new InvalidDataException("Native source scalar offset outside bounded material row.");
                instruction |= (ulong)mapped << 20;
            }
            code.Add(instruction);
        }
        Add(0x0103F8000007F03CUL); // source i0
        Add(0x4C9807940007003DUL, 7, 33); // source i30
        Add(0x4C9807940007003EUL, 7, 120); // source i98
        Add(0x4C9807940007003FUL, 7, 34); // source i101
        Add(0x5C58100009579440UL); // source i116
        Add(0x3859103F80073F41UL); // source i118
        Add(0x5C58100004079642UL); // source i119
        Add(0x5C6017800417FF40UL); // source i120
        Add(0x4C59101400073E41UL, 7, 121); // source i122
        Add(0x5C5810000427973EUL); // source i123
        Add(0x4C98079400070042UL, 7, 35); // source i125
        Add(0x385D103F80073E43UL); // source i127
        Add(0x51A0209400079F3EUL, 7, 120); // source i132
        Add(0x5C98078003E700B1UL);
        Add(0x4C98079400070041UL, 7, 36); // source i135
        Add(0x4C58301400073C44UL, 7, 33); // source i136
        Add(0x49A2461400074145UL, 8, 68); // source i138
        Add(0x4C68101400073D46UL, 8, 76); // source i139
        Add(0x4C68101400073D47UL, 8, 77); // source i140
        Add(0x49A2469400074148UL, 8, 69); // source i141
        Add(0x5C6017800447FF49UL); // source i142
        Add(0x49A2471400074144UL, 8, 70); // source i143
        Add(0x59A046000457964AUL); // source i144
        Add(0x4C68101400073D45UL, 8, 78); // source i145
        Add(0x5C6810000467433DUL); // source i146
        Add(0x5C68100004774346UL); // source i147
        Add(0x59A0468004879647UL); // source i148
        Add(0x4C98079400070048UL, 7, 37); // source i149
        Add(0x59A047000447964BUL); // source i150
        Add(0x5C68100004574344UL); // source i151
        Add(0x49A21E9400073F45UL, 8, 80); // source i152
        Add(0x49A2231400073F4CUL, 8, 81); // source i153
        Add(0x59A120000497434DUL); // source i154
        Add(0x5C6810000497434EUL); // source i155
        Add(0x4C58301400073C4FUL, 7, 35); // source i156
        Add(0x49A2221400073F50UL, 8, 82); // source i157
        Add(0x59A01E800457943FUL); // source i158
        Add(0x59A0230004C7943DUL); // source i160
        Add(0x49A2251400074845UL, 8, 424); // source i161
        Add(0x59A14A0004379446UL); // source i162
        Add(0x59A022000507944CUL); // source i163
        Add(0x59A0250004579744UL); // source i167
        Add(0x5C58100004674345UL); // source i168
        Add(0x59A0270004D79446UL); // source i169
        Add(0x49A225940007484AUL, 8, 426); // source i172
        Add(0x5C60178004F7FF4DUL); // source i174
        Add(0x59A14A800457954EUL); // source i175
        Add(0x59A0258004A7974FUL); // source i178
        Add(0x49A21E940007424AUL, 8, 85); // source i179
        Add(0x5C58300004674D4BUL); // source i180
        Add(0x5C58100004E74550UL); // source i182
        Add(0x49A21F9400074245UL, 8, 84); // source i183
        Add(0x4C58301400073C4EUL, 7, 36); // source i184
        Add(0x59A01E8004A79551UL); // source i186
        Add(0x59A0230004B7953DUL); // source i187
        Add(0x49A2261400074246UL, 8, 86); // source i188
        Add(0x49A2239400074842UL, 8, 425); // source i190
        Add(0x59A01F800457954AUL); // source i191
        Add(0x5C60178004E7FF3FUL); // source i192
        Add(0x59A14B0005079645UL); // source i194
        Add(0x59A023800427974BUL); // source i196
        Add(0x49A2251400074142UL, 8, 88); // source i197
        Add(0x5C58300003D73F47UL); // source i198
        Add(0x49A228940007414EUL, 8, 89); // source i199
        Add(0x5C68100004379A52UL); // source i202
        Add(0x59A0260004679553UL); // source i203
        Add(0x59A0250004279646UL); // source i204
        Add(0x59A01E8004779642UL); // source i205
        Add(0x59A0288004E7963DUL); // source i206
        Add(0x49A2479400073F47UL, 8, 68); // source i207
        Add(0x5C6810000437984AUL); // source i208
        Add(0x5C6810000437994CUL); // source i209
        Add(0x5C68100005274943UL); // source i210
        Add(0x5C5810000457504EUL); // source i212
        Add(0x59A0478004779645UL); // source i213
        Add(0x5C68100004A74947UL); // source i214
        Add(0x5C68100004C7494AUL); // source i215
        Add(0x4C68101400079849UL, 8, 40); // source i216
        Add(0x4C6810140007994CUL, 8, 41); // source i217
        Add(0x4C68101400079A50UL, 8, 42); // source i218
        Add(0x49A2299400074151UL, 8, 90); // source i219
        Add(0x49A2481400073F41UL, 8, 69); // source i221
        Add(0x59A2238004974052UL); // source i222
        Add(0x59A2250004C74049UL); // source i223
        Add(0x59A221800507404CUL); // source i224
        Add(0x59A0298005179640UL); // source i225
        Add(0x49A2489400073F50UL, 8, 70); // source i226
        Add(0x59A0480004179651UL); // source i227
        Add(0x59A0238005279441UL); // source i228
        Add(0x59A0250004979447UL); // source i229
        Add(0x59A0218004C79449UL); // source i230
        Add(0x4C68101400079843UL, 8, 44); // source i231
        Add(0x4C6810140007994AUL, 8, 45); // source i232
        Add(0x4C68101400079A4CUL, 8, 46); // source i233
        Add(0x59A0488005079652UL); // source i234
        Add(0x49A21E9400074850UL, 8, 93); // source i235
        Add(0x59A14B8004E79753UL); // source i236
        Add(0x59A2208004374D54UL); // source i237
        Add(0x59A2238004A74D43UL); // source i238
        Add(0x59A2248004C74D4AUL); // source i239
        Add(0x4C58301400073C4CUL, 7, 37); // source i240
        Add(0x59A01E800507974DUL); // source i241
        Add(0x4C6810140007983DUL, 8, 48); // source i242
        Add(0x59A0208005479550UL); // source i243
        Add(0x59A0238004379541UL); // source i244
        Add(0x59A0248004A79543UL); // source i245
        Add(0x4C68101400079947UL, 8, 49); // source i246
        Add(0x4C68101400079A49UL, 8, 50); // source i247
        Add(0x5C58100005374E4AUL); // source i248
        Add(0x59A2280003D73F4EUL); // source i249
        Add(0x49A223140007483DUL, 8, 92); // source i250
        Add(0x5C60178004C7FF53UL); // source i251
        Add(0x59A2208004773F4CUL); // source i252
        Add(0x59A2218004973F47UL); // source i253
        Add(0x4C68101400079A3FUL, 8, 54); // source i254
        Add(0x5C6017800A174A49UL); // source i255
        Add(0x5C980780049700B6UL);
        Add(0x59A0280004E79649UL); // source i256
        Add(0x59A0208004C7964AUL); // source i258
        Add(0x59A0218004779641UL); // source i259
        Add(0x4C68101400079843UL, 8, 52); // source i260
        Add(0x4C68101400079947UL, 8, 53); // source i261
        Add(0x59A0230003D7974CUL); // source i262
        Add(0x49A220140007483DUL, 8, 94); // source i263
        Add(0x49A2289400075346UL, 8, 425); // source i264
        Add(0x59A2208003F75348UL); // source i265
        Add(0x59A224800437533FUL); // source i266
        Add(0x59A2250004775343UL); // source i267
        Add(0x49A2229400075347UL, 8, 424); // source i268
        Add(0x49A229140007534EUL, 8, 426); // source i269
        Add(0x5C6810000A074C50UL); // source i270
        Add(0x59A020800487974CUL); // source i271
        Add(0x59A0248003F79741UL); // source i272
        Add(0x59A025000437973FUL); // source i273
        Add(0x5C6810000A074D43UL); // source i274
        Add(0x59A0200003D79748UL); // source i275
        Add(0x59A028800467973DUL); // source i276
        Add(0x59A0228004779740UL); // source i277
        Add(0x59A228000A074445UL); // source i278
        Add(0x5C6810000A073F44UL); // source i279
        Add(0x59A0290004E7973FUL); // source i280
        Add(0x5C6810000A074146UL); // source i281
        Add(0x5C6810000A074C41UL); // source i282
        Add(0x59A221800A074B47UL); // source i283
        Add(0x5C6810000A074849UL); // source i284
        Add(0x59A222000A073D48UL); // source i285
        Add(0x59A0280004573E3DUL); // source i286
        Add(0x59A223000A074045UL); // source i287
        Add(0x59A220800A073F40UL); // source i288
        Add(0x5C5830000427533FUL); // source i289
        Add(0x59A0218004773E4AUL); // source i290
        Add(0x4C98079400070043UL, 7, 38); // source i292
        Add(0x4C58301400073C47UL, 7, 38); // source i294
        Add(0x59A224800A074F3CUL); // source i295
        Add(0x59A0220003E7484BUL); // source i296
        Add(0x59A0230003E74544UL); // source i297
        Add(0x59A0208003E74045UL); // source i298
        Add(0x59A0210003F79740UL); // source i299
        Add(0x49A21E940007433FUL, 8, 96); // source i300
        Add(0x49A2251400074341UL, 8, 97); // source i301
        Add(0x59A0248003C73E42UL); // source i302
        Add(0x4C9807940007003CUL, 7, 271); // source i304
        Add(0x5C6017800477FF46UL); // source i305
        Add(0x59A01E8003F79B47UL); // source i307
        Add(0x5C980780047700AAUL);
        Add(0x59A0250004179B3DUL); // source i308
        Add(0x5C98078003D700ABUL);
        Add(0x49A221140007433DUL, 8, 98); // source i309
        Add(0x59A220000407463FUL); // source i311
        Add(0x49A2221400074641UL, 8, 96); // source i312
        Add(0x49A2259400074643UL, 8, 97); // source i313
        Add(0x49A2229400074647UL, 8, 98); // source i314
        Add(0x59A0210003D79B46UL); // source i315
        Add(0x5C980780046700ACUL);
        Add(0x59A0200003F79B3DUL); // source i317
        Add(0x5C98078003D700B0UL);
        Add(0x59A0220004179B3DUL); // source i318
        Add(0x5C98078003D700ADUL);
        Add(0x59A0258004379B3DUL); // source i319
        Add(0x5C98078003D700AEUL);
        Add(0x59A0228004779B3DUL); // source i320
        Add(0x5C98078003D700AFUL);
        Add(0x4C59101400073C3DUL, 7, 272); // source i321
        Add(0x4C59101400079C3CUL, 7, 12); // source i325
        Add(0x51A01E9400073E3FUL, 7, 271); // source i329
        Add(0x4C59101400079D3DUL, 7, 28); // source i330
        Add(0x59A04E0003C73E40UL); // source i331
        Add(0x5C980780040700B2UL);
        Add(0x59A11F8009E73F3CUL); // source i333
        Add(0x5C98078003C700B4UL);
        Add(0x59A04E8003D73E3FUL); // source i334
        Add(0x5C98078003F700B3UL);
        Add(0x4C6C101400073E3DUL, 7, 188); // source i337
        Add(0x5C98078003D700B5UL);
        Add(0x3859103F80073C3DUL); // source i339
        Add(0x5C98078003D700B7UL);
        return new(code.ToArray(),
        [
            new(0, 10, prefix + 0, 60, "i0r10"),
            new(30, 30, prefix + 1, 61, "i30r30"),
            new(98, 24, prefix + 2, 62, "i98r24"),
            new(101, 14, prefix + 3, 63, "i101r14"),
            new(116, 17, prefix + 4, 64, "i116r17"),
            new(118, 23, prefix + 5, 65, "i118r23"),
            new(119, 17, prefix + 6, 66, "i119r17"),
            new(120, 23, prefix + 7, 64, "i120r23"),
            new(122, 8, prefix + 8, 65, "i122r8"),
            new(123, 17, prefix + 9, 62, "i123r17"),
            new(125, 21, prefix + 10, 66, "i125r21"),
            new(127, 25, prefix + 11, 67, "i127r25"),
            new(132, 8, prefix + 12, 62, "i132r8"),
            new(132, 8, prefix + 13, 177, "FresnelFactor"),
            new(135, 22, prefix + 14, 65, "i135r22"),
            new(136, 26, prefix + 15, 68, "i136r26"),
            new(138, 33, prefix + 16, 69, "i138r33"),
            new(139, 18, prefix + 17, 70, "i139r18"),
            new(140, 19, prefix + 18, 71, "i140r19"),
            new(141, 37, prefix + 19, 72, "i141r37"),
            new(142, 26, prefix + 20, 73, "i142r26"),
            new(143, 28, prefix + 21, 68, "i143r28"),
            new(144, 40, prefix + 22, 74, "i144r40"),
            new(145, 20, prefix + 23, 69, "i145r20"),
            new(146, 33, prefix + 24, 61, "i146r33"),
            new(147, 19, prefix + 25, 70, "i147r19"),
            new(148, 17, prefix + 26, 71, "i148r17"),
            new(149, 18, prefix + 27, 72, "i149r18"),
            new(150, 29, prefix + 28, 75, "i150r29"),
            new(151, 37, prefix + 29, 68, "i151r37"),
            new(152, 34, prefix + 30, 69, "i152r34"),
            new(153, 38, prefix + 31, 76, "i153r38"),
            new(154, 30, prefix + 32, 77, "i154r30"),
            new(155, 39, prefix + 33, 78, "i155r39"),
            new(156, 20, prefix + 34, 79, "i156r20"),
            new(157, 14, prefix + 35, 80, "i157r14"),
            new(158, 24, prefix + 36, 63, "i158r24"),
            new(160, 28, prefix + 37, 61, "i160r28"),
            new(161, 34, prefix + 38, 69, "i161r34"),
            new(162, 19, prefix + 39, 70, "i162r19"),
            new(163, 32, prefix + 40, 76, "i163r32"),
            new(167, 14, prefix + 41, 68, "i167r14"),
            new(168, 34, prefix + 42, 69, "i168r34"),
            new(169, 30, prefix + 43, 70, "i169r30"),
            new(172, 36, prefix + 44, 74, "i172r36"),
            new(174, 20, prefix + 45, 77, "i174r20"),
            new(175, 33, prefix + 46, 78, "i175r33"),
            new(178, 19, prefix + 47, 79, "i178r19"),
            new(179, 38, prefix + 48, 74, "i179r38"),
            new(180, 36, prefix + 49, 75, "i180r36"),
            new(182, 33, prefix + 50, 80, "i182r33"),
            new(183, 34, prefix + 51, 69, "i183r34"),
            new(184, 29, prefix + 52, 78, "i184r29"),
            new(186, 37, prefix + 53, 81, "i186r37"),
            new(187, 51, prefix + 54, 61, "i187r51"),
            new(188, 50, prefix + 55, 70, "i188r50"),
            new(190, 40, prefix + 56, 66, "i190r40"),
            new(191, 36, prefix + 57, 74, "i191r36"),
            new(192, 21, prefix + 58, 63, "i192r21"),
            new(194, 24, prefix + 59, 69, "i194r24"),
            new(196, 17, prefix + 60, 75, "i196r17"),
            new(197, 29, prefix + 61, 66, "i197r29"),
            new(198, 41, prefix + 62, 71, "i198r41"),
            new(199, 28, prefix + 63, 78, "i199r28"),
            new(202, 39, prefix + 64, 82, "i202r39"),
            new(203, 49, prefix + 65, 83, "i203r49"),
            new(204, 29, prefix + 66, 70, "i204r29"),
            new(205, 30, prefix + 67, 66, "i205r30"),
            new(206, 28, prefix + 68, 61, "i206r28"),
            new(207, 41, prefix + 69, 71, "i207r41"),
            new(208, 36, prefix + 70, 74, "i208r36"),
            new(209, 37, prefix + 71, 76, "i209r37"),
            new(210, 38, prefix + 72, 67, "i210r38"),
            new(212, 33, prefix + 73, 78, "i212r33"),
            new(213, 25, prefix + 74, 69, "i213r25"),
            new(214, 34, prefix + 75, 71, "i214r34"),
            new(215, 37, prefix + 76, 74, "i215r37"),
            new(216, 40, prefix + 77, 73, "i216r40"),
            new(217, 26, prefix + 78, 76, "i217r26"),
            new(218, 36, prefix + 79, 80, "i218r36"),
            new(219, 24, prefix + 80, 81, "i219r24"),
            new(221, 47, prefix + 81, 65, "i221r47"),
            new(222, 39, prefix + 82, 82, "i222r39"),
            new(223, 46, prefix + 83, 73, "i223r46"),
            new(224, 41, prefix + 84, 76, "i224r41"),
            new(225, 24, prefix + 85, 64, "i225r24"),
            new(226, 49, prefix + 86, 80, "i226r49"),
            new(227, 22, prefix + 87, 81, "i227r22"),
            new(228, 39, prefix + 88, 65, "i228r39"),
            new(229, 46, prefix + 89, 71, "i229r46"),
            new(230, 38, prefix + 90, 73, "i230r38"),
            new(231, 34, prefix + 91, 67, "i231r34"),
            new(232, 37, prefix + 92, 74, "i232r37"),
            new(233, 41, prefix + 93, 76, "i233r41"),
            new(234, 26, prefix + 94, 82, "i234r26"),
            new(235, 49, prefix + 95, 80, "i235r49"),
            new(236, 47, prefix + 96, 83, "i236r47"),
            new(237, 4, prefix + 97, 84, "i237r4"),
            new(238, 32, prefix + 98, 67, "i238r32"),
            new(239, 20, prefix + 99, 74, "i239r20"),
            new(240, 40, prefix + 100, 76, "i240r40"),
            new(241, 34, prefix + 101, 77, "i241r34"),
            new(242, 28, prefix + 102, 61, "i242r28"),
            new(243, 39, prefix + 103, 80, "i243r39"),
            new(244, 32, prefix + 104, 65, "i244r32"),
            new(245, 20, prefix + 105, 67, "i245r20"),
            new(246, 38, prefix + 106, 71, "i246r38"),
            new(247, 46, prefix + 107, 73, "i247r46"),
            new(248, 33, prefix + 108, 74, "i248r33"),
            new(249, 28, prefix + 109, 78, "i249r28"),
            new(250, 36, prefix + 110, 61, "i250r36"),
            new(251, 23, prefix + 111, 83, "i251r23"),
            new(252, 5, prefix + 112, 76, "i252r5"),
            new(253, 21, prefix + 113, 71, "i253r21"),
            new(254, 2, prefix + 114, 63, "i254r2"),
            new(255, 4, prefix + 115, 73, "i255r4"),
            new(255, 4, prefix + 116, 182, "CoverageDenominator"),
            new(256, 28, prefix + 117, 73, "i256r28"),
            new(258, 5, prefix + 118, 74, "i258r5"),
            new(259, 21, prefix + 119, 65, "i259r21"),
            new(260, 6, prefix + 120, 67, "i260r6"),
            new(261, 0, prefix + 121, 71, "i261r0"),
            new(262, 29, prefix + 122, 76, "i262r29"),
            new(263, 37, prefix + 123, 61, "i263r37"),
            new(264, 33, prefix + 124, 70, "i264r33"),
            new(265, 2, prefix + 125, 72, "i265r2"),
            new(266, 6, prefix + 126, 63, "i266r6"),
            new(267, 0, prefix + 127, 67, "i267r0"),
            new(268, 18, prefix + 128, 71, "i268r18"),
            new(269, 1, prefix + 129, 78, "i269r1"),
            new(270, 29, prefix + 130, 80, "i270r29"),
            new(271, 2, prefix + 131, 76, "i271r2"),
            new(272, 6, prefix + 132, 65, "i272r6"),
            new(273, 5, prefix + 133, 63, "i273r5"),
            new(274, 34, prefix + 134, 67, "i274r34"),
            new(275, 37, prefix + 135, 72, "i275r37"),
            new(276, 22, prefix + 136, 61, "i276r22"),
            new(277, 25, prefix + 137, 64, "i277r25"),
            new(278, 14, prefix + 138, 69, "i278r14"),
            new(279, 5, prefix + 139, 68, "i279r5"),
            new(280, 1, prefix + 140, 63, "i280r1"),
            new(281, 6, prefix + 141, 70, "i281r6"),
            new(282, 2, prefix + 142, 65, "i282r2"),
            new(283, 17, prefix + 143, 71, "i283r17"),
            new(284, 37, prefix + 144, 73, "i284r37"),
            new(285, 22, prefix + 145, 72, "i285r22"),
            new(286, 29, prefix + 146, 61, "i286r29"),
            new(287, 25, prefix + 147, 69, "i287r25"),
            new(288, 1, prefix + 148, 64, "i288r1"),
            new(289, 40, prefix + 149, 63, "i289r40"),
            new(290, 17, prefix + 150, 74, "i290r17"),
            new(292, 18, prefix + 151, 67, "i292r18"),
            new(294, 10, prefix + 152, 71, "i294r10"),
            new(295, 19, prefix + 153, 60, "i295r19"),
            new(296, 22, prefix + 154, 75, "i296r22"),
            new(297, 25, prefix + 155, 68, "i297r25"),
            new(298, 5, prefix + 156, 69, "i298r5"),
            new(299, 30, prefix + 157, 64, "i299r30"),
            new(300, 6, prefix + 158, 63, "i300r6"),
            new(301, 2, prefix + 159, 65, "i301r2"),
            new(302, 19, prefix + 160, 66, "i302r19"),
            new(304, 4, prefix + 161, 60, "i304r4"),
            new(305, 7, prefix + 162, 70, "i305r7"),
            new(307, 0, prefix + 163, 71, "i307r0"),
            new(307, 0, prefix + 164, 170, "EmissionR"),
            new(308, 2, prefix + 165, 61, "i308r2"),
            new(308, 2, prefix + 166, 171, "EmissionG"),
            new(309, 1, prefix + 167, 61, "i309r1"),
            new(311, 21, prefix + 168, 63, "i311r21"),
            new(312, 6, prefix + 169, 65, "i312r6"),
            new(313, 17, prefix + 170, 67, "i313r17"),
            new(314, 7, prefix + 171, 71, "i314r7"),
            new(315, 1, prefix + 172, 70, "i315r1"),
            new(315, 1, prefix + 173, 172, "EmissionB"),
            new(317, 10, prefix + 174, 61, "i317r10"),
            new(317, 10, prefix + 175, 176, "ComplementWeight"),
            new(318, 25, prefix + 176, 61, "i318r25"),
            new(318, 25, prefix + 177, 173, "ComplementR"),
            new(319, 17, prefix + 178, 61, "i319r17"),
            new(319, 17, prefix + 179, 174, "ComplementG"),
            new(320, 16, prefix + 180, 61, "i320r16"),
            new(320, 16, prefix + 181, 175, "ComplementB"),
            new(321, 5, prefix + 182, 61, "i321r5"),
            new(325, 6, prefix + 183, 60, "i325r6"),
            new(329, 5, prefix + 184, 63, "i329r5"),
            new(330, 7, prefix + 185, 61, "i330r7"),
            new(331, 6, prefix + 186, 64, "i331r6"),
            new(331, 6, prefix + 187, 178, "Metallic"),
            new(333, 15, prefix + 188, 60, "i333r15"),
            new(333, 15, prefix + 189, 180, "AoOcclusion"),
            new(334, 9, prefix + 190, 63, "i334r9"),
            new(334, 9, prefix + 191, 179, "Roughness"),
            new(337, 4, prefix + 192, 61, "i337r4"),
            new(337, 4, prefix + 193, 181, "SssMask"),
            new(339, 7, prefix + 194, 61, "i339r7"),
            new(339, 7, prefix + 195, 183, "AoFactor"),
        ]);
    }
    private static float Bits(uint word) => BitConverter.UInt32BitsToSingle(word);
    private static void RequireFinite(float value)
    { if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite authored Eelektross arithmetic input."); }
    private static void RequireValues(ReadOnlySpan<float> values)
    {
        if (values.Length != Inputs.Count) throw new InvalidDataException("Eelektross source word count changed.");
        foreach (var value in values) RequireFinite(value);
    }
}
