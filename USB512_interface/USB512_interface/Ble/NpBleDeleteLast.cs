using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    // The same confirmed NDU1 transaction as USB; transport ownership stays
    // with the existing selected BLE connection and its epoch guard.
    internal sealed class NpBleDeleteLast
    {
        string uid;
        readonly Func<byte[],Task<byte[]>> exchange;
        readonly Func<Task<NpSdStatus>> status;
        readonly Action target;
        internal NpBleDeleteLast(string uid,Func<byte[],Task<byte[]>> exchange,Func<Task<NpSdStatus>> status,Action target)
        {this.uid=uid;this.exchange=exchange;this.status=status;this.target=target;}
        static byte[] Query(uint operation,uint nonce=0){var q=new byte[8];BitConverter.GetBytes(operation).CopyTo(q,0);BitConverter.GetBytes(nonce).CopyTo(q,4);return q;}
        internal static bool Supported(NpSdStatus s)
        {return NpV1BleProtocol.CanManageStorage(s) && (s.Flags&(NpV1BleProtocol.TransactionalDelete|NpV1BleProtocol.StoreV2Selected|17U))==(NpV1BleProtocol.TransactionalDelete|NpV1BleProtocol.StoreV2Selected|17U);}
        async Task RequireIdle(){target();if(!Supported(await status()))throw new IOException("Delete Last requires a stopped, writable V1 card and matching firmware.");target();}
        async Task<NpUsbDeleteLast.Receipt> Read(byte[] q)
        {
            target();byte[] b=await exchange(q);target();
            if(b==null || b.Length!=128)throw new IOException("Incomplete V1 Delete Last reply; no command replayed.");
            if(uid==null){string actual=BitConverter.ToUInt32(b,80).ToString("X8")+BitConverter.ToUInt32(b,84).ToString("X8")+BitConverter.ToUInt32(b,88).ToString("X8");
                if(actual==new string('0',24) || actual==new string('F',24))throw new IOException("Logger identity unavailable; nothing deleted.");
                NpUsbDeleteLast.Parse(b,actual);uid=actual;
            }
            var r=NpUsbDeleteLast.Parse(b,uid);r.Generation=1;return r;
        }
        async Task<NpUsbDeleteLast.Receipt> Wait(NpUsbDeleteLast.Receipt r,uint op,uint nonce,NpUsbDeleteLast.Receipt confirmed=null)
        {
            var watch=Stopwatch.StartNew();
            while(true){
                if(r.Operation!=op || r.Nonce!=nonce || confirmed!=null && (r.Uid!=confirmed.Uid || r.Cid!=confirmed.Cid || r.Capacity!=confirmed.Capacity || r.Epoch!=confirmed.Epoch || r.InspectedNonce!=confirmed.Nonce))
                    throw new IOException("Delete transaction/card changed; outcome unknown. No command replayed.");
                if(r.Phase==4)throw new IOException("Delete Last failed (error "+r.Error+"). Refresh before another action.");
                if(r.Phase==3){
                    if(confirmed!=null && (r.Count!=confirmed.Count-1 || r.Frontier<8192 || r.Frontier>confirmed.Start))throw new IOException("Deleted catalog boundary not confirmed. No command replayed.");
                    await RequireIdle();return r;
                }
                if(!r.Active || watch.ElapsedMilliseconds>=30000)throw new IOException("Delete Last outcome unknown; keep power connected and refresh. No command replayed.");
                await Task.Delay(100);r=await Read(Query(0));
            }
        }
        internal async Task<NpUsbDeleteLast.Receipt> Inspect()
        {
            await RequireIdle();var prior=await Read(Query(0));
            if(prior.Active || prior.Nonce==uint.MaxValue)throw new IOException("Delete maintenance is busy or its transaction counter is exhausted.");
            uint nonce=prior.Nonce+1;return await Wait(await Read(Query(1,nonce)),1,nonce);
        }
        internal async Task Delete(NpUsbDeleteLast.Receipt confirmed)
        {
            await RequireIdle();var fresh=await Read(Query(0));
            if(confirmed==null || !confirmed.CanDelete || Convert.ToBase64String(fresh.Bytes)!=Convert.ToBase64String(confirmed.Bytes))throw new IOException("The confirmed deletion snapshot changed. Refresh and confirm again; nothing deleted.");
            byte[] q=NpUsbDeleteLast.DeleteRequest(confirmed);NpUsbDeleteLast.Receipt first;
            try{first=await Read(q);}catch(Exception ex) when(ex is IOException || ex is TimeoutException){throw new IOException("Delete acknowledgement unavailable; outcome unknown. No command replayed. Reconnect and refresh.",ex);}
            await Wait(first,2,BitConverter.ToUInt32(q,16),confirmed);
        }
    }
}
