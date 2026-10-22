using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

// Compile with the actual NpBleClock, NpV2OnlineClock, NpSdControl and
// NpSdGeometry sources. Only correctly framed synthetic device replies are
// supplied; no BLE object, transport, or alternate calibration engine is used.
internal static class NpBleClockTests
{
    static int checks;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static uint U(byte[] bytes,int at){return BitConverter.ToUInt32(bytes,at);}
    static ulong Utc(){return (ulong)((DateTime.UtcNow.Ticks-621355968000000000L)/10);}
    static void Put(byte[] bytes,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,at,4);}

    sealed class Device
    {
        internal uint State=1,Generation;
        internal bool CorruptCrc,ChangeGeneration,WrongCommitBound;
        internal int DelayMilliseconds,Reads,Commits;
        readonly HashSet<uint> ids=new HashSet<uint>();
        readonly Stopwatch uptime=Stopwatch.StartNew();

        internal async Task<byte[]> Exchange(byte[] request)
        {
            Need(request!=null && request.Length==64 && U(request,0)==NpV2OnlineClock.RequestMagic &&
                U(request,4)==1 && U(request,60)==NpSdControl.Crc(request,60),"Actual engine request framing/CRC changed.");
            uint op=U(request,8),id=U(request,12);
            Need(id!=0 && ids.Add(id) && op<=1,"Clock request was replayed or is not READ/COMMIT.");
            if(op==0){Reads++;if(DelayMilliseconds!=0)await Task.Delay(DelayMilliseconds);}
            else Commits++;
            uint tick=unchecked((uint)uptime.ElapsedMilliseconds);
            uint generation=Generation;
            ulong utc=Generation==0?0:Utc();
            uint bound=1000;
            bool valid=Generation!=0;
            if(op==1){
                generation=unchecked(U(request,32)+1);if(generation==0)generation=1;
                ulong anchor=(ulong)U(request,20)|((ulong)U(request,24)<<32);
                utc=anchor+(ulong)unchecked(tick-U(request,16))*1000;
                bound=U(request,28)+(WrongCommitBound?1U:0U);valid=true;
                Generation=generation;
            }else if(ChangeGeneration && Reads>1)generation++;
            var reply=new byte[64];
            uint[] words={NpV2OnlineClock.ReplyMagic,1,op,id,0,tick,tick,generation,bound,
                valid?1U:0U,(uint)utc,(uint)(utc>>32),State,1000,0};
            for(int i=0;i<words.Length;i++)Put(reply,4*i,words[i]);
            Put(reply,60,NpSdControl.Crc(reply,60));
            if(CorruptCrc)reply[60]^=1;
            return reply;
        }
    }

    static Func<uint> FreshIds(){uint id=100;return ()=>++id;}
    static NpSdStatus Status(uint state,uint flags){return new NpSdStatus{State=state,Flags=flags,Mask=3,Hz=20000};}
    static async Task Refused(NpBleClock clock,Device device,string expected)
    {
        bool refused=false;
        try{await clock.Initialize(Status(1,256),device.Exchange,FreshIds());}
        catch(IOException error){refused=true;Need(error.Message.Contains(expected),"Wrong calibration refusal: "+error.Message);}
        Need(refused && !clock.Synchronized && clock.Generation==0,"Failed idle calibration was marked synchronized.");
    }

    static async Task Run()
    {
        var method=typeof(NpV2OnlineClock).GetMethod("Synchronize",BindingFlags.Static|BindingFlags.NonPublic);
        Need(method!=null && Convert.ToUInt32(method.GetParameters()[2].DefaultValue)==64,"Shared engine default must remain 64.");
        var idle=new Device();var clock=new NpBleClock();
        await clock.Initialize(Status(1,256),idle.Exchange,FreshIds());
        Need(idle.Reads==16 && idle.Commits==1,"Idle GUI initialization must use 16 READs and exactly one COMMIT.");
        Need(clock.Synchronized && clock.Generation==1 && clock.BoundMilliseconds>0 && clock.BoundMilliseconds<=500,
            "Valid idle receipt did not establish the actual engine's guarded result.");
        var quick=new Device();var quickResult=await NpV2OnlineClock.Synchronize(quick.Exchange,FreshIds(),8);
        Need(quick.Reads==8 && quick.Commits==1 && quickResult.Samples==8 && quickResult.UncertaintySeconds<=0.5,
            "Automatic V1 Start must support eight guarded observations and one COMMIT.");

        var active=new Device{State=3,Generation=7};clock=new NpBleClock();var next=FreshIds();
        await clock.Initialize(Status(3,256|4),active.Exchange,next);
        Need(active.Reads==1 && active.Commits==0 && clock.Synchronized && clock.Generation==7,
            "Active recording initialization must only Observe the existing mapping.");
        await clock.Observe(active.Exchange,next);
        Need(active.Reads==2 && active.Commits==0 && clock.Synchronized && clock.Generation==7,
            "Heartbeat Observe must remain a single read with no commit.");

        var poor=new Device{DelayMilliseconds=1100};clock=new NpBleClock();
        await Refused(clock,poor,"uncertainty too large");
        Need(poor.Reads==16 && poor.Commits==0,"Poor timing quality must refuse before the only COMMIT.");
        var crc=new Device{CorruptCrc=true};await Refused(new NpBleClock(),crc,"Invalid NP2 clock receipt");
        Need(crc.Reads==1 && crc.Commits==0,"Corrupt receipt must not be retried or committed.");
        var changed=new Device{ChangeGeneration=true};await Refused(new NpBleClock(),changed,"generation changed");
        Need(changed.Reads==2 && changed.Commits==0,"Generation drift must refuse immediately.");
        var badCommit=new Device{WrongCommitBound=true};await Refused(new NpBleClock(),badCommit,"anchor was not confirmed");
        Need(badCommit.Reads==16 && badCommit.Commits==1,"Invalid COMMIT receipt must never be replayed.");
    }

    static int Main()
    {
        try{Run().GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" actual-source GUI clock checks; idle16/singlecommit, active/heartbeat Observe, quality/CRC/generation/receipt refusals; synthetic replies, no radio.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
