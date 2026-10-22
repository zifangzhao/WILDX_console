using System;
using System.IO;
using System.Threading.Tasks;
namespace CE32_console {
 internal sealed class NpSdStatus {
  internal uint Operation,RequestId,Error,State,Fault,Mask,Hz,Chunks,Start,End,Capacity,Flags,LastId;
  internal bool Qualified{get{return (Flags&2)!=0;}}
  internal bool Busy{get{return (Flags&12)!=0;}}
  public override string ToString(){return string.Format("Mask {0}, AP {1:N0} Hz / LFP {2:N3} Hz\r\nState {3}, fault {4}, chunks {5:N0}; card {6:N0} sectors\r\n{7}; writes {8}; mounted {9}; pending {10}",Mask,Hz,Hz/12.0,State,Fault,Chunks,Capacity,Qualified?"Card/profile validation reported":"Card/profile validation not reported",(Flags&1)!=0,(Flags&16)!=0,(Flags&8)!=0);}
 }
 internal sealed class NpSdCommandException : IOException {
  internal readonly NpSdStatus Status;
  internal readonly int Version;
  internal NpSdCommandException(int version,NpSdStatus status,string detail=null) : base(
   "Device rejected operation "+status.Operation+": error "+status.Error+"; fault "+status.Fault+"."+
   (String.IsNullOrEmpty(detail)?"":" "+detail)) {
   Version=version;Status=status;
  }
 }
 internal sealed class NpSdControl {
  readonly Func<byte[],Task<byte[]>> exchange;
  uint statusSequence=unchecked((uint)Environment.TickCount);
  internal NpSdControl(Func<byte[],Task<byte[]>> send){exchange=send;}
  internal static uint Crc(byte[] b,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[i];for(int bit=0;bit<8;bit++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
  static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
  internal static byte[] Request(uint op,uint id,uint mask=0,uint hz=0,uint start=0,uint end=0,uint session=0,uint flags=0,uint mode=0){
   if(id==0 || op>5)throw new ArgumentException("Invalid command/id");
   var b=new byte[64];uint[] w={0x31444c4e,1,op,id,mask,hz,start,end,session,flags,mode};for(int i=0;i<w.Length;i++)Put(b,i*4,w[i]);Put(b,60,Crc(b,60));return b;
  }
  internal static NpSdStatus Parse(byte[] b,uint op,uint id){
   if(b==null || b.Length!=64 || NpSdGeometry.U(b,0)!=0x31524c4e || NpSdGeometry.U(b,4)!=1 || NpSdGeometry.U(b,8)!=op || NpSdGeometry.U(b,12)!=id || NpSdGeometry.U(b,60)!=Crc(b,60))throw new IOException("No valid selectable-SD firmware acknowledgement");
   return new NpSdStatus{Operation=op,RequestId=id,Error=NpSdGeometry.U(b,16),State=NpSdGeometry.U(b,20),Fault=NpSdGeometry.U(b,24),Mask=NpSdGeometry.U(b,28),Hz=NpSdGeometry.U(b,32),Chunks=NpSdGeometry.U(b,36),Start=NpSdGeometry.U(b,40),End=NpSdGeometry.U(b,44),Capacity=NpSdGeometry.U(b,48),Flags=NpSdGeometry.U(b,52),LastId=NpSdGeometry.U(b,56)};
  }
  internal async Task<NpSdStatus> Send(byte[] request){
   // No blind retry after timeout: the user can read status and decide. Firmware
   // supports exact-id retries, but reboot could clear its duplicate cache.
   var s=Parse(await exchange(request),NpSdGeometry.U(request,8),NpSdGeometry.U(request,12));
   if(s.Error!=0)throw new IOException("SD command rejected: "+s.Error+" (1 frame, 2 busy, 3 range, 4 writes disabled, 5 unqualified card/profile, 6 stale ID, 7 sensor/I/O).\r\n"+s);
   return s;
  }
  internal Task<NpSdStatus> Status(){uint id=unchecked(++statusSequence);if(id==0)id=++statusSequence;return Send(Request(0,id));}
  internal static uint NextId(NpSdStatus s){uint n=unchecked(s.LastId+1);return n==0?1:n;}
  internal static uint EndLba(NpSdStatus s,uint mask,uint hz,uint seconds){
   if(!NpSdGeometry.Valid(mask,hz) || seconds==0 || s.Start<8192)throw new IOException("Invalid SD selection/start extent");
   ulong header=8192+((0U-(s.Start+8192U))&16383U);
   ulong bytes=(ulong)seconds*hz*832U*(uint)NpSdGeometry.Count(mask);
   ulong end=s.Start+header+((bytes+524287)/524288)*1024;
   if(end>s.Capacity || end>uint.MaxValue)throw new IOException("Not enough reserved SD space for requested duration");
   return (uint)end;
  }
 }
}
