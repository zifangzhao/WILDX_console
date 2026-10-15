using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface
{
    internal sealed partial class NpBleConnection
    {
        // V1's existing 0x33 settings command and 0x90 read return the native
        // 512-byte parameter structure. They have no transaction ID or CRC.
        // Serialize them with all other controls and fence the connection epoch.
        internal async Task<byte[]> ExchangeParameters(byte[] frame)
        {
            if(frame==null || frame.Length<3 || frame[0]!=0x3c || frame[frame.Length-1]!=0x3e ||
                !(frame.Length==35 && frame[1]==0x33 || frame.Length==3 && frame[1]==0x90))
                throw new ArgumentException("V1 media settings/readback command required.");
            await transaction.WaitAsync();
            TaskCompletionSource<byte[]> waiting=null;
            try {
                if(!Connected)throw new IOException("Connect the V1 device before reading media settings.");
                int generation;
                lock(receiveLock) {
                    generation=epoch;
                    waiting=receipt=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    matches=(id,body)=>id==0x90 && body!=null && body.Length==512;
                }
                await WriteFrame(frame,generation,CancellationToken.None);
                if(await Task.WhenAny(waiting.Task,Task.Delay(5000))!=waiting.Task)
                    throw new TimeoutException("V1 media settings not confirmed. Outcome unknown; no command replayed.");
                byte[] response=await waiting.Task;
                if(!Connected || epoch!=generation)throw new IOException("Connection changed; media settings receipt discarded.");
                return response;
            } finally {
                lock(receiveLock){if(receipt==waiting){receipt=null;matches=null;if(waiting!=null && waiting.Task.Status!=TaskStatus.RanToCompletion)wire.Clear();}}
                transaction.Release();
            }
        }
    }
}
