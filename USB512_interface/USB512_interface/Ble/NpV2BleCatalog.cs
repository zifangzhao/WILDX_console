using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpV2BleCatalogInfo
    {
        internal readonly uint Token,Count,Next,Capacity,CidCrc,Flags,LastOrdinal,LastStart,LastEnd,LastCookie;
        internal NpV2BleCatalogInfo(byte[] body)
        {
            Token=NpSdGeometry.U(body,20);Count=NpSdGeometry.U(body,24);Next=NpSdGeometry.U(body,28);Capacity=NpSdGeometry.U(body,32);
            CidCrc=NpSdGeometry.U(body,36);Flags=NpSdGeometry.U(body,40);LastOrdinal=NpSdGeometry.U(body,44);
            LastStart=NpSdGeometry.U(body,48);LastEnd=NpSdGeometry.U(body,52);LastCookie=NpSdGeometry.U(body,56);
        }
        internal bool SameAs(NpV2BleCatalogInfo other)
        {
            return other!=null && Token==other.Token && Count==other.Count && Next==other.Next && Capacity==other.Capacity && CidCrc==other.CidCrc &&
                Flags==other.Flags && LastOrdinal==other.LastOrdinal && LastStart==other.LastStart && LastEnd==other.LastEnd && LastCookie==other.LastCookie;
        }
    }

    // Version2's CRC-correlated catalog protocol. Tokens/cookies are opaque
    // firmware snapshots; the host does not have the CID/tail sectors to recompute them.
    internal sealed class NpV2BleCatalog
    {
        internal const uint Capability=0x00400000,Last=1U<<16,DeleteAllowed=1U<<17,MetadataValid=1U<<18,Incomplete=1U<<19;
        internal const uint CatalogFull=1U<<16,CatalogRecovery=1U<<17,CatalogRecoverable=1U<<18,CatalogQuarantinable=1U<<19,
            CatalogDegraded=0x00800000,CatalogRewind=0x04000000,CatalogFlags=0x048f0000;
        internal const uint MaxRecords=1024U*63U;
        internal const string AppendDiagnostic="Complete* describes stored metadata only; download verifies payload and logical coverage. Incomplete recordings may contain gaps. Physical/prefix/high-water counts are not reported by this BLE summary.";
        readonly Func<Task<NpSdStatus>> readStatus;
        readonly Func<byte[],Task<byte[]>> exchange;
        readonly Func<byte[],Task<NpSdStatus>> send;
        readonly Action target;
        static int sequence=Environment.TickCount;
        static uint Next(){uint id=unchecked((uint)Interlocked.Increment(ref sequence));return id==0?Next():id;}
        internal NpV2BleCatalog(Func<Task<NpSdStatus>> status,Func<byte[],Task<byte[]>> read,Func<byte[],Task<NpSdStatus>> mutate,Action checkTarget)
        {readStatus=status;exchange=read;send=mutate;target=checkTarget;}
        internal static bool Supported(NpSdStatus s){return s!=null && (s.Flags&Capability)!=0;}
        internal static bool CanRead(NpSdStatus s){return Supported(s) && !s.Busy && (s.Flags&16)!=0 && s.Capacity>8192 && (s.State==1 || s.State==5 || s.State==7);}
        static void Put(byte[] body,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,body,at,4);}
        static byte[] Request(uint operation,uint id)
        {
            if(id==0 || operation<9 || operation>11)throw new ArgumentException("V2 catalog operation/transaction required.");
            var body=new byte[64];Put(body,0,NpV2SdControl.RequestMagic);Put(body,4,2);Put(body,8,operation);Put(body,12,id);return body;
        }
        static byte[] Seal(byte[] body){Put(body,60,NpSdControl.Crc(body,60));return body;}
        internal static byte[] InfoRequest(uint id){return Seal(Request(9,id));}
        internal static byte[] EntryRequest(uint id,NpV2BleCatalogInfo info,uint ordinal)
        {
            if(info==null || ordinal>=info.Count)throw new ArgumentOutOfRangeException("ordinal");
            var body=Request(10,id);Put(body,16,info.Token);Put(body,20,info.Count);Put(body,24,ordinal);return Seal(body);
        }
        internal static byte[] DeleteRequest(uint id,NpBleCatalog catalog)
        {
            if(catalog==null || catalog.V2==null || !catalog.CanDeleteLast || catalog.Records.Count!=catalog.V2.Count)throw new IOException("A deletable last recording and confirmed V2 snapshot are required.");
            var info=catalog.V2;var last=catalog.Records[catalog.Records.Count-1];
            if(last.Id!=info.Count || last.Start!=info.LastStart || last.End!=info.LastEnd || last.Cookie!=info.LastCookie)throw new IOException("V2 delete target differs from its snapshot.");
            var body=Request(11,id);Put(body,16,info.Token);Put(body,20,info.Count);Put(body,24,info.LastOrdinal);
            Put(body,28,last.Start);Put(body,32,last.End);Put(body,36,last.Cookie);Put(body,40,0x4c44324e);return Seal(body);
        }
        static void Reply(byte[] body,uint operation,uint id)
        {
            if(id==0 || body==null || body.Length!=64 || NpSdGeometry.U(body,0)!=NpV2SdControl.ReplyMagic || NpSdGeometry.U(body,4)!=2 ||
                NpSdGeometry.U(body,8)!=operation || NpSdGeometry.U(body,12)!=id || NpSdGeometry.U(body,60)!=NpSdControl.Crc(body,60))throw new IOException("Invalid V2 catalog receipt.");
            uint error=NpSdGeometry.U(body,16);
            if(error!=0){
                // Errors retain the normal STATUS layout, never INFO/ENTRY fields.
                var status=NpV2SdControl.Parse(body,operation,id);
                throw new IOException(error==9?"The V2 catalog snapshot changed. Refresh recordings; no command replayed.":error==10?"V2 catalog I/O failed. Read device status; no command replayed.":"V2 catalog operation rejected: "+error+"; device/command error "+status.Fault+".");
            }
        }
        internal static NpV2BleCatalogInfo ParseInfo(byte[] body,uint id)
        {
            Reply(body,9,id);var info=new NpV2BleCatalogInfo(body);
            if(info.Count>MaxRecords || info.Capacity<=8192 || info.Next<8192 || info.Next%8192!=0 ||
                info.Next>((ulong)info.Capacity+8191)/8192*8192 || (info.Flags&~CatalogFlags)!=0 ||
                ((info.Flags&(CatalogRecoverable|CatalogQuarantinable))!=0 && (info.Flags&CatalogRecovery)==0) ||
                ((info.Flags&CatalogRewind)!=0 && (info.Flags&(CatalogDegraded|CatalogRecovery))!=(CatalogDegraded|CatalogRecovery)))throw new IOException("Invalid V2 catalog geometry/flags.");
            if(info.Count==0){
                if(info.LastOrdinal!=UInt32.MaxValue || info.LastStart!=0 || info.LastEnd!=0 || info.LastCookie!=0 || info.Next!=8192)throw new IOException("Invalid empty V2 catalog.");
            }else if(info.LastOrdinal!=info.Count-1 || info.LastStart<8192 || info.LastStart%8192!=0 || info.LastEnd<=((ulong)info.LastStart+4) ||
                info.LastEnd>info.Capacity || ((ulong)info.LastEnd+8191)/8192*8192!=info.Next)throw new IOException("Invalid V2 catalog tail.");
            return info;
        }
        internal static NpBleRecord ParseEntry(byte[] body,uint id,NpV2BleCatalogInfo info,uint ordinal,uint expectedStart)
        {
            Reply(body,10,id);
            if(info==null || ordinal>=info.Count || NpSdGeometry.U(body,20)!=info.Token || NpSdGeometry.U(body,24)!=info.Count || NpSdGeometry.U(body,28)!=ordinal)throw new IOException("V2 catalog entry snapshot/ordinal mismatch.");
            var entry=new NpBleRecord{Id=ordinal+1,Start=NpSdGeometry.U(body,32),End=NpSdGeometry.U(body,36),Cookie=NpSdGeometry.U(body,40),Session=NpSdGeometry.U(body,44),Hz=NpSdGeometry.U(body,48),Mask=NpSdGeometry.U(body,52),Flags=NpSdGeometry.U(body,56)};
            uint format=entry.Flags&255,state=(entry.Flags>>8)&255;bool last=ordinal==info.Count-1;
            if(entry.Start!=expectedStart || entry.End<=(ulong)entry.Start+4 || entry.End>info.Capacity || (entry.Flags&0xfff00000U)!=0 ||
                (format!=0 && format!=8 && format!=11 && format!=12 && format!=13 && format!=14 && format!=15) || (state!=0 && state!=1 && state!=3 && state!=4) ||
                ((entry.Flags&Last)!=0)!=last)throw new IOException("Invalid V2 catalog entry geometry/flags.");
            if((entry.Flags&MetadataValid)!=0 && (format==0 || entry.Session==0 || (format==8?entry.Hz!=0 || entry.Mask!=0:!NpV2SdControl.IsProfile(entry.Mask,entry.Hz))))throw new IOException("Invalid V2 recording metadata summary.");
            if((format==14 || format==15) && (entry.Flags&MetadataValid)!=0 &&
                (state==0 || (state==3)==((entry.Flags&Incomplete)!=0)))
                throw new IOException("Contradictory V2 stored state/incomplete flags. Refresh recordings; no payload completion was inferred.");
            if((entry.Flags&DeleteAllowed)!=0 && (!last || (entry.Flags&MetadataValid)==0 || (state!=3 && state!=4) || (info.Flags&~CatalogFull)!=0))throw new IOException("Invalid V2 delete eligibility.");
            if(format==15 && (entry.Flags&DeleteAllowed)!=0 && (state!=3 || (entry.Flags&Incomplete)!=0))throw new IOException("Incomplete V2 closed-loop recordings cannot be deleted through this catalog.");
            if(last && (entry.Start!=info.LastStart || entry.End!=info.LastEnd || entry.Cookie!=info.LastCookie))throw new IOException("V2 catalog tail cookie/extent changed.");
            return entry;
        }
        internal static string RecordStatus(NpBleRecord record)
        {
            uint state=(record.Flags>>8)&255;
            if((record.Flags&255)==14 || (record.Flags&255)==15){
                if((record.Flags&MetadataValid)==0)return "Unverified";
                return state==3 && (record.Flags&Incomplete)==0?"Complete*":"Incomplete";
            }
            return state==3?"Complete":state==4?"Failed":state==1?"Reserved":"Unverified";
        }
        internal static bool SameRecord(NpBleRecord a,NpBleRecord b,bool compareFlags=true)
        {return a.Id==b.Id && a.Start==b.Start && a.End==b.End && a.Cookie==b.Cookie && a.Session==b.Session && a.Hz==b.Hz && a.Mask==b.Mask && (!compareFlags || a.Flags==b.Flags);}
        async Task<NpV2BleCatalogInfo> Info()
        {target();uint id=Next();var body=await exchange(InfoRequest(id));target();return ParseInfo(body,id);}
        internal async Task<NpBleCatalog> Read()
        {
            target();var status=await readStatus();target();
            if(Supported(status) && status.State==4)throw new IOException("Recording is still finalizing. The list will refresh when it finishes.");
            if(!CanRead(status))throw new IOException(Supported(status)?"Stop recording before listing V2 recordings.":"This V2 firmware does not advertise BLE file management.");
            var info=await Info();
            if(info.Capacity!=status.Capacity || info.Next!=status.Start || info.Flags!=(status.Flags&CatalogFlags))throw new IOException("V2 card/catalog changed. Refresh recordings.");
            var catalog=new NpBleCatalog{V2=info,Capacity=info.Capacity,NextStart=info.Next};uint start=8192;
            for(uint ordinal=0;ordinal<info.Count;ordinal++){
                uint id=Next();target();var body=await exchange(EntryRequest(id,info,ordinal));target();
                var entry=ParseEntry(body,id,info,ordinal,start);catalog.Records.Add(entry);
                ulong next=((ulong)entry.End+8191)/8192*8192;if(next>UInt32.MaxValue)throw new IOException("V2 catalog extent exceeds the address range.");start=(uint)next;
            }
            if(start!=info.Next || !info.SameAs(await Info()))throw new IOException("V2 catalog changed while listing. Refresh recordings.");
            return catalog;
        }
        internal async Task<NpBleCatalog> Delete(NpBleCatalog confirmed)
        {
            if(confirmed==null || confirmed.V2==null || !confirmed.CanDeleteLast)throw new IOException("The last V2 recording is not eligible for deletion.");
            var fresh=await Read();if(!confirmed.SameAs(fresh))throw new IOException("V2 catalog changed. Refresh and confirm the last recording again.");
            target();var status=await readStatus();target();
            if(!CanRead(status) || NpV2SdControl.NeedsRecovery(status) || status.Capacity!=fresh.Capacity || status.Start!=fresh.NextStart)throw new IOException("Stopped, unchanged V2 storage without pending recovery is required.");
            uint id=NpV2SdControl.NextId(status);var ack=await send(DeleteRequest(id,fresh));target();
            if(ack.Error!=0 || ack.LastId!=id)throw new IOException("V2 delete was not acknowledged. No command replayed.");
            var wait=Stopwatch.StartNew();
            do{
                status=await readStatus();target();
                if(status.LastId!=id)throw new IOException("V2 mutation receipt changed during deletion. Refresh recordings.");
                if(!status.Busy)break;
                if(wait.ElapsedMilliseconds>=10000)throw new TimeoutException("V2 delete outcome not confirmed. Refresh recordings; no command replayed.");
                await Task.Delay(200);
            }while(true);
            if(!CanRead(status) || status.Error!=0 || status.Fault!=0)throw new IOException("V2 delete execution failed: "+status.Fault+". Refresh recordings; no command replayed.");
            var result=await Read();
            if(result.Capacity!=fresh.Capacity || result.V2.CidCrc!=fresh.V2.CidCrc || result.Records.Count!=fresh.Records.Count-1)throw new IOException("V2 delete result does not match the confirmed recording. No command replayed.");
            for(int i=0;i<result.Records.Count;i++){
                var before=fresh.Records[i];var after=result.Records[i];
                // The new tail legitimately gains LAST/delete eligibility.
                if(!SameRecord(before,after,false) || ((before.Flags^after.Flags)&~(Last|DeleteAllowed))!=0)throw new IOException("An earlier V2 recording changed during deletion.");
            }
            return result;
        }
    }
}
