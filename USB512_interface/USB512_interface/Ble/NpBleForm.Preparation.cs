using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        NpProbePreparation.Receipt probePreparation;
        NpProbePreparation.Attempt probePreparationAttempt;
        int probePreparationEpoch,probePreparationVersion;
        ulong probePreparationPeer;
        bool UnknownBlePreparation {get{return probePreparationAttempt!=null&&probePreparationAttempt.Unknown;}}
        void ClearBleProbePreparation(){probePreparation=null;probePreparationAttempt=null;}
        void CheckBlePreparationConnection()
        {
            if(!connection.Connected||connection.Epoch!=probePreparationEpoch||connectedAddress!=probePreparationPeer||recorderVersion!=probePreparationVersion)
                throw new IOException("Connection changed before probe preparation was confirmed.");
        }
        async Task RetireBleProbePreparationForSettings()
        {
            if(probePreparation!=null){await CancelBleProbePreparation();return;}
            if(probePreparationAttempt==null)return;
            CheckBlePreparationConnection();var attempt=probePreparationAttempt;
            var reply=await NpProbePreparation.Query((q,c)=>connection.ExchangeAsync(0xb8,q,c),CancellationToken.None);
            CheckBlePreparationConnection();NpProbePreparation.RequireUid(reply,attempt.Uid);
            if(reply.Result!=0||reply.State!=NpProbePreparation.Empty)
                throw new IOException("Previous probe preparation is unconfirmed. Press Stop before changing settings.");
            ClearBleProbePreparation();configurationStatus.Text="Probe preparation stopped.";
        }
        async Task PrepareBleProbes(NpSdStatus status)
        {
            if(status.Mask==0){ClearBleProbePreparation();configurationStatus.Text="";return;}
            int epoch=connection.Epoch,version=recorderVersion;ulong peer=connectedAddress;
            Action check=()=>{
                RequireStartNotCanceled();
                if(!connection.Connected||connection.Epoch!=epoch||recorderVersion!=version||connectedAddress!=peer)
                    throw new IOException("Connection changed during probe preparation.");
            };
            try{
                // Discovery follows all map COMMITs. Never discover over a partial upload.
                if(V2){
                    var resident=await new Np2ConfigClient(new ConfigTransport(connection)).StatusAsync(0,CancellationToken.None);
                    if((resident.GlobalState!=0&&resident.GlobalState!=2)||resident.Received!=0){configurationStatus.Text="Probe preparation not confirmed. Start remains available.";return;}
                }else{
                    var resident=await new WildNpConfiguration.NpConfigClient(new ConfigTransport(connection)).StatusAsync(0,CancellationToken.None);
                    if(resident.Error!=0||(resident.TransferState!=0&&resident.TransferState!=2)||resident.Received!=0){configurationStatus.Text="Probe preparation not confirmed. Start remains available.";return;}
                }
                check();
                var receipt=await NpProbePreparation.Begin((q,c)=>connection.ExchangeAsync(0xb8,q,c),NpProbePreparation.Sd,
                    status.Mask,status.Hz,bleDeviceUid,check,CancellationToken.None,
                    r=>probePreparation=r,a=>{probePreparationAttempt=a;probePreparationEpoch=epoch;probePreparationPeer=peer;probePreparationVersion=version;});
                check();
                if(receipt==null){ClearBleProbePreparation();configurationStatus.Text="This firmware prepares probes at recording start.";Append(configurationStatus.Text);return;}
                probePreparation=receipt;bleDeviceUid=receipt.Uid;configurationStatus.Text=receipt.Text;Append(receipt.Text);
            }catch(Exception ex) when(ex is IOException||ex is TimeoutException||ex is NotSupportedException){
                check();configurationStatus.Text="Probe preparation not confirmed. Start remains available.";
                Append(configurationStatus.Text+" "+ex.Message);
            }
        }
        async Task CancelBleProbePreparation()
        {
            var expected=probePreparation;
            if(expected==null)return;
            CheckBlePreparationConnection();
            try{
                var recorder=await ReadStatus();CheckBlePreparationConnection();
                if(!StopConfirmed(recorder))throw new IOException("The recorder is still active; no preparation cancellation was sent.");
                await NpProbePreparation.CancelPrepared((q,c)=>connection.ExchangeAsync(0xb8,q,c),expected,CheckBlePreparationConnection,CancellationToken.None,idleSafe:true);
                ClearBleProbePreparation();configurationStatus.Text="Probe preparation stopped. Start remains available.";
            }catch{configurationStatus.Text="Probe preparation Stop is unconfirmed. Read status before changing settings.";throw;}
        }
        async Task StopUnknownBlePreparation(NpSdStatus status)
        {
            var attempt=probePreparationAttempt;
            if(attempt==null||!attempt.Unknown)return;
            CheckBlePreparationConnection();
            // No exact generation survived a lost ACK. One explicit ordinary
            // STOP retires any preparation on this same connection/UID.
            if(!attempt.StopIssued){
                attempt.StopIssued=true;
                uint id=status==null?Next():V2?NpV2SdControl.NextId(status):NpSdControl.NextId(status);
                await Send(V2?NpV2SdControl.Request(3,id):NpSdControl.Request(3,id));
            }
            CheckBlePreparationConnection();
            await NpProbePreparation.WaitEmpty((q,c)=>connection.ExchangeAsync(0xb8,q,c),attempt.Uid,CheckBlePreparationConnection,CancellationToken.None);
            ClearBleProbePreparation();configurationStatus.Text="Probe preparation stopped. Start remains available.";
        }
        async Task ObserveProbePreparation(NpSdStatus status)
        {
            var expected=probePreparation;
            if(expected==null)return;
            if(connection.Epoch!=probePreparationEpoch||connectedAddress!=probePreparationPeer||recorderVersion!=probePreparationVersion||
                status.Fault!=0||status.Mask!=expected.Mask||status.Hz!=expected.Hz||status.State==(V2?3U:1U)){
                ClearBleProbePreparation();configurationStatus.Text="Probe preparation no longer active.";return;
            }
            if(expected.State!=NpProbePreparation.Preparing&&expected.State!=NpProbePreparation.Canceling)return;
            int epoch=connection.Epoch;ulong peer=connectedAddress;
            try{
                var receipt=await NpProbePreparation.Query((q,c)=>connection.ExchangeAsync(0xb8,q,c),CancellationToken.None);
                if(!connection.Connected||connection.Epoch!=epoch||connectedAddress!=peer||probePreparation!=expected)return;
                NpProbePreparation.RequireUid(receipt,expected.Uid);
                if(receipt.Result!=0)throw new IOException("Probe preparation status is unconfirmed.");
                if(receipt.Generation!=expected.Generation||receipt.Mode!=expected.Mode||receipt.Mask!=expected.Mask||receipt.Hz!=expected.Hz){ClearBleProbePreparation();configurationStatus.Text="Probe preparation changed. Start remains available.";return;}
                receipt.CancelIssued=expected.CancelIssued;probePreparation=receipt;configurationStatus.Text=receipt.Text;
                if(receipt.State!=expected.State)Append(receipt.Text);
            }catch(Exception ex) when(ex is IOException||ex is TimeoutException||ex is NotSupportedException){
                if(connection.Epoch==epoch&&probePreparation==expected)configurationStatus.Text="Probe preparation not confirmed. Start remains available.";
            }
        }
    }
}
