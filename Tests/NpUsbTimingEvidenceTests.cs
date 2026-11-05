using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using USB512_interface;

// No WinUSB call is permitted by this standalone model.
namespace USB512_interface {internal static class USB512WinUsbNative {
    internal static bool WinUsbSetPipePolicy(IntPtr a,byte b,uint c,uint d,ref uint e){throw new Exception("Native call in model");}
    internal static bool WinUsbAbortPipe(IntPtr a,byte b){throw new Exception("Native call in model");}
}}
class NpUsbTimingEvidenceTests {
    const string Uid="001A0004434250062039314E";const ulong Token=0x1020304050607080;
    static int checks;static string root;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static void Put64(byte[] b,int at,ulong value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,8);}
    static void UID(byte[] b,int at){Put(b,at,0x001A0004);Put(b,at+4,0x43425006);Put(b,at+8,0x2039314E);}
    static void Crc(byte[] b){Put(b,b.Length-4,NpAcquisitionAnchor.Crc(b,b.Length-4));}
    static string Hex(byte[] b){return b==null?null:BitConverter.ToString(b).Replace("-","");}
    static string Hash(string p){using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(File.ReadAllBytes(p)));}
    static string Folder(string name){string p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
    static byte[] Clock(uint id,uint ms,bool pre,bool fine,int family=2) {
        var b=new byte[64];Put(b,0,NpUsbTimingWire.Clock);Put(b,4,1);Put(b,8,id);Put(b,12,(uint)(family<<8)|1U|(pre?4U:0)|(fine?2U:0));Put(b,16,fine?17U:0);Put(b,20,pre?8U:9U);
        Put(b,24,ms);Put(b,28,ms);Put(b,32,fine?unchecked(ms*128000):0);Put(b,36,fine?unchecked(ms*128000+8):0);Put(b,40,fine?128000000U:1000U);Put(b,44,fine&&!pre?17U:0);UID(b,48);Crc(b);return b;
    }
    static byte[] Source(int family=2) {var b=new byte[64];uint[] w={NpAcquisitionAnchor.ReplyMagic,1,0,(uint)(family<<8)|15U,9,1500,1500,192000000,192000012,128000000,(uint)(20000*(family==1?780:1600)),0,0,20000,1000};for(int i=0;i<w.Length;i++)Put(b,4*i,w[i]);Crc(b);return b;}
    static byte[] Wrap(byte[] body,ushort kind,ulong sequence,ulong token=Token) {var b=new byte[108];Put(b,0,NpUsbTimingWire.Envelope);Put(b,4,1U|((uint)kind<<16));Put64(b,8,token);Put(b,16,BitConverter.ToUInt32(body,kind==2?16:20));UID(b,20);Put64(b,32,sequence);body.CopyTo(b,40);Crc(b);return b;}
    static long At(double ms){return NpHostClock.OriginTicks+(long)(ms*Stopwatch.Frequency/1000);}
    static void Evidence(string folder,bool fine=false,bool gap=false,bool truncated=false) {
        string path=Path.Combine(folder,NpUsbTimingEvidence.FileName);var lines=new List<object>();
        lines.Add(new {kind="metadata",schema=2,device_uid=Uid,device_family=2,token_hex=Token.ToString("X16"),host_epoch=NpHostClock.Epoch,clock=NpHostClock.Receipt});
        lines.Add(new {kind="request",phase="prearm",request_id=1,generation=8,send_qpc=At(1000)-Stopwatch.Frequency/20000,raw_hex=(string)null});
        lines.Add(new {kind="record",outcome="accepted",receive_qpc=At(1000)+Stopwatch.Frequency/20000,container_sequence=UInt64.MaxValue.ToString(),raw_hex=Hex(Wrap(Clock(1,1000,true,fine),1,UInt64.MaxValue))});
        lines.Add(new {kind="record",outcome="accepted",receive_qpc=At(1600),container_sequence="40",raw_hex=Hex(Wrap(Source(),2,40))});
        if(gap)lines.Add(new {kind="request",phase="active",request_id=2,generation=9,send_qpc=At(1800),raw_hex=Hex(NpUsbTimingWire.Query(2,Token,9))});
        uint id=gap?3U:2U;
        lines.Add(new {kind="request",phase="active",request_id=id,generation=9,send_qpc=At(2000)-Stopwatch.Frequency/20000,raw_hex=Hex(NpUsbTimingWire.Query(id,Token,9))});
        lines.Add(new {kind="record",outcome="accepted",receive_qpc=At(2000)+Stopwatch.Frequency/20000,container_sequence="42",raw_hex=Hex(Wrap(Clock(id,2000,false,fine),1,42))});
        lines.Add(new {kind="close",error=(string)null});File.WriteAllLines(path,lines.Select(x=>Json.Serialize(x)));
        if(truncated)File.AppendAllText(path,"{\"kind\":");
    }
    static void Correction(string folder) {
        var snap=new byte[140];Put(snap,0,0x31534e53);Put(snap,4,1);Put(snap,8,1);Put(snap,12,1);Put(snap,16,1500);Put(snap,20,1);Put(snap,32,1500);Put(snap,44,25600);
        using(var j=new NpImuSampleJournal(folder,Uid))j.Observe(snap,0,1500,1,null,null,0,0);
        NpImuTimeCorrection.Correct(folder);
    }
    static byte[] Info(int family,ulong token) {var b=new byte[64];Put(b,0,NpUsbTimingWire.Info);Put(b,4,1U|((token==0?5U:7U)<<16));Put64(b,8,token);UID(b,16);Put(b,28,1000);Put(b,32,8);Put(b,36,3);Put(b,40,108);Put(b,44,1);Put(b,48,family==1?192U:396U);Put(b,52,1);Crc(b);return b;}
    static void Pump(int family,Assembly app=null) {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        string p=Folder((app==null?"pump-v":"compiled-pump-v")+family);ulong token=0;int controls=0,writes=0;object session=null;var sent=new ManualResetEvent(false);
        Action<string,object[]> call=(name,values)=>session.GetType().GetMethod(name,all).Invoke(session,values);
        Action<byte[],ulong> feed=(body,sequence)=>{
            var wrapped=Wrap(body,BitConverter.ToUInt32(body,0)==NpAcquisitionAnchor.ReplyMagic?(ushort)2:(ushort)1,sequence,token);
            if(family==2){var block=new byte[512];Put(block,52,512);Put64(block,24,sequence);wrapped.CopyTo(block,396);call("FeedV2",new object[]{block,0,Stopwatch.GetTimestamp()});}
            else {var block=new byte[506];System.Text.Encoding.ASCII.GetBytes("NPSENS01").CopyTo(block,0);Put(block,8,(uint)sequence);Put(block,16,140);System.Text.Encoding.ASCII.GetBytes("NPUSB001").CopyTo(block,164);Put(block,172,1);Put(block,188,15);wrapped.CopyTo(block,192);Put(block,160,NpUsbSensors.Crc(block,0,160));Put(block,502,NpUsbSensors.Crc(block,0,502));call("FeedV1",new object[]{block,0,506,Stopwatch.GetTimestamp()});}
        };
        Func<byte[]> info=()=>{controls++;return Info(family,token);};Action<byte[]> enable=b=>{controls++;token=BitConverter.ToUInt64(b,8);};Func<uint,byte[]> prearm=id=>{controls++;return Wrap(Clock(id,1000,true,false,family),1,UInt64.MaxValue,token);};
        Action<byte[]> write=b=>{Interlocked.Increment(ref writes);uint id=BitConverter.ToUInt32(b,8);feed(Clock(id,1001,false,false,family),42);sent.Set();};
        if(app==null)session=NpUsbTimingSession.Prepare(p,Uid,family,info,enable,prearm,()=>new NpUsbTimingOut(write));
        else {
            Type output=app.GetType("USB512_interface.NpUsbTimingOut",true),owner=app.GetType("USB512_interface.NpUsbTimingSession",true);
            var factory=Expression.Lambda(typeof(Func<>).MakeGenericType(output),Expression.New(output.GetConstructor(all,null,new[]{typeof(Action<byte[]>)},null),Expression.Constant(write))).Compile();
            var prepare=owner.GetMethod("Prepare",all);var arguments=new List<object>{p,Uid,family,info,enable,prearm,factory};
            if(prepare.GetParameters().Length==8)arguments.Add(null); // Optional v2 probe stays off in the legacy regression.
            session=prepare.Invoke(null,arguments.ToArray());
        }
        Check((bool)session.GetType().GetProperty("Enabled",all).GetValue(session),"Fake negotiated session did not enable");call("Start",new object[0]);feed(Source(family),40);
        Check(sent.WaitOne(1500),"No active timing request after startup receipt");call("StopQueries",new object[0]);((IDisposable)session).Dispose();
        Check(controls==4&&writes==1,"Unexpected live EP0 query or duplicate pending OUT");
        var history=NpUsbTimingEvidence.Read(p);Check(history.Points.Count==2&&history.Generation==9,"Actual session did not retain matching pre/active evidence");
        Check(session.GetType().GetProperty("Error",all).GetValue(session)==null&&File.ReadAllText(Path.Combine(p,"usb_timing_status.json")).Contains("\"dropped_records\":0"),"Optional owner did not close cleanly");
    }
    static void Main(string[] args) {
        root=FolderRoot(args[0]);
        foreach(bool fine in new[]{false,true}) {
            string p=Folder(fine?"fine":"ms");Evidence(p,fine);string hash=Hash(Path.Combine(p,NpUsbTimingEvidence.FileName));Correction(p);
            using(var b=new BinaryReader(File.OpenRead(Path.Combine(p,NpImuTimeCorrection.TimeName))))Check(Math.Abs(b.ReadDouble()-(NpHostClock.OriginUtcUs+1500000)/1e6)<.000003,"In-capture IMU clock map wrong");
            var t=new NpAcquisitionTiming(p,Uid,2);t.Observe(()=>{throw new System.ComponentModel.Win32Exception(31,"Terminal query unavailable");});
            t.Save(new[]{new NpFirstSampleRow(1,"ap",508,20000).ForData("neuropixels_ap.dat",384,100,20000)});
            var doc=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(p,"first_sample_timestamps.json")));var start=Json.ConvertToType<Dictionary<string,object>>(doc["recording_start"]);
            Check((bool)start["pc_time_valid"]&&!(bool)start["hardware_start_locked"],"Interrupted Stop lost valid source receipt or claimed physical lock");
            var rows=Json.ConvertToType<Dictionary<string,object>[]>(doc["rows"]);double lo=Convert.ToDouble(rows[0]["pc_us_lower"]),hi=Convert.ToDouble(rows[0]["pc_us_upper"]);
            Check(lo<=NpHostClock.OriginUtcUs+1500015.875&&hi>=NpHostClock.OriginUtcUs+1500015.875,"Conditional first-row interval lost truth");
            if(fine)Check((string)rows[0]["pc_mapping"]=="dwt_cycle_affine_interval"&&hi-lo<500,"Fine provider degraded to milliseconds");
            Check(Hash(Path.Combine(p,NpUsbTimingEvidence.FileName))==hash,"Postprocessing changed raw timing evidence");
        }
        string gap=Folder("missing-query");Evidence(gap,gap:true);Correction(gap);
        using(var b=new BinaryReader(File.OpenRead(Path.Combine(gap,NpImuTimeCorrection.TimeName))))Check(Double.IsNaN(b.ReadDouble()),"Missing query interval silently filled");
        string partial=Folder("partial-tail");Evidence(partial,truncated:true);Check(NpUsbTimingEvidence.Read(partial).Points.Count==2,"Truncated last line erased valid prefix");
        string noSource=Folder("no-source");Evidence(noSource);var path=Path.Combine(noSource,NpUsbTimingEvidence.FileName);
        File.WriteAllLines(path,File.ReadAllLines(path).Where(l=>!l.Contains("\"container_sequence\":\"40\"")));Check(NpUsbTimingEvidence.Read(noSource).Points.Count==0,"Prearm generation bridged without source-generation evidence");
        Pump(1);Pump(2);
        if(args.Length>1){var app=Assembly.LoadFrom(Path.GetFullPath(args[1]));Pump(1,app);Pump(2,app);}
        Console.WriteLine("PASS "+checks+" actual session/evidence/IMU/start checks; fake endpoints and generated clocks only.");
    }
    static string FolderRoot(string parent){string p=Path.Combine(Path.GetFullPath(parent),"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
}
