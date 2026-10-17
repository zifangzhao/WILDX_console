using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;

/*
 * Deliberately headless NP2 BLE HIL smoke runner.  It loads the built desktop
 * assembly and calls its internal BLE/configuration APIs by reflection; it
 * does not construct a Form or fabricate a device status/receipt.
 *
 * status/reconnect/observed-status: discovery + STATUS + NPC2 CAPS only (read-only).
 * latency also measures eight clock-op0 reads; no clock commit or synchronization claim.
 * sync: stopped-only production GUI Initialize (16 reads, one commit), then one Observe heartbeat.
 * record1/record/record3: select P1, P1+P2, or P1+P2+P3 and stage exactly
 * the supplied prefix maps at a requested 7.5/15/20/30k profile.
 * Disposable preview remains P1 only; this does not qualify P3 preview.
 * starts a caller-bounded reservation, normally stops about ten seconds after Recording,
 * or waits for exact automatic Complete with the explicit full-duration option.
 *
 * format: explicit, UID-guarded initialization of a stopped detected SD.
 * rescue: explicit reset of this stopped MCU, followed by reconnect/status.
 * recover: explicit, UID-guarded preservation of an interrupted allocation.
 * Never switches mode, downloads data, or replays a mutating command.
 */
internal static class Np2BleHardware
{
    const string RequiredUid = "001A0004434250062039314E";
    const uint RequiredMask = 3U;
    const uint DefaultRecordHz = 20000U;
    const uint DefaultReservationSeconds = 60U;
    const int OverallSeconds = 180;
    const int LatencyStatusQueries = 20;
    const int LatencyCapsQueries = 4;
    const int LatencyClockQueries = 8;
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static Assembly app;
    static TextWriter log;
    static uint sequence;
    static bool sequenceSeeded;
    static string assemblyDirectory;
    // Diagnostic-only fixed wall/monotonic correspondence for clock exchanges.
    // It is never supplied to the production clock model.
    static DateTime clockWallAnchor;
    static long clockStopwatchAnchor;

    [MTAThread] // Headless WinRT callbacks must not wait for an absent UI message pump.
    static int Main(string[] args)
    {
        try { return Run(args).GetAwaiter().GetResult(); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static async Task<int> Run(string[] args)
    {
        if (args == null || args.Length < 4)
            throw new ArgumentException("<WILDX_console.exe> <BLE-12hex> <output-dir> <status|observed-status|latency|sync|reconnect [0..30 seconds]|format|recover|rescue|record1|record|record3> [P1 [P2 [P3]] [7500|15000|20000|30000] [1..900 seconds; >60 requires full-duration] [no-preview] [full-duration] [record-reconnect]]");
        string action = args[3].ToLowerInvariant();
        if (action != "status" && action != "observed-status" && action != "latency" && action != "sync" && action != "reconnect" && action != "record1" && action != "record" && action != "record3" && action != "format" && action != "recover" && action != "rescue") throw new ArgumentException("Action must be status, observed-status, latency, sync, reconnect, record1, record, record3, format, recover or rescue.");
        uint recordHz = DefaultRecordHz, reservationSeconds = DefaultReservationSeconds, reconnectDelay = 0;
        uint recordMask=action=="record1"?1U:action=="record3"?7U:RequiredMask;
        bool isRecord=action=="record1" || action=="record" || action=="record3";
        bool requestPreview = true, fullDuration = false, recordReconnect = false;
        string[] maps;
        if (isRecord)
        {
            int mapCount=RecordMapCount(args),rateIndex=4+mapCount;
            requestPreview = RecordPreview(args);
            fullDuration = RecordFlag(args,"full-duration");
            recordReconnect = RecordFlag(args,"record-reconnect");
            maps = args.Skip(4).Take(mapCount).ToArray();
            foreach(string map in maps)if(!File.Exists(map))throw new FileNotFoundException("Probe configuration input was not found; no connection or command attempted.",map);
            if(recordMask==7U)recordHz=15000U;
            if (args.Length > rateIndex) recordHz = Positive(args[rateIndex], "sample rate");
            if (args.Length > rateIndex+1) reservationSeconds = Positive(args[rateIndex+1], "capture duration");
            RequireRecordDuration(recordHz,reservationSeconds,fullDuration,recordMask);
            RequireRecordReconnectOptions(recordReconnect,fullDuration,reservationSeconds);
        }
        else if (action == "reconnect")
        {
            if (args.Length != 4 && args.Length != 5) throw new ArgumentException("reconnect takes an optional 0..30 second disconnected delay.");
            if (args.Length == 5) reconnectDelay = ReconnectDelay(args[4]);
            maps = new string[0];
        }
        else
        {
            if (args.Length != 4) throw new ArgumentException(action + " takes no configuration/rate arguments.");
            maps = new string[0];
        }

        ulong address;
        string addressText = args[1].Replace(":", "").Replace("-", "").Trim();
        if (addressText.Length != 12 || !UInt64.TryParse(addressText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) || address == 0)
            throw new ArgumentException("Exact 12-digit BLE address required.");
        string exe = Path.GetFullPath(args[0]);
        if (!File.Exists(exe)) throw new FileNotFoundException("Built WILDX assembly was not found.", exe);
        string outputRoot = Path.GetFullPath(args[2]);
        string output = Directory.Exists(outputRoot)
            ? Path.Combine(outputRoot, "np2-ble-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
            : outputRoot;
        Directory.CreateDirectory(output);

        using (var writer = new StreamWriter(Path.Combine(output, "events.log"), false))
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(isRecord?RecordBudgetSeconds(recordReconnect,reservationSeconds):OverallSeconds)))
        {
            log = TextWriter.Synchronized(writer);
            clockWallAnchor = DateTime.UtcNow;
            clockStopwatchAnchor = Stopwatch.GetTimestamp();
            Log("runner=NP2 BLE HIL; action=" + action + "; output=" + output);
            Log("assembly=" + exe + "; sha256=" + Hash(exe) + "; peer=" + addressText.ToUpperInvariant());
            assemblyDirectory = Path.GetDirectoryName(exe);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSibling;
            try
            {
                app = Assembly.LoadFrom(exe);
                if(isRecord)RequireRecordHost(T("CE32_console.NpV2SdControl"),recordMask,recordHz);
                sequence = NewSequence();
                sequenceSeeded = false;
                if(isRecord)Log("preview_policy="+(requestPreview ? "one-disposable-P1-request" : "no-preview")+"; P3-preview=unsupported");
                if(isRecord)Log("record_policy="+(fullDuration ? "finite-automatic-complete" : "historical-early-stop")+"; mask="+recordMask+"; rate="+recordHz+"; overall_budget_seconds="+RecordBudgetSeconds(recordReconnect,reservationSeconds));
                if(recordReconnect)Log("record_reconnect=true; overall_budget_seconds="+RecordBudgetSeconds(true,reservationSeconds)+"; diagnostic failure never sends STOP; finite device recording remains autonomous.");
                await RunConnected(action, address, output, maps, recordHz, reservationSeconds, reconnectDelay, requestPreview, fullDuration, recordReconnect, recordMask, budget.Token);
                Log("RESULT=PASS; claims are limited to the logged production BLE receipts and states.");
                return 0;
            }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= ResolveSibling; }
        }
    }

    static uint ReconnectDelay(string text)
    {
        uint seconds;
        if (!UInt32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds > 30U)
            throw new ArgumentException("reconnect delay is 0..30 seconds.");
        return seconds;
    }

    static async Task WaitReconnectDelay(uint seconds, CancellationToken cancel)
    {
        if (seconds != 0) await Task.Delay(TimeSpan.FromSeconds(seconds), cancel);
        else cancel.ThrowIfCancellationRequested();
    }

    static bool RecordPreview(string[] args)
    {
        return !RecordFlag(args,"no-preview");
    }

    static int RecordMapCount(string[] args)
    {
        if(args==null || args.Length<4)throw new ArgumentException("Invalid record arguments.");
        int count=String.Equals(args[3],"record1",StringComparison.OrdinalIgnoreCase)?1:
            String.Equals(args[3],"record",StringComparison.OrdinalIgnoreCase)?2:
            String.Equals(args[3],"record3",StringComparison.OrdinalIgnoreCase)?3:0;
        if(count==0 || args.Length<4+count || args.Length>9+count)throw new ArgumentException("Record requires exactly the selected P1/P2[/P3] inputs, optional rate/duration and unique final flags.");
        return count;
    }

    static bool RecordFlag(string[] args,string wanted)
    {
        int mapCount=RecordMapCount(args);
        bool noPreview=false,fullDuration=false,recordReconnect=false;
        for(int i=6+mapCount;i<args.Length;i++){
            if(args[i]=="no-preview" && !noPreview)noPreview=true;
            else if(args[i]=="full-duration" && !fullDuration)fullDuration=true;
            else if(args[i]=="record-reconnect" && !recordReconnect)recordReconnect=true;
            else throw new ArgumentException("Only unique final no-preview/full-duration/record-reconnect literals are allowed.");
        }
        return wanted=="no-preview"?noPreview:wanted=="full-duration"?fullDuration:recordReconnect;
    }

    static bool RecordProfile(uint mask,uint hz)
    {
        return (mask==1U || mask==3U || mask==7U) && (hz==7500U || hz==15000U || hz==20000U || hz==30000U);
    }

    static void RequireRecordDuration(uint recordHz,uint reservationSeconds,bool fullDuration,uint mask=RequiredMask)
    {
        if(!RecordProfile(mask,recordHz) || reservationSeconds<1U || reservationSeconds>900U ||
            (!fullDuration && reservationSeconds>DefaultReservationSeconds))
            throw new ArgumentException("Supported prefix profiles are masks1/3/7 at7.5/15/20/30k; firmware acknowledgement remains authoritative. Bounded duration is1..900 seconds and >60 requires full-duration.");
    }

    static byte[] RecordRequest(Type control,uint operation,uint id,uint hz,uint blocks,uint mask)
    {
        bool profile=operation==1U || operation==2U || operation==4U;
        byte[] request=(byte[])(profile && mask!=RequiredMask
            ? InvokeStatic(control,"Request",operation,id,hz,blocks,0UL,mask)
            : InvokeStatic(control,"Request",operation,id,hz,blocks,0UL));
        if(request==null || request.Length!=64 || BitConverter.ToUInt32(request,16)!=(profile?mask:0U) ||
            BitConverter.ToUInt32(request,20)!=hz || BitConverter.ToUInt32(request,24)!=blocks)
            throw new IOException("Production control request did not preserve the full selected mask/rate/blocks.");
        return request;
    }

    static void RequireRecordHost(Type control,uint mask,uint hz)
    {
        if(!RecordProfile(mask,hz))throw new ArgumentException("Unsupported record pair.");
        // Pure request construction before Connect: a legacy five-argument-only
        // assembly must refuse non-dual prefixes, not quietly send a mask3 START.
        RecordRequest(control,1U,2U,hz,0U,mask);
        RecordRequest(control,2U,2U,hz,4U,mask);
    }

    static void RequireRecordProfile(object status,uint mask,uint hz)
    {
        if(!RecordProfile(mask,hz) || U(Get(status,"Mask"))!=mask || U(Get(status,"Hz"))!=hz)
            throw new IOException("The acknowledged selected recording mask/rate changed; no replay allowed.");
    }

    static void RequireRecordReconnectOptions(bool recordReconnect,bool fullDuration,uint seconds)
    {
        if(recordReconnect && (!fullDuration || (seconds!=60U && seconds!=120U && seconds!=900U)))throw new ArgumentException("record-reconnect requires full-duration and an explicit 60-, 120- or 900-second reservation.");
    }

    static int RecordBudgetSeconds(bool recordReconnect,uint reservationSeconds)
    {
        return reservationSeconds>120U?2400:reservationSeconds>=DefaultReservationSeconds?420:recordReconnect?240:OverallSeconds;
    }

    static bool RecordAutonomous(bool recordReconnect,bool fullDuration,uint reservationSeconds)
    {
        return recordReconnect || (fullDuration && reservationSeconds>DefaultReservationSeconds);
    }

    static bool RecordReconnectDue(bool enabled,bool attempted,bool applied,uint state,long elapsedMilliseconds)
    {
        return enabled && !attempted && applied && state==3U && elapsedMilliseconds>=10000L;
    }

    static uint[] RecordReconnectSnapshot(object status)
    {
        if(U(Get(status,"State"))!=3U || U(Get(status,"Error"))!=0U || U(Get(status,"Fault"))!=0U ||
            !RecordProfile(U(Get(status,"Mask")),U(Get(status,"Hz"))))
            throw new IOException("Reconnect requires a fresh healthy closed-profile NP2 Recording status.");
        return new[]{"Mask","Hz","Start","End","Capacity","LastId","Chunks"}.Select(name=>U(Get(status,name))).ToArray();
    }

    static string RecordReconnectSnapshotText(uint[] snapshot,uint epoch)
    {
        string[] names={"Mask","Hz","Start","End","Capacity","LastId","Chunks"};
        return "epoch="+epoch+"; "+String.Join("; ",names.Select((name,i)=>name+"="+snapshot[i]));
    }

    static void RequireRecordReconnectIdentity(uint[] before,object after,uint oldEpoch,uint newEpoch,bool connected)
    {
        if(before==null || before.Length!=7 || !connected || newEpoch==oldEpoch)
            throw new IOException("Reconnect did not establish a new owned transport epoch.");
        uint[] current=RecordReconnectSnapshot(after);
        for(int i=0;i<6;i++)if(current[i]!=before[i])throw new IOException("Reconnect changed immutable recording identity at field "+i+".");
        if(current[6]<before[6])throw new IOException("Reconnect recording progress moved backwards.");
    }

    static void RequireRecordReconnectComplete(bool enabled,bool witnessed,int postReconnectHeartbeats)
    {
        if(enabled && (!witnessed || postReconnectHeartbeats<1))
            throw new IOException("Recording completed, but reconnect/current-epoch heartbeat was not witnessed.");
    }

    static uint RecordStopSeconds(uint reservationSeconds)
    {
        return Math.Min(10U,Math.Max(1U,reservationSeconds/2U));
    }

    static bool RecordStopDue(bool fullDuration,uint reservationSeconds,TimeSpan elapsed)
    {
        return !fullDuration && elapsed>=TimeSpan.FromSeconds(RecordStopSeconds(reservationSeconds));
    }

    static bool RecordHeartbeatDue(uint state,long elapsedMilliseconds,long lastAttemptMilliseconds)
    {
        return state==3U && elapsedMilliseconds-lastAttemptMilliseconds>=2000L;
    }

    static int RecordHeartbeatMinimum(bool fullDuration,uint reservationSeconds)
    {
        // One successful read per four bounded seconds tolerates the 2s poll
        // cadence's transport jitter, but cannot qualify a quiet/failed clock.
        return Math.Max(1,(int)(fullDuration?reservationSeconds:RecordStopSeconds(reservationSeconds))/4);
    }

    static void RequireRecordComplete(bool fullDuration,uint chunks,uint blocks,uint reservationSeconds,int heartbeats,int heartbeatErrors)
    {
        if(fullDuration && (blocks==0U || chunks!=blocks))throw new IOException("Full-duration Complete did not contain the exact requested block count.");
        if(heartbeatErrors!=0 || heartbeats<RecordHeartbeatMinimum(fullDuration,reservationSeconds))
            throw new IOException("Recording completed, but clock heartbeat coverage/quality did not qualify.");
    }

    static void RequireRecordingClockRequest(byte[] request,uint magic)
    {
        if(request==null || request.Length!=64 || BitConverter.ToUInt32(request,0)!=magic || BitConverter.ToUInt32(request,8)!=0U)
            throw new IOException("Recording heartbeat permits only the production clock READ; no commit or replay.");
    }

    static async Task<BluetoothAddressType> ObserveAddressType(ulong address,CancellationToken cancel,string label)
    {
        var elapsed=Stopwatch.StartNew();
        var watcher=new BluetoothLEAdvertisementWatcher{ScanningMode=BluetoothLEScanningMode.Active};
        var seen=new TaskCompletionSource<BluetoothAddressType>(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementReceivedEventArgs> receive=
            (sender,args)=>{if(args.BluetoothAddress==address)seen.TrySetResult(args.BluetoothAddressType);};
        object token=watcher.GetType().GetMethod("add_Received").Invoke(watcher,new object[]{receive});
        try {
            watcher.Start();
            Task first=await Task.WhenAny(seen.Task,Task.Delay(30000,cancel));
            cancel.ThrowIfCancellationRequested();
            if(first!=seen.Task)throw new TimeoutException("No fresh advertisement for the exact selected BLE address within 30 seconds.");
            BluetoothAddressType type=await seen.Task;
            Log(label+".witness_type="+type+"; elapsed_ms="+elapsed.ElapsedMilliseconds);
            return type;
        } finally {
            if(watcher.Status==BluetoothLEAdvertisementWatcherStatus.Started)watcher.Stop();
            watcher.GetType().GetMethod("remove_Received").Invoke(watcher,new[]{token});
        }
    }

    static Task ConnectObserved(object connection,ulong address,BluetoothAddressType type)
    {
        MethodInfo method=connection.GetType().GetMethods(All).SingleOrDefault(m=>m.Name=="ConnectAsync" && m.GetParameters().Length==2 &&
            m.GetParameters()[0].ParameterType==typeof(ulong) && Nullable.GetUnderlyingType(m.GetParameters()[1].ParameterType)==typeof(BluetoothAddressType));
        if(method==null)throw new MissingMethodException(connection.GetType().FullName,"ConnectAsync(ulong,BluetoothAddressType?)");
        try{return (Task)method.Invoke(connection,new object[]{address,type});}
        catch(TargetInvocationException error){throw error.InnerException??error;}
    }

    static long PercentileMilliseconds(long[] values,int numerator,int denominator)
    {
        if(values==null || values.Length==0 || numerator<1 || numerator>denominator)throw new ArgumentException("Latency percentile needs samples and a valid rank.");
        long[] sorted=(long[])values.Clone();Array.Sort(sorted);
        int index=(values.Length*numerator+denominator-1)/denominator-1;
        return sorted[index];
    }

    static string LatencySummary(long[] values)
    {
        return "count="+values.Length+"; median_ms="+PercentileMilliseconds(values,1,2)+"; p95_ms="+PercentileMilliseconds(values,95,100)+"; max_ms="+values.Max();
    }

    static long ClockProcessingMicroseconds(uint rx,uint tx,uint state,bool valid,uint generation,
        bool previous,uint previousTx,bool previousValid,uint previousGeneration)
    {
        // Invalid mappings may retain an old generation. Do not commit a clock
        // merely to measure transport latency or call this synchronization proof.
        if(state!=1U || (valid && generation==0U) || unchecked((int)(tx-rx))<0 ||
            (previous && (valid!=previousValid || generation!=previousGeneration || unchecked((int)(rx-previousTx))<0)))
            throw new IOException("Clock latency receipt changed state/generation/validity or moved backwards.");
        return (long)unchecked(tx-rx)*1000;
    }

    static async Task RunLatency(object connection,object client,CancellationToken cancel)
    {
        var statusMilliseconds=new long[LatencyStatusQueries];
        for(int i=0;i<statusMilliseconds.Length;i++){
            var elapsed=Stopwatch.StartNew();object status=await SendV2(connection,0,0,0,cancel,"latency-status-"+(i+1));
            RequireHealthy(status);if(U(Get(status,"State"))!=1U)throw new IOException("Latency action requires every measured STATUS to remain Idle.");
            statusMilliseconds[i]=elapsed.ElapsedMilliseconds;Log("latency.status-"+(i+1)+".elapsed_ms="+statusMilliseconds[i]);
        }
        var capsMilliseconds=new long[LatencyCapsQueries];
        for(int i=0;i<capsMilliseconds.Length;i++){
            int port=i%2;var elapsed=Stopwatch.StartNew();object caps=await Await(Invoke(client,"CapsAsync",(byte)port,cancel));
            RequireCaps(caps,port);capsMilliseconds[i]=elapsed.ElapsedMilliseconds;LogCaps(port,caps);Log("latency.caps-"+(i+1)+".elapsed_ms="+capsMilliseconds[i]);
        }
        Log("latency.status."+LatencySummary(statusMilliseconds));
        Log("latency.caps."+LatencySummary(capsMilliseconds));
        var clockMilliseconds=new long[LatencyClockQueries];
        Type clockType=T("CE32_console.NpV2OnlineClock");
        MethodInfo midpoint=clockType.GetMethod("MidpointUtc",All);
        if(midpoint==null)throw new MissingMethodException(clockType.FullName,"MidpointUtc");
        uint epoch=U(Get(connection,"Epoch")),previousTx=0,previousGeneration=0;
        bool previous=false,previousValid=false;
        for(int i=0;i<clockMilliseconds.Length;i++){
            uint id=NextId();
            byte[] request=(byte[])InvokeStatic(clockType,"Request",0U,id,0U,0UL,0U,0U);
            var elapsed=Stopwatch.StartNew();
            byte[] reply=(byte[])await Await(Invoke(connection,"ExchangeAsync",(byte)0xb9,request,cancel));
            elapsed.Stop();
            long elapsedUs=(long)(elapsed.ElapsedTicks*1000000.0/Stopwatch.Frequency);
            object receipt=InvokeStatic(clockType,"Parse",reply,0U,id);
            uint rx=U(Get(receipt,"Rx")),tx=U(Get(receipt,"Tx")),generation=U(Get(receipt,"Generation"));
            bool valid=(bool)Get(receipt,"Valid");
            if(!(bool)Get(connection,"Connected") || U(Get(connection,"Epoch"))!=epoch)
                throw new IOException("Clock latency connection changed; no retry sent.");
            long processingUs=ClockProcessingMicroseconds(rx,tx,U(Get(receipt,"State")),valid,generation,
                previous,previousTx,previousValid,previousGeneration);
            // Use the loaded production engine's timing plausibility check;
            // zero is just a relative host origin, not a proposed UTC mapping.
            object[] timing={0L,elapsedUs,rx,tx,0L};midpoint.Invoke(null,timing);
            clockMilliseconds[i]=elapsed.ElapsedMilliseconds;
            Log("latency.clock-"+(i+1)+"; id="+id+"; elapsed_us="+elapsedUs+"; device_processing_us="+processingUs+
                "; rx_ms="+rx+"; tx_ms="+tx+"; valid="+valid+"; generation="+generation+
                "; mapping="+(valid?"reported-valid":"unsynchronized")+"; timing_only=true");
            Log("latency.clock-"+(i+1)+".request="+Hex(request));
            Log("latency.clock-"+(i+1)+".reply="+Hex(reply));
            previous=true;previousTx=tx;previousValid=valid;previousGeneration=generation;
        }
        Log("latency.clock."+LatencySummary(clockMilliseconds)+"; device processing uses 1ms ticks; no clock commit or synchronization qualification.");
    }

    static async Task RunConnected(string action, ulong address, string output, string[] mapInputs, uint recordHz, uint reservationSeconds, uint reconnectDelay, bool requestPreview, bool fullDuration, bool recordReconnect, uint recordMask, CancellationToken cancel)
    {
        Type connectionType = T("USB512_interface.NpBleConnection");
        object connection = New(connectionType);
        bool started = false, stopAttempted = false;
        bool autonomous = RecordAutonomous(recordReconnect,fullDuration,reservationSeconds);
        object lastStatus = null;
        try
        {
            if(action=="observed-status" || action=="latency" || action=="sync")
            {
                BluetoothAddressType type=await ObserveAddressType(address,cancel,action);
                await ConnectObserved(connection,address,type);
            }
            else
            {
                try { await Await(Invoke(connection, "ConnectAsync", address)); }
                catch (IOException error)
                {
                    LogConnectDiagnostic(connection,"connect-failed");
                    Log("connect=failed before commands; one read-only reconnect after 5s: "+error.Message);
                    await Task.Delay(5000,cancel);
                    await Await(Invoke(connection,"ConnectAsync",address));
                }
            }
            Log("connected=true; epoch=" + U(Get(connection, "Epoch")));
            LogConnectDiagnostic(connection,"connect");

            lastStatus = action=="sync" ? await ReadDevice(connection,cancel) : await ReadDeviceWithRecovery(connection, cancel, "initial");
            LogStatus("initial", lastStatus);

            object client = NewConfigClient(connection);
            uint initialMask=U(Get(lastStatus,"Mask"));
            object caps0 = await Await(Invoke(client, "CapsAsync", (byte)0, cancel));
            RequireCaps(caps0, 0); LogCaps(0, caps0);
            object caps1=null;
            if((initialMask&2U)!=0){
                caps1=await Await(Invoke(client,"CapsAsync",(byte)1,cancel));
                RequireCaps(caps1,1);LogCaps(1,caps1);
            }
            if((initialMask&4U)!=0){
                object caps2=await Await(Invoke(client,"CapsAsync",(byte)2,cancel));
                RequireCaps(caps2,2);LogCaps(2,caps2);
            }
            SeedSequence(lastStatus, "initial-caps");

            if (action == "sync")
            {
                await RunSync(connection,cancel);
                Log("sync action completed: one clock commit and one Observe heartbeat; no configuration/profile/start/stop/reset/reconnect command was sent.");
                return;
            }

            if (action == "latency")
            {
                RequireHealthy(lastStatus);
                if(U(Get(lastStatus,"Mask"))!=RequiredMask || U(Get(lastStatus,"State"))!=1U)throw new IOException("Latency action requires the exact fault-free dual-NP2 Idle status.");
                await RunLatency(connection,client,cancel);
                Log("latency action completed read-only; no clock commit/configuration/profile/start/stop command was sent.");
                return;
            }

            if (action == "status" || action == "observed-status")
            {
                Log(action+" action completed read-only; no configuration/profile/start/stop command was sent.");
                return;
            }

            if (action == "reconnect")
            {
                Log("reconnect=local disconnect; same connection instance will rediscover the exact peer with STATUS/CAPS only.");
                Log("reconnect_delay_seconds="+reconnectDelay);
                Invoke(connection,"Disconnect");
                await WaitReconnectDelay(reconnectDelay,cancel);
                try { await Await(Invoke(connection,"ConnectAsync",address)); }
                catch(IOException error) {
                    LogConnectDiagnostic(connection,"reconnect-failed");
                    Log("reconnect=failed before read-only commands; one retry after 5s: "+error.Message);
                    await Task.Delay(5000,cancel);
                    await Await(Invoke(connection,"ConnectAsync",address));
                }
                Log("reconnected=true; epoch="+U(Get(connection,"Epoch")));
                LogConnectDiagnostic(connection,"reconnect");
                lastStatus=await ReadDeviceWithRecovery(connection,cancel,"after-reconnect");
                LogStatus("after-reconnect",lastStatus);
                initialMask=U(Get(lastStatus,"Mask"));
                caps0=await Await(Invoke(client,"CapsAsync",(byte)0,cancel));
                RequireCaps(caps0,0);LogCaps(0,caps0);
                if((initialMask&2U)!=0){
                    caps1=await Await(Invoke(client,"CapsAsync",(byte)1,cancel));
                    RequireCaps(caps1,1);LogCaps(1,caps1);
                }
                if((initialMask&4U)!=0){
                    object caps2=await Await(Invoke(client,"CapsAsync",(byte)2,cancel));
                    RequireCaps(caps2,2);LogCaps(2,caps2);
                }
                Log("reconnect action completed read-only; no reset/sync/profile/config/start/stop command was sent.");
                return;
            }

            if (action == "rescue")
            {
                if ((bool)Get(lastStatus,"Busy")) throw new IOException("This bounded rescue test requires a stopped recorder; no reset was sent.");
                await Await(Invoke(connection,"RescueReset",cancel));
                Log("rescue=request sent once, no ACK; reconnecting to verify the same MCU.");
                await Task.Delay(2000,cancel);
                await Await(Invoke(connection,"ConnectAsync",address));
                LogConnectDiagnostic(connection,"after-rescue-connect");
                lastStatus=await ReadDeviceWithRecovery(connection,cancel,"after-rescue");LogStatus("after-rescue",lastStatus);
                caps0=await Await(Invoke(client,"CapsAsync",(byte)0,cancel));
                caps1=await Await(Invoke(client,"CapsAsync",(byte)1,cancel));
                RequireCaps(caps0,0);RequireCaps(caps1,1);LogCaps(0,caps0);LogCaps(1,caps1);
                SeedSequence(lastStatus,"after-rescue-caps");
                RequireHealthy(lastStatus);
                if ((bool)Get(lastStatus,"Busy") || U(Get(caps0,"PortState"))!=0 || U(Get(caps1,"PortState"))!=0)
                    throw new IOException("Fresh stopped APP/reset configuration state was not confirmed.");
                Log("rescue=reconnected to exact MCU, fault cleared, staged/applied RAM state reset; no recording restarted.");
                return;
            }

            if (action == "recover")
            {
                RequireHealthy(lastStatus);
                if (!(bool)InvokeStatic(T("CE32_console.NpV2SdControl"),"CanRecover",lastStatus) ||
                    !(bool)InvokeStatic(T("CE32_console.NpV2SdControl"),"CanQuarantine",lastStatus))
                    throw new IOException("Explicit preservation requires a stopped, mounted, quarantinable interrupted allocation; no command sent.");
                uint before=U(Get(lastStatus,"Start")),capacity=U(Get(lastStatus,"Capacity")),id=NextId();
                byte[] request=(byte[])InvokeStatic(T("CE32_console.NpV2SdControl"),"Request",8U,id,0U,0U,0UL);
                byte[] reply=(byte[])await Await(Invoke(connection,"ExchangeAsync",(byte)0xb9,request,cancel));
                Log("recover.request="+Hex(request));Log("recover.reply="+Hex(reply));
                RequireHealthy(InvokeStatic(T("CE32_console.NpV2SdControl"),"Parse",reply,8U,id));
                for(int i=0;i<30;i++){
                    await Task.Delay(500,cancel);lastStatus=await ReadStatusWithRecovery(connection,cancel,"recover-status");
                    LogStatus("recover-poll",lastStatus);RequireHealthy(lastStatus);
                    if(U(Get(lastStatus,"Capacity"))!=capacity)throw new IOException("Card capacity changed during preservation.");
                    if(U(Get(lastStatus,"State"))==1 && !(bool)Get(lastStatus,"Busy") &&
                        (bool)InvokeStatic(T("CE32_console.NpV2SdControl"),"CanStart",lastStatus) && U(Get(lastStatus,"Start"))>before){
                        Log("recover=complete; interrupted allocation preserved, next LBA advanced; no format/start/stop command sent.");return;
                    }
                }
                throw new TimeoutException("Preservation not confirmed complete; do not replay it.");
            }

            if (action == "format")
            {
                RequireHealthy(lastStatus);
                if (!(bool)InvokeStatic(T("CE32_console.NpV2SdControl"),"CanPrepareStorage",lastStatus) ||
                    U(Get(lastStatus,"Capacity"))<=8192 || (U(Get(lastStatus,"Flags"))&1)==0)
                    throw new IOException("Explicit formatting requires the identified, stopped, writable detected card.");
                uint capacity=U(Get(lastStatus,"Capacity")),id=NextId();
                byte[] request=(byte[])InvokeStatic(T("CE32_console.NpV2SdControl"),"StorageRequest",id,true);
                byte[] reply=(byte[])await Await(Invoke(connection,"ExchangeAsync",(byte)0xb9,request,cancel));
                Log("format.request="+Hex(request));Log("format.reply="+Hex(reply));
                RequireHealthy(InvokeStatic(T("CE32_console.NpV2SdControl"),"Parse",reply,7U,id));
                for(int i=0;i<30;i++){
                    await Task.Delay(500,cancel);lastStatus=await ReadStatusWithRecovery(connection,cancel,"format-status");
                    LogStatus("format-poll",lastStatus);RequireHealthy(lastStatus);
                    if(U(Get(lastStatus,"Capacity"))!=capacity)throw new IOException("Card capacity changed during format; no command replayed.");
                    if(U(Get(lastStatus,"State"))==1 && !(bool)Get(lastStatus,"Busy") && U(Get(lastStatus,"LastId"))==id){Log("format=complete; card catalog initialized");return;}
                }
                throw new TimeoutException("Format not confirmed complete; do not replay it.");
            }

            RequireStartable(lastStatus);
            object clock = New(T("USB512_interface.NpBleClock"));
            uint recordEpoch=U(Get(connection,"Epoch"));
            uint clockMagic=U(T("CE32_console.NpV2OnlineClock").GetField("RequestMagic",All).GetValue(null));
            bool recordingClock=false;
            Func<byte[], Task<byte[]>> clockExchange = async request => {
                RequireRecordConnection(connection,recordEpoch);
                if(recordingClock)RequireRecordingClockRequest(request,clockMagic);
                byte[] reply=await ExchangeTimed(connection,request,cancel,recordingClock?"record-heartbeat":"record-clock-initialize");
                RequireRecordConnection(connection,recordEpoch);
                return reply;
            };
            Func<uint> next = NextId;
            await Await(Invoke(clock, "Initialize", lastStatus, clockExchange, next));
            if (!(bool)Get(clock, "Synchronized")) throw new IOException("Production NpBleClock did not report a synchronized stopped clock.");
            uint recordGeneration=U(Get(clock,"Generation"));
            recordingClock=true; // All subsequent clock exchanges are Observe-only.
            Log("clock=synchronized; generation=" + U(Get(clock, "Generation")) + "; bound_ms=" + Get(clock, "BoundMilliseconds"));

            lastStatus = await SendV2(connection, 1, recordHz, 0, cancel, "profile",recordMask);
            RequireStartable(lastStatus);
            RequireRecordProfile(lastStatus,recordMask,recordHz);

            object p1 = PrepareRequest(mapInputs[0], 1, output);
            object p2 = (recordMask&2U)!=0?PrepareRequest(mapInputs[1], 2, output):null;
            object p3 = recordMask==7U?PrepareRequest(mapInputs[2],3,output):null;
            object staged1 = await Await(Invoke(client, "StageAsync", p1, cancel));
            LogReceipt("staged-P1", staged1);
            if(p2!=null){
                object staged2=await Await(Invoke(client,"StageAsync",p2,cancel));
                LogReceipt("staged-P2",staged2);
            }
            if(p3!=null){
                object staged3=await Await(Invoke(client,"StageAsync",p3,cancel));
                LogReceipt("staged-P3",staged3);
            }

            uint blocks = (uint)InvokeStatic(T("CE32_console.NpV2SdControl"), "Blocks", lastStatus, recordHz, reservationSeconds);
            if (blocks == 0) throw new IOException("The bounded record request unexpectedly selected continuous storage.");
            started = true; // A START write may have reached firmware even if its receipt is lost.
            lastStatus = await SendV2(connection, 2, recordHz, blocks, cancel, "start",recordMask);
            RequireRecordProfile(lastStatus,recordMask,recordHz);
            Log("start_request_blocks=" + blocks + "; mask="+recordMask+"; rate=" + recordHz + "; reservation_seconds=" + reservationSeconds);

            LogStatus("start-ack", lastStatus);
            bool sawPreparing = false, sawRecording = false, applied = false, previewLogged = false;
            int heartbeats=0,heartbeatErrors=0;
            bool reconnectAttempted=false,reconnectWitnessed=false;
            int postReconnectHeartbeats=0;
            long lastHeartbeatAttempt=-2000L;
            Stopwatch recorded = new Stopwatch();
            Stopwatch poll = Stopwatch.StartNew();
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                if (poll.ElapsedMilliseconds != 0) await Task.Delay(400, cancel);
                lastStatus = await ReadStatusWithRecovery(connection, cancel, "status");
                RequireRecordConnection(connection,recordEpoch);
                LogStatus("poll", lastStatus);
                uint state = U(Get(lastStatus, "State"));
                RequireHealthy(lastStatus);
                RequireRecordProfile(lastStatus,recordMask,recordHz);
                if (state == 2) sawPreparing = true;
                if (state == 3 && !sawRecording) { sawRecording = true; recorded.Start(); Log("recording=true; preparing_observed="+sawPreparing); }
                if(RecordReconnectDue(recordReconnect,reconnectAttempted,applied,state,recorded.ElapsedMilliseconds)){
                    reconnectAttempted=true; // Exactly one runner disconnect/connect attempt; never replay a mutation.
                    lastStatus=await ReconnectRecording(connection,client,address,lastStatus,p1,p2,recordEpoch,cancel,p3);
                    recordEpoch=U(Get(connection,"Epoch")); // Only after STATUS/UID/applied-map admission.
                    reconnectWitnessed=true;
                    state=U(Get(lastStatus,"State"));
                    lastHeartbeatAttempt=recorded.ElapsedMilliseconds-2000L;
                }
                if(state==3 && RecordHeartbeatDue(state,recorded.ElapsedMilliseconds,lastHeartbeatAttempt)){
                    RequireRecordProfile(lastStatus,recordMask,recordHz);
                    lastHeartbeatAttempt=recorded.ElapsedMilliseconds;
                    try {
                        await ObserveRecordingClock(connection,clock,clockExchange,next,recordEpoch,recordGeneration);
                        heartbeats++;
                        if(reconnectWitnessed)postReconnectHeartbeats++;
                    } catch(Exception heartbeatError) {
                        heartbeatErrors++;
                        Log("record-heartbeat.failed="+heartbeatError.GetType().Name+": "+heartbeatError.Message+"; acquisition continues; no clock mutation/retry.");
                    }
                }
                if (state == 3 && !applied)
                {
                    object applied1 = await Await(Invoke(client, "StatusAsync", (byte)0, cancel));
                    InvokeStatic(T("USB512_interface.Np2ConfigClient"), "RequireApplied", applied1, p1);
                    LogReceipt("applied-P1", applied1);
                    if(p2!=null){
                        object applied2=await Await(Invoke(client,"StatusAsync",(byte)1,cancel));
                        InvokeStatic(T("USB512_interface.Np2ConfigClient"),"RequireApplied",applied2,p2);
                        LogReceipt("applied-P2",applied2);
                    }
                    if(p3!=null){
                        object applied3=await Await(Invoke(client,"StatusAsync",(byte)2,cancel));
                        InvokeStatic(T("USB512_interface.Np2ConfigClient"),"RequireApplied",applied3,p3);
                        LogReceipt("applied-P3",applied3);
                    }
                    applied = true;
                }
                if (requestPreview && state == 3 && applied && !previewLogged)
                {
                    previewLogged = true;
                    try
                    {
                        object burst = await Await(Invoke(connection, "CapturePreview", NextId(), 1U, 0U, recordHz, cancel));
                        Log("preview_supersync_phase="+Invoke(burst,"FindPhase"));
                        Log("preview=received; kind=" + U(Get(burst, "Kind")) + "; probe=" + U(Get(burst, "Probe")) + "; channel=" + U(Get(burst, "Channel")) + "; hz=" + U(Get(burst, "Hz")) + "; session=" + U(Get(burst, "Session")));
                    }
                    catch (Exception previewError) { Log("preview=unavailable/disposable; " + previewError.GetType().Name + ": " + previewError.Message); }
                }
                if (state == 3 && !stopAttempted && RecordStopDue(fullDuration,reservationSeconds,recorded.Elapsed))
                {
                    stopAttempted = true; // Never retry a mutating STOP whose receipt becomes uncertain.
                    lastStatus = await SendV2(connection, 3, 0, 0, cancel, "stop");
                    RequireRecordProfile(lastStatus,recordMask,recordHz);
                    Log("stop_requested_id=" + U(Get(lastStatus, "RequestId")));
                }
                if (state == 5)
                {
                    if (!sawRecording || !applied) throw new IOException("Complete occurred without Recording/configuration verification.");
                    RequireHealthy(lastStatus);
                    Log("record-heartbeat.summary; successful="+heartbeats+"; failed="+heartbeatErrors+"; required="+RecordHeartbeatMinimum(fullDuration,reservationSeconds));
                    RequireRecordComplete(fullDuration,U(Get(lastStatus,"Chunks")),blocks,reservationSeconds,heartbeats,heartbeatErrors);
                    RequireRecordReconnectComplete(recordReconnect,reconnectWitnessed,postReconnectHeartbeats);
                    if(recordReconnect)Log("record-reconnect.complete; witnessed="+reconnectWitnessed+"; post_reconnect_heartbeats="+postReconnectHeartbeats+"; exact_finite_blocks="+blocks);
                    Log("complete=true; stop_attempted="+stopAttempted+"; blocks=" + U(Get(lastStatus, "Chunks")));
                    return;
                }
                if (state == 255 || U(Get(lastStatus, "Error")) != 0 || U(Get(lastStatus, "Fault")) != 0)
                    throw new IOException("Firmware reported error/fault while recording; see raw status log.");
            }
        }
        finally
        {
            if (started && !stopAttempted)
            {
                try
                {
                    if ((bool)Get(connection, "Connected"))
                    {
                        lastStatus = await SendV2(connection, 0, 0, 0, CancellationToken.None, "finally-status");
                        RequireRecordProfile(lastStatus,recordMask,recordHz);
                        if(!autonomous)stopAttempted = true;
                        uint state=U(Get(lastStatus,"State"));
                        if(autonomous){
                            Log("finally_stop=FORBIDDEN for record-reconnect or full-duration >60s; fresh state="+state+"; finite device recording remains autonomous; no reset/replay.");
                        }else if(state==2 || state==3 || state==6){
                            lastStatus = await SendV2(connection, 3, 0, 0, CancellationToken.None, "finally-stop");
                            Log("finally_stop=sent; id=" + U(Get(lastStatus, "RequestId")) + "; no retry will occur.");
                        }else Log("finally_stop=not needed; state="+state);
                    }
                    else Log(autonomous?"finally_stop=FORBIDDEN; link closed; finite device recording remains autonomous.":"finally_stop=deferred; link is no longer live and no replay is allowed.");
                }
                catch (Exception stopError) { Log((autonomous?"finally_stop=FORBIDDEN; read-only cleanup status failed; finite device recording remains autonomous. ":"finally_stop=failed/unknown; no retry. ") + stopError.GetType().Name + ": " + stopError.Message); }
            }
            try { Invoke(connection, "Disconnect"); Log("disconnected=true"); }
            finally { Invoke(connection, "Dispose"); }
        }
    }

    static async Task<object> ReconnectRecording(object connection,object client,ulong address,object before,object p1,object p2,uint oldEpoch,CancellationToken cancel,object p3=null)
    {
        RequireRecordConnection(connection,oldEpoch);
        uint[] snapshot=RecordReconnectSnapshot(before);
        if((snapshot[0]&1U)==0 || ((snapshot[0]&2U)!=0)!=(p2!=null) || ((snapshot[0]&4U)!=0)!=(p3!=null))throw new IOException("Reconnect requires the retained requests for every selected port.");
        Log("record-reconnect.begin; "+RecordReconnectSnapshotText(snapshot,oldEpoch)+"; one local disconnect, 2s wait, same-instance/exact-address connect; no recorder mutation.");
        var elapsed=Stopwatch.StartNew();
        bool validated=false;
        try {
        Invoke(connection,"Disconnect");
        await Task.Delay(2000,cancel);
        await Await(Invoke(connection,"ConnectAsync",address)); // No runner retry after deliberate disconnect.
        cancel.ThrowIfCancellationRequested();
        LogConnectDiagnostic(connection,"record-reconnect-connect");
        uint newEpoch=U(Get(connection,"Epoch"));
        object after=await SendV2(connection,0,0,0,cancel,"record-reconnect-status"); // First operation after connection.
        RequireRecordReconnectIdentity(snapshot,after,oldEpoch,newEpoch,(bool)Get(connection,"Connected"));
        object caps0=await Await(Invoke(client,"CapsAsync",(byte)0,cancel));
        RequireCaps(caps0,0);LogCaps(0,caps0);
        if(p2!=null){
            object caps1=await Await(Invoke(client,"CapsAsync",(byte)1,cancel));
            RequireCaps(caps1,1);LogCaps(1,caps1);
        }
        if(p3!=null){
            object caps2=await Await(Invoke(client,"CapsAsync",(byte)2,cancel));
            RequireCaps(caps2,2);LogCaps(2,caps2);
        }
        object applied1=await Await(Invoke(client,"StatusAsync",(byte)0,cancel));
        InvokeStatic(T("USB512_interface.Np2ConfigClient"),"RequireApplied",applied1,p1);
        LogReceipt("record-reconnect-applied-P1",applied1);
        if(p2!=null){
            object applied2=await Await(Invoke(client,"StatusAsync",(byte)1,cancel));
            InvokeStatic(T("USB512_interface.Np2ConfigClient"),"RequireApplied",applied2,p2);
            LogReceipt("record-reconnect-applied-P2",applied2);
        }
        if(p3!=null){
            object applied3=await Await(Invoke(client,"StatusAsync",(byte)2,cancel));
            InvokeStatic(T("USB512_interface.Np2ConfigClient"),"RequireApplied",applied3,p3);
            LogReceipt("record-reconnect-applied-P3",applied3);
        }
        RequireRecordConnection(connection,newEpoch);
        validated=true;
        Log("record-reconnect.validated; elapsed_ms="+elapsed.ElapsedMilliseconds+"; old_epoch="+oldEpoch+"; "+
            RecordReconnectSnapshotText(RecordReconnectSnapshot(after),newEpoch)+"; UID/CAPS/applied-map CRC and IDs confirmed; transport epoch only is replaced.");
        return after;
        } finally {
            elapsed.Stop();Log("record-reconnect.end; validated="+validated+"; old_epoch="+oldEpoch+"; elapsed_ms="+elapsed.ElapsedMilliseconds);
        }
    }

    static async Task RunSync(object connection,CancellationToken cancel)
    {
        uint epoch=U(Get(connection,"Epoch"));
        Action current=()=>{
            cancel.ThrowIfCancellationRequested();
            if(!(bool)Get(connection,"Connected") || U(Get(connection,"Epoch"))!=epoch)
                throw new IOException("Sync connection changed; no retry or replay allowed.");
        };
        object status=await SendV2(connection,0,0,0,cancel,"sync-before");
        current();RequireHealthy(status);
        if(U(Get(status,"State"))!=1U || (bool)Get(status,"Busy") || U(Get(status,"Mask"))!=RequiredMask)
            throw new IOException("Sync action requires a fresh, stopped dual-NP2 Idle STATUS.");
        object clock=New(T("USB512_interface.NpBleClock"));
        Type engine=T("CE32_console.NpV2OnlineClock");
        uint magic=U(engine.GetField("RequestMagic",All).GetValue(null));
        int reads=0,commits=0;
        string clockPhase="sync-clock-initialize";
        Func<byte[],Task<byte[]>> exchange=async request=>{
            current();
            if(request==null || request.Length!=64 || BitConverter.ToUInt32(request,0)!=magic)
                throw new IOException("Only actual GUI clock requests are admitted by sync.");
            uint op=BitConverter.ToUInt32(request,8),id=BitConverter.ToUInt32(request,12);
            if(op==0)reads++;
            else if(op==1 && commits==0 && reads==16)commits++;
            else throw new IOException("Unexpected clock operation/count; no mutation replay allowed.");
            LogClockBasis(clockPhase,"before");
            var elapsed=Stopwatch.StartNew();
            byte[] reply;
            try{reply=(byte[])await Await(Invoke(connection,"ExchangeAsync",(byte)0xb9,request,cancel));}
            catch(TimeoutException error){
                LogClockBasis(clockPhase,"after");
                throw new IOException("Sync receipt timed out; no retry or commit replay allowed.",error);
            }
            catch{
                LogClockBasis(clockPhase,"after");
                throw;
            }
            LogClockBasis(clockPhase,"after");
            current();
            object receipt=InvokeStatic(engine,"Parse",reply,op,id);
            if(U(Get(receipt,"State"))!=1U)throw new IOException("Recorder left Idle during synchronization.");
            Log("sync.clock; op="+op+"; id="+id+"; elapsed_ms="+elapsed.ElapsedMilliseconds+
                "; generation="+U(Get(receipt,"Generation"))+"; valid="+Get(receipt,"Valid")+
                "; uncertainty_us="+U(Get(receipt,"Uncertainty")));
            Log("sync.clock.request="+Hex(request));Log("sync.clock.reply="+Hex(reply));
            return reply;
        };
        var initialized=Stopwatch.StartNew();
        await Await(Invoke(clock,"Initialize",status,exchange,(Func<uint>)NextId));
        current();
        if(reads!=16 || commits!=1 || !(bool)Get(clock,"Synchronized") || U(Get(clock,"Generation"))==0)
            throw new IOException("Production GUI initialization did not confirm 16 observations and one commit.");
        uint generation=U(Get(clock,"Generation"));
        Log("sync.initialize; elapsed_ms="+initialized.ElapsedMilliseconds+"; read_op0="+reads+"; commit_op1="+commits+
            "; generation="+generation+"; bound_ms="+Get(clock,"BoundMilliseconds")+"; offset_ms="+Get(clock,"OffsetMilliseconds"));
        clockPhase="sync-clock-observe";
        await Await(Invoke(clock,"Observe",exchange,(Func<uint>)NextId));
        current();
        if(reads!=17 || commits!=1 || !(bool)Get(clock,"Synchronized") || U(Get(clock,"Generation"))!=generation)
            throw new IOException("Observe heartbeat changed generation or committed/replayed a clock.");
        object after=await SendV2(connection,0,0,0,cancel,"sync-after");
        current();RequireHealthy(after);
        if(U(Get(after,"State"))!=1U || (bool)Get(after,"Busy") ||
            new[]{"Mask","Hz","Chunks","Start","End","Capacity","LastId"}.Any(name=>U(Get(after,name))!=U(Get(status,name))))
            throw new IOException("Stopped recorder status changed during sync/heartbeat.");
        Log("sync.heartbeat=PASS; one read/no commit; generation="+generation+"; epoch="+epoch+
            "; bound_ms="+Get(clock,"BoundMilliseconds")+"; offset_ms="+Get(clock,"OffsetMilliseconds"));
    }

    static async Task<object> ReadDevice(object connection, CancellationToken cancel)
    {
        Func<byte, byte[], CancellationToken, Task<byte[]>> exchange = (command, request, token) =>
            (Task<byte[]>)Invoke(connection, "ExchangeAsync", command, request, token);
        object tuple = await Await(InvokeStatic(T("USB512_interface.NpBleConnection"), "ReadDevice", exchange, (Func<uint>)NextId));
        int version = (int)Get(tuple, "Item1");
        if (version != 2) throw new IOException("This runner accepts only V2 NP2 status protocol.");
        return Get(tuple, "Item2");
    }

    static async Task<object> ReadDeviceWithRecovery(object connection, CancellationToken cancel, string label)
    {
        try { return await ReadDevice(connection, cancel); }
        catch (TimeoutException error)
        {
            Log(label + ".readonly_timeout=" + error.Message + "; retrying one fresh discovery/STATUS only.");
            return await ReadDevice(connection, cancel);
        }
    }

    static async Task<object> ReadStatusWithRecovery(object connection, CancellationToken cancel, string label)
    {
        try { return await SendV2(connection, 0, 0, 0, cancel, label); }
        catch (TimeoutException error)
        {
            Log(label + ".readonly_timeout=" + error.Message + "; retrying one fresh STATUS only.");
            return await SendV2(connection, 0, 0, 0, cancel, label + "-retry");
        }
    }

    static void RequireRecordConnection(object connection,uint epoch)
    {
        if(!(bool)Get(connection,"Connected") || U(Get(connection,"Epoch"))!=epoch)
            throw new IOException("Recording clock connection epoch changed; no replay allowed.");
    }

    static async Task ObserveRecordingClock(object connection,object clock,Func<byte[],Task<byte[]>> exchange,Func<uint> next,uint epoch,uint generation)
    {
        RequireRecordConnection(connection,epoch);
        await Await(Invoke(clock,"Observe",exchange,next));
        RequireRecordConnection(connection,epoch);
        if(!(bool)Get(clock,"Synchronized") || generation==0U || U(Get(clock,"Generation"))!=generation)
            throw new IOException("Recording heartbeat lost the initial clock generation/quality.");
        Log("record-heartbeat.valid; epoch="+epoch+"; generation="+generation+
            "; bound_ms="+Get(clock,"BoundMilliseconds")+"; offset_ms="+Get(clock,"OffsetMilliseconds"));
    }

    static async Task<byte[]> ExchangeTimed(object connection,byte[] request,CancellationToken cancel,string label)
    {
        bool clockDiagnostic = label == "record-clock-initialize" || label == "record-heartbeat";
        if(clockDiagnostic) LogClockBasis(label, "before");
        // Start before reflection: ExchangeAsync may dispatch synchronously
        // before returning its already-running Task.
        var elapsed=Stopwatch.StartNew();
        byte[] reply;
        try {reply=(byte[])await Await(Invoke(connection,"ExchangeAsync",(byte)0xb9,request,cancel));}
        catch(Exception error){
            elapsed.Stop();
            if(clockDiagnostic) LogClockBasis(label, "after");
            Log(label+".elapsed_us="+(long)(elapsed.ElapsedTicks*1000000.0/Stopwatch.Frequency)+
                "; outcome=failed; type="+error.GetType().Name);
            Log(label+".request="+Hex(request));
            throw;
        }
        elapsed.Stop();
        if(clockDiagnostic) LogClockBasis(label, "after");
        Log(label+".elapsed_us="+(long)(elapsed.ElapsedTicks*1000000.0/Stopwatch.Frequency)+"; outcome=reply");
        Log(label+".request="+Hex(request));
        Log(label+".reply="+Hex(reply));
        return reply;
    }

    static void LogClockBasis(string label,string edge)
    {
        DateTime wall = DateTime.UtcNow;
        long elapsed = Stopwatch.GetTimestamp() - clockStopwatchAnchor;
        DateTime monotonic = clockWallAnchor.AddTicks((long)(elapsed * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        double deltaMilliseconds = (wall - monotonic).TotalMilliseconds;
        Log(label+".clock_"+edge+"; wall_utc="+wall.ToString("O",CultureInfo.InvariantCulture)+
            "; monotonic_utc="+monotonic.ToString("O",CultureInfo.InvariantCulture)+
            "; wall_minus_monotonic_ms="+deltaMilliseconds.ToString("F3",CultureInfo.InvariantCulture));
    }

    static async Task<object> SendV2(object connection, uint operation, uint hz, uint blocks, CancellationToken cancel, string label,uint mask=RequiredMask)
    {
        uint id = NextId();
        byte[] request = RecordRequest(T("CE32_console.NpV2SdControl"),operation,id,hz,blocks,mask);
        byte[] reply = await ExchangeTimed(connection,request,cancel,label);
        object status=InvokeStatic(T("CE32_console.NpV2SdControl"), "Parse", reply, operation, id);
        ObserveSequence(status);
        return status;
    }

    static object NewConfigClient(object connection)
    {
        Type transportType = T("USB512_interface.NpBleForm+ConfigTransport");
        object transport = New(transportType, connection);
        return New(T("USB512_interface.Np2ConfigClient"), transport);
    }

    static object PrepareRequest(string input, int connector, string output)
    {
        string path = Path.GetFullPath(input);
        if (!File.Exists(path)) throw new FileNotFoundException("Retained NP2 input was not found.", path);
        Type requestType = T("USB512_interface.Np2ConfigRequest");
        object request;
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length == 876 && raw[0] == (byte)'N' && raw[1] == (byte)'2' && raw[2] == (byte)'B' && raw[3] == (byte)'1')
        {
            if (raw[5] != connector - 1) throw new IOException("Raw NP2 selection has the wrong physical connector.");
            request = New(requestType, "{\"source\":\"retained-n2b1\",\"connector\":" + connector + "}", raw);
        }
        else
        {
            string json = File.ReadAllText(path);
            request = InvokeStatic(T("USB512_interface.NpConfigurationDialog"), "PrepareV2Request", json, connector);
        }
        byte[] saved = (byte[])Get(request, "Selection");
        File.WriteAllBytes(Path.Combine(output, "P" + connector + ".n2b1"), saved);
        Log("map-P" + connector + "=" + path + "; payload_crc=" + Crc32(saved).ToString("X8"));
        return request;
    }

    static void RequireCaps(object caps, int port)
    {
        uint available=U(Get(caps,"AvailableMask"));
        if ((available==0U || (available&~7U)!=0) || U(Get(caps, "SupportedModel")) != 4U ||
            U(Get(caps, "Uid0")) != 0x001A0004U || U(Get(caps, "Uid1")) != 0x43425006U || U(Get(caps, "Uid2")) != 0x2039314EU)
            throw new IOException("NPC2 CAPS does not identify the exact authorized MCU/closed NP2013 capability; no mutation was sent.");
        if ((U(Get(caps, "AvailableMask")) & (1U << port)) == 0) throw new IOException("NPC2 CAPS omitted required port " + port + ".");
    }

    static void RequireStartable(object status)
    {
        RequireHealthy(status);
        if (!RecordProfile(U(Get(status,"Mask")),U(Get(status,"Hz"))) || (bool)Get(status, "Busy") ||
            !(bool)InvokeStatic(T("CE32_console.NpV2SdControl"), "CanStart", status))
            throw new IOException("Recorder must have a fault-free closed NP2 profile in firmware-admitted Idle, Complete, or Cancelled state before mutations.");
    }

    static void RequireHealthy(object status)
    {
        if (U(Get(status, "Error")) != 0 || U(Get(status, "Fault")) != 0) throw new IOException("Recorder reports command error/fault.");
    }

    static void LogStatus(string label, object s)
    {
        Log(label + ": state=" + U(Get(s, "State")) + "; error=" + U(Get(s, "Error")) + "; fault=" + U(Get(s, "Fault")) +
            "; mask=" + U(Get(s, "Mask")) + "; hz=" + U(Get(s, "Hz")) + "; blocks=" + U(Get(s, "Chunks")) +
            "; capacity="+U(Get(s,"Capacity"))+"; start_lba="+U(Get(s,"Start"))+
            "; flags=0x" + U(Get(s, "Flags")).ToString("X8") + "; last_id=" + U(Get(s, "LastId")));
    }

    static void LogConnectDiagnostic(object connection,string label)
    {
        try { Log(label+".diagnostic="+Get(connection,"LastConnectDiagnostic")); }
        catch (MissingMemberException) { Log(label+".diagnostic=unavailable in this desktop assembly"); }
    }

    static void LogCaps(int port, object c)
    {
        Log("caps-P" + (port + 1) + ": mask=" + U(Get(c, "AvailableMask")) + "; model=" + U(Get(c, "SupportedModel")) +
            "; uid=" + U(Get(c, "Uid0")).ToString("X8") + U(Get(c, "Uid1")).ToString("X8") + U(Get(c, "Uid2")).ToString("X8"));
    }

    static void LogReceipt(string label, object r)
    {
        Log(label + ": error=" + U(Get(r, "Error")) + "; port=" + U(Get(r, "Port")) + "; model=" + U(Get(r, "Model")) +
            "; state=" + U(Get(r, "PortState")) + "; verification=" + U(Get(r, "Verification")) +
            "; crc=" + U(Get(r, "PayloadCrc")).ToString("X8") + "; id=" + Hex((byte[])Get(r, "ConfigurationId")));
    }

    static uint NextId()
    {
        sequence = unchecked(sequence + 1U);
        if (sequence == 0 || sequence == 1) sequence = 2;
        return sequence;
    }

    static void SeedSequence(object status, string label)
    {
        uint last=U(Get(status,"LastId"));
        // Zero is the genuine cold-boot "no prior mutation" value; Get throws
        // if the parsed contract lacks LastId. NextId skips reserved zero/one.
        sequence=last;sequenceSeeded=true;
        Log("id_seed="+label+"; last_id="+last);
    }

    static void ObserveSequence(object status)
    {
        if(!sequenceSeeded)return;
        uint last=U(Get(status,"LastId"));
        if(last!=0 && unchecked((int)(last-sequence))>0){sequence=last;Log("id_advanced_from_status="+last);}
    }

    static uint NewSequence()
    {
        byte[] random = Guid.NewGuid().ToByteArray();
        uint id = BitConverter.ToUInt32(random, 0);
        return id < 2 ? 2U : id;
    }

    static uint Positive(string text, string label)
    {
        uint value;
        if (!UInt32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value == 0)
            throw new ArgumentException("Positive " + label + " required.");
        return value;
    }

    static Type T(string name)
    {
        Type type = app.GetType(name, false);
        if (type == null) throw new MissingMemberException("Missing expected WILDX type " + name + ".");
        return type;
    }

    static object New(Type type, params object[] args)
    {
        ConstructorInfo ctor = type.GetConstructors(All & ~BindingFlags.Static).SingleOrDefault(c => Matches(c.GetParameters(), args));
        if (ctor == null) throw new MissingMethodException(type.FullName, ".ctor");
        return ctor.Invoke(args);
    }

    static object Invoke(object target, string name, params object[] args)
    {
        return Call(target.GetType(), target, name, args);
    }

    static object InvokeStatic(Type type, string name, params object[] args)
    {
        return Call(type, null, name, args);
    }

    static object Call(Type type, object target, string name, object[] args)
    {
        MethodInfo method = type.GetMethods(All).SingleOrDefault(m => m.Name == name && m.IsStatic == (target == null) && Matches(m.GetParameters(), args));
        if (method == null) throw new MissingMethodException(type.FullName, name);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    static bool Matches(ParameterInfo[] parameters, object[] args)
    {
        if (parameters.Length != args.Length) return false;
        for (int i = 0; i < args.Length; i++)
            if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
        return true;
    }

    static async Task<object> Await(object task)
    {
        Task actual = task as Task;
        if (actual == null) throw new ArgumentException("Expected Task from production API.");
        await actual;
        PropertyInfo result = actual.GetType().GetProperty("Result", All);
        return result == null ? null : result.GetValue(actual, null);
    }

    static object Get(object value, string member)
    {
        if (value == null) throw new IOException("Missing required production value " + member + ".");
        Type type = value.GetType();
        FieldInfo field = type.GetField(member, All);
        if (field != null) return field.GetValue(value);
        PropertyInfo property = type.GetProperty(member, All);
        if (property != null) return property.GetValue(value, null);
        throw new MissingMemberException(type.FullName, member);
    }

    static uint U(object value) { return Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
    static string Hex(byte[] bytes) { return bytes == null ? "" : BitConverter.ToString(bytes).Replace("-", ""); }
    static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return Hex(sha.ComputeHash(stream)); }
    static uint Crc32(byte[] bytes) { return (uint)InvokeStatic(T("USB512_interface.Np2ConfigClient"), "Crc", bytes, bytes.Length); }
    static void Log(string text) { log.WriteLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + text); log.Flush(); Console.WriteLine(text); }

    static Assembly ResolveSibling(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string candidate = Path.Combine(assemblyDirectory ?? AppDomain.CurrentDomain.BaseDirectory, name);
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
