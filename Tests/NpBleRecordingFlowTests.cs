using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

// The actual Apply/Start handlers are inserted by the runner. Only their
// transport/configuration services are faked; the V1/V2 admission is real.
namespace WildNpConfiguration {
 class NpConfigRequest {internal byte Port;internal string Identity;}
 class Np2ConfigRequest {}
 class NpConfigClient {
  internal static readonly List<NpConfigRequest> Staged=new List<NpConfigRequest>();
  internal NpConfigClient(object transport){}
  internal Task StageForCaptureAsync(IEnumerable<NpConfigRequest> requests,int mask,CancellationToken cancel){foreach(var request in requests)if((mask&(1<<request.Port))!=0)Staged.Add(request);return Task.CompletedTask;}
 }
 class Np2ConfigClient {
  internal Np2ConfigClient(object transport){}
  internal Task StageAsync(Np2ConfigRequest request,CancellationToken cancel){return Task.CompletedTask;}
 }
}
namespace USB512_interface {
 using WildNpConfiguration;
 class NpConfigurationDialog {internal static Np2ConfigRequest PrepareV2Request(string json,int port){return new Np2ConfigRequest();}}
 class NpBleConfigurationReceipts {internal void Reset(){}internal void Expect(IEnumerable<Np2ConfigRequest> requests){}}
 class NpBleForm {
  sealed class Profile {internal uint Mask,Hz;}
  sealed class Connection {internal bool Connected=true;internal int Epoch=1;}
  sealed class ConfigTransport {internal ConfigTransport(Connection connection){}}
  sealed class Clock {internal bool Synchronized=true;}
  readonly ComboBox profiles=new ComboBox();readonly TextBox address=new TextBox{Text="123456789ABC"};
  readonly CheckBox unqualified=new CheckBox{Checked=true},mediaCamera=new CheckBox(),mediaMicrophone=new CheckBox();readonly NumericUpDown mediaRate=new NumericUpDown();
  readonly Label configurationStatus=new Label();readonly Connection connection=new Connection();readonly Clock clock=new Clock();
  readonly NpBleConfigurationReceipts configurationReceipts=new NpBleConfigurationReceipts();
  readonly Dictionary<ulong,Dictionary<int,string>> np2HostConfigurations=new Dictionary<ulong,Dictionary<int,string>>();
  readonly Dictionary<ulong,Dictionary<byte,NpConfigRequest>> v1HostConfigurations=new Dictionary<ulong,Dictionary<byte,NpConfigRequest>>();
  ulong connectedAddress=0x123456789ABCUL;
ACTUAL_V1_CONFIGURATIONS
  readonly List<byte[]> writes=new List<byte[]>();
  NpSdStatus current;string configurationReadError;bool V2,loseSelectReceipt,failConfirmation,mediaSettingsChanged,stopRequested,stopping,loseStopReceipt,cancelDuringSelect,wrongReceiptId,genericTimeout,changeEpochOnTimeout,cancelOnTimeout,changePeerOnTimeout;int pending,receiptTimeouts;uint operation,sequence;
  void UpdateControls(){}
  uint Next(){return current.LastId+1;}
  HashSet<int> Np2ApplyRequests(ulong peer){return null;}
  void Append(string text){}
  Task VerifyV1Media(){throw new Exception("Unexpected media path");}
  Task ApplyV1Media(){throw new Exception("Unexpected media path");}
  async Task<NpSdStatus> ReadStatus(){
   await Task.CompletedTask;
   sequence++;
   if(pending>0 && receiptTimeouts>0){receiptTimeouts--;if(changeEpochOnTimeout)connection.Epoch++;if(cancelOnTimeout)stopRequested=true;if(changePeerOnTimeout)address.Text="FEDCBA987654";if(genericTimeout)throw new TimeoutException("simulated write timeout");throw new NpV1ReceiptTimeout(0,wrongReceiptId?sequence+1:sequence);}
   if(pending>0){if(failConfirmation)throw new IOException("simulated read failure");if(cancelDuringSelect){stopRequested=true;cancelDuringSelect=false;}if(--pending==0){current.Flags&=~8U;if(operation==3){current.Flags&=~4U;current.State=current.Fault!=0?(V2?255U:4U):(V2?5U:3U);}}}
   return current;
  }
  Task<NpSdStatus> Send(byte[] request){
   writes.Add(request);uint op=BitConverter.ToUInt32(request,8);operation=op;current.LastId=BitConverter.ToUInt32(request,12);
   if(op==1){current.Mask=BitConverter.ToUInt32(request,16);current.Hz=BitConverter.ToUInt32(request,20);current.Flags|=8;pending=2;if(loseSelectReceipt)throw new TimeoutException("simulated lost SELECT receipt");}
   if(op==2){current.Fault=0;current.State=V2?3U:1U;current.Flags|=4U;}
   if(op==3){current.Flags|=8U;pending=2;if(loseStopReceipt)throw new TimeoutException("simulated lost STOP receipt");if(current.Fault==0)current.State=V2?4U:2U;}
   return Task.FromResult(current);
  }
ACTUAL_RECORDING_METHODS
  static int checks;static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
  static NpBleForm New(bool v2,bool changed){
   var form=new NpBleForm{V2=v2,current=new NpSdStatus{State=v2?1U:0U,Mask=changed?1U:3U,Hz=20000,Flags=17U|2U|512U|1048576U,Start=8192,Capacity=5000000,LastId=40}};
   form.profiles.Items.Add(new Profile{Mask=3,Hz=20000});form.profiles.SelectedIndex=0;return form;
  }
  static async Task Tests(){
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
    Need(form.StopConfirmed(form.current)&&!form.stopping,"Stop did not confirm completion after drain");
    var faulted=New(v2,false);faulted.current.State=v2?255U:4U;faulted.current.Fault=2;await faulted.StopRecording();
    Need(faulted.writes.Count==0&&faulted.current.Fault==2,"Already finalized failure required Complete, replayed Stop or cleared the fault");
    if(!v2){faulted.current.Flags|=4U;await faulted.StopRecording();Need(faulted.writes.Count==1&&faulted.StopConfirmed(faulted.current),"Failed state was declared stopped before ownership cleared");}
    var lost=New(v2,false);lost.current.State=v2?3U:1U;lost.current.Flags|=4U;lost.loseStopReceipt=true;bool timedOut=false;
    try{await lost.StopRecording();}catch(TimeoutException){timedOut=true;}Need(timedOut&&lost.writes.Count==1&&!lost.stopping,"Lost Stop ACK caused replay or stuck UI");
   }
   var canceled=New(false,true);canceled.cancelDuringSelect=true;bool canceledStart=false;
   try{await canceled.StartRecording();}catch(OperationCanceledException){canceledStart=true;}
   Need(canceledStart&&canceled.writes.Count==1,"Stop during SELECT preparation released START");
   await canceled.StopRecording();Need(!canceled.stopRequested&&canceled.StopConfirmed(canceled.current)&&canceled.writes.Count==1,"Pending SELECT was overwritten or a canceled start remained active");
   var delayed=New(false,true);delayed.receiptTimeouts=1;await delayed.StartRecording();Need(delayed.writes.Count==2&&delayed.writes.Count(b=>BitConverter.ToUInt32(b,8)==1)==1&&delayed.writes.Count(b=>BitConverter.ToUInt32(b,8)==2)==1,"Qualifying STATUS timeout replayed mutation or prevented confirmed start");
   for(int mode=0;mode<7;mode++){
    var rejected=New(mode==4,true);rejected.receiptTimeouts=mode==0?2:1;rejected.wrongReceiptId=mode==1;rejected.genericTimeout=mode==2;rejected.changeEpochOnTimeout=mode==3;rejected.cancelOnTimeout=mode==5;rejected.changePeerOnTimeout=mode==6;
    bool failedRead=false;try{await rejected.StartRecording();}catch(Exception ex) when(ex is TimeoutException || mode==5 && ex is OperationCanceledException){failedRead=true;}
    Need(failedRead&&rejected.writes.Count==1,"Unqualified timeout/epoch/V2 failure continued or replayed START");
   }
   var peers=New(false,false);var requestA=new NpConfigRequest{Port=0,Identity="A-map"};peers.V1Configurations().Add(0,requestA);
   peers.connectedAddress=0xABCDEF123456UL;peers.address.Text="ABCDEF123456";NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==0,"Actual START on peerB staged peerA's map");await peers.StopRecording();
   var requestB=new NpConfigRequest{Port=1,Identity="B-map"};peers.V1Configurations().Add(1,requestB);NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==1&&Object.ReferenceEquals(NpConfigClient.Staged[0],requestB),"Actual START did not stage only peerB's own map");await peers.StopRecording();
   peers.connectedAddress=0x123456789ABCUL;peers.address.Text="123456789ABC";NpConfigClient.Staged.Clear();await peers.StartRecording();
   Need(NpConfigClient.Staged.Count==1&&Object.ReferenceEquals(NpConfigClient.Staged[0],requestA),"Actual START after returning to peerA staged another peer's map");
  }
  [STAThread]static int Main(){try{var task=Tests();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(2);}task.GetAwaiter().GetResult();Console.WriteLine("PASS "+checks+" actual-source BLE Apply/Start checks; fake transport only.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
