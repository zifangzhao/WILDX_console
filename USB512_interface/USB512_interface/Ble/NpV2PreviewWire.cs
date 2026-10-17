using System;
using System.IO;
namespace CE32_console {
 // Only expendable preview requests may be re-armed. Recording/configuration
 // writes and unknown transport failures must never use this retry policy.
 internal sealed class NpV2PreviewUnavailableException : IOException {
  internal readonly uint Reason;
  internal bool CanRetry {get{return Reason==2||Reason==3||Reason==4||Reason==5||Reason==128;}}
  internal NpV2PreviewUnavailableException(uint reason):base(reason==2?
   "Preview waiting for SD recording; preview never starts acquisition.":reason==128?
   "Preview unavailable/no data; recording is independent.":
   "Preview skipped (reason "+reason+"): recording/state/pressure takes priority."){Reason=reason;}
 }
 internal sealed class NpV2PreviewRetry {
  int failures;long next;
  internal bool CanAttempt(long now){return now>=next;}
  internal int Defer(long now,bool leaseMayBeActive=false){
   int delay=1000<<Math.Min(failures,3);if(failures<4)failures++;
   if(leaseMayBeActive)delay=Math.Max(delay,2000); // Let the device lease expire.
   next=now+delay;return delay;
  }
  internal void Reset(){failures=0;next=0;}
 }
 internal sealed class NpV2PreviewBurst {
  internal const uint Magic=0x3256504e;
  internal const uint SixBitPayload=64; // Legacy kinds0..2: six-bit PSSI; kind5: compact layout.
  internal uint Id,Session,Hz,Kind,Probe,Channel,PreviewHz,ValidMask;
  internal int Rows;
  internal ulong FirstTick,ValidRows;
  internal ulong SourceStep {get{return PreviewHz==0?0:(ulong)(Hz/PreviewHz)*1600;}}
  internal byte[] Data;
  int received;
  bool sixBitPayload;
  internal NpV2PreviewBurst(uint id){Id=id;}
  internal static byte[] Request(uint id,uint kind,uint probe,uint channel,uint phase,uint session){
   if(id==0||kind>5||probe<1||probe>2||channel>=384||phase>=1600)throw new ArgumentException();
   byte[] b=new byte[64];uint[] w={Magic,1,kind,id,probe,channel,phase,session};
   for(int i=0;i<w.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(w[i]),0,b,i*4,4);
   Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(b,60)),0,b,60,4);return b;
  }
  internal bool Accept(byte[] b){
   if(b==null||b.Length!=224||NpSdGeometry.U(b,0)!=Magic||NpSdGeometry.U(b,4)!=Id)return false;
   uint session=NpSdGeometry.U(b,8),hz=NpSdGeometry.U(b,12),range=NpSdGeometry.U(b,24),selection=NpSdGeometry.U(b,28);
   int offset=(int)(range&65535),total=(int)(range>>16);uint probe=selection&255,kind=(selection>>8)&255,channel=selection>>16;
   if((kind&128)!=0)throw new NpV2PreviewUnavailableException(kind&127);
   bool marked=(kind&SixBitPayload)!=0;kind&=63;
   ulong first=NpSdGeometry.U(b,16)|((ulong)NpSdGeometry.U(b,20)<<32);
   if((kind>2&&kind!=5)||probe<1||probe>2||channel>=384||session==0||(hz!=7500&&hz!=15000&&hz!=20000&&hz!=30000)||total!=(kind==0?3200:kind==1?3072:192))throw new IOException("Invalid preview metadata");
   if(kind==5){
    uint previewHz=NpSdGeometry.U(b,32),rows=NpSdGeometry.U(b,36);
    ulong valid=NpSdGeometry.U(b,40)|((ulong)NpSdGeometry.U(b,44)<<32);
    if(!marked||offset!=64||previewHz!=500||rows!=64||valid==0||first>ulong.MaxValue-(ulong)rows*(hz/previewHz)*1600)throw new IOException("Invalid compact preview metadata");
    for(int i=49;i<176;i+=2)if((b[i]&240)!=0)throw new IOException("Invalid compact preview raw word");
    for(int i=176;i<b.Length;i++)if(b[i]!=0)throw new IOException("Invalid compact preview padding");
    if(Data!=null)throw new IOException("Repeated compact preview packet");
    Session=session;Hz=hz;Probe=probe;Kind=kind;Channel=channel;FirstTick=first;PreviewHz=previewHz;Rows=(int)rows;ValidRows=valid;ValidMask=(uint)valid;
    Data=new byte[128];Buffer.BlockCopy(b,48,Data,0,Data.Length);received=Data.Length;return true;
   }
   if(kind==2){ValidMask=(uint)offset;if(ValidMask==0)throw new IOException("Empty rolling preview packet");ValidRows=ValidMask;offset=0;}
   if(Data==null){if(offset!=0)throw new NpV2PreviewUnavailableException(128);Data=new byte[total];Session=session;Hz=hz;Probe=probe;Kind=kind;Channel=channel;FirstTick=first;sixBitPayload=marked;Rows=kind==2?16:kind==1?256:0;PreviewHz=kind==2?500U:0;}
   if(session!=Session||hz!=Hz||probe!=Probe||kind!=Kind||channel!=Channel||first!=FirstTick||total!=Data.Length||marked!=sixBitPayload)throw new IOException("Incomplete/mixed preview; discard burst");
   if(offset!=received)throw new NpV2PreviewUnavailableException(128); // Disposable gap: no partial waveform accepted.
   if(marked)for(int i=32;i<b.Length;i++)if(b[i]>=64)throw new NpV2PreviewUnavailableException(128);
   int n=Math.Min(192,total-offset);if(n<=0)throw new IOException("Repeated preview fragment");
   Buffer.BlockCopy(b,32,Data,offset,n);received+=n;return received==Data.Length;
  }
  internal int FindPhase(){
   if(Kind!=0||Data==null||received!=3200)throw new IOException("Complete calibration snapshot required");
   byte[] sync={0xd3,0x6a,0x59,0xc5,0x3e,0xa7,0x1d,0xe4,0xb2,0x4f,0x78,0x96};
   int found=-1,shift=((int)Probe-1)*3;
   for(int phase=0;phase<1600;phase++){
    bool valid=true;int position=(int)(FirstTick%1600)+(int)phase;position%=1600;
    for(int i=0;i<Data.Length;i++,position=(position+1)%1600){
     int tick=position%100;if(tick>=4)continue;int expected=0,frame=position/100;
     if(tick==0)expected=frame==0?7:0;
     else if(tick>1)for(int lane=0;lane<3;lane++){int bit=frame*6+lane*2+tick-2;expected|=((sync[bit/8]>>(bit&7))&1)<<lane;}
     if(((Data[i]>>shift)&7)!=expected){valid=false;break;}
    }
    if(valid){if(found>=0)throw new IOException("Ambiguous probe framing");found=phase;}
   }
   if(found<0)throw new IOException("Probe SuperSync not found; no waveform displayed");return found;
  }
  internal ushort[] Decode(int phase){
   if(Kind!=1||Data==null||received!=3072)throw new IOException("Complete gathered burst required");
   var plan=new NpV2PreviewPlan((int)Probe,(int)Channel,phase,256,(int)Hz,FirstTick);
   if(plan.FirstTick!=0)throw new IOException("Preview phase/origin mismatch");
   var values=plan.Decode(Data);ushort[] selected=new ushort[256];for(int i=0;i<256;i++)selected[i]=values[i,Channel/128];return selected;
  }
  internal ushort[,] DecodeRolling(int phase){
   if((Kind!=2&&Kind!=5)||Data==null||received!=(Kind==5?128:192))throw new IOException("Complete rolling packet required");
   var plan=new NpV2PreviewPlan((int)Probe,(int)Channel,phase,Rows,(int)Hz,FirstTick);
   if(plan.FirstTick!=0)throw new IOException("Rolling preview phase/origin mismatch");
   if(Kind==2)return plan.Decode(Data);
   // Compact words retain first-wire-tick in bit0. Expand only that selected
   // PSSI bit for the existing PC decoder; never resample or fill missing rows.
   var raw=new byte[Rows*12];int lane=(int)Channel/128,shift=((int)Probe-1)*3+lane;
   for(int row=0;row<Rows;row++){
    ushort word=BitConverter.ToUInt16(Data,row*2);
    for(int bit=0;bit<12;bit++)raw[row*12+bit]=(byte)(((word>>bit)&1)<<shift);
   }
   var decoded=plan.Decode(raw);var selected=new ushort[Rows,1];
   for(int row=0;row<Rows;row++)selected[row,0]=decoded[row,lane];return selected;
  }
 }
}
