using System;
using System.IO;
using System.Security.Cryptography;
namespace CE32_console {
 // Only a completed send followed by a missing receipt is eligible. Transport
 // write failures (possibly partial frames) and device rejections are not.
 internal sealed class NpV1ReceiptTimeout : TimeoutException {
  internal readonly uint Operation, Transaction;
  internal NpV1ReceiptTimeout(uint operation,uint transaction)
   :base("No matching V1 receipt: op="+operation+" id="+transaction+"; no mutation replay"){
   Operation=operation;Transaction=transaction;
  }
 }
 internal static class NpV1StatusRead {
  internal static NpSdStatus Read(Func<byte[],NpSdStatus> exchange,Func<uint> next,Action<string> report){
   uint first=next();
   try{return exchange(NpSdControl.Request(0,first));}
   catch(NpV1ReceiptTimeout e){
    if(e.Operation!=0||e.Transaction!=first)throw;
    uint second=next();
    if(second==0||second==first)throw new InvalidOperationException("Fresh STATUS transaction required");
    report("STATUS receipt timeout id="+first+"; one read-only retry id="+second+" (no mutation replay)");
    // Deliberately outside a retry loop: a second timeout is terminal.
    return exchange(NpSdControl.Request(0,second));
   }
  }
 }
 internal static class NpV1BleProtocol {
  internal const uint Schedule=1024, Preview=128, Clock=256, Modes=8192;
  // NLD1 destination values differ from the resident boot target numbers.
  internal const uint DestinationLoader=1,DestinationUsbStreaming=2,DestinationDfu=3,DestinationUsbDownload=4;
  internal const uint Repeat=512, Storage=16384, Waiting=131072, Preparing=262144;
  internal const uint Scaled20=524288, RateProfiles=1048576;
  internal const uint MediaCapability=0x200000, MediaSelected=0x400000;
  internal const uint CatalogReadOnly=0x01000000;
  // Versioned V1 filesystem, not the V2 logger board or payload format.
  internal const uint StoreV2=0x02000000,StoreV2Selected=0x04000000;
  internal static bool HasReadOnlyCatalog(NpSdStatus s){return s!=null&&(s.Flags&CatalogReadOnly)!=0&&(s.Flags&16)==0&&s.Start==0&&s.Capacity>8192;}
  internal static bool IsMedia(NpSdStatus s){return s!=null&&s.Mask==0&&s.Hz==0&&(s.Flags&(MediaCapability|MediaSelected))==(MediaCapability|MediaSelected);}
  internal static bool TheoreticalMode(uint mask,uint hz){
   if(mask<1||mask>7||(hz!=7500&&hz!=15000&&hz!=20000&&hz!=30000))return false;
   int count=NpSdGeometry.Count(mask);
   if(count==3&&hz>15000||count==2&&hz>=20000&&mask!=3)return false;
   return (ulong)count*hz*832<=60000000;
  }
  internal static bool CanSelect(NpSdStatus s,uint mask,uint hz){
   if(mask==0&&hz==0)return s!=null&&(s.Flags&MediaCapability)!=0;
   if(!TheoreticalMode(mask,hz))return false;
   if(hz!=20000)return true;
   return s!=null&&((s.Flags&RateProfiles)!=0||(s.Flags&Scaled20)!=0&&mask==3);
  }
  internal static bool CanConfigure(NpSdStatus s){return s!=null&&!s.Busy&&(s.Flags&8)==0&&s.Fault==0&&(s.State==0||s.State==3);}
  // Finalized FAILED recordings can still own a stopped, writable card. The
  // firmware's active/pending flags fence storage; a stale recorder fault does
  // not describe the current card operation or require a mounted catalog.
  internal static bool CanManageStorage(NpSdStatus s){
   return s!=null&&(s.State==0||s.State==3||s.State==4)&&!s.Busy&&(s.Flags&(Waiting|Preparing))==0;
  }
  internal static bool StopAlreadySatisfied(NpSdStatus s){return CanManageStorage(s);}
  internal static bool CanFormat(NpSdStatus s,uint expectedCapacity){
   return CanManageStorage(s)&&expectedCapacity>8192&&s.Capacity==expectedCapacity&&(s.Flags&1)!=0;
  }
  internal static bool CanStart(NpSdStatus s){return CanConfigure(s)&&(s.Flags&CatalogReadOnly)==0&&CanSelect(s,s.Mask,s.Hz)&&(s.Mask!=0||IsMedia(s))&&(s.Flags&17)==17&&s.Start>=8192&&s.Capacity>s.Start;}
  // Manual SELECT/START reinitialize a finalized capture in existing firmware.
  // Scheduled START, MODE and RECOVER retain their fault-zero contracts.
  internal static bool CanSelectProfile(NpSdStatus s,uint mask,uint hz){return CanManageStorage(s)&&(s.Flags&1)!=0&&CanSelect(s,mask,hz);}
  internal static bool CanManualStart(NpSdStatus s){return CanManageStorage(s)&&(s.Flags&CatalogReadOnly)==0&&CanSelect(s,s.Mask,s.Hz)&&(s.Mask!=0||IsMedia(s))&&(s.Flags&17)==17&&s.Start>=8192&&s.Capacity>s.Start;}
  internal static bool CanMount(NpSdStatus s){return CanManageStorage(s)&&(s.Flags&Storage)!=0&&(s.Capacity==0||s.Capacity>8192);}
  // Explicit MOUNT may recover an approved card after failed initialization.
  // Firmware owns CID admission; only fresh, correlated mounted STATUS can
  // establish a previously unavailable capacity for this dialog.
  internal static uint RequireMountStatusCompleted(NpSdStatus s,uint expectedCapacity,uint mutationId){
   if(s==null||s.Operation!=0||s.RequestId==0||s.Capacity<=8192||!CanMount(s)||
      (expectedCapacity!=0&&s.Capacity!=expectedCapacity))
    throw new IOException("V1 mount capacity was not confirmed by fresh status. Read status; no command replayed.");
   RequireStorageLayoutCompleted(s,s.Capacity,mutationId,false,0);return s.Capacity;
  }
  internal static void RequireStorageCompleted(NpSdStatus s,uint capacity,uint mutationId,bool format){
   if(mutationId==0||!CanManageStorage(s)||s.Capacity!=capacity||s.LastId!=mutationId||s.Error!=0||(s.Flags&16)==0||
      (format&&s.Start!=8192))throw new IOException("V1 storage operation not confirmed by a stopped, mounted card. Read status; no command replayed.");
  }
  internal static void RequireStorageLayoutCompleted(NpSdStatus s,uint capacity,uint mutationId,bool format,uint requestedLayout){
   RequireStorageCompleted(s,capacity,mutationId,format);
   if((format?(requestedLayout!=1&&requestedLayout!=2):requestedLayout!=0)||
      requestedLayout==2&&(s.Flags&(StoreV2|StoreV2Selected))!=(StoreV2|StoreV2Selected))
    throw new IOException("Requested V1 filesystem layout was not confirmed. Read status; no command replayed.");
  }
  internal static string StateText(NpSdStatus s){
   if(s==null)return "Reading V1 status...";
   if(CanManageStorage(s)){
    string storage=HasReadOnlyCatalog(s)?"SD catalog read-only — readable entries available; recording and deletion disabled.":
     (s.Flags&16)!=0&&s.Start>=s.Capacity&&s.Capacity>8192?"SD card full — saved recordings available; recording disabled.":null;
    if(storage!=null)return storage+(s.Fault==0?"":" Last recording error: "+FaultText(s)+" (fault "+s.Fault+"); saved data may be incomplete.");
   }
   if(s.Fault!=0||s.State==4)return (StopAlreadySatisfied(s)?"Stopped after recording error — ":"Recording error — finalizing — ")+FaultText(s)+" (state "+s.State+", fault "+s.Fault+"). Saved data may be incomplete.";
   if((s.Flags&Waiting)!=0)return "Waiting for scheduled start...";
   if((s.Flags&Preparing)!=0)return "Preparing probes / storage — capture has not started";
   if((s.Flags&8)!=0)return "Command queued — awaiting device confirmation...";
   if(s.State==1)return "Recording";
   if(s.State==2)return "Stopping — draining SD; keep power connected";
   if((s.Flags&16)==0)return s.Capacity==0?"SD unavailable":"SD catalog not mounted";
   return s.State==3?"Recording complete — ready":"Ready";
  }
  internal static string FaultText(NpSdStatus s){
   switch(s.Fault){
    case 1:return "Invalid recording configuration";
    case 2:return s.Mask==0?"Camera/microphone capture source failed":"Probe capture source failed — probe startup or acquisition stream";
    case 3:return "Data staging failed";
    case 4:return "SD write failed";
    case 5:return "Recording transfer timed out";
    case 6:return "Recording storage range failed";
    case 7:return "Recording protocol/integrity failure";
    default:return "Device reported recording failure";
   }
  }
  internal static uint EndLba(NpSdStatus s,uint seconds,uint cameraHz=50,bool microphone=true){
   if(!CanManualStart(s))throw new IOException("V1 storage/recorder is not ready");
   if(seconds==0&&(s.Flags&Repeat)==0)throw new IOException("Firmware does not advertise continuous recording");
   ulong payload=(ulong)s.Start+8192+((0U-(s.Start+8192U))&16383U);
   if(payload>=s.Capacity)throw new IOException("SD card is full");
   // The V1 storage admission path reserves whole 8 MiB allocation units,
   // although the rolling writer transfers smaller CMD25 transactions.
   // Fixed media: three typed chunks/frame, seven audio chunks/second and
   // an eight-chunk tail. This reserves storage; it is not a precise timer.
   if(IsMedia(s)&&(cameraHz>50||cameraHz==0&&!microphone))throw new IOException("Select a supported media stream.");
   ulong bytes=IsMedia(s)?(ulong)seconds*(cameraHz*3U+(microphone?7U:0U))*65536U+8U*65536U:(ulong)seconds*s.Hz*832U*(uint)NpSdGeometry.Count(s.Mask);
   ulong end=seconds==0?((ulong)s.Capacity&~16383UL):payload+((bytes+8388607)/8388608)*16384;
   if(end<=payload||end>s.Capacity)throw new IOException("No complete allocation-unit extent remains");
   return (uint)end;
  }
  internal static byte[] Mount(NpSdStatus s,uint id){
   if(!CanMount(s))throw new IOException("Stopped V1 with storage controls required");
   var b=NpSdControl.Request(0,id);Put(b,8,8);Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  internal static byte[] Format(NpSdStatus s,uint expectedCapacity){
   if(!CanFormat(s,expectedCapacity))throw new IOException("Stopped, writable V1 with the confirmed card capacity required.");
   if((s.Flags&StoreV2)==0)return NpSdControl.Request(5,NpSdControl.NextId(s),flags:0x46524d54U);
   var nonce=new byte[8];ulong epoch;
   using(var random=RandomNumberGenerator.Create()){do{random.GetBytes(nonce);epoch=BitConverter.ToUInt64(nonce,0);}while(epoch==0);}
   return FormatWithEpoch(s,expectedCapacity,epoch);
  }
  internal static byte[] FormatWithEpoch(NpSdStatus s,uint expectedCapacity,ulong epoch){
   if(!CanFormat(s,expectedCapacity)||(s.Flags&StoreV2)==0)throw new IOException("Stopped, writable V1 with filesystem2 support and the confirmed card capacity required.");
   if(epoch==0)throw new ArgumentOutOfRangeException("epoch","A fresh nonzero 64-bit format epoch is required.");
   var b=NpSdControl.Request(5,NpSdControl.NextId(s),flags:0x46524d54U);
   Put(b,4,2);Put(b,16,2);Put(b,44,(uint)epoch);Put(b,48,(uint)(epoch>>32));Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  internal static bool IsStoreV2FormatRequest(byte[] b){
   return b!=null&&b.Length==64&&NpSdGeometry.U(b,0)==0x31444c4eU&&NpSdGeometry.U(b,4)==2&&
    NpSdGeometry.U(b,8)==5&&NpSdGeometry.U(b,12)!=0&&NpSdGeometry.U(b,16)==2&&NpSdGeometry.U(b,36)==0x46524d54U&&
    (NpSdGeometry.U(b,20)|NpSdGeometry.U(b,24)|NpSdGeometry.U(b,28)|NpSdGeometry.U(b,32)|NpSdGeometry.U(b,40)|NpSdGeometry.U(b,52)|NpSdGeometry.U(b,56))==0&&
    (NpSdGeometry.U(b,44)|NpSdGeometry.U(b,48))!=0&&NpSdGeometry.U(b,60)==NpSdControl.Crc(b,60);
  }
  internal static byte[] Recover(NpSdStatus s,uint id){
   if(!CanConfigure(s)||s.Capacity<=8192)throw new IOException("Stopped, fault-free V1 required for catalog recovery");
   var b=Mount(s,id);Put(b,8,9);Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  internal static uint RecordedSeconds(NpSdStatus s){return s==null||s.Hz==0||NpSdGeometry.Count(s.Mask)==0?0:(uint)((ulong)s.Chunks*61440/((ulong)s.Hz*780*(uint)NpSdGeometry.Count(s.Mask)));}
  internal static double RecordedMiB(NpSdStatus s){return s==null?0:s.Chunks/16.0;}
  // V1-specific host telemetry. Never reuse the NP2 state/geometry decoder.
  internal static byte[] Telemetry(NpSdStatus s){
   var b=new byte[32];uint[] w={0x31544c4e,s.State,s.Hz,s.Chunks,s.Flags,s.Fault,s.Mask,s.Capacity};
   for(int i=0;i<w.Length;i++)Put(b,4*i,w[i]);return b;
  }
  internal static NpSdStatus ReadTelemetry(byte[] b){
   if(b==null||b.Length!=32||NpSdGeometry.U(b,0)!=0x31544c4e)throw new IOException("Invalid V1 telemetry");
   var s=new NpSdStatus{State=NpSdGeometry.U(b,4),Hz=NpSdGeometry.U(b,8),Chunks=NpSdGeometry.U(b,12),Flags=NpSdGeometry.U(b,16),Fault=NpSdGeometry.U(b,20),Mask=NpSdGeometry.U(b,24),Capacity=NpSdGeometry.U(b,28)};
   if(s.State>4||!CanSelect(s,s.Mask,s.Hz)||(s.Mask==0&&!IsMedia(s)))throw new IOException("Invalid V1 telemetry geometry");return s;
  }
  static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
  internal static byte[] ScheduledStart(NpSdStatus s,uint id,uint end,uint session,ulong utc,bool acceptUnqualified){
   if(!CanStart(s)||(s.Flags&Schedule)==0||utc==0)throw new IOException("V1 is not ready for scheduled recording");
   byte[] b=NpSdControl.Request(2,id,s.Mask,s.Hz,s.Start,end,session,acceptUnqualified?1U:0U);
   Put(b,8,6);Put(b,44,(uint)utc);Put(b,48,(uint)(utc>>32));Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  internal static byte[] Mode(NpSdStatus s,uint id,uint destination){
   if(s!=null && s.Fault!=0 && CanManageStorage(s))
    throw new IOException("V1 recording is already stopped with fault "+s.Fault+" ("+FaultText(s)+"). Firmware blocks mode switching while this fault is latched. Open Report for probe receipts; no mode command was sent.");
   if(!CanConfigure(s)||(s.Flags&Modes)==0||destination<1||destination>4)
    throw new IOException("Stop V1 and wait for completion before mode switching");
   byte[] b=NpSdControl.Request(0,id);Put(b,8,7);Put(b,16,destination);Put(b,60,NpSdControl.Crc(b,60));return b;
  }
  // Explicit diagnostic recovery only. Never reset a healthy/live recorder or
  // replay a mutation when its destination arrival has not been confirmed.
  internal static byte[] FailedRescue(NpSdStatus s,uint expectedFault){
   if(s==null||s.Busy||s.State!=4||expectedFault==0||s.Fault!=expectedFault||(s.Flags&4096)==0)
    throw new IOException("Expected quiescent failed V1 with advertised rescue support; no reset sent");
   return new byte[]{0x3c,0xab,0xba,0x3e};
  }
  internal static double LfpHz(NpSdStatus s){return s.Hz/12.0;}
 }
 internal sealed class NpV1CameraStatus {
  internal bool Auto,Initialized,Pending;
  internal ushort SeedReg0,Reg1,EffectiveReg0,EffectiveReg1;
  internal uint Mean,Frame,Tick,Updates,Fault,Observed;
  internal static byte[] Request(){var b=new byte[64];System.Text.Encoding.ASCII.GetBytes("CAMEX1").CopyTo(b,0);BitConverter.GetBytes(NpSdControl.Crc(b,60)).CopyTo(b,60);return b;}
  internal static bool IsFrame(byte[] b){return b!=null&&b.Length==64&&System.Text.Encoding.ASCII.GetString(b,0,6)=="CAMEX1";}
  internal static NpV1CameraStatus Parse(byte[] b){
   if(!IsFrame(b)||b[6]!=0||b[7]!=0||NpSdGeometry.U(b,8)!=1||NpSdGeometry.U(b,60)!=NpSdControl.Crc(b,60)||
      NpSdGeometry.U(b,12)>1||NpSdGeometry.U(b,16)>65535||NpSdGeometry.U(b,20)!=0x425||NpSdGeometry.U(b,24)>65535||NpSdGeometry.U(b,28)>65535||NpSdGeometry.U(b,32)>1023||(NpSdGeometry.U(b,52)&~3U)!=0)
    throw new IOException("Invalid V1 camera exposure state; settings not confirmed");
   uint flags=NpSdGeometry.U(b,52);
   return new NpV1CameraStatus{Auto=NpSdGeometry.U(b,12)!=0,SeedReg0=(ushort)NpSdGeometry.U(b,16),Reg1=(ushort)NpSdGeometry.U(b,20),EffectiveReg0=(ushort)NpSdGeometry.U(b,24),EffectiveReg1=(ushort)NpSdGeometry.U(b,28),Mean=NpSdGeometry.U(b,32),Frame=NpSdGeometry.U(b,36),Tick=NpSdGeometry.U(b,40),Updates=NpSdGeometry.U(b,44),Fault=NpSdGeometry.U(b,48),Initialized=(flags&1)!=0,Pending=(flags&2)!=0,Observed=NpSdGeometry.U(b,56)};
  }
  internal byte[] Settings(ushort reg0,ushort reg1,bool automatic,byte profile){
   if((reg0&0xcf)!=(SeedReg0&0xcf)||reg1!=Reg1||profile>1)throw new ArgumentException("Preserve V1 fixed register bits and Reg1; only reset rows/gain/AE may change");
   return automatic?new byte[]{0x3c,0x18,(byte)(reg0>>8),(byte)((reg0>>4)&3),profile,1,0x3e}:new byte[]{0x3c,0x15,(byte)reg0,(byte)(reg0>>8),(byte)reg1,(byte)(reg1>>8),0x3e};
  }
  internal bool Staged(ushort reg0,ushort reg1,bool automatic){return Fault==0&&Auto==automatic&&SeedReg0==reg0&&Reg1==reg1;}
  internal bool Confirms(ushort reg0,bool automatic){return Staged(reg0,Reg1,automatic)&&Initialized&&!Pending&&EffectiveReg1==Reg1&&(automatic||EffectiveReg0==reg0);}
 }
 internal sealed class NpV1MediaStatus {
  internal uint CameraHz,MicrophoneHz,Frames,Missed,CameraFault,MicrophoneFault,Overruns,StartMs,State;
  internal ulong Samples;
  internal static byte[] Request(){var b=new byte[64];Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("MEDIA1"),0,b,0,6);Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;}
  internal static bool IsFrame(byte[] b){return b!=null&&b.Length==64&&System.Text.Encoding.ASCII.GetString(b,0,6)=="MEDIA1";}
  internal static NpV1MediaStatus Parse(byte[] b){
   if(!IsFrame(b)||b[6]!=0||b[7]!=0||NpSdGeometry.U(b,8)!=1||NpSdGeometry.U(b,60)!=NpSdControl.Crc(b,60))throw new IOException("Invalid V1 MEDIA1 telemetry");
   var s=new NpV1MediaStatus{CameraHz=NpSdGeometry.U(b,12),MicrophoneHz=NpSdGeometry.U(b,16),Frames=NpSdGeometry.U(b,20),Missed=NpSdGeometry.U(b,24),CameraFault=NpSdGeometry.U(b,28),MicrophoneFault=NpSdGeometry.U(b,32),Samples=NpSdGeometry.U(b,36)|((ulong)NpSdGeometry.U(b,40)<<32),Overruns=NpSdGeometry.U(b,44),StartMs=NpSdGeometry.U(b,48),State=NpSdGeometry.U(b,56)};
   if(s.CameraHz>50||(s.MicrophoneHz!=0&&s.MicrophoneHz!=200000)||s.State>4)throw new IOException("Unsupported V1 media telemetry rates/state");return s;
  }
  internal static byte[] ProfileCommand(bool camera=true,bool microphone=true,uint cameraHz=40){
   if(!camera&&!microphone)throw new ArgumentException("Select camera or microphone");
   if(camera&&(cameraHz<1||cameraHz>50))throw new ArgumentOutOfRangeException("cameraHz","Select 1 to 50 fps");
   var b=new byte[35];b[0]=0x3c;b[1]=0x33;b[34]=0x3e;Buffer.BlockCopy(BitConverter.GetBytes(camera?cameraHz:0U),0,b,10,4);Buffer.BlockCopy(BitConverter.GetBytes(microphone?200000U:0U),0,b,18,4);return b;
  }
  internal static bool FixedRates(byte[] b,int rateOffset,int nchOffset){
   bool camera,microphone;return ReadRates(b,rateOffset,nchOffset,out camera,out microphone)&&camera&&microphone;
  }
  internal static bool ReadRates(byte[] b,int rateOffset,int nchOffset,out bool camera,out bool microphone){
   camera=microphone=false;
   if(b==null||b.Length!=512||rateOffset<0||rateOffset>b.Length-32||nchOffset<0||nchOffset>b.Length-16)return false;
   uint cam=NpSdGeometry.U(b,rateOffset+8),mic=NpSdGeometry.U(b,rateOffset+16);
   if(cam>50||(mic!=0&&mic!=200000)||(cam==0&&mic==0))return false;
   for(int i=0;i<8;i++)if(i!=2&&i!=4&&NpSdGeometry.U(b,rateOffset+i*4)!=0)return false;
   if(NpSdGeometry.U(b,0)!=0||BitConverter.ToUInt16(b,nchOffset)!=0||BitConverter.ToUInt16(b,nchOffset+2)!=0||BitConverter.ToUInt16(b,nchOffset+4)!=(cam==0?0:320)||BitConverter.ToUInt16(b,nchOffset+8)!=(mic==0?0:1))return false;
   camera=cam!=0;microphone=mic!=0;return true;
  }
 }
 // Host-only diagnostic bounds. Firmware admission and card capacity remain
 // authoritative; extending a test never promotes an unqualified mode.
 internal static class NpV1LifecycleBounds {
  internal static uint ReservationSeconds(int milliseconds){
   if(milliseconds<1000||milliseconds>600000)throw new ArgumentOutOfRangeException("milliseconds");
   // Preserve short preview A/B tests. Endurance timing begins only after a
   // successful STATUS reply; allow a delayed reply plus one read-only retry
   // and STOP/drain latency before the bounded allocation ends naturally.
   return milliseconds<=8000?10U:((uint)milliseconds+999U)/1000U+12U;
  }
 }
 internal sealed class NpV1PreviewBurst {
  internal const uint Magic=0x3156504e;
  internal uint Id,Session,Hz,Kind,Probe,Channel,Band,ValidMask,Stride,Dropped;
  internal ulong FirstTick;
  internal byte[] Data;
  int received;
  internal NpV1PreviewBurst(uint id){Id=id;}
  internal static byte[] Request(uint id,uint kind,uint probe,uint channel,uint phase,uint session,uint band){
   if(id==0||(kind!=0&&kind!=2&&kind!=3&&kind!=4)||probe<1||probe>3||channel>=384||phase>=9360||band>1)
    throw new ArgumentException("Invalid V1 preview selection");
   byte[] b=new byte[64];uint[] w={Magic,1,kind,id,probe,channel,phase,session,band};
   for(int i=0;i<w.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(w[i]),0,b,4*i,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal bool Accept(byte[] b){
   if(b==null||b.Length!=224||NpSdGeometry.U(b,0)!=Magic||NpSdGeometry.U(b,4)!=Id)return false;
   uint session=NpSdGeometry.U(b,8),hz=NpSdGeometry.U(b,12),range=NpSdGeometry.U(b,24),select=NpSdGeometry.U(b,28);
   uint probe=select&255,kind=(select>>8)&15,band=(select>>12)&15,channel=select>>16;
   ulong first=NpSdGeometry.U(b,16)|((ulong)NpSdGeometry.U(b,20)<<32);
   int at=(int)(range&65535),total=(int)(range>>16);
   uint stride=NpSdGeometry.U(b,192),dropped=NpSdGeometry.U(b,196);
   if(session==0||probe<1||probe>3||!NpV1BleProtocol.TheoreticalMode(1U<<(int)(probe-1),hz)||channel>=384||band>1||
      (kind!=0&&kind!=2)||total!=(kind==0?1560:160)||
      stride!=(band==0?((hz+499)/500)*780:((hz+5999)/6000)*9360))throw new IOException("Invalid V1 preview metadata");
   if(kind==2){ValidMask=(uint)at;if(ValidMask==0)throw new IOException("Empty preview");at=0;}
   if(Data==null){if(at!=0)throw new IOException("Missing preview beginning");Data=new byte[total];Session=session;Hz=hz;
    Probe=probe;Kind=kind;Band=band;Channel=channel;FirstTick=first;Stride=stride;Dropped=dropped;}
   if(at!=received||session!=Session||hz!=Hz||probe!=Probe||kind!=Kind||band!=Band||channel!=Channel||
      first!=FirstTick||stride!=Stride||dropped!=Dropped||total!=Data.Length)throw new IOException("Mixed/incomplete preview");
   int n=Math.Min(160,total-at);if(n<=0)throw new IOException("Repeated preview fragment");
   Buffer.BlockCopy(b,32,Data,at,n);received+=n;return received==Data.Length;
  }
  int Word(int at,int line,int word){int v=0;for(int i=0;i<10;i++)v=(v<<1)|((Data[at+10*word+i]>>line)&1);return v;}
  internal int FindPhase(){
   if(Kind!=0||Data==null||received!=1560||FirstTick!=0)throw new IOException("Session-origin snapshot required for LFP phase");
   for(int at=0;at<780;at++){
    int fixedWord=Word(at,7,2);if(fixedWord!=0x11&&fixedWord!=0x22)continue;
    uint previous=0;bool good=true;
    for(int f=0;f<13&&good;f++){
     int p=at+f*60;
     if(Word(p,0,0)!=(f==0?0x330:0xcf)||Word(p,7,2)!=fixedWord){good=false;break;}
     int changes=0,last=Data[p+10]&1;
     for(int t=11;t<60;t++){int value=Data[p+t]&1;if(value!=last){last=value;changes++;}}
     if(changes>1)good=false;
     for(int t=50;t<60;t++)if((Data[p+t]&0xfe)!=0)good=false;
     uint counter=(uint)(Word(p,7,3)<<10|Word(p,7,4));
     if(f>0&&counter!=((previous+1)&0xfffff))good=false;previous=counter;
    }
    if(good)return at;
   }
   throw new IOException("No validated NP1 session-origin superframe; do not guess LFP phase");
  }
  internal double[] DecodeRolling(uint phase){
   if(Kind!=2||Data==null||received!=160||phase>=9360)throw new IOException("Complete V1 rolling packet required");
   uint adc=(Channel/24)*2+(Channel&1),sample=(Channel%24)/2,word=adc<30?adc%5:adc-30;
   uint period=Band==0?780U:9360U,offset=phase+(Band==0?(sample+1)*60:sample*780)+word*10;
   if(FirstTick%period!=offset%period)throw new IOException("V1 preview phase/origin mismatch");
   uint line=adc<30?adc/5+1:7;var values=new double[16];
   for(int i=0;i<16;i++){
    if((ValidMask&(1U<<i))==0){values[i]=double.NaN;continue;}
    uint value=0;for(int bit=0;bit<10;bit++)value=(value<<1)|((uint)(Data[10*i+bit]>>(int)line)&1);
    values[i]=value; // unsigned 10-bit ADC code; no hidden offset/gain or wrap
   }
   return values;
  }
 }
}
