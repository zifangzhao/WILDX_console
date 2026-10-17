using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

// Compile with the actual media partials and protocol/wire sources. Only the
// GATT boundary and pre-existing form services below are simulated; no radio.
namespace USB512_interface {
 internal static class NpV2PreviewBurst {internal const uint Magic=0x3256504e;}
 internal sealed partial class NpBleConnection {
  internal readonly List<byte[]> Writes=new List<byte[]>();
  internal bool Connected=true,LoseReceipt,ChangeEpoch;
  internal uint Mask=3,Hz=20000;internal int Pending;
  internal int Epoch {get{return epoch;}}
  internal byte[] Parameters=new byte[512];
  readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);
  readonly object receiveLock=new object();
  readonly NpBleWire wire=new NpBleWire();
  TaskCompletionSource<byte[]> receipt;Func<byte,byte[],bool> matches;int epoch;
  internal static void Put(byte[] b,int p,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,p,4);}
  Task WriteFrame(byte[] frame,int generation,CancellationToken cancel){
   Writes.Add((byte[])frame.Clone());
   if(frame[1]==0x33){
    Parameters=new byte[512];Buffer.BlockCopy(frame,2,Parameters,40,32);
    if(BitConverter.ToUInt32(frame,10)!=0){Parameters[12]=64;Parameters[13]=1;}
    Parameters[16]=(byte)(BitConverter.ToUInt32(frame,18)==0?0:1);Mask=Hz=0;Pending=2;
    if(LoseReceipt){LoseReceipt=false;wire.Feed(new byte[]{0xad,0x90,1,2,3});throw new TimeoutException("simulated lost profile receipt");}
   }
   var native=new byte[516];native[0]=0xad;native[1]=0x90;
   Buffer.BlockCopy(Parameters,0,native,2,512);native[514]=native[515]=0xda;
   for(int at=0;at<native.Length;at+=19){var part=new byte[Math.Min(19,native.Length-at)];Buffer.BlockCopy(native,at,part,0,part.Length);foreach(var reply in wire.Feed(part))if(matches(reply.Item1,reply.Item2))receipt.TrySetResult(reply.Item2);}
   if(ChangeEpoch){ChangeEpoch=false;epoch++;}return Task.CompletedTask;
  }
  internal NpSdStatus Status(){bool pending=Pending-->0;return new NpSdStatus{Mask=Mask,Hz=Hz,Flags=NpV1BleProtocol.MediaCapability+(Mask==0?NpV1BleProtocol.MediaSelected:0)+17U+(pending?8U:0U),Start=8192,Capacity=5000000};}
  internal Task<byte[]> ExchangeAsync(byte[] request){var b=new byte[64];Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("MEDIA1"),0,b,0,6);Put(b,8,1);Put(b,12,BitConverter.ToUInt32(Parameters,48));Put(b,16,BitConverter.ToUInt32(Parameters,56));Put(b,20,400);Put(b,56,1);Put(b,60,NpSdControl.Crc(b,60));return Task.FromResult(b);}
  internal bool ControlAfterTimeout(){var b=NpV1MediaStatus.Request();var f=new byte[68];f[0]=0xad;f[1]=0xb9;Buffer.BlockCopy(b,0,f,2,64);f[66]=f[67]=0xda;return wire.Feed(f).Count==1;}
 }
 internal sealed partial class NpBleForm : Form {
  sealed class Profile {internal uint Mask,Hz;}
  readonly NpBleConnection connection=new NpBleConnection();
  readonly ComboBox profiles=new ComboBox();readonly CheckBox poll=new CheckBox();
  readonly Label state=new Label();readonly List<string> messages=new List<string>();
  int recorderVersion=1,clockSyncs,controlUpdates,stopCalls;uint np2AvailableMask;
  bool busy,hostBusy,closing,mediaSettingsChanged,stopRequested;bool V2 {get{return false;}}
  NpSdStatus current;TaskCompletionSource<bool> heldStatus;
  void Append(string text){messages.Add(text);}
  void UpdateControls(){controlUpdates++;UpdateMediaControls();}
  void NotifyState(){}
  void OfferFirmwareReview(){} // Unrelated firmware UI service; media tests never display a form.
  Task StopRecording(){stopRequested=false;stopCalls++;return Task.CompletedTask;}
  void RequireStartNotCanceled(){if(stopRequested)throw new OperationCanceledException();}
  uint sequence;uint Next(){return ++sequence;}
  Task CheckClock(){return Task.CompletedTask;}
  async Task<NpSdStatus> ReadStatus(){if(heldStatus!=null)await heldStatus.Task;current=connection.Status();await RefreshV1Media(current);return current;}
  Task<NpSdStatus> Send(byte[] request){if(BitConverter.ToUInt32(request,8)!=0)throw new Exception("Unexpected mutation during profile confirmation");current=connection.Status();return Task.FromResult(current);}
  Task SyncClock(){clockSyncs++;return Task.CompletedTask;}
  // Actual Profiles and Run methods are inserted by the runner.
ACTUAL_FORM_METHODS
  static int checks;static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
  static async Task Refused(Func<Task> action,string why){bool refused=false;try{await action();}catch(Exception ex) when(ex is IOException||ex is ArgumentException){refused=true;}Need(refused,why);}
  static NpBleForm New(){var h=new NpBleForm();h.InitializeMediaControls(new FlowLayoutPanel());h.current=h.connection.Status();h.Profiles(h.current);for(int i=0;i<h.profiles.Items.Count;i++)if(((Profile)h.profiles.Items[i]).Mask==0)h.profiles.SelectedIndex=i;return h;}
  static async Task Tests(){
   foreach(uint fps in new uint[]{16,25,40,50})foreach(int streams in new[]{1,2,3}) {
    byte[] command=NpV1MediaStatus.ProfileCommand((streams&1)!=0,(streams&2)!=0,fps);
    Need(command.Length==35 && command[1]==0x33 && BitConverter.ToUInt32(command,10)==((streams&1)!=0?fps:0U) && BitConverter.ToUInt32(command,18)==((streams&2)!=0?200000U:0U),"rate-vector command changed");
   }
   foreach(int streams in new[]{1,2,3})using(var h=New()) {
    h.mediaCamera.Checked=(streams&1)!=0;h.mediaMicrophone.Checked=(streams&2)!=0;h.mediaRate.Value=40;
    await h.ApplyV1Media();
    Need(h.mediaConfirmed && h.clockSyncs==1 && h.connection.Mask==0,"profile/reset/time not confirmed");
    Need(h.connection.Writes.FindAll(b=>b[1]==0x33).Count==1,"profile mutation was replayed");
    Need(h.mediaCamera.Checked==((streams&1)!=0) && h.mediaMicrophone.Checked==((streams&2)!=0),"independent media streams lost");
    await h.VerifyV1Media();
    if((streams&1)!=0){h.mediaRate.Value=25;await h.RefreshV1Media(h.current);Need(h.mediaRate.Value==25,"poll overwrote pending rate edit");await Refused(()=>h.VerifyV1Media(),"unapplied rate admitted for Start");}
   }
   using(var h=New()) {
    h.connection.LoseReceipt=true;await h.ApplyV1Media();
    Need(h.mediaConfirmed && h.connection.Writes.FindAll(b=>b[1]==0x33).Count==1,"lost receipt did not use read-only recovery");
    Need(h.connection.Writes.TrueForAll(b=>b[1]==0x33 || b[1]==0x90),"Apply automatically started recording");
   }
   using(var h=New()) {
    h.current=h.connection.Status();h.connection.Pending=100;
    await Refused(()=>h.ApplyV1Media(),"pending device accepted a settings mutation");Need(h.connection.Writes.Count==0,"busy recorder received mutation");
   }
   using(var h=New()) {
    h.connection.LoseReceipt=true;try{await h.connection.ExchangeParameters(NpV1MediaStatus.ProfileCommand());}catch(TimeoutException){}
    Need(h.connection.ControlAfterTimeout(),"truncated parameters blocked later status controls");
   }
   using(var h=New()) {
    h.connection.ChangeEpoch=true;await Refused(()=>h.ApplyV1Media(),"connection change was not fenced");
    Need(h.connection.Writes.FindAll(b=>b[1]==0x33).Count==1,"connection change replayed mutation");
   }
   using(var h=New()) {
    await Refused(()=>h.connection.ExchangeParameters(null),"null native command admitted");await Refused(()=>h.connection.ExchangeParameters(new byte[0]),"empty native command admitted");
    var selected=h.profiles.SelectedItem;h.Profiles(h.current);Need(Object.ReferenceEquals(selected,h.profiles.SelectedItem),"status rebuilt an unchanged profile menu");
    int updates=h.controlUpdates;h.heldStatus=new TaskCompletionSource<bool>();h.refreshTask=h.RefreshStatusQuietly();
    Need(!h.busy && h.controlUpdates==updates,"poll disabled UI while awaiting the device");
    bool ran=false;var action=h.Run(()=>{ran=true;return Task.CompletedTask;});Need(!ran,"foreground command overlapped status poll");
    h.heldStatus.SetResult(true);await action;Need(ran && !h.busy,"click during polling was dropped");
    h.heldStatus=new TaskCompletionSource<bool>();h.refreshTask=h.RefreshStatusQuietly();
    var pending=h.Run(()=>Task.CompletedTask);h.stopRequested=true;h.heldStatus.SetResult(true);await pending;
    Need(h.stopCalls==1&&!h.stopRequested&&!h.busy,"Stop requested during another transfer was lost or repeated");
   }
   var s=new NpSdStatus{Mask=0,Hz=0,Flags=NpV1BleProtocol.MediaCapability|NpV1BleProtocol.MediaSelected|17,Start=8192,Capacity=5000000};
   Need(NpV1BleProtocol.EndLba(s,60,40,false)<NpV1BleProtocol.EndLba(s,60,50,true),"camera-only reservation still assumes50fps+microphone");
   Need(NpV1BleProtocol.EndLba(s,60,0,true)<NpV1BleProtocol.EndLba(s,60,40,false),"audio-only reserves camera chunks");
   Console.WriteLine("PASS "+checks+" actual-source BLE media flow/framing checks; fake GATT only, no hardware.");
  }
  [STAThread]static int Main(){try{var task=Tests();while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(2);}task.GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
