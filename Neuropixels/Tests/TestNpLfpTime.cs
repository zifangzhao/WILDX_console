using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Execute the actual optimized console's file/queue/writer paths. No USB calls.
internal static class TestNpLfpTime {
    const BindingFlags I=BindingFlags.Instance|BindingFlags.NonPublic,S=BindingFlags.Static|BindingFlags.NonPublic;
    static Assembly app;static int checks;static string root;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),I|BindingFlags.Public,null,args,null);}
    static object Get(object obj,string name){var field=obj.GetType().GetField(name,I);return field!=null?field.GetValue(obj):obj.GetType().GetProperty(name,I).GetValue(obj,null);}
    static void Set(object obj,string name,object value){obj.GetType().GetField(name,I).SetValue(obj,value);}
    static object Call(object obj,string name,params object[] args){foreach(var method in obj.GetType().GetMethods(I))if(method.Name==name && method.GetParameters().Length==args.Length)return method.Invoke(obj,args);throw new Exception(name);}
    static void Dispose(object obj){((IDisposable)obj).Dispose();}
    static string Folder(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static byte[] Hash(string path){using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return sha.ComputeHash(file);}
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static Dictionary<string,object> Write(string path,int channels,int hz,DateTime utc,long anchorRows,TimeZoneInfo zone){return (Dictionary<string,object>)T("NpLfpTimeWriter").GetMethod("Write",S).Invoke(null,new object[]{path,channels,hz,utc,anchorRows,zone});}
    static uint[] Values(string folder){byte[] b=File.ReadAllBytes(Path.Combine(folder,"time.dat"));Check(b.Length%4==0,"Partial uint32");var values=new uint[b.Length/4];for(int i=0;i<values.Length;i++)values[i]=(uint)(b[4*i]|b[4*i+1]<<8|b[4*i+2]<<16|b[4*i+3]<<24);return values;}
    static uint Expected(DateTime local,long index,long anchorRows,int hz){decimal ms=(decimal)local.TimeOfDay.Ticks/10000m+(index-(anchorRows-1))*12000m/hz;ms=(ms%86400000m+86400000m)%86400000m;return (uint)decimal.Floor(ms);}
    static void Grid(string name,int channels,int hz,int rows,long anchorRows,DateTime utc,TimeZoneInfo zone,int trailing=0){
        string folder=Folder(name),dat=Path.Combine(folder,"neuropixels_lfp.dat");using(var file=File.Create(dat)){file.SetLength((long)rows*channels*2+trailing);}
        byte[] before=Hash(dat);var receipt=Write(dat,channels,hz,utc,anchorRows,zone);var values=Values(folder);
        Check(values.Length==rows && new FileInfo(Path.Combine(folder,"time.dat")).Length==4L*rows,"One uint32 per saved LFP row");
        DateTime local=TimeZoneInfo.ConvertTimeFromUtc(utc,zone);for(int i=0;i<rows;i++)Check(values[i]==Expected(local,i,anchorRows,hz),"Rational grid/local-day wrap drift at "+i);
        Check(Same(before,Hash(dat)),"DAT bytes changed");Check((long)receipt["rows"]==rows && (long)receipt["lfp_trailing_bytes"]==trailing,"Saved prefix receipt wrong");
        Check(receipt["semantics"].ToString().Contains("not hardware"),"Synchronization claim");
        if(rows==0)Check(receipt["anchor_read_completion_utc"]==null,"Zero-data clock fabricated");
        else Check(receipt["anchor_read_completion_utc"].ToString()==utc.ToString("O"),"Processing clock replaced arrival anchor");
        var json=new JavaScriptSerializer().Serialize(receipt);Check(json.Contains("anchor_logical_lfp_row_index") && json.Contains("local_timezone"),"Time provenance not serializable");
    }
    static void RejectMissingAndOverwrite(){
        string folder=Folder("missing-anchor"),dat=Path.Combine(folder,"neuropixels_lfp.dat");File.WriteAllBytes(dat,new byte[768]);bool rejected=false;
        try{Write(dat,384,20000,DateTime.MinValue,0,TimeZoneInfo.Utc);}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
        Check(rejected && !File.Exists(Path.Combine(folder,"time.dat")),"Missing arrival timestamp fabricated");
        File.WriteAllBytes(Path.Combine(folder,"time.dat"),new byte[]{1,2,3,4});rejected=false;
        try{Write(dat,384,20000,new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),1,TimeZoneInfo.Utc);}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}
        Check(rejected && File.ReadAllBytes(Path.Combine(folder,"time.dat"))[0]==1,"Existing timeline overwritten");
    }
    static object Caps(int mask){var caps=New("NpModeCapabilities");Set(caps,"AvailableMask",3);Set(caps,"ActiveMask",mask);Set(caps,"ApHz",20000);Set(caps,"WireId",0x76);Set(caps,"BatchPackets",4);Set(caps,"SupportsStartupAdmission",true);return caps;}
    static void Session(bool merge,bool reconfigure){
        string folder=Folder(merge?"merged-session":reconfigure?"startup-subset":"per-probe-session");
        var session=New("NpCaptureSession");var status=New("XeNpStatus");Set(status,"Layout",Enum.ToObject(T("XeNpLaneLayout"),3));
        var options=New("NpCaptureOptions",merge,false);Call(session,"Configure",folder,status,Caps(3),options);
        Check(!File.Exists(Path.Combine(folder,"time.dat")) && !File.Exists(Path.Combine(folder,"probe1","time.dat")),"Timeline created on active reader/startup path");
        if(reconfigure){Call(session,"ReconfigureBeforeFirstData",folder,status,Caps(2),options);Check(!File.Exists(Path.Combine(folder,"probe1","neuropixels_lfp.dat")),"Old empty startup DAT not retired");}
        var probes=(IList)Get(session,"Probes");DateTime first=new DateTime(2026,1,2,3,4,5,DateTimeKind.Utc),latest=first.AddSeconds(-1); // Later arrival after a wall-clock step.
        foreach(var probe in probes){int port=(int)Get(probe,"Probe");Set(probe,"DecodedReadStamp",New("NpReadStamp",port==1?first:latest,(long)port));Call(probe,"OnSamples",new short[384],1,new short[384],1);}
        // An unmatched tail must not move the merged row's anchor.
        if(merge){var probe=probes[0];Set(probe,"DecodedReadStamp",New("NpReadStamp",first.AddSeconds(50),3L));Call(probe,"OnSamples",new short[384],1,new short[384],1);}
        Dispose(session);Call(session,"WriteLfpTimes");var receipts=(IList)Get(session,"LfpTimeOutputs");Check(receipts.Count==(merge?1:probes.Count),"Per-output timeline count");
        foreach(Dictionary<string,object> receipt in receipts){string path=Path.Combine(folder,receipt["file"].ToString());Check(new FileInfo(path).Length==4 && (long)receipt["rows"]==1,"Merged/per-probe row length");
            string expected=merge || reconfigure?latest.ToString("O"):(int)receipt["probe"]==1?first.ToString("O"):latest.ToString("O");
            Check(receipt["anchor_read_completion_utc"].ToString()==expected,"Merged unmatched tail or processing time changed anchor");}
    }
    static void Word(byte[] bytes,int at,int line,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)bytes[at+word*10+bit]|=(byte)(1<<line);}
    static byte[] Wire(int rows,bool corrupt){var b=new byte[rows*780];for(int sf=0;sf<rows;sf++)for(int f=0;f<13;f++){int at=(sf*13+f)*60;uint c=(uint)(sf*13+f);if(corrupt && sf==rows-1)c+=64;Word(b,at,0,0,f==0?0x330U:0xCFU);Word(b,at,7,2,0x11);Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);}return b;}
    static void DecoderQueue(bool corrupt){
        string folder=Folder(corrupt?"failed-prefix":"delayed-decoder");var probe=New("NpProbeRecording",folder,1,Enum.ToObject(T("XeNpLaneLayout"),3));Call(Get(probe,"Decoder"),"DeclareRecordingReset");
        DateTime utc=new DateTime(2025,4,5,6,7,8,DateTimeKind.Utc);var wire=Wire(36,corrupt);
        Call(probe,"Feed",wire,0,wire.Length,New("NpReadStamp",utc,1L)); // Remains in producer block until Complete, deliberately delaying processing.
        bool failed=false;try{Call(probe,"Complete");}catch(TargetInvocationException e){failed=e.InnerException is IOException;}
        Check(failed==corrupt,"Decoder failure fixture did not exercise expected path");Dispose(probe);
        long rows=new FileInfo(Path.Combine(folder,"neuropixels_lfp.dat")).Length/768;Check(rows==(corrupt?2:3),"Decoder saved prefix changed");
        Check((DateTime)Get(probe,"LfpLastReceiveUtc")==utc,"Decoder queue used foreground time");
        var receipt=Write(Path.Combine(folder,"neuropixels_lfp.dat"),384,20000,(DateTime)Get(probe,"LfpLastReceiveUtc"),(long)Get(probe,"LfpOutputRows"),TimeZoneInfo.Utc);
        Check(Values(folder).Length==rows && (long)receipt["rows"]==rows,"Failed/successful decoder timeline row count");
    }
    static void MainFlow(string source){
        string main=File.ReadAllText(Path.Combine(source,"mainForm.cs"));int read=main.IndexOf("int bytesRead=reader.ReadInto(buffer);"),stamp=main.IndexOf("DateTime receiveUtc=DateTime.UtcNow;",read),enqueue=main.IndexOf("new ReceiveBlock(buffer,bytesRead,receiveUtc)",read);
        Check(read>=0 && stamp>read && enqueue>stamp,"Reader timestamp not captured before queueing");
        Check(main.Contains("parser.Feed(block.Buffer,block.Count,block.ReceiveUtc)"),"Raw queue lost arrival time");
        int close=main.IndexOf("try {parser.Dispose();}"),write=main.IndexOf("parser.WriteLfpTimes()",close);Check(close>=0 && write>close,"Timeline written on reader path or before DAT close");
        Check(main.Contains("settings.Context(\"lfp_time_output\",parser.LfpTimeOutputs)"),"Recording settings lost time provenance");
        var block=app.GetType("USB512_interface.Form1",true).GetNestedType("ReceiveBlock",BindingFlags.NonPublic);DateTime utc=DateTime.UtcNow.AddHours(-10);
        var value=Activator.CreateInstance(block,I,null,new object[]{new byte[]{1},1,utc},null);Check((DateTime)Get(value,"ReceiveUtc")==utc && block.IsValueType,"Reader block timestamp/ownership changed");
        var marker=block.GetMethod("CreateStreamRestartMarker",S).Invoke(null,null);Check((bool)Get(marker,"IsStreamRestartMarker"),"Other USB-family restart marker changed");
    }
    static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
        DateTime utc=new DateTime(2026,1,2,0,0,0,DateTimeKind.Utc).AddTicks(7500);
        foreach(int hz in new[]{7500,15000,20000,30000})Grid("rate"+hz,384,hz,17001,17001,utc,TimeZoneInfo.Utc);
        Grid("zero",384,20000,0,0,DateTime.MinValue,TimeZoneInfo.Utc);Grid("single",384,20000,1,1,utc,TimeZoneInfo.Utc);
        Grid("fractional-repeat",384,20000,4,4,utc,TimeZoneInfo.Utc);Grid("partial-prefix",768,20000,8,12,utc,TimeZoneInfo.Utc,5);
        Grid("more-than-day",384,20000,4,144000004,utc,TimeZoneInfo.Utc);
        Grid("local-offset",384,20000,20,20,utc,TimeZoneInfo.CreateCustomTimeZone("test local",TimeSpan.FromMinutes(-300),"test","test"));
        RejectMissingAndOverwrite();Session(false,false);Session(true,false);Session(false,true);DecoderQueue(false);DecoderQueue(true);MainFlow(args[2]);
        Console.WriteLine("PASS actual Release LFP time.dat: "+checks+" exact row/byte, rational rate, wrap/repeat/local-day, merged/per-probe, delayed queue, startup, zero-data and failed-prefix checks; no hardware.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
