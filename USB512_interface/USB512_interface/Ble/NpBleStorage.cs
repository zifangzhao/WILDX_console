using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpBleRecord
    {
        internal uint Id,Start,End,Cookie,Session,Hz,Mask,Flags;
        internal double MiB{get{return ((ulong)End-Start)*512.0/1048576;}}
    }
    internal sealed class NpBleCatalog
    {
        internal readonly List<NpBleRecord> Records=new List<NpBleRecord>();
        internal readonly List<byte[]> Pages=new List<byte[]>();
        internal uint Capacity,NextStart;
        internal bool V1CanDeleteLast;
        internal bool V1ReadOnly,V1Full;
        internal string V1Warning;
        internal const int ReadOnlyPageLimit=16;
        internal NpV2BleCatalogInfo V2;
        internal bool CanDeleteLast{get{return Records.Count!=0 && (V2==null?!V1ReadOnly&&V1CanDeleteLast:(Records[Records.Count-1].Flags&NpV2BleCatalog.DeleteAllowed)!=0);}}
        internal static bool CanReadV1(NpSdStatus s){return NpV1BleProtocol.CanManageStorage(s) && s.Capacity>8192 &&
            (NpV1BleProtocol.HasReadOnlyCatalog(s) || (s.Flags&16)!=0 && (s.Flags&NpV1BleProtocol.CatalogReadOnly)==0);}
        static NpBleCatalog ReadOnlyCutoff(NpBleCatalog result,string reason){result.V1Warning="Readable prefix only; listing stopped: "+reason;return result;}
        internal static async Task<NpBleCatalog> ReadV1(NpSdStatus status,Func<ushort,Task<byte[]>> read)
        {
            if(status!=null&&(status.Flags&NpV1BleProtocol.StoreV2Selected)!=0)return await ReadStoreV2(status,read);
            if(!CanReadV1(status))throw new IOException("Stop recording and mount the catalog before listing recordings.");
            bool readOnly=NpV1BleProtocol.HasReadOnlyCatalog(status);
            var result=new NpBleCatalog{Capacity=status.Capacity,NextStart=readOnly?0:status.Start,V1ReadOnly=readOnly,
                V1Full=!readOnly&&status.Start>=status.Capacity,V1CanDeleteLast=!readOnly&&NpV1BleProtocol.CanConfigure(status)&&(status.Flags&1)!=0};ulong previous=8192;
            for(ushort page=0;page<(readOnly?ReadOnlyPageLimit:1024);page++){
                byte[] body;
                try{body=await read(page);if(body==null || body.Length!=512)throw new IOException("Incomplete V1 catalog page.");}
                catch(Exception ex) when(readOnly&&(ex is IOException||ex is TimeoutException)){return ReadOnlyCutoff(result,"page "+page+" unreadable ("+ex.Message+"). Reconnect or refresh before further access.");}
                uint count=BitConverter.ToUInt32(body,0);if(count>63){if(readOnly)return ReadOnlyCutoff(result,"invalid count on page "+page+".");throw new IOException("Invalid V1 catalog count.");}
                result.Pages.Add((byte[])body.Clone());
                for(int i=0;i<count;i++){
                    ulong start=(previous+8191)&~8191UL;uint end=BitConverter.ToUInt32(body,4+4*i);
                    if(start>uint.MaxValue || end<=start+4 || end>status.Capacity || BitConverter.ToUInt32(body,256+4*i)!=end){
                        if(readOnly)return ReadOnlyCutoff(result,"invalid extent at recording "+((uint)result.Records.Count+1)+"; later starts were not inferred.");
                        throw new IOException("Invalid V1 catalog extent; nothing modified.");
                    }
                    result.Records.Add(new NpBleRecord{Id=(uint)result.Records.Count+1,Start=(uint)start,End=end});previous=end;
                }
                if(count<63){
                    if(!readOnly&&((previous+8191)&~8191UL)!=status.Start)throw new IOException("Catalog changed or is not mounted consistently; refresh before modifying it.");
                    return result;
                }
            }
            if(readOnly)return ReadOnlyCutoff(result,"read-only page limit ("+ReadOnlyPageLimit+") reached.");
            throw new IOException("Catalog exceeds the supported page limit.");
        }
        static async Task<NpBleCatalog> ReadStoreV2(NpSdStatus status,Func<ushort,Task<byte[]>> read)
        {
            if(!CanReadV1(status)||(status.Flags&16)==0||(status.Flags&NpV1BleProtocol.CatalogReadOnly)!=0||
                status.Start<8192||(status.Start&8191U)!=0||status.Start>status.Capacity)
                throw new IOException("V1 filesystem2 allocation is unavailable. Mount the catalog before listing; no legacy extents were inferred.");
            uint capacity=status.Capacity,frontier=status.Start,previous=8192,total=0,missing=0;ulong epoch=0,generation=0;
            var result=new NpBleCatalog{Capacity=capacity,NextStart=frontier,V1Full=frontier>=capacity,V1CanDeleteLast=false};
            for(ushort page=0;page<70;page++){
                byte[] body=await read(page);
                if(body==null||body.Length!=512)throw new IOException("Incomplete V1 filesystem2 catalog page.");
                if(BitConverter.ToUInt32(body,0)!=0x47503253U)
                    throw new IOException("Unsupported V1 filesystem2 BLE catalog page. Update to matching V1 firmware; legacy projection was not used.");
                uint count=BitConverter.ToUInt32(body,24),currentTotal=BitConverter.ToUInt32(body,28),first=(uint)page*58;
                ulong currentEpoch=BitConverter.ToUInt64(body,8),currentGeneration=BitConverter.ToUInt64(body,16);
                uint more=BitConverter.ToUInt32(body,36);
                if(BitConverter.ToUInt32(body,508)!=NpSdControl.Crc(body,508)||BitConverter.ToUInt32(body,4)!=page||
                    currentEpoch==0||currentGeneration==0||currentTotal>4032||first>currentTotal||
                    count!=Math.Min(58U,currentTotal-first)||BitConverter.ToUInt32(body,32)!=frontier||
                    more!=(first+count<currentTotal?1U:0U)||BitConverter.ToUInt32(body,504)!=0||
                    (currentTotal==0&&frontier!=8192)||(currentTotal!=0&&frontier==8192))
                    throw new IOException("Invalid V1 filesystem2 catalog CRC/page/count/allocation metadata; nothing modified.");
                if(page==0){epoch=currentEpoch;generation=currentGeneration;total=currentTotal;}
                else if(currentEpoch!=epoch||currentGeneration!=generation||currentTotal!=total)
                    throw new IOException("V1 filesystem2 catalog snapshot changed while listing. Refresh; nothing modified.");
                for(uint i=0;i<58;i++){
                    uint start=BitConverter.ToUInt32(body,40+(int)i*8),end=BitConverter.ToUInt32(body,44+(int)i*8);
                    if(i>=count){if(start!=0||end!=0)throw new IOException("Nonzero unused V1 filesystem2 catalog entry.");continue;}
                    if(start==0&&end==0){missing++;continue;}
                    if(start<8192||(start&8191U)!=0||(end&8191U)!=0||end<=start||end>frontier||end>capacity||start<previous)
                        throw new IOException("Invalid/overlapping V1 filesystem2 catalog extent at record "+(first+i+1)+"; no missing ranges were inferred.");
                    result.Records.Add(new NpBleRecord{Id=first+i+1,Start=start,End=end});previous=end;
                }
                result.Pages.Add((byte[])body.Clone());
                if(more==0){
                    if(missing!=0)result.V1Warning=missing+" unavailable filesystem2 recording descriptor(s) skipped; retained IDs/extents are explicit. Allocation frontier is unchanged; missing ranges were not inferred. Deletion is unsupported.";
                    return result;
                }
            }
            throw new IOException("V1 filesystem2 catalog exceeds4032 records; nothing modified.");
        }
        internal bool SameAs(NpBleCatalog other)
        {
            if(other==null || Capacity!=other.Capacity || NextStart!=other.NextStart || Pages.Count!=other.Pages.Count || V1ReadOnly!=other.V1ReadOnly || V1Full!=other.V1Full)return false;
            if(V2!=null || other.V2!=null){
                if(V2==null || other.V2==null || !V2.SameAs(other.V2) || Records.Count!=other.Records.Count)return false;
                for(int i=0;i<Records.Count;i++)if(!NpV2BleCatalog.SameRecord(Records[i],other.Records[i]))return false;
            }
            for(int page=0;page<Pages.Count;page++)for(int i=0;i<512;i++)if(Pages[page][i]!=other.Pages[page][i])return false;
            return true;
        }
    }
}
