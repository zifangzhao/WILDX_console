using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace CE32_console {
    static class NpSdControl { internal static uint Crc(byte[] b,int n){return USB512_interface.NpFirstSyncWire.Crc(b,n);} }
}
namespace USB512_interface {
    static class NpAcquisitionAnchor {internal const uint RequestMagic=0x5141504e;}
    sealed class NpBleConnection {
ACTUAL_READ_ONLY_SETUP
        CancellationTokenSource pending;
        int written;
        async Task WriteFrame(byte[] frame,int generation,CancellationToken cancel){
            for(int at=0;at<frame.Length;at+=20){
                cancel.ThrowIfCancellationRequested();written+=Math.Min(20,frame.Length-at);
                if(at==0){pending.Cancel();await Task.Yield();}
            }
        }
        async Task Deliver(byte command,byte[] request,bool opportunistic,CancellationToken cancel){
            byte[] frame=new byte[67];int generation=1;
ACTUAL_WRITE_AND_CANCEL
        }
        static int checks;
        static void Need(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static byte[] Seal(byte[] b){Put(b,60,NpFirstSyncWire.Crc(b,60));return b;}
        static async Task Probe(byte[] request,bool readOnly,byte command=0xb8){
            Need(IsReadOnlySetupRequest(command,request)==readOnly,"Read-only setup admission disagrees with valid protocol shape");
            var connection=new NpBleConnection();using(connection.pending=new CancellationTokenSource()){
                bool canceled=false;try{await connection.Deliver(command,request,false,connection.pending.Token);}catch(OperationCanceledException){canceled=true;}
                Need(canceled,"Outer cancellation was lost");
                Need(connection.written==(readOnly?67:20),"Cancellation left a read-only frame partial or changed mutation cancellation");
            }
        }
        static async Task Run(){
            const string uid="001D00065942500B20313354";
            var caps=NpFirstSyncSd.Query(NpFirstSyncSd.Caps,11,1,uid);
            var caps2=NpFirstSyncSd.QueryFineAux(NpFirstSyncSd.Caps,12,1,uid);
            var caps3=NpFirstSyncSd.QueryFineCounter64(NpFirstSyncSd.Caps,14,1,uid);
            var caps4=NpFirstSyncSd.QueryGrid16(NpFirstSyncSd.Caps,15,2,uid);
            var caps5=NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Caps,16,2,uid);
            var status=NpProbePreparation.Request(NpProbePreparation.Status,13);
            foreach(var query in new[]{caps,caps2,caps3,caps4,caps5,status}){
                await Probe(query,true);await Probe(query,false,0xb9);
                for(int at=0;at<query.Length;at++){var bad=(byte[])query.Clone();bad[at]^=1;Need(!IsReadOnlySetupRequest(0xb8,bad),"Corrupt setup query admitted");}
            }
            var legacy=new byte[64];Put(legacy,0,0x31444c4e);Put(legacy,4,1);Put(legacy,8,2);Put(legacy,12,20);Put(legacy,16,3);Put(legacy,20,20000);Seal(legacy);
            await Probe(NpFirstSyncSd.WrapLegacy(legacy,1,uid,77),false);
            await Probe(NpFirstSyncSd.WrapLegacyFineCounter64(legacy,1,uid,78),false);
            await Probe(NpFirstSyncSd.QueryFineCounter64(NpFirstSyncSd.Status,22,1,uid,78),false);
            await Probe(NpFirstSyncSd.Query(NpFirstSyncSd.Status,21,1,uid,77),false);
            var legacy2=(byte[])legacy.Clone();Put(legacy2,0,0x32444c4e);Put(legacy2,4,2);Seal(legacy2);
            await Probe(NpFirstSyncSd.WrapLegacyGrid16(legacy2,2,uid,79),false);
            await Probe(NpFirstSyncSd.WrapLegacyGrid16Append(legacy2,2,uid,80),false);
            await Probe(NpFirstSyncSd.QueryGrid16(NpFirstSyncSd.Status,24,2,uid,79),false);
            await Probe(NpFirstSyncSd.QueryGrid16Append(NpFirstSyncSd.Status,25,2,uid,80),false);
            await Probe(NpProbePreparation.Request(NpProbePreparation.Prepare,22,NpProbePreparation.Sd,3,20000),false);
            var anchor=new byte[64];Put(anchor,0,NpAcquisitionAnchor.RequestMagic);Put(anchor,4,1);Put(anchor,8,23);Seal(anchor);
            await Probe(anchor,true,0xb9);await Probe(anchor,false);
            for(int at=0;at<64;at++){var bad=(byte[])anchor.Clone();bad[at]^=1;Need(!IsReadOnlySetupRequest(0xb9,bad),"Corrupt startup query admitted");}
            foreach(int at in new[]{4,12,24,59}){var bad=(byte[])anchor.Clone();bad[at]^=0x80;Seal(bad);Need(!IsReadOnlySetupRequest(0xb9,bad),"Unknown startup query payload admitted");}
            foreach(var query in new[]{caps,caps2,caps3,caps4,caps5,status}){
                bool npp=BitConverter.ToUInt32(query,0)==NpProbePreparation.RequestMagic;
                foreach(int at in npp?new[]{4,8,16,24,32,40,59}:new[]{4,6,24,32,40,59}){
                    var bad=(byte[])query.Clone();bad[at]^=0x80;Seal(bad);Need(!IsReadOnlySetupRequest(0xb8,bad),"Unknown version/operation/payload admitted as read-only");
                }
                var noId=(byte[])query.Clone();Put(noId,npp?12:8,0);Seal(noId);Need(!IsReadOnlySetupRequest(0xb8,noId),"Missing fresh ID admitted");
                if(!npp){var noUid=(byte[])query.Clone();Array.Clear(noUid,12,12);Seal(noUid);Need(!IsReadOnlySetupRequest(0xb8,noUid),"Empty MCU identity admitted");}
            }
            Need(!IsReadOnlySetupRequest(0xb8,null)&&!IsReadOnlySetupRequest(0xb8,new byte[63]),"Malformed setup length admitted");
            Console.WriteLine("PASS "+checks+" setup admission/cancellation checks; modeled writer, no BLE hardware");
        }
        static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    }
}
