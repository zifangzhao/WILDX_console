using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace USB512_interface
{
    // Host implementation for the XE_NP firmware protocol.  It deliberately uses
    // the console's proven WinUSB setup (D0, partial reads, and one bulk-IN pipe)
    // but sends XE_NP's own start/stop requests.
    internal sealed class XeNpWinUsbDevice : IDisposable
    {
        private const byte RequestStart = 0x40;
        private const byte RequestStop = 0x41;
        private const byte RequestSetMcoDivider = 0x44;
        private const uint PipeTransferTimeout = 3;
        private const uint AllowPartialReads = 5;
        private const uint AutoClearStall = 2;
        private const uint AutoSuspend = 0x81;
        private readonly SafeFileHandle handle;
        private readonly string devicePath;
        private IntPtr winUsb;
        private readonly byte bulkInPipe;
        private bool awaitingFirstSynchronousSample;
        private bool queuedCapture;
        private uint deferredAdmissionSession;
        private XeNpLaneLayout deferredAdmissionLayout;
        private NpModeCapabilities deferredAdmissionCapabilities;
        internal NpStartupAdmission StartupAdmission {get;private set;}
        [DllImport("winusb.dll",EntryPoint="WinUsb_GetPipePolicy",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPipePolicy(IntPtr h,byte pipe,uint policy,ref uint length,out uint value);
        [DllImport("winusb.dll",EntryPoint="WinUsb_SetPipePolicy",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetBooleanPolicy(IntPtr h,byte pipe,uint policy,uint length,ref byte value);

        private XeNpWinUsbDevice(string path, SafeFileHandle handle, IntPtr winUsb, byte bulkInPipe)
        {
            this.devicePath = path;
            this.handle = handle;
            this.winUsb = winUsb;
            this.bulkInPipe = bulkInPipe;
        }

        internal static IList<string> EnumerateDevicePaths()
        {
            var paths=new List<string>();
            foreach(string path in USB512WinUsbNative.EnumerateInterfacePaths(USB512WinUsbNative.NeuropixelsInterfaceGuid))
                if(IsCapturePath(path)) paths.Add(path);
            return paths;
        }

        internal static bool IsCapturePath(string path)
        {
            return path!=null && path.IndexOf("vid_1209&pid_b386",StringComparison.OrdinalIgnoreCase)>=0;
        }

        internal static XeNpWinUsbDevice Open(string path) { return OpenCore(path,true); }
        internal static XeNpWinUsbDevice OpenForControl(string path) { return OpenCore(path,false); }
        private static XeNpWinUsbDevice OpenCore(string path, bool configureCapturePipe)
        {
            if(!IsCapturePath(path)) throw new InvalidOperationException("WILDX capture requires WinUSB PID B386, not the B387 updater or B388 MSC personality.");
            SafeFileHandle handle = USB512WinUsbNative.CreateFile(path,
                USB512WinUsbNative.GenericRead | USB512WinUsbNative.GenericWrite,
                USB512WinUsbNative.FileShareRead | USB512WinUsbNative.FileShareWrite,
                IntPtr.Zero, USB512WinUsbNative.OpenExisting,
                USB512WinUsbNative.FileAttributeNormal | USB512WinUsbNative.FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile for WILDX WinUSB failed");

            IntPtr winUsb;
            if (!USB512WinUsbNative.WinUsbInitialize(handle, out winUsb))
            {
                handle.Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize for WILDX failed");
            }
            try
            {
                USB512WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
                if (!USB512WinUsbNative.WinUsbQueryInterfaceSettings(winUsb, 0, out descriptor))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings for WILDX failed");
                byte pipeId = 0;
                for (byte index = 0; index < descriptor.bNumEndpoints; index++)
                {
                    USB512WinUsbNative.WINUSB_PIPE_INFORMATION pipe;
                    if (USB512WinUsbNative.WinUsbQueryPipe(winUsb, 0, index, out pipe) &&
                        pipe.PipeType == USB512WinUsbNative.USBD_PIPE_TYPE.Bulk && (pipe.PipeId & 0x80) != 0)
                    {
                        pipeId = pipe.PipeId;
                        break;
                    }
                }
                if (pipeId == 0)
                    throw new InvalidOperationException("WILDX bulk-IN endpoint was not found.");

                XeNpWinUsbDevice device = new XeNpWinUsbDevice(path, handle, winUsb, pipeId);
                if(configureCapturePipe)device.ConfigurePipe();
                return device;
            }
            catch
            {
                USB512WinUsbNative.WinUsbFree(winUsb);
                handle.Dispose();
                throw;
            }
        }

        private void ConfigurePipe()
        {
            byte off = 0;
            if(!USB512WinUsbNative.WinUsbSetPowerPolicy(winUsb, AutoSuspend, 1, ref off))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "WILDX cannot disable WinUSB selective suspend");
            uint timeout = 1000;
            USB512WinUsbNative.WinUsbSetPipePolicy(winUsb, bulkInPipe, PipeTransferTimeout, 4, ref timeout);
            uint partial = 1;
            USB512WinUsbNative.WinUsbSetPipePolicy(winUsb, bulkInPipe, AllowPartialReads, 4, ref partial);
            uint autoClear = 1;
            USB512WinUsbNative.WinUsbSetPipePolicy(winUsb, bulkInPipe, AutoClearStall, 4, ref autoClear);
            USB512WinUsbNative.WinUsbResetPipe(winUsb, bulkInPipe);
            USB512WinUsbNative.WinUsbFlushPipe(winUsb, bulkInPipe);
        }

        internal NpModeCapabilities GetModeCapabilities()
        {
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0xc0,Request=0x73,Length=48};
            byte[] data=new byte[48];uint got;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,data,48,out got,IntPtr.Zero)) {
                int error=Marshal.GetLastWin32Error();
                // Legacy firmware stalls unimplemented requests. Verify its
                // ordinary status still works before accepting legacy mode.
                if(error==31 || error==1) {GetStatus();return null;}
                throw new Win32Exception(error,"Cannot read NP mode capabilities");
            }
            return NpModeCapabilities.Parse(data,checked((int)got));
        }

        // Resolve a human request from the capability descriptor, then send
        // only a concrete supported mode.  The returned text is saved with
        // the recording so a reduced best-effort profile is never mistaken
        // for the requested one.
        internal NpModeCapabilities SelectCaptureMode(NpCaptureRequest requested,out NpCaptureMode resolved,out string resolution,out string reconnectUid)
        {
            reconnectUid=null;
            XeNpStatus status=GetStatus();
            if(status.Streaming)throw new IOException("Stop recording before selecting an NP mode.");
            NpModeCapabilities caps=GetModeCapabilities();
            if(caps==null) {
                resolved=null;
                resolution=requested==null?"firmware default (legacy capability descriptor absent)":
                    "requested "+requested.Describe()+"; firmware does not advertise mode selection, using its active default";
                return null;
            }
            if(requested==null && caps.SupportsPresence)
                requested=new NpCaptureRequest(caps.ActiveMask,caps.ApHz);
            if(requested==null) {
                resolved=caps.Active;resolution="firmware default; active "+NpCaptureRequest.Ports(resolved.ProbeMask)+" at "+resolved.ApHz+" Hz";
                return caps;
            }
            if(!caps.Selectable) {
                resolved=caps.Active;
                resolution="requested "+requested.Describe()+"; firmware profile is fixed, using "+
                    NpCaptureRequest.Ports(resolved.ProbeMask)+" at "+resolved.ApHz+" Hz";
                return caps;
            }
            resolved=requested.ResolveBest(caps,out resolution);
            if(caps.SupportsPresence) {
                int checkMask=resolved.ProbeMask;
                VendorOut(0x75,(ushort)checkMask,(ushort)resolved.ApHz);
                DateTime presenceDeadline=DateTime.UtcNow.AddSeconds(12);
                do {
                    System.Threading.Thread.Sleep(10);
                    caps=GetModeCapabilities();
                    if(caps==null || !caps.SupportsPresence)
                        throw new IOException("NP presence capability disappeared during preflight.");
                    if(caps.PresenceRequestedMask!=checkMask)
                        throw new IOException("NP presence preflight refused: a different selection was detected.");
                    if(caps.PresenceState==3)
                        throw new IOException("NP presence preflight failed; I2C/reset/clock fault (ports "+
                            NpCaptureRequest.Ports(caps.PresenceFaultMask)+"). No START sent.");
                    if(caps.PresenceReady)break;
                } while(DateTime.UtcNow<presenceDeadline);
                if(!caps.PresenceReady)throw new IOException("NP presence preflight timed out. No START sent.");
                if((requested.ProbeMask&caps.PresentMask)==0)
                    throw new IOException("No selected NP probe responded. No recording was started.");
                resolved=requested.ResolveBest(caps.ForPresentProbes(),out resolution);
                if(caps.AbsentMask!=0)resolution+="; automatically disabled absent "+NpCaptureRequest.Ports(caps.AbsentMask);
            }
            if(resolved.ProbeMask!=caps.ActiveMask || resolved.ApHz!=caps.ApHz) {
                string uid=NpV1Management.UidFromPath(devicePath);
                bool clockReset=uid!=null && ((resolved.ApHz==20000)!=(caps.ApHz==20000));
                VendorOut(0x74,(ushort)resolved.ProbeMask,(ushort)resolved.ApHz);
                // V1 entering/leaving native 20 kHz queues a STREAM reset after
                // the ACK. Other families retain synchronous mode verification.
                if(clockReset) {reconnectUid=uid;return null;}
                caps=GetModeCapabilities();
                if(caps==null || caps.ActiveMask!=resolved.ProbeMask || caps.ApHz!=resolved.ApHz)
                    throw new IOException("Firmware did not acknowledge the resolved NP mode. No START sent.");
            }
            return caps;
        }

        internal XeNpStatus Start(ushort mcoDivider)
        {
            XeNpStatus status=GetStatus();
            var startCaps=GetModeCapabilities();
            bool admission=startCaps!=null && startCaps.SupportsStartupAdmission;
            StartupAdmission=null;deferredAdmissionSession=0;deferredAdmissionCapabilities=null;
            if(admission && !status.SupportsFreshStart)
                throw new IOException("NP startup admission requires a fresh-session status contract. No START sent.");
            awaitingFirstSynchronousSample=status.UsesV1SynchronousReads && !queuedCapture;
            if(awaitingFirstSynchronousSample) {
                uint startupTimeout=10000;
                if(!USB512WinUsbNative.WinUsbSetPipePolicy(winUsb,bulkInPipe,PipeTransferTimeout,4,ref startupTimeout))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"NP startup read timeout policy failed");
            }
            // Preserve older GPIO profiles. V2 explicitly advertises PSSI;
            // restore its required 30 kS/s only when the divider is derivable.
            if(mcoDivider==0 && status.Layout==XeNpLaneLayout.Pssi && status.PsbClockHz!=23400000U)
            {
                ulong parent=(ulong)status.PsbClockHz*status.McoDivider;
                ulong divider=parent/23400000U;
                if(parent%23400000U!=0 || (divider!=1 && divider!=2 && divider!=4 && divider!=8 && divider!=16))
                    throw new IOException("Cannot establish the NP1 30 kS/s clock from the advertised PSSI profile.");
                mcoDivider=(ushort)divider;
            }
            if (mcoDivider != 0)
            {
                VendorOut(RequestSetMcoDivider, mcoDivider, 0);
                status=GetStatus();
            }
            if(status.Layout==XeNpLaneLayout.Pssi && status.ApSampleRateHz!=30000)
                throw new IOException("PSSI firmware does not report the required NP1 30 kS/s rate.");
            int expectedHz=startCaps==null?status.ApSampleRateHz:startCaps.ApHz;
            if(NpCaptureMode.RateBit(expectedHz)==0)
                throw new IOException("No supported NP capture profile is available. No START sent.");
            VendorOut(RequestStart, 0, 0);
            uint previousSession=status.SessionId,expectedSession=0;
            DateTime deadline=DateTime.UtcNow.AddSeconds(3);
            do {
                System.Threading.Thread.Sleep(10);
                XeNpStatus current=GetStatus();
                if(current.SupportsFreshStart!=status.SupportsFreshStart)
                    throw new IOException("NP session contract changed during START; capture rejected.");
                if(status.SupportsFreshStart) {
                    if(current.SessionId==previousSession)continue;
                    if(current.SessionId==0 || (expectedSession!=0 && current.SessionId!=expectedSession))
                        throw new IOException("NP session changed during START; capture rejected.");
                    expectedSession=current.SessionId;
                }
                current.ThrowIfFault();
                if(admission) {
                    WaitForStartupAdmission(current,startCaps);
                    // Diagnostic clocks may change before READY. Only this
                    // fresh snapshot supplies the admitted recording rate.
                    current=GetStatus();
                    RequireReadyProfile(current,expectedHz,status.Layout,expectedSession,startCaps,StartupAdmission);
                    deferredAdmissionLayout=current.Layout;deferredAdmissionCapabilities=startCaps;
                    return current;
                }
                // Older firmware has no holding receipt: its streaming bit is
                // the READY evidence. Never return the pre-START diagnostic.
                if(!current.Streaming)continue;
                RequireReadyProfile(current,expectedHz,status.Layout,expectedSession,startCaps,null);
                return current;
            } while(DateTime.UtcNow<deadline);
            throw new IOException("Firmware did not acknowledge a READY acquisition profile; capture rejected.");
        }

        internal static void RequireReadyProfile(XeNpStatus status,int expectedHz,XeNpLaneLayout expectedLayout,
            uint expectedSession,NpModeCapabilities requested,NpStartupAdmission admission)
        {
            status.ThrowIfFault();status.RequireCaptureProfile(expectedHz,expectedLayout);
            if(status.SupportsFreshStart && expectedSession==0)
                throw new IOException("NP READY status has no acknowledged fresh session; capture rejected.");
            if(expectedSession!=0 && (!status.SupportsFreshStart || status.SessionId!=expectedSession))
                throw new IOException("NP session changed after startup admission; capture was not released.");
            if(requested!=null && requested.ApHz!=expectedHz)
                throw new IOException("NP requested profile changed during START; capture rejected.");
            if(admission==null) {
                if(!status.Streaming)throw new IOException("NP capture is not READY; capture rejected.");
                return;
            }
            if(requested==null || !requested.SupportsStartupAdmission || expectedSession==0 ||
               admission.State!=2 || admission.SessionId!=expectedSession || admission.ApHz!=expectedHz ||
               admission.RequestedMask!=requested.ActiveMask || requested.ApHz!=expectedHz ||
               (admission.AcceptedMask&~requested.ActiveMask)!=0 ||
               !NpCaptureMode.Valid(admission.AcceptedMask,expectedHz) ||
               (requested.Rates[NpCaptureMode.Count(admission.AcceptedMask)-1]&NpCaptureMode.RateBit(expectedHz))==0)
                throw new IOException("NP READY receipt does not match the supported requested profile; capture was not released.");
        }

        internal NpStartupAdmission GetStartupAdmission() {
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0xc0,Request=0x77,Length=64};
            var b=new byte[64];uint got;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,b,64,out got,IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"NP startup admission unavailable");
            return NpStartupAdmission.Parse(b,checked((int)got));
        }
        void WaitForStartupAdmission(XeNpStatus status,NpModeCapabilities requested) {
            DateTime deadline=DateTime.UtcNow.AddSeconds(30);
            do {
                var a=GetStartupAdmission();
                if(a.SessionId!=status.SessionId || a.RequestedMask!=requested.ActiveMask || a.ApHz!=requested.ApHz)
                    throw new IOException("NP startup admission changed session or requested profile.");
                if(a.State==3)throw new IOException("No selected probe passed startup. "+a.Describe());
                if(a.State==2) {StartupAdmission=a;deferredAdmissionSession=a.SessionId;return;}
                GetStatus().ThrowIfFault();System.Threading.Thread.Sleep(10);
            }while(DateTime.UtcNow<deadline);
            throw new IOException("NP startup admission timed out; capture was not released."+StartupDiagnosticSummary());
        }
        internal void CompleteStartupAdmission() {
            if(deferredAdmissionSession==0)return;
            var a=GetStartupAdmission();
            if(a.State!=2 || a.SessionId!=deferredAdmissionSession || StartupAdmission==null ||
               a.AcceptedMask!=StartupAdmission.AcceptedMask || a.RequestedMask!=StartupAdmission.RequestedMask || a.ApHz!=StartupAdmission.ApHz)
                throw new IOException("NP admission receipt changed before capture release.");
            RequireReadyProfile(GetStatus(),checked((int)a.ApHz),deferredAdmissionLayout,
                deferredAdmissionSession,deferredAdmissionCapabilities,a);
            VendorOut(0x78,(ushort)deferredAdmissionSession,(ushort)(deferredAdmissionSession>>16));
            deferredAdmissionSession=0;
        }

        internal XeNpStatus GetStatus()
        {
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {
                RequestType=0xC0, Request=0x42, Value=0, Index=0, Length=400 };
            byte[] buffer=new byte[400]; uint transferred;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,buffer,400,out transferred,IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"WILDX status request failed");
            return XeNpStatus.Parse(buffer,checked((int)transferred));
        }

        internal void RequestRecovery() { VendorOut(0x46,0,0); }

        internal NpCameraStatus GetCameraStatus()
        {
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0xc0,Request=0x53,Length=128};
            var bytes=new byte[128];uint count;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,bytes,128,out count,IntPtr.Zero))
                throw new NotSupportedException("This USB firmware does not provide camera capabilities.");
            return NpCameraStatus.Parse(bytes,checked((int)count));
        }
        internal void StartCameraOnly(ushort rate)
        {
            var caps=GetModeCapabilities();var camera=GetCameraStatus();
            if(GetStatus().Streaming || caps==null || !caps.CameraOnlyActive || !camera.SupportsCameraOnly || !camera.SupportsControls)
                throw new IOException("Select an idle, advertised camera-only USB profile before starting preview.");
            if(rate<1 || rate>58)throw new ArgumentOutOfRangeException("rate");
            VendorOut(0x46,rate,3); // V1: rate in wValue, stream ID in wIndex.
            VendorOut(0x47,USB512WinUsbDevice.StreamMaskCamera,0);
        }
        internal void SetCameraExposure(byte rows,byte gain) { VendorOut(0x4c,rows,gain); }
        internal void SelectCameraOnly()
        {
            var caps=GetModeCapabilities();
            if(GetStatus().Streaming || caps==null || !caps.SupportsCameraOnly)
                throw new IOException("Camera-only selection requires an idle supported device.");
            VendorOut(0x74,0,0);
        }

        // Optional cached firmware evidence, queried only on failed startup.
        // This never sends configuration/I2C/reset requests or reads the ring.
        internal string GetStartupDiagnostic(byte port)
        {
            if(port>2)throw new ArgumentOutOfRangeException("port");
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0xc0,Request=0x76,Value=port,Length=352};
            var data=new byte[352];uint got;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,data,352,out got,IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Startup diagnostic unavailable");
            return DescribeStartupDiagnostic(data,checked((int)got),port);
        }
        internal static string DescribeStartupDiagnostic(byte[] b,int got,byte port)
        {
            if(b==null || got!=352 || b.Length<got || BitConverter.ToUInt32(b,0)!=0x3144504e ||
               BitConverter.ToUInt32(b,4)!=1 || BitConverter.ToUInt32(b,8)!=352 || BitConverter.ToUInt32(b,12)!=port)
                throw new IOException("Invalid NP startup diagnostic");
            var failures=new[]{"none","configuration not ready","ASIC I2C ping failed","identity/configuration apply failed",
                "storage wait","DMA start failed","legacy filesystem failed","capture path fault","run register verification failed",
                "capture sync lock timeout","MCLK fault","runtime capture fault","runtime register verification fault","SD sink failed"};
            var reasons=new[]{"OK","EEPROM absent","EEPROM I/O error","EEPROM unstable","EEPROM invalid","unsupported part number","identity conflict"};
            uint reason=BitConverter.ToUInt32(b,132);
            int end=171;while(end<212 && b[end]!=0 && b[end]!=255)end++;
            string part=System.Text.Encoding.ASCII.GetString(b,171,end-171);
            bool attempted=BitConverter.ToUInt32(b,348)!=0;
            uint lastPort=BitConverter.ToUInt32(b,16);
            string identity=attempted ? "part="+(part.Length==0?"unknown":part)+
                ", identity="+(reason<reasons.Length?reasons[reason]:reason.ToString())+
                ", model="+BitConverter.ToUInt32(b,128)+
                ", EEPROM HAL="+BitConverter.ToUInt32(b,320)+"/"+BitConverter.ToUInt32(b,328)+
                " errors=0x"+BitConverter.ToUInt32(b,324).ToString("X")+"/0x"+BitConverter.ToUInt32(b,332).ToString("X")+
                ", config HAL="+BitConverter.ToUInt32(b,344) : "identity/configuration not attempted";
            string message="P"+(port+1)+": "+identity+
                "; latest fault P"+(b[122]+1)+", stage="+b[121]+
                ", "+(b[123]<failures.Length?failures[b[123]]:"fault "+b[123])+
                ", HAL="+b[124]+", I2C error=0x"+BitConverter.ToUInt32(b,100).ToString("X")+
                ", state=0x"+b[125].ToString("X2")+", rail/pins=0x"+b[126].ToString("X2")+
                ", last configured probe="+(lastPort<3?"P"+(lastPort+1):"none")+", session="+BitConverter.ToUInt32(b,28);
            // Chain receipts are global and belong to the last configured
            // probe. Never present another port's cached result as this one.
            if(attempted && lastPort==port) {
                for(int chain=0;chain<3;chain++)message+="; chain"+(chain+1)+" HAL="+BitConverter.ToUInt32(b,216+24*chain)+
                    ", read HAL="+BitConverter.ToUInt32(b,224+24*chain)+", mismatches="+BitConverter.ToUInt32(b,232+24*chain);
            } else message+="; no chain receipt for this probe";
            return message;
        }
        internal string StartupDiagnosticSummary()
        {
            try {
                var caps=GetModeCapabilities();int mask=caps==null?7:caps.ActiveMask;string result="";
                for(byte p=0;p<3;p++)if((mask&(1<<p))!=0)result+="\r\n"+GetStartupDiagnostic(p);
                return result;
            } catch(Exception ex) {return "\r\nStartup diagnostics unavailable: "+ex.Message;}
        }

        // This multi-packet EP0 request is deliberately idle-only in firmware.
        // The console calls it after STOP so neither USB control processing nor
        // a fresh I2C EEPROM read can perturb capture timing.
        internal NpProbeManifest GetProbeManifest(byte port)
        {
            if(port>2)throw new ArgumentOutOfRangeException("port");
            if(GetStatus().Streaming)throw new IOException("Probe metadata can only be read after recording stops.");
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0xc0,Request=0x72,Value=port,Index=0,Length=NpProbeManifest.WireBytes};
            var buffer=new byte[NpProbeManifest.WireBytes];uint transferred;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,buffer,(uint)buffer.Length,out transferred,IntPtr.Zero)) {
                int error=Marshal.GetLastWin32Error();
                if(error==31 || error==1)throw new NotSupportedException("This firmware does not provide the applied NP probe-metadata receipt.");
                throw new Win32Exception(error,"NP probe-metadata request failed");
            }
            if(transferred!=buffer.Length)throw new IOException("NP probe-metadata receipt was truncated.");
            return NpProbeManifest.Parse(buffer);
        }

        internal byte[] ExchangeNpConfiguration(byte[] request, System.Threading.CancellationToken cancel)
        {
            if(request==null || request.Length!=64)throw new ArgumentException("NP configuration frame must be 64 bytes.");
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {RequestType=0x40,Request=0x70,Length=64};
            uint transferred;
            cancel.ThrowIfCancellationRequested();
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,request,64,out transferred,IntPtr.Zero)||transferred!=64)
                throw new IOException("NP configuration OUT request failed; compatible idle V1 configuration firmware is required.");
            var clock=System.Diagnostics.Stopwatch.StartNew();
            setup.RequestType=0xc0;setup.Request=0x71;
            while(clock.ElapsedMilliseconds<2500){
                cancel.ThrowIfCancellationRequested();var reply=new byte[64];
                if(USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,reply,64,out transferred,IntPtr.Zero)&&transferred==64){
                    try {
                        var r=WildNpConfiguration.NpConfigClient.Parse(reply,request[5],BitConverter.ToUInt32(request,8));
                        if(request[5]!=2 || r.Error!=0 || r.Received==BitConverter.ToUInt32(request,12)+BitConverter.ToUInt32(request,16))return reply;
                    } catch(IOException) { /* a previous response is not an ACK */ }
                }
                System.Threading.Thread.Sleep(20);
            }
            throw new TimeoutException("NP configuration receipt timed out; read status before retrying a commit.");
        }

        internal XeNpSdInfo GetSdInfo()
        {
            var setup=new USB512WinUsbNative.WINUSB_SETUP_PACKET {
                RequestType=0xC0,Request=0x4B,Value=0,Index=0,Length=XeNpSdInfo.WireBytes };
            byte[] buffer=new byte[XeNpSdInfo.WireBytes];uint transferred;
            if(!USB512WinUsbNative.WinUsbControlTransfer(winUsb,setup,buffer,(uint)buffer.Length,out transferred,IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"SD Info request unavailable (firmware support or USB connection)");
            return XeNpSdInfo.Parse(buffer,checked((int)transferred));
        }

        internal XeNpSdInfo ReadSdInfo()
        {
            if(GetStatus().Streaming)throw new IOException("SD Info is blocked while recording.");
            XeNpSdInfo previous=GetSdInfo(); // Capability probe: old firmware is never sent a new OUT command.
            if(previous.State==1)throw new IOException("An SD Info query is already running.");
            ushort token=(ushort)((previous.Sequence%65535)+1);
            VendorOut(0x4C,token,0);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            do {
                System.Threading.Thread.Sleep(100);
                XeNpSdInfo current=GetSdInfo();
                if(current.Sequence!=token)throw new IOException("SD Info session changed or the device restarted; discard this snapshot.");
                if(current.State==2 || current.State==3)return current;
            } while(timer.ElapsedMilliseconds<15000);
            throw new IOException("SD Info timed out. No START, STOP or USB-pipe reset was sent; the firmware may still be finishing its bounded query.");
        }

        internal void Stop()
        {
            if (winUsb == IntPtr.Zero) return;
            deferredAdmissionSession=0;
            try { VendorOut(RequestStop, 0, 0); }
            finally {
                if(!USB512WinUsbNative.WinUsbAbortPipe(winUsb, bulkInPipe))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"NP bulk reads did not abort after STOP; recording is incomplete.");
            }
        }

        internal int Read(byte[] buffer)
        {
            CompleteStartupAdmission();
            uint transferred;
            if (USB512WinUsbNative.WinUsbReadPipe(winUsb, bulkInPipe, buffer, (uint)buffer.Length, out transferred, IntPtr.Zero)) {
                if(transferred>0 && awaitingFirstSynchronousSample) {
                    uint steadyTimeout=2000;
                    if(!USB512WinUsbNative.WinUsbSetPipePolicy(winUsb,bulkInPipe,PipeTransferTimeout,4,ref steadyTimeout))
                        throw new Win32Exception(Marshal.GetLastWin32Error(),"NP streaming timeout policy failed");
                    awaitingFirstSynchronousSample=false;
                }
                return (int)transferred;
            }
            int error = Marshal.GetLastWin32Error();
            if (error == USB512WinUsbNative.ErrorSemTimeout)
                throw new IOException(awaitingFirstSynchronousSample ?
                    "No NP data within the 10-second startup allowance. Check probe configuration; no automatic restart was attempted."+StartupDiagnosticSummary() :
                    "NP USB read timed out; recording stopped to preserve continuity.");
            if (error == USB512WinUsbNative.ErrorOperationAborted)
                return 0;
            throw new Win32Exception(error, "WILDX WinUsb_ReadPipe failed");
        }

        // Reuse the console's tested multi-URB reader.  Keeping several IN
        // requests armed prevents a scheduler pause between synchronous reads
        // from starving the HS endpoint once real probe data is available.
        internal USB512WinUsbQueuedReader CreateQueuedReader(int bufferBytes, int depth, bool rawIo=false)
        {
            if (winUsb == IntPtr.Zero) throw new ObjectDisposedException("XeNpWinUsbDevice");
            // Software polling is non-destructive. A native timeout can cancel
            // a partially filled request and invalidate all subsequent bytes.
            uint timeout=0;
            if(!USB512WinUsbNative.WinUsbSetPipePolicy(winUsb,bulkInPipe,PipeTransferTimeout,4,ref timeout))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"WILDX queued read policy failed");
            if(rawIo) {
                uint size=4,maximum;
                if(!GetPipePolicy(winUsb,bulkInPipe,8,ref size,out maximum) || size!=4)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot query maximum USB transfer");
                USB512WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
                if(!USB512WinUsbNative.WinUsbQueryInterfaceSettings(winUsb,0,out descriptor))throw new IOException("No interface descriptor");
                uint packet=0;
                for(byte i=0;i<descriptor.bNumEndpoints;i++) {
                    USB512WinUsbNative.WINUSB_PIPE_INFORMATION info;
                    if(USB512WinUsbNative.WinUsbQueryPipe(winUsb,0,i,out info) && info.PipeId==bulkInPipe)packet=info.MaximumPacketSize;
                }
                if(packet==0 || bufferBytes%packet!=0 || bufferBytes>maximum)throw new IOException("Invalid RAW_IO transfer size");
                byte clear=0,raw=1;
                if(!SetBooleanPolicy(winUsb,bulkInPipe,2,1,ref clear) || !SetBooleanPolicy(winUsb,bulkInPipe,7,1,ref raw))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"RAW_IO configuration failed");
            }
            queuedCapture=true;
            return new USB512WinUsbQueuedReader(winUsb, bulkInPipe, bufferBytes, depth,
                null, null, null, true);
        }

        private void VendorOut(byte request, ushort value, ushort index)
        {
            USB512WinUsbNative.WINUSB_SETUP_PACKET setup = new USB512WinUsbNative.WINUSB_SETUP_PACKET
            {
                RequestType = 0x40, Request = request, Value = value, Index = index, Length = 0
            };
            uint transferred;
            if (!USB512WinUsbNative.WinUsbControlTransfer(winUsb, setup, new byte[0], 0, out transferred, IntPtr.Zero))
                throw new IOException("WILDX vendor request failed: " + Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            if (winUsb != IntPtr.Zero)
            {
                USB512WinUsbNative.WinUsbFree(winUsb);
                winUsb = IntPtr.Zero;
            }
            handle.Dispose();
        }
    }

    internal sealed class XeNpStatus
    {
        internal uint Version, RecordingFault, McoDivider, PsbClockHz, LastStatus;
        internal bool SupportsFreshStart;
        internal uint SessionId, PreviousSessionFault, SessionResetCount;
        internal bool UsesV1SynchronousReads { get { return Version<8; } }
        internal int ApSampleRateHz=15000;
        internal bool Streaming;
        internal XeNpLaneLayout Layout;
        internal bool IsTransientStartupClock {get{return !Streaming && Version>=5 && Version<8 &&
            McoDivider==4 && PsbClockHz==7800000 && ApSampleRateHz==10000;}}
        internal static XeNpStatus Parse(byte[] data,int count)
        {
            if(data==null || count<76 || count>data.Length || BitConverter.ToUInt32(data,0)!=0x55534253)
                throw new IOException("Invalid/truncated WILDX diagnostic status.");
            var s=new XeNpStatus { Version=BitConverter.ToUInt32(data,4), Streaming=BitConverter.ToUInt32(data,20)!=0 };
            s.LastStatus=BitConverter.ToUInt32(data,72);
            if(s.Version<7 && !s.Streaming && (s.LastStatus&0xFFFF0000U)==0xBADA0000U)
                s.RecordingFault=s.LastStatus;
            if(s.Version>=5 && count>=280) {
                s.McoDivider=BitConverter.ToUInt32(data,260);s.PsbClockHz=BitConverter.ToUInt32(data,268);
                if(s.PsbClockHz>0) {
                    if(s.PsbClockHz%780!=0)throw new IOException("Malformed NP1 diagnostic PSB clock.");
                    s.ApSampleRateHz=checked((int)(s.PsbClockHz/780));
                }
            }
            if(s.Version==5 && count>=328 && BitConverter.ToUInt32(data,304)==0x31525356U) {
                s.SupportsFreshStart=(BitConverter.ToUInt32(data,308)&1)!=0;
                s.SessionId=BitConverter.ToUInt32(data,312);
                s.PreviousSessionFault=BitConverter.ToUInt32(data,316);
                s.SessionResetCount=BitConverter.ToUInt32(data,320);
                uint latched=BitConverter.ToUInt32(data,324);
                if(latched!=0)s.RecordingFault=latched;
            }
            if(s.Version>=7) {
                if(count<336)throw new IOException("Truncated WILDX recording-fault status.");
                s.RecordingFault=BitConverter.ToUInt32(data,332);
            }
            if(s.Version>=8) {
                if(count<344)throw new IOException("Truncated WILDX capture profile.");
                uint layout=BitConverter.ToUInt32(data,336);
                if(layout!=1 && layout!=2)throw new IOException("Unsupported WILDX lane layout.");
                s.Layout=(XeNpLaneLayout)layout;s.ApSampleRateHz=checked((int)BitConverter.ToUInt32(data,340));
            }
            // A stopped V1 can report its temporary native20 bring-up clock.
            // It is diagnostic evidence, never a supported recording mode.
            if(NpCaptureMode.RateBit(s.ApSampleRateHz)==0 && !s.IsTransientStartupClock)
                throw new IOException("Unsupported NP1 sample rate: "+s.ApSampleRateHz);
            if(s.Streaming && s.PsbClockHz>0 && s.PsbClockHz/780!=s.ApSampleRateHz)
                throw new IOException("Streaming NP1 rate disagrees with the diagnostic PSB clock.");
            return s;
        }
        internal void RequireCaptureProfile(int expectedHz,XeNpLaneLayout expectedLayout)
        {
            if(NpCaptureMode.RateBit(expectedHz)==0 || NpCaptureMode.RateBit(ApSampleRateHz)==0 ||
               ApSampleRateHz!=expectedHz || Layout!=expectedLayout ||
               (PsbClockHz>0 && PsbClockHz/780!=ApSampleRateHz) ||
               (Layout==XeNpLaneLayout.Pssi && ApSampleRateHz!=30000))
                throw new IOException("Actual READY NP1 profile differs from the expected supported capture rate/layout: "+
                    ApSampleRateHz+" Hz, "+Layout+"; expected "+expectedHz+" Hz, "+expectedLayout+". Capture was not released.");
        }
        internal void ThrowIfFault()
        {
            if((RecordingFault&0xFFFF0000U)==0xBADA0000U)
                throw new IOException("V1 USB exporter fault is latched (0x"+RecordingFault.ToString("X8")+"). Saved data is incomplete. "+
                    (SupportsFreshStart?"Press Start for a NEW recording; the failed file will not be continued.":"Reset/power-cycle the board before starting another recording (this firmware lacks USB session recovery)."));
            if(RecordingFault==1)throw new IOException("Firmware capture backlog overflow (continuity fault 1); saved data is incomplete. USB draining fell behind acquisition.");
            if(RecordingFault!=0)throw new IOException("Firmware recording continuity fault "+RecordingFault+"; saved data is incomplete.");
        }
        internal void RequireStopped()
        {
            ThrowIfFault();
            if(Streaming)throw new IOException("NP STOP was not confirmed; firmware is still streaming and the recording is incomplete.");
        }
    }

    internal sealed class XeNpPacketParser
    {
        // The active XE_NP firmware deliberately uses USB512's compact framing:
        // A512 magic, stream id 6, flags, and a 16-bit sequence/short length.
        private const int HeaderBytes = 6;
        private const int PacketBytes = 512;
        private const int PayloadBytes = PacketBytes - HeaderBytes;
        private const ushort Magic = 0xA512;
        private const byte NeuropixelsStreamId = 6;
        private const byte FlagPayloadLengthInSequence = 0x04;
        private const byte QuadFlags = 0x60;
        private byte[] buffer = new byte[32768];
        private int stored;
        private bool haveSequence, failed;
        private ushort expectedSequence;
        private bool haveMiscSequence;
        private ushort expectedMiscSequence;
        internal Action<byte[],int,int> OnMisc;
        internal Action<byte[],int,int,byte,ushort> OnCamera;
        internal long MiscPackets { get; private set; }
        private bool transportKnown, quadTransport;
        private int quadLane;
        private readonly byte[] quadLanes=new byte[4*PayloadBytes];
        private readonly byte[] chronological=new byte[4*PayloadBytes];
        internal bool IsQuadInterleaved { get { return transportKnown && quadTransport; } }
        internal long Packets { get; private set; }
        internal long SequenceGaps { get; private set; }
        internal int PendingBytes { get { return stored; } }
        internal int PendingLanePackets { get { return quadLane; } }
        internal void Complete()
        {
            if(failed)throw new IOException("WILDX packet parser previously failed.");
            if(stored!=0 || quadLane!=0)throw new IOException("Capture ended with an incomplete USB packet or four-lane group.");
        }

        internal void Feed(byte[] input, int count, Action<byte[]> onPacket)
        {
            Feed(input,count,delegate(byte[] bytes,int offset,int length) {
                byte[] copy=new byte[length];Buffer.BlockCopy(bytes,offset,copy,0,length);onPacket(copy);
            });
        }

        // Slice is valid only during the callback; the recording worker consumes
        // it synchronously. Avoid one allocation per 506-byte USB packet.
        internal void Feed(byte[] input,int count,Action<byte[],int,int> onPacket)
        {
            if(failed)throw new IOException("WILDX packet parser previously failed.");
            if(input==null || count<0 || count>input.Length || count>16*1024*1024)throw new ArgumentOutOfRangeException("count");
            int length=checked(stored+count);
            if(buffer.Length<length)Array.Resize(ref buffer,length);
            Buffer.BlockCopy(input,0,buffer,stored,count);
            int offset = 0;
            while (length - offset >= HeaderBytes)
            {
                if (BitConverter.ToUInt16(buffer, offset) != Magic ||
                    (buffer[offset + 2] != NeuropixelsStreamId && buffer[offset + 2] != 5 && buffer[offset + 2] != 3))
                {
                    failed=true;throw new IOException("WILDX USB packet framing lost; refusing silent resynchronization.");
                }
                byte flags = buffer[offset + 3];
                if(buffer[offset+2]==3) {
                    try {
                        int cameraBytes=NpCameraRecording.PayloadLength(flags);
                        if(length-offset<HeaderBytes+cameraBytes)break;
                        if(OnCamera==null)throw new IOException("NP camera data has no recorder attached.");
                        OnCamera(buffer,offset+HeaderBytes,cameraBytes,flags,BitConverter.ToUInt16(buffer,offset+4));
                        offset+=HeaderBytes+cameraBytes;continue;
                    } catch {failed=true;throw;}
                }
                if(buffer[offset+2]==5) {
                    if(flags!=8 || quadLane!=0) {
                        failed=true;throw new IOException("Invalid NP miscellaneous packet flags or lane boundary.");
                    }
                    if(length-offset<PacketBytes)break;
                    ushort miscSequence=BitConverter.ToUInt16(buffer,offset+4);
                    if(haveMiscSequence && miscSequence!=expectedMiscSequence) {
                        failed=true;SequenceGaps++;throw new IOException("NP miscellaneous USB sequence gap.");
                    }
                    expectedMiscSequence=unchecked((ushort)(miscSequence+1));haveMiscSequence=true;
                    try {
                        if(OnMisc==null)throw new IOException("NP firmware sends miscellaneous data but no recorder is attached.");
                        OnMisc(buffer,offset+HeaderBytes,PayloadBytes);MiscPackets++;
                    } catch {failed=true;throw;}
                    offset+=PacketBytes;continue;
                }
                bool quad=flags==QuadFlags;
                if(!quad && (flags & ~FlagPayloadLengthInSequence)!=0) {
                    failed=true;throw new IOException("Unsupported NP1 packet flags/lane interleaving.");
                }
                int payloadLength = (flags & FlagPayloadLengthInSequence) != 0 ?
                    BitConverter.ToUInt16(buffer, offset + 4) : PayloadBytes;
                if (payloadLength < 0 || payloadLength > PayloadBytes)
                {
                    failed=true;throw new IOException("Invalid WILDX payload length.");
                }
                int packetLength = HeaderBytes + payloadLength;
                if (length - offset < packetLength) break;
                if(transportKnown && quad!=quadTransport) {
                    failed=true;throw new IOException("WILDX packet lane layout changed mid-recording.");
                }
                if(!transportKnown) { quadTransport=quad;transportKnown=true; }
                if((flags & FlagPayloadLengthInSequence)==0) {
                    ushort seq=BitConverter.ToUInt16(buffer,offset+4);
                    if(quad && (seq&3)!=quadLane) {
                        failed=true;throw new IOException("WILDX quad lane order is invalid; refusing a shifted reconstruction.");
                    }
                    if(haveSequence && seq!=expectedSequence) {
                        failed=true;SequenceGaps++;throw new IOException("WILDX USB packet sequence gap; recording is incomplete.");
                    }
                    expectedSequence=unchecked((ushort)(seq+1));haveSequence=true;
                } else haveSequence=false; // Length replaces sequence in this legacy wire record.
                try {
                    Packets++;
                    if(quad) {
                        Buffer.BlockCopy(buffer,offset+HeaderBytes,quadLanes,quadLane*PayloadBytes,PayloadBytes);
                        if(++quadLane==4) {
                            for(int i=0;i<PayloadBytes;i++) {
                                int at=i*4;
                                chronological[at]=quadLanes[i];
                                chronological[at+1]=quadLanes[PayloadBytes+i];
                                chronological[at+2]=quadLanes[2*PayloadBytes+i];
                                chronological[at+3]=quadLanes[3*PayloadBytes+i];
                            }
                            quadLane=0;onPacket(chronological,0,chronological.Length);
                        }
                    } else onPacket(buffer,offset+HeaderBytes,payloadLength);
                }
                catch { failed=true;throw; }
                offset += packetLength;
            }
            stored=length-offset;
            if(stored!=0)Buffer.BlockCopy(buffer,offset,buffer,0,stored);
        }
    }
}
