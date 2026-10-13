using System;
using System.Diagnostics;
using System.IO;

/* Actual-source warm-open and link-aware ConnectAsync recovery checks. GATT endpoints
 * and phase expiry are modeled; no radio or WinRT device is constructed. */
internal static class NpBleWarmLinkTests
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
    static string Method(string source,string marker){
        int start=source.IndexOf(marker,StringComparison.Ordinal);Need(start>=0,"Missing actual method: "+marker);
        int open=source.IndexOf('{',start),depth=0;Need(open>start,"Missing method body: "+marker);
        for(int at=open;at<source.Length;at++){if(source[at]=='{')depth++;if(source[at]=='}' && --depth==0)return source.Substring(start,at-start+1);}
        throw new Exception("Unterminated actual method: "+marker);
    }
    static int Main(string[] args)
    {
        string folder=null;
        try {
            if(args.Length!=2 && (args.Length!=3 || args[2]!="--subscription-only"))
                throw new ArgumentException("<NpBleConnection.cs> <csc.exe> [--subscription-only]");
            string source=File.ReadAllText(args[0]);
            int start=source.IndexOf("static async Task<BluetoothLEDevice> TryWarmAddress",StringComparison.Ordinal);
            int end=source.IndexOf("async Task<BluetoothLEDevice> ResolveAddress",StringComparison.Ordinal);
            Need(start>=0 && end>start,"Warm-address helper missing.");
            string warm=source.Substring(start,end-start);
            Need(warm.Contains("CreateLinkedTokenSource(cancel)") && warm.Contains("CancelAfter(250)"),"Warm open is not bounded to 250 ms.");
            Need(warm.Contains("if(cancel.IsCancellationRequested)throw;"),"Outer cancellation is swallowed.");
            Need(warm.Contains("TryOpenAddress(address,type,warm.Token)"),"Warm open does not use the supplied observed address type.");
            Need(warm.Contains("if(device.BluetoothAddress!=address)") && warm.Contains("if(type.HasValue)"),"Exact retained-type identity/pending-link gate missing.");
            Need(warm.Contains("device.Dispose()") && warm.Contains("mismatched; disposed") && warm.Contains("disconnected/mismatched"),"Rejected warm device is not disposed.");
            Need(!warm.Contains("Write") && !warm.Contains("service") && !warm.Contains("rx") && !warm.Contains("tx"),"Warm path caches GATT state or writes a command.");
            int call=source.IndexOf("TryWarmAddress(address,warmType,cancel,stage)",end,StringComparison.Ordinal);
            int watcher=source.IndexOf("var watcher=new BluetoothLEAdvertisementWatcher",end,StringComparison.Ordinal);
            Need(call>=end && watcher>call,"Warm fallback does not precede the existing watcher.");
            string resolve=source.Substring(end,watcher-end);
            Need(resolve.Contains("lastObservedAddress==address && lastObservedType!=BluetoothAddressType.Unspecified"),"Observed type is not isolated to the same literal address.");
            Need(resolve.Contains("? (BluetoothAddressType?)lastObservedType : null"),"Different address can inherit an observed address type.");
            Need(resolve.Contains("if(allowWarm && warmType.HasValue)") && resolve.Contains("lastResolvedWarm=true"),"Unknown cold peer can use the retained-type route.");
            Need(source.Contains("if(!warm || freshFallback)") && source.Contains("ResolveAddress(address,timeout.Token,value=>{stage=value;TraceConnect(elapsed,value);},false)") && source.Contains("catch (OperationCanceledException error)"),"Warm failure lacks one bounded cancellable fresh-observation fallback.");
            Need(!warm.Contains("BluetoothAddressType.Random") && !warm.Contains("BluetoothAddressType.Public"),"Warm path infers an address type.");
            Need(source.Contains("internal Task ConnectAsync(ulong address) { return ConnectAsync(address,null); }") && source.Contains("ConnectAsync(ulong address,BluetoothAddressType? observedType)") && source.Contains("lastObservedAddress=address;lastObservedType=observedType.Value;"),"Connection does not accept an observed exact-peer type seed.");
            string connect=Method(source,"internal async Task ConnectAsync(ulong address,BluetoothAddressType? observedType)");
            string disconnect=Method(source,"internal void Disconnect()");
            string connectionChanged=Method(source,"void ConnectionChanged(");
            Need(connect.Contains("!timeout.IsCancellationRequested || connectCanceled") && connect.Contains("timeout.Dispose();timeout=new CancellationTokenSource(35000)"),"Only the owned warm phase deadline may replace its canceled budget.");
            Need(disconnect.Contains("connectCanceled=true") && !connectionChanged.Contains("connectCanceled"),"Explicit close and link-status epochs are conflated.");
            Need(disconnect.Contains("if(connectTimeout!=null)connectTimeout.Cancel()") && connect.Contains("connectTimeout=timeout") && connect.Contains("if(connectTimeout==timeout)connectTimeout=null"),"Pending connection wait is not canceled/retired under the existing lock.");
            Need(!connect.Contains("ExchangeAsync") && !connect.Contains("WriteFrame") && !connect.Contains("RescueReset"),"Discovery recovery routes a recorder command.");
            Need(connect.Contains("cacheAttempt<2") && connect.Contains("cacheAttempt==0?BluetoothCacheMode.Cached:BluetoothCacheMode.Uncached") && connect.Contains("one Uncached refresh"),"Cache-first lookup lacks a single bounded refresh.");
            Need(connect.Contains("firstCacheAttempt=warm && device.ConnectionStatus==BluetoothConnectionStatus.Connected?0:1") && connect.Contains("cacheAttempt=firstCacheAttempt"),"Fresh or pending links can start with the slow cached lookup.");
            Need(connect.Contains("selectedService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)") && connect.Contains("services=null; // Ownership moves to Disconnect only after fresh admission."),"Cached service bypasses fresh characteristic admission/ownership.");
            Need(connect.IndexOf("rx.ValueChanged += ValueChanged",StringComparison.Ordinal)>connect.IndexOf("if(admitted)break",StringComparison.Ordinal),"Notification subscription is attempted before fresh admission.");
            int subscriptionBudget=connect.IndexOf("timeout.CancelAfter(5000)",StringComparison.Ordinal);
            int subscriptionEvent=connect.IndexOf("rx.ValueChanged += ValueChanged",StringComparison.Ordinal);
            Need(subscriptionBudget>connect.IndexOf("if(admitted)break",StringComparison.Ordinal) && subscriptionBudget<subscriptionEvent,"Subscription budget is not renewed after fresh admission and before handler ownership.");
            string subscriptionGate=connect.Substring(connect.LastIndexOf("lock(receiveLock)",subscriptionBudget,StringComparison.Ordinal),subscriptionEvent-connect.LastIndexOf("lock(receiveLock)",subscriptionBudget,StringComparison.Ordinal));
            Need(subscriptionGate.Contains("connectCanceled || connectTimeout!=timeout") && subscriptionGate.IndexOf("timeout.Token.ThrowIfCancellationRequested()",StringComparison.Ordinal)<subscriptionGate.IndexOf("timeout.CancelAfter(5000)",StringComparison.Ordinal),"Closed/expired connection can renew its budget.");
            Need(connect.Contains("await Task.Delay(250, timeout.Token)") && !subscriptionGate.Contains("new CancellationTokenSource"),"Subscription settling or existing CTS ownership changed.");
            string form=File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"NpBleForm.cs"));
            Need(form.Contains("internal ulong Address; internal BluetoothAddressType Type;") && form.Contains("p.Type=e.BluetoothAddressType;") && form.Contains("witness!=null && witness.Address==value && witness.Type!=BluetoothAddressType.Unspecified") && form.Contains("connection.ConnectAsync(value,observedType)"),"Form does not pass only its matching observed peer type to the first connection.");
            Need(form.Contains("disconnect.Enabled=connecting || (!busy && !hostBusy && linked)") && form.Contains("connecting=true;UpdateControls()") && form.Contains("finally{connecting=false;UpdateControls();}"),"Cancel connection is unavailable during the connecting lease or remains enabled afterward.");
            int cancelStart=form.IndexOf("if(connecting){connection.Disconnect();",StringComparison.Ordinal);
            int cancelEnd=form.IndexOf("return;}",cancelStart,StringComparison.Ordinal);
            Need(cancelStart>=0 && cancelEnd>cancelStart,"Missing local cancel-connect handler.");
            string cancelPath=form.Substring(cancelStart,cancelEnd-cancelStart);
            Need(!cancelPath.Contains("Run(") && !cancelPath.Contains("Send(") && !cancelPath.Contains("RescueReset") && !cancelPath.Contains("StartRecording"),"Connect cancellation waits for a GUI lease or mutates the recorder.");
            folder=Path.Combine(Path.GetTempPath(),"NpBleWarmLink_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string program=Path.Combine(folder,"WarmHarness.cs"),exe=Path.Combine(folder,"WarmHarness.exe");File.WriteAllText(program,Harness(warm,connect,disconnect,connectionChanged));
            var compile=new ProcessStartInfo(args[1],"/nologo /target:exe /r:System.Core.dll /out:"+Quote(exe)+" "+Quote(program)){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(compile)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted helper failed to compile: "+output);}
            var run=new ProcessStartInfo(exe,args.Length==3?"--subscription-only":""){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            using(var p=Process.Start(run)){p.WaitForExit();string output=p.StandardOutput.ReadToEnd()+p.StandardError.ReadToEnd();if(p.ExitCode!=0)throw new Exception("Extracted helper behavior failed: "+output);Console.Write(output);}
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        finally { if(folder!=null)try{Directory.Delete(folder,true);}catch(IOException){} }
    }

    static string Harness(string warm,string connect,string disconnect,string connectionChanged)
    {
        return @"using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
enum BluetoothAddressType { Unspecified, Random, Public }
enum BluetoothConnectionStatus { Disconnected, Connected }
enum BluetoothCacheMode { Cached, Uncached }
enum GattCommunicationStatus { Success, Unreachable, ProtocolError }
[Flags] enum GattCharacteristicProperties { Notify=1,Indicate=2,Write=4,WriteWithoutResponse=8 }
enum GattClientCharacteristicConfigurationDescriptorValue { None,Notify,Indicate }
sealed class AsyncOperation<T> { readonly Func<CancellationToken,Task<T>> run;public AsyncOperation(Func<CancellationToken,Task<T>> run){this.run=run;}public Task<T> AsTask(CancellationToken token){return run(token);} }
sealed class CancellationTokenSource : IDisposable {
 readonly System.Threading.CancellationTokenSource source;
 internal static readonly List<CancellationTokenSource> Created=new List<CancellationTokenSource>();internal static CancellationTokenSource Last;
 internal readonly int InitialBudget;internal int Budget,Remaining,PhaseStarts,SubscriptionStarts;internal bool Disposed;
 CancellationTokenSource(System.Threading.CancellationTokenSource source,int budget){this.source=source;InitialBudget=budget;Last=this;Created.Add(this);if(budget>0)CancelAfter(budget);}
 public CancellationTokenSource():this(new System.Threading.CancellationTokenSource(),0){}
 public CancellationTokenSource(int budget):this(new System.Threading.CancellationTokenSource(),budget){}
 public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancel){return new CancellationTokenSource(System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancel),0);}
 public CancellationToken Token{get{return source.Token;}}public bool IsCancellationRequested{get{return source.IsCancellationRequested;}}
 public void CancelAfter(int ms){Budget=Remaining=ms;if(ms==20000)PhaseStarts++;if(ms==5000)SubscriptionStarts++;source.CancelAfter(ms);}
 public void Advance(int ms){Remaining-=ms;if(Remaining<=0)source.Cancel();}
 public void Cancel(){source.Cancel();}public void Dispose(){Disposed=true;source.Dispose();}
}
sealed class BluetoothLEDevice : IDisposable {
 public ulong BluetoothAddress;public BluetoothConnectionStatus ConnectionStatus;public bool Disposed,Fresh,Retained,InitiallyConnected;public int Disposals;
 public event Action<BluetoothLEDevice,object> ConnectionStatusChanged;
 public Func<BluetoothCacheMode,CancellationToken,Task<GattDeviceServicesResult>> Query;
 public readonly List<BluetoothCacheMode> Modes=new List<BluetoothCacheMode>();
 public AsyncOperation<GattDeviceServicesResult> GetGattServicesForUuidAsync(Guid id,BluetoothCacheMode mode){Harness.Check(id==Harness.ServiceId,""wrong discovery endpoint"");Modes.Add(mode);return new AsyncOperation<GattDeviceServicesResult>(t=>Query(mode,t));}
 public void Dispose(){Disposed=true;Disposals++;ConnectionStatus=BluetoothConnectionStatus.Disconnected;}
}
sealed class GattDeviceServicesResult { public GattCommunicationStatus Status;public readonly List<GattDeviceService> Services=new List<GattDeviceService>(); }
sealed class GattCharacteristicsResult { public GattCommunicationStatus Status;public readonly List<GattCharacteristic> Characteristics=new List<GattCharacteristic>(); }
sealed class GattDeviceService : IDisposable {
 public Guid Uuid;public int Disposals;public GattCharacteristicsResult Characteristics;public Func<CancellationToken,Task<GattCharacteristicsResult>> Query;
 public AsyncOperation<GattCharacteristicsResult> GetCharacteristicsAsync(BluetoothCacheMode mode){Harness.Check(mode==BluetoothCacheMode.Uncached,""characteristics reused from cache"");return new AsyncOperation<GattCharacteristicsResult>(t=>{t.ThrowIfCancellationRequested();return Query==null?Task.FromResult(Characteristics):Query(t);});}
 public void Dispose(){Disposals++;}
}
sealed class GattCharacteristic {
 public Guid Uuid;GattCharacteristicProperties properties;public Action PropertiesRead;
 public GattCharacteristicProperties CharacteristicProperties{get{if(PropertiesRead!=null)PropertiesRead();return properties;}set{properties=value;}}
 public event Action<GattCharacteristic,object> ValueChanged;
 public Func<GattClientCharacteristicConfigurationDescriptorValue,CancellationToken,Task<GattCommunicationStatus>> Subscribe;
 public AsyncOperation<GattCommunicationStatus> WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue value){return new AsyncOperation<GattCommunicationStatus>(t=>Subscribe(value,t));}
}
sealed class Wire { public void Clear(){} }
static class Harness {
 internal static readonly Guid ServiceId=new Guid(""0000fff0-0000-1000-8000-00805f9b34fb"");
 static Func<ulong,BluetoothAddressType?,CancellationToken,Task<BluetoothLEDevice>> open;
 static Task<BluetoothLEDevice> TryOpenAddress(ulong address,BluetoothAddressType? type,CancellationToken cancel){return open(address,type,cancel);}
"+warm+@"
 static readonly List<string> stages=new List<string>();
 static Task<BluetoothLEDevice> Run(ulong address,BluetoothAddressType? type,CancellationToken cancel){stages.Clear();return TryWarmAddress(address,type,cancel,value=>stages.Add(value));}
 static int checks;internal static void Check(bool value,string text){Need(value,text);}
 static void Need(bool value,string text){checks++;if(!value)throw new Exception(text);}
 static async Task Exact(){BluetoothAddressType? seen=null;var d=new BluetoothLEDevice{BluetoothAddress=7,ConnectionStatus=BluetoothConnectionStatus.Connected};open=(a,t,c)=>{seen=t;return Task.FromResult(d);};var r=await Run(7,BluetoothAddressType.Random,CancellationToken.None);Need(Object.ReferenceEquals(r,d)&&!d.Disposed&&seen==BluetoothAddressType.Random&&stages.Contains(""connected selected-address cache reuse""),""exact connected typed reuse"");}
 static async Task RetainedPending(){var d=new BluetoothLEDevice{BluetoothAddress=7,ConnectionStatus=BluetoothConnectionStatus.Disconnected};open=(a,t,c)=>Task.FromResult(d);Need(Object.ReferenceEquals(await Run(7,BluetoothAddressType.Random,CancellationToken.None),d)&&!d.Disposed&&stages.Contains(""selected-address retained-type reuse; connection pending""),""typed pending link was not retained for FFF0 validation"");}
 static async Task Rejected(){var mismatch=new BluetoothLEDevice{BluetoothAddress=8,ConnectionStatus=BluetoothConnectionStatus.Connected};open=(a,t,c)=>Task.FromResult(mismatch);Need(await Run(7,BluetoothAddressType.Random,CancellationToken.None)==null&&mismatch.Disposed,""mismatch dispose"");var disconnected=new BluetoothLEDevice{BluetoothAddress=7,ConnectionStatus=BluetoothConnectionStatus.Disconnected};open=(a,t,c)=>Task.FromResult(disconnected);Need(await Run(7,null,CancellationToken.None)==null&&disconnected.Disposed,""untyped disconnected dispose"");}
 static async Task Null(){open=(a,t,c)=>Task.FromResult<BluetoothLEDevice>(null);Need(await Run(7,BluetoothAddressType.Random,CancellationToken.None)==null&&stages.Contains(""selected-address cache reuse null""),""null fallback"");}
 static async Task DifferentAddress(){BluetoothAddressType? seen=BluetoothAddressType.Random;var d=new BluetoothLEDevice{BluetoothAddress=8,ConnectionStatus=BluetoothConnectionStatus.Connected};open=(a,t,c)=>{seen=t;return Task.FromResult(d);};Need(Object.ReferenceEquals(await Run(8,null,CancellationToken.None),d)&&!seen.HasValue,""different address must use no observed type hint"");}
 static async Task LocalExpiry(){open=async (a,t,c)=>{await Task.Delay(1000,c);return null;};var w=Stopwatch.StartNew();Need(await Run(7,BluetoothAddressType.Random,CancellationToken.None)==null&&stages.Contains(""selected-address cache reuse expired"")&&w.ElapsedMilliseconds<800,""local expiry fallback"");}
 static async Task OuterCancel(){open=async (a,t,c)=>{await Task.Delay(1000,c);return null;};using(var c=new CancellationTokenSource()){c.CancelAfter(10);bool threw=false;try{await Run(7,BluetoothAddressType.Random,c.Token);}catch(OperationCanceledException){threw=true;}Need(threw,""outer cancellation propagation"");}}
"+RecoveryHarness(connect,disconnect,connectionChanged)+@"
  static int Main(string[] args){try{
   if(args.Length==1 && args[0]==""--subscription-only"")SubscriptionCases().GetAwaiter().GetResult();
   else{Exact().GetAwaiter().GetResult();RetainedPending().GetAwaiter().GetResult();Rejected().GetAwaiter().GetResult();Null().GetAwaiter().GetResult();DifferentAddress().GetAwaiter().GetResult();LocalExpiry().GetAwaiter().GetResult();OuterCancel().GetAwaiter().GetResult();RecoveryCases().GetAwaiter().GetResult();SubscriptionCases().GetAwaiter().GetResult();}
   Console.WriteLine((args.Length==1?""PASS extracted actual ConnectAsync subscription/deadline/cleanup sequence; checks="":""PASS extracted warm identity and actual ConnectAsync deadline/fallback/cleanup sequence; checks="")+checks+""; modeled GATT only, no radio used"");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}";
    }

    static string RecoveryHarness(string connect,string disconnect,string connectionChanged)
    {
        return @"
 sealed class Recovery {
  readonly SemaphoreSlim transaction=new SemaphoreSlim(1,1);readonly object receiveLock=new object();readonly Wire wire=new Wire();
  BluetoothLEDevice device;GattDeviceService service;GattCharacteristic rx,tx;TaskCompletionSource<byte[]> receipt;int epoch;volatile bool connectCanceled;Action<string> Disconnected;CancellationTokenSource connectTimeout;
  ulong lastObservedAddress;BluetoothAddressType lastObservedType;bool lastResolvedWarm;
  readonly string mode;readonly bool startWarm;internal int FreshOpens,InitialQueries,FreshQueries,Subscriptions;
  internal Stopwatch SubscriptionFinished;
  internal readonly List<BluetoothLEDevice> Devices=new List<BluetoothLEDevice>();internal readonly List<GattDeviceService> Services=new List<GattDeviceService>();internal readonly List<string> Trace=new List<string>();
  internal Recovery(string mode,bool warm){this.mode=mode;startWarm=warm;}
  internal int Epoch{get{return Volatile.Read(ref epoch);}}
  internal bool Connected{get{return device!=null && tx!=null && device.ConnectionStatus==BluetoothConnectionStatus.Connected;}}
  void ResetConnectDiagnostic(ulong address){Need(address==7,""unexpected peer"");Trace.Clear();}
  void TraceConnect(Stopwatch elapsed,string stage){Trace.Add(stage);}
  void ValueChanged(GattCharacteristic sender,object args){}
  void CloseClockJournal(){} // Journal retirement is outside this discovery-only model.
  Task<BluetoothLEDevice> ResolveAddress(ulong address,CancellationToken cancel,Action<string> stage,bool allowWarm=true){
   Need(address==7 && cancel==CancellationTokenSource.Last.Token && CancellationTokenSource.Last.Budget==35000,""fresh address budget/identity"");
   lastResolvedWarm=allowWarm && startWarm;if(!allowWarm)FreshOpens++;
   if(!allowWarm && mode==""fresh-address-timeout""){CancellationTokenSource.Last.Cancel();return Canceled<BluetoothLEDevice>();}
   bool connected=mode!=""pending-link"";
   var selected=new BluetoothLEDevice{BluetoothAddress=address,Fresh=!allowWarm,Retained=lastResolvedWarm,InitiallyConnected=connected,ConnectionStatus=connected?BluetoothConnectionStatus.Connected:BluetoothConnectionStatus.Disconnected};selected.Query=(cache,t)=>Query(selected,cache,t);Devices.Add(selected);
   if(mode==""resolve-close"" || (!allowWarm && mode==""fresh-resolve-close""))Disconnect();return Task.FromResult(selected);
  }
  Task<GattDeviceServicesResult> Query(BluetoothLEDevice selected,BluetoothCacheMode cache,CancellationToken token){
   var phase=CancellationTokenSource.Last;Need(token==phase.Token && phase.Budget==20000,""GATT phase budget"");
   if(selected.Fresh)FreshQueries++;else InitialQueries++;
   if(mode==""pending-close"")return PendingQuery(token);
   if(mode==""explicit-cancel"")return Canceled<GattDeviceServicesResult>();
   if(mode==""disposed-deadline""){Disconnect();phase.Cancel();return Canceled<GattDeviceServicesResult>();}
   if(!selected.Fresh && mode==""link-deadline""){selected.ConnectionStatus=BluetoothConnectionStatus.Disconnected;ConnectionChanged(selected,null);phase.Cancel();return Canceled<GattDeviceServicesResult>();}
   if(mode==""cold-timeout"" || (!selected.Fresh && (mode==""warm-timeout"" || mode==""fresh-timeout"" || mode==""fresh-address-timeout"" || mode==""fresh-resolve-close"")) || (selected.Fresh && mode==""fresh-timeout"")){
    phase.Cancel();return Canceled<GattDeviceServicesResult>();
   }
   if(selected.Fresh && mode==""fresh-failure"")throw new IOException(""synthetic fresh discovery failure"");
   if(cache==BluetoothCacheMode.Cached && mode==""cache-exception"")throw new IOException(""synthetic cached discovery error"");
   if(mode==""refresh-close"" && cache==BluetoothCacheMode.Uncached){Disconnect();return Canceled<GattDeviceServicesResult>();}
   if(mode==""refresh-timeout"" && cache==BluetoothCacheMode.Uncached){phase.Cancel();return Canceled<GattDeviceServicesResult>();}
   if(mode==""cold-stale-cache"" && cache==BluetoothCacheMode.Cached)phase.Advance(7700);
   var result=new GattDeviceServicesResult{Status=!selected.Fresh && (mode==""unreachable"" || mode==""fresh-failure"")?GattCommunicationStatus.Unreachable:
    cache==BluetoothCacheMode.Cached && mode==""cold-stale-cache""?GattCommunicationStatus.Unreachable:cache==BluetoothCacheMode.Cached && mode==""cache-error""?GattCommunicationStatus.ProtocolError:GattCommunicationStatus.Success};
   if(cache==BluetoothCacheMode.Cached && mode==""cache-miss"")return Task.FromResult(result);
   var candidate=new GattDeviceService{Uuid=ServiceId,Characteristics=new GattCharacteristicsResult{Status=GattCommunicationStatus.Success}};
   candidate.Characteristics.Characteristics.Add(new GattCharacteristic{Uuid=new Guid(""0000fff1-0000-1000-8000-00805f9b34fb""),CharacteristicProperties=GattCharacteristicProperties.Notify,Subscribe=(value,t)=>{
    Need(value==GattClientCharacteristicConfigurationDescriptorValue.Notify && t==phase.Token,""subscription changed"");
    Need(phase.Budget==5000 && phase.SubscriptionStarts==1 && !t.IsCancellationRequested,""subscription has no fresh owned five-second budget"");Subscriptions++;
    if(mode==""subscription-pending-close"")return PendingSubscription(t);
    if(mode==""subscription-hung""){phase.Advance(4999);Need(!t.IsCancellationRequested,""subscription expired early"");phase.Advance(1);return Canceled<GattCommunicationStatus>();}
    if(mode==""late-discovery""){phase.Advance(750);Need(!t.IsCancellationRequested,""late discovery consumed subscription budget"");}
    SubscriptionFinished=Stopwatch.StartNew();
    if(mode==""settle-timeout"")phase.Advance(5000);
    return Task.FromResult(mode==""subscription-failure""?GattCommunicationStatus.Unreachable:GattCommunicationStatus.Success);
   }});
   candidate.Characteristics.Characteristics.Add(new GattCharacteristic{Uuid=new Guid(""0000fff2-0000-1000-8000-00805f9b34fb""),CharacteristicProperties=GattCharacteristicProperties.Write});
   bool stale=cache==BluetoothCacheMode.Cached;
   if(stale && mode==""cache-wrong-service"")candidate.Uuid=Guid.Empty;
   if(stale && mode==""stale-status"")candidate.Characteristics.Status=GattCommunicationStatus.Unreachable;
   if((stale && mode==""stale-missing"") || mode==""invalid-characteristics"")candidate.Characteristics.Characteristics.RemoveAt(1);
   if(stale && mode==""stale-notify"")candidate.Characteristics.Characteristics[0].CharacteristicProperties=0;
   if(stale && mode==""stale-write"")candidate.Characteristics.Characteristics[1].CharacteristicProperties=0;
   if(stale && mode==""stale-duplicate"")candidate.Characteristics.Characteristics.Add(candidate.Characteristics.Characteristics[1]);
   if(stale && mode==""stale-exception"")candidate.Query=t=>{throw new IOException(""synthetic stale characteristic error"");};
   if(mode==""characteristic-close"")candidate.Query=t=>{Disconnect();return Canceled<GattCharacteristicsResult>();};
   if(mode==""characteristic-timeout"")candidate.Query=t=>{phase.Cancel();return Canceled<GattCharacteristicsResult>();};
   if(mode==""late-discovery"")candidate.Query=t=>{phase.Advance(19990);Need(!t.IsCancellationRequested && phase.Remaining==10,""late discovery model boundary"");return Task.FromResult(candidate.Characteristics);};
   if(mode==""subscription-close-before-budget"" || mode==""subscription-expired-before-budget""){
    int reads=0;candidate.Characteristics.Characteristics[0].PropertiesRead=()=>{
     if(++reads==2){if(mode==""subscription-close-before-budget"")Disconnect();else phase.Cancel();}
    };
   }
   if((mode==""refresh-close"" || mode==""refresh-timeout"") && stale)candidate.Characteristics.Status=GattCommunicationStatus.Unreachable;
   Services.Add(candidate);result.Services.Add(candidate);if(result.Status==GattCommunicationStatus.Success)selected.ConnectionStatus=BluetoothConnectionStatus.Connected;
   if(stale && mode==""cache-duplicate-service""){var extra=new GattDeviceService{Uuid=ServiceId};Services.Add(extra);result.Services.Add(extra);}
   if(mode==""service-close"")Disconnect();return Task.FromResult(result);
  }
  static readonly Guid ServiceId=Harness.ServiceId,RxId=new Guid(""0000fff1-0000-1000-8000-00805f9b34fb""),TxId=new Guid(""0000fff2-0000-1000-8000-00805f9b34fb"");
  static Task<T> Canceled<T>(){var done=new TaskCompletionSource<T>();done.SetCanceled();return done.Task;}
  static async Task<GattDeviceServicesResult> PendingQuery(CancellationToken token){await Task.Delay(10000,token);throw new Exception(""pending GATT wait was not canceled"");}
  static async Task<GattCommunicationStatus> PendingSubscription(CancellationToken token){await Task.Delay(10000,token);throw new Exception(""pending subscription was not canceled"");}
"+connect+"\n"+disconnect+"\n"+connectionChanged+@"
  internal bool LeaseFree(){bool free=transaction.Wait(0);if(free)transaction.Release();return free;}
  internal bool WaitRetired{get{return connectTimeout==null;}}
 }
 static async Task RecoveryCase(string mode,bool warm,bool success,int fresh,int initialQueries,int freshQueries){
  CancellationTokenSource.Created.Clear();var r=new Recovery(mode,warm);bool passed=false;Exception failure=null;
  try{await r.ConnectAsync(7,BluetoothAddressType.Random);passed=true;}catch(Exception e){failure=e;}
  Need(passed==success && (passed || failure is IOException),mode+"" terminal outcome"");
  Need(r.FreshOpens==fresh && r.InitialQueries==initialQueries && r.FreshQueries==freshQueries,mode+"" fallback/query bound"");
  Need(CancellationTokenSource.Created.Count==1+fresh && CancellationTokenSource.Created.All(c=>c.InitialBudget==35000 && c.Disposed),mode+"" fresh bounded CTS cleanup"");
  Need(r.LeaseFree(),mode+"" transaction lease leaked"");
  Need(r.WaitRetired,mode+"" pending connection CTS leaked"");
  foreach(var d in r.Devices)Need(d.Modes.Count==0 || (d.Modes[0]==(d.Retained && d.InitiallyConnected?BluetoothCacheMode.Cached:BluetoothCacheMode.Uncached) && d.Modes.Skip(1).All(c=>c==BluetoothCacheMode.Uncached) && d.Modes.Count<=(d.Retained && d.InitiallyConnected?4:3)),mode+"" cache policy/refresh/retry bound"");
  Need(CancellationTokenSource.Created.All(c=>c.PhaseStarts<=1) && CancellationTokenSource.Created.Sum(c=>c.PhaseStarts)==r.Devices.Count(d=>d.Modes.Count>0),mode+"" cache refresh restarted the finite phase deadline"");
  if(!success)Need(!r.Connected && r.Devices.All(d=>d.Disposals==1) && r.Services.All(s=>s.Disposals==1),mode+"" failure resource cleanup"");
  Need(r.Subscriptions==(success || mode==""subscription-failure""?1:0),mode+"" notification write replay"");
  if(mode==""warm-timeout"")Need(r.Trace.Contains(""retained-type FFF0 phase deadline expired""),""missing owned deadline diagnostic"");
  r.Disconnect();Need(!r.Connected && r.Devices.All(d=>d.Disposals==1) && r.Services.All(s=>s.Disposals==1),mode+"" final resource cleanup"");
 }
 static async Task RecoveryCases(){
  CancellationTokenSource.Created.Clear();var pending=new Recovery(""pending-close"",true);
  var attempt=pending.ConnectAsync(7,BluetoothAddressType.Random);await Task.Delay(10);
  Need(!attempt.IsCompleted && pending.InitialQueries==1,""pending discovery was not reached"");
  pending.Disconnect();Need(await Task.WhenAny(attempt,Task.Delay(500))==attempt,""local close did not cancel pending discovery wait"");
  Exception canceled=null;try{await attempt;}catch(Exception e){canceled=e;}
  Need(canceled is IOException && canceled.Message.Contains(""connection attempt canceled""),""explicit close mislabeled as timeout"");
  Need(pending.FreshOpens==0 && pending.Subscriptions==0 && pending.LeaseFree() && pending.WaitRetired && pending.Devices.All(d=>d.Disposals==1) && CancellationTokenSource.Created.All(c=>c.Disposed),""local close retried/subscribed or leaked resources"");
  await RecoveryCase(""success"",true,true,0,1,0);
  await RecoveryCase(""success"",false,true,0,1,0);
  await RecoveryCase(""cold-stale-cache"",false,true,0,1,0);
  await RecoveryCase(""pending-link"",true,true,0,1,0);
  foreach(string cacheCase in new[]{""cache-miss"",""cache-error"",""cache-exception"",""cache-wrong-service"",""cache-duplicate-service"",""stale-status"",""stale-missing"",""stale-notify"",""stale-write"",""stale-duplicate"",""stale-exception""})await RecoveryCase(cacheCase,true,true,0,2,0);
  await RecoveryCase(""invalid-characteristics"",false,false,0,1,0);
  await RecoveryCase(""invalid-characteristics"",true,false,1,2,1);
  await RecoveryCase(""characteristic-close"",false,false,0,1,0);
  await RecoveryCase(""characteristic-timeout"",false,false,0,1,0);
  await RecoveryCase(""refresh-close"",true,false,0,2,0);
  await RecoveryCase(""refresh-timeout"",true,false,1,2,1);
  await RecoveryCase(""unreachable"",true,true,1,4,1);
  await RecoveryCase(""warm-timeout"",true,true,1,1,1);
  await RecoveryCase(""explicit-cancel"",true,false,0,1,0);
  await RecoveryCase(""disposed-deadline"",true,false,0,1,0);
  await RecoveryCase(""resolve-close"",true,false,0,0,0);
  await RecoveryCase(""service-close"",true,false,0,1,0);
  await RecoveryCase(""fresh-resolve-close"",true,false,1,1,0);
  await RecoveryCase(""link-deadline"",true,true,1,1,1);
  await RecoveryCase(""cold-timeout"",false,false,0,1,0);
  await RecoveryCase(""unreachable"",false,false,0,3,0);
  await RecoveryCase(""fresh-timeout"",true,false,1,1,1);
  await RecoveryCase(""fresh-address-timeout"",true,false,1,1,0);
  await RecoveryCase(""fresh-failure"",true,false,1,4,1);
  await RecoveryCase(""subscription-failure"",true,false,0,1,0);
 }
 static async Task SubscriptionCases(){
  foreach(string mode in new[]{""late-discovery"",""subscription-close-before-budget"",""subscription-expired-before-budget"",""subscription-hung"",""settle-timeout""}){
   CancellationTokenSource.Created.Clear();var r=new Recovery(mode,true);Exception failure=null;
   try{await r.ConnectAsync(7,BluetoothAddressType.Random);}catch(Exception e){failure=e;}
   bool success=mode==""late-discovery"",closed=mode==""subscription-close-before-budget"";
   Need(success?failure==null:failure is IOException,mode+"" terminal result"");
   if(!success)Need(failure.Message.Contains(closed?""connection attempt canceled"":""timed out""),mode+"" cancel/expiry classification"");
   Need(r.FreshOpens==0 && r.InitialQueries==1 && r.FreshQueries==0,mode+"" discovery replay/fallback"");
   Need(CancellationTokenSource.Created.Count==1,mode+"" replaced owned CTS"");
   var phase=CancellationTokenSource.Created[0];
   bool before=mode==""subscription-close-before-budget"" || mode==""subscription-expired-before-budget"";
   Need(phase.PhaseStarts==1 && phase.SubscriptionStarts==(before?0:1) && r.Subscriptions==(before?0:1),mode+"" subscription retry/renewal count"");
   Need(phase.Disposed && r.WaitRetired && r.LeaseFree(),mode+"" leaked connection ownership"");
   if(success)Need(r.Connected && r.SubscriptionFinished!=null && r.SubscriptionFinished.ElapsedMilliseconds>=200,""250 ms settle was skipped"");
   r.Disconnect();Need(!r.Connected && r.Devices.All(d=>d.Disposals==1) && r.Services.All(s=>s.Disposals==1),mode+"" resource retirement"");
  }
  CancellationTokenSource.Created.Clear();var pending=new Recovery(""subscription-pending-close"",true);
  var attempt=pending.ConnectAsync(7,BluetoothAddressType.Random);await Task.Delay(10);
  Need(!attempt.IsCompleted && pending.Subscriptions==1 && CancellationTokenSource.Last.Budget==5000,""pending subscription gate not reached"");
  pending.Disconnect();Need(await Task.WhenAny(attempt,Task.Delay(500))==attempt,""explicit close did not cancel subscription"");
  Exception canceled=null;try{await attempt;}catch(Exception e){canceled=e;}
  Need(canceled is IOException && canceled.Message.Contains(""connection attempt canceled""),""subscription close mislabeled as timeout"");
  Need(pending.FreshOpens==0 && pending.Subscriptions==1 && pending.WaitRetired && pending.LeaseFree() && CancellationTokenSource.Created.All(c=>c.Disposed),""subscription close replayed/leaked"");
  Need(pending.Devices.All(d=>d.Disposals==1) && pending.Services.All(s=>s.Disposals==1),""pending subscription resources leaked"");
 }
";
    }
}
