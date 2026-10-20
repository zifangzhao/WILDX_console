using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CE32_console {
    // Read-only browsing only. Neither a recovered record nor the stopping
    // boundary is an allocation cursor or permission to reclaim a reservation.
    internal static class NpCatalogRecovery {
        const int MaxRecords=1024*63;
        const string Notice="Read-only reconstructed catalog candidate; current catalog membership unproven; card unchanged; allocation unproven. Payload has not been validated.";

        internal static List<NpDownloadRecord> Read(Stream card,Func<bool> cancelled=null,Action<int> progress=null,bool forceHeaders=false) {
            if(card==null)throw new ArgumentNullException("card");
            Cancel(cancelled);
            if(!card.CanRead || !card.CanSeek)throw new IOException("Catalog recovery requires a readable, seekable source.");
            long length=card.Length,capacity=length/512;
            if(length%512!=0 || capacity<8197 || capacity>uint.MaxValue)throw new IOException("Invalid SD source capacity for V1 catalog recovery.");
            var source=new MetadataSource(card,length,cancelled);
            List<NpDownloadRecord> records;
            try {
                records=NpDownloadCatalog.ReadAvailable(source);
                Cancel(cancelled);
                if(!forceHeaders && records.All(r=>r.Downloadable))return records;
            }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is OverflowException) {
                records=new List<NpDownloadRecord>{Unavailable(0,"Catalog metadata unavailable: "+ex.Message)};
            }
            // An FS2 entry failure must not be bypassed by re-adopting a stale
            // legacy-shaped header at the same LBA/session after a new epoch.
            if(IsStoreV2Source(source,(uint)capacity)) {
                records.Add(Unavailable(0,"Filesystem2 header-only fallback is disabled; only current epoch/index-bound catalog entries are retained. Card unchanged; no allocation inferred."));
                Cancel(cancelled);if(progress!=null)progress(100);return records;
            }
            if(progress!=null)progress(0);
            long start=8192;
            for(int count=0;count<MaxRecords;count++) {
                Cancel(cancelled);
                NpDownloadRecord found;
                try {
                    if(start<8192 || (start&8191)!=0 || start>capacity-5)throw new InvalidDataException("Next recording metadata is outside the source capacity.");
                    byte[] h=NpDownloadCatalog.Sector(source,start+3);
                    Header(h,"NPSDLOG1");
                    uint version=NpDownloadStream.U(h,8);bool media=NpMediaExport.IsTyped(h);
                    if((version!=6 && !media) || NpDownloadStream.U(h,12)==0)throw new InvalidDataException("Recovery accepts only V1 format6/v7/v8 with a nonzero session.");
                    long reserved=NpDownloadStream.U(h,44),hs=NpDownloadStream.U(h,48);
                    if(reserved>capacity || reserved<=start || hs>reserved-start || start+hs>=reserved)throw new InvalidDataException("Recording reservation exceeds the source or cannot contain payload.");
                    byte[] f=NpDownloadCatalog.Sector(source,start+4);Header(f,"NPSDEND1");
                    if(media)NpMediaExport.ValidateCommitted(h,f,start,reserved);
                    else NpSdGeometry.Validate(h,f,start,reserved);
                    uint state=NpDownloadStream.U(f,60),fault=NpDownloadStream.U(f,64),chunks=NpDownloadStream.U(f,68);
                    if(state>4)throw new InvalidDataException("Unknown V1 footer state.");
                    long committed=checked(start+hs+(long)chunks*128);
                    bool complete=state==3 && fault==0 && chunks!=0;
                    long end=complete?committed:reserved;
                    if(media) {
                        if(complete)NpMediaExport.Validate(h,f,start,end);
                        else NpMediaExport.ValidateCommitted(h,f,start,end);
                    }else NpSdGeometry.Validate(h,f,start,end);
                    found=new NpDownloadRecord{Start=start,End=end,CatalogEnd=reserved,CommittedEnd=committed,
                        Session=NpDownloadStream.U(h,12),FormatVersion=version,ProbeMask=NpDownloadStream.U(h,16),
                        CameraHz=NpDownloadStream.U(h,112),MicrophoneHz=NpDownloadStream.U(h,116),
                        LaneBytes=NpDownloadStream.U(h,32),ApHz=NpDownloadStream.U(h,24),LfpHz=NpDownloadStream.U(h,28),
                        Chunks=chunks,State=state,Fault=fault,RecoveryNotice=Notice};
                }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is OverflowException) {
                    records.Add(Unavailable(start,"Metadata walk stopped: "+ex.Message+" No later space was inferred free."));
                    break;
                }
                Cancel(cancelled);
                var existing=records.Where(r=>r.Downloadable && r.Start==start).ToArray();
                if(existing.Any(r=>!Same(r,found))) {
                    records.Add(Unavailable(start,"Header/footer reconstruction conflicts with a readable catalog record. The original record and later readable entries were retained; the walk stopped."));
                    break;
                }
                if(existing.Length==0) {
                    records.RemoveAll(r=>!r.Downloadable && r.Start==start);
                    records.Add(found);
                }
                if(progress!=null)progress((int)Math.Min(99,start*100/capacity));
                if(!found.Complete) {
                    records.Add(Unavailable(0,"Interrupted reservation preserved through LBA "+found.End+"; metadata walk stopped without reclaiming or scanning its tail."));
                    break;
                }
                start=checked((found.End+8191)/8192*8192);
                if(count==MaxRecords-1)records.Add(Unavailable(start,"Metadata walk reached the 64512-record bound; later entries were not scanned."));
            }
            Cancel(cancelled);
            records.Sort((a,b)=>a.Start!=b.Start?a.Start.CompareTo(b.Start):b.Downloadable.CompareTo(a.Downloadable));
            if(progress!=null)progress(100);
            return records;
        }
        static void Cancel(Func<bool> cancelled){if(cancelled!=null && cancelled())throw new OperationCanceledException();}
        static bool IsStoreV2Source(Stream source,uint capacity) {
            if(capacity<=8192)return false;
            Func<byte[],bool> recognized=bytes=>bytes!=null && bytes.Length==512 &&
                Encoding.ASCII.GetString(bytes,0,16)=="NP_FILESYSTEM_V2" && Encoding.ASCII.GetString(bytes,32,9)=="NP_CE64_2";
            byte[] primary=null;try{primary=NpDownloadCatalog.Sector(source,16);}catch(IOException){}
            if(NpDownloadCatalog.IsCatalogIdentity(primary))return false;
            // Torn FS2 authority is not permission to adopt an old payload as
            // a legacy catalog. Recognition is deliberately not mount validity.
            if(recognized(primary))return true;
            try{return recognized(NpDownloadCatalog.Sector(source,24));}catch(IOException){return false;}
        }
        static void Header(byte[] bytes,string magic){if(Encoding.ASCII.GetString(bytes,0,8)!=magic || NpDownloadStream.U(bytes,508)!=NpDownloadStream.Crc(bytes,0,508))throw new InvalidDataException("Unknown/torn V1 recording header or footer.");}
        static NpDownloadRecord Unavailable(long start,string error){return new NpDownloadRecord{Start=start,ValidationError=error,RecoveryNotice=Notice};}
        static bool Same(NpDownloadRecord a,NpDownloadRecord b){return a.Start==b.Start && (a.End==b.End || b.Complete && a.End>=b.End && a.End<=b.CatalogEnd) && a.Session==b.Session && a.FormatVersion==b.FormatVersion && a.ProbeMask==b.ProbeMask && a.LaneBytes==b.LaneBytes && a.ApHz==b.ApHz && a.LfpHz==b.LfpHz && a.CameraHz==b.CameraHz && a.MicrophoneHz==b.MicrophoneHz && a.Chunks==b.Chunks && a.State==b.State && a.Fault==b.Fault;}

        // Both existing catalog inspection and the bounded header walk read
        // sectors through this cache: no payload reads or retry of failed I/O.
        sealed class MetadataSource:Stream {
            readonly Stream card;readonly long length;readonly Func<bool> cancelled;
            readonly Dictionary<long,byte[]> sectors=new Dictionary<long,byte[]>();
            readonly Dictionary<long,IOException> failures=new Dictionary<long,IOException>();
            long position;
            internal MetadataSource(Stream card,long length,Func<bool> cancelled){this.card=card;this.length=length;this.cancelled=cancelled;}
            public override int Read(byte[] bytes,int offset,int count) {
                Cancel(cancelled);
                if(bytes==null || offset<0 || count<0 || offset>bytes.Length-count)throw new ArgumentException("Invalid metadata read buffer.");
                if(position<0 || position>length)throw new IOException("Metadata seek outside the source.");
                int total=(int)Math.Min(count,length-position),copied=0;
                while(copied<total) {
                    Cancel(cancelled);long lba=position/512;byte[] sector;IOException failure;
                    if(failures.TryGetValue(lba,out failure))throw failure;
                    if(!sectors.TryGetValue(lba,out sector)) {
                        sector=new byte[512];card.Position=checked(lba*512);int read=0;
                        try {
                            while(read<512){Cancel(cancelled);int n=card.Read(sector,read,512-read);if(n<=0)throw new EndOfStreamException("Short metadata sector at LBA "+lba);read+=n;}
                        }catch(IOException ex){failures.Add(lba,ex);throw;}
                        sectors.Add(lba,sector);
                    }
                    int ncopy=Math.Min(total-copied,512-(int)(position%512));
                    Buffer.BlockCopy(sector,(int)(position%512),bytes,offset+copied,ncopy);position+=ncopy;copied+=ncopy;
                }
                return copied;
            }
            public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}
            public override long Length{get{return length;}}public override long Position{get{return position;}set{if(value<0 || value>length)throw new IOException("Metadata seek outside the source.");position=value;}}
            public override long Seek(long offset,SeekOrigin origin){Position=checked((origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length)+offset);return position;}
            public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException("Read-only recovery.");}
            public override void Write(byte[] bytes,int offset,int count){throw new NotSupportedException("Read-only recovery.");}
        }
    }
}
