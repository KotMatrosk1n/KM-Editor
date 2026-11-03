// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SwSh;

/// <summary>Samples qualified authored RG tangent normals after the native skin transform.</summary>
public static class SwShTangentNormalShader
{
 public static byte[] Vertex(byte[] original) => Vertex(original, secondaryUv: false);
 internal static byte[] PoltchageistVertex(byte[] original) => Vertex(original, secondaryUv: true);
 private static byte[] Vertex(byte[] original, bool secondaryUv)
 {
  var template=KM.Formats.ZA.ZaSwShEyeShader.Vertex(original);
  int I(int at)=>BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at,4));
  int P(int at)=>checked((int)BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(at,8)));
  var stage=P(P(P(0x80)+16)+8);var control=P(stage+8);var data=P(stage+16);var length=I(stage+24);var end=I(control+0x6f8)+0x30;var constants=I(control+0x700);var constantLength=I(control+0x6fc);var relocation=I(24);
  var section=Enumerable.Range(0,6).Select(i=>relocation+16+i*24).Single(s=>I(s+8)==data);var capacity=I(section+12);
  var code=new List<(ulong Instruction,ulong Control)>(); int originalBoundary = 0;var prepares=0;var exits=0;
  void Emit(ulong w,bool asynchronous=false)=>code.Add((w,KM.Formats.SwSh.SwShShaderScheduling.Serial(w)));
  void Load(int d,int attribute,bool output=false)=>Emit(0xefd87f800007ff00UL|(output?1UL<<32:0)|(ulong)attribute<<20|(uint)d,true);
  void Store(int s,int attribute)=>Emit(0xeff07f800007ff00UL|(ulong)attribute<<20|(uint)s);
  void Multiply(int d,int a,int b)=>Emit(0x5c68100000070000UL|(ulong)b<<20|(uint)a<<8|(uint)d);
  void Fma(int d,int a,int b,int c)=>Emit(0x59a0000000070000UL|(ulong)c<<39|(ulong)b<<20|(uint)a<<8|(uint)d);
  void Constant(int d,float value)=>Emit(0x010000000007f000UL|(ulong)BitConverter.SingleToUInt32Bits(value)<<20|(uint)d);
  void MultiplyWorld(int d,int a,int word)=>Emit(0x4c68100000070000UL|3UL<<34|(ulong)word<<20|(uint)a<<8|(uint)d);
  void FmaWorld(int d,int a,int word,int c)=>Emit(0x49a0000000070000UL|(ulong)c<<39|3UL<<34|(ulong)word<<20|(uint)a<<8|(uint)d);
  void Normalize(int first)
  {Multiply(66,first,first);Fma(66,first+1,first+1,66);Fma(66,first+2,first+2,66);Emit(0x5080000000570000UL|66UL<<8|66,true);for(var c=0;c<3;c++)Multiply(first+c,first+c,66);}
  void Prepare()
  {
   // At the pinned position load, the native weighted matrix is complete and
   // still unnormalized. Its later output columns have already lost their scale.
   int[][] columns=[[29,26,8],[5,35,37],[21,19,24]];
   for(var c=0;c<3;c++){Load(40+c,0x90+c*4);Load(43+c,0xa0+c*4);}Load(46,0xac);
   for(var c=0;c<3;c++)
   {
    var v=columns[c];var inverse=50+c;
    Multiply(inverse,v[0],v[0]);Fma(inverse,v[1],v[1],inverse);Fma(inverse,v[2],v[2],inverse);
    Emit(0x5080000000470000UL|(ulong)inverse<<8|(uint)inverse,true);
   }
   // Source skinning applies the same inverse-squared column correction to N
   // and T, accumulating Y, X, Z before independent normalization.
   for(var t=0;t<2;t++)
   {
    var input=40+t*3;var first=60+t*3;
    for(var c=0;c<3;c++)
    {
     Multiply(first+c,columns[1][c],51);Multiply(first+c,first+c,input+1);
     Multiply(66,columns[0][c],50);Fma(first+c,66,input,first+c);
     Multiply(66,columns[2][c],52);Fma(first+c,66,input+2,first+c);
    }
    Normalize(first);
   }
   // Recreate the signed10 transform-feedback values consumed by Standard.
   Constant(48,511);Constant(49,1f/511);
   for(var r=60;r<66;r++)
   {
    Multiply(r,r,48);Emit(0x5cb0118000071a00UL|(ulong)r<<20|(uint)r);
    Emit(0x5cb8000000072a00UL|(ulong)r<<20|(uint)r);Multiply(r,r,49);
   }
  }
  for(var block=0x80;block<end;block+=32)
  {
   var scheduling=BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data+block,8));
   for(var lane=0;lane<3;lane++)
   {
    var w=BinaryPrimitives.ReadUInt64LittleEndian(template.AsSpan(data+block+8+lane*8,8));
    if(w==0xefd87f800807ff07UL){Prepare();prepares++;}
    if(w==0xe30000000007000fUL)
    {
     exits++;
     // Standard transforms the packed frame by its world matrix in X,Y,Z
     // order. Native c3 words16..31 carry that same column-major matrix.
     for(var c=0;c<3;c++)for(var t=0;t<2;t++)
     {var result=40+t*3+c;var input=60+t*3;MultiplyWorld(result,input,16+c);FmaWorld(result,input+1,20+c,result);FmaWorld(result,input+2,24+c,result);}
     Normalize(40);Normalize(43);
     int[] attributes=[0x9c,0xac,0xbc,0xf8,0x10c,0x118];for(var c=0;c<6;c++)Store(40+c,attributes[c]);Store(46,0x11c);
     if(secondaryUv){Load(67,0xc0);Load(68,0xc4);Store(67,0x120);Store(68,0x124);}
    }
    KM.Formats.SwSh.SwShShaderScheduling.Original(code,ref originalBoundary,w,scheduling>>(lane*21)&0x1ffff);
   }
  }
  if(prepares!=1||exits!=1)throw new InvalidDataException("Skinned tangent vertex preparation or exit is ambiguous.");
  while(code.Count%3!=0)code.Add((0x50b0000000070f00UL,0x7ff));
  var newEnd=checked(0x80+code.Count/3*32);var newConstants=(newEnd+255)&~255;var newLength=(newConstants+constantLength+255)&~255;
  if(newLength>capacity)throw new InvalidDataException("Skinned tangent vertex extension exceeds its allocation.");
  var resultBytes=template.ToArray();resultBytes.AsSpan(data+0x80,capacity-0x80).Clear();template.AsSpan(data+constants,constantLength).CopyTo(resultBytes.AsSpan(data+newConstants));
  void Write(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(resultBytes.AsSpan(at,4),value);
  Write(stage+24,newLength);Write(control+0x6f8,newEnd-0x30);Write(control+0x700,newConstants);Write(control+0x704,newLength);Write(control+0x70c,secondaryUv?69:67);
  if(secondaryUv)
  {
   var input=data+0x30+24+4/2;var output=data+0x30+54+10/2;
   if((resultBytes[input]&3)!=0||(resultBytes[output]&3)!=0)throw new InvalidDataException("Poltchageist UV varying allocation is already in use.");
   resultBytes[input]|=3;resultBytes[output]|=3;
  }
  for(var i=0;i<code.Count;i+=3)
  {
   var at=data+0x80+i/3*32;BinaryPrimitives.WriteUInt64LittleEndian(resultBytes.AsSpan(at,8),code[i].Control|code[i+1].Control<<21|code[i+2].Control<<42);
   for(var lane=0;lane<3;lane++)BinaryPrimitives.WriteUInt64LittleEndian(resultBytes.AsSpan(at+8+lane*8,8),code[i+lane].Instruction);
  }
  return KM.Formats.SV.SvSwShPointLightReflection.Add(resultBytes, vertex: true, tangentInputOnly: true, tangentSecondaryUv: secondaryUv);
 }
}
