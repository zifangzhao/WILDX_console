using System;
using System.IO;
namespace CE32_console {
 internal static class NpV2SdControl {
  internal const uint RequestMagic=0x32444c4e,ReplyMagic=0x32524c4e;
  internal const uint Live7500=0x00100000U,Stored7500=0x00200000U;
  internal const uint CatalogDegraded=0x00800000U,OverwriteAvailable=0x04000000U;
  internal static uint NextId(NpSdStatus s){
   if(s.LastId!=0)return NpSdControl.NextId(s);
   uint id=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0);return id==0?1:id;
  }
  internal static uint ProfileTicks(uint mask,uint hz){
   if(hz!=7500 && hz!=15000 && hz!=20000 && hz!=30000)return 0;
   if(mask==1)return 122880;
   if(mask==3)return 61440;
   if(mask==7)return 40960;
   return 0;
  }
  internal static bool IsProfile(uint mask,uint hz){return ProfileTicks(mask,hz)!=0;}
  internal static string ProbePrefix(uint mask){return mask==1?"probe 1":mask==3?"probes 1 + 2":mask==7?"probes 1 + 2 + 3":"invalid probe mask";}
  static uint Ticks(uint mask,uint hz){return ProfileTicks(mask,hz);}
  static bool ValidStatus(NpSdStatus s){return s!=null && Ticks(s.Mask,s.Hz)!=0 && (s.State<=7 || s.State==255);}
  // The preview wire decodes P1/P2's PSSI lanes. A raw P3 stream remains
  // recordable, but never creates a P3 preview selection.
  internal static bool CanPreview(NpSdStatus s){return ValidStatus(s) && s.State==3 && (s.Flags&128)!=0;}
  // Keep the five-argument reflection contract and all legacy call defaults.
  internal static byte[] Request(uint op,uint id,uint hz=0,uint blocks=0,ulong utc=0){
   return Request(op,id,hz,blocks,utc,3);
  }
  internal static byte[] Request(uint op,uint id,uint hz,uint blocks,ulong utc,uint mask){
   bool profile=op==1||op==2||op==4;
   if(id==0 || op>8 || (op!=4 && utc!=0) || (profile && Ticks(mask,hz)==0))throw new ArgumentException("Invalid V2 command/id/profile");
   var b=new byte[64];uint[] w={RequestMagic,2,op,id,profile?mask:0U,hz,blocks,(uint)utc,(uint)(utc>>32)};
   for(int i=0;i<w.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(w[i]),0,b,4*i,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal static byte[] ModeRequest(uint id,uint mode){
   if(mode<1||mode>3)throw new ArgumentException("Unsupported NP2 mode");
   var b=Request(5,id);Buffer.BlockCopy(BitConverter.GetBytes(mode),0,b,16,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal static byte[] StorageRequest(uint id,bool format){
   var b=Request(format?7U:6U,id);
   if(format)Buffer.BlockCopy(BitConverter.GetBytes(0x3850534eU),0,b,16,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  // With this offer, Start is a proposed discard boundary, not safe free space.
  internal static bool HasOverwriteOffer(NpSdStatus s){return s!=null&&(s.Flags&OverwriteAvailable)!=0;}
  internal static bool CanOverwriteFromLastKnownPosition(NpSdStatus s){
   const uint required=CatalogDegraded|OverwriteAvailable|131072U|17U;
   return CanPrepareStorage(s)&&s.State==1&&s.Error==0&&s.Fault==0&&(s.Flags&required)==required&&
    s.Start>=8192&&s.Start%8192==0&&s.Start<s.Capacity;
  }
  internal static byte[] OverwriteRequest(NpSdStatus s,uint id,uint expectedStart,uint expectedCapacity){
   if(!CanOverwriteFromLastKnownPosition(s)||s.Start!=expectedStart||s.Capacity!=expectedCapacity)
    throw new IOException("The overwrite position or card changed. Read status and confirm again; no command sent.");
   var b=Request(0,id);uint[] words={12U,0x574f324eU,expectedStart,expectedCapacity};
   Buffer.BlockCopy(BitConverter.GetBytes(words[0]),0,b,8,4);
   for(int i=1;i<words.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,b,12+i*4,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal static bool CanConfigure(NpSdStatus s){
   return s!=null&&!s.Busy &&
    (s.State==1||s.State==5||s.State==7||(s.State==0&&(s.Flags&16384)!=0));
  }
  internal static bool CanSelectProfile(NpSdStatus s,uint mask,uint hz){
   return CanConfigure(s)&&IsProfile(mask,hz)&&(hz!=7500U||(s.Flags&Live7500)!=0);
  }
  internal static bool SupportsLive7500(NpSdStatus s){return s!=null&&(s.Flags&Live7500)!=0;}
  internal static bool SupportsCurrentLiveProfile(NpSdStatus s){return ValidStatus(s)&&(s.Hz!=7500U||SupportsLive7500(s));}
  internal static bool RecoveryRequiresUpgrade(NpSdStatus s){return NeedsRecovery(s)&&(s.Flags&Stored7500)==0;}
  internal static bool CanPrepareStorage(NpSdStatus s){
   // A previous mount/format command error must not prevent an explicit retry.
   return s!=null&&!s.Busy&&(s.Flags&16384)!=0&&s.Capacity!=0&&(s.State==0||s.State==1);
  }
  internal static bool NeedsRecovery(NpSdStatus s){return s!=null&&(s.Flags&131072)!=0;}
  internal static bool IsFull(NpSdStatus s){return s!=null&&(s.Flags&65536)!=0;}
  internal static string StartStorageError(NpSdStatus s){
   string full=IsFull(s)?"No unreserved SD space remains. An incomplete recording may still reserve unused space; recorded size can be smaller than its allocation. ":"";
   if(NeedsRecovery(s))return full+(CanRecover(s)?
    "The SD catalog needs recovery. Use Recover interrupted catalog; recovery preserves recorded data but may retain its full allocation.":
    "The SD catalog needs recovery, which this device state or firmware cannot currently perform. Download existing recordings before further storage maintenance.");
   if(IsFull(s))return full+"Existing recordings are preserved. Recover does not release a finalized incomplete allocation on this firmware. Download data before deleting recordings or formatting.";
   return "The SD catalog is not ready for another recording. Read status and save the BLE report before further storage maintenance.";
  }
  internal static bool CanQuarantine(NpSdStatus s){return NeedsRecovery(s)&&(s.Flags&524288)!=0;}
  internal static bool CanRecover(NpSdStatus s){return CanPrepareStorage(s)&&!RecoveryRequiresUpgrade(s)&&(s.Flags&32768)!=0&&(s.Flags&(262144|524288))!=0;}
  internal static string RecoveryAction(NpSdStatus s){return CanQuarantine(s)?"Preserve interrupted allocation (keeps all data)":"Recover saved recording end (keeps data)";}
  internal static NpSdStatus Parse(byte[] b,uint op,uint id){
   if(b==null || b.Length!=64 || NpSdGeometry.U(b,0)!=ReplyMagic || NpSdGeometry.U(b,4)!=2 ||
      NpSdGeometry.U(b,8)!=op || NpSdGeometry.U(b,12)!=id || NpSdGeometry.U(b,60)!=NpSdControl.Crc(b,60))
    throw new IOException("No matching V2 NP2 SD acknowledgement");
   var s=new NpSdStatus{Operation=op,RequestId=id,Error=NpSdGeometry.U(b,16),State=NpSdGeometry.U(b,20),Fault=NpSdGeometry.U(b,24),
    Mask=NpSdGeometry.U(b,28),Hz=NpSdGeometry.U(b,32),Chunks=NpSdGeometry.U(b,36),Start=NpSdGeometry.U(b,40),End=NpSdGeometry.U(b,44),
    Capacity=NpSdGeometry.U(b,48),Flags=NpSdGeometry.U(b,52),LastId=NpSdGeometry.U(b,56)};
   if(!ValidStatus(s) || (s.Flags&128)==0)
    throw new IOException("Unsupported V2 capability/status response");
   return s;
  }
  internal static string Describe(NpSdStatus s){
   if(!ValidStatus(s))throw new IOException("Unsupported V2 capability/status response");
   string[] names={"SD not mounted","Idle","Preparing probes","Recording","Draining / verifying SD","Complete","Armed for UTC start","Cancelled before capture"};
   string state=s.State==255?(StopAlreadySatisfied(s)?"Stopped after recording failure":"Failed — finalizing"):names[s.State];
   return "WILD-X V2 — "+state+"\r\n"+ProbePrefix(s.Mask)+", "+s.Hz.ToString("N0")+" samples/s"+
    (s.Fault==0?"":"\r\nRecording error: fault "+s.Fault+". Saved data may be incomplete.")+
    (s.Error==0?"":"\r\nDevice command failed: error "+s.Error)+
    ((s.Operation==2 && s.Error==8) || StopAlreadySatisfied(s) && (IsFull(s)||NeedsRecovery(s))?"\r\n"+StartStorageError(s):"")+
    (HasOverwriteOffer(s)?"\r\nRecording boundary unknown — Overwrite from last known position… requires confirmation.":
     (s.Flags&CatalogDegraded)!=0?"\r\nSD catalog degraded — some recordings may be incomplete.":"")+
    ((s.Flags&8)!=0?"\r\nCommand queued — not yet confirmed running/stopped.":"")+
    ((s.Flags&64)!=0?"\r\nThis firmware requires a restart before another session.":"");
  }
  internal static bool CanStart(NpSdStatus s){
   return s!=null && !s.Busy && s.Fault==0 && SupportsCurrentLiveProfile(s) && !IsFull(s) && !NeedsRecovery(s) && !HasOverwriteOffer(s) &&
    (s.State==1 || ((s.Flags&512)!=0 && (s.State==5 || s.State==7)));
  }
  internal static bool StopAlreadySatisfied(NpSdStatus s){
   return s!=null&&!s.Busy&&
    (s.State==0||s.State==1||s.State==5||s.State==7||s.State==255);
  }
  internal static bool StopIsDraining(NpSdStatus s){return s!=null&&s.State==4;}
  internal static bool CanSwitchMode(NpSdStatus s){
   return s!=null && !s.Busy && s.Fault==0 && (s.Flags&8192)!=0 &&
    (s.State==0 || s.State==1 || s.State==5 || s.State==7);
  }
  // Host-only immutable snapshot; not a fabricated CE64 voltage/ADC packet.
  internal static byte[] Telemetry(NpSdStatus s){
   if(!ValidStatus(s))throw new IOException("Unsupported NP2 telemetry");
   var b=new byte[28];uint[] words={2,s.State,s.Hz,s.Chunks,s.Flags,s.Fault,s.Mask};
   for(int i=0;i<words.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,b,i*4,4);
   return b;
  }
  internal static NpSdStatus ReadTelemetry(byte[] b){
   bool legacy=b!=null && b.Length==24 && BitConverter.ToUInt32(b,0)==1;
   if(!legacy && (b==null||b.Length!=28||BitConverter.ToUInt32(b,0)!=2))throw new IOException("Invalid NP2 telemetry");
   var s=new NpSdStatus{State=BitConverter.ToUInt32(b,4),Hz=BitConverter.ToUInt32(b,8),
    Chunks=BitConverter.ToUInt32(b,12),Flags=BitConverter.ToUInt32(b,16),Fault=BitConverter.ToUInt32(b,20),
    Mask=legacy?3U:BitConverter.ToUInt32(b,24)};
   if(!ValidStatus(s))throw new IOException("Unsupported NP2 telemetry");
   return s;
  }
  internal static uint RecordedSeconds(NpSdStatus s){
   if(!ValidStatus(s))throw new IOException("Unsupported NP2 recording profile");
   return (uint)((ulong)s.Chunks*Ticks(s.Mask,s.Hz)/((ulong)s.Hz*1600));
  }
  internal static double RecordedMiB(NpSdStatus s){return s.Chunks*(46592.0/1048576);}
  internal static string OnlineState(NpSdStatus s){
   if(s==null)return "Reading device status...";
   if((s.Flags&8)!=0)return "Command sent — waiting for device confirmation...";
   if(HasOverwriteOffer(s))return "SD recording boundary unknown — Overwrite from last known position… requires confirmation";
   if(s.State==1||s.State==5||s.State==7){
    if(NeedsRecovery(s))return RecoveryRequiresUpgrade(s)?"SD mounted — firmware does not advertise stored 7.5-kHz catalog recovery; upgrade before recovery; raw download remains available":CanQuarantine(s)?"SD mounted — preserve interrupted allocation before Start; valid sample count unknown":(s.Flags&262144)!=0?"SD mounted — recover saved recording end before Start":"SD mounted — interrupted recording needs PC-side recovery; data preserved";
    if(IsFull(s))return "SD mounted — card full; existing recordings retained";
   }
   switch(s.State){
    case 0:return s.Capacity==0?"SD unavailable; recording disabled":"SD detected; recording catalog not mounted";
    case 1:return "Ready";
    case 2:return "Starting recording — preparing probes...";
    case 3:return "Recording";
    case 4:return "Stopping recording — saving and verifying; keep power connected...";
    case 5:return "Recording complete — ready";
    case 6:return "Waiting for scheduled start...";
    case 7:return "Recording cancelled — ready";
    case 255:return "Recording error — data may be incomplete (fault "+s.Fault+")";
    default:return "SD card not ready";
   }
  }
  internal static uint Blocks(NpSdStatus s,uint hz,uint seconds){
   if(!CanStart(s) || !ValidStatus(s) || Ticks(s.Mask,hz)==0 || seconds>3600 || s.Start<8192)
    throw new IOException("Device is not ready for a V2 recording.");
   ulong payload=((ulong)s.Start+8192+16383)&~16383UL,end=(ulong)s.Capacity&~16383UL;
   ulong available=end>payload?(end-payload)/364*4:0;
   if(available<4)throw new IOException("SD card is full.");
   if(seconds==0){if((s.Flags&512)==0)throw new IOException("Firmware does not support continuous recording");return 0;}
   ulong blocks=((ulong)seconds*hz*1600/Ticks(s.Mask,hz)/4)*4;
   if(blocks<4 || blocks>available || blocks>uint.MaxValue)throw new IOException("Not enough SD space for this recording.");
   return (uint)blocks;
  }
 }
}
