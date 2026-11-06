using System;
using System.IO;
using System.Reflection;
using CE32_console;
using USB512_interface;
using WildNpClosedLoop;

// Pure production parsers + extracted production admission/receipt functions.
// No app construction, BLE discovery, firmware build or hardware operations.
static class NpBlePreviewAdmissionTests
{
    static int checks;
    static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Reject(Action action,string why){try{action();}catch(IOException){checks++;return;}catch(InvalidDataException){checks++;return;}throw new Exception(why);}
    static void Put(byte[] b,int at,uint v){BitConverter.GetBytes(v).CopyTo(b,at);}
    static byte[] Reply(byte[] request,uint flags,uint result=0,uint config=0){
        var q=NpClosedLoopControl.Parse(request);var b=new byte[64];
        System.Text.Encoding.ASCII.GetBytes("NPCR").CopyTo(b,0);Put(b,4,1);Put(b,8,q.Operation);Put(b,12,q.RequestId);
        Put(b,16,result);Put(b,20,flags);Put(b,24,config);Put(b,36,1);Put(b,40,2);Put(b,44,7);Put(b,48,64);Put(b,52,511);
        Put(b,60,NpClosedLoopEvent.Crc(b,60));return b;
    }
    static void Admission(){
        foreach(bool available in new[]{false,true})foreach(bool? configured in new bool?[]{null,false,true})
            Need(NpBleForm.CanPreviewHost(available,configured)==(available&&configured==false),"Preview ignored current device/CL state");
        Need(NpBleForm.PreviewBlockedReason(true).Contains("closed-loop"),"Configured reason missing");
        Need(NpBleForm.PreviewBlockedReason(null).Contains("did not confirm"),"Unknown firmware reason missing");
        Need(NpBleForm.PreviewBlockedReason(false)==String.Empty,"Confirmed-off firmware falsely blocked");
        var query=NpClosedLoopControl.Request(2,10);
        foreach(uint flags in new uint[]{3,7,19,23,31})Need(NpBleForm.ClosedLoopPreviewState(query,Reply(query,flags))==true,"Configured/active/armed receipt lost");
        foreach(uint flags in new uint[]{0,1,9})Need(NpBleForm.ClosedLoopPreviewState(query,Reply(query,flags))==false,"Confirmed idle/unsupported receipt refused");
        Need(NpBleForm.ClosedLoopPreviewState(query,Reply(query,0,1))==null,"Rejected query became confirmed off");
        var stale=Reply(NpClosedLoopControl.Request(2,11),1);Reject(()=>NpBleForm.ClosedLoopPreviewState(query,stale),"Stale receipt trusted after reconnect");
        var corrupt=Reply(query,1);corrupt[60]^=1;Reject(()=>NpBleForm.ClosedLoopPreviewState(query,corrupt),"Invalid CRC trusted");
        Reject(()=>NpBleForm.ClosedLoopPreviewState(query,null),"Missing receipt became off");
        var enable=NpClosedLoopControl.Request(1,12,15000,0,10000,new[]{new NpClosedLoopChannel{Probe=1,Channel=0,Filter=6,Gain=1}});
        uint crc=NpClosedLoopControl.Parse(enable).ConfigCrc;
        Need(NpBleForm.ClosedLoopPreviewState(enable,Reply(enable,19,0,crc))==true,"Confirmed configuration not observed");
        Need(NpBleForm.ClosedLoopPreviewState(enable,Reply(enable,19,0,crc+1))==null,"Wrong configuration CRC trusted");
        Need(NpBleForm.ClosedLoopPreviewState(enable,Reply(enable,1))==null,"Missing configured flag trusted");
        var disable=NpClosedLoopControl.Request(1,13);
        Need(NpBleForm.ClosedLoopPreviewState(disable,Reply(disable,1))==false,"Confirmed disable not observed");
        Need(NpBleForm.ClosedLoopPreviewState(disable,Reply(disable,3))==null,"Failed disable freed preview");
    }
    static byte[] Packet(uint hz,uint band,uint stride,bool compact){
        var b=new byte[224];uint[] words={NpV1PreviewBurst.Magic,91,7,hz,50,0,compact?(128U<<16)|16U:(160U<<16)|65535U,1U|((compact?5U:2U)<<8)|(band<<12)};
        for(int i=0;i<words.Length;i++)Put(b,4*i,words[i]);Put(b,192,stride);
        if(compact){Put(b,168,65535);Put(b,200,1);Put(b,204,1);Put(b,220,NpSdControl.Crc(b,220));}return b;
    }
    static void Strides(){
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(uint band in new uint[]{0,1})foreach(bool compact in new[]{false,true}){
            uint old=band==0?((hz+499)/500)*780:((hz+5999)/6000)*9360;
            uint current=band==0?old:Math.Max(1U,hz/6000)*9360;
            foreach(uint stride in new[]{old,current}){
                var burst=new NpV1PreviewBurst(91);Need(burst.Accept(Packet(hz,band,stride,compact)),"Legacy/new packet rejected");
                Need(Math.Abs(burst.PreviewHz-hz*780.0/stride)<1e-9,"Display rate ignored actual packet stride");
            }
            Reject(()=>new NpV1PreviewBurst(91).Accept(Packet(hz,band,current+1,compact)),"Arbitrary stride admitted");
            if(band==1)Need(hz*780.0/current>=500,"New LFP stride below500Hz");
        }
        foreach(uint hz in new uint[]{0,10000,40000})Need(!NpV1PreviewBurst.ValidStride(hz,1,9360),"Unsupported source rate admitted");
    }
    static void FirmwareVectors(string path){
        byte[] data=File.ReadAllBytes(path);Need(data.Length>0&&data.Length%224==0,"Actual firmware vector extent");
        int lfp=0;for(int at=0;at<data.Length;at+=224){
            var b=new byte[224];Buffer.BlockCopy(data,at,b,0,224);var burst=new NpV1PreviewBurst(NpSdGeometry.U(b,4));
            Need(burst.Accept(b),"Actual staged firmware packet rejected");
            Need(Math.Abs(burst.PreviewHz-burst.Hz*780.0/burst.Stride)<1e-9,"Firmware display rate mismatch");
            if(burst.Band==1){lfp++;Need(burst.PreviewHz>=500,"Actual staged LFP preview below500Hz");}
        }
        Need(lfp!=0,"No actual firmware LFP vectors tested");
    }
    static void InstalledAssembly(string appPath,string vectors){
        const BindingFlags all=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var app=Assembly.LoadFrom(Path.GetFullPath(appPath));var guard=app.GetType("USB512_interface.NpBleForm",true);
        foreach(bool available in new[]{false,true})foreach(bool? configured in new bool?[]{null,false,true})
            Need((bool)guard.GetMethod("CanPreviewHost",all).Invoke(null,new object[]{available,configured})==NpBleForm.CanPreviewHost(available,configured),"Actual app preview guard differs");
        var query=NpClosedLoopControl.Request(2,41);
        foreach(uint flags in new uint[]{0,1,3,7,19,23,31}){
            var reply=Reply(query,flags);Need((bool?)guard.GetMethod("ClosedLoopPreviewState",all).Invoke(null,new object[]{query,reply})==NpBleForm.ClosedLoopPreviewState(query,reply),"Actual app closed-loop receipt guard differs");
        }
        var type=app.GetType("CE32_console.NpV1PreviewBurst",true);var accept=type.GetMethod("Accept",all);var rate=type.GetProperty("PreviewHz",all);
        Action<byte[]> packet=bytes=>{
            var burst=Activator.CreateInstance(type,all,null,new object[]{NpSdGeometry.U(bytes,4)},null);
            Need((bool)accept.Invoke(burst,new object[]{bytes}),"Actual app rejected preview packet");
            Need(Math.Abs((double)rate.GetValue(burst,null)-NpSdGeometry.U(bytes,12)*780.0/NpSdGeometry.U(bytes,192))<1e-9,"Actual app display rate is not packet-derived");
        };
        foreach(uint hz in new uint[]{7500,15000,20000,30000})foreach(bool compact in new[]{false,true})foreach(uint stride in new[]{((hz+5999)/6000)*9360,Math.Max(1U,hz/6000)*9360})packet(Packet(hz,1,stride,compact));
        byte[] data=File.ReadAllBytes(vectors);for(int at=0;at<data.Length;at+=224){var b=new byte[224];Buffer.BlockCopy(data,at,b,0,224);packet(b);}
    }
    static int Main(string[] args){try{Admission();Strides();FirmwareVectors(args[0]);if(args.Length>1)InstalledAssembly(args[1],args[0]);Console.WriteLine("PASS "+checks+" BLE preview/CL admission, validated receipt/reconnect uncertainty, legacy+floor stride and actual firmware packet checks"+(args.Length>1?" including actual app assembly":"")+"; no hardware/app build.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
