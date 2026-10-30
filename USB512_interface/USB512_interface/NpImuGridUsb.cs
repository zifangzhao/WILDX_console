#if NP_IMU_GRID_ENABLE && NP_FIRST_SYNC_ENABLE
using System;
using System.Collections.Generic;
using System.IO;
namespace USB512_interface {
    // Continuity of the saved NP prefix is distinct from lossless acquisition
    // from byte zero. Startup seeking is allowed only when the final producer
    // receipt attests exactly the same first byte and native counter per port.
    internal sealed class NpImuGridSourceWindow {
        internal sealed class Probe {
            internal int Port;internal long RawBytes,ApRows,FramesChecked,FirstTick;
            internal uint FirstCounter;
            internal long HostDroppedBytes,HostDropEvents,FrameErrors,CounterErrors,GapEvents,Relocks,RecoveryBytes,MissingApRows,MissingLfpRows,LfpPhaseErrors;
        }
        internal uint Mask,ApHz;internal bool Finalized,StartFinalized,OuterLossless;internal Probe[] Probes;
        static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
        internal bool TryStart(NpFirstSyncUsbFineRoot root,out string error){
            error=null;try{
                Need(root!=null&&root.Family==1,"Initial V1 source root unavailable.");
                var r=NpFirstSyncWire.ValidateSourceAnchor(root.Anchor,root.Receipt);
                Need(StartFinalized&&OuterLossless,"Saved source closure or outer-loss ordering before first sample unavailable.");
                Need(Mask==r.Mask&&ApHz==r.ApHz&&Probes!=null,"Initial source profile/selected mask mismatch.");
                uint seen=0;foreach(var p in Probes){
                    Need(p!=null&&p.Port>=1&&p.Port<=3,"Invalid initial physical probe.");uint bit=1U<<(p.Port-1);
                    Need((Mask&bit)!=0&&(seen&bit)==0,"Duplicate/unselected initial physical probe.");seen|=bit;
                    Need(p.HostDroppedBytes==0&&p.HostDropEvents==0,"Host loss ordering before first sample unavailable.");
                    Need(NpImuGridWriter.FirstSourceMatches(r,p.Port,p.FirstTick,p.FirstCounter,p.ApRows,p.FramesChecked,p.RawBytes),"Saved first byte/native counter does not match final NPSTART port proof.");
                }
                Need(seen==Mask,"Not every selected port has saved initial-source evidence.");return true;
            }catch(Exception ex) when(ex is InvalidDataException||ex is OverflowException){error=ex.Message;return false;}
        }
        internal bool TryBound(NpFirstSyncUsbFineRoot root,out ulong end,out string error){
            end=0;error=null;
            try{
                Need(root!=null&&root.Family==1,"Final V1 source root unavailable.");
                var r=NpFirstSyncWire.ValidateSourceAnchor(root.Anchor,root.Receipt);
                Need(Finalized&&OuterLossless,"Capture finalization or outer transport continuity unavailable.");
                Need(Mask==r.Mask&&ApHz==r.ApHz&&r.SourceHz==ApHz*780U,"Final source profile/selected mask mismatch.");
                Need(Probes!=null&&Probes.Length>0,"No finalized probe source windows.");
                uint seen=0;long rawBytes=-1;ulong common=UInt64.MaxValue;
                foreach(var p in Probes){
                    Need(p!=null&&p.Port>=1&&p.Port<=3,"Invalid physical probe source window.");uint bit=1U<<(p.Port-1);
                    Need((Mask&bit)!=0&&(seen&bit)==0,"Duplicate/unselected physical probe window.");seen|=bit;
                    Need(p.RawBytes>0&&p.ApRows>0&&p.FramesChecked>0&&p.FirstTick>=0,"Empty or unavailable decoded NP prefix.");
                    var native=r.Probes[p.Port-1];Need((ulong)p.FirstTick==native.FirstTick&&p.FirstCounter==native.Counter,"Decoded first byte/native counter does not match final NPSTART port proof.");
                    Need(p.HostDroppedBytes==0&&p.HostDropEvents==0&&p.FrameErrors==0&&p.CounterErrors==0&&p.GapEvents==0&&p.Relocks==0&&p.RecoveryBytes==0&&p.MissingApRows==0&&p.MissingLfpRows==0&&p.LfpPhaseErrors==0,"NP prefix has actual loss/recovery/framing/counter/phase errors.");
                    if(rawBytes<0)rawBytes=p.RawBytes;else Need(p.RawBytes==rawBytes,"Selected probes have different retained raw source extents.");
                    ulong validatedEnd=checked((ulong)p.FirstTick+(ulong)p.FramesChecked*60UL);
                    ulong savedEnd=checked((ulong)p.FirstTick+(ulong)p.ApRows*780UL);
                    Need(savedEnd<=validatedEnd&&validatedEnd<=(ulong)p.RawBytes&&(ulong)p.RawBytes-validatedEnd<60UL,"Saved AP prefix/validated-frame/raw extent inconsistency.");
                    common=Math.Min(common,savedEnd);
                }
                Need(seen==Mask&&common!=UInt64.MaxValue&&common>r.ReferenceTick,"Not every selected NP port has a verified saved prefix.");end=common;return true;
            }catch(Exception ex) when(ex is InvalidDataException||ex is OverflowException){error=ex.Message;return false;}
        }
    }
    // Called on the existing ordered verifier worker, no device/control I/O.
    internal sealed class NpImuGridUsb:IDisposable {
        readonly string folder,uid;readonly Func<NpFirstSyncUsbFineRoot> root;readonly Func<uint> session;
        readonly Queue<byte[]> pending=new Queue<byte[]>();NpImuGridWriter writer;NpFirstSyncUsbFineRoot bound;uint generation,lastSequence;bool haveSequence,disabled;
        internal string Error;internal const int MaximumPending=32;
        internal NpImuGridUsb(string folder,string uid,Func<uint> session,Func<NpFirstSyncUsbFineRoot> root){this.folder=folder;this.uid=uid;this.session=session;this.root=root;}
        void Accept(byte[] frame){if(NpImuGridCarrier.IsPhase(frame)){var r=NpFirstSyncWire.Parse(bound.Receipt);writer.AcceptPhase(frame,NpImuGridWriter.PhaseBinding(bound.Uid,bound.Token,r),r.SourceHz,r);}else writer.Feed(frame);}
        void Bind(){var r=root();if(r==null){if(bound!=null)throw new InvalidDataException("Grid parent proof was withdrawn.");return;}if(r.Family!=1||!String.Equals(r.Uid,uid,StringComparison.OrdinalIgnoreCase)||r.Token==0)throw new InvalidDataException("Grid parent UID/family/token mismatch.");if(writer==null){bound=r;generation=r.Generation;writer=new NpImuGridWriter(folder,generation);}else if(generation!=r.Generation||r.Token!=bound.Token||r.Sequence!=bound.Sequence)throw new InvalidDataException("Grid parent token/generation/root changed.");}
        internal void Feed(byte[] bytes,int offset,int length){if(disabled)return;try{
            uint sequence;byte[] frame=NpImuGridCarrier.Usb(bytes,offset,length,session(),out sequence);
            if(haveSequence&&sequence!=unchecked(lastSequence+1)){
                uint delta=unchecked(sequence-lastSequence);if(delta==0||delta>=0x80000000U)throw new InvalidDataException("Grid transport sequence reversed/duplicated.");
            }
            haveSequence=true;lastSequence=sequence;Bind();
            if(writer==null){if(pending.Count==MaximumPending)throw new InvalidDataException("Grid parent proof did not arrive within bounded pending storage.");pending.Enqueue(frame);return;}
            while(pending.Count!=0)Accept(pending.Dequeue());Accept(frame);
        }catch(Exception e) when(e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is OverflowException){Error=e.Message;disabled=true;pending.Clear();if(writer!=null)writer.Report(Error);}}
        internal void Complete(NpImuGridSourceWindow source){try{if(!disabled){Bind();if(writer!=null){while(pending.Count!=0)Accept(pending.Dequeue());var r=NpFirstSyncWire.Parse(bound.Receipt);ulong sourceEnd=0;string why=null,startError;bool start=source!=null&&source.TryStart(bound,out startError);bool full=source!=null&&source.TryBound(bound,out sourceEnd,out why);if(!full)Error="Final NP source window unavailable: "+(source==null?"No finalized source evidence.":why);writer.Complete(sourceEnd,r.SourceHz,r.ReferenceTick,full,start);}}}catch(Exception e) when(e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is OverflowException){Error=e.Message;if(writer!=null)writer.Report(Error);}}
        public void Dispose(){try{if(writer!=null)writer.Dispose();}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){Error=e.Message;}try{File.WriteAllText(Path.Combine(folder,"imu_grid_status.txt"),"selected=true\ncarrier=NSG1\ngrid_phase_qualified="+(writer!=null&&writer.PhaseQualified&&Error==null)+"\nfirst_slot_phase_valid="+(writer!=null&&writer.FirstSlotPhaseValid)+"\nfirst_slot_phase_scope=start_only; no full-record clock or tail claim\nrows="+(writer==null?0:writer.Rows)+"\nslot_extent="+(writer==null?0:writer.Slots)+"\nerror="+(Error??(writer==null?"Final parent proof unavailable; pending frames="+pending.Count:"none"))+"\n");}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){Error=e.Message;}}
    }
}
#endif
