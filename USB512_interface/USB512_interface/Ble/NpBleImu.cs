using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface {
    internal sealed class NpBleImuRetry {
        int failures;long next;
        internal bool CanAttempt(long now){return now>=next;}
        internal int Defer(long now){int delay=1000<<Math.Min(failures,3);if(failures<4)failures++;next=now+delay;return delay;}
        internal void Reset(){failures=0;next=0;}
    }
    internal sealed class NpBleImuUnavailableException : IOException {
        internal readonly uint Error;
        internal NpBleImuUnavailableException(uint error) : base(error==2?"BLE IMU is unavailable on this firmware.":
            error==4?"IMU is busy.":error==3?"IMU snapshot expired.":"IMU request rejected.") { Error=error; }
    }

    // Optional read-only B9 lane. Each view is one frozen SNS1 snapshot, not a
    // FIFO or a clock observation. Nothing here starts or alters acquisition.
    internal static class NpBleImu {
        internal const uint RequestMagic=0x5153504e,ReplyMagic=0x5253504e;
        internal const uint V1Capability=0x10000000U;
        internal const int SnapshotBytes=140,ChunkBytes=28;
        internal static bool CanPoll(int version,uint flags){return version==2 || version==1 && (flags&V1Capability)!=0;}
        static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        internal static byte[] Request(uint op,uint id,uint token,uint offset) {
            if(id==0 || op>1 || (op==0?(token!=0 || offset!=0):(token==0 || offset==0 || offset>=SnapshotBytes || offset%ChunkBytes!=0)))
                throw new ArgumentException("Invalid read-only IMU request.");
            var b=new byte[64];Put(b,0,RequestMagic);Put(b,4,1);Put(b,8,op);Put(b,12,id);Put(b,16,token);Put(b,20,offset);
            Put(b,60,NpSdControl.Crc(b,60));return b;
        }
        internal static bool Matches(byte[] request,byte[] response) {
            return request!=null && request.Length==64 && response!=null && response.Length==64 &&
                U(request,0)==RequestMagic && U(request,4)==1 && U(request,12)!=0 && U(request,8)<=1 &&
                U(request,60)==NpSdControl.Crc(request,60) && U(response,0)==ReplyMagic && U(response,4)==1 &&
                U(response,8)==U(request,8) && U(response,12)==U(request,12) && U(response,24)==U(request,20) &&
                U(response,60)==NpSdControl.Crc(response,60);
        }
        internal static byte[] Accept(byte[] request,byte[] response,uint token) {
            if(!Matches(request,response))throw new IOException("IMU reply does not match the request; view discarded.");
            uint error=U(response,16);
            if(error!=0) {
                if(error>4 || U(response,20)!=0 || U(response,28)!=0)throw new IOException("Invalid IMU error reply.");
                for(int i=32;i<60;i++)if(response[i]!=0)throw new IOException("Invalid IMU error payload.");
                throw new NpBleImuUnavailableException(error);
            }
            if(U(response,28)!=SnapshotBytes || U(response,20)==0 || (token!=0 && U(response,20)!=token))
                throw new IOException("IMU snapshot identity changed; view discarded.");
            var data=new byte[ChunkBytes];Buffer.BlockCopy(response,32,data,0,ChunkBytes);return data;
        }
        internal static async Task<byte[]> ReadAsync(Func<byte[],CancellationToken,Task<byte[]>> exchange,Func<uint> nextId,
            Func<int> epoch,Func<bool> yield,CancellationToken cancel) {
            int generation=epoch();var view=new byte[SnapshotBytes];uint token=0;
            var ids=new System.Collections.Generic.HashSet<uint>();
            for(uint offset=0;offset<SnapshotBytes;offset+=ChunkBytes) {
                cancel.ThrowIfCancellationRequested();
                if(epoch()!=generation)throw new IOException("BLE connection changed; IMU view discarded.");
                if(yield())return null;
                uint id=nextId();if(id==0 || !ids.Add(id))throw new IOException("Fresh IMU transaction ID required.");
                byte[] request=Request(offset==0?0U:1U,id,token,offset);
                byte[] response=await exchange(request,cancel);
                cancel.ThrowIfCancellationRequested();
                if(epoch()!=generation)throw new IOException("BLE connection changed; IMU view discarded.");
                if(yield() || response==null)return null;
                var data=Accept(request,response,token);if(offset==0)token=U(response,20);
                Buffer.BlockCopy(data,0,view,(int)offset,ChunkBytes);
            }
            if(U(view,0)!=0x31534e53 || U(view,4)!=1)throw new IOException("Unsupported SNS1 IMU snapshot.");
            return view;
        }
    }

    // Shared by the form and headless validation. Only complete validated views
    // reach the existing parser/journal. No held-value DAT is synthesized.
    internal sealed class NpBleImuSession : IDisposable {
        readonly NpUsbSensors sensors=new NpUsbSensors(null,false);
        readonly NpImuSampleJournal journal;
        internal string Error {get{return journal==null?null:journal.Error;}}
        internal string DisplayText {get{return sensors.DisplayText;}}
        internal NpBleImuSession(string folder,string identity) {
            if(folder!=null) {
                if(String.IsNullOrWhiteSpace(identity))throw new ArgumentException("Device identity required for IMU journal.");
                File.WriteAllText(Path.Combine(folder,"ble_imu_stream.txt"),
                    "source=coherent_B9_SNS1\nmaximum_poll_hz=1\nsnapshot_bytes=140\n"+
                    "device_identity_kind="+(identity.StartsWith("BLE-",StringComparison.Ordinal)?"BLE_address":"MCU_UID")+"\n"+
                    "Distinct valid retained observations only; not lossless native200Hz sampling.\n"+
                    "Host receipt is provenance, not a calibrated sensor/PC clock observation.\n");
                journal=new NpImuSampleJournal(folder,identity);
            }
        }
        internal NpImuDisplay Accept(byte[] snapshot) {
            if(snapshot==null || snapshot.Length!=NpBleImu.SnapshotBytes)throw new IOException("Incomplete IMU view.");
            uint observed=BitConverter.ToUInt32(snapshot,16);
            sensors.FeedSnapshot(snapshot,0,observed);
            if(journal!=null)journal.Observe(snapshot,0,observed,sensors.Valid,null,null,0,0);
            return sensors.GetDisplay();
        }
        public void Dispose(){try{sensors.Dispose();}finally{if(journal!=null)journal.Dispose();}}
    }
}
