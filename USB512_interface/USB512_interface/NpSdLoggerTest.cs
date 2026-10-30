using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        internal string SdTestTarget {get{return connection.Connected?"V"+recorderVersion+" · "+connectedAddress.ToString("X12")+" · "+recordingSummary.Text:"Connect a logger in BLE Mode, then choose recording settings.";}}
        internal bool SdTestConnected {get{return connection.Connected;}}
        internal async Task SdTestDownloadMode()
        {
            if(busy||hostBusy)throw new IOException("The logger is busy.");
            busy=true;UpdateControls();
            try {await SwitchMode(NpV1BleProtocol.DestinationUsbDownload,2);}
            finally {busy=false;UpdateControls();NotifyState();}
        }
        async Task<NpBleCatalog> ReadSdTestCatalog()
        {
            int epoch=connection.Epoch;
            Action target=()=>{if(!connection.Connected||connection.Epoch!=epoch)throw new IOException("BLE connection changed during the test.");};
            var s=await ReadStatus();target();
            if(V2)return await new NpV2BleCatalog(ReadStatus,connection.ExchangeAsync,Send,target).Read();
            var catalog=await NpBleCatalog.ReadV1(s,async page=>{target();return await connection.ExchangeV1Storage(page,false);});
            target();if(catalog.V1ReadOnly||catalog.V1Warning!=null)throw new IOException("A writable, fully readable catalog is required for an append-only test.");
            return catalog;
        }
        internal async Task<NpSdTestResult> TestSdRecording(uint seconds,CancellationToken cancel,Action<int,string> progress)
        {
            if(seconds<10||seconds>300)throw new ArgumentException("Choose a 10–300 second recording test.");
            if(busy||hostBusy||!connection.Connected||recorderVersion==0)throw new IOException("Connect an idle logger in BLE Mode first.");
            var selected=profiles.SelectedItem as Profile;
            if(selected==null||selected.Mask==0)throw new IOException("Select a neural recording profile and connect its probes before testing.");
            busy=true;ClosePreview();UpdateControls();NotifyState();
            int epoch=connection.Epoch;ulong peer=connectedAddress;
            var result=new NpSdTestResult {PathKind="logger",Target=SdTestTarget,DeviceUid=bleDeviceUid,LoggerVersion=recorderVersion,
                ProbeMask=selected.Mask,SampleHz=selected.Hz,Firmware=NpFirmwareCatalog.Bundled.Installed((uint)recorderVersion,firmwareIdentity),
                Identity="BLE "+peer.ToString("X12")+" · MCU "+(bleDeviceUid??"not reported"),
                Scope="Real recording with connected probes. Applies only to this profile, card, firmware and tested extent. The test recording stays on SD. Stored framing checks are not a source checksum of every ADC bit. No format, repair or delete is performed."};
            bool submitted=false,cancelSent=false;var wait=Stopwatch.StartNew();
            try
            {
                var initial=await ReadStatus();
                if(initial.Busy||initial.Fault!=0||(initial.Flags&17)!=17 ||
                    !(V2?NpV2SdControl.CanConfigure(initial):NpV1BleProtocol.CanConfigure(initial)))throw new IOException("An idle, mounted writable card with no pending operation is required for the test.");
                var before=await ReadSdTestCatalog();
                if(before.V2!=null){result.CardCidCrc=before.V2.CidCrc;result.Identity+=" · card CID CRC "+before.V2.CidCrc.ToString("X8");}
                else result.Identity+=" · card CID not reported";
                cancel.ThrowIfCancellationRequested();
                await StartRecordingCore(seconds,(s,request)=>{
                    cancel.ThrowIfCancellationRequested();
                    result.CardCapacitySectors=s.Capacity;result.StartLba=s.Start;result.StartRequestId=NpSdGeometry.U(request,12);
                    result.Session=V2?0:NpSdGeometry.U(request,32);result.RequestedBlocks=V2?NpSdGeometry.U(request,24):0;
                    result.RequestedEndLba=V2?0:NpSdGeometry.U(request,28);
                    long header=8192+((0U-(s.Start+8192U))&16383U);
                    result.ExpectedChunks=V2?result.RequestedBlocks:checked((uint)((result.RequestedEndLba-s.Start-header)/128));
                    result.RequestedBytes=(long)result.ExpectedChunks*(V2?46592:65536);
                    if(!V2&&NpSdGeometry.U(request,28)==0 || V2&&result.RequestedBlocks==0)throw new IOException("Continuous recording is prohibited in an SD test.");
                    submitted=true;
                });
                wait.Restart();bool observedRunning=false;Stopwatch finalizing=null;
                double finalizationLimit=Math.Max(180,result.RequestedBytes/10000000.0+120);
                while(true)
                {
                    if(!connection.Connected||connection.Epoch!=epoch||connectedAddress!=peer)throw new IOException("BLE connection changed; test outcome is unknown. Reconnect and read status. The finite extent remains the device's byte limit.");
                    var s=await ReadStatus();
                    if(s.Capacity!=result.CardCapacitySectors||s.Mask!=result.ProbeMask||s.Hz!=result.SampleHz)throw new IOException("Card or recording profile changed during the test.");
                    bool active=V2?s.State==2||s.State==3||s.State==4:s.State==1||s.State==2;
                    if(active)observedRunning=true;
                    if(V2&&s.State==4&&finalizing==null)finalizing=Stopwatch.StartNew();
                    if(s.Fault!=0||s.Error!=0)throw new IOException("Recording failed: state "+s.State+", fault "+s.Fault+", command error "+s.Error+".");
                    if((cancel.IsCancellationRequested||finalizing==null&&wait.Elapsed.TotalSeconds>seconds+120)&&!cancelSent)
                    {
                        if(s.LastId!=result.StartRequestId)throw new IOException("The test no longer owns the active command; no Stop was sent.");
                        cancelSent=true;if(finalizing==null&&(active||s.Busy))await StopRecording();
                    }
                    if(!s.Busy&&(s.State==(V2?5U:3U))&&s.LastId==result.StartRequestId && s.Chunks>0)
                    {
                        result.Chunks=s.Chunks;result.RecordingSeconds=V2?NpV2SdControl.RecordedSeconds(s):NpV1BleProtocol.RecordedSeconds(s);break;
                    }
                    if(cancelSent&&StopConfirmed(s)){result.Status="Cancelled — not passed";return result;}
                    if(finalizing!=null?finalizing.Elapsed.TotalSeconds>finalizationLimit:wait.Elapsed.TotalSeconds>seconds+180)
                        throw new TimeoutException("The bounded recording has not confirmed completion. Finalization was not interrupted; read status before another operation.");
                    if(!active&&observedRunning&&!s.Busy)throw new IOException("Recording stopped without a complete test receipt.");
                    if(progress!=null)progress(Math.Min(99,(int)(wait.Elapsed.TotalSeconds*100/seconds)),finalizing!=null?
                        "Finalizing / verifying on device · "+NpProgressEstimate.Duration(finalizing.Elapsed.TotalSeconds)+" elapsed":
                        "Recording test · "+NpProgressEstimate.Duration(wait.Elapsed.TotalSeconds)+" elapsed · finite extent, then finalize");
                    await Task.Delay(500);
                }
                if(cancelSent){result.Status="Cancelled — not passed";return result;}
                var after=await ReadSdTestCatalog();
                if(result.CardCidCrc.HasValue&&(after.V2==null||after.V2.CidCrc!=result.CardCidCrc.Value))throw new IOException("The card identity changed during the test.");
                if(after.Capacity!=before.Capacity||after.Records.Count!=before.Records.Count+1)throw new IOException("The recording catalog does not contain exactly one new test record.");
                for(int i=0;i<before.Records.Count;i++)if(before.Records[i].Start!=after.Records[i].Start||before.Records[i].End!=after.Records[i].End)throw new IOException("An earlier catalog extent changed during the test.");
                var added=after.Records.Last();
                if(added.Start!=result.StartLba || result.Session!=0&&added.Session!=0&&result.Session!=added.Session)throw new IOException("The new catalog extent does not match the requested test.");
                if(added.Session!=0)result.Session=added.Session;
                result.EndLba=added.End;result.WrittenBytes=(long)result.Chunks*(V2?46592:65536);
                if(result.Chunks!=result.ExpectedChunks)throw new IOException("Recorded blocks do not match the finite test target.");
                result.RecordingCompleted=true;result.Status="Recording completed — USB readback required";
                result.Details="V"+result.LoggerVersion+" · "+NpCaptureRequest.Ports((int)result.ProbeMask)+" · "+result.SampleHz.ToString("N0")+" samples/s\r\n"+
                    result.RecordingSeconds.ToString("F1")+" s recorded · "+(result.WrittenBytes/1048576.0).ToString("F1")+" MiB · start LBA "+result.StartLba+"\r\n"+
                    "Firmware: "+result.Firmware;
                return result;
            }
            catch(OperationCanceledException){result.Status="Cancelled — not passed";return result;}
            catch(Exception ex){result.Status=submitted?"Test not confirmed":"Test not started";result.Error=ex.Message;return result;}
            finally {busy=false;stopRequested=false;UpdateControls();NotifyState();}
        }
    }

    internal static class NpSdLoggerTest
    {
        internal static uint Blocks(uint mask,uint hz,uint seconds)
        {
            uint ticks=NpV2SdControl.ProfileTicks(mask,hz);
            if(ticks==0||seconds<10||seconds>300)throw new ArgumentException("Unsupported bounded test profile.");
            ulong count=4*((seconds*(ulong)hz*1600+ticks*4UL-1)/(ticks*4UL));
            if(count==0||count>uint.MaxValue)throw new ArgumentException("Test extent is too large.");
            return (uint)count;
        }
        internal static void Verify(string path,NpSdTestResult result,string output,CancellationToken cancel,Action<int,string> progress)
        {
            if(result==null||!result.RecordingCompleted)throw new IOException("Complete a logger recording test first.");
            if(!String.IsNullOrEmpty(result.DeviceUid) && !String.Equals(result.DeviceUid,NpV1Management.UidFromPath(path)??NpV2FirmwareForm.Uid(path),StringComparison.OrdinalIgnoreCase))throw new IOException("Select the same MCU UID as the BLE test.");
            if(result.LoggerVersion==2)
            {
                bool inspectCurrent=false;result.CurrentCardCidCrc=null;
                byte[] serviceBytes=null;
                try {using(var control=new NpSystemInfoControl(path))serviceBytes=control.Read(0xc0,0x6e,0,32);}
                catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1||ex.NativeErrorCode==31) { }
                if(serviceBytes!=null)
                {
                    var service=NpV2ServiceStatus.Parse(serviceBytes);
                    if(service.Valid){service.RequireUid(NpV2FirmwareForm.Uid(path));inspectCurrent=(service.Capabilities&NpUsbCatalogRepair.V2Capability)!=0;}
                }
                if(inspectCurrent)
                {
                    using(var inspection=NpUsbCatalogRepair.Open(path,()=>!cancel.IsCancellationRequested,()=>cancel.IsCancellationRequested))
                        result.CurrentCardCidCrc=inspection.Inspect().CidCrc;
                    if(result.CardCidCrc.HasValue&&result.CurrentCardCidCrc!=result.CardCidCrc)throw new IOException("The current USB card differs from the BLE test card.");
                }
                result.Details+="\r\nCurrent USB card identity: "+(result.CurrentCardCidCrc.HasValue?"CID CRC "+result.CurrentCardCidCrc.Value.ToString("X8"):"not exposed by this firmware");
            }
            // The user selects the USB path explicitly. Older V1 has no BLE UID;
            // match its fresh session, profile, capacity and exact extent as well.
            using(var card=new NpDownloadStream(path))
            {
                if(!card.TransportDescription.StartsWith("High-speed USB",StringComparison.Ordinal))throw new IOException("High-speed USB is required for logger test readback.");
                if(card.Length/512!=result.CardCapacitySectors)throw new IOException("The card capacity differs from the recorded test.");
                var record=NpDownloadCatalog.Read(card).SingleOrDefault(r=>r.Start==result.StartLba);
                if(record==null||!record.Downloadable||!record.Complete||record.V2!=(result.LoggerVersion==2)||record.ProbeMask!=result.ProbeMask||record.ApHz!=result.SampleHz||
                    record.Chunks!=result.Chunks||result.Session!=0&&record.Session!=result.Session)throw new IOException("The selected USB recording does not match the completed test.");
                if(record.End>result.EndLba)throw new IOException("The committed recording extends beyond the confirmed reservation.");
                result.Session=record.Session;
                byte[] header=NpDownloadCatalog.Sector(card,record.Start+3);
                byte[] footer=NpDownloadCatalog.Sector(card,record.Start+4);
                var clock=Stopwatch.StartNew();
                var exported=NpCe64Export.ExportCalibrated(card,record.Start,record.End,output,()=>cancel.IsCancellationRequested,p=>{
                    if(progress!=null)progress(p,"Reading and validating stored data · "+NpProgressEstimate.Remaining(p,100,clock.Elapsed.TotalSeconds,true));
                },null);
                if(record.V2)
                {
                    CheckV2Readback(result,header,exported);
                    result.RecordingSeconds=record.V2DurationSeconds;
                }
                else
                {
                    CheckV1Readback(result,header,footer,File.ReadAllLines(Path.Combine(output,"capture_status.txt")));
                    result.LossCountersAvailable=System.Text.Encoding.ASCII.GetString(footer,320,8)=="NPLOSS01" && BitConverter.ToUInt32(footer,328)==1;
                    if(result.LossCountersAvailable)
                    {
                        foreach(int offset in new[]{332,336,340,344,348,384})if(BitConverter.ToUInt32(footer,offset)!=0)throw new IOException("Recording loss counters are nonzero.");
                        foreach(int offset in new[]{360,368,376})if(BitConverter.ToUInt64(footer,offset)!=0)throw new IOException("Recording skipped source bytes.");
                    }
                    uint maximum=BitConverter.ToUInt32(footer,76);result.MaxWriteMs=maximum==0?(double?)null:maximum;
                    result.Details+="\r\nLongest recorded write transaction: "+(result.MaxWriteMs.HasValue?result.MaxWriteMs.Value.ToString("F1")+" ms":"not reported")+" (not a CMD25 histogram).";
                }
                result.VerifiedBytes=(long)record.Chunks*(record.V2?46592:65536);result.ReadSeconds=clock.Elapsed.TotalSeconds;
                result.Status=result.LossCountersAvailable?"Passed committed stored continuity":"Stored checks passed — loss counters unavailable";
                result.Details+="\r\nFull stored-data framing readback: "+(result.VerifiedBytes/1048576.0).ToString("F1")+" MiB.\r\n"+
                    "USB readback is matched by MCU (where reported), session, profile and extent; it is not a reusable card certification.";
            }
        }
        internal static void CheckV2Readback(NpSdTestResult result,byte[] header,NpV2SdDatExport.Result exported)
        {
            if(exported==null)throw new IOException("V2 export did not return integrity evidence.");
            result.StoredSourceGaps=exported.SourceGapCount;result.MissingSourceTicks=exported.MissingSourceTicks;result.DroppedBlocks=exported.DroppedBlocks;
            result.LossCountersAvailable=exported.StoppedLossStatsPresent;
            result.OverflowEvents=exported.StoppedLossStatsPresent?(uint?)exported.OverflowEvents:null;
            result.ProducerDiscardedTicks=exported.StoppedLossStatsPresent?(ulong?)exported.ProducerDiscardedTicks:null;
            var cid=new byte[16];Buffer.BlockCopy(header,80,cid,0,16);result.RecordedCardCidCrc=NpSdControl.Crc(cid,16);
            if(result.CardCidCrc.HasValue&&result.RecordedCardCidCrc!=result.CardCidCrc)throw new IOException("The saved recording belongs to a different card CID.");
            if(!exported.RecordingComplete||exported.SourceGapCount!=0||exported.MissingSourceTicks!=0||exported.DroppedBlocks!=0||exported.OverflowEvents!=0||exported.ProducerDiscardedTicks!=0)
                throw new IOException("Readback reports incomplete data or source loss. Observed counters are retained in the report.");
        }
        internal static void CheckV1Readback(NpSdTestResult result,byte[] header,byte[] footer,string[] lines)
        {
            if(!lines.Contains("complete=True")||!lines.Contains("failure=none")||!lines.Contains("download_complete=True"))
                throw new IOException("Readback reports incomplete data or neural loss.");
            if(result.ExpectedChunks==0||result.Chunks!=result.ExpectedChunks||NpSdGeometry.U(footer,68)!=result.ExpectedChunks||
                NpSdGeometry.U(header,44)!=result.RequestedEndLba||NpSdGeometry.U(footer,44)!=result.RequestedEndLba)
                throw new IOException("Stored V1 extent does not match the finite test target.");
            var fields=new Dictionary<string,string>();
            foreach(string line in lines){int at=line.IndexOf('=');if(at>0){string key=line.Substring(0,at);if(fields.ContainsKey(key))throw new IOException("Repeated readback field: "+key);fields.Add(key,line.Substring(at+1));}}
            Func<string,long> number=key=>{string value;long n;if(!fields.TryGetValue(key,out value)||!Int64.TryParse(value,out n)||n<0)throw new IOException("Missing or invalid readback field: "+key);return n;};
            int ports=NpSdGeometry.Count(result.ProbeMask),index=0;
            for(int p=1;p<=3;p++)if((result.ProbeMask&(1U<<(p-1)))!=0)
            {
                string prefix="np"+p;
                foreach(string suffix in new[]{"_frame_errors","_counter_errors","_lfp_phase_errors","_relocks"})
                    if(number(prefix+suffix)!=0)throw new IOException("Readback framing/counter check failed: "+prefix+suffix);
                long frames=number(prefix+"_frames"),ap=number(prefix+"_ap_rows"),lfp=number(prefix+"_lfp_rows"),startup=number(prefix+"_startup_discarded_bytes");
                if(frames==0||ap==0||lfp==0)throw new IOException("No checked samples for "+prefix);
                bool paired=NpSdGeometry.U(header,8)>=5;
                long native=paired?((result.Chunks+(long)ports-1-index)/ports)*NpSdGeometry.U(header,32)*4:
                    (long)result.Chunks*NpSdGeometry.U(header,32)*4/NpSdGeometry.U(header,20);
                long partialFrame=native-startup-frames*60,terminalAp=native-startup-ap*780;
                if(partialFrame<0||partialFrame>=60||terminalAp<0||terminalAp>=780)throw new IOException("Inconsistent decoded boundary counts for "+prefix);
                result.Details+="\r\n"+prefix+": startup alignment "+startup+" bytes; terminal partial AP row "+terminalAp+
                    " bytes (including "+partialFrame+" incomplete frame bytes); initial partial LFP "+number(prefix+"_startup_discarded_lfp_superframes")+
                    " AP rows; terminal partial LFP "+((ap+number(prefix+"_initial_lfp_slot"))%12)+" AP rows.";
                index++;
            }
            result.Details+="\r\nBoundary trims are disclosed separately; stored continuity does not prove every physical sample was captured.";
        }
    }
}
