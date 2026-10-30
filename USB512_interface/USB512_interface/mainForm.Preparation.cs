using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WildNpConfiguration;
using CE32_console;

namespace USB512_interface
{
    public partial class Form1
    {
#if NP_FIRST_SYNC_ENABLE
        sealed class UsbRtcSetup {
            internal string Uid,Path,Error;internal int Family;internal bool Supported;
            internal object Handle,Key;internal byte[] Probe,CurrentReply;internal NpHardwareRtcCalibration Attempt;internal bool CurrentConfirmed;
            internal UsbRtcSetup PreviousCompleted;
            internal readonly List<object> UsbTransactions=new List<object>();internal int DroppedUsbTransactions;
            internal bool ControlRetirementUnknown;internal object ControlIdleProof;
            internal DateTime HostBegin;internal long HostQpc;
        }
        // An uncertain COMMIT is sticky for this UID for this Form lifetime,
        // including a later reopen/Start. Never automatically repeat it.
        readonly Dictionary<string,UsbRtcSetup> npUsbRtcSetups=new Dictionary<string,UsbRtcSetup>(StringComparer.OrdinalIgnoreCase);
        object npUsbRtcSetupKey,npUsbRtcPendingSetup;
        string npUsbRtcPendingUid;int npUsbRtcPendingFamily;uint npUsbRtcPendingMask,npUsbRtcPendingHz;
        void BeginUsbRtcSettingsSetup(string uid,int family,uint mask=0,uint hz=0){
            npUsbRtcSetupKey=npUsbRtcPendingSetup=new object();npUsbRtcPendingUid=uid;npUsbRtcPendingFamily=family;npUsbRtcPendingMask=mask;npUsbRtcPendingHz=hz;
        }
        void BeginUsbRtcRecordingSetup(string uid,int family,uint mask=0,uint hz=0){
            npUsbRtcSetupKey=npUsbRtcPendingSetup!=null&&String.Equals(npUsbRtcPendingUid,uid,StringComparison.OrdinalIgnoreCase)&&
                npUsbRtcPendingFamily==family&&npUsbRtcPendingMask==mask&&npUsbRtcPendingHz==hz?npUsbRtcPendingSetup:new object();
            npUsbRtcPendingSetup=null; // First Start consumes the accepted settings/PREPARE setup.
        }
        uint npUsbRtcId;
        uint NextUsbRtcId(){npUsbRtcId=unchecked(npUsbRtcId+1);if(npUsbRtcId==0)npUsbRtcId=1;return npUsbRtcId;}
        bool ConfirmUsbRtcControlIdle(UsbRtcSetup setup,object handle,Action check){
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            try{
                check();bool idle;object proof;var v1=handle as XeNpWinUsbDevice;var v2=handle as NpV2UsbDevice;
                if(v1!=null&&setup.Family==1){
                    var s=v1.ReadV1ServiceStatus();idle=s!=null&&s.Split&&s.Role==2&&s.Busy==0&&s.PendingMode==0;
                    proof=NpRecordingSettings.Fields("family",1,"mode",s==null?null:(object)s.Role,"busy",s==null?null:(object)s.Busy,"pending_mode",s==null?null:(object)s.PendingMode);
                }else if(v2!=null&&setup.Family==2){
                    var s=v2.GetServiceStatus();s.RequireUid(setup.Uid);idle=s.Valid&&s.Mode==2&&s.Busy==0&&s.Target==0;
                    proof=NpRecordingSettings.Fields("family",2,"mode",s.Mode,"busy",s.Busy,"pending_mode",s.Target);
                }else throw new IOException("No bound USB RTC control owner is available.");
                check();setup.ControlIdleProof=NpRecordingSettings.Fields("request",0x6e,"uid",setup.Uid,"host_begin_qpc",begin,
                    "host_end_qpc",System.Diagnostics.Stopwatch.GetTimestamp(),"idle",idle,"status",proof,
                    "scope","Same bound handle's service Busy includes configuration ownership in the advertised hardware-RTC firmware.");
                setup.ControlRetirementUnknown=!idle;return idle;
            }catch(Exception ex){setup.ControlRetirementUnknown=true;setup.ControlIdleProof=NpRecordingSettings.Fields("request",0x6e,"uid",setup.Uid,"host_begin_qpc",begin,
                "host_end_qpc",System.Diagnostics.Stopwatch.GetTimestamp(),"idle",false,"error",ex.Message);return false;}
        }
        byte[] ExchangeUsbRtc(UsbRtcSetup setup,object handle,Func<byte[],CancellationToken,byte[]> exchange,byte[] request,CancellationToken cancel,Action check){
            byte[] reply;try{reply=exchange(request,cancel);}finally{
                var v1=handle as XeNpWinUsbDevice;var v2=handle as NpV2UsbDevice;
                var transaction=v1!=null?v1.LastHardwareRtcTransaction:v2==null?null:v2.LastHardwareRtcTransaction;
                if(transaction!=null&&transaction.Matches(request)){
                    if(transaction.RetirementUnknown){
                        if(ConfirmUsbRtcControlIdle(setup,handle,check)){transaction.ControlIdleConfirmed=true;transaction.ControlIdleProof=setup.ControlIdleProof;}
                        else setup.ControlRetirementUnknown=true;
                    }
                    if(setup.UsbTransactions.Count==32){setup.UsbTransactions.RemoveAt(0);setup.DroppedUsbTransactions++;}
                    setup.UsbTransactions.Add(transaction.Evidence());
                }
            }
            RequireUsbRtcControlRetired(setup);return reply;
        }
        static void RequireUsbRtcControlRetired(UsbRtcSetup setup){
            if(setup.ControlRetirementUnknown)throw new IOException("USB RTC control retirement is unconfirmed; recording Start was not issued. Read status before trying again.");
        }
        void PrepareUsbHardwareRtc(string uid,string path,int family,object handle,Func<byte[]> info,
            Func<byte[],CancellationToken,byte[]> exchange,Action check,NpRecordingSettings settings=null)
        {
            check();try{NpFirstSyncUsb.UidWords(uid);}catch(InvalidDataException){return;}
            var key=npUsbRtcSetupKey??(npUsbRtcSetupKey=new object());var ownerCheck=check;
            check=()=>{ownerCheck();if(!ReferenceEquals(npUsbRtcSetupKey,key))throw new OperationCanceledException("RTC logical setup changed.");};
            UsbRtcSetup setup,previousCompleted=null;
            if(npUsbRtcSetups.TryGetValue(uid,out setup)){
                if(setup.ControlRetirementUnknown){ConfirmUsbRtcControlIdle(setup,handle,check);SaveUsbHardwareRtc(setup,settings,ReferenceEquals(setup.Key,key));RequireUsbRtcControlRetired(setup);}
                bool sameSetup=ReferenceEquals(setup.Key,key);
                bool newSetup=false;
                if(!sameSetup||!ReferenceEquals(handle,setup.Handle)||setup.Path!=path){
                    setup.CurrentConfirmed=false;setup.CurrentReply=null;setup.Handle=handle;setup.Path=path;
                    // A new handle is not the old calibration transaction.
                    // Observe first; only a known completed/nonmutating prior
                    // transaction can admit a new stopped setup below.
                    if(!HasUsbProbePreparation)try{
                        var caps=NpFirstSyncUsb.ParseInfo(info(),uid,family);check();
                        if(caps.HardwareRtc&&(setup.Attempt==null||!setup.Attempt.CommitAttempted||setup.Attempt.Confirmed)){
                            var query=NpHardwareRtcClock.Request(0,NextUsbRtcId());
                            check();setup.CurrentReply=ExchangeUsbRtc(setup,handle,exchange,query,CancellationToken.None,check);check();
                            var r=NpHardwareRtcClock.Parse(setup.CurrentReply,query);
                            setup.CurrentConfirmed=r.Error==0&&r.PcCalibrated&&setup.Attempt!=null&&setup.Attempt.Confirmed&&r.Generation==setup.Attempt.Receipt.Generation&&r.PcUncertainty==setup.Attempt.PcUncertaintyUs;
                            bool stopped=family==1?r.State==1||r.State==5:r.State==0||r.State==1||r.State==5||r.State==7;
                            newSetup=(!sameSetup||!setup.CurrentConfirmed)&&r.Error==0&&stopped&&(r.Flags&(NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32|NpHardwareRtcClock.Frozen))==(NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32);
                            if(!setup.CurrentConfirmed&&!newSetup)setup.Error="Reopened RTC setting is unconfirmed; no phase write was repeated.";
                        }
                    }catch(Exception ex){setup.Error=ex.Message;check();}
                }
                RequireUsbRtcControlRetired(setup);
                if(!newSetup){SaveUsbHardwareRtc(setup,settings,ReferenceEquals(setup.Key,key));return;}
                // Fresh stopped flags/generation proved a new setup, and the
                // prior transaction is known complete (or never mutated).
                // This is a new calibration with new IDs, not a COMMIT replay.
                previousCompleted=setup.Attempt!=null&&setup.Attempt.Confirmed?setup:setup.PreviousCompleted;
                if(previousCompleted!=null)previousCompleted.PreviousCompleted=null; // Immediate prior setup only.
                npUsbRtcSetups.Remove(uid);
            }
            if(HasUsbProbePreparation)return; // Never phase-write an armed or uncertain PREPARE.
            setup=new UsbRtcSetup{Uid=uid,Path=path,Family=family,Handle=handle,Key=key,PreviousCompleted=previousCompleted};npUsbRtcSetups.Add(uid,setup);
            try{
                var caps=NpFirstSyncUsb.ParseInfo(info(),uid,family);check();
                if(!caps.HardwareRtc)return; // INFO7/legacy is not hardware-RTC support.
                setup.Supported=true;setup.HostBegin=DateTime.UtcNow;setup.HostQpc=System.Diagnostics.Stopwatch.GetTimestamp();
                Action bound=()=>{check();if(HasUsbProbePreparation||!ReferenceEquals(handle,setup.Handle))throw new IOException("RTC setup handle or stopped owner changed.");};
                Func<byte[],CancellationToken,Task<byte[]>> send=(q,c)=>{
                    bound();c.ThrowIfCancellationRequested();byte[] raw=ExchangeUsbRtc(setup,handle,exchange,q,c,bound);c.ThrowIfCancellationRequested();
                    bound();return Task.FromResult(raw);
                };
                var request=NpHardwareRtcClock.Request(0,NextUsbRtcId());
                setup.Probe=send(request,CancellationToken.None).GetAwaiter().GetResult();
                var observed=NpHardwareRtcClock.Parse(setup.Probe,request);
                if(observed.Error!=0||(observed.Flags&(NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32))!=(NpHardwareRtcClock.Hardware|NpHardwareRtcClock.HseDiv32))
                    throw new IOException("Advertised hardware RTC did not confirm stopped control support.");
                if((observed.Flags&NpHardwareRtcClock.Frozen)!=0)throw new IOException("RTC is already frozen for preparation; no phase write was attempted.");
                setup.Attempt=NpHardwareRtcClock.Synchronize(send,NextUsbRtcId,
                    state=>family==1?state==1||state==5:state==0||state==1||state==5||state==7,CancellationToken.None).GetAwaiter().GetResult();
                setup.CurrentConfirmed=setup.Attempt.Confirmed;setup.CurrentReply=setup.Attempt.LastReply;
            }catch(NpHardwareRtcCalibrationException ex){setup.Attempt=ex.Attempt;setup.Error=ex.Message;check();}
            catch(Exception ex){setup.Error=ex.Message;check();}
            finally{SaveUsbHardwareRtc(setup,settings,ReferenceEquals(setup.Key,key));}
            RequireUsbRtcControlRetired(setup);
            if(setup.Supported&&setup.Error!=null)PostToUi(()=>lblRecInfo.Text="RTC synchronization unconfirmed; recording remains available.");
        }
        static void SaveUsbHardwareRtc(UsbRtcSetup setup,NpRecordingSettings settings,bool sameSetup){
            if(settings==null)return;
            try{
            NpHardwareRtcEvidence.Save(settings,"USB",setup.Uid,0,setup.HostBegin,setup.HostQpc,setup.Supported,setup.Probe,setup.Attempt,setup.Error);
            settings.Context("hardware_rtc_current_handle",NpRecordingSettings.Fields("readback_confirmed",setup.CurrentConfirmed,
                "reply_base64",setup.CurrentReply==null?null:Convert.ToBase64String(setup.CurrentReply),
                "scope","A reopened handle requires fresh flags/generation confirmation. Historical calibration is not current phase proof."));
            settings.Context("hardware_rtc_control_path",setup.Path);
            settings.Context("hardware_rtc_usb_transactions",setup.UsbTransactions.ToArray());
            settings.Context("hardware_rtc_usb_transactions_dropped",setup.DroppedUsbTransactions);
            settings.Context("hardware_rtc_control_retirement_unknown",setup.ControlRetirementUnknown);
            settings.Context("hardware_rtc_control_idle_proof",setup.ControlIdleProof);
            settings.Context("hardware_rtc_same_logical_setup",sameSetup);
            var prior=new List<object>();for(var old=setup.PreviousCompleted;old!=null;old=old.PreviousCompleted)
                prior.Add(NpRecordingSettings.Fields("uid",old.Uid,"host_observation_begin_utc",old.HostBegin.ToString("O"),
                    "commit_request_base64",Convert.ToBase64String(old.Attempt.CommitRequest),"commit_ack_base64",Convert.ToBase64String(old.Attempt.CommitAcknowledgement),
                    "last_readback_base64",Convert.ToBase64String(old.Attempt.LastReply)));
            settings.Context("hardware_rtc_prior_completed_setups",prior.ToArray());
            settings.Context("hardware_rtc_evidence_scope","Historical stopped control attempt; a reopened handle must freshly confirm its generation or establish a separate new stopped setup. Ambiguous COMMIT is never replayed.");
            settings.Write();
            }catch(Exception ex){System.Diagnostics.Trace.WriteLine("Optional RTC metadata: "+ex.Message);}
        }
#endif
        volatile bool npSettingsApplying,npPreparationStopRequested;
        NpProbePreparation.Receipt npUsbPreparation;
        NpProbePreparation.Attempt npUsbPreparationAttempt;
        string npUsbPreparationPath;
        DeviceKind npUsbPreparationKind;
        readonly System.Windows.Forms.Timer npPreparationTimer=new System.Windows.Forms.Timer{Interval=1500};
        bool npPreparationTimerInitialized;
        bool HasUsbProbePreparation {get{return npUsbPreparation!=null||npUsbPreparationAttempt!=null;}}

        void ClearUsbProbePreparation()
        {
            npPreparationTimer.Stop();npUsbPreparation=null;npUsbPreparationAttempt=null;npUsbPreparationPath=null;
        }
        async Task RetirePassiveUsbReads()
        {
            // The UI lease prevents new readers. Existing callbacks must retire
            // their physical handles before settings or cancellation take EP0.
            var wait=System.Diagnostics.Stopwatch.StartNew();
            while(systemInfoActive!=0||deviceRefreshActive!=0){
                if(wait.ElapsedMilliseconds>=10000)throw new IOException("Device information is still being read. Wait before changing settings.");
                await Task.Delay(20);
            }
        }
        void CheckUsbPreparationContext(string uid,DeviceKind kind,bool observeStop=true)
        {
            if(IsDisposed||Disposing||selectedDeviceKind!=kind||
                !String.Equals(uid,kind==DeviceKind.NpV2?NpV2FirmwareForm.Uid(devicePath):NpV1Management.UidFromPath(devicePath),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Selected logger changed during probe preparation.");
            if(observeStop&&npPreparationStopRequested)throw new OperationCanceledException("Probe preparation canceled by Stop.");
        }
        async Task RetireUsbProbePreparationForSettings()
        {
            if(!HasUsbProbePreparation)return;
            await RetireUsbProbePreparation(false);
        }
        async Task RetireUsbProbePreparation(bool explicitStop)
        {
            var expected=npUsbPreparation;var attempt=npUsbPreparationAttempt;
            string path=npUsbPreparationPath,uid=expected==null?attempt==null?null:attempt.Uid:expected.Uid;
            DeviceKind kind=npUsbPreparationKind;
            if(uid==null){ClearUsbProbePreparation();return;}
            CheckUsbPreparationContext(uid,kind,false);
            Action check=()=>CheckUsbPreparationContext(uid,kind,false);
            await Task.Run(()=>{
                Func<Func<byte[],CancellationToken,Task<byte[]>>,Action,Task> retire=async(exchange,stop)=>{
                    if(expected!=null){await NpProbePreparation.CancelPrepared(exchange,expected,check,CancellationToken.None,idleSafe:true);return;}
                    if(attempt!=null&&attempt.Unknown&&explicitStop){
                        // A lost PREPARE ACK permits one ordinary explicit Stop,
                        // never a guessed-generation CANCEL or PREPARE replay.
                        if(!attempt.StopIssued){check();attempt.StopIssued=true;stop();}
                        await NpProbePreparation.WaitEmpty(exchange,uid,check,CancellationToken.None);return;
                    }
                    var current=await NpProbePreparation.Query(exchange,CancellationToken.None);check();NpProbePreparation.RequireUid(current,uid);
                    if(current.Result!=0)throw new IOException("Probe preparation status is unconfirmed; no command was sent.");
                    if(current.State==NpProbePreparation.Empty)return;
                    if(attempt==null||!attempt.Unknown)throw new IOException("Probe preparation ownership is unconfirmed. Read status before changing settings.");
                    throw new IOException("Previous probe preparation is unconfirmed. Press Stop before changing settings.");
                };
                if(kind==DeviceKind.NpV2)using(var device=NpV2UsbDevice.Open(path)){
                    var service=device.GetServiceStatus();service.RequireDeviceUid(uid,NpV2FirmwareForm.Uid(path));
                    if(service.Mode!=2U)throw new IOException("The selected V2 USB mode changed; no Stop was sent.");
                    var capture=device.GetCaptureStatus();
                    if(service.Busy!=0U || (capture.State!=0U&&capture.State!=4U&&capture.State!=255U))
                        throw new IOException("The V2 capture owner is still active; no preparation cancellation was sent.");
                    retire((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),device.Cancel).GetAwaiter().GetResult();
                }else using(var device=XeNpWinUsbDevice.OpenForControl(path)){
                    var service=device.ReadV1ServiceStatus();
                    if(service==null||service.Role!=2||service.Recovery)throw new IOException("The original V1 logger streaming service is unavailable; no Stop was sent.");
                    if(service.Busy!=0 || device.GetStatus().Streaming)throw new IOException("The V1 capture owner is still active; no preparation cancellation was sent.");
                    retire((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),device.StopPreparation).GetAwaiter().GetResult();
                }
            });
            ClearUsbProbePreparation();lblRecInfo.Text="Probe preparation stopped. Start remains available.";
        }
        async Task StopUsbProbePreparation()
        {
            npPreparationStopRequested=true;
            if(npSettingsApplying)return; // The current owner retires its request first.
            npSettingsApplying=true;npPreparationTimer.Stop();UpdateStartEnabled();
            try{await RetirePassiveUsbReads();await RetireUsbProbePreparation(true);}
            catch(Exception ex){lblRecInfo.Text="Probe preparation Stop is unconfirmed. Read status before changing settings. "+ex.Message;}
            finally{npSettingsApplying=false;npPreparationStopRequested=false;if(!IsDisposed&&!Disposing)UpdateStartEnabled();}
        }
        async Task PrepareUsbProbeSettings()
        {
            bool np2=selectedDeviceKind==DeviceKind.NpV2;DeviceKind kind=selectedDeviceKind;
            string selectedPath=devicePath,uid=np2?NpV2FirmwareForm.Uid(selectedPath):NpV1Management.UidFromPath(selectedPath);
            Action check=()=>CheckUsbPreparationContext(uid,kind);
            var v1=NpRequestsForDevice(selectedPath);
            var v1Requests=v1==null?new List<NpConfigRequest>():v1.Values.ToList();
            var v2Requests=new List<Np2ConfigRequest>();
            if(np2){
                var apply=NpV2ApplyRequestsForDevice(selectedPath);var requests=NpV2RequestsForDevice(selectedPath);
                if(apply!=null)foreach(int port in apply){string json;if(!requests.TryGetValue(port,out json))throw new IOException("A requested probe map is missing for connector "+port+".");v2Requests.Add(NpConfigurationDialog.PrepareV2Request(json,port));}
            }
            npPreparationTimer.Stop();lblRecInfo.Text="Applying recording settings…";
            var receipt=await Task.Run(()=>{
                check();
                if(np2){
                    string path=NpV2EnsureIdleStreamMode();check();
                    using(var device=NpV2UsbDevice.Open(path)){
                        var caps=device.GetProfileCapabilities();uint mask=npV2RequestedProbeMask==0?caps.SelectedProbeMask:npV2RequestedProbeMask;
                        check();caps=device.ConfigureProfile(mask,npV2RequestedSampleHz);check();
#if NP_FIRST_SYNC_ENABLE
                        PrepareUsbHardwareRtc(uid,path,2,device,device.GetUsbFirstSyncInfo,device.ExchangeHardwareRtc,check);
#endif
                        foreach(var request in v2Requests)if((caps.SelectedProbeMask&(1U<<request.Port))!=0){check();Np2ConfigurationUsb.StageForCapture(device,request);}
                        var client=new Np2ConfigClient(new Np2ConfigurationUsb(device));
                        var status=client.StatusAsync(0,CancellationToken.None).GetAwaiter().GetResult();check();
                        if((status.GlobalState!=0&&status.GlobalState!=2)||status.Received!=0)return null;
                        npUsbPreparationPath=path;npUsbPreparationKind=kind;
                        return NpProbePreparation.Begin((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),NpProbePreparation.Usb,
                            caps.SelectedProbeMask,caps.SampleHz,uid,check,CancellationToken.None,r=>npUsbPreparation=r,a=>npUsbPreparationAttempt=a).GetAwaiter().GetResult();
                    }
                }
                string streaming=NpV1Management.EnsureStreaming(selectedPath);check();
                NpModeCapabilities mode;NpCaptureMode resolved;string resolution,reconnect;
                using(var device=XeNpWinUsbDevice.OpenForControl(streaming))mode=device.SelectCaptureMode(npRequestedMode,out resolved,out resolution,out reconnect);
                if(reconnect!=null)streaming=NpV1Management.WaitForStreaming(reconnect);
                devicePath=streaming;check();
                using(var device=XeNpWinUsbDevice.OpenForControl(streaming)){
                    mode=device.GetModeCapabilities();
                    if(mode==null||mode.ActiveMask==0)return null;
                    if(resolved!=null&&(mode.ActiveMask!=resolved.ProbeMask||mode.ApHz!=resolved.ApHz))throw new IOException("The device did not confirm the selected recording profile.");
#if NP_FIRST_SYNC_ENABLE
                    PrepareUsbHardwareRtc(uid,streaming,1,device,device.GetUsbFirstSyncInfo,device.ExchangeHardwareRtc,check);
#endif
                    var selected=v1Requests.Where(r=>(mode.ActiveMask&(1<<r.Port))!=0).ToList();
                    check();NpConfigurationUsb.StageForCapture(device,selected,mode.ActiveMask);check();
                    var client=new NpConfigClient(new PreparationV1Transport(device));
                    var status=client.StatusAsync(0,CancellationToken.None).GetAwaiter().GetResult();check();
                    if(status.Error!=0||(status.TransferState!=0&&status.TransferState!=2)||status.Received!=0)return null;
                    npUsbPreparationPath=streaming;npUsbPreparationKind=kind;
                    return NpProbePreparation.Begin((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),NpProbePreparation.Usb,
                        (uint)mode.ActiveMask,(uint)mode.ApHz,uid,check,CancellationToken.None,r=>npUsbPreparation=r,a=>npUsbPreparationAttempt=a).GetAwaiter().GetResult();
                }
            });
            if(IsDisposed||Disposing)return;
            npUsbPreparation=receipt;
            if(receipt==null){ClearUsbProbePreparation();lblRecInfo.Text="Settings saved. This firmware prepares probes at Start.";return;}
            lblRecInfo.Text=receipt.Text;
            if(receipt.State==NpProbePreparation.Preparing){
                if(!npPreparationTimerInitialized){npPreparationTimer.Tick+=async(sender,e)=>await ObserveUsbProbePreparation();npPreparationTimerInitialized=true;}
                npPreparationTimer.Start();
            }
        }
        sealed class PreparationV1Transport:INpConfigTransport
        {
            readonly XeNpWinUsbDevice device;
            internal PreparationV1Transport(XeNpWinUsbDevice device){this.device=device;}
            public Task<byte[]> ExchangeAsync(byte[] request,CancellationToken cancel){return Task.FromResult(device.ExchangeNpConfiguration(request,cancel));}
        }
        async Task ObserveUsbProbePreparation()
        {
            var expected=npUsbPreparation;
            if(expected==null||running||IsDisposed||Disposing){npPreparationTimer.Stop();return;}
            if(devicePath!=npUsbPreparationPath||selectedDeviceKind!=npUsbPreparationKind){ClearUsbProbePreparation();lblRecInfo.Text="Probe preparation not confirmed. Start remains available.";return;}
            if(!NpControlsAvailable)return;
            string path=devicePath;bool np2=selectedDeviceKind==DeviceKind.NpV2;
            npSettingsApplying=true;UpdateStartEnabled();
            try{
                var receipt=await Task.Run(()=>{
                    if(np2)using(var device=NpV2UsbDevice.Open(path))return NpProbePreparation.Query((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),CancellationToken.None).GetAwaiter().GetResult();
                    using(var device=XeNpWinUsbDevice.OpenForControl(path))return NpProbePreparation.Query((q,c)=>Task.FromResult(device.ExchangeProbePreparation(q,c)),CancellationToken.None).GetAwaiter().GetResult();
                });
                if(devicePath!=path||npUsbPreparation!=expected)return;
                NpProbePreparation.RequireUid(receipt,expected.Uid);
                if(receipt.Result!=0)throw new IOException("Probe preparation status is unconfirmed.");
                if(receipt.Generation!=expected.Generation||receipt.Mode!=expected.Mode||receipt.Mask!=expected.Mask||receipt.Hz!=expected.Hz){ClearUsbProbePreparation();lblRecInfo.Text="Probe preparation changed. Start remains available.";return;}
                // Retain the original exact-generation cancellation token.
                receipt.CancelIssued=expected.CancelIssued;npUsbPreparation=receipt;lblRecInfo.Text=receipt.Text;
                if(receipt.State!=NpProbePreparation.Preparing&&receipt.State!=NpProbePreparation.Canceling)npPreparationTimer.Stop();
            }catch(Exception){npPreparationTimer.Stop();lblRecInfo.Text="Probe preparation not confirmed. Start remains available.";}
            finally{npSettingsApplying=false;if(!IsDisposed&&!Disposing){UpdateStartEnabled();if(npPreparationStopRequested)await StopUsbProbePreparation();}}
        }
    }
}
