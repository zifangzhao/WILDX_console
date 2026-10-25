// Bounded read-only observer; all card, catalog, prefetch and export code is loaded from the checked WILDX assembly.
using System;using System.Collections;using System.Collections.Generic;using System.Diagnostics;
using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using System.Web.Script.Serialization;
class LifecycleSdReadback {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 const string Uid="001D00065942500B20313354";static Assembly app;
 static Type T(string n){return app.GetType(n,true);}
 static object V(object o,string n){var f=o.GetType().GetField(n,F);return f!=null?f.GetValue(o):o.GetType().GetProperty(n,F).GetValue(o,null);}
 static object C(object o,string n,params object[] p){try{return (o as Type??o.GetType()).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==p.Length).Invoke(o is Type?null:o,p);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 static object New(string n,params object[] p){return Activator.CreateInstance(T(n),F,null,p,null);}
 static void Need(bool b,string s){if(!b)throw new IOException(s);}
 static void Save(string path,object value){File.WriteAllText(path,new JavaScriptSerializer{MaxJsonLength=Int32.MaxValue}.Serialize(value));}
 static int Main(string[] args){string output=null;var result=new Dictionary<string,object>();try{
  Need(args.Length==4||(args.Length==5&&(args[4]=="export-only"||args[4]=="raw-save")),"host_EXE fresh_output start_LBA session_ID [export-only|raw-save] (0 0 for catalog only)");
  string host=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);long start=Int64.Parse(args[2]);uint session=UInt32.Parse(args[3]);
  Need(!Directory.Exists(output),"Fresh output required");
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(Path.GetDirectoryName(host),new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};app=Assembly.LoadFrom(host);
  var target=((IEnumerable)C(T("USB512_interface.NpV1Management"),"Enumerate")).Cast<object>().Single(x=>String.Equals((string)V(x,"Uid"),Uid,StringComparison.OrdinalIgnoreCase)&&!(bool)V(x,"Recovery"));
  using(var management=(IDisposable)New("USB512_interface.NpV1Management",target)){
   var s=C(management,"ReadStatus");Need(Convert.ToUInt32(V(s,"Role"))==3&&Convert.ToUInt32(V(s,"Busy"))==0&&Convert.ToUInt32(V(s,"PendingMode"))==0,"Idle download required");
  }
  Directory.CreateDirectory(output);result["uid"]=Uid;result["host_version"]=app.GetName().Version.ToString();
  using(var card=(Stream)New("CE32_console.NpDownloadStream",(string)V(target,"Path"),false)){
   result["transport"]=V(card,"TransportDescription");Need(result["transport"].ToString().StartsWith("High-speed USB"),"HS USB required");result["card_bytes"]=card.Length;
   var records=((IEnumerable)C(T("CE32_console.NpDownloadCatalog"),"Read",card)).Cast<object>().ToArray();
   result["catalog"]=records.Select(r=>new {start=V(r,"Start"),end=V(r,"End"),catalog_end=V(r,"CatalogEnd"),committed_end=V(r,"CommittedEnd"),session=V(r,"Session"),complete=V(r,"Complete"),chunks=V(r,"Chunks"),mask=V(r,"ProbeMask"),hz=V(r,"ApHz")}).ToArray();
   Save(Path.Combine(output,"catalog.json"),result["catalog"]);
   if(start!=0){
    var record=records.Single(r=>Convert.ToInt64(V(r,"Start"))==start&&Convert.ToUInt32(V(r,"Session"))==session);
    long end=Convert.ToInt64(V(record,"End")),bytes=checked((end-start)*512L);
    bool saveRaw=args.Length==5&&args[4]=="raw-save";
    Need((bool)V(record,"Complete")&&bytes>0&&bytes<=(saveRaw?2L*1024*1024*1024:512L*1024*1024),"Complete bounded extent required (512 MiB; explicit raw-save <=2 GiB)");
    result["session"]=session;result["start"]=start;result["end"]=end;result["extent_bytes"]=bytes;result["chunks"]=V(record,"Chunks");
    var clock=(byte[])C(T("CE32_console.NpDownloadCatalog"),"Sector",card,start+5);File.WriteAllBytes(Path.Combine(output,"clock.bin"),clock);
    if(args.Length==4||saveRaw){
    result["before_raw"]=C(card,"Diagnostics");card.Position=start*512L;
    byte[] buffer=new byte[510*1024];long done=0,readTicks=0;var wall=Stopwatch.StartNew();
    using(var raw=saveRaw?File.Create(Path.Combine(output,"extent.bin")):null)
    using(var hash=SHA256.Create()){
     while(done<bytes){Need(wall.Elapsed.TotalMinutes<2,"Read-only raw deadline exceeded");long tick=Stopwatch.GetTimestamp();int n=card.Read(buffer,0,(int)Math.Min(buffer.Length,bytes-done));readTicks+=Stopwatch.GetTimestamp()-tick;Need(n>0,"Truncated raw extent");hash.TransformBlock(buffer,0,n,null,0);if(raw!=null)raw.Write(buffer,0,n);done+=n;}
     hash.TransformFinalBlock(new byte[0],0,0);result["raw_sha256"]=BitConverter.ToString(hash.Hash).Replace("-","");
    }
    result["raw_wall_seconds"]=wall.Elapsed.TotalSeconds;result["raw_read_seconds"]=readTicks/(double)Stopwatch.Frequency;result["raw_bytes"]=done;result["after_raw"]=C(card,"Diagnostics");
    }
    result["before_export"]=C(card,"Diagnostics");
    var exportWatch=Stopwatch.StartNew();Func<bool> cancel=()=>exportWatch.Elapsed.TotalMinutes>2;
    if(app.GetType("CE32_console.NpDownloadPrefetchStream",false)!=null)using(var prefetch=(Stream)New("CE32_console.NpDownloadPrefetchStream",card,start*512L,end*512L,cancel,1024*1024)){
     C(T("CE32_console.NpCe64Export"),"Export",prefetch,start,end,Path.Combine(output,"export"),cancel,(Action<int>)(p=>{}));
     result["prefetch_source_bytes"]=V(prefetch,"SourceBytes");result["prefetch_source_read_seconds"]=Convert.ToInt64(V(prefetch,"SourceReadTicks"))/(double)Stopwatch.Frequency;result["prefetch_consumer_wait_seconds"]=Convert.ToInt64(V(prefetch,"ConsumerWaitTicks"))/(double)Stopwatch.Frequency;
    }
    else {result["prefetch_present"]=false;C(T("CE32_console.NpCe64Export"),"Export",card,start,end,Path.Combine(output,"export"),cancel,(Action<int>)(p=>{}));}
    result["export_seconds"]=exportWatch.Elapsed.TotalSeconds;result["after_export"]=C(card,"Diagnostics");
   }
   result["result"]="PASS read-only production APIs; inspect export status for sample integrity";
  }
  Save(Path.Combine(output,"result.json"),result);Console.WriteLine(result["result"]);return 0;
 }catch(Exception e){result["result"]="FAIL";result["error"]=e.ToString();if(output!=null&&Directory.Exists(output))Save(Path.Combine(output,"result.json"),result);Console.Error.WriteLine(e);return 1;}}
}
