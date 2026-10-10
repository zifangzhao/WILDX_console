using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

// Test the maintained, optimized console assembly without USB/device calls.
internal static class TestNpPairedRaw {
    const BindingFlags I=BindingFlags.Instance|BindingFlags.NonPublic,S=BindingFlags.Static|BindingFlags.NonPublic;
    static Assembly app;static int checks;static string root;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),I|BindingFlags.Public,null,args,null);}
    static object Get(object obj,string name){var field=obj.GetType().GetField(name,I);return field!=null?field.GetValue(obj):obj.GetType().GetProperty(name,I).GetValue(obj,null);}
    static void Set(object obj,string name,object value){obj.GetType().GetField(name,I).SetValue(obj,value);}
    static object Call(object obj,string name,params object[] args){foreach(var m in obj.GetType().GetMethods(I))if(m.Name==name && m.GetParameters().Length==args.Length)return m.Invoke(obj,args);throw new Exception(name);}
    static void Close(object obj){((IDisposable)obj).Dispose();}
    static void Reject(Action action,string why){bool failed=false;try{action();}catch(TargetInvocationException e){failed=e.InnerException is IOException || e.InnerException is ArgumentException;}Check(failed,why);}
    static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
    static object Caps(int mask,int hz,int wire=0x77,int batch=112){var b=new byte[48];uint[] words={0x314d504e,1,7,15,15,3,(uint)mask,(uint)hz,(uint)wire,(uint)batch,5,0};for(int i=0;i<words.Length;i++)Put(b,i*4,words[i]);return T("NpModeCapabilities").GetMethod("Parse",S).Invoke(null,new object[]{b,48});}
    static byte[] Group(byte[] p1,byte[] p2,int offset,int group){var b=new byte[2048];for(int lane=0;lane<4;lane++){int at=lane*512;ushort seq=unchecked((ushort)(group*4+lane));b[at]=0x12;b[at+1]=0xa5;b[at+2]=0x77;b[at+3]=(byte)lane;b[at+4]=(byte)seq;b[at+5]=(byte)(seq>>8);b[at+6]=3;b[at+7]=1;for(int i=0;i<252;i++){b[at+8+i*2]=p1[offset+4*i+lane];b[at+9+i*2]=p2[offset+4*i+lane];}}return b;}
    static byte[] Stream(byte[] p1,byte[] p2){if(p1.Length!=p2.Length || p1.Length%1008!=0)throw new Exception();var b=new byte[p1.Length/1008*2048];for(int group=0;group<p1.Length/1008;group++)Buffer.BlockCopy(Group(p1,p2,group*1008,group),0,b,group*2048,2048);return b;}
    static Action<int,byte[],int,int> Ignore=(p,b,o,n)=>{};
    static object Parser(){return New("NpTaggedPacketParser",3,112,true);}
    static void Feed(object parser,byte[] bytes,Action<int,byte[],int,int> receive,int chunk=Int32.MaxValue){for(int at=0;at<bytes.Length;at+=Math.Min(chunk,bytes.Length-at)){int n=Math.Min(chunk,bytes.Length-at);var block=new byte[n];Buffer.BlockCopy(bytes,at,block,0,n);Call(parser,"Feed",block,n,receive);}}
    static void Reconstruction(){
        var p1=new byte[2016];var p2=new byte[2016];for(int i=0;i<p1.Length;i++){p1[i]=(byte)(i*17+3);p2[i]=(byte)(i*29+211);}byte[] wire=Stream(p1,p2);
        foreach(int chunk in new[]{1,2,5,6,7,8,9,63,110,251,504,511,512,513,1008,2047,2048,4096}){
            var parser=Parser();var a=new MemoryStream();var b=new MemoryStream();int callbacks=0;
            Feed(parser,wire,(probe,data,offset,n)=>{Check(n==1008,"Paired group output size");if(probe==1)a.Write(data,offset,n);else if(probe==2)b.Write(data,offset,n);else throw new Exception("Wrong probe");callbacks++;},chunk);
            Call(parser,"Complete");Check(callbacks==4 && (long)Get(parser,"Packets")==8,"Paired packet/callback accounting");
            Check(Same(a.ToArray(),p1) && Same(b.ToArray(),p2),"GPIOE low/high bytes or time lanes swapped");
        }
        for(int split=0;split<=2048;split++){var parser=Parser();byte[] first=new byte[split],last=new byte[2048-split];Buffer.BlockCopy(wire,0,first,0,first.Length);Buffer.BlockCopy(wire,split,last,0,last.Length);Call(parser,"Feed",first,first.Length,Ignore);Call(parser,"Feed",last,last.Length,Ignore);Call(parser,"Complete");checks++;}
        var wrap=Parser();byte[] one=new byte[1008];for(int group=0;group<16386;group++)Feed(wrap,Group(one,one,0,group),Ignore);Call(wrap,"Complete");Check((long)Get(wrap,"Packets")==65544 && (long)Get(wrap,"SequenceGaps")==0,"NP uint16 sequence wrap");
        // Existing 0x76 path reconstructs the original 2024 bytes unchanged.
        var legacy=New("NpTaggedPacketParser",1,4);var old=new byte[2048];var expected=new byte[2024];for(int i=0;i<expected.Length;i++)expected[i]=(byte)(i*13);
        for(int lane=0;lane<4;lane++){int at=lane*512;old[at]=0x12;old[at+1]=0xa5;old[at+2]=0x76;old[at+3]=(byte)(0x61|(lane<<2));old[at+4]=(byte)lane;for(int i=0;i<506;i++)old[at+6+i]=expected[i*4+lane];}
        Feed(legacy,old,(probe,data,offset,n)=>{Check(probe==1 && n==2024,"Legacy 0x76 metadata");var copy=new byte[n];Buffer.BlockCopy(data,offset,copy,0,n);Check(Same(copy,expected),"Legacy 0x76 changed");},7);Call(legacy,"Complete");
    }
    static bool Same(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static byte[] Auxiliary(byte kind,byte flags,ushort sequence,int bytes){var b=new byte[bytes];b[0]=0x12;b[1]=0xa5;b[2]=kind;b[3]=flags;b[4]=(byte)sequence;b[5]=(byte)(sequence>>8);for(int i=6;i<bytes;i++)b[i]=(byte)i;return b;}
    static void StrictFailures(){
        var zeros=new byte[1008];byte[] good=Group(zeros,zeros,0,0);
        foreach(int field in new[]{0,1,2,3,6,7,512+3,1024+3,1536+3,4,512+4}){var bad=(byte[])good.Clone();bad[field]^=1;var parser=Parser();Reject(()=>Feed(parser,bad,Ignore),"Malformed paired header/order/sequence accepted "+field);Reject(()=>Feed(parser,good,Ignore),"Failed parser resumed");}
        for(int n=1;n<2048;n++){var parser=Parser();var partial=new byte[n];Buffer.BlockCopy(good,0,partial,0,n);Feed(parser,partial,Ignore);Reject(()=>Call(parser,"Complete"),"Incomplete paired packet/group accepted");}
        var mixed=Parser();int misc=0,camera=0;Set(mixed,"OnMisc",new Action<byte[],int,int>((b,o,n)=>{Check(o==6 && n==506,"Misc format changed");misc++;}));Set(mixed,"OnCamera",new Action<byte[],int,int,byte,ushort>((b,o,n,f,s)=>{Check(o==6 && n==104 && f==2 && s==900,"Camera format changed");camera++;}));
        Feed(mixed,good,Ignore);Feed(mixed,Auxiliary(5,8,77,512),Ignore);Feed(mixed,Auxiliary(3,2,900,110),Ignore,3);Feed(mixed,Group(zeros,zeros,0,1),Ignore);Call(mixed,"Complete");Check(misc==1 && camera==1 && (long)Get(mixed,"Packets")==8,"Auxiliary packets consumed NP sequence");
        foreach(byte kind in new byte[]{3,5}){var parser=Parser();Set(parser,"OnMisc",new Action<byte[],int,int>((b,o,n)=>{}));Set(parser,"OnCamera",new Action<byte[],int,int,byte,ushort>((b,o,n,f,s)=>{}));var lane=new byte[512];Buffer.BlockCopy(good,0,lane,0,512);Feed(parser,lane,Ignore);Reject(()=>Feed(parser,Auxiliary(kind,kind==3?(byte)2:(byte)8,0,kind==3?110:512),Ignore),"Auxiliary packet interrupted four-lane group");}
        var gap=Parser();Feed(gap,good,Ignore);var skipped=Group(zeros,zeros,0,2);Reject(()=>Feed(gap,skipped,Ignore),"NP group gap accepted");Check((long)Get(gap,"SequenceGaps")==1,"Sequence gap not reported");
        foreach(int mask in new[]{1,2,4,5,6,7})Reject(()=>Caps(mask,20000),"Paired mask accepted "+mask);
        foreach(int hz in new[]{7500,15000,30000})Reject(()=>Caps(3,hz),"Paired rate accepted "+hz);
        foreach(int batch in new[]{0,2,6,512})Reject(()=>Caps(3,20000,0x77,batch),"Invalid paired batch accepted");
        foreach(int batch in new[]{4,8,32,112,256})Check((int)Get(Caps(3,20000,0x77,batch),"WireId")==0x77,"Valid paired descriptor rejected");
        Check((int)Get(Caps(1,20000,0x76),"WireId")==0x76 && (int)Get(Caps(3,15000,0x76),"WireId")==0x76,"Legacy modes rejected");
    }
    static void Word(byte[] b,int at,int line,int word,uint value){for(int bit=0;bit<10;bit++)if((value&(1U<<(9-bit)))!=0)b[at+word*10+bit]|=(byte)(1<<line);}
    static byte[] Raw(int salt,bool corrupt){const int rows=84;var b=new byte[rows*780];for(int sf=0;sf<rows;sf++)for(int f=0;f<13;f++){int at=(sf*13+f)*60;uint c=(uint)(sf*13+f);if(corrupt && sf==rows-1)c+=64;Word(b,at,0,0,f==0?0x330U:0xcfU);Word(b,at,7,2,0x11);Word(b,at,7,3,c>>10);Word(b,at,7,4,c&1023);for(int adc=0;adc<32;adc++){int line=adc>=30?7:adc/5+1,word=adc>=30?adc-30:adc%5;Word(b,at,line,word,(uint)((salt+adc*37+sf/12+f*11)&1023));}}return b;}
    static byte[][] Expected(byte[] raw){var decoder=New("XeNpStreamDecoder",Enum.ToObject(T("XeNpLaneLayout"),3));Call(decoder,"DeclareRecordingReset");var ap=new MemoryStream();var lfp=new MemoryStream();Action<short[],int,short[],int> receive=(a,na,l,nl)=>{if(na!=0){var b=new byte[na*384*2];Buffer.BlockCopy(a,0,b,0,b.Length);ap.Write(b,0,b.Length);}if(nl!=0){var b=new byte[nl*384*2];Buffer.BlockCopy(l,0,b,0,b.Length);lfp.Write(b,0,b.Length);}};Call(decoder,"Feed",raw,0,raw.Length,receive);Call(decoder,"Complete");return new[]{ap.ToArray(),lfp.ToArray()};}
    static void Pipeline(bool merge,bool corrupt){
        string folder=Path.Combine(root,(merge?"merged":"per_probe")+(corrupt?"_corrupt":"_clean"));Directory.CreateDirectory(folder);
        var session=New("NpCaptureSession");var status=New("XeNpStatus");Set(status,"Layout",Enum.ToObject(T("XeNpLaneLayout"),3));Set(status,"ApSampleRateHz",20000);
        Call(session,"Configure",folder,status,Caps(3,20000),New("NpCaptureOptions",merge,false));var probes=(IList)Get(session,"Probes");foreach(var p in probes)Call(Get(p,"Decoder"),"DeclareRecordingReset");
        byte[] p1=Raw(7,false),p2=Raw(119,corrupt),wire=Stream(p1,p2);DateTime utc=new DateTime(2026,10,2,12,0,0,DateTimeKind.Utc);
        for(int at=0;at<wire.Length;at+=317){int n=Math.Min(317,wire.Length-at);var block=new byte[n];Buffer.BlockCopy(wire,at,block,0,n);Call(session,"Feed",block,n,utc.AddTicks(at));}
        bool failed=false;try{Call(session,"Complete");}catch(TargetInvocationException e){failed=e.InnerException is IOException;}
        Check(failed==corrupt,"Strict NP counter/frame failure was repaired or unexpected");Close(session);Call(session,"WriteLfpTimes");Call(session,"WriteStatus",folder,!failed);
        Check((long)Get(session,"SequenceGaps")==0 && (long)Get(session,"Packets")==260,"NP packet accounting changed");
        Check(File.ReadAllText(Path.Combine(folder,"neuropixels_probes.txt")).Contains("wire_id=0x77"),"Paired metadata reported old wire ID");
        var receipts=(IList)Get(session,"LfpTimeOutputs");foreach(Dictionary<string,object> receipt in receipts){string dat=Path.Combine(folder,receipt["lfp_file"].ToString());int channels=merge?768:384;Check(new FileInfo(Path.Combine(folder,receipt["file"].ToString())).Length==new FileInfo(dat).Length/(channels*2)*4,"Paired LFP time row count");}
        if(corrupt){Check((long)Get(session,"CounterErrors")+(long)Get(session,"FrameErrors")>0,"Strict corruption not reported");return;}
        byte[][] e1=Expected(p1),e2=Expected(p2);
        for(int band=0;band<2;band++){string name=band==0?"neuropixels_ap.dat":"neuropixels_lfp.dat";
            if(merge){byte[] actual=File.ReadAllBytes(Path.Combine(folder,name));int rows=e1[band].Length/768;Check(actual.Length==rows*1536,"Merged paired row count");for(int row=0;row<rows;row++)for(int i=0;i<768;i++)Check(actual[row*1536+i]==e1[band][row*768+i] && actual[row*1536+768+i]==e2[band][row*768+i],"Merged paired DAT bytes/order changed");}
            else{Check(Same(File.ReadAllBytes(Path.Combine(folder,"probe1",name)),e1[band]) && Same(File.ReadAllBytes(Path.Combine(folder,"probe2",name)),e2[band]),"Per-probe strict decoder DAT bytes differ");}}
        Check((long)Get(session,"FrameErrors")+(long)Get(session,"CounterErrors")+(long)Get(session,"Resynchronizations")==0,"Clean paired decoder errors");
        var settings=New("NpRecordingSettings",folder,null,"live_usb",false,(DateTime?)utc,null,null);Call(settings,"Actual",20000,20000,3,merge,!merge,"paired 0x77 software fixture");Call(settings,"Context","lfp_time_output",Get(session,"LfpTimeOutputs"));Call(settings,"Finished",true,null);Call(settings,"Write");
        string target=merge?folder:Path.Combine(folder,"probe1");Check(File.Exists(Path.Combine(target,"neuropixels_ap.xml")) && File.Exists(Path.Combine(target,"neuropixels_lfp.xml")),"Paired recording bypassed XML path");Check(File.ReadAllText(Path.Combine(folder,"recording_settings.json")).Contains("lfp_time_output"),"Paired recording bypassed time metadata");
    }
    static void Admission(string source){string main=File.ReadAllText(Path.Combine(source,"mainForm.cs"));Check(main.Contains("modeCaps.WireId==0x77 && modeCaps.ActiveMask==3 && modeCaps.ApHz==20000") && main.Contains("admitted.WireId==0x76 && (admitted.ActiveMask==1 || admitted.ActiveMask==2) && admitted.ApHz==20000"),"Startup wire fallback not restricted to admitted single probe");
        string folder=Path.Combine(root,"admitted_subset");var session=New("NpCaptureSession");var status=New("XeNpStatus");Set(status,"Layout",Enum.ToObject(T("XeNpLaneLayout"),3));var options=New("NpCaptureOptions",true,false);Call(session,"Configure",folder,status,Caps(3,20000),options);Call(session,"ReconfigureBeforeFirstData",folder,status,Caps(2,20000,0x76),options);Check((int)Get(session,"ProbeMask")==2 && !(bool)Get(session,"MergedDat"),"Paired startup subset layout");Close(session);Call(session,"WriteStatus",folder,false);Check(File.ReadAllText(Path.Combine(folder,"neuropixels_probes.txt")).Contains("wire_id=0x76"),"Startup fallback retained paired parser");}
    static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);Reconstruction();StrictFailures();Pipeline(false,false);Pipeline(true,false);Pipeline(false,true);Pipeline(true,true);Admission(args[2]);Console.WriteLine("PASS actual Release 0x77 paired raw: "+checks+" reconstruction/fragmentation/wrap/header/order/sequence, legacy 0x76, strict decoder, DAT/merged/time/XML/metadata and admission checks; no hardware.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
