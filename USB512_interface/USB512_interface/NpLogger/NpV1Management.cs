using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpV1Target
    {
        internal string Path, Uid;
        internal bool Recovery;
        public override string ToString() { return "NP V1 " + Uid + (Path==null ? " — reconnect / DFU" : Recovery ? " — recovery" : " — USB service"); }
    }

    // V1 split protocol only. Never use the generic DFU/V2 updater for an NP V1 app.
    internal sealed class NpV1Management : IDisposable
    {
        private SafeFileHandle file;
        private IntPtr usb;
        private readonly NpV1Target target;
        internal const uint Abi=257, AppBase=0x08020000, Capacity=0x15e000;
        internal sealed class Status
        {
            internal bool Split, Recovery, Download, AppValid;
            internal uint Role, Busy, LoaderAbi, Capabilities, PendingMode;
            internal string Description;
        }
        internal static string UidFromPath(string path)
        {
            var match=Regex.Match(path??"",@"usb#vid_1209&pid_(?:b386|b387)#npv1-([0-9a-f]{24})#",RegexOptions.IgnoreCase);
            return match.Success?match.Groups[1].Value.ToUpperInvariant():null;
        }
        internal static List<NpV1Target> Enumerate()
        {
            var result=new List<NpV1Target>();
            foreach(var p in NpDownloadNative.EnumerateInterfacePaths(NpDownloadNative.NeuropixelsInterfaceGuid))
            {
                var m=Regex.Match(p,@"usb#vid_1209&pid_(b386|b387)#npv1-([0-9a-f]{24})#",RegexOptions.IgnoreCase);
                if(m.Success)result.Add(new NpV1Target{Path=p,Uid=m.Groups[2].Value.ToUpperInvariant(),Recovery=m.Groups[1].Value.Equals("b387",StringComparison.OrdinalIgnoreCase)});
            }
            return result;
        }
        // A cold-booted unified image is intentionally in read-only download
        // mode. Switch only the explicitly selected UID, before opening a
        // capture handle or issuing START. Never substitute another board.
        internal static string EnsureStreaming(string selectedPath)
        {
            var match=Regex.Match(selectedPath??"",@"#npv1-([0-9a-f]{24})#",RegexOptions.IgnoreCase);
            if(!match.Success)return selectedPath; // Non-V1/legacy capture path.
            string uid=match.Groups[1].Value.ToUpperInvariant();
            var selected=Enumerate().Find(t=>!t.Recovery && t.Path.Equals(selectedPath,StringComparison.OrdinalIgnoreCase));
            if(selected==null)throw new IOException("Selected V1 is no longer connected. Refresh the device list.");
            using(var manager=new NpV1Management(selected)) {
                var state=manager.ReadStatus();
                if(!state.Split || state.Busy!=0 || state.PendingMode!=0)throw new IOException("Selected V1 must be idle before USB recording.");
                if(state.Role==2)return selectedPath;
                if(state.Role!=3)throw new IOException("Selected V1 is not in a supported USB mode.");
                manager.SetMode(2);
            }
            return WaitForStreaming(uid);
        }
        internal static string WaitForStreaming(string uid)
        { return WaitForUsbMode(uid,2,null); }
        internal static string EnsureDownload(string selectedPath,Func<bool> cancelled=null)
        {
            string uid=UidFromPath(selectedPath);if(uid==null)return selectedPath;
            if(cancelled!=null && cancelled())throw new OperationCanceledException();
            var selected=Enumerate().Find(t=>!t.Recovery && t.Path.Equals(selectedPath,StringComparison.OrdinalIgnoreCase));
            if(selected==null)throw new IOException("Selected V1 is no longer connected. Refresh sources.");
            using(var manager=new NpV1Management(selected)){
                var state=manager.ReadStatus();
                if(state.Busy!=0 || state.PendingMode!=0)throw new IOException("Stop recording and finish the active V1 operation before downloading.");
                if(state.Download)return selectedPath;
                if(!state.Split || state.Role!=2)throw new IOException("Selected V1 cannot enter USB download mode from its current mode.");
                if(cancelled!=null && cancelled())throw new OperationCanceledException();
                manager.SetMode(3);
                Thread.Sleep(150); // Keep the acknowledged handle alive through deferred mode entry.
            }
            return WaitForUsbMode(uid,3,cancelled);
        }
        static string WaitForUsbMode(string uid,ushort mode,Func<bool> cancelled)
        {
            if(!Regex.IsMatch(uid??"",@"\A[0-9a-fA-F]{24}\z"))
                throw new IOException("A selected V1 UID is required before waiting for its USB mode.");
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            while(elapsed.ElapsedMilliseconds<30000) {
                if(cancelled!=null && cancelled())throw new OperationCanceledException();
                // Allow the 50 ms ACK gate and 100 ms detach before reopening.
                System.Threading.Thread.Sleep(250);
                var found=Enumerate().FindAll(t=>!t.Recovery && t.Uid.Equals(uid,StringComparison.OrdinalIgnoreCase));
                if(found.Count>1)throw new IOException("Ambiguous V1 UID after mode switch.");
                if(found.Count==0)continue;
                try {using(var manager=new NpV1Management(found[0])) {
                    var state=manager.ReadStatus();
                    if(state.Split && state.Role==mode && state.Busy==0 && state.PendingMode==0)return found[0].Path;
                }} catch(Win32Exception) { /* Device is detaching/re-enumerating. */ }
            }
            throw new IOException("Selected V1 did not return to idle USB "+(mode==2?"streaming":"download")+" within 30 seconds. No recording or download was started.");
        }
        internal static string SwitchMode(NpV1Target target,ushort mode)
        {
            using(var manager=new NpV1Management(target)){
                bool inPlace=UsesInPlaceTransition(manager.ReadStatus(),mode);
                manager.SetMode(mode);
                if(inPlace)return "Mode verified on the same USB interface: "+manager.WaitForInPlaceMode(mode).Description;
            }
            if(mode==1)return "SD / BLE mode acknowledged. USB disconnects; recording has not been verified.";
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<20){
                var matching=Enumerate().FindAll(device=>device.Uid==target.Uid && device.Recovery==(mode==4));
                if(matching.Count==1)try{using(var manager=new NpV1Management(matching[0])){
                    var state=manager.ReadStatus();
                    if(state.Split && state.Role==mode && state.Busy==0 && state.PendingMode==0)return "Mode verified: "+state.Description;
                }}catch(Win32Exception){}catch(IOException){}
                Thread.Sleep(250);
            }
            throw new IOException("Mode acknowledged, but target role did not enumerate within 20 seconds. Refresh; do not assume entry succeeded.");
        }
        internal NpV1Management(NpV1Target selected)
        {
            var matching=selected==null?new List<NpV1Target>():Enumerate().FindAll(t=>String.Equals(t.Uid,selected.Uid,StringComparison.OrdinalIgnoreCase));
            if(matching.Count!=1 || !matching[0].Path.Equals(selected.Path,StringComparison.OrdinalIgnoreCase) || matching[0].Recovery!=selected.Recovery)
                throw new IOException("Selected V1 UID is no longer uniquely enumerated. Refresh; another board will not be substituted.");
            target=selected;
            file=NpDownloadNative.CreateFile(target.Path,0xc0000000,3,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
            try {
                if(file.IsInvalid || !NpDownloadNative.WinUsbInitialize(file,out usb))throw Error();
                byte autoSuspend=0;
                if(!NpDownloadNative.WinUsbSetPowerPolicy(usb,0x81,1,ref autoSuspend))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"V1 cannot disable WinUSB selective suspend");
                uint timeout=30000;
                if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x81,3,4,ref timeout))throw Error();
                if(target.Recovery && !NpDownloadNative.WinUsbSetPipePolicy(usb,0x01,3,4,ref timeout))throw Error();
            } catch {Dispose();throw;}
        }
        static Win32Exception Error(){return new Win32Exception(Marshal.GetLastWin32Error());}
        static uint U(byte[] b,int at){return NpDownloadStream.U(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        static uint Crc(byte[] b,int at,int count){return NpDownloadStream.Crc(b,at,count);}
        private byte[] Control(byte request,ushort value,int length)
        {
            var b=new byte[length];uint n;
            var setup=new NpDownloadNative.WINUSB_SETUP_PACKET{RequestType=(byte)(length==0?0x40:0xc0),Request=request,Value=value,Length=(ushort)length};
            if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,b,(uint)length,out n,IntPtr.Zero))throw Error();
            if(n!=length)throw new IOException("Short V1 control reply.");return b;
        }
        internal Status ReadStatus()
        {
            if(target.Recovery) {
                var b=Transfer(1,1);uint loaderAbi=CheckRecovery(b);
                bool valid=(U(b,16)&1)!=0;
                return new Status{Split=true,Recovery=true,AppValid=valid,Role=4,LoaderAbi=loaderAbi,Description="V1 recovery ABI "+loaderAbi+"; SD app "+(valid?"valid":"INVALID / missing")};
            }
            byte[] state=null;
            try {state=Control(0x6e,0,32);}
            catch(Win32Exception error) {
                if(error.NativeErrorCode!=1 && error.NativeErrorCode!=31)throw;
                // Legacy standalone firmware stalls this optional request.
            }
            if(state!=null) {
                return ParseSplitStatus(state,target.Uid);
            }
            var info=Control(0x60,0,32);
            if(U(info,0)!=0x3144504e || U(info,4)!=1 || U(info,28)!=1)throw new InvalidDataException("Not a supported read-only NP SD download service.");
            return new Status{Download=true,Role=3,Busy=U(info,16),Description="Standalone SD download firmware. Split mode switching / app update unavailable."};
        }
        internal static Status ParseSplitStatus(byte[] state,string uid)
        {
            if(state==null || state.Length!=32 || !Regex.IsMatch(uid??"",@"\A[0-9a-fA-F]{24}\z") ||
               U(state,0)!=0x3153504e || !SupportedAbi(U(state,4)) ||
               (U(state,8)!=2 && U(state,8)!=3 && !(U(state,8)==4 && (U(state,12)&64U)!=0)) || (U(state,12)&15U)!=15U || U(state,16)>1 ||
               U(state,20)>5 || (U(state,20)==5 && (U(state,12)&16U)==0))
                throw new InvalidDataException("Unsupported split service identity/ABI/capabilities.");
            uint uid0=Convert.ToUInt32(uid.Substring(0,8),16),uid1=Convert.ToUInt32(uid.Substring(8,8),16),uid2=Convert.ToUInt32(uid.Substring(16,8),16);
            if(U(state,24)!=uid0 || U(state,28)!=(uid1^uid2))throw new InvalidDataException("V1 UID/status mismatch.");
            uint role=U(state,8),busy=U(state,16),pending=U(state,20),capabilities=U(state,12);
            return new Status{Split=true,Role=role,Busy=busy,Download=role==3,Capabilities=capabilities,PendingMode=pending,
                Description="V1 USB ABI "+U(state,4)+"; "+(role==4?"APP update":role==3?"SD download":"USB streaming")+"; busy="+busy+
                    "; pending mode="+pending+"; APP update "+((capabilities&64U)!=0?"on this USB interface":"via recovery")+
                    "; ROM DFU "+((capabilities&16U)!=0?"advertised":"unavailable")};
        }
        internal static bool CanUpdateApp(Status status)
        {
            return status!=null && status.Split && status.Busy==0 && status.PendingMode==0 &&
                ((status.Recovery && status.Role==4) || (!status.Recovery && (status.Capabilities&64U)!=0 && status.Role>=2 && status.Role<=4));
        }
        internal static bool CanAbortAppUpdate(Status status)
        {
            return status!=null && status.Split && !status.Recovery && status.Role==4 &&
                (status.Capabilities&64U)!=0 && status.PendingMode==0;
        }
        internal static bool UsesInPlaceTransition(Status status,ushort mode)
        {
            return status!=null && status.Split && !status.Recovery && (status.Capabilities&64U)!=0 &&
                mode==4 && status.Role>=2 && status.Role<=4;
        }
        internal static bool CanEnterRomDfu(Status status)
        {
            return status!=null && status.Split && !status.Recovery && (status.Role==2 || status.Role==3) &&
                status.Busy==0 && status.PendingMode==0 && (status.Capabilities&16U)!=0;
        }
        internal static void RequireMode(Status status,ushort mode)
        {
            if(mode<1 || mode>5)throw new ArgumentOutOfRangeException("mode");
            if(status==null || !status.Split || status.Busy!=0 || status.PendingMode!=0)
                throw new IOException("Mode switching requires an idle V1 split service with no pending transition.");
            if(mode==5 && !CanEnterRomDfu(status))
                throw new IOException("Selected firmware does not advertise guarded ROM DFU. No mode request was sent.");
        }
        internal void SetMode(ushort mode)
        {
            if(mode<1 || mode>5)throw new ArgumentOutOfRangeException("mode");
            var status=ReadStatus();
            RequireMode(status,mode); // Capability and idle checks precede every OUT request.
            if(status.Recovery) {if(mode!=4)Transfer(mode==1?(ushort)6:mode==2?(ushort)7:(ushort)10,2);}
            else Control(0x6f,mode,0);
        }
        internal Status WaitForInPlaceMode(ushort mode)
        {
            if(mode<2 || mode>4)throw new ArgumentOutOfRangeException("mode");
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            while(elapsed.ElapsedMilliseconds<3000) {
                var state=ReadStatus();
                if(state.Recovery || (state.Capabilities&64U)==0)throw new IOException("Same-interface APP update capability was lost. No device substitution was attempted.");
                if(state.Role==mode && state.Busy==0 && state.PendingMode==0)return state;
                System.Threading.Thread.Sleep(20);
            }
            throw new IOException("Same-interface mode entry was not verified. Read status before proceeding.");
        }
        private void SetUpdateTimeout()
        {
            uint timeout=30000;
            if(!NpDownloadNative.WinUsbSetPipePolicy(usb,0x01,3,4,ref timeout))throw Error();
        }
        internal static byte[] ValidatePackage(string path)
        {
            var length=new FileInfo(path).Length;
            bool hex=String.Equals(Path.GetExtension(path),".hex",StringComparison.OrdinalIgnoreCase);
            if(hex ? length<11 || length>8*1024*1024 : length<136 || length>128+Capacity)
                throw new InvalidDataException("Invalid V1 application image length.");
            var b=hex?ReadAppHex(path):File.ReadAllBytes(path);
            return ValidateAppBytes(b);
        }
        internal static byte[] ValidateAppBytes(byte[] b)
        {
            if(b==null || b.Length<136 || b.Length>128+Capacity)throw new InvalidDataException("Invalid V1 application image length.");
            if(BitConverter.ToUInt64(b,0)!=0x31464e4d31504e58UL || U(b,8)!=1 || U(b,12)!=0x56414c44 || U(b,16)!=0x41505031 || U(b,24)!=AppBase || U(b,28)!=b.Length-128 || !SupportedAbi(U(b,80)) || U(b,124)!=Crc(b,0,124) || U(b,32)!=Crc(b,128,b.Length-128))
                throw new InvalidDataException("Invalid V1 SD app package: signature, ABI, partition or CRC mismatch.");
            uint sp=U(b,128),pc=U(b,132);
            bool ram=(sp>0x20000000 && sp<=0x20270000)||(sp>0x28000000 && sp<=0x28004000);
            if(!ram || (sp&7)!=0 || (pc&1)==0 || (pc&~1U)<AppBase || (pc&~1U)>=AppBase+U(b,28) || sp!=U(b,36) || pc!=U(b,40))
                throw new InvalidDataException("V1 app vector validation failed.");return b;
        }
        // Strict addressed APP-only input. Never reinterpret a factory or USB
        // HEX as a raw application and never repair missing/duplicate records.
        private static byte[] ReadAppHex(string path)
        {
            var memory=new Dictionary<uint,byte>();uint upper=0;uint? start=null;bool eof=false;
            foreach(string raw in File.ReadLines(path)) {
                string line=raw.Trim();if(line.Length==0)continue;
                if(eof || line.Length<11 || line[0]!=':')throw new InvalidDataException("Malformed HEX or records after EOF.");
                int count=HexByte(line,1);
                if(line.Length!=1+2*(count+5))throw new InvalidDataException("HEX record length mismatch.");
                var record=new byte[count+5];int checksum=0;
                for(int i=0;i<record.Length;i++){record[i]=(byte)HexByte(line,1+2*i);checksum+=record[i];}
                if((checksum&255)!=0)throw new InvalidDataException("HEX record checksum mismatch.");
                uint offset=(uint)(record[1]*256+record[2]);int kind=record[3];
                if(kind==0) {
                    for(int i=0;i<count;i++) {
                        ulong full=(ulong)upper+offset+(uint)i;
                        if(full>UInt32.MaxValue)throw new InvalidDataException("HEX address overflow.");
                        uint address=(uint)full;
                        if(!((address>=0x0801e000 && address<0x0801e080) || (address>=AppBase && address<AppBase+Capacity)))
                            throw new InvalidDataException("Select the APP-only HEX. Factory, USB and combined images cannot use the protected APP updater.");
                        if(memory.ContainsKey(address))throw new InvalidDataException("Duplicate HEX address.");
                        memory.Add(address,record[4+i]);
                    }
                } else if(kind==1) {
                    if(count!=0 || offset!=0)throw new InvalidDataException("Invalid HEX EOF.");eof=true;
                } else if(kind==2 || kind==4) {
                    if(count!=2 || offset!=0)throw new InvalidDataException("Invalid HEX address record.");
                    upper=(uint)(record[4]*256+record[5])<<(kind==2?4:16);
                } else if(kind==5) {
                    if(count!=4 || offset!=0 || start.HasValue)throw new InvalidDataException("Invalid HEX start record.");
                    start=((uint)record[4]<<24)|((uint)record[5]<<16)|((uint)record[6]<<8)|record[7];
                } else throw new InvalidDataException("Unsupported HEX record type.");
            }
            if(!eof)throw new InvalidDataException("HEX EOF missing.");
            var manifest=new byte[128];byte value;
            for(uint i=0;i<128;i++) {
                if(!memory.TryGetValue(0x0801e000+i,out value))throw new InvalidDataException("APP manifest is incomplete.");
                manifest[i]=value;
            }
            uint size=U(manifest,28);
            if(size<8 || size>Capacity || memory.Count!=128+size)throw new InvalidDataException("HEX application extent does not match its manifest.");
            var result=new byte[128+(int)size];Buffer.BlockCopy(manifest,0,result,0,128);
            for(uint i=0;i<size;i++) {
                if(!memory.TryGetValue(AppBase+i,out value))throw new InvalidDataException("HEX application has a missing byte.");
                result[128+i]=value;
            }
            if(start.HasValue && (start.Value&~1U)!=(U(manifest,40)&~1U))throw new InvalidDataException("HEX start address does not match the APP reset vector.");
            return result;
        }
        internal static int SelectionAfterRefresh(IList<NpV1Target> targets,string previousUid)
        {
            if(targets==null)return -1;
            if(String.IsNullOrEmpty(previousUid))return targets.Count==1?0:-1;
            int index=-1;
            for(int i=0;i<targets.Count;i++)if(String.Equals(targets[i].Uid,previousUid,StringComparison.OrdinalIgnoreCase)) {
                if(index!=-1)return -1; // A transitional/ambiguous UID is not an update target.
                index=i;
            }
            return index; // Never auto-select a neighbor when the selected logger detaches.
        }
        private static int HexByte(string line,int at)
        {
            int value=0;
            for(int i=0;i<2;i++) {
                char c=line[at+i];int digit=c>='0'&&c<='9'?c-'0':c>='A'&&c<='F'?c-'A'+10:c>='a'&&c<='f'?c-'a'+10:-1;
                if(digit<0)throw new InvalidDataException("Non-hexadecimal HEX record.");value=value*16+digit;
            }
            return value;
        }
        private static bool SupportedAbi(uint abi){return abi==257 || abi==258;}
        internal static void RequireCompatibleLoader(byte[] image,uint loaderAbi)
        {
            if(!SupportedAbi(loaderAbi) || image==null || image.Length<136 || U(image,80)>loaderAbi)
                throw new InvalidDataException("Application requires a newer or unsupported recovery loader. No erase was requested.");
        }
        internal ushort Update(string packagePath,Action<int> progress)
        {
            return UpdateBytes(ValidatePackage(packagePath),progress);
        }
        internal ushort UpdateBytes(byte[] image,Action<int> progress)
        {
            var b=ValidateAppBytes((byte[])image.Clone()); // Freeze and validate before BEGIN erases the app partition.
            var state=ReadStatus();
            if(!CanUpdateApp(state))throw new IOException("APP update requires idle recovery, or a stopped service advertising same-interface APP updates. No erase was requested.");
            ushort returnMode=(ushort)(!state.Recovery && state.Role!=4?state.Role:0);
            if(!state.Recovery) {
                if(state.Role!=4)SetMode(4);
                WaitForInPlaceMode(4); // The selected handle/path/UID stays open; never wait for B387.
            }
            SetUpdateTimeout();
            uint loaderAbi=CheckRecovery(Transfer(1,1));
            RequireCompatibleLoader(b,loaderAbi); // Must precede BEGIN/erase.
            uint seq=2;
            var id=new byte[32];Buffer.BlockCopy(b,44,id,0,32);
            Transfer(2,seq++,(uint)b.Length-128,U(b,32),id);
            for(int at=128;at<b.Length;) {
                int n=Math.Min(480,b.Length-at);var payload=new byte[n];Buffer.BlockCopy(b,at,payload,0,n);
                var reply=Transfer(3,seq++,(uint)at-128,0,payload);at+=n;
                if(U(reply,16)!=at-128)throw new IOException("App write offset mismatch.");
                if(progress!=null)progress((at-128)*100/(b.Length-128));
            }
            Transfer(4,seq++);var status=Transfer(1,seq);CheckRecovery(status);
            if((U(status,16)&1)==0)throw new IOException("Committed APP did not validate. APP update mode remains active.");
            if(returnMode!=0) {
                WaitForInPlaceMode(4);SetMode(returnMode);WaitForInPlaceMode(returnMode);
            }
            return returnMode==0?(ushort)4:returnMode;
        }
        internal ushort AbortAppUpdate(ushort returnMode)
        {
            if(returnMode!=0 && returnMode!=2 && returnMode!=3)throw new ArgumentOutOfRangeException("returnMode");
            if(!CanAbortAppUpdate(ReadStatus()))throw new IOException("Abort requires the selected same-interface APP update mode with no pending transition.");
            SetUpdateTimeout();Transfer(5,1);WaitForInPlaceMode(4);
            if(returnMode!=0){SetMode(returnMode);WaitForInPlaceMode(returnMode);}
            return returnMode==0?(ushort)4:returnMode;
        }
        static uint CheckRecovery(byte[] b){uint abi=U(b,16)>>16;if(!SupportedAbi(abi) || U(b,20)!=Capacity)throw new InvalidDataException("V1 recovery partition ABI mismatch.");return abi;}
        [DllImport("winusb.dll",EntryPoint="WinUsb_WritePipe",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool WritePipe(IntPtr h,byte pipe,byte[] b,uint count,out uint sent,IntPtr overlapped);
        private byte[] Transfer(ushort op,uint seq,uint arg0=0,uint arg1=0,byte[] payload=null)
        {
            payload=payload??new byte[0];var b=new byte[32+payload.Length];
            Put(b,0,0x31564e58);b[4]=2;Buffer.BlockCopy(BitConverter.GetBytes(op),0,b,6,2);
            Put(b,8,seq);Put(b,12,arg0);Put(b,16,arg1);Put(b,20,(uint)payload.Length);Put(b,24,Crc(b,0,24));Put(b,28,Crc(payload,0,payload.Length));Buffer.BlockCopy(payload,0,b,32,payload.Length);
            uint n;if(!WritePipe(usb,1,b,(uint)b.Length,out n,IntPtr.Zero))throw Error();
            if(n!=b.Length)throw new IOException("Short recovery write.");b=new byte[32];
            if(!NpDownloadNative.WinUsbReadPipe(usb,0x81,b,32,out n,IntPtr.Zero))throw Error();
            if(n!=32 || U(b,0)!=0x31564e58 || BitConverter.ToUInt16(b,4)!=2 || BitConverter.ToUInt16(b,6)!=(op|0x8000) || U(b,8)!=seq || U(b,24)!=Crc(b,0,24) || U(b,28)!=Crc(b,0,28))throw new InvalidDataException("Recovery response identity/sequence/CRC mismatch.");
            if(U(b,12)!=0)throw new IOException("Recovery rejected operation: status "+U(b,12));return b;
        }
        public void Dispose(){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}}
    }
}
