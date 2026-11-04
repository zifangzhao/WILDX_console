using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using USB512_interface;
using WildNpClosedLoop;

static class NpClosedLoopWorkflowTests
{
    const string Uid="001A0004434250062039314E";const ulong Token=0x1020304050607080;
    static int checks;
    static void Need(bool b,string why){checks++;if(!b)throw new Exception(why);}
    static void U(byte[] b,int o,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,o,4);}
    static void Q(byte[] b,int o,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,o,8);}
    static void Magic(byte[] b,int o,string value){System.Text.Encoding.ASCII.GetBytes(value).CopyTo(b,o);}
    static void Crc(byte[] b){U(b,b.Length-4,NpClosedLoopEvent.Crc(b,b.Length-4));}
    static string Folder(string root,string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static NpClosedLoopSettings Settings(bool enabled=true){return new NpClosedLoopSettings{Enabled=enabled,Channels=new[]{new NpClosedLoopChannel{Probe=3,Channel=270},new NpClosedLoopChannel{Probe=3,Channel=300}}};}
    static byte[] Reply(byte[] request,uint result=0,uint groups=511)
    {
        var q=NpClosedLoopControl.Parse(request);var r=new byte[64];Magic(r,0,"NPCR");U(r,4,1);U(r,8,q.Operation);U(r,12,q.RequestId);U(r,16,result);
        U(r,20,result==0?1U|(q.Channels.Length!=0?18U:0):0);U(r,24,q.Channels.Length!=0?q.ConfigCrc:0);U(r,32,q.ApHz);
        U(r,40,2);U(r,44,7);U(r,48,1023);U(r,52,groups);Crc(r);return r;
    }
    static void Controls(string root)
    {
        foreach(string scenario in new[]{"off","enable","unsupported","legacy","lost","stale","tap","disable","camera"}){
            var s=Settings(scenario!="off"&&scenario!="disable");s.MayBeConfigured=scenario=="disable";
            s.Channels[0].Probe=1;s.Channels[1].Probe=2;
            int sent=0,saved=0;var warnings=new List<string>();
            var result=NpClosedLoopSettings.Prepare(s,false,scenario=="camera"?0U:3U,15000,(request,cancel)=>{
                sent++;cancel.ThrowIfCancellationRequested();var q=NpClosedLoopControl.Parse(request);
                if(scenario=="legacy")throw new IOException("Unsupported mailbox");
                if(scenario=="lost"&&q.Operation==1)throw new TimeoutException("Missing configure receipt");
                var reply=Reply(request,scenario=="unsupported"?1U:0,scenario=="tap"?1U:511);
                if(scenario=="stale"&&q.Operation==1){U(reply,12,q.RequestId+1);Crc(reply);}return Task.FromResult(reply);
            },CancellationToken.None,warnings.Add,value=>saved++,Folder(root,scenario)).GetAwaiter().GetResult();
            if(scenario=="off"||scenario=="camera")Need(sent==0&&saved==0&&warnings.Count==0&&result==null,"Default-off/camera touched old firmware");
            else if(scenario=="enable")Need(sent==2&&saved==2&&warnings.Count==0&&result!=null&&s.MayBeConfigured,"Enabled workflow did not confirm once");
            else if(scenario=="disable")Need(sent==2&&saved==2&&warnings.Count==0&&result!=null&&!s.MayBeConfigured&&NpClosedLoopControl.Parse(result.Request).Channels.Length==0,"Explicit disable was not confirmed");
            else if(scenario=="lost"||scenario=="stale")Need(sent==2&&saved==1&&warnings.Count==1&&result==null&&s.MayBeConfigured,"Unknown configure replayed or became a recording gate");
            else Need(sent==1&&saved==0&&warnings.Count==1&&result==null,"Unsupported optional feature sent CONFIGURE or blocked the caller");
        }
    }
    static void ProfileLimits()
    {
        // USB/SD masks from the selected firmware's qualification matrix.
        int[] qualifiedCounts={3,3,0,0};
        foreach(bool np2 in new[]{false,true}){
            uint acceptedMask=np2?7U:3U, acceptedHz=np2?20000U:15000U;
            foreach(uint mask in Enumerable.Range(0,8).Select(i=>(uint)i))
                foreach(uint hz in new[]{0U,7500U,15000U,20000U,30000U})
                    Need(NpClosedLoopSettings.CanUseProfile(np2,mask,hz)==(mask==acceptedMask&&hz==acceptedHz),
                        "Unexpected detector profile admission");

            foreach(bool sd in new[]{false,true}){
                int qualified=qualifiedCounts[(np2?2:0)+(sd?1:0)];
                foreach(int count in new[]{0,1,2,3}){
                    bool expected=count>=1&&count<=2&&(qualified&(1<<(count-1)))!=0;
                    Need(NpClosedLoopSettings.CanUseDetectors(np2,sd,acceptedMask,acceptedHz,count)==expected,
                        "Detector count/transport differs from hardware qualification");
                    Need(!NpClosedLoopSettings.CanUseDetectors(np2,sd,acceptedMask,30000U,count),
                        "Unqualified rate admitted a detector");
                    if(expected)continue;
                    var s=Settings();s.Channels=new NpClosedLoopChannel[count];s.MayBeConfigured=true;
                    var requests=new List<NpClosedLoopControl>();var warnings=new List<string>();
                    var disabled=NpClosedLoopSettings.Prepare(s,np2,acceptedMask,acceptedHz,(request,cancel)=>{
                        requests.Add(NpClosedLoopControl.Parse(request));return Task.FromResult(Reply(request));
                    },CancellationToken.None,warnings.Add,value=>{},null,sd).GetAwaiter().GetResult();
                    Need(disabled!=null&&requests.Count==2&&requests[1].Channels.Length==0&&!s.MayBeConfigured&&s.Enabled&&warnings.Count==1,
                        "Unqualified mode/count did not disable staged detection while preserving the preference");
                }
                foreach(byte filter in new byte[]{0,5,7,9}){
                    var s=Settings();foreach(var c in s.Channels)c.Filter=filter;s.MayBeConfigured=true;
                    var requests=new List<NpClosedLoopControl>();var warnings=new List<string>();
                    var disabled=NpClosedLoopSettings.Prepare(s,np2,acceptedMask,acceptedHz,(request,cancel)=>{
                        requests.Add(NpClosedLoopControl.Parse(request));return Task.FromResult(Reply(request));
                    },CancellationToken.None,warnings.Add,value=>{},null,sd).GetAwaiter().GetResult();
                    Need(disabled!=null&&requests.Count==2&&requests[1].Channels.Length==0&&warnings.Count==1,
                        "A saved unqualified filter bypassed the recording guard");
                }
            }

            foreach(bool stale in new[]{false,true}){
                var settings=Settings();settings.MayBeConfigured=stale;
                var requests=new List<NpClosedLoopControl>();
                var messages=new List<string>();
                var result=NpClosedLoopSettings.Prepare(settings,np2,acceptedMask,30000U,(request,cancel)=>{
                    requests.Add(NpClosedLoopControl.Parse(request));return Task.FromResult(Reply(request));
                },CancellationToken.None,messages.Add,value=>{}).GetAwaiter().GetResult();
                Need(requests.Count==(stale?2:0)&&messages.Count==1&&settings.Enabled,
                    "Unsupported profile must skip detection without clearing the saved preference");
                Need(!stale ? result==null : result!=null && requests[1].Operation==1 && requests[1].Channels.Length==0 && !settings.MayBeConfigured,
                    "An unqualified profile did not retire the previously staged detector");
            }

            var admitted=Settings();
            if(!np2)admitted.Channels=new[]{new NpClosedLoopChannel{Probe=1,Channel=0}};
            var notices=new List<string>();
            var accepted=NpClosedLoopSettings.Prepare(admitted,np2,acceptedMask,acceptedHz,(request,cancel)=>Task.FromResult(Reply(request)),
                CancellationToken.None,notices.Add,value=>{}).GetAwaiter().GetResult();
            bool allowed=(qualifiedCounts[np2?2:0]&(1<<(admitted.Channels.Length-1)))!=0;
            Need(allowed?accepted!=null&&NpClosedLoopControl.Parse(accepted.Request).ApHz==acceptedHz:accepted==null&&notices.Count==1,
                "Profile did not respect its qualified USB detector count");
        }
    }
    static byte[] Header(byte[] request,byte[] eventPacket,ulong sequence,bool timing=true,ulong token=Token,uint generation=0xabcd)
    {
        var h=new byte[512];U(h,52,32|512|1024);Q(h,24,sequence);U(h,252,BitConverter.ToUInt32(request,60));
        if(eventPacket!=null)Buffer.BlockCopy(eventPacket,0,h,184,64);
        if(timing){
            var body=new byte[64];uint[] words={NpAcquisitionAnchor.ReplyMagic,1,0,0x20f,generation,1000,1000,128000000,128000012,128000000,48000000,0,0,30000,1000};
            for(int i=0;i<words.Length;i++)U(body,i*4,words[i]);Crc(body);
            var r=new byte[108];U(r,0,NpUsbTimingWire.Envelope);U(r,4,1|(2U<<16));Q(r,8,token);U(r,16,generation);
            U(r,20,0x001A0004);U(r,24,0x43425006);U(r,28,0x2039314E);Q(r,32,sequence);Buffer.BlockCopy(body,0,r,40,64);Crc(r);Buffer.BlockCopy(r,0,h,396,108);
        }
        return h;
    }
    static void LiveV2(string root,byte[] golden)
    {
        byte[] request=golden.Skip(1920).Take(64).ToArray(),e=golden.Skip(2112+184).Take(64).ToArray();U(e,40,0xabcd);Crc(e);
        foreach(string scenario in new[]{"valid","delayed","token","generation","event-crc","missing"}){
            string folder=Folder(root,"v2-"+scenario);var log=new NpClosedLoopV2UsbLog(folder,Uid,Token,request);
            if(scenario=="delayed")log.Feed(Header(request,e,0,false),0);
            byte[] packet=Header(request,scenario=="delayed"?null:e,scenario=="delayed"?1UL:0UL,scenario!="missing",scenario=="token"?Token+1:Token,scenario=="generation"?0xabceU:0xabcdU);
            if(scenario=="event-crc")packet[228]^=1;
            log.Feed(packet,0);
            if(scenario=="missing")for(ulong n=1;n<18;n++)log.Feed(Header(request,null,n,false),0);
            log.Complete();
            bool valid=scenario=="valid"||scenario=="delayed";
            Need(File.Exists(Path.Combine(folder,"closed_loop_events.csv"))==valid,"Live V2 event identity/isolation failed: "+scenario);
            if(valid)Need(File.ReadAllLines(Path.Combine(folder,"closed_loop_events.csv")).Length==2,"Startup queue lost or duplicated the event");
            else Need(File.ReadAllText(Path.Combine(folder,"closed_loop_status.txt")).Contains("incomplete"),"Unconfirmed events acquired a complete log");
        }
    }
    static void LiveV1(string root,byte[] eventPacket)
    {
        byte[] request=NpClosedLoopControl.Request(1,99,15000,0,10000,new[]{new NpClosedLoopChannel{Probe=1,Channel=17}});
        var p=new byte[506];Magic(p,0,"NPSENS01");Magic(p,164,"NPUSB001");U(p,172,1);U(p,176,0xabcd);
        var e=eventPacket.Take(64).ToArray();U(e,16,BitConverter.ToUInt32(request,60));Crc(e);Buffer.BlockCopy(e,0,p,300,64);
        Buffer.BlockCopy(request,0,p,364,64);U(p,428,BitConverter.ToUInt32(request,60));U(p,436,NpClosedLoopCarrier.V1Tag|9);Crc(p);
        string folder=Folder(root,"v1-valid");using(var log=NpClosedLoopLog.FromV1Usb(folder,p,0,506)){Need(log!=null,"V1 carrier not discovered");log.ObserveV1Usb(p,0,506);log.Complete();}
        Need(File.ReadAllLines(Path.Combine(folder,"closed_loop_events.csv")).Length==2,"V1 event not exported");
        p[344]^=1;Crc(p);folder=Folder(root,"v1-bad-event");using(var log=NpClosedLoopLog.FromV1Usb(folder,p,0,506)){log.ObserveV1Usb(p,0,506);log.Complete();}
        Need(!File.Exists(Path.Combine(folder,"closed_loop_events.csv"))&&File.ReadAllText(Path.Combine(folder,"closed_loop_status.txt")).Contains("incomplete"),"Bad V1 event was not isolated");
    }
    static int Main(string[] args)
    {
        try{string root=Folder(args[0],Guid.NewGuid().ToString("N"));Controls(root);ProfileLimits();LiveV2(root,File.ReadAllBytes(args[1]));LiveV1(root,File.ReadAllBytes(args[2]));
            Console.WriteLine("PASS "+checks+" optional setup/legacy/no-replay/disable and bound V1/V2 live event-log checks; no hardware or user preference changes");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
