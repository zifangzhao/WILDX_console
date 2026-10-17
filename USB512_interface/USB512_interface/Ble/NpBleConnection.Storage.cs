using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    internal sealed partial class NpBleConnection
    {
        // Native V1 catalog replies have neither page IDs nor CRCs. Keep the
        // whole operation serialized, and end the connection after an uncertain
        // reply so a late response cannot satisfy the next operation.
        internal async Task<byte[]> ExchangeV1Storage(ushort page,bool deleteLast)
        {
            if(page>=1024)throw new ArgumentOutOfRangeException("page");
            await transaction.WaitAsync();
            TaskCompletionSource<byte[]> waiting=null;
            try{
                if(!Connected)throw new IOException("Connect the V1 logger first.");
                int generation;byte[] deleted=null,parameters=null;
                lock(receiveLock){
                    generation=epoch;waiting=receipt=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    matches=(command,body)=>{
                        if(!deleteLast)return command==0x94 && body.Length==512;
                        if(command==0x95 && body.Length==4)deleted=body;
                        if(command==0x90 && body.Length==512)parameters=body;
                        return deleted!=null && parameters!=null;
                    };
                }
                byte[] frame=deleteLast?new byte[]{0x3c,0x95,1,0x3e}:new byte[]{0x3c,0x94,(byte)page,(byte)(page>>8),0x3e};
                await WriteFrame(frame,generation,CancellationToken.None);
                if(await Task.WhenAny(waiting.Task,Task.Delay(5000))!=waiting.Task)
                    throw new TimeoutException(deleteLast?"Delete outcome unknown. Reconnect and refresh recordings; no command replayed.":"No V1 catalog page received. Reconnect before retrying.");
                byte[] response=await waiting.Task;
                if(!Connected || Epoch!=generation)throw new IOException("Connection changed; catalog reply discarded.");
                return deleteLast?deleted:response;
            }catch{Disconnect();throw;}
            finally{
                lock(receiveLock){if(receipt==waiting){receipt=null;matches=null;}}
                transaction.Release();
            }
        }
    }
}
