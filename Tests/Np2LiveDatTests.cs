using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;

// Actual app, synthetic verified wire blocks, files only. Never opens a device.
static class Np2LiveDatTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app;
    static MethodInfo make;
    static int checks;
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return T(name).GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Single(c=>c.GetParameters().Length==args.Length).Invoke(args);}
    static object Call(object target,string name,params object[] args){try{return target.GetType().GetMethod(name,All).Invoke(target,args);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}}
    static object Get(object target,string name){return target.GetType().GetProperty(name,All).GetValue(target,null);}
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static byte[] Block(ulong sequence,uint mask,uint hz,bool packed){ulong ticks=mask==7?40960UL:mask==3?(packed?61440UL:46080UL):122880UL;return (byte[])make.Invoke(null,new object[]{sequence,sequence*ticks,mask,mask,hz,packed,UInt32.MaxValue});}
    static int[] Ports(uint mask){return Enumerable.Range(1,3).Where(p=>(mask&(1U<<(p-1)))!=0).ToArray();}
    static string ProbePath(string root,int probe,int count){return Path.Combine(count==1?root:Path.Combine(root,"probe"+probe),"neuropixels_ap.dat");}
    static void Layout(string root,uint mask,uint hz,bool packed,bool correction)
    {
        string name=mask+"-"+hz+"-"+packed+"-"+correction;
        string original=Path.Combine(root,name+"-per-probe"),combined=Path.Combine(root,name+"-live");
        Directory.CreateDirectory(original);Directory.CreateDirectory(combined);
        var policy=New("NpV2BufferPolicy",64);
        Action<int,short[]> correct=correction?(Action<int,short[]>)((p,row)=>{for(int ch=0;ch<384;ch++)row[ch]=(short)(row[ch]+p*100);}):null;
        Action<int,short[]> preview=(p,row)=>{throw new IOException("Synthetic preview failure; recording must remain valid.");};
        var baseline=New("NpV2DatRecorder",original,null,policy,correct);
        var live=New("NpV2DatRecorder",combined,preview,policy,correct,true);
        var verifier=New("NpV2WireVerifier");var wire=new MemoryStream();
        try{
            for(ulong seq=0;seq<121;seq++){
                byte[] bytes=Block(seq,mask,hz,packed);wire.Write(bytes,0,bytes.Length);
                Call(verifier,"Feed",bytes,0);Call(baseline,"Enqueue",bytes,0,verifier);Call(live,"Enqueue",bytes,0,verifier);
                if(seq==0){Check(File.Exists(Path.Combine(combined,"neuropixels_ap.dat")),"Combined DAT not created during capture");Check(!Directory.GetDirectories(combined,"probe*").Any(),"Live mode created duplicate per-probe DAT folders");}
            }
            string file=Path.Combine(combined,"neuropixels_ap.dat");var deadline=Stopwatch.StartNew();
            while(new FileInfo(file).Length==0 && deadline.ElapsedMilliseconds<5000)Thread.Sleep(10);
            Check(new FileInfo(file).Length>0,"Combined DAT did not grow before Complete/Stop: "+Get(live,"DecodeError"));
            using(var reader=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){Check(reader.Length>0,"Live DAT cannot be read during capture");}
            Call(baseline,"Complete");Call(live,"Complete");Call(live,"WriteMergeReport",true);
            Check(Get(live,"DecodeError")==null && Get(live,"RawError")==null,"Live writer failed");
            Check((long)Get(live,"RawDroppedBlocks")==0 && (long)Get(live,"DecodedDroppedLaneBlocks")==0,"Live writer dropped data");
            Check(((string)Get(live,"PreviewError")).Contains("Synthetic preview"),"Preview failure test not exercised");
            Check(File.ReadAllBytes(Path.Combine(combined,"neuropixels_np2usb.raw")).SequenceEqual(wire.ToArray()),"Live merge changed native RAW bytes");
            int[] ports=Ports(mask);var sources=ports.Select(p=>File.ReadAllBytes(ProbePath(original,p,ports.Length))).ToArray();
            long common=sources.Min(bytes=>bytes.Length/768);byte[] dat=File.ReadAllBytes(file);
            Check(dat.LongLength==common*ports.Length*768,"Combined complete row extent differs from per-probe reference");
            Check((long)Get(live,"TotalRows")==common,"Combined row count is not file row count");
            for(int sample=0;sample<common;sample++)for(int p=0;p<ports.Length;p++)for(int channel=0;channel<384;channel++)
                Check(BitConverter.ToInt16(dat,(sample*ports.Length*384+p*384+channel)*2)==BitConverter.ToInt16(sources[p],(sample*384+channel)*2),"Expected [S0 P1 1..384][S0 P2 1..384]... sample/probe/channel ordering");
            for(int p=0;p<ports.Length;p++){
                int tail=sources[p].Length-(int)common*768;
                string stem=Path.Combine(combined,"unpaired_tail","neuropixels_ap_probe"+ports[p]);
                if(tail>0){Check(File.ReadAllBytes(stem+".dat").SequenceEqual(sources[p].Skip((int)common*768)),"Unmatched complete tail rows lost");
                    byte[] ticks=File.ReadAllBytes(stem+"_source_ticks.dat");int phase=(int)Call(verifier,"PhaseForProbe",ports[p]-1);
                    Check(ticks.Length==tail/768*8 && BitConverter.ToUInt64(ticks,0)==(ulong)((1600-phase)%1600)+(ulong)common*1600UL,"Tail source tick provenance incorrect");}
            }
            Check(!Directory.GetDirectories(combined,"probe*").Any(),"Live mode left duplicate probe DAT folders");
            string report=File.ReadAllText(Path.Combine(combined,"np2_merge_status.txt"));Check(report.Contains("merge_mode=live") && report.Contains("phase offsets retained, not corrected"),"Live merge/timing receipt missing");
            var settings=New("NpRecordingSettings",combined,null,"live_usb",true,null,null,"FIXTURE-UID");
            Call(settings,"PlannedLayout",(int)mask,true,false);Call(settings,"Actual",(int)hz,(int)hz,(int)mask,ports.Length>1,false,"Synthetic verified native V2 profile; live combined output");Call(settings,"Finished",true,null);Call(settings,"Write");
            var json=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(combined,"recording_settings.json")));
            var output=(Dictionary<string,object>)json["output"];Check((int)output["channel_count_per_dat"]==ports.Length*384 && !(bool)output["probe_subfolders"],"Combined root metadata layout incorrect");
            foreach(int port in ports)Check(File.Exists(Path.Combine(combined,"probe_"+port+".json")),"Probe metadata not retained alongside combined DAT");
            var xml=new XmlDocument{XmlResolver=null};xml.Load(Path.ChangeExtension(file,"xml"));Check(xml.SelectSingleNode("//nChannels").InnerText==(ports.Length*384).ToString(),"Combined Neuroscope channel count incorrect");
        }finally{((IDisposable)baseline).Dispose();((IDisposable)live).Dispose();wire.Dispose();}
    }
    static void DecodeFailureKeepsRaw(string root)
    {
        string folder=Path.Combine(root,"decode-failure");Directory.CreateDirectory(folder);
        var policy=New("NpV2BufferPolicy",64);var verifier=New("NpV2WireVerifier");var wire=new MemoryStream();
        int rows=0;Action<int,short[]> fail=(p,row)=>{if(p==2 && Interlocked.Increment(ref rows)==10)throw new IOException("Injected correction failure");};
        var recorder=New("NpV2DatRecorder",folder,null,policy,fail,true);
        try{
            for(ulong seq=0;seq<40;seq++){byte[] bytes=Block(seq,7,20000,false);wire.Write(bytes,0,bytes.Length);Call(verifier,"Feed",bytes,0);Call(recorder,"Enqueue",bytes,0,verifier);}
            var clock=Stopwatch.StartNew();Call(recorder,"Complete");
            Check(clock.ElapsedMilliseconds<5000,"Decode failure deadlocked live merge drain");
            Check(((string)Get(recorder,"DecodeError")).Contains("Injected correction failure"),"Live decode failure not reported");
            Check((long)Get(recorder,"RawDroppedBlocks")==0 && File.ReadAllBytes(Path.Combine(folder,"neuropixels_np2usb.raw")).SequenceEqual(wire.ToArray()),"Decode failure prevented native RAW drain");
            Check(new FileInfo(Path.Combine(folder,"neuropixels_ap.dat")).Length%2304==0,"Failure left a partial merged sample row");
        }finally{((IDisposable)recorder).Dispose();wire.Dispose();}
    }
    static void BoundedSkewAbort(string root)
    {
        string folder=Path.Combine(root,"skew-abort");Directory.CreateDirectory(folder);var verifier=New("NpV2WireVerifier");Call(verifier,"Feed",Block(0,3,20000,true),0);
        var writer=New("NpV2LiveDatWriter",folder,New("NpV2BufferPolicy",64));Call(writer,"Open",3U,20000U,verifier);
        int added=0;Exception stopped=null;var row=new short[384];
        var thread=new Thread(()=>{try{for(int n=0;n<3000;n++){Call(writer,"Add",1,row);Interlocked.Increment(ref added);}}catch(Exception e){stopped=e;}});
        thread.Start();var wait=Stopwatch.StartNew();while(Volatile.Read(ref added)<2048 && wait.ElapsedMilliseconds<5000)Thread.Sleep(1);
        Check(Volatile.Read(ref added)==2048,"Probe skew queue is not bounded at 2048 rows");
        Call(writer,"Abort",new IOException("Injected abort"));Check(thread.Join(2000) && stopped is IOException,"Abort did not release waiting decoder");
        try{Call(writer,"Complete");throw new Exception("Aborted merge incorrectly completed");}catch(IOException){checks++;}
        try{((IDisposable)writer).Dispose();}catch(IOException){}
    }
    [STAThread]static int Main(string[] args){try{
        app=Assembly.LoadFrom(Path.GetFullPath(args[0]));make=Assembly.LoadFile(Path.GetFullPath(args[2])).GetType("NpV2UsbConsoleTests",true).GetMethod("MakeV3Block",All);
        string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
        Layout(root,1,20000,false,false);Layout(root,3,20000,true,true);Layout(root,3,30000,false,false);Layout(root,7,15000,false,false);Layout(root,7,20000,false,true);
        DecodeFailureKeepsRaw(root);BoundedSkewAbort(root);
        Console.WriteLine("PASS "+checks+" actual-app live DAT checks: pre-Stop file growth, exact sample/probe/channel blocks, RAW, correction, preview isolation, tails/ticks, metadata/XML and bounded skew/abort; no hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
