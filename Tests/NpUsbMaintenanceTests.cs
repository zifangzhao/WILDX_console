using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

static class NpUsbMaintenanceTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string Uid="001E002A5942500B20313354";
    const string PathV2=@"\\?\usb#vid_1209&pid_b386#001E002A5942500B20313354#{fixture}";
    static Assembly app;static Type del,sys;static int checks;
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static object Call(object target,string method,params object[] args){try{return (target as Type??target.GetType()).GetMethod(method,All).Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static object Field(object target,string name){return target.GetType().GetField(name,All).GetValue(target);}
    static void Reject(Action action,string why){try{action();}catch(Exception ex)when(ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is System.ComponentModel.Win32Exception){checks++;return;}throw new Exception("Accepted "+why);}
    static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static uint Crc(byte[] b,int length){uint c=0xffffffff;for(int i=0;i<length;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((c&1)!=0?0xedb88320U:0U);}return ~c;}
    static void Seal(byte[] b){Put(b,124,Crc(b,124));}
    static byte[] Service(uint cap=0x40000,uint mode=3,uint busy=0,uint pending=0,bool v1=false){var b=new byte[32];Put(b,0,v1?0x3153504eU:0x3253504eU);Put(b,4,v1?257U:4U);Put(b,8,mode);Put(b,12,v1?cap|15U:cap);Put(b,16,busy);Put(b,20,pending);Put(b,24,0x001e002a);Put(b,28,0x5942500b^0x20313354);return b;}
    static byte[] Receipt(uint op=1,uint phase=3,uint nonce=7){var b=new byte[128];Put(b,0,0x3155444e);Put(b,4,1);Put(b,8,op);Put(b,12,phase);Put(b,16,nonce);Put(b,24,9);Put(b,28,3);Put(b,32,500000000);Put(b,36,5);Put(b,40,20000);Put(b,44,4);Put(b,48,15000);Put(b,52,20000);Put(b,56,123);Put(b,60,456);for(int i=0;i<4;i++)Put(b,64+i*4,(uint)(100+i));Put(b,80,0x001e002a);Put(b,84,0x5942500b);Put(b,88,0x20313354);Put(b,92,120000000);Seal(b);return b;}
    static object Manager(Func<byte[]> service,Func<byte[]> report,Action<uint> inspect,Action<byte[]> commit,Func<bool> available){return Activator.CreateInstance(del,All,null,new object[]{Uid,service,report,inspect,commit,available},null);}
    static byte[] Sd(uint state=0,uint sequence=7){var b=new byte[384];Put(b,0,0x53444946);Put(b,4,1);Put(b,8,384);Put(b,12,state);Put(b,16,sequence);Put(b,168,0x00ffffff);Put(b,240,0x00ff0ff2);Put(b,312,0x00ff0ff3);return b;}
    static void DeleteCases()
    {
        var first=Receipt();var confirmed=Call(del,"Parse",first,Uid);
        Need((bool)confirmed.GetType().GetProperty("CanDelete",All).GetValue(confirmed),"Valid inspected entry refused");
        for(int i=0;i<128;i++){var b=(byte[])first.Clone();b[i]^=1;Reject(()=>Call(del,"Parse",b,Uid),"corrupt receipt byte "+i);}
        var wrong=(byte[])first.Clone();Put(wrong,80,99);Seal(wrong);Reject(()=>Call(del,"Parse",wrong,Uid),"replaced UID");
        Need((bool)Call(del,"Supported",Service(),Uid),"New idle service unavailable");
        foreach(var s in new[]{Service(0),Service(0x40000,2),Service(0x40000,3,1),Service(0x40000,3,0,2)})Need(!(bool)Call(del,"Supported",s,Uid),"Unsupported/busy service offered deletion");
        int writes=0;byte[] current=first;var manager=Manager(()=>Service(),()=>current,n=>{},b=>{writes++;Need(U(b,8)==2&&U(b,16)==8&&U(b,96)==7&&U(b,124)==Crc(b,124),"Delete request not bound to inspection");current=Receipt(2,3,8);Put(current,28,1);Put(current,36,4);Put(current,40,15000);Put(current,96,7);Seal(current);},()=>true);
        Call(manager,"Delete",confirmed);Need(writes==1,"Delete mutation count");
        foreach(int at in new[]{16,24,32,36,40,44,48,52,56,60,64,92}){
            current=(byte[])first.Clone();Put(current,at,U(current,at)+1);Seal(current);writes=0;
            var m=Manager(()=>Service(),()=>current,n=>{},b=>writes++,()=>true);Reject(()=>Call(m,"Delete",confirmed),"changed confirmation field "+at);Need(writes==0,"Changed snapshot deleted");
        }
        current=first;writes=0;manager=Manager(()=>Service(),()=>current,n=>{},b=>{writes++;throw new IOException("lost acknowledgement");},()=>true);
        Reject(()=>Call(manager,"Delete",confirmed),"lost delete ACK");Need(writes==1,"Lost ACK replayed deletion");
        foreach(var service in new[]{Service(0),Service(0x40000,2),Service(0x40000,3,1)}){
            writes=0;manager=Manager(()=>service,()=>first,n=>writes++,b=>writes++,()=>true);Reject(()=>Call(manager,"Inspect"),"legacy/busy inspect");Need(writes==0,"Unsupported firmware received OUT");
        }
        writes=0;current=Receipt(0,0,0);manager=Manager(()=>Service(),()=>current,n=>{writes++;current=Receipt(1,3,n);},b=>{},()=>true);Call(manager,"Inspect");Need(writes==1,"Inspection was replayed");
        writes=0;manager=Manager(()=>Service(),()=>first,n=>{},b=>writes++,()=>false);Reject(()=>Call(manager,"Delete",confirmed),"lost host ownership");Need(writes==0,"Unavailable host deleted");
        foreach(int at in new[]{24,32,36,40,64,96}){
            current=first;writes=0;manager=Manager(()=>Service(),()=>current,n=>{},b=>{writes++;current=Receipt(2,3,8);Put(current,28,1);Put(current,36,4);Put(current,40,15000);Put(current,96,7);Put(current,at,U(current,at)+1);Seal(current);},()=>true);
            Reject(()=>Call(manager,"Delete",confirmed),"unconfirmed deletion result "+at);Need(writes==1,"Unconfirmed result replayed deletion");
        }
    }
    static void SdCases()
    {
        int outs=0;byte[] cache=Sd();bool v1=false;uint mode=3;
        Func<byte,byte,ushort,int,byte[]> read=(type,req,value,length)=>{
            Need(type==0xc0&&value==0,"Unexpected SD query");if(req==0x6e)return Service(0x20000,mode,0,0,v1);if(req==0xa5)return cache;throw new IOException("Unexpected request "+req);
        };
        Action<ushort> send=seq=>{outs++;Need(seq==8,"Refresh sequence");cache=Sd(2,seq);};
        foreach(bool family in new[]{false,true}){v1=family;mode=family?2U:3U;cache=Sd();outs=0;var got=(byte[])Call(sys,"RefreshCard",family?PathV2.Replace("#001E","#npv1-001E"):PathV2,family?"NP V1":"NP V2",mode,read,send,cache);Need(U(got,12)==2&&outs==1,"New family refresh failed");}
        v1=false;mode=3;cache=Sd();outs=0;
        foreach(var s in new[]{Service(0),Service(0x20000,2),Service(0x20000,3,1),Service(0x20000,3,0,2)}){
            Func<byte,byte,ushort,int,byte[]> unavailable=(t,r,v,l)=>s;Reject(()=>Call(sys,"RefreshCard",PathV2,"NP V2",3U,unavailable,send,cache),"unsupported/busy refresh");Need(outs==0,"Unsafe SD refresh sent OUT");
        }
        cache=Sd(1);Reject(()=>Call(sys,"RefreshCard",PathV2,"NP V2",3U,read,send,cache),"active refresh");Need(outs==0,"Active query replayed");
        cache=Sd();Action<ushort> lost=n=>{outs++;throw new IOException("lost ACK");};Reject(()=>Call(sys,"RefreshCard",PathV2,"NP V2",3U,read,lost,cache),"refresh lost ACK");Need(outs==1,"Refresh replayed");
        cache=Sd();outs=0;Action<ushort> mismatch=n=>{outs++;cache=Sd(2,99);};Reject(()=>Call(sys,"RefreshCard",PathV2,"NP V2",3U,read,mismatch,cache),"stale completion");Need(outs==1,"Wrong SD sequence replayed");
        // Public passive path must retain legacy capacity and never request A6.
        foreach(bool family in new[]{false,true}){
            var requests=new List<byte>();var descriptor=new byte[18];descriptor[0]=18;descriptor[1]=1;descriptor[8]=9;descriptor[9]=0x12;descriptor[10]=0x86;descriptor[11]=0xb3;
            Func<byte,byte,ushort,int,byte[]> legacy=(t,r,v,l)=>{requests.Add(r);if(r==6)return descriptor;if(r==0x6e)return Service(0,3,0,0,family);if(r==0x60){var b=new byte[32];Put(b,0,0x3144504e);Put(b,4,1);Put(b,8,125000000);Put(b,28,1);return b;}throw new System.ComponentModel.Win32Exception(31);};
            var result=Call(sys,"ReadSnapshot",family?PathV2.Replace("#001E","#npv1-001E"):PathV2,family?"NP V1":"NP V2",legacy);
            Need(((string)Field(result,"Capacity")).Contains("64.00"),"Legacy capacity lost");Need(!requests.Contains(0xa5)&&!requests.Contains(0x4c)&&!requests.Contains(0xa6),"Legacy received new SD commands");
            outs=0;Action<ushort> forbidden=n=>outs++;
            result=Call(sys,"ReadSnapshotCore",family?PathV2.Replace("#001E","#npv1-001E"):PathV2,family?"NP V1":"NP V2",legacy,forbidden);
            Need(outs==0&&((string)Field(result,"Capacity")).Contains("64.00"),"Active SD Info button changed legacy firmware state");
        }
        var card=new byte[64];Put(card,0,0x3153464e);Put(card,4,1);Put(card,20,125000000);Put(card,24,0x001e002a);Put(card,28,0x5942500b);Put(card,32,0x20313354);Put(card,36,0x12345678);Put(card,56,3);Put(card,60,Crc(card,60));
        var desc=new byte[18];desc[0]=18;desc[1]=1;desc[8]=9;desc[9]=0x12;desc[10]=0x86;desc[11]=0xb3;
        cache=Sd();outs=0;
        Func<byte,byte,ushort,int,byte[]> active=(t,r,v,l)=>r==6?desc:r==0x6e?Service(0x20000,2):r==0xa5?cache:throw new IOException("Unexpected active SD Info query");
        var activeResult=Call(sys,"ReadSnapshotCore",PathV2,"NP V2",active,send);
        Need(outs==1&&(bool)Field(activeResult,"CardRefreshed"),"Actual active SD Info path did not submit A6");
        Func<byte,byte,ushort,int,byte[]> v2Cid=(t,r,v,l)=>{if(r==6)return desc;if(r==0x6e)return Service(0x20);if(r==0x79)return card;if(r==0x60){var b=new byte[32];Put(b,0,0x3144504e);Put(b,4,1);Put(b,8,125000000);Put(b,28,1);return b;}throw new Exception("Unexpected V2 CID request");};
        Need(((string)Field(Call(sys,"ReadSnapshot",PathV2,"NP V2",v2Cid),"Card")).Contains("12345678"),"V2 supported passive CID omitted");
    }
    static int Main(string[] args){try{app=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));del=app.GetType("USB512_interface.NpUsbDeleteLast",true);sys=app.GetType("USB512_interface.NpSystemInfo",true);DeleteCases();SdCases();Console.WriteLine("PASS "+checks+" actual-assembly USB maintenance, identity/nonce/CRC, one-shot delete, idle refresh and legacy fallback checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
