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
        internal long Start,End;internal uint Session,Chunks,ApHz,LfpHz,State,Fault,ProbeMask,LaneBytes,TicksPerBlock;
        internal uint FormatVersion,UnderlyingFormatVersion,CameraHz,MicrophoneHz,LogicalPrefixBlocks,LogicalHighWaterBlocks;
        internal ulong PhysicalSectors;
        internal string ValidationError;
        internal long CatalogEnd,CommittedEnd;
        internal bool OpenTail;
        internal string RecoveryNotice;
        internal bool Downloadable{get{return string.IsNullOrEmpty(ValidationError);}}
        internal bool V2{get{return FormatVersion==11 || FormatVersion==12 || FormatVersion==13 || FormatVersion==14 || FormatVersion==15 || FormatVersion==16;}}
        internal bool Media{get{return (FormatVersion==7 || FormatVersion==8) && ProbeMask==0 && ApHz==0;}}
        internal bool Complete{get{return State==3 && Fault==0 && Chunks>0;}}
        internal bool PowerInterruptedRecovery{get{return Downloadable && (FormatVersion==6 && State>=1 && State<=4 || Media && State==1 && Fault==0) && Chunks==0 && !Complete;}}
        internal string V2ProbeLabel{get{return ProbeMask==1?"probe 1":ProbeMask==3?"probes 1 + 2":ProbeMask==7?"probes 1 + 2 + 3":"mask "+ProbeMask;}}
        internal double V2DurationSeconds{get{return ApHz==0||TicksPerBlock==0?0:Chunks*(double)TicksPerBlock/(ApHz*1600.0);}}
        internal string RecordedSize{get{
            if(PowerInterruptedRecovery)return "size unknown";
            double size=FormatVersion==16?PhysicalSectors*512UL:(ulong)Chunks*(V2?46592UL:65536UL);int unit=0;
            string[] units={"B","KiB","MiB","GiB","TiB"};
            while(size>=1024 && unit<units.Length-1){size/=1024;unit++;}
            return size.ToString(unit==0?"N0":"N2")+" "+units[unit]+(Chunks==0?" committed":"");
        }}
        public override string ToString(){
            if(!Downloadable)return (Start>0?"Recording at LBA "+Start:"Catalog")+" | UNAVAILABLE — "+ValidationError;
            string state=Complete?"complete (payload not yet validated)":PowerInterruptedRecovery?"INCOMPLETE — unfinished footer; read-only recovery available":Chunks==0?"INCOMPLETE — no committed data (payload not scanned)":"INCOMPLETE — committed prefix only";
            if(FormatVersion==14 || FormatVersion==15 && UnderlyingFormatVersion==14)return string.Format("Session {0} | NP2 {1} | {2:N0} samples/s | {3} | {4}",
                Session,V2ProbeLabel,ApHz,RecordedSize,
                LogicalPrefixBlocks!=Chunks || LogicalHighWaterBlocks!=Chunks?"INCOMPLETE — logical gaps; DAT export refused":state)+
                (RecoveryNotice==null?"":" — "+RecoveryNotice);
            if(V2)return string.Format("Session {0} | NP2 {1} | {2:N0} samples/s | {3:N1} s | {4} | {5}{6}",Session,V2ProbeLabel,ApHz,V2DurationSeconds,RecordedSize,state,RecoveryNotice==null?"":" — "+RecoveryNotice);
            string notice=RecoveryNotice==null?"":" — "+RecoveryNotice;
            if(Media)return string.Format("Session {0} | Camera {1} fps / microphone {2:N0} Hz | {3} | {4}",Session,CameraHz,MicrophoneHz,RecordedSize,state)+notice;
            return string.Format("Session {0} | mask "+ProbeMask+" | AP {1:N0} / LFP {2:N0} Hz | {3:N1} s | {4} | {5}",Session,ApHz,LfpHz,Chunks*(LaneBytes*4.0)/(ApHz*780.0*NpSdGeometry.Count(ProbeMask)),RecordedSize,state)+notice;
        }
    }
    internal static class NpDownloadCatalog {
        // Discovery is read-only; this wider USB policy must not admit formatting.
        internal static bool IsReadOnlyDiskSource(string mediaType,string interfaceType){return NpCardFormat.IsRemovableMedia(mediaType) || string.Equals(interfaceType,"USB",StringComparison.OrdinalIgnoreCase);}
        internal static byte[] Sector(Stream s,long lba){byte[] b=new byte[512];s.Position=checked(lba*512);int p=0;while(p<512){int n=s.Read(b,p,512-p);if(n==0)throw new EndOfStreamException();p+=n;}return b;}
        // Folder dates use recorded START evidence, never the download clock.
        // UTC brackets describe capture arming, not the first ADC conversion.
        internal static DateTime? RecordingStartTime(Stream s,NpDownloadRecord record) {
            if(record.Media) {
                // Interrupted recordings may retain the START clock only in
                // the descriptor. Neither sector's legacy CE date is a clock.
                foreach(int offset in new[]{4,3}) {
                    byte[] sector=Sector(s,record.Start+offset);
                    if(Encoding.ASCII.GetString(sector,0,8)!=(offset==4?"NPSDEND1":"NPSDLOG1") ||
                       NpDownloadStream.U(sector,508)!=NpDownloadStream.Crc(sector,0,508) || NpDownloadStream.U(sector,12)!=record.Session)continue;
                    var metadata=NpMediaExport.Metadata(sector);
                    if(metadata==null)continue;
                    ulong? start=NpMediaExport.UtcAt(metadata,Convert.ToUInt32(metadata["start_tick"]),false);
                    var local=start.HasValue?LocalTime(start.Value):null;
                    if(local.HasValue)return local;
                }
            }
            byte[] b=Sector(s,record.Start+5);bool v2=record.V2;
            uint clockVersion=NpDownloadStream.U(b,8);
            if((clockVersion!=1 && clockVersion!=2) || Encoding.ASCII.GetString(b,0,8)!=(v2?"N2SDUTC":"N1SDUTC")+clockVersion ||
               NpDownloadStream.U(b,508)!=NpDownloadStream.Crc(b,0,508))return null;
            if(clockVersion==2 && b.Skip(160).Take(348).Any(value=>value!=0)){
                // A recognized sealed receipt may extend UTC2. Normalize only
                // a private copy, after exact recorded family/session/profile
                // binding; arbitrary reserved bytes never acquire a date.
                try {
                    var bound=USB512_interface.NpFirstSyncSd.ReadSector(b,v2?2:1,record.Session,record.ApHz);
                    if(bound==null || bound.Receipt.Mask!=record.ProbeMask)return null;
                }catch(InvalidDataException){return null;}
                b=(byte[])b.Clone();Array.Clear(b,160,348);
                Buffer.BlockCopy(BitConverter.GetBytes(NpDownloadStream.Crc(b,0,508)),0,b,508,4);
            }
            uint flags=NpDownloadStream.U(b,16),uncertainty=NpDownloadStream.U(b,24);
            ulong anchor=BitConverter.ToUInt64(b,32);
            if(NpDownloadStream.U(b,12)!=record.Session ||
               (flags&~(v2?15U:7U))!=0 || (flags&3)!=3 || NpDownloadStream.U(b,20)==0 ||
               uncertainty<1000 || uncertainty>5000000 || NpDownloadStream.U(b,72)!=record.ApHz ||
               NpDownloadStream.U(b,76)!=1000 || anchor<1577836800000000UL || anchor>4102444800000000UL)return null;
            int reserved=(flags&8U)!=0?112:80;
            if(v2 && b.Skip(reserved).Take(508-reserved).Any(value=>value!=0))return null;
            uint before=unchecked(NpDownloadStream.U(b,v2?48:64)-NpDownloadStream.U(b,28));
            uint after=unchecked(NpDownloadStream.U(b,v2?52:68)-NpDownloadStream.U(b,28));
            ulong beforeUtc=BitConverter.ToUInt64(b,v2?56:48),afterUtc=BitConverter.ToUInt64(b,v2?64:56);
            if(before>=0x80000000U || after>=0x80000000U || after<before ||
               beforeUtc!=anchor+(ulong)before*1000 || afterUtc!=anchor+(ulong)after*1000)return null;
            return LocalTime(beforeUtc);
        }
        static DateTime? LocalTime(ulong us) {
            if(us<1577836800000000UL || us>4102444800000000UL)return null;
            return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddTicks(checked((long)us*10)).ToLocalTime();
        }
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
        internal static List<NpDownloadRecord> Read(Stream s) { return ReadCore(s,false); }
        internal static List<NpDownloadRecord> ReadAvailable(Stream s) { return ReadCore(s,true); }
        static NpDownloadRecord Unavailable(long start,long end,string error){return new NpDownloadRecord{Start=start,End=end,ValidationError=error};}
        static bool HeaderAt(byte[] header,long start){
            string magic=Encoding.ASCII.GetString(header,0,8);
            return (magic=="NPSDLOG1" || magic=="N2SDLOG1") && NpDownloadStream.U(header,508)==NpDownloadStream.Crc(header,0,508) &&
                NpDownloadStream.U(header,magic=="N2SDLOG1"?56:40)==start;
        }
        static List<NpDownloadRecord> ReadCore(Stream s,bool available) {
            var list=new List<NpDownloadRecord>();byte[] id=null;IOException identityError=null;
            try{id=Sector(s,16);}catch(IOException ex){identityError=ex;}
            // A recognized legacy primary remains legacy even if sector24
            // contains a stale filesystem2 identity left by an old formatter.
            if(!IsCatalogIdentity(id)) {
                long identitySectors=s.Length/512;bool store2=false;
                if(s.Length%512==0 && identitySectors>8192 && identitySectors<=uint.MaxValue) {
                    store2=NpV1StoreV2.IdentityValid(id,(uint)identitySectors);
                    if(!store2) {
                        byte[] second=null;try{second=Sector(s,24);}catch(IOException){}
                        store2=NpV1StoreV2.IdentityValid(second,(uint)identitySectors);
                    }
                }
                if(store2)return ReadStoreV2(s,available);
                if(identityError!=null)throw identityError;
                throw new InvalidDataException("This is not a supported NP1 / NP2 SD catalog.");
            }
            var journal=Sector(s,112);bool cleanDirectory=!journal.Any(b=>b!=0);
            if(!cleanDirectory){
                if(!available)throw new InvalidDataException("Pending/torn SD journal: recover using recorder firmware before downloading. Nothing was modified.");
                list.Add(Unavailable(0,0,"Pending/torn journal; showing readable published entries only. The card was not modified."));
            }
            long[] previous={8192};long sectors=s.Length/512;
            for(int page=0;page<1024;page++) {
                byte[] log;
                try{log=Sector(s,128+page);}catch(IOException ex){if(!available)throw;list.Add(Unavailable(0,0,"Directory page "+page+" unreadable: "+ex.Message));return list;}
                uint storedCount=NpDownloadStream.U(log,0),count=storedCount;
                if(count>63){if(!available)throw new InvalidDataException("Invalid SD directory count.");cleanDirectory=false;count=63;list.Add(Unavailable(0,0,"Directory page "+page+" has an invalid count; inspecting its mirrored entries."));}
                for(int i=0;i<count;i++) {
                    long end=NpDownloadStream.U(log,4+4*i),mirror=NpDownloadStream.U(log,256+4*i);
                    var starts=previous.Select(value=>checked((value+8191)/8192*8192)).Distinct().Where(value=>value+4<end).ToArray();
                    previous=new[]{end,mirror}.Where(value=>value>8192 && value<=sectors).Distinct().ToArray();
                    if(available && storedCount>63 && end==0 && mirror==0)continue;
                    if(end!=mirror || end>sectors || starts.Length==0){
                        if(!available)throw new InvalidDataException("Invalid NP directory extent.");
                        list.Add(Unavailable(starts.FirstOrDefault(),end,"Directory extent is invalid/ambiguous; later entries will still be inspected."));continue;
                    }
                    long start=starts[0];byte[] h=null;
                    try{
                        if(starts.Length>1){
                            var matches=new List<Tuple<long,byte[]>>();
                            foreach(long candidate in starts){var header=Sector(s,candidate+3);if(HeaderAt(header,candidate))matches.Add(Tuple.Create(candidate,header));}
                            if(matches.Count!=1)throw new InvalidDataException("Cannot uniquely identify this recording after a damaged directory entry.");
                            start=matches[0].Item1;h=matches[0].Item2;
                        }
                        list.Add(ReadRecord(s,start,end,h,available));
                    }catch(Exception ex) when(ex is IOException || ex is InvalidDataException){if(!available)throw;list.Add(Unavailable(start,end,ex.Message));}
                }
                if(storedCount<63){
                    var tail=list.LastOrDefault();
                    // V2 publishes only payload_start at Reserve. A durable
                    // FAILED checkpoint can seal substantial data before Finish
                    // publishes the final directory end. Recover this prefix
                    // only at the proven catalog tail; never infer append space.
                    if(available && cleanDirectory && tail!=null && tail.OpenTail){
                        tail.End=tail.CommittedEnd;tail.ValidationError=null;
                        tail.RecoveryNotice="unfinalized catalog; committed footer used";
                    }
                    return list;
                }
            }
            if(available){list.Add(Unavailable(0,0,"Directory exceeds the supported page bound; readable prefix retained."));return list;}
            throw new InvalidDataException("Directory exceeds supported page bound.");
        }
        static List<NpDownloadRecord> ReadStoreV2(Stream s,bool available) {
            var store=NpV1StoreV2.Read(s);var list=new List<NpDownloadRecord>();
            string allocation="V1 filesystem2 checkpoint generation "+store.Generation+", allocation frontier "+store.Frontier+". Card unchanged; no tail reclaimed or free space inferred. ";
            foreach(var entry in store.Entries) {
                if(!entry.Available) {
                    var missing=Unavailable(0,0,"Filesystem2 record "+entry.Index+": "+entry.ValidationError);
                    missing.RecoveryNotice=allocation;list.Add(missing);continue;
                }
                long bound=entry.ReservedEnd;
                bool extended=entry.State==1 && store.Open==1 && entry.Index==store.Count-1 &&
                    entry.Start==store.Start && entry.Session==store.Session && store.End>entry.ReservedEnd;
                if(extended)bound=store.End;
                try {
                    byte[] h=Sector(s,entry.Start+3L),f=Sector(s,entry.Start+4L);
                    Header(h,"NPSDLOG1");Header(f,"NPSDEND1");
                    NpV1StoreBinding.RequireRecord(h,f,store.Epoch,entry.Index);
                    if(NpDownloadStream.U(h,12)!=entry.Session)throw new InvalidDataException("Payload session differs from the filesystem2 record.");
                    uint state=NpDownloadStream.U(f,60),fault=NpDownloadStream.U(f,64),chunks=NpDownloadStream.U(f,68);
                    if(state<1 || state>4)throw new InvalidDataException("Unknown V1 payload footer state.");
                    if(NpMediaExport.IsTyped(h))NpMediaExport.ValidateCommitted(h,f,entry.Start,bound);
                    else NpSdGeometry.Validate(h,f,entry.Start,bound);
                    long committed=checked((long)entry.Start+NpDownloadStream.U(h,48)+(long)chunks*128);
                    bool complete=state==3 && fault==0 && chunks>0;
                    var record=ReadV1Record(entry.Start,complete?committed:bound,h,f,available);
                    record.CatalogEnd=bound;record.CommittedEnd=committed;
                    record.RecoveryNotice=allocation+"Filesystem record state "+entry.State+", stored committed end "+entry.CommittedEnd+"; payload not yet validated. "+
                        (extended?"Interrupted OPEN last-record recovery bound extended from descriptor grant "+entry.ReservedEnd+" to matching checkpoint grant "+bound+". No write or reclaim is authorized.":
                        "Reserved grant end "+bound+" remains allocated, including any unused tail.");
                    list.Add(record);
                }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is OverflowException) {
                    var bad=Unavailable(entry.Start,bound,ex.Message);bad.Session=entry.Session;bad.CatalogEnd=bound;bad.RecoveryNotice=allocation;
                    list.Add(bad);
                }
            }
            return list;
        }
        static NpDownloadRecord ReadRecord(Stream s,long start,long end,byte[] h,bool available){
                    h=h??Sector(s,start+3);byte[] f=Sector(s,start+4);
                    if(Encoding.ASCII.GetString(h,0,8)=="N2SDLOG1") {
                        Header(h,"N2SDLOG1");Header(f,"N2SDEND1");
                        string failure=null;bool openTail=false;
                        try{if(available)USB512_interface.NpV2SdDatExport.ValidateCommittedMetadata(h,f,checked((uint)start),s.Length);else USB512_interface.NpV2SdDatExport.ValidateMetadata(h,f,checked((uint)start),s.Length);
                            uint state=NpDownloadStream.U(f,100);
                            openTail=available && (state==4 || state==3 && NpDownloadStream.U(f,104)==0) && end==NpDownloadStream.U(h,60) && end<NpDownloadStream.U(f,116) &&
                                NpDownloadStream.U(h,72)==s.Length/512 && NpDownloadStream.U(h,68)<=s.Length/512;
                            if(end<NpDownloadStream.U(f,116) || end>NpDownloadStream.U(h,68))throw new InvalidDataException("NP2 extent mismatch: catalog="+end+", payload="+NpDownloadStream.U(h,60)+", committed="+NpDownloadStream.U(f,116)+", reserved="+NpDownloadStream.U(h,68)+", state="+state+", fault="+NpDownloadStream.U(f,104)+", blocks="+NpDownloadStream.U(f,108)+".");}
                        catch(InvalidDataException ex){failure=ex.Message;}
                        return new NpDownloadRecord{ValidationError=failure,CatalogEnd=end,CommittedEnd=NpDownloadStream.U(f,116),OpenTail=openTail,FormatVersion=NpDownloadStream.U(h,8),UnderlyingFormatVersion=NpDownloadStream.U(h,8)==16?NpDownloadStream.U(h,220):NpDownloadStream.U(h,8)==15?NpDownloadStream.U(h,136):NpDownloadStream.U(h,8),PhysicalSectors=NpDownloadStream.U(h,8)==16?BitConverter.ToUInt64(f,256):(ulong)NpDownloadStream.U(f,108)*91,ProbeMask=NpDownloadStream.U(h,24),LaneBytes=46592/4,TicksPerBlock=NpDownloadStream.U(h,44),Start=start,End=end,Session=NpDownloadStream.U(h,12),Chunks=NpDownloadStream.U(f,108),ApHz=NpDownloadStream.U(h,16),LfpHz=0,State=NpDownloadStream.U(f,100),Fault=NpDownloadStream.U(f,104),LogicalPrefixBlocks=NpDownloadStream.U(f,132),LogicalHighWaterBlocks=NpDownloadStream.U(f,136)};
                    }
                    return ReadV1Record(start,end,h,f,available);
        }
        static NpDownloadRecord ReadV1Record(long start,long end,byte[] h,byte[] f,bool available){
                    Header(h,"NPSDLOG1");Header(f,"NPSDEND1");
                    string validationError=null;
                    if(NpMediaExport.IsTyped(h)) {
                        // Failed/empty allocations remain in the directory. Keep
                        // them visible without hiding later valid recordings.
                        try{if(available)NpMediaExport.ValidateCommitted(h,f,start,end);else NpMediaExport.Validate(h,f,start,end);}
                        catch(InvalidDataException ex){validationError=ex.Message;}
                    }else NpSdGeometry.Validate(h,f,start,end);
                    uint chunks=NpDownloadStream.U(f,68),rate=NpDownloadStream.U(h,24);
                    return new NpDownloadRecord{ValidationError=validationError,FormatVersion=NpDownloadStream.U(h,8),CameraHz=NpDownloadStream.U(h,112),MicrophoneHz=NpDownloadStream.U(h,116),ProbeMask=NpDownloadStream.U(h,16),LaneBytes=NpDownloadStream.U(h,32),Start=start,End=end,Session=NpDownloadStream.U(h,12),Chunks=chunks,ApHz=rate,LfpHz=rate/12,State=NpDownloadStream.U(f,60),Fault=NpDownloadStream.U(f,64)};
        }
    }
    internal static class NpCardFormat {
        internal static bool IsRemovableMedia(string mediaType){return string.Equals(mediaType,"Removable Media",StringComparison.OrdinalIgnoreCase);}
        internal sealed class Target {
            internal string Path,Name,Serial;
            internal long Bytes;
            internal byte[] Identity;
            internal byte[] SecondaryIdentity;
            internal bool StoreV2;
            internal ulong StoreV2Epoch;
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
                InspectIdentity(stream,target);
            }
            return target;
        }
        private static void InspectIdentity(Stream stream,Target target) {
            if(!stream.CanRead || !stream.CanSeek || stream.Length!=target.Bytes || target.Bytes%512!=0 ||
               target.Bytes/512<=8192 || target.Bytes/512>uint.MaxValue)throw new IOException("Disk capacity/access changed; refresh the source.");
            byte[] first=NpDownloadCatalog.Sector(stream,16);
            // An exact supported legacy primary always remains legacy. Never
            // migrate it because a stale filesystem2 copy survives at24.
            if(NpDownloadCatalog.IsCatalogIdentity(first)) {
                target.Identity=first;target.SecondaryIdentity=null;target.StoreV2=false;target.StoreV2Epoch=0;return;
            }
            byte[] second=NpDownloadCatalog.Sector(stream,24);uint capacity=(uint)(target.Bytes/512);
            bool a=NpV1StoreV2.IdentityValid(first,capacity),b=NpV1StoreV2.IdentityValid(second,capacity);
            if(!a && !b)throw new IOException("Quick Format accepts only an existing NP/media or confirmed V1 filesystem2 card. Other filesystems and unknown identities are protected.");
            // Reformat is explicit and destructive: conflicting valid FS2
            // copies may be reset, but both raw copies must stay confirmed.
            target.Identity=first;target.SecondaryIdentity=second;target.StoreV2=true;
            target.StoreV2Epoch=BitConverter.ToUInt64(a?first:second,56);
        }
        internal static void Format(Target confirmed) {Edit(confirmed,stream=>QuickFormatConfirmed(stream,confirmed));}
        internal static void DeleteLast(Target confirmed,LastRecording latest) {Edit(confirmed,stream=>DeleteLast(stream,confirmed.Bytes,confirmed.Identity,latest));}
        private static void Edit(Target confirmed,Action<Stream> edit) {
            if(confirmed==null)throw new IOException("Confirm a physical card before maintenance.");
            var current=Inspect(confirmed.Path);
            ValidateTarget(confirmed,current);
            var locks=new List<SafeFileHandle>();
            try {
                foreach(string path in current.Volumes.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var handle=NpDownloadNative.CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
                    if(handle.IsInvalid){handle.Dispose();throw new IOException("Cannot open the card volume; close applications using it. Nothing was written.");}
                    locks.Add(handle);uint returned;
                    if(!DeviceIoControl(handle,0x00090018,IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero) || !DeviceIoControl(handle,0x00090020,IntPtr.Zero,0,IntPtr.Zero,0,out returned,IntPtr.Zero))throw new IOException("Cannot lock/dismount the card volume. Nothing was written.");
                }
                // Recheck the same physical identity and complete volume set
                // after acquiring the existing exclusive volume locks.
                ValidateTarget(confirmed,Inspect(confirmed.Path));
                using(var stream=NpDownloadStream.OpenDiskForFormat(current.Path))edit(stream);
            }finally{foreach(var handle in locks)handle.Dispose();}
        }
        private static bool SameBytes(byte[] a,byte[] b){return a==null?b==null:b!=null && a.SequenceEqual(b);}
        private static void ValidateTarget(Target confirmed,Target current) {
            if(confirmed==null || current==null || current.Path!=confirmed.Path || current.Bytes!=confirmed.Bytes ||
               current.Name!=confirmed.Name || current.Serial!=confirmed.Serial || current.StoreV2!=confirmed.StoreV2 ||
               current.StoreV2Epoch!=confirmed.StoreV2Epoch || !SameBytes(current.Identity,confirmed.Identity) ||
               !SameBytes(current.SecondaryIdentity,confirmed.SecondaryIdentity) ||
               !new HashSet<string>(current.Volumes,StringComparer.OrdinalIgnoreCase).SetEquals(confirmed.Volumes))
                throw new IOException("Selected disk/volume identity changed after confirmation; nothing was written.");
        }
        private static byte[] ValidateCard(Stream stream,long confirmedBytes,byte[] confirmedIdentity) {
            if(!stream.CanRead || !stream.CanWrite || !stream.CanSeek || stream.Length!=confirmedBytes || confirmedBytes/512<=8192 || confirmedBytes%512!=0 || confirmedBytes/512>uint.MaxValue)throw new IOException("Invalid or changed SD capacity/access; nothing was written.");
            var identity=NpDownloadCatalog.Sector(stream,16);
            if(!NpDownloadCatalog.IsCatalogIdentity(identity) || confirmedIdentity==null || !identity.SequenceEqual(confirmedIdentity))throw new IOException("NP card identity changed; nothing was written.");
            return identity;
        }
        internal sealed class LastRecording {
            internal NpDownloadRecord Record;
            internal int Count;
            internal long Page;
            internal byte[] Directory,Descriptor,Footer;
        }
        internal static LastRecording Latest(Stream stream) {
            var records=NpDownloadCatalog.Read(stream);
            if(records.Count==0)throw new IOException("The card has no recordings to delete.");
            var record=records[records.Count-1];long page=128+(records.Count-1)/63;
            return new LastRecording{Record=record,Count=records.Count,Page=page,Directory=NpDownloadCatalog.Sector(stream,page),Descriptor=NpDownloadCatalog.Sector(stream,record.Start+3),Footer=NpDownloadCatalog.Sector(stream,record.Start+4)};
        }
        internal static void DeleteLast(Stream stream,long confirmedBytes,byte[] confirmedIdentity,LastRecording confirmed) {
            ValidateCard(stream,confirmedBytes,confirmedIdentity);
            var latest=Latest(stream);
            if(confirmed==null || latest.Count!=confirmed.Count || latest.Page!=confirmed.Page || latest.Record.Start!=confirmed.Record.Start ||
               !latest.Directory.SequenceEqual(confirmed.Directory) || !latest.Descriptor.SequenceEqual(confirmed.Descriptor) || !latest.Footer.SequenceEqual(confirmed.Footer))throw new IOException("Recording catalog changed after confirmation; nothing was written. List the card again.");
            byte[] directory=(byte[])latest.Directory.Clone();int index=(latest.Count-1)%63;
            Buffer.BlockCopy(BitConverter.GetBytes((uint)index),0,directory,0,4);
            Array.Clear(directory,4+4*index,4);Array.Clear(directory,256+4*index,4);
            // Use the existing V1/V2 write-ahead catalog protocol. Flush and
            // read back every phase before publishing or clearing its journal.
            byte[] journal=new byte[512];Encoding.ASCII.GetBytes("NPC64TX1").CopyTo(journal,0);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)latest.Page),0,journal,8,4);
            Buffer.BlockCopy(BitConverter.GetBytes(NpDownloadStream.Crc(directory,0,512)),0,journal,12,4);
            Buffer.BlockCopy(BitConverter.GetBytes(NpDownloadStream.Crc(journal,0,508)),0,journal,508,4);
            WriteVerified(stream,113,directory);WriteVerified(stream,112,journal);
            WriteVerified(stream,latest.Page,directory);WriteVerified(stream,112,new byte[512]);
            if(NpDownloadCatalog.Read(stream).Count!=latest.Count-1)throw new IOException("Delete verification failed; preserve the card and inspect its catalog before recording.");
        }
        private static void WriteVerified(Stream stream,long lba,byte[] bytes) {
            WriteSector(stream,lba,bytes);FlushDisk(stream);
            if(!NpDownloadCatalog.Sector(stream,lba).SequenceEqual(bytes))throw new IOException("Card write verification failed; preserve the card and inspect its catalog before recording. No automatic retry.");
        }
        private static void StoreV2Word(byte[] bytes,int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,offset,4);}
        private static void StoreV2Seal(byte[] bytes){StoreV2Word(bytes,508,NpDownloadStream.Crc(bytes,0,508));}
        internal static void QuickFormatConfirmed(Stream stream,Target confirmed) {
            if(confirmed==null)throw new IOException("Confirm a physical card before formatting.");
            if(!confirmed.StoreV2){QuickFormat(stream,confirmed.Bytes,confirmed.Identity);return;}
            if(!stream.CanRead || !stream.CanWrite || !stream.CanSeek)throw new IOException("Card must be readable, writable and seekable; nothing was written.");
            var current=new Target{Bytes=confirmed.Bytes};InspectIdentity(stream,current);
            if(!current.StoreV2 || current.StoreV2Epoch!=confirmed.StoreV2Epoch ||
               !SameBytes(current.Identity,confirmed.Identity) || !SameBytes(current.SecondaryIdentity,confirmed.SecondaryIdentity) ||
               stream.Length!=confirmed.Bytes)throw new IOException("V1 filesystem2 identity/capacity changed; nothing was written.");
            uint capacity=(uint)(confirmed.Bytes/512);byte[] nonce=new byte[8];ulong epoch;
            using(var random=System.Security.Cryptography.RandomNumberGenerator.Create()) {
                do {random.GetBytes(nonce);epoch=BitConverter.ToUInt64(nonce,0);}
                while(epoch==0 || NpV1StoreV2.IdentityValid(current.Identity,capacity) && epoch==BitConverter.ToUInt64(current.Identity,56) ||
                    NpV1StoreV2.IdentityValid(current.SecondaryIdentity,capacity) && epoch==BitConverter.ToUInt64(current.SecondaryIdentity,56));
            }
            byte[] identity=new byte[512],checkpoint=new byte[512],zero=new byte[512];
            Encoding.ASCII.GetBytes("NP_FILESYSTEM_V2").CopyTo(identity,0);Encoding.ASCII.GetBytes("NP_CE64_2").CopyTo(identity,32);
            StoreV2Word(identity,48,2);StoreV2Word(identity,52,capacity);Buffer.BlockCopy(nonce,0,identity,56,8);
            StoreV2Word(identity,64,8192);StoreV2Word(identity,68,4032);StoreV2Seal(identity);
            Encoding.ASCII.GetBytes("NPS2CP01").CopyTo(checkpoint,0);StoreV2Word(checkpoint,8,2);StoreV2Word(checkpoint,12,capacity);
            Buffer.BlockCopy(nonce,0,checkpoint,16,8);Buffer.BlockCopy(BitConverter.GetBytes(1UL),0,checkpoint,24,8);
            StoreV2Word(checkpoint,32,8192);StoreV2Seal(checkpoint);
            if(!NpV1StoreV2.IdentityValid(identity,capacity) || epoch==0 ||
               NpDownloadStream.U(checkpoint,508)!=NpDownloadStream.Crc(checkpoint,0,508))throw new IOException("Cannot prepare valid V1 filesystem2 metadata; nothing was written.");
            // Exactly canonical nps2_format's six verified publications. Old
            // record slots/settings/payload stay untouched; fresh epoch hides them.
            WriteVerified(stream,16,zero);WriteVerified(stream,24,zero);
            WriteVerified(stream,112,checkpoint);WriteVerified(stream,113,checkpoint);
            WriteVerified(stream,16,identity);WriteVerified(stream,24,identity);
            var empty=NpV1StoreV2.Mount(stream);
            if(!empty.Ready || empty.Epoch!=epoch || empty.Generation!=1 || empty.Frontier!=8192 ||
               empty.Count!=0 || empty.Start!=0 || empty.End!=0 || empty.Session!=0 || empty.Open!=0)
                throw new IOException("V1 filesystem2 format verification failed; preserve the card. No automatic retry.");
        }
        internal static void QuickFormat(Stream stream,long confirmedBytes,byte[] confirmedIdentity) {
            var identity=ValidateCard(stream,confirmedBytes,confirmedIdentity);
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
