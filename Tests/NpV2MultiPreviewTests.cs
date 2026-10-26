using System;
using System.IO;
using System.Linq;
using CE32_console;
static class NpV2MultiPreviewTests
{
 static int checks;static void Need(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
 static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){checks++;return;}throw new Exception("Expected "+typeof(T).Name);}
 static void Put(byte[] b,int at,uint v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,4);}
 static void Put16(byte[] b,int at,ushort v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,at,2);}
 static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
 static void Crc(byte[] b){Put(b,220,NpSdControl.Crc(b,220));}
 static readonly NpV2MultiPreview.Selection[] Ch={new NpV2MultiPreview.Selection(1,17,123),new NpV2MultiPreview.Selection(2,383,1537)};
 const uint Id=29,Session=456,Hz=20000;const ulong First=16000000,Step=64000,Span=40*Step;
 static byte[] Frame(uint seq,ulong first,uint hz=Hz,int count=2){
  byte[] b=new byte[224];Put(b,0,NpV2PreviewBurst.Magic);Put(b,4,Id);Put(b,8,Session);Put(b,12,hz);Put(b,16,(uint)first);Put(b,20,(uint)(first>>32));Put(b,24,(160U<<16)|40);Put(b,28,(6U<<8)|((uint)count<<16));Put(b,32,seq);Put(b,36,500);
  for(int i=0;i<count;i++){Put16(b,40+i*2,Ch[i].Descriptor);Put16(b,48+i*2,Ch[i].WordOffset);}Put16(b,52,1);Put16(b,54,NpV2MultiPreview.Delay(hz));
  for(int row=0;row<40;row++)for(int col=0;col<count;col++)Put16(b,60+(row*2+col)*2,unchecked((ushort)(short)(-1200+row*5+col*17)));
  Crc(b);return b;
 }
 static byte[] Cap(){var b=new byte[224];Put(b,0,NpV2PreviewBurst.Magic);Put(b,4,Id);Put(b,8,Session);Put(b,12,Hz);Put(b,28,8<<8);Put(b,32,1);Put(b,36,2);Put(b,40,500);Put(b,44,40);Put(b,48,32);Put(b,52,7);Crc(b);return b;}
 static NpV2MultiPreviewReceiver Receiver(){return new NpV2MultiPreviewReceiver(Id,Session,Hz,Ch,8);}
 static void Run(){
  Need(NpV2MultiPreview.ValidRequest(NpV2MultiPreview.Configure(Id,Session,Ch)),"Configuration request failed validation");
  Need(NpV2MultiPreview.ValidRequest(NpV2MultiPreview.CapabilityRequest(Id)),"Capability request failed");
  Need(NpV2MultiPreview.ValidRequest(NpV2MultiPreview.Feedback(Id,Session,3,4)),"Feedback failed");
  Need(NpV2MultiPreview.ValidRequest(NpV2MultiPreview.Feedback(Id,Session,3,4,true)),"Stop feedback failed");
  Throws<ArgumentException>(()=>NpV2MultiPreview.Configure(Id,Session,new[]{Ch[0],Ch[0]}));
  Throws<ArgumentOutOfRangeException>(()=>NpV2MultiPreview.Configure(Id,Session,new[]{default(NpV2MultiPreview.Selection)}));
  var badRequest=NpV2MultiPreview.Configure(Id,Session,Ch);badRequest[16]=250;Buffer.BlockCopy(BitConverter.GetBytes(NpSdControl.Crc(badRequest,60)),0,badRequest,60,4);Need(!NpV2MultiPreview.ValidRequest(badRequest),"Lower unqualified rate accepted");
  foreach(uint hz in new uint[]{7500,15000,20000,30000})for(int probe=1;probe<=2;probe++)foreach(int ch in new[]{0,17,127,128,255,256,383}){
   var x=new NpV2MultiPreview.Selection((uint)probe,(uint)ch,1537);var p=new NpV2PreviewPlan(probe,ch,1537,1,(int)hz,0);Need(x.WordOffset==p.FirstTick,"Existing decoder phase convention changed");
  }
  var cap=NpV2MultiPreview.ParseCapabilities(Cap(),Id);Need(cap.HistorySlots==32&&cap.SourceHz==Hz&&cap.Session==Session,"Capability metadata");
  var legacy=new byte[224];Put(legacy,0,NpV2PreviewBurst.Magic);Put(legacy,4,Id);Put(legacy,28,129<<8);Need(NpV2MultiPreview.LegacyUnsupported(legacy,Id),"Legacy refusal not recognized");Need(!NpV2MultiPreview.LegacyUnsupported(legacy,Id+1),"Unrelated refusal recognized");
  var decoded=NpV2MultiPreview.ParseFrame(Frame(1,First),Id,Session,Hz,Ch);Need(decoded.Samples[0,0]==-1200&&decoded.Samples[39,1]==-988,"Signed fractional samples changed");Need(decoded.FirstTick==First&&decoded.Step==Step,"Group delay subtracted twice or cadence changed");
  var one=NpV2MultiPreview.ParseFrame(Frame(1,First,Hz,1),Id,Session,Hz,new[]{Ch[0]});Need(one.Samples.GetLength(1)==1,"Single filtered channel not admitted");
  for(int at=8;at<224;at++){var bad=Frame(1,First);bad[at]^=1;Throws<NpV2MultiPreview.ChecksumException>(()=>NpV2MultiPreview.ParseFrame(bad,Id,Session,Hz,Ch));}
  using(var rx=Receiver()){
   rx.Receive(8,Frame(2,First+Span));rx.Receive(8,Frame(3,First+2*Span));Need(rx.Drain().Count==0&&rx.Acknowledged==0&&rx.Missing==1,"Out-of-order data was published");
   Need(U(rx.Feedback(),20)==0&&U(rx.Feedback(),24)==1,"Missing sequence not requested");
   rx.Receive(8,Frame(1,First));var q=rx.Drain();Need(q.Count==3&&q[0].Sequence==1&&q[2].Sequence==3&&rx.Samples==240,"Replayed contiguous data lost samples");Need(rx.Acknowledged==3&&rx.Missing==0,"Cumulative ACK incorrect");
   rx.Receive(8,Frame(2,First+Span));Need(rx.Drain().Count==0&&rx.Packets==3,"Duplicate replay published");
   var corrupt=Frame(4,First+3*Span);corrupt[80]^=1;rx.Receive(8,corrupt);Need(rx.ChecksumFailures==1&&rx.Missing==4&&rx.Drain().Count==0,"Bad CRC was published");
   rx.Receive(8,Frame(4,First+3*Span));Need(rx.Drain().Count==1&&rx.Acknowledged==4&&rx.Missing==0,"CRC replay failed");
   rx.Receive(9,Frame(5,First+4*Span));Need(rx.Drain().Count==0&&rx.Acknowledged==4,"Prior connection epoch accepted");
  }
  using(var rx=Receiver()){rx.Receive(8,Frame(1,First));rx.Drain();rx.Receive(8,Frame(2,First+Span+1));Throws<InvalidDataException>(()=>rx.Drain());Need(rx.Acknowledged==1,"Timestamp hole was acknowledged");}
  using(var rx=Receiver()){var f=Frame(1,First);Put(f,8,Session+1);Crc(f);rx.Receive(8,f);Throws<InvalidDataException>(()=>rx.Drain());Need(rx.Acknowledged==0,"Other recording acknowledged");}
  using(var rx=Receiver()){var f=Frame(1,First);Put(f,24,0);Put(f,56,9);Crc(f);rx.Receive(8,f);Throws<NpV2MultiPreview.ContinuityException>(()=>rx.Drain());Need(rx.Samples==0,"Continuity error fabricated samples");}
  using(var rx=Receiver()){for(uint i=1;i<=129;i++)rx.Receive(8,Frame(i,First+(i-1)*Span));Throws<IOException>(()=>rx.Drain());Need(rx.Packets==128,"Receive pressure silently dropped data");}
  using(var rx=Receiver()){rx.Receive(8,Frame(65,First+64*Span));Throws<IOException>(()=>rx.Drain());Need(rx.Acknowledged==0,"Unbounded reorder window accepted");}
  Console.WriteLine("PASS multi-preview codec/replay continuity: "+checks+" assertions");
 }
 static int Main(){try{Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
