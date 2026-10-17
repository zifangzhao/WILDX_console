using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Exercises the shipped catalog reader against read-only synthetic cards.
static class NpAvailableCatalogTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Type catalog;static int checks;
 static readonly uint[] Starts={8192,24576,40960,57344};
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Put(byte[] b,int at,uint value){BitConverter.GetBytes(value).CopyTo(b,at);}
 static uint Crc(byte[] b,int length){uint c=uint.MaxValue;for(int n=0;n<length;n++){c^=b[n];for(int k=0;k<8;k++)c=(c>>1)^((c&1)!=0?0xedb88320U:0);}return ~c;}
 static void Seal(byte[] b){Put(b,508,Crc(b,508));}
 static void Sector(byte[] card,uint lba,byte[] b){b.CopyTo(card,checked((int)lba*512));}
 static byte[] Card(){
  byte[] card=new byte[(Starts[3]+9216)*512],id=new byte[512],directory=new byte[512];
  Encoding.ASCII.GetBytes("CE_FILESYSTEM_V1").CopyTo(id,0);Encoding.ASCII.GetBytes("NP_CE64_1").CopyTo(id,32);Sector(card,16,id);Put(directory,0,4);
  for(int i=0;i<4;i++){
   uint start=Starts[i],end=start+9216;Put(directory,4+i*4,end);Put(directory,256+i*4,end);
   byte[] h=new byte[512];Encoding.ASCII.GetBytes("NPSDLOG1").CopyTo(h,0);
   foreach(var pair in new[]{new uint[]{8,6},new uint[]{12,(uint)i+1},new uint[]{16,1},new uint[]{20,1},new uint[]{24,15000},new uint[]{28,1250},new uint[]{32,15360},new uint[]{36,128},new uint[]{40,start},new uint[]{44,end},new uint[]{48,8192},new uint[]{80,2},new uint[]{96,61632}})Put(h,(int)pair[0],pair[1]);
   byte[] f=(byte[])h.Clone();Encoding.ASCII.GetBytes("NPSDEND1").CopyTo(f,0);Put(f,60,3);Put(f,68,8);Seal(h);Seal(f);Sector(card,start+3,h);Sector(card,start+4,f);
  }
  Sector(card,128,directory);return card;
 }
 sealed class VirtualCard:MemoryStream{readonly long length;internal VirtualCard(byte[] bytes,long size):base(bytes,false){length=size;}public override long Length{get{return length;}}}
 static IList Read(byte[] bytes,bool available=true,long length=0){using(var s=new VirtualCard(bytes,length==0?bytes.LongLength:length)){try{return (IList)catalog.GetMethod(available?"ReadAvailable":"Read",All).Invoke(null,new object[]{s});}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}}}
 static object Value(object row,string name,bool property=false){return property?row.GetType().GetProperty(name,All).GetValue(row,null):row.GetType().GetField(name,All).GetValue(row);}
 static bool Usable(object row){return (bool)Value(row,"Downloadable",true);}
 static void Sessions(IList rows,params uint[] expected){var actual=rows.Cast<object>().Where(Usable).Select(r=>(uint)Value(r,"Session")).ToArray();Need(actual.SequenceEqual(expected),"Readable sessions lost: "+String.Join(",",actual));Need(rows.Cast<object>().All(r=>!String.IsNullOrWhiteSpace(r.ToString())),"Catalog row could not be displayed");}
 static void StrictRefuses(byte[] card){bool refused=false;try{Read(card,false);}catch(InvalidDataException){refused=true;}Need(refused,"Strict mutation catalog admitted damaged metadata");}
 static void OpenNp2Tail(){
  const uint start=8192,payload=16384,reserved=268435456,blocks=2400000;
  long capacity=((long)reserved+16384)*512;
  var card=new byte[(start+16)*512];byte[] id=new byte[512],directory=new byte[512],h=new byte[512];
  Encoding.ASCII.GetBytes("CE_FILESYSTEM_V1").CopyTo(id,0);Encoding.ASCII.GetBytes("NP_PSSI_7").CopyTo(id,32);Sector(card,16,id);
  Encoding.ASCII.GetBytes("N2SDLOG1").CopyTo(h,0);
  foreach(var pair in new[]{new uint[]{8,11},new uint[]{12,558295225},new uint[]{16,20000},new uint[]{20,32000000},new uint[]{24,3},new uint[]{28,63},new uint[]{32,4},new uint[]{36,6},new uint[]{40,46592},new uint[]{44,61440},new uint[]{48,91},new uint[]{52,4},new uint[]{56,start},new uint[]{60,payload},new uint[]{64,payload+(reserved-payload)/364*364},new uint[]{68,reserved},new uint[]{72,reserved+16384},new uint[]{96,15},new uint[]{100,1}})Put(h,(int)pair[0],pair[1]);
  Seal(h);Sector(card,start+3,h);byte[] f=(byte[])h.Clone();Encoding.ASCII.GetBytes("N2SDEND1").CopyTo(f,0);
  Put(f,100,4);Put(f,104,0x56455249);Put(f,108,blocks);Put(f,112,blocks/4);Put(f,116,payload+blocks*91);BitConverter.GetBytes(blocks*61440UL).CopyTo(f,120);Seal(f);Sector(card,start+4,f);
  Put(directory,0,1);Put(directory,4,payload);Put(directory,256,payload);Sector(card,128,directory);
  var rows=Read(card,true,capacity);Need(rows.Count==1&&Usable(rows[0]),"Open V2 checkpoint remains blocked by its published payload-start fence");
  Need((long)Value(rows[0],"End")==payload+(long)blocks*91&&(long)Value(rows[0],"CatalogEnd")==payload,"Original fence lost or committed extent narrowed");
  Need(!(bool)Value(rows[0],"Complete",true)&&rows[0].ToString().Contains("unfinalized catalog"),"Incomplete tail promoted to complete");
  Need(blocks*46592UL>100000000000UL,"Large catalog case did not exceed100GB");Need(!Usable(Read(card,false,capacity)[0]),"Strict writer reader admitted open checkpoint");
  Need(!Usable(Read(card,true,capacity-512)[0]),"Changed card capacity admitted open-tail recovery");
  card[112*512]=1;Need(!Usable(Read(card,true,capacity)[1]),"Pending journal admitted unfinalized tail expansion");card[112*512]=0;
  Put(directory,4,payload+512);Put(directory,256,payload+512);Sector(card,128,directory);Need(!Usable(Read(card,true,capacity)[0]),"Arbitrary shortened directory fence admitted");
  Put(directory,4,reserved+1);Put(directory,256,reserved+1);Sector(card,128,directory);Need(!Usable(Read(card,true,capacity)[0]),"Fence beyond immutable reservation admitted");
  Put(directory,4,payload);Put(directory,256,payload);Put(directory,0,2);Put(directory,8,payload+8192);Put(directory,260,payload+8192);Sector(card,128,directory);
  Need(!Usable(Read(card,true,capacity)[0]),"Nonfinal checkpoint expanded across a following entry");
  Put(directory,0,1);Sector(card,128,directory);Put(f,100,3);Put(f,104,0);Seal(f);Sector(card,start+4,f);
  Need(Usable(Read(card,true,capacity)[0]),"Durable COMPLETE footer before directory publication was rejected");
  card[(start+4)*512+508]^=1;Need(!Usable(Read(card,true,capacity)[0]),"Corrupt footer recovered as an open tail");
 }
 [STAThread]static int Main(string[] args){try{
  catalog=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("CE32_console.NpDownloadCatalog",true);
  OpenNp2Tail();
  byte[] card=Card();Sessions(Read(card),1,2,3,4);Need(Read(card,false).Count==4,"Strict valid catalog regressed");
  card=Card();card[(Starts[1]+4)*512+508]^=1;Sessions(Read(card),1,3,4);Need(Read(card).Count==4,"Damaged footer not retained as an unavailable row");StrictRefuses(card);
  card=Card();card[(Starts[1]+3)*512]^=1;Sessions(Read(card),1,3,4);StrictRefuses(card);
  card=Card();Put(card,128*512+256+4,Starts[1]+17408);Sessions(Read(card),1,3,4);StrictRefuses(card);
  card=Card();Put(card,128*512+8,uint.MaxValue);Put(card,128*512+260,uint.MaxValue);Sessions(Read(card),1,4);StrictRefuses(card);
  card=Card();Put(card,128*512,uint.MaxValue);Sessions(Read(card),1,2,3,4);Need(Read(card).Count==5,"Invalid count fabricated unused entries");StrictRefuses(card);
  card=Card();card[112*512]=1;Sessions(Read(card),1,2,3,4);Need(Read(card).Count==5,"Pending journal notice missing");StrictRefuses(card);
  card=Card();var footer=new byte[512];Buffer.BlockCopy(card,(int)(Starts[1]+4)*512,footer,0,512);Put(footer,60,4);Put(footer,64,2);Seal(footer);Sector(card,Starts[1]+4,footer);
  var failed=Read(card);Sessions(failed,1,2,3,4);Need(!(bool)Value(failed[1],"Complete",true)&&failed[1].ToString().Contains("INCOMPLETE"),"Failed source was presented as complete");
  card=Card();card[16*512]=0;bool unknown=false;try{Read(card);}catch(InvalidDataException){unknown=true;}Need(unknown,"Unknown filesystem guessed as NP");
  Console.WriteLine("PASS "+checks+" actual-app best-effort catalog checks; read-only synthetic cards, no hardware.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
