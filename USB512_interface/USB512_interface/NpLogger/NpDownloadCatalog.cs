using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace CE32_console
{
    internal sealed class NpDownloadRecord {
        internal long Start,End;internal uint Session,Chunks,ApHz,LfpHz,State,Fault,ProbeMask,LaneBytes;
        internal uint FormatVersion,CameraHz,MicrophoneHz;
        internal string ValidationError;
        internal bool Downloadable{get{return string.IsNullOrEmpty(ValidationError);}}
        internal bool V2{get{return FormatVersion==11 || FormatVersion==12;}}
        internal bool Media{get{return (FormatVersion==7 || FormatVersion==8) && ProbeMask==0 && ApHz==0;}}
        internal bool Complete{get{return State==3 && Fault==0 && Chunks>0;}}
        public override string ToString(){if(V2)return string.Format("Session {0} | NP2 probes 1 + 2 | {1:N0} samples/s | {2:N1} s | {3}",Session,ApHz,Chunks*61440.0/(ApHz*1600.0),Downloadable?"complete (payload not yet validated)":"NOT DOWNLOADABLE — "+ValidationError);if(Media)return string.Format("Session {0} | Camera {1} fps / microphone {2:N0} Hz | {3:N0} chunks | {4}",Session,CameraHz,MicrophoneHz,Chunks,!Downloadable?"NOT DOWNLOADABLE — "+ValidationError:Complete?"complete (payload not yet validated)":"INCOMPLETE");return string.Format("Session {0} | mask "+ProbeMask+" | AP {1:N0} / LFP {2:N0} Hz | {3:N1} s | {4}",Session,ApHz,LfpHz,Chunks*(LaneBytes*4.0)/(ApHz*780.0*NpSdGeometry.Count(ProbeMask)),Complete?"complete (not yet decoded)":"INCOMPLETE — committed prefix only");}
    }
    internal static class NpDownloadCatalog {
        // Discovery is read-only; this wider USB policy must not admit formatting.
        internal static bool IsReadOnlyDiskSource(string mediaType,string interfaceType){return NpCardFormat.IsRemovableMedia(mediaType) || string.Equals(interfaceType,"USB",StringComparison.OrdinalIgnoreCase);}
        internal static byte[] Sector(Stream s,long lba){byte[] b=new byte[512];s.Position=checked(lba*512);int p=0;while(p<512){int n=s.Read(b,p,512-p);if(n==0)throw new EndOfStreamException();p+=n;}return b;}
        private static void Header(byte[] b,string magic){if(Encoding.ASCII.GetString(b,0,8)!=magic || NpDownloadStream.U(b,508)!=NpDownloadStream.Crc(b,0,508))throw new InvalidDataException("Invalid NP recording descriptor/footer.");}
        internal static bool IsNpIdentity(byte[] id){return id!=null && id.Length==512 && Encoding.ASCII.GetString(id,0,16)=="CE_FILESYSTEM_V1" && Encoding.ASCII.GetString(id,32,9)=="NP_CE64_1";}
        internal static bool IsCatalogIdentity(byte[] id){
            if(id==null || id.Length!=512 || Encoding.ASCII.GetString(id,0,16)!="CE_FILESYSTEM_V1")return false;
            string tag=Encoding.ASCII.GetString(id,32,9);return tag=="NP_CE64_1" || tag=="NP_PSSI_6" || tag=="NP_PSSI_7";
        }
        // Both formats share the leading magic. Only the complete, known
        // ASCII-zero-padded legacy identity may reach legacy writers.
        internal static int ClassifyIdentity(byte[] id) {
            if(id==null || id.Length!=512)return -1;
            if(IsNpIdentity(id))return 2;
            if(Encoding.ASCII.GetString(id,0,16)!="CE_FILESYSTEM_V1")
                return Encoding.ASCII.GetString(id,32,9)=="NP_CE64_1"?3:0;
            for(int n=16;n<512;n++)if(id[n]!=(byte)'0')return 3;
            return 1;
        }
        internal static List<NpDownloadRecord> Read(Stream s) {
            var list=new List<NpDownloadRecord>();var id=Sector(s,16);
            if(!IsCatalogIdentity(id))throw new InvalidDataException("This is not a supported NP1 / NP2 SD catalog.");
            var journal=Sector(s,112);foreach(byte b in journal)if(b!=0)throw new InvalidDataException("Pending/torn SD journal: recover using recorder firmware before downloading. Nothing was modified.");
            long previous=8192;
            for(int page=0;page<1024;page++) {
                var log=Sector(s,128+page);uint count=NpDownloadStream.U(log,0);if(count>63)throw new InvalidDataException("Invalid SD directory count.");
                for(int i=0;i<count;i++) {
                    long end=NpDownloadStream.U(log,4+4*i),start=checked((previous+8191)/8192*8192);
                    if(end<=start+4 || end>s.Length/512 || NpDownloadStream.U(log,256+4*i)!=end)throw new InvalidDataException("Invalid NP directory extent.");
                    byte[] h=Sector(s,start+3),f=Sector(s,start+4);
                    if(Encoding.ASCII.GetString(h,0,8)=="N2SDLOG1") {
                        Header(h,"N2SDLOG1");Header(f,"N2SDEND1");
                        string failure=null;
                        try{USB512_interface.NpV2SdDatExport.ValidateMetadata(h,f,checked((uint)start),s.Length);
                            if(end<NpDownloadStream.U(f,116) || end>NpDownloadStream.U(h,68))throw new InvalidDataException("NP2 committed extent escapes the catalog entry.");}
                        catch(InvalidDataException ex){failure=ex.Message;}
                        list.Add(new NpDownloadRecord{ValidationError=failure,FormatVersion=NpDownloadStream.U(h,8),ProbeMask=3,LaneBytes=46592/4,Start=start,End=end,Session=NpDownloadStream.U(h,12),Chunks=NpDownloadStream.U(f,108),ApHz=NpDownloadStream.U(h,16),LfpHz=0,State=NpDownloadStream.U(f,100),Fault=NpDownloadStream.U(f,104)});
                        previous=end;continue;
                    }
                    Header(h,"NPSDLOG1");Header(f,"NPSDEND1");
                    string validationError=null;
                    if(NpMediaExport.IsTyped(h)) {
                        // Failed/empty allocations remain in the directory. Keep
                        // them visible without hiding later valid recordings.
                        try{NpMediaExport.Validate(h,f,start,end);}
                        catch(InvalidDataException ex){validationError=ex.Message;}
                    }else NpSdGeometry.Validate(h,f,start,end);
                    uint chunks=NpDownloadStream.U(f,68),rate=NpDownloadStream.U(h,24);
                    list.Add(new NpDownloadRecord{ValidationError=validationError,FormatVersion=NpDownloadStream.U(h,8),CameraHz=NpDownloadStream.U(h,112),MicrophoneHz=NpDownloadStream.U(h,116),ProbeMask=NpDownloadStream.U(h,16),LaneBytes=NpDownloadStream.U(h,32),Start=start,End=end,Session=NpDownloadStream.U(h,12),Chunks=chunks,ApHz=rate,LfpHz=rate/12,State=NpDownloadStream.U(f,60),Fault=NpDownloadStream.U(f,64)});previous=end;
                }
                if(count<63)return list;
            }throw new InvalidDataException("Directory exceeds supported page bound.");
        }
    }
    internal static class NpCardFormat {
        internal static bool IsRemovableMedia(string mediaType){return string.Equals(mediaType,"Removable Media",StringComparison.OrdinalIgnoreCase);}
        internal sealed class Target {
            internal string Path,Name,Serial;
            internal long Bytes;
            internal byte[] Identity;
            internal readonly List<string> Volumes=new List<string>();
            internal string Description{get{return Name+" | "+Path+" | "+(Bytes/1073741824.0).ToString("N1")+" GiB | serial "+Serial;}}
        }
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inputBytes,IntPtr output,uint outputBytes,out uint returned,IntPtr overlapped);
        internal static Target Inspect(string path) {
            if(path==null || !System.Text.RegularExpressions.Regex.IsMatch(path,@"^\\\\\.\\PHYSICALDRIVE[0-9]+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new IOException("Select a physical SD card reader, not a logger USB endpoint or volume.");
            uint number=uint.Parse(path.Substring(@"\\.\PHYSICALDRIVE".Length));
            var target=new Target{Path=path};
            using(var search=new ManagementObjectSearcher("SELECT MediaType FROM Win32_DiskDrive WHERE Index="+number))
            using(var disks=search.Get()) {
                if(disks.Count!=1)throw new IOException("Selected card reader is no longer present.");
                foreach(ManagementObject disk in disks)if(!IsRemovableMedia(Convert.ToString(disk["MediaType"])))throw new IOException("Hard drives and unknown media are excluded from NP/media Quick Format.");
            }
            using(var search=new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage","SELECT * FROM MSFT_Disk WHERE Number="+number))
            using(var disks=search.Get()) {
                if(disks.Count!=1)throw new IOException("Selected disk is no longer present.");
                foreach(ManagementObject disk in disks) {
                    uint bus=Convert.ToUInt32(disk["BusType"]);
                    if((bus!=7 && bus!=12 && bus!=13) || Convert.ToBoolean(disk["IsBoot"]) || Convert.ToBoolean(disk["IsSystem"]) || Convert.ToBoolean(disk["IsOffline"]) || Convert.ToBoolean(disk["IsReadOnly"]))throw new IOException("Only an online, writable USB/SD/MMC non-system card can be formatted.");
                    target.Name=Convert.ToString(disk["FriendlyName"]);target.Serial=Convert.ToString(disk["SerialNumber"]).Trim();
                    target.Bytes=checked((long)Convert.ToUInt64(disk["Size"]));
                }
            }
            using(var search=new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage","SELECT AccessPaths FROM MSFT_Partition WHERE DiskNumber="+number))
            using(var partitions=search.Get())foreach(ManagementObject partition in partitions) {
                var paths=partition["AccessPaths"] as string[];
                if(paths!=null)foreach(string volume in paths)if(volume.StartsWith(@"\\?\Volume{",StringComparison.OrdinalIgnoreCase))target.Volumes.Add(volume.TrimEnd('\\'));
            }
            using(var stream=NpDownloadStream.OpenDisk(path)) {
                if(stream.Length!=target.Bytes)throw new IOException("Disk capacity changed; refresh the source.");
                target.Identity=NpDownloadCatalog.Sector(stream,16);
                if(!NpDownloadCatalog.IsCatalogIdentity(target.Identity))throw new IOException("Quick Format accepts only an existing NP/media card. Other filesystems and unknown identities are protected.");
            }
            return target;
        }
        internal static void Format(Target confirmed) {
            var current=Inspect(confirmed.Path);
            if(current.Bytes!=confirmed.Bytes || current.Name!=confirmed.Name || current.Serial!=confirmed.Serial || !current.Identity.SequenceEqual(confirmed.Identity))throw new IOException("Selected disk changed after confirmation; nothing was written.");
            var locks=new List<SafeFileHandle>();
            try {
                foreach(string path in current.Volumes.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var handle=NpDownloadNative.CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
                    if(handle.IsInvalid){handle.Dispose();throw new IOException("Cannot open the card volume; close applications using it. Nothing was written.");}
                    locks.Add(handle);uint returned;
                    if(!DeviceIoControl(handle,0x00090018,IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero) || !DeviceIoControl(handle,0x00090020,IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero))throw new IOException("Cannot lock/dismount the card volume. Nothing was written.");
                }
                using(var stream=NpDownloadStream.OpenDiskForFormat(current.Path))QuickFormat(stream,confirmed.Bytes,confirmed.Identity);
            }finally{foreach(var handle in locks)handle.Dispose();}
        }
        internal static void QuickFormat(Stream stream,long confirmedBytes,byte[] confirmedIdentity) {
            if(!stream.CanRead || !stream.CanWrite || !stream.CanSeek || stream.Length!=confirmedBytes || confirmedBytes/512<=8192 || confirmedBytes%512!=0 || confirmedBytes/512>uint.MaxValue)throw new IOException("Invalid or changed SD capacity/access; nothing was written.");
            var identity=NpDownloadCatalog.Sector(stream,16);
            if(!NpDownloadCatalog.IsCatalogIdentity(identity) || confirmedIdentity==null || !identity.SequenceEqual(confirmedIdentity))throw new IOException("NP card identity changed; nothing was written.");
            var zero=new byte[512];
            // Match V1's catalog reset: invalidate first, publish identity last.
            // Existing system/DSP settings and payload sectors remain untouched.
            WriteSector(stream,16,zero);FlushDisk(stream);
            foreach(long lba in new long[]{128,112,113})WriteSector(stream,lba,zero);
            FlushDisk(stream);
            foreach(long lba in new long[]{128,112,113})if(NpDownloadCatalog.Sector(stream,lba).Any(b=>b!=0))throw new IOException("Catalog reset readback failed; identity remains invalid. Preserve the card; no automatic retry.");
            WriteSector(stream,16,identity);FlushDisk(stream);
            if(!NpDownloadCatalog.Sector(stream,16).SequenceEqual(identity) || NpDownloadCatalog.Read(stream).Count!=0)throw new IOException("Format verification failed; preserve the card and inspect it before recording.");
        }
        private static void WriteSector(Stream stream,long lba,byte[] bytes){stream.Position=checked(lba*512);stream.Write(bytes,0,512);}
        private static void FlushDisk(Stream stream){var file=stream as FileStream;if(file!=null)file.Flush(true);else stream.Flush();}
    }
}
