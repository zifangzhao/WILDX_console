using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    class NpFirmwareIdentity {internal uint Family;internal string Uid;}
    internal sealed partial class NpBleForm {
        sealed class RecordingClockSchedule {internal void Reset(){}}
        readonly RecordingClockSchedule recordingClockSchedule=new RecordingClockSchedule();
        long fineRecordingClockStart,nextFineRecordingClock;
        const string FirstUid="001D00065942500B20313354";
        int recorderVersion,firstSyncNormalReads;
        string bleDeviceUid;
        NpFirmwareIdentity firmwareIdentity;
        bool busy,hostBusy,closing,firstSyncFailNextStatus,firstSyncRejectStop;
        bool firstSyncUnsupported,firstSyncFineUnsupported,firstSyncLoseStart,firstSyncDelayStart,firstSyncContinuous,firstSyncCounterUnsupported;
        bool firstSyncIdentityTimeout,firstSyncIdentityBadCrc,firstSyncIdentityZeroUid,firstSyncGrid16,firstSyncGridDeadline,firstSyncGridAppend,firstSyncGridAppendDeadline;
        uint firstSyncStartError,firstSyncSupport,firstSyncIdentityResult=1;
        byte[] firstSyncDelayed;
        Action<byte[]> firstSyncOnExchange;
        readonly List<byte[]> firstSyncCalls=new List<byte[]>();
        static NpBleForm FirstForm(bool v2){
            var f=New(v2,false);f.recorderVersion=v2?2:1;f.bleDeviceUid=FirstUid;f.busy=true;
            f.connection.OptionalExchange=f.FirstExchange;return f;
        }
        static byte[] FirstLegacy(bool v2,uint id){return v2?NpV2SdControl.Request(2,id,20000,0,0,3):NpSdControl.Request(2,id,3,20000,8192,1048576,100,0);}
        void PersistBleFirstSyncAttempt(NpFirstSyncSdAttempt attempt){} // File evidence is covered by the dedicated actual-writer fixture.
        Task<byte[]> FirstExchange(byte[] q,CancellationToken cancel){
            firstSyncCalls.Add((byte[])q.Clone());
            firstSyncOnExchange?.Invoke(q);
            if(BitConverter.ToUInt32(q,0)==NpProbePreparation.RequestMagic){
                Need(BitConverter.ToUInt32(q,8)==NpProbePreparation.Status,"Warm-up sent a preparation mutation");
                if(firstSyncIdentityTimeout)throw new TimeoutException("Old APP has no NPP STATUS route");
                var response=new byte[64];FirstPut(response,0,NpProbePreparation.ReplyMagic);FirstPut(response,4,1);FirstPut(response,8,NpProbePreparation.Status);FirstPut(response,12,BitConverter.ToUInt32(q,12));FirstPut(response,16,firstSyncIdentityResult);
                if(!firstSyncIdentityZeroUid)for(int i=0;i<3;i++)FirstPut(response,48+4*i,Convert.ToUInt32(FirstUid.Substring(i*8,8),16));
                FirstPut(response,60,NpProbePreparation.Crc(response,60));if(firstSyncIdentityBadCrc)response[60]^=1;return Task.FromResult(response);
            }
            ushort op=BitConverter.ToUInt16(q,6),version=BitConverter.ToUInt16(q,4);bool fineAux=version==2,fineCounter64=version==3,grid16=version==4,append=version==5;
            if(append&&firstSyncGridAppendDeadline)return FirstGridDeadline(cancel);
            if(append&&!firstSyncGridAppend)throw new TimeoutException("Legacy firmware has no SD16 append opt-in");
            if(grid16&&firstSyncGridDeadline)return FirstGridDeadline(cancel);
            if(grid16&&!firstSyncGrid16)throw new TimeoutException("Legacy firmware has no SD16 opt-in");
            if(op==NpFirstSyncSd.Caps&&(firstSyncUnsupported||fineAux&&firstSyncFineUnsupported||fineCounter64&&firstSyncCounterUnsupported))throw new TimeoutException("Legacy firmware has no NPSD capability version");
            bool start=op==NpFirstSyncSd.Start||op==NpFirstSyncSd.Scheduled;
            if(start&&firstSyncDelayStart){firstSyncDelayed=(byte[])q.Clone();throw new TimeoutException("Start is still delayed before dispatch");}
            if(start&&firstSyncStartError==0){
                current.LastId=BitConverter.ToUInt32(q,8);current.Operation=2;current.RequestId=current.LastId;
                current.Flags|=8U;operation=2;pending=1; // Queued ACK remains idle until ordinary status proves more.
            }
            if(start&&firstSyncLoseStart)throw new TimeoutException("NPSD Start ACK lost");
            uint error=start?firstSyncStartError:0;
            return Task.FromResult(NpFirstSyncSd.BuildReply(q,recorderVersion,new NpFirstSyncSdReply{
                Result=error,Supported=append?29U:grid16||fineCounter64||fineAux&&firstSyncContinuous?(V2?31U:29U):fineAux?(V2?15U:13U):firstSyncSupport!=0?firstSyncSupport:V2?3U:1U,RecordFeatures=op==0||error!=0?0U:append||grid16||fineCounter64?25U:fineAux?9U:1U,ProfileCrc=UInt32.MaxValue}));
        }
        static Task<byte[]> FirstGridDeadline(CancellationToken cancel){return Task.FromException<byte[]>(new OperationCanceledException("Optional transport deadline",cancel));}
        static void FirstPut(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
        async Task FirstStopToTerminal(){
            await StopRecording();
            if(V2&&pendingStop!=null){await ReadStatus();await ReadStatus();}
        }
        static async Task FirstFails<T>(Func<Task> work,string why) where T:Exception {
            bool failed=false;try{await work();}catch(T){failed=true;}Need(failed,why);
        }
        static async Task FirstSyncTests(){
            await FirstSyncWarmTests();
            foreach(bool v2 in new[]{false,true}){
                var old=FirstForm(v2);old.firstSyncUnsupported=true;byte[] prepared=null;
                await old.StartRecordingCore(0,(s,b)=>prepared=b);
                Need(old.firstSyncCalls.Count==1&&old.writes.Count==1&&Object.ReferenceEquals(prepared,old.writes[0]),"Unsupported CAPS did not send exactly the original legacy Start");
                Need(old.bleFirstSyncClient.Attempt==null,"Legacy fallback invented an NPSD attempt");
                var unknown=FirstForm(v2);unknown.bleDeviceUid=null;await unknown.StartRecording();
                Need(unknown.firstSyncCalls.Count==0&&unknown.writes.Count==1,"Unknown UID generated optional queries or changed legacy Start");
                var form=FirstForm(v2);prepared=null;await form.StartRecordingCore(0,(s,b)=>prepared=b);
                var attempt=form.bleFirstSyncClient.Attempt;
                Need(form.firstSyncCalls.Count==2&&form.writes.Count==0&&attempt!=null&&!attempt.OutcomeUnknown,"NPSD path replayed legacy or lost its queued ACK");
                Need(attempt.Legacy.SequenceEqual(prepared)&&attempt.Id==BitConverter.ToUInt32(prepared,12),"Logical legacy callback/ID changed");
                Need(form.firstSyncNormalReads>=2&&form.current.State==(v2?1U:0U)&&attempt.Acknowledgement.Session==0,"Queued NPSD ACK fabricated running state");
                await FirstFails<IOException>(form.StartRecording,"Outstanding attempt allowed another Start");
                Need(form.firstSyncCalls.Count==2,"Outstanding attempt issued another wrapper");
                await form.FirstStopToTerminal();
                Need(form.bleFirstSyncClient.Attempt==null&&form.writes.Count==1&&BitConverter.ToUInt32(form.writes[0],8)==3,"Terminal ordered Stop did not retire the attempt");
                Need(form.firstSyncCalls.All(q=>BitConverter.ToUInt16(q,6)!=NpFirstSyncSd.Status),"Optional NPSD Status became a Stop prerequisite");
                await form.StartRecording();Need(form.bleFirstSyncClient.Attempt.Token!=attempt.Token&&form.firstSyncCalls.Count==3,"Confirmed Stop did not allow a fresh token using cached CAPS");

                var lost=FirstForm(v2);lost.firstSyncLoseStart=true;
                await FirstFails<TimeoutException>(lost.StartRecording,"Lost NPSD ACK did not remain unknown");
                Need(lost.LastFirstSyncAttempt!=null&&lost.LastFirstSyncAttempt.OutcomeUnknown&&lost.writes.Count==0,"Unknown NPSD delivery was lost or sent a helper Stop");
                await FirstFails<IOException>(lost.StartRecording,"Unknown NPSD Start was replayed");
                Need(lost.firstSyncCalls.Count==2&&lost.writes.Count==0,"Unknown NPSD path fell back/replayed");
                lost.firstSyncFailNextStatus=true;await lost.FirstStopToTerminal();
                Need(lost.bleFirstSyncClient.Attempt==null&&lost.writes.Count==1,"Unavailable normal status blocked explicit ordered Stop");

                var rejected=FirstForm(v2);rejected.firstSyncStartError=3;
                await FirstFails<IOException>(rejected.StartRecording,"NPSD rejection did not surface");
                Need(rejected.bleFirstSyncClient.Attempt.Rejected&&rejected.writes.Count==0,"Rejected wrapper fell back to legacy");

                foreach(bool afterDelivery in new[]{false,true}){
                    var canceled=FirstForm(v2);canceled.firstSyncOnExchange=q=>{
                        if((BitConverter.ToUInt16(q,6)!=0)==afterDelivery){canceled.stopRequested=true;canceled.CancelBleFirstSyncStart();}
                    };
                    await FirstFails<OperationCanceledException>(canceled.StartRecording,"Stop cancellation did not interrupt NPSD path");
                    int starts=canceled.firstSyncCalls.Count(q=>BitConverter.ToUInt16(q,6)==1);
                    Need(starts==(afterDelivery?1:0)&&canceled.writes.Count==0,"Stop before/after delivery replayed or started capture");
                    Need(afterDelivery?canceled.LastFirstSyncAttempt.OutcomeUnknown:canceled.bleFirstSyncClient.Attempt==null,"Cancellation crossed the attempt boundary incorrectly");
                }
                for(int change=0;change<2;change++){
                    int kind=change;var changed=FirstForm(v2);changed.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,6)!=0){if(kind==0)changed.connection.Epoch++;else changed.connectedAddress++;}};
                    await FirstFails<InvalidOperationException>(changed.StartRecording,"Changed peer/epoch accepted old ACK");
                    Need(changed.LastFirstSyncAttempt.OutcomeUnknown&&changed.writes.Count==0,"Changed binding lost unknown attempt");
                    await FirstFails<IOException>(changed.FirstStopToTerminal,"Changed binding sent Stop to a substituted peer");
                    Need(changed.writes.Count==0,"Connection mismatch mutated another logger");
                    var saved=changed.LastFirstSyncAttempt;changed.ResetBleFirstSync();Need(changed.bleFirstSyncClient==null&&Object.ReferenceEquals(saved,changed.LastFirstSyncAttempt)&&changed.writes.Count==0,"Reset stopped device or discarded inspectable attempt");
                }
                var busyStop=FirstForm(v2);await busyStop.StartRecording();var kept=busyStop.bleFirstSyncClient.Attempt;busyStop.firstSyncRejectStop=true;
                busyStop.firstSyncFailNextStatus=true;busyStop.current.Flags|=8U;busyStop.pending=0;
                await FirstFails<NpSdCommandException>(busyStop.StopRecording,"Definite Stop BUSY did not stay a rejection");
                Need(busyStop.bleFirstSyncStopId==0&&Object.ReferenceEquals(kept,busyStop.bleFirstSyncClient.Attempt)&&busyStop.pendingStop==null,"Rejected Stop trapped its issued marker or retired Start");
                uint rejectedId=BitConverter.ToUInt32(busyStop.writes.Last(),12);busyStop.firstSyncRejectStop=false;busyStop.current.Error=0;busyStop.current.Flags&=~8U;
                await busyStop.FirstStopToTerminal();
                Need(busyStop.bleFirstSyncClient.Attempt==null&&busyStop.writes.Count==2&&unchecked((int)(BitConverter.ToUInt32(busyStop.writes.Last(),12)-rejectedId))>0,"Later explicit Stop did not use a fresh ID after BUSY");

                var stopLost=FirstForm(v2);await stopLost.StartRecording();stopLost.loseStopReceipt=true;
                await FirstFails<Exception>(stopLost.StopRecording,"Lost Stop ACK was silently accepted");
                Need(stopLost.bleFirstSyncStopId!=0&&stopLost.bleFirstSyncClient.Attempt!=null,"Lost Stop ACK discarded its no-replay context");
                await stopLost.FirstStopToTerminal();
                Need(stopLost.writes.Count==1&&stopLost.bleFirstSyncClient.Attempt==null,"Unknown Stop ACK caused replay or failed terminal retirement");

                var delayed=FirstForm(v2);delayed.firstSyncDelayStart=true;
                await FirstFails<TimeoutException>(delayed.StartRecording,"Delayed dispatch did not remain unknown");
                uint lateId=BitConverter.ToUInt32(delayed.firstSyncDelayed,8);await delayed.FirstStopToTerminal();
                uint stopId=delayed.current.LastId;
                Need(unchecked((int)(stopId-lateId))>0&&delayed.bleFirstSyncClient.Attempt==null,"Stop ID did not dominate the delayed wrapper");
                bool admitted=unchecked((int)(lateId-delayed.current.LastId))>0;
                Need(!admitted&&delayed.current.State==(v2?5U:3U),"A delayed B8 Start could pass the existing controller ordering after Stop");
            }
            var identity=FirstForm(true);identity.bleDeviceUid=null;identity.firmwareIdentity=new NpFirmwareIdentity{Family=2,Uid=FirstUid};await identity.StartRecording();
            Need(identity.bleFirstSyncClient!=null&&identity.writes.Count==0,"Known firmware UID did not enable binding");
            foreach(var pair in new[]{new[]{41U,40U,42U},new[]{41U,100U,101U},new[]{100U,41U,101U},new[]{UInt32.MaxValue,UInt32.MaxValue-1,1U},new[]{UInt32.MaxValue-1,UInt32.MaxValue,1U},new[]{UInt32.MaxValue,0U,1U}}){
                var f=FirstForm(true);await f.SendBleSdStart(FirstLegacy(true,pair[0]));
                Need(f.BleFirstSyncStopRequestId(new NpSdStatus{LastId=pair[1]},123)==pair[2],"Signed/modulo Stop ID ahead/behind/wrap handling failed");
            }
            var ambiguous=FirstForm(true);await ambiguous.SendBleSdStart(FirstLegacy(true,41));bool failed=false;
            try{ambiguous.BleFirstSyncStopRequestId(new NpSdStatus{LastId=unchecked(41U+0x80000000U)},1);}catch(IOException){failed=true;}
            Need(failed&&ambiguous.bleFirstSyncClient.Attempt!=null&&ambiguous.writes.Count==0,"Ambiguous half-range issued a speculative Stop");
        }
        static async Task FirstSyncWarmTests(){
            Console.WriteLine("Checking legacy Connect warm-up...");
            foreach(bool v2 in new[]{false,true}){
                var known=FirstForm(v2);known.firstSyncSupport=v2?7U:5U;
                await known.WarmBleFirstSyncCapabilities();
                Need(known.firstSyncCalls.Count==2&&known.firstSyncCalls.All(q=>BitConverter.ToUInt32(q,0)==NpFirstSyncSd.RequestMagic)&&known.bleFirstSyncClient.FineAuxSupportedFeatures==(v2?15U:13U)&&known.messages.Count==0,"Known UID warm-up queried NPP, missed CAPS, or added UI debug text");
                int count=known.firstSyncCalls.Count;await known.StartRecording();
                Need(known.firstSyncCalls.Count==count+1&&known.bleFirstSyncClient.Attempt.Version==2&&known.bleFirstSyncClient.Attempt.Acknowledgement.RecordFeatures==9,"Prewarmed Start performed more capability queries or used wrong version");
                var unsupported=FirstForm(v2);unsupported.firstSyncUnsupported=true;await unsupported.WarmBleFirstSyncCapabilities();int queries=unsupported.firstSyncCalls.Count;
                await unsupported.StartRecording();Need(queries==1&&unsupported.firstSyncCalls.Count==queries&&unsupported.writes.Count==1,"Unsupported prewarm blocked/changed ordinary legacy Start");
                foreach(int condition in new[]{0,1,2,3}){
                    var unavailable=FirstForm(v2);if(condition==0)unavailable.current.Flags|=4;if(condition==1)unavailable.current.Fault=1;if(condition==2)unavailable.current.Error=8;if(condition==3)unavailable.current.State=v2?3U:1U;
                    await unavailable.WarmBleFirstSyncCapabilities();Need(unavailable.firstSyncCalls.Count==0,"Busy/faulted/nonidle state admitted optional warm-up");
                }
                foreach(bool peer in new[]{false,true}){
                    var stale=FirstForm(v2);stale.firstSyncSupport=v2?7U:5U;stale.firstSyncOnExchange=q=>{if(peer)stale.connectedAddress++;else stale.connection.Epoch++;};
                    await stale.WarmBleFirstSyncCapabilities();Need(stale.firstSyncCalls.Count==1&&stale.bleFirstSyncClient.FineAuxSupportedFeatures==0,"Stale peer/epoch admitted further warm-up");
                }
            }
            foreach(uint result in new uint[]{0,1}){
                var v1=FirstForm(false);v1.bleDeviceUid=null;v1.firstSyncIdentityResult=result;v1.firstSyncSupport=5;
                await v1.WarmBleFirstSyncCapabilities();
                Need(v1.bleDeviceUid==FirstUid&&v1.firstSyncCalls.Count==3&&BitConverter.ToUInt32(v1.firstSyncCalls[0],0)==NpProbePreparation.RequestMagic&&v1.bleFirstSyncClient.FineAuxSupportedFeatures==13,"Valid/unsupported read-only identity did not bind V1 UID before CAPS");
                int count=v1.firstSyncCalls.Count;await v1.StartRecording();Need(v1.firstSyncCalls.Count==count+1&&v1.bleFirstSyncClient.Attempt.Version==2,"V1 Start repeated identity/capability warm-up");
            }
            for(int failure=0;failure<4;failure++){
                var old=FirstForm(false);old.bleDeviceUid=null;old.firstSyncIdentityTimeout=failure==0;old.firstSyncIdentityBadCrc=failure==1;old.firstSyncIdentityZeroUid=failure==2;if(failure==3)old.firstSyncIdentityResult=2;
                await old.WarmBleFirstSyncCapabilities();Need(old.bleDeviceUid==null&&old.bleFirstSyncClient==null&&old.firstSyncCalls.Count==1,"Unavailable/malformed identity fabricated a known UID");
                await old.StartRecording();Need(old.firstSyncCalls.Count==1&&old.writes.Count==1,"Unknown V1 UID blocked legacy Start or repeated optional discovery");
            }
            foreach(bool peer in new[]{false,true}){
                var stale=FirstForm(false);stale.bleDeviceUid=null;stale.firstSyncOnExchange=q=>{if(peer)stale.connectedAddress++;else stale.connection.Epoch++;};
                await stale.WarmBleFirstSyncCapabilities();Need(stale.bleDeviceUid==null&&stale.bleFirstSyncClient==null&&stale.firstSyncCalls.Count==1,"Stale NPP identity reply was assigned to a changed connection");
            }
            var unknownV2=FirstForm(true);unknownV2.bleDeviceUid=null;await unknownV2.WarmBleFirstSyncCapabilities();Need(unknownV2.firstSyncCalls.Count==0,"Unknown V2 UID caused a new core identity query");
            var canceled=FirstForm(false);canceled.bleDeviceUid=null;canceled.firstSyncOnExchange=q=>canceled.CancelBleFirstSyncStart();await canceled.WarmBleFirstSyncCapabilities();
            Need(canceled.bleDeviceUid==null&&canceled.firstSyncCalls.Count==1&&canceled.bleFirstSyncWarmCancel==null,"Canceled identity warm-up retained/bound stale state");
            foreach(ushort stage in new ushort[]{1,2}){
                var interrupted=FirstForm(false);interrupted.firstSyncSupport=5;interrupted.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==stage)interrupted.CancelBleFirstSyncStart();};
                await interrupted.WarmBleFirstSyncCapabilities();Need(interrupted.firstSyncCalls.Count==stage&&interrupted.bleFirstSyncClient.Attempt==null&&interrupted.bleFirstSyncClient.FineAuxSupportedFeatures==0,"Canceled capability warm-up made a Start attempt or cached v2 support");
                interrupted.firstSyncOnExchange=null;int calls=interrupted.firstSyncCalls.Count;await interrupted.StartRecording();
                Need(stage==1?interrupted.firstSyncCalls.Count==calls&&interrupted.writes.Count==1:interrupted.firstSyncCalls.Count==calls+1&&interrupted.bleFirstSyncClient.Attempt.Version==1,"Start retried canceled warm-up or lost cached v1 fallback");
            }
            Console.WriteLine("Checking continuous-clock Connect warm-up...");
            var oldFine=FirstForm(false);oldFine.firstSyncSupport=5;oldFine.firstSyncFineUnsupported=true;await oldFine.WarmBleFirstSyncCapabilities();
            int oldQueries=oldFine.firstSyncCalls.Count;await oldFine.StartRecording();Need(oldQueries==2&&oldFine.firstSyncCalls.Count==3&&oldFine.bleFirstSyncClient.Attempt.Version==1,"Fine v2 timeout blocked cached v1 Start or added another query");
            foreach(bool v2 in new[]{false,true}){
                var modern=FirstForm(v2);modern.firstSyncSupport=v2?7U:5U;modern.firstSyncContinuous=true;await modern.WarmBleFirstSyncCapabilities();
                Need(modern.firstSyncCalls.Count==(v2?4:3)&&modern.bleFirstSyncClient.FineCounter64SupportedFeatures==(v2?31U:29U)&&modern.messages.Count==0,"Connect did not discover explicit v3 once/silently");
                int count=modern.firstSyncCalls.Count;await modern.StartRecording();Need(modern.firstSyncCalls.Count==count+1&&modern.bleFirstSyncClient.Attempt.Version==3&&modern.bleFirstSyncClient.Attempt.Acknowledgement.RecordFeatures==25,"Prewarmed v3 Start queried/fell back to9");
                var unavailable=FirstForm(v2);unavailable.firstSyncSupport=v2?7U:5U;unavailable.firstSyncContinuous=true;unavailable.firstSyncCounterUnsupported=true;await unavailable.WarmBleFirstSyncCapabilities();count=unavailable.firstSyncCalls.Count;
                await unavailable.StartRecording();Need(count==3&&unavailable.firstSyncCalls.Count==count+1&&unavailable.bleFirstSyncClient.Attempt.Version==1,"Failed v3 warmup selected unsupported v2record9 or delayed Start with queries");
                var canceled3=FirstForm(v2);canceled3.firstSyncSupport=v2?7U:5U;canceled3.firstSyncContinuous=true;canceled3.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==3)canceled3.CancelBleFirstSyncStart();};await canceled3.WarmBleFirstSyncCapabilities();count=canceled3.firstSyncCalls.Count;
                Need(count==3&&canceled3.bleFirstSyncClient.FineCounter64SupportedFeatures==0&&canceled3.bleFirstSyncClient.Attempt==null,"Canceled v3 warmup cached support/record context");canceled3.firstSyncOnExchange=null;await canceled3.StartRecording();Need(canceled3.firstSyncCalls.Count==count+1&&canceled3.bleFirstSyncClient.Attempt.Version==1,"Start replayed canceled v3 discovery");
                foreach(bool peer in new[]{false,true}){
                    var stale3=FirstForm(v2);stale3.firstSyncSupport=v2?7U:5U;stale3.firstSyncContinuous=true;stale3.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==3){if(peer)stale3.connectedAddress++;else stale3.connection.Epoch++;}};await stale3.WarmBleFirstSyncCapabilities();
                    Need(stale3.firstSyncCalls.Count==3&&stale3.bleFirstSyncClient.FineCounter64SupportedFeatures==0&&stale3.bleFirstSyncClient.Attempt==null,"Stale v3 capability reply was cached or made record context");
                }
                var noLongerIdle=FirstForm(v2);noLongerIdle.firstSyncSupport=v2?7U:5U;noLongerIdle.firstSyncContinuous=true;noLongerIdle.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==2)noLongerIdle.current.Fault=1;};await noLongerIdle.WarmBleFirstSyncCapabilities();Need(noLongerIdle.firstSyncCalls.Count==2,"v3 query ignored fresh idle/fault guard");
            }
            Console.WriteLine("Checking SD16 Connect warm-up/local timeout...");
            foreach(bool timeout in new[]{false,true}){
                var grid=FirstForm(true);grid.firstSyncSupport=7;grid.firstSyncContinuous=true;grid.firstSyncGrid16=!timeout;grid.firstSyncGridDeadline=timeout;
                Console.WriteLine("SD16 local timeout fixture="+timeout);await grid.WarmBleFirstSyncCapabilities();
                Need(grid.firstSyncCalls.Count==(timeout?4:5)&&grid.rtcWarmCalls==1&&grid.bleFirstSyncClient.FineCounter64SupportedFeatures==31&&grid.bleFirstSyncClient.Grid16CapabilitiesChecked,"Optional CAPS4 did not retain v3/RTC warm after local timeout");
                Need(grid.bleFirstSyncClient.Grid16SupportedFeatures==(timeout?0U:31U)&&grid.bleFirstSyncWarmCancel==null,"CAPS4 cached unsupported proof or leaked warm ownership");
                int count=grid.firstSyncCalls.Count;await grid.StartRecording();Need(grid.firstSyncCalls.Count==count+1&&grid.bleFirstSyncClient.Attempt.Version==(timeout?3:4),"Start queried or ignored explicit v4 capability");
            }
            foreach(int loss in new[]{0,1,2}){
                var grid=FirstForm(true);grid.firstSyncSupport=7;grid.firstSyncContinuous=true;grid.firstSyncGrid16=true;
                grid.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==4){if(loss==0)grid.CancelBleFirstSyncStart();else if(loss==1)grid.connection.Epoch++;else grid.connectedAddress++;}};
                await grid.WarmBleFirstSyncCapabilities();Need(grid.firstSyncCalls.Count==4&&grid.rtcWarmCalls==0&&grid.bleFirstSyncClient.Grid16SupportedFeatures==0&&grid.bleFirstSyncClient.Attempt==null,"Changed/canceled CAPS4 context cached proof or warmed RTC");
            }
            foreach(bool timeout in new[]{false,true}){
                var grid=FirstForm(true);grid.firstSyncSupport=7;grid.firstSyncContinuous=true;grid.firstSyncGrid16=true;grid.firstSyncGridAppend=!timeout;grid.firstSyncGridAppendDeadline=timeout;
                await grid.WarmBleFirstSyncCapabilities();Need(grid.firstSyncCalls.Count==5&&grid.rtcWarmCalls==1&&grid.bleFirstSyncClient.Grid16AppendCapabilitiesChecked&&grid.bleFirstSyncClient.Grid16SupportedFeatures==31,"CAPS5 local timeout removed v4/RTC or added discovery");
                Need(grid.bleFirstSyncClient.Grid16AppendSupportedFeatures==(timeout?0U:29U)&&grid.bleFirstSyncWarmCancel==null,"CAPS5 cached unknown proof or leaked warm ownership");int count=grid.firstSyncCalls.Count;
                await grid.StartRecording();Need(grid.firstSyncCalls.Count==count+1&&grid.bleFirstSyncClient.Attempt.Version==(timeout?4:5)&&grid.bleFirstSyncClient.Attempt.Acknowledgement.RecordFeatures==25,"Start queried or ignored explicit v5 capability");
            }
            foreach(int loss in new[]{0,1,2}){
                var grid=FirstForm(true);grid.firstSyncSupport=7;grid.firstSyncContinuous=true;grid.firstSyncGrid16=true;grid.firstSyncGridAppend=true;
                grid.firstSyncOnExchange=q=>{if(BitConverter.ToUInt16(q,4)==5){if(loss==0)grid.CancelBleFirstSyncStart();else if(loss==1)grid.connection.Epoch++;else grid.connectedAddress++;}};
                await grid.WarmBleFirstSyncCapabilities();Need(grid.firstSyncCalls.Count==5&&grid.rtcWarmCalls==0&&grid.bleFirstSyncClient.Grid16AppendSupportedFeatures==0&&grid.bleFirstSyncClient.Grid16SupportedFeatures==31&&grid.bleFirstSyncClient.Attempt==null,"Changed/canceled CAPS5 context cached proof or warmed RTC");
            }
        }
    }
}
