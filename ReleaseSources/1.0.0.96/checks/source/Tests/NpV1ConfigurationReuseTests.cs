using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WildNpConfiguration;

static class NpV1ConfigurationReuseTests {
 static int checks;
 static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
 static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
 static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
 static NpConfigRequest Request(byte port){
  var b=new byte[1576];b[0]=78;b[1]=80;b[2]=66;b[3]=49;b[4]=2;b[5]=port;
  for(int i=0;i<32;i++)b[8+i]=(byte)(i+port+1);
  for(int ch=0;ch<384;ch++){int at=40+ch*4;int site=ch==191?65535:ch;b[at]=(byte)site;b[at+1]=(byte)(site>>8);b[at+2]=(byte)(83|(ch==191?128:0));}
  return new NpConfigRequest("test map "+port,b);
 }
 sealed class Wire:INpConfigTransport {
  internal readonly Dictionary<byte,byte[]> Active=new Dictionary<byte,byte[]>();
  internal readonly List<byte[]> Frames=new List<byte[]>();
  internal byte PortState=2,TransferState=2,StatusError;
  internal uint Received;
  internal bool WrongCrc,WrongId,BadReplyCrc;
  byte[] upload;uint received;
  public Task<byte[]> ExchangeAsync(byte[] q,CancellationToken cancel){
   cancel.ThrowIfCancellationRequested();Frames.Add((byte[])q.Clone());byte op=q[5],port=q[6];
   byte error=op==0?StatusError:(byte)0;
   if(op==1){if(TransferState==1)error=9;else{upload=new byte[1576];received=0;}}
   if(op==2){uint offset=U(q,12),length=U(q,16);Buffer.BlockCopy(q,24,upload,(int)offset,(int)length);received=offset+length;}
   if(op==3){Active[port]=(byte[])upload.Clone();PortState=TransferState=2;received=0;}
   var r=new byte[64];r[0]=78;r[1]=80;r[2]=82;r[3]=49;r[4]=1;r[5]=op;r[6]=error;r[7]=TransferState;
   Put(r,8,U(q,8));Put(r,12,op==0?Received:received);Put(r,16,1576);
   byte[] active;if(Active.TryGetValue(port,out active)){
    r[20]=PortState;Put(r,24,NpConfigClient.Crc(active,active.Length)^(op==0 && WrongCrc?1U:0U));Buffer.BlockCopy(active,8,r,28,32);
    if(op==0 && WrongId)r[28]^=1;
   }
   Put(r,60,NpConfigClient.Crc(r,60));if(op==0 && BadReplyCrc)r[60]^=1;
   return Task.FromResult(r);
  }
 }
 static async Task Run(){
  var p1=Request(0);var p2=Request(1);var both=new[]{p1,p2};
  var wire=new Wire();var client=new NpConfigClient(wire);
  await client.StageForCaptureAsync(both,3,CancellationToken.None,true);
  Need(wire.Frames.Count==106,"Initial two-probe setup did not use the complete legacy upload");
  Need(wire.Active.Count==2,"Initial setup lost a selected probe");
  wire.Frames.Clear();await client.StageForCaptureAsync(both,3,CancellationToken.None,true);
  Need(wire.Frames.Count==2 && wire.Frames.TrueForAll(q=>q[5]==0),"Unchanged resident maps were reuploaded or reused without a fresh per-port status");
  foreach(byte global in new byte[]{0,2})foreach(byte port in new byte[]{2,3}){
   wire=new Wire{TransferState=global,PortState=port};wire.Active[0]=p1.Selection;
   await new NpConfigClient(wire).StageForCaptureAsync(new[]{p1},1,CancellationToken.None,true);
   Need(wire.Frames.Count==1,"A supported idle/staged resident-map identity was not reused");
  }
  foreach(string changed in new[]{"crc","id","failed-port","empty-port","receiving","unknown-transfer","failed-transfer","partial-receipt","reset"}){
   wire=new Wire();wire.Active[0]=p1.Selection;
   if(changed=="crc")wire.WrongCrc=true;if(changed=="id")wire.WrongId=true;
   if(changed=="failed-port")wire.PortState=4;if(changed=="empty-port")wire.PortState=0;
   if(changed=="receiving")wire.TransferState=1;if(changed=="unknown-transfer")wire.TransferState=3;
   if(changed=="failed-transfer")wire.TransferState=4;if(changed=="partial-receipt")wire.Received=32;
   if(changed=="reset")wire.Active.Clear();
   bool refused=false;try{await new NpConfigClient(wire).StageForCaptureAsync(new[]{p1},1,CancellationToken.None,true);}catch(IOException){refused=true;}
   Need(wire.Frames.Count>1 && wire.Frames[1][5]==1,"Untrusted or mismatching map was silently reused: "+changed);
   Need(changed=="receiving"?refused:!refused && wire.Frames.Count==53,"Legacy upload/error path changed: "+changed);
  }
  foreach(bool corrupt in new[]{false,true}){
   wire=new Wire{BadReplyCrc=corrupt,StatusError=corrupt?(byte)0:(byte)7};wire.Active[0]=p1.Selection;
   bool refused=false;try{await new NpConfigClient(wire).StageForCaptureAsync(new[]{p1},1,CancellationToken.None,true);}catch(IOException){refused=true;}
   Need(refused && wire.Frames.Count==1,"Invalid/error status released upload or a defaults fallback");
  }
  wire=new Wire();wire.Active[0]=p1.Selection;
  await new NpConfigClient(wire).StageForCaptureAsync(new[]{p1},1,CancellationToken.None);
  Need(wire.Frames.Count==53,"Existing USB/manual API silently enabled reuse");
  wire=new Wire();wire.Active[0]=p1.Selection;
  await new NpConfigClient(wire).StageForCaptureAsync(both,1,CancellationToken.None,true);
  Need(wire.Frames.Count==1 && wire.Frames[0][6]==0 && !wire.Active.ContainsKey(1),"Inactive ports were touched");
  Console.WriteLine("PASS "+checks+" actual V1 map-reuse checks; unchanged two-probe setup 106 to 2 exchanges, reset/mismatch/failure fallback retained; no hardware.");
 }
 static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
