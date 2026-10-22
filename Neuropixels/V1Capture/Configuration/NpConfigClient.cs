using System;using System.IO;using System.Threading;using System.Threading.Tasks;
namespace WildNpConfiguration {
 public sealed class NpConfigRequest {
  public readonly string Json;public readonly byte[] Selection;
  public byte Port {get{return Selection[5];}}
  public NpConfigRequest(string json,byte[] selection) {
   if(String.IsNullOrEmpty(json) || selection==null || selection.Length!=1576 ||
      selection[0]!=78 || selection[1]!=80 || selection[2]!=66 || selection[3]!=49 ||
      (selection[4]!=1 && selection[4]!=2) || selection[5]>2)
    throw new InvalidDataException("Invalid V1 configuration request.");
   Json=json;Selection=(byte[])selection.Clone();
  }
 }
 public interface INpConfigTransport {Task<byte[]> ExchangeAsync(byte[] request,CancellationToken cancel);}
 public sealed class NpConfigReply {
  public byte Error,TransferState,PortState,Verification;
  public uint Transaction,Received,PayloadCrc;
  public byte[] ConfigurationId;
 }
 public sealed class NpConfigClient {
  readonly INpConfigTransport transport;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
  public NpConfigClient(INpConfigTransport transport){this.transport=transport;}
  public static uint Crc(byte[] b,int n){uint c=~0U;for(int i=0;i<n;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((0-(c&1))&0xedb88320);}return ~c;}
  static void Put(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(8*i));}
  public static byte[] Frame(byte op,byte port,uint transaction,uint offset,uint length,uint crc,byte[] data=null){
   if(port>2 || (data!=null && data.Length>32))throw new ArgumentOutOfRangeException();
   var b=new byte[64];b[0]=78;b[1]=80;b[2]=67;b[3]=49;b[4]=1;b[5]=op;b[6]=port;
   Put(b,8,transaction);Put(b,12,offset);Put(b,16,length);Put(b,20,crc);
   if(data!=null)Buffer.BlockCopy(data,0,b,24,data.Length);Put(b,60,Crc(b,60));return b;
  }
  public static NpConfigReply Parse(byte[] b,byte op,uint transaction){
   if(b==null||b.Length!=64||b[0]!=78||b[1]!=80||b[2]!=82||b[3]!=49||b[4]!=1||b[5]!=op||
      BitConverter.ToUInt32(b,8)!=transaction||BitConverter.ToUInt32(b,16)!=1576||BitConverter.ToUInt32(b,60)!=Crc(b,60))
    throw new IOException("NP configuration response identity/version/CRC mismatch.");
   var id=new byte[32];Buffer.BlockCopy(b,28,id,0,32);
   return new NpConfigReply {Error=b[6],TransferState=b[7],Transaction=transaction,Received=BitConverter.ToUInt32(b,12),
    PortState=b[20],Verification=b[21],PayloadCrc=BitConverter.ToUInt32(b,24),ConfigurationId=id};
  }
  async Task<NpConfigReply> Send(byte[] frame,CancellationToken cancel){
   // Retry the SAME transaction/offset only on missing replies, never replay a
   // different upload or claim that a lost COMMIT ACK means failure to apply.
   for(int attempt=0;;attempt++) {
    cancel.ThrowIfCancellationRequested();
    try{return Parse(await transport.ExchangeAsync(frame,cancel),frame[5],BitConverter.ToUInt32(frame,8));}
    catch(TimeoutException){if(attempt>=2)throw;}
   }
  }
  public async Task<NpConfigReply> StatusAsync(byte port,CancellationToken cancel){
   await gate.WaitAsync(cancel);try{return await Send(Frame(0,port,0,0,0,0),cancel);}finally{gate.Release();}
  }
  public Task<NpConfigReply> StageAsync(byte[] selection,CancellationToken cancel){return StageAsync(selection,cancel,false);}
  public async Task<NpConfigReply> StageAsync(byte[] selection,CancellationToken cancel,bool reuseMatching){
   if(selection==null||selection.Length!=1576||selection[0]!=78||selection[1]!=80||selection[2]!=66||selection[3]!=49||selection[5]>2)
    throw new InvalidDataException("Invalid NP selection payload.");
   await gate.WaitAsync(cancel);byte port=selection[5];uint id=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0)|1U,crc=Crc(selection,selection.Length);
   bool begun=false,commitSent=false;
   try {
    var status=await Send(Frame(0,port,0,0,0,0),cancel);Require(status);
    // A fresh matching receipt proves that the map is resident, not that the
    // probe has been configured for this recording. START still applies it.
    if(reuseMatching && (status.TransferState==0 || status.TransferState==2) && status.Received==0 &&
       (status.PortState==2 || status.PortState==3) && status.PayloadCrc==crc &&
       status.ConfigurationId!=null && status.ConfigurationId.Length==32){
     bool same=true;for(int i=0;i<32;i++)if(status.ConfigurationId[i]!=selection[8+i]){same=false;break;}
     if(same)return status;
    }
    Require(await Send(Frame(1,port,id,0,1576,crc),cancel));begun=true;
    for(int at=0;at<selection.Length;at+=32) {
     int count=Math.Min(32,selection.Length-at);var data=new byte[count];Buffer.BlockCopy(selection,at,data,0,count);
     var reply=await Send(Frame(2,port,id,(uint)at,(uint)count,0,data),cancel);Require(reply);
     if(reply.Received!=(uint)(at+count))throw new IOException("NP configuration upload offset mismatch.");
    }
    commitSent=true;var result=await Send(Frame(3,port,id,0,0,crc),cancel);Require(result);
    if(result.PayloadCrc!=crc || result.PortState!=2)throw new IOException("NP configuration was not staged as requested.");
    for(int i=0;i<32;i++)if(result.ConfigurationId[i]!=selection[8+i])throw new IOException("NP configuration identity mismatch.");
    return result;
   } finally {
    // Never abort after an uncertain COMMIT; query the device before retrying.
    if(begun && !commitSent)try{await transport.ExchangeAsync(Frame(4,port,id,0,0,0),CancellationToken.None);}catch(Exception){}
    gate.Release();
   }
  }
  public static void RequireApplied(NpConfigReply reply,byte[] selection) {
   Require(reply);
   if(reply.PortState!=3 || reply.PayloadCrc!=Crc(selection,selection.Length))
    throw new IOException("Requested P"+(selection[5]+1)+" configuration is not applied with the matching CRC. Capture was not released.");
   for(int i=0;i<32;i++)if(reply.ConfigurationId[i]!=selection[8+i])
    throw new IOException("Applied P"+(selection[5]+1)+" configuration ID differs from the request. Capture was not released.");
   int required=selection[4]==2?3:4;
   if((reply.Verification&required)!=required)
    throw new IOException("Requested P"+(selection[5]+1)+" configuration lacks the firmware verification receipt. Capture was not released.");
  }
  static void Require(NpConfigReply reply){if(reply.Error!=0)throw new IOException("NP configuration rejected by firmware, code "+reply.Error+". No applied-state claim is made.");}
  public Task StageForCaptureAsync(System.Collections.Generic.IEnumerable<NpConfigRequest> requests,int mask,CancellationToken cancel) {
   return StageForCaptureAsync(requests,mask,cancel,false);
  }
  public async Task StageForCaptureAsync(System.Collections.Generic.IEnumerable<NpConfigRequest> requests,int mask,CancellationToken cancel,bool reuseMatching) {
   foreach(var request in requests)if((mask&(1<<request.Port))!=0) {
    try{await StageAsync(request.Selection,cancel,reuseMatching);}
    catch(IOException ex){throw new IOException("Cannot stage requested P"+(request.Port+1)+" configuration after final capture-mode selection: "+ex.Message,ex);}
   }
  }
 }
}
