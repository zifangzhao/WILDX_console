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
        internal NpV2BleCatalogInfo V2;
        internal bool CanDeleteLast{get{return Records.Count!=0 && (V2==null?V1CanDeleteLast:(Records[Records.Count-1].Flags&NpV2BleCatalog.DeleteAllowed)!=0);}}
        internal static bool CanReadV1(NpSdStatus s){return NpV1BleProtocol.CanManageStorage(s) && (s.Flags&16)!=0 && s.Capacity>8192;}
        internal static async Task<NpBleCatalog> ReadV1(NpSdStatus status,Func<ushort,Task<byte[]>> read)
        {
            if(!CanReadV1(status))throw new IOException("Stop recording and mount the catalog before listing recordings.");
            var result=new NpBleCatalog{Capacity=status.Capacity,NextStart=status.Start,V1CanDeleteLast=NpV1BleProtocol.CanConfigure(status)&&(status.Flags&1)!=0};ulong previous=8192;
            for(ushort page=0;page<1024;page++){
                var body=await read(page);if(body==null || body.Length!=512)throw new IOException("Incomplete V1 catalog page.");
                uint count=BitConverter.ToUInt32(body,0);if(count>63)throw new IOException("Invalid V1 catalog count.");
                result.Pages.Add((byte[])body.Clone());
                for(int i=0;i<count;i++){
                    ulong start=(previous+8191)&~8191UL;uint end=BitConverter.ToUInt32(body,4+4*i);
                    if(end<=start+4 || end>status.Capacity || BitConverter.ToUInt32(body,256+4*i)!=end)throw new IOException("Invalid V1 catalog extent; nothing modified.");
                    result.Records.Add(new NpBleRecord{Id=(uint)result.Records.Count+1,Start=(uint)start,End=end});previous=end;
                }
                if(count<63){
                    if(((previous+8191)&~8191UL)!=status.Start)throw new IOException("Catalog changed or is not mounted consistently; refresh before modifying it.");
                    return result;
                }
            }
            throw new IOException("Catalog exceeds the supported page limit.");
        }
        internal bool SameAs(NpBleCatalog other)
        {
            if(other==null || Capacity!=other.Capacity || NextStart!=other.NextStart || Pages.Count!=other.Pages.Count)return false;
            if(V2!=null || other.V2!=null){
                if(V2==null || other.V2==null || !V2.SameAs(other.V2) || Records.Count!=other.Records.Count)return false;
                for(int i=0;i<Records.Count;i++)if(!NpV2BleCatalog.SameRecord(Records[i],other.Records[i]))return false;
            }
            for(int page=0;page<Pages.Count;page++)for(int i=0;i<512;i++)if(Pages[page][i]!=other.Pages[page][i])return false;
            return true;
        }
    }
}
