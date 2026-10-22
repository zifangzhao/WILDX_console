using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using CE32_console;

namespace USB512_interface
{
    // V2 explicitly advertises the shared NFS1/NSF1 guarded format protocol.
    // Older USB services must not receive 0x79/0x7A based on repair support.
    internal sealed class NpV2UsbFormat : IDisposable
    {
        internal const uint Capability=0x20;
        const string UpdateMessage="This V2 USB firmware does not support card formatting. Install the bundled full firmware update (APP + USB + boot); an APP-only update cannot add USB formatting. No format command was sent.";
        readonly string uid;
        readonly Func<byte[]> service,info;
        readonly Action<byte[]> send;
        readonly Func<bool> available;
        SafeFileHandle file;IntPtr usb;
        internal NpV2UsbFormat(string uid,Func<byte[]> service,Func<byte[]> info,Action<byte[]> send,Func<bool> available)
        {this.uid=uid;this.service=service;this.info=info;this.send=send;this.available=available;}
        internal NpV2UsbFormat(string path,Func<bool> available)
        {
            if(!NpUsbCatalogRepair.IsEligiblePath(path) || NpV1Management.UidFromPath(path)!=null)throw new IOException("Select the exact V2 USB logger before formatting.");
            uid=NpV2FirmwareForm.Uid(path);this.available=available;
            service=()=>Read(0x6e,32);info=()=>Read(0x79,64);send=Send;
            RequireHost();file=NpDownloadNative.CreateFile(path,0xc0000000,0,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
            try{if(file.IsInvalid || !NpDownloadNative.WinUsbInitialize(file,out usb))throw new Win32Exception(Marshal.GetLastWin32Error());}catch{Dispose();throw;}
        }
        void RequireHost(){if(available==null || !available())throw new IOException("USB Format access is unavailable. Disconnect BLE and finish capture/download. No new format command was sent.");}
        bool RequireIdle()
        {
            RequireHost();byte[] bytes;
            try{bytes=service();}catch(Win32Exception ex) when(ex.NativeErrorCode==1 || ex.NativeErrorCode==31){throw new NotSupportedException(UpdateMessage,ex);}
            var state=NpV2ServiceStatus.Parse(bytes);state.RequireUid(uid);
            if((state.Capabilities&Capability)==0)throw new NotSupportedException(UpdateMessage);
            if(state.Mode!=3 || state.Busy!=0 || state.Target!=0)throw new IOException("USB Format requires this V2 idle in download mode with no pending operation. Nothing sent.");
            return true;
        }
        static void RequireCard(NpV1Management.SdFormatInfo card)
        {
            if(card==null || !card.Ready || card.Cid==new string('0',32) || card.Cid==new string('F',32))throw new IOException("This V2 does not report a ready SD card and guarded USB Format capability.");
        }
        internal NpV1Management.SdFormatInfo Inspect()
        {
            RequireIdle();var card=NpV1Management.ParseSdFormatInfo(info(),uid);RequireCard(card);return card;
        }
        internal void Format(NpV1Management.SdFormatInfo confirmed)
        {
            RequireCard(confirmed);bool first=true;
            NpV1Management.RunSdFormat(confirmed,()=>{
                RequireHost();byte[] bytes=info();
                if(first){first=false;var fresh=NpV1Management.ParseSdFormatInfo(bytes,uid);RequireCard(fresh);
                    if(fresh.Transaction!=confirmed.Transaction || fresh.State!=confirmed.State || fresh.Error!=confirmed.Error)throw new IOException("The confirmed V2 format receipt changed. Inspect and confirm again; nothing sent.");}
                return bytes;
            },RequireIdle,b=>{RequireHost();send(b);});
        }
        byte[] Read(byte request,int length)
        {
            var bytes=new byte[length];uint n;var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0xc0,Request=request,Length=(ushort)length};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,bytes,(uint)length,out n,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(n!=length)throw new IOException("Short USB format reply; outcome is not confirmed. No command replayed.");return bytes;
        }
        void Send(byte[] bytes)
        {
            RequireHost();uint n;var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0x40,Request=0x7a,Length=64};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,bytes,64,out n,IntPtr.Zero))throw new IOException("USB format acknowledgement lost; outcome unknown. No retry sent.",new Win32Exception(Marshal.GetLastWin32Error()));
            if(n!=64)throw new IOException("Short USB format acknowledgement; outcome unknown. No retry sent.");
        }
        public void Dispose(){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}}
    }

    // Shared stopped V1/V2 metadata repair; each service must advertise its own capability.
    internal sealed class NpUsbCatalogRepair : IDisposable
    {
        internal const byte ReportRequest=0xa0, InspectRequest=0xa1, RepairRequest=0xa2;
        internal const uint V1Capability=0x20,V2Capability=0x10,Full=0x10000;
        internal sealed class Receipt
        {
            internal uint Operation,Phase,Nonce,Error,Capacity,Flags,NextLba,CidCrc,InspectedNonce,Epoch;
            internal string Cid;
            internal int Generation;
            internal bool Active {get{return Phase==1 || Phase==2;}}
            internal bool IsFull {get{return Generation==2 && (Flags&Full)!=0;}}
            internal string Description {get{return "SD CID "+Cid+"\r\n"+(Capacity*512.0/1073741824).ToString("N2")+" GiB ("+Capacity+" sectors)\r\nCatalog flags 0x"+Flags.ToString("X8")+(IsFull?" (full)":"")+"; next LBA "+NextLba;}}
        }
        readonly string uid;
        readonly int pathGeneration;
        int generation;
        readonly Func<byte[]> readService,readReport;
        readonly Action<byte,uint> send;
        readonly Func<bool> hostAvailable,cancelled;
        SafeFileHandle file;
        IntPtr usb;
        internal string Uid {get{return uid;}}
        internal int Generation {get{return generation;}}
        string UpdateRequired {get{int known=generation==0?pathGeneration:generation;return "This "+(known==0?"logger":"V"+known)+" USB firmware does not support SD catalog repair. Install the matching USB-service firmware update; an APP-only update is insufficient. No repair command was sent.";}}

        // The same transaction path is exercised with a fake EP0 boundary in regression checks.
        internal NpUsbCatalogRepair(string uid,Func<byte[]> service,Func<byte[]> report,Action<byte,uint> command,Func<bool> available,Func<bool> cancel=null)
        {this.uid=uid;readService=service;readReport=report;send=command;hostAvailable=available;cancelled=cancel;}
        NpUsbCatalogRepair(string path,Func<bool> available,Func<bool> cancel)
        {
            if(!IsEligiblePath(path))throw new IOException("Select the exact V1/V2 USB logger before repairing its SD catalog.");
            uid=NpV1Management.UidFromPath(path)??NpV2FirmwareForm.Uid(path);hostAvailable=available;cancelled=cancel;
            pathGeneration=NpV1Management.UidFromPath(path)!=null?1:path.IndexOf("#npv2-",StringComparison.OrdinalIgnoreCase)>=0?2:0;
            readService=()=>ReadControl(0x6e,32);readReport=()=>ReadControl(ReportRequest,64);send=SendControl;
            RequireHost();
            // Exclusive ownership; no pipe reset, stream cancellation, card reset or firmware command.
            file=NpDownloadNative.CreateFile(path,0xc0000000,0,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
            try {
                if(file.IsInvalid || !NpDownloadNative.WinUsbInitialize(file,out usb))throw new Win32Exception(Marshal.GetLastWin32Error());
            }catch{Dispose();throw;}
        }
        internal static NpUsbCatalogRepair Open(string path,Func<bool> available,Func<bool> cancel=null)
        {return new NpUsbCatalogRepair(path,available,cancel);}
        internal static bool IsEligiblePath(string path)
        {
            if(path==null || path.IndexOf("usb#vid_1209&pid_b386#",StringComparison.OrdinalIgnoreCase)<0)return false;
            string id=NpV1Management.UidFromPath(path)??NpV2FirmwareForm.Uid(path);
            return id!=null && id!=new string('0',24) && !id.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase);
        }
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal static Receipt Parse(byte[] b)
        {
            if(b==null || b.Length!=64 || U(b,0)!=0x3152434e || U(b,4)!=1 || U(b,12)>4 ||
               (U(b,8)!=0 && U(b,8)!=InspectRequest && U(b,8)!=RepairRequest) ||
               (U(b,8)==0 && U(b,12)!=0) || (U(b,8)!=0 && U(b,16)==0))
                throw new InvalidDataException("Invalid USB catalog receipt; no command was replayed.");
            if(U(b,12)==3 && (U(b,20)!=0 || U(b,24)==0 ||
               (U(b,40)==0 && U(b,44)==0 && U(b,48)==0 && U(b,52)==0) ||
               (U(b,40)==uint.MaxValue && U(b,44)==uint.MaxValue && U(b,48)==uint.MaxValue && U(b,52)==uint.MaxValue) ||
               U(b,36)!=NpDownloadStream.Crc(b,40,16)))
                throw new InvalidDataException("USB catalog completion/card CRC is invalid; outcome is not confirmed.");
            return new Receipt{Operation=U(b,8),Phase=U(b,12),Nonce=U(b,16),Error=U(b,20),Capacity=U(b,24),Flags=U(b,28),NextLba=U(b,32),CidCrc=U(b,36),
                Cid=U(b,40).ToString("X8")+U(b,44).ToString("X8")+U(b,48).ToString("X8")+U(b,52).ToString("X8"),InspectedNonce=U(b,56),Epoch=U(b,60)};
        }
        static uint NextNonce(uint previous)
        {
            if(previous==uint.MaxValue)throw new IOException("USB catalog transaction numbers are exhausted; nonce wrap is not supported. Reset the device, then inspect before another explicit action. Nothing sent.");
            return previous+1;
        }
        void RequireHost()
        {
            if(hostAvailable==null || !hostAvailable())throw new IOException("Disconnect BLE and finish capture/download before USB catalog repair. No new command was sent.");
            if(cancelled!=null && cancelled())throw new IOException("Stopped waiting for USB catalog maintenance. A submitted firmware job is not aborted; its outcome may be unknown. Inspect before another explicit action.");
        }
        void RequireIdleService()
        {
            RequireHost();byte[] bytes;
            try{bytes=readService();}
            catch(Win32Exception ex) when(ex.NativeErrorCode==1 || ex.NativeErrorCode==31){throw new IOException(UpdateRequired,ex);}
            if(bytes==null || bytes.Length!=32)throw new IOException("USB service identity is truncated. No catalog command was sent.");
            uint mode,busy,target,capabilities;int actualGeneration;
            if(U(bytes,0)==0x3153504e){
                var service=NpV1Management.ParseSplitStatus(bytes,uid);
                actualGeneration=1;mode=service.Role;busy=service.Busy;target=service.PendingMode;capabilities=service.Capabilities;
            }else{
                var service=NpV2ServiceStatus.Parse(bytes);service.RequireUid(uid);
                actualGeneration=2;mode=service.Mode;busy=service.Busy;target=service.Target;capabilities=service.Capabilities;
            }
            if(pathGeneration!=0 && actualGeneration!=pathGeneration || generation!=0 && actualGeneration!=generation)
                throw new IOException("USB logger generation changed or differs from the selected device. Nothing sent.");
            generation=actualGeneration;
            // V1 bit0x10 is ROM DFU, not repair. Never send A1/A2 on that evidence.
            if((capabilities&(generation==1?V1Capability:V2Capability))==0)throw new IOException(UpdateRequired);
            if(mode!=3 || busy!=0 || target!=0)throw new IOException("USB catalog repair requires idle download mode with no pending operation. Nothing sent.");
        }
        Receipt ReadReceipt(){var reply=Parse(readReport());reply.Generation=generation;return reply;}
        static bool SameCard(Receipt a,Receipt b)
        {return a.Capacity==b.Capacity && a.Cid==b.Cid;}
        static string ErrorDescription(uint error)
        {
            switch(error){case 1:return "bad request";case 2:return "busy or wrong mode";case 3:return "transaction nonce";case 4:return "card identity changed";case 5:return "SD I/O";case 6:return "unrecoverable catalog or unknown allocation boundary";case 7:return "cancelled";case 8:return "SD clock";default:return "firmware error";}
        }
        void SendOnce(byte operation,uint nonce)
        {
            RequireHost();
            try{send(operation,nonce);}
            catch(Exception ex) when(ex is IOException || ex is Win32Exception){
                throw new IOException("USB catalog "+(operation==RepairRequest?"repair":"inspection")+" acknowledgement was lost (nonce "+nonce+"). Outcome unknown; no command was replayed. Keep power connected and inspect before another explicit action.",ex);
            }
        }
        Receipt WaitFor(byte operation,uint nonce,Receipt inspected=null)
        {
            var watch=Stopwatch.StartNew();Receipt first=null;
            while(watch.ElapsedMilliseconds<60000){
                RequireHost();Receipt reply;
                try{reply=ReadReceipt();}
                catch(Exception ex) when(ex is IOException || ex is Win32Exception){throw new IOException("USB catalog receipt unavailable; outcome unknown. No command was replayed. Inspect before another explicit action.",ex);}
                if(reply.Operation!=operation || reply.Nonce!=nonce ||
                   (first!=null && (reply.Epoch!=first.Epoch || !SameCard(first,reply))) ||
                   (inspected!=null && (reply.Epoch!=inspected.Epoch || !SameCard(inspected,reply) || reply.InspectedNonce!=inspected.Nonce)))
                    throw new IOException("USB catalog transaction/card/epoch changed; outcome unknown. No command was replayed. Inspect before another explicit action.");
                first=reply;
                if(reply.Phase==4)throw new IOException("V"+generation+" USB catalog "+(operation==RepairRequest?"repair":"inspection")+" failed: "+ErrorDescription(reply.Error)+" (error "+reply.Error+", nonce "+nonce+"). Inspect before retrying.");
                if(reply.Phase==3){
                    if(operation==InspectRequest && reply.InspectedNonce!=nonce)throw new IOException("The logger did not bind the completed inspection to its transaction. Nothing repaired.");
                    return reply;
                }
                if(!reply.Active)throw new IOException("USB catalog transaction is not pending or complete; outcome unknown. No command was replayed.");
                Thread.Sleep(100);
            }
            throw new IOException("USB catalog completion timed out after 60 seconds; outcome unknown. The firmware job was not aborted or replayed. Inspect before another explicit action.");
        }
        internal Receipt Inspect()
        {
            RequireIdleService();var previous=ReadReceipt();
            if(previous.Active)throw new IOException("A USB catalog job is still active. Wait for it before inspecting; no command was sent.");
            uint nonce=NextNonce(previous.Nonce);SendOnce(InspectRequest,nonce);return WaitFor(InspectRequest,nonce);
        }
        internal Receipt Repair(Receipt inspected)
        {
            if(inspected==null || inspected.Operation!=InspectRequest || inspected.Phase!=3 || inspected.Error!=0 || inspected.InspectedNonce!=inspected.Nonce)
                throw new IOException("A successful fresh inspection and explicit confirmation are required. Nothing repaired.");
            RequireIdleService();var fresh=ReadReceipt();
            if(fresh.Operation!=InspectRequest || fresh.Phase!=3 || fresh.Nonce!=inspected.Nonce || fresh.InspectedNonce!=inspected.Nonce ||
               fresh.Epoch!=inspected.Epoch || !SameCard(inspected,fresh) || fresh.CidCrc!=inspected.CidCrc || fresh.Flags!=inspected.Flags || fresh.NextLba!=inspected.NextLba)
                throw new IOException("The confirmed card or inspection changed. Inspect and confirm again; no repair command was sent.");
            uint nonce=NextNonce(fresh.Nonce);SendOnce(RepairRequest,nonce);return WaitFor(RepairRequest,nonce,inspected);
        }
        internal async Task<Receipt> ConfirmAndRepairAsync(Func<Receipt,bool> confirm,Action<string> progress,Action invalidate)
        {
            progress("Inspecting SD catalog…");var inspected=await Task.Run(()=>Inspect());
            if(!confirm(inspected))return null;
            RequireHost();invalidate();progress("Repairing the confirmed V"+generation+" SD catalog; keep USB and power connected…");
            return await Task.Run(()=>Repair(inspected));
        }
        byte[] ReadControl(byte request,int length)
        {
            var b=new byte[length];uint received;
            var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0xc0,Request=request,Length=(ushort)length};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,b,(uint)length,out received,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(received!=length)throw new IOException("Short USB catalog control reply.");return b;
        }
        void SendControl(byte request,uint nonce)
        {
            uint sent;var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0x40,Request=request,Value=(ushort)nonce,Index=(ushort)(nonce>>16),Length=0};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,new byte[0],0,out sent,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(sent!=0)throw new IOException("Unexpected USB catalog OUT acknowledgement length.");
        }
        public void Dispose(){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}}
    }
}
