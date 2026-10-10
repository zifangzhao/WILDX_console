using System;
using System.IO;
using System.Globalization;
namespace USB512_interface {
 // Incoming metadata follows SD NPSENS01. The saved analogin.dat has the
 // CE64-compatible 16 x 16-bit layout on an explicit 1.25 kHz held-value grid.
 internal sealed class NpUsbSensors : IDisposable {
  private readonly NpMisc1250 uniform;
  internal long Misc1250Records { get { return uniform.Records; } }
  private bool haveImu,haveRecord,haveSession;
  private uint lastImu,lastRecord,session;
  internal long Records,ImuSamples,ImuSequenceGaps;
  internal uint Valid,Overflow,MissedEstimate,ImuError,EnvironmentError;
  internal string DisplayText="Waiting for NP sensor metadata...";
  private readonly double[] displayValues=new double[9];
  private uint displaySequence,displayTick;
  internal NpImuDisplay GetDisplay() {return new NpImuDisplay {
   Values=(double[])displayValues.Clone(),Valid=Valid,Sequence=displaySequence,
   DeviceMs=displayTick,ImuError=ImuError,EnvironmentError=EnvironmentError,Overflow=Overflow};}
  internal NpUsbSensors(string folder) {
   // Keep one canonical sensor sidecar.  The 1.25 kHz held-value stream is
   // the useful acquisition product; raw metadata packets and a duplicate
   // CSV made each NP USB recording needlessly large and ambiguous.
   uniform=new NpMisc1250(folder);
  }
  private static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
  private static int I(byte[] b,int o){return BitConverter.ToInt32(b,o);}
  private static bool Magic(byte[] b,int o,string text) {
   for(int i=0;i<text.Length;i++)if(b[o+i]!=(byte)text[i])return false;return true;
  }
  internal static uint Crc(byte[] b,int o,int n) {
   uint c=0xffffffff;for(int i=0;i<n;i++){c^=b[o+i];for(int j=0;j<8;j++)c=(c>>1)^((0-(c&1))&0xedb88320);}return ~c;
  }
  internal void Feed(byte[] b,int o,int n) {
   if(n!=506 || !Magic(b,o,"NPSENS01") || U(b,o+16)!=140 ||
      U(b,o+20)!=0x31534e53 || U(b,o+24)!=1 || !Magic(b,o+164,"NPUSB001") ||
      U(b,o+172)!=1 || U(b,o+160)!=Crc(b,o,160) || U(b,o+502)!=Crc(b,o,502))
    throw new IOException("Invalid NP sensor metadata/version/CRC; recording stopped.");
   uint record=U(b,o+8), sid=U(b,o+176), seq=U(b,o+40);
   if(haveRecord && record!=unchecked(lastRecord+1))throw new IOException("NP sensor record sequence gap.");
   if(haveSession && session!=sid)throw new IOException("NP sensor firmware session changed without a new segment.");
   lastRecord=record;haveRecord=true;session=sid;haveSession=true;
   Valid=U(b,o+32);Overflow=U(b,o+184);MissedEstimate=U(b,o+148);
   ImuError=U(b,o+124);EnvironmentError=U(b,o+128);
   for(int axis=0;axis<9;axis++)displayValues[axis]=I(b,o+68+axis*4)/(axis<3?1e6:1e3);
   displaySequence=seq;displayTick=U(b,o+52);
   Records++;
   uniform.Feed(b,o);
   if((Valid&1)!=0 && (!haveImu || seq!=lastImu)) {
    if(haveImu) {uint delta=unchecked(seq-lastImu);if(delta>1)ImuSequenceGaps+=delta-1;}
    ImuSamples++;lastImu=seq;haveImu=true;
   }
   string axes=(Valid&1)==0?"NOT READY":(Valid&2)==0?"6-axis; magnetic data unavailable":"9-axis";
   string magText=(Valid&2)==0?"Mag: unavailable (not zero-field data)":String.Format(CultureInfo.InvariantCulture,
    "Mag (uT): {0:F2}, {1:F2}, {2:F2}",I(b,o+92)/1e3,I(b,o+96)/1e3,I(b,o+100)/1e3);
   DisplayText=String.Format(CultureInfo.InvariantCulture,
    "NP IMU | {0} | sample {1} | device {2} ms\nAccel (g): {3:F3}, {4:F3}, {5:F3}\nGyro (deg/s): {6:F2}, {7:F2}, {8:F2}\n{9}\nErrors: IMU {10}, environment {11} | queue loss {12} | missed estimate {13}",
    axes,seq,U(b,o+52),I(b,o+68)/1e6,I(b,o+72)/1e6,I(b,o+76)/1e6,
    I(b,o+80)/1e3,I(b,o+84)/1e3,I(b,o+88)/1e3,magText,
    ImuError,EnvironmentError,Overflow,MissedEstimate);
  }
  public void Dispose(){uniform.Dispose();}
 }

 internal sealed class NpImuDisplay {
  internal double[] Values;
  internal uint Valid,Sequence,DeviceMs,ImuError,EnvironmentError,Overflow;
 }

 // Zero-order hold on device source timestamps, not host arrival times. This
 // derived 1250 Hz grid never claims that repeated readings are fresh samples.
 // Every row is exactly the 32-byte CE64 analogin record layout:
 // [digin, ax, ay, az, gx, gy, gz, mx, my, mz, slow ADC, test,
 //  closed-loop 0, closed-loop 1, PC time low/high].
 internal sealed class NpMisc1250 : IDisposable {
  private readonly FileStream file;
  private readonly byte[] held=new byte[140],record=new byte[32];
  private bool started,disposed;
  private uint lastTick,lastSequence,lastObservation,pcOriginMs,deviceOriginMs;
  private ulong sourceUs,nextUs;
  internal long Records;
  internal NpMisc1250(string folder) {
   // This is the NP equivalent of the CE64 low-rate misc stream.  Name it
   // analogin.dat to match the rest of the WILD/CE64 acquisition ecosystem.
   file=new FileStream(Path.Combine(folder,"analogin.dat"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
  }
  private static uint U(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
  private static void Put(byte[] b,int o,uint v){for(int i=0;i<4;i++)b[o+i]=(byte)(v>>(8*i));}
  private static void Put16(byte[] b,int o,short v){b[o]=(byte)v;b[o+1]=(byte)(v>>8);}
  private static short Scale(int value,int divisor) {
   long magnitude=value<0?-(long)value:value;
   long scaled=(magnitude+divisor/2)/divisor;
   if(value<0)scaled=-scaled;
   if(scaled>short.MaxValue)return short.MaxValue;
   if(scaled<short.MinValue)return short.MinValue;
   return (short)scaled;
  }
  internal void Feed(byte[] b,int o) {
   uint seq=U(b,o+40),tick=U(b,o+52),now=U(b,o+12);
   if(!started) {
    // Do not backfill before the first actual AG observation.
    if((U(b,o+32)&1)==0)return;
    lastTick=tick;lastSequence=seq;started=true;deviceOriginMs=tick;
    pcOriginMs=(uint)DateTime.Now.TimeOfDay.TotalMilliseconds;
    Buffer.BlockCopy(b,o+20,held,0,140);
   } else if(seq!=lastSequence && (U(b,o+32)&1)!=0) {
    uint delta=unchecked(tick-lastTick);
    if(delta>60000)throw new IOException("NP misc source clock discontinuity (>60 s); start a new segment.");
    ulong time=sourceUs+(ulong)delta*1000;
    EmitBefore(time);sourceUs=time;lastTick=tick;lastSequence=seq;
    Buffer.BlockCopy(b,o+20,held,0,140);
   }
   // NP_OPTIONAL_SENSOR_VALIDITY: a failure/status transition is not a
   // new IMU sample, but it must end the previous held-value interval.
   if(started && U(held,12)!=U(b,o+32)) {
    uint delta=unchecked(now-lastTick);
    if(delta>60000)throw new IOException("NP misc status clock discontinuity.");
    ulong time=sourceUs+(ulong)delta*1000;
    EmitBefore(time);sourceUs=time;lastTick=now;
    Buffer.BlockCopy(b,o+20,held,0,140);
   }
   // Export observations can contain several queued samples with the same now.
   // Only use this watermark on close, after all those samples were consumed.
   if(unchecked(now-lastTick)>60000)throw new IOException("NP misc observation clock discontinuity.");
   lastObservation=now;
  }
  private void EmitBefore(ulong end) {
   while(nextUs<end) {
    Array.Clear(record,0,record.Length);
    // Digin, slow ADC and closed-loop outputs are not in NPSENS01.  Use the
    // CE64 unavailable/idle representations instead of fabricating values.
    Put16(record,0,0);                 // word 1: digital input unavailable
    uint atMs=unchecked(deviceOriginMs+(uint)(nextUs/1000));
    uint valid=U(held,12);
    bool ag=(valid&1)!=0 && unchecked(atMs-U(held,32))<=100;
    bool mag=(valid&2)!=0 && unchecked(atMs-U(held,36))<=200;
    // Fixed CE64 channel geometry is preserved. Disabled sensor channels
    // carry the unavailable sentinel, never plausible zero/stale readings.
    for(int axis=0;axis<6;axis++)
     Put16(record,2+axis*2,ag?Scale(BitConverter.ToInt32(held,48+axis*4),axis<3?1000:100):short.MinValue);
    for(int axis=0;axis<3;axis++)
     Put16(record,14+axis*2,mag?Scale(BitConverter.ToInt32(held,72+axis*4),1000):short.MinValue);
    Put16(record,20,short.MinValue);   // word 11: slow ADC unavailable
    Put16(record,22,ag?unchecked((short)(ushort)U(held,20)):(short)-1); // word 12: valid IMU sequence or unavailable
    Put16(record,24,0);Put16(record,26,0); // words 13-14: closed-loop idle
    Put(record,28,unchecked(pcOriginMs+(uint)(nextUs/1000)));
    file.Write(record,0,record.Length);
    Records++;nextUs+=800;
   }
  }
  public void Dispose() {
   if(disposed)return;disposed=true;
   try {
    if(started)EmitBefore(sourceUs+(ulong)unchecked(lastObservation-lastTick)*1000);
    file.Flush(true);
   } finally {file.Dispose();}
  }
 }
}
