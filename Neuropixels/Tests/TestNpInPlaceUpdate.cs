using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;
using USB512_interface;

// Compile the maintained management source against an in-memory WinUSB model.
// The runner replaces only its private WritePipe P/Invoke declaration. Every
// enumeration, handle, control, policy and bulk operation below is a mock.
namespace CE32_console
{
    internal static class NpDownloadStream
    {
        internal static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal static uint Crc(byte[] b,int at,int count){uint c=~0U;for(int i=at;i<at+count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);}return ~c;}
    }
    internal static class NpDownloadNative
    {
        internal const string Uid="001E002A5942500B20313354";
        internal static readonly Guid NeuropixelsInterfaceGuid=Guid.Empty;
        internal struct WINUSB_SETUP_PACKET{internal byte RequestType,Request;internal ushort Value,Length;internal ushort Index{get{return 0;}}}
        internal static List<string> Paths=new List<string>();
        internal static List<ushort> Modes=new List<ushort>();
        internal static List<ushort> Operations=new List<ushort>();
        internal static List<string> Opened=new List<string>();
        internal static bool EnumerationAllowed,Transaction,AppValid,WrongUid,HoldEntry,ShortWrite,BadReply;
        internal static uint Role,OriginalRole,Capabilities,Busy,Pending,LoaderAbi,Offset;
        internal static int EntryPolls,BeginCalls,OutTimeout,InTimeout,FailOpcode,StatusReads;
        internal static byte[] Reply,Written;
        internal static string Path(bool recovery,string uid){return @"\\?\usb#vid_1209&pid_"+(recovery?"b387":"b386")+"#npv1-"+uid+"#{test}";}
        internal static void Reset(uint role=2,uint capabilities=79,bool recovery=false)
        {
            Paths.Clear();Paths.Add(Path(recovery,Uid));Modes.Clear();Operations.Clear();Opened.Clear();
            EnumerationAllowed=AppValid=true;Transaction=WrongUid=HoldEntry=ShortWrite=BadReply=false;
            Role=OriginalRole=role;Capabilities=capabilities;Busy=Pending=Offset=0;LoaderAbi=258;
            EntryPolls=BeginCalls=OutTimeout=InTimeout=FailOpcode=StatusReads=0;Reply=null;Written=new byte[0];
        }
        internal static IList<string> EnumerateInterfacePaths(Guid guid){if(!EnumerationAllowed)throw new Exception("Update re-enumerated or substituted a device");return Paths;}
        internal static SafeFileHandle CreateFile(string path,uint a,uint b,IntPtr c,uint d,uint e,IntPtr f){Opened.Add(path);return new SafeFileHandle(new IntPtr(123),false);}
        internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr usb){usb=new IntPtr(456);return true;}
        internal static bool WinUsbFree(IntPtr usb){return true;}
        internal static bool WinUsbSetPipePolicy(IntPtr h,byte pipe,uint policy,uint size,ref uint timeout)
        {if(policy!=3 || size!=4 || timeout!=30000)throw new Exception("Wrong command timeout");if(pipe==1)OutTimeout=(int)timeout;else if(pipe==0x81)InTimeout=(int)timeout;else throw new Exception("Wrong pipe");return true;}
        internal static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        internal static bool WinUsbControlTransfer(IntPtr h,WINUSB_SETUP_PACKET setup,byte[] bytes,uint count,out uint sent,IntPtr overlap)
        {
            if(h!=new IntPtr(456) || setup.Index!=0)throw new Exception("Handle/index changed");sent=count;
            if(setup.Request==0x6f){
                if(setup.RequestType!=0x40 || count!=0 || Transaction || Busy!=0 || Pending!=0)throw new Exception("Unsafe mode OUT");
                Modes.Add(setup.Value);Pending=setup.Value;EntryPolls=2;return true;
            }
            if(setup.Request!=0x6e || setup.RequestType!=0xc0 || count!=32)throw new Exception("Unexpected control request");
            StatusReads++;
            if(Pending!=0 && !HoldEntry && --EntryPolls<=0){Role=Pending;Pending=0;}
            Put(bytes,0,0x3153504e);Put(bytes,4,258);Put(bytes,8,Role);Put(bytes,12,Capabilities);
            Put(bytes,16,Busy|(Transaction?1U:0));Put(bytes,20,Pending);
            Put(bytes,24,Convert.ToUInt32(Uid.Substring(0,8),16)^(WrongUid?1U:0));
            Put(bytes,28,Convert.ToUInt32(Uid.Substring(8,8),16)^Convert.ToUInt32(Uid.Substring(16,8),16));return true;
        }
        internal static bool WritePipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlap)
        {
            if(pipe!=1 || h!=new IntPtr(456) || OutTimeout!=30000 || InTimeout!=30000 || Reply!=null)throw new Exception("Pipe ownership/timeout changed");
            sent=ShortWrite?0:count;if(ShortWrite)return true;
            uint n=NpDownloadStream.U(b,20);ushort op=BitConverter.ToUInt16(b,6);
            if(NpDownloadStream.U(b,0)!=0x31564e58 || BitConverter.ToUInt16(b,4)!=2 || count!=32+n || n>480 || NpDownloadStream.U(b,24)!=NpDownloadStream.Crc(b,0,24) || NpDownloadStream.U(b,28)!=NpDownloadStream.Crc(b,32,(int)n))throw new Exception("Update request CRC/layout changed");
            Operations.Add(op);uint status=0,a=0,capacity=0;
            if(op==FailOpcode)status=7;
            else if(op==1){a=(LoaderAbi<<16)|(AppValid?1U:0)|(Transaction?2U:0)|4U;capacity=NpV1Management.Capacity;}
            else if(op==2){if(Transaction || n!=32 || Role!=4)throw new Exception("Unsafe BEGIN");BeginCalls++;Transaction=true;AppValid=false;Offset=0;Written=new byte[NpDownloadStream.U(b,12)];}
            else if(op==3){if(!Transaction || NpDownloadStream.U(b,12)!=Offset)throw new Exception("Nonsequential DATA");Buffer.BlockCopy(b,32,Written,(int)Offset,(int)n);Offset+=n;a=Offset;}
            else if(op==4){if(!Transaction || Offset!=Written.Length)throw new Exception("Incomplete COMMIT");Transaction=false;AppValid=true;a=1;}
            else if(op==5){Transaction=false;}
            else throw new Exception("Unexpected update opcode");
            Reply=new byte[32];Put(Reply,0,0x31564e58);Reply[4]=2;Buffer.BlockCopy(BitConverter.GetBytes((ushort)(op|0x8000)),0,Reply,6,2);
            Put(Reply,8,NpDownloadStream.U(b,8));Put(Reply,12,status);Put(Reply,16,a);Put(Reply,20,capacity);
            Put(Reply,24,NpDownloadStream.Crc(Reply,0,24));Put(Reply,28,NpDownloadStream.Crc(Reply,0,28));if(BadReply)Reply[28]^=1;return true;
        }
        internal static bool WinUsbReadPipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlap)
        {if(pipe!=0x81 || count!=32 || Reply==null)throw new Exception("Unexpected bulk read");Buffer.BlockCopy(Reply,0,b,0,32);Reply=null;sent=32;return true;}
    }
}

internal static class TestNpInPlaceUpdate
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();throw new Exception("Accepted "+message);}catch(Exception error){if(!(error is IOException) && !(error is InvalidDataException))throw;checks++;}}
    static NpV1Management Open(bool recovery=false)
    {
        var manager=new NpV1Management(new NpV1Target{Uid=CE32_console.NpDownloadNative.Uid,Path=CE32_console.NpDownloadNative.Path(recovery,CE32_console.NpDownloadNative.Uid),Recovery=recovery});
        CE32_console.NpDownloadNative.EnumerationAllowed=false;return manager;
    }
    static byte[] Image()
    {
        var image=new byte[128+1447];Buffer.BlockCopy(BitConverter.GetBytes(0x31464e4d31504e58UL),0,image,0,8);
        Action<int,uint> put=(at,v)=>CE32_console.NpDownloadNative.Put(image,at,v);
        put(8,1);put(12,0x56414c44);put(16,0x41505031);put(24,NpV1Management.AppBase);put(28,(uint)image.Length-128);put(80,258);
        for(int at=128;at<image.Length;at++)image[at]=(byte)(at*37);
        put(128,0x20010000);put(132,NpV1Management.AppBase+9);put(36,0x20010000);put(40,NpV1Management.AppBase+9);
        put(32,CE32_console.NpDownloadStream.Crc(image,128,image.Length-128));put(124,CE32_console.NpDownloadStream.Crc(image,0,124));return image;
    }
    static void Success(string package,byte[] image,uint role,bool legacy)
    {
        var native=typeof(CE32_console.NpDownloadNative); // Ensure the stub, never winusb.dll, is linked.
        CE32_console.NpDownloadNative.Reset(legacy?4:role,legacy?15U:79U,legacy);
        using(var manager=Open(legacy)){
            int progress=0;ushort returned=manager.Update(package,p=>progress=p);
            Check(returned==(legacy?4:role),"Original personality not returned");Check(progress==100,"Progress not complete");
            Check(CE32_console.NpDownloadNative.BeginCalls==1 && CE32_console.NpDownloadNative.AppValid && !CE32_console.NpDownloadNative.Transaction,"Transaction not committed exactly once");
            Check(CE32_console.NpDownloadNative.Opened.Count==1,"Update reopened another device");
            Check(CE32_console.NpDownloadNative.Opened[0]==CE32_console.NpDownloadNative.Path(legacy,CE32_console.NpDownloadNative.Uid),"Path/PID changed");
            Check(CE32_console.NpDownloadNative.Modes.Count==(legacy?0:role==4?0:2),"Wrong mode transitions");
            if(!legacy && role!=4){Check(CE32_console.NpDownloadNative.Modes[0]==4 && CE32_console.NpDownloadNative.Modes[1]==role,"Wrong in-place targets");Check(manager.ReadStatus().Role==role,"Returned role not verified");}
            for(int n=0;n<CE32_console.NpDownloadNative.Written.Length;n++)Check(CE32_console.NpDownloadNative.Written[n]==image[128+n],"APP payload changed");
            Check(native.Assembly==typeof(NpV1Management).Assembly,"Wrong transport model");
        }
    }
    static void Negative(string package)
    {
        foreach(uint role in new[]{2U,3U}){
            CE32_console.NpDownloadNative.Reset(role,15);using(var manager=Open()){Reject(()=>manager.Update(package,null),"unadvertised update");Check(CE32_console.NpDownloadNative.Modes.Count==0 && CE32_console.NpDownloadNative.BeginCalls==0,"Older service received update entry/erase");}
        }
        foreach(bool pending in new[]{false,true}){
            CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.Busy=pending?0U:1U;CE32_console.NpDownloadNative.Pending=pending?3U:0U;CE32_console.NpDownloadNative.HoldEntry=true;
            using(var manager=Open()){Reject(()=>manager.Update(package,null),"busy/pending update");Check(CE32_console.NpDownloadNative.Modes.Count==0 && CE32_console.NpDownloadNative.BeginCalls==0,"Busy service received OUT/erase");}
        }
        CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.WrongUid=true;
        using(var manager=Open()){Reject(()=>manager.Update(package,null),"status UID mismatch");Check(CE32_console.NpDownloadNative.Modes.Count==0 && CE32_console.NpDownloadNative.BeginCalls==0,"Wrong UID received update");}
        CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.LoaderAbi=257;
        using(var manager=Open()){Reject(()=>manager.Update(package,null),"incompatible APP ABI");Check(CE32_console.NpDownloadNative.BeginCalls==0,"ABI failure erased APP");}
        CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.HoldEntry=true;
        using(var manager=Open()){Reject(()=>manager.Update(package,null),"unacknowledged update role");Check(CE32_console.NpDownloadNative.BeginCalls==0 && CE32_console.NpDownloadNative.Operations.Count==0,"Unverified update role sent bulk command");}
        foreach(bool badReply in new[]{false,true}){
            CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.ShortWrite=!badReply;CE32_console.NpDownloadNative.BadReply=badReply;
            using(var manager=Open()){Reject(()=>manager.Update(package,null),"short/corrupt transfer");Check(CE32_console.NpDownloadNative.BeginCalls==0 && CE32_console.NpDownloadNative.Modes.Count==1,"Transport failure retried or restored prematurely");}
        }
        CE32_console.NpDownloadNative.Reset(3);CE32_console.NpDownloadNative.FailOpcode=3;
        using(var manager=Open()){
            Reject(()=>manager.Update(package,null),"DATA rejection");
            Check(CE32_console.NpDownloadNative.Transaction && CE32_console.NpDownloadNative.BeginCalls==1 && CE32_console.NpDownloadNative.Modes.Count==1,"Failed write retried/committed/left ownership");
            Check(!CE32_console.NpDownloadNative.Operations.Contains(5),"Automatic ABORT sent");
            Check(manager.AbortAppUpdate(3)==3 && !CE32_console.NpDownloadNative.Transaction && !CE32_console.NpDownloadNative.AppValid,"Explicit ABORT/return failed");
            Check(CE32_console.NpDownloadNative.Modes[1]==3,"Abort did not restore original download role");
        }
        CE32_console.NpDownloadNative.Reset();using(var manager=Open()){Reject(()=>manager.AbortAppUpdate(0),"abort outside update role");Check(CE32_console.NpDownloadNative.Operations.Count==0,"Abort outside role sent bulk request");}
        CE32_console.NpDownloadNative.Reset(4);using(var manager=Open()){Check(manager.AbortAppUpdate(0)==4,"Already-selected update mode not retained");Check(CE32_console.NpDownloadNative.Modes.Count==0,"Unknown original role was fabricated");}
        foreach(bool duplicate in new[]{false,true}){
            CE32_console.NpDownloadNative.Reset();if(duplicate)CE32_console.NpDownloadNative.Paths.Add(CE32_console.NpDownloadNative.Path(true,CE32_console.NpDownloadNative.Uid));else CE32_console.NpDownloadNative.Paths.Clear();
            Reject(()=>Open(),"ambiguous/missing selected UID");Check(CE32_console.NpDownloadNative.Opened.Count==0,"Wrong/ambiguous target handle opened");
        }
        CE32_console.NpDownloadNative.Reset();CE32_console.NpDownloadNative.Paths.Add(CE32_console.NpDownloadNative.Path(false,"001E002A5942500B20313355"));
        using(var manager=Open()){Check(manager.ReadStatus().Role==2,"Exact target selection failed with neighboring logger");}
    }
    static int Main(string[] args)
    {
        try{
            Directory.CreateDirectory(args[0]);byte[] image=Image();string package=System.IO.Path.Combine(args[0],"test-app.xenp");File.WriteAllBytes(package,image);
            foreach(uint role in new[]{2U,3U,4U})Success(package,image,role,false);Success(package,image,4,true);Negative(package);
            byte[] corrupt=(byte[])image.Clone();corrupt[0]^=1;string invalid=System.IO.Path.Combine(args[0],"invalid.xenp");File.WriteAllBytes(invalid,corrupt);
            CE32_console.NpDownloadNative.Reset();using(var manager=Open()){Reject(()=>manager.Update(invalid,null),"invalid package");Check(CE32_console.NpDownloadNative.StatusReads==0 && CE32_console.NpDownloadNative.Modes.Count==0,"Invalid package accessed update state");}
            Console.WriteLine("PASS maintained in-place APP updater source: "+checks+" mocked checks; same handle/path/UID, capability/idle/ACK/ABI/CRC admission, exact payload, COMMIT return, explicit ABORT and legacy recovery. No hardware/native USB calls.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
