using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;
using WildNpConfiguration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace USB512_interface
{
    internal sealed class NpBleConnection : IDisposable
    {
        static readonly Guid ServiceId = new Guid("0000fff0-0000-1000-8000-00805f9b34fb");
        static readonly Guid RxId = new Guid("0000fff1-0000-1000-8000-00805f9b34fb");
        static readonly Guid TxId = new Guid("0000fff2-0000-1000-8000-00805f9b34fb");
        readonly SemaphoreSlim transaction = new SemaphoreSlim(1, 1);
        readonly object receiveLock = new object();
        readonly NpBleWire wire = new NpBleWire();
        BluetoothLEDevice device;
        GattDeviceService service;
        GattCharacteristic rx, tx;
        TaskCompletionSource<byte[]> receipt;
        Func<byte, byte[], bool> matches;
        int epoch;
        internal event Action<string> Disconnected;
        internal event Action<int,byte[]> PreviewReceived;
        internal int Epoch { get { return Volatile.Read(ref epoch); } }
        internal bool Connected { get { return device != null && tx != null && device.ConnectionStatus == BluetoothConnectionStatus.Connected; } }

        internal async Task ConnectAsync(ulong address)
        {
            await transaction.WaitAsync();
            try {
                Disconnect();
                using (var timeout = new CancellationTokenSource(20000)) {
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(timeout.Token);
                    if (device == null) throw new IOException("Windows could not open the selected BLE address.");
                    device.ConnectionStatusChanged += ConnectionChanged;
                    var services = await device.GetGattServicesForUuidAsync(ServiceId, BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                    if (services.Status != GattCommunicationStatus.Success || services.Services.Count != 1) {
                        foreach (var candidate in services.Services) candidate.Dispose();
                        throw new IOException("Selected device does not expose the WILD FFF0 control service: " + services.Status);
                    }
                    service = services.Services[0];
                    var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                    if (characteristics.Status != GattCommunicationStatus.Success) throw new IOException("GATT discovery failed: " + characteristics.Status);
                    rx = characteristics.Characteristics.SingleOrDefault(c => c.Uuid == RxId);
                    tx = characteristics.Characteristics.SingleOrDefault(c => c.Uuid == TxId);
                    if (rx == null || tx == null) throw new IOException("FFF1 notifications and FFF2 writes are required.");
                    var properties = rx.CharacteristicProperties;
                    var subscription = (properties & GattCharacteristicProperties.Notify) != 0 ? GattClientCharacteristicConfigurationDescriptorValue.Notify :
                        (properties & GattCharacteristicProperties.Indicate) != 0 ? GattClientCharacteristicConfigurationDescriptorValue.Indicate : GattClientCharacteristicConfigurationDescriptorValue.None;
                    if (subscription == GattClientCharacteristicConfigurationDescriptorValue.None) throw new IOException("FFF1 does not support notifications or indications.");
                    if ((tx.CharacteristicProperties & (GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse)) == 0) throw new IOException("FFF2 is not writable.");
                    rx.ValueChanged += ValueChanged;
                    var result = await rx.WriteClientCharacteristicConfigurationDescriptorAsync(subscription).AsTask(timeout.Token);
                    if (result != GattCommunicationStatus.Success) throw new IOException("Notification subscription failed: " + result);
                    // The BLE/UART bridge needs a brief subscription settle time.
                    await Task.Delay(250, timeout.Token);
                    if (!Connected) throw new IOException("Selected device disconnected during discovery.");
                }
            }
            catch { Disconnect(); throw; }
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
            var handler = Disconnected; if (handler != null) handler("BLE disconnected. Reconnect and read status; recording was not stopped.");
        }
        internal static bool Matches(byte command, byte[] request, byte receivedCommand, byte[] response)
        {
            if (response==null || response.Length!=64 || BitConverter.ToUInt32(response,60)!=NpSdControl.Crc(response,60))return false;
            if(command==0x82){
                if(!DiscoveryRequest(request) || receivedCommand!=0xb9)return false;
                try { DeviceVersion(response,1);return true; }catch(IOException){return false;}
            }
            if (command != receivedCommand || request==null || request.Length!=64) return false;
            uint magic = BitConverter.ToUInt32(request, 0);
            if (command == 0xb8) {
                try {
                    var reply = NpConfigClient.Parse(response,request[5],BitConverter.ToUInt32(request,8));
                    return request[5] != 2 || reply.Error != 0 || reply.Received == BitConverter.ToUInt32(request,12) + BitConverter.ToUInt32(request,16);
                } catch(IOException) { return false; }
            }
            uint expected = magic == 0x31444c4e ? 0x31524c4eU : magic == NpV2SdControl.RequestMagic ? NpV2SdControl.ReplyMagic :
                magic == NpV2OnlineClock.RequestMagic ? NpV2OnlineClock.ReplyMagic : magic;
            return BitConverter.ToUInt32(response, 0) == expected && BitConverter.ToUInt32(response, 4) == BitConverter.ToUInt32(request, 4) &&
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
            if(s.Error!=0)throw new IOException("Device rejected operation "+op+": error "+s.Error+"; fault "+s.Fault+".");
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
                await WriteFrame(frame,generation,cancel);
                if (await Task.WhenAny(waiting.Task, Task.Delay(5000, cancel)) != waiting.Task) {
                    cancel.ThrowIfCancellationRequested();
                    throw new TimeoutException("No matching firmware receipt. Outcome unknown; read status before retrying. No command replayed.");
                }
                byte[] response = await waiting.Task;
                if (!Connected || epoch != generation) throw new IOException("Connection changed; the old receipt was discarded.");
                return response;
            }
            finally {
                lock (receiveLock) { if (receipt == waiting) { receipt = null; matches = null; } }
                transaction.Release();
            }
        }
        async Task WriteFrame(byte[] frame,int generation,CancellationToken cancel)
        {
            if(!Connected || Epoch!=generation)throw new IOException("BLE connection changed; no command sent.");
            var transmitter=tx;
            bool fast=(transmitter.CharacteristicProperties & GattCharacteristicProperties.WriteWithoutResponse)!=0;
            var option=fast?GattWriteOption.WriteWithoutResponse:GattWriteOption.WriteWithResponse;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                timeout.CancelAfter(5000);
                for(int at=0;at<frame.Length;at+=20){
                    timeout.Token.ThrowIfCancellationRequested();
                    if(!Connected || Epoch!=generation)throw new IOException("Connection changed during command delivery; no fragments replayed.");
                    var part=frame.Skip(at).Take(Math.Min(20,frame.Length-at)).ToArray();
                    var result=await transmitter.WriteValueAsync(part.AsBuffer(),option).AsTask(timeout.Token);
                    if(result!=GattCommunicationStatus.Success)throw new IOException("BLE write failed: "+result+"; outcome unknown, no retry sent.");
                    if(fast && at+part.Length<frame.Length)await Task.Delay(3,timeout.Token);
                }
            }
            if(!Connected || Epoch!=generation)throw new IOException("Connection changed during delivery; outcome unknown.");
        }
        internal async Task<bool> TryWritePreview(byte[] request,int expectedEpoch,CancellationToken cancel)
        {
            if(request==null || request.Length!=64 || BitConverter.ToUInt32(request,0)!=NpV2PreviewBurst.Magic ||
                BitConverter.ToUInt32(request,4)!=1 || BitConverter.ToUInt32(request,8)>4 || BitConverter.ToUInt32(request,12)==0 ||
                BitConverter.ToUInt32(request,60)!=NpSdControl.Crc(request,60))throw new ArgumentException("Valid preview request required.");
            if(!await transaction.WaitAsync(0,cancel))return false; // Recording/status/clock controls take priority.
            try{await WriteFrame(NpBleWire.Frame(0xb9,request),expectedEpoch,cancel);return true;}
            finally{transaction.Release();}
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
                if(!await TryWritePreview(NpV2PreviewBurst.Request(id,0,probe,channel,0,0),expectedEpoch,cancel))throw new IOException("Controls busy; preview deferred.");
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
                epoch++; wire.Clear();
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
