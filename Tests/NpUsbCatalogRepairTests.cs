using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using USB512_interface;
using CE32_console;

static class NpUsbCatalogRepairTests
{
    internal const string Uid="001A0020434250062039314E";
    internal const string Path=@"\\?\usb#vid_1209&pid_b386#001a0020434250062039314e#{test}";
    internal sealed class Fixture
    {
        internal byte[] Service;
        internal string SelectedPath;
        internal Fixture(int generation=2,bool bareV1=false){Service=ServiceBytes(generation);SelectedPath=generation==1&&!bareV1?Path.Replace("#001a","#npv1-001a"):Path;}
        internal readonly Queue<byte[]> Reports=new Queue<byte[]>();
        internal readonly List<NpDownloadNative.WINUSB_SETUP_PACKET> Commands=new List<NpDownloadNative.WINUSB_SETUP_PACKET>();
        internal bool Available=true,Cancelled,LostInspectAck,LostRepairAck,LostReceipt,CancelAfterRepair;
        internal int ReportReads,Opens;
        internal uint Share;
        byte[] inspectionReceipt;
        internal static byte[] ServiceBytes(int generation=2){var b=new byte[32];Put(b,0,generation==1?0x3153504eU:0x3253504eU);Put(b,4,generation==1?258U:4U);Put(b,8,3);Put(b,12,generation==1?0x3fU:0x10U);Put(b,24,0x001a0020);Put(b,28,0x43425006^0x2039314e);return b;}
        internal NpUsbCatalogRepair Open(){NpDownloadNative.Current=this;return NpUsbCatalogRepair.Open(SelectedPath,()=>Available,()=>Cancelled);}
        internal void Inspection(uint nonce=1,uint flags=0x800000){Reports.Enqueue(Report(0,0,nonce-1,0));Reports.Enqueue(Report(0xa1,1,nonce,flags));inspectionReceipt=Report(0xa1,3,nonce,flags);Reports.Enqueue(inspectionReceipt);}
        internal void SuccessfulRepair(uint nonce=1,uint flags=0x800000){Reports.Enqueue((byte[])inspectionReceipt.Clone());Reports.Enqueue(Report(0xa2,2,nonce+1,flags));Reports.Enqueue(Report(0xa2,3,nonce+1,flags));}
    }
    static int checks;
    internal static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    internal static byte[] Report(uint op,uint phase,uint nonce,uint flags=0x800000,uint epoch=9,uint cid=0x12345678,uint inspected=uint.MaxValue,uint error=0)
    {
        var b=new byte[64];Put(b,0,0x3152434e);Put(b,4,1);Put(b,8,op);Put(b,12,phase);Put(b,16,nonce);Put(b,20,error);Put(b,24,500006912);Put(b,28,flags);Put(b,32,8192);
        for(int i=0;i<4;i++)Put(b,40+i*4,cid+(uint)i);
        Put(b,36,phase==3?NpDownloadStream.Crc(b,40,16):0);
        Put(b,56,inspected==uint.MaxValue?(op==0xa2?nonce-1:nonce):inspected);Put(b,60,epoch);return b;
    }
    static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static string Reject(Action call,string why){try{call();}catch(Exception ex) when(ex is IOException || ex is InvalidDataException){checks++;return ex.Message;}throw new Exception("Accepted "+why);}
    static void NeedSingleRepair(Fixture f){Need(f.Commands.FindAll(c=>c.Request==0xa2).Count==1,"Repair was missing or replayed");}
    static async Task Run()
    {
        Need(NpUsbCatalogRepair.IsEligiblePath(Path),"V2 source missing");
        Need(NpUsbCatalogRepair.IsEligiblePath(Path.Replace("#001a","#npv1-001a")),"V1 source missing");
        foreach(string bad in new[]{null,@"\\.\PhysicalDrive99",Path.Replace("b386","b387"),Path.Replace("001a0020434250062039314e",new string('0',24)),Path.Replace("#001a0020434250062039314e","#npv1-"+new string('0',24))})
            Need(!NpUsbCatalogRepair.IsEligiblePath(bad),"Non-logger/invalid download source admitted");
        var f=new Fixture();f.Inspection(0x10000,0x10000);f.SuccessfulRepair(0x10000,0x10000);
        using(var manager=f.Open()){
            bool confirmed=false,invalidated=false;var messages=new List<string>();
            var done=await manager.ConfirmAndRepairAsync(card=>{confirmed=true;Need(card.Description.Contains("(full)"),"Full state hidden at confirmation");return true;},messages.Add,()=>{Need(confirmed,"List invalidated before confirmation");invalidated=true;});
            Need(invalidated && done.Phase==3 && done.Flags==0x10000,"Matching FULL completion was misreported");
            Need(f.Commands.Count==2 && f.Commands[0].Request==0xa1 && f.Commands[1].Request==0xa2,"Unexpected mutation or repeated inspection");
            Need(f.Commands[0].Value==0 && f.Commands[0].Index==1 && f.Commands[1].Value==1 && f.Commands[1].Index==1,"32-bit nonce not split into wIndex:wValue");
            Need(f.Commands.TrueForAll(c=>c.RequestType==0x40 && c.Length==0),"Command was not a zero-data vendor OUT");
            Need(f.Opens==1 && f.Share==0,"Control handle was not exclusive");
        }
        foreach(bool bare in new[]{false,true}){
            f=new Fixture(1,bare);f.Inspection();f.SuccessfulRepair(flags:0x10000);
            using(var manager=f.Open()){
                var done=await manager.ConfirmAndRepairAsync(card=>{Need(manager.Generation==1 && card.Generation==1,"V1 identity not detected from service");return true;},s=>{},()=>{});
                Need(done.Generation==1 && done.Flags==0x10000 && !done.IsFull && !done.Description.Contains("(full)"),"V2 flag meanings leaked into V1");
                NeedSingleRepair(f);Need(f.Commands.Count==2,"V1 workflow differs or adds a mutation");
            }
            f=new Fixture(1,bare);Put(f.Service,12,0x1f);using(var manager=f.Open()){
                string error=Reject(()=>manager.Inspect(),"old V1 ROM-DFU bit mistaken for repair");
                Need(f.Commands.Count==0 && f.ReportReads==0,"Old V1 received A0/A1/A2");
                Need(error.Contains("V1") && error.Contains("USB-service") && error.Contains("APP-only"),"V1 update requirement not explicit");
            }
            f=new Fixture(1,bare);f.Inspection();using(var manager=f.Open()){
                var card=manager.Inspect();f.Service=Fixture.ServiceBytes(2);
                Reject(()=>manager.Repair(card),"service generation changed");Need(f.Commands.Count==1,"Generation change sent Repair");
            }
            f=new Fixture(1,bare);f.Inspection();f.Reports.Enqueue(Report(0xa1,3,1));f.LostRepairAck=true;
            using(var manager=f.Open()){var card=manager.Inspect();Reject(()=>manager.Repair(card),"lost V1 repair ACK");NeedSingleRepair(f);}
        }
        foreach(int actual in new[]{1,2}){
            f=new Fixture(actual);f.SelectedPath=Path.Replace("#001a",actual==1?"#npv2-001a":"#npv1-001a");
            using(var manager=f.Open())Reject(()=>manager.Inspect(),"path/service generation mismatch");
            Need(f.Commands.Count==0 && f.ReportReads==0,"Mismatched board received catalog traffic");
        }
        f=new Fixture();f.Inspection();using(var manager=f.Open()){
            bool invalidated=false;var done=await manager.ConfirmAndRepairAsync(c=>false,s=>{},()=>invalidated=true);
            Need(done==null && !invalidated && f.Commands.Count==1,"Cancelled confirmation sent Repair or discarded list");
        }
        foreach(int kind in new[]{0,1,2,3,4,5}){
            f=new Fixture();
            if(kind==1)Put(f.Service,12,0);if(kind==2)Put(f.Service,8,2);if(kind==3)Put(f.Service,16,1);if(kind==4)Put(f.Service,20,3);if(kind==5)Put(f.Service,24,42);
            using(var manager=f.Open()){
                if(kind==0)f.Available=false;
                string error=Reject(()=>manager.Inspect(),"host/service preflight "+kind);
                Need(f.Commands.Count==0,"Preflight failure submitted a command");
                if(kind==1)Need(error.Contains("USB-service") && error.Contains("APP-only"),"Old USB firmware update guidance missing");
            }
        }
        foreach(uint phase in new[]{1U,2U}){
            f=new Fixture();f.Reports.Enqueue(Report(0xa2,phase,7));using(var manager=f.Open())Reject(()=>manager.Inspect(),"existing job");Need(f.Commands.Count==0,"Inspect queued over an active job");
        }
        foreach(int kind in new[]{0,1,2,3,4,5}){
            f=new Fixture();f.Inspection();using(var manager=f.Open()){
                var card=manager.Inspect();var changed=Report(0xa1,3,1);
                if(kind==0)Put(changed,60,10);if(kind==1){Put(changed,40,66);Put(changed,36,NpDownloadStream.Crc(changed,40,16));}if(kind==2)Put(changed,24,500000000);if(kind==3)Put(changed,56,99);if(kind==4)Put(changed,28,0);if(kind==5)Put(changed,32,9000);
                f.Reports.Enqueue(changed);Reject(()=>manager.Repair(card),"confirmation changed "+kind);Need(f.Commands.Count==1,"Changed confirmation sent Repair");
            }
        }
        foreach(int kind in new[]{0,1,2,3,4,5,6}){
            f=new Fixture();f.Inspection();f.Reports.Enqueue(Report(0xa1,3,1));var bad=Report(0xa2,3,2);
            if(kind==0)Put(bad,16,3);if(kind==1)Put(bad,60,10);if(kind==2){Put(bad,40,66);Put(bad,36,NpDownloadStream.Crc(bad,40,16));}if(kind==3)Put(bad,56,99);if(kind==4)Put(bad,36,42);if(kind==5){Put(bad,12,4);Put(bad,20,1234);}if(kind==6)Put(bad,8,0xa1);
            f.Reports.Enqueue(bad);using(var manager=f.Open()){
                var card=manager.Inspect();string error=Reject(()=>manager.Repair(card),"unconfirmed receipt "+kind);NeedSingleRepair(f);
                if(kind==5)Need(error.Contains("1234"),"Unknown future firmware error was hidden");
            }
        }
        foreach(int kind in new[]{0,1,2}){
            f=new Fixture();f.Inspection();f.Reports.Enqueue(Report(0xa1,3,1));f.LostRepairAck=kind==0;f.LostReceipt=kind==1;f.CancelAfterRepair=kind==2;
            using(var manager=f.Open()){
                var card=manager.Inspect();
                string error=Reject(()=>manager.Repair(card),"lost/cancelled operation "+kind);
                Need(error.IndexOf("unknown",StringComparison.OrdinalIgnoreCase)>=0,"Unknown outcome was hidden");
                NeedSingleRepair(f);
            }
        }
        f=new Fixture();f.Inspection();f.LostInspectAck=true;using(var manager=f.Open())Reject(()=>manager.Inspect(),"lost inspection ACK");Need(f.Commands.Count==1,"Lost Inspect ACK replayed or repaired");
        f=new Fixture();f.Reports.Enqueue(Report(0xa1,3,uint.MaxValue,inspected:uint.MaxValue));using(var manager=f.Open())Reject(()=>manager.Inspect(),"nonce wrap");Need(f.Commands.Count==0,"Nonce wrap submitted command");
        foreach(int at in new[]{0,4,8,12,16,36}){var bad=Report(0xa1,3,1);Put(bad,at,at==12?99U:0U);Reject(()=>NpUsbCatalogRepair.Parse(bad),"bad receipt field "+at);}
        Reject(()=>NpUsbCatalogRepair.Parse(new byte[63]),"short receipt");
        Console.WriteLine("PASS "+checks+" actual V1/V2 USB catalog workflow/EP0 boundary checks; no hardware.");
    }
    static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}

namespace CE32_console
{
    internal static class NpDownloadNative
    {
        internal static NpUsbCatalogRepairTests.Fixture Current;
        internal struct WINUSB_SETUP_PACKET {internal byte RequestType,Request;internal ushort Value,Index,Length;}
        internal static SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template){Current.Opens++;Current.Share=share;if(path!=Current.SelectedPath)throw new Exception("Wrong selected path");return new SafeFileHandle(new IntPtr(123),false);}
        internal static bool WinUsbInitialize(SafeFileHandle file,out IntPtr usb){usb=new IntPtr(1);return true;}
        internal static bool WinUsbFree(IntPtr usb){return true;}
        internal static bool WinUsbControlTransfer(IntPtr usb,WINUSB_SETUP_PACKET p,byte[] b,uint size,out uint transferred,IntPtr overlap)
        {
            transferred=size;
            if(p.RequestType==0xc0){
                if(p.Value!=0 || p.Index!=0)throw new Exception("Unexpected IN arguments");
                byte[] reply;
                if(p.Request==0x6e && size==32 && p.Length==32)reply=Current.Service;
                else if(p.Request==0xa0 && size==64 && p.Length==64){Current.ReportReads++;if(Current.LostReceipt && Current.Commands.Exists(c=>c.Request==0xa2))throw new IOException("lost receipt");reply=Current.Reports.Dequeue();}
                else throw new Exception("Unexpected IN command");
                Buffer.BlockCopy(reply,0,b,0,reply.Length);return true;
            }
            if(p.RequestType!=0x40 || (p.Request!=0xa1 && p.Request!=0xa2) || p.Length!=0 || size!=0)throw new Exception("Unexpected mutation or data payload");
            Current.Commands.Add(p);
            if(p.Request==0xa2 && Current.CancelAfterRepair)Current.Cancelled=true;
            if(p.Request==0xa1 && Current.LostInspectAck || p.Request==0xa2 && Current.LostRepairAck)throw new IOException("lost ACK");
            return true;
        }
    }
    internal static class NpDownloadStream { ACTUAL_CRC }
}
namespace USB512_interface
{
    internal static class NpV1Management {
        ACTUAL_V1_STATUS
        ACTUAL_V1_UID
        ACTUAL_V1_PARSE
        ACTUAL_V1_ABI
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    }
    internal static class NpV2FirmwareForm { ACTUAL_V2_UID }
    ACTUAL_SERVICE_STATUS
}
