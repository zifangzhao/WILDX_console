using System;
using System.IO;
namespace CE32_console {
 internal static class NpV2SdControl {
  internal const uint RequestMagic=0x32444c4e,ReplyMagic=0x32524c4e;
  internal static uint NextId(NpSdStatus s){
   if(s.LastId!=0)return NpSdControl.NextId(s);
   uint id=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0);return id==0?1:id;
  }
  internal static byte[] Request(uint op,uint id,uint hz=0,uint blocks=0,ulong utc=0){
   if(id==0 || op>8 || (op!=4 && utc!=0))throw new ArgumentException("Invalid V2 command/id");
   var b=new byte[64];uint[] w={RequestMagic,2,op,id,(op==1||op==2||op==4)?3U:0U,hz,blocks,(uint)utc,(uint)(utc>>32)};
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
  internal static bool CanConfigure(NpSdStatus s){
   return s!=null&&!s.Busy &&
    (s.State==1||s.State==5||s.State==7||(s.State==0&&(s.Flags&16384)!=0));
  }
  internal static bool CanPrepareStorage(NpSdStatus s){
   // A previous mount/format command error must not prevent an explicit retry.
   return s!=null&&!s.Busy&&(s.Flags&16384)!=0&&s.Capacity!=0&&(s.State==0||s.State==1);
  }
  internal static bool NeedsRecovery(NpSdStatus s){return s!=null&&(s.Flags&131072)!=0;}
  internal static bool IsFull(NpSdStatus s){return s!=null&&(s.Flags&65536)!=0;}
  internal static bool CanQuarantine(NpSdStatus s){return NeedsRecovery(s)&&(s.Flags&524288)!=0;}
  internal static bool CanRecover(NpSdStatus s){return CanPrepareStorage(s)&&NeedsRecovery(s)&&(s.Flags&32768)!=0&&(s.Flags&(262144|524288))!=0;}
  internal static string RecoveryAction(NpSdStatus s){return CanQuarantine(s)?"Preserve interrupted allocation (keeps all data)":"Recover saved recording end (keeps data)";}
  internal static NpSdStatus Parse(byte[] b,uint op,uint id){
   if(b==null || b.Length!=64 || NpSdGeometry.U(b,0)!=ReplyMagic || NpSdGeometry.U(b,4)!=2 ||
      NpSdGeometry.U(b,8)!=op || NpSdGeometry.U(b,12)!=id || NpSdGeometry.U(b,60)!=NpSdControl.Crc(b,60))
    throw new IOException("No matching V2 NP2 SD acknowledgement");
   var s=new NpSdStatus{Operation=op,RequestId=id,Error=NpSdGeometry.U(b,16),State=NpSdGeometry.U(b,20),Fault=NpSdGeometry.U(b,24),
    Mask=NpSdGeometry.U(b,28),Hz=NpSdGeometry.U(b,32),Chunks=NpSdGeometry.U(b,36),Start=NpSdGeometry.U(b,40),End=NpSdGeometry.U(b,44),
    Capacity=NpSdGeometry.U(b,48),Flags=NpSdGeometry.U(b,52),LastId=NpSdGeometry.U(b,56)};
   if(s.Mask!=3 || (s.Hz!=20000 && s.Hz!=30000) || (s.Flags&128)==0 || (s.State>7 && s.State!=255))
    throw new IOException("Unsupported V2 capability/status response");
   return s;
  }
  internal static string Describe(NpSdStatus s){
   string[] names={"SD not mounted","Idle","Preparing probes","Recording","Draining / verifying SD","Complete","Armed for UTC start","Cancelled before capture"};
   string state=s.State==255?"Failed":names[s.State];
   return "WILD-X V2 / NP2 — "+state+"\r\nDevice-selected: probes 1 + 2, "+s.Hz.ToString("N0")+" samples/s (no separate LFP stream)"+
    "\r\nCommitted: "+s.Chunks.ToString("N0")+" blocks / "+(s.Chunks*46592.0/1e6).ToString("F2")+" MB"+
    "\r\nDevice fault: "+s.Fault+"; command error: "+s.Error+
    ((s.Flags&8)!=0?"\r\nCommand queued — not yet confirmed running/stopped.":"")+
    ((s.Flags&64)!=0?"\r\nThis firmware requires a restart before another session.":"")+
    "\r\nBLE is control only. Three-probe SD catalog mode is not supported by this firmware.";
  }
  internal static bool CanStart(NpSdStatus s){
   return s!=null && !s.Busy && s.Fault==0 && !IsFull(s) && !NeedsRecovery(s) &&
    (s.State==1 || ((s.Flags&512)!=0 && (s.State==5 || s.State==7)));
  }
  internal static bool StopAlreadySatisfied(NpSdStatus s){
   return s!=null&&(s.Flags&8)==0&&
    (s.State==0||s.State==1||s.State==5||s.State==7);
  }
  internal static bool StopIsDraining(NpSdStatus s){return s!=null&&s.State==4;}
  internal static bool CanSwitchMode(NpSdStatus s){
   return s!=null && !s.Busy && s.Fault==0 && (s.Flags&8192)!=0 &&
    (s.State==0 || s.State==1 || s.State==5 || s.State==7);
  }
  // Host-only immutable snapshot; not a fabricated CE64 voltage/ADC packet.
  internal static byte[] Telemetry(NpSdStatus s){
   var b=new byte[24];uint[] words={1,s.State,s.Hz,s.Chunks,s.Flags,s.Fault};
   for(int i=0;i<words.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,b,i*4,4);
   return b;
  }
  internal static NpSdStatus ReadTelemetry(byte[] b){
   if(b==null||b.Length!=24||BitConverter.ToUInt32(b,0)!=1)throw new IOException("Invalid NP2 telemetry");
   var s=new NpSdStatus{State=BitConverter.ToUInt32(b,4),Hz=BitConverter.ToUInt32(b,8),
    Chunks=BitConverter.ToUInt32(b,12),Flags=BitConverter.ToUInt32(b,16),Fault=BitConverter.ToUInt32(b,20)};
   if((s.State>7&&s.State!=255)||(s.Hz!=20000&&s.Hz!=30000))throw new IOException("Unsupported NP2 telemetry");
   return s;
  }
  internal static uint RecordedSeconds(NpSdStatus s){return (uint)((ulong)s.Chunks*61440/((ulong)s.Hz*1600));}
  internal static double RecordedMiB(NpSdStatus s){return s.Chunks*(46592.0/1048576);}
  internal static string OnlineState(NpSdStatus s){
   if(s==null)return "Reading device status...";
   if((s.Flags&8)!=0)return "Command sent — waiting for device confirmation...";
   if(s.State==1||s.State==5||s.State==7){
    if(NeedsRecovery(s))return CanQuarantine(s)?"SD mounted — preserve interrupted allocation before Start; valid sample count unknown":(s.Flags&262144)!=0?"SD mounted — recover saved recording end before Start":"SD mounted — interrupted recording needs PC-side recovery; data preserved";
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
   if(!CanStart(s) || s.Mask!=3 || (hz!=20000 && hz!=30000) || seconds>3600 || s.Start<8192)
    throw new IOException("Device is not ready for a V2 recording.");
   ulong payload=((ulong)s.Start+8192+16383)&~16383UL,end=(ulong)s.Capacity&~16383UL;
   ulong available=end>payload?(end-payload)/364*4:0;
   if(available<4)throw new IOException("SD card is full.");
   if(seconds==0){if((s.Flags&512)==0)throw new IOException("Firmware does not support continuous recording");return 0;}
   ulong blocks=((ulong)seconds*hz*1600/61440/4)*4;
   if(blocks<4 || blocks>available || blocks>uint.MaxValue)throw new IOException("Not enough SD space for this recording.");
   return (uint)blocks;
  }
 }
}
