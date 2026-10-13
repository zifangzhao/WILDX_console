using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace USB512_interface
{
    // Requested settings are not evidence of an actual sampling frequency.
    // Only an acknowledged firmware descriptor supplies the recording profile.
    internal sealed class NpCaptureMode
    {
        internal readonly int ProbeMask, ApHz;
        internal int ProbeCount { get { return Count(ProbeMask); } }
        internal bool Experimental { get { return ApHz==20000 || (ProbeCount==2 && ApHz==30000); } }
        internal static bool Valid(int mask,int hz) {
            return mask>=1 && mask<=7 &&
                (hz==7500 || hz==15000 || (hz==20000 && (Count(mask)==1 || mask==3)) || (hz==30000 && Count(mask)<3));
        }
        internal static int RateBit(int hz) {return hz==7500?1:hz==15000?2:hz==20000?8:hz==30000?4:0;}
        internal static string LfpHzText(int apHz) {return (apHz/12.0).ToString("0.######",CultureInfo.InvariantCulture);}
        internal NpCaptureMode(int mask,int hz) {
            if(!Valid(mask,hz))
                throw new ArgumentException("Unsupported NP mode (20 kHz requires a single probe or probes 1+2; maximum 3 probes at 15 kHz).");
            ProbeMask=mask;ApHz=hz;
        }
        internal static int Count(int mask) { return (mask&1)+((mask>>1)&1)+((mask>>2)&1); }
        public override string ToString() { return ProbeCount+" probe(s), "+(ApHz/1000.0).ToString("0.0",CultureInfo.InvariantCulture)+" kHz AP / "+LfpHzText(ApHz)+" Hz LFP"+(Experimental?" — experimental":""); }
    }

    // A request deliberately remains less specific than a wire-level capture
    // mode.  The UI requests one of the V1's 7.5/15/20/30 kHz profiles. Resolve
    // that request only after reading the device capability descriptor; never
    // imply that the requested rate was actually selected.
    internal sealed class NpCaptureRequest
    {
        internal readonly int ProbeMask, RequestedApHz;
        internal NpCaptureRequest(int probeMask,int requestedApHz) {
            if(probeMask<1 || probeMask>7 || NpCaptureMode.RateBit(requestedApHz)==0)
                throw new ArgumentException("NP request must select one to three probes and 7.5, 15, 20, or 30 kHz.");
            ProbeMask=probeMask;RequestedApHz=requestedApHz;
        }
        internal NpCaptureMode ResolveBest(NpModeCapabilities caps,out string resolution) {
            if(caps==null)throw new ArgumentNullException("caps");
            int usableMask=ProbeMask&caps.AvailableMask;
            if(usableMask==0)
                throw new IOException("None of the requested NP ports are available on the connected device.");
            int advertised=caps.Rates[NpCaptureMode.Count(usableMask)-1];
            int selectedRate=0;
            // Prefer the fastest advertised profile that does not exceed the
            // requested bandwidth.  Only choose a faster one if the firmware
            // offers no slower profile at all.
            foreach(int rate in new[]{30000,20000,15000,7500})
                if(rate<=RequestedApHz && NpCaptureMode.Valid(usableMask,rate) &&
                   (advertised&NpCaptureMode.RateBit(rate))!=0) {selectedRate=rate;break;}
            if(selectedRate==0)
                foreach(int rate in new[]{7500,15000,20000,30000})
                    if(NpCaptureMode.Valid(usableMask,rate) &&
                       (advertised&NpCaptureMode.RateBit(rate))!=0) {selectedRate=rate;break;}
            if(selectedRate==0)
                throw new IOException("The connected firmware advertises no usable NP rate for the requested port count.");
            var mode=new NpCaptureMode(usableMask,selectedRate);
            string requested=Ports(ProbeMask)+" at "+RequestedApHz+" Hz";
            string accepted=Ports(mode.ProbeMask)+" at "+mode.ApHz+" Hz";
            resolution=requested==accepted?"requested "+requested+"; accepted exact profile":
                "requested "+requested+"; best effort accepted "+accepted;
            return mode;
        }
        internal static string Ports(int mask) {
            string ports="";
            for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0)ports+=(ports.Length==0?"":"+")+"P"+p;
            return ports;
        }
        internal string Describe() {return Ports(ProbeMask)+" at "+RequestedApHz+" Hz";}
    }

    internal sealed class NpModeCapabilities
    {
        internal int AvailableMask, ActiveMask, ApHz, WireId, BatchPackets;
        internal bool Selectable;
        internal bool SupportsStartupAdmission;
        internal bool SupportsPresence;
        internal bool SupportsCameraOnly;
        internal bool CameraOnlyActive {get{return SupportsCameraOnly && ActiveMask==0 && ApHz==0;}}
        internal int PresenceState, PresenceRequestedMask, PresentMask, AbsentMask, PresenceFaultMask;
        internal bool PresenceReady {get{return SupportsPresence && PresenceState==2;}}
        internal NpModeCapabilities ForPresentProbes() {
            if(!PresenceReady)throw new IOException("NP presence preflight has not completed.");
            if(PresenceFaultMask!=0)throw new IOException("NP I2C fault on "+NpCaptureRequest.Ports(PresenceFaultMask)+"; not treated as an absent probe.");
            var copy=(NpModeCapabilities)MemberwiseClone();
            copy.AvailableMask=AvailableMask&PresentMask;
            return copy;
        }
        internal readonly int[] Rates=new int[3];
        internal NpCaptureMode Active { get {
            if(CameraOnlyActive)throw new IOException("Camera-only mode has no active NP capture profile.");
            return new NpCaptureMode(ActiveMask,ApHz);
        } }
        internal bool Supports(NpCaptureMode m) {
            int bit=NpCaptureMode.RateBit(m.ApHz);
            return (m.ProbeMask&~AvailableMask)==0 && (Rates[m.ProbeCount-1]&bit)!=0 &&
                (Selectable || (m.ProbeMask==ActiveMask && m.ApHz==ApHz));
        }
        internal static NpModeCapabilities Parse(byte[] b,int length) {
            if(b==null || length!=48 || b.Length<length || BitConverter.ToUInt32(b,0)!=0x314d504e || BitConverter.ToUInt32(b,4)!=1)
                throw new IOException("Invalid NP mode capability descriptor.");
            var c=new NpModeCapabilities();
            c.AvailableMask=checked((int)BitConverter.ToUInt32(b,8));
            for(int i=0;i<3;i++)c.Rates[i]=checked((int)BitConverter.ToUInt32(b,12+i*4));
            c.ActiveMask=checked((int)BitConverter.ToUInt32(b,24));c.ApHz=checked((int)BitConverter.ToUInt32(b,28));
            c.WireId=checked((int)BitConverter.ToUInt32(b,32));c.BatchPackets=checked((int)BitConverter.ToUInt32(b,36));
            uint flags=BitConverter.ToUInt32(b,40);
            uint extension=BitConverter.ToUInt32(b,44);
            if(c.AvailableMask<1 || c.AvailableMask>7 || (flags&~7U)!=0 || ((flags&2U)==0 && (extension&~1U)!=0) ||
               (c.Rates[0]&~15)!=0 || (c.Rates[1]&~15)!=0 || (c.Rates[2]&~3)!=0)
                throw new IOException("Unsupported NP capabilities.");
            c.Selectable=(flags&1)!=0;
            c.SupportsStartupAdmission=(flags&4)!=0;
            c.SupportsPresence=(flags&2)!=0;
            // Legacy presence receipts already use bit zero for requested P1.
            // Interpret the camera extension only when presence is absent.
            c.SupportsCameraOnly=!c.SupportsPresence && (extension&1U)!=0;
            if(c.SupportsPresence) {
                uint presence=BitConverter.ToUInt32(b,44);
                c.PresenceRequestedMask=(int)(presence&7);
                c.PresentMask=(int)((presence>>4)&7);
                c.AbsentMask=(int)((presence>>8)&7);
                c.PresenceFaultMask=(int)((presence>>12)&7);
                c.PresenceState=(int)((presence>>16)&3);
                if((presence&~0x00037777U)!=0 ||
                   ((c.PresentMask|c.AbsentMask|c.PresenceFaultMask)&~c.PresenceRequestedMask)!=0 ||
                   (c.PresentMask&c.AbsentMask)!=0 ||
                   (c.PresenceFaultMask&(c.PresentMask|c.AbsentMask))!=0 ||
                   (c.PresenceState==2 && (c.PresenceFaultMask!=0 ||
                     (c.PresentMask|c.AbsentMask)!=c.PresenceRequestedMask)))
                    throw new IOException("Invalid NP presence receipt.");
            }
            if(c.CameraOnlyActive) {
                if(!c.Selectable || c.WireId!=0x76 || c.BatchPackets<4 || c.BatchPackets>256 || c.BatchPackets%4!=0)
                    throw new IOException("Inconsistent camera-only capability descriptor.");
                return c;
            }
            if(!NpCaptureMode.Valid(c.ActiveMask,c.ApHz) || !c.Supports(c.Active) || (c.WireId!=6 && c.WireId!=0x76 && c.WireId!=0x77) ||
               (c.WireId==6 && c.Active.ProbeCount!=1) ||
               (c.WireId==0x77 && (c.ActiveMask!=3 || c.ApHz!=20000)) ||
               ((c.WireId==0x76 || c.WireId==0x77) && (c.BatchPackets<4 || c.BatchPackets>256 || c.BatchPackets%4!=0)))
                throw new IOException("Inconsistent NP capture profile.");
            return c;
        }
    }

    internal sealed class NpStartupAdmission {
        internal uint State,SessionId,ApHz;
        internal int RequestedMask,AcceptedMask,RejectedMask;
        internal readonly uint[] Reasons=new uint[3],Errors=new uint[3],Hal=new uint[3];
        internal static NpStartupAdmission Parse(byte[] b,int got) {
            if(b==null || got!=64 || b.Length<got || BitConverter.ToUInt32(b,0)!=0x3141504e ||
               BitConverter.ToUInt32(b,4)!=1 || BitConverter.ToUInt32(b,8)!=64)
                throw new IOException("Invalid NP startup admission receipt.");
            var a=new NpStartupAdmission {State=BitConverter.ToUInt32(b,12),
                RequestedMask=checked((int)BitConverter.ToUInt32(b,16)),AcceptedMask=checked((int)BitConverter.ToUInt32(b,20)),
                RejectedMask=checked((int)BitConverter.ToUInt32(b,24)),SessionId=BitConverter.ToUInt32(b,28),ApHz=BitConverter.ToUInt32(b,32)};
            if(a.State>3 || (a.RequestedMask&~7)!=0 || a.AcceptedMask<0 || a.RejectedMask<0 ||
               (a.AcceptedMask&a.RejectedMask)!=0 || (a.AcceptedMask|a.RejectedMask)!=a.RequestedMask ||
               (a.State==2 && a.AcceptedMask==0) || (a.State==3 && a.AcceptedMask!=0) ||
               (a.State!=0 && (a.RequestedMask==0 || a.SessionId==0)))
                throw new IOException("Inconsistent NP startup admission receipt.");
            uint reasons=BitConverter.ToUInt32(b,36);
            if((reasons&0xff000000U)!=0)throw new IOException("Invalid NP rejection reasons.");
            for(int p=0;p<3;p++) {a.Reasons[p]=(reasons>>(8*p))&255;a.Errors[p]=BitConverter.ToUInt32(b,40+4*p);a.Hal[p]=BitConverter.ToUInt32(b,52+4*p);}
            return a;
        }
        internal string Describe() {
            string text="accepted "+(AcceptedMask==0?"none":NpCaptureRequest.Ports(AcceptedMask))+" at "+ApHz+" Hz";
            for(int p=0;p<3;p++)if((RejectedMask&(1<<p))!=0)
                text+="; automatically disabled P"+(p+1)+" ("+(Reasons[p]==2?"ASIC I2C readiness failed":Reasons[p]==3?"identity/configuration failed":"failure "+Reasons[p])+
                    ", HAL="+Hal[p]+", I2C=0x"+Errors[p].ToString("X")+")";
            return text;
        }
    }

    // Preallocate a bounded host backlog from the acknowledged capture mode.
    // 780 raw bytes are emitted per probe/AP sample. USB packet headers add
    // 6 bytes to every 506 payload bytes. Rounding to 32 MiB quanta provides
    // guard room beyond the requested ten seconds without unbounded growth.
    internal static class NpHostQueuePolicy
    {
        internal const int TargetSeconds=10;
        internal const int StallToleranceMilliseconds=12000;
        internal const int MinimumBlocks=256;
        internal const int BlockQuantum=256;
        internal const int RawBytesPerProbeApSample=780;
        internal const int WirePacketBytes=512;
        internal const int WirePayloadBytes=506;

        internal static long WireBytesPerSecond(int probeCount,int apHz) {
            if(probeCount<1 || probeCount>3 || apHz<=0)throw new ArgumentOutOfRangeException();
            long payload=checked((long)probeCount*apHz*RawBytesPerProbeApSample);
            return checked((payload*WirePacketBytes+WirePayloadBytes-1)/WirePayloadBytes);
        }
        internal static int BufferCount(int blockBytes,int probeCount,int apHz) {
            if(blockBytes<=0)throw new ArgumentOutOfRangeException("blockBytes");
            long required=checked(WireBytesPerSecond(probeCount,apHz)*TargetSeconds);
            long blocks=(required+blockBytes-1)/blockBytes;
            blocks=((blocks+BlockQuantum-1)/BlockQuantum)*BlockQuantum;
            if(blocks<MinimumBlocks)blocks=MinimumBlocks;
            if(blocks>Int32.MaxValue)throw new OverflowException("NP host queue is too large.");
            return (int)blocks;
        }
        internal static double CapacitySeconds(int blockBytes,int blocks,int probeCount,int apHz) {
            if(blockBytes<=0 || blocks<=0)throw new ArgumentOutOfRangeException();
            return ((double)blockBytes*blocks)/WireBytesPerSecond(probeCount,apHz);
        }
    }

    // Tagged quad packets: global NP sequence, explicit physical probe and time
    // lane. A whole group must belong to one probe. Never resynchronize silently.
    internal sealed class NpTaggedPacketParser
    {
        readonly int[] probes;
        readonly int batchPackets;
        internal readonly bool PairedRaw;
        readonly byte[] packet=new byte[512],lanes=new byte[2024],output=new byte[2024];
        int stored,lane;bool failed;
        internal long Packets { get; private set; }
        internal long MiscPackets { get; private set; }
        internal long SequenceGaps { get; private set; }
        internal int PendingBytes { get { return stored; } }
        internal int PendingLanePackets { get { return lane; } }
        ushort miscSequence;bool haveMisc;
        internal Action<byte[],int,int> OnMisc;
        internal Action<byte[],int,int,byte,ushort> OnCamera;
        internal NpTaggedPacketParser(int mask,int batchPackets) : this(mask,batchPackets,false) {}
        internal NpTaggedPacketParser(int mask,int batchPackets,bool pairedRaw) {
            if(mask<1 || mask>7 || (pairedRaw && mask!=3) || batchPackets<4 || batchPackets>256 || batchPackets%4!=0)throw new ArgumentException();
            var list=new List<int>();for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0)list.Add(p);
            probes=list.ToArray();this.batchPackets=batchPackets;PairedRaw=pairedRaw;
        }
        internal void Feed(byte[] input,int count,Action<int,byte[],int,int> receive) {
            if(failed)throw new IOException("NP tagged parser previously failed.");
            if(input==null || count<0 || count>input.Length)throw new ArgumentOutOfRangeException("count");
            try {
                int at=0;
                while(at<count) {
                    int target=stored<6?6:packet[2]==3?6+NpCameraRecording.PayloadLength(packet[3]):512;
                    int n=Math.Min(target-stored,count-at);Buffer.BlockCopy(input,at,packet,stored,n);stored+=n;at+=n;
                    if(stored<6)continue;
                    target=packet[2]==3?6+NpCameraRecording.PayloadLength(packet[3]):512;
                    if(stored!=target)continue;
                    if(BitConverter.ToUInt16(packet,0)!=0xa512)throw new IOException("NP packet magic lost.");
                    ushort seq=BitConverter.ToUInt16(packet,4);
                    if(packet[2]==3) {
                        if(PairedRaw && lane!=0)throw new IOException("Camera packet interrupted a paired NP four-lane group.");
                        if(OnCamera==null)throw new IOException("NP camera data has no recorder attached.");
                        OnCamera(packet,6,target-6,packet[3],seq);stored=0;continue;
                    }
                    if(packet[2]==5) {
                        if(packet[3]!=8 || lane!=0 || OnMisc==null)throw new IOException("Invalid NP misc packet/boundary.");
                        if(haveMisc && seq!=miscSequence) {SequenceGaps++;throw new IOException("NP misc sequence gap.");}
                        miscSequence=unchecked((ushort)(seq+1));haveMisc=true;OnMisc(packet,6,506);MiscPackets++;stored=0;continue;
                    }
                    int probe=PairedRaw?0:probes[(int)((Packets/batchPackets)%probes.Length)];
                    if(PairedRaw) {
                        if(packet[2]!=0x77 || packet[3]!=lane || packet[6]!=3 || packet[7]!=1)
                            throw new IOException("Paired NP wire/lane/mask/version changed at packet "+Packets+".");
                    } else if(packet[2]!=0x76 || packet[3]!=(0x60|probe|(lane<<2)))
                        throw new IOException("NP probe/time-lane order changed at packet "+Packets+".");
                    if(seq!=unchecked((ushort)Packets)) {SequenceGaps++;throw new IOException("NP global sequence gap at packet "+Packets+".");}
                    int payload=PairedRaw?504:506,header=PairedRaw?8:6;
                    Buffer.BlockCopy(packet,header,lanes,lane*payload,payload);stored=0;Packets++;lane++;
                    if(lane!=4)continue;
                    if(PairedRaw) {
                        for(int i=0;i<252;i++)for(int l=0;l<4;l++) {
                            output[i*4+l]=lanes[l*504+i*2];
                            output[1008+i*4+l]=lanes[l*504+i*2+1];
                        }
                        lane=0;receive(1,output,0,1008);receive(2,output,1008,1008);
                    } else {
                        for(int i=0;i<506;i++)for(int l=0;l<4;l++)output[i*4+l]=lanes[l*506+i];
                        lane=0;receive(probe,output,0,output.Length);
                    }
                }
            } catch {failed=true;throw;}
        }
        internal void Complete() {
            if(failed || stored!=0 || lane!=0)throw new IOException("Incomplete NP packet/time-lane group; recording is incomplete.");
        }
    }

    // Pure CAM1 decoding: no device access. Version 2 is the confirmed 128-byte
    // extension; the first 64 bytes retain the version 1 diagnostics.
    internal sealed class NpCameraStatus
    {
        internal uint Version,Flags,RateHz,CapturedFrames,DeliveredFrames,Packets,PayloadBytes;
        internal uint FailureCount,FailureOrigin,FailurePhase,FailureSpiError,FailureDmaError,Recoveries,LastStatus,CreditBytes;
        internal uint RequestedHz,MaxHz,SclkHz,Profile,DroppedFrames,MissedDeadlines,FrameId,FrameTick;
        internal uint FrameBytes,Width,Height,KernelHz,SensorMaxHz,MeasuredMillihz;
        internal uint CapabilityFlags;
        internal bool SupportsCameraOnly {get{return Version==2 && (CapabilityFlags&1)!=0;}}
        internal bool CameraOnlyActive {get{return Version==2 && (Flags&256)!=0;}}
        internal bool SupportsControls {get{return Version==2 && MaxHz!=0;}}
        internal static NpCameraStatus Parse(byte[] b,int count) {
            if(b==null || count<64 || count>b.Length || BitConverter.ToUInt32(b,0)!=0x314d4143)
                throw new IOException("Invalid CAM1 camera status.");
            uint version=BitConverter.ToUInt32(b,4);
            if((version!=1 && version!=2) || count!=(version==1?64:128))
                throw new IOException("Unsupported CAM1 camera status version/length.");
            var s=new NpCameraStatus {
                Version=version,Flags=BitConverter.ToUInt32(b,8),RateHz=BitConverter.ToUInt32(b,12),
                CapturedFrames=BitConverter.ToUInt32(b,16),DeliveredFrames=BitConverter.ToUInt32(b,20),
                Packets=BitConverter.ToUInt32(b,24),PayloadBytes=BitConverter.ToUInt32(b,28),
                FailureCount=BitConverter.ToUInt32(b,32),FailureOrigin=BitConverter.ToUInt32(b,36),
                FailurePhase=BitConverter.ToUInt32(b,40),FailureSpiError=BitConverter.ToUInt32(b,44),
                FailureDmaError=BitConverter.ToUInt32(b,48),Recoveries=BitConverter.ToUInt32(b,52),
                LastStatus=BitConverter.ToUInt32(b,56),CreditBytes=BitConverter.ToUInt32(b,60)};
            if(version==1)return s;
            s.RequestedHz=BitConverter.ToUInt32(b,64);s.MaxHz=BitConverter.ToUInt32(b,68);
            s.SclkHz=BitConverter.ToUInt32(b,72);s.Profile=BitConverter.ToUInt32(b,76);
            s.DroppedFrames=BitConverter.ToUInt32(b,80);s.MissedDeadlines=BitConverter.ToUInt32(b,84);
            s.FrameId=BitConverter.ToUInt32(b,88);s.FrameTick=BitConverter.ToUInt32(b,92);
            s.FrameBytes=BitConverter.ToUInt32(b,96);s.Width=BitConverter.ToUInt32(b,100);s.Height=BitConverter.ToUInt32(b,104);
            s.KernelHz=BitConverter.ToUInt32(b,108);s.SensorMaxHz=BitConverter.ToUInt32(b,112);s.MeasuredMillihz=BitConverter.ToUInt32(b,116);
            s.CapabilityFlags=BitConverter.ToUInt32(b,120);
            if(s.FrameBytes!=NpCameraRecording.FrameBytes || s.Width!=320 || (s.Height!=320 && s.Height!=500) || s.Profile>1 ||
               s.SensorMaxHz!=58 || s.MaxHz>s.SensorMaxHz || s.RequestedHz>s.SensorMaxHz ||
               s.RateHz!=Math.Min(s.RequestedHz,s.MaxHz) || (s.CapabilityFlags&~1U)!=0 || BitConverter.ToUInt32(b,124)!=0 ||
               (s.CameraOnlyActive && !s.SupportsCameraOnly))
                throw new IOException("Unsupported camera raster/profile or inconsistent advertised rate.");
            return s;
        }
        internal void Save(string path) {
            File.WriteAllLines(path,new[]{"version="+Version,"flags="+Flags,"requested_rate_hz="+RequestedHz,
                "effective_rate_hz="+RateHz,"calculated_max_rate_hz="+MaxHz,"measured_capture_millihz="+MeasuredMillihz,
                "profile="+Profile,"sensor_max_rate_hz="+SensorMaxHz,"sclk_hz="+SclkHz,"kernel_hz="+KernelHz,
                "captured_frames="+CapturedFrames,"delivered_frames="+DeliveredFrames,"dropped_frames="+DroppedFrames,
                "missed_deadlines="+MissedDeadlines,"frame_id="+FrameId,"frame_tick_ms="+FrameTick,
                "failure_count="+FailureCount,"failure_origin="+FailureOrigin,"failure_phase="+FailurePhase,
                "failure_spi_error="+FailureSpiError,"failure_dma_error="+FailureDmaError,"recoveries="+Recoveries,
                "last_status="+LastStatus,"packets="+Packets,"payload_bytes="+PayloadBytes,"credit_bytes="+CreditBytes,
                "camera_only_supported="+SupportsCameraOnly,"camera_only_active="+CameraOnlyActive});
        }
    }

    // Camera continuity is independent of NP continuity. Preserve every received
    // packed byte in the established misc.dat format and report damaged camera
    // frames separately; a lost camera frame must not discard neural samples.
    internal sealed class NpCameraRecording : IDisposable
    {
        internal const int FrameBytes=320*500,FullPayloadBytes=506;
        readonly FileStream file;
        readonly StreamWriter index;
        ushort nextSequence,firstSequence,lastSequence;
        long frameOffset;
        bool haveSequence,frameOpen,frameValid,finished;
        internal long Packets {get;private set;}
        internal long PayloadBytes {get;private set;}
        internal long Frames {get;private set;}
        internal long SequenceGaps {get;private set;}
        internal long FramingErrors {get;private set;}
        internal long PendingFrameBytes {get;private set;}
        internal bool Complete {get{return finished && Frames!=0 && SequenceGaps==0 && FramingErrors==0;}}
        internal static int PayloadLength(byte flags) {
            if((flags&~3)!=0)throw new IOException("Unsupported NP camera packet flags.");
            return (flags&2)!=0?FrameBytes%FullPayloadBytes:FullPayloadBytes;
        }
        internal NpCameraRecording(string folder) {
            file=new FileStream(Path.Combine(folder,"misc.dat"),FileMode.CreateNew,FileAccess.Write,
                FileShare.Read,128*1024,FileOptions.SequentialScan);
            try {
                index=new StreamWriter(new FileStream(Path.Combine(folder,"misc.frames.csv"),FileMode.CreateNew,
                    FileAccess.Write,FileShare.Read,16*1024,FileOptions.SequentialScan),new System.Text.UTF8Encoding(false));
                index.WriteLine("offset_bytes,length_bytes,complete,first_packet_sequence,last_packet_sequence");
            } catch {file.Dispose();throw;}
        }
        void IndexFrame(bool complete) {
            index.WriteLine(frameOffset+","+PendingFrameBytes+","+(complete?"True":"False")+","+firstSequence+","+lastSequence);
        }
        // True means this packet can also enter the lossy preview assembler.
        internal bool Write(byte[] bytes,int offset,int count,byte flags,ushort sequence) {
            if(finished)throw new InvalidOperationException("Camera recording is closed.");
            if(bytes==null || offset<0 || count!=PayloadLength(flags) || offset>bytes.Length-count)
                throw new ArgumentOutOfRangeException("count");
            if(haveSequence && sequence!=nextSequence) {
                SequenceGaps++;frameValid=false;
            }
            nextSequence=unchecked((ushort)(sequence+1));haveSequence=true;
            if((flags&1)!=0) {
                if(frameOpen) {FramingErrors++;IndexFrame(false);}
                frameOpen=false;
            }
            if(!frameOpen) {
                frameOpen=true;frameValid=(flags&1)!=0;PendingFrameBytes=0;
                frameOffset=PayloadBytes;firstSequence=sequence;
                if(!frameValid)FramingErrors++;
            }
            file.Write(bytes,offset,count);Packets++;PayloadBytes+=count;lastSequence=sequence;
            PendingFrameBytes+=count;
            if(PendingFrameBytes>FrameBytes) {frameValid=false;FramingErrors++;}
            bool valid=frameValid;
            if((flags&2)!=0) {
                frameOpen=false;
                if(PendingFrameBytes!=FrameBytes) {FramingErrors++;valid=false;}
                IndexFrame(valid);
                if(valid)Frames++;
                PendingFrameBytes=0;
            }
            return valid;
        }
        internal void Finish() {
            if(finished)return;
            if(frameOpen) {FramingErrors++;IndexFrame(false);frameOpen=false;}
            index.Flush();((FileStream)index.BaseStream).Flush(true);
            file.Flush(true);finished=true;
        }
        public void Dispose() {try {Finish();}finally {try {index.Dispose();}finally {file.Dispose();}}}
    }

    internal sealed class NpCaptureOptions
    {
        internal readonly bool MergeProbeDat;
        internal readonly bool UpsampleAp15To20;
        internal NpCaptureOptions(bool mergeProbeDat,bool upsampleAp15To20) {
            MergeProbeDat=mergeProbeDat;UpsampleAp15To20=upsampleAp15To20;
        }
    }

    // FileStream's internal buffer does not remove the call overhead from tens
    // of thousands of decoded rows per second. Coalesce every logical output
    // into MiB writes and retain Flush(true) as the durability boundary.
    internal sealed class NpBufferedSequentialWriter : IDisposable
    {
        const int BufferBytes=1024*1024;
        readonly FileStream file;
        readonly byte[] buffer=new byte[BufferBytes];
        int buffered;bool disposed;
        internal NpBufferedSequentialWriter(string path) {
            file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,
                128*1024,FileOptions.SequentialScan);
        }
        void Drain() {
            if(buffered==0)return;
            file.Write(buffer,0,buffered);buffered=0;
        }
        internal void Write(short[] source,int offset,int samples) {
            if(disposed)throw new ObjectDisposedException("NP output writer");
            if(source==null || offset<0 || samples<0 || offset>source.Length-samples)
                throw new ArgumentOutOfRangeException("samples");
            int sourceByte=checked(offset*2),count=checked(samples*2);
            while(count!=0) {
                int n=Math.Min(count,buffer.Length-buffered);
                Buffer.BlockCopy(source,sourceByte,buffer,buffered,n);
                buffered+=n;sourceByte+=n;count-=n;
                if(buffered==buffer.Length)Drain();
            }
        }
        internal void Flush() {if(disposed)throw new ObjectDisposedException("NP output writer");Drain();file.Flush(true);}
        public void Dispose() {
            if(disposed)return;
            try {Drain();} finally {disposed=true;file.Dispose();}
        }
    }

    // Deterministic streaming 4:3 AP resampler. Output timestamps are spaced
    // by 3/4 of an input interval and samples are linearly interpolated using
    // integer arithmetic. It adds at most one native sample of latency.
    internal sealed class NpDatWriter : IDisposable
    {
        readonly NpBufferedSequentialWriter file;
        readonly int channels;
        readonly bool upsample4To3;
        readonly short[] previous,interpolated;
        long nextOutputQuarter;
        bool havePrevious,disposed;
        internal long InputRows { get; private set; }
        internal long OutputRows { get; private set; }
        internal long FileBytes { get {return checked(OutputRows*channels*2L);} }
        internal NpDatWriter(string path,int channels,bool upsample4To3) {
            if(channels<=0)throw new ArgumentOutOfRangeException("channels");
            this.channels=channels;this.upsample4To3=upsample4To3;
            previous=upsample4To3?new short[channels]:null;
            interpolated=upsample4To3?new short[channels]:null;
            file=new NpBufferedSequentialWriter(path);
        }
        internal void Write(short[] source,int rows) {
            if(disposed)throw new ObjectDisposedException("NP DAT writer");
            if(rows<0)throw new ArgumentOutOfRangeException("rows");
            if(rows==0)return;
            if(source==null || checked(rows*channels)>source.Length)throw new ArgumentOutOfRangeException("rows");
            if(!upsample4To3) {
                file.Write(source,0,checked(rows*channels));InputRows+=rows;OutputRows+=rows;return;
            }
            for(int row=0;row<rows;row++)WriteUpsampledRow(source,row*channels);
        }
        void WriteUpsampledRow(short[] source,int offset) {
            if(!havePrevious) {
                file.Write(source,offset,channels);Array.Copy(source,offset,previous,0,channels);
                havePrevious=true;InputRows=1;OutputRows=1;nextOutputQuarter=3;return;
            }
            long inputIndex=InputRows,lowerQuarter=(inputIndex-1)*4,upperQuarter=inputIndex*4;
            while(nextOutputQuarter<=upperQuarter) {
                int fraction=checked((int)(nextOutputQuarter-lowerQuarter));
                for(int c=0;c<channels;c++) {
                    int sum=(4-fraction)*previous[c]+fraction*source[offset+c];
                    interpolated[c]=(short)(sum>=0?(sum+2)/4:(sum-2)/4);
                }
                file.Write(interpolated,0,channels);OutputRows++;nextOutputQuarter+=3;
            }
            Array.Copy(source,offset,previous,0,channels);InputRows++;
        }
        internal void Flush() {file.Flush();}
        public void Dispose() {if(disposed)return;disposed=true;file.Dispose();}
    }

    // Queue order is independent of wall-clock adjustments on the host.
    internal struct NpReadStamp {
        internal readonly DateTime Utc;
        internal readonly long Order;
        internal NpReadStamp(DateTime utc,long order){Utc=utc;Order=order;}
    }

    // Post-drain, file-only timeline. No sensor/device ticks or processing clock.
    internal static class NpLfpTimeWriter {
        internal static Dictionary<string,object> Write(string dat,int channels,int apHz,DateTime anchorUtc,long anchorRows,TimeZoneInfo zone=null) {
            if(channels<=0 || apHz<=0 || anchorRows<0)throw new ArgumentOutOfRangeException();
            long bytes;using(var input=new FileStream(dat,FileMode.Open,FileAccess.Read,FileShare.Read))bytes=input.Length;
            long rowBytes=checked(channels*2L),rows=bytes/rowBytes,expectedBytes=checked(rows*4);
            if(rows>0 && (anchorRows==0 || anchorUtc.Kind!=DateTimeKind.Utc || anchorUtc==DateTime.MinValue))
                throw new IOException("Saved LFP rows have no USB read-completion time anchor; PC time will not be fabricated.");
            zone=zone??TimeZoneInfo.Local;
            DateTime local=rows==0?DateTime.MinValue:TimeZoneInfo.ConvertTimeFromUtc(anchorUtc,zone);
            long day=checked(TimeSpan.TicksPerDay*apHz),step=12*TimeSpan.TicksPerSecond;
            long rowsPerDay=checked(apHz*7200L);
            long scaled=rows==0?0:checked(local.TimeOfDay.Ticks*apHz-((anchorRows-1)%rowsPerDay)*step);
            scaled=(scaled%day+day)%day;
            string path=Path.Combine(Path.GetDirectoryName(dat),"time.dat"),partial=path+".partial";
            if(File.Exists(path))throw new IOException("LFP time.dat already exists; existing recording files will not be replaced.");
            var buffer=new byte[64*1024];int used=0;
            using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                for(long row=0;row<rows;row++) {
                    uint ms=(uint)(scaled/(apHz*TimeSpan.TicksPerMillisecond));
                    buffer[used++]=(byte)ms;buffer[used++]=(byte)(ms>>8);buffer[used++]=(byte)(ms>>16);buffer[used++]=(byte)(ms>>24);
                    if(used==buffer.Length){output.Write(buffer,0,used);used=0;}
                    scaled+=step;if(scaled>=day)scaled-=day;
                }
                if(used!=0)output.Write(buffer,0,used);output.Flush(true);
                if(output.Length!=expectedBytes)throw new IOException("LFP time.dat length does not match saved complete rows.");
            }
            File.Move(partial,path);
            return new Dictionary<string,object> {
                {"file","time.dat"},{"lfp_file",Path.GetFileName(dat)},{"rows",rows},{"bytes",expectedBytes},
                {"format","headerless little-endian uint32 milliseconds of local day"},{"day_modulus_ms",86400000},
                {"lfp_rate_numerator",apHz},{"lfp_rate_denominator",12},{"lfp_trailing_bytes",bytes%rowBytes},
                {"semantics","host USB read-completion observation anchoring a uniform derived LFP grid; not hardware sample time or synchronization"},
                {"anchor_read_completion_utc",rows==0?null:anchorUtc.ToString("O",CultureInfo.InvariantCulture)},
                {"anchor_logical_lfp_row_index",rows==0?null:(object)(anchorRows-1)},
                {"anchor_local",rows==0?null:local.ToString("O",CultureInfo.InvariantCulture)},
                {"local_timezone",zone.Id},{"anchor_utc_offset_minutes",rows==0?null:(object)zone.GetUtcOffset(anchorUtc).TotalMinutes},
                {"rounding","floor to milliseconds; sub-ms rows repeat; exact rational spacing; modulo 86400000"},
                {"clock_policy","fixed local-day grid at anchor offset; does not reconstruct host clock adjustments or hardware timing"}
            };
        }
    }

    // Merge rows only when all active probes present the same NP hardware frame
    // counter. This converts asynchronous decoder completion into deterministic
    // channel-major probe ordering without guessing or silently dropping rows.
    internal sealed class NpMergedDatWriter : IDisposable
    {
        const int ChannelsPerProbe=384,PendingRows=4096;
        sealed class RowRing {
            internal readonly short[] Samples=new short[PendingRows*ChannelsPerProbe];
            internal readonly uint[] Counters=new uint[PendingRows];
            internal readonly NpReadStamp[] ReadStamps=new NpReadStamp[PendingRows];
            internal int Head,Count;
            internal bool IsFull {get{return Count==PendingRows;}}
            internal void Add(uint counter,short[] source,NpReadStamp readStamp) {
                if(Count==PendingRows)throw new IOException("Merged NP DAT queue cannot keep up.");
                int tail=(Head+Count)%PendingRows;
                Array.Copy(source,0,Samples,tail*ChannelsPerProbe,ChannelsPerProbe);
                Counters[tail]=counter;ReadStamps[tail]=readStamp;Count++;
            }
            internal uint Counter {get{return Counters[Head];}}
            internal void CopyTo(short[] target,int offset) {Array.Copy(Samples,Head*ChannelsPerProbe,target,offset,ChannelsPerProbe);}
            internal void Pop() {Head=(Head+1)%PendingRows;Count--;}
            internal void SavePending(string dataPath,string counterPath) {
                if(Count==0)return;
                byte[] row=new byte[ChannelsPerProbe*sizeof(short)];
                byte[] counter=new byte[sizeof(uint)];
                using(var data=new FileStream(dataPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                using(var counters=new FileStream(counterPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                    for(int i=0;i<Count;i++) {
                        int slot=(Head+i)%PendingRows;
                        Buffer.BlockCopy(Samples,slot*ChannelsPerProbe*sizeof(short),row,0,row.Length);
                        data.Write(row,0,row.Length);
                        Buffer.BlockCopy(BitConverter.GetBytes(Counters[slot]),0,counter,0,counter.Length);
                        counters.Write(counter,0,counter.Length);
                    }
                    data.Flush();counters.Flush();
                }
            }
        }
        sealed class MergedBand {
            readonly int[] probes,indexByProbe=new int[4];
            readonly RowRing[] rings;
            readonly short[] mergedRow;
            readonly string tailFolder,bandName;
            bool completed;
            internal readonly int[] TailRows=new int[4];
            internal readonly NpDatWriter Output;
            internal NpReadStamp LastReadStamp;
            internal MergedBand(string path,string band,int[] activeProbes,bool upsample) {
                probes=(int[])activeProbes.Clone();rings=new RowRing[probes.Length];
                for(int i=0;i<probes.Length;i++){indexByProbe[probes[i]]=i;rings[i]=new RowRing();}
                mergedRow=new short[checked(probes.Length*ChannelsPerProbe)];
                tailFolder=Path.Combine(Path.GetDirectoryName(path),"unpaired_tail");bandName=band;
                Output=new NpDatWriter(path,mergedRow.Length,upsample);
            }
            internal void Add(int probe,uint counter,short[] row,NpReadStamp readStamp) {
                int index=Index(probe);
                if(row==null || row.Length<ChannelsPerProbe)throw new IOException("Incomplete NP row supplied to merged writer.");
                rings[index].Add(counter,row,readStamp);Drain();
            }
            int Index(int probe) {
                int index=probe>=0 && probe<indexByProbe.Length?indexByProbe[probe]:-1;
                if(index<0 || index>=rings.Length || probes[index]!=probe)throw new IOException("Inactive NP probe supplied a merged row.");
                return index;
            }
            internal bool IsFull(int probe) {return rings[Index(probe)].IsFull;}
            void Drain() {
                while(true) {
                    for(int i=0;i<rings.Length;i++)if(rings[i].Count==0)return;
                    uint counter=rings[0].Counter;
                    for(int i=1;i<rings.Length;i++)if(rings[i].Counter!=counter)
                        throw new IOException("NP probe frame counters are not aligned for merged DAT output.");
                    NpReadStamp stamp=default(NpReadStamp);
                    for(int i=0;i<rings.Length;i++){
                        if(rings[i].ReadStamps[rings[i].Head].Order>=stamp.Order)stamp=rings[i].ReadStamps[rings[i].Head];
                        rings[i].CopyTo(mergedRow,i*ChannelsPerProbe);rings[i].Pop();
                    }
                    Output.Write(mergedRow,1);
                    LastReadStamp=stamp;
                }
            }
            internal void Complete() {
                if(completed)return;
                Drain();
                for(int i=0;i<rings.Length;i++)if(rings[i].Count!=0) {
                    // STOP may leave different numbers of complete rows per
                    // probe. Keep the counter-aligned merged prefix and save
                    // every extra native row with its exact hardware counter.
                    Directory.CreateDirectory(tailFolder);
                    string stem=Path.Combine(tailFolder,"neuropixels_"+bandName+"_probe"+probes[i]);
                    rings[i].SavePending(stem+".dat",stem+"_counters.dat");
                    TailRows[probes[i]]=rings[i].Count;
                }
                completed=true;
            }
        }
        readonly object sync=new object();
        readonly MergedBand ap,lfp;
        Exception failure;bool disposed;
        internal int ChannelCount {get;private set;}
        internal long FileBytes {get{return ap.Output.FileBytes+lfp.Output.FileBytes;}}
        internal long ApOutputRows {get{return ap.Output.OutputRows;}}
        internal long LfpOutputRows {get{return lfp.Output.OutputRows;}}
        internal DateTime LfpLastReceiveUtc {get{return lfp.LastReadStamp.Utc;}}
        internal int ApTailRows(int probe) {return ap.TailRows[probe];}
        internal int LfpTailRows(int probe) {return lfp.TailRows[probe];}
        internal NpMergedDatWriter(string folder,int[] probes,bool upsampleAp) {
            if(probes==null || probes.Length<2)throw new ArgumentException("Merged output requires multiple probes.");
            ChannelCount=checked(probes.Length*ChannelsPerProbe);
            ap=new MergedBand(Path.Combine(folder,"neuropixels_ap.dat"),"ap",probes,upsampleAp);
            try {lfp=new MergedBand(Path.Combine(folder,"neuropixels_lfp.dat"),"lfp",probes,false);}
            catch {ap.Output.Dispose();throw;}
        }
        internal void Add(int probe,uint counter,short[] apRow,int apRows,short[] lfpRow,int lfpRows,NpReadStamp readStamp=default(NpReadStamp)) {
            lock(sync) {
                if(failure!=null)throw new IOException("Merged NP DAT writer previously failed.",failure);
                try {
                    if(apRows<0 || apRows>1 || lfpRows<0 || lfpRows>1)throw new IOException("Unexpected NP decoder callback row count.");
                    while((apRows==1 && ap.IsFull(probe)) || (lfpRows==1 && lfp.IsFull(probe))) {
                        if(!Monitor.Wait(sync,NpHostQueuePolicy.StallToleranceMilliseconds))
                            throw new IOException("Merged NP DAT queue remained full beyond the ten-second host reserve.");
                        if(failure!=null)throw new IOException("Merged NP DAT writer previously failed.",failure);
                    }
                    if(apRows==1)ap.Add(probe,counter,apRow,readStamp);
                    if(lfpRows==1)lfp.Add(probe,counter,lfpRow,readStamp);
                    Monitor.PulseAll(sync);
                } catch(Exception e) {failure=e;Monitor.PulseAll(sync);throw;}
            }
        }
        internal void Complete() {lock(sync){if(failure!=null)throw new IOException("Merged NP DAT writer failed.",failure);ap.Complete();lfp.Complete();}}
        internal void Flush() {lock(sync){ap.Output.Flush();lfp.Output.Flush();}}
        public void Dispose() {if(disposed)return;disposed=true;ap.Output.Dispose();lfp.Output.Dispose();}
    }

    internal sealed class NpCaptureSession : IDisposable
    {
        readonly XeNpPacketParser legacy=new XeNpPacketParser();
        NpTaggedPacketParser tagged;
        internal readonly List<NpProbeRecording> Probes=new List<NpProbeRecording>();
        readonly NpProbeRecording[] byProbe=new NpProbeRecording[4];
        internal Action<byte[],int,int> OnMisc;
        internal Action<byte[],int,int,byte,ushort> OnCamera;
        internal Action<int,short[],int,short[],int> OnSamples;
        internal Action<int,short[],int,short[],int> SampleCorrection;
        internal Action<int> OnPayload;
        // The scope queue was designed as a single-producer ring.  Probe
        // decoder/writer lanes run independently, so serialize only this very
        // small callback; decoding and all file I/O remain parallel.
        readonly object samplesCallbackLock=new object();
        NpMergedDatWriter merged;
        NpCaptureOptions options=new NpCaptureOptions(false,false);
        internal int ProbeMask { get; private set; }
        internal int ApHz { get; private set; }
        internal int OutputApHz { get {return options.UpsampleAp15To20 && ApHz==15000?20000:ApHz;} }
        internal int OutputLfpHz { get {return ApHz/12;} }
        internal string OutputLfpHzExact { get {return NpCaptureMode.LfpHzText(ApHz);} }
        internal bool MergedDat { get {return merged!=null;} }
        internal bool UpsampledAp { get {return OutputApHz!=ApHz;} }
        internal int OutputChannelCount { get {return MergedDat?merged.ChannelCount:384;} }
        internal bool PhysicalPortsKnown { get; private set; }
        internal bool Tagged { get { return tagged!=null; } }
        internal bool IsQuadInterleaved { get { return Tagged || legacy.IsQuadInterleaved; } }
        internal long Packets { get { return Tagged?tagged.Packets:legacy.Packets; } }
        internal long MiscPackets { get { return Tagged?tagged.MiscPackets:legacy.MiscPackets; } }
        internal long SequenceGaps { get { return Tagged?tagged.SequenceGaps:legacy.SequenceGaps; } }
        internal int PendingLanePackets { get { return Tagged?tagged.PendingLanePackets:legacy.PendingLanePackets; } }
        internal long FileBytes { get {long n=MergedDat?merged.FileBytes:0;foreach(var p in Probes)n+=p.FileBytes;return n;} }
        internal long FramesChecked { get {long n=0;foreach(var p in Probes)n+=p.Decoder.FramesChecked;return n;} }
        internal long FrameErrors { get {long n=0;foreach(var p in Probes)n+=p.Decoder.FrameErrors;return n;} }
        internal long CounterErrors { get {long n=0;foreach(var p in Probes)n+=p.Decoder.CounterErrors;return n;} }
        internal long Resynchronizations { get {long n=0;foreach(var p in Probes)n+=p.Decoder.Resynchronizations;return n;} }
        internal long StartupDiscardedBytes { get {long n=0;foreach(var p in Probes)n+=p.Decoder.StartupDiscardedBytes;return n;} }
        Action<byte[],int,int> legacyReceive;
        Action<int,byte[],int,int> taggedReceive;
        bool synchronous;
        string configuredFolder;
        NpReadStamp receivingStamp;
        long readOrder;
        internal readonly List<Dictionary<string,object>> LfpTimeOutputs=new List<Dictionary<string,object>>();
        internal void Configure(string folder,XeNpStatus status,NpModeCapabilities caps) {
            Configure(folder,status,caps,new NpCaptureOptions(false,false));
        }
        // Firmware holds REC_NRESET and DMA until this completes. Existing
        // requested-mode files are still empty; no recorded data is replaced.
        internal void ReconfigureBeforeFirstData(string folder,XeNpStatus status,NpModeCapabilities caps,NpCaptureOptions requestedOptions) {
            if(Packets!=0 || SequenceGaps!=0)throw new IOException("Cannot change probe admission after data starts.");
            foreach(var p in Probes)if(p.RawBytes!=0 || p.ApRows!=0 || p.LfpRows!=0)
                throw new IOException("Cannot change a decoded probe layout.");
            if(caps==null || !caps.SupportsStartupAdmission || caps.ActiveMask==0 ||
               (caps.ActiveMask&~ProbeMask)!=0 || caps.ApHz!=ApHz)
                throw new IOException("Invalid startup-only accepted probe subset.");
            if(!string.Equals(Path.GetFullPath(folder),configuredFolder,StringComparison.OrdinalIgnoreCase))
                throw new IOException("Startup admission must retain its owned recording folder.");
            var ownedEmptyPaths=new List<string>();
            if(MergedDat) {
                ownedEmptyPaths.Add(Path.Combine(configuredFolder,"neuropixels_ap.dat"));
                ownedEmptyPaths.Add(Path.Combine(configuredFolder,"neuropixels_lfp.dat"));
            } else foreach(var p in Probes) {
                string dir=Probes.Count>1?Path.Combine(configuredFolder,"probe"+p.Probe):configuredFolder;
                ownedEmptyPaths.Add(Path.Combine(dir,"neuropixels_ap.dat"));
                ownedEmptyPaths.Add(Path.Combine(dir,"neuropixels_lfp.dat"));
            }
            foreach(string path in ownedEmptyPaths)if(!File.Exists(path) || new FileInfo(path).Length!=0)
                throw new IOException("Startup layout owns only its newly created empty DAT files.");
            foreach(var p in Probes)p.Dispose();if(merged!=null)merged.Dispose();
            foreach(string path in ownedEmptyPaths)File.Delete(path);
            Probes.Clear();Array.Clear(byProbe,0,byProbe.Length);merged=null;tagged=null;
            Configure(folder,status,caps,requestedOptions);
        }
        internal void Configure(string folder,XeNpStatus status,NpModeCapabilities caps,NpCaptureOptions requestedOptions) {
            if(Probes.Count!=0)throw new InvalidOperationException();
            if(requestedOptions==null)throw new ArgumentNullException("requestedOptions");
            Directory.CreateDirectory(folder);
            configuredFolder=Path.GetFullPath(folder);
            PhysicalPortsKnown=caps!=null;
            ProbeMask=caps==null?1:caps.ActiveMask;ApHz=caps==null?status.ApSampleRateHz:caps.ApHz;
            options=new NpCaptureOptions(requestedOptions.MergeProbeDat,requestedOptions.UpsampleAp15To20 && ApHz==15000);
            synchronous=status.UsesV1SynchronousReads;
            if(caps!=null && (caps.WireId==0x76 || caps.WireId==0x77))tagged=new NpTaggedPacketParser(ProbeMask,caps.BatchPackets,caps.WireId==0x77);
            var active=new List<int>();foreach(int id in new[]{1,2,3})if((ProbeMask&(1<<(id-1)))!=0)active.Add(id);
            if(options.MergeProbeDat && active.Count>1)merged=new NpMergedDatWriter(folder,active.ToArray(),UpsampledAp);
            foreach(int id in active) {
                // Individual output keeps the established per-probe folders.
                // Merged output writes one root AP/LFP pair in physical-port order.
                string dir=active.Count>1 && !MergedDat?Path.Combine(folder,"probe"+id):folder;
                var p=new NpProbeRecording(dir,id,Tagged?XeNpLaneLayout.GpioV1Quad:status.Layout,!MergedDat,UpsampledAp);
                p.SampleCorrection=SampleCorrection;
                Probes.Add(p);byProbe[id]=p;p.Decoded=delegate(int probe,uint counter,short[] a,int na,short[] l,int nl) {
                    if(merged!=null)merged.Add(probe,counter,a,na,l,nl,p.DecodedReadStamp);
                    lock(samplesCallbackLock)if(OnSamples!=null)OnSamples(probe,a,na,l,nl);
                };
            }
            Action<byte[],int,int> misc=delegate(byte[] b,int o,int n) {if(OnMisc==null)throw new IOException("No misc recorder.");OnMisc(b,o,n);};
            legacy.OnMisc=misc;if(Tagged)tagged.OnMisc=misc;
            legacy.OnCamera=OnCamera;if(Tagged)tagged.OnCamera=OnCamera;
            taggedReceive=Receive;
            legacyReceive=delegate(byte[] b,int o,int n) {
                if(legacy.IsQuadInterleaved) {
                    if(!synchronous)throw new IOException("Firmware advertised V2 but sent V1 quad data.");
                    Probes[0].Decoder.UseV1QuadFormat();
                }
                Receive(Probes[0].Probe,b,o,n);
            };
        }
        void Receive(int probe,byte[] b,int o,int n) {byProbe[probe].Feed(b,o,n,receivingStamp);if(OnPayload!=null)OnPayload(n);}
        internal void Feed(byte[] b,int n) {Feed(b,n,DateTime.MinValue);}
        internal void Feed(byte[] b,int n,DateTime receiveUtc) {
            receivingStamp=new NpReadStamp(receiveUtc,++readOrder);
            if(Tagged)tagged.Feed(b,n,taggedReceive);else legacy.Feed(b,n,legacyReceive);
        }
        // Call only after all workers/files have drained and closed, including
        // failed recordings. File lengths define the complete saved prefix.
        internal void WriteLfpTimes() {
            if(LfpTimeOutputs.Count!=0)throw new InvalidOperationException("LFP times already generated.");
            if(MergedDat) {
                var receipt=NpLfpTimeWriter.Write(Path.Combine(configuredFolder,"neuropixels_lfp.dat"),merged.ChannelCount,ApHz,merged.LfpLastReceiveUtc,merged.LfpOutputRows);
                receipt["file"]="time.dat";LfpTimeOutputs.Add(receipt);
            } else foreach(var probe in Probes) {
                string relative=Probes.Count>1?"probe"+probe.Probe+"/":"";
                var receipt=NpLfpTimeWriter.Write(Path.Combine(configuredFolder,relative,"neuropixels_lfp.dat"),384,ApHz,probe.LfpLastReceiveUtc,probe.LfpOutputRows);
                receipt["file"]=relative+"time.dat";receipt["lfp_file"]=relative+"neuropixels_lfp.dat";receipt["probe"]=probe.Probe;LfpTimeOutputs.Add(receipt);
            }
        }
        internal void Complete() {
            if(Tagged)tagged.Complete();else legacy.Complete();
            Exception error=null;
            foreach(var p in Probes)try {p.Complete();} catch(Exception e) {if(error==null)error=e;}
            if(merged!=null)try {merged.Complete();} catch(Exception e) {if(error==null)error=e;}
            if(error!=null)throw error;
        }
        internal void Flush() {foreach(var p in Probes)p.Flush();if(merged!=null)merged.Flush();}
        internal void WriteStatus(string folder,bool complete) {
            var lines=new List<string> {"complete="+complete,"probe_mask="+ProbeMask,"probe_count="+Probes.Count,
                "native_ap_sample_rate_hz_per_probe="+ApHz,"output_ap_sample_rate_hz="+OutputApHz,
                "output_lfp_sample_rate_hz="+OutputLfpHzExact,"output_lfp_rate_numerator="+ApHz,
                "output_lfp_rate_denominator=12","ap_upsampled_15_to_20khz="+UpsampledAp,
                "ap_resampler="+(UpsampledAp?"deterministic linear 4:3; integer nearest rounding; <=1 native-row latency":"none"),
                "dat_layout="+(MergedDat?"merged":"individual"),"dat_channel_count="+OutputChannelCount,
                "raw_lane_bin_saved=False","wire_id="+(Tagged?(tagged.PairedRaw?"0x77":"0x76"):"0x06"),
                SampleCorrection==null?"codes=signed nominal ADC counts stored in little-endian int16; no gain calibration or microvolt conversion":"codes=signed calibrated nominal ADC counts stored in little-endian int16; see adc_calibration.json; no hardware trim or microvolt conversion claim",
                "sample_transform_version=1",SampleCorrection==null?"sample_transform=512 - raw10":"sample_transform=factory digital ADC threshold/offset; 512 - corrected_raw10; factory gain",
                SampleCorrection==null?"sample_range=-511..512":"sample_range=int16; factory-dependent; no clipping",
                "sample_polarity=inverted relative to raw ASIC ADC codes; ASIC negative analog gain undone",
                "legacy_dat_encoding=USB console <=1.0.3.16 NP1 DAT: unsigned raw codes 0..1023 stored as int16; existing DAT files are not modified",
                "startup_qualification=2 consecutive superframes; sync/fixed/fill/heartbeat/counter continuity",
                "adc_bit_integrity=unknown; NP source has no payload checksum",
                "alignment="+(MergedDat?"merged rows require identical NP hardware frame counters":"independent per-probe files"),
                "lfp_time_format=little-endian uint32 milliseconds of local day; one value per saved complete LFP DAT row",
                "lfp_time_semantics=host read-completion observation anchor; exact rational derived grid; not hardware synchronization",
                "lfp_time_outputs="+LfpTimeOutputs.Count};
            foreach(var receipt in LfpTimeOutputs)foreach(string key in new[]{"rows","anchor_read_completion_utc","anchor_logical_lfp_row_index","local_timezone","anchor_utc_offset_minutes","clock_policy","lfp_trailing_bytes"})
                lines.Add(receipt["file"]+"."+key+"="+receipt[key]);
            if(MergedDat) {
                lines.Add("merged_channel_order=ascending physical probe; 384 contiguous channels per probe");
                lines.Add("merged_ap_output_rows="+merged.ApOutputRows);lines.Add("merged_lfp_output_rows="+merged.LfpOutputRows);
                lines.Add("unpaired_tail_format=native 384-channel int16 rows; matching uint32 hardware counters in unpaired_tail/*_counters.dat");
            }
            foreach(var p in Probes) {
                string prefix="probe"+p.Probe+".";
                lines.Add(prefix+"raw_bytes="+p.RawBytes);lines.Add(prefix+"ap_rows="+p.ApRows);lines.Add(prefix+"lfp_rows="+p.LfpRows);
                if(MergedDat) {lines.Add(prefix+"unpaired_ap_tail_rows="+merged.ApTailRows(p.Probe));lines.Add(prefix+"unpaired_lfp_tail_rows="+merged.LfpTailRows(p.Probe));}
                lines.Add(prefix+"ap_output_rows="+p.ApOutputRows);lines.Add(prefix+"lfp_output_rows="+p.LfpOutputRows);
                lines.Add(prefix+"layout="+p.Decoder.Layout);lines.Add(prefix+"fixed_word="+p.Decoder.ExpectedFixedWord);
                lines.Add(prefix+"validated_frames="+p.Decoder.FramesChecked);lines.Add(prefix+"frame_errors="+p.Decoder.FrameErrors);
                lines.Add(prefix+"counter_errors="+p.Decoder.CounterErrors);lines.Add(prefix+"relocks="+p.Decoder.Resynchronizations);
                lines.Add(prefix+"startup_discarded_bytes="+p.Decoder.StartupDiscardedBytes);
                lines.Add(prefix+"first_superframe_counter="+p.Decoder.FirstSuperframeCounter);
                lines.Add(prefix+"initial_lfp_slot="+p.Decoder.InitialLfpSlot);
                lines.Add(prefix+"startup_discarded_lfp_superframes="+p.Decoder.StartupDiscardedLfpSuperframes);
                lines.Add(prefix+"lfp_phase_errors="+p.Decoder.LfpPhaseErrors);
            }
            lines.Add("physical_ports_known="+PhysicalPortsKnown);
            if(!PhysicalPortsKnown)lines.Add("port_mapping=legacy logical stream 1; physical connector not reported by firmware");
            File.WriteAllLines(Path.Combine(folder,"neuropixels_probes.txt"),lines);
        }
        public void Dispose() {Exception error=null;foreach(var p in Probes)try{p.Dispose();}catch(Exception e){if(error==null)error=e;}if(merged!=null)try{merged.Dispose();}catch(Exception e){if(error==null)error=e;}if(error!=null)throw error;}
    }

    internal sealed class NpProbeRecording : IDisposable
    {
        const int DecodeBlockBytes=128*1024,DecodeBuffers=64;
        sealed class DecodeBlock {
            internal readonly byte[] Buffer;internal readonly int Count;internal readonly NpReadStamp ReadStamp;
            internal DecodeBlock(byte[] buffer,int count,NpReadStamp readStamp){Buffer=buffer;Count=count;ReadStamp=readStamp;}
        }
        internal int Probe;
        internal XeNpStreamDecoder Decoder;
        NpDatWriter ap,lfp;
        readonly BlockingCollection<byte[]> free=new BlockingCollection<byte[]>(DecodeBuffers);
        readonly BlockingCollection<DecodeBlock> queued=new BlockingCollection<DecodeBlock>(DecodeBuffers);
        Thread worker;
        Exception workerFailure;
        byte[] producerBuffer;int producerBytes;
        NpReadStamp producerStamp;
        internal NpReadStamp DecodedReadStamp;
        internal DateTime LfpLastReceiveUtc;
        bool addingCompleted,workerJoined,disposed;
        internal long RawBytes,ApRows,LfpRows;
        internal long ApOutputRows {get{return ap==null?0:ap.OutputRows;}}
        internal long LfpOutputRows {get{return lfp==null?0:lfp.OutputRows;}}
        internal long FileBytes { get { return (ap==null?0:ap.FileBytes)+(lfp==null?0:lfp.FileBytes); } }
        internal Action<int,uint,short[],int,short[],int> Decoded;
        internal Action<int,short[],int,short[],int> SampleCorrection;
        Action<short[],int,short[],int> samples;
        internal NpProbeRecording(string folder,int probe,XeNpLaneLayout layout) {
            Initialize(folder,probe,layout,true,false);
        }
        internal NpProbeRecording(string folder,int probe,XeNpLaneLayout layout,bool writeDat,bool upsampleAp) {
            Initialize(folder,probe,layout,writeDat,upsampleAp);
        }
        void Initialize(string folder,int probe,XeNpLaneLayout layout,bool writeDat,bool upsampleAp) {
            Probe=probe;Decoder=new XeNpStreamDecoder(layout);Directory.CreateDirectory(folder);
            try {
                if(writeDat) {
                    ap=new NpDatWriter(Path.Combine(folder,"neuropixels_ap.dat"),384,upsampleAp);
                    lfp=new NpDatWriter(Path.Combine(folder,"neuropixels_lfp.dat"),384,false);
                }
                for(int i=0;i<DecodeBuffers;i++)free.Add(new byte[DecodeBlockBytes]);
                worker=new Thread(DecodeLoop) {IsBackground=true,Priority=ThreadPriority.AboveNormal,Name="NP"+probe+" decoder/writer"};
                worker.Start();
            } catch {Dispose();throw;}
            samples=OnSamples;
        }
        void OnSamples(short[] a,int na,short[] l,int nl) {
            if(SampleCorrection!=null)SampleCorrection(Probe,a,na,l,nl);
            if(ap!=null)ap.Write(a,na);if(lfp!=null)lfp.Write(l,nl);
            if(nl>0)LfpLastReceiveUtc=DecodedReadStamp.Utc;
            Interlocked.Add(ref ApRows,na);Interlocked.Add(ref LfpRows,nl);
            if(Decoded!=null)Decoded(Probe,Decoder.LastSuperframeCounter,a,na,l,nl);
        }
        void DecodeLoop() {
            try {
                foreach(DecodeBlock block in queued.GetConsumingEnumerable()) {
                    try {
                        Interlocked.Add(ref RawBytes,block.Count);
                        DecodedReadStamp=block.ReadStamp;
                        Decoder.Feed(block.Buffer,0,block.Count,samples);
                    } finally {free.Add(block.Buffer);}
                }
                Decoder.Complete();
            } catch(Exception e) {Interlocked.CompareExchange(ref workerFailure,e,null);}
        }
        void ThrowWorkerFailure() {
            Exception e=Volatile.Read(ref workerFailure);
            if(e!=null)throw new IOException("Probe "+Probe+" decoder/writer failed: "+e.Message,e);
        }
        void PublishProducer() {
            if(producerBytes==0)return;
            ThrowWorkerFailure();
            if(!queued.TryAdd(new DecodeBlock(producerBuffer,producerBytes,producerStamp)))
                throw new IOException("Probe "+Probe+" decoder queue cannot keep up; no probe data was dropped.");
            producerBuffer=null;producerBytes=0;
        }
        internal void Feed(byte[] data,int offset,int count) {Feed(data,offset,count,default(NpReadStamp));}
        internal void Feed(byte[] data,int offset,int count,NpReadStamp readStamp) {
            if(addingCompleted || disposed)throw new InvalidOperationException("Probe recording input is closed.");
            if(data==null || offset<0 || count<0 || offset>data.Length-count)throw new ArgumentOutOfRangeException("count");
            ThrowWorkerFailure();
            while(count!=0) {
                while(producerBuffer==null && !free.TryTake(out producerBuffer,100)) {
                    ThrowWorkerFailure();
                    if(addingCompleted || disposed)throw new IOException("Probe "+Probe+" decoder queue closed during backpressure.");
                }
                int n=Math.Min(count,producerBuffer.Length-producerBytes);
                Buffer.BlockCopy(data,offset,producerBuffer,producerBytes,n);
                producerBytes+=n;offset+=n;count-=n;producerStamp=readStamp;
                if(producerBytes==producerBuffer.Length)PublishProducer();
            }
        }
        internal void Complete() {
            if(!addingCompleted) {
                PublishProducer();addingCompleted=true;queued.CompleteAdding();
            }
            if(!workerJoined && worker!=null) {worker.Join();workerJoined=true;}
            ThrowWorkerFailure();
        }
        internal void Flush() {if(ap!=null)ap.Flush();if(lfp!=null)lfp.Flush();}
        public void Dispose() {
            if(disposed)return;disposed=true;
            if(!addingCompleted) {addingCompleted=true;queued.CompleteAdding();}
            if(!workerJoined && worker!=null) {worker.Join();workerJoined=true;}
            if(ap!=null)ap.Dispose();if(lfp!=null)lfp.Dispose();
            free.Dispose();queued.Dispose();
        }
    }
}
