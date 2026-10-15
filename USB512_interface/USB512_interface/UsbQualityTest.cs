using System;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CE32_console;

namespace USB512_interface {
    internal sealed class UsbQualityResult {
        internal string Outcome,Scope,Error;
        internal long Bytes,Blocks;
        internal bool Continuous;
        internal TimeSpan Elapsed;
        internal string Summary {get{return string.Format("USB test {0} | {1:F2} MiB validated | {2:F2} MB/s\r\n{3}",Outcome,Bytes/1048576.0,Elapsed.TotalSeconds>0?Bytes/Elapsed.TotalSeconds/1e6:0,Scope);}}
        internal string Report {get{return Summary+"\r\nMode: "+(Continuous?"Continuous (until Stop or error)":"Bounded (10 seconds / 256 MiB)")+"\r\nValidated blocks: "+Blocks+"\r\nRead time: "+Elapsed.TotalSeconds.ToString("F3")+" s\r\n"+(Error==null?"":Error+"\r\n")+"\r\nCRC/sequence checks describe observed data integrity, not a physical cable bit-error rate. SD-backed results include storage latency. Slow transfers do not establish a bad cable.\r\n";}}
    }
    internal static class UsbQualityTest {
        internal const long MaximumBytes=256L*1024*1024;
        internal const int Seconds=10;
        internal static UsbQualityResult RunDevice(string path,bool allowAcquisition,Func<bool> cancelled,Action<long> progress,bool continuous) {
            if(cancelled!=null && cancelled())return new UsbQualityResult{Outcome=continuous?"stopped":"cancelled",Continuous=continuous,Scope="No USB test started"};
            try {
                using(var stream=new NpDownloadStream(path,true,true))return stream.TestUsbQuality(cancelled,progress,continuous);
            } catch(NpDownloadProtocolUnavailableException) {
                if(!allowAcquisition)throw;
            }
            return NpAcquisitionUsbQuality.Run(path,cancelled,progress,continuous);
        }
        // The injected clock/read boundary permits a focused check without USB.
        internal static UsbQualityResult Run(Func<int> read,int blockBytes,Func<bool> cancelled,Action<long> progress,Func<TimeSpan> elapsed,string scope,bool continuous=false) {
            if(read==null || elapsed==null || blockBytes<512 || blockBytes>1020*512 || blockBytes%512!=0)throw new ArgumentException("Invalid USB test geometry.");
            var result=new UsbQualityResult{Outcome="completed",Scope=scope,Continuous=continuous};TimeSpan nextProgress=TimeSpan.Zero;
            try {
                while(continuous || (result.Bytes<=MaximumBytes-blockBytes && elapsed()<TimeSpan.FromSeconds(Seconds))) {
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                    int bytes=read();if(bytes!=blockBytes)throw new InvalidDataException("USB test returned a short or oversized validated block.");
                    checked {result.Bytes+=bytes;result.Blocks++;}
                    TimeSpan now=elapsed();if(progress!=null&&now>=nextProgress){progress(result.Bytes);nextProgress=now+TimeSpan.FromMilliseconds(100);}
                }
                if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                if(result.Blocks==0)throw new IOException("No USB test data was received.");
            }catch(OperationCanceledException){result.Outcome=continuous?"stopped":"cancelled";}
            catch(TimeoutException ex){result.Outcome="incomplete";result.Error=ex.Message;}
            catch(Exception ex){result.Outcome="failed";result.Error=ex.ToString();}
            result.Elapsed=elapsed();return result;
        }
        internal static void CheckDeadline(bool continuous,TimeSpan total,TimeSpan block) {
            if((continuous?block:total)>TimeSpan.FromSeconds(Seconds+5))throw new TimeoutException("USB test "+(continuous?"block ":"")+"time budget expired before a complete validated block; no integrity conclusion for the partial tail.");
        }
    }

    // Existing V1 acquisition firmware: 0x43 starts a private SRAM pattern,
    // not a probe/SD recording. Never send this command to the V2 protocol.
    internal static class NpAcquisitionUsbQuality {
        const int BlockBytes=64*512;
        static byte[] Control(IntPtr usb,byte type,byte request,ushort size,out uint count) {
            var bytes=new byte[size];var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=type,Request=request,Length=size};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,bytes,size,out count,IntPtr.Zero)) {
                int error=Marshal.GetLastWin32Error();
                throw new IOException("Acquisition USB test request 0x"+request.ToString("X2")+" failed (Windows "+error+")."+
                    (request==0x43?" This firmware did not acknowledge the synthetic test; no valid test result is available.":""),new Win32Exception(error));
            }
            if((size==0 && count!=0) || (size!=0 && (count<76 || count>size)))throw new InvalidDataException("Invalid acquisition USB test status length.");
            return bytes;
        }
        static byte[] Status(IntPtr usb,bool synthetic) {
            uint count;var status=Control(usb,0xc0,0x42,400,out count);uint version=NpDownloadStream.U(status,4),flags=NpDownloadStream.U(status,12);
            if(NpDownloadStream.U(status,0)!=0x55534253 || version<1 || version>8 || NpDownloadStream.U(status,16)!=1)
                throw new IOException("This firmware does not identify the supported acquisition test protocol. No valid test result is available.");
            if(synthetic) {
                if(NpDownloadStream.U(status,20)!=1 || (flags&0x21)!=0x21 || (flags&0x46)!=0 || NpDownloadStream.U(status,44)!=0)
                    throw new IOException("Acquisition firmware did not retain a clean synthetic USB test stream.");
            } else if(NpDownloadStream.U(status,20)!=0 || NpDownloadStream.U(status,28)!=0 || (flags&0x49)!=0 || (count>=216 && NpDownloadStream.U(status,212)!=0))
                throw new IOException("Stop acquisition and wait for the USB device to become idle before testing. No test START was sent.");
            return status;
        }
        internal static void Validate(byte[] bytes,ref ushort expected) {
            if(bytes==null || bytes.Length==0 || bytes.Length%512!=0)throw new InvalidDataException("Invalid acquisition test packet extent.");
            for(int offset=0;offset<bytes.Length;offset+=512) {
                if(BitConverter.ToUInt16(bytes,offset)!=0xa512 || bytes[offset+2]!=6 || bytes[offset+3]!=0 || BitConverter.ToUInt16(bytes,offset+4)!=expected)
                    throw new InvalidDataException("Acquisition USB test packet header/sequence mismatch; test stopped without resynchronizing.");
                byte seed=bytes[offset+6];if(seed!=0 && seed!=17)throw new InvalidDataException("Unknown acquisition USB test pattern.");
                for(int i=0;i<506;i++)if(bytes[offset+6+i]!=unchecked((byte)(i+seed)))throw new InvalidDataException("Acquisition USB reference pattern mismatch.");
                expected=unchecked((ushort)(expected+1));
            }
        }
        internal static UsbQualityResult Run(string path,Func<bool> cancelled,Action<long> progress,bool continuous) {
            using(SafeFileHandle file=NpDownloadNative.CreateFile(path,0xc0000000,0,IntPtr.Zero,3,0x40000080,IntPtr.Zero)) {
                if(file.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr usb;if(!NpDownloadNative.WinUsbInitialize(file,out usb,path))throw new Win32Exception(Marshal.GetLastWin32Error());
                bool attempted=false;UsbQualityResult result=null;
                try {
                    UsbHealth.Diagnostic(usb);
                    NpDownloadNative.USB_INTERFACE_DESCRIPTOR descriptor;
                    if(!NpDownloadNative.WinUsbQueryInterfaceSettings(usb,0,out descriptor))throw new Win32Exception(Marshal.GetLastWin32Error());
                    bool bulk=false;
                    for(byte i=0;i<descriptor.bNumEndpoints;i++) {
                        NpDownloadNative.WINUSB_PIPE_INFORMATION pipe;
                        if(!NpDownloadNative.WinUsbQueryPipe(usb,0,i,out pipe))throw new Win32Exception(Marshal.GetLastWin32Error());
                        if(pipe.PipeId==0x81 && pipe.PipeType==NpDownloadNative.USBD_PIPE_TYPE.Bulk && (pipe.MaximumPacketSize==64 || pipe.MaximumPacketSize==512))bulk=true;
                    }
                    if(!bulk)throw new IOException("Acquisition USB test bulk-IN endpoint 0x81 is unavailable.");
                    Status(usb,false);
                    uint timeout=3000,partial=1;
                    if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,3,4,ref timeout) || !NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,5,4,ref partial))throw new Win32Exception(Marshal.GetLastWin32Error());
                    byte suspend=0;if(!NpDownloadNative.WinUsbSetPowerPolicy(usb,0x81,1,ref suspend))throw new Win32Exception(Marshal.GetLastWin32Error());
                    if(cancelled!=null && cancelled())return new UsbQualityResult{Outcome=continuous?"stopped":"cancelled",Continuous=continuous,Scope="No acquisition test START sent"};
                    attempted=true;uint ignored;Control(usb,0x40,0x43,0,out ignored);Status(usb,true);
                    var watch=Stopwatch.StartNew();var blockWatch=Stopwatch.StartNew();var bytes=new byte[BlockBytes];ushort expected=0;
                    result=UsbQualityTest.Run(()=>{
                        blockWatch.Restart();int got=0;
                        while(got<bytes.Length) {
                            if(cancelled!=null && cancelled())throw new OperationCanceledException();
                            UsbQualityTest.CheckDeadline(continuous,watch.Elapsed,blockWatch.Elapsed);
                            byte[] part=got==0?bytes:new byte[bytes.Length-got];uint count;
                            if(!NpDownloadNative.WinUsbReadPipe(usb,0x81,part,(uint)part.Length,out count,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"Acquisition USB test bulk read failed");
                            if(count==0 || count>part.Length)throw new IOException("Invalid acquisition USB test read length.");
                            if(got!=0)Buffer.BlockCopy(part,0,bytes,got,(int)count);got+=(int)count;
                        }
                        try {Validate(bytes,ref expected);UsbHealth.Integrity(usb,0x81,false,"acquisition SRAM pattern + packet sequence");}
                        catch(InvalidDataException){UsbHealth.Integrity(usb,0x81,true,"acquisition SRAM pattern + packet sequence");throw;}
                        return bytes.Length;
                    },BlockBytes,cancelled,progress,()=>watch.Elapsed,"Acquisition firmware SRAM pattern + packet sequence; no probe/SD recording",continuous);
                    if(result.Outcome!="failed")try {Status(usb,true);}catch(Exception ex){result.Outcome="failed";result.Error=ex.ToString();}
                    return result;
                } finally {
                    try {
                        if(attempted) {
                            try {uint ignored;Control(usb,0x40,0x41,0,out ignored);Status(usb,false);}
                            catch(Exception ex) {
                                if(result==null)throw;
                                result.Outcome="failed";result.Error=(result.Error==null?"":result.Error+"\r\n")+"USB test cleanup: "+ex;
                            }
                        }
                    } finally {NpDownloadNative.WinUsbFree(usb);}
                }
            }
        }
    }
}
namespace CE32_console {
    internal sealed partial class NpDownloadStream {
        internal USB512_interface.UsbQualityResult TestUsbQuality(Func<bool> cancelled,Action<long> progress,bool continuous=false) {
            int block=checked((int)Math.Min(maxBlocks,length/512)*512);
            if(IsSynthetic && block!=1020*512)throw new IOException("Unsupported RAM diagnostic geometry.");
            var watch=Stopwatch.StartNew();
            var blockWatch=Stopwatch.StartNew();
            Cancelled=()=>{if(cancelled!=null&&cancelled())return true;USB512_interface.UsbQualityTest.CheckDeadline(continuous,watch.Elapsed,blockWatch.Elapsed);return false;};
            var result=USB512_interface.UsbQualityTest.Run(()=>{
                blockWatch.Restart();
                position=0;Fill(); // Always submit a fresh read; never serve the cache.
                if(IsSynthetic) {
                    for(int word=0;word<cachePayloadCount/4;word++)if(U(cache,32+word*4)!=(unchecked((uint)word*0x9e3779b9U)^0xa5c37e19U)) {
                        USB512_interface.UsbHealth.Integrity(usb,0x81,true,"RAM pattern + header/sequence/CRC");throw new InvalidDataException("RAM diagnostic reference pattern mismatch.");
                    }
                    USB512_interface.UsbHealth.Integrity(usb,0x81,false,"RAM pattern + header/sequence/CRC");
                }
                return cachePayloadCount;
            },block,cancelled,progress,()=>watch.Elapsed,IsSynthetic?"RAM pattern + header/sequence/CRC":"Read-only SD-backed data + header/sequence/CRC",continuous);
            if(qualityRequestPending) {
                try {
                    // This handle admitted an idle service and issued this read.
                    // Retire only that unfinished test request, never a request
                    // found busy before the test started.
                    Control(0x40,0x62,0,0,0);
                    var cleanup=Stopwatch.StartNew();
                    while(U(Control(0xc0,0x60,0,0,32),16)!=0) {
                        if(cleanup.ElapsedMilliseconds>2000)throw new IOException("The test's pending read did not return to idle after cancellation.");
                        System.Threading.Thread.Sleep(10);
                    }
                    qualityRequestPending=false;
                }catch(Exception ex){result.Outcome="failed";result.Error=(result.Error==null?"":result.Error+"\r\n")+"USB test cleanup: "+ex;}
            }
            return result;
        }
    }
}
