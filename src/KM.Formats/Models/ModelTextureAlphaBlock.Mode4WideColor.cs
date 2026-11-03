// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Shared;
using BCnEncoder.Decoder;

namespace KM.Formats.Models;

// Purpose-specific fallback: BC7 mode4, rotation0, indexSelection1.
// Unlike FitMode4, RGB has eight selectors and alpha has four. This is a
// bounded color fit, not a proof of unencodability when it returns null.
internal static partial class ModelTextureAlphaBlock
{
    private sealed record WideColorFit(int[] Ends, int[] Indices, double Error);
    internal static byte[]? FitMode4WideColor(ColorRgba32[] pixels, ushort visible = ushort.MaxValue)
    {
        if (pixels.Length != 16 || visible == 0)
            throw new InvalidDataException("Mode4 wide-color fitting requires a visible 4x4 block.");
        var active = Enumerable.Range(0,16).Where(i => (visible & (1<<i)) != 0).ToArray();
        var values = pixels.Select(p => new[]{(int)p.r,(int)p.g,(int)p.b,(int)p.a}).ToArray();
        int[] colorWeights = [0,9,18,27,37,46,55,64];
        int[] alphaWeights = [0,21,43,64];
        var rgb = FitWideColorRgb(values,active,5,colorWeights,
            Enumerable.Repeat(255,16).ToArray(),-1,0,0);
        if(rgb.Error > active.Length*27) return null;
        var minimum=active.Min(i=>(int)pixels[i].a); var maximum=active.Max(i=>(int)pixels[i].a);
        int[]? alphaIndices=null; var alphaError=long.MaxValue; var first=0; var last=0;
        // Normalize endpoint order here. Packing independently swaps each
        // endpoint pair and inverts its selectors to encode anchor MSBs legally.
        for(var low=0;low<64;low++) for(var high=low;high<64;high++)
        {
            var a=WideExpand(low,6); var b=WideExpand(high,6);
            if(a>minimum+1 || b<maximum-1 || minimum==0 && a!=0 || maximum==255 && b!=255) continue;
            var indices=new int[16]; long error=0; var valid=true;
            foreach(var i in active)
            {
                var best=int.MaxValue;
                for(var j=0;j<4;j++)
                {
                    var value=WideInterpolate(a,b,alphaWeights[j]); var d=Math.Abs(value-pixels[i].a);
                    if(d<=1 && (pixels[i].a is not(0 or 255) || value==pixels[i].a) && d<best)
                    {best=d;indices[i]=j;}
                }
                if(best==int.MaxValue){valid=false;break;} error+=best*best;
            }
            if(valid && error<alphaError){alphaError=error;alphaIndices=indices;first=low;last=high;}
        }
        if(alphaIndices is null) return null;
        var ends=rgb.Ends.Select(v=>WideQuantize(v,5)).ToArray(); var colors=rgb.Indices.ToArray();
        if(colors[0]>=4)
        {
            for(var c=0;c<3;c++) (ends[c*2],ends[c*2+1])=(ends[c*2+1],ends[c*2]);
            for(var i=0;i<16;i++) colors[i]=7-colors[i];
        }
        if(alphaIndices[0]>=2)
        {
            (first,last)=(last,first);
            for(var i=0;i<16;i++) alphaIndices[i]=3-alphaIndices[i];
        }
        var bytes=new byte[16]; var bit=0;
        Put(16,5); Put(0,2); Put(1,1);
        foreach(var end in ends) Put(end,5);
        Put(first,6); Put(last,6);
        // Selection1: first 2-bit stream belongs to alpha, second 3-bit to RGB.
        for(var i=0;i<16;i++) Put(alphaIndices[i],i==0?1:2);
        for(var i=0;i<16;i++) Put(colors[i],i==0?2:3);
        if(bit!=128) throw new InvalidDataException("Mode4 wide-color block length changed.");
        var decoded=new BcDecoder().DecodeRaw(bytes,4,4,CompressionFormat.Bc7);
        double squared=0;
        foreach(var i in active)
        {
            if(Math.Abs(pixels[i].a-decoded[i].a)>1 || pixels[i].a is 0 or 255 && pixels[i].a!=decoded[i].a) return null;
            squared+=Math.Pow(pixels[i].r-decoded[i].r,2)+Math.Pow(pixels[i].g-decoded[i].g,2)+Math.Pow(pixels[i].b-decoded[i].b,2);
        }
        return squared<=active.Length*27 ? bytes : null;
        void Put(int value,int count){for(var i=0;i<count;i++,bit++)bytes[bit/8]|=(byte)(((value>>i)&1)<<(bit%8));}
    }

    private static WideColorFit FitWideColorRgb(int[][] values,int[] active,int bits,int[] weights,int[] allowed,int alphaChannel,int alphaFirst,int alphaLast)
    {
        var dimensions=Enumerable.Range(0,3).Where(c=>c!=alphaChannel).ToArray();
        WideColorFit? best=null;
        var seeds=new List<int[]>();
        for(var c=0;c<3;c++)
        {
            if(c==alphaChannel)continue;
            var ordered=active.OrderBy(i=>values[i][c]).ToArray();
            seeds.Add([ordered[0],ordered[^1]]);
            seeds.Add([ordered[^1],ordered[0]]);
        }
        foreach(var seed in seeds)
        {
            var ends=new int[6];var indices=new int[16];
            for(var c=0;c<3;c++)
            {
                ends[c*2]=c==alphaChannel?alphaFirst:WideExpand(WideQuantize(values[seed[0]][c],bits),bits);
                ends[c*2+1]=c==alphaChannel?alphaLast:WideExpand(WideQuantize(values[seed[1]][c],bits),bits);
            }
            for(var step=0;step<12;step++)
            {
                Select();
                double aa=0,ab=0,bb=0;var av=new double[3];var bv=new double[3];
                foreach(var i in active)
                {
                    var b=weights[indices[i]]/64d;var a=1-b;
                    aa+=a*a;ab+=a*b;bb+=b*b;
                    foreach(var c in dimensions){av[c]+=a*values[i][c];bv[c]+=b*values[i][c];}
                }
                var determinant=aa*bb-ab*ab;
                foreach(var c in dimensions)
                {
                    var mean=active.Average(i=>values[i][c]);
                    ends[c*2]=WideExpand(WideQuantize(determinant>1e-10?(av[c]*bb-bv[c]*ab)/determinant:mean,bits),bits);
                    ends[c*2+1]=WideExpand(WideQuantize(determinant>1e-10?(bv[c]*aa-av[c]*ab)/determinant:mean,bits),bits);
                }
            }
            Select();var error=Error();
            for(var pass=0;pass<8;pass++)
            {
                var before=error;
                foreach(var c in dimensions)for(var e=0;e<2;e++)
                {
                    var slot=c*2+e;var original=ends[slot];var originalQ=WideQuantize(original,bits);
                    var chosen=original;var chosenError=error;
                    foreach(var delta in new[]{-2,-1,1,2})
                    {
                        ends[slot]=WideExpand(Math.Clamp(originalQ+delta,0,(1<<bits)-1),bits);Select();var candidateError=Error();
                        if(candidateError<chosenError){chosen=ends[slot];chosenError=candidateError;}
                    }
                    ends[slot]=chosen;Select();error=chosenError;
                }
                if(error==before)break;
            }
            if(best is null || error<best.Error)best=new(ends.ToArray(),indices.ToArray(),error);
            void Select()
            {
                foreach(var i in active)
                {
                    var error=double.PositiveInfinity;
                    for(var j=0;j<weights.Length;j++)
                    {
                        if((allowed[i] & (1<<j))==0)continue;
                        double d=0;foreach(var c in dimensions)d+=Math.Pow(WideInterpolate(ends[c*2],ends[c*2+1],weights[j])-values[i][c],2);
                        if(d<error){error=d;indices[i]=j;}
                    }
                }
            }
            double Error(){double total=0;foreach(var i in active)foreach(var c in dimensions)total+=Math.Pow(WideInterpolate(ends[c*2],ends[c*2+1],weights[indices[i]])-values[i][c],2);return total;}
        }
        return best ?? throw new InvalidDataException("No color dimensions.");
    }


    private static int WideInterpolate(int a,int b,int weight)=>((64-weight)*a+weight*b+32)>>6;
    private static int WideExpand(int value,int bits)=>bits switch{5=>(value<<3)|(value>>2),6=>(value<<2)|(value>>4),_=>throw new ArgumentOutOfRangeException(nameof(bits))};
    private static int WideQuantize(double value,int bits)
    {
        var best=0;var error=double.PositiveInfinity;
        for(var q=0;q<(1<<bits);q++){var d=Math.Abs(WideExpand(q,bits)-value);if(d<error){error=d;best=q;}}
        return best;
    }
}
