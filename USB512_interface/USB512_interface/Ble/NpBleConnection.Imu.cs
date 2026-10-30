using System;
using System.Threading;
using System.Threading.Tasks;

namespace USB512_interface {
    internal sealed partial class NpBleConnection {
        int imuSequence=Environment.TickCount;
        int imuReaders;
        internal bool ImuPending {get{return Volatile.Read(ref imuReaders)!=0;}}
        uint NextImuId(){uint id;do{id=unchecked((uint)Interlocked.Increment(ref imuSequence));}while(id==0);return id;}
        // Production headless entry point. The caller owns connection/profile/
        // recording setup and cancellation; this method is read-only.
        internal Task<byte[]> ReadImuSnapshot(CancellationToken cancel) {
            return ReadImuSnapshot(()=>false,cancel);
        }
        internal async Task<byte[]> ReadImuSnapshot(Func<bool> yield,CancellationToken cancel) {
            Interlocked.Increment(ref imuReaders);
            try {
                await RetireRecordingClock();
                return await NpBleImu.ReadAsync((request,token)=>ExchangeAsync(0xb9,request,token,true),
                    NextImuId,()=>Epoch,()=>!Connected || yield(),cancel);
            }finally{Interlocked.Decrement(ref imuReaders);}
        }
    }
}
