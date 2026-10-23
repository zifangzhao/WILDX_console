using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;
using WildNpConfiguration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace USB512_interface
{
    internal sealed partial class NpBleConnection : IDisposable
    {
        static readonly Guid ServiceId = new Guid("0000fff0-0000-1000-8000-00805f9b34fb");
        static readonly Guid RxId = new Guid("0000fff1-0000-1000-8000-00805f9b34fb");
        static readonly Guid TxId = new Guid("0000fff2-0000-1000-8000-00805f9b34fb");
        readonly SemaphoreSlim transaction = new SemaphoreSlim(1, 1);
        readonly object receiveLock = new object();
        readonly object clockJournalLock = new object();
        NpClockJournal clockJournal;
        int clockJournalEpoch;
        string clockJournalFolder;
        readonly NpBleWire wire = new NpBleWire();
        BluetoothLEDevice device;
        ulong lastObservedAddress;
        BluetoothAddressType lastObservedType=BluetoothAddressType.Unspecified;
        bool lastResolvedWarm;
        GattDeviceService service;
        GattCharacteristic rx, tx;
        TaskCompletionSource<byte[]> receipt;
        Func<byte, byte[], bool> matches;
        int epoch;
        volatile bool connectCanceled;
        CancellationTokenSource connectTimeout; // Owned by ConnectAsync; guarded by receiveLock.
        readonly object connectTraceLock=new object();
        string lastConnectDiagnostic="No connection attempt yet.";
        internal event Action<string> Disconnected;
        internal event Action<int,byte[]> PreviewReceived;
        internal int Epoch { get { return Volatile.Read(ref epoch); } }
        internal bool Connected { get { return device != null && tx != null && device.ConnectionStatus == BluetoothConnectionStatus.Connected; } }
        internal string LastConnectDiagnostic { get { lock(connectTraceLock)return lastConnectDiagnostic; } }
        internal string ClockJournalFolder { get { lock(clockJournalLock)return clockJournalFolder; } }

        internal static bool IsReadOnlyClockRequest(byte command,byte[] request)
        {
            return command==0xb9 && request!=null && request.Length==64 &&
                BitConverter.ToUInt32(request,0)==NpV2OnlineClock.RequestMagic && BitConverter.ToUInt32(request,4)==1 &&
                BitConverter.ToUInt32(request,8)==0 && BitConverter.ToUInt32(request,12)!=0 &&
                BitConverter.ToUInt32(request,60)==NpSdControl.Crc(request,60);
        }
        internal static bool ReadOnlyClockObservation(NpClockJournal journal,byte command,byte[] request,byte[] response,long start,long end)
        {
            try {
                if(journal==null || !IsReadOnlyClockRequest(command,request))return false;
                var reply=NpV2OnlineClock.Parse(response,0,BitConverter.ToUInt32(request,12));
                long uncertainty;long midpoint=NpV2OnlineClock.MidpointUtc(start,end,reply.Rx,reply.Tx,out uncertainty);
                journal.Add(reply.Generation,reply.Tx,midpoint,uncertainty,"ble_read_only_clock_tx_construction");
                return journal.Error==null;
            }catch{return false;} // Timing evidence must never change command success or recording.
        }
        NpClockJournal ClockJournal(int generation)
        {
            try { lock(clockJournalLock) {
                if(clockJournal==null) {
                    if(device==null || generation!=Epoch)return null;
                    string address=device.BluetoothAddress.ToString("X12");
                    string session=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N");
                    clockJournalFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WILDX","Sync","BLE-"+address,session);
                    // Transport address and connection epoch only; never fabricate an MCU UID.
                    clockJournal=new NpClockJournal(clockJournalFolder,"BLE-"+address,"connection-"+generation+"-"+session);
                    clockJournalEpoch=generation;
                }
                return clockJournalEpoch==generation?clockJournal:null;
            }}catch{return null;}
        }
        void CloseClockJournal()
        {
            NpClockJournal retired;
            lock(clockJournalLock){retired=clockJournal;clockJournal=null;}
            // Close/fit away from receive/control callbacks; no recording backpressure.
            if(retired!=null)try {Task.Run(delegate {try{retired.Dispose();}catch{}});}catch{}
        }

        void ResetConnectDiagnostic(ulong address){lock(connectTraceLock)lastConnectDiagnostic="address="+address.ToString("X12")+"; 0ms begin";}
        void TraceConnect(Stopwatch elapsed,string stage){lock(connectTraceLock)lastConnectDiagnostic+=" | "+elapsed.ElapsedMilliseconds+"ms "+stage;}

        static async Task<BluetoothLEDevice> TryOpenAddress(ulong address,BluetoothAddressType? type,CancellationToken cancel)
        {
            try {
                return type.HasValue
                    ? await BluetoothLEDevice.FromBluetoothAddressAsync(address,type.Value).AsTask(cancel)
                    : await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancel);
            } catch (OperationCanceledException) { throw; }
            catch { return null; } // Same-address cache/type probe only; never pairing or peer selection.
        }

        static async Task<BluetoothLEDevice> TryWarmAddress(ulong address,BluetoothAddressType? type,CancellationToken cancel,Action<string> stage)
        {
            using(var warm=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                warm.CancelAfter(250);
                BluetoothLEDevice device;
                try { device=await TryOpenAddress(address,type,warm.Token); }
                catch(OperationCanceledException) {
                    if(cancel.IsCancellationRequested)throw;
                    stage("selected-address cache reuse expired");return null;
                }
                if(device==null){stage("selected-address cache reuse null");return null;}
                if(device.BluetoothAddress!=address){
                    stage("selected-address cache reuse mismatched; disposed");device.Dispose();return null;
                }
                if(type.HasValue){
                    stage(device.ConnectionStatus==BluetoothConnectionStatus.Connected
                        ? "connected selected-address cache reuse" : "selected-address retained-type reuse; connection pending");
                    return device;
                }
                if(device.ConnectionStatus==BluetoothConnectionStatus.Connected){
                    stage("connected selected-address cache reuse");return device;
                }
                stage("selected-address cache reuse disconnected/mismatched; disposed");device.Dispose();return null;
            }
        }

        async Task<BluetoothLEDevice> ResolveAddress(ulong address,CancellationToken cancel,Action<string> stage,bool allowWarm=true)
        {
            // Prefer the current advertisement's address type. If it is missed,
            // only read-only routes for this exact selected address are tried.
            lastResolvedWarm=false;
            BluetoothAddressType? warmType=lastObservedAddress==address && lastObservedType!=BluetoothAddressType.Unspecified
                ? (BluetoothAddressType?)lastObservedType : null;
            BluetoothLEDevice device=null;
            if(allowWarm){
                device=await TryWarmAddress(address,warmType,cancel,stage);
                if(device!=null){lastResolvedWarm=true;return device;}
            }
            var watcher=new BluetoothLEAdvertisementWatcher{ScanningMode=BluetoothLEScanningMode.Active};
            var seen=new TaskCompletionSource<BluetoothAddressType>(TaskCreationOptions.RunContinuationsAsynchronously);
            TypedEventHandler<BluetoothLEAdvertisementWatcher,BluetoothLEAdvertisementReceivedEventArgs> receive=
                (sender,args)=>{if(args.BluetoothAddress==address)seen.TrySetResult(args.BluetoothAddressType);};
            object token=watcher.GetType().GetMethod("add_Received").Invoke(watcher,new object[]{receive});
            BluetoothAddressType type=BluetoothAddressType.Unspecified;
            try {
                stage("advertisement watcher started");
                watcher.Start();
                Task first=await Task.WhenAny(seen.Task,Task.Delay(30000,cancel));
                cancel.ThrowIfCancellationRequested();
                if(first==seen.Task){
                    type=await seen.Task;
                    lastObservedAddress=address;
                    lastObservedType=type;
                    stage("advertisement observed; observed_type="+type);
                }
                else stage("advertisement wait elapsed; observed_type=Unspecified");
            } finally {
                watcher.Stop();watcher.GetType().GetMethod("remove_Received").Invoke(watcher,new[]{token});
            }
            stage("selected-address opens (observed type="+type+")");
            if(type!=BluetoothAddressType.Unspecified && (device=await TryOpenAddress(address,type,cancel))!=null)return device;
            if((device=await TryOpenAddress(address,null,cancel))!=null)return device;
            if(type==BluetoothAddressType.Unspecified){
                if((device=await TryOpenAddress(address,BluetoothAddressType.Random,cancel))!=null)return device;
                if((device=await TryOpenAddress(address,BluetoothAddressType.Public,cancel))!=null)return device;
            }
            string selector=type==BluetoothAddressType.Unspecified
                ? BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(address)
                : BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(address,type);
            stage("selected-address endpoint lookup (observed type="+type+")");
            var peers=await DeviceInformation.FindAllAsync(selector,null,DeviceInformationKind.AssociationEndpoint).AsTask(cancel);
            foreach(var peer in peers){
                device=await BluetoothLEDevice.FromIdAsync(peer.Id).AsTask(cancel);
                if(device==null)continue;
                if(device.BluetoothAddress==address)return device;
                device.Dispose();
            }
            return null;
        }

        internal Task ConnectAsync(ulong address) { return ConnectAsync(address,null); }
        internal async Task ConnectAsync(ulong address,BluetoothAddressType? observedType)
        {
            await transaction.WaitAsync();
            string stage="advertisement/address discovery";
            var elapsed=Stopwatch.StartNew();ResetConnectDiagnostic(address);
            try {
                if(observedType.HasValue && observedType.Value!=BluetoothAddressType.Unspecified){
                    lastObservedAddress=address;lastObservedType=observedType.Value;
                }
                lock(receiveLock){Disconnect();connectCanceled=false;}
                var timeout = new CancellationTokenSource(35000);
                lock(receiveLock){connectTimeout=timeout;if(connectCanceled)timeout.Cancel();}
                try {
                    var resolved = await ResolveAddress(address,timeout.Token,value=>{stage=value;TraceConnect(elapsed,value);});
                    lock(receiveLock){
                        if(connectCanceled){if(resolved!=null)resolved.Dispose();throw new OperationCanceledException("Connection attempt was closed.");}
                        device=resolved;
                    }
                    if (device == null) throw new IOException("Windows could not open the selected BLE address.");
                    bool warm=lastResolvedWarm,freshFallback=false;
                    for(;;){
                        // Slow advertising must not consume the GATT connection's
                        // whole budget. Each phase is bounded; no writes replayed.
                        timeout.CancelAfter(20000);
                        device.ConnectionStatusChanged += ConnectionChanged;
                        bool admitted=false;
                        Exception discoveryError=null;
                        try{
                            // Cached can stall on a disconnected Windows record.
                            // Use it only for a connected retained peer; fresh or
                            // pending links begin with the same bounded refresh.
                            int firstCacheAttempt=warm && device.ConnectionStatus==BluetoothConnectionStatus.Connected?0:1;
                            for(int cacheAttempt=firstCacheAttempt;cacheAttempt<2;cacheAttempt++){
                                var cacheMode=cacheAttempt==0?BluetoothCacheMode.Cached:BluetoothCacheMode.Uncached;
                                GattDeviceServicesResult services=null;
                                try{
                                    stage="FFF0 service discovery; cache_mode="+cacheMode;
                                    TraceConnect(elapsed,stage);
                                    services=await device.GetGattServicesForUuidAsync(ServiceId,cacheMode).AsTask(timeout.Token);
                                    TraceConnect(elapsed,"FFF0 service discovery result="+services.Status+"; cache_mode="+cacheMode+"; attempt=1");
                                    // Preserve the bounded discovery-only retry
                                    // on the refresh, never replay a command.
                                    for(int attempt=1;cacheAttempt!=0 && attempt<3 && services.Status==GattCommunicationStatus.Unreachable;attempt++){
                                        foreach(var candidate in services.Services)candidate.Dispose();services=null;
                                        await Task.Delay(250,timeout.Token);
                                        services=await device.GetGattServicesForUuidAsync(ServiceId,cacheMode).AsTask(timeout.Token);
                                        TraceConnect(elapsed,"FFF0 service discovery result="+services.Status+"; cache_mode="+cacheMode+"; attempt="+(attempt+1));
                                    }
                                    if(services.Status!=GattCommunicationStatus.Success || services.Services.Count(c=>c.Uuid==ServiceId)!=1)
                                        throw new IOException("Selected device does not expose one WILD FFF0 control service: "+services.Status);
                                    var selectedService=services.Services.Single(c=>c.Uuid==ServiceId);
                                    stage="FFF1/FFF2 characteristic discovery; cache_mode=Uncached";
                                    TraceConnect(elapsed,stage);
                                    var characteristics=await selectedService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                                    if(characteristics.Status!=GattCommunicationStatus.Success)throw new IOException("GATT discovery failed: "+characteristics.Status);
                                    var selectedRx=characteristics.Characteristics.SingleOrDefault(c=>c.Uuid==RxId);
                                    var selectedTx=characteristics.Characteristics.SingleOrDefault(c=>c.Uuid==TxId);
                                    if(selectedRx==null || selectedTx==null)throw new IOException("FFF1 notifications and FFF2 writes are required.");
                                    if((selectedRx.CharacteristicProperties & (GattCharacteristicProperties.Notify|GattCharacteristicProperties.Indicate))==0)
                                        throw new IOException("FFF1 does not support notifications or indications.");
                                    if((selectedTx.CharacteristicProperties & (GattCharacteristicProperties.Write|GattCharacteristicProperties.WriteWithoutResponse))==0)
                                        throw new IOException("FFF2 is not writable.");
                                    lock(receiveLock){
                                        if(connectCanceled)throw new OperationCanceledException("Connection attempt was closed.");
                                        service=selectedService;rx=selectedRx;tx=selectedTx;
                                        foreach(var candidate in services.Services)if(candidate!=selectedService)candidate.Dispose();
                                        services=null; // Ownership moves to Disconnect only after fresh admission.
                                    }
                                    admitted=true;break;
                                }catch(OperationCanceledException){throw;}
                                catch(Exception error){
                                    if(connectCanceled || timeout.IsCancellationRequested)throw;
                                    discoveryError=error;
                                    if(cacheAttempt!=0)break;
                                    TraceConnect(elapsed,"Cached FFF0/fresh characteristic admission failed; one Uncached refresh: "+error.Message);
                                }finally{if(services!=null)foreach(var candidate in services.Services)candidate.Dispose();}
                            }
                        }catch(OperationCanceledException){
                            // Only this warm phase's deadline permits the same
                            // one-shot fresh fallback. Closing/canceling the
                            // operation itself must remain terminal.
                            if(!warm || freshFallback || !timeout.IsCancellationRequested || connectCanceled)throw;
                            TraceConnect(elapsed,"retained-type FFF0 phase deadline expired");
                        }
                        if(connectCanceled)throw new OperationCanceledException("Connection attempt was closed.");
                        if(admitted)break;
                        if(!warm || freshFallback)throw new IOException("Selected device failed FFF0/fresh characteristic admission.",discoveryError);
                        TraceConnect(elapsed,"retained-type discovery failed; fresh exact-peer observation fallback");
                        lock(receiveLock){
                            if(connectCanceled)throw new OperationCanceledException("Connection attempt was closed.");
                            Disconnect();connectCanceled=false;
                        }
                        freshFallback=true;warm=false;
                        lock(receiveLock){
                            timeout.Dispose();timeout=new CancellationTokenSource(35000);
                            connectTimeout=timeout;if(connectCanceled)timeout.Cancel();
                        }
                        resolved=await ResolveAddress(address,timeout.Token,value=>{stage=value;TraceConnect(elapsed,value);},false);
                        lock(receiveLock){
                            if(connectCanceled){if(resolved!=null)resolved.Dispose();throw new OperationCanceledException("Connection attempt was closed.");}
                            device=resolved;
                        }
                        if(device==null)throw new IOException("Windows could not open the selected BLE address after retained-type discovery failed.");
                    }
                    var properties = rx.CharacteristicProperties;
                    var subscription = (properties & GattCharacteristicProperties.Notify) != 0 ? GattClientCharacteristicConfigurationDescriptorValue.Notify :
                        (properties & GattCharacteristicProperties.Indicate) != 0 ? GattClientCharacteristicConfigurationDescriptorValue.Indicate : GattClientCharacteristicConfigurationDescriptorValue.None;
                    lock(receiveLock){
                        if(connectCanceled || connectTimeout!=timeout)throw new OperationCanceledException("Connection attempt was closed.");
                        timeout.Token.ThrowIfCancellationRequested(); // Never revive an expired discovery or explicit close.
                        timeout.CancelAfter(5000); // Fresh bounded CCCD + settle phase, not a discovery retry.
                    }
                    rx.ValueChanged += ValueChanged;
                    stage="notification subscription";
                    TraceConnect(elapsed,stage);
                    var result = await rx.WriteClientCharacteristicConfigurationDescriptorAsync(subscription).AsTask(timeout.Token);
                    if (result != GattCommunicationStatus.Success) throw new IOException("Notification subscription failed: " + result);
                    // The BLE/UART bridge needs a brief subscription settle time.
                    await Task.Delay(250, timeout.Token);
                    if (!Connected) throw new IOException("Selected device disconnected during discovery.");
                    TraceConnect(elapsed,"connected");
                }
                finally { lock(receiveLock){if(connectTimeout==timeout)connectTimeout=null;timeout.Dispose();} }
            }
            catch (OperationCanceledException error) {
                bool closed=connectCanceled;TraceConnect(elapsed,(closed?"canceled; ":"timeout; ")+stage);
                Disconnect();throw new IOException(closed?"BLE connection attempt canceled. No recorder command was sent.":"BLE timed out during "+stage+". No recorder command was sent.",error);
            }
            catch (Exception error) { TraceConnect(elapsed,"failed; "+stage+"; "+error.GetType().Name);Disconnect(); throw; }
            finally { transaction.Release(); }
        }

        void ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] bytes;
            using (var reader = DataReader.FromBuffer(args.CharacteristicValue)) { bytes = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(bytes); }
            System.Collections.Generic.List<Tuple<byte,byte[]>> frames;int receivedEpoch;
            lock (receiveLock) {
                if (sender != rx) return;
                receivedEpoch=epoch;frames=wire.Feed(bytes);
                foreach (var frame in frames)
                    if (receipt != null && matches != null && matches(frame.Item1, frame.Item2)) receipt.TrySetResult(frame.Item2);
            }
            foreach(var frame in frames)if(frame.Item1==0xba){var handler=PreviewReceived;if(handler!=null)handler(receivedEpoch,frame.Item2);}
        }
        void ConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender != device || sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
            lock (receiveLock) { epoch++; wire.Clear(); if (receipt != null) receipt.TrySetException(new IOException("BLE disconnected; command outcome may be unknown. Reconnect and read status.")); }
            CloseClockJournal();
            var handler = Disconnected; if (handler != null) handler("BLE disconnected. Reconnect and read status; recording was not stopped.");
        }
        internal static bool Matches(byte command, byte[] request, byte receivedCommand, byte[] response)
        {
            if(command==NpFirmwareIdentity.BleCommand)return request!=null && request.Length==0 && receivedCommand==command && response!=null &&
                response.Length==NpFirmwareIdentity.WireBytes && BitConverter.ToUInt32(response,188)==NpSdControl.Crc(response,188);
            if (response==null || response.Length!=64 || BitConverter.ToUInt32(response,60)!=NpSdControl.Crc(response,60))return false;
            if(command==0x82){
                if(!DiscoveryRequest(request) || receivedCommand!=0xb9)return false;
                try { DeviceVersion(response,1);return true; }catch(IOException){return false;}
            }
            if (command != receivedCommand || request==null || request.Length!=64) return false;
            if(command==0xb9 && NpV1MediaStatus.IsFrame(request)) {
                try { NpV1MediaStatus.Parse(response);return true; }catch(IOException){return false;}
            }
            uint magic = BitConverter.ToUInt32(request, 0);
            if (command == 0xb8) {
                try {
                    uint transaction=BitConverter.ToUInt32(request,8);
                    if (magic == 0x3243504eU) {
                        var configReply=Np2ConfigClient.Parse(response,request[5],transaction);
                        return request[5] != 2 || configReply.Error != 0 || configReply.Received == BitConverter.ToUInt32(request,12) + BitConverter.ToUInt32(request,16);
                    }
                    var legacyReply=NpConfigClient.Parse(response,request[5],transaction);
                    return request[5] != 2 || legacyReply.Error != 0 || legacyReply.Received == BitConverter.ToUInt32(request,12) + BitConverter.ToUInt32(request,16);
                } catch(IOException) { return false; }
            }
            uint expected = magic == 0x31444c4e ? 0x31524c4eU : magic == NpV2SdControl.RequestMagic ? NpV2SdControl.ReplyMagic :
                magic == NpV2OnlineClock.RequestMagic ? NpV2OnlineClock.ReplyMagic : magic;
            // FORMAT2 opts into a V1 filesystem; its status receipt ABI stays1.
            uint replyVersion=command==0xb9&&NpV1BleProtocol.IsStoreV2FormatRequest(request)?1U:BitConverter.ToUInt32(request,4);
            return BitConverter.ToUInt32(response, 0) == expected && BitConverter.ToUInt32(response, 4) == replyVersion &&
                BitConverter.ToUInt32(response, 8) == BitConverter.ToUInt32(request, 8) && BitConverter.ToUInt32(response, 12) == BitConverter.ToUInt32(request, 12);
        }
        internal static int DeviceVersion(byte[] body,uint id)
        {
            if(body==null || body.Length!=64)throw new IOException("Invalid device status.");
            uint magic=NpSdGeometry.U(body,0);
            int version=magic==0x31524c4e?1:magic==NpV2SdControl.ReplyMagic?2:0;
            if(version==0)throw new IOException("Unsupported device control protocol.");
            ParseStatus(body,version,0,id);return version;
        }
        internal static NpSdStatus ParseStatus(byte[] body,int version,uint op,uint id)
        {
            if(version!=1 && version!=2)throw new IOException("Unsupported device control protocol.");
            var s=version==2?NpV2SdControl.Parse(body,op,id):NpSdControl.Parse(body,op,id);
            if(s.Error!=0)throw new NpSdCommandException(version,s,
                version==2 && op==2 && s.Error==8?NpV2SdControl.StartStorageError(s):null);
            return s;
        }
        internal static async Task<Tuple<int,NpSdStatus>> ReadDevice(Func<byte,byte[],CancellationToken,Task<byte[]>> exchange,Func<uint> next)
        {
            // Both firmware generations answer this read-only handshake with
            // their own status ABI. Confirm it with a fresh, correlated STATUS.
            int version=DeviceVersion(await exchange(0x82,new byte[]{0x81,0,0},CancellationToken.None),1);
            uint id=next();if(id==1)id=next();
            if(id==0 || id==1)throw new IOException("Fresh status transaction required.");
            var request=version==2?NpV2SdControl.Request(0,id):NpSdControl.Request(0,id);
            var status=ParseStatus(await exchange(0xb9,request,CancellationToken.None),version,0,id);
            return Tuple.Create(version,status);
        }
        internal async Task<byte[]> ExchangeAsync(byte command, byte[] request, CancellationToken cancel)
        {
            byte[] frame;
            if(command==0x82){
                if(!DiscoveryRequest(request))throw new ArgumentException("Read-only discovery request required.");
                frame=new byte[]{0x3c,0x82,0x81,0,0,0x3e};
            }else if(command==NpFirmwareIdentity.BleCommand){
                if(request==null || request.Length!=0)throw new ArgumentException("Firmware identity is a read-only short request.");
                frame=new byte[]{0x3c,command,0x3e};
            }else frame=NpBleWire.Frame(command, request);
            await transaction.WaitAsync(cancel);
            TaskCompletionSource<byte[]> waiting = null;
            try {
                if (!Connected) throw new IOException("Connect the selected BLE device before sending a command.");
                int generation;
                lock (receiveLock) {
                    generation = epoch;
                    waiting = receipt = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    matches = (id, body) => Matches(command, request, id, body);
                }
                NpClockJournal observationJournal=IsReadOnlyClockRequest(command,request)?ClockJournal(generation):null;
                long clockStart=observationJournal==null?0:observationJournal.NowMicroseconds;
                await WriteFrame(frame,generation,cancel);
                if (await Task.WhenAny(waiting.Task, Task.Delay(5000, cancel)) != waiting.Task) {
                    cancel.ThrowIfCancellationRequested();
                    if(!Connected || Epoch!=generation)throw new IOException("Connection changed while waiting for the firmware receipt.");
                    throw MissingReceipt(command,request);
                }
                byte[] response = await waiting.Task;
                long clockEnd=observationJournal==null?0:observationJournal.NowMicroseconds;
                if (!Connected || epoch != generation) throw new IOException("Connection changed; the old receipt was discarded.");
                lock(clockJournalLock)if(clockJournal==observationJournal && clockJournalEpoch==generation)
                    ReadOnlyClockObservation(observationJournal,command,request,response,clockStart,clockEnd);
                return response;
            }
            catch {if(command==NpFirmwareIdentity.BleCommand)Disconnect();throw;}
            finally {
                lock (receiveLock) { if (receipt == waiting) { receipt = null; matches = null; } }
                transaction.Release();
            }
        }
        internal static TimeoutException MissingReceipt(byte command,byte[] request)
        {
            // Called only after WriteFrame completed. A partial/failed write
            // never acquires this read-only continuation contract.
            if(command==0xb9 && request!=null && request.Length==64 && BitConverter.ToUInt32(request,0)==0x31444c4eU &&
                BitConverter.ToUInt32(request,4)==1 && BitConverter.ToUInt32(request,8)==0 && BitConverter.ToUInt32(request,12)!=0 &&
                BitConverter.ToUInt32(request,60)==NpSdControl.Crc(request,60))
                return new NpV1ReceiptTimeout(0,BitConverter.ToUInt32(request,12));
            return new TimeoutException("No matching firmware receipt. Outcome unknown; read status before retrying. No command replayed.");
        }
        internal static int WriteChunkBytes(byte[] frame,int maxPdu,bool acknowledged)
        {
            if(!acknowledged || maxPdu<70 || frame==null || frame.Length!=67 ||
                frame[0]!=0x3c || frame[1]!=0xb9 || frame[66]!=0x3e)return 20;
            uint magic=BitConverter.ToUInt32(frame,2);
            return magic==NpV2SdControl.RequestMagic || magic==NpV2OnlineClock.RequestMagic?67:20;
        }
        async Task WriteFrame(byte[] frame,int generation,CancellationToken cancel)
        {
            if(!Connected || Epoch!=generation)throw new IOException("BLE connection changed; no command sent.");
            var transmitter=tx;
            var activeService=service;
            var session=activeService==null?null:activeService.Session;
            int chunkBytes=WriteChunkBytes(frame,session==null?0:session.MaxPduSize,
                (transmitter.CharacteristicProperties & GattCharacteristicProperties.Write)!=0);
            // Restore the already-validated whole NP2 control frame. Borrow
            // session information only; no MTU request, retry or recorder reset.
            bool fast=chunkBytes==20 && (transmitter.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse)!=0;
            var option=fast?GattWriteOption.WriteWithoutResponse:GattWriteOption.WriteWithResponse;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                timeout.CancelAfter(5000);
                for(int at=0;at<frame.Length;at+=chunkBytes){
                    timeout.Token.ThrowIfCancellationRequested();
                    if(!Connected || Epoch!=generation)throw new IOException("Connection changed during command delivery; no fragments replayed.");
                    var part=frame.Skip(at).Take(Math.Min(chunkBytes,frame.Length-at)).ToArray();
                    var result=await transmitter.WriteValueAsync(part.AsBuffer(),option).AsTask(timeout.Token);
                    if(result!=GattCommunicationStatus.Success)throw new IOException("BLE write failed: "+result+"; outcome unknown, no retry sent.");
                    if(fast && at+part.Length<frame.Length)await Task.Delay(3,timeout.Token);
                }
            }
            if(!Connected || Epoch!=generation)throw new IOException("Connection changed during delivery; outcome unknown.");
        }
        internal async Task<bool> TryWritePreview(byte[] request,int expectedEpoch,CancellationToken cancel)
        {
            if(request==null || request.Length!=64 || (BitConverter.ToUInt32(request,0)!=NpV2PreviewBurst.Magic && BitConverter.ToUInt32(request,0)!=NpV1PreviewBurst.Magic) ||
                BitConverter.ToUInt32(request,4)!=1 || BitConverter.ToUInt32(request,8)>(BitConverter.ToUInt32(request,0)==NpV2PreviewBurst.Magic?5U:4U) || BitConverter.ToUInt32(request,12)==0 ||
                BitConverter.ToUInt32(request,60)!=NpSdControl.Crc(request,60))throw new ArgumentException("Valid preview request required.");
            if(!await transaction.WaitAsync(0,cancel))return false; // Recording/status/clock controls take priority.
            try{await WriteFrame(NpBleWire.Frame(0xb9,request),expectedEpoch,cancel);return true;}
            finally{transaction.Release();}
        }
        internal async Task RescueReset(CancellationToken cancel)
        {
            await transaction.WaitAsync(cancel);
            try {
                // A previous incomplete UART command must age out (500 ms)
                // before the existing CE64 boundary-only rescue frame arrives.
                int generation=Epoch;
                await Task.Delay(600,cancel);
                await WriteFrame(new byte[]{0x3c,0xab,0xba,0x3e},generation,cancel);
                // Rescue has no ACK. Never replay it or infer a successful boot.
            }
            finally { Disconnect();transaction.Release(); }
        }
        internal async Task<NpV2PreviewBurst> CapturePreview(uint id,uint probe,uint channel,uint hz,CancellationToken cancel)
        {
            int expectedEpoch=Epoch;var burst=new NpV2PreviewBurst(id);
            var done=new TaskCompletionSource<NpV2PreviewBurst>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<int,byte[]> receive=(generation,body)=>{
                if(generation!=expectedEpoch)return;
                try{if(burst.Accept(body)){
                    if(burst.Kind!=0 || burst.Probe!=probe || burst.Channel!=channel || burst.Hz!=hz)throw new IOException("Preview selection/rate changed; discard.");
                    done.TrySetResult(burst);
                }}catch(Exception ex){done.TrySetException(ex);}
            };
            PreviewReceived+=receive;
            try{
                if(!await TryWritePreview(NpV2PreviewBurst.Request(id,0,probe,channel,0,0),expectedEpoch,cancel))throw new NpV2PreviewUnavailableException(129); // Confirmed not submitted; safe to defer this display request.
                if(await Task.WhenAny(done.Task,Task.Delay(2000,cancel))!=done.Task){cancel.ThrowIfCancellationRequested();throw new NpV2PreviewUnavailableException(128);}
                var complete=await done.Task;
                if(!Connected || Epoch!=expectedEpoch)throw new IOException("Preview connection changed; discard.");
                return complete;
            }finally{PreviewReceived-=receive;}
        }
        internal Task<byte[]> ExchangeAsync(byte[] request) { return ExchangeAsync(0xb9, request, CancellationToken.None); }
        static bool DiscoveryRequest(byte[] request){return request!=null && request.Length==3 && request[0]==0x81 && request[1]==0 && request[2]==0;}
        internal void Disconnect()
        {
            lock (receiveLock) {
                connectCanceled=true;
                // Request cancellation of the pending wait, not a recorder
                // reset. Windows may still finish its underlying radio attempt.
                if(connectTimeout!=null)connectTimeout.Cancel();
                epoch++; wire.Clear();
                CloseClockJournal();
                if (receipt != null) receipt.TrySetException(new IOException("BLE connection closed; pending receipt invalidated."));
                if (rx != null) rx.ValueChanged -= ValueChanged;
                rx = null; tx = null;
                if (device != null) device.ConnectionStatusChanged -= ConnectionChanged;
                if (service != null) service.Dispose(); service = null;
                if (device != null) device.Dispose(); device = null;
            }
        }
        public void Dispose() { Disconnect(); }
    }
}
