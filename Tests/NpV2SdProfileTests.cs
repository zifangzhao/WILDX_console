using System;
using System.IO;
using System.Reflection;
using CE32_console;

// Compile with the actual NpV2SdControl, NpSdControl and NpSdGeometry sources.
// Pure codec/geometry checks: no Form, transport, discovery or hardware.
internal static class NpV2SdProfileTests
{
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static uint U(byte[] b,int offset){return BitConverter.ToUInt32(b,offset);}
    static void Put(byte[] b,int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,4);}
    static uint Crc(byte[] b)
    {
        uint crc=UInt32.MaxValue;
        for(int i=0;i<60;i++){
            crc^=b[i];
            for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)==0?0U:0xedb88320U);
        }
        return ~crc;
    }
    static void Seal(byte[] b){Put(b,60,Crc(b));}
    static void Equal(byte[] actual,byte[] expected,string message)
    {
        Need(actual.Length==expected.Length,message+" length");
        for(int i=0;i<actual.Length;i++)Need(actual[i]==expected[i],message+" byte "+i);
    }
    static void Refuse<T>(Action action,string message) where T:Exception
    {
        bool refused=false;try{action();}catch(T){refused=true;}
        Need(refused,message);
    }
    static byte[] ExpectedRequest(uint op,uint hz,uint blocks,ulong utc,uint word4)
    {
        var b=new byte[64];uint[] words={0x32444c4e,2,op,91,word4,hz,blocks,(uint)utc,(uint)(utc>>32)};
        for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);Seal(b);return b;
    }
    static NpSdStatus Status(uint mask,uint hz)
    {
        return new NpSdStatus{Operation=0,RequestId=91,State=1,Mask=mask,Hz=hz,
            Chunks=8,Start=8192,End=32768,Capacity=501301248,Flags=128|512|8192,LastId=90};
    }
    static byte[] Reply(NpSdStatus s)
    {
        var b=new byte[64];uint[] words={0x32524c4e,2,s.Operation,s.RequestId,s.Error,s.State,s.Fault,
            s.Mask,s.Hz,s.Chunks,s.Start,s.End,s.Capacity,s.Flags,s.LastId};
        for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);Seal(b);return b;
    }
    static byte[] LegacyTelemetry(NpSdStatus s)
    {
        var b=new byte[24];uint[] words={1,s.State,s.Hz,s.Chunks,s.Flags,s.Fault};
        for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);return b;
    }
    static void Requests()
    {
        // The unchanged headless runner resolves this exact five-argument method.
        var method=typeof(NpV2SdControl).GetMethod("Request",BindingFlags.NonPublic|BindingFlags.Static,
            null,new[]{typeof(uint),typeof(uint),typeof(uint),typeof(uint),typeof(ulong)},null);
        Need(method!=null,"Five-parameter reflective request contract disappeared.");
        Equal((byte[])method.Invoke(null,new object[]{2U,91U,20000U,8U,0UL}),
            ExpectedRequest(2,20000,8,0,3),"Legacy reflection frame");
        for(uint op=0;op<=8;op++){
            bool profile=op==1||op==2||op==4;uint hz=profile?20000U:0U,blocks=profile?8U:0U;
            ulong utc=op==4?0x123456789abcdef0UL:0UL;
            Equal(NpV2SdControl.Request(op,91,hz,blocks,utc),
                ExpectedRequest(op,hz,blocks,utc,profile?3U:0U),"Legacy dual command "+op);
            if(!profile)Need(U(NpV2SdControl.Request(op,91,0,0,0,7),16)==0,
                "A non-profile command acquired a probe mask.");
        }
        foreach(uint mask in new[]{3U,7U})foreach(uint hz in mask==3?new[]{20000U,30000U}:new[]{15000U,20000U}){
            foreach(uint op in new[]{1U,2U,4U}){
                ulong utc=op==4?123456UL:0UL;
                Equal(NpV2SdControl.Request(op,91,hz,8,utc,mask),
                    ExpectedRequest(op,hz,8,utc,mask),"Closed profile command");
            }
            Need(U(NpV2SdControl.Request(2,91,hz,0,0,mask),24)==0,"Continuous Start was changed.");
        }
        uint[,] bad={{0,20000},{1,20000},{2,20000},{4,20000},{8,20000},{3,15000},{3,25000},{3,0},{7,30000},{7,25000}};
        for(int i=0;i<bad.GetLength(0);i++)foreach(uint op in new[]{1U,2U,4U}){
            uint mask=bad[i,0],hz=bad[i,1];
            Refuse<ArgumentException>(()=>NpV2SdControl.Request(op,91,hz,8,0,mask),"Unsupported request pair admitted.");
        }
        Refuse<ArgumentException>(()=>NpV2SdControl.Request(1,91,15000),"Legacy wrapper changed to triple.");
        Refuse<ArgumentException>(()=>NpV2SdControl.Request(0,0),"Zero request ID admitted.");
        Refuse<ArgumentException>(()=>NpV2SdControl.Request(9,91),"Unknown opcode admitted.");
        Refuse<ArgumentException>(()=>NpV2SdControl.Request(2,91,20000,8,1),"UTC accepted outside scheduled Start.");
        for(uint mode=1;mode<=3;mode++)
            Equal(NpV2SdControl.ModeRequest(91,mode),ExpectedRequest(5,0,0,0,mode),"Existing mode destination word");
        Equal(NpV2SdControl.StorageRequest(91,false),ExpectedRequest(6,0,0,0,0),"Existing storage mount");
        Equal(NpV2SdControl.StorageRequest(91,true),ExpectedRequest(7,0,0,0,0x3850534e),"Existing format key");
    }
    static void Profiles(uint mask,uint hz,uint ticks,uint sixtySecondBlocks)
    {
        var s=Status(mask,hz);s.Chunks=sixtySecondBlocks;
        foreach(uint state in new[]{0U,1U,2U,3U,4U,5U,6U,7U,255U}){
            s.State=state;
            var parsed=NpV2SdControl.Parse(Reply(s),0,91);
            Need(parsed.Mask==mask && parsed.Hz==hz && parsed.State==state && parsed.LastId==90,
                "Actual closed status tuple/identity was changed.");
            string description=NpV2SdControl.Describe(parsed);
            Need(description.Contains(mask==3?"probes 1 + 2,":"probes 1 + 2 + 3,") &&
                !description.Contains("not supported by this firmware"),"Description invented a profile/capability.");
            var telemetry=NpV2SdControl.Telemetry(parsed);
            Need(telemetry.Length==28 && U(telemetry,0)==2 && U(telemetry,24)==mask,"Host telemetry v2 layout wrong.");
            var read=NpV2SdControl.ReadTelemetry(telemetry);
            Need(read.Mask==mask && read.Hz==hz && read.State==state && read.Chunks==s.Chunks &&
                read.Flags==s.Flags && read.Fault==s.Fault,"Host telemetry v2 lost actual metadata.");
        }
        s.State=1;
        Need(NpV2SdControl.Blocks(s,hz,60)==sixtySecondBlocks,"Wrong finite profile/RU rounding.");
        Need(NpV2SdControl.RecordedSeconds(s)==59,"Wrong actual profile recorded duration.");
        s.Chunks=UInt32.MaxValue;
        Need(NpV2SdControl.RecordedSeconds(s)==(uint)((ulong)UInt32.MaxValue*ticks/((ulong)hz*1600)),
            "Recorded source ticks narrowed or used a different profile.");
        Need(Math.Abs(NpV2SdControl.RecordedMiB(s)-UInt32.MaxValue*(46592.0/1048576))<0.001,
            "Stored block-byte geometry changed.");
        Need(NpV2SdControl.Blocks(s,hz,0)==0,"Admitted continuous recording no longer returns zero.");
        s.Flags&=~512U;
        Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,0),"Continuous capability was invented.");
        s.Flags|=512U;
        foreach(uint flag in new[]{8U,65536U,131072U}){
            s.Flags|=flag;Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,60),"Busy/full/recovery recording admitted.");s.Flags&=~flag;
        }
        s.Fault=5;Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,60),"Faulted recorder admitted.");s.Fault=0;
        s.State=3;Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,60),"Active recorder admitted.");s.State=1;
        Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,3601),"Host duration bound relaxed.");
        s.Capacity=16384;Refuse<IOException>(()=>NpV2SdControl.Blocks(s,hz,60),"Full card admitted.");
    }
    static void RefusalsAndLegacyTelemetry()
    {
        var s=Status(3,20000);var valid=Reply(s);
        foreach(int offset in new[]{0,4,8,12,60}){
            var damaged=(byte[])valid.Clone();Put(damaged,offset,U(damaged,offset)^1U);if(offset!=60)Seal(damaged);
            Refuse<IOException>(()=>NpV2SdControl.Parse(damaged,0,91),"Identity/CRC mismatch admitted.");
        }
        Refuse<IOException>(()=>NpV2SdControl.Parse(null,0,91),"Missing reply admitted.");
        Refuse<IOException>(()=>NpV2SdControl.Parse(new byte[63],0,91),"Short reply admitted.");
        Refuse<IOException>(()=>NpV2SdControl.Parse(new byte[65],0,91),"Long reply admitted.");
        uint[,] bad={{0,20000},{1,20000},{2,20000},{4,20000},{8,20000},{3,15000},{7,30000},{7,0}};
        for(int i=0;i<bad.GetLength(0);i++){
            s=Status(bad[i,0],bad[i,1]);var body=Reply(s);
            Refuse<IOException>(()=>NpV2SdControl.Parse(body,0,91),"CRC-valid unsupported status admitted.");
            Refuse<IOException>(()=>NpV2SdControl.Telemetry(s),"Unsupported telemetry encoded.");
            Refuse<IOException>(()=>NpV2SdControl.Describe(s),"Unsupported profile described as a known one.");
            Refuse<IOException>(()=>NpV2SdControl.RecordedSeconds(s),"Unsupported profile assigned source ticks.");
            Refuse<IOException>(()=>NpV2SdControl.Blocks(s,s.Hz,60),"Unsupported profile assigned blocks.");
            var telemetry=new byte[28];Put(telemetry,0,2);Put(telemetry,4,1);Put(telemetry,8,s.Hz);Put(telemetry,24,s.Mask);
            Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(telemetry),"Unsupported telemetry pair admitted.");
        }
        s=Status(3,20000);s.Flags&=~128U;
        Refuse<IOException>(()=>NpV2SdControl.Parse(Reply(s),0,91),"Missing NP2 capability admitted.");
        s=Status(7,20000);s.State=8;
        Refuse<IOException>(()=>NpV2SdControl.Parse(Reply(s),0,91),"Unknown recorder state admitted.");
        Refuse<IOException>(()=>NpV2SdControl.Telemetry(s),"Unknown telemetry state encoded.");
        var unknownState=NpV2SdControl.Telemetry(Status(7,20000));Put(unknownState,4,8);
        Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(unknownState),"Unknown telemetry state admitted.");
        foreach(uint hz in new[]{20000U,30000U}){
            s=Status(3,hz);s.State=255;s.Fault=5;var legacy=LegacyTelemetry(s);
            var read=NpV2SdControl.ReadTelemetry(legacy);
            Need(read.Mask==3 && read.Hz==hz && read.State==255 && read.Fault==5 && read.Chunks==8,
                "Legacy 24-byte/v1 telemetry compatibility lost.");
            var modern=NpV2SdControl.Telemetry(read);
            Need(modern.Length==28 && U(modern,0)==2 && U(modern,24)==3,"Legacy telemetry did not upgrade truthfully.");
            Put(legacy,8,15000);Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(legacy),"Legacy telemetry invented a triple mask.");
        }
        foreach(var shape in new[]{new byte[0],new byte[23],new byte[24],new byte[27],new byte[28],new byte[29]})
            Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(shape),"Malformed telemetry shape/version admitted.");
        Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(null),"Missing telemetry admitted.");
        var v1wrongLength=new byte[28];Put(v1wrongLength,0,1);
        Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(v1wrongLength),"Version1 accepted version2 length.");
        var v2wrongLength=new byte[24];Put(v2wrongLength,0,2);
        Refuse<IOException>(()=>NpV2SdControl.ReadTelemetry(v2wrongLength),"Version2 accepted version1 length.");
    }
    static int Main()
    {
        try{
            Requests();Profiles(3,20000,61440,31248);Profiles(3,30000,61440,46872);
            Profiles(7,15000,40960,35156);Profiles(7,20000,40960,46872);RefusalsAndLegacyTelemetry();
            Console.WriteLine("PASS "+checks+" actual-source closed SD profiles, legacy requests/telemetry, CRC, geometry and refusal checks; no radio/hardware qualification.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
