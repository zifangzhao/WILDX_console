using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CE32_console;

// Fake native boundary; the actual download stream, CRC/parser, quality runner
// and diagnostic accounting sources are compiled unchanged with this fixture.
namespace CE32_console {
    internal static class NpDownloadNative {
        internal struct USB_INTERFACE_DESCRIPTOR {internal byte bNumEndpoints;}
        internal enum USBD_PIPE_TYPE {Bulk=2}
        internal struct WINUSB_PIPE_INFORMATION {internal byte PipeId;internal USBD_PIPE_TYPE PipeType;internal ushort MaximumPacketSize;}
        internal struct WINUSB_SETUP_PACKET {internal byte RequestType,Request;internal ushort Value,Index,Length;}
        internal static uint Magic,Capacity,State,Sequence,Epoch,Share;
        internal static bool BadPattern,BadCrc,Partial;
        internal static bool Acquisition,SyntheticRunning,BadStatus,MissingSynthetic,StopFails,ShortControl;
        internal static uint DownloadError=31;
        [DllImport("kernel32.dll",EntryPoint="SetLastError",SetLastError=true)]static extern void SetLastError(uint error);
        internal static int Resets,Flushes,Power,Reads,Initializations;
        internal static readonly List<byte> Out=new List<byte>();
        static uint lba;
        static byte[] reply;static int at;
        internal static void Setup(){Magic=0x3144504e;Capacity=1020*4096;State=0;Sequence=0;Epoch=7;BadPattern=BadCrc=Partial=Acquisition=SyntheticRunning=BadStatus=MissingSynthetic=StopFails=ShortControl=false;DownloadError=31;Resets=Flushes=Power=Reads=0;Out.Clear();reply=null;}
        internal static SafeFileHandle CreateFile(string p,uint access,uint share,IntPtr a,uint c,uint flags,IntPtr t){Share=share;return new SafeFileHandle(new IntPtr(999),false);}
        internal static bool WinUsbInitialize(SafeFileHandle f,out IntPtr h,string target=null){h=new IntPtr(++Initializations);USB512_interface.UsbHealth.Open(h,target,3,0);return true;}
        internal static bool WinUsbFree(IntPtr h){USB512_interface.UsbHealth.Close(h);return true;}
        internal static bool WinUsbQueryInterfaceSettings(IntPtr h,byte a,out USB_INTERFACE_DESCRIPTOR d){d=new USB_INTERFACE_DESCRIPTOR{bNumEndpoints=1};return true;}
        internal static bool WinUsbQueryPipe(IntPtr h,byte a,byte i,out WINUSB_PIPE_INFORMATION p){p=new WINUSB_PIPE_INFORMATION{PipeId=0x81,PipeType=USBD_PIPE_TYPE.Bulk,MaximumPacketSize=512};return true;}
        internal static bool WinUsbSetPipePolicy(IntPtr h,byte p,uint kind,uint size,ref uint value){return true;}
        internal static bool WinUsbSetPowerPolicy(IntPtr h,uint kind,uint size,ref byte value){Power++;return true;}
        internal static bool WinUsbResetPipe(IntPtr h,byte pipe){Resets++;return true;}
        internal static bool WinUsbFlushPipe(IntPtr h,byte pipe){Flushes++;return true;}
        static void Put(byte[] b,int offset,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,offset,4);}
        internal static bool WinUsbControlTransfer(IntPtr h,WINUSB_SETUP_PACKET setup,byte[] b,uint length,out uint n,IntPtr ov){
            n=length;
            if(Acquisition) {
                if(setup.RequestType==0xc0 && setup.Request==0x60){n=0;SetLastError(DownloadError);return false;}
                if(setup.RequestType==0xc0 && setup.Request==0x42){Put(b,0,BadStatus?0U:0x55534253U);Put(b,4,8);Put(b,12,SyntheticRunning?0x21U:State);Put(b,16,1);Put(b,20,SyntheticRunning?1U:State);return true;}
                if(setup.RequestType!=0x40)throw new Exception("Unexpected acquisition control request");
                Out.Add(setup.Request);
                if(setup.Request==0x43){if(MissingSynthetic){SetLastError(31);return false;}SyntheticRunning=true;Sequence=0;return true;}
                if(setup.Request==0x41){if(StopFails){SetLastError(31);return false;}SyntheticRunning=false;State=0;return true;}
                throw new Exception("Unexpected acquisition mutation");
            }
            if(setup.RequestType==0xc0&&setup.Request==0x60){Put(b,0,Magic);Put(b,4,1);Put(b,8,Capacity);Put(b,12,1020);Put(b,16,State);Put(b,20,Sequence);Put(b,24,Epoch);Put(b,28,Magic==0x3154504e?3U:1U);if(ShortControl)n=31;return true;}
            if(setup.RequestType!=0x40)throw new Exception("Unexpected control request");Out.Add(setup.Request);
            if(setup.Request==0x62){State=0;return true;}
            if(setup.Request!=0x61)throw new Exception("Unexpected mutation/request 0x"+setup.Request.ToString("X2"));
            lba=(uint)setup.Value|((uint)setup.Index<<16);reply=null;at=0;return true;
        }
        internal static bool WinUsbReadPipe(IntPtr h,byte pipe,byte[] buffer,uint length,out uint n,IntPtr ov){
            Reads++;
            if(Acquisition) {
                var bytes=new byte[length];
                for(int offset=0;offset<bytes.Length;offset+=512){bytes[offset]=0x12;bytes[offset+1]=0xa5;bytes[offset+2]=6;bytes[offset+4]=(byte)Sequence;bytes[offset+5]=(byte)(Sequence>>8);int seed=((Sequence/63)&1)==0?0:17;for(int i=0;i<506;i++)bytes[offset+6+i]=(byte)(i+seed);Sequence++;}
                if(BadPattern)bytes[100]^=1;if(BadCrc)bytes[4]^=1;
                n=Partial?16:length;Buffer.BlockCopy(bytes,0,buffer,0,(int)n);var attempt=USB512_interface.UsbHealth.Begin(h,pipe,Reads*100);USB512_interface.UsbHealth.Submitted(h,attempt,IntPtr.Zero,true,0,n,false,Reads*100+50);return true;
            }
            if(reply==null){reply=new byte[32+1020*512];Put(reply,0,0x3142504e);Put(reply,4,++Sequence);Put(reply,8,lba);Put(reply,12,1020);Put(reply,24,Epoch);
                for(uint word=0;word<1020*128;word++)Put(reply,32+(int)word*4,unchecked(word*0x9e3779b9U)^0xa5c37e19U);
                if(BadPattern)reply[100]^=1;Put(reply,20,NpDownloadStream.Crc(reply,32,1020*512));Put(reply,28,NpDownloadStream.Crc(reply,0,28));if(BadCrc)reply[120]^=1;
            }
            n=(uint)Math.Min((int)length,Partial?16:reply.Length-at);Buffer.BlockCopy(reply,at,buffer,0,(int)n);at+=(int)n;
            var a=USB512_interface.UsbHealth.Begin(h,pipe,Reads*100);USB512_interface.UsbHealth.Submitted(h,a,IntPtr.Zero,true,0,n,false,Reads*100+50);return true;
        }
    }
}
static class UsbQualityTransportTests {
    static int checks;const string Path="usb#vid_1209&pid_b386#quality-fixture";
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Refuse(){bool refused=false;try{using(var s=new NpDownloadStream(Path,true,true)){}}catch(IOException){refused=true;}Check(refused,"Unsafe/unsupported test was admitted");Check(NpDownloadNative.Out.Count==0&&NpDownloadNative.Resets==0&&NpDownloadNative.Flushes==0&&NpDownloadNative.Reads==0,"Refused test changed device or read payload");}
    static int Main(){try{
        foreach(string kind in new[]{"busy","no-card","unsupported"}){NpDownloadNative.Setup();if(kind=="busy")NpDownloadNative.State=1;else if(kind=="no-card")NpDownloadNative.Capacity=0;else NpDownloadNative.Magic=0;Refuse();}
        NpDownloadNative.Setup();using(var s=new NpDownloadStream(Path,true,true)){
            var result=s.TestUsbQuality(()=>NpDownloadNative.Reads>=2,null);
            Check(result.Outcome=="cancelled"&&result.Blocks==2&&result.Bytes==2*1020L*512,"Cancellation did not preserve validated prefix");
            Check(NpDownloadNative.Share==0&&NpDownloadNative.Power==0&&NpDownloadNative.Resets==0&&NpDownloadNative.Flushes==0,"Quality path was not exclusive/passive");
            Check(NpDownloadNative.Out.SequenceEqual(new byte[]{0x61,0x61}),"Test sent cancel/reset/mode/recording commands");
            Check(USB512_interface.UsbHealth.Snapshots(Path).Length==0,"Test polluted recording counters");
        }
        NpDownloadNative.Setup();NpDownloadNative.Magic=0x3154504e;using(var s=new NpDownloadStream(Path,true,true)){
            var result=s.TestUsbQuality(()=>NpDownloadNative.Reads>=1,null);Check(result.Blocks==1&&result.Scope.StartsWith("RAM"),"Known RAM pattern not checked");
        }
        foreach(bool badCrc in new[]{false,true}){NpDownloadNative.Setup();NpDownloadNative.Magic=0x3154504e;NpDownloadNative.BadCrc=badCrc;NpDownloadNative.BadPattern=!badCrc;using(var s=new NpDownloadStream(Path,true,true)){
            var result=s.TestUsbQuality(()=>false,null);Check(result.Outcome=="failed"&&result.Blocks==0&&NpDownloadNative.Reads==1,"Integrity error retried or falsely completed");
            string report=USB512_interface.UsbHealth.DiagnosticReport(Path);Check(report.Contains("Application integrity failures: 1")&&report.Contains("failed: 0"),"Integrity and Windows failures mixed");
        }}
        NpDownloadNative.Setup();NpDownloadNative.Partial=true;using(var s=new NpDownloadStream(Path,true,true)){var result=s.TestUsbQuality(()=>NpDownloadNative.Reads>=1,null);Check(result.Outcome=="cancelled"&&result.Bytes==0&&NpDownloadNative.Reads==1,"Cancellation ignored between partial native reads");Check(NpDownloadNative.Out.SequenceEqual(new byte[]{0x61,0x62})&&NpDownloadNative.State==0,"Stopped test left its owned download request pending");}
        NpDownloadNative.Setup();using(var s=new NpDownloadStream(Path,true,true)){var result=s.TestUsbQuality(()=>NpDownloadNative.Reads>=2,null,true);Check(result.Outcome=="stopped"&&result.Blocks==2&&result.Continuous,"Continuous transport did not stop cleanly");Check(NpDownloadNative.Out.SequenceEqual(new byte[]{0x61,0x61}),"Continuous mode sent a new firmware or mode command");}
        NpDownloadNative.Setup();using(var s=new NpDownloadStream(Path)){Check(NpDownloadNative.Share==3&&NpDownloadNative.Power==1&&NpDownloadNative.Resets==1&&NpDownloadNative.Flushes==1&&NpDownloadNative.Out.SequenceEqual(new byte[]{0x62}),"Existing download initialization changed");}
        foreach(uint error in new uint[]{1,31}) {
            NpDownloadNative.Setup();NpDownloadNative.Acquisition=true;NpDownloadNative.DownloadError=error;
            var result=USB512_interface.UsbQualityTest.RunDevice(Path,true,()=>NpDownloadNative.Reads>=3,null,true);
            Check(result.Outcome=="stopped"&&result.Blocks==3&&result.Scope.StartsWith("Acquisition"),"Acquisition protocol fallback unavailable after unsupported download request");
            Check(NpDownloadNative.Out.SequenceEqual(new byte[]{0x43,0x41})&&!NpDownloadNative.SyntheticRunning,"Acquisition test did not own/start/stop only its synthetic stream");
            Check(NpDownloadNative.Share==0&&NpDownloadNative.Resets==0&&NpDownloadNative.Flushes==0&&NpDownloadNative.Power==1,"Acquisition test changed USB modes or reset a pipe");
            Check(USB512_interface.UsbHealth.Snapshots(Path).Length==0,"Acquisition test polluted recording metrics");
        }
        foreach(string failure in new[]{"busy","bad-status","missing-start","v2","timeout","short-control"}) {
            NpDownloadNative.Setup();NpDownloadNative.Acquisition=true;
            if(failure=="busy")NpDownloadNative.State=1;if(failure=="bad-status")NpDownloadNative.BadStatus=true;if(failure=="missing-start")NpDownloadNative.MissingSynthetic=true;
            if(failure=="timeout")NpDownloadNative.DownloadError=121;if(failure=="short-control"){NpDownloadNative.Acquisition=false;NpDownloadNative.ShortControl=true;}
            bool refused=false;try{USB512_interface.UsbQualityTest.RunDevice(Path,failure!="v2",()=>false,null,true);}catch(Exception ex)when(ex is IOException || ex is InvalidDataException){refused=true;}
            Check(refused&&NpDownloadNative.Reads==0,"Unsafe acquisition fallback admitted: "+failure);
            Check(failure=="missing-start"?NpDownloadNative.Out.SequenceEqual(new byte[]{0x43,0x41}):NpDownloadNative.Out.Count==0,"Refused test sent a recording/mode command: "+failure);
        }
        foreach(bool badSequence in new[]{false,true}) {
            NpDownloadNative.Setup();NpDownloadNative.Acquisition=true;NpDownloadNative.BadPattern=!badSequence;NpDownloadNative.BadCrc=badSequence;
            var result=USB512_interface.UsbQualityTest.RunDevice(Path,true,()=>false,null,true);
            Check(result.Outcome=="failed"&&result.Blocks==0&&NpDownloadNative.Reads==1&&!NpDownloadNative.SyntheticRunning,"Acquisition pattern/sequence failure retried or falsely passed");
        }
        NpDownloadNative.Setup();NpDownloadNative.Acquisition=true;NpDownloadNative.Partial=true;
        var partial=USB512_interface.UsbQualityTest.RunDevice(Path,true,()=>NpDownloadNative.Reads>=1,null,true);
        Check(partial.Outcome=="stopped"&&partial.Bytes==0&&!NpDownloadNative.SyntheticRunning,"Partial acquisition stop did not retire its test stream");
        NpDownloadNative.Setup();NpDownloadNative.Acquisition=true;NpDownloadNative.StopFails=true;
        var stopFailure=USB512_interface.UsbQualityTest.RunDevice(Path,true,()=>NpDownloadNative.Reads>=1,null,true);
        Check(stopFailure.Outcome=="failed"&&stopFailure.Blocks==1&&stopFailure.Error.Contains("cleanup"),"Unconfirmed acquisition STOP reported success");
        Console.WriteLine("PASS "+checks+" actual-source quality transport checks with fake native boundary; no hardware calls.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
