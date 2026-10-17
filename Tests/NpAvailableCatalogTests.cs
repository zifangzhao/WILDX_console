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
 static IList Read(byte[] bytes,bool available=true){using(var s=new MemoryStream(bytes,false)){try{return (IList)catalog.GetMethod(available?"ReadAvailable":"Read",All).Invoke(null,new object[]{s});}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}}}
 static object Value(object row,string name,bool property=false){return property?row.GetType().GetProperty(name,All).GetValue(row,null):row.GetType().GetField(name,All).GetValue(row);}
 static bool Usable(object row){return (bool)Value(row,"Downloadable",true);}
 static void Sessions(IList rows,params uint[] expected){var actual=rows.Cast<object>().Where(Usable).Select(r=>(uint)Value(r,"Session")).ToArray();Need(actual.SequenceEqual(expected),"Readable sessions lost: "+String.Join(",",actual));Need(rows.Cast<object>().All(r=>!String.IsNullOrWhiteSpace(r.ToString())),"Catalog row could not be displayed");}
 static void StrictRefuses(byte[] card){bool refused=false;try{Read(card,false);}catch(InvalidDataException){refused=true;}Need(refused,"Strict mutation catalog admitted damaged metadata");}
 [STAThread]static int Main(string[] args){try{
  catalog=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("CE32_console.NpDownloadCatalog",true);
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
