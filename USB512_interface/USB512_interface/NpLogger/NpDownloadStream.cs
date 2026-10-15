using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CE32_console
{
    // One synchronous bounded request at a time. No unmanaged OVERLAPPED/buffer
    // lifetime races and no automatic restart/resync after a transport failure.
    internal sealed partial class NpDownloadStream : Stream
    {
        private SafeFileHandle file;
        private IntPtr usb;
        private long position, length, cacheStart=-1;
        private byte[] cache;
        private byte[] wireBuffer;
        private int cachePayloadCount;
        private uint epoch, sequence;
        private uint maxBlocks;
        private bool faulted;
        private readonly bool qualityTestSession;
        internal string TransportDescription {get;private set;}
        internal bool IsSynthetic {get;private set;}
        internal Func<bool> Cancelled;
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inputBytes,out long output,uint outputBytes,out uint returned,IntPtr overlapped);
        private sealed class RawDisk : FileStream {
            private readonly long bytes;
            internal RawDisk(SafeFileHandle h,long size,FileAccess access):base(h,access,512){bytes=size;}
            public override long Length{get{return bytes;}}
        }
        internal static Stream OpenDisk(string path) {return OpenRawDisk(path,false);}
        internal static Stream OpenDiskForFormat(string path) {return OpenRawDisk(path,true);}
        private static Stream OpenRawDisk(string path,bool writable) {
            if(!path.StartsWith(@"\\.\",StringComparison.Ordinal))path=@"\\.\"+path.Trim();
            var h=NpDownloadNative.CreateFile(path,writable?0xc0000000U:0x80000000U,writable?0U:3U,IntPtr.Zero,3,0,IntPtr.Zero);
            if(h.IsInvalid){int error=Marshal.GetLastWin32Error();h.Dispose();throw new IOException("Cannot open SD reader "+(writable?"exclusively for format":"read-only")+" (Windows "+error+"). Run WILDX_console as administrator and close other card tools.");}
            long bytes;uint returned;
            if(!DeviceIoControl(h,0x0007405c,IntPtr.Zero,0,out bytes,8,out returned,IntPtr.Zero) || returned!=8 || bytes<=0 || bytes%512!=0){h.Dispose();throw new IOException("Cannot obtain a sector-aligned SD disk capacity.");}
            return new RawDisk(h,bytes,writable?FileAccess.ReadWrite:FileAccess.Read);
        }
        internal static uint U(byte[] b,int n){return BitConverter.ToUInt32(b,n);}
        private static readonly uint[] CrcTable=MakeCrcTable();
        private static uint[] MakeCrcTable(){var t=new uint[2048];for(uint i=0;i<256;i++){uint c=i;for(int j=0;j<8;j++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);t[i]=c;}for(uint i=0;i<256;i++){uint c=t[i];for(int slice=1;slice<8;slice++){c=(c>>8)^t[c&255];t[slice*256+i]=c;}}return t;}
        internal static uint Crc(byte[] b,int offset,int count) {
            if(b==null)throw new ArgumentNullException("b");
            if(offset<0||count<0||offset>b.Length-count)throw new ArgumentOutOfRangeException();
            uint c=uint.MaxValue;int end=offset+count;var t=CrcTable;
            // IEEE CRC32, slicing-by-eight; keep all transport checks while
            // avoiding a byte-at-a-time dependency chain on the USB consumer.
            while(end-offset>=8){
                c^=BitConverter.ToUInt32(b,offset);
                c=t[1792+(c&255)]^t[1536+((c>>8)&255)]^t[1280+((c>>16)&255)]^t[1024+(c>>24)]^
                  t[768+b[offset+4]]^t[512+b[offset+5]]^t[256+b[offset+6]]^t[b[offset+7]];
                offset+=8;
            }
            while(offset<end)c=(c>>8)^t[(c^b[offset++])&255];return ~c;
        }
        internal NpDownloadStream(string path,bool allowSynthetic=false):this(path,allowSynthetic,false) {}
        internal NpDownloadStream(string path,bool allowSynthetic,bool qualityTest) {
            qualityTestSession=qualityTest;
            if(path==null || path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)<0)throw new IOException("Not an NP WinUSB device.");
            file=NpDownloadNative.CreateFile(path,0xc0000000,qualityTest?0U:3U,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
            if(file.IsInvalid){file.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
            try {
                if(!NpDownloadNative.WinUsbInitialize(file,out usb,path))throw new Win32Exception(Marshal.GetLastWin32Error());
                if(qualityTest)USB512_interface.UsbHealth.Diagnostic(usb);
                NpDownloadNative.USB_INTERFACE_DESCRIPTOR descriptor;
                if(!NpDownloadNative.WinUsbQueryInterfaceSettings(usb,0,out descriptor))throw new Win32Exception(Marshal.GetLastWin32Error());
                ushort packetSize=0;
                for(byte i=0;i<descriptor.bNumEndpoints;i++) {
                    NpDownloadNative.WINUSB_PIPE_INFORMATION pipe;
                    if(!NpDownloadNative.WinUsbQueryPipe(usb,0,i,out pipe))throw new Win32Exception(Marshal.GetLastWin32Error());
                    if(pipe.PipeId==0x81 && pipe.PipeType==NpDownloadNative.USBD_PIPE_TYPE.Bulk)packetSize=pipe.MaximumPacketSize;
                }
                if(packetSize!=64 && packetSize!=512)throw new IOException("NP bulk IN endpoint has an unsupported packet size.");
                uint timeout=3000;
                if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,3,4,ref timeout))throw new Win32Exception(Marshal.GetLastWin32Error());
                byte autoSuspend=0;
                if(!qualityTest && !NpDownloadNative.WinUsbSetPowerPolicy(usb,0x81,1,ref autoSuspend))throw new Win32Exception(Marshal.GetLastWin32Error());
                var info=Control(0xc0,0x60,0,0,32);
                IsSynthetic=U(info,0)==0x3154504e && U(info,28)==3;
                if((U(info,0)!=0x3144504e && !(allowSynthetic && IsSynthetic)) || U(info,4)!=1 || U(info,12)<1 || U(info,12)>1020 || (U(info,28)!=1 && !(allowSynthetic && IsSynthetic)))
                    throw new IOException(qualityTest?"This firmware mode does not provide the USB transfer test. Select SD download mode or supported RAM diagnostic firmware first. No mode was changed.":"Install the NP read-only USB download firmware; acquisition firmware does not serve SD recordings.");
                if(qualityTest && (U(info,8)==0 || U(info,16)!=0))throw new IOException("USB test requires an available, idle download device. No card reset or stream cancellation was attempted.");
                if(U(info,8)==0 && !IsSynthetic)info=ResetSdDownloadOnce();
                length=(long)U(info,8)*512;
                if(length==0)throw new IOException("SD card remains unavailable after the bounded USB reset; the card was not modified.");
                maxBlocks=U(info,12);
                if(packetSize==64 && maxBlocks>120)throw new IOException("Full-speed device advertises an unsafe transfer size.");
                TransportDescription=(packetSize==512?"High-speed USB":"Full-speed USB")+"; "+(maxBlocks*512/1024)+" KiB requests";
                if(!qualityTest) {
                    Control(0x40,0x62,0,0,0); // discard previous client's pending request
                    for(int i=0;i<100;i++){info=Control(0xc0,0x60,0,0,32);if(U(info,16)==0)break;Thread.Sleep(10);}
                    if(U(info,16)!=0)throw new IOException("NP USB download service is busy.");
                    // Reset only at a new, idle download session; never replay a
                    // failed read or alter the continuous recording path.
                    if(!NpDownloadNative.WinUsbResetPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
                    if(!NpDownloadNative.WinUsbFlushPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                epoch=U(info,24);sequence=U(info,20);
            } catch {Dispose();throw;}
        }
        private byte[] Control(byte type,byte request,ushort value,ushort index,ushort size) {
            byte[] b=new byte[size];uint n;
            var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=type,Request=request,Value=value,Index=index,Length=size};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,b,size,out n,IntPtr.Zero) || n!=size)
                throw new IOException("NP SD download control request failed (Windows "+Marshal.GetLastWin32Error()+"). Acquisition firmware cannot download SD data.");
            return b;
        }
        private byte[] ResetSdDownloadOnce() {
            try {Control(0x40,0x68,0x5344,0,0);}
            catch(IOException e) {throw new IOException("SD capacity is zero and this firmware rejected the idle-only reset request. Install reset-capable download firmware; no card write was attempted.",e);}
            byte[] receipt=null;
            for(int i=0;i<300;i++) {
                Thread.Sleep(100);
                receipt=Control(0xc0,0x69,0,0,32);
                if(U(receipt,0)!=0x3152534e)throw new IOException("Invalid NP SD reset receipt.");
                uint state=U(receipt,4);
                if(state==3 || state==4) {
                    if(state==4)throw new IOException("NP SD reset failed: result="+U(receipt,12).ToString("X8")+
                        ", HAL="+U(receipt,20).ToString("X8")+", SDMMC="+U(receipt,24).ToString("X8")+
                        ", elapsed="+U(receipt,28)+" ms; card contents preserved.");
                    byte[] info=Control(0xc0,0x60,0,0,32);
                    if(U(info,8)==0)throw new IOException("NP SD reset reported success but capacity is still zero; card contents preserved.");
                    return info;
                }
                if(state!=1 && state!=2)throw new IOException("NP SD reset entered unexpected state "+state+".");
            }
            throw new IOException("NP SD reset did not finish within 30 seconds; card contents preserved.");
        }
        internal string Diagnostics() {
            var b=Control(0xc0,0x63,0,0,32);
            return "firmware requests="+U(b,0)+" SD_ms="+U(b,4)+" CRC_ms="+U(b,8)+" USB_ms="+U(b,12)+" sectors="+U(b,16)+" CRC_selftest="+U(b,20)+" errors="+U(b,24)+" completions="+U(b,28);
        }
        private static void ValidateReplyInPlace(byte[] b,int n,uint lba,uint blocks,uint expectedSequence,uint expectedEpoch) {
            if(b==null || n>b.Length)throw new InvalidDataException("Invalid reply extent.");
            if(n<32 || U(b,0)!=0x3142504e || U(b,4)!=expectedSequence || U(b,8)!=lba || U(b,24)!=expectedEpoch || U(b,28)!=Crc(b,0,28))
                throw new InvalidDataException("NP USB reply header/sequence/address/CRC mismatch; download stopped.");
            if(U(b,16)!=0)throw new IOException("NP firmware SD read failed: 0x"+U(b,16).ToString("X8"));
            if(U(b,12)!=blocks || n!=32+blocks*512 || U(b,20)!=Crc(b,32,n-32))throw new InvalidDataException("NP USB payload length/CRC mismatch; download stopped.");
        }
        internal static byte[] ValidateReply(byte[] b,int n,uint lba,uint blocks,uint expectedSequence,uint expectedEpoch) {
            ValidateReplyInPlace(b,n,lba,blocks,expectedSequence,expectedEpoch);
            byte[] result=new byte[n-32];Buffer.BlockCopy(b,32,result,0,result.Length);return result;
        }
        private void Fill() {
            if(faulted)throw new IOException("This download connection failed; close and reopen it before retrying.");
            if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
            uint lba=checked((uint)(position/512)),blocks=(uint)Math.Min(maxBlocks,length/512-lba);
            try {
                Control(0x40,0x61,(ushort)lba,(ushort)(lba>>16),0);
                int expected=checked(32+(int)blocks*512);
                if(wireBuffer==null)wireBuffer=new byte[32+maxBlocks*512];
                byte[] reply=wireBuffer;int got=0;
                while(got<expected) {
                    if(qualityTestSession && Cancelled!=null && Cancelled())throw new OperationCanceledException();
                    // Normal full replies read directly into one reusable array.
                    // A partial reply takes the bounded slow path, preserving
                    // the existing framing/error handling without unsafe pins.
                    byte[] part=got==0?reply:new byte[expected-got];uint n;
                    if(!NpDownloadNative.WinUsbReadPipe(usb,0x81,part,(uint)(expected-got),out n,IntPtr.Zero) || n==0 || n>expected-got)throw new IOException("NP USB read timed out/failed (Windows "+Marshal.GetLastWin32Error()+", bytes "+n+"); partial download retained.");
                    if(got!=0)Buffer.BlockCopy(part,0,reply,got,(int)n);got+=(int)n;
                    // Error response is deliberately a short header-only transfer.
                    if(got==32 && U(reply,16)!=0)break;
                }
                try {ValidateReplyInPlace(reply,got,lba,blocks,unchecked(sequence+1),epoch);USB512_interface.UsbHealth.Integrity(usb,0x81,false,"download header/sequence/payload CRC");}
                catch(InvalidDataException){USB512_interface.UsbHealth.Integrity(usb,0x81,true,"download header/sequence/payload CRC");throw;}
                sequence++;
                cache=reply;cachePayloadCount=got-32;
                cacheStart=(long)lba*512;
            } catch {faulted=true;cache=null;cachePayloadCount=0;throw;}
        }
        public override int Read(byte[] b,int offset,int count) {
            if(usb==IntPtr.Zero)throw new ObjectDisposedException(GetType().Name);
            if(faulted)throw new IOException("This download connection failed; close and reopen it before retrying.");
            if(b==null)throw new ArgumentNullException("b");if(offset<0 || count<0 || offset>b.Length-count)throw new ArgumentOutOfRangeException();
            int copied=0;while(count>0 && position<length) {
                if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                if(cache==null || position<cacheStart || position>=cacheStart+cachePayloadCount)Fill();
                int at=(int)(position-cacheStart),n=Math.Min(count,cachePayloadCount-at);
                Buffer.BlockCopy(cache,32+at,b,offset,n);position+=n;offset+=n;count-=n;copied+=n;
            }return copied;
        }
        public override bool CanRead{get{return usb!=IntPtr.Zero;}} public override bool CanSeek{get{return CanRead;}} public override bool CanWrite{get{return false;}}
        public override long Length{get{return length;}} public override long Position{get{return position;}set{if(value<0 || value>length)throw new ArgumentOutOfRangeException();position=value;}}
        public override long Seek(long offset,SeekOrigin origin){Position=checked(offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:origin==SeekOrigin.End?length:throw new ArgumentOutOfRangeException()));return position;}
        public override void Flush(){} public override void SetLength(long v){throw new NotSupportedException();} public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}base.Dispose(disposing);}
    }
}
