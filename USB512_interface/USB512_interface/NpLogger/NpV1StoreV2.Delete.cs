using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CE32_console
{
    internal static partial class NpV1StoreV2
    {
        // Read-only projection of an already authorized on-device deletion.
        // This does not repair the card or authorize reuse of its space. A raw
        // PC reader cannot independently query the card CID; firmware recovery
        // also verifies the physical CID before writing either checkpoint.
        static void Put(byte[] b,int at,uint value){BitConverter.GetBytes(value).CopyTo(b,at);}
        static void Put64(byte[] b,int at,ulong value){BitConverter.GetBytes(value).CopyTo(b,at);}
        static void Seal(byte[] b){Put(b,508,NpDownloadStream.Crc(b,0,508));}
        static bool Zero(byte[] b,int at,int count){for(int i=at;i<at+count;i++)if(b[i]!=0)return false;return true;}
        static byte[] DeleteCheckpoint(byte[] journal,int at)
        {
            var b=new byte[512];Encoding.ASCII.GetBytes("NPS2CP01").CopyTo(b,0);Put(b,8,2);Put(b,12,U(journal,16));Put64(b,16,U64(journal,24));
            Buffer.BlockCopy(journal,at,b,24,32);Seal(b);return b;
        }
        static byte[] DeleteRecord(byte[] journal,int at)
        {
            var b=new byte[512];Encoding.ASCII.GetBytes("NPS2REC1").CopyTo(b,0);Put(b,8,2);Put(b,12,U(journal,16));Put64(b,16,U64(journal,24));
            Buffer.BlockCopy(journal,at,b,24,32);Seal(b);return b;
        }
        static Store State(byte[] b)
        {return new Store{Ready=true,Capacity=U(b,12),Epoch=U64(b,16),Generation=U64(b,24),Frontier=U(b,32),Count=U(b,36),Start=U(b,40),End=U(b,44),Session=U(b,48),Open=U(b,52)};}
        static byte[] SelectBefore(byte[] j,byte[][] before)
        {
            uint mask=U(j,20);byte[] selected=null;
            for(int n=0;n<2;n++){
                if((mask&(1U<<n))==0){if(!Zero(j,56+n*32,32)||U(j,224+n*4)!=0)throw new InvalidDataException("Invalid deletion checkpoint preimage.");continue;}
                var b=before[n];if(!CheckpointValid(b,U(j,16),U64(j,24)) || U(b,508)!=U(j,224+n*4))throw new InvalidDataException("Delete checkpoint preimage CRC/state is invalid.");
                if(selected==null || U64(b,24)>U64(selected,24))selected=b;
            }
            if(mask==3){var a=before[0];var b=before[1];ulong ga=U64(a,24),gb=U64(b,24);var hi=ga>=gb?a:b;var lo=ga>=gb?b:a;
                if(ga==gb?!a.SequenceEqual(b):U64(hi,24)-U64(lo,24)!=1 || U(hi,32)<U(lo,32) || U(hi,36)<U(lo,36) || U(hi,36)-U(lo,36)>1)
                    throw new InvalidDataException("Conflicting deletion checkpoint preimages.");
            }
            return selected;
        }
        static bool Intent(byte[] b)
        {return Magic(b,0,"NPS2DL01") && Crc(b);}
        static bool DeleteRecordMatches(Stream card,Store old,uint index,byte[] expected,Func<bool> cancelled)
        {
            var selected=ReadRecord(card,old,index,cancelled);
            if(!selected.Available || !expected.SequenceEqual(selected.Raw))return false;
            for(int n=0;n<2;n++){
                byte[] raw=Sector(card,128U+index*2U+(uint)n,cancelled);
                if(!RecordValid(raw,old,index))continue;
                var canonical=new byte[512];Encoding.ASCII.GetBytes("NPS2REC1").CopyTo(canonical,0);Put(canonical,8,2);Put(canonical,12,old.Capacity);Put64(canonical,16,old.Epoch);
                Buffer.BlockCopy(raw,24,canonical,24,32);Seal(canonical);
                if(!raw.SequenceEqual(canonical))return false;
            }
            return true;
        }
        static byte[] DeleteProjection(Stream card,uint capacity,ulong epoch,byte[] cp0,byte[] cp1,Func<bool> cancelled,out bool pending,out string note)
        {
            pending=false;note=null;
            byte[] ja=Sector(card,114,cancelled),jb=Sector(card,115,cancelled);bool va=Intent(ja),vb=Intent(jb);
            if(!va && !vb){
                if(Magic(ja,0,"NPS2DL01") || Magic(jb,0,"NPS2DL01"))note="An uncommitted/torn deletion journal was ignored only when ordinary checkpoints remain valid. Device recovery is required before writing.";
                return null;
            }
            if(va && vb && !ja.SequenceEqual(jb))throw new InvalidDataException("Conflicting V1 deletion journals. Use updated device firmware for recovery; card unchanged.");
            byte[] j=va?ja:jb;
            if(U(j,8)!=1 || U(j,12)!=1 || U64(j,24)==0 || U64(j,32)==0 || U(j,20)<1 || U(j,20)>3 ||
                (U(j,232)&~3U)!=0 || !Zero(j,236,272) || Zero(j,40,16) || j.Skip(40).Take(16).All(x=>x==255))
                throw new InvalidDataException("Unsupported V1 deletion intent. Card unchanged; device recovery required.");
            // A different random format epoch cannot authorize this card's
            // rollback. The normal mount below must still validate its roots.
            if(U64(j,24)!=epoch){note="Stale deletion intent from another format epoch was not applied. Current checkpoints are read-only.";return null;}
            if(U(j,16)!=capacity)throw new InvalidDataException("V1 deletion intent capacity differs from the current card.");
            var before=new[]{DeleteCheckpoint(j,56),DeleteCheckpoint(j,88)};byte[] selected=SelectBefore(j,before),after=DeleteCheckpoint(j,120);
            var old=State(selected);
            if(old.Count==0 || old.Generation==ulong.MaxValue || !CheckpointValid(after,capacity,epoch) || U64(after,24)!=old.Generation+1 ||
                U(after,36)!=old.Count-1 || U(after,32)!=old.Start)
                throw new InvalidDataException("Delete Last checkpoint transition is invalid. Nothing repaired.");
            byte[] last=DeleteRecord(j,152),previous=DeleteRecord(j,184);
            if(U(last,508)!=U(j,216) || !RecordValid(last,old,old.Count-1) || U(last,36)!=old.Start || U(last,48)!=old.Session || U64(last,24)>old.Generation+1)
                throw new InvalidDataException("Delete Last descriptor binding is invalid.");
            if(!DeleteRecordMatches(card,old,old.Count-1,last,cancelled))throw new InvalidDataException("Deleted-record descriptor changed. Device recovery required.");
            uint state=U(last,52),flags=state==1?1U:state==3?2U:0U;
            if(U(j,232)!=flags)throw new InvalidDataException("Delete Last state flags conflict.");
            if(old.Count==1){
                if(!Zero(j,184,32) || U(j,220)!=0 || U(after,32)!=DataStart || U(after,40)!=0 || U(after,44)!=0 || U(after,48)!=0 || U(after,52)!=0)
                    throw new InvalidDataException("Invalid empty-card deletion checkpoint.");
            }else{
                if(U(previous,508)!=U(j,220) || !RecordValid(previous,old,old.Count-2) || U(previous,40)!=old.Start || U64(previous,24)>old.Generation ||
                    U(after,40)!=U(previous,36) || U(after,44)!=old.Start || U(after,48)!=U(previous,48) || U(after,52)!=(U(previous,52)==1?1U:0U))
                    throw new InvalidDataException("Previous-record checkpoint binding is invalid.");
                if(!DeleteRecordMatches(card,old,old.Count-2,previous,cancelled))throw new InvalidDataException("Earlier-record descriptor changed. Device recovery required.");
            }
            byte[][] actual={cp0,cp1};int valid=0;
            for(int n=0;n<2;n++)if(CheckpointValid(actual[n],capacity,epoch)){
                valid++;
                if(!actual[n].SequenceEqual(after) && ((U(j,20)&(1U<<n))==0 || !actual[n].SequenceEqual(before[n])))
                    throw new InvalidDataException("V1 checkpoint advanced outside the confirmed deletion. No rollback applied.");
            }
            if(valid==0)throw new InvalidDataException("Deletion recovery has no valid matching allocation checkpoint.");
            pending=true;note="Read-only view of a pending confirmed Delete Last. Updated logger firmware must finish recovery before another recording; this reader wrote no sectors and did not verify physical card CID.";
            return after;
        }
    }
}
