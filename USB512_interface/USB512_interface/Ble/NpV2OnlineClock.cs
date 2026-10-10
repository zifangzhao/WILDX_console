using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CE32_console {
 internal sealed class NpV2ClockReply {
  internal uint Op,Id,Error,Rx,Tx,Generation,Uncertainty,State;
  internal bool Valid;internal ulong Utc;
 }
 internal sealed class NpV2ClockResult {
  internal double UncertaintySeconds,RoundTripSeconds,OffsetSeconds;
  internal uint Samples,ObservationTimeouts;internal NpV2ClockReply Receipt;
 }
 internal static class NpV2OnlineClock {
  internal const uint RequestMagic=0x3254504e,ReplyMagic=0x3252504e;
  internal static bool CanCalibrate(uint state){return state==0||state==1||state==5||state==7;}
  internal static byte[] Request(uint op,uint id,uint reference=0,ulong utc=0,uint uncertainty=0,uint generation=0){
   if(op>1||id==0)throw new ArgumentException("Invalid clock command");
   var b=new byte[64];uint[] w={RequestMagic,1,op,id,reference,(uint)utc,(uint)(utc>>32),uncertainty,generation};
   for(int i=0;i<w.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(w[i]),0,b,4*i,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal static NpV2ClockReply Parse(byte[] b,uint op,uint id){
   if(b==null||b.Length!=64||NpSdGeometry.U(b,0)!=ReplyMagic||NpSdGeometry.U(b,4)!=1||
      NpSdGeometry.U(b,8)!=op||NpSdGeometry.U(b,12)!=id||NpSdGeometry.U(b,60)!=NpSdControl.Crc(b,60)||
      NpSdGeometry.U(b,52)!=1000)throw new IOException("Invalid NP2 clock receipt");
   var r=new NpV2ClockReply{Op=op,Id=id,Error=NpSdGeometry.U(b,16),Rx=NpSdGeometry.U(b,20),Tx=NpSdGeometry.U(b,24),
    Generation=NpSdGeometry.U(b,28),Uncertainty=NpSdGeometry.U(b,32),Valid=NpSdGeometry.U(b,36)==1,
    Utc=(ulong)NpSdGeometry.U(b,40)|((ulong)NpSdGeometry.U(b,44)<<32),State=NpSdGeometry.U(b,48)};
   if(r.Error!=0)throw new IOException("NP2 PC-time command rejected: "+r.Error);
   return r;
  }
  internal static long MidpointUtc(long hostSend,long hostReceive,uint deviceRx,uint deviceTx,out long uncertainty){
   long processing=(long)unchecked(deviceTx-deviceRx)*1000;
   long elapsed=hostReceive-hostSend;
   if(elapsed<0||elapsed>5000000||processing>elapsed+2000)throw new IOException("Invalid clock observation timing");
   uncertainty=Math.Max(0,elapsed-processing)/2+1000;
   return hostSend+(elapsed+processing)/2;
  }
  internal static async Task<NpV2ClockResult> Synchronize(Func<byte[],Task<byte[]>> exchange,Func<uint> nextId,uint observations=64){
   if(observations<8||observations>64)throw new ArgumentOutOfRangeException("observations");
   // Monotonic host anchor: a PC wall-clock correction cannot corrupt an
   // in-flight timing exchange. No pretend CE64 RTC or sub-ms accuracy.
   long hostBase=(DateTime.UtcNow.Ticks-621355968000000000L)/10;
   long tickBase=Stopwatch.GetTimestamp();
   Func<long> now=()=>hostBase+(long)((Stopwatch.GetTimestamp()-tickBase)*1000000.0/Stopwatch.Frequency);
   NpV2ClockReply best=null;long bestUtc=0,bestError=long.MaxValue,bestRtt=0;
   uint generation=0,lastTx=0,samples=0,timeouts=0,consecutiveTimeouts=0;
   // A missing read-only observation must not discard an otherwise valid
   // calibration. Default to 64 observations; V1 selects 16 for quick connect.
   // and fewer than 3 consecutive timeouts. Each read gets a fresh ID.
   // Invalid receipts still fail immediately. The anchor COMMIT below is
   // always sent exactly once: an ambiguous mutation is never replayed.
   for(uint attempt=0;attempt<observations+16 && samples<observations;attempt++){
    uint id=nextId();long start=now();byte[] raw;
    try{raw=await exchange(Request(0,id)).ConfigureAwait(false);}
    catch(TimeoutException){timeouts++;if(++consecutiveTimeouts>=3)throw;continue;}
    long end=now();consecutiveTimeouts=0;
    var r=Parse(raw,0,id);
    lastTx=r.Tx;
    if(!CanCalibrate(r.State))throw new IOException("PC-time calibration requires a stopped recorder (unmounted, idle, completed or cancelled)");
    if(samples==0)generation=r.Generation;else if(generation!=r.Generation)throw new IOException("Clock generation changed during calibration");
    long error;long utc=MidpointUtc(start,end,r.Rx,r.Tx,out error);
    if(error<bestError){best=r;bestUtc=utc;bestError=error;bestRtt=end-start;}
    samples++;
   }
   if(samples!=observations)throw new IOException("PC-time observation budget exhausted; sync not confirmed");
   if(best==null||bestError>500000)throw new IOException("PC-time uncertainty too large; sync not confirmed");
   // Transport the best observation along the same monotonic millisecond
   // clock to the newest observation, keeping the bounded reference fresh.
   ulong anchor=(ulong)bestUtc+(ulong)unchecked(lastTx-best.Tx)*1000;
   uint commitId=nextId();long commitStart=now();
   var ack=Parse(await exchange(Request(1,commitId,lastTx,anchor,(uint)bestError,generation)).ConfigureAwait(false),1,commitId);
   long commitEnd=now();
   uint nextGeneration=unchecked(generation+1);if(nextGeneration==0)nextGeneration=1;
   if(!ack.Valid||ack.Utc<1577836800000000UL||ack.Generation!=nextGeneration||ack.Uncertainty!=(uint)bestError)
    throw new IOException("PC-time anchor was not confirmed");
   long verifyError;long estimatedTx=MidpointUtc(commitStart,commitEnd,ack.Rx,ack.Tx,out verifyError);
   long residual=estimatedTx-(long)ack.Utc;
   if(Math.Abs(residual)>bestError+verifyError+5000)throw new IOException("Acknowledged PC-time mapping failed the offset check");
   return new NpV2ClockResult{Samples=samples,ObservationTimeouts=timeouts,OffsetSeconds=residual/1e6,UncertaintySeconds=bestError/1e6,RoundTripSeconds=bestRtt/1e6,Receipt=ack};
  }
 }
}
