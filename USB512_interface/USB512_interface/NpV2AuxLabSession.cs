using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace USB512_interface {
    // Requested production IMU uses one root analog owner, selected before arm.
    // The old explicit lab entry remains a compatibility fixture only.
    internal sealed class NpV2AuxLabSession {
        internal const string TerminalFile="imu_grid_terminal.bin";
        readonly string folder,recordingFolder,uid;readonly Queue<NpV2AuxPacket> pending=new Queue<NpV2AuxPacket>();
        Func<NpFirstSyncUsbFineRoot> root;NpFirstSyncUsbFineRoot bound;NpImuGridWriter writer;
        NpV2AuxCapture capture;NpV2AuxTerminal terminal;byte[] phase;bool configured,attempted,selected,disabled,enforced;
        readonly bool labOnly;uint controlVersion;string fallback;
        string error,firstSourceError;bool firstSourceVerified;long startupUs;internal string Error {get{return error;}}
        internal bool Configured {get{return configured;}}internal bool Selected {get{return selected;}}
        internal bool EnforcedScheduleVerified {get{return enforced;}}
        internal bool PhaseQualified {get{return writer!=null&&writer.PhaseQualified;}}
        internal bool SourceWindowVerified {get{return writer!=null&&writer.SourceWindowVerified;}}
        internal ulong Rows {get{return writer==null?0:writer.Rows;}}internal ulong Observed {get{return writer==null?0:writer.Observed;}}
        internal static NpV2AuxLabSession ForRecording(string output,string uid,bool requested){return requested?new NpV2AuxLabSession(output,uid,false):null;}
        internal static NpV2AuxLabSession ForLab(string output,string uid){return String.Equals(Environment.GetEnvironmentVariable("WILDX_V2_AUX_GRID_LAB"),"1",StringComparison.Ordinal)?new NpV2AuxLabSession(output,uid,true):null;}
        NpV2AuxLabSession(string output,string deviceUid,bool lab){recordingFolder=output;folder=lab?Path.Combine(output,"imu_grid_lab"):output;uid=deviceUid;labOnly=lab;}
        static bool Optional(Exception ex){return ex is IOException||ex is InvalidDataException||ex is Win32Exception||ex is UnauthorizedAccessException||ex is TimeoutException||ex is InvalidOperationException||ex is NotSupportedException||ex is OverflowException;}
        static bool Unsupported(Exception ex){var native=ex as Win32Exception;return ex is NotSupportedException||native!=null&&(native.NativeErrorCode==1||native.NativeErrorCode==31||native.NativeErrorCode==50);}
        void Fail(Exception ex){Interlocked.CompareExchange(ref error,ex.Message,null);}
        internal void BeforeConfiguration(NpV2UsbDevice device){
            BeforeConfiguration(device.AuxiliaryEndpointAvailable,device.GetGridSelection,device.ConfigureAuxImu,device.GetAuxStatus);
        }
        internal void BeforeConfiguration(bool endpoint,Func<byte[]> gridInfo,Action configure,Func<byte[]> auxStatus){
            try {
                if(!endpoint){fallback="No AUX endpoint; legacy IMU retained.";return;}
                // A recognized same-UID NSGC establishes the optional control
                // protocol even while feature15 readiness is still false.
                controlVersion=NpImuGridControl.ParseUsb(gridInfo(),uid).Version;
                if(!labOnly&&controlVersion!=2){fallback="Legacy grid capability; enforced schedule not available.";return;}
                var existing=auxStatus();
                if(!AuxIdle(existing)){fallback="Unknown or nonidle AUX owner retained; grid not configured.";return;}
                uint existingFlags=BitConverter.ToUInt32(existing,16);
                if((existingFlags&~0x307U)!=0U||(existingFlags&(3U|512U))!=0U){fallback="Existing camera/ADC, AV-only or unknown AUX flags retained; grid not configured.";return;}
                attempted=true;Exception configureError=null;try{configure();}catch(Exception ex) when(Optional(ex)){configureError=ex;}
                var s=auxStatus();
                configured=AuxIdle(s)&&BitConverter.ToUInt32(s,8)==1&&BitConverter.ToUInt32(s,16)==4;
                if(!configured)Fail(configureError??new IOException("AUX IMU configuration was not confirmed; NP retains its normal start path."));
            }catch(Exception ex) when(Unsupported(ex)){fallback="Optional grid transport unavailable; legacy IMU retained.";}
            catch(Exception ex) when(Optional(ex)){Fail(ex);}
        }
        bool AuxIdle(byte[] s){
            if(s==null||s.Length!=128||BitConverter.ToUInt32(s,0)!=0x32535641U||BitConverter.ToUInt32(s,4)!=1||BitConverter.ToUInt32(s,8)>1||BitConverter.ToUInt32(s,12)!=0)return false;
            string actual=BitConverter.ToUInt32(s,112).ToString("X8")+BitConverter.ToUInt32(s,116).ToString("X8")+BitConverter.ToUInt32(s,120).ToString("X8");return String.Equals(actual,uid,StringComparison.OrdinalIgnoreCase);
        }
        internal void AfterTiming(NpV2UsbDevice device,Func<NpFirstSyncUsbFineRoot> source,bool feature15){
            AfterTiming(device.GetGridSelection,device.SelectGrid,source,feature15);
        }
        internal void AfterTiming(Func<byte[]> info,Action<bool> select,Func<NpFirstSyncUsbFineRoot> source,bool feature15){
            root=source;if(!feature15||!configured&&(labOnly||controlVersion!=1))return;
            try{
                if(labOnly){selected=NpImuGridControl.PrepareUsb(info,select,uid,true);return;}
                var selection=NpImuGridControl.PrepareUsbSelection(info,select,uid,true);
                controlVersion=selection.Version;enforced=selection.EnforcedScheduleVerified;selected=selection.Selected&&enforced;
                if(!selected)fallback="Enforced grid selection unavailable; legacy IMU retained.";
            }
            catch(Exception ex) when(Optional(ex)){Fail(new IOException("Scheduled IMU selection was not confirmed; neural recording continues.",ex));} // No replay or NP Start gate.
        }
        internal void AfterArm(NpV2UsbDevice device){
            AfterArm((grid,sink)=>new NpV2AuxCapture(device,grid,sink));
        }
        internal void AfterArm(Func<bool,Action<NpV2AuxPacket>,NpV2AuxCapture> start){
            if(!attempted)return;
            var watch=Stopwatch.StartNew();try{capture=start(selected,Packet);}catch(Exception ex) when(Optional(ex)){Fail(ex);}
            finally{startupUs=(long)Math.Ceiling(watch.ElapsedTicks*1000000m/Stopwatch.Frequency);}
        }
        bool Bind(){
            var r=root==null?null:root();if(r==null)return false;
            if(r.Family!=2||!String.Equals(r.Uid,uid,StringComparison.OrdinalIgnoreCase)||r.Token==0)throw new InvalidDataException("AUX grid final parent identity mismatch.");
            if(bound!=null){if(bound.Token!=r.Token||bound.Generation!=r.Generation||bound.Sequence!=r.Sequence)throw new InvalidDataException("AUX grid final parent changed.");return true;}
            var receipt=NpFirstSyncWire.Parse(r.Receipt);if(receipt.Family!=2||receipt.Generation!=r.Generation||receipt.Session!=r.Generation)throw new InvalidDataException("AUX grid final session/generation mismatch.");
            NpSensorFineTime.FromUsb(r); // Reparse the immutable final identity/source bytes.
            Directory.CreateDirectory(folder);writer=new NpImuGridWriter(folder,r.Generation,enforced);bound=r;return true;
        }
        void Accept(NpV2AuxPacket packet){
            packet.CheckBody();if(terminal!=null)throw new InvalidDataException("AUX grid data followed its terminal.");
            var receipt=NpFirstSyncWire.Parse(bound.Receipt);var binding=NpImuGridWriter.PhaseBinding(bound.Uid,bound.Token,receipt);byte[] body=packet.Payload;
            if(packet.Type==6){if(phase!=null)throw new InvalidDataException("AUX grid phase was repeated.");NpImuGridPhase.Read(body,binding);phase=body;if(!writer.AcceptPhase(body,binding,receipt.SourceHz,receipt))Fail(new InvalidDataException("Grid phase does not qualify against the final NPSTART auxiliary source proof."));}
            else if(packet.Type==5){if(phase==null)throw new InvalidDataException("AUX grid rows preceded their phase record.");writer.Feed(body);}
            else if(packet.Type==7){
                var end=NpV2AuxTerminal.Read(body,binding);if(end.FinalKnown&&end.FinalExclusive<writer.Slots)throw new InvalidDataException("AUX terminal precedes retained grid ordinals.");
                using(var raw=new FileStream(Path.Combine(folder,TerminalFile),FileMode.CreateNew,FileAccess.Write,FileShare.Read))raw.Write(body,0,body.Length);
                terminal=end;
                if(end.GridError)Fail(new IOException("Device reports an IMU grid error; retained values remain available."));
            }
        }
        void Packet(NpV2AuxPacket packet){
            if(packet.Type<5||disabled)return;
            try {
                if(!selected)throw new InvalidDataException("AUX grid selection was not confirmed.");
                if(!Bind()){if(pending.Count==32)throw new InvalidDataException("AUX grid parent proof exceeded bounded pending storage.");pending.Enqueue(packet);return;}
                while(pending.Count!=0)Accept(pending.Dequeue());Accept(packet);
            }catch(Exception ex) when(Optional(ex)){disabled=true;pending.Clear();Fail(ex);if(writer!=null)writer.Report(ex.Message);}
        }
        internal void StopAndRetire(){if(capture!=null){if(!capture.StopAndRetire())Fail(new IOException(capture.RetirementError??"AUX ownership remains on HOLD; NP data is preserved."));if(capture.Error!=null)Fail(new IOException(capture.Error));}}
        static void NeedFirst(bool valid,string why){if(!valid)throw new InvalidDataException(why);}
        // Stopped actual verifier/recorder objects only. Later capture faults do
        // not erase a saved initial row, and a receipt alone cannot invent it.
        internal static bool SavedFirstSource(string output,string expectedUid,NpFirstSyncUsbFineRoot source,NpV2WireVerifier verifier,NpV2DatRecorder saved,out string why){
            why=null;
            try{
                NeedFirst(source!=null&&verifier!=null&&saved!=null,"Actual initial NP verifier/closed DAT evidence unavailable.");
                NeedFirst(String.Equals(source.Uid,expectedUid,StringComparison.OrdinalIgnoreCase),"Actual capture UID disagrees with final source root.");
                var clock=NpSensorFineTime.FromUsb(source);var r=NpFirstSyncWire.Parse(source.Receipt);
                var info=NpFirstSyncUsb.ParseInfo(source.Info,source.Uid,2);byte[] h=verifier.IdentityHeader;
                NeedFirst(clock.Family==2&&info.HardwareRtc&&r.Session==r.Generation,"V2 selected final source context unavailable.");
                byte[] stopped;
                using(var input=new FileStream(Path.Combine(output,"acquisition_anchor.bin"),FileMode.Open,FileAccess.Read,FileShare.Read)){
                    NeedFirst(input.Length==64,"Saved actual stopped source anchor length is invalid.");
                    using(var bytes=new BinaryReader(input))stopped=bytes.ReadBytes(64);
                }
                var actual=NpAcquisitionAnchor.Parse(stopped,stopped.Length==64?BitConverter.ToUInt32(stopped,8):0,2);
                byte[] root=source.Anchor;var original=NpAcquisitionAnchor.Parse(root,BitConverter.ToUInt32(root,8),2);
                NeedFirst(actual.Flags==original.Flags&&actual.Generation==original.Generation&&actual.BeforeMs==original.BeforeMs&&actual.AfterMs==original.AfterMs&&
                    actual.BeforeCycles==original.BeforeCycles&&actual.AfterCycles==original.AfterCycles&&actual.CounterHz==original.CounterHz&&actual.SourceHz==original.SourceHz&&
                    actual.SampleHz==original.SampleHz&&actual.Origin==original.Origin,"Saved actual stopped source anchor disagrees with final NPSTART root.");
                NeedFirst(BitConverter.ToUInt64(source.Anchor,44)==0,"Nonzero/ambiguous initial source discard is not qualified by this V2 path.");
                NeedFirst(verifier.Blocks>0&&verifier.FormatVersion==3&&h!=null&&h.Length==512&&
                    System.Text.Encoding.ASCII.GetString(h,0,8)=="NP2USB03"&&BitConverter.ToUInt64(h,24)==0&&BitConverter.ToUInt64(h,32)==0,
                    "Actual first verified NP block/source origin unavailable.");
                NeedFirst(verifier.ProbeMask==r.Mask&&verifier.LiveProbeMask==r.Mask&&verifier.SampleHz==r.ApHz&&verifier.MclkHz==r.SourceHz&&
                    saved.LiveProbeMask==r.Mask&&saved.SampleHz==r.ApHz&&BitConverter.ToUInt32(h,76)==r.Mask&&BitConverter.ToUInt32(h,80)==r.Mask&&
                    BitConverter.ToUInt32(h,60)==r.ApHz&&BitConverter.ToUInt32(h,44)==r.SourceHz,
                    "Initial NP header/saved selected probes or source rate disagree with final receipt.");
                int count=((r.Mask&1)!=0?1:0)+((r.Mask&2)!=0?1:0)+((r.Mask&4)!=0?1:0);
                for(int p=1;p<=3;p++)if((r.Mask&(1U<<(p-1)))!=0){
                    int phase=verifier.PhaseForProbe(p-1);long rows=saved.RowsForProbe(p);var position=r.Probes[p-1];
                    NeedFirst(phase>=0&&phase<1600&&position.Counter==UInt32.MaxValue&&position.Phase==phase&&
                        position.FirstTick==(ulong)((1600-phase)%1600)&&rows>0,"Saved initial V2 STARTBYTE/phase/row is not proven for every selected probe.");
                    string relative=saved.MergedDat?"neuropixels_ap.dat":count==1?"neuropixels_ap.dat":Path.Combine("probe"+p,"neuropixels_ap.dat");
                    int rowBytes=saved.MergedDat?count*768:768;
                    // Exclusive metadata-only open proves the capture writer no
                    // longer owns this file. Never scan neural payload here.
                    using(var file=new FileStream(Path.Combine(output,relative),FileMode.Open,FileAccess.Read,FileShare.None))
                        NeedFirst(file.Length==checked(rows*rowBytes),"Saved initial V2 DAT extent is partial or disagrees with its closed writer.");
                }
                return true;
            }catch(Exception ex) when(Optional(ex)){why=ex.Message;return false;}
        }
        internal void Complete(ulong sourceEnd,bool npVerified,NpV2WireVerifier verifier=null,NpV2DatRecorder saved=null){
            try {
                if(capture!=null&&!capture.RetirementConfirmed){Fail(new IOException("AUX owner remains on HOLD; grid files remain partial."));return;}
                if(selected&&!disabled){if(Bind()){while(pending.Count!=0)Accept(pending.Dequeue());var receipt=NpFirstSyncWire.Parse(bound.Receipt);firstSourceVerified=SavedFirstSource(recordingFolder,uid,bound,verifier,saved,out firstSourceError);
                    bool retained=npVerified&&capture!=null&&capture.RetirementConfirmed&&capture.Error==null&&error==null&&
                        (terminal==null||!terminal.GridError&&!terminal.CountersAreLowerBounds);
                    // A bound unknown cutoff is not an observed-row phase fault.
                    writer.Complete(sourceEnd,receipt.SourceHz,receipt.ReferenceTick,retained,firstSourceVerified);
                }else Fail(new IOException("Final NPSTART/kind5 parent proof is unavailable; grid timing remains unknown."));}
            }catch(Exception ex) when(Optional(ex)){Fail(ex);}finally{
                if(writer!=null&&(capture==null||capture.RetirementConfirmed)){try{writer.Dispose();}catch(Exception ex) when(Optional(ex)){Fail(ex);}}
                try{Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,labOnly?"aux_grid_lab_status.txt":"imu_grid_status.txt"),"lab_only="+labOnly+"\nselected="+(selected?"true":"false")+"\ncarrier="+(selected?"NSG1":"SNS1_legacy")+"\nanalog_owner="+(selected?"scheduled_grid":"legacy_held")+"\ncontrol_version="+controlVersion+"\nenforced_schedule_verified="+enforced+"\nfallback="+(fallback??"none")+"\nNP_output_unchanged=true\nphase_qualified="+PhaseQualified+"\nsource_window_verified="+SourceWindowVerified+"\nfirst_source_verified="+firstSourceVerified+"\nfirst_source_scope=saved initial NP STARTBYTE only; later source window/cutoff separate\nfirst_source_error="+(firstSourceError??"none")+"\nterminal_bound="+(terminal!=null)+"\nfinal_slot_known="+(terminal!=null&&terminal.FinalKnown)+"\nfinal_exclusive="+(terminal==null?"unknown":terminal.FinalExclusive.ToString())+"\nqueue_dropped="+(terminal==null?"unknown":terminal.QueueDropped.ToString())+"\nscheduler_missing="+(terminal==null?"unknown":terminal.SchedulerMissed.ToString())+"\ncounters_lower_bounds="+(terminal!=null&&terminal.CountersAreLowerBounds)+"\nretirement_confirmed="+(capture!=null&&capture.RetirementConfirmed)+"\nep82_startup_host_elapsed_us="+startupUs+"\naux_packets="+(capture==null?0:capture.Packets)+"\naux_sequence_gap_count="+(capture==null?0:capture.SequenceGaps)+"\ntransport_error="+(capture==null?"not started":capture.Error??"none")+"\nerror="+(error??"none")+"\n");}catch(Exception ex) when(Optional(ex)){Fail(ex);}
            }
        }
    }
}
