using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using CE32_console;
using Microsoft.Win32.SafeHandles;

namespace USB512_interface
{
    // Idle, explicitly advertised metadata operation. It never falls back to
    // format, repair or a raw sector write on an older USB service.
    internal sealed class NpUsbDeleteLast : IDisposable
    {
        internal const uint Capability=0x40000;
        internal const int WireBytes=128;
        internal sealed class Receipt
        {
            internal byte[] Bytes;
            internal uint Operation,Phase,Nonce,Error,Epoch,Flags,Capacity,Count,Frontier,Ordinal,Start,End,InspectedNonce;
            internal int Generation;
            internal string Uid,Cid;
            internal bool Active {get{return Phase==1 || Phase==2;}}
            internal bool CanDelete {get{return Operation==1 && Phase==3 && Error==0 && (Flags&3)==3 && Count!=0;}}
            internal string Description {get{return "WILD-X V"+Generation+" "+Uid+"\r\nSD CID "+Cid+"\r\nLast recording "+Ordinal+((Flags&8)!=0?" (failed)":(Flags&4)!=0?" (incomplete)":"")+" · LBA "+Start+"–"+End+"\r\nReserved space "+((End-Start)*512.0/1073741824).ToString("N2")+" GiB";}}
        }
        readonly string uid;
        readonly Func<byte[]> service,report;
        readonly Action<uint> inspect;
        readonly Action<byte[]> commit;
        readonly Func<bool> available;
        readonly int pathGeneration;
        int generation;
        SafeFileHandle file;IntPtr usb;
        internal NpUsbDeleteLast(string uid,Func<byte[]> service,Func<byte[]> report,Action<uint> inspect,Action<byte[]> commit,Func<bool> available)
        {this.uid=uid;this.service=service;this.report=report;this.inspect=inspect;this.commit=commit;this.available=available;}
        internal NpUsbDeleteLast(string path,Func<bool> available)
        {
            if(!NpUsbCatalogRepair.IsEligiblePath(path))throw new IOException("Select the exact V1/V2 USB logger before deleting. No delete command was sent.");
            string v1=NpV1Management.UidFromPath(path);pathGeneration=v1==null?2:1;
            uid=v1??NpV2FirmwareForm.Uid(path);this.available=available;
            service=()=>Read(0x6e,32);report=()=>Read(0xa7,WireBytes);inspect=n=>Send(null,n);commit=b=>Send(b,0);
            RequireHost();file=NpDownloadNative.CreateFile(path,0xc0000000,0,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
            try{if(file.IsInvalid || !NpDownloadNative.WinUsbInitialize(file,out usb))throw new Win32Exception(Marshal.GetLastWin32Error());}catch{Dispose();throw;}
        }
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        internal static Receipt Parse(byte[] b,string uid)
        {
            if(b==null || b.Length!=WireBytes || U(b,0)!=0x3155444e || U(b,4)!=1 || U(b,8)>2 || U(b,12)>4 || (U(b,28)&~15U)!=0 || U(b,124)!=NpDownloadStream.Crc(b,0,124))
                throw new InvalidDataException("Invalid USB Delete Last receipt; no command replayed.");
            for(int at=100;at<124;at++)if(b[at]!=0)throw new InvalidDataException("Unsupported USB Delete Last receipt fields.");
            string actual=U(b,80).ToString("X8")+U(b,84).ToString("X8")+U(b,88).ToString("X8");
            if(!string.Equals(actual,uid,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("USB Delete Last logger UID changed.");
            var r=new Receipt{Bytes=(byte[])b.Clone(),Operation=U(b,8),Phase=U(b,12),Nonce=U(b,16),Error=U(b,20),Epoch=U(b,24),Flags=U(b,28),Capacity=U(b,32),Count=U(b,36),Frontier=U(b,40),Ordinal=U(b,44),Start=U(b,48),End=U(b,52),InspectedNonce=U(b,96),Uid=actual,
                Cid=U(b,64).ToString("X8")+U(b,68).ToString("X8")+U(b,72).ToString("X8")+U(b,76).ToString("X8")};
            if((r.Operation==0)!=(r.Phase==0) || r.Operation!=0 && r.Nonce==0 || r.Phase==3 && r.Error!=0)
                throw new InvalidDataException("Inconsistent USB Delete Last transaction state.");
            if((r.Flags&1)!=0 && (r.Capacity<=8192 || r.Frontier<8192 || r.Frontier>r.Capacity || r.Cid==new string('0',32) || r.Cid==new string('F',32)))
                throw new InvalidDataException("Invalid SD identity or allocation boundary.");
            if((r.Flags&2)!=0 && ((r.Flags&1)==0 || r.Count==0 || r.Start<8192 || r.End<=r.Start || r.End>r.Frontier))
                throw new InvalidDataException("Invalid last-recording extent.");
            return r;
        }
        void RequireHost(){if(available==null || !available())throw new IOException("Disconnect BLE and finish recording/download before deleting. No new command sent.");}
        internal static bool Supported(byte[] bytes,string uid)
        {int family;return SupportedCore(bytes,uid,out family);}
        static bool SupportedCore(byte[] bytes,string uid,out int family)
        {
            if(bytes==null || bytes.Length!=32)throw new IOException("USB service identity is truncated.");
            if(U(bytes,0)==0x3153504e){var s=NpV1Management.ParseSplitStatus(bytes,uid);family=1;return (s.Capabilities&Capability)!=0 && s.Role==3 && s.Busy==0 && s.PendingMode==0;}
            var v2=NpV2ServiceStatus.Parse(bytes);v2.RequireUid(uid);family=2;return (v2.Capabilities&Capability)!=0 && v2.Mode==3 && v2.Busy==0 && v2.Target==0;
        }
        void RequireIdle()
        {
            RequireHost();
            int actual;
            if(!SupportedCore(service(),uid,out actual))throw new IOException("USB Delete Last requires an idle download service with this capability. Install matching USB firmware if unavailable. No delete command sent.");
            if(pathGeneration!=0 && actual!=pathGeneration || generation!=0 && actual!=generation)throw new IOException("USB logger family changed. No delete command sent.");
            generation=actual;
        }
        static uint Next(uint n){if(n==uint.MaxValue)throw new IOException("USB transaction numbers exhausted; reconnect before a new inspection.");return n+1;}
        internal static byte[] DeleteRequest(Receipt confirmed)
        {
            if(confirmed==null || !confirmed.CanDelete)throw new IOException("A fresh deletable last-recording inspection is required.");
            byte[] request=(byte[])confirmed.Bytes.Clone();Put(request,8,2);Put(request,16,Next(confirmed.Nonce));Put(request,96,confirmed.Nonce);Put(request,124,NpDownloadStream.Crc(request,0,124));return request;
        }
        internal static bool SameTarget(Receipt a,Receipt b)
        {
            if(a==null || b==null)return a==b;
            for(int at=24;at<96;at++)if(a.Bytes[at]!=b.Bytes[at])return false;
            return true;
        }
        Receipt ReadReceipt(){var r=Parse(report(),uid);r.Generation=generation;if(generation==2 && (r.Flags&~3U)!=0)throw new InvalidDataException("Unsupported V2 Delete Last flags.");return r;}
        static bool SameCard(Receipt a,Receipt b){return a.Uid==b.Uid && a.Cid==b.Cid && a.Capacity==b.Capacity && a.Epoch==b.Epoch;}
        Receipt Wait(uint operation,uint nonce,Receipt confirmed=null)
        {
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<30000) {
                RequireHost();var r=ReadReceipt();
                if(r.Operation!=operation || r.Nonce!=nonce || confirmed!=null && (!SameCard(r,confirmed) || r.InspectedNonce!=confirmed.Nonce))
                    throw new IOException("USB Delete Last transaction/card changed. Outcome unknown; no command replayed.");
                if(r.Phase==4)throw new IOException("USB Delete Last failed (error "+r.Error+"). Inspect before another action; no command replayed.");
                if(r.Phase==3) {
                    if(confirmed!=null && (r.Count!=confirmed.Count-1 || r.Frontier>confirmed.Start))throw new IOException("Deleted catalog boundary was not confirmed. No command replayed.");
                    RequireIdle();return r;
                }
                if(!r.Active)throw new IOException("USB Delete Last outcome is unknown. No command replayed.");
                Thread.Sleep(100);
            }
            throw new IOException("USB Delete Last completion timed out. Keep power connected; no command replayed.");
        }
        internal Receipt Inspect()
        {
            RequireIdle();var prior=ReadReceipt();if(prior.Active)throw new IOException("SD maintenance is already running; wait before inspecting.");
            uint nonce=Next(prior.Nonce);inspect(nonce);return Wait(1,nonce);
        }
        internal Receipt Delete(Receipt confirmed)
        {
            if(confirmed==null || !confirmed.CanDelete)throw new IOException("The last recording is active or cannot be safely deleted. Nothing deleted.");
            RequireIdle();var fresh=ReadReceipt();
            if(Convert.ToBase64String(fresh.Bytes)!=Convert.ToBase64String(confirmed.Bytes))throw new IOException("The confirmed SD snapshot changed. Inspect and confirm again; nothing deleted.");
            byte[] request=DeleteRequest(confirmed);uint nonce=U(request,16);
            RequireIdle();
            try{commit(request);}catch(Exception ex) when(ex is IOException || ex is Win32Exception){throw new IOException("Delete acknowledgement lost; outcome unknown. No delete command replayed. Keep power connected and inspect before another action.",ex);}
            return Wait(2,nonce,confirmed);
        }
        byte[] Read(byte request,int length)
        {
            byte[] b=new byte[length];uint n;var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0xc0,Request=request,Length=(ushort)length};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,b,(uint)length,out n,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(n!=length)throw new IOException("Short USB maintenance receipt.");return b;
        }
        void Send(byte[] bytes,uint nonce)
        {
            RequireHost();uint n;bytes=bytes??new byte[0];var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=0x40,Request=0xa7,Value=(ushort)nonce,Index=(ushort)(nonce>>16),Length=(ushort)bytes.Length};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,bytes,(uint)bytes.Length,out n,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(n!=bytes.Length)throw new IOException("Short USB maintenance acknowledgement.");
        }
        public void Dispose(){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}}
    }
}
