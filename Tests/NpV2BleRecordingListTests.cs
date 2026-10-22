using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class NpV2BleRecordingListTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static int checks,reads,mutations;static bool pending,fail;static object deviceStatus;
    static Type T(string name){return app.GetType("USB512_interface."+name,true);}
    static object New(string name,params object[] args){return Activator.CreateInstance(T(name),All,null,args,null);}
    static object Get(object value,string name){return value.GetType().GetField(name,All).GetValue(value);}
    static void Set(object value,string name,object content){value.GetType().GetField(name,All).SetValue(value,content);}
    static object Call(object target,string method,params object[] args){try{return (target as Type??target.GetType()).GetMethod(method,All).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Need(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(IOException){checks++;return;}throw new Exception(message);}
    static Task<V> Read<V>(){reads++;if(fail)throw new IOException("Recording is still finalizing. The list will refresh when it finishes.");return Task.FromResult((V)Catalog());}
    static Task<V> Delete<V>(V value){mutations++;throw new Exception("Unexpected delete");}
    static Task<V> Status<V>(){return Task.FromResult((V)deviceStatus);}
    static Task<V> Mutate<V>(byte[] request){mutations++;throw new Exception("Unexpected mutation");}
    static Delegate Callback(string method,Type type){var value=typeof(NpV2BleRecordingListTests).GetMethod(method,All).MakeGenericMethod(type);return Delegate.CreateDelegate(method=="Read"?typeof(Func<>).MakeGenericType(typeof(Task<>).MakeGenericType(type)):typeof(Func<,>).MakeGenericType(type,typeof(Task<>).MakeGenericType(type)),value);}
    static object Catalog(){
        var value=New("NpBleCatalog");Set(value,"Capacity",1048576U);Set(value,"NextStart",32768U);
        var record=New("NpBleRecord");Set(record,"Id",1U);Set(record,"Start",8192U);Set(record,"End",32768U);
        ((IList)Get(value,"Records")).Add(record);return value;
    }
    static Form Dialog(string peer,bool supported,bool auto){
        var form=(Form)New("NpBleStorageForm",peer,1048576U,Callback("Read",T("NpBleCatalog")),Callback("Delete",T("NpBleCatalog")),(Func<bool,Task>)(format=>{mutations++;throw new Exception("Unexpected storage mutation");}),supported);
        if(form.GetType().GetField("RefreshOnOpen",All)!=null){Set(form,"RefreshOnOpen",auto);Set(form,"RefreshPending",(Func<bool>)(()=>pending));}
        form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-20000,-20000);return form;
    }
    static void Pump(){Application.DoEvents();Thread.Sleep(1);}
    static void Put(byte[] body,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,body,at,4);}
    static byte[] Seal(byte[] body){Put(body,60,(uint)Call(app.GetType("CE32_console.NpSdControl",true),"Crc",body,60));return body;}
    static byte[] Info(uint flags){
        var body=new byte[64];uint[] words={0x32524c4e,2,9,71,0,123,1,32768,1048576,456,flags,0,8192,32768,789};
        for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);return Seal(body);
    }
    static void CatalogFlags(){
        foreach(uint flags in new uint[]{0,0x10000,0x60000,0x800000,0x810000,0x820000,0x4820000}){
            var info=Call(T("NpV2BleCatalog"),"ParseInfo",Info(flags),71U);Need((uint)Get(info,"Flags")==flags,"Catalog flags lost");
            var body=new byte[64];uint[] words={0x32524c4e,2,10,72,0,123,1,0,8192,32768,789,42,20000,7,14|(4U<<8)|(1U<<16)|(1U<<18)|(1U<<19)};
            for(int i=0;i<words.Length;i++)Put(body,i*4,words[i]);Seal(body);
            var entry=Call(T("NpV2BleCatalog"),"ParseEntry",body,72U,info,0U,8192U);
            Need((string)Call(T("NpV2BleCatalog"),"RecordStatus",entry)=="Incomplete","Incomplete recording hidden or promoted");
            if(flags!=0 && flags!=0x10000){Put(body,56,14|(3U<<8)|(1U<<16)|(1U<<17)|(1U<<18));Seal(body);Reject(()=>Call(T("NpV2BleCatalog"),"ParseEntry",body,72U,info,0U,8192U),"Recovery/degraded catalog enabled deletion");}
        }
        foreach(uint flags in new uint[]{0x20000000,0x4000000,0x4800000,0x40000})Reject(()=>Call(T("NpV2BleCatalog"),"ParseInfo",Info(flags),71U),"Unknown/contradictory catalog flags accepted");
        var bad=Info(0x800000);Put(bad,28,40960);Seal(bad);Reject(()=>Call(T("NpV2BleCatalog"),"ParseInfo",bad,71U),"Degraded flag allowed an invented allocation frontier");
        var info2=Call(T("NpV2BleCatalog"),"ParseInfo",Info(0x800000),71U);Need((uint)Get(info2,"LastEnd")==32768U,"Degraded extent changed");
    }
    static void Reader(){
        var statusType=app.GetType("CE32_console.NpSdStatus",true);
        deviceStatus=Activator.CreateInstance(statusType,true);Set(deviceStatus,"State",4U);Set(deviceStatus,"Flags",0x400000U|17U|4U);Set(deviceStatus,"Capacity",1048576U);
        var taskType=typeof(Task<>).MakeGenericType(statusType);
        var readStatus=Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(taskType),typeof(NpV2BleRecordingListTests).GetMethod("Status",All).MakeGenericMethod(statusType));
        var mutate=Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(byte[]),taskType),typeof(NpV2BleRecordingListTests).GetMethod("Mutate",All).MakeGenericMethod(statusType));
        int traffic=0;Func<byte[],Task<byte[]>> wire=request=>{throw new Exception("Premature catalog I/O");};
        var reader=New("NpV2BleCatalog",readStatus,(Func<byte[],Task<byte[]>>)(request=>{traffic++;return wire(request);}),mutate,(Action)(()=>{}));
        try{((Task)Call(reader,"Read")).GetAwaiter().GetResult();throw new Exception("Finalizing state accepted");}catch(IOException ex){Need(ex.Message.Contains("still finalizing"),"Production reader did not report finalization");}
        Need(traffic==0,"Finalizing state sent catalog commands");
        Set(deviceStatus,"State",5U);Set(deviceStatus,"Flags",0x400000U|17U|0x800000U);Need((bool)Call(T("NpV2BleCatalog"),"CanRead",deviceStatus),"Stopped degraded catalog blocked");
        Set(deviceStatus,"Flags",17U);Reject(()=>((Task)Call(reader,"Read")).GetAwaiter().GetResult(),"Missing capability bypassed");Need(traffic==0,"Old firmware received a catalog request");
        Set(deviceStatus,"Flags",0x400000U|17U|0x800000U);Set(deviceStatus,"Start",32768U);
        wire=request=>{
            var body=Info(0x800000);uint op=BitConverter.ToUInt32(request,8);Put(body,8,op);Put(body,12,BitConverter.ToUInt32(request,12));
            if(op==10){uint[] fields={123,1,0,8192,32768,789,42,20000,7,14|(4U<<8)|(1U<<16)|(1U<<18)|(1U<<19)};for(int i=0;i<fields.Length;i++)Put(body,20+4*i,fields[i]);}
            else Need(op==9,"Reader sent a non-catalog request");return Task.FromResult(Seal(body));
        };
        var task=(Task)Call(reader,"Read");task.GetAwaiter().GetResult();var result=task.GetType().GetProperty("Result").GetValue(task);
        Need(traffic==3 && ((IList)Get(result,"Records")).Count==1,"Degraded INFO/ENTRY snapshot did not list its readable recording");
        Need(!(bool)result.GetType().GetProperty("CanDeleteLast",All).GetValue(result,null),"Degraded list enabled deletion");
        Need(mutations==0,"Read-only listing sent a mutation");
    }
    static void Ui(){
        reads=mutations=0;pending=fail=false;
        using(var form=Dialog("V1 · TEST",true,false)){form.Show();Pump();Need(reads==0,"V1 recovery dialog issued an automatic read");Need(((Label)Get(form,"info")).Text.Contains("Format does not require"),"V1 recovery guidance missing");}
        using(var form=Dialog("V2 · OLD",false,true)){form.Show();Pump();Need(reads==0,"Old firmware received an unsupported catalog command");Need(((Label)Get(form,"info")).Text.Contains("APP firmware update"),"Missing capability not explained");Need(!((FlowLayoutPanel)Get(form,"actions")).Controls[0].Enabled,"Unsupported Refresh enabled");}
        using(var form=Dialog("V2 · TEST",true,true)){form.Show();Pump();Need(reads==1,"V2 did not list automatically on open");Need(((ListView)Get(form,"records")).Items.Count==1,"Automatic list did not show its recording");Need(!((Button)Get(form,"remove")).Enabled,"Unverified fake catalog enabled Delete");}
        reads=0;pending=fail=true;
        using(var form=Dialog("V2 · STOPPING",true,true)){
            form.Show();Pump();Need(reads==1 && ((ListView)Get(form,"records")).Items.Count==0,"Finalizing list reported an empty successful catalog");
            Need(((Label)Get(form,"info")).Text.Contains("still finalizing"),"Finalizing status missing");
            fail=false;var timer=(System.Windows.Forms.Timer)Get(form,"refreshTimer");typeof(System.Windows.Forms.Timer).GetMethod("OnTick",All).Invoke(timer,new object[]{EventArgs.Empty});Pump();
            Need(reads==2 && ((ListView)Get(form,"records")).Items.Count==1,"List did not recover automatically after finalization");
            pending=false;typeof(System.Windows.Forms.Timer).GetMethod("OnTick",All).Invoke(timer,new object[]{EventArgs.Empty});Pump();Need(reads==2,"Completed list kept polling the card");
        }
        Need(mutations==0,"Opening/refreshing the list modified storage");
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(args[0]);CatalogFlags();Reader();Ui();Console.WriteLine("PASS "+checks+" actual-app BLE list flags/initialization/finalization/legacy compatibility checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
