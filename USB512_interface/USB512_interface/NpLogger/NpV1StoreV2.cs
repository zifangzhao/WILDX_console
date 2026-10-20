using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CE32_console {
    // V1 filesystem version2, not the V2 logger or a payload-format version.
    // Mirrors np_store_v2.h read semantics. No adoption, repair or card writes.
    internal static class NpV1StoreV2 {
        internal const uint FilesystemVersion=2,DataStart=8192,MaxRecords=4032;
        internal const int Ok=0,Invalid=-1,Missing=-3;
        internal sealed class Store {
            internal bool Ready;
            internal ulong Epoch,Generation;
            internal uint Capacity,Frontier,Count,Start,End,Session,Open;
            internal uint IdentityLba,CheckpointLba;
            internal byte[] Identity,Checkpoint;
            internal readonly List<Entry> Entries=new List<Entry>();
        }
        internal sealed class Entry {
            internal ulong Generation;
            internal uint Index,Start,ReservedEnd,CommittedEnd,Session,State;
            internal int Result;
            internal byte[] Raw;
            internal string ValidationError;
            internal bool Available{get{return Result==Ok;}}
        }
        static uint U(byte[] b,int offset){return NpDownloadStream.U(b,offset);}
        static ulong U64(byte[] b,int offset){return BitConverter.ToUInt64(b,offset);}
        static bool Magic(byte[] b,int offset,string magic){return Encoding.ASCII.GetString(b,offset,magic.Length)==magic;}
        static bool Crc(byte[] b){return b!=null && b.Length==512 && U(b,508)==NpDownloadStream.Crc(b,0,508);}
        static bool Aligned(uint lba){return lba>=DataStart && (lba&8191U)==0;}
        static void Cancel(Func<bool> cancelled){if(cancelled!=null && cancelled())throw new OperationCanceledException();}
        static uint Capacity(Stream card) {
            if(card==null)throw new ArgumentNullException("card");
            if(!card.CanRead || !card.CanSeek)throw new IOException("V1 store requires a readable, seekable source.");
            long bytes=card.Length;
            if(bytes%512!=0 || bytes/512<=DataStart || bytes/512>uint.MaxValue)
                throw new InvalidDataException("Invalid V1 store source capacity.");
            return (uint)(bytes/512);
        }
        static byte[] Sector(Stream card,uint lba,Func<bool> cancelled) {
            Cancel(cancelled);byte[] bytes;
            try{bytes=NpDownloadCatalog.Sector(card,lba);}
            catch(IOException){Cancel(cancelled);return null;}
            Cancel(cancelled);return bytes;
        }
        internal static bool IdentityValid(byte[] b,uint capacity) {
            return Crc(b) && Magic(b,0,"NP_FILESYSTEM_V2") && Magic(b,32,"NP_CE64_2") &&
                U(b,48)==FilesystemVersion && U(b,52)==capacity && U64(b,56)!=0 &&
                U(b,64)==DataStart && U(b,68)==MaxRecords;
        }
        static bool CheckpointValid(byte[] b,uint capacity,ulong epoch) {
            if(!Crc(b) || !Magic(b,0,"NPS2CP01") || U(b,8)!=FilesystemVersion || U(b,12)!=capacity ||
               U64(b,16)!=epoch || U64(b,24)==0 || !Aligned(U(b,32)) || U(b,32)>capacity ||
               U(b,36)>MaxRecords || U(b,52)>1)return false;
            if(U(b,36)==0)return U(b,32)==DataStart && U(b,40)==0 && U(b,44)==0 && U(b,48)==0 && U(b,52)==0;
            return Aligned(U(b,40)) && Aligned(U(b,44)) && U(b,40)<U(b,44) && U(b,44)==U(b,32) && U(b,48)!=0;
        }
        internal static Store Mount(Stream card,Func<bool> cancelled=null) {
            Cancel(cancelled);uint capacity=Capacity(card);
            byte[] a=Sector(card,16,cancelled),b=Sector(card,24,cancelled);
            bool va=IdentityValid(a,capacity),vb=IdentityValid(b,capacity);
            if((!va && !vb) || va && vb && !a.SequenceEqual(b))
                throw new InvalidDataException("V1 filesystem2 identity copies are unavailable, unsupported or conflicting.");
            byte[] identity=va?a:b;uint identityLba=va?16U:24U;ulong epoch=U64(identity,56);
            a=Sector(card,112,cancelled);b=Sector(card,113,cancelled);
            va=CheckpointValid(a,capacity,epoch);vb=CheckpointValid(b,capacity,epoch);
            if(!va && !vb)throw new InvalidDataException("No valid matching V1 filesystem2 allocation checkpoint.");
            byte[] selected;uint checkpointLba;
            if(va && vb) {
                ulong ga=U64(a,24),gb=U64(b,24);byte[] hi=ga>=gb?a:b,lo=ga>=gb?b:a;
                if(ga==gb?!a.SequenceEqual(b):U64(hi,24)-U64(lo,24)!=1 || U(hi,32)<U(lo,32) ||
                    U(hi,36)<U(lo,36) || U(hi,36)-U(lo,36)>1)
                    throw new InvalidDataException("V1 filesystem2 checkpoint generations/frontier/count conflict.");
                selected=hi;checkpointLba=ga>=gb?112U:113U;
            }else{selected=va?a:b;checkpointLba=va?112U:113U;}
            return new Store{Ready=true,Capacity=capacity,Epoch=epoch,Generation=U64(selected,24),Frontier=U(selected,32),Count=U(selected,36),
                Start=U(selected,40),End=U(selected,44),Session=U(selected,48),Open=U(selected,52),
                IdentityLba=identityLba,CheckpointLba=checkpointLba,Identity=identity,Checkpoint=selected};
        }
        static bool RecordValid(byte[] b,Store s,uint index) {
            return Crc(b) && Magic(b,0,"NPS2REC1") && U(b,8)==FilesystemVersion && U(b,12)==s.Capacity &&
                U64(b,16)==s.Epoch && U64(b,24)!=0 && U(b,32)==index &&
                Aligned(U(b,36)) && Aligned(U(b,40)) && U(b,36)<U(b,40) && U(b,40)<=s.Frontier &&
                U(b,44)>=U(b,36) && U(b,44)<=U(b,40) && U(b,48)!=0 && U(b,52)>=1 && U(b,52)<=3;
        }
        internal static Entry ReadRecord(Stream card,Store store,uint index,Func<bool> cancelled=null) {
            Cancel(cancelled);
            if(store==null || !store.Ready || index>=store.Count || index>=MaxRecords)
                return new Entry{Index=index,Result=Invalid,ValidationError="Record index is outside the mounted checkpoint count."};
            if(Capacity(card)!=store.Capacity)throw new InvalidDataException("V1 store source capacity changed.");
            uint lba=128U+index*2U;
            byte[] a=Sector(card,lba,cancelled),b=Sector(card,lba+1U,cancelled);
            bool va=RecordValid(a,store,index),vb=RecordValid(b,store,index);
            if(!va && !vb)return new Entry{Index=index,Result=Missing,ValidationError="Both record copies are unavailable, stale or invalid; allocation checkpoint is unchanged."};
            if(va && vb && U64(a,24)==U64(b,24) && !a.SequenceEqual(b))
                return new Entry{Index=index,Result=Invalid,ValidationError="Equal-generation record copies conflict; allocation checkpoint is unchanged."};
            if(va && vb && U64(a,24)!=U64(b,24)) {
                byte[] hi=U64(a,24)>U64(b,24)?a:b,lo=ReferenceEquals(hi,a)?b:a;
                if(U(hi,36)!=U(lo,36) || U(hi,48)!=U(lo,48) || U(hi,40)<U(lo,40) || U(hi,44)<U(lo,44) || U(lo,52)!=1)
                    return new Entry{Index=index,Result=Invalid,ValidationError="Record generations change immutable fields, retreat or mutate a terminal record; allocation checkpoint is unchanged."};
            }
            // Record-before-checkpoint publication permits an ahead record.
            // Unlike checkpoint copies, record copies have no adjacency rule.
            byte[] selected=va && (!vb || U64(a,24)>=U64(b,24))?a:b;
            return new Entry{Index=index,Result=Ok,Raw=selected,Generation=U64(selected,24),Start=U(selected,36),
                ReservedEnd=U(selected,40),CommittedEnd=U(selected,44),Session=U(selected,48),State=U(selected,52)};
        }
        internal static Store Read(Stream card,Func<bool> cancelled=null) {
            Store store=Mount(card,cancelled);
            for(uint index=0;index<store.Count;index++)store.Entries.Add(ReadRecord(card,store,index,cancelled));
            Cancel(cancelled);return store;
        }
    }
    // Optional in legacy metadata; mandatory at filesystem2 catalog admission.
    // Existing signature/CRC/geometry checks remain owned by each caller.
    internal static class NpV1StoreBinding {
        static bool Read(byte[] bytes,int offset,out ulong epoch,out uint index) {
            epoch=0;index=0;
            if(bytes==null || offset<0 || offset>bytes.Length-512)throw new InvalidDataException("Incomplete V1 filesystem binding metadata.");
            if(Encoding.ASCII.GetString(bytes,offset+464,8)!="NPS2BND1")return false;
            epoch=BitConverter.ToUInt64(bytes,offset+472);index=NpDownloadStream.U(bytes,offset+480);
            if(epoch==0 || index>=NpV1StoreV2.MaxRecords)throw new InvalidDataException("Invalid V1 filesystem2 epoch/index binding.");
            return true;
        }
        static void Require(byte[] bytes,int offset,ulong epoch,uint index) {
            ulong actualEpoch;uint actualIndex;
            if(!Read(bytes,offset,out actualEpoch,out actualIndex) || actualEpoch!=epoch || actualIndex!=index)
                throw new InvalidDataException("V1 filesystem2 epoch/index binding mismatch; stale or unbound metadata was not accepted.");
        }
        internal static void RequireRecord(byte[] header,byte[] footer,ulong epoch,uint index) {
            if(epoch==0 || index>=NpV1StoreV2.MaxRecords)throw new InvalidDataException("Invalid expected V1 filesystem2 binding.");
            Require(header,0,epoch,index);Require(footer,0,epoch,index);
        }
        internal static void Validate(byte[] descriptor,byte[] metadata,int offset=0) {
            ulong epoch;uint index;
            if(Read(descriptor,0,out epoch,out index))Require(metadata,offset,epoch,index);
            else Read(metadata,offset,out epoch,out index); // Reject malformed known bindings even on a legacy source.
        }
    }
}
