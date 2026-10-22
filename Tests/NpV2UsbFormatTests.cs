using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NpV2UsbFormatTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    const string Uid="001A0020434250062039314E";
    static Assembly app;static Type format;static int checks;
    static object Get(object x,string name){return x.GetType().GetField(name,All).GetValue(x);}
    static object Call(object x,string name,params object[] args){try{return(x is Type?(Type)x:x.GetType()).GetMethod(name,All).Invoke(x is Type?null:x,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Reject(Action call,string why,bool unsupported=false){try{call();}catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is NotSupportedException){Need(!unsupported || ex is NotSupportedException,"Missing capability did not offer update");return;}throw new Exception("Accepted "+why);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    static uint Crc(byte[] b){uint c=uint.MaxValue;for(int i=0;i<60;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
    static byte[] Info(uint state=0,uint txn=7){var b=new byte[64];Put(b,0,0x3153464e);Put(b,4,1);Put(b,8,state);Put(b,16,txn);Put(b,20,500006912);Put(b,24,0x001a0020);Put(b,28,0x43425006);Put(b,32,0x2039314e);for(int i=0;i<4;i++)Put(b,36+i*4,(uint)(300+i));Put(b,52,state==3?8192U:40960U);Put(b,56,3);Seal(b);return b;}
    static void Seal(byte[] b){Put(b,60,Crc(b));}
    sealed class Fixture : IDisposable
    {
        internal byte[] Service=new byte[32];internal Queue<byte[]> Replies=new Queue<byte[]>();internal bool Available=true,LostAck,LostReceipt;internal int Writes,InfoReads,ServiceReads;internal byte[] Request;internal object Manager;
        internal Fixture(){Put(Service,0,0x3253504e);Put(Service,4,4);Put(Service,8,3);Put(Service,12,0x3f);Put(Service,24,0x001a0020);Put(Service,28,0x43425006^0x2039314e);
            Manager=Activator.CreateInstance(format,All,null,new object[]{Uid,(Func<byte[]>)(()=>{ServiceReads++;return Service;}),(Func<byte[]>)(()=>{InfoReads++;if(LostReceipt&&Writes>0)throw new IOException("lost receipt");return Replies.Dequeue();}),(Action<byte[]>)(b=>{Writes++;Request=b;if(LostAck)throw new IOException("lost ACK");}),(Func<bool>)(()=>Available)},null);}
        internal object Inspect(){return Call(Manager,"Inspect");}
        internal void Format(object card){Call(Manager,"Format",card);}
        public void Dispose(){((IDisposable)Manager).Dispose();}
    }
    [STAThread]static int Main(string[] args){try{
        app=Assembly.LoadFrom(Path.GetFullPath(args[0]));format=app.GetType("USB512_interface.NpV2UsbFormat",true);
        using(var f=new Fixture()){
            f.Replies.Enqueue(Info());var card=f.Inspect();f.Replies.Enqueue(Info());f.Replies.Enqueue(Info(1,8));f.Replies.Enqueue(Info(2,8));f.Replies.Enqueue(Info(3,8));f.Format(card);
            Need(f.Writes==1&&f.InfoReads==5&&f.ServiceReads==3,"Format replayed or skipped idle/completion checks");
            Need(U(f.Request,0)==0x3146534e&&U(f.Request,4)==1&&U(f.Request,8)==1&&U(f.Request,12)==8&&U(f.Request,16)==500006912,"Request framing changed");
            byte[] original=Info();for(int i=0;i<28;i++)Need(f.Request[20+i]==original[24+i],"Full UID/CID not bound");Need(U(f.Request,48)==0x46524d54&&U(f.Request,52)==0&&U(f.Request,56)==0&&U(f.Request,60)==Crc(f.Request),"Destructive key/reserved/CRC changed");
        }
        foreach(uint caps in new[]{0U,0x10U,0x1fU})using(var f=new Fixture()){Put(f.Service,12,caps);Reject(()=>f.Inspect(),"repair mistaken for format",true);Need(f.InfoReads==0&&f.Writes==0,"Old firmware received format query/mutation");}
        foreach(int kind in new[]{0,1,2,3,4,5,6})using(var f=new Fixture()){
            if(kind==0)f.Available=false;if(kind==1)Put(f.Service,0,0x3153504e);if(kind==2)Put(f.Service,4,3);if(kind==3)Put(f.Service,8,2);if(kind==4)Put(f.Service,16,1);if(kind==5)Put(f.Service,20,3);if(kind==6)Put(f.Service,24,42);
            Reject(()=>f.Inspect(),"wrong owner/service/mode/UID");Need(f.Writes==0&&f.InfoReads==0,"Rejected service reached card format interface");
        }
        foreach(int kind in new[]{0,1,2,3,4,5,6,7})using(var f=new Fixture()){
            var b=Info();if(kind==0)b[60]^=1;if(kind==1)Put(b,24,99);if(kind==2)Put(b,56,1);if(kind==3)Put(b,8,1);if(kind==4)Put(b,20,8192);if(kind==5)Array.Clear(b,36,16);if(kind==6)for(int i=36;i<52;i++)b[i]=255;if(kind==7)Put(b,0,0);
            if(kind!=0)Seal(b);f.Replies.Enqueue(b);Reject(()=>f.Inspect(),"bad/unready card");Need(f.Writes==0,"Invalid card sent Format");
        }
        foreach(int kind in new[]{0,1,2,3,4,5,6,7})using(var f=new Fixture()){
            f.Replies.Enqueue(Info());var card=f.Inspect();var fresh=Info();if(kind==0)Put(fresh,20,500006913);if(kind==1)Put(fresh,36,999);if(kind==2)Put(fresh,24,42);if(kind==3)Put(fresh,16,8);if(kind==4)Put(fresh,8,3);if(kind==5)Put(fresh,56,1);if(kind==6)f.Available=false;if(kind==7)Put(f.Service,16,1);Seal(fresh);f.Replies.Enqueue(fresh);
            Reject(()=>f.Format(card),"changed confirmation");Need(f.Writes==0,"Stale confirmation sent Format");
        }
        foreach(int kind in new[]{0,1,2,3,4,5,6,7})using(var f=new Fixture()){
            f.Replies.Enqueue(Info());var card=f.Inspect();f.Replies.Enqueue(Info());var done=Info(3,8);
            if(kind==0)Put(done,16,9);if(kind==1)Put(done,52,40960);if(kind==2)Put(done,12,5);if(kind==3)Put(done,8,4);if(kind==4)Put(done,36,999);if(kind==5)done[60]^=1;if(kind==6)f.LostAck=true;
            if(kind!=5)Seal(done);f.Replies.Enqueue(done);
            if(kind==7)f.LostReceipt=true;
            Reject(()=>f.Format(card),"unconfirmed completion");Need(f.Writes==1,"Failed acknowledgement/completion replayed Format");
        }
        using(var f=new Fixture()){f.Replies.Enqueue(Info());var card=f.Inspect();Need(f.Writes==0,"Read-only inspection formatted without confirmation");}
        var formType=app.GetType("CE32_console.NpDownloadForm",true);using(var form=(Form)Activator.CreateInstance(formType,All,null,new object[]{null,false},null)){
            string path=@"\\?\usb#vid_1209&pid_b386#npv2-"+Uid+"#{test}";Call(form,"PollUsbPaths",(object)new[]{path});Need(((Button)Get(form,"format")).Enabled,"V2 Format remains disabled");
            Call(form,"SetBusy",true);Need(!((Button)Get(form,"format")).Enabled,"Format available during transfer");Call(form,"SetBusy",false);Call(form,"SetUsbBlocked",true);Need(!((Button)Get(form,"format")).Enabled,"Format available with BLE owner");
        }
        using(var update=(Form)Activator.CreateInstance(app.GetType("USB512_interface.NpV2FirmwareForm",true),All,null,new object[]{null},null)){Call(update,"SelectFullBundle");Need(((ComboBox)Get(update,"bundledPart")).SelectedIndex==1,"Required USB update defaults to APP only");}
        Console.WriteLine("PASS "+checks+" actual-app V2 USB format compatibility/admission/one-shot receipt/UI checks; fake boundaries only, no hardware.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
