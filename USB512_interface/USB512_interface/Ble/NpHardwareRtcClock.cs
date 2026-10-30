using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using USB512_interface;

namespace CE32_console {
    // One stopped USB mailbox transaction. Cancellation after the OUT is an
    // intent to stop observing, not evidence that the device retired its owner.
    internal sealed class NpHardwareRtcUsbTransfer {
        internal bool Success;internal uint Bytes;internal int NativeError;
        internal NpHardwareRtcUsbTransfer(bool success,uint bytes,int error){Success=success;Bytes=bytes;NativeError=error;}
    }
    internal sealed class NpHardwareRtcUsbTransaction {
        readonly byte[] request;readonly byte output,input;readonly List<object> transfers=new List<object>();
        internal bool OutAttempted,OutAccepted,ReceiptConsumed,ControlIdleConfirmed;
        internal bool RetirementUnknown {get{return OutAttempted&&(!OutAccepted||!ReceiptConsumed)&&!ControlIdleConfirmed;}}
        internal object ControlIdleProof;internal string Error;
        internal NpHardwareRtcUsbTransaction(byte[] request,byte output,byte input){
            NpHardwareRtcClock.ValidateRequest(request);this.request=(byte[])request.Clone();this.output=output;this.input=input;
        }
        internal bool Matches(byte[] other){if(other==null||other.Length!=request.Length)return false;for(int i=0;i<other.Length;i++)if(other[i]!=request[i])return false;return true;}
        internal byte[] Exchange(CancellationToken cancel,Func<bool,byte[],NpHardwareRtcUsbTransfer> native){
            cancel.ThrowIfCancellationRequested();
            Func<bool,byte[],NpHardwareRtcUsbTransfer> call=(isOut,bytes)=>{
                long begin=Stopwatch.GetTimestamp();NpHardwareRtcUsbTransfer result;
                try{result=native(isOut,bytes);}catch(Exception ex){
                    var win=ex as System.ComponentModel.Win32Exception;result=new NpHardwareRtcUsbTransfer(false,0,win==null?0:win.NativeErrorCode);
                    transfers.Add(Row(isOut,bytes,result,begin,Stopwatch.GetTimestamp(),cancel,"exception: "+ex.Message));throw;
                }
                transfers.Add(Row(isOut,bytes,result,begin,Stopwatch.GetTimestamp(),cancel,null));return result;
            };
            try{
                OutAttempted=true;try{var sent=call(true,(byte[])request.Clone());OutAccepted=sent.Success&&sent.Bytes==64;}
                catch(Exception ex){Error=ex.Message;} // Delivery may have happened; drain reads only.
                var wait=Stopwatch.StartNew();
                // Poll budget only: synchronous native calls can exceed it.
                // Never issue a second OUT, including after a failed/short OUT.
                while(wait.ElapsedMilliseconds<2500){
                    var reply=new byte[64];NpHardwareRtcUsbTransfer received;
                    try{received=call(false,reply);}catch(Exception ex){Error=ex.Message;Thread.Sleep(20);continue;}
                    if(received.Success&&received.Bytes==64){
                        try{NpHardwareRtcClock.Parse(reply,request);ReceiptConsumed=true;}
                        catch(InvalidDataException ex){((Dictionary<string,object>)transfers[transfers.Count-1])["receipt_error"]=ex.Message;}
                        if(ReceiptConsumed){cancel.ThrowIfCancellationRequested();return reply;}
                    }
                    Thread.Sleep(20);
                }
                throw new TimeoutException("RTC mailbox retirement is unknown; no command replayed.");
            }catch(Exception ex){Error=ex.Message;throw;}
        }
        Dictionary<string,object> Row(bool isOut,byte[] raw,NpHardwareRtcUsbTransfer result,long begin,long end,CancellationToken cancel,string error){
            return new Dictionary<string,object>{{"direction",isOut?"OUT":"IN"},{"opcode",isOut?output:input},
                {"host_begin_qpc",begin},{"host_end_qpc",end},{"host_qpc_hz",Stopwatch.Frequency},{"success",result.Success},
                {"transferred_bytes",result.Bytes},{"native_error",result.NativeError},{"cancel_requested_after_call",cancel.IsCancellationRequested},
                {"raw_base64",Convert.ToBase64String(raw)},{"error",error}};
        }
        internal object Evidence(){return new Dictionary<string,object>{{"operation",BitConverter.ToUInt32(request,8)},{"request_id",BitConverter.ToUInt32(request,12)},
            {"request_base64",Convert.ToBase64String(request)},{"out_attempted",OutAttempted},{"out_accepted",OutAccepted},{"receipt_consumed",ReceiptConsumed},
            {"retirement_unknown",RetirementUnknown},{"control_idle_confirmed",ControlIdleConfirmed},{"control_idle_proof",ControlIdleProof},{"error",Error},
            {"transfers",transfers.ToArray()},{"scope","Single OUT; cancellation after delivery drains read-only receipt. Poll budget is not a hard bound on synchronous native control calls."}};}
    }
    internal sealed class NpHardwareRtcReply {
        internal uint Operation,Id,Error,Rx,Tx,Generation,PcUncertainty,State,QuantumUs,Flags;
        internal ulong Utc;
        internal bool CalendarValid {get{return (Flags&NpHardwareRtcClock.CalendarValid)!=0;}}
        internal bool PcCalibrated {get{return (Flags&NpHardwareRtcClock.PcCalibrated)!=0;}}
    }
    internal sealed class NpHardwareRtcCalibration {
        byte[] request,acknowledgement,lastReply;
        internal byte[] CommitRequest {get{return request==null?null:(byte[])request.Clone();}}
        internal byte[] CommitAcknowledgement {get{return acknowledgement==null?null:(byte[])acknowledgement.Clone();}}
        internal byte[] LastReply {get{return lastReply==null?null:(byte[])lastReply.Clone();}}
        internal bool CommitAttempted,Confirmed;
        internal uint Observations,ReadbackQueries,PcUncertaintyUs;
        internal long BestRoundTripUs,PhaseLowerUs,PhaseUpperUs;
        internal NpHardwareRtcReply Accepted,Receipt;
        internal string Error;
        internal void BeforeCommit(byte[] raw){request=(byte[])raw.Clone();CommitAttempted=true;}
        internal void Acknowledged(byte[] raw,NpHardwareRtcReply reply){acknowledgement=(byte[])raw.Clone();Accepted=reply;}
        internal void Observed(byte[] raw,NpHardwareRtcReply reply){lastReply=(byte[])raw.Clone();Receipt=reply;}
    }
    internal sealed class NpHardwareRtcCalibrationException:IOException {
        internal readonly NpHardwareRtcCalibration Attempt;
        internal NpHardwareRtcCalibrationException(Exception error,NpHardwareRtcCalibration attempt):base(error.Message,error){Attempt=attempt;}
    }
    // Explicit v2 only. No probing/fallback, UI, device ownership or replay.
    // The bound caller supplies the actual provider's stopped-state predicate.
    internal static class NpHardwareRtcClock {
        internal const uint RequestMagic=0x3254504e,ReplyMagic=0x3252504e;
        internal const uint Hardware=1,CalendarValid=2,PcCalibrated=4,Frozen=8,HseDiv32=16;
        internal const ulong MinimumUtc=946684800000000UL,MaximumUtc=4102444799999999UL;
        internal const uint MinimumPcUncertainty=1000,MaximumPcUncertainty=500000,ObservationCount=8,MaximumReadbacks=13;
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static ulong L(byte[] b,int at){return BitConverter.ToUInt64(b,at);}
        static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        static void Need(bool ok,string reason){if(!ok)throw new InvalidDataException("Hardware RTC: "+reason);}
        static bool Zero(byte[] b,int at,int n){for(int i=at;i<at+n;i++)if(b[i]!=0)return false;return true;}
        internal static void ValidateRequest(byte[] b){
            Need(b!=null&&b.Length==64,"request size");
            Need(U(b,0)==RequestMagic&&U(b,4)==2&&U(b,8)<=1&&U(b,12)!=0&&Zero(b,36,24)&&U(b,60)==NpFirstSyncWire.Crc(b,60),"request tag/version/op/ID/reserved/CRC");
            if(U(b,8)==0)Need(Zero(b,16,44),"observation payload must be zero");
            else Need(L(b,20)>=MinimumUtc&&L(b,20)<=MaximumUtc&&U(b,28)>=MinimumPcUncertainty&&U(b,28)<=MaximumPcUncertainty,"commit UTC/PC uncertainty");
        }
        internal static byte[] Request(uint operation,uint id,uint referenceHal=0,ulong utc=0,uint uncertainty=0,uint expectedGeneration=0){
            var b=new byte[64];uint[] words={RequestMagic,2,operation,id,referenceHal,(uint)utc,(uint)(utc>>32),uncertainty,expectedGeneration};
            for(int i=0;i<words.Length;i++)Put(b,i*4,words[i]);Put(b,60,NpFirstSyncWire.Crc(b,60));ValidateRequest(b);return b;
        }
        // Nonzero Error is a structurally valid receipt, never implicit success.
        internal static NpHardwareRtcReply Parse(byte[] b,byte[] request){
            ValidateRequest(request);Need(b!=null&&b.Length==64&&U(b,0)==ReplyMagic&&U(b,4)==2&&U(b,8)==U(request,8)&&U(b,12)==U(request,12)&&U(b,60)==NpFirstSyncWire.Crc(b,60),"reply tag/version/op/ID/CRC");
            var r=new NpHardwareRtcReply {Operation=U(b,8),Id=U(b,12),Error=U(b,16),Rx=U(b,20),Tx=U(b,24),Generation=U(b,28),PcUncertainty=U(b,32),Utc=L(b,40),State=U(b,48),QuantumUs=U(b,52),Flags=U(b,56)};
            Need((r.Flags&~31U)==0&&U(b,36)<=1&&(U(b,36)!=0)==r.CalendarValid,"calendar flag/boolean/known flags");
            Need(r.QuantumUs==((r.Flags&Hardware)!=0?100U:0U),"hardware quantum");
            Need(r.CalendarValid?r.Utc>=MinimumUtc&&r.Utc<=MaximumUtc:r.Utc==0,"actual RTC calendar UTC");
            Need(r.PcCalibrated?(r.Flags&(Hardware|CalendarValid|HseDiv32))==(Hardware|CalendarValid|HseDiv32)&&r.Generation!=0&&r.PcUncertainty<=MaximumPcUncertainty:r.PcUncertainty==0,"PC calibration flags/generation/uncertainty");
            // Uncalibrated Generation is opaque setting-admission state and may
            // be nonzero, including during a pending hardware phase adjustment.
            return r;
        }
        static void Admit(NpHardwareRtcReply r,Func<uint,bool> stopped){
            if(r.Error!=0)throw new IOException("Hardware RTC command rejected: "+r.Error);
            if(!stopped(r.State)||(r.Flags&Frozen)!=0)throw new IOException("Hardware RTC calibration requires the bound stopped, unfrozen recorder.");
            if((r.Flags&(Hardware|HseDiv32))!=(Hardware|HseDiv32))throw new IOException("HSE/32 hardware RTC is unavailable.");
        }
        static void Phase(long start,long end,NpHardwareRtcReply r,out long lower,out long upper){
            long uncertainty,tx=NpV2OnlineClock.MidpointUtc(start,end,r.Rx,r.Tx,out uncertainty);
            long processing=checked((long)unchecked(r.Tx-r.Rx)*1000);
            // The actual calendar read may occur anywhere within Rx..Tx. Its
            //100us quantization is included once; no seal-phase assumption.
            lower=checked(tx-processing-uncertainty-r.PcUncertainty-r.QuantumUs-(long)r.Utc);
            upper=checked(tx+uncertainty+r.PcUncertainty+r.QuantumUs-(long)r.Utc);
        }
        internal static Task<NpHardwareRtcCalibration> Synchronize(Func<byte[],CancellationToken,Task<byte[]>> exchange,Func<uint> nextId,Func<uint,bool> isStoppedState,CancellationToken cancel){
            long utc=(DateTime.UtcNow.Ticks-621355968000000000L)/10,tick=Stopwatch.GetTimestamp();
            Func<long> now=()=>utc+(long)((Stopwatch.GetTimestamp()-tick)*1000000.0/Stopwatch.Frequency);
            return Synchronize(exchange,nextId,isStoppedState,now,cancel);
        }
        internal static async Task<NpHardwareRtcCalibration> Synchronize(Func<byte[],CancellationToken,Task<byte[]>> exchange,Func<uint> nextId,Func<uint,bool> isStoppedState,Func<long> now,CancellationToken cancel){
            if(exchange==null||nextId==null||isStoppedState==null||now==null)throw new ArgumentNullException("Explicit transport/ID/stopped-state/monotonic PC clock required.");
            var result=new NpHardwareRtcCalibration();var ids=new HashSet<uint>();long lastNow=long.MinValue;
            Func<long> stamp=()=>{long value=now();Need(value>=lastNow,"PC observation coordinate went backwards");lastNow=value;return value;};
            Func<uint> id=()=>{uint value=nextId();Need(value!=0&&ids.Add(value),"fresh nonzero request ID required");return value;};
            try{
                NpHardwareRtcReply best=null;long bestUtc=0,bestError=long.MaxValue,referenceLower=long.MinValue,referenceUpper=long.MaxValue;uint expected=0,lastTx=0,firstTx=0;
                for(uint i=0;i<ObservationCount;i++){
                    cancel.ThrowIfCancellationRequested();var request=Request(0,id());long start=stamp();var raw=await exchange((byte[])request.Clone(),cancel).ConfigureAwait(false);long end=stamp();result.Observed(raw,null);cancel.ThrowIfCancellationRequested();
                    var r=Parse(raw,request);result.Observed(raw,r);Admit(r,isStoppedState);
                    if(i==0){expected=r.Generation;firstTx=r.Tx;}else Need(r.Generation==expected&&unchecked(r.Tx-lastTx)<0x80000000U,"RTC setting generation/HAL lifetime changed during observations");
                    long error,utc=NpV2OnlineClock.MidpointUtc(start,end,r.Rx,r.Tx,out error);
                    uint elapsed=unchecked(r.Tx-firstTx);Need(elapsed<0x80000000U,"HAL reference lifetime is ambiguous");
                    long atFirst=checked(utc-(long)elapsed*1000);referenceLower=Math.Max(referenceLower,checked(atFirst-error));referenceUpper=Math.Min(referenceUpper,checked(atFirst+error));
                    Need(referenceLower<=referenceUpper,"HAL and PC observations do not share a continuous mapping");
                    if(error<bestError){best=r;bestUtc=utc;bestError=error;result.BestRoundTripUs=end-start;}
                    lastTx=r.Tx;result.Observations++;
                }
                Need(best!=null&&bestError<=MaximumPcUncertainty&&bestUtc>=0,"PC uncertainty is too large; no commit attempted");
                uint delta=unchecked(lastTx-best.Tx);Need(delta<0x80000000U,"HAL reference lifetime is ambiguous");
                ulong reference=checked((ulong)bestUtc+(ulong)delta*1000);result.PcUncertaintyUs=(uint)Math.Max(MinimumPcUncertainty,bestError);
                var commit=Request(1,id(),lastTx,reference,result.PcUncertaintyUs,expected);cancel.ThrowIfCancellationRequested();
                result.BeforeCommit(commit); // Retained BEFORE the single delivery attempt.
                var ackRaw=await exchange((byte[])commit.Clone(),cancel).ConfigureAwait(false);result.Acknowledged(ackRaw,null);var ack=Parse(ackRaw,commit);result.Acknowledged(ackRaw,ack);Admit(ack,isStoppedState);cancel.ThrowIfCancellationRequested();
                Need(ack.Generation!=0&&ack.Generation!=expected,"accepted commit did not publish a fresh setting generation");
                for(uint i=0;i<MaximumReadbacks;i++){
                    // Cover the provider's 2 s deadline plus two 200 ms
                    // observation intervals (13 reads span 2.4 s). Early success
                    // returns immediately; only pending readbacks are spaced.
                    if(i!=0)await Task.Delay(200,cancel).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();var request=Request(0,id());result.ReadbackQueries++;long start=stamp();byte[] raw;
                    try{raw=await exchange((byte[])request.Clone(),cancel).ConfigureAwait(false);}
                    catch(TimeoutException){continue;} // Fresh read-only IDs only; never re-COMMIT.
                    long end=stamp();result.Observed(raw,null);cancel.ThrowIfCancellationRequested();var r=Parse(raw,request);result.Observed(raw,r);Admit(r,isStoppedState);
                    Need(r.Generation==ack.Generation,"RTC setting generation changed during readback");
                    if(!r.PcCalibrated)continue; // SHPF may still be pending.
                    Need(r.PcUncertainty==result.PcUncertaintyUs,"RTC calibration uncertainty was not retained");
                    Phase(start,end,r,out result.PhaseLowerUs,out result.PhaseUpperUs);
                    Need(result.PhaseLowerUs<=0&&result.PhaseUpperUs>=0,"hardware RTC readback falls outside measured PC phase bounds");
                    result.Confirmed=true;return result;
                }
                throw new IOException("Hardware RTC phase completion was not confirmed within the read-only observation budget; commit was not replayed.");
            }catch(Exception e){result.Error=e.Message;throw new NpHardwareRtcCalibrationException(e,result);}
        }
    }
}
