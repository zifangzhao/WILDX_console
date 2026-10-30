using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CE32_console;
using Microsoft.Win32.SafeHandles;

namespace USB512_interface {
    internal sealed class NpSdUsbGapStats {
        public double? CardMaximumWriteMs,CardMeanWriteMs,MaximumServiceMs;
        public string CardTimingSource;
        public uint? DeviceSourceGaps,DeviceOverflows,ReadbackSourceGaps,ReadbackOverflows;
        public ulong? DeviceMissingTicks,DeviceDroppedBlocks,DeviceDiscardedTicks,ReadbackMissingTicks,ReadbackDroppedBlocks,ReadbackDiscardedTicks;
        public bool NeuralDistributionKnown;
        public uint? NeuralGapCount,NeuralSourceHz,MissingStoredBlocks,StorageBreaks,V1LossEventsLowerBound;
        public ulong? NeuralMissingTicks;
        public ulong[] NeuralGapTicks,V1SkippedLaneBytesLowerBound;
        public decimal? NeuralTotalMissingMs,NeuralMaximumGapMs,NeuralMeanGapMs,NeuralP95GapMs,NeuralP99GapMs;
        public string NeuralGapError,ReadbackCounterScope;

        internal void Device(NpSdUsbTestReport report){
            DeviceSourceGaps=report.SourceGaps;DeviceMissingTicks=report.MissingTicks;DeviceDroppedBlocks=report.Drops;
            DeviceOverflows=report.Overflows;DeviceDiscardedTicks=report.DiscardedTicks;
            CardMaximumWriteMs=report.MaximumWriteMicroseconds/1000.0;MaximumServiceMs=report.MaximumServiceMicroseconds/1000.0;
            if(report.WriteCount>0&&report.TotalWriteMicroseconds.HasValue)CardMeanWriteMs=report.TotalWriteMicroseconds.Value/(report.WriteCount.Value*1000.0);
            if(report.Has(NpSdUsbTestValidity.PerformanceKnown))CardTimingSource="Device write-transaction counters; not inferred from neural gaps or a card-busy histogram.";
        }
        internal void V1Footer(byte[] footer){
            if(footer==null||footer.Length!=512||BitConverter.ToUInt32(footer,508)!=NpSdControl.Crc(footer,508))return;
            uint maximum=BitConverter.ToUInt32(footer,76);
            if(!CardMaximumWriteMs.HasValue&&maximum!=0&&maximum!=UInt32.MaxValue){CardMaximumWriteMs=maximum;CardTimingSource="V1 sealed footer maximum write transaction (milliseconds); not a card-busy histogram.";}
            if(System.Text.Encoding.ASCII.GetString(footer,320,8)!="NPLOSS01"||BitConverter.ToUInt32(footer,328)!=1)return;
            V1LossEventsLowerBound=BitConverter.ToUInt32(footer,332);
            V1SkippedLaneBytesLowerBound=new[]{BitConverter.ToUInt64(footer,360),BitConverter.ToUInt64(footer,368),BitConverter.ToUInt64(footer,376)};
            // Lane-byte counters cannot reconstruct individual source gaps.
            NeuralGapError="V1 loss-event and skipped-lane counters are lower bounds; exact neural gap durations are unavailable.";
        }
        internal void V2Readback(NpV2SdDatExport.Result exported){
            if(exported==null){NeuralGapError="No saved segment evidence was returned.";return;}
            ReadbackSourceGaps=exported.SourceGapCount;ReadbackMissingTicks=exported.MissingSourceTicks;ReadbackDroppedBlocks=exported.DroppedBlocks;
            if(exported.StoppedLossStatsPresent){ReadbackOverflows=exported.OverflowEvents;ReadbackDiscardedTicks=exported.ProducerDiscardedTicks;}
            ReadbackCounterScope=exported.StoppedLossStatsComparison;MissingStoredBlocks=exported.MissingStoredBlocks;
            try{
                if(exported.SampleHz!=7500&&exported.SampleHz!=15000&&exported.SampleHz!=20000&&exported.SampleHz!=30000)
                    throw new InvalidDataException("Unknown source tick frequency.");
                if(exported.Segments.Count==0)throw new InvalidDataException("No nonempty saved segments.");
                var gaps=new List<ulong>();ulong sum=0;uint storageBreaks=0;NpV2SdDatExport.Segment previous=null;
                foreach(var segment in exported.Segments){
                    if(segment==null||segment.SourceEndTick<=segment.SourceStartTick||segment.LogicalEndExclusive<=segment.FirstLogicalBlock)
                        throw new InvalidDataException("Invalid saved segment extent.");
                    if(previous!=null){
                        if(segment.SourceStartTick<previous.SourceEndTick||segment.FirstLogicalBlock<previous.LogicalEndExclusive)
                            throw new InvalidDataException("Overlapping or reversed saved segments.");
                        bool storage=segment.FirstLogicalBlock!=previous.LogicalEndExclusive;
                        if(storage){if(!exported.IndexedGridAppend)throw new InvalidDataException("Unexpected logical storage hole.");storageBreaks=checked(storageBreaks+1);}
                        else {ulong gap=segment.SourceStartTick-previous.SourceEndTick;if(gap!=0){gaps.Add(gap);sum=checked(sum+gap);}}
                    }
                    previous=segment;
                }
                StorageBreaks=storageBreaks;
                if((uint)gaps.Count!=exported.SourceGapCount||sum!=exported.MissingSourceTicks)
                    throw new InvalidDataException("Saved segment gap count/tick total disagrees with the exporter.");
                ulong[] ordered=gaps.OrderBy(n=>n).ToArray();decimal ticksPerMs=(decimal)exported.SampleHz*1600/1000;
                NeuralGapTicks=gaps.ToArray();NeuralSourceHz=checked(exported.SampleHz*1600);NeuralGapCount=(uint)gaps.Count;NeuralMissingTicks=sum;
                NeuralTotalMissingMs=sum/ticksPerMs;NeuralMaximumGapMs=ordered.Length==0?0:ordered[ordered.Length-1]/ticksPerMs;
                NeuralMeanGapMs=ordered.Length==0?0:NeuralTotalMissingMs/ordered.Length;
                // Nearest-rank percentiles over actual positive saved-source gaps.
                NeuralP95GapMs=ordered.Length==0?0:ordered[(int)(((long)ordered.Length*95+99)/100)-1]/ticksPerMs;
                NeuralP99GapMs=ordered.Length==0?0:ordered[(int)(((long)ordered.Length*99+99)/100)-1]/ticksPerMs;
                NeuralDistributionKnown=true;
            }catch(Exception ex) when(ex is InvalidDataException||ex is OverflowException){NeuralGapError=ex.Message;}
        }
    }
    internal sealed class NpSdUsbProfileResult {
        public uint SampleHz;
        public decimal RequiredMBs;
        public string Status="Not tested",Error;
        public string SubmissionError;
        public int? SubmissionNativeErrorCode;
        public string OriginalTerminalRawFile,FreshTerminalRawFile;
        public bool ReadbackReadRetired;
        public string ReadbackRetirementError;
        public NpSdSweepEvidence Evidence;
        public NpSdSweepDecision Decision;
        public string Folder;
        public NpSdTestResult Stored;
        public bool SubmitEntered,SubmitAcknowledged,ReadbackCompleted,SafeToContinue;
        public uint[] PreviousSessions;
        public long[] PreviousStarts,PreviousEnds;
        public string[] PreviousCatalog;
        public readonly NpSdUsbGapStats Gaps=new NpSdUsbGapStats();
        public ulong? SavedImuRows,SavedImuObservedRows,SavedImuInitialUnavailableSlots;
        public string SavedImuScope;
    }
    internal sealed class NpSdUsbSweepResult {
        public int Schema=1,Family;
        public string DeviceUid,StartedUtc=DateTime.UtcNow.ToString("O"),Status="Not tested",Error,ReportFolder;
        public uint ProbeMask,Seconds;
        public uint? SupportedSampleHz;
        public readonly List<NpSdUsbProfileResult> Profiles=new List<NpSdUsbProfileResult>();
        public string Scope="Bounded real SD recordings with the selected probes, installed firmware and card. Full stored readback is required. A pass is for the tested duration, not card endurance or every physical ADC bit. Test recordings remain on SD.";
    }
    internal sealed class NpSdUsbTestController {
        internal sealed class CapabilityUnavailableException:NotSupportedException {
            internal CapabilityUnavailableException():base("USB SD recording tests need matching APP and USB firmware. Update the full bundled firmware in Advanced."){}
        }
        sealed class Control : IDisposable {
            SafeFileHandle file;IntPtr usb;
            internal Control(string path){
                file=NpDownloadNative.CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0x40000080,IntPtr.Zero);
                try{if(file.IsInvalid||!NpDownloadNative.WinUsbInitialize(file,out usb))throw new Win32Exception(Marshal.GetLastWin32Error());
                    NpDownloadNative.USB_INTERFACE_DESCRIPTOR descriptor;
                    if(!NpDownloadNative.WinUsbQueryInterfaceSettings(usb,0,out descriptor))throw new Win32Exception(Marshal.GetLastWin32Error());
                    bool highSpeed=false;for(byte p=0;p<descriptor.bNumEndpoints;p++){
                        NpDownloadNative.WINUSB_PIPE_INFORMATION pipe;
                        if(!NpDownloadNative.WinUsbQueryPipe(usb,0,p,out pipe))throw new Win32Exception(Marshal.GetLastWin32Error());
                        if(pipe.PipeType==NpDownloadNative.USBD_PIPE_TYPE.Bulk&&pipe.MaximumPacketSize==512)highSpeed=true;
                    }
                    if(!highSpeed)throw new IOException("High-speed USB is required for the SD card test.");
                }catch{Dispose();throw;}
            }
            internal byte[] Read(byte request,int bytes){return Transfer(0xc0,request,new byte[bytes]);}
            internal void Submit(byte[] bytes){Transfer(0x40,NpSdUsbTestWire.SubmitRequest,bytes);}
            internal void CancelRead(){Transfer(0x40,0x62,new byte[0]);}
            byte[] Transfer(byte type,byte request,byte[] bytes){
                uint transferred;var setup=new NpDownloadNative.WINUSB_SETUP_PACKET {RequestType=type,Request=request,Length=(ushort)bytes.Length};
                if(!NpDownloadNative.WinUsbControlTransfer(usb,setup,bytes,(uint)bytes.Length,out transferred,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
                if(transferred!=bytes.Length)throw new IOException("Short USB SD-test control transfer; command outcome may be unknown.");return bytes;
            }
            internal uint Capabilities(int family,string uid){
                var service=Read(0x6e,32);uint capabilities;
                if(family==1)capabilities=NpV1Management.ParseSplitStatus(service,uid).Capabilities;
                else {var status=NpV2ServiceStatus.Parse(service);status.RequireUid(uid);capabilities=status.Capabilities;}
                if((capabilities&NpSdUsbTestWire.Capability)==0)throw new CapabilityUnavailableException();
                return capabilities;
            }
            internal NpSdUsbTestReport Report(int family,string uid){
                Capabilities(family,uid);
                return NpSdUsbTestWire.ParseReport(Read(NpSdUsbTestWire.GetReportRequest,NpSdUsbTestWire.ReportBytes),family,uid);
            }
            public void Dispose(){if(usb!=IntPtr.Zero){NpDownloadNative.WinUsbFree(usb);usb=IntPtr.Zero;}if(file!=null){file.Dispose();file=null;}}
        }
        internal readonly int Family;
        internal readonly string Uid;
        string path;
        internal NpSdUsbTestController(string selectedPath){
            path=selectedPath;Uid=NpV1Management.UidFromPath(path);Family=Uid==null?2:1;
            if(Uid==null)Uid=NpV2FirmwareForm.Uid(path);
            if(Uid==null)throw new IOException("Select a Neuropixels USB logger before testing its SD card.");
        }
        async Task DownloadMode(CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();
            if(Family==1)path=await Task.Run(()=>NpV1Management.EnsureDownload(path,()=>cancel.IsCancellationRequested));
            else path=await NpV2FirmwareForm.EnsureDownload(path,()=>cancel.IsCancellationRequested);
        }
        internal async Task<NpSdUsbTestReport> Inspect(CancellationToken cancel){
            await DownloadMode(cancel);return await Task.Run(()=>FreshReport(cancel));
        }
        internal static bool CatalogInspectSupported(int family,uint capabilities){return family==1?(capabilities&NpUsbCatalogRepair.V1Capability)!=0:family==2&&(capabilities&NpUsbCatalogRepair.V2Capability)!=0;}
        internal static bool InspectionMatches(NpUsbCatalogRepair.Receipt inspection,NpSdUsbTestReport report){
            return inspection!=null&&report!=null&&inspection.Generation==report.Family&&inspection.Operation==NpUsbCatalogRepair.InspectRequest&&
                inspection.Phase==3&&inspection.Error==0&&inspection.Nonce!=0&&inspection.InspectedNonce==inspection.Nonce&&
                report.Capacity==inspection.Capacity&&report.Frontier==inspection.NextLba&&report.CatalogFlags==inspection.Flags&&report.Cid!=null&&
                String.Concat(report.Cid.Select(w=>w.ToString("X8")))==inspection.Cid;
        }
        NpSdUsbTestReport FreshReport(CancellationToken cancel){
            cancel.ThrowIfCancellationRequested();uint capabilities;using(var usb=new Control(path))capabilities=usb.Capabilities(Family,Uid);
            NpUsbCatalogRepair.Receipt inspected=null;
            if(CatalogInspectSupported(Family,capabilities))using(var catalog=NpUsbCatalogRepair.Open(path,()=>!cancel.IsCancellationRequested,()=>cancel.IsCancellationRequested))inspected=catalog.Inspect();
            else if(Family==2)throw new NotSupportedException("This V2 USB service cannot provide the read-only catalog inspection required for a recording test.");
            var elapsed=Stopwatch.StartNew();
            do{
                cancel.ThrowIfCancellationRequested();NpSdUsbTestReport report;using(var usb=new Control(path))report=usb.Report(Family,Uid);
                if(inspected==null||InspectionMatches(inspected,report)&&(inspected.Flags!=0||report.Has(NpSdUsbTestValidity.CurrentCardIdentity|
                    NpSdUsbTestValidity.FreshCatalog|NpSdUsbTestValidity.SourceQuiet|NpSdUsbTestValidity.SdQuiet|NpSdUsbTestValidity.AppendAvailable)))return report;
                Thread.Sleep(50); // A3 publishes its foreground refresh asynchronously.
            }while(elapsed.ElapsedMilliseconds<2500);
            throw new IOException("The test report did not match the fresh read-only catalog inspection. No Start was sent.");
        }
        internal static byte[] RetireCatalogRead(Func<byte[]> readStatus,Action cancelRead,uint capacity,int maximumPolls=250){
            var before=readStatus();ValidateDownloadStatus(before,capacity);
            if(BitConverter.ToUInt32(before,16)!=0)throw new IOException("The closed catalog read is not idle; no new control owner was cancelled.");
            uint epoch=unchecked(BitConverter.ToUInt32(before,24)+1),sequence=BitConverter.ToUInt32(before,20),limit=BitConverter.ToUInt32(before,12);
            // Random reads can leave an idle prefetch cache without an active
            // sequential range. Retire this owned read session exactly once.
            cancelRead();
            for(int i=0;i<maximumPolls;i++){
                var after=readStatus();ValidateDownloadStatus(after,capacity);uint state=BitConverter.ToUInt32(after,16);
                if(BitConverter.ToUInt32(after,24)!=epoch||BitConverter.ToUInt32(after,20)!=sequence||BitConverter.ToUInt32(after,12)!=limit||state!=0&&state!=4)
                    throw new IOException("Catalog read retirement epoch/sequence/geometry changed. No Start was sent.");
                if(state==0)return after;
                Thread.Sleep(10);
            }
            throw new IOException("Catalog read retirement did not reach matching idle. No cancel or Start was replayed.");
        }
        static void ValidateDownloadStatus(byte[] status,uint capacity){
            if(status==null||status.Length!=32||BitConverter.ToUInt32(status,0)!=0x3144504e||BitConverter.ToUInt32(status,4)!=1||
                BitConverter.ToUInt32(status,8)!=capacity||BitConverter.ToUInt32(status,12)==0||BitConverter.ToUInt32(status,12)>1020||BitConverter.ToUInt32(status,28)!=1)
                throw new InvalidDataException("Catalog read retirement lacks matching NPD1 identity/capacity/geometry.");
        }
        Task RetireClosedCatalog(CancellationToken cancel,uint capacity){return Task.Run(()=>{
            cancel.ThrowIfCancellationRequested();using(var usb=new Control(path)){
                usb.Capabilities(Family,Uid); // Exact selected peer before the one read-session cancel.
                RetireCatalogRead(()=>usb.Read(0x60,32),()=>usb.CancelRead(),capacity);
            }
        });}
        internal static async Task<NpSdUsbTestReport> RefreshAfterCatalog(Func<Task> readAndClose,Func<Task> retireRead,Func<Task<NpSdUsbTestReport>> inspect,NpSdUsbTestReport previous,bool explicitReconcile){
            await readAndClose();await retireRead();var fresh=await inspect();
            if(!SameCard(previous,fresh)||previous.Frontier!=fresh.Frontier)throw new IOException("The SD card identity/frontier changed after catalog read. No Start was sent.");
            RequireReady(fresh,explicitReconcile);return fresh;
        }
        internal static async Task<NpSdUsbTestReport> RefreshTerminal(byte[] plan,NpSdUsbTestReport original,NpSdUsbTestReport before,
            Func<Task<NpSdUsbTestReport>> inspect,Func<Task<NpSdUsbTestReport>> readReport,int maximumPolls=50){
            if(!NpSdUsbTestWire.ValidateMatch(plan,original))throw new IOException("The original terminal lost its exact test ticket.");
            if(original.Phase==NpSdUsbTestPhase.InterruptedUnknown)return original;
            if(original.Phase!=NpSdUsbTestPhase.Finished&&original.Phase!=NpSdUsbTestPhase.RejectedNoStart)throw new IOException("The test has not reached a terminal result.");
            if(original.Capacity.HasValue&&!SameCard(before,original))throw new IOException("The card changed before terminal refresh. No next Start was sent.");
            if(SafeTerminal(original)&&SameCard(before,original))return original;
            var fresh=await inspect();var initial=original.Raw;
            for(int i=0;i<maximumPolls;i++){
                if(!NpSdUsbTestWire.ValidateMatch(plan,fresh))throw new IOException("Terminal refresh returned a foreign test ticket. No next Start was sent.");
                var raw=fresh.Raw;
                // Only live card/catalog/quiet/append facts may refresh. The
                // accepted result, recorded profile/extents and counters stay immutable.
                for(int word=0;word<63;word++){
                    if(word>=20&&word<=26)continue;
                    uint difference=BitConverter.ToUInt32(initial,word*4)^BitConverter.ToUInt32(raw,word*4);
                    if(word==6)difference&=~0x2fU;
                    if(difference!=0)throw new IOException("Terminal refresh changed retained test evidence. Original raw report preserved.");
                }
                if(fresh.Capacity.HasValue&&!SameCard(before,fresh))throw new IOException("The card changed during terminal refresh. No next Start was sent.");
                if(SafeTerminal(fresh)&&SameCard(before,fresh))return fresh;
                if(i+1<maximumPolls){await Task.Delay(50);fresh=await readReport();}
            }
            throw new IOException("Matching terminal lacks fresh quiet/writable card facts. Original result preserved; no next Start was sent.");
        }
        internal static void RetireOwnedReadback(NpSdUsbProfileResult profile,bool opened,Action retire){
            if(!opened)return;
            try{retire();profile.ReadbackReadRetired=true;}
            catch(Exception ex){profile.SafeToContinue=false;profile.ReadbackRetirementError=ex.Message;
                profile.Error=(String.IsNullOrEmpty(profile.Error)?"":profile.Error+"; ")+"Owned readback retirement unconfirmed: "+ex.Message;}
        }
        internal static void RecordRunFailure(NpSdUsbSweepResult result,NpSdUsbProfileResult active,Exception error){
            result.Status="Not confirmed — sweep stopped";
            result.Error=(active==null&&result.Profiles.Count>0?"Next-profile preparation failed: ":"")+error.Message;
            if(active!=null){active.Status="Not confirmed";active.Error=error.Message;active.Evidence=NpSdSweepEvidence.TransportOrUnknownDelivery;active.Decision=NpSdSweepDecision.Inconclusive;}
        }
        string ReconnectedPath(){
            var found=NpDownloadNative.EnumerateNpPaths().Where(p=>String.Equals(Family==1?NpV1Management.UidFromPath(p):
                NpV1Management.UidFromPath(p)==null?NpV2FirmwareForm.Uid(p):null,Uid,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(found.Length>1)throw new IOException("The selected MCU UID is ambiguous after the test handoff.");return found.Length==1?found[0]:null;
        }
        static bool SameCard(NpSdUsbTestReport a,NpSdUsbTestReport b){return a.Family==b.Family&&a.Uid==b.Uid&&a.Capacity.HasValue&&a.Capacity==b.Capacity&&a.Cid!=null&&b.Cid!=null&&a.Cid.SequenceEqual(b.Cid);}
        internal static bool SafeTerminal(NpSdUsbTestReport report){
            if(report==null)return false;
            var flags=NpSdUsbTestValidity.CurrentCardIdentity|NpSdUsbTestValidity.FreshCatalog|NpSdUsbTestValidity.SourceQuiet|
                NpSdUsbTestValidity.SdQuiet|NpSdUsbTestValidity.AppendAvailable|NpSdUsbTestValidity.PriorProfileRestored;
            return report.CatalogFlags==0&&report.Has(flags)&&(report.Phase==NpSdUsbTestPhase.Finished&&report.Has(NpSdUsbTestValidity.ClosedTerminal)||
                report.Phase==NpSdUsbTestPhase.RejectedNoStart&&report.Has(NpSdUsbTestValidity.RejectedNoMediaMutation));
        }
        internal static void RequireReady(NpSdUsbTestReport report,bool explicitReconcile){
            if(report==null)throw new IOException("A fresh USB SD-test report is required.");
            var ready=NpSdUsbTestValidity.CurrentCardIdentity|NpSdUsbTestValidity.FreshCatalog|NpSdUsbTestValidity.SourceQuiet|
                NpSdUsbTestValidity.SdQuiet|NpSdUsbTestValidity.AppendAvailable;
            bool initial=report.Phase==NpSdUsbTestPhase.None||report.Phase==NpSdUsbTestPhase.InterruptedUnknown;
            // The APP captures its prior profile after handoff. A fresh USB
            // initial report need not claim it; completed tests must restore it.
            if(!report.Has(ready)||report.CatalogFlags!=0||!(initial?explicitReconcile:SafeTerminal(report)))
                throw new IOException("The previous test is not freshly reconciled and quiet. No Start was sent or replayed.");
        }
        internal static byte[] PlanForProfile(NpSdUsbTestReport report,byte[] inner,uint seconds,bool firstManualProfile,byte[] previousPlan){
            RequireReady(report,firstManualProfile);
            if(!firstManualProfile&&(previousPlan==null||!NpSdUsbTestWire.ValidateMatch(previousPlan,report)))
                throw new IOException("Automatic fallback lost the exact previous test ticket. No next Start was sent.");
            return NpSdUsbTestWire.BuildPlan(report,inner,seconds,firstManualProfile);
        }
        internal static Task<NpSdUsbTestReport> SubmitOnce(NpSdUsbProfileResult profile,Func<Task> submit,Func<Task<NpSdUsbTestReport>> observe){return SubmitOnce(profile,submit,observe,null);}
        internal static async Task<NpSdUsbTestReport> SubmitOnce(NpSdUsbProfileResult profile,Func<Task> submit,Func<Task<NpSdUsbTestReport>> observe,Action saveFailure){
            try{await submit();profile.SubmitAcknowledged=true;}
            catch(Exception ex) when(ex is IOException||ex is Win32Exception||ex is OperationCanceledException){
                if(!profile.SubmitEntered)throw;
                profile.SubmissionError=ex.Message;profile.SubmissionNativeErrorCode=(ex as Win32Exception)?.NativeErrorCode;
                profile.Error="Submission outcome unconfirmed: "+ex.Message;
                try{if(saveFailure!=null)saveFailure();}catch(Exception evidenceError){Trace.WriteLine("SD-test submission evidence: "+evidenceError.Message);}
            }
            // After possible delivery, finite owner retirement is observed even
            // when the user cancels. There is no second OUT or replacement plan.
            return await observe();
        }
        internal static string CatalogIdentity(NpDownloadRecord r){
            // RecoveryNotice may describe the catalog-wide checkpoint/frontier,
            // which advances on a valid append; it is not stored record identity.
            return new JavaScriptSerializer().Serialize(new object[]{r.Start,r.End,r.Session,r.Chunks,r.ApHz,r.LfpHz,r.State,r.Fault,r.ProbeMask,r.LaneBytes,r.TicksPerBlock,
                r.FormatVersion,r.UnderlyingFormatVersion,r.CameraHz,r.MicrophoneHz,r.LogicalPrefixBlocks,r.LogicalHighWaterBlocks,r.PhysicalSectors,
                r.ValidationError,r.CatalogEnd,r.CommittedEnd,r.OpenTail});
        }
        internal static void RequirePreviousCatalog(string[] previous,NpDownloadRecord[] current){
            if(previous==null||current==null||current.Length!=previous.Length+1)throw new InvalidDataException("The catalog did not append exactly one test recording.");
            for(int i=0;i<previous.Length;i++)if(previous[i]!=CatalogIdentity(current[i]))throw new InvalidDataException("A previous recording's stored identity, geometry or extent changed during the test.");
        }
        internal static NpFirstSyncSdBound ValidateStoredTicket(byte[] utc,NpSdUsbTestReport terminal,NpDownloadRecord record,int family,string uid){
            if(terminal==null||record==null||terminal.Family!=family||!String.Equals(terminal.Uid,uid,StringComparison.OrdinalIgnoreCase)||!terminal.Session.HasValue||
                !terminal.ReserveStart.HasValue||!terminal.ReserveEnd.HasValue||record.Start!=terminal.ReserveStart||record.End>terminal.ReserveEnd||record.V2!=(family==2)||
                record.Session!=terminal.Session||record.ProbeMask!=terminal.Mask||record.ApHz!=terminal.Hz||terminal.Ticket==0)
                throw new IOException("The stored test session/profile does not match the retained ticket.");
            NpFirstSyncSdBound bound;
            try{bound=NpFirstSyncSd.ReadSector(utc,family,record.Session,record.ApHz,uid,terminal.Ticket);}
            catch(InvalidDataException ex){throw new IOException("The on-card NPSD ticket/UID could not be bound; readback was not started.",ex);}
            if(bound==null||bound.Receipt.Mask!=record.ProbeMask)throw new IOException("The on-card NPSD mask/ticket binding is unavailable; readback was not started.");
            return bound;
        }
        internal static bool V1CleanLossCounters(byte[] footer){
            if(footer==null||footer.Length!=512||System.Text.Encoding.ASCII.GetString(footer,320,8)!="NPLOSS01"||BitConverter.ToUInt32(footer,328)!=1||
                BitConverter.ToUInt32(footer,508)!=NpSdControl.Crc(footer,508))return false;
            foreach(int at in new[]{332,336,340,344,348,384})if(BitConverter.ToUInt32(footer,at)!=0)return false;
            foreach(int at in new[]{360,368,376})if(BitConverter.ToUInt64(footer,at)!=0)return false;
            return true;
        }
        static Dictionary<string,string> StatusFields(string path){
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string line in File.ReadLines(path)){int at=line.IndexOf('=');if(at<1)continue;
                string key=line.Substring(0,at);if(fields.ContainsKey(key))throw new InvalidDataException("Repeated IMU status field: "+key);fields.Add(key,line.Substring(at+1));}
            return fields;
        }
        static string Field(Dictionary<string,string> fields,string key){string value;if(!fields.TryGetValue(key,out value))throw new InvalidDataException("Missing IMU status field: "+key);return value;}
        static ulong Number(Dictionary<string,string> fields,string key){ulong n;if(!UInt64.TryParse(Field(fields,key).Split(';')[0],NumberStyles.None,CultureInfo.InvariantCulture,out n))throw new InvalidDataException("Invalid IMU status count: "+key);return n;}
        internal static void CheckV1ScheduledImu(string output,NpSdUsbProfileResult profile){
            var status=StatusFields(Path.Combine(output,"imu_grid_status.txt"));var encoding=StatusFields(Path.Combine(output,"analogin_encoding.txt"));
            if(!Field(status,"selected").Equals("true",StringComparison.OrdinalIgnoreCase)||Field(status,"carrier")!="NSG1"||
                !Field(status,"final_parent_verified").Equals("true",StringComparison.OrdinalIgnoreCase)||Field(status,"error")!="none"||
                Field(encoding,"schema")!="NP_IMU_NSG1_GRID_V1"||Number(encoding,"output_grid_hz")!=1250)
                throw new InvalidDataException("Scheduled V1 IMU readback lacks a verified parent or reports an export error.");
            ulong rows=Number(encoding,"rows"),observed=Number(encoding,"observed"),firstSlot=Number(encoding,"first_observed_slot"),missing=Number(encoding,"missing");
            if(rows==0||observed==0||Number(status,"rows")!=rows||rows>(ulong)Int64.MaxValue/32||
                new FileInfo(Path.Combine(output,"analogin.dat")).Length!=(long)rows*32||new FileInfo(Path.Combine(output,"analogin_validity.dat")).Length!=(long)rows)
                throw new InvalidDataException("Scheduled V1 IMU data/validity extents are missing or inconsistent.");
            ulong actualObserved=0,firstRow=0,row=0;bool ag=false,mag=false;
            using(var validity=File.OpenRead(Path.Combine(output,"analogin_validity.dat"))){int flags;while((flags=validity.ReadByte())>=0){
                if((flags&~63)!=0||(flags&32)!=0||flags!=0&&(flags&1)==0)throw new InvalidDataException("Scheduled V1 IMU row has an error or invalid validity mask.");
                if((flags&1)!=0){if(actualObserved==0)firstRow=row;actualObserved++;ag|=(flags&2)!=0;mag|=(flags&4)!=0;}
                else if(actualObserved!=0)throw new InvalidDataException("Scheduled V1 IMU readback has a missing slot after its first observed row.");row++;
            }}
            if(actualObserved!=observed||!ag||!mag)throw new InvalidDataException("Scheduled V1 IMU lacks saved AG/MAG observations or its counts disagree.");
            ulong fileRows=0,slotEnd=0;bool mappedFirst=false,haveSegment=false;
            using(var segments=new StreamReader(Path.Combine(output,"imu_grid_segments.csv"))){
                if(segments.ReadLine()!="first_slot,row_count,first_file_row")throw new InvalidDataException("Invalid V1 IMU segment header.");
                string line;while((line=segments.ReadLine())!=null){var c=line.Split(',');ulong slot,count,fileRow;
                    if(c.Length!=3||!UInt64.TryParse(c[0],out slot)||!UInt64.TryParse(c[1],out count)||!UInt64.TryParse(c[2],out fileRow)||
                        count==0||fileRow!=fileRows||count>rows-fileRows||haveSegment&&(slot<slotEnd||fileRows>firstRow&&slot!=slotEnd))
                        throw new InvalidDataException("Invalid or discontinuous V1 IMU saved segments.");
                    if(firstRow>=fileRows&&firstRow-fileRows<count){if(checked(slot+firstRow-fileRows)!=firstSlot)throw new InvalidDataException("V1 IMU first observed ordinal mismatch.");mappedFirst=true;}
                    fileRows=checked(fileRows+count);slotEnd=checked(slot+count);haveSegment=true;
                }
            }
            if(fileRows!=rows||!mappedFirst||Number(encoding,"slot_extent")!=slotEnd||missing!=firstSlot)
                throw new InvalidDataException("V1 IMU saved ordinal extent or initial unavailable coverage disagrees.");
            ulong gapTotal=0;using(var gaps=new StreamReader(Path.Combine(output,"imu_grid_gaps.csv"))){
                if(gaps.ReadLine()!="first_slot,slot_count,reason")throw new InvalidDataException("Invalid V1 IMU gap header.");
                string line;while((line=gaps.ReadLine())!=null){var c=line.Split(',');ulong first,count;
                    if(c.Length!=3||!UInt64.TryParse(c[0],out first)||!UInt64.TryParse(c[1],out count)||first!=gapTotal||count==0||checked(first+count)>firstSlot||
                        (c[2]!="transport_gap"&&c[2]!="transport_gap_sparse"&&c[2]!="producer_missing"))throw new InvalidDataException("V1 IMU reports a missing slot after its first observation or a malformed gap.");gapTotal=checked(gapTotal+count);}
            }
            if(gapTotal!=missing)throw new InvalidDataException("V1 IMU missing-slot status and saved gap records disagree.");
            profile.SavedImuRows=rows;profile.SavedImuObservedRows=observed;profile.SavedImuInitialUnavailableSlots=firstSlot;
            profile.SavedImuScope="Actual saved scheduled NSG1 rows, AG/MAG validity and retained-slot continuity checked. Initial unavailable slots disclosed; baseline/lifetime coverage, physical conversion phase and terminal tail remain unknown. Exact device drop/discard/scheduler counters remain unknown when unreported.";
        }
        internal static void CheckCleanCompletion(int family,NpSdUsbTestReport terminal,bool complete,byte[] footer,string output,NpSdUsbProfileResult profile){
            if(!complete)throw new IOException("The test recording did not complete.");
            if(family==1){if(!V1CleanLossCounters(footer))throw new IOException("Stored V1 loss counters or skipped-lane bytes are nonzero or unqualified.");CheckV1ScheduledImu(output,profile);return;}
            if(family!=2||terminal.Drops!=0||terminal.Overflows!=0||terminal.DiscardedTicks!=0||
                !terminal.Has(NpSdUsbTestValidity.CaptureCountersKnown|NpSdUsbTestValidity.AuxKnown)||terminal.AuxQueueDrops!=0||terminal.AuxSchedulerMisses!=0)
                throw new IOException("The test lacks clean neural/IMU completion evidence.");
        }
        static void Json(string file,object value){NpMetadataFile.WriteText(file,new JavaScriptSerializer {MaxJsonLength=8*1024*1024}.Serialize(value)+Environment.NewLine);}
        static ulong Ticket(ulong previous){ulong value;do{value=BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);}while(value==0||value==previous);return value;}
        internal static uint V1EndLba(NpSdUsbTestReport before,uint mask,uint hz,uint seconds){
            if(before==null||before.Family!=1||!before.Supports(mask,hz)||!before.Frontier.HasValue||!before.Capacity.HasValue||seconds==0)
                throw new IOException("Fresh supported V1 USB test geometry is required.");
            // Geometry-only adapter to the existing V1 allocator. The fresh
            // USB report, not these compatibility flags, owns hardware admission.
            var geometry=new NpSdStatus {State=0,Flags=17U|NpV1BleProtocol.RateProfiles,Mask=mask,Hz=hz,Start=before.Frontier.Value,Capacity=before.Capacity.Value};
            return NpV1BleProtocol.EndLba(geometry,seconds);
        }
        byte[] Inner(NpSdUsbTestReport before,uint mask,uint hz,uint seconds){
            uint id=unchecked(before.InnerId+2);if(id<2)id=2;ulong ticket=Ticket(before.Ticket);byte[] legacy;
            if(Family==1){var status=new NpSdStatus {Start=before.Frontier.Value,Capacity=before.Capacity.Value};
                uint end=V1EndLba(before,mask,hz,seconds),session=unchecked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                legacy=NpSdControl.Request(2,id,mask,hz,status.Start,end,session==0?1U:session,1);
                return NpFirstSyncSd.WrapLegacyFineCounter64(legacy,Family,Uid,ticket,before.Ticket);
            }
            legacy=NpV2SdControl.Request(2,id,hz,NpSdLoggerTest.Blocks(mask,hz,seconds),0,mask);
            return NpFirstSyncSd.WrapLegacyGrid16Append(legacy,Family,Uid,ticket,before.Ticket);
        }
        internal static async Task<NpSdUsbTestReport> ObserveAcceptedTicket(byte[] plan,Func<Task<NpSdUsbTestReport>> readReport,Action<Exception> transient){
            try{var report=await readReport();return NpSdUsbTestWire.ValidateMatch(plan,report)?report:null;}
            catch(Exception ex) when(ex is Win32Exception||ex is IOException||ex is CapabilityUnavailableException){if(transient!=null)transient(ex);return null;}
        }
        async Task<NpSdUsbTestReport> WaitForResult(byte[] plan,uint seconds,Action<int,string> progress){
            var elapsed=Stopwatch.StartNew();Exception last=null;
            while(elapsed.Elapsed.TotalSeconds<seconds+240){
                string current=await Task.Run(ReconnectedPath);
                if(current!=null){
                    var report=await ObserveAcceptedTicket(plan,()=>Task.Run(()=>{using(var usb=new Control(current))return usb.Report(Family,Uid);}),ex=>last=ex);
                    if(report!=null){
                        path=current;
                        if(report.Phase==NpSdUsbTestPhase.Finished||report.Phase==NpSdUsbTestPhase.RejectedNoStart||report.Phase==NpSdUsbTestPhase.InterruptedUnknown)return report;
                    }
                }
                if(progress!=null)progress(Math.Min(75,(int)(elapsed.Elapsed.TotalSeconds*75/Math.Max(1,seconds))),
                    "SD recording test · "+NpProgressEstimate.Duration(elapsed.Elapsed.TotalSeconds)+" elapsed · waiting for the same logger to return to USB");
                await Task.Delay(500);
            }
            throw new IOException("The selected logger did not return a matching terminal test result. Outcome unknown; no Start was replayed. Reconnect and read status.",last);
        }
        internal async Task<NpSdUsbSweepResult> Run(uint mask,uint seconds,string reportRoot,CancellationToken cancel,Action<int,string> progress,Action<NpSdUsbProfileResult> updated){
            var before=await Inspect(cancel);
            if(seconds<10||seconds>before.MaximumSeconds)throw new ArgumentException("Choose a test duration between 10 and "+before.MaximumSeconds+" seconds.");
            RequireReady(before,true);
            var rates=NpSdSweepPolicy.Rates(Family,mask,hz=>before.Supports(mask,hz));
            if(rates.Length==0)throw new IOException("The firmware does not advertise a supported rate for the selected probes.");
            string folder=NpFolderNaming.CreateFresh(reportRoot,NpFolderNaming.DownloadName("SD-test-"+Uid,DateTime.Now,""));
            var result=new NpSdUsbSweepResult {Family=Family,DeviceUid=Uid,ProbeMask=mask,Seconds=seconds,ReportFolder=folder,Status="Testing"};
            Json(Path.Combine(folder,"sd-test.json"),result);
            NpSdUsbProfileResult activeProfile=null;
            try{
                byte[] previousPlan=null;
                foreach(uint hz in rates){
                    cancel.ThrowIfCancellationRequested();var current=await Inspect(cancel);
                    if(!SameCard(before,current))throw new IOException("The SD card identity changed. No next test was started.");
                    bool firstManualProfile=previousPlan==null;
                    RequireReady(current,firstManualProfile);
                    var profile=new NpSdUsbProfileResult {SampleHz=hz,RequiredMBs=NpSdSweepPolicy.PhysicalBytesPerSecond(Family,mask,hz)/1000000m,
                        Folder=NpFolderNaming.CreateFresh(folder,hz+"Hz"),Status="Preparing",Evidence=NpSdSweepEvidence.MissingProof};
                    activeProfile=profile;
                    result.Profiles.Add(profile);if(updated!=null)updated(profile);
                    current=await RefreshAfterCatalog(()=>Task.Run(()=>{using(var card=new NpDownloadStream(path)){
                        var prior=NpDownloadCatalog.Read(card).ToArray();profile.PreviousSessions=prior.Select(r=>r.Session).ToArray();
                        profile.PreviousStarts=prior.Select(r=>r.Start).ToArray();profile.PreviousEnds=prior.Select(r=>r.End).ToArray();
                        profile.PreviousCatalog=prior.Select(CatalogIdentity).ToArray();
                    }}),()=>RetireClosedCatalog(cancel,current.Capacity.Value),()=>Inspect(cancel),current,firstManualProfile);
                    byte[] inner=Inner(current,mask,hz,seconds),plan=PlanForProfile(current,inner,seconds,firstManualProfile,previousPlan);
                    File.WriteAllBytes(Path.Combine(profile.Folder,"request128.bin"),plan);File.WriteAllBytes(Path.Combine(profile.Folder,"before256.bin"),current.Raw);
                    var attempt=new NpFirstSyncSdAttempt(Uid,Family,0,inner,NpFirstSyncSd.Unwrap(inner,Family,Uid));
                    NpFirstSyncSdAttemptEvidence.Write(profile.Folder,attempt);
                    cancel.ThrowIfCancellationRequested();profile.Status="Recording";Json(Path.Combine(folder,"sd-test.json"),result);
                    // Exactly one OUT. A lost acknowledgement still waits for the
                    // immutable ticket; cancellation never replays or resets it.
                    var terminal=await SubmitOnce(profile,()=>Task.Run(()=>{using(var usb=new Control(path)){
                        var latest=usb.Report(Family,Uid);if(!SameCard(current,latest)||latest.Frontier!=current.Frontier)throw new IOException("Card/frontier changed before test submission.");
                        if(!PlanForProfile(latest,inner,seconds,firstManualProfile,previousPlan).SequenceEqual(plan))throw new IOException("USB test admission changed before submission.");
                        cancel.ThrowIfCancellationRequested();profile.SubmitEntered=true;
                        usb.Submit(plan);}}),()=>WaitForResult(plan,seconds,progress),()=>Json(Path.Combine(folder,"sd-test.json"),result));
                    File.WriteAllBytes(Path.Combine(profile.Folder,"after256.bin"),terminal.Raw);
                    profile.OriginalTerminalRawFile="after256.bin";
                    profile.Gaps.Device(terminal);
                    if(!cancel.IsCancellationRequested){
                        var refreshed=await RefreshTerminal(plan,terminal,current,()=>Task.Run(()=>FreshReport(CancellationToken.None)),
                            ()=>Task.Run(()=>{using(var usb=new Control(path))return usb.Report(Family,Uid);}));
                        if(!ReferenceEquals(refreshed,terminal)){File.WriteAllBytes(Path.Combine(profile.Folder,"fresh-after256.bin"),refreshed.Raw);profile.FreshTerminalRawFile="fresh-after256.bin";}
                        terminal=refreshed;
                    }
                    profile.SafeToContinue=SameCard(current,terminal)&&SafeTerminal(terminal);
                    if(cancel.IsCancellationRequested){profile.Evidence=NpSdSweepEvidence.Cancelled;profile.Status="Cancelled after finite test stopped";}
                    else if(!profile.SafeToContinue){profile.Evidence=NpSdSweepEvidence.TransportOrUnknownDelivery;profile.Status="Test outcome not confirmed";}
                    else if(terminal.Outcome==NpSdUsbTestOutcome.DeviceCompleteReadbackRequired||terminal.Outcome==NpSdUsbTestOutcome.ThroughputLoss){
                        profile.Status="Verifying stored data";if(updated!=null)updated(profile);
                        await Task.Run(()=>Verify(terminal,profile,()=>cancel.IsCancellationRequested,progress));
                    }else {profile.Status=terminal.Outcome.ToString();profile.Evidence=terminal.Outcome==NpSdUsbTestOutcome.ProbeFault?NpSdSweepEvidence.ProbeOrConfigurationFailure:NpSdSweepEvidence.MissingProof;}
                    profile.Decision=NpSdSweepPolicy.Decide(profile.Evidence,profile.SafeToContinue);activeProfile=null;Json(Path.Combine(folder,"sd-test.json"),result);if(updated!=null)updated(profile);
                    if(profile.Decision==NpSdSweepDecision.Passed){result.SupportedSampleHz=hz;result.Status="Passed at "+hz.ToString("N0")+" samples/s";break;}
                    if(profile.Decision!=NpSdSweepDecision.TryLower){result.Status=cancel.IsCancellationRequested?"Cancelled — not passed":"Not confirmed — sweep stopped";break;}
                    previousPlan=plan;
                    result.Status="Trying the next lower rate";
                }
                if(!result.SupportedSampleHz.HasValue&&result.Status=="Trying the next lower rate")result.Status="No passing tested rate";
            }catch(OperationCanceledException){result.Status="Cancelled — not passed";}
            catch(Exception ex){RecordRunFailure(result,activeProfile,ex);}
            finally{Json(Path.Combine(folder,"sd-test.json"),result);}return result;
        }
        void Verify(NpSdUsbTestReport terminal,NpSdUsbProfileResult profile,Func<bool> cancelled,Action<int,string> progress){
            if(!terminal.Session.HasValue||!terminal.ReserveStart.HasValue||!terminal.ReserveEnd.HasValue||!terminal.StoredNeuralBlocks.HasValue)throw new IOException("The firmware did not retain a complete test extent identity.");
            string output=Path.Combine(profile.Folder,"readback");bool readbackOpened=false;
            try{using(var card=new NpDownloadStream(path)){
                readbackOpened=true;
                if(!card.TransportDescription.StartsWith("High-speed USB",StringComparison.Ordinal))throw new IOException("High-speed USB is required.");
                if(card.Length/512!=terminal.Capacity.Value)throw new IOException("Card capacity changed before readback.");
                var catalog=NpDownloadCatalog.Read(card).ToArray();
                RequirePreviousCatalog(profile.PreviousCatalog,catalog);
                var record=catalog.SingleOrDefault(r=>r.Start==terminal.ReserveStart.Value);
                if(record==null||!record.Downloadable||record.Session!=terminal.Session.Value||record.V2!=(Family==2)||record.ProbeMask!=terminal.Mask||record.ApHz!=terminal.Hz||record.Chunks!=terminal.StoredNeuralBlocks.Value||record.End>terminal.ReserveEnd.Value)
                    throw new IOException("The stored test does not match the ticket, profile and extent.");
                long bytes=record.FormatVersion==16?checked((long)record.PhysicalSectors*512):(long)record.Chunks*(Family==2?46592:65536);
                long free=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(profile.Folder))).AvailableFreeSpace;
                if(free<bytes*2+512L*1024*1024){profile.Evidence=NpSdSweepEvidence.InsufficientSpace;profile.Status="Not enough PC space for readback";return;}
                var header=NpDownloadCatalog.Sector(card,record.Start+3);var footer=NpDownloadCatalog.Sector(card,record.Start+4);
                ValidateStoredTicket(NpDownloadCatalog.Sector(card,record.Start+5),terminal,record,Family,Uid);
                if(Family==2&&!header.Skip(80).Take(16).SequenceEqual(ToBytes(terminal.Cid)))throw new IOException("The saved recording belongs to a different card CID.");
                if(Family==1)profile.Gaps.V1Footer(footer);
                var stored=new NpSdTestResult {PathKind="logger",LoggerVersion=Family,DeviceUid=Uid,ProbeMask=record.ProbeMask,SampleHz=record.ApHz,
                    Session=record.Session,Chunks=record.Chunks,ExpectedChunks=terminal.TargetNeuralBlocks.Value,CardCapacitySectors=terminal.Capacity.Value,
                    StartLba=record.Start,EndLba=record.End,RequestedEndLba=terminal.ReserveEnd.Value,RecordingCompleted=record.Complete,WrittenBytes=bytes,
                    CardCidCrc=NpSdControl.Crc(ToBytes(terminal.Cid),16),Details=""};profile.Stored=stored;
                var elapsed=Stopwatch.StartNew();var exported=NpCe64Export.ExportCalibrated(card,record.Start,record.End,output,cancelled,p=>{
                    if(progress!=null)progress(75+p/4,"Verifying "+record.ApHz.ToString("N0")+" samples/s · "+p+"%");},null);
                bool loss=terminal.Outcome==NpSdUsbTestOutcome.ThroughputLoss;
                if(Family==2){
                    profile.Gaps.V2Readback(exported);
                    if(exported==null||!exported.StoppedLossStatsPresent)throw new IOException("The stored recording lacks loss-counter evidence.");
                    if(terminal.Drops.HasValue&&exported.DroppedBlocks!=terminal.Drops.Value||terminal.Overflows.HasValue&&exported.OverflowEvents!=terminal.Overflows.Value||
                        terminal.DiscardedTicks.HasValue&&exported.ProducerDiscardedTicks!=terminal.DiscardedTicks.Value)throw new IOException("Device and stored loss counters disagree.");
                    if(!loss)NpSdLoggerTest.CheckV2Readback(stored,header,exported);
                    else if(exported.OverflowEvents==0&&exported.DroppedBlocks==0&&exported.ProducerDiscardedTicks==0)throw new IOException("Stored data does not corroborate the reported throughput loss.");
                }else{
                    if(System.Text.Encoding.ASCII.GetString(footer,320,8)!="NPLOSS01"||BitConverter.ToUInt32(footer,328)!=1)throw new IOException("The stored test lacks V1 loss statistics.");
                    var lines=File.ReadAllLines(Path.Combine(output,"capture_status.txt"));
                    if(!loss)NpSdLoggerTest.CheckV1Readback(stored,header,footer,lines);
                    else if(!lines.Contains("complete=True")||!lines.Contains("failure=none")||!lines.Contains("download_complete=True")||BitConverter.ToUInt32(footer,332)==0)
                        throw new IOException("Stored V1 readback does not corroborate a closed throughput-limited test.");
                    stored.LossCountersAvailable=true;
                }
                if(!loss)CheckCleanCompletion(Family,terminal,record.Complete,footer,output,profile);
                profile.ReadbackCompleted=true;stored.VerifiedBytes=bytes;stored.ReadSeconds=elapsed.Elapsed.TotalSeconds;
                profile.Evidence=loss?NpSdSweepEvidence.VerifiedThroughputLoss:NpSdSweepEvidence.VerifiedPass;
                profile.Status=loss?"Did not sustain rate — stored readback checked":"Passed stored readback";
            }}catch(OperationCanceledException){profile.Evidence=NpSdSweepEvidence.Cancelled;profile.Status="Readback cancelled — not passed";}
            catch(InvalidDataException ex){profile.Evidence=NpSdSweepEvidence.VerifiedIntegrityFailure;profile.Status="Stored verification failed";profile.Error=ex.Message;}
            catch(Exception ex){profile.Evidence=NpSdSweepEvidence.TransportOrUnknownDelivery;profile.Status="Readback not confirmed";profile.Error=ex.Message;}
            finally{RetireOwnedReadback(profile,readbackOpened,()=>RetireClosedCatalog(CancellationToken.None,terminal.Capacity.Value).GetAwaiter().GetResult());
                if(Directory.Exists(output))foreach(string file in Directory.GetFiles(output,"*.dat",SearchOption.AllDirectories)){
                string full=Path.GetFullPath(file),prefix=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))try{File.Delete(full);}catch(IOException){} // Only fresh generated test DATs, never SD recordings.
            }}
        }
        static byte[] ToBytes(uint[] words){var b=new byte[words.Length*4];Buffer.BlockCopy(words,0,b,0,b.Length);return b;}
    }
}
