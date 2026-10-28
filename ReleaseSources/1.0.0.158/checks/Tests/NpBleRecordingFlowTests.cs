using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

// This control-flow fixture never exports SD data. Fail rather than emulate
// the unrelated filesystem binding dependency pulled in by NpSdGeometry.
namespace CE32_console {
 static class NpV1StoreBinding {
  internal static void Validate(byte[] header,byte[] record,int offset=0){throw new InvalidOperationException("Unexpected SD export in BLE handler test");}
 }
}

// The extracted production handlers keep their real Stopwatch API and deadline.
// Only this fixture substitutes elapsed reads for deterministic expiry cases.
#pragma warning disable 0436
namespace System.Diagnostics {
 sealed class Stopwatch {
  internal static int ExpireOnRead;
  readonly int expireOnRead=ExpireOnRead;int started,reads;
  Stopwatch(){Restart();}
  internal static Stopwatch StartNew(){return new Stopwatch();}
  internal void Restart(){started=Environment.TickCount;reads=0;}
  internal TimeSpan Elapsed{get{if(expireOnRead>0 && ++reads>=expireOnRead)return TimeSpan.FromSeconds(60);return TimeSpan.FromMilliseconds(unchecked((uint)(Environment.TickCount-started)));}}
 }
}

// The actual Apply/Start handlers are inserted by the runner. Only their
// transport/configuration services are faked; the V1/V2 admission is real.
namespace WildNpConfiguration {
 class NpConfigRequest {internal byte Port;internal string Identity;}
 class Np2ConfigRequest {}
 class NpConfigClient {
  internal static readonly List<NpConfigRequest> Staged=new List<NpConfigRequest>();
  internal NpConfigClient(object transport){}
  internal static bool ReuseMatching;
  internal Task StageForCaptureAsync(IEnumerable<NpConfigRequest> requests,int mask,CancellationToken cancel,bool reuseMatching=false){ReuseMatching=reuseMatching;foreach(var request in requests)if((mask&(1<<request.Port))!=0)Staged.Add(request);return Task.CompletedTask;}
 }
 class Np2ConfigClient {
  internal Np2ConfigClient(object transport){}
  internal Task StageAsync(Np2ConfigRequest request,CancellationToken cancel,bool reuseMatching=false){return Task.CompletedTask;}
 }
}
namespace USB512_interface {
 using WildNpConfiguration;
 static class NpSdLoggerTest { ACTUAL_SD_TEST_BLOCKS }
 static class MessageBox {
  internal static int Warnings;internal static Action OnWarning;
  internal static DialogResult Show(IWin32Window owner,string text,string title,MessageBoxButtons buttons,MessageBoxIcon icon){
   if(buttons!=MessageBoxButtons.OK || icon!=MessageBoxIcon.Warning)throw new Exception("Unexpected qualification dialog policy");
   Warnings++;if(OnWarning!=null)OnWarning();return DialogResult.OK;
  }
 }
 class NpConfigurationDialog {internal static Np2ConfigRequest PrepareV2Request(string json,int port){return new Np2ConfigRequest();}}
 static class NpClosedLoopSettings {
  internal static Task<object> Prepare(bool v2,uint mask,uint hz,Func<byte[],CancellationToken,Task<byte[]>> exchange,CancellationToken cancel,Action<string> report){return Task.FromResult<object>(null);}
 }
 class NpBleConfigurationReceipts {internal int Pending;internal void Reset(){Pending=0;}internal void Expect(IEnumerable<Np2ConfigRequest> requests){Pending=requests.Count();}}
 class NpBleForm {
  sealed class Profile {internal uint Mask,Hz;}
  sealed class Connection {
   internal bool Connected=true;internal int Epoch=1;
   internal Task<byte[]> ExchangeAsync(byte command,byte[] request,CancellationToken cancel){throw new Exception("Unexpected optional control transfer in recording-flow test");}
  }
  sealed class ConfigTransport {internal ConfigTransport(Connection connection,Action<byte[]> progress=null){}}
  sealed class Clock {internal bool Synchronized=true;}
  readonly ComboBox profiles=new ComboBox();readonly TextBox address=new TextBox{Text="123456789ABC"};
  readonly CheckBox mediaCamera=new CheckBox(),mediaMicrophone=new CheckBox();readonly NumericUpDown mediaRate=new NumericUpDown();
  IWin32Window DialogOwner{get{return null;}}
  readonly Label configurationStatus=new Label(),clockStatus=new Label(),state=new Label();readonly Connection connection=new Connection();readonly Clock clock=new Clock();
  readonly NpBleConfigurationReceipts configurationReceipts=new NpBleConfigurationReceipts();
  readonly HashSet<Tuple<uint,uint,uint>> acknowledgedSdProfiles=new HashSet<Tuple<uint,uint,uint>>();
  readonly Dictionary<ulong,Dictionary<int,string>> np2HostConfigurations=new Dictionary<ulong,Dictionary<int,string>>();
  readonly Dictionary<ulong,Dictionary<byte,NpConfigRequest>> v1HostConfigurations=new Dictionary<ulong,Dictionary<byte,NpConfigRequest>>();
  ulong connectedAddress=0x123456789ABCUL;
ACTUAL_V1_CONFIGURATIONS
  readonly List<byte[]> writes=new List<byte[]>();
  readonly List<string> messages=new List<string>();
  // Preparation transport/lost-ACK behavior is covered by preparation host
  // tests; these scenarios begin with no outstanding preparation job.
  sealed class PreparationAttempt {internal bool Unknown,StopIssued;}
  PreparationAttempt probePreparationAttempt;
  bool UnknownBlePreparation {get{return probePreparationAttempt!=null&&probePreparationAttempt.Unknown;}}
  Task CancelBleProbePreparation(){return Task.CompletedTask;}
  Task StopUnknownBlePreparation(NpSdStatus status){throw new Exception("Unexpected unknown preparation in legacy recording-flow scenario");}
  object bleClosedLoopSetup;
  System.Diagnostics.Stopwatch pendingStop;int pendingStopEpoch;ulong pendingStopPeer;bool pendingStopAcknowledged,finishOnStopAck;
  readonly List<uint> statusIds=new List<uint>();readonly Queue<Action> statusSteps=new Queue<Action>();
  NpSdStatus current;string configurationReadError;bool V2,loseSelectReceipt,failConfirmation,mediaSettingsChanged,stopRequested,stopping,loseStopReceipt,cancelDuringSelect,wrongReceiptId,genericTimeout,changeEpochOnTimeout,cancelOnTimeout,changePeerOnTimeout,rejectSelect,mismatchSelectAck;bool selectPendingAck=true;int pending,receiptTimeouts;uint operation,sequence;
  void UpdateControls(){}
  bool StartEnabled(bool ready){return ACTUAL_START_BUTTON_EXPRESSION;}
  uint Next(){return current.LastId+1;}
  HashSet<int> np2Apply;
  HashSet<int> Np2ApplyRequests(ulong peer){return np2Apply;}
  void Append(string text){messages.Add(text);}
  void BeginBleRecordingMetadata(NpSdStatus before,byte[] request){}
  int clockSyncs;uint clockObservations;bool failClockSync,cancelClockSync,unconfirmedClockSync;
  Task SyncClock(uint observations=16){clockSyncs++;clockObservations=observations;if(cancelClockSync)stopRequested=true;if(unconfirmedClockSync)clock.Synchronized=false;if(failClockSync)throw new IOException("simulated clock failure");return Task.CompletedTask;}
  Task VerifyV1Media(){throw new Exception("Unexpected media path");}
  Task ApplyV1Media(){throw new Exception("Unexpected media path");}
  async Task<NpSdStatus> ReadStatus(){
   await Task.CompletedTask;
   sequence++;
   if(operation==1){statusIds.Add(sequence);if(statusSteps.Count>0){statusSteps.Dequeue()();return current;}}
   if(pending>0 && receiptTimeouts>0){receiptTimeouts--;if(changeEpochOnTimeout)connection.Epoch++;if(cancelOnTimeout)stopRequested=true;if(changePeerOnTimeout)address.Text="FEDCBA987654";if(genericTimeout)throw new TimeoutException("simulated write timeout");throw new NpV1ReceiptTimeout(0,wrongReceiptId?sequence+1:sequence);}
   if(pending>0){if(failConfirmation)throw new IOException("simulated read failure");if(cancelDuringSelect){stopRequested=true;cancelDuringSelect=false;}if(--pending==0){current.Flags&=~8U;if(operation==3){current.Flags&=~4U;current.State=current.Fault!=0?(V2?255U:4U):(V2?5U:3U);}}}
   state.Text="Device state "+current.State;ObservePendingStop(current);return current;
  }
  Task<NpSdStatus> Send(byte[] request){
   writes.Add(request);uint op=BitConverter.ToUInt32(request,8);operation=op;current.Operation=op;current.RequestId=current.LastId=BitConverter.ToUInt32(request,12);
   if(op==1){if(rejectSelect)throw new IOException("simulated SELECT rejection");current.Mask=BitConverter.ToUInt32(request,16);current.Hz=BitConverter.ToUInt32(request,20);if(mismatchSelectAck)current.Hz=15000;if(selectPendingAck)current.Flags|=8;pending=2;if(loseSelectReceipt)throw new TimeoutException("simulated lost SELECT receipt");}
   if(op==2){if(current.Error!=0)throw new NpSdCommandException(V2?2:1,current);current.Fault=0;current.State=V2?3U:1U;current.Flags|=4U;}
   if(op==3){current.Flags|=8U;pending=2;if(current.Fault==0)current.State=V2?4U:2U;if(finishOnStopAck){pending=0;current.Flags&=~12U;current.State=5;}if(loseStopReceipt)throw new TimeoutException("simulated lost STOP receipt");}
   return Task.FromResult(current);
  }
ACTUAL_RECORDING_METHODS
  static int checks;static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
  static NpBleForm New(bool v2,bool changed){
   System.Diagnostics.Stopwatch.ExpireOnRead=0;
   var form=new NpBleForm{V2=v2,current=new NpSdStatus{State=v2?1U:0U,Mask=changed?1U:3U,Hz=20000,Flags=17U|2U|512U|1048576U,Start=8192,Capacity=5000000,LastId=40}};
   form.profiles.Items.Add(new Profile{Mask=3,Hz=20000});form.profiles.SelectedIndex=0;return form;
  }
  static async Task Tests(){
   var unsyncedTest=New(true,false);unsyncedTest.unconfirmedClockSync=true;bool rejectedClock=false;
   try{await unsyncedTest.StartRecordingCore(10);}catch(IOException){rejectedClock=true;}
   Need(rejectedClock&&unsyncedTest.writes.Count==0,"Finite SD test sent START after swallowed clock failure");
   foreach(bool v2 in new[]{false,true}){
    var bounded=New(v2,false);byte[] prepared=null;
    await bounded.StartRecordingCore(10,(status,request)=>prepared=request);
    Need(prepared!=null && bounded.writes.Count==1 && Object.ReferenceEquals(prepared,bounded.writes[0]),"Finite test callback does not identify the sent request");
    Need(BitConverter.ToUInt32(prepared,v2?24:28)>0,"Finite recording test sent an unbounded START");
    if(v2)Need(BitConverter.ToUInt32(prepared,24)%4==0,"V2 finite extent is not aligned");
    else Need(BitConverter.ToUInt32(prepared,28)>bounded.current.Start && BitConverter.ToUInt32(prepared,28)<=bounded.current.Capacity,"V1 finite extent is outside free card space");
    if(v2)Need(bounded.clockSyncs==1,"V2 finite test did not freshly synchronize after selection");
   }
   MessageBox.Warnings=0;
   var unqualified=New(false,false);unqualified.current.Flags&=~2U;await unqualified.StartRecording();
   Need(MessageBox.Warnings==1 && unqualified.writes.Count==1,"Unqualified profile did not warn once and continue without a checkbox");
   Need(BitConverter.ToUInt32(unqualified.writes[0],36)==1U,"Unqualified START did not retain firmware acceptance flag");
   await unqualified.StopRecording();await unqualified.StartRecording();
   Need(MessageBox.Warnings==1 && BitConverter.ToUInt32(unqualified.writes.Last(),36)==1U,"Same profile repeated the warning or lost the legacy acceptance flag");
   await unqualified.StopRecording();unqualified.current.Capacity+=16384;await unqualified.StartRecording();
   Need(MessageBox.Warnings==2,"Changed card capacity did not warn again");
   await unqualified.StopRecording();unqualified.acknowledgedSdProfiles.Clear();await unqualified.StartRecording();
   Need(MessageBox.Warnings==3,"A new connection did not require warning acknowledgment again");
   await unqualified.StopRecording();unqualified.ObserveSdProfileContext(new NpSdStatus{Capacity=unqualified.current.Capacity,Flags=unqualified.current.Flags});
   Need(unqualified.acknowledgedSdProfiles.Count==1,"Ordinary status polling discarded profile acknowledgment");
   unqualified.ObserveSdProfileContext(new NpSdStatus{Capacity=unqualified.current.Capacity,Flags=unqualified.current.Flags&~16U});
   Need(unqualified.acknowledgedSdProfiles.Count==0,"Loss of mounted storage kept the previous profile acknowledgment");
   var highRate=New(false,false);highRate.current.Hz=30000;highRate.profiles.Items.Clear();highRate.profiles.Items.Add(new Profile{Mask=3,Hz=30000});highRate.profiles.SelectedIndex=0;
   int priorWarnings=MessageBox.Warnings;await highRate.StartRecording();
   Need(MessageBox.Warnings==priorWarnings+1,"Known-risk V1 dual30 profile did not warn when firmware qualification bit was set");
   var warningCanceled=New(false,false);warningCanceled.current.Flags&=~2U;MessageBox.OnWarning=()=>warningCanceled.stopRequested=true;bool canceledAtWarning=false;
   try{await warningCanceled.StartRecording();}catch(OperationCanceledException){canceledAtWarning=true;}finally{MessageBox.OnWarning=null;}
   Need(canceledAtWarning && warningCanceled.writes.Count==0 && warningCanceled.clockSyncs==0,"Stop during warning released START or clock synchronization");
   foreach(bool failedSync in new[]{false,true}){
    var timed=New(false,false);timed.current.Flags|=NpV1BleProtocol.Clock;timed.failClockSync=failedSync;await timed.StartRecording();
    Need(timed.clockSyncs==1 && timed.clockObservations==8 && timed.writes.Count==1 && BitConverter.ToUInt32(timed.writes[0],8)==2,"V1 quick clock sync missing, replayed START or blocked capture");
    Need(timed.clockStatus.Text==(failedSync?"Recording time unavailable":"PC time synced"),"Recording clock result hidden");
   }
   var clockCanceled=New(false,false);clockCanceled.current.Flags|=NpV1BleProtocol.Clock;clockCanceled.cancelClockSync=true;bool stoppedDuringClock=false;
   try{await clockCanceled.StartRecording();}catch(OperationCanceledException){stoppedDuringClock=true;}
   Need(stoppedDuringClock && clockCanceled.writes.Count==0,"Stop during clock sync still started recording");
   foreach(uint flags in new uint[]{0U,65536U,131072U,0x04000000U}){
    var manual=New(true,false);manual.clock.Synchronized=false;manual.current.Flags|=flags;
    Need(manual.StartEnabled(true)&&!manual.StartEnabled(false),"Manual V2 button uses cached policy or ignores transport ownership.");
    await manual.StartRecording();
    Need(manual.writes.Count==1&&BitConverter.ToUInt32(manual.writes[0],8)==2,"Manual V2 START was gated by host clock/catalog policy.");
    Need(BitConverter.ToUInt32(manual.writes[0],24)==0&&BitConverter.ToUInt64(manual.writes[0],28)==0,"Manual START invented bounded duration or scheduled UTC.");
   }
   var firmwareRejected=New(true,false);firmwareRejected.current.Error=2;bool rejectedStart=false;
   try{await firmwareRejected.StartRecording();}catch(IOException ex){rejectedStart=ex.Message.Contains("operation 2: error 2");}
   Need(rejectedStart&&firmwareRejected.writes.Count==1&&firmwareRejected.current.State==1,"Firmware rejection hidden, START replayed or running fabricated.");
   var stagedRejected=New(true,false);stagedRejected.current.Error=8;
   stagedRejected.np2Apply=new HashSet<int>{1,2};stagedRejected.np2HostConfigurations.Add(stagedRejected.connectedAddress,new Dictionary<int,string>{{1,"map1"},{2,"map2"}});
   bool storageRejected=false;try{await stagedRejected.StartRecording();}catch(NpSdCommandException){storageRejected=true;}
   Need(storageRejected && stagedRejected.configurationReceipts.Pending==0 && stagedRejected.configurationStatus.Text.Contains("recording was rejected"),"Rejected START retained false applied-map checks or hid the rejected capture");
   Need(stagedRejected.writes.Count==1 && stagedRejected.current.State==1,"Storage rejection retried START or fabricated recording");
   foreach(bool v2 in new[]{false,true})foreach(bool changed in new[]{false,true}){
    var form=New(v2,changed);await form.StartRecording();
    Need(form.writes.Count==(changed?2:1),"Unexpected command count");
    Need(form.writes.Count(b=>BitConverter.ToUInt32(b,8)==2)==1,"START not sent exactly once");
    Need(form.pending==0,"START sent before settings confirmed");
    var start=form.writes.Last();Need(BitConverter.ToUInt32(start,16)==3,"START lost P1+P2 selection");
    Need(BitConverter.ToUInt32(start,20)==20000,"START lost20k rate");
    Need(BitConverter.ToUInt32(start,12)==(changed?42U:41U),"START reused SELECT transaction");
   }
   foreach(bool lost in new[]{false,true}){
    var form=New(false,true);form.loseSelectReceipt=lost;form.failConfirmation=!lost;
    bool refused=false;try{await form.StartRecording();}catch(Exception ex) when(ex is IOException||ex is TimeoutException){refused=true;}
    Need(refused&&form.writes.Count==1&&BitConverter.ToUInt32(form.writes[0],8)==1,"Failed SELECT/confirmation caused a START or mutation replay");
   }
   var failed=New(false,false);failed.current.State=4;failed.current.Fault=2;await failed.StartRecording();Need(failed.writes.Count==1,"Explicit retry after finalized V1 source fault is blocked or replayed");
   foreach(bool v2 in new[]{false,true}){
    var form=New(v2,false);form.current.State=v2?3U:1U;form.current.Flags|=4U;await form.StopRecording();
    Need(form.writes.Count==1&&BitConverter.ToUInt32(form.writes[0],8)==3,"Stop not sent exactly once");
    if(v2){Need(form.pendingStop!=null&&!form.stopping,"V2 finalization retained the foreground UI lease or lost pending Stop");await form.ReadStatus();await form.ReadStatus();}
    Need(form.StopConfirmed(form.current)&&!form.stopping,"Stop did not confirm completion after drain");
    var faulted=New(v2,false);faulted.current.State=v2?255U:4U;faulted.current.Fault=2;await faulted.StopRecording();
    Need(faulted.writes.Count==0&&faulted.current.Fault==2,"Already finalized failure required Complete, replayed Stop or cleared the fault");
    if(!v2){faulted.current.Flags|=4U;await faulted.StopRecording();Need(faulted.writes.Count==1&&faulted.StopConfirmed(faulted.current),"Failed state was declared stopped before ownership cleared");}
    var lost=New(v2,false);lost.current.State=v2?3U:1U;lost.current.Flags|=4U;lost.loseStopReceipt=true;bool timedOut=false;
    try{await lost.StopRecording();}catch(TimeoutException){timedOut=true;}catch(IOException ex){timedOut=v2&&ex.Message.Contains("Stop outcome is unconfirmed");}Need(timedOut&&lost.writes.Count==1&&!lost.stopping,"Lost Stop ACK caused replay or stuck UI");
    if(v2){Need(lost.pendingStop!=null&&!lost.pendingStopAcknowledged,"Lost ACK invented acknowledgment or lost observation");await lost.ReadStatus();await lost.ReadStatus();Need(lost.pendingStop==null&&lost.messages.Count(m=>m.StartsWith("Recording complete;"))==1,"A fresh terminal failed to resolve the unknown Stop exactly once");}
   }
   var longStop=New(true,false);longStop.current.State=3;longStop.current.Flags|=4U;System.Diagnostics.Stopwatch.ExpireOnRead=1;await longStop.StopRecording();
   longStop.pending=100;for(int read=0;read<3;read++)await longStop.ReadStatus();
   Need(longStop.pendingStop!=null&&longStop.state.Text.Contains("60 s elapsed")&&longStop.state.Text.Contains("finalizing SD"),"More than30s finalization became a timeout or lost elapsed status");
   Need(!longStop.StartEnabled(true),"A pending Stop allowed another START");bool blockedStart=false;
   try{await longStop.StartRecording();}catch(IOException ex){blockedStart=ex.Message.Contains("Stop completion");}
   Need(blockedStart&&longStop.writes.Count==1,"Actual START bypassed pending Stop or sent a command");
   await longStop.StopRecording();Need(longStop.writes.Count==1,"Explicit second Stop replayed the pending mutation");
   longStop.ObservePendingStop(null);Need(longStop.pendingStop!=null,"Missing status fabricated completion");
   longStop.failConfirmation=true;bool readFailed=false;try{await longStop.ReadStatus();}catch(IOException){readFailed=true;}longStop.failConfirmation=false;
   Need(readFailed&&longStop.pendingStop!=null&&longStop.writes.Count==1,"Read failure replayed Stop or discarded observation");
   longStop.pending=1;await longStop.ReadStatus();await longStop.ReadStatus();
   Need(longStop.pendingStop==null&&longStop.messages.Count(m=>m.StartsWith("Recording complete;"))==1,"Late Complete failed to resolve pending Stop exactly once");
   Need(longStop.StartEnabled(true)&&longStop.messages.All(m=>!m.Contains("has not confirmed it is stopped")),"Late Complete left stale timeout or Start blocked");
   var terminalFault=New(true,false);terminalFault.current.State=3;terminalFault.current.Flags|=4U;await terminalFault.StopRecording();terminalFault.current.State=255;terminalFault.current.Fault=77;terminalFault.pending=10;await terminalFault.ReadStatus();
   Need(terminalFault.pendingStop!=null&&terminalFault.messages.All(m=>!m.StartsWith("Recording is stopped after")),"Busy failure was reported as stopped");
   terminalFault.pending=1;await terminalFault.ReadStatus();await terminalFault.ReadStatus();
   Need(terminalFault.pendingStop==null&&terminalFault.messages.Count(m=>m.Contains("Fault 77"))==1&&terminalFault.messages.All(m=>!m.StartsWith("Recording complete;")),"Terminal fault was hidden, duplicated or claimed complete");
   for(int change=0;change<3;change++){
    var changed=New(true,false);changed.current.State=3;changed.current.Flags|=4U;await changed.StopRecording();
    if(change==0)changed.connection.Epoch++;if(change==1)changed.connectedAddress++;if(change==2)changed.connection.Connected=false;
    changed.current.State=5;changed.current.Flags&=~12U;changed.ObservePendingStop(changed.current);
    Need(changed.pendingStop==null&&changed.messages.Any(m=>m.Contains("completion is unknown"))&&changed.messages.All(m=>!m.StartsWith("Recording complete;")),"Changed peer/epoch/disconnection confirmed an old Stop");
   }
   var immediateStop=New(true,false);immediateStop.current.State=3;immediateStop.current.Flags|=4U;immediateStop.finishOnStopAck=true;await immediateStop.StopRecording();await immediateStop.ReadStatus();
   Need(immediateStop.pendingStop==null&&immediateStop.writes.Count==1&&immediateStop.messages.Count(m=>m.StartsWith("Recording complete;"))==1,"Immediate terminal Stop ACK was lost or duplicated");
   var alreadyDraining=New(true,false);alreadyDraining.current.State=4;alreadyDraining.current.Flags|=4U;await alreadyDraining.StopRecording();
   Need(alreadyDraining.writes.Count==0&&alreadyDraining.pendingStop!=null,"Already finalizing recorder received another Stop");
   alreadyDraining.current.State=5;alreadyDraining.current.Flags&=~12U;await alreadyDraining.ReadStatus();
   Need(alreadyDraining.pendingStop==null&&alreadyDraining.messages.Count(m=>m.StartsWith("Recording complete;"))==1,"Existing finalization failed to resolve");
   var canceled=New(false,true);canceled.cancelDuringSelect=true;bool canceledStart=false;
   try{await canceled.StartRecording();}catch(OperationCanceledException){canceledStart=true;}
   Need(canceledStart&&canceled.writes.Count==1,"Stop during SELECT preparation released START");
   await canceled.StopRecording();Need(!canceled.stopRequested&&canceled.StopConfirmed(canceled.current)&&canceled.writes.Count==1,"Pending SELECT was overwritten or a canceled start remained active");
   var delayed=New(false,true);delayed.receiptTimeouts=1;await delayed.StartRecording();Need(delayed.writes.Count==2&&delayed.writes.Count(b=>BitConverter.ToUInt32(b,8)==1)==1&&delayed.writes.Count(b=>BitConverter.ToUInt32(b,8)==2)==1,"Qualifying STATUS timeout replayed mutation or prevented confirmed start");
   for(int mode=0;mode<7;mode++){
    var rejected=New(mode==4,true);rejected.selectPendingAck=mode!=0;rejected.receiptTimeouts=mode==0?2:1;rejected.wrongReceiptId=mode==1;rejected.genericTimeout=mode==2;rejected.changeEpochOnTimeout=mode==3;rejected.cancelOnTimeout=mode==5;rejected.changePeerOnTimeout=mode==6;
    bool failedRead=false;try{await rejected.StartRecording();}catch(Exception ex) when(ex is TimeoutException || mode==5 && ex is OperationCanceledException){failedRead=true;}
    Need(failedRead&&rejected.writes.Count==1,"Unqualified timeout/epoch/V2 failure continued or replayed START");
   }
   var restarted=New(false,true);restarted.receiptTimeouts=3;await restarted.StartRecording();
   Need(restarted.writes.Count==2 && restarted.writes.Count(b=>BitConverter.ToUInt32(b,8)==1)==1 && restarted.writes.Count(b=>BitConverter.ToUInt32(b,8)==2)==1 && restarted.pending==0,"Pending V1 restart did not confirm before exactly one START");
   Need(restarted.statusIds.Count>=5 && restarted.statusIds.All(id=>id!=0) && restarted.statusIds.Distinct().Count()==restarted.statusIds.Count,"Restart confirmation did not use fresh STATUS IDs");
   var ordinary=New(false,true);ordinary.selectPendingAck=false;ordinary.receiptTimeouts=1;await ordinary.StartRecording();
   Need(ordinary.writes.Count==2 && ordinary.pending==0,"Ordinary one-STATUS continuation no longer confirms before START");
   var mismatch=New(false,true);mismatch.mismatchSelectAck=true;mismatch.receiptTimeouts=2;bool mismatched=false;
   try{await mismatch.StartRecording();}catch(NpV1ReceiptTimeout){mismatched=true;}
   Need(mismatched && mismatch.writes.Count==1 && mismatch.statusIds.Count==2,"Mismatched SELECT ACK gained the restart allowance");
   var closed=New(false,true);closed.statusSteps.Enqueue(()=>{throw new NpV1ReceiptTimeout(0,closed.sequence);});
   closed.statusSteps.Enqueue(()=>{closed.pending=0;closed.current.Flags&=~8U;closed.current.Mask=1;});
   closed.statusSteps.Enqueue(()=>{throw new NpV1ReceiptTimeout(0,closed.sequence);});
   closed.statusSteps.Enqueue(()=>{throw new IOException("Restart allowance incorrectly remained open");});bool closedFailed=false;
   try{await closed.StartRecording();}catch(NpV1ReceiptTimeout){closedFailed=true;}
   Need(closedFailed && closed.writes.Count==1 && closed.statusIds.Count==3 && closed.statusSteps.Count==1,"A fresh pending-clear STATUS failed to close extra timeout continuation");
   foreach(bool atSelect in new[]{false,true}){
    var rejected=New(false,true);rejected.rejectSelect=atSelect;if(!atSelect)rejected.statusSteps.Enqueue(()=>{throw new IOException("simulated STATUS rejection");});bool refused=false;
    try{await rejected.StartRecording();}catch(IOException){refused=true;}
    Need(refused && rejected.writes.Count==1 && rejected.writes.All(b=>BitConverter.ToUInt32(b,8)==1),"Rejection caused a mutation replay or START");
   }
   for(int change=0;change<4;change++){
    int kind=change;var changed=New(false,true);changed.statusSteps.Enqueue(()=>{changed.pending=0;changed.current.Flags&=~8U;if(kind==0)changed.connection.Epoch++;if(kind==1)changed.address.Text="FEDCBA987654";if(kind==2)changed.stopRequested=true;if(kind==3)changed.connection.Connected=false;});bool refused=false;
    try{await changed.StartRecording();}catch(Exception ex) when(ex is IOException || kind==2 && ex is OperationCanceledException){refused=true;}
    Need(refused && changed.writes.Count==1 && changed.statusIds.Count==1,"A ready STATUS promoted changed peer/epoch/disconnection or canceled START");
   }
   foreach(int expiryRead in new[]{2,3}){
    var expired=New(false,true);System.Diagnostics.Stopwatch.ExpireOnRead=expiryRead;
    if(expiryRead==3)expired.statusSteps.Enqueue(()=>{expired.pending=0;expired.current.Flags&=~8U;});bool timedOut=false;
    try{await expired.StartRecording();}catch(IOException){timedOut=true;}
    Need(timedOut && expired.writes.Count==1 && expired.statusIds.Count==expiryRead-2,"Profile deadline allowed another STATUS or promoted a late ready receipt");
   }
   var peers=New(false,false);var requestA=new NpConfigRequest{Port=0,Identity="A-map"};peers.V1Configurations().Add(0,requestA);
   peers.connectedAddress=0xABCDEF123456UL;peers.address.Text="ABCDEF123456";NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==0,"Actual START on peerB staged peerA's map");await peers.StopRecording();
   var requestB=new NpConfigRequest{Port=1,Identity="B-map"};peers.V1Configurations().Add(1,requestB);NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==1&&Object.ReferenceEquals(NpConfigClient.Staged[0],requestB),"Actual START did not stage only peerB's own map");await peers.StopRecording();
   peers.connectedAddress=0x123456789ABCUL;peers.address.Text="123456789ABC";NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==1&&Object.ReferenceEquals(NpConfigClient.Staged[0],requestA),"Actual START after returning to peerA staged another peer's map");
   Need(NpConfigClient.ReuseMatching,"Actual BLE START did not enable fresh resident-map reuse");
  }
  [STAThread]static int Main(){try{var task=Tests();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(2);}task.GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" actual-source BLE Apply/Start checks; fake transport only.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
