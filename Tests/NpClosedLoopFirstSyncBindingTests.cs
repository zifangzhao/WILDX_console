using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using USB512_interface;
using WildNpClosedLoop;

// Actual source parsers/logger, no form, saved settings, transport or hardware.
static class NpClosedLoopFirstSyncBindingTests
{
    const string Uid="001A0004434250062039314E";
    const ulong Token=0x1020304050607080UL;
    static int checks;static string root;
    static void Need(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void Q(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
    static void Seal(byte[] b){Put(b,b.Length-4,NpFirstSyncWire.Crc(b,b.Length-4));}
    static byte[] Config(){return NpClosedLoopControl.Request(1,7,20000,0,1000,new[]{new NpClosedLoopChannel{Probe=3,Channel=256}});}
    static byte[] Wrap(byte[] body,ushort kind,ulong seq,string uid,ulong token,uint generation){
        var b=new byte[108];Put(b,0,NpUsbTimingWire.Envelope);Put(b,4,1U|((uint)kind<<16));Q(b,8,token);Put(b,16,generation);
        var u=NpFirstSyncUsb.UidWords(uid);for(int i=0;i<3;i++)Put(b,20+4*i,u[i]);Q(b,32,seq);body.CopyTo(b,40);Seal(b);return b;
    }
    static byte[] Anchor(uint generation=9,uint hz=20000,ulong origin=0){
        var b=new byte[64];uint[] w={0x5241504e,1,77,0x21f,generation,1000,1000,128000000,128000012,128000000,hz*1600,0,0,hz,1000};
        for(int i=0;i<w.Length;i++)Put(b,4*i,w[i]);Q(b,44,origin);Seal(b);return b;
    }
    static byte[] Info(string uid,ulong token,uint generation){
        var rawInfo=new byte[64];Put(rawInfo,0,NpUsbTimingWire.Info);Put(rawInfo,4,token==0?0x50002U:0x70002U);Q(rawInfo,8,token);
        var u=NpFirstSyncUsb.UidWords(uid);for(int i=0;i<3;i++)Put(rawInfo,16+4*i,u[i]);
        Put(rawInfo,28,1000);Put(rawInfo,32,generation-1);Put(rawInfo,36,3);Put(rawInfo,40,108);Put(rawInfo,44,1);Put(rawInfo,48,396);Put(rawInfo,52,1);Put(rawInfo,56,15);Seal(rawInfo);
        return rawInfo;
    }
    static NpFirstSyncUsbFineRoot Source(string uid=Uid,ulong token=Token,uint generation=9,byte mask=7,uint hz=20000,ulong origin=0){
        var rawInfo=Info(uid,token,generation);
        var info=NpFirstSyncUsb.ParseInfo(rawInfo,uid,2);var receipt=NpFirstSyncWireTests.Fixture(2,false,hz,mask);
        Put(receipt,16,generation);Put(receipt,20,generation);Seal(receipt); // SourceEpoch remains independently 11.
        var assembled=new NpFirstSyncUsbAssembler(info,token,generation);
        for(int i=0;i<7;i++){
            var body=new byte[64];Put(body,0,NpFirstSyncUsb.PageMagic);body[4]=1;body[5]=(byte)i;body[6]=7;Put(body,8,BitConverter.ToUInt32(receipt,324));
            Buffer.BlockCopy(receipt,i*48,body,12,i==6?40:48);Seal(body);
            if(!assembled.Add(Wrap(body,4,(ulong)i+1,uid,token,generation),(ulong)i+1))throw new Exception(assembled.Error);
        }
        var envelope=NpFirstSyncUsb.Anchor(Anchor(generation,hz,origin),receipt,uid,token,generation,8);
        if(!assembled.FeedAnchor(envelope,8))throw new Exception(assembled.FineError);
        return new NpFirstSyncUsbFineRoot(rawInfo,assembled,envelope,8);
    }
    static byte[] Event(byte[] config,uint seq,uint session=9,uint epoch=11){
        var b=new byte[64];System.Text.Encoding.ASCII.GetBytes("NPCL").CopyTo(b,0);b[4]=1;b[5]=3;b[7]=1;Put(b,8,session);Put(b,12,seq);Put(b,16,BitConverter.ToUInt32(config,60));
        b[20]=3;b[23]=1;Q(b,24,seq*1600);Q(b,32,seq*1600+1599);Put(b,40,epoch);Seal(b);return b;
    }
    static byte[] Header(byte[] config,uint seq,byte[] packet=null,uint drops=0,byte[] timing=null){
        var b=new byte[512];Put(b,52,32|512|1024);Q(b,24,seq);Put(b,248,drops);Put(b,252,BitConverter.ToUInt32(config,60));
        if(packet!=null)packet.CopyTo(b,184);if(timing!=null)timing.CopyTo(b,396);return b;
    }
    static void Feed(NpClosedLoopV2UsbLog log,byte[] header){var original=(byte[])header.Clone();log.Feed(header,0);Need(header.SequenceEqual(original),"Optional event logger modified neural header");}
    static string Folder(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static void Exported(string folder,int events){
        Need(File.Exists(Path.Combine(folder,"closed_loop_events.csv")),"Bound current source did not export");
        Need(File.ReadAllLines(Path.Combine(folder,"closed_loop_events.csv")).Length==events+1,"Events missing or duplicated");
    }
    static void Rejected(string folder){
        Need(!File.Exists(Path.Combine(folder,"closed_loop_events.csv")),"Unbound/mismatched source exported events");
        Need(File.ReadAllText(Path.Combine(folder,"closed_loop_status.txt")).Contains("incomplete"),"Unbound export did not report incomplete");
    }
    static void Current(){
        var config=Config();NpFirstSyncUsbFineRoot source=null;string p=Folder("delayed-root");
        var log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1)));for(uint i=2;i<130;i++)Feed(log,Header(config,i));
        source=Source();Feed(log,Header(config,130,Event(config,2)));log.Complete();Exported(p,2);
        p=Folder("root-at-complete");source=null;log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1)));source=Source();log.Complete();Exported(p,1);
        p=Folder("no-events");source=null;log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        for(uint i=1;i<70;i++)Feed(log,Header(config,i));source=Source();log.Complete();Exported(p,0);
        Need(File.ReadAllText(Path.Combine(p,"closed_loop_status.txt")).Contains("not evidence of zero detections"),"Empty transport claimed zero detections");
        p=Folder("current-no-legacy-downgrade");source=null;log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1),0,Wrap(Anchor(),2,1,Uid,Token,9)));log.Complete();Rejected(p);
        p=Folder("late-invalidation");source=Source();log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1)));source=null;Feed(log,Header(config,2,Event(config,2)));log.Complete();Rejected(p);
        p=Folder("invalidation-at-complete");source=Source();log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1)));source=null;log.Complete();Rejected(p);
        p=Folder("replaced-generation");source=Source();log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
        Feed(log,Header(config,1,Event(config,1)));source=Source(generation:10);log.Complete();Rejected(p);
    }
    static void Mismatches(){
        string[] modes={"uid","token","generation","mask","rate","origin","epoch","missing","drop-rollback","overflow"};
        foreach(string mode in modes){
            var config=Config();string p=Folder("mismatch-"+mode);NpFirstSyncUsbFineRoot source=
                mode=="uid"?Source(uid:"001A0005434250062039314E"):mode=="token"?Source(token:Token+1):mode=="generation"?Source(generation:10):
                mode=="mask"?Source(mask:3):mode=="rate"?Source(hz:30000):mode=="origin"?Source(origin:1):mode=="missing"||mode=="drop-rollback"||mode=="overflow"?null:Source();
            var log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
            if(mode=="drop-rollback"){Feed(log,Header(config,1,null,1));Feed(log,Header(config,2));source=Source();}
            else if(mode=="overflow"){for(uint i=1;i<=17;i++)Feed(log,Header(config,i,Event(config,i)));source=Source();}
            else Feed(log,Header(config,1,Event(config,1,9,mode=="epoch"?12U:11U)));
            log.Complete();Rejected(p);
        }
    }
    static void Legacy(){
        var config=Config();string p=Folder("legacy");var log=new NpClosedLoopV2UsbLog(p,Uid,Token,config);
        Feed(log,Header(config,1,Event(config,1,9,9),0,Wrap(Anchor(),2,1,Uid,Token,9)));log.Complete();Exported(p,1);
        p=Folder("legacy-root-is-not-source");log=new NpClosedLoopV2UsbLog(p,Uid,Token,config);
        Feed(log,Header(config,1,Event(config,1)));log.Complete();Rejected(p);
    }
    static byte[] SelectedEvent(byte[] config,uint sequence,int lane){
        var packet=Event(config,sequence);packet[6]=(byte)lane;packet[20]=2;packet[22]=(byte)lane;packet[23]=0;Seal(packet);return packet;
    }
    static void SelectedSources(){
        foreach(int count in new[]{1,2}){
            var channels=Enumerable.Range(0,count).Select(i=>new NpClosedLoopChannel{Probe=2,Channel=(ushort)i}).ToArray();
            var config=NpClosedLoopControl.Request(1,17,20000,0,1000,channels);
            string p=Folder("selected-"+count);NpFirstSyncUsbFineRoot source=null;
            var log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
            for(int lane=0;lane<count;lane++)Feed(log,Header(config,(uint)lane+1,SelectedEvent(config,(uint)lane+1,lane)));
            source=Source();Feed(log,Header(config,3));log.Complete();Exported(p,count);
            var bytes=File.ReadAllBytes(Path.Combine(p,"closed_loop_events.npcl"));
            for(int lane=0;lane<count;lane++){
                var e=NpClosedLoopEvent.Parse(bytes.Skip(lane*64).Take(64).ToArray());
                Need(e.Probe==2&&e.Channel==lane&&e.Lane==lane&&e.Session==9&&e.SourceEpoch==11,
                    "Selected channel/session/independent source epoch changed");
            }
            p=Folder("selected-wrong-probe-"+count);log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
            Feed(log,Header(config,1,Event(config,1)));log.Complete();Rejected(p);
            p=Folder("selected-wrong-epoch-"+count);log=new NpClosedLoopV2UsbLog(p,Uid,Token,config,7,()=>source);
            var wrong=SelectedEvent(config,1,0);Put(wrong,40,12);Seal(wrong);
            Feed(log,Header(config,1,wrong));log.Complete();Rejected(p);
        }
    }
    static void ActualTimingOwner(){
        foreach(bool invalidate in new[]{false,true}){
            string p=Folder(invalidate?"actual-owner-invalidated":"actual-owner-drained");ulong token=0;int writes=0;
            var timing=NpUsbTimingSession.Prepare(p,Uid,2,()=>{throw new Exception("Unexpected legacy negotiation");},b=>token=BitConverter.ToUInt64(b,8),
                id=>{
                    var b=new byte[64];Put(b,0,NpUsbTimingWire.Clock);Put(b,4,1);Put(b,8,id);Put(b,12,0x205);Put(b,20,8);Put(b,24,1000);Put(b,28,1000);Put(b,40,1000);
                    var u=NpFirstSyncUsb.UidWords(Uid);for(int i=0;i<3;i++)Put(b,48+4*i,u[i]);Seal(b);return Wrap(b,1,UInt64.MaxValue,Uid,token,8);
                },()=>new NpUsbTimingOut(b=>Interlocked.Increment(ref writes)),()=>Info(Uid,token,9));
            Need(timing.Enabled&&timing.FirstSyncEnabled,"Actual timing owner did not negotiate current service");
            var config=Config();var log=new NpClosedLoopV2UsbLog(p,Uid,timing.Token,config,7,()=>timing.FineRoot);
            var source=Source(token:timing.Token);var receipt=source.Receipt;timing.Start();
            for(int i=0;i<7;i++){
                var body=new byte[64];Put(body,0,NpFirstSyncUsb.PageMagic);body[4]=1;body[5]=(byte)i;body[6]=7;Put(body,8,BitConverter.ToUInt32(receipt,324));
                Buffer.BlockCopy(receipt,i*48,body,12,i==6?40:48);Seal(body);
                var b=Header(config,(uint)i+1,i==0?Event(config,1):null,0,Wrap(body,4,(ulong)i+1,Uid,timing.Token,9));
                timing.FeedV2(b,0,Stopwatch.GetTimestamp());Feed(log,b);
            }
            var rootHeader=Header(config,8,null,0,source.Envelope);timing.FeedV2(rootHeader,0,Stopwatch.GetTimestamp());Feed(log,rootHeader);
            if(invalidate){
                var watch=Stopwatch.StartNew();while(timing.FineRoot==null&&watch.ElapsedMilliseconds<2500)Thread.Sleep(1);
                Need(timing.FineRoot!=null,"Actual timing owner did not publish validated root");Feed(log,Header(config,9));
                var changed=(byte[])receipt.Clone();Put(changed,24,12);Seal(changed);
                var body=new byte[64];Put(body,0,NpFirstSyncUsb.PageMagic);body[4]=1;body[6]=7;Put(body,8,BitConverter.ToUInt32(changed,324));Buffer.BlockCopy(changed,0,body,12,48);Seal(body);
                var b=Header(config,10,null,0,Wrap(body,4,10,Uid,timing.Token,9));timing.FeedV2(b,0,Stopwatch.GetTimestamp());Feed(log,b);
            }
            timing.Dispose(); // Exact production order: drain/join first, then finalize CL.
            log.Complete();Need(writes==0,"Current source binding added active USB commands");
            if(invalidate){Need(timing.FineRoot==null,"Conflicting source did not invalidate owner context");Rejected(p);}
            else{Need(timing.FineRoot!=null&&timing.FirstSyncQualified&&!timing.RtcQualified,"Actual source proof/independent RTC scope changed");Exported(p,1);}
        }
    }
    static int Main(string[] args){try{
        root=Path.Combine(Path.GetFullPath(args[0]),"current-source-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);
        Current();Mismatches();Legacy();ActualTimingOwner();SelectedSources();Console.WriteLine("PASS "+checks+" actual current/legacy/P2 closed-loop source binding checks; no hardware");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
