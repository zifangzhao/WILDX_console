using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using USB512_interface;

internal static class NpImuGridWireTests {
    static int checks;
    sealed class Vector {internal byte[] Raw;internal bool Valid;}
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Bad(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static void Put16(byte[] b,int o,ushort v){b[o]=(byte)v;b[o+1]=(byte)(v>>8);}
    static void Put32(byte[] b,int o,uint v){for(int i=0;i<4;i++)b[o+i]=(byte)(v>>(8*i));}
    static void Put64(byte[] b,int o,ulong v){Put32(b,o,(uint)v);Put32(b,o+4,(uint)(v>>32));}
    static uint ReferenceCrc(byte[] b){uint c=0xffffffff;foreach(byte v in b.Take(44).Concat(b.Skip(48))){c^=v;for(int n=0;n<8;n++)c=(c&1)!=0?(c>>1)^0xedb88320:c>>1;}return ~c;}
    static void Seal(byte[] b){Put32(b,44,ReferenceCrc(b));}
    static byte[] Hex(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
    static NpImuGridBatch Sparse(){return new NpImuGridBatch{FirstSlot=0x1020304050607080,SlotCount=4,CaptureGeneration=0x12345678,ObservedMask=10,AgValid=10,MagValid=8,AgRepeat=8,ErrorMask=2,
        Rows=new[]{new[]{1,-1,Int32.MinValue,Int32.MaxValue,1000000,-1000000,0,300,-300},new[]{11,22,33,-44,-55,-66,77,88,99}}};}
    static void Generate(string vectors,string expected){
        var cases=new List<Vector>();Action<byte[],bool> add=(raw,valid)=>{
            if(valid){var parsed=NpImuGridWire.Parse(raw);Check(NpImuGridWire.Pack(parsed).SequenceEqual(raw),"C# exact roundtrip");}
            else Bad(()=>NpImuGridWire.Parse(raw),"malformed C# frame accepted");cases.Add(new Vector{Raw=raw,Valid=valid});
        };
        var known=Hex("4E534731010130008070605040302010040024000A000A0008000800000002004800000000000000785634123EE8B14001000000FFFFFFFF00000080FFFFFF7F40420F00C0BDF0FF000000002C010000D4FEFFFF0B0000001600000021000000D4FFFFFFC9FFFFFFBEFFFFFF4D0000005800000063000000");
        Check(NpImuGridWire.Pack(Sparse()).SequenceEqual(known),"Independent Python struct/zlib sparse golden differs");
        add(known,true);
        add(NpImuGridWire.Pack(new NpImuGridBatch{SlotCount=1,CaptureGeneration=1}),true); // Explicit unobserved interval.
        add(NpImuGridWire.Pack(new NpImuGridBatch{FirstSlot=10,SlotCount=12,CaptureGeneration=7,ObservedMask=4095,AgValid=4095,MagValid=0x555,AgRepeat=0xaaa,MagRepeat=0x111,ErrorMask=0x800,
            Rows=Enumerable.Range(0,12).Select(r=>Enumerable.Range(0,9).Select(c=>(r+1)*(c%2==0?-1000000:1000000)+c).ToArray()).ToArray()}),true);
        add(NpImuGridWire.Pack(new NpImuGridBatch{FirstSlot=UInt64.MaxValue-12,SlotCount=12,CaptureGeneration=UInt32.MaxValue,ObservedMask=0x801,AgValid=0x801,Rows=Sparse().Rows}),true);
        add(NpImuGridWire.Pack(new NpImuGridBatch{FirstSlot=UInt64.MaxValue-1,SlotCount=1,CaptureGeneration=1,ObservedMask=1,AgValid=1,MagValid=1,AgRepeat=1,MagRepeat=1,Rows=new[]{new int[9]}}),true);
        add(NpImuGridWire.Pack(new NpImuGridBatch{SlotCount=1,CaptureGeneration=1,ObservedMask=1,ErrorMask=1,Rows=new[]{new int[9]}}),true);
        foreach(var mutate in new Action<byte[]>[]{
            b=>b[0]^=1,b=>b[4]=2,b=>b[5]=0,b=>Put16(b,6,47),b=>Put16(b,6,49),b=>Put16(b,16,0),b=>Put16(b,16,13),
            b=>Put16(b,18,32),b=>Put16(b,20,0x10),b=>Put16(b,22,1),b=>Put16(b,24,1),b=>Put16(b,26,1),b=>Put16(b,28,2),b=>Put16(b,30,1),
            b=>{Put16(b,22,8);Put16(b,26,2);},b=>Put32(b,32,36),b=>Put32(b,32,UInt32.MaxValue),b=>Put32(b,36,1),b=>Put32(b,40,0),b=>Put64(b,8,UInt64.MaxValue-3)}){
            var raw=(byte[])known.Clone();mutate(raw);Seal(raw);add(raw,false);
        }
        var corrupt=(byte[])known.Clone();corrupt[44]^=1;add(corrupt,false);
        corrupt=(byte[])known.Clone();corrupt[119]^=1;add(corrupt,false);
        add(known.Take(known.Length-1).ToArray(),false);add(known.Concat(new byte[1]).ToArray(),false);add(new byte[47],false);add(new byte[481],false);add(new byte[48],false);
        var above12=NpImuGridWire.Pack(new NpImuGridBatch{SlotCount=12,CaptureGeneration=1});Put16(above12,20,0x1000);Seal(above12);add(above12,false);
        Bad(()=>NpImuGridWire.Parse(null),"null accepted");
        foreach(var invalid in new Action<NpImuGridBatch>[] {b=>b.CaptureGeneration=0,b=>b.SlotCount=0,b=>b.SlotCount=13,b=>b.FirstSlot=UInt64.MaxValue-3,
            b=>b.AgRepeat=1,b=>b.ErrorMask=1,b=>b.Rows=null,b=>b.Rows=new int[0][],b=>b.Rows[0]=new int[8],b=>b.Rows[1]=null}){
            var batch=Sparse();invalid(batch);Bad(()=>NpImuGridWire.Pack(batch),"invalid C# input accepted");
        }
        var preserved=(byte[])known.Clone();var copy=NpImuGridWire.Parse(preserved);preserved[48]=99;Check(copy.Rows[0][0]==1,"parsed row aliases raw caller bytes");
        Check(copy.Rows[0][2]==Int32.MinValue&&copy.Rows[0][3]==Int32.MaxValue&&copy.Rows[1][8]==99,"signed row/order mapping lost");
        Check(NpImuGridWire.MaxBytes==480&&NpImuGridWire.HeaderBytes+12*NpImuGridWire.RowBytes==480,"bounded geometry");
        using(var writer=new BinaryWriter(File.Create(vectors)))foreach(var v in cases){writer.Write(v.Valid?1U:0U);writer.Write((uint)v.Raw.Length);writer.Write(v.Raw);}
        using(var writer=new BinaryWriter(File.Create(expected)))foreach(var v in cases.Where(v=>v.Valid)){writer.Write((uint)v.Raw.Length);writer.Write(v.Raw);}
        Console.WriteLine("PASS "+checks+" managed NSG1 checks; "+cases.Count+" shared vectors");
    }
    static void Main(string[] args){
        if(args.Length==3&&args[0]=="generate")Generate(args[1],args[2]);
        else if(args.Length==3&&args[0]=="verify"){Check(File.ReadAllBytes(args[1]).SequenceEqual(File.ReadAllBytes(args[2])),"C/C# packed golden bytes differ");Console.WriteLine("PASS exact native/managed NSG1 golden bytes");}
        else throw new ArgumentException("generate vectors expected | verify expected native");
    }
}
