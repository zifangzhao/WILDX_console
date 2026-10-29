using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using USB512_interface;

static class NpImuGridPhaseTests {
    static int checks;
    static void Check(bool yes){checks++;if(!yes)throw new Exception("NSGP check "+checks);}
    static void Reject(Action call){bool bad=false;try{call();}catch(InvalidDataException){bad=true;}Check(bad);}
    static NpImuGridPhaseRecord Record(uint flags){
        return new NpImuGridPhaseRecord{Uid=new byte[]{0,0x1d,0,6,0x59,0x42,0x50,0x0b,0x20,0x31,0x33,0x54},
            Session=7,CaptureGeneration=11,SourceEpoch=13,CounterDomain=17,Token=0x1122334455667788UL,FirstAcceptedSlot=0,
            LowerNumerator=(flags&1)!=0?17:Int64.MinValue,UpperNumerator=(flags&1)!=0?23:Int64.MinValue,
            Denominator=(flags&1)!=0?4U:0,MaximumJitter=(flags&2)!=0?5U:UInt32.MaxValue,Flags=flags};
    }
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,8);}
    static byte[] Change(byte[] source,Action<byte[]> change,bool crc=true){var b=(byte[])source.Clone();change(b);if(crc&&b.Length==84)Put(b,80,NpImuGridPhase.Crc(b));return b;}
    static void Generate(string vectors,string expected){
        var cases=new List<Tuple<byte[],bool>>();var valid=new List<byte[]>();
        Action<byte[],bool> add=(raw,ok)=>{bool parsed=true;try{NpImuGridPhase.Parse(raw);}catch(InvalidDataException){parsed=false;}Check(parsed==ok);cases.Add(Tuple.Create(raw,ok));if(ok)valid.Add(raw);};
        for(uint flags=0;flags<4;flags++){var p=Record(flags);byte[] b=NpImuGridPhase.Pack(p);add(b,true);Check(NpImuGridPhase.Match(b,p));Check(NpImuGridPhase.Read(b,p).Flags==flags);}
        var boundary=Record(3);boundary.FirstAcceptedSlot=UInt64.MaxValue-1;boundary.LowerNumerator=Int64.MaxValue;boundary.UpperNumerator=Int64.MaxValue;boundary.Denominator=UInt32.MaxValue;boundary.MaximumJitter=UInt32.MaxValue-1;add(NpImuGridPhase.Pack(boundary),true);
        var zero=Record(3);zero.LowerNumerator=zero.UpperNumerator=0;zero.MaximumJitter=0;add(NpImuGridPhase.Pack(zero),true);
        byte[] good=NpImuGridPhase.Pack(Record(3)),unknown=NpImuGridPhase.Pack(Record(0));
        add(new byte[0],false);add(new byte[83],false);add(new byte[85],false);
        for(int at=0;at<84;at++)add(Change(good,b=>b[at]^=1,false),false);
        foreach(int at in new[]{0,4,5,6,7})add(Change(good,b=>b[at]^=2),false);
        add(Change(good,b=>Array.Clear(b,8,12)),false);add(Change(good,b=>{for(int i=8;i<20;i++)b[i]=255;}),false);
        foreach(int at in new[]{20,24,28,32})add(Change(good,b=>Put(b,at,0)),false);
        add(Change(good,b=>Put64(b,36,0)),false);add(Change(good,b=>Put64(b,44,UInt64.MaxValue)),false);
        add(Change(good,b=>Put64(b,52,UInt64.MaxValue)),false);add(Change(good,b=>Put64(b,52,0x8000000000000000UL)),false);
        add(Change(good,b=>Put64(b,60,0x8000000000000001UL)),false);add(Change(good,b=>Put64(b,52,24)),false);
        add(Change(good,b=>Put(b,68,0)),false);add(Change(good,b=>Put(b,72,UInt32.MaxValue)),false);add(Change(good,b=>Put(b,76,7)),false);
        add(Change(unknown,b=>Put64(b,52,0)),false);add(Change(unknown,b=>Put64(b,60,0)),false);add(Change(unknown,b=>Put(b,68,1)),false);add(Change(unknown,b=>Put(b,72,0)),false);
        foreach(int at in new[]{8,20,24,28,32,36}){
            byte[] other=Change(good,b=>b[at]^=1);add(other,true);Check(!NpImuGridPhase.Match(other,Record(3)));Reject(()=>NpImuGridPhase.Read(other,Record(3)));
        }
        Check(!NpImuGridPhase.Match(good,null));var invalidExpected=Record(3);invalidExpected.Token=0;Check(!NpImuGridPhase.Match(good,invalidExpected));
        Reject(()=>NpImuGridPhase.Pack(null));var bad=Record(3);bad.FirstAcceptedSlot=UInt64.MaxValue;Reject(()=>NpImuGridPhase.Pack(bad));
        var before=(byte[])good.Clone();var parsedCopy=NpImuGridPhase.Parse(good);parsedCopy.Uid[0]^=1;Check(good.SequenceEqual(before));
        using(var w=new BinaryWriter(File.Create(vectors)))foreach(var item in cases){w.Write(item.Item2?1U:0);w.Write((uint)item.Item1.Length);w.Write(item.Item1);}
        using(var w=new BinaryWriter(File.Create(expected)))foreach(byte[] b in valid){w.Write((uint)b.Length);w.Write(b);}
        Console.WriteLine("PASS "+checks+" managed NSGP checks; "+cases.Count+" vectors");
    }
    static int Main(string[] args){try{
        if(args.Length==3&&args[0]=="generate"){Generate(args[1],args[2]);return 0;}
        if(args.Length==3&&args[0]=="verify"){Check(File.ReadAllBytes(args[1]).SequenceEqual(File.ReadAllBytes(args[2])));Console.WriteLine("PASS exact C/C# NSGP bytes");return 0;}
        return 2;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
