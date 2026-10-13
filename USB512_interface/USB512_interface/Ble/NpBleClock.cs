using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed class NpBleClock
    {
        internal uint Generation { get; private set; }
        internal bool Synchronized { get; private set; }
        internal string Error { get; private set; }
        internal double OffsetMilliseconds { get; private set; }
        internal double BoundMilliseconds { get; private set; }

        internal void Reset(){Generation=0;Synchronized=false;Error=null;OffsetMilliseconds=BoundMilliseconds=0;}
        internal void Failed(Exception error){Synchronized=false;Error=error.Message;}
        internal async Task Initialize(NpSdStatus status,Func<byte[],Task<byte[]>> exchange,Func<uint> next)
        {
            Reset();
            if(status==null || (status.Flags&256)==0)throw new IOException("Device does not advertise PC-time support.");
            if(!status.Busy && NpV2OnlineClock.CanCalibrate(status.State)){
                var result=await NpV2OnlineClock.Synchronize(exchange,next,16);
                Generation=result.Receipt.Generation;Synchronized=true;
                OffsetMilliseconds=result.OffsetSeconds*1000;BoundMilliseconds=result.UncertaintySeconds*1000;
            }else await Observe(exchange,next); // Attaching to an active recorder never commits its clock.
        }
        internal async Task Observe(Func<byte[],Task<byte[]>> exchange,Func<uint> next)
        {
            uint id=next();long before=(DateTime.UtcNow.Ticks-621355968000000000L)/10;
            var elapsed=Stopwatch.StartNew();
            try {
                var reply=NpV2OnlineClock.Parse(await exchange(NpV2OnlineClock.Request(0,id)),0,id);
                if((reply.State>7 && reply.State!=255) || reply.Utc<1577836800000000UL)throw new IOException("Invalid device clock observation.");
                long error;long estimate=NpV2OnlineClock.MidpointUtc(before,before+(long)(elapsed.Elapsed.TotalMilliseconds*1000),reply.Rx,reply.Tx,out error);
                if(!reply.Valid || reply.Generation==0 || (Generation==0?NpV2OnlineClock.CanCalibrate(reply.State):reply.Generation!=Generation)){
                    Generation=0;throw new IOException("Device clock changed; synchronize while stopped.");
                }
                Generation=reply.Generation;
                OffsetMilliseconds=(estimate-(long)reply.Utc)/1000.0;
                BoundMilliseconds=(error+reply.Uncertainty)/1000.0+5;
                if(Math.Abs(OffsetMilliseconds)>BoundMilliseconds)throw new IOException("PC-time offset exceeds the reported bound.");
                Synchronized=true;Error=null;
            }catch(Exception ex){Failed(ex);throw;}
        }
    }
}
