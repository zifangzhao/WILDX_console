using System;
using System.Diagnostics;
using System.IO;

// Compiles mechanically extracted runner helpers and the actual pure control
// codecs against endpoint spies. No GUI assembly, WinRT, GATT or hardware.
internal static class Np2BleHardwareRecordTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static string Between(string source,string first,string next){
        int start=source.IndexOf(first,StringComparison.Ordinal);
        int end=start<0?-1:source.IndexOf(next,start,StringComparison.Ordinal);
        Need(start>=0 && end>start,"Actual helper boundaries missing: "+first);
        return source.Substring(start,end-start);
    }
    static int Main(string[] args)
    {
        string folder=null;
        try {
            if(args.Length!=2)throw new ArgumentException("<Np2BleHardware.cs> <cached-csc.exe>");
            string source=File.ReadAllText(args[0]);
            string recovery=Between(source,"if (action == \"recover\")","if (action == \"format\")");
            Need(recovery.Contains("\"CanRecover\",lastStatus") && recovery.Contains("\"CanQuarantine\",lastStatus") &&
                recovery.IndexOf("RequireHealthy(lastStatus);",StringComparison.Ordinal)<recovery.IndexOf("\"Request\",8U,id,0U,0U,0UL",StringComparison.Ordinal),"Preservation must require healthy stopped quarantinable admission before its single command.");
            string form=File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"..","USB512_interface","USB512_interface","Ble","NpBleForm.cs"));
            Need(form.Contains("NpV2SdControl.Request(8, NpV2SdControl.NextId(s))"),"Preservation opcode must match the canonical GUI recovery contract, not Mount.");
            Need(recovery.Contains("\"Request\",8U,id,0U,0U,0UL") && recovery.Contains("\"Parse\",reply,8U,id") &&
                recovery.Split(new[]{"\"ExchangeAsync\""},StringSplitOptions.None).Length==2 && !recovery.Contains("SendV2"),"Preservation must send exactly one non-format request with a matching opcode/id receipt.");
            Need(recovery.Contains("i<30") && recovery.Contains("\"CanStart\",lastStatus") &&
                recovery.Contains("U(Get(lastStatus,\"Start\"))>before") && recovery.Contains("U(Get(lastStatus,\"Capacity\"))!=capacity"),"Preservation completion must be bounded and prove stable card plus advanced ready LBA.");
            string options=Between(source,"static bool RecordPreview","static async Task<BluetoothAddressType> ObserveAddressType");
            options+=Between(source,"static void RequireCaps(object caps","static void RequireStartable");
            string clock=Between(source,"static void RequireRecordConnection","static async Task<object> SendV2");
            string reflection=Between(source,"static object Invoke(object target","static uint U(object value)");
            int record=source.IndexOf("RequireStartable(lastStatus);",StringComparison.Ordinal);
            int end=source.IndexOf("static async Task RunSync",record,StringComparison.Ordinal);
            Need(record>=0 && end>record,"Actual record path missing.");
            string path=source.Substring(record,end-record);
            Need(source.Contains("int mapCount=RecordMapCount(args),rateIndex=4+mapCount") && source.Contains("fullDuration = RecordFlag(args,\"full-duration\")") &&
                source.Contains("args.Length != 4") && source.Contains("RequireRecordDuration(recordHz,reservationSeconds,fullDuration,recordMask)") &&
                source.Contains("const uint DefaultReservationSeconds = 60U;"),"Option/rate/duration admission missing before connection, or default changed.");
            Need(source.Contains("uint recordMask=action==\"record3\"?7U:RequiredMask") &&
                source.IndexOf("if(isRecord)RequireRecordHost",StringComparison.Ordinal)<source.IndexOf("await RunConnected",StringComparison.Ordinal),
                "Triple host admission must use explicit mask7 and refuse before the connection/START path.");
            Need(path.Contains("PrepareRequest(mapInputs[2],3,output)") && path.Contains("\"StageAsync\",p3,cancel") &&
                path.Contains("\"StatusAsync\",(byte)2,cancel") && path.Contains("\"RequireApplied\",applied3,p3") &&
                path.Contains("LogReceipt(\"staged-P3\"") && path.Contains("LogReceipt(\"applied-P3\""),
                "Triple recording must stage/apply/read the real P3 request and receipt.");
            Need(path.Contains("\"profile\",recordMask") && path.Contains("\"start\",recordMask") &&
                path.Contains("RequireRecordProfile(lastStatus,recordMask,recordHz)") &&
                path.Contains("\"CapturePreview\", NextId(), 1U, 0U, recordHz, cancel"),
                "Selected profile is not explicit/pinned, or disposable preview was broadened to P3.");
            Need(source.Contains("RequireRecordReconnectOptions(recordReconnect,fullDuration,reservationSeconds)") &&
                source.Contains("TimeSpan.FromSeconds(RecordBudgetSeconds(recordReconnect,reservationSeconds))"),"Reconnect restrictions/conditional budget are not wired.");
            Need(path.Contains("\"Blocks\", lastStatus, recordHz, reservationSeconds") && path.Contains("blocks == 0") &&
                path.Contains("RecordStopDue(fullDuration,reservationSeconds,recorded.Elapsed)"),"Finite reservation/default Stop policy bypassed.");
            int initialize=path.IndexOf("Invoke(clock, \"Initialize\"",StringComparison.Ordinal);
            int heartbeat=path.IndexOf("RecordHeartbeatDue(state",StringComparison.Ordinal);
            int preview=path.IndexOf("if (requestPreview &&",StringComparison.Ordinal);
            Need(initialize>=0 && heartbeat>initialize && preview>heartbeat,"Heartbeat must follow initial sync and fresh STATUS, before disposable preview.");
            string heartbeatPath=Between(path,"if(state==3 && RecordHeartbeatDue","if (state == 3 && !applied)");
            Need(heartbeatPath.Contains("ObserveRecordingClock") && heartbeatPath.Contains("heartbeatErrors++") &&
                heartbeatPath.Contains("acquisition continues") && !heartbeatPath.Contains("SendV2") &&
                !heartbeatPath.Contains("Initialize") && !heartbeatPath.Contains("Synchronize") && !heartbeatPath.Contains("Reset"),"Recording heartbeat mutates or prematurely Stops acquisition.");
            Need(path.Contains("RequireRecordComplete(fullDuration,U(Get(lastStatus,\"Chunks\")),blocks,reservationSeconds,heartbeats,heartbeatErrors)") &&
                path.IndexOf("RequireRecordComplete",StringComparison.Ordinal)<path.IndexOf("complete=true",StringComparison.Ordinal),"Full/heartbeat verdict is not guarded before Complete PASS.");
            Need(path.Contains("RequireRecordingClockRequest(request,clockMagic)") && path.Contains("ExchangeTimed(connection,request,cancel,recordingClock?"),"Record clock bypasses READ-only/pre-dispatch timing.");
            string reconnect=Between(source,"static async Task<object> ReconnectRecording","static async Task RunSync");
            int connect=reconnect.IndexOf("Invoke(connection,\"ConnectAsync\",address)",StringComparison.Ordinal);
            int status=reconnect.IndexOf("await SendV2(connection,0,0,0",StringComparison.Ordinal);
            int caps=reconnect.IndexOf("\"CapsAsync\"",StringComparison.Ordinal);
            Need(reconnect.Split(new[]{"Invoke(connection,\"ConnectAsync\",address)"},StringSplitOptions.None).Length==2 &&
                connect>reconnect.IndexOf("Task.Delay(2000,cancel)",StringComparison.Ordinal) && status>connect && caps>status,
                "Reconnect must connect exactly once, then STATUS before CAPS/other operations.");
            Need(reconnect.Contains("RequireRecordReconnectIdentity") && reconnect.Contains("RequireCaps(caps0,0)") &&
                reconnect.Contains("RequireCaps(caps1,1)") && reconnect.Contains("\"StatusAsync\"") &&
                reconnect.Contains("\"RequireApplied\",applied1,p1") && reconnect.Contains("\"RequireApplied\",applied2,p2") &&
                reconnect.Contains("LogReceipt(\"record-reconnect-applied-P1\"") && reconnect.Contains("LogReceipt(\"record-reconnect-applied-P2\""),
                "Reconnect identity/applied map CRC and IDs are not independently checked/logged.");
            Need(reconnect.Contains("(snapshot[0]==7U)!=(p3!=null)") && reconnect.Contains("RequireCaps(caps2,2)") &&
                reconnect.Contains("\"RequireApplied\",applied3,p3") && reconnect.Contains("LogReceipt(\"record-reconnect-applied-P3\""),
                "Reconnect can omit the third selected port or its real applied request proof.");
            Need(!reconnect.Contains("Initialize") && !reconnect.Contains("Synchronize") && !reconnect.Contains("StageAsync") &&
                !reconnect.Contains("RescueReset") && !reconnect.Contains("SendV2(connection,1") && !reconnect.Contains("SendV2(connection,2") &&
                !reconnect.Contains("SendV2(connection,3"),"Reconnect repeats a clock or recorder mutation.");
            int reconnectCall=path.IndexOf("await ReconnectRecording",StringComparison.Ordinal);
            int rebind=reconnectCall<0?-1:path.IndexOf("recordEpoch=U(Get(connection,\"Epoch\"))",reconnectCall,StringComparison.Ordinal);
            Need(path.Contains("RequireRecordReconnectComplete(recordReconnect,reconnectWitnessed,postReconnectHeartbeats)") &&
                path.Contains("if(reconnectWitnessed)postReconnectHeartbeats++") &&
                reconnectCall>=0 && rebind>reconnectCall,"Reconnect admission/heartbeat verdict or epoch update is missing.");
            string cleanup=Between(path,"if(autonomous){","}else if(state==2 || state==3 || state==6){");
            Need(cleanup.Contains("finally_stop=FORBIDDEN") && !cleanup.Contains("SendV2") &&
                source.Contains("bool autonomous = RecordAutonomous(recordReconnect,fullDuration,reservationSeconds)") &&
                path.Contains("SendV2(connection, 3, 0, 0, CancellationToken.None, \"finally-stop\")"),
                "Autonomous finite cleanup Stops, or legacy cleanup changed.");
            string send=Between(source,"static async Task<object> SendV2","static object NewConfigClient");
            Need(send.Contains("await ExchangeTimed(connection,request,cancel,label)") &&
                send.IndexOf("await ExchangeTimed",StringComparison.Ordinal)<send.IndexOf("\"Parse\"",StringComparison.Ordinal),"Control timing starts after response/parsing.");
            folder=Path.Combine(Path.GetTempPath(),"Np2BleRecord_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"RecordHarness.cs"),exe=Path.Combine(folder,"RecordHarness.exe");
            File.WriteAllText(program,Harness(options,clock,reflection));
            string project=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(args[0]),"..","USB512_interface","USB512_interface"));
            string codecs=Quote(Path.Combine(project,"Ble","NpV2SdControl.cs"))+" "+
                Quote(Path.Combine(project,"Ble","NpSdControl.cs"))+" "+Quote(Path.Combine(project,"NpLogger","NpSdGeometry.cs"));
            var compile=new ProcessStartInfo(args[1],"/nologo /optimize+ /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)+" "+codecs){
                UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){
                string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();
                if(p.ExitCode!=0)throw new Exception("Extracted record helpers failed to compile: "+output);
            }
            var run=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(run)){
                string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();p.WaitForExit();
                if(p.ExitCode!=0)throw new Exception("Extracted record helpers failed: "+output);Console.Write(output);
            }
            return 0;
        } catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){}}
    }

    static string Harness(string options,string clock,string reflection)
    {
        return @"using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
static class Harness {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 const uint Magic=0x3254504e;
 const uint RequiredMask=3;
 const uint DefaultReservationSeconds=60;
 const int OverallSeconds=180;
 static int checks;
 static List<string> messages=new List<string>();
 static uint U(object value){return Convert.ToUInt32(value,CultureInfo.InvariantCulture);}
 static string Hex(byte[] value){return value==null?"""":BitConverter.ToString(value).Replace(""-"","""");}
 static void Log(string value){messages.Add(value);}
 static void Need(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static void Refuse(Action action){bool refused=false;try{action();}catch(IOException){refused=true;}Need(refused,""expected quality refusal"");}
 static void BadOption(string[] args){bool refused=false;try{RecordPreview(args);}catch(ArgumentException){refused=true;}Need(refused,""invalid option admitted"");}
 static byte[] Request(uint op){var r=new byte[64];Buffer.BlockCopy(BitConverter.GetBytes(Magic),0,r,0,4);Buffer.BlockCopy(BitConverter.GetBytes(op),0,r,8,4);return r;}
 sealed class Connection {
  public bool Connected=true;
  public uint Epoch=7;
  public int Calls;
  public bool Fail,ChangeEpoch;
  public byte[] Last;
  public Task<byte[]> ExchangeAsync(byte command,byte[] request,CancellationToken cancel){
   Calls++;Need(command==0xb9,""wrong wire command"");Last=request;
   Thread.Sleep(35); // Synchronous dispatch before the returned Task exists.
   return Reply(cancel);
  }
  async Task<byte[]> Reply(CancellationToken cancel){
   await Task.Delay(35,cancel);if(Fail)throw new IOException(""deliberate response failure"");
   if(ChangeEpoch)Epoch++;return new byte[]{1,2,3};
  }
 }
 sealed class Clock {
  public bool Synchronized=true;
  public uint Generation=9,Operation;
  public int ObserveCalls;
  public double BoundMilliseconds=20,OffsetMilliseconds=0;
  public async Task Observe(Func<byte[],Task<byte[]>> exchange,Func<uint> next){
   ObserveCalls++;Need(next()!=0,""heartbeat reused reserved ID"");await exchange(Request(Operation));
  }
 }
 sealed class Status {
  public uint State=3,Error,Fault,Mask=3,Hz=30000,Start=12000,End=50000,Capacity=1000000,LastId=123,Chunks=400;
 }
 sealed class Caps {
  public uint AvailableMask=7,SupportedModel=4,Uid0=0x001A0004,Uid1=0x43425006,Uid2=0x2039314e;
 }
 // Signature-only legacy host witness; request bytes come from the real codec.
 sealed class LegacyControl {
  internal static byte[] Request(uint op,uint id,uint hz,uint blocks,ulong utc){return CE32_console.NpV2SdControl.Request(op,id,hz,blocks,utc);}
 }
"+options+clock+reflection+@"
 static async Task Run(){
  string[] basic={""exe"",""peer"",""out"",""record"",""p1"",""p2""};
  string[] full=basic.Concat(new[]{""20000"",""60"",""full-duration""}).ToArray();
  Need(RecordPreview(basic) && !RecordFlag(basic,""full-duration""),""default changed"");
  Need(RecordMapCount(basic)==2,""legacy two-input indices changed"");
  string[] triple={""exe"",""peer"",""out"",""record3"",""p1"",""p2"",""p3""};
  Need(RecordMapCount(triple)==3 && RecordPreview(triple) && !RecordFlag(triple,""full-duration""),""triple requires real P3/default changed"");
  BadOption(basic.Take(4).Concat(new[]{""p1"",""p2""}).Select((v,i)=>i==3?""record3"":v).ToArray());
  foreach(uint hz in new uint[]{15000,20000}){
   RequireRecordDuration(hz,60,true,7);
   var flags=triple.Concat(new[]{hz.ToString(),""60"",""record-reconnect"",""no-preview"",""full-duration""}).ToArray();
   Need(RecordMapCount(flags)==3 && RecordFlag(flags,""record-reconnect"") && RecordFlag(flags,""full-duration"") && !RecordPreview(flags),""triple rate/duration/flag indices rejected"");
   BadOption(triple.Concat(new[]{hz.ToString(),""60"",""no-preview"",""no-preview""}).ToArray());
   BadOption(triple.Concat(new[]{hz.ToString(),""60"",""unknown""}).ToArray());
  }
  foreach(uint mask in new uint[]{0,1,2,4,8,0x103}){
   bool rejected=false;try{RequireRecordDuration(20000,60,true,mask);}catch(ArgumentException){rejected=true;}Need(rejected,""unsupported/narrowed mask admitted"");
  }
  foreach(uint hz in new uint[]{0,30000,40000}){
   bool rejected=false;try{RequireRecordDuration(hz,60,true,7);}catch(ArgumentException){rejected=true;}Need(rejected,""unsupported triple rate admitted"");
  }
  Type control=typeof(CE32_console.NpV2SdControl);
  RequireRecordHost(typeof(LegacyControl),3,20000);
  bool legacyRefused=false;try{RequireRecordHost(typeof(LegacyControl),7,20000);}catch(MissingMethodException){legacyRefused=true;}
  Need(legacyRefused,""legacy host silently constructed a dual START for record3"");
  RequireCaps(new Caps(),2);RequireCaps(new Caps{AvailableMask=3},0);RequireCaps(new Caps{AvailableMask=3},1);
  Refuse(()=>RequireCaps(new Caps{AvailableMask=3},2));
  Refuse(()=>RequireCaps(new Caps{AvailableMask=15},2));
  Refuse(()=>RequireCaps(new Caps{Uid0=0x001A0020},2));
  uint[,] pairs={{3,20000,31248},{3,30000,46872},{7,15000,35156},{7,20000,46872}};
  for(int p=0;p<pairs.GetLength(0);p++){
   uint mask=pairs[p,0],hz=pairs[p,1];RequireRecordHost(control,mask,hz);
   foreach(uint op in new uint[]{1,2}){
    byte[] built=RecordRequest(control,op,91,hz,op==2?pairs[p,2]:0,mask);
    Need(BitConverter.ToUInt32(built,16)==mask && BitConverter.ToUInt32(built,20)==hz &&
     BitConverter.ToUInt32(built,60)==CE32_console.NpSdControl.Crc(built,60),""production request lost full32 mask/rate/CRC"");
    if(mask==3)Need(built.SequenceEqual(CE32_console.NpV2SdControl.Request(op,91,hz,op==2?pairs[p,2]:0)),""legacy five-parameter request bytes changed"");
   }
   foreach(uint op in new uint[]{0,3})Need(BitConverter.ToUInt32(RecordRequest(control,op,91,0,0,mask),16)==0,""STATUS/STOP acquired selected mask"");
   var stopped=new CE32_console.NpSdStatus{State=1,Mask=mask,Hz=hz,Start=8192,Capacity=501301248,Flags=128|512};
   Need(CE32_console.NpV2SdControl.Blocks(stopped,hz,60)==pairs[p,2],""actual selected-profile finite block accounting wrong"");
   var status=new Status{Mask=mask,Hz=hz};RequireRecordProfile(status,mask,hz);
   Refuse(()=>RequireRecordProfile(new Status{Mask=mask==3?7U:3U,Hz=20000},mask,hz));
   Refuse(()=>RequireRecordProfile(new Status{Mask=mask+256U,Hz=hz},mask,hz));
   Refuse(()=>RequireRecordProfile(new Status{Mask=mask,Hz=hz==20000?30000U:20000U},mask,hz));
  }
  Need(RecordPreview(full) && RecordFlag(full,""full-duration""),""full option rejected"");
  foreach(string hz in new[]{""20000"",""30000""})foreach(string[] flags in new[]{new[]{""no-preview"",""full-duration""},new[]{""full-duration"",""no-preview""}}){
   var a=basic.Concat(new[]{hz,""60""}).Concat(flags).ToArray();
   Need(!RecordPreview(a) && RecordFlag(a,""full-duration""),""combined options rejected"");
  }
  Need(!RecordPreview(basic.Concat(new[]{""20000"",""10"",""no-preview""}).ToArray()),""old no-preview changed"");
  foreach(string[] flags in new[]{new[]{""full-duration"",""full-duration""},new[]{""no-preview"",""no-preview""},new[]{""FULL-DURATION""},new[]{""unknown""},new[]{""full-duration"",""no-preview"",""extra""}})
   BadOption(basic.Concat(new[]{""20000"",""60""}).Concat(flags).ToArray());
  BadOption(null);BadOption(new string[5]);
  foreach(string hz in new[]{""20000"",""30000""}){
   var flags=basic.Concat(new[]{hz,""60"",""record-reconnect"",""no-preview"",""full-duration""}).ToArray();
   Need(RecordFlag(flags,""record-reconnect"") && RecordFlag(flags,""full-duration"") && !RecordPreview(flags),""reconnect option combination rejected"");
  }
  BadOption(basic.Concat(new[]{""20000"",""60"",""record-reconnect"",""record-reconnect""}).ToArray());
  foreach(uint hz in new uint[]{20000,30000}){
   foreach(uint seconds in new uint[]{1,10,60})RequireRecordDuration(hz,seconds,false);
   foreach(uint seconds in new uint[]{1,60,120,121,900})RequireRecordDuration(hz,seconds,true);
   foreach(uint seconds in new uint[]{0,901,UInt32.MaxValue}){
    bool refused=false;try{RequireRecordDuration(hz,seconds,true);}catch(ArgumentException){refused=true;}Need(refused,""invalid finite bound admitted"");
   }
   bool rejected=false;try{RequireRecordDuration(hz,120,false);}catch(ArgumentException){rejected=true;}Need(rejected,""120 admitted without autonomous full duration"");
   rejected=false;try{RequireRecordDuration(hz,900,false);}catch(ArgumentException){rejected=true;}Need(rejected,""900 admitted without autonomous full duration"");
   var flags=basic.Concat(new[]{hz.ToString(),""120"",""record-reconnect"",""no-preview"",""full-duration""}).ToArray();
   Need(RecordFlag(flags,""record-reconnect"") && RecordFlag(flags,""full-duration"") && !RecordPreview(flags),""120 reconnect options refused"");
   flags=basic.Concat(new[]{hz.ToString(),""900"",""record-reconnect"",""no-preview"",""full-duration""}).ToArray();
   Need(RecordFlag(flags,""record-reconnect"") && RecordFlag(flags,""full-duration"") && !RecordPreview(flags),""900 reconnect options refused"");
  }
  foreach(uint hz in new uint[]{0,15000,40000}){
   bool rejected=false;try{RequireRecordDuration(hz,120,true);}catch(ArgumentException){rejected=true;}Need(rejected,""unsupported rate admitted"");
  }
  RequireRecordReconnectOptions(false,false,10);RequireRecordReconnectOptions(false,true,60);RequireRecordReconnectOptions(true,true,60);RequireRecordReconnectOptions(true,true,120);
  RequireRecordReconnectOptions(true,true,900);
  bool bad=false;try{RequireRecordReconnectOptions(true,false,60);}catch(ArgumentException){bad=true;}Need(bad,""reconnect admitted without full duration"");
  bad=false;try{RequireRecordReconnectOptions(true,false,900);}catch(ArgumentException){bad=true;}Need(bad,""900 reconnect admitted without full duration"");
  foreach(uint seconds in new uint[]{0,1,10,59,61,119,121,899,901}){
   bad=false;try{RequireRecordReconnectOptions(true,true,seconds);}catch(ArgumentException){bad=true;}Need(bad,""reconnect admitted wrong bound"");
  }
  Need(RecordBudgetSeconds(false,60)==180 && RecordBudgetSeconds(true,60)==240,""conditional reconnect budget changed legacy budget"");
  Need(RecordBudgetSeconds(false,120)==420 && RecordBudgetSeconds(true,120)==420,""120 finite budget is not bounded420"");
  Need(RecordBudgetSeconds(false,121)==2400 && RecordBudgetSeconds(true,121)==2400 &&
   RecordBudgetSeconds(false,900)==2400 && RecordBudgetSeconds(true,900)==2400,"">120 finite budget is not bounded2400"");
  Need(RecordBudgetSeconds(false,1)==180 && RecordBudgetSeconds(false,10)==180,""short legacy budget changed"");
  Need(!RecordAutonomous(false,false,60) && !RecordAutonomous(false,true,60) && RecordAutonomous(true,true,60),""legacy cleanup policy changed"");
  Need(RecordAutonomous(false,true,120) && RecordAutonomous(true,true,120),""120 failure cleanup can emit Stop"");
  Need(RecordAutonomous(false,true,900) && RecordAutonomous(true,true,900),""900 failure cleanup can emit Stop"");
  Need(RecordReconnectDue(true,false,true,3,10000),""valid reconnect deadline refused"");
  Need(!RecordReconnectDue(true,false,true,3,9999) && !RecordReconnectDue(false,false,true,3,20000) &&
   !RecordReconnectDue(true,true,true,3,20000) && !RecordReconnectDue(true,false,false,3,20000),""reconnect repeated/early/unapplied"");
  foreach(uint state in new uint[]{0,1,2,4,5,6,7,255})Need(!RecordReconnectDue(true,false,true,state,20000),""reconnect outside Recording"");
  foreach(uint mask in new uint[]{3,7})foreach(uint hz in mask==3?new uint[]{20000,30000}:new uint[]{15000,20000}){
   var beforeStatus=new Status{Mask=mask,Hz=hz};uint[] snapshot=RecordReconnectSnapshot(beforeStatus);var afterStatus=new Status{Mask=mask,Hz=hz,Chunks=500};
   RequireRecordReconnectIdentity(snapshot,afterStatus,7,11,true);
   beforeStatus.LastId++;Need(snapshot[5]==123,""snapshot retained mutable status reference"");
   Need(RecordReconnectSnapshotText(snapshot,7).Contains(""End=50000"") && RecordReconnectSnapshotText(snapshot,7).Contains(""LastId=123""),""immutable status omitted from log"");
   Refuse(()=>RequireRecordReconnectIdentity(snapshot,afterStatus,7,7,true));
   Refuse(()=>RequireRecordReconnectIdentity(snapshot,afterStatus,7,11,false));
   Refuse(()=>RequireRecordReconnectIdentity(null,afterStatus,7,11,true));
   Refuse(()=>RequireRecordReconnectIdentity(new uint[6],afterStatus,7,11,true));
   foreach(string name in new[]{""Mask"",""Hz"",""Start"",""End"",""Capacity"",""LastId""}){
    var changed=new Status{Mask=mask,Hz=hz,Chunks=500};FieldInfo field=typeof(Status).GetField(name);field.SetValue(changed,(uint)field.GetValue(changed)+1);
    Refuse(()=>RequireRecordReconnectIdentity(snapshot,changed,7,11,true));
   }
   foreach(string name in new[]{""State"",""Error"",""Fault""}){
    var changed=new Status{Mask=mask,Hz=hz};FieldInfo field=typeof(Status).GetField(name);field.SetValue(changed,(uint)field.GetValue(changed)+1);
    Refuse(()=>RequireRecordReconnectIdentity(snapshot,changed,7,11,true));
   }
   Refuse(()=>RequireRecordReconnectIdentity(snapshot,new Status{Mask=mask,Hz=hz,Chunks=399},7,11,true));
   Refuse(()=>RequireRecordReconnectIdentity(snapshot,new Status{Mask=mask,Hz=hz==20000?30000U:20000U,Chunks=500},7,11,true));
   Refuse(()=>RequireRecordReconnectIdentity(snapshot,new Status{Mask=mask==3?7U:3U,Hz=20000,Chunks=500},7,11,true));
   RequireRecordReconnectIdentity(snapshot,new Status{Mask=mask,Hz=hz,Chunks=400},7,11,true);
  }
  RequireRecordReconnectComplete(false,false,0);RequireRecordReconnectComplete(true,true,1);
  Refuse(()=>RequireRecordReconnectComplete(true,false,1));Refuse(()=>RequireRecordReconnectComplete(true,true,0));
  Need(!RecordStopDue(false,60,TimeSpan.FromSeconds(9)) && RecordStopDue(false,60,TimeSpan.FromSeconds(10)),""default ten-second Stop changed"");
  Need(RecordStopDue(false,10,TimeSpan.FromSeconds(5)) && RecordStopDue(false,1,TimeSpan.FromSeconds(1)),""short default Stop changed"");
  Need(!RecordStopDue(true,60,TimeSpan.FromSeconds(10)) && !RecordStopDue(true,60,TimeSpan.FromSeconds(600)),""full duration emits host Stop"");
  Need(!RecordStopDue(true,120,TimeSpan.FromSeconds(10)) && !RecordStopDue(true,120,TimeSpan.FromSeconds(420)),""120 full duration emits host Stop"");
  Need(!RecordStopDue(true,900,TimeSpan.FromSeconds(10)) && !RecordStopDue(true,900,TimeSpan.FromSeconds(2400)),""900 full duration emits host Stop"");
  Need(RecordHeartbeatDue(3,0,-2000) && !RecordHeartbeatDue(3,1999,0) && RecordHeartbeatDue(3,2000,0),""heartbeat cadence wrong"");
  foreach(uint state in new uint[]{0,1,2,4,5,6,7,255})Need(!RecordHeartbeatDue(state,60000,0),""heartbeat scheduled outside Recording"");
  Need(RecordHeartbeatMinimum(true,60)==15 && RecordHeartbeatMinimum(false,60)==2 && RecordHeartbeatMinimum(true,1)==1,""coverage floor wrong"");
  Need(RecordHeartbeatMinimum(true,120)==30,""120 heartbeat coverage weakened"");
  Need(RecordHeartbeatMinimum(true,900)==225,""900 heartbeat coverage weakened"");
  foreach(uint blocks in new uint[]{31248,46872}){
   RequireRecordComplete(true,blocks,blocks,60,15,0);
   Refuse(()=>RequireRecordComplete(true,blocks-4,blocks,60,15,0));
   Refuse(()=>RequireRecordComplete(true,blocks+4,blocks,60,15,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,60,14,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,60,30,1));
  }
  foreach(uint blocks in new uint[]{62500,93748}){
   RequireRecordComplete(true,blocks,blocks,120,30,0);
   Refuse(()=>RequireRecordComplete(true,blocks-4,blocks,120,30,0));
   Refuse(()=>RequireRecordComplete(true,blocks+4,blocks,120,30,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,120,29,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,120,30,1));
  }
  foreach(uint blocks in new uint[]{468748,703124}){
   RequireRecordComplete(true,blocks,blocks,900,225,0);
   Refuse(()=>RequireRecordComplete(true,blocks-4,blocks,900,225,0));
   Refuse(()=>RequireRecordComplete(true,blocks+4,blocks,900,225,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,900,224,0));
   Refuse(()=>RequireRecordComplete(true,blocks,blocks,900,225,1));
  }
  RequireRecordComplete(false,100,31248,60,2,0); // Default intentionally early-Stops.
  Refuse(()=>RequireRecordComplete(true,0,0,60,30,0));
  Refuse(()=>RequireRecordComplete(false,100,31248,60,0,0));
  Refuse(()=>RequireRecordComplete(false,100,31248,60,2,1));
  RequireRecordingClockRequest(Request(0),Magic);
  Refuse(()=>RequireRecordingClockRequest(Request(1),Magic));
  Refuse(()=>RequireRecordingClockRequest(Request(0),Magic+1));
  Refuse(()=>RequireRecordingClockRequest(null,Magic));
  Refuse(()=>RequireRecordingClockRequest(new byte[63],Magic));
  var c=new Connection();messages.Clear();byte[] request=Request(0);
  var reply=await ExchangeTimed(c,request,CancellationToken.None,""timed"");
  Need(c.Calls==1 && Object.ReferenceEquals(c.Last,request) && reply.SequenceEqual(new byte[]{1,2,3}),""timed exchange changed bytes/replayed"");
  long us=Int64.Parse(messages[0].Split('=')[1].Split(';')[0],CultureInfo.InvariantCulture);
  Need(us>=50000 && messages[0].Contains(""outcome=reply"") && messages[1].StartsWith(""timed.request="") && messages[2].StartsWith(""timed.reply=""),""timing omitted synchronous dispatch or wire logs"");
  c=new Connection{Fail=true};messages.Clear();bool failed=false;
  try{await ExchangeTimed(c,request,CancellationToken.None,""failed"");}catch(IOException){failed=true;}
  Need(failed && c.Calls==1 && messages.Count==2 && messages[0].Contains(""outcome=failed"") && messages[1].StartsWith(""failed.request=""),""failed timing replayed or invented reply"");
  c=new Connection();messages.Clear();using(var canceled=new CancellationTokenSource()){
   canceled.Cancel();bool aborted=false;try{await ExchangeTimed(c,request,canceled.Token,""canceled"");}catch(OperationCanceledException){aborted=true;}
   Need(aborted && c.Calls==1 && messages[0].Contains(""outcome=failed""),""cancellation replayed/suppressed"");
  }
  c=new Connection();var clock=new Clock();uint id=100;
  Func<byte[],Task<byte[]>> exchange=r=>{RequireRecordingClockRequest(r,Magic);return ExchangeTimed(c,r,CancellationToken.None,""heartbeat"");};
  await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);
  Need(clock.ObserveCalls==1 && c.Calls==1 && clock.Generation==9,""heartbeat did not use one Observe/read"");
  c.Connected=false;failed=false;try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed && c.Calls==1 && clock.ObserveCalls==1,""disconnected heartbeat dispatched"");
  c.Connected=true;c.Epoch=8;failed=false;try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed && c.Calls==1,""stale epoch dispatched"");
  c.Epoch=7;c.ChangeEpoch=true;failed=false;try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed && c.Calls==2,""epoch change during Observe accepted/replayed"");
  c.Epoch=7;c.ChangeEpoch=false;clock.Operation=1;int before=c.Calls;failed=false;
  try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed && c.Calls==before,""recording COMMIT reached transport"");
  clock.Operation=0;clock.Generation=10;failed=false;try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed,""changed generation accepted"");
  clock.Generation=9;clock.Synchronized=false;failed=false;try{await ObserveRecordingClock(c,clock,exchange,()=>++id,7,9);}catch(IOException){failed=true;}
  Need(failed,""poor heartbeat quality accepted"");
 }
 static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine(""PASS ""+checks+"" actual runner record-option/finite-completion/heartbeat/dispatch-timing checks; endpoint spies only, no radio/media integrity claim"");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}";
    }
}
