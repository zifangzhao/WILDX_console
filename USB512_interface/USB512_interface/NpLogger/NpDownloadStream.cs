using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CE32_console
{
    internal sealed class NpDownloadProtocolUnavailableException : IOException {
        internal NpDownloadProtocolUnavailableException(Exception inner):base("The SD-download test protocol could not be confirmed. No bulk data was tested; this is not a cable-quality result.",inner) {}
    }
    // One synchronous bounded request at a time. No unmanaged OVERLAPPED/buffer
    // lifetime races. Only negotiated, exclusively owned SD reads may be retried.
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
        private bool validatedRead;
        private readonly bool qualityTestSession;
        private bool qualityRequestPending;
        private bool canRecoverReads;
        private byte[] recoveryIdentity;
        private uint recoveryErrorBaseline;
        internal int RecoveredReads {get;private set;}
        internal int ReadRecoveryAttempts {get;private set;}
        internal string ReadRecoveryDiagnostics {get;private set;}
        internal string TransportDescription {get;private set;}
        internal bool IsSynthetic {get;private set;}
        internal Func<bool> Cancelled;
        internal sealed class ReadTiming {
            internal long ReadTicks,ControlTicks,BulkTicks,ValidationTicks,ReadySleepTicks;
            internal long SourceBytes,BulkBytes,ValidatedBytes,Replies,ControlCalls,BulkCalls;
            internal ReadTiming Copy(){return (ReadTiming)MemberwiseClone();}
        }
        private readonly ReadTiming timing=new ReadTiming();
        internal ReadTiming TimingSnapshot(){return timing.Copy();}
        internal Dictionary<string,object> PerformanceSince(ReadTiming before,double wallSeconds) {
            double scale=1.0/Stopwatch.Frequency,read=(timing.ReadTicks-before.ReadTicks)*scale;
            double control=(timing.ControlTicks-before.ControlTicks)*scale,bulk=(timing.BulkTicks-before.BulkTicks)*scale;
            double crc=(timing.ValidationTicks-before.ValidationTicks)*scale,sleep=(timing.ReadySleepTicks-before.ReadySleepTicks)*scale;
            long source=timing.SourceBytes-before.SourceBytes,wire=timing.BulkBytes-before.BulkBytes;
            return new Dictionary<string,object>{
                {"wall_seconds",wallSeconds},{"stream_read_seconds",read},{"ep0_control_seconds",control},
                {"bulk_call_wait_seconds",bulk},{"reply_validation_seconds",crc},{"readiness_sleep_seconds",sleep},
                {"read_copy_and_other_seconds",Math.Max(0,read-control-bulk-crc-sleep)},
                {"outside_read_processing_output_seconds",Math.Max(0,wallSeconds-read)},
                {"source_bytes_copied",source},{"bulk_success_bytes_including_headers",wire},
                {"validated_payload_bytes",timing.ValidatedBytes-before.ValidatedBytes},
                {"validated_replies",timing.Replies-before.Replies},{"bulk_calls",timing.BulkCalls-before.BulkCalls},
                {"ep0_calls",timing.ControlCalls-before.ControlCalls},{"max_request_payload_bytes",maxBlocks*512},
                {"copied_source_MB_per_second",wallSeconds>0?(object)(source/wallSeconds/1e6):null},
                {"active_bulk_call_MB_per_second",bulk>0?(object)(wire/bulk/1e6):null},
                {"recovered_reads",RecoveredReads},{"read_retry_attempts",ReadRecoveryAttempts},
                {"scope","Per-stream instance delta; read time includes control/bulk/CRC/cache copies. Outside-read time includes structural checks, decode, output writes and flush. Bulk waits can include device SD work; this is not isolated bus bandwidth. Firmware timing counters may overlap."}
            };
        }
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
            file=NpDownloadNative.CreateFile(path,0xc0000000,0,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
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
                if(qualityTest && U(info,16)!=0)throw new IOException("USB download service is busy (state "+U(info,16)+"). Finish the active download before testing. No existing request was cancelled.");
                if(qualityTest && U(info,8)==0)throw new IOException("The SD-download firmware reports no usable card. The SD-backed test needs a readable card. For a card-independent V1 test, select USB streaming mode and run the test again. No card reset was sent.");
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
                    // Initial idle-session reset. A later negotiated recovery
                    // separately requires the owned firmware-retirement barrier.
                    if(!NpDownloadNative.WinUsbResetPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
                    if(!NpDownloadNative.WinUsbFlushPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                epoch=U(info,24);sequence=U(info,20);
                TryEnableReadRecovery(info);
            } catch {Dispose();throw;}
        }
        private byte[] Control(byte type,byte request,ushort value,ushort index,ushort size) {
            byte[] b=new byte[size];uint n;
            var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=type,Request=request,Value=value,Index=index,Length=size};
            long started=Stopwatch.GetTimestamp();
            bool ok=NpDownloadNative.WinUsbControlTransfer(usb,setup,b,size,out n,IntPtr.Zero);
            int error=Marshal.GetLastWin32Error(); // Preserve native error before timing work.
            timing.ControlTicks+=Stopwatch.GetTimestamp()-started;timing.ControlCalls++;
            if(!ok) {
                var native=new Win32Exception(error);
                if(qualityTestSession && type==0xc0 && request==0x60 && (error==1 || error==31))throw new NpDownloadProtocolUnavailableException(native);
                throw new IOException("NP SD download control request 0x"+request.ToString("X2")+" failed (Windows "+error+").",native);
            }
            if(n!=size)throw new InvalidDataException("Short NP SD download control reply for request 0x"+request.ToString("X2")+" ("+n+"/"+size+" bytes).");
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
        private sealed class NativeBulkReadFailure : IOException {
            internal readonly int NativeError;
            internal NativeBulkReadFailure(string message,int error):base(message,new Win32Exception(error)) {NativeError=error;}
        }
        private void CheckRecoveryCancellation() {
            if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
        }
        private uint ValidateRecoveryInfo(byte[] info,uint expectedEpoch,uint expectedSequence) {
            if(U(info,0)!=0x3144504e || U(info,4)!=1 || U(info,8)!=(uint)(length/512) ||
               U(info,12)!=maxBlocks || U(info,20)!=expectedSequence || U(info,24)!=expectedEpoch || U(info,28)!=1)
                throw new InvalidDataException("NP USB guarded read identity/geometry/epoch/sequence changed; no read replayed.");
            return U(info,16);
        }
        private void ValidateRecoveryReceipt(byte[] receipt,uint expectedEpoch,uint expectedSequence) {
            if(U(receipt,0)!=0x3152444e || U(receipt,4)!=1 || U(receipt,8)!=1 || U(receipt,12)!=0 ||
               U(receipt,16)!=expectedEpoch || U(receipt,20)!=expectedSequence || U(receipt,24)!=0 || U(receipt,28)!=Crc(receipt,0,28))
                throw new InvalidDataException("NP USB NDR1 retirement receipt invalid or not clean idle; no read replayed.");
        }
        private byte[] ReadRecoveryIdentity() {
            byte[] identity=Control(0xc0,0x79,0,0,64);
            bool uid=false,cid=false;
            for(int i=24;i<36;i++)uid|=identity[i]!=0;
            for(int i=36;i<52;i++)cid|=identity[i]!=0;
            if(U(identity,0)!=0x3153464e || U(identity,4)!=1 || U(identity,8)>4 || U(identity,8)==1 || U(identity,8)==2 ||
               U(identity,12)!=0 || U(identity,20)!=(uint)(length/512) || U(identity,56)!=3 ||
               U(identity,60)!=Crc(identity,0,60) || !uid || !cid)
                throw new InvalidDataException("NP USB reported SD identity is unavailable/invalid; no read replayed.");
            if(recoveryIdentity!=null)for(int i=24;i<52;i++)if(identity[i]!=recoveryIdentity[i])
                throw new InvalidDataException("NP USB reported logger/card identity changed; no read replayed.");
            return identity;
        }
        private void TryEnableReadRecovery(byte[] info) {
            ReadRecoveryDiagnostics="guarded read recovery disabled";
            if(qualityTestSession || IsSynthetic)return;
            try {
                if(ValidateRecoveryInfo(info,epoch,sequence)!=0)return;
                // 0x6A remains the independent 192-byte firmware identity API.
                ValidateRecoveryReceipt(Control(0xc0,0x6b,0,0,32),epoch,sequence);
                recoveryIdentity=ReadRecoveryIdentity();
                recoveryErrorBaseline=U(Control(0xc0,0x63,0,0,32),24);canRecoverReads=true;
                ReadRecoveryDiagnostics="guarded read recovery available; recovered=0 retries=0";
            } catch(Exception error) when(error is IOException || error is InvalidDataException) {
                // Unsupported/invalid negotiation never changes legacy failure policy.
                ReadRecoveryDiagnostics="guarded read recovery disabled: "+error.Message;
            }
        }
        private void RecoverOwnedRead(uint lba,int retry,NativeBulkReadFailure failure) {
            RetireOwnedRead(lba,retry,failure);
        }
        private void RetireOwnedRead(uint lba,int retry,NativeBulkReadFailure failure) {
            CheckRecoveryCancellation();
            uint oldEpoch=epoch,ownedSequence=unchecked(sequence+1),nextEpoch=unchecked(epoch+1);
            byte[] info=Control(0xc0,0x60,0,0,32);
            CheckRecoveryCancellation();
            uint state=ValidateRecoveryInfo(info,oldEpoch,ownedSequence);
            if(state>3)throw new IOException("NP USB failed read is not an owned recoverable request (state "+state+"); no cancel/replay sent.");
            ReadRecoveryIdentity();CheckRecoveryCancellation();
            if(retry!=0)ReadRecoveryAttempts++;
            ReadRecoveryDiagnostics=(retry==0?"terminal owned-read retirement":"guarded read retry "+retry+"/2")+" LBA="+lba+"; "+failure.Message;
            Trace.WriteLine(ReadRecoveryDiagnostics);
            Control(0x40,0x62,0,0,0); // exactly once, only after proving this exclusive handle's read
            var wait=Stopwatch.StartNew();
            while(true) {
                CheckRecoveryCancellation();info=Control(0xc0,0x60,0,0,32);CheckRecoveryCancellation();
                state=ValidateRecoveryInfo(info,nextEpoch,ownedSequence);
                if(wait.ElapsedMilliseconds>=3000)throw new TimeoutException("NP USB owned-read retirement did not reach matching idle; no read replayed.");
                if(state==0) {
                    break;
                }
                if(state!=4)throw new IOException("NP USB owned-read retirement failed (state "+state+"); no read replayed.");
                long sleeping=Stopwatch.GetTimestamp();Thread.Sleep(1);timing.ReadySleepTicks+=Stopwatch.GetTimestamp()-sleeping;
            }
            CheckRecoveryCancellation();
            if(!NpDownloadNative.WinUsbResetPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(!NpDownloadNative.WinUsbFlushPipe(usb,0x81))throw new Win32Exception(Marshal.GetLastWin32Error());
            info=Control(0xc0,0x60,0,0,32);CheckRecoveryCancellation();
            if(ValidateRecoveryInfo(info,nextEpoch,ownedSequence)!=0)throw new IOException("NP USB retired read is no longer idle; no read replayed.");
            ValidateRecoveryReceipt(Control(0xc0,0x6b,0,0,32),nextEpoch,ownedSequence);
            ReadRecoveryIdentity();CheckRecoveryCancellation();
            uint errors=U(Control(0xc0,0x63,0,0,32),24);CheckRecoveryCancellation();
            if(errors!=recoveryErrorBaseline)
                throw new IOException("NP USB SD/backend error counter changed ("+recoveryErrorBaseline+" -> "+errors+"); no read replayed.");
            epoch=nextEpoch;sequence=ownedSequence;validatedRead=false;
            if(retry==0)ReadRecoveryDiagnostics="terminal owned read retired without replay; recovered="+RecoveredReads+" retries="+ReadRecoveryAttempts+"; "+failure.Message;
        }
        internal string Diagnostics() {
            var b=Control(0xc0,0x63,0,0,32);
            return "firmware requests="+U(b,0)+" SD_ms="+U(b,4)+" CRC_ms="+U(b,8)+" USB_ms="+U(b,12)+" sectors="+U(b,16)+" CRC_selftest="+U(b,20)+" errors="+U(b,24)+" completions="+U(b,28)+"; "+ReadRecoveryDiagnostics;
        }
        private static void ValidateReplyHeader(byte[] b,int n,uint lba,uint blocks,uint expectedSequence,uint expectedEpoch) {
            if(b==null || n>b.Length)throw new InvalidDataException("Invalid reply extent.");
            if(n<32 || U(b,0)!=0x3142504e || U(b,4)!=expectedSequence || U(b,8)!=lba || U(b,24)!=expectedEpoch || U(b,28)!=Crc(b,0,28))
                throw new InvalidDataException("NP USB reply header/sequence/address/CRC mismatch; download stopped.");
            if(U(b,16)!=0)throw new IOException("NP firmware SD read failed: 0x"+U(b,16).ToString("X8"));
            if(U(b,12)!=blocks)throw new InvalidDataException("NP USB payload length/CRC mismatch; download stopped.");
        }
        private static void ValidateReplyInPlace(byte[] b,int n,uint lba,uint blocks,uint expectedSequence,uint expectedEpoch) {
            ValidateReplyHeader(b,n,lba,blocks,expectedSequence,expectedEpoch);
            if(n!=32+blocks*512 || U(b,20)!=Crc(b,32,n-32))throw new InvalidDataException("NP USB payload length/CRC mismatch; download stopped.");
        }
        internal static byte[] ValidateReply(byte[] b,int n,uint lba,uint blocks,uint expectedSequence,uint expectedEpoch) {
            ValidateReplyInPlace(b,n,lba,blocks,expectedSequence,expectedEpoch);
            byte[] result=new byte[n-32];Buffer.BlockCopy(b,32,result,0,result.Length);return result;
        }
        private void WaitForDownloadReady() {
            var wait=Stopwatch.StartNew();
            while(true) {
                if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                byte[] info=Control(0xc0,0x60,0,0,32);
                if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                if(U(info,0)!=(IsSynthetic?0x3154504eU:0x3144504eU) || U(info,4)!=1 ||
                   U(info,8)!=(uint)(length/512) || U(info,12)!=maxBlocks || U(info,20)!=sequence ||
                   U(info,24)!=epoch || U(info,28)!=(IsSynthetic?3U:1U))
                    throw new InvalidDataException("NP USB download identity/geometry/epoch/sequence/capability changed; next read was not sent.");
                if(wait.ElapsedMilliseconds>=1000)throw new TimeoutException("NP USB download readiness timed out; next read was not sent.");
                uint state=U(info,16);
                if(state==0)return;
                if(state!=3)throw new IOException("Unexpected NP USB download readiness state "+state+"; next read was not sent.");
                long sleeping=Stopwatch.GetTimestamp();Thread.Sleep(1);timing.ReadySleepTicks+=Stopwatch.GetTimestamp()-sleeping;
            }
        }
        private void Fill() {
            if(faulted)throw new IOException("This download connection failed; close and reopen it before retrying.");
            if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
            uint lba=checked((uint)(position/512)),blocks=(uint)Math.Min(maxBlocks,length/512-lba);
            try {
              for(int retry=0;;retry++) {
               try {
                if(retry!=0)CheckRecoveryCancellation();
                // Host read completion may precede firmware's EP1 completion.
                // Only a matching idle receipt permits the next request; never
                // replay 0x61 after a failed control transfer.
                bool burst=PrepareBurstRead(lba);
                if(!burst){
                    if(validatedRead)WaitForDownloadReady();
                    if(qualityTestSession)qualityRequestPending=true;
                    Control(0x40,0x61,(ushort)lba,(ushort)(lba>>16),0);
                }
                int expected=checked(32+(int)blocks*512);
                if(wireBuffer==null)wireBuffer=new byte[32+maxBlocks*512];
                byte[] reply=wireBuffer;int got=0;
                if(burst)got=TakeBurstReply(lba,out reply);
                else{
                while(got<expected) {
                    if(qualityTestSession && Cancelled!=null && Cancelled())throw new OperationCanceledException();
                    // Normal full replies read directly into one reusable array.
                    // A partial reply takes the bounded slow path, preserving
                    // the existing framing/error handling without unsafe pins.
                    byte[] part=got==0?reply:new byte[expected-got];uint n;
                    long readStarted=Stopwatch.GetTimestamp();
                    bool readOk=NpDownloadNative.WinUsbReadPipe(usb,0x81,part,(uint)(expected-got),out n,IntPtr.Zero);
                    int nativeError=Marshal.GetLastWin32Error(); // Capture before timing/formatting/native work.
                    long readTicks=Stopwatch.GetTimestamp()-readStarted;
                    timing.BulkTicks+=readTicks;timing.BulkCalls++;
                    if(readOk && n<=expected-got)timing.BulkBytes+=n;
                    if(!readOk || n==0 || n>expected-got) {
                        string message="NP USB read timed out/failed (Windows "+nativeError+", bytes "+n+"); LBA="+lba+" (0x"+lba.ToString("X8")+"), blocks="+blocks+", expected="+expected+", accumulated="+got+", native_ok="+readOk+", read_ms="+(readTicks*1000.0/Stopwatch.Frequency).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"; partial download retained.";
                        if(!readOk)throw new NativeBulkReadFailure(message,nativeError);
                        throw new IOException(message);
                    }
                    if(got!=0)Buffer.BlockCopy(part,0,reply,got,(int)n);got+=(int)n;
                    // Reject an already observed bad header/SD result before a
                    // later native failure could qualify for transport recovery.
                    if(got>=32 && got<expected && got-(int)n<32) {
                        long validating=Stopwatch.GetTimestamp();
                        try {ValidateReplyHeader(reply,got,lba,blocks,unchecked(sequence+1),epoch);}
                        catch(InvalidDataException){USB512_interface.UsbHealth.Integrity(usb,0x81,true,"download header/sequence/payload CRC");throw;}
                        finally {timing.ValidationTicks+=Stopwatch.GetTimestamp()-validating;}
                    }
                    // Error response is deliberately a short header-only transfer.
                    if(got==32 && U(reply,16)!=0)break;
                }
                }
                qualityRequestPending=false;
                long validationStarted=Stopwatch.GetTimestamp();
                try {ValidateReplyInPlace(reply,got,lba,blocks,unchecked(sequence+1),epoch);USB512_interface.UsbHealth.Integrity(usb,0x81,false,"download header/sequence/payload CRC");}
                catch(InvalidDataException){USB512_interface.UsbHealth.Integrity(usb,0x81,true,"download header/sequence/payload CRC");throw;}
                finally {timing.ValidationTicks+=Stopwatch.GetTimestamp()-validationStarted;}
                timing.Replies++;timing.ValidatedBytes+=got-32;
                CountDownloadedPayload(got-32);
                sequence++;
                if(burst)CommitBurstReply(lba);
                validatedRead=true;
                cache=reply;cachePayloadCount=got-32;
                cacheStart=(long)lba*512;
                if(retry!=0) {
                    RecoveredReads++;
                    ReadRecoveryDiagnostics="guarded read recovered LBA="+lba+"; recovered="+RecoveredReads+" retries="+ReadRecoveryAttempts+" (last read "+retry+"/2)";
                    Trace.WriteLine(ReadRecoveryDiagnostics);
                }
                return;
               } catch(NativeBulkReadFailure failure) {
                    cache=null;cachePayloadCount=0;cacheStart=-1;wireBuffer=null;
                    ReadRecoveryDiagnostics="native bulk failure; recovered="+RecoveredReads+" retries="+ReadRecoveryAttempts+"; "+failure.Message;
                    if(burstOwned)throw; // Retire the bounded session; never replay its packets.
                    if(!canRecoverReads || failure.NativeError==995 || failure.NativeError==1223)throw;
                    if(retry>=2) {
                        // Exhausted reads still own a firmware reply. Retire
                        // only that proven read, never submit another READ or
                        // replace the original native failure with cleanup.
                        try {RetireOwnedRead(lba,0,failure);}
                        catch(Exception retirementFailure) {
                            failure.Data["TerminalReadRetirementFailure"]=retirementFailure;
                            ReadRecoveryDiagnostics+="; terminal retirement failed: "+retirementFailure.Message;
                        }
                        try {Trace.WriteLine(ReadRecoveryDiagnostics);}catch {}throw;
                    }
                    RecoverOwnedRead(lba,retry+1,failure);
               }
              }
            } catch(Exception error) {if(burstOwned)burstFailure=error;faulted=true;cache=null;cachePayloadCount=0;wireBuffer=null;throw;}
        }
        public override int Read(byte[] b,int offset,int count) {
            if(usb==IntPtr.Zero)throw new ObjectDisposedException(GetType().Name);
            if(faulted)throw new IOException("This download connection failed; close and reopen it before retrying.");
            if(b==null)throw new ArgumentNullException("b");if(offset<0 || count<0 || offset>b.Length-count)throw new ArgumentOutOfRangeException();
            long started=Stopwatch.GetTimestamp();
            try {int copied=0;while(count>0 && position<length) {
                if(Cancelled!=null && Cancelled())throw new OperationCanceledException();
                if(cache==null || position<cacheStart || position>=cacheStart+cachePayloadCount)Fill();
                int at=(int)(position-cacheStart),n=Math.Min(count,cachePayloadCount-at);
                Buffer.BlockCopy(cache,32+at,b,offset,n);position+=n;offset+=n;count-=n;copied+=n;timing.SourceBytes+=n;
            }return copied;
            } finally {timing.ReadTicks+=Stopwatch.GetTimestamp()-started;}
        }
        public override bool CanRead{get{return usb!=IntPtr.Zero;}} public override bool CanSeek{get{return CanRead;}} public override bool CanWrite{get{return false;}}
        public override long Length{get{return length;}} public override long Position{get{return position;}set{if(value<0 || value>length)throw new ArgumentOutOfRangeException();position=value;}}
        public override long Seek(long offset,SeekOrigin origin){Position=checked(offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:origin==SeekOrigin.End?length:throw new ArgumentOutOfRangeException()));return position;}
        public override void Flush(){} public override void SetLength(long v){throw new NotSupportedException();} public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){try{if(usb!=IntPtr.Zero)EndSequentialRange();}finally{if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}base.Dispose(disposing);}}
    }
}
